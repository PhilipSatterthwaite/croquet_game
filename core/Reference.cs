using System.Collections.Generic;

namespace Croquet.Core
{
    /// <summary>
    /// One fixed match, played the same way everywhere, so that two builds can
    /// be asked whether they agree.
    ///
    /// This exists because of a gap that is easy to miss: the test suite runs
    /// on CoreCLR and the game runs on Mono or IL2CPP, and nothing about
    /// passing tests says those three arrive at the same doubles. Online play
    /// that sends STROKES rather than positions is built entirely on their
    /// agreeing, so it is worth being able to check rather than assume.
    ///
    /// The rally is nothing special -- it is only long enough to run balls into
    /// each other, through hoops and off the lawn, which is where two
    /// implementations would part company if they were going to.
    ///
    /// <see cref="Hash"/> failing does NOT mean something is broken. Any
    /// deliberate change to the simulation moves it, and the answer is then to
    /// re-pin it. What it must never do is move on its own, or differ between
    /// two machines running the same build.
    /// </summary>
    public static class Reference
    {
        /// <summary>
        /// What <see cref="Play"/> arrives at. Pinned from CoreCLR; the same
        /// number should come back from a Unity build.
        /// </summary>
        public const ulong Hash = 0x91CC2FA93F2FA2DCUL;

        /// <summary>How many strokes the rally is, once the rules have had their say.</summary>
        public const int Length = 8;

        /// <summary>
        /// A court written out in full rather than taken from the defaults, so
        /// that tuning the game never changes what this checks.
        /// </summary>
        public static MatchSetup Setup() => new MatchSetup
        {
            Variant = Variant.NineWicket,
            Balls = 4,
            Teams = 0,
            Court = new CourtSpec
            {
                Width = 30,
                Height = 15,
                Friction = 0.9,
                Restitution = 0.8,
                ObstacleRestitution = 0.5
            }
        };

        public static IEnumerable<Stroke> Strokes()
        {
            yield return Stroke.Ordinary(new Vec2(1, 0.05), 4.2);
            yield return Stroke.Ordinary(new Vec2(0.8, 0.6), 3.1);
            yield return Stroke.Ordinary(new Vec2(1, -0.2), 5.5);
            yield return Stroke.Ordinary(new Vec2(-0.4, 1), 2.7);
            yield return Stroke.Ordinary(new Vec2(1, 0.02), 6.4);
            yield return Stroke.Ordinary(new Vec2(0.2, -1), 3.9);
            yield return Stroke.Ordinary(new Vec2(-1, 0.3), 4.8);
            yield return Stroke.Ordinary(new Vec2(1, 0.9), 5.1);
        }

        /// <summary>
        /// Plays the rally. A roquet leaves a bonus stroke owed, which an
        /// ordinary stroke does not fit; taking it the same way every time
        /// keeps the sequence reproducible without this having to know the
        /// rules.
        /// </summary>
        public static Match Play()
        {
            var m = Match.Begin(Setup());

            foreach (var s in Strokes())
            {
                if (m.Game.Winner != null) break;

                var fitting = s.IsBonus == (m.Game.Stroke == StrokeKind.Bonus)
                    ? s
                    : Stroke.Bonus(BonusWay.CroquetShot, new Vec2(-1, 0), s.Aim, s.Power);

                m.Play(fitting);
            }

            return m;
        }

        /// <summary>Whether this build agrees with the pinned number.</summary>
        public static bool Agrees(out ulong got)
        {
            got = Play().Hash;
            return got == Hash;
        }
    }
}
