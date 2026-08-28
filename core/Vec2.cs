using System;

namespace Croquet.Core
{
    /// <summary>
    /// A 2D vector in metres. Doubles rather than floats, and deliberately no
    /// trigonometry: IEEE-754 add/multiply/divide/sqrt are correctly rounded and
    /// so give bit-identical results everywhere, while Sin/Cos/Atan2 are not
    /// guaranteed to agree between platforms or runtime versions. Keeping the
    /// simulation to these four operations is what makes a shot replay to the
    /// same final position on every device -- which is the whole basis for
    /// sending shots over the wire instead of ball positions.
    ///
    /// Aiming code may use trigonometry freely; it runs before the shot and its
    /// result is part of the shot input, not of the simulation.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0, 0);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(double s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, double s) => new Vec2(a.X / s, a.Y / s);

        public double Dot(Vec2 b) => X * b.X + Y * b.Y;

        /// <summary>Squared length. Prefer this to Length for comparisons -- it
        /// avoids a square root and the rounding that comes with it.</summary>
        public double LengthSq => X * X + Y * Y;

        public double Length => Math.Sqrt(LengthSq);

        /// <summary>Unit vector, or Zero for a zero-length input. Never throws:
        /// a stationary ball asking for its direction is ordinary, not an error.</summary>
        public Vec2 Normalized
        {
            get
            {
                double len = Length;
                return len > 0 ? new Vec2(X / len, Y / len) : Zero;
            }
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.####}, {Y:0.####})";
    }
}
