using System;
using Croquet.Core;
using Xunit;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// Physics tests.
    ///
    /// Every test builds the CourtSpec it needs rather than leaning on the
    /// defaults. Feel -- friction, restitution, court size -- is going to be
    /// tuned by hand over and over, and a suite that bakes today's numbers in
    /// would go red on every tuning pass and stop being useful exactly when it
    /// is needed most. So the assertions here are about behaviour that must
    /// hold for ANY feel: energy leaves the system, balls never overlap, a
    /// shot replays identically.
    /// </summary>
    public class SimTests
    {
        static CourtSpec Lawn(double friction = 0.6, double restitution = 0.8) =>
            new CourtSpec
            {
                Width = 30.48,
                Height = 15.24,
                BallRadius = 0.046,
                Friction = friction,
                Restitution = restitution,
                SleepSpeed = 0.02
            };

        /// <summary>No friction and no sleep, so collision maths is exact.</summary>
        static CourtSpec Ice(double restitution = 0.8) =>
            new CourtSpec
            {
                Width = 1000,
                Height = 1000,
                BallRadius = 0.046,
                Friction = 0,
                Restitution = restitution,
                SleepSpeed = 0
            };

        static Ball[] Balls(params (double x, double y)[] at)
        {
            var b = new Ball[at.Length];
            for (int i = 0; i < at.Length; i++) b[i] = new Ball(new Vec2(at[i].x, at[i].y));
            return b;
        }

        // ---- rolling ------------------------------------------------------

        [Fact]
        public void A_struck_ball_rolls_and_stops()
        {
            var c = Lawn();
            var b = Balls((5, 5));
            b[0].Vel = new Vec2(4, 0);

            int steps = Sim.Settle(b, c);

            Assert.True(steps > 0, "the shot should take some time");
            Assert.False(b[0].Moving);
            Assert.True(b[0].Pos.X > 5, "it should have gone forwards");
            Assert.Equal(5, b[0].Pos.Y, 9);          // no sideways drift
        }

        [Fact]
        public void Stopping_distance_follows_the_friction_it_was_given()
        {
            // v^2 / 2a is the analytic roll-out. Deriving the expectation from
            // the spec rather than hard-coding a distance is what lets friction
            // be retuned without touching this test.
            //
            // On an oversized lawn: a 5 m/s strike on keen grass rolls over 40
            // metres, further than a real court is long, and a boundary stop
            // would be measuring the court rather than the friction.
            foreach (double friction in new[] { 0.3, 0.6, 1.2 })
            {
                var c = Lawn(friction);
                c.Width = 500;
                c.Height = 500;

                var b = Balls((1, 5));
                double v0 = 5;
                b[0].Vel = new Vec2(v0, 0);

                Sim.Settle(b, c);

                Assert.False(b[0].WentOut, "the roll-out must not be cut short by a boundary");

                double expected = v0 * v0 / (2 * friction);
                double actual = b[0].Pos.X - 1;
                Assert.True(Math.Abs(actual - expected) / expected < 0.02,
                    $"friction {friction}: rolled {actual:0.###} m, expected about {expected:0.###} m");
            }
        }

        [Fact]
        public void A_harder_strike_goes_further()
        {
            var c = Lawn();
            double last = 0;
            foreach (double v in new[] { 1.0, 2.0, 4.0, 8.0 })
            {
                var b = Balls((0.5, 5));
                b[0].Vel = new Vec2(v, 0);
                Sim.Settle(b, c);
                double d = b[0].Pos.X - 0.5;
                Assert.True(d > last, $"{v} m/s went {d:0.##} m, no further than the shot before it");
                last = d;
            }
        }

        [Fact]
        public void A_ball_at_rest_stays_at_rest()
        {
            var c = Lawn();
            var b = Balls((5, 5), (20, 9));
            Assert.False(Sim.Step(b, c, 1.0 / 120));
            Assert.Equal(new Vec2(5, 5), b[0].Pos);
            Assert.Equal(new Vec2(20, 9), b[1].Pos);
        }

        // ---- determinism --------------------------------------------------

        [Fact]
        public void The_same_shot_replays_bit_for_bit()
        {
            // The whole networking plan rests on this: send the shot, not the
            // positions, and trust both devices to land in the same place.
            // Exact equality on purpose -- an approximate match here would mean
            // two clients slowly diverging over a game.
            var c = Lawn();

            Ball[] Run()
            {
                var b = Balls((3, 4), (9, 5.5), (9.4, 7), (15, 5));
                b[0].Vel = new Vec2(6.5, 1.25);
                Sim.Settle(b, c);
                return b;
            }

            var first = Run();
            var second = Run();

            for (int i = 0; i < first.Length; i++)
            {
                Assert.Equal(first[i].Pos, second[i].Pos);
                Assert.Equal(first[i].Vel, second[i].Vel);
            }
        }

        [Fact]
        public void Replay_survives_being_split_across_frames()
        {
            // A rendered shot is stepped a frame at a time; the AI settles it in
            // one call. Both must agree, or a previewed shot would not match the
            // shot that gets played.
            var c = Lawn();

            var a = Balls((3, 4), (9, 5.5));
            a[0].Vel = new Vec2(6.5, 1.25);
            Sim.Settle(a, c);

            var b = Balls((3, 4), (9, 5.5));
            b[0].Vel = new Vec2(6.5, 1.25);
            while (Sim.Step(b, c, 1.0 / 120)) { }

            Assert.Equal(a[0].Pos, b[0].Pos);
            Assert.Equal(a[1].Pos, b[1].Pos);
        }

        // ---- contact ------------------------------------------------------

        [Fact]
        public void A_roquet_sends_the_struck_ball_on()
        {
            var c = Lawn();
            var b = Balls((5, 5), (7, 5));
            b[0].Vel = new Vec2(5, 0);

            Sim.Settle(b, c);

            Assert.True(b[1].Pos.X > 7, "the struck ball should have been driven forwards");
            Assert.True(b[0].Pos.X > 5, "the striker should have carried on some way");
            Assert.True(b[0].Pos.X < b[1].Pos.X, "the striker should end up behind it");
        }

        [Fact]
        public void A_head_on_strike_splits_the_speed_by_restitution()
        {
            // Equal masses, head on: the striker keeps v(1-e)/2 and the struck
            // ball leaves at v(1+e)/2. Frictionless so the numbers are clean.
            const double v = 2.0, e = 0.8;
            var c = Ice(e);
            var b = Balls((0, 0), (0.5, 0));
            b[0].Vel = new Vec2(v, 0);

            for (int i = 0; i < 200 && b[1].Vel.LengthSq == 0; i++) Sim.Step(b, c, 1.0 / 240);

            Assert.True(b[1].Moving, "contact never happened");
            Assert.Equal(v * (1 - e) / 2, b[0].Vel.X, 6);
            Assert.Equal(v * (1 + e) / 2, b[1].Vel.X, 6);
            Assert.Equal(0, b[0].Vel.Y, 9);
            Assert.Equal(0, b[1].Vel.Y, 9);
        }

        [Fact]
        public void A_perfectly_elastic_head_on_strike_stops_the_striker()
        {
            // The classic Newton's-cradle case, and a good check that the
            // impulse is being split rather than doubled somewhere.
            var c = Ice(restitution: 1.0);
            var b = Balls((0, 0), (0.5, 0));
            b[0].Vel = new Vec2(3, 0);

            for (int i = 0; i < 200 && b[1].Vel.LengthSq == 0; i++) Sim.Step(b, c, 1.0 / 240);

            Assert.Equal(0, b[0].Vel.X, 6);
            Assert.Equal(3, b[1].Vel.X, 6);
        }

        [Fact]
        public void A_glancing_strike_sends_the_balls_either_side()
        {
            var c = Lawn();
            var b = Balls((5, 5), (7, 5.05));      // just off centre
            b[0].Vel = new Vec2(5, 0);

            Sim.Settle(b, c);

            Assert.True(b[1].Pos.Y > 5.05, "the struck ball should go one way");
            Assert.True(b[0].Pos.Y < 5, "and the striker the other");
        }

        [Fact]
        public void Contact_never_leaves_balls_overlapping()
        {
            var c = Lawn();
            var b = Balls((5, 5), (7, 5.02), (7.3, 5.4), (7.1, 4.6), (9, 5));
            b[0].Vel = new Vec2(9, 0.4);

            Sim.Settle(b, c);

            double min = c.BallRadius * 2;
            for (int i = 0; i < b.Length; i++)
                for (int j = i + 1; j < b.Length; j++)
                {
                    double d = (b[j].Pos - b[i].Pos).Length;
                    Assert.True(d >= min - 1e-9,
                        $"balls {i} and {j} ended {d:0.####} m apart, closer than {min:0.####}");
                }
        }

        [Theory]
        [InlineData(5.0)]
        [InlineData(15.0)]
        [InlineData(40.0)]
        public void A_fast_ball_cannot_pass_through_another(double speed)
        {
            // At 40 m/s and a 120 Hz frame a ball covers 33 cm in one step,
            // against a 9 cm target. Without substepping the strike is missed
            // entirely and the striker sails on untouched.
            var c = Lawn();
            var b = Balls((1, 5), (6, 5));
            b[0].Vel = new Vec2(speed, 0);

            Sim.Settle(b, c);

            Assert.True(b[1].Pos.X > 6.0 + 1e-6,
                $"at {speed} m/s the struck ball never moved -- the striker went through it");
        }

        // ---- boundary -----------------------------------------------------

        [Fact]
        public void A_ball_driven_off_the_lawn_stops_on_the_line()
        {
            var c = Lawn();
            var b = Balls((c.Width - 1, 5));
            b[0].Vel = new Vec2(12, 0);

            Sim.Settle(b, c);

            Assert.True(b[0].WentOut);
            Assert.Equal(c.Width, b[0].Pos.X, 9);
            Assert.False(b[0].Moving);
        }

        [Fact]
        public void A_ball_that_stays_in_is_not_flagged()
        {
            var c = Lawn();
            var b = Balls((5, 5));
            b[0].Vel = new Vec2(2, 0);

            Sim.Settle(b, c);

            Assert.False(b[0].WentOut);
        }

        [Fact]
        public void Every_edge_of_the_lawn_is_a_boundary()
        {
            var c = Lawn();
            var shots = new[]
            {
                (new Vec2(1, 5),  new Vec2(-12, 0)),
                (new Vec2(29, 5), new Vec2(12, 0)),
                (new Vec2(15, 1), new Vec2(0, -12)),
                (new Vec2(15, 14), new Vec2(0, 12))
            };

            foreach (var (pos, vel) in shots)
            {
                var b = Balls((pos.X, pos.Y));
                b[0].Vel = vel;
                Sim.Settle(b, c);
                Assert.True(b[0].WentOut, $"a ball hit {vel} from {pos} should have gone out");
                Assert.True(b[0].Pos.X >= 0 && b[0].Pos.X <= c.Width);
                Assert.True(b[0].Pos.Y >= 0 && b[0].Pos.Y <= c.Height);
            }
        }

        // ---- housekeeping -------------------------------------------------

        [Fact]
        public void A_pegged_out_ball_is_ignored()
        {
            var c = Lawn();
            var b = Balls((5, 5), (7, 5));
            b[1].InPlay = false;
            b[0].Vel = new Vec2(5, 0);

            Sim.Settle(b, c);

            Assert.Equal(new Vec2(7, 5), b[1].Pos);
            Assert.True(b[0].Pos.X > 7, "the striker should have rolled straight through where it was");
        }

        [Fact]
        public void Settling_always_terminates()
        {
            var c = Lawn();
            var b = Balls((5, 5), (5.2, 5.05), (5.1, 4.9), (15, 7), (20, 8), (25, 3));
            b[0].Vel = new Vec2(30, 12);

            int steps = Sim.Settle(b, c, maxSteps: 50000);

            Assert.True(steps < 50000, "the shot never came to rest");
            foreach (var ball in b) Assert.False(ball.Moving);
        }
    }
}
