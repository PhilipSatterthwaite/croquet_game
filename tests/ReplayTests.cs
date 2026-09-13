using System.Linq;
using Croquet.Core;
using Xunit;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// The seam between the rules and the picture.
    ///
    /// A front end plays the stroke for real and then animates a rebuild of it,
    /// which is only honest if the rebuild lands where the rules already put
    /// the balls. The first test here is the one that matters: it is exact, and
    /// it is exact on purpose. Everything else in this file is about the
    /// animation starting in the right place.
    /// </summary>
    public class ReplayTests
    {
        /// <summary>
        /// A court built here rather than taken from the defaults, so tuning
        /// feel never turns this suite red.
        /// </summary>
        static CourtSpec Lawn() => new CourtSpec
        {
            Width = 30,
            Height = 15,
            Friction = 0.6,
            Restitution = 0.8,
            ObstacleRestitution = 0.5
        };

        static Game NewGame(int balls, params (double x, double y)[] at)
        {
            var arr = new Ball[balls];
            for (int i = 0; i < balls; i++) arr[i] = new Ball(Vec2.Zero);
            var g = new Game(new World(arr, Field.NineWicket(), Lawn()));

            for (int i = 0; i < balls; i++)
            {
                g.States[i].Started = true;
                g.World.Balls[i].InPlay = true;
                g.World.Balls[i].Pos = i < at.Length ? new Vec2(at[i].x, at[i].y)
                                                     : new Vec2(2 + i * 0.9, 13.5);
            }
            return g;
        }

        [Fact]
        public void The_last_frame_is_where_the_ball_actually_finished()
        {
            // Exact equality, not a tolerance. The frames are stepped with the
            // same dt the rules settled the shot with, so they integrate the
            // identical sequence and land on the identical double. If this ever
            // needs a tolerance, the replay has stopped agreeing with the game
            // and the ball will visibly jump on the last frame -- fix the step,
            // do not loosen the assertion.
            var g = NewGame(4, (5, 7), (11, 7.6));

            var r = Replay.Play(g, new Vec2(1, 0.1), 5.5);

            Assert.True(r.FrameCount > 1, "the shot should have taken some frames");

            for (int i = 0; i < g.World.Balls.Length; i++)
            {
                // A ball brought back in from off the lawn is placed by the
                // rules after the rolling stopped, so the sim never saw it move
                // there. That is the one legitimate difference.
                if (r.Result.BroughtIn.Contains(i)) continue;
                Assert.Equal(g.World.Balls[i].Pos, r.LastFrame[i]);
            }
        }

        [Fact]
        public void The_first_frame_is_where_the_balls_stood()
        {
            var g = NewGame(4, (5, 7), (11, 7.6));
            var before = g.World.Balls.Select(b => b.Pos).ToArray();

            var r = Replay.Play(g, new Vec2(1, 0.1), 5.5);

            Assert.Equal(before, r.Frames[0]);
        }

        [Fact]
        public void A_bonus_stroke_starts_from_where_the_striker_is_set_down()
        {
            var g = NewGame(4, (5, 7), (8, 7));
            var roquet = Replay.Play(g, new Vec2(1, 0), 3.0);

            Assert.Equal(1, roquet.Result.Roqueted);
            Assert.Equal(StrokeKind.Bonus, g.Stroke);

            // Set down a mallet head away on the far side of the roqueted ball.
            var r = Replay.PlayBonus(g, BonusWay.MalletHead, new Vec2(1, 0),
                                     new Vec2(1, 0), 2.0);

            // Not where it came to rest after the roquet -- where the placement
            // put it, which is what the animation has to show it leaving from.
            var other = roquet.LastFrame[1];
            var gap = g.World.Spec.BallRadius * 2 + g.World.Spec.MalletHead;
            Assert.Equal(other.X + gap, r.Frames[0][0].X, 9);
            Assert.Equal(other.Y, r.Frames[0][0].Y, 9);
        }

        [Fact]
        public void A_foot_shot_follows_the_ball_that_was_sent()
        {
            var g = NewGame(4, (5, 7), (8, 7));
            Replay.Play(g, new Vec2(1, 0), 3.0);
            Assert.Equal(StrokeKind.Bonus, g.Stroke);

            var r = Replay.PlayBonus(g, BonusWay.FootShot, new Vec2(-1, 0),
                                     new Vec2(1, 0), 3.0);

            // The striker is held under a foot, so the camera must watch the
            // other ball or it watches a ball that never moves.
            Assert.Equal(1, r.Struck);
            Assert.Equal(0, r.Striker);

            var start = r.Frames[0][0];
            Assert.Equal(start, r.LastFrame[0]);
        }

        [Fact]
        public void A_croquet_stroke_lands_where_the_rules_put_it()
        {
            // The croquet stroke is the one shot where the rules add something a
            // plain collision would not -- the mallet's follow-through -- so it is
            // the one most able to leave the film and the game disagreeing. Exact,
            // for the same reason as the first test in this file.
            var g = NewGame(4, (5, 7), (8, 7));
            Replay.Play(g, new Vec2(1, 0), 3.0);
            Assert.Equal(StrokeKind.Bonus, g.Stroke);

            var r = Replay.PlayBonus(g, BonusWay.CroquetShot, new Vec2(-1, 0.3),
                                     new Vec2(1, 0.1), 3.5);

            for (int i = 0; i < g.World.Balls.Length; i++)
            {
                if (r.Result.BroughtIn.Contains(i)) continue;
                Assert.Equal(g.World.Balls[i].Pos, r.LastFrame[i]);
            }
        }

        [Fact]
        public void A_stroke_the_bot_chose_replays_the_same_way()
        {
            var g = NewGame(4, (5, 7), (11, 7.6));
            var move = Bot.Casual().Choose(g);

            var r = Replay.Play(g, move);

            Assert.Equal(move.Power, r.Power);
            for (int i = 0; i < g.World.Balls.Length; i++)
            {
                if (r.Result.BroughtIn.Contains(i)) continue;
                Assert.Equal(g.World.Balls[i].Pos, r.LastFrame[i]);
            }
        }

        [Theory]
        [InlineData(3.0)]
        [InlineData(12.0)]
        [InlineData(28.0)]
        public void A_stroke_rolls_the_distance_it_was_struck_for(double wanted)
        {
            // Both the player's pull and the bot's power sampling are DISTANCES,
            // converted to a speed with v^2 = 2ad. This holds that conversion
            // honest against the simulation actually integrating it -- if the
            // two ever part company, a shot aimed to reach a ball stops short of
            // it or sails past, and no amount of tuning fixes that.
            //
            // Feel-independent on purpose: it asserts that the ball goes as far
            // as it was asked to, whatever the friction happens to be tuned to.
            var court = Lawn();
            var g = NewGame(2, (2, 6), (28, 13.5));

            // A lane clear of every hoop and both pegs, so this measures rolling
            // and nothing else.
            double v = Math.Sqrt(2 * court.Friction * wanted);
            var r = Replay.Play(g, new Vec2(1, 0), v);

            double rolled = (r.LastFrame[0] - new Vec2(2, 6)).Length;
            Assert.InRange(rolled, wanted * 0.92, wanted * 1.03);

            // And it takes as long as the physics says it should: under a
            // constant deceleration a ball stops after v/a seconds. A shot can
            // cover exactly the right ground and still feel wrong if the film
            // runs at the wrong speed, so the frames are held to the clock as
            // well as to the distance.
            double seconds = v / court.Friction;
            Assert.InRange(r.Seconds, seconds * 0.92, seconds * 1.06);
        }

        [Fact]
        public void A_replay_remembers_the_point_the_stroke_was_for()
        {
            // The rules are applied the instant a stroke is played, so the Game
            // has already moved on to the next hoop before there is one frame of
            // the ball rolling towards this one. An interface reading the Game
            // live therefore announced the result of a shot still in the air --
            // the marker jumped to the next wicket as the ball left the mallet.
            //
            // So a Replay carries what the shot was ABOUT, and it stays true for
            // as long as the shot is on screen.
            var g = NewGame(2, (3.2, 7.62), (28, 13.5));
            var field = g.World.Field;

            Assert.Equal(0, g.States[0].Point);

            var r = Replay.Play(g, new Vec2(1, 0), 2.2);

            Assert.Contains(0, r.Result.PointsScored);           // it ran wicket 1
            Assert.True(g.States[0].Point > 0, "the game should have moved on");
            Assert.Equal(0, r.PointBefore);                      // the replay has not

            // Which is what the marker is placed from, so it stays on the hoop
            // the ball is actually rolling at.
            Assert.Equal(field.TargetFor(0), field.TargetFor(r.PointBefore));
            Assert.NotEqual(field.TargetFor(g.States[0].Point), field.TargetFor(r.PointBefore));
        }

        [Fact]
        public void The_same_stroke_replays_bit_for_bit()
        {
            // The determinism rule, at the level a front end sees it: two
            // clients handed the same stroke draw the same animation, frame for
            // frame, which is what lets online play send strokes rather than
            // positions.
            var a = Replay.Play(NewGame(4, (5, 7), (11, 7.6)), new Vec2(1, 0.1), 5.5);
            var b = Replay.Play(NewGame(4, (5, 7), (11, 7.6)), new Vec2(1, 0.1), 5.5);

            Assert.Equal(a.FrameCount, b.FrameCount);
            for (int f = 0; f < a.FrameCount; f++)
                Assert.Equal(a.Frames[f], b.Frames[f]);
        }
    }
}
