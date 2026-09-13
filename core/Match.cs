using System;
using System.Collections.Generic;

namespace Croquet.Core
{
    /// <summary>
    /// Everything the two ends have to agree about before a stroke is played.
    ///
    /// The court is carried whole rather than derived from the variant. It is
    /// only ten numbers, and the alternative -- rebuilding it from a default at
    /// each end -- means the day someone tunes the friction, two clients on
    /// different builds play the same strokes on different lawns and drift
    /// apart with nothing to point at. A match records the court it was played
    /// on.
    /// </summary>
    public sealed class MatchSetup
    {
        public Variant Variant = Variant.NineWicket;

        /// <summary>Balls in play. Association croquet is always four.</summary>
        public int Balls = 4;

        /// <summary>0 is every ball for itself.</summary>
        public int Teams;

        public bool CarryOverDeadness;
        public bool OutOfBoundsEndsTurn;

        /// <summary>The exact court. Null means the variant's own.</summary>
        public CourtSpec Court;

        public RuleOptions Options => new RuleOptions
        {
            CarryOverDeadness = CarryOverDeadness,
            OutOfBoundsEndsTurn = OutOfBoundsEndsTurn
        };

        public MatchSetup Clone() => new MatchSetup
        {
            Variant = Variant,
            Balls = Balls,
            Teams = Teams,
            CarryOverDeadness = CarryOverDeadness,
            OutOfBoundsEndsTurn = OutOfBoundsEndsTurn,
            Court = Court == null ? null : Copy(Court)
        };

        static CourtSpec Copy(CourtSpec c) => new CourtSpec
        {
            Width = c.Width,
            Height = c.Height,
            BallRadius = c.BallRadius,
            Friction = c.Friction,
            Restitution = c.Restitution,
            ObstacleRestitution = c.ObstacleRestitution,
            CroquetFollow = c.CroquetFollow,
            SleepSpeed = c.SleepSpeed,
            BoundaryReturn = c.BoundaryReturn,
            MalletHead = c.MalletHead,
            MalletLength = c.MalletLength
        };

        /// <summary>
        /// The court this setup means, with the same corrections a front end
        /// would apply -- association croquet settles its own ball count and
        /// sides, and a split that does not divide the balls evenly is no
        /// split at all.
        /// </summary>
        public void Settle()
        {
            if (Variant == Variant.SixWicket) { Balls = 4; Teams = 2; }
            else
            {
                if (Balls < 2) Balls = 2;
                if (Balls > 6) Balls = 6;
                if (Teams < 2 || Balls % Teams != 0) Teams = 0;
            }
            if (Court == null) Court = Field.CourtFor(Variant);
        }

        public ulong Hash
        {
            get
            {
                ulong h = Checksum.Mix(Checksum.Of(Court ?? Field.CourtFor(Variant)),
                                       (ulong)Variant);
                h = Checksum.Mix(h, (ulong)Balls);
                h = Checksum.Mix(h, (ulong)Teams);
                h = Checksum.Mix(h, CarryOverDeadness ? 1UL : 0UL);
                h = Checksum.Mix(h, OutOfBoundsEndsTurn ? 1UL : 0UL);
                return h;
            }
        }
    }

    /// <summary>
    /// A game, and the ordered list of strokes that made it.
    ///
    /// The list is the game. Because the simulation is deterministic, a setup
    /// plus its strokes reproduces the position exactly -- so this one type is
    /// the save file, the replay, the spectator feed, and what a client that
    /// dropped out is sent to catch up. Online play is then a matter of
    /// agreeing on a setup and appending to the same list at both ends.
    ///
    /// Every stroke carries the checksum of the position it produced. That is
    /// the point at which a disagreement becomes visible: not several turns
    /// later as an argument about whether a ball was hit, but on the stroke it
    /// happened, as two numbers that differ.
    /// </summary>
    public sealed class Match
    {
        readonly List<Stroke> strokes = new List<Stroke>();
        readonly List<ulong> after = new List<ulong>();

        public readonly MatchSetup Setup;
        public readonly Game Game;

        /// <summary>Every stroke played, in order.</summary>
        public IReadOnlyList<Stroke> Strokes => strokes;

        /// <summary>The checksum after each stroke, in the same order.</summary>
        public IReadOnlyList<ulong> Checkpoints => after;

        public int Count => strokes.Count;

        /// <summary>The position as it now stands.</summary>
        public ulong Hash => Checksum.Of(Game);

        Match(MatchSetup setup, Game game)
        {
            Setup = setup;
            Game = game;
        }

        /// <summary>Starts a match. The setup is copied, so the caller may go on using theirs.</summary>
        public static Match Begin(MatchSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));

            var mine = setup.Clone();
            mine.Settle();

            var balls = new Ball[mine.Balls];
            for (int i = 0; i < balls.Length; i++) balls[i] = new Ball(Vec2.Zero);

            // Sides are cut ACROSS the playing order rather than along it, the
            // way partners alternate on a lawn: with four balls and two sides
            // that is blue and black against red and yellow, as the laws set it
            // out. The same rule at both ends, from the same two numbers.
            int[] side = null;
            if (mine.Teams >= 2)
            {
                side = new int[mine.Balls];
                for (int i = 0; i < side.Length; i++) side[i] = i % mine.Teams;
            }

            var world = new World(balls, Field.For(mine.Variant), mine.Court);
            return new Match(mine, new Game(world, side, mine.Options));
        }

        /// <summary>
        /// Plays a stroke, records it, and hands back the frames.
        ///
        /// The one way a stroke enters a match. Nothing else appends to the
        /// list, so the list cannot fall out of step with the game it describes.
        /// </summary>
        public Replay Play(Stroke s)
        {
            if (!s.Fits(Game))
                throw new InvalidOperationException(
                    "that stroke does not fit the game as it stands: " + s);

            var shot = s.IsBonus
                ? Replay.PlayBonus(Game, s.Way, s.Placement, s.Aim, s.Power)
                : Replay.Play(Game, s.Aim, s.Power);

            strokes.Add(s);
            after.Add(Checksum.Of(Game));
            return shot;
        }

        /// <summary>
        /// Replays a match from its setup and its strokes -- what a client
        /// rejoining is handed, and what a saved game is loaded from.
        ///
        /// It plays them through the real rules rather than restoring
        /// positions, so a log that cannot legally have happened is refused
        /// here rather than becoming a game nobody can explain.
        /// </summary>
        /// <remarks>
        /// Named Rebuild rather than Replay so it cannot be read as -- or shadow
        /// -- the <see cref="Croquet.Core.Replay"/> type this class uses.
        /// </remarks>
        public static Match Rebuild(MatchSetup setup, IEnumerable<Stroke> strokes)
        {
            if (strokes == null) throw new ArgumentNullException(nameof(strokes));

            var m = Begin(setup);
            foreach (var s in strokes) m.Play(s);
            return m;
        }

        /// <summary>
        /// Whether this match agrees with a checksum somebody else computed for
        /// the same stroke number. False is a desync, and the stroke it is
        /// false on is where it started.
        /// </summary>
        public bool Agrees(int strokeNumber, ulong theirs) =>
            strokeNumber >= 0 && strokeNumber < after.Count && after[strokeNumber] == theirs;
    }
}
