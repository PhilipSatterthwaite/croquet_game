namespace Croquet.Core
{
    public enum EventKind
    {
        /// <summary>Two balls touched for the first time this shot.</summary>
        BallContact,

        /// <summary>A ball crossed the plane of a hoop between its uprights.</summary>
        HoopCross,

        /// <summary>A ball touched a peg.</summary>
        PegContact,

        /// <summary>A ball's centre left the lawn.</summary>
        OutOfBounds
    }

    /// <summary>
    /// Something that happened during a shot, stamped with the substep it
    /// happened on.
    ///
    /// The order is the point of this. Croquet turns hinge on sequence: running
    /// your hoop clears your deadness, so a ball you were dead on at the start
    /// of the stroke is fair game if you ran the hoop before you hit it, and is
    /// a fault if you hit it first. Final positions cannot tell those apart.
    /// </summary>
    public readonly struct ShotEvent
    {
        public readonly int Step;
        public readonly EventKind Kind;

        /// <summary>The ball this happened to.</summary>
        public readonly int Ball;

        /// <summary>BallContact: the ball it touched. Otherwise -1.</summary>
        public readonly int Other;

        /// <summary>HoopCross: which hoop. Otherwise -1.</summary>
        public readonly int Hoop;

        /// <summary>HoopCross: +1 up the court, -1 back down. PegContact: the peg's point.</summary>
        public readonly int Value;

        ShotEvent(int step, EventKind kind, int ball, int other, int hoop, int value)
        {
            Step = step; Kind = kind; Ball = ball; Other = other; Hoop = hoop; Value = value;
        }

        public static ShotEvent Contact(int step, int a, int b) =>
            new ShotEvent(step, EventKind.BallContact, a, b, -1, 0);

        public static ShotEvent Cross(int step, int ball, int hoop, int dir) =>
            new ShotEvent(step, EventKind.HoopCross, ball, -1, hoop, dir);

        public static ShotEvent Peg(int step, int ball, int pegPoint) =>
            new ShotEvent(step, EventKind.PegContact, ball, -1, -1, pegPoint);

        public static ShotEvent Out(int step, int ball) =>
            new ShotEvent(step, EventKind.OutOfBounds, ball, -1, -1, 0);
    }
}
