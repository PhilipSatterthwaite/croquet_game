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

        /// <summary>Leading candidates re-priced by what this hand would do to them.</summary>
        public int RiskChecks = 10;

        /// <summary>Wobbled replays per candidate in that re-pricing.</summary>
        public int RiskSamples = 4;

        /// <summary>
        /// Aim wobble applied to the chosen stroke, in radians, one standard
        /// deviation. THIS is what makes a weaker opponent, rather than a
        /// smaller search: a bot that searches less still picks a sensible
        /// shot and plays it perfectly, which reads as an unbeatable player
        /// with poor ideas. A bot whose hand shakes misses, like a person.
        /// </summary>
        public double AimError;

        /// <summary>Power wobble, as a fraction of the intended strength.</summary>
        public double PowerError;

        /// <summary>
        /// How much the aim error grows with the length of the shot. A player's
        /// hand is about as steady whatever the range, but the same angular
        /// slip costs far more at twenty metres than at two -- and a real player
        /// is also less certain of the line over distance. See Spread for the
        /// shape of it: near enough nothing at a tap, and growing from there.
        /// </summary>
        public double RangeError = 1.0;

        // A regulation hoop leaves under four centimetres either side of the
        // ball, so running one from a metre away needs the line inside about
        // 0.04 radians. These are set against that: beginner is well outside it
        // and scores by luck, expert is comfortably inside it and still misses
        // across the court. Values sized for the old double-width hoops made
        // every level score nothing at all.
        //
        /// <summary>Barely a player. Misses often, and badly at any range.</summary>
        public static Bot Beginner(int seed = DefaultSeed) => new Bot(seed)
        {
            SweepAngles = 10, PlacementAngles = 4,
            AimError = 0.038, PowerError = 0.26, RangeError = 1.4
        };

        /// <summary>Knows what to do, is not much good at doing it.</summary>
        public static Bot Casual(int seed = DefaultSeed) => new Bot(seed)
        {
            SweepAngles = 18, PlacementAngles = 6,
            AimError = 0.014, PowerError = 0.11, RangeError = 1.2
        };

        /// <summary>A sound club player.</summary>
        public static Bot Steady(int seed = DefaultSeed) => new Bot(seed)
        {
            SweepAngles = 30, PlacementAngles = 8,
            AimError = 0.006, PowerError = 0.045, RangeError = 1.0
        };

        /// <summary>
        /// An excellent human, not a machine: it looks a stroke further ahead
        /// and its hand is very good, but it is still a hand. Long shots are
        /// still missed occasionally, which is what makes it beatable at all.
        /// </summary>
        public static Bot Expert(int seed = DefaultSeed) => new Bot(seed)
        {
            Lookahead = 1, SweepAngles = 48, PlacementAngles = 12, Deepen = 4,
            AimError = 0.0035, PowerError = 0.02, RangeError = 0.8
        };

        /// <summary>Strokes simulated by the current or last Choose.</summary>
        public volatile int LastSearched;

        /// <summary>Roughly how many it expects to simulate, for a progress bar.</summary>
        public volatile int Planned;

        readonly Random rng;

        /// <summary>The hand a level gets unless one is asked for by name.</summary>
        public const int DefaultSeed = 20260902;

        public Bot(int seed = DefaultSeed) { rng = new Random(seed); }

        /// <summary>
        /// Normal deviate, for the wobble. Never used inside the simulation.
        ///
        /// Clamped at two and a half deviations. An unclamped normal has a tail,
        /// and the tail of a MULTIPLICATIVE power error crosses zero: a beginner
        /// meaning to hit seven metres would occasionally strike at three per
        /// cent of that and dribble the ball two centimetres. Nobody does that.
        /// A bad player hits a shot too softly, not almost not at all.
        /// </summary>
        double Gauss()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return g < -2.5 ? -2.5 : g > 2.5 ? 2.5 : g;
        }

        // ---- choosing -----------------------------------------------------

        /// <summary>
        /// Skips the search entirely and plays an obvious stroke: aim at the
        /// point in order, hit it hard enough to get there. Not a difficulty
        /// level -- a diagnostic. It separates "the search picks a bad shot"
        /// from "a chosen shot never reaches the screen", which are otherwise
        /// indistinguishable from the outside.
        /// </summary>
        public bool Simple;

        public static Bot Dummy(int seed = DefaultSeed) => new Bot(seed) { Simple = true };

        BotMove SimpleMove(Game game)
        {
            var field = game.World.Field;
            int me = game.Striker;
            var c = game.World.Spec;

            bool bonus = game.Stroke == StrokeKind.Bonus;
            var placement = new Vec2(-1, 0);
            var from = bonus
                ? game.BonusPlacement(BonusWay.CroquetShot, placement)
                : game.World.Balls[me].Pos;

            int point = game.States[me].Point;
            var to = field.IsFinished(point) ? field.Pegs[0] : field.TargetFor(point);

            var d = to - from;
            double dist = d.Length;
            var aim = dist > 1e-6 ? d / dist : new Vec2(1, 0);

            return new BotMove
            {
                IsBonus = bonus,
                Way = BonusWay.CroquetShot,
                Placement = placement,
                Aim = aim,
                Power = Math.Min(6.0, SpeedFor(dist, c) * 1.1),
                Note = "straight at " + (field.IsFinished(point) ? "the peg" : field.Labels[point])
            };
        }

        public BotMove Choose(Game game)
        {
            if (Simple) return SimpleMove(game);

            var m = Search(game, Lookahead, out _);

            // The wobble goes on the chosen stroke, not on the search: it knows
            // what it meant to do and simply fails to do it, which is what
            // being beatable looks like.
            if (m != null && Shaky)
            {
                Wobble(m, game.World.Spec);
                m.Note += " (roughly)";
            }
            return m;
        }

        /// <summary>Does this level miss at all? A perfect bot skips the wobble.</summary>
        bool Shaky => AimError > 0 || PowerError > 0;

        /// <summary>
        /// How far off a stroke of this strength is likely to go, as a multiple
        /// of the level's base error.
        ///
        /// This is what makes weakness read as human. Error grows with the
        /// RANGE of the shot, because that is where a person's uncertainty
        /// actually lives: nobody misjudges a tap, and everybody misjudges a
        /// shot across the court. It used to carry a floor of 0.6, which meant
        /// even a half-metre push into a hoop was struck with most of a
        /// beginner's full error and missed -- a shot that no player of any
        /// standard gets wrong. The floor is now small enough that anything
        /// within comfortable range goes in whoever is playing, and the levels
        /// separate on the long shots, which is where they separate in life.
        /// </summary>
        double Spread(double power, CourtSpec c)
        {
            double range = power * power / (2 * Math.Max(0.05, c.Friction));
            return 0.12 + range / 10.0 * RangeError;
        }

        /// <summary>Puts the hand's error on a stroke, in place.</summary>
        void Wobble(BotMove m, CourtSpec c)
        {
            double spread = Spread(m.Power, c);

            double a = Math.Atan2(m.Aim.Y, m.Aim.X) + Gauss() * AimError * spread;
            m.Aim = new Vec2(Math.Cos(a), Math.Sin(a));

            // Floored at two thirds. The error is multiplicative, so without a
            // floor a large enough slip turns a firm stroke into a nudge, which
            // is a different mistake from the one being modelled.
            double f = 1 + Gauss() * PowerError * spread;
            m.Power *= f < 0.66 ? 0.66 : f > 1.5 ? 1.5 : f;
        }

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
            Planned = 0;
            return SearchInner(game, depth, out best);
        }

        BotMove SearchInner(Game game, int depth, out double best)
        {
            int me = game.Striker;
            var candidates = game.Stroke == StrokeKind.Bonus
                ? BonusCandidates(game)
                : OrdinaryCandidates(game);

            // An estimate, set once by the outermost call. Deepening explores
            // whole candidate sets of its own, so this is the right order of
            // magnitude rather than an exact count -- which is all a progress
            // bar needs, and it is honest about being approximate by never
            // being allowed to read as finished before it is.
            if (Planned == 0)
                Planned = System.Linq.Enumerable.Count(candidates)
                        * (depth > 0 ? 1 + Deepen : 1);

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

            // Re-price the leaders by what they are worth to THIS hand. Up to
            // here every candidate has been played perfectly, so a roquet from
            // thirty metres always goes in and always outscores a quiet
            // positioning shot -- and then the wobble is applied on the way out
            // and it misses. That is exactly the player who is infuriating to
            // watch: forever attempting things they cannot do. Replaying the
            // leaders a few times with the error actually on them turns the
            // score into the average outcome rather than the best case, and a
            // shot that only works when struck perfectly collapses on its own.
            if (Shaky && depth == Lookahead)
            {
                // The shortlist has to hold different IDEAS, not the same idea
                // at a dozen strengths. Straight off the top, the leaders are
                // all one shot with the power nudged, so re-pricing them merely
                // discovers which version of that one shot is least bad -- and
                // the quiet positioning shot that should have won was never in
                // the running. Skipping near-duplicates buys real alternatives
                // for the same number of simulations.
                var pool = new List<(BotMove Move, double Score, Game After)>();
                foreach (var e in scored)
                {
                    if (pool.Count >= RiskChecks) break;
                    bool same = false;
                    foreach (var k in pool)
                    {
                        double dot = k.Move.Aim.X * e.Move.Aim.X + k.Move.Aim.Y * e.Move.Aim.Y;
                        if (k.Move.IsBonus == e.Move.IsBonus && dot > 0.99
                            && Math.Abs(k.Move.Power - e.Move.Power) < e.Move.Power * 0.25)
                        { same = true; break; }
                    }
                    if (!same) pool.Add(e);
                }

                for (int i = 0; i < pool.Count; i++)
                {
                    var (m, clean, after) = pool[i];
                    pool[i] = (m, Expected(game, m, me, clean), after);
                }

                // Only the re-priced ones may now be chosen. A best-case score
                // and an expected score are not the same quantity, and sorting
                // them together simply hands the choice to whichever heave was
                // ranked eleventh and never got examined. Everything dropped
                // here was worse than these even when played perfectly, so it
                // cannot be the answer once the hand is taken into account.
                scored = pool;
                scored.Sort((a, b) => b.Score.CompareTo(a.Score));
            }

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

        /// <summary>
        /// What a stroke is actually worth to a player with this hand: the mean
        /// of playing it several times with the error on, plus the clean score
        /// as one more sample so a shot with real upside is not written off for
        /// being difficult. A gentle stroke barely moves under the wobble and
        /// keeps its score; a heave across the court is mostly misses and loses
        /// most of it. Nothing here is a rule about long shots -- the search
        /// simply stops being told it is a better player than it is.
        /// </summary>
        double Expected(Game game, BotMove m, int me, double clean)
        {
            double total = clean;
            int n = 1;

            for (int k = 0; k < RiskSamples; k++)
            {
                var trial = new BotMove
                {
                    IsBonus = m.IsBonus, Way = m.Way, Placement = m.Placement,
                    Aim = m.Aim, Power = m.Power
                };
                Wobble(trial, game.World.Spec);

                var clone = game.Clone();
                StrokeResult r;
                try { r = Apply(clone, trial); }
                catch (InvalidOperationException) { continue; }
                LastSearched++;

                total += Evaluate(game, clone, r, me);
                n++;
            }
            return total / n;
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

        // Multipliers on SPEED, so the distance rolled goes as the SQUARE of
        // these. The old ladder topped out at 2.2, which is not a firm stroke
        // at a ball five metres away -- it is a stroke that rolls twenty-four
        // metres and finishes against the far boundary. Capped at 1.4, which
        // still passes comfortably through a target and runs a hoop with
        // something to spare.
        static readonly double[] Overhit = { 0.85, 1.0, 1.15, 1.4 };

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
                        // As with Overhit: squared into distance, so 1.7 was a
                        // stroke rolling three times as far as it was aimed --
                        // and "through it" already aims past the other ball, so
                        // that compounded into strokes crossing the whole court.
                        foreach (var f in new[] { 0.85, 1.1, 1.4 })
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
        /// <summary>Are these two balls on the same side? Cutthroat: only itself.</summary>
        public static bool SameSide(Game g, int a, int b) =>
            a == b || (g.Side != null && g.Side[a] == g.Side[b]);

        public static double Evaluate(Game before, Game after, StrokeResult r, int me)
        {
            var field = after.World.Field;
            var c = after.World.Spec;
            double s = 0;

            // Scoring is the whole object of the game, and a stroke that scores
            // also earns another, so it is worth far more than position.
            int gained = after.States[me].Point - before.States[me].Point;
            s += gained * 1200;

            // Points other balls were driven through count for their own side,
            // so putting a partner through its hoop is nearly as good as scoring
            // and putting an opponent through theirs is a gift.
            foreach (var (ball, _) in r.OthersScored)
                s += SameSide(after, me, ball) ? 900 : -750;

            // Winning is winning whichever of our balls did it.
            if (after.Winner != null && after.Winner.Contains(me)) s += 100000;
            if (after.Winner != null && !after.Winner.Contains(me)) s -= 100000;
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

            // What the stroke did to everyone else. This is the whole reason a
            // split or a send is worth playing: the striker gains nothing
            // directly, but a partner ends up in front of its hoop, or an
            // opponent ends up in a corner. Scored as the CHANGE in each ball's
            // distance to its own next point, so pushing an opponent away and
            // pulling a partner closer both read as gains.
            for (int j = 0; j < after.World.Balls.Length; j++)
            {
                if (j == me || !after.World.Balls[j].InPlay) continue;
                if (after.States[j].Finished) continue;

                int pp = after.States[j].Point;
                if (field.IsFinished(pp)) continue;

                var target = field.TargetFor(pp);
                double closer = (target - before.World.Balls[j].Pos).Length
                              - (target - after.World.Balls[j].Pos).Length;

                // Clamped, because the value of shoving a ball is not linear in
                // how far it goes. Unclamped, driving one opponent twenty
                // metres from its hoop was worth more than running a hoop --
                // and in cutthroat every other ball is an opponent, so the
                // whole game became blasting whatever was nearest as hard as
                // possible. Three metres is about where it stops mattering.
                closer = closer > 3.0 ? 3.0 : closer < -3.0 ? -3.0 : closer;

                if (SameSide(after, me, j))
                {
                    s += closer * 30;
                    if (r.BroughtIn.Contains(j)) s -= 250;    // sent a partner off
                }
                else
                {
                    s -= closer * 22;                         // do them no favours
                }
            }

            // Two shots in hand and nothing to show for the first is a waste:
            // with a stroke to spare it should be improving something, its own
            // position or somebody else's.
            if (r.ShotsLeft >= 2 && r.PointsScored.Count == 0 && r.Roqueted < 0) s -= 150;

            // Off the edge of the lawn is a poor place to leave a ball even when
            // it costs nothing directly.
            double edge = Math.Min(Math.Min(pos.X, c.Width - pos.X),
                                   Math.Min(pos.Y, c.Height - pos.Y));
            if (edge < 1.0) s -= (1.0 - edge) * 120;

            return s;
        }
    }
}
