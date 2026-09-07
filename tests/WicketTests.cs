using Croquet.Core;
using Xunit;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// Running a wicket, which is fussier than it looks.
    ///
    /// A hoop is not a plane the ball crosses -- it is a gap with thickness,
    /// and the ball can stop inside it. The USCA rules and their diagram (page
    /// 5 and 6 of the rulebook in the repo root) are explicit about the three
    /// places a ball can be:
    ///
    ///   * short of the playing-side face  -- has not started
    ///   * between the two faces           -- started, has NOT scored (ball C)
    ///   * clear of the far face           -- has scored (ball D)
    ///
    /// and about the two ways of not scoring: "if a ball passes through a
    /// wicket but rolls back, it has not scored the wicket", and "if a ball
    /// travels backwards through its wicket to get position, it must be clear
    /// of the non-playing side to then score the wicket in the correct
    /// direction".
    ///
    /// These place balls by hand and tap them the short distance needed. The
    /// physics is proven elsewhere and is only the delivery mechanism.
    /// </summary>
    public class WicketTests
    {
        /// <summary>A court built here, so tuning feel never turns this red.</summary>
        static CourtSpec Lawn() => new CourtSpec
        {
            Width = 30,
            Height = 15,
            Friction = 0.9,
            Restitution = 0.8,
            ObstacleRestitution = 0.5
        };

        static Game NewGame(params (double x, double y)[] at)
        {
            var spec = Lawn();
            var balls = new Ball[at.Length];
            for (int i = 0; i < at.Length; i++) balls[i] = new Ball(Vec2.Zero);

            var g = new Game(new World(balls, Field.NineWicket(), spec), null, RuleOptions.Basic);
            for (int i = 0; i < at.Length; i++)
            {
                g.States[i].Started = true;
                g.World.Balls[i].InPlay = true;
                g.World.Balls[i].Pos = new Vec2(at[i].x, at[i].y);
            }
            return g;
        }

        /// <summary>Wicket 1: the first point, run in the +x direction.</summary>
        static Hoop First(Game g) => g.World.Field.Hoops[g.World.Field.HoopFor(0)];

        /// <summary>How far past the hoop's far face a ball has to be to have cleared it.</summary>
        static double ClearOf(Game g, Hoop h) => h.WireRadius + g.World.Spec.BallRadius;

        [Fact]
        public void A_ball_that_runs_right_through_scores_it()
        {
            var g = NewGame((0, 0));
            var h = First(g);
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.5, h.Center.Y);

            var r = g.Play(new Vec2(1, 0), 1.6);

            Assert.Contains(0, r.PointsScored);
            Assert.Equal(1, g.States[0].Point);
        }

        [Fact]
        public void A_ball_left_in_the_jaws_has_not_scored_it()
        {
            // Ball C in the diagram: through the near face, not clear of the
            // far one. It is the case the old centre-plane model got wrong --
            // the centre was past the middle, so it counted.
            var g = NewGame((0, 0));
            var h = First(g);

            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.30, h.Center.Y);
            var r = g.Play(new Vec2(1, 0), 0.74);

            var deep = g.World.Balls[0].Pos.X - h.Center.X;
            Assert.True(System.Math.Abs(deep) < ClearOf(g, h),
                        $"the ball should have stopped in the jaws, but sits at {deep:0.###}");

            Assert.Empty(r.PointsScored);
            Assert.Equal(0, g.States[0].Point);
        }

        [Fact]
        public void A_ball_in_the_jaws_scores_on_the_stroke_that_carries_it_out()
        {
            // ...and it is the LATER stroke that scores it, which is the other
            // half of getting this right: the point is not lost, only deferred.
            var g = NewGame((0, 0));
            var h = First(g);

            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.30, h.Center.Y);
            g.Play(new Vec2(1, 0), 0.74);
            Assert.Equal(0, g.States[0].Point);

            var r = g.Play(new Vec2(1, 0), 1.2);

            Assert.Contains(0, r.PointsScored);
            Assert.Equal(1, g.States[0].Point);
        }

        [Fact]
        public void A_ball_that_goes_through_and_rolls_back_has_not_scored_it()
        {
            // "If a ball passes through a wicket but rolls back, it has not
            // scored the wicket." Set up by hand rather than by trying to make
            // a ball rebound, so the test is about the rule and not the bounce.
            var g = NewGame((0, 0), (0, 0));
            var h = First(g);

            // Blue is beyond the hoop, having run it in an earlier stroke, and
            // is played back through towards the playing side.
            g.World.Balls[0].Pos = new Vec2(h.Center.X + 0.5, h.Center.Y);
            g.World.Balls[1].Pos = new Vec2(2, 13.5);
            g.Play(new Vec2(-1, 0), 1.6);        // back through, to get position

            Assert.Equal(0, g.States[0].Point);
            Assert.True(g.World.Balls[0].Pos.X < h.Center.X - ClearOf(g, h),
                        "it should be clear on the playing side again");
        }

        [Fact]
        public void Going_part_of_the_way_back_and_forward_again_scores_nothing()
        {
            // The rule that says a ball which retreats INTO the jaws has not
            // undone anything: it must be clear of the far side before running
            // the wicket can start again. Half a retreat is no retreat.
            var g = NewGame((0, 0), (0, 0));
            var h = First(g);
            g.World.Balls[1].Pos = new Vec2(2, 13.5);

            // Run it properly first, which scores.
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.5, h.Center.Y);
            var first = g.Play(new Vec2(1, 0), 1.6);
            Assert.Contains(0, first.PointsScored);

            int after = g.States[0].Point;

            // Now put it back into the jaws and bring it forward again. It has
            // never been clear on the playing side, so nothing is scored.
            g.World.Balls[0].Pos = new Vec2(h.Center.X + 0.02, h.Center.Y);
            var again = g.Play(new Vec2(1, 0), 1.2);

            Assert.DoesNotContain(0, again.PointsScored);
            Assert.Equal(after, g.States[0].Point);
        }

        [Fact]
        public void Running_it_the_wrong_way_scores_nothing()
        {
            var g = NewGame((0, 0), (0, 0));
            var h = First(g);
            g.World.Balls[1].Pos = new Vec2(2, 13.5);

            g.World.Balls[0].Pos = new Vec2(h.Center.X + 0.5, h.Center.Y);
            var r = g.Play(new Vec2(-1, 0), 1.6);

            Assert.Empty(r.PointsScored);
            Assert.Equal(0, g.States[0].Point);
        }

        [Fact]
        public void Rolling_past_the_outside_of_a_hoop_scores_nothing()
        {
            // The oldest trap, and still worth holding: the ball ends up on the
            // far side, but it never went between the uprights.
            var g = NewGame((0, 0), (0, 0));
            var h = First(g);
            g.World.Balls[1].Pos = new Vec2(2, 13.5);

            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.5, h.Center.Y + 1.2);
            var r = g.Play(new Vec2(1, 0), 1.6);

            Assert.Empty(r.PointsScored);
            Assert.True(g.World.Balls[0].Pos.X > h.Center.X, "it should have gone past");
        }
    }
}
