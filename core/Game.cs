using System;
using System.Collections.Generic;
using System.Linq;

namespace Croquet.Core
{
    /// <summary>Whether the stroke in hand is an ordinary one or the first bonus after a roquet.</summary>
    public enum StrokeKind
    {
        Ordinary,

        /// <summary>
        /// The first of the two shots a roquet earns. It may be taken in any of
        /// four ways — see <see cref="BonusWay"/>. The second is always an
        /// ordinary continuation from wherever the striker comes to rest.
        /// </summary>
        Bonus
    }

    /// <summary>
    /// The four ways the first bonus shot after a roquet may be taken, from the
    /// Bonus Shots section of the USCA nine-wicket rules.
    /// </summary>
    public enum BonusWay
    {
        /// <summary>A mallet-head distance or less from the roqueted ball.</summary>
        MalletHead,

        /// <summary>
        /// In contact, with the striker's ball held steady under a foot or hand.
        /// Everything goes into the other ball; the striker stays put.
        /// </summary>
        FootShot,

        /// <summary>In contact, both balls sent on together.</summary>
        CroquetShot,

        /// <summary>Left where it came to rest after the roquet, and simply played.</summary>
        WhereItLies
    }

    /// <summary>The rules-side state of one ball. Physics lives in Ball.</summary>
    public sealed class BallState
    {
        /// <summary>How far round the course: 0 is for wicket 1, 16 is staked out.</summary>
        public int Point;

        /// <summary>
        /// False until this ball's first turn comes round. Balls are not laid
        /// out on the lawn at the start; each enters from the starting spot
        /// when it is first played.
        /// </summary>
        public bool Started;

        /// <summary>
        /// Balls already roqueted this turn. Bonus shots are not earned for
        /// hitting them again until the striker clears its next wicket -- and
        /// the whole set is forgotten at the start of the next turn, which is
        /// the basic rule. Carry-over deadness is Challenging Option 1.
        /// </summary>
        public readonly HashSet<int> Dead = new HashSet<int>();

        public bool Finished => Course.IsFinished(Point);
    }

    /// <summary>What a stroke produced. Purely a report; the game is already updated.</summary>
    public sealed class StrokeResult
    {
        public int Striker;

        /// <summary>Points the striker scored, in course order.</summary>
        public readonly List<int> PointsScored = new List<int>();

        /// <summary>Points other balls were driven through, as (ball, point).</summary>
        public readonly List<(int Ball, int Point)> OthersScored = new List<(int, int)>();

        /// <summary>The ball roqueted, or -1. Only the first ball struck can be one.</summary>
        public int Roqueted = -1;

        /// <summary>
        /// A ball was touched but earned nothing -- either the striker was
        /// already dead on it, or a wicket in the same stroke came first.
        /// </summary>
        public int TouchedButNoRoquet = -1;

        /// <summary>Balls that left the lawn and were brought back in.</summary>
        public readonly List<int> BroughtIn = new List<int>();

        public bool PeggedOut;
        public bool TurnEnded;

        /// <summary>The turn ended because a ball left the lawn (Option 2A).</summary>
        public bool EndedByOutOfBounds;

        /// <summary>Strokes the striker still has after this one.</summary>
        public int ShotsLeft;

        public StrokeKind Next;
        public int NextStriker;
    }

    /// <summary>
    /// Nine-wicket croquet, to the USCA basic rules.
    ///
    /// A turn is one stroke plus the bonus shots it earns:
    ///
    ///   * a wicket, or the turning stake  -> one bonus shot
    ///   * a roquet                        -> two bonus shots
    ///
    /// Two is the ceiling; there is never a third. And bonuses do not
    /// accumulate: earning any forfeits whatever was owed before, so a bonus
    /// shot that scores a wicket leaves ONE shot, not two.
    ///
    /// Order within a stroke decides everything, which is why events carry a
    /// step. Wicket first, then a ball: the wicket counts and the contact is
    /// ignored. Ball first, then a wicket: two shots for the roquet and the
    /// wicket does not count at all.
    ///
    /// The rules layer never looks at a velocity; it reads the ordered events
    /// the simulation recorded and applies the rules to them.
    /// </summary>
    public sealed class Game
    {
        public readonly World World;
        public readonly BallState[] States;

        public int Striker { get; private set; }
        public StrokeKind Stroke { get; private set; } = StrokeKind.Ordinary;

        /// <summary>The ball a pending bonus stroke is taken against, or -1.</summary>
        public int RoquetedBall { get; private set; } = -1;

        /// <summary>Strokes the striker may still play, including the one in hand.</summary>
        public int ShotsLeft { get; private set; } = 1;

        public int[] Winner { get; private set; }

        /// <summary>Ball index -> side. Null means every ball for itself.</summary>
        public readonly int[] Side;

        /// <summary>Which Challenging Options are in force.</summary>
        public readonly RuleOptions Options;

        public Game(World world, int[] side = null, RuleOptions options = null)
        {
            Options = options ?? new RuleOptions();
            World = world;
            States = new BallState[world.Balls.Length];
            for (int i = 0; i < States.Length; i++)
            {
                States[i] = new BallState();
                world.Balls[i].InPlay = false;      // nothing is on the lawn yet
            }
            Side = side;
            Striker = 0;
            EnterLawn(Striker);
        }

        public BallState Current => States[Striker];
        public int Target => Current.Point;

        /// <summary>Would hitting this ball earn bonus shots?</summary>
        public bool IsAlive(int on) => !Current.Dead.Contains(on);

        // ---- coming onto the lawn -----------------------------------------

        void EnterLawn(int i)
        {
            if (States[i].Started || States[i].Finished) return;
            States[i].Started = true;
            World.Balls[i].InPlay = true;
            World.Balls[i].Pos = FreeStartSpot(i);
        }

        Vec2 FreeStartSpot(int forBall)
        {
            var spot = World.Field.StartSpot;
            double step = World.Spec.BallRadius * 2.2;

            for (int k = 0; k < 40; k++)
            {
                int rank = (k + 1) / 2;
                double dy = (k % 2 == 0 ? 1 : -1) * rank * step;
                var p = new Vec2(spot.X, spot.Y + dy);

                bool clear = true;
                for (int j = 0; j < World.Balls.Length && clear; j++)
                {
                    if (j == forBall || !World.Balls[j].InPlay) continue;
                    if ((World.Balls[j].Pos - p).LengthSq < step * step) clear = false;
                }
                if (clear) return p;
            }
            return spot;
        }

        // ---- playing ------------------------------------------------------

        /// <summary>An ordinary stroke: the striker is struck from where it lies.</summary>
        public StrokeResult Play(Vec2 direction, double power)
        {
            if (Winner != null) throw new InvalidOperationException("the game is over");
            if (Stroke == StrokeKind.Bonus)
                throw new InvalidOperationException("a bonus stroke is owed; use PlayBonus");

            World.ClearShot();
            World.Balls[Striker].Vel = direction.Normalized * power;
            Sim.Settle(World);
            return Resolve();
        }

        /// <summary>
        /// The first bonus shot after a roquet, taken one of four ways.
        /// <paramref name="placement"/> is the direction from the roqueted ball
        /// to where the striker is set down, and is ignored for
        /// <see cref="BonusWay.WhereItLies"/>.
        /// </summary>
        public StrokeResult PlayBonus(BonusWay way, Vec2 placement, Vec2 aim, double power)
        {
            if (Winner != null) throw new InvalidOperationException("the game is over");
            if (Stroke != StrokeKind.Bonus)
                throw new InvalidOperationException("no bonus stroke is owed");

            int other = RoquetedBall;
            if (way != BonusWay.WhereItLies)
                World.Balls[Striker].Pos = BonusPlacement(way, placement);

            World.ClearShot();

            // A foot shot pins the striker under a foot, so all of it goes into
            // the other ball. Driving that ball directly, rather than modelling
            // a very heavy follow-through, is what leaves the striker exactly
            // where it was set down.
            if (way == BonusWay.FootShot)
                World.Balls[other].Vel = aim.Normalized * power;
            else
                World.Balls[Striker].Vel = aim.Normalized * power;

            Sim.Settle(World);
            return Resolve();
        }

        /// <summary>Where the striker would be set down, for previewing the choice.</summary>
        public Vec2 BonusPlacement(BonusWay way, Vec2 placement)
        {
            if (Stroke != StrokeKind.Bonus)
                throw new InvalidOperationException("no bonus stroke is owed");
            if (way == BonusWay.WhereItLies) return World.Balls[Striker].Pos;

            Vec2 n = placement.Normalized;
            if (n.LengthSq == 0) n = new Vec2(-1, 0);

            double gap = World.Spec.BallRadius * 2
                       + (way == BonusWay.MalletHead ? World.Spec.MalletHead : 0);
            return World.Balls[RoquetedBall].Pos + n * gap;
        }

        // ---- resolving ----------------------------------------------------

        StrokeResult Resolve()
        {
            var r = new StrokeResult { Striker = Striker };
            var me = Current;
            var deadAtStart = new HashSet<int>(me.Dead);

            // The first ball the striker touched. Only the first can be a
            // roquet: anything hit after it is ignored, with no penalty.
            int firstBall = -1, contactStep = int.MaxValue;
            foreach (var e in World.Events)
            {
                if (e.Kind != EventKind.BallContact) continue;
                if (e.Ball != Striker && e.Other != Striker) continue;
                firstBall = e.Ball == Striker ? e.Other : e.Ball;
                contactStep = e.Step;
                break;
            }

            // Points the striker could claim, and when the first of them landed.
            var claimable = new List<(int Point, int Step)>();
            int probe = me.Point;
            while (!Course.IsFinished(probe))
            {
                int at = World.Field.IsPeg(probe)
                       ? World.StepHitPeg(Striker, probe)
                       : World.StepRanPoint(Striker, probe);
                if (at < 0) break;
                claimable.Add((probe, at));
                probe++;
            }
            int firstPointStep = claimable.Count > 0 ? claimable[0].Step : int.MaxValue;

            // Whichever came first wins outright. A wicket then a ball: the
            // wicket counts, the contact is nothing. A ball then a wicket: two
            // shots for the roquet and the wicket does not count.
            bool roquet = firstBall >= 0
                       && !deadAtStart.Contains(firstBall)
                       && contactStep < firstPointStep;

            int earned;
            if (roquet)
            {
                r.Roqueted = firstBall;
                me.Dead.Add(firstBall);
                RoquetedBall = firstBall;
                earned = 2;
            }
            else
            {
                if (firstBall >= 0) r.TouchedButNoRoquet = firstBall;

                foreach (var (point, at) in claimable)
                {
                    me.Point++;
                    r.PointsScored.Add(point);
                    if (!World.Field.IsPeg(point)) me.Dead.Clear();   // a wicket revives you
                    if (me.Finished)
                    {
                        World.Balls[Striker].InPlay = false;
                        r.PeggedOut = true;
                        break;
                    }
                }
                // Never three. Two wickets in a stroke earn two, and so does a
                // wicket plus the turning stake.
                earned = Math.Min(2, r.PointsScored.Count);
            }

            // A ball driven through its own wicket by someone else scores the
            // point for its side -- but earns nobody a bonus shot.
            for (int i = 0; i < World.Balls.Length; i++)
            {
                if (i == Striker || States[i].Finished) continue;
                while (!States[i].Finished)
                {
                    int p = States[i].Point;
                    bool got = World.Field.IsPeg(p)
                             ? World.StepHitPeg(i, p) >= 0
                             : World.RanPoint(i, p);
                    if (!got) break;

                    States[i].Point++;
                    r.OthersScored.Add((i, p));
                    if (States[i].Finished) World.Balls[i].InPlay = false;
                }
            }

            // Out of bounds carries no penalty in the basic rules: the ball is
            // brought back in a mallet's length, square to the line it crossed,
            // and play carries on.
            for (int i = 0; i < World.Balls.Length; i++)
            {
                if (!World.Balls[i].InPlay || !World.Balls[i].WentOut) continue;
                BringInbounds(i);
                r.BroughtIn.Add(i);
            }

            // Bonuses do not accumulate: earning any forfeits what was owed.
            ShotsLeft = earned > 0 ? earned : ShotsLeft - 1;

            // Option 2A: sending anything off the lawn ends the turn, however
            // well the rest of the stroke went. Applied after the bonus maths
            // so it overrides shots that were genuinely earned.
            if (Options.OutOfBoundsEndsTurn && r.BroughtIn.Count > 0)
            {
                ShotsLeft = 0;
                r.EndedByOutOfBounds = true;
            }

            // Read before the turn can pass: NextTurn resets ShotsLeft for
            // whoever is next, and this report is about the stroke just played.
            r.ShotsLeft = Math.Max(0, ShotsLeft);

            if (r.PeggedOut || ShotsLeft <= 0)
            {
                Stroke = StrokeKind.Ordinary;
                RoquetedBall = -1;
                r.TurnEnded = true;
                CheckWinner();
                if (Winner == null) NextTurn();
            }
            else
            {
                Stroke = roquet ? StrokeKind.Bonus : StrokeKind.Ordinary;
                if (!roquet) RoquetedBall = -1;
            }

            r.Next = Stroke;
            r.NextStriker = Striker;
            return r;
        }

        /// <summary>
        /// Square to the line it crossed, a mallet's length in. Perpendicular,
        /// not diagonal -- that is explicit in the rules.
        /// </summary>
        void BringInbounds(int i)
        {
            var c = World.Spec;
            double d = c.BoundaryReturn;
            var p = World.Balls[i].Pos;
            double x = p.X, y = p.Y;

            if (x <= 0) x = d;
            else if (x >= c.Width) x = c.Width - d;

            if (y <= 0) y = d;
            else if (y >= c.Height) y = c.Height - d;

            World.Balls[i].Pos = new Vec2(x, y);
            World.Balls[i].Vel = Vec2.Zero;
        }

        void NextTurn()
        {
            for (int k = 1; k <= States.Length; k++)
            {
                int j = (Striker + k) % States.Length;
                if (States[j].Finished) continue;
                Striker = j;
                ShotsLeft = 1;
                Stroke = StrokeKind.Ordinary;

                // The basic rule lets deadness lapse here. Under Option 1 it
                // does not: it lifts only when the ball clears its next wicket.
                if (!Options.CarryOverDeadness) States[j].Dead.Clear();

                EnterLawn(j);
                return;
            }
        }

        void CheckWinner()
        {
            if (Side == null)
            {
                for (int i = 0; i < States.Length; i++)
                    if (States[i].Finished) { Winner = new[] { i }; return; }
                return;
            }

            foreach (var group in Enumerable.Range(0, States.Length).GroupBy(i => Side[i]))
                if (group.All(i => States[i].Finished)) { Winner = group.ToArray(); return; }
        }
    }
}
