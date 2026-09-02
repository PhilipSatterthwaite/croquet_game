using System;
using System.Collections.Generic;
using System.Linq;

namespace Croquet.Core
{
    /// <summary>One stroke the bot has decided on.</summary>
    public sealed class BotMove
    {
        public bool IsBonus;
        public BonusWay Way;

        /// <summary>Direction from the roqueted ball to the placement. Bonus strokes only.</summary>
        public Vec2 Placement;

        public Vec2 Aim;
        public double Power;

        /// <summary>What the search thought the position would be worth afterwards.</summary>
        public double Score;

        /// <summary>How the move was arrived at, for showing the reasoning.</summary>
        public string Note = "";
    }

    /// <summary>
    /// An opponent.
    ///
    /// It works by playing candidate strokes on CLONES of the real game and
    /// reading the real StrokeResult back, so it is never working from a second
    /// implementation of the rules that could drift from the first. That is the
    /// whole reason the simulation was built headless and deterministic: a
    /// thousand candidate strokes cost a few milliseconds and render nothing.
    ///
    /// Candidates are not sampled blindly. They are generated the way a player
    /// thinks -- at each ball worth hitting, at the hoop in order, at a spot in
    /// front of it -- and only then topped up with a coarse sweep for the shots
    /// that idea misses. Blind sampling wastes almost all of its budget on
    /// angles that hit nothing.
    /// </summary>
    public sealed class Bot
    {
        /// <summary>
        /// How many extra strokes of the same turn to search ahead. Zero is not
        /// a weak setting: the evaluator already rewards the position a stroke
        /// leaves, so one ply plays a recognisable break. Each extra ply costs
        /// several times the budget, so it is opt-in.
        /// </summary>
        public int Lookahead = 0;

        /// <summary>Angles in the fallback sweep. Raise for a stronger, slower bot.</summary>
        public int SweepAngles = 36;

        /// <summary>Candidate placements tried around the roqueted ball.</summary>
        public int PlacementAngles = 8;

        /// <summary>Best candidates taken a stroke deeper, when looking ahead.</summary>
        public int Deepen = 3;

        /// <summary>A quick opponent: fewer candidates, no lookahead.</summary>
        public static Bot Fast() => new Bot { SweepAngles = 20, PlacementAngles = 6 };

        /// <summary>A slower, stronger one.</summary>
        public static Bot Strong() =>
            new Bot { Lookahead = 1, SweepAngles = 48, PlacementAngles = 12, Deepen = 4 };

        /// <summary>Strokes simulated by the last Choose. For tuning the budget.</summary>
        public int LastSearched { get; private set; }

        // ---- choosing -----------------------------------------------------

        public BotMove Choose(Game game) => Search(game, Lookahead, out _);

        /// <summary>Chooses and plays one stroke on the real game.</summary>
        public StrokeResult PlayStroke(Game game)
        {
            var m = Choose(game);
            return Apply(game, m);
        }

        public static StrokeResult Apply(Game game, BotMove m) =>
            m.IsBonus
                ? game.PlayBonus(m.Way, m.Placement, m.Aim, m.Power)
                : game.Play(m.Aim, m.Power);

        /// <summary>
        /// Plays until the turn passes or the game is won. Returns the strokes
        /// played; the cap is a guard against a rule bug that never ends a turn,
        /// not a real limit on how long a break may run.
        /// </summary>
        public int PlayTurn(Game game, int maxStrokes = 60)
        {
            int me = game.Striker, n = 0;
            while (n < maxStrokes && game.Winner == null && game.Striker == me)
            {
                PlayStroke(game);
                n++;
            }
            return n;
        }

        // ---- the search ---------------------------------------------------

        BotMove Search(Game game, int depth, out double best)
        {
            LastSearched = 0;
            var move = SearchInner(game, depth, out best);
            return move;
        }

        BotMove SearchInner(Game game, int depth, out double best)
        {
            int me = game.Striker;
            var candidates = game.Stroke == StrokeKind.Bonus
                ? BonusCandidates(game)
                : OrdinaryCandidates(game);

            BotMove chosen = null;
            best = double.NegativeInfinity;

            // Kept so the best few can be searched a stroke deeper. Looking
            // ahead on everything would cost the square of the budget for
            // almost no gain -- the ordering from one ply is already good.
            var scored = new List<(BotMove Move, double Score, Game After)>();

            foreach (var m in candidates)
            {
                var clone = game.Clone();
                StrokeResult r;
                try { r = Apply(clone, m); }
                catch (InvalidOperationException) { continue; }
                LastSearched++;

                double s = Evaluate(game, clone, r, me);
                m.Score = s;
                scored.Add((m, s, clone));
            }

            if (scored.Count == 0)
            {
                best = 0;
                return Fallback(game);
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            int deepen = depth > 0 ? Math.Min(Deepen, scored.Count) : 0;
            for (int i = 0; i < deepen; i++)
            {
                var (m, s, after) = scored[i];
                // Only worth looking further if the turn is still ours.
                if (after.Winner != null || after.Striker != me) continue;

                SearchInner(after, depth - 1, out double follow);
                // Discounted: a stroke in hand is worth more than one hoped for.
                scored[i] = (m, s + follow * 0.75, after);
            }

            foreach (var (m, s, _) in scored)
                if (s > best) { best = s; chosen = m; }

            chosen.Score = best;
            return chosen;
        }

        static BotMove Fallback(Game game)
        {
            // Nothing was playable, which should not happen. Tap gently toward
            // the target rather than throwing in the middle of a game.
            var me = game.World.Balls[game.Striker].Pos;
            var to = game.World.Field.TargetFor(game.States[game.Striker].Point) - me;
            return new BotMove
            {
                IsBonus = game.Stroke == StrokeKind.Bonus,
                Way = BonusWay.WhereItLies,
                Placement = new Vec2(-1, 0),
                Aim = to.LengthSq > 0 ? to.Normalized : new Vec2(1, 0),
                Power = 1.0,
                Note = "fallback"
            };
        }

        // ---- candidate generation -----------------------------------------

        /// <summary>
        /// The speed needed to roll a given distance, from v^2 = 2*a*d. Sampling
        /// power in metres-of-roll rather than in metres per second is what makes
        /// the candidates sensible: "reach that ball" is a distance, not a speed.
        /// </summary>
        static double SpeedFor(double distance, CourtSpec c) =>
            Math.Sqrt(2 * c.Friction * Math.Max(0.05, distance));

        static readonly double[] Overhit = { 0.8, 1.0, 1.2, 1.6, 2.2 };

        IEnumerable<BotMove> OrdinaryCandidates(Game game)
        {
            var list = new List<BotMove>();
            int me = game.Striker;
            var c = game.World.Spec;
            var from = game.World.Balls[me].Pos;
            var field = game.World.Field;

            void Toward(Vec2 target, string note, double[] factors = null)
            {
                var d = target - from;
                double dist = d.Length;
                if (dist < 1e-6) return;
                var aim = d / dist;
                foreach (var f in factors ?? Overhit)
                    list.Add(new BotMove { Aim = aim, Power = SpeedFor(dist, c) * f, Note = note });
            }

            // Every ball worth hitting: a roquet is two strokes and the start of
            // a break, so these deserve the most candidates.
            for (int j = 0; j < game.World.Balls.Length; j++)
            {
                if (j == me || !game.World.Balls[j].InPlay) continue;
                if (!game.IsAlive(j)) continue;

                var target = game.World.Balls[j].Pos;
                Toward(target, "roquet " + j);

                // Fine angular spread, because a roquet at range is decided by
                // fractions of a degree and the coarse sweep will never find it.
                var d = target - from;
                double dist = d.Length;
                if (dist < 1e-6) continue;
                double baseAng = Math.Atan2(d.Y, d.X);
                double spread = Math.Atan2(c.BallRadius, Math.Max(0.3, dist));
                foreach (var k in new[] { -1.0, -0.5, 0.5, 1.0 })
                {
                    double a = baseAng + spread * k;
                    var aim = new Vec2(Math.Cos(a), Math.Sin(a));
                    list.Add(new BotMove { Aim = aim, Power = SpeedFor(dist, c) * 1.15,
                                           Note = "roquet " + j + " edge" });
                }
            }

            // The point in order, and a spot short of it to take position from.
            int point = game.States[me].Point;
            if (!field.IsFinished(point))
            {
                var target = field.TargetFor(point);
                Toward(target, "run " + field.Labels[point]);

                if (!field.IsPeg(point))
                {
                    int dir = field.DirectionFor(point);
                    foreach (var back in new[] { 0.5, 1.1, 2.0 })
                        Toward(new Vec2(target.X - dir * back, target.Y),
                               "position for " + field.Labels[point],
                               new[] { 0.9, 1.0, 1.1 });
                }
            }

            // A coarse sweep, for everything the above did not think of.
            for (int i = 0; i < SweepAngles; i++)
            {
                double a = i * 2 * Math.PI / SweepAngles;
                var aim = new Vec2(Math.Cos(a), Math.Sin(a));
                foreach (var d in new[] { 1.5, 4.0, 9.0, 18.0 })
                    list.Add(new BotMove { Aim = aim, Power = SpeedFor(d, c), Note = "sweep" });
            }

            return list;
        }

        IEnumerable<BotMove> BonusCandidates(Game game)
        {
            var list = new List<BotMove>();
            var c = game.World.Spec;
            int me = game.Striker;
            var field = game.World.Field;
            var other = game.World.Balls[game.RoquetedBall].Pos;

            var ways = game.Laws.FourWaysToTakeCroquet
                ? new[] { BonusWay.CroquetShot, BonusWay.MalletHead,
                          BonusWay.FootShot, BonusWay.WhereItLies }
                : new[] { BonusWay.CroquetShot };

            int point = game.States[me].Point;
            var target = field.IsFinished(point) ? other : field.TargetFor(point);

            foreach (var way in ways)
            {
                // Where it lies has no placement, so one pass over it is enough.
                int places = way == BonusWay.WhereItLies ? 1 : PlacementAngles;

                for (int p = 0; p < places; p++)
                {
                    double pa = p * 2 * Math.PI / Math.Max(1, places);
                    var place = new Vec2(Math.Cos(pa), Math.Sin(pa));

                    Vec2 stand;
                    try { stand = game.BonusPlacement(way, place); }
                    catch (InvalidOperationException) { continue; }

                    void From(Vec2 to, string note)
                    {
                        var d = to - stand;
                        double dist = d.Length;
                        if (dist < 1e-6) return;
                        var aim = d / dist;
                        foreach (var f in new[] { 0.8, 1.15, 1.7 })
                            list.Add(new BotMove
                            {
                                IsBonus = true, Way = way, Placement = place,
                                Aim = aim, Power = SpeedFor(dist, c) * f,
                                Note = way + " " + note
                            });
                    }

                    From(target, "at " + (field.IsFinished(point) ? "ball" : field.Labels[point]));

                    // Sending the croqueted ball somewhere useful is half the
                    // point of the stroke, so aim through it as well.
                    From(other + (other - stand), "through it");
                }
            }

            return list;
        }

        // ---- evaluation ---------------------------------------------------

        /// <summary>
        /// What the position is worth to the ball that just played. Points
        /// dominate everything, then keeping the turn, then being somewhere
        /// useful next stroke.
        /// </summary>
        public static double Evaluate(Game before, Game after, StrokeResult r, int me)
        {
            var field = after.World.Field;
            var c = after.World.Spec;
            double s = 0;

            // Scoring is the whole object of the game, and a stroke that scores
            // also earns another, so it is worth far more than position.
            int gained = after.States[me].Point - before.States[me].Point;
            s += gained * 1200;

            if (after.Winner != null && after.Winner.Contains(me)) s += 100000;
            if (r.PeggedOut) s += 3000;

            // A roquet is two strokes and the beginning of a break.
            if (r.Roqueted >= 0) s += 450;

            // Losing the turn is the real cost of a bad stroke.
            if (r.TurnEnded) s -= 700;
            if (r.EndedByOutOfBounds) s -= 500;   // and it was avoidable

            if (!after.World.Balls[me].InPlay) return s;   // round; nothing else matters

            var pos = after.World.Balls[me].Pos;
            int point = after.States[me].Point;

            if (!field.IsFinished(point))
            {
                var tgt = field.TargetFor(point);
                double d = (tgt - pos).Length;
                s -= d * 22;

                // Being in front of the hoop, on the right side and near the
                // line of it, is worth much more than being merely close: it is
                // the difference between a hoop next stroke and a scramble.
                if (!field.IsPeg(point))
                {
                    int dir = field.DirectionFor(point);
                    double along = (tgt.X - pos.X) * dir;      // >0 means still to come
                    double across = Math.Abs(pos.Y - tgt.Y);
                    if (along > 0 && along < 3.0 && across < 0.9)
                        s += 260 * (1 - across / 0.9) * (1 - along / 3.0);
                }
            }

            // Somewhere to go next turn: the nearest ball still worth hitting.
            double nearest = double.MaxValue;
            for (int j = 0; j < after.World.Balls.Length; j++)
            {
                if (j == me || !after.World.Balls[j].InPlay) continue;
                if (after.States[me].Dead.Contains(j)) continue;
                nearest = Math.Min(nearest, (after.World.Balls[j].Pos - pos).Length);
            }
            if (nearest < double.MaxValue) s -= Math.Min(nearest, 12) * 6;

            // Off the edge of the lawn is a poor place to leave a ball even when
            // it costs nothing directly.
            double edge = Math.Min(Math.Min(pos.X, c.Width - pos.X),
                                   Math.Min(pos.Y, c.Height - pos.Y));
            if (edge < 1.0) s -= (1.0 - edge) * 120;

            return s;
        }
    }
}
