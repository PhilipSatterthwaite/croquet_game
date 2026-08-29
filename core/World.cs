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

        /// <summary>Everything that happened this shot, in the order it happened.</summary>
        public readonly List<ShotEvent> Events = new List<ShotEvent>();

        /// <summary>Substep counter, so events can be ordered against each other.</summary>
        public int Step;

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
            touched = new bool[balls.Length, balls.Length];
        }

        /// <summary>Wipes the per-shot tallies. Call before striking.</summary>
        public void ClearShot()
        {
            Events.Clear();
            Step = 0;
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

        internal void NotePeg(int ball, int pegPoint) =>
            Events.Add(ShotEvent.Peg(Step, ball, pegPoint));

        internal void NoteOut(int ball) => Events.Add(ShotEvent.Out(Step, ball));

        /// <summary>
        /// Did this ball run the hoop for this course point during the shot?
        /// The point carries the direction, which is what stops a ball coming
        /// home through hoop 2 from being credited with hoop 13.
        /// </summary>
        public bool RanPoint(int ball, int point)
        {
            int hoop = Field.HoopFor(point);
            if (hoop < 0) return false;                 // pegs are hit, not run
            int dir = Field.DirectionFor(point);
            return dir > 0 ? Passes[ball, hoop] > 0
                           : Passes[ball, hoop] < 0;
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

        /// <summary>Did the ball touch a peg, and on which substep? -1 if not.</summary>
        public int StepHitPeg(int ball, int pegPoint)
        {
            for (int i = 0; i < Events.Count; i++)
            {
                var e = Events[i];
                if (e.Kind == EventKind.PegContact && e.Ball == ball && e.Value == pegPoint)
                    return e.Step;
            }
            return -1;
        }
    }
}
