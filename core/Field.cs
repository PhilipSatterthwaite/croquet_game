using System;

namespace Croquet.Core
{
    /// <summary>
    /// A hoop, as two uprights with a gap between them.
    ///
    /// Every hoop on a nine-wicket court is set square to the long axis, so a
    /// hoop is run travelling along x in one direction or the other and needs
    /// no orientation of its own. That is not a simplification: the outbound
    /// path meets even the side hoops within a few degrees of square, because
    /// it arrives from one diamond and leaves along the next.
    /// </summary>
    public readonly struct Hoop
    {
        /// <summary>Midpoint of the gap.</summary>
        public readonly Vec2 Center;

        /// <summary>Half the clear width between the uprights.</summary>
        public readonly double HalfGap;

        /// <summary>Radius of each upright. Backyard wickets are thin wire.</summary>
        public readonly double WireRadius;

        public Hoop(Vec2 center, double halfGap, double wireRadius)
        {
            Center = center;
            HalfGap = halfGap;
            WireRadius = wireRadius;
        }

        public Vec2 LeftPost => new Vec2(Center.X, Center.Y - HalfGap - WireRadius);
        public Vec2 RightPost => new Vec2(Center.X, Center.Y + HalfGap + WireRadius);
    }

    /// <summary>
    /// The nine-wicket court: nine hoops and two pegs, laid out as a double
    /// diamond down the long axis.
    ///
    /// Nine hoops carry fourteen wicket points because the outbound leg runs
    /// one side of each diamond and the return leg runs the other. So a point
    /// maps to a hoop AND a direction, and most hoops answer to two points --
    /// the same relationship the scorekeeper app draws as "2 · 13".
    ///
    /// Measurements are in feet in the source, because that is how a backyard
    /// court is actually set out, and converted once on the way in.
    /// </summary>
    public sealed class Field
    {
        public const double Foot = 0.3048;

        /// <summary>Points on the course, matching Course.Labels.</summary>
        public const int TurningPegPoint = 7;
        public const int HomePegPoint = 15;
        public const int TotalPoints = 16;

        public readonly Hoop[] Hoops;
        public readonly Vec2 HomePeg;
        public readonly Vec2 TurningPeg;
        public readonly double PegRadius;

        // Which hoop each point is, indexed by point. -1 marks the two pegs.
        static readonly int[] HoopOfPoint =
        {
            0,  1,  2,  3,  4,  5,  6,     // wickets 1-7, outbound
            -1,                            // turning peg
            6,  5,  7,  3,  8,  1,  0,     // wickets 8-14, homeward
            -1                             // home peg
        };

        // +1 runs a hoop up the court, -1 runs it back down.
        static readonly int[] DirOfPoint =
        {
            1, 1, 1, 1, 1, 1, 1,
            0,
            -1, -1, -1, -1, -1, -1, -1,
            0
        };

        Field(Hoop[] hoops, Vec2 homePeg, Vec2 turningPeg, double pegRadius)
        {
            Hoops = hoops;
            HomePeg = homePeg;
            TurningPeg = turningPeg;
            PegRadius = pegRadius;
        }

        /// <summary>
        /// The standard backyard double diamond on a 100 by 50 foot court,
        /// symmetric about the middle: the home peg mirrors the turning peg,
        /// hoop 1 mirrors hoop 7, and the two diamonds mirror each other.
        /// </summary>
        /// <param name="hoopGap">
        /// Clear width between the uprights. A real backyard wicket is about
        /// 0.17 m against a 0.092 m ball — punishingly tight while the game
        /// logic is still being exercised, so the default is double that.
        /// Narrow it once the rules stop being the thing under test.
        /// </param>
        public static Field NineWicket(double hoopGap = 0.34, double wireRadius = 0.006,
                                       double pegRadius = 0.019)
        {
            const double mid = 25;      // centre line, feet

            Vec2 Ft(double x, double y) => new Vec2(x * Foot, y * Foot);

            var hoops = new[]
            {
                new Hoop(Ft(12, mid),      hoopGap / 2, wireRadius),   // 0  A   pts 1,14
                new Hoop(Ft(21, mid),      hoopGap / 2, wireRadius),   // 1  B   pts 2,13
                new Hoop(Ft(33, mid - 12), hoopGap / 2, wireRadius),   // 2  L1  pt  3
                new Hoop(Ft(50, mid),      hoopGap / 2, wireRadius),   // 3  C   pts 4,11
                new Hoop(Ft(67, mid - 12), hoopGap / 2, wireRadius),   // 4  L2  pt  5
                new Hoop(Ft(79, mid),      hoopGap / 2, wireRadius),   // 5  T1  pts 6,9
                new Hoop(Ft(88, mid),      hoopGap / 2, wireRadius),   // 6  T2  pts 7,8
                new Hoop(Ft(67, mid + 12), hoopGap / 2, wireRadius),   // 7  R2  pt  10
                new Hoop(Ft(33, mid + 12), hoopGap / 2, wireRadius)    // 8  R1  pt  12
            };

            return new Field(hoops, Ft(6, mid), Ft(94, mid), pegRadius);
        }

        /// <summary>Hoop index for a course point, or -1 if the point is a peg.</summary>
        public int HoopFor(int point) => HoopOfPoint[point];

        /// <summary>+1 or -1 along the long axis; 0 for the pegs.</summary>
        public int DirectionFor(int point) => DirOfPoint[point];

        public bool IsPeg(int point) => HoopOfPoint[point] < 0;

        public Vec2 PegFor(int point) =>
            point == TurningPegPoint ? TurningPeg
          : point == HomePegPoint ? HomePeg
          : throw new ArgumentOutOfRangeException(nameof(point), "not a peg point");

        /// <summary>Where a ball playing for this point is trying to get to.</summary>
        public Vec2 TargetFor(int point) =>
            IsPeg(point) ? PegFor(point) : Hoops[HoopOfPoint[point]].Center;

        /// <summary>
        /// Where every ball plays its first stroke from: halfway between the
        /// finishing stake and wicket 1. All of them start from the same spot,
        /// one at a time as their turn first comes round -- they do not sit on
        /// the lawn in a row waiting.
        /// </summary>
        public Vec2 StartSpot =>
            new Vec2((HomePeg.X + Hoops[0].Center.X) / 2, HomePeg.Y);
    }
}
