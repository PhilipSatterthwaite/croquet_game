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

    /// <summary>The rules-side state of one ball. Physics lives in Ball.</summary>
    public sealed class BallState
    {
        /// <summary>How far round the course: 0 is for wicket 1, 16 is pegged out.</summary>
        public int Point;

        /// <summary>Balls this one has roqueted since it last ran a wicket.</summary>
        public readonly HashSet<int> Dead = new HashSet<int>();

        public bool Finished => Course.IsFinished(Point);

        public BallState Clone()
        {
            var c = new BallState { Point = Point };
            foreach (var d in Dead) c.Dead.Add(d);
            return c;
        }
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
            for (int i = 0; i < States.Length; i++) States[i] = new BallState();
            Side = side;
            Striker = 0;
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

            World.ClearShot();
            World.Balls[Striker].Vel = direction.Normalized * power;
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
        /// Where the striker's ball must be placed for a croquet stroke:
        /// touching the roqueted ball, on the side the striker chooses.
        /// </summary>
        public Vec2 CroquetPlacement(Vec2 fromDirection)
        {
            if (Stroke != StrokeKind.Croquet)
                throw new InvalidOperationException("no croquet stroke is owed");

            Vec2 n = fromDirection.Normalized;
            if (n.LengthSq == 0) n = new Vec2(-1, 0);
            return World.Balls[CroquetFrom].Pos + n * (World.Spec.BallRadius * 2);
        }

        /// <summary>Places the striker for the croquet stroke it owes.</summary>
        public void TakeCroquet(Vec2 fromDirection)
        {
            World.Balls[Striker].Pos = CroquetPlacement(fromDirection);
        }

        void NextStriker()
        {
            for (int k = 1; k <= States.Length; k++)
            {
                int j = (Striker + k) % States.Length;
                if (World.Balls[j].InPlay) { Striker = j; return; }
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
