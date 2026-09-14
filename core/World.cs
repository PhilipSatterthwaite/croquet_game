using System;
using System.Collections.Generic;

namespace Croquet.Core
{
    /// <summary>
    /// Everything one shot needs: the balls, the court they are on, and the
    /// bookkeeping that accumulates while they roll.
    ///
    /// The passes tally is the interesting part. Whether a hoop was RUN cannot
    /// be decided from where the balls finish -- a ball can go through and roll
    /// back out, which scores nothing, and end up on the far side after
    /// wandering round the outside, which also scores nothing. So crossings are
    /// counted as they happen, signed by direction, and the tally is read at
    /// the end of the shot. A net of +1 means through and stayed through.
    /// </summary>
    public sealed class World
    {
        public readonly Ball[] Balls;
        public readonly Field Field;
        public readonly CourtSpec Spec;

        /// <summary>[ball, hoop] net signed crossings since the last ClearShot.</summary>
        public readonly int[,] Passes;

        /// <summary>
        /// [ball, hoop] which side of each hoop a ball was last definitely on:
        /// -1 for the low-x side, +1 for the high-x side, 0 for never yet.
        ///
        /// This is the "jaws" of the wicket, and it is the whole reason the
        /// state has to persist between strokes. A hoop is not a plane, it is a
        /// gap with thickness, and a ball can stop inside it -- ball C in the
        /// USCA diagram, which has NOT scored. Being in the jaws is neither
        /// side, so it changes nothing here; the ball keeps the side it came
        /// from until it fully clears one or the other.
        ///
        /// That gives the rules for free. Stopping in the jaws scores nothing
        /// now and scores properly on the stroke that carries the ball out.
        /// Going part of the way back and forward again scores nothing, because
        /// the side never changed. And "if a ball travels backwards through its
        /// wicket to get position, it must be clear of the non-playing side to
        /// then score the wicket in the correct direction" is simply what
        /// having to change sides means.
        /// </summary>
        public readonly int[,] Side;

        /// <summary>Everything that happened this shot, in the order it happened.</summary>
        public readonly List<ShotEvent> Events = new List<ShotEvent>();

        /// <summary>Substep counter, so events can be ordered against each other.</summary>
        public int Step;

        /// <summary>
        /// The two balls of a croquet stroke, striker first, or -1 when this shot
        /// is not one. Their first contact gets the mallet's follow-through; see
        /// <see cref="CourtSpec.CroquetFollow"/>.
        /// </summary>
        public int CroquetStriker = -1, CroquetOther = -1;

        /// <summary>Whether that first contact has happened, so it is given once.</summary>
        internal bool CroquetSpent;

        /// <summary>
        /// Marks this shot as a croquet stroke between these two. Call it AFTER
        /// <see cref="ClearShot"/>, which forgets it.
        /// </summary>
        public void TakeCroquet(int striker, int other)
        {
            CroquetStriker = striker;
            CroquetOther = other;
            CroquetSpent = false;
        }

        internal bool IsCroquetPair(int a, int b) =>
            !CroquetSpent && CroquetStriker >= 0 &&
            ((a == CroquetStriker && b == CroquetOther) ||
             (a == CroquetOther && b == CroquetStriker));

        // Only the first touch of each pair is an event. A ball resting against
        // another re-collides every substep, and a rules layer asking "what did
        // the striker hit first" does not want that noise.
        readonly bool[,] touched;

        public World(Ball[] balls, Field field, CourtSpec spec)
        {
            Balls = balls;
            Field = field;
            Spec = spec;
            Passes = new int[balls.Length, field.Hoops.Length];
            Side = new int[balls.Length, field.Hoops.Length];
            touched = new bool[balls.Length, balls.Length];
        }

        /// <summary>Wipes the per-shot tallies. Call before striking.</summary>
        public void ClearShot()
        {
            Events.Clear();
            Step = 0;
            CroquetStriker = CroquetOther = -1;
            CroquetSpent = false;
            for (int b = 0; b < Balls.Length; b++)
            {
                Balls[b].WentOut = false;
                for (int h = 0; h < Field.Hoops.Length; h++) Passes[b, h] = 0;
                for (int o = 0; o < Balls.Length; o++) touched[b, o] = false;
            }
        }

        internal void NoteContact(int a, int b)
        {
            if (touched[a, b]) return;
            touched[a, b] = touched[b, a] = true;
            Events.Add(ShotEvent.Contact(Step, a, b));
        }

        internal void NoteCross(int ball, int hoop, int dir)
        {
            Passes[ball, hoop] += dir;
            Events.Add(ShotEvent.Cross(Step, ball, hoop, dir));
        }

        internal void NotePeg(int ball, int pegIndex) =>
            Events.Add(ShotEvent.Peg(Step, ball, pegIndex));

        internal void NoteOut(int ball) => Events.Add(ShotEvent.Out(Step, ball));

        /// <summary>
        /// Did this ball run the hoop for this course point during the shot?
        /// The point carries the direction, which is what stops a ball coming
        /// home through hoop 2 from being credited with hoop 13.
        ///
        /// Two things have to hold, and they are different things. The ball has
        /// to have changed sides the right way during the stroke -- and it has
        /// to be sitting clear at the end of it, because "if a ball passes
        /// through a wicket but rolls back, it has not scored the wicket".
        /// </summary>
        public bool RanPoint(int ball, int point)
        {
            int hoop = Field.HoopFor(point);
            if (hoop < 0) return false;                 // pegs are hit, not run

            int dir = Field.DirectionFor(point);
            bool crossed = dir > 0 ? Passes[ball, hoop] > 0
                                   : Passes[ball, hoop] < 0;

            return crossed && Clear(ball, hoop, dir);
        }

        /// <summary>
        /// Whether the ball is entirely past the far face of the hoop -- not
        /// touching it, not in the jaws. Ball D in the USCA diagram rather than
        /// ball C.
        /// </summary>
        public bool Clear(int ball, int hoop, int dir)
        {
            var h = Field.Hoops[hoop];
            double deep = dir * (Balls[ball].Pos.X - h.Center.X);
            return deep > h.WireRadius + Spec.BallRadius;
        }

        /// <summary>
        /// The hoop whose jaws this ball is stuck in, or -1: not clear of either
        /// face, and between the uprights. Ball C in the USCA diagram, and what
        /// Challenging Option 11 calls "wicketed".
        ///
        /// Between the uprights means the centre is inside the gap. A ball
        /// resting against the outside of a post can overlap the hoop's
        /// thickness too, but it is beside the wicket, not in it.
        /// </summary>
        public int JawsOf(int ball)
        {
            var p = Balls[ball].Pos;
            for (int i = 0; i < Field.Hoops.Length; i++)
            {
                var h = Field.Hoops[i];
                if (Math.Abs(p.X - h.Center.X) < h.WireRadius + Spec.BallRadius &&
                    Math.Abs(p.Y - h.Center.Y) < h.HalfGap)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Hands the jaws state to a copy of this world.
        ///
        /// Game.Clone builds a fresh World, which would otherwise start every
        /// ball as never having been near a hoop -- and a bot searching from a
        /// position with a ball sitting in the jaws would conclude it could
        /// never score it.
        /// </summary>
        public void CopySidesTo(World other)
        {
            if (other == null) return;
            Array.Copy(Side, other.Side, Side.Length);
        }

        /// <summary>
        /// The substep on which the ball completed the running of that hoop --
        /// the crossing that left the tally where it finished. Used to order a
        /// hoop against a roquet in the same stroke. -1 if it was not run.
        /// </summary>
        public int StepRanPoint(int ball, int point)
        {
            if (!RanPoint(ball, point)) return -1;
            int hoop = Field.HoopFor(point);
            int step = -1;
            for (int i = 0; i < Events.Count; i++)
            {
                var e = Events[i];
                if (e.Kind == EventKind.HoopCross && e.Ball == ball && e.Hoop == hoop)
                    step = e.Step;
            }
            return step;
        }

        /// <summary>
        /// Did the ball touch the peg that carries this course point, and on
        /// which substep? -1 if not. Nine wicket has two pegs and association
        /// croquet one, so the point has to be resolved to a peg first.
        /// </summary>
        public int StepHitPeg(int ball, int point)
        {
            int peg = Field.PegIndexFor(point);
            if (peg < 0) return -1;

            for (int i = 0; i < Events.Count; i++)
            {
                var e = Events[i];
                if (e.Kind == EventKind.PegContact && e.Ball == ball && e.Value == peg)
                    return e.Step;
            }
            return -1;
        }
    }
}
