using System;
using System.Linq;
using Croquet.Core;
using Xunit;
using Xunit.Abstractions;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// The bot. These are behaviour tests, not strength tests: they check that
    /// it takes the obvious shot when there is one, and that it beats a
    /// deliberately poor player over a whole game. Actual strength is judged by
    /// playing it.
    /// </summary>
    public class BotTests
    {
        readonly ITestOutputHelper output;
        public BotTests(ITestOutputHelper output) { this.output = output; }

        static Game NewGame(int balls = 4, params (double x, double y)[] at)
        {
            var arr = new Ball[balls];
            for (int i = 0; i < balls; i++) arr[i] = new Ball(Vec2.Zero);
            var g = new Game(new World(arr, Field.NineWicket(), new CourtSpec()),
                             null, new RuleOptions());
            for (int i = 0; i < balls; i++)
            {
                g.States[i].Started = true;
                g.World.Balls[i].InPlay = true;
                g.World.Balls[i].Pos = i < at.Length ? new Vec2(at[i].x, at[i].y)
                                                     : new Vec2(2.0 + i * 0.8, 14.0);
            }
            return g;
        }

        // ---- cloning, which the whole search rests on ----------------------

        [Fact]
        public void A_clone_starts_identical()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (9.0, 7.0) });
            g.States[0].Point = 3;
            g.States[0].Dead.Add(1);

            var c = g.Clone();

            Assert.Equal(g.Striker, c.Striker);
            Assert.Equal(g.ShotsLeft, c.ShotsLeft);
            for (int i = 0; i < g.World.Balls.Length; i++)
            {
                Assert.Equal(g.World.Balls[i].Pos, c.World.Balls[i].Pos);
                Assert.Equal(g.World.Balls[i].InPlay, c.World.Balls[i].InPlay);
                Assert.Equal(g.States[i].Point, c.States[i].Point);
            }
            Assert.Contains(1, c.States[0].Dead);
        }

        [Fact]
        public void Playing_a_clone_leaves_the_original_alone()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (9.0, 7.0) });
            var before = g.World.Balls.Select(b => b.Pos).ToArray();
            int striker = g.Striker;

            var c = g.Clone();
            c.Play(new Vec2(1, 0), 6.0);

            for (int i = 0; i < before.Length; i++)
                Assert.Equal(before[i], g.World.Balls[i].Pos);
            Assert.Equal(striker, g.Striker);
            Assert.NotEqual(before[0], c.World.Balls[0].Pos);
        }

        [Fact]
        public void A_clone_does_not_drag_a_ball_onto_the_starting_spot()
        {
            // The constructor brings the first ball on. A clone must not.
            var g = NewGame(at: new[] { (20.0, 5.0), (9.0, 7.0) });
            var c = g.Clone();
            Assert.Equal(new Vec2(20.0, 5.0), c.World.Balls[0].Pos);
        }

        // ---- does it take the obvious shot? -------------------------------

        [Fact]
        public void It_runs_a_hoop_that_is_there_to_be_run()
        {
            var g = NewGame();
            var f = g.World.Field;
            var h = f.Hoops[0];
            // Squarely in front of wicket 1, everything else out of the way.
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 1.2, h.Center.Y);
            for (int i = 1; i < 4; i++) g.World.Balls[i].Pos = new Vec2(2.0 + i * 0.6, 13.5);

            var r = new Bot().PlayStroke(g);

            Assert.Contains(0, r.PointsScored);
            output.WriteLine($"scored {string.Join(",", r.PointsScored.Select(p => f.Labels[p]))}");
        }

        [Fact]
        public void It_takes_a_roquet_that_is_on()
        {
            // Clear of the centre line and of the diamond hoops at y = 11.28,
            // so the lane between the two balls really is open -- and the other
            // two parked at the far end, where they are live roquets too and
            // would make "which ball did it hit" an unfair question.
            var g = NewGame(at: new[] { (5.0, 6.0), (10.0, 6.0), (26.0, 13.0), (28.0, 13.0) });

            var move = new Bot().Choose(g);
            var r = Bot.Apply(g, move);

            output.WriteLine($"chose: {move.Note} at {move.Power:0.00} m/s, score {move.Score:0}");
            Assert.Equal(1, r.Roqueted);
        }

        [Fact]
        public void It_does_not_hit_a_ball_it_is_dead_on()
        {
            // The only ball nearby is dead, so hitting it earns nothing and
            // ends the turn. A hoop is available instead.
            var g = NewGame();
            var f = g.World.Field;
            var h = f.Hoops[0];
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 1.2, h.Center.Y);
            g.World.Balls[1].Pos = new Vec2(h.Center.X - 0.6, h.Center.Y + 1.4);
            g.States[0].Dead.Add(1);
            for (int i = 2; i < 4; i++) g.World.Balls[i].Pos = new Vec2(2.0 + i, 13.5);

            var r = new Bot().PlayStroke(g);

            Assert.NotEqual(1, r.Roqueted);
            Assert.Contains(0, r.PointsScored);
        }

        [Fact]
        public void It_does_not_send_itself_off_the_lawn()
        {
            // Under the house rules that ends the turn, so a bot that values
            // its turn will not do it even from right on the edge.
            var g = NewGame();
            var c = g.World.Spec;
            g.World.Balls[0].Pos = new Vec2(c.Width - 0.8, c.Height / 2);
            for (int i = 1; i < 4; i++) g.World.Balls[i].Pos = new Vec2(3.0 + i, 2.0);

            var r = new Bot().PlayStroke(g);

            Assert.DoesNotContain(0, r.BroughtIn);
        }

        [Fact]
        public void It_finishes_when_the_peg_is_there_to_be_hit()
        {
            var g = NewGame();
            var f = g.World.Field;
            g.States[0].Point = Field.HomePegPoint;
            g.World.Balls[0].Pos = new Vec2(f.HomePeg.X + 1.5, f.HomePeg.Y);
            for (int i = 1; i < 4; i++) g.World.Balls[i].Pos = new Vec2(10.0 + i, 13.0);

            var r = new Bot().PlayStroke(g);

            Assert.True(r.PeggedOut, "it had a peg-out and did not take it");
        }

        // ---- the bonus stroke ---------------------------------------------

        [Fact]
        public void It_chooses_a_way_to_take_croquet_and_plays_on()
        {
            var g = NewGame(at: new[] { (5.0, 6.0), (10.0, 6.0) });
            for (int i = 2; i < 4; i++) g.World.Balls[i].Pos = new Vec2(2.0 + i, 2.0);

            var bot = new Bot();
            bot.PlayStroke(g);                       // the roquet
            Assert.Equal(StrokeKind.Bonus, g.Stroke);

            var move = bot.Choose(g);
            Assert.True(move.IsBonus);

            var r = Bot.Apply(g, move);
            Assert.False(r.TurnEnded, "a bonus stroke always leaves the continuation");
            output.WriteLine($"took croquet as {move.Way} ({move.Note})");
        }

        // ---- does it actually play? ---------------------------------------

        [Fact]
        public void A_whole_turn_terminates()
        {
            var g = NewGame();
            int played = Bot.Fast().PlayTurn(g, maxStrokes: 60);
            Assert.InRange(played, 1, 59);
        }

        [Fact]
        public void It_makes_progress_over_a_number_of_turns()
        {
            // Left to itself against nobody, it should get some way round.
            var g = NewGame();
            var bot = Bot.Fast();

            for (int t = 0; t < 12 && g.Winner == null; t++) bot.PlayTurn(g);

            int best = Enumerable.Range(0, 4).Max(i => g.States[i].Point);
            output.WriteLine("points after 12 turns: " +
                string.Join(", ", Enumerable.Range(0, 4).Select(i => g.States[i].Point)));
            Assert.True(best >= 3, $"the best ball had only reached point {best}");
        }

        [Fact]
        public void It_beats_a_player_who_swings_at_random()
        {
            // The bar is low on purpose: this asserts the bot is playing the
            // game rather than flailing, not that it is any good. Strength is
            // judged by playing it.
            var g = NewGame();
            var bot = Bot.Fast();
            var dice = new Random(1234);

            for (int t = 0; t < 24 && g.Winner == null; t++)
            {
                if (g.Striker % 2 == 0) bot.PlayTurn(g);
                else
                {
                    // A duffer: any direction, any strength, until the turn passes.
                    int me = g.Striker, n = 0;
                    while (g.Winner == null && g.Striker == me && n++ < 20)
                    {
                        double a = dice.NextDouble() * Math.PI * 2;
                        var aim = new Vec2(Math.Cos(a), Math.Sin(a));
                        double p = 1 + dice.NextDouble() * 6;
                        if (g.Stroke == StrokeKind.Bonus)
                            g.PlayBonus(BonusWay.CroquetShot, new Vec2(-1, 0), aim, p);
                        else g.Play(aim, p);
                    }
                }
            }

            int botPoints = g.States[0].Point + g.States[2].Point;
            int duffPoints = g.States[1].Point + g.States[3].Point;
            output.WriteLine($"bot {botPoints} - {duffPoints} duffer");
            Assert.True(botPoints > duffPoints,
                $"the bot scored {botPoints} against a random player's {duffPoints}");
        }

        [Fact]
        public void A_stroke_is_decided_quickly_enough_to_play_against()
        {
            var g = NewGame();
            var bot = new Bot();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bot.Choose(g);
            sw.Stop();

            output.WriteLine($"{bot.LastSearched} strokes searched in {sw.ElapsedMilliseconds} ms");
            Assert.True(sw.ElapsedMilliseconds < 4000,
                $"a stroke took {sw.ElapsedMilliseconds} ms to choose");
        }
    }
}
