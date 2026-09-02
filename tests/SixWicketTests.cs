using System;
using System.Linq;
using Croquet.Core;
using Xunit;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// Association croquet: the WCF Laws, 7th edition (the PDF in the repo
    /// root). Six hoops carrying twelve points plus the peg.
    ///
    /// The court is laid out rotated a quarter turn from Diagram 1 so its long
    /// axis is x, matching the other variant and the whole engine. The tests
    /// are written in those rotated terms.
    /// </summary>
    public class SixWicketTests
    {
        static Field F => Field.For(Variant.SixWicket);
        static CourtSpec Court => Field.CourtFor(Variant.SixWicket);

        static Game NewGame(params (double x, double y)[] at)
        {
            var arr = new Ball[4];
            for (int i = 0; i < 4; i++) arr[i] = new Ball(Vec2.Zero);
            var g = new Game(new World(arr, F, Court), new[] { 0, 1, 0, 1 }, RuleOptions.Basic);
            for (int i = 0; i < 4; i++)
            {
                g.States[i].Started = true;
                g.World.Balls[i].InPlay = true;
                g.World.Balls[i].Pos = i < at.Length ? new Vec2(at[i].x, at[i].y)
                                                     : new Vec2(2.0 + i * 0.8, 24.0);
            }
            return g;
        }

        static Vec2 InFrontOf(Field f, int point, double back = 0.45)
        {
            var h = f.Hoops[f.HoopFor(point)];
            return new Vec2(h.Center.X - f.DirectionFor(point) * back, h.Center.Y);
        }

        // ---- the court ----------------------------------------------------

        [Fact]
        public void Six_hoops_and_one_peg()
        {
            Assert.Equal(6, F.Hoops.Length);
            Assert.Single(F.Pegs);
        }

        [Fact]
        public void Thirteen_points_twelve_hoops_and_the_peg()
        {
            var f = F;
            Assert.Equal(13, f.TotalPoints);
            Assert.Equal(12, Enumerable.Range(0, 13).Count(p => !f.IsPeg(p)));
            Assert.Equal(new[] { 12 }, Enumerable.Range(0, 13).Where(f.IsPeg).ToArray());
        }

        [Fact]
        public void The_court_is_28_by_35_yards()
        {
            var c = Court;
            Assert.Equal(35 * Field.Yard, c.Width, 6);
            Assert.Equal(28 * Field.Yard, c.Height, 6);
        }

        [Fact]
        public void Every_hoop_is_run_exactly_twice_once_each_way()
        {
            var f = F;
            for (int h = 0; h < f.Hoops.Length; h++)
            {
                var dirs = Enumerable.Range(0, f.TotalPoints)
                                     .Where(p => !f.IsPeg(p) && f.HoopFor(p) == h)
                                     .Select(f.DirectionFor).ToArray();
                Assert.Equal(2, dirs.Length);
                Assert.Equal(0, dirs.Sum());       // one each way
            }
        }

        [Fact]
        public void The_outer_hoops_are_seven_yards_from_their_boundaries()
        {
            // Law 4.4.2. In rotated terms the four outer hoops sit 7 yards from
            // the near end and 7 from the near side.
            var f = F;
            var c = Court;
            foreach (int h in new[] { 0, 1, 2, 3 })
            {
                double fromEnd = Math.Min(f.Hoops[h].Center.X, c.Width - f.Hoops[h].Center.X);
                double fromSide = Math.Min(f.Hoops[h].Center.Y, c.Height - f.Hoops[h].Center.Y);
                Assert.Equal(7 * Field.Yard, fromEnd, 6);
                Assert.Equal(7 * Field.Yard, fromSide, 6);
            }
        }

        [Fact]
        public void The_inner_hoops_are_seven_yards_either_side_of_the_peg()
        {
            var f = F;
            var peg = f.Pegs[0];
            foreach (int h in new[] { 4, 5 })
            {
                Assert.Equal(7 * Field.Yard, Math.Abs(f.Hoops[h].Center.X - peg.X), 6);
                Assert.Equal(peg.Y, f.Hoops[h].Center.Y, 6);
            }
        }

        [Fact]
        public void The_peg_is_in_the_centre()
        {
            var c = Court;
            Assert.Equal(c.Width / 2, F.Pegs[0].X, 6);
            Assert.Equal(c.Height / 2, F.Pegs[0].Y, 6);
        }

        [Fact]
        public void Everything_is_inside_the_court()
        {
            var f = F;
            var c = Court;
            foreach (var h in f.Hoops)
            {
                Assert.InRange(h.Center.X, 0, c.Width);
                Assert.InRange(h.LeftPost.Y, 0, c.Height);
                Assert.InRange(h.RightPost.Y, 0, c.Height);
            }
            Assert.InRange(f.Pegs[0].X, 0, c.Width);
            Assert.InRange(f.StartSpot.X, 0, c.Width);
            Assert.InRange(f.StartSpot.Y, 0, c.Height);
        }

        [Fact]
        public void The_circuit_matches_the_diagram_in_the_laws()
        {
            // Diagram 1 read off directly, in rotated terms: which hoop each
            // point is and which way it is run. This is the one thing in the
            // layout that cannot be derived from anything else, so it is
            // asserted outright rather than inferred.
            var f = F;
            var expected = new[]
            {
                //          hoop, direction
                (hoop: 0, dir:  1),   // Hoop 1
                (hoop: 1, dir:  1),   // Hoop 2
                (hoop: 2, dir: -1),   // Hoop 3
                (hoop: 3, dir: -1),   // Hoop 4
                (hoop: 4, dir:  1),   // Hoop 5
                (hoop: 5, dir:  1),   // Hoop 6
                (hoop: 1, dir: -1),   // 1-back
                (hoop: 0, dir: -1),   // 2-back
                (hoop: 3, dir:  1),   // 3-back
                (hoop: 2, dir:  1),   // 4-back
                (hoop: 5, dir: -1),   // Penultimate
                (hoop: 4, dir: -1)    // Rover
            };

            for (int p = 0; p < expected.Length; p++)
            {
                Assert.Equal(expected[p].hoop, f.HoopFor(p));
                Assert.Equal(expected[p].dir, f.DirectionFor(p));
            }
        }

        [Fact]
        public void Most_legs_of_the_circuit_are_straight_runs()
        {
            // Eight of the eleven hoop-to-hoop legs share a row or a column, so
            // the break flows along the court rather than criss-crossing it.
            // The three that do not are the turns between one lap and the next.
            var f = F;
            int straight = 0;
            for (int p = 0; p < 11; p++)
            {
                var a = f.TargetFor(p);
                var b = f.TargetFor(p + 1);
                if (Math.Abs(a.Y - b.Y) < 1e-9 || Math.Abs(a.X - b.X) < 1e-9) straight++;
            }
            Assert.True(straight >= 8, $"only {straight} of 11 legs run along an axis");
        }

        [Fact]
        public void No_two_consecutive_points_are_the_same_hoop()
        {
            var f = F;
            for (int p = 0; p < f.TotalPoints - 2; p++)
                Assert.NotEqual(f.HoopFor(p), f.HoopFor(p + 1));
        }

        [Fact]
        public void The_labels_are_the_association_names()
        {
            var f = F;
            Assert.Equal("Hoop 1", f.Labels[0]);
            Assert.Equal("1-back", f.Labels[6]);
            Assert.Equal("Penultimate", f.Labels[10]);
            Assert.Equal("Rover", f.Labels[11]);
            Assert.Equal("Peg", f.Labels[12]);
        }

        [Fact]
        public void A_ball_can_be_walked_all_the_way_round()
        {
            var arr = new[] { new Ball(Vec2.Zero) };
            var g = new Game(new World(arr, F, Court), null, RuleOptions.Basic);
            var f = g.World.Field;

            for (int p = 0; p < f.TotalPoints; p++)
            {
                Assert.Equal(p, g.States[0].Point);
                if (f.IsPeg(p))
                {
                    var peg = f.PegFor(p);
                    g.World.Balls[0].Pos = new Vec2(peg.X - 1.0, peg.Y);
                    g.Play(new Vec2(1, 0), 2.0);
                }
                else
                {
                    g.World.Balls[0].Pos = InFrontOf(f, p);
                    g.Play(new Vec2(f.DirectionFor(p), 0), 1.3);
                }
            }

            Assert.True(g.States[0].Finished);
        }

        // ---- the laws that differ -----------------------------------------

        [Fact]
        public void Two_hoops_in_one_stroke_still_earn_only_one_continuation()
        {
            // Law 19.3: continuation strokes are not cumulative. The USCA rules
            // give two for two wickets; association croquet gives one.
            var g = NewGame();
            var f = g.World.Field;

            // Hoops 1 and 2 share a row, so one firm stroke can run both --
            // but not so firm that it carries on off the far end, which under
            // these laws would end the turn and prove nothing.
            g.World.Balls[0].Pos = new Vec2(f.Hoops[0].Center.X - 0.3, f.Hoops[0].Center.Y);

            var r = g.Play(new Vec2(1, 0), 5.2);

            Assert.Equal(2, r.PointsScored.Count);
            Assert.Equal(1, r.ShotsLeft);
            Assert.False(r.TurnEnded);
        }

        [Fact]
        public void Running_a_hoop_and_hitting_a_ball_beyond_it_gives_both()
        {
            // Law 21.2, and the opposite of the USCA rule, where the contact
            // after a wicket is ignored.
            var g = NewGame();
            var f = g.World.Field;
            var h = f.Hoops[0];

            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.4, h.Center.Y);
            g.World.Balls[1].Pos = new Vec2(h.Center.X + 0.9, h.Center.Y);

            var r = g.Play(new Vec2(1, 0), 2.4);

            Assert.Equal(new[] { 0 }, r.PointsScored);
            Assert.Equal(1, r.Roqueted);
            Assert.Equal(2, r.ShotsLeft);
            Assert.Equal(StrokeKind.Bonus, g.Stroke);
        }

        [Fact]
        public void A_roquet_before_the_hoop_still_cancels_the_hoop()
        {
            // Law 21.3, which both games share.
            var g = NewGame();
            var f = g.World.Field;
            var h = f.Hoops[0];

            g.World.Balls[1].Pos = new Vec2(h.Center.X - 0.6, h.Center.Y);
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 1.7, h.Center.Y);

            var r = g.Play(new Vec2(1, 0), 3.2);

            Assert.Equal(1, r.Roqueted);
            Assert.Empty(r.PointsScored);
            Assert.Equal(0, g.States[0].Point);
        }

        [Fact]
        public void Only_the_croquet_shot_is_offered()
        {
            var g = NewGame();
            Assert.False(g.Laws.FourWaysToTakeCroquet);
        }

        [Fact]
        public void Deadness_lapses_between_turns()
        {
            // AC Law 2.6.10: croquet may be taken once from each ball per turn,
            // renewed by scoring a hoop. It does not carry over.
            var g = NewGame((5.0, 12.8), (7.0, 12.8));
            Assert.False(g.Options.CarryOverDeadness);

            g.Play(new Vec2(1, 0), 3.0);
            Assert.False(g.IsAlive(1));

            g.PlayBonus(BonusWay.CroquetShot, new Vec2(-1, 0), new Vec2(0, 1), 0.6);
            g.Play(new Vec2(0, 1), 0.6);
            while (g.Striker != 0) g.Play(new Vec2(0, 1), 0.5);

            Assert.True(g.IsAlive(1));
        }

        [Fact]
        public void The_strikers_own_ball_going_out_ends_the_turn()
        {
            var g = NewGame();
            var f = g.World.Field;
            g.World.Balls[0].Pos = InFrontOf(f, 0);

            var r = g.Play(new Vec2(1, 0), 9.0);

            Assert.Contains(0, r.BroughtIn);
            Assert.True(r.EndedByOutOfBounds);
            Assert.True(r.TurnEnded);
        }

        [Fact]
        public void Sending_someone_else_out_does_not_end_the_turn()
        {
            // Association croquet has no general penalty for sending another
            // ball off -- only for the croqueted ball in a croquet stroke,
            // which is not modelled yet.
            // In line, so the striker actually drives the other one out, and
            // far enough back that the striker itself stays on.
            var g = NewGame((20.0, 20.0), (20.0, 23.0));

            var r = g.Play(new Vec2(0, 1), 5.0);

            Assert.Contains(1, r.BroughtIn);
            Assert.DoesNotContain(0, r.BroughtIn);
            Assert.False(r.EndedByOutOfBounds);
            Assert.False(r.TurnEnded);
        }

        [Fact]
        public void A_ball_out_is_replaced_a_yard_in_on_the_yard_line()
        {
            var g = NewGame();
            var c = g.World.Spec;
            g.World.Balls[0].Pos = new Vec2(10.0, 2.0);

            g.Play(new Vec2(0, -1), 5.0);

            Assert.Equal(Field.Yard, g.World.Balls[0].Pos.Y, 6);
            Assert.Equal(10.0, g.World.Balls[0].Pos.X, 6);
        }

        [Fact]
        public void A_side_wins_when_both_its_balls_are_round()
        {
            var g = NewGame();
            var f = g.World.Field;

            g.States[2].Point = f.TotalPoints;          // black already round
            g.World.Balls[2].InPlay = false;

            g.States[0].Point = 12;                      // blue is a rover, for the peg
            g.World.Balls[0].Pos = new Vec2(f.Pegs[0].X - 1.0, f.Pegs[0].Y);

            var r = g.Play(new Vec2(1, 0), 2.0);

            Assert.True(r.PeggedOut);
            Assert.Equal(new[] { 0, 2 }, g.Winner);
        }
    }
}
