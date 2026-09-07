using System;
using Croquet.Core;
using Xunit;
using Xunit.Abstractions;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// Whether the three levels are actually three levels.
    ///
    /// Difficulty here is a HAND, not a smaller search, so the thing to measure
    /// is how often a stroke of a known length arrives where it was sent. That
    /// is measured directly -- the stroke is handed to the bot already aimed,
    /// and only <see cref="Bot.Wobble"/> is applied -- because a full game
    /// mixes the hand with the search, and the search is not what separates the
    /// levels. Strokes-to-go-round measures hand and search together and is
    /// dominated by whichever is weaker, which is why it is not the test here.
    /// </summary>
    public class BotLadderTests
    {
        readonly ITestOutputHelper output;
        public BotLadderTests(ITestOutputHelper output) { this.output = output; }

        /// <summary>Built here, so tuning feel never turns this red.</summary>
        static CourtSpec Lawn() => new CourtSpec
        {
            Width = 30.48,
            Height = 15.24,
            Friction = 1.6,
            Restitution = 0.8,
            ObstacleRestitution = 0.5
        };

        static readonly (string Name, Func<int, Bot> Make)[] Levels =
        {
            ("beginner", Bot.Beginner), ("casual", Bot.Casual), ("expert", Bot.Expert)
        };

        /// <summary>
        /// How often this hand hits a ball <paramref name="range"/> metres away,
        /// struck straight at it and hard enough to arrive.
        /// </summary>
        static int HitsPerHundred(Func<int, Bot> make, double range, int tries = 200)
        {
            var spec = Lawn();
            int hit = 0;

            for (int t = 0; t < tries; t++)
            {
                var balls = new Ball[2];
                for (int i = 0; i < 2; i++) balls[i] = new Ball(Vec2.Zero);

                var g = new Game(new World(balls, Field.NineWicket(), spec),
                                 null, RuleOptions.Basic);
                for (int i = 0; i < 2; i++)
                {
                    g.States[i].Started = true;
                    g.World.Balls[i].InPlay = true;
                }

                // Clear of every hoop, along the middle of the court.
                g.World.Balls[0].Pos = new Vec2(4.4, 11.0);
                g.World.Balls[1].Pos = new Vec2(4.4 + range, 11.0);

                // A stroke that would just carry past the other ball: the power
                // a player picks for a roquet, not a heave.
                var move = new BotMove
                {
                    Aim = new Vec2(1, 0),
                    Power = Math.Sqrt(2 * spec.Friction * (range + 1.0))
                };

                var bot = make(9000 + t * 37);
                bot.Wobble(move, spec);

                if (Bot.Apply(g, move).Roqueted >= 0) hit++;
            }

            return hit * 100 / tries;
        }

        [Fact]
        public void Each_level_is_a_worse_hand_than_the_one_above_it()
        {
            // Short enough that nobody misses, out to long enough that everybody
            // does. The levels have to separate SOMEWHERE in between, and it is
            // the middle of this range that a game is actually played over.
            foreach (double range in new[] { 1.0, 3.0, 6.0, 10.0 })
            {
                var made = new int[Levels.Length];

                for (int i = 0; i < Levels.Length; i++)
                {
                    made[i] = HitsPerHundred(Levels[i].Make, range);
                    output.WriteLine($"{Levels[i].Name,-9} at {range,4:N0}m: {made[i]}%");
                }

                for (int i = 1; i < made.Length; i++)
                    Assert.True(made[i] >= made[i - 1],
                        $"at {range:N0}m {Levels[i].Name} ({made[i]}%) is no better than " +
                        $"{Levels[i - 1].Name} ({made[i - 1]}%)");
            }
        }

        [Fact]
        public void The_levels_are_far_enough_apart_to_be_told_apart()
        {
            // Six metres is an ordinary roquet on this court -- the length at
            // which a player's standard shows. Two levels that agree here are
            // one level with two names, which is what casual and steady were.
            const double range = 6.0;

            int beginner = HitsPerHundred(Bot.Beginner, range);
            int casual = HitsPerHundred(Bot.Casual, range);
            int expert = HitsPerHundred(Bot.Expert, range);

            output.WriteLine($"at {range:N0}m -- beginner {beginner}%, " +
                             $"casual {casual}%, expert {expert}%");

            Assert.True(casual - beginner >= 15,
                $"beginner {beginner}% and casual {casual}% play the same shot");
            Assert.True(expert - casual >= 15,
                $"casual {casual}% and expert {expert}% play the same shot");
        }

        [Fact]
        public void Even_the_best_hand_misses_across_the_court()
        {
            // An opponent that never misses is not an opponent. Fifteen metres
            // is half the long axis: a shot a good player takes and loses.
            int expert = HitsPerHundred(Bot.Expert, 15.0);
            output.WriteLine($"expert at 15m: {expert}%");

            Assert.True(expert <= 60, $"expert hits {expert}% across the court");
        }
    }
}
