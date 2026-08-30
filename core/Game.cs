using System;
using System.Collections.Generic;
using System.Linq;

namespace Croquet.Core
{
    /// <summary>What the striker is allowed to do with the stroke in hand.</summary>
    public enum StrokeKind
    {
        /// <summary>An ordinary stroke: strike your own ball and see what happens.</summary>
        Ordinary,

        /// <summary>
        /// The stroke owed after a roquet. The striker's ball is picked up and
        /// placed touching the roqueted ball, and both are sent on together.
        /// </summary>
        Croquet
    }

    /// <summary>
    /// What the striker does with the stroke it owes after a roquet. All three
    /// place the striker against (or a mallet head from) the ball it hit; they
    /// differ in what moves afterwards.
    /// </summary>
    public enum CroquetStyle
    {
        /// <summary>
        /// Placed a mallet head clear of the roqueted ball, then struck. Only
        /// the striker travels — nothing is sent.
        /// </summary>
        Continue,

        /// <summary>
        /// Placed touching, at whatever angle the striker chooses, and struck.
        /// Both balls travel, and the angle between them is the choice.
        /// </summary>
        Split,

        /// <summary>
        /// Placed touching as for a split, but the striker stays put and all of
        /// it goes into the other ball. The way to put a ball somewhere without
        /// giving up your own position.
        /// </summary>
        Send
    }

    /// <summary>The rules-side state of one ball. Physics lives in Ball.</summary>
    public sealed class BallState
    {
        /// <summary>How far round the course: 0 is for wicket 1, 16 is pegged out.</summary>
        public int Point;

        /// <summary>
        /// False until this ball's first turn comes round. Balls are not laid
        /// out on the lawn at the start; each one enters from the starting spot
        /// when it is first played.
        /// </summary>
        public bool Started;

        /// <summary>Balls this one has roqueted since it last ran a wicket.</summary>
        public readonly HashSet<int> Dead = new HashSet<int>();

        public bool Finished => Course.IsFinished(Point);
    }

    /// <summary>What a stroke produced. Purely a report; the game is already updated.</summary>
    public sealed class StrokeResult
    {
        public int Striker;
        public readonly List<int> PointsScored = new List<int>();
        public int Roqueted = -1;
        public bool WentOut;
        public bool PeggedOut;

        /// <summary>The croqueted ball was sent off the lawn — a fault.</summary>
        public bool Faulted;
        public bool TurnEnded;
        public StrokeKind Next;
        public int NextStriker;
    }

    /// <summary>
    /// Nine-wicket croquet's turn structure.
    ///
    /// A turn is one stroke plus whatever that stroke earns:
    ///
    ///   * run your wicket           -> one continuation stroke, and your
    ///                                  deadness is wiped
    ///   * hit the turning peg       -> one continuation stroke
    ///   * roquet a ball you are
    ///     alive on                  -> a croquet stroke, then a continuation
    ///                                  stroke, and you are now dead on it
    ///
    /// Earn nothing and the turn passes. That is the whole engine, and it is
    /// deliberately separate from the physics: it consumes the ordered events
    /// of a shot and never looks at a velocity.
    ///
    /// Order matters, which is why events carry a step. Running your wicket
    /// clears deadness, so hitting a ball you were dead on is a live roquet if
    /// the wicket came first in the same stroke, and nothing at all if it did
    /// not.
    /// </summary>
    public sealed class Game
    {
        public readonly World World;
        public readonly BallState[] States;

        /// <summary>Index into World.Balls of whoever is on strike.</summary>
        public int Striker { get; private set; }

        public StrokeKind Stroke { get; private set; } = StrokeKind.Ordinary;

        /// <summary>The ball a pending croquet stroke must be taken from, or -1.</summary>
        public int CroquetFrom { get; private set; } = -1;

        /// <summary>Set once a side has taken every one of its balls round.</summary>
        public int[] Winner { get; private set; }

        /// <summary>Ball index -> side. Null means every ball for itself.</summary>
        public readonly int[] Side;

        public Game(World world, int[] side = null)
        {
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

        /// <summary>
        /// Brings a ball onto the lawn for its first stroke. Every ball starts
        /// from the same spot, so if the last one to start has not moved off it
        /// yet the newcomer is nudged clear rather than placed on top of it.
        /// </summary>
        void EnterLawn(int i)
        {
            if (States[i].Started || States[i].Finished) return;

            States[i].Started = true;
            World.Balls[i].InPlay = true;
            World.Balls[i].Pos = FreeStartSpot(i);
        }

        Vec2 FreeStartSpot(int forBall)
        {
            var spot = World.Field.StartSpot(World.Spec);
            double step = World.Spec.BallRadius * 2.2;

            for (int k = 0; k < 40; k++)
            {
                // The spot itself first, then alternating either side of it.
                int rank = (k + 1) / 2;
                double dy = (k % 2 == 0 ? 1 : -1) * rank * step;
                var p = new Vec2(spot.X, spot.Y + dy);

                bool clear = true;
                for (int j = 0; j < World.Balls.Length && clear; j++)
                {
                    if (j == forBall || !World.Balls[j].InPlay) continue;
                    if ((World.Balls[j].Pos - p).LengthSq < (step * step)) clear = false;
                }
                if (clear) return p;
            }
            return spot;
        }

        public BallState Current => States[Striker];

        /// <summary>The course point the striker is playing for.</summary>
        public int Target => Current.Point;

        /// <summary>
        /// Is the striker allowed to take a roquet off this ball? False once it
        /// has been roqueted, until the striker runs its next wicket.
        /// </summary>
        public bool IsAlive(int on) => !Current.Dead.Contains(on);

        // ------------------------------------------------------------------

        /// <summary>
        /// Plays the stroke in hand: sets the striker rolling and resolves what
        /// the shot earned. The caller has already positioned the balls for a
        /// croquet stroke, if that is what this is.
        /// </summary>
        public StrokeResult Play(Vec2 direction, double power)
        {
            if (Winner != null) throw new InvalidOperationException("the game is over");
            if (Stroke == StrokeKind.Croquet)
                throw new InvalidOperationException("a croquet stroke is owed; use PlayCroquet");

            World.ClearShot();
            World.Balls[Striker].Vel = direction.Normalized * power;
            Sim.Settle(World);

            return Resolve();
        }

        /// <summary>
        /// Takes the croquet stroke owed by a roquet.
        ///
        /// <paramref name="placement"/> is the direction from the roqueted ball
        /// to where the striker is set down — that angle, against the aim, is
        /// what decides where the two balls part company on a split.
        /// </summary>
        public StrokeResult PlayCroquet(CroquetStyle style, Vec2 placement,
                                        Vec2 aim, double power)
        {
            if (Winner != null) throw new InvalidOperationException("the game is over");
            if (Stroke != StrokeKind.Croquet)
                throw new InvalidOperationException("no croquet stroke is owed");

            int other = CroquetFrom;

            Vec2 n = placement.Normalized;
            if (n.LengthSq == 0) n = new Vec2(-1, 0);

            double gap = World.Spec.BallRadius * 2
                       + (style == CroquetStyle.Continue ? World.Spec.MalletHead : 0);
            World.Balls[Striker].Pos = World.Balls[other].Pos + n * gap;

            World.ClearShot();

            // A send puts everything into the other ball and leaves the striker
            // standing. Modelling it as "strike the other ball" rather than as
            // a very heavy follow-through keeps the striker exactly where it
            // was put, which is the whole point of the stroke.
            if (style == CroquetStyle.Send)
                World.Balls[other].Vel = aim.Normalized * power;
            else
                World.Balls[Striker].Vel = aim.Normalized * power;

            Sim.Settle(World);
            return Resolve();
        }

        StrokeResult Resolve()
        {
            var r = new StrokeResult { Striker = Striker };
            var me = Current;

            bool wasCroquet = Stroke == StrokeKind.Croquet;
            int croquetedBall = CroquetFrom;

            // Deadness as it stood when the ball was struck. The scoring loop
            // below wipes it, so a snapshot is the only way to ask "was the
            // striker alive on that ball at the moment it hit it".
            var deadAtStart = new HashSet<int>(me.Dead);

            // 1. Points, in course order. One stroke can run more than one --
            //    through your wicket and on through the next is rare but legal.
            int clearedAtStep = -1;      // the substep a wicket wiped deadness
            while (!me.Finished)
            {
                int point = me.Point;
                int at = World.Field.IsPeg(point)
                       ? World.StepHitPeg(Striker, point)
                       : World.StepRanPoint(Striker, point);
                if (at < 0) break;

                me.Point++;
                r.PointsScored.Add(point);

                // Running a wicket brings you alive on everything again. The
                // pegs do not: they are struck, not run.
                if (!World.Field.IsPeg(point)) { me.Dead.Clear(); clearedAtStep = at; }

                if (me.Finished)
                {
                    World.Balls[Striker].InPlay = false;
                    r.PeggedOut = true;
                    break;
                }
            }

            // 2. The roquet: the first ball the striker touched that it was
            //    alive on AT THE MOMENT OF CONTACT. Dead at the start of the
            //    stroke still counts if a wicket was run before the contact --
            //    which is the whole reason events carry a step.
            if (!r.PeggedOut)
            {
                foreach (var e in World.Events)
                {
                    if (e.Kind != EventKind.BallContact) continue;
                    if (e.Ball != Striker && e.Other != Striker) continue;

                    int other = e.Ball == Striker ? e.Other : e.Ball;
                    bool alive = !deadAtStart.Contains(other)
                              || (clearedAtStep >= 0 && e.Step > clearedAtStep);
                    if (!alive) continue;

                    r.Roqueted = other;
                    me.Dead.Add(other);
                    break;
                }
            }

            r.WentOut = World.Balls[Striker].WentOut;

            // Sending the ball you took croquet from off the lawn is a fault,
            // and a fault ends the turn however well the rest of it went.
            bool faulted = wasCroquet && croquetedBall >= 0
                        && World.Balls[croquetedBall].WentOut;
            r.Faulted = faulted;

            // 3. What the striker has left.
            //    A roquet owes a croquet stroke. A point owes a continuation.
            //    And a croquet stroke ALWAYS owes the continuation that came
            //    with the roquet, even if the croquet stroke itself did
            //    nothing -- that is the pair of strokes a roquet buys.
            bool carryOn;
            StrokeKind next = StrokeKind.Ordinary;

            if (r.PeggedOut || r.WentOut || faulted) carryOn = false;
            else if (r.Roqueted >= 0) { carryOn = true; next = StrokeKind.Croquet; }
            else if (r.PointsScored.Count > 0) carryOn = true;
            else carryOn = wasCroquet;

            if (!carryOn)
            {
                CroquetFrom = -1;
                Stroke = StrokeKind.Ordinary;
                r.TurnEnded = true;
                CheckWinner();
                if (Winner == null) NextStriker();
            }
            else
            {
                Stroke = next;
                CroquetFrom = next == StrokeKind.Croquet ? r.Roqueted : -1;
            }

            r.Next = Stroke;
            r.NextStriker = Striker;
            return r;
        }

        /// <summary>
        /// Where the striker would be set down for this croquet stroke, without
        /// committing to it. For drawing the preview while the player chooses.
        /// </summary>
        public Vec2 CroquetPlacement(CroquetStyle style, Vec2 placement)
        {
            if (Stroke != StrokeKind.Croquet)
                throw new InvalidOperationException("no croquet stroke is owed");

            Vec2 n = placement.Normalized;
            if (n.LengthSq == 0) n = new Vec2(-1, 0);
            double gap = World.Spec.BallRadius * 2
                       + (style == CroquetStyle.Continue ? World.Spec.MalletHead : 0);
            return World.Balls[CroquetFrom].Pos + n * gap;
        }

        void NextStriker()
        {
            // Skips balls that are ROUND, not balls that are off the lawn --
            // a ball that has not started yet is still due its turn, and gets
            // brought on when that turn arrives.
            for (int k = 1; k <= States.Length; k++)
            {
                int j = (Striker + k) % States.Length;
                if (States[j].Finished) continue;
                Striker = j;
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
