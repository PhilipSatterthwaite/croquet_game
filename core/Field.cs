using System;

namespace Croquet.Core
{
    /// <summary>Which game is being played. The two have different courts, courses and laws.</summary>
    public enum Variant
    {
        /// <summary>USCA nine-wicket backyard croquet: 16 points, two stakes.</summary>
        NineWicket,

        /// <summary>WCF Association Croquet on six hoops: 13 points, one peg.</summary>
        SixWicket
    }

    /// <summary>
    /// A hoop, as two uprights with a gap between them.
    ///
    /// Every hoop on both courts is set square to the long axis, so a hoop is
    /// run travelling along x in one direction or the other and needs no
    /// orientation of its own. On the nine-wicket court that is very nearly
    /// true of the side hoops as well; on the association court it is exact,
    /// because the laws set every hoop parallel to two opposite boundaries.
    /// The association court is laid out here rotated a quarter turn from the
    /// diagram in the Laws, so that its long axis is x like the other one.
    /// </summary>
    public readonly struct Hoop
    {
        public readonly Vec2 Center;
        public readonly double HalfGap;
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
    /// The court and the course over it: where the hoops and pegs are, and
    /// which of them each point of the course is.
    ///
    /// Both games share a shape. A handful of hoops carry twice as many points,
    /// because most are run once out and once back, so a point maps to a hoop
    /// AND a direction. That relationship is the whole of this class.
    /// </summary>
    public sealed class Field
    {
        public const double Yard = 0.9144;
        public const double Foot = 0.3048;

        public readonly Variant Variant;
        public readonly Hoop[] Hoops;

        /// <summary>Pegs, in course order of the points they carry.</summary>
        public readonly Vec2[] Pegs;
        public readonly double PegRadius;

        /// <summary>Where a ball enters the game on its first turn.</summary>
        public readonly Vec2 StartSpot;

        /// <summary>The name of each point, in order.</summary>
        public readonly string[] Labels;

        readonly int[] hoopOfPoint;    // -1 where the point is a peg
        readonly int[] dirOfPoint;     // +1 / -1 along x; 0 for a peg
        readonly int[] pegOfPoint;     // index into Pegs, or -1

        Field(Variant variant, Hoop[] hoops, Vec2[] pegs, double pegRadius, Vec2 start,
              string[] labels, int[] hoopOf, int[] dirOf, int[] pegOf)
        {
            Variant = variant;
            Hoops = hoops;
            Pegs = pegs;
            PegRadius = pegRadius;
            StartSpot = start;
            Labels = labels;
            hoopOfPoint = hoopOf;
            dirOfPoint = dirOf;
            pegOfPoint = pegOf;
        }

        public int TotalPoints => Labels.Length;

        /// <summary>
        /// Nine-wicket course indices. Named because the rules and everyone
        /// discussing them say "the turning post", not "point 7".
        /// </summary>
        public const int TurningPegPoint = 7;
        public const int HomePegPoint = 15;

        /// <summary>
        /// The two nine-wicket pegs by name. Association croquet has a single
        /// peg, which both of these resolve to.
        /// </summary>
        public Vec2 HomePeg => Pegs[0];
        public Vec2 TurningPeg => Pegs[Pegs.Length - 1];
        public int HoopFor(int point) => hoopOfPoint[point];
        public int DirectionFor(int point) => dirOfPoint[point];
        public bool IsPeg(int point) => hoopOfPoint[point] < 0;
        public Vec2 PegFor(int point) => Pegs[pegOfPoint[point]];
        public int PegIndexFor(int point) => pegOfPoint[point];

        public Vec2 TargetFor(int point) =>
            IsPeg(point) ? PegFor(point) : Hoops[hoopOfPoint[point]].Center;

        /// <summary>A ball that has scored every point is round.</summary>
        public bool IsFinished(int point) => point >= TotalPoints;

        // ------------------------------------------------------------------

        public static Field For(Variant v, double hoopGap = 0, double wireRadius = 0.006,
                                double pegRadius = 0.019) =>
            v == Variant.SixWicket
                ? SixWicket(hoopGap > 0 ? hoopGap : 0.24, wireRadius, pegRadius)
                : NineWicket(hoopGap > 0 ? hoopGap : 0.34, wireRadius, pegRadius);

        /// <summary>The court a variant is played on. Sizes come from the rules.</summary>
        public static CourtSpec CourtFor(Variant v) =>
            v == Variant.SixWicket
                ? new CourtSpec { Width = 35 * Yard, Height = 28 * Yard }   // 32.0 x 25.6 m
                : new CourtSpec { Width = 100 * Foot, Height = 50 * Foot }; // 30.5 x 15.2 m

        /// <summary>
        /// The USCA backyard double diamond on a 100 by 50 foot court, symmetric
        /// end to end: the home peg mirrors the turning peg, hoop 1 mirrors
        /// hoop 7, and the two diamonds mirror each other.
        /// </summary>
        /// <param name="hoopGap">
        /// A real backyard wicket is about 0.17 m against a 0.092 m ball --
        /// punishingly tight while the game logic is still under test, so the
        /// default is double that.
        /// </param>
        public static Field NineWicket(double hoopGap = 0.34, double wireRadius = 0.006,
                                       double pegRadius = 0.019)
        {
            const double mid = 25;      // centre line, feet
            Vec2 Ft(double x, double y) => new Vec2(x * Foot, y * Foot);

            var hoops = new[]
            {
                new Hoop(Ft(12, mid),      hoopGap / 2, wireRadius),   // 0  pts 1,14
                new Hoop(Ft(21, mid),      hoopGap / 2, wireRadius),   // 1  pts 2,13
                new Hoop(Ft(33, mid - 12), hoopGap / 2, wireRadius),   // 2  pt  3
                new Hoop(Ft(50, mid),      hoopGap / 2, wireRadius),   // 3  pts 4,11
                new Hoop(Ft(67, mid - 12), hoopGap / 2, wireRadius),   // 4  pt  5
                new Hoop(Ft(79, mid),      hoopGap / 2, wireRadius),   // 5  pts 6,9
                new Hoop(Ft(88, mid),      hoopGap / 2, wireRadius),   // 6  pts 7,8
                new Hoop(Ft(67, mid + 12), hoopGap / 2, wireRadius),   // 7  pt  10
                new Hoop(Ft(33, mid + 12), hoopGap / 2, wireRadius)    // 8  pt  12
            };

            var pegs = new[] { Ft(6, mid), Ft(94, mid) };              // 0 home, 1 turning

            var labels = new[]
            {
                "Wicket 1", "Wicket 2", "Wicket 3", "Wicket 4", "Wicket 5", "Wicket 6",
                "Wicket 7", "Turning post",
                "Wicket 8", "Wicket 9", "Wicket 10", "Wicket 11", "Wicket 12",
                "Wicket 13", "Wicket 14", "Home post"
            };

            var hoopOf = new[] { 0, 1, 2, 3, 4, 5, 6, -1, 6, 5, 7, 3, 8, 1, 0, -1 };
            var dirOf  = new[] { 1, 1, 1, 1, 1, 1, 1,  0, -1, -1, -1, -1, -1, -1, -1, 0 };
            var pegOf  = new[] { -1, -1, -1, -1, -1, -1, -1, 1, -1, -1, -1, -1, -1, -1, -1, 0 };

            // Halfway between the finishing stake and wicket 1, per the rules.
            var start = new Vec2((pegs[0].X + hoops[0].Center.X) / 2, pegs[0].Y);

            return new Field(Variant.NineWicket, hoops, pegs, pegRadius, start,
                             labels, hoopOf, dirOf, pegOf);
        }

        /// <summary>
        /// The WCF standard court, 28 by 35 yards, rotated so its long axis is x.
        /// Four outer hoops seven yards in from the two adjacent boundaries, two
        /// inner hoops seven yards either side of the peg, peg in the centre.
        ///
        /// Six hoops carry twelve points: each is run once outward and once on
        /// the way back, which is why the circuit reads as three laps of the
        /// court and every leg of it is a straight run.
        /// </summary>
        /// <param name="hoopGap">
        /// The laws set a hoop between 94 and 102 mm against a 92 mm ball --
        /// a couple of millimetres of daylight, which is what makes association
        /// croquet what it is. The default here is far wider while the rules are
        /// the thing under test. Narrow it to 0.098 for the real game.
        /// </param>
        public static Field SixWicket(double hoopGap = 0.24, double wireRadius = 0.008,
                                      double pegRadius = 0.019)
        {
            Vec2 Yd(double x, double y) => new Vec2(x * Yard, y * Yard);

            var hoops = new[]
            {
                new Hoop(Yd( 7,  7), hoopGap / 2, wireRadius),   // 0  A  hoops 1 and 2-back
                new Hoop(Yd(28,  7), hoopGap / 2, wireRadius),   // 1  B  hoops 2 and 1-back
                new Hoop(Yd(28, 21), hoopGap / 2, wireRadius),   // 2  C  hoops 3 and 4-back
                new Hoop(Yd( 7, 21), hoopGap / 2, wireRadius),   // 3  D  hoops 4 and 3-back
                new Hoop(Yd(10.5, 14), hoopGap / 2, wireRadius), // 4  E  hoop 5 and rover
                new Hoop(Yd(24.5, 14), hoopGap / 2, wireRadius)  // 5  F  hoop 6 and penultimate
            };

            var pegs = new[] { Yd(17.5, 14) };

            var labels = new[]
            {
                "Hoop 1", "Hoop 2", "Hoop 3", "Hoop 4", "Hoop 5", "Hoop 6",
                "1-back", "2-back", "3-back", "4-back", "Penultimate", "Rover",
                "Peg"
            };

            //            1  2  3  4  5  6  1b 2b 3b 4b pen rov peg
            var hoopOf = new[] { 0, 1, 2, 3, 4, 5, 1, 0, 3, 2, 5, 4, -1 };
            var dirOf  = new[] { 1, 1, -1, -1, 1, 1, -1, -1, 1, 1, -1, -1, 0 };
            var pegOf  = new[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0 };

            // Balls come in from a baulk-line; this is the middle of baulk A.
            var start = Yd(1, 7);

            return new Field(Variant.SixWicket, hoops, pegs, pegRadius, start,
                             labels, hoopOf, dirOf, pegOf);
        }
    }
}
