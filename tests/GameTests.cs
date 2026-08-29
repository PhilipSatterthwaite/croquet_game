using System.Linq;
using Croquet.Core;
using Xunit;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// The turn structure. These are rules tests, so they place balls exactly
    /// where a situation needs them and tap them the short distance required --
    /// the physics is proven elsewhere and is only the delivery mechanism here.
    /// </summary>
    public class GameTests
    {
        static Game NewGame(int balls = 4, int[] side = null, params (double x, double y)[] at)
        {
            var f = Field.NineWicket();
            var c = new CourtSpec();
            var arr = new Ball[balls];
            for (int i = 0; i < balls; i++)
                arr[i] = new Ball(i < at.Length ? new Vec2(at[i].x, at[i].y)
                                                : new Vec2(1 + i * 0.9, 1.0));
            return new Game(new World(arr, f, c), side);
        }

        /// <summary>Places a ball a short way in front of the hoop for a point.</summary>
        static Vec2 InFrontOf(Field f, int point, double back = 0.45)
        {
            var h = f.Hoops[f.HoopFor(point)];
            return new Vec2(h.Center.X - f.DirectionFor(point) * back, h.Center.Y);
        }

        // ---- scoring ------------------------------------------------------

        [Fact]
        public void Running_your_wicket_scores_it_and_earns_another_stroke()
        {
            var g = NewGame();
            var f = g.World.Field;
            g.World.Balls[0].Pos = InFrontOf(f, 0);

            var r = g.Play(new Vec2(1, 0), 1.3);

            Assert.Equal(new[] { 0 }, r.PointsScored);
            Assert.Equal(1, g.States[0].Point);
            Assert.False(r.TurnEnded);
            Assert.Equal(0, g.Striker);
        }

        [Fact]
        public void Missing_your_wicket_ends_the_turn()
        {
            var g = NewGame();
            var f = g.World.Field;
            // Well off to one side, so it passes the hoop's line outside the uprights.
            var h = f.Hoops[0];
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.45, h.Center.Y + 1.5);

            var r = g.Play(new Vec2(1, 0), 1.3);

            Assert.Empty(r.PointsScored);
            Assert.True(r.TurnEnded);
            Assert.Equal(1, g.Striker);
        }

        [Fact]
        public void Wickets_must_be_run_in_order()
        {
            // Send a fresh ball through wicket 2. It is playing for wicket 1,
            // so nothing is scored.
            var g = NewGame();
            var f = g.World.Field;
            g.World.Balls[0].Pos = InFrontOf(f, 1);

            var r = g.Play(new Vec2(1, 0), 1.3);

            Assert.Empty(r.PointsScored);
            Assert.Equal(0, g.States[0].Point);
            Assert.True(r.TurnEnded);
        }

        [Fact]
        public void The_turning_peg_is_a_point_and_earns_a_stroke()
        {
            var g = NewGame();
            var f = g.World.Field;
            g.States[0].Point = Field.TurningPegPoint;
            g.World.Balls[0].Pos = new Vec2(f.TurningPeg.X - 1.0, f.TurningPeg.Y);

            var r = g.Play(new Vec2(1, 0), 2.0);

            Assert.Equal(new[] { Field.TurningPegPoint }, r.PointsScored);
            Assert.Equal(Field.TurningPegPoint + 1, g.States[0].Point);
            Assert.False(r.TurnEnded);
        }

        [Fact]
        public void Hitting_the_home_peg_at_the_end_pegs_the_ball_out()
        {
            var g = NewGame();
            var f = g.World.Field;
            g.States[0].Point = Field.HomePegPoint;
            g.World.Balls[0].Pos = new Vec2(f.HomePeg.X + 1.0, f.HomePeg.Y);

            var r = g.Play(new Vec2(-1, 0), 2.0);

            Assert.True(r.PeggedOut);
            Assert.True(g.States[0].Finished);
            Assert.False(g.World.Balls[0].InPlay);
            Assert.True(r.TurnEnded);
        }

        [Fact]
        public void A_pegged_out_ball_is_skipped_in_the_rotation()
        {
            var g = NewGame();
            var f = g.World.Field;
            g.States[1].Point = Field.HomePegPoint;
            g.World.Balls[1].InPlay = false;      // already gone

            var h = f.Hoops[0];
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.45, h.Center.Y + 1.5);
            g.Play(new Vec2(1, 0), 1.3);          // a miss, so the turn passes

            Assert.Equal(2, g.Striker);
        }

        // ---- roquet and croquet -------------------------------------------

        [Fact]
        public void Hitting_a_ball_is_a_roquet_and_owes_a_croquet_stroke()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0) });

            var r = g.Play(new Vec2(1, 0), 3.0);

            Assert.Equal(1, r.Roqueted);
            Assert.False(r.TurnEnded);
            Assert.Equal(StrokeKind.Croquet, g.Stroke);
            Assert.Equal(1, g.CroquetFrom);
        }

        [Fact]
        public void A_roquet_makes_you_dead_on_that_ball()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0) });
            g.Play(new Vec2(1, 0), 3.0);

            Assert.False(g.IsAlive(1));
            Assert.True(g.IsAlive(2));
        }

        [Fact]
        public void Hitting_a_ball_you_are_dead_on_earns_nothing()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0) });
            g.States[0].Dead.Add(1);

            var r = g.Play(new Vec2(1, 0), 3.0);

            Assert.Equal(-1, r.Roqueted);
            Assert.True(r.TurnEnded);
            Assert.Equal(1, g.Striker);
        }

        [Fact]
        public void Running_a_wicket_brings_you_alive_again()
        {
            var g = NewGame();
            var f = g.World.Field;
            g.States[0].Dead.Add(1);
            g.States[0].Dead.Add(2);
            g.World.Balls[0].Pos = InFrontOf(f, 0);
            // The other balls out of the way, so this stroke is only the wicket.
            g.World.Balls[1].Pos = new Vec2(2, 13);
            g.World.Balls[2].Pos = new Vec2(3, 13);
            g.World.Balls[3].Pos = new Vec2(4, 13);

            g.Play(new Vec2(1, 0), 1.3);

            Assert.True(g.IsAlive(1));
            Assert.True(g.IsAlive(2));
            Assert.Empty(g.States[0].Dead);
        }

        [Fact]
        public void A_wicket_run_before_the_contact_revives_a_dead_ball()
        {
            // The ordering case. The striker is dead on ball 1, but ball 1 is
            // sitting just beyond the striker's own wicket. Running the wicket
            // clears the deadness, and the ball beyond it is then a live
            // roquet -- which is only decidable from the order of events.
            var g = NewGame();
            var f = g.World.Field;
            var h = f.Hoops[0];

            g.States[0].Dead.Add(1);
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.45, h.Center.Y);
            g.World.Balls[1].Pos = new Vec2(h.Center.X + 0.9, h.Center.Y);
            g.World.Balls[2].Pos = new Vec2(2, 13);
            g.World.Balls[3].Pos = new Vec2(3, 13);

            var r = g.Play(new Vec2(1, 0), 2.2);

            Assert.Equal(new[] { 0 }, r.PointsScored);
            Assert.Equal(1, r.Roqueted);
            Assert.False(r.TurnEnded);
            Assert.False(g.IsAlive(1));      // dead on it again, from the new roquet
        }

        [Fact]
        public void A_croquet_stroke_starts_touching_the_roqueted_ball()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0) });
            g.Play(new Vec2(1, 0), 3.0);
            Assert.Equal(StrokeKind.Croquet, g.Stroke);

            g.TakeCroquet(new Vec2(-1, 0));

            double gap = (g.World.Balls[0].Pos - g.World.Balls[1].Pos).Length;
            Assert.Equal(g.World.Spec.BallRadius * 2, gap, 9);
        }

        [Fact]
        public void After_the_croquet_stroke_a_continuation_is_owed()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0), (2.0, 13.0), (3.0, 13.0) });
            g.Play(new Vec2(1, 0), 3.0);
            g.TakeCroquet(new Vec2(-1, 0));

            var r = g.Play(new Vec2(1, 0), 2.0);

            Assert.False(r.TurnEnded);
            Assert.Equal(StrokeKind.Ordinary, g.Stroke);
            Assert.Equal(0, g.Striker);
        }

        [Fact]
        public void A_second_roquet_on_the_same_ball_in_one_turn_ends_it()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0), (2.0, 13.0), (3.0, 13.0) });
            g.Play(new Vec2(1, 0), 3.0);          // roquet
            g.TakeCroquet(new Vec2(-1, 0));
            g.Play(new Vec2(1, 0), 0.8);          // croquet stroke, ball 1 pushed on
            var r = g.Play(new Vec2(1, 0), 3.0);  // continuation, hits it again

            Assert.Equal(-1, r.Roqueted);
            Assert.True(r.TurnEnded);
        }

        // ---- ending the turn ----------------------------------------------

        [Fact]
        public void Going_out_of_bounds_ends_the_turn_even_if_you_scored()
        {
            // Runs its wicket and carries on off the end of the lawn. The point
            // stands; the continuation does not.
            //
            // Wicket 3 rather than wicket 1, because the centre-line hoops lead
            // straight into the turning peg — a ball driven hard down that lane
            // rebounds off the peg and never reaches the boundary at all.
            var g = NewGame();
            var f = g.World.Field;
            g.States[0].Point = 2;
            g.World.Balls[0].Pos = InFrontOf(f, 2);
            for (int i = 1; i < 4; i++) g.World.Balls[i].Pos = new Vec2(2 + i, 14.5);

            var r = g.Play(new Vec2(1, 0), 7.0);

            Assert.Contains(2, r.PointsScored);
            Assert.True(r.WentOut);
            Assert.True(r.TurnEnded);
            Assert.Equal(1, g.Striker);
        }

        [Fact]
        public void A_stroke_that_earns_nothing_passes_the_turn_on()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (20.0, 2.0), (21.0, 2.0), (22.0, 2.0) });

            var r = g.Play(new Vec2(0, 1), 1.0);

            Assert.True(r.TurnEnded);
            Assert.Equal(1, g.Striker);
            Assert.Empty(r.PointsScored);
            Assert.Equal(-1, r.Roqueted);
        }

        [Fact]
        public void The_turn_wraps_round_the_ball_order()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (8.0, 2.0), (11.0, 2.0), (14.0, 2.0) });
            for (int expected = 1; expected <= 4; expected++)
            {
                g.Play(new Vec2(0, 1), 0.6);      // a nothing stroke every time
                Assert.Equal(expected % 4, g.Striker);
            }
        }

        // ---- winning ------------------------------------------------------

        [Fact]
        public void A_side_wins_when_both_its_balls_are_round()
        {
            var side = new[] { 0, 1, 0, 1 };      // blue+black against red+yellow
            var g = NewGame(4, side);
            var f = g.World.Field;

            g.States[2].Point = Field.HomePegPoint + 1;   // black already round
            g.World.Balls[2].InPlay = false;

            g.States[0].Point = Field.HomePegPoint;
            g.World.Balls[0].Pos = new Vec2(f.HomePeg.X + 1.0, f.HomePeg.Y);

            var r = g.Play(new Vec2(-1, 0), 2.0);

            Assert.True(r.PeggedOut);
            Assert.NotNull(g.Winner);
            Assert.Equal(new[] { 0, 2 }, g.Winner);
        }

        [Fact]
        public void One_ball_round_is_not_a_win_for_the_side()
        {
            var side = new[] { 0, 1, 0, 1 };
            var g = NewGame(4, side);
            var f = g.World.Field;

            g.States[0].Point = Field.HomePegPoint;
            g.World.Balls[0].Pos = new Vec2(f.HomePeg.X + 1.0, f.HomePeg.Y);

            g.Play(new Vec2(-1, 0), 2.0);

            Assert.Null(g.Winner);
        }

        [Fact]
        public void With_no_sides_the_first_ball_round_wins()
        {
            var g = NewGame(4);
            var f = g.World.Field;
            g.States[0].Point = Field.HomePegPoint;
            g.World.Balls[0].Pos = new Vec2(f.HomePeg.X + 1.0, f.HomePeg.Y);

            g.Play(new Vec2(-1, 0), 2.0);

            Assert.Equal(new[] { 0 }, g.Winner);
        }

        // ---- the whole course ---------------------------------------------

        [Fact]
        public void A_ball_can_be_walked_all_the_way_round()
        {
            // Sixteen points, placed in front of each in turn. Proves the
            // course, the direction mapping and the scoring agree end to end.
            var g = NewGame(1);
            var f = g.World.Field;

            for (int p = 0; p < Field.TotalPoints; p++)
            {
                Assert.Equal(p, g.States[0].Point);

                if (f.IsPeg(p))
                {
                    var peg = f.PegFor(p);
                    double from = p == Field.TurningPegPoint ? -1.0 : 1.0;
                    g.World.Balls[0].Pos = new Vec2(peg.X + from, peg.Y);
                    g.Play(new Vec2(-from, 0), 2.0);
                }
                else
                {
                    g.World.Balls[0].Pos = InFrontOf(f, p);
                    g.Play(new Vec2(f.DirectionFor(p), 0), 1.3);
                }
            }

            Assert.True(g.States[0].Finished);
            Assert.Equal(new[] { 0 }, g.Winner);
        }
    }
}
