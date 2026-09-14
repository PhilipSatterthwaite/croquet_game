using System;
using System.Collections.Generic;

namespace Croquet.Core
{
    /// <summary>
    /// A number that stands for the whole state of a game.
    ///
    /// Two clients playing the same strokes should arrive at the same position.
    /// "Should" is the problem: if they ever do not, the game does not stop
    /// working -- it goes on happily with the two ends looking at different
    /// lawns, and the disagreement surfaces several turns later as an argument
    /// about whether a ball was hit. This turns that into something a single
    /// comparison catches on the stroke it happened.
    ///
    /// It is bit-exact on purpose. Positions go in as their raw IEEE bits
    /// rather than rounded, because a divergence begins in the last bit and
    /// grows; catching it while it is still invisible is the entire point, and
    /// a tolerance here would hide exactly the case worth knowing about.
    ///
    /// FNV-1a: not cryptographic, and does not need to be. Nothing here defends
    /// against a forged checksum -- a server that cares recomputes the state
    /// itself. This is for honest disagreement.
    /// </summary>
    public static class Checksum
    {
        const ulong Offset = 14695981039346656037;
        const ulong Prime = 1099511628211;

        /// <summary>The state of a game in progress, as one number.</summary>
        public static ulong Of(Game game)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));

            ulong h = Offset;

            h = Mix(h, (ulong)game.Striker);
            h = Mix(h, (ulong)game.Stroke);
            h = Mix(h, unchecked((ulong)(long)game.RoquetedBall));
            h = Mix(h, unchecked((ulong)(long)game.ShotsLeft));

            // Whether anybody has won, and who. Null and "nobody yet" have to
            // read differently from a win by ball zero.
            h = Mix(h, game.Winner == null ? 0UL : 1UL);
            if (game.Winner != null)
                foreach (var w in game.Winner) h = Mix(h, (ulong)w);

            // Option 11's protection and the turn it has cost. Mixed only while
            // either is set, so a game that never uses the option hashes exactly
            // as it did before the option existed -- Reference.Hash included.
            if (game.Wicketed >= 0 || game.LosesTurn >= 0)
            {
                h = Mix(h, 0xB0B0UL);
                h = Mix(h, unchecked((ulong)(long)game.Wicketed));
                h = Mix(h, unchecked((ulong)(long)game.LosesTurn));
            }

            var world = game.World;
            for (int i = 0; i < world.Balls.Length; i++)
            {
                var b = world.Balls[i];
                h = Mix(h, Bits(b.Pos.X));
                h = Mix(h, Bits(b.Pos.Y));
                h = Mix(h, b.InPlay ? 1UL : 0UL);

                // Which side of each hoop it is on. Not derivable from the
                // position -- a ball in the jaws is on neither, and which side
                // it came in from decides whether the next stroke can score it.
                for (int j = 0; j < world.Field.Hoops.Length; j++)
                    h = Mix(h, unchecked((ulong)(long)world.Side[i, j]));

                var s = game.States[i];
                h = Mix(h, unchecked((ulong)(long)s.Point));
                h = Mix(h, s.Started ? 1UL : 0UL);

                // Sorted, because a HashSet has no order and two runs that
                // agree about the game could otherwise disagree about the
                // number -- which would be a false alarm, and a false alarm
                // here is worse than no alarm at all.
                h = Mix(h, (ulong)s.Dead.Count);
                var dead = new List<int>(s.Dead);
                dead.Sort();
                foreach (var d in dead) h = Mix(h, (ulong)d);
            }

            return h;
        }

        /// <summary>
        /// The court a game is played on. Part of what the two ends have to
        /// agree about before a single stroke is played: the same stroke on a
        /// keener lawn lands somewhere else.
        /// </summary>
        public static ulong Of(CourtSpec c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));

            ulong h = Offset;
            h = Mix(h, Bits(c.Width));
            h = Mix(h, Bits(c.Height));
            h = Mix(h, Bits(c.BallRadius));
            h = Mix(h, Bits(c.Friction));
            h = Mix(h, Bits(c.Restitution));
            h = Mix(h, Bits(c.ObstacleRestitution));
            h = Mix(h, Bits(c.SleepSpeed));
            h = Mix(h, Bits(c.BoundaryReturn));
            h = Mix(h, Bits(c.MalletHead));
            h = Mix(h, Bits(c.MalletLength));

            // It moves where a croquet stroke's back ball stops, so two ends
            // that disagree about it are playing on different lawns.
            h = Mix(h, Bits(c.CroquetFollow));
            return h;
        }

        /// <summary>A stroke, so a log of them can be compared without walking it.</summary>
        public static ulong Of(Stroke s)
        {
            ulong h = Offset;
            h = Mix(h, s.IsBonus ? 1UL : 0UL);
            h = Mix(h, (ulong)s.Way);
            h = Mix(h, Bits(s.Placement.X));
            h = Mix(h, Bits(s.Placement.Y));
            h = Mix(h, Bits(s.Aim.X));
            h = Mix(h, Bits(s.Aim.Y));
            h = Mix(h, Bits(s.Power));
            return h;
        }

        /// <summary>Folds one value into a running hash.</summary>
        public static ulong Mix(ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xFF;
                hash *= Prime;
            }
            return hash;
        }

        /// <summary>
        /// A double as its raw bits. Not rounded: see the note above about
        /// catching a divergence in the last bit rather than after it has grown
        /// into something visible.
        /// </summary>
        static ulong Bits(double d) => unchecked((ulong)BitConverter.DoubleToInt64Bits(d));
    }
}
