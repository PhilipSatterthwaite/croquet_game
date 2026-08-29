using System;
using System.Linq;
using Croquet.Core;
using Xunit;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// The court, and running hoops on it.
    ///
    /// The layout tests are structural -- symmetry, monotonic progress, every
    /// point accounted for -- rather than assertions about particular
    /// coordinates, so the court can be re-measured without them all going red.
    /// </summary>
    public class FieldTests
    {
        static CourtSpec Lawn() => new CourtSpec();
        static Field F => Field.NineWicket();

        // ---- layout -------------------------------------------------------

        [Fact]
        public void There_are_nine_hoops_and_two_pegs()
        {
            Assert.Equal(9, F.Hoops.Length);
            Assert.NotEqual(F.HomePeg, F.TurningPeg);
        }

        [Fact]
        public void Every_point_of_the_course_is_a_hoop_or_a_peg()
        {
            var f = F;
            for (int p = 0; p < Field.TotalPoints; p++)
            {
                if (f.IsPeg(p)) Assert.Equal(0, f.DirectionFor(p));
                else
                {
                    Assert.InRange(f.HoopFor(p), 0, f.Hoops.Length - 1);
                    Assert.True(Math.Abs(f.DirectionFor(p)) == 1,
                        $"point {p} is a hoop and must be run in a direction");
                }
            }
        }

        [Fact]
        public void Exactly_two_points_are_pegs()
        {
            var f = F;
            var pegs = Enumerable.Range(0, Field.TotalPoints).Where(f.IsPeg).ToArray();
            Assert.Equal(new[] { Field.TurningPegPoint, Field.HomePegPoint }, pegs);
        }

        [Fact]
        public void Five_hoops_are_run_twice_and_four_once()
        {
            // The double diamond: outbound takes one side, homeward the other.
            // 5x2 + 4 = the fourteen wicket points.
            var f = F;
            var uses = new int[f.Hoops.Length];
            for (int p = 0; p < Field.TotalPoints; p++)
                if (!f.IsPeg(p)) uses[f.HoopFor(p)]++;

            Assert.Equal(5, uses.Count(u => u == 2));
            Assert.Equal(4, uses.Count(u => u == 1));
            Assert.Equal(14, uses.Sum());
        }

        [Fact]
        public void A_hoop_run_twice_is_run_once_each_way()
        {
            var f = F;
            for (int h = 0; h < f.Hoops.Length; h++)
            {
                var dirs = Enumerable.Range(0, Field.TotalPoints)
                                     .Where(p => !f.IsPeg(p) && f.HoopFor(p) == h)
                                     .Select(f.DirectionFor).ToArray();
                if (dirs.Length == 2)
                    Assert.Equal(0, dirs.Sum());   // +1 and -1
            }
        }

        [Fact]
        public void The_course_marches_up_the_court_and_back()
        {
            var f = F;
            // Outbound: home peg, hoops 1-7, turning peg -- never doubling back.
            double x = f.HomePeg.X;
            for (int p = 0; p <= Field.TurningPegPoint; p++)
            {
                double next = f.TargetFor(p).X;
                Assert.True(next >= x, $"point {p} steps back down the court");
                x = next;
            }
            // Homeward: hoops 8-14 and the home peg, never doubling back.
            for (int p = Field.TurningPegPoint + 1; p < Field.TotalPoints; p++)
            {
                double next = f.TargetFor(p).X;
                Assert.True(next <= x, $"point {p} steps back up the court");
                x = next;
            }
        }

        [Fact]
        public void The_layout_is_symmetric_end_to_end()
        {
            var f = F;
            var c = Lawn();
            double mirror(double x) => c.Width - x;

            Assert.Equal(mirror(f.HomePeg.X), f.TurningPeg.X, 6);
            Assert.Equal(f.HomePeg.Y, f.TurningPeg.Y, 6);

            // Hoop 1 mirrors hoop 7, hoop 2 mirrors hoop 6, and so on outward.
            Assert.Equal(mirror(f.Hoops[0].Center.X), f.Hoops[6].Center.X, 6);
            Assert.Equal(mirror(f.Hoops[1].Center.X), f.Hoops[5].Center.X, 6);
            Assert.Equal(mirror(f.Hoops[2].Center.X), f.Hoops[4].Center.X, 6);
        }

        [Fact]
        public void Everything_is_inside_the_lawn()
        {
            var f = F;
            var c = Lawn();
            foreach (var h in f.Hoops)
            {
                Assert.InRange(h.Center.X, 0, c.Width);
                Assert.InRange(h.LeftPost.Y, 0, c.Height);
                Assert.InRange(h.RightPost.Y, 0, c.Height);
            }
            Assert.InRange(f.HomePeg.X, 0, c.Width);
            Assert.InRange(f.TurningPeg.X, 0, c.Width);
        }

        [Fact]
        public void A_hoop_is_wider_than_a_ball()
        {
            var f = F;
            var c = Lawn();
            foreach (var h in f.Hoops)
                Assert.True(h.HalfGap * 2 > c.BallRadius * 2,
                    "a ball has to be able to fit through");
        }

        // ---- running hoops ------------------------------------------------

        static World Shot(Field f, CourtSpec c, params (double x, double y)[] at)
        {
            var balls = new Ball[at.Length];
            for (int i = 0; i < at.Length; i++) balls[i] = new Ball(new Vec2(at[i].x, at[i].y));
            var w = new World(balls, f, c);
            w.ClearShot();
            return w;
        }

        [Fact]
        public void Rolling_through_a_hoop_the_right_way_runs_it()
        {
            var f = F;
            var c = Lawn();
            var hoop = f.Hoops[0];                       // hoop 1, point 0

            var w = Shot(f, c, (hoop.Center.X - 1.0, hoop.Center.Y));
            w.Balls[0].Vel = new Vec2(1.6, 0);
            Sim.Settle(w);

            Assert.True(w.Balls[0].Pos.X > hoop.Center.X, "it should have gone through");
            Assert.True(w.RanPoint(0, 0), "point 1 should be scored");
        }

        [Fact]
        public void Rolling_through_the_wrong_way_does_not_run_it()
        {
            // Hoop 1 going backwards is not point 1. It IS point 14, which is
            // the same hoop from the other side -- and that is exactly the trap
            // the direction check exists to catch.
            var f = F;
            var c = Lawn();
            var hoop = f.Hoops[0];

            var w = Shot(f, c, (hoop.Center.X + 1.0, hoop.Center.Y));
            w.Balls[0].Vel = new Vec2(-1.6, 0);
            Sim.Settle(w);

            Assert.True(w.Balls[0].Pos.X < hoop.Center.X, "it should have gone through");
            Assert.False(w.RanPoint(0, 0), "that is not point 1");
            Assert.True(w.RanPoint(0, 14), "it is point 14, the same hoop homeward");
        }

        [Fact]
        public void Passing_wide_of_a_hoop_runs_nothing()
        {
            var f = F;
            var c = Lawn();
            var hoop = f.Hoops[0];

            var w = Shot(f, c, (hoop.Center.X - 1.0, hoop.Center.Y + 1.5));
            w.Balls[0].Vel = new Vec2(1.6, 0);
            Sim.Settle(w);

            Assert.True(w.Balls[0].Pos.X > hoop.Center.X, "it did cross the line of the hoop");
            Assert.False(w.RanPoint(0, 0), "but outside the uprights, so nothing is scored");
        }

        [Fact]
        public void Going_through_and_rolling_back_scores_nothing()
        {
            // Through the hoop, off the boundary is too far away, so instead:
            // through it and back again under its own steam is impossible on a
            // flat lawn, so drive it through and then send it back by hand --
            // the tally must cancel to zero.
            var f = F;
            var c = Lawn();
            var hoop = f.Hoops[0];

            var w = Shot(f, c, (hoop.Center.X - 0.6, hoop.Center.Y));
            w.Balls[0].Vel = new Vec2(1.2, 0);
            Sim.Settle(w);
            Assert.True(w.RanPoint(0, 0), "through once");

            w.Balls[0].Vel = new Vec2(-1.2, 0);
            Sim.Settle(w);

            Assert.False(w.RanPoint(0, 0), "back out again, so the point is not made");
            Assert.Equal(0, w.Passes[0, 0]);
        }

        [Fact]
        public void ClearShot_forgets_the_previous_shot()
        {
            var f = F;
            var c = Lawn();
            var hoop = f.Hoops[0];

            var w = Shot(f, c, (hoop.Center.X - 0.6, hoop.Center.Y));
            w.Balls[0].Vel = new Vec2(1.2, 0);
            Sim.Settle(w);
            Assert.True(w.RanPoint(0, 0));

            w.ClearShot();
            Assert.False(w.RanPoint(0, 0), "a new shot starts with nothing scored");
        }

        [Fact]
        public void A_ball_cannot_pass_through_an_upright()
        {
            // Aimed squarely at one post. It must come off, not through.
            var f = F;
            var c = Lawn();
            var hoop = f.Hoops[0];
            var post = hoop.LeftPost;

            var w = Shot(f, c, (post.X - 1.5, post.Y));
            w.Balls[0].Vel = new Vec2(3.0, 0);
            Sim.Settle(w);

            double reach = (w.Balls[0].Pos - post).Length;
            Assert.True(reach >= c.BallRadius + hoop.WireRadius - 1e-9,
                "the ball ended up inside the upright");
            Assert.False(w.RanPoint(0, 0), "hitting the wire is not running the hoop");
        }

        [Fact]
        public void A_hoop_takes_the_sting_out_of_a_ball()
        {
            // Same strike, once into open lawn and once square into an upright.
            // The rattle must cost it distance, or hoops are decoration.
            var f = F;
            var c = Lawn();
            var post = f.Hoops[0].LeftPost;

            var free = Shot(Field.NineWicket(), c, (post.X - 1.5, post.Y - 4.0));
            free.Balls[0].Vel = new Vec2(3.0, 0);
            Sim.Settle(free);
            double open = free.Balls[0].Pos.X - (post.X - 1.5);

            var hit = Shot(f, c, (post.X - 1.5, post.Y));
            hit.Balls[0].Vel = new Vec2(3.0, 0);
            Sim.Settle(hit);
            double rattled = hit.Balls[0].Pos.X - (post.X - 1.5);

            Assert.True(rattled < open,
                $"rattling the wire went {rattled:0.##} m, no less than the {open:0.##} m clear roll");
        }

        [Fact]
        public void A_peg_stops_a_ball_dead_on()
        {
            var f = F;
            var c = Lawn();

            var w = Shot(f, c, (f.TurningPeg.X - 1.5, f.TurningPeg.Y));
            w.Balls[0].Vel = new Vec2(3.0, 0);
            Sim.Settle(w);

            Assert.True(w.Balls[0].Pos.X < f.TurningPeg.X,
                "the ball should have come off the peg, not passed it");
            Assert.True((w.Balls[0].Pos - f.TurningPeg).Length >= c.BallRadius + f.PegRadius - 1e-9);
        }

        [Fact]
        public void The_whole_course_can_be_walked_hoop_by_hoop()
        {
            // Place the ball right in front of each point in turn and tap it
            // through. Proves every point in the course is reachable and that
            // the direction mapping is right for all fourteen.
            var f = F;
            var c = Lawn();

            for (int p = 0; p < Field.TotalPoints; p++)
            {
                if (f.IsPeg(p)) continue;

                var hoop = f.Hoops[f.HoopFor(p)];
                int dir = f.DirectionFor(p);

                var w = Shot(f, c, (hoop.Center.X - dir * 0.5, hoop.Center.Y));
                w.Balls[0].Vel = new Vec2(dir * 1.4, 0);
                Sim.Settle(w);

                Assert.True(w.RanPoint(0, p),
                    $"point {p} ({Course.Labels[p]}) was not scored by running its hoop");
            }
        }

        [Fact]
        public void Hoops_do_not_disturb_a_ball_that_misses_them_all()
        {
            // A clear lane down the middle of the lawn, well clear of the
            // centre-line hoops, must roll exactly as it would on bare grass.
            var f = F;
            var c = Lawn();

            var bare = new[] { new Ball(new Vec2(2, 2)) };
            bare[0].Vel = new Vec2(4, 0);
            Sim.Settle(bare, c);

            var w = Shot(f, c, (2, 2));
            w.Balls[0].Vel = new Vec2(4, 0);
            Sim.Settle(w);

            Assert.Equal(bare[0].Pos, w.Balls[0].Pos);
        }
    }
}
