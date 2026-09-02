using System.Linq;
using Croquet.Core;
using Xunit;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// The turn structure, against the USCA 9-wicket basic rules (the PDF in
    /// the repo root). These are rules tests: they place balls exactly where a
    /// situation needs them and tap them the short distance required. The
    /// physics is proven elsewhere and is only the delivery mechanism here.
    /// </summary>
    public class GameTests
    {
        /// <summary>
        /// A game with every ball already on the lawn at a chosen spot.
        /// Options default to the house set (carry-over deadness, out of bounds
        /// ends the turn); tests of the plain rulebook pass RuleOptions.Basic.
        /// </summary>
        // Instance, not static: xunit builds a fresh test class per test, so
        // this cannot leak from one to the next. Null means the house defaults.
        RuleOptions Opts;

        Game NewGame(int balls = 4, int[] side = null, params (double x, double y)[] at)
        {
            var arr = new Ball[balls];
            for (int i = 0; i < balls; i++) arr[i] = new Ball(Vec2.Zero);
            var g = new Game(new World(arr, Field.NineWicket(), new CourtSpec()), side, Opts);

            for (int i = 0; i < balls; i++)
            {
                g.States[i].Started = true;
                g.World.Balls[i].InPlay = true;
                g.World.Balls[i].Pos = i < at.Length ? new Vec2(at[i].x, at[i].y)
                                                     : new Vec2(1 + i * 0.9, 1.0);
            }
            return g;
        }

        /// <summary>A game left as the rules make it: nothing on the lawn yet.</summary>
        Game FreshGame(int balls = 4) =>
            new Game(new World(Enumerable.Range(0, balls).Select(_ => new Ball(Vec2.Zero)).ToArray(),
                               Field.NineWicket(), new CourtSpec()), null, Opts);

        static Vec2 InFrontOf(Field f, int point, double back = 0.45)
        {
            var h = f.Hoops[f.HoopFor(point)];
            return new Vec2(h.Center.X - f.DirectionFor(point) * back, h.Center.Y);
        }

        /// <summary>Parks the balls that are not part of a test well out of the way.</summary>
        static void ParkOthers(Game g, params int[] keep)
        {
            for (int i = 0; i < g.World.Balls.Length; i++)
                if (!keep.Contains(i)) g.World.Balls[i].Pos = new Vec2(1.0 + i * 0.7, 14.0);
        }

        // ---- scoring ------------------------------------------------------

        [Fact]
        public void Running_your_wicket_scores_it_and_earns_one_bonus_shot()
        {
            var g = NewGame();
            ParkOthers(g, 0);
            g.World.Balls[0].Pos = InFrontOf(g.World.Field, 0);

            var r = g.Play(new Vec2(1, 0), 1.3);

            Assert.Equal(new[] { 0 }, r.PointsScored);
            Assert.Equal(1, g.States[0].Point);
            Assert.Equal(1, r.ShotsLeft);
            Assert.False(r.TurnEnded);
        }

        [Fact]
        public void Missing_ends_the_turn()
        {
            var g = NewGame();
            ParkOthers(g, 0);
            var h = g.World.Field.Hoops[0];
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.45, h.Center.Y + 1.5);

            var r = g.Play(new Vec2(1, 0), 1.3);

            Assert.Empty(r.PointsScored);
            Assert.True(r.TurnEnded);
            Assert.Equal(1, g.Striker);
        }

        [Fact]
        public void Wickets_must_be_run_in_order()
        {
            var g = NewGame();
            ParkOthers(g, 0);
            g.World.Balls[0].Pos = InFrontOf(g.World.Field, 1);   // wicket 2, out of turn

            var r = g.Play(new Vec2(1, 0), 1.3);

            Assert.Empty(r.PointsScored);
            Assert.Equal(0, g.States[0].Point);
        }

        [Fact]
        public void The_turning_stake_is_a_point_and_earns_a_shot()
        {
            var g = NewGame();
            ParkOthers(g, 0);
            var f = g.World.Field;
            g.States[0].Point = Field.TurningPegPoint;
            g.World.Balls[0].Pos = new Vec2(f.TurningPeg.X - 1.0, f.TurningPeg.Y);

            var r = g.Play(new Vec2(1, 0), 2.0);

            Assert.Equal(new[] { Field.TurningPegPoint }, r.PointsScored);
            Assert.Equal(1, r.ShotsLeft);
            Assert.False(r.TurnEnded);
        }

        [Fact]
        public void Two_wickets_in_one_stroke_earn_two_shots_not_three()
        {
            // "Two bonus shots are earned when the striker ball scores two
            // wickets in one shot." Wickets 1 and 2 are both on the centre line.
            var g = NewGame();
            ParkOthers(g, 0);
            var f = g.World.Field;
            g.World.Balls[0].Pos = new Vec2(f.Hoops[0].Center.X - 0.3, f.Hoops[0].Center.Y);

            var r = g.Play(new Vec2(1, 0), 3.0);

            Assert.Equal(new[] { 0, 1 }, r.PointsScored);
            Assert.Equal(2, r.ShotsLeft);
        }

        [Fact]
        public void Hitting_the_finishing_stake_at_the_end_stakes_the_ball_out()
        {
            var g = NewGame();
            ParkOthers(g, 0);
            var f = g.World.Field;
            g.States[0].Point = Field.HomePegPoint;
            g.World.Balls[0].Pos = new Vec2(f.HomePeg.X + 1.0, f.HomePeg.Y);

            var r = g.Play(new Vec2(-1, 0), 2.0);

            Assert.True(r.PeggedOut);
            Assert.False(g.World.Balls[0].InPlay);
            Assert.True(r.TurnEnded);
        }

        [Fact]
        public void A_ball_driven_through_its_own_wicket_by_another_scores_it()
        {
            // "A ball caused to score its wicket during another ball's turn
            // earns the point for its side, but no bonus shot is earned."
            var g = NewGame();
            ParkOthers(g, 0, 1);
            var f = g.World.Field;
            var h = f.Hoops[0];

            g.World.Balls[1].Pos = new Vec2(h.Center.X - 0.35, h.Center.Y);
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 1.4, h.Center.Y);

            // Gently: harder and the driven ball runs wicket 2 as well, which
            // is legal but makes this a test of two things at once.
            var r = g.Play(new Vec2(1, 0), 2.0);

            Assert.Contains((1, 0), r.OthersScored);
            Assert.Equal(1, g.States[1].Point);
            Assert.Empty(r.PointsScored);              // the striker did not run it
        }

        // ---- roquet, and the order rules ----------------------------------

        [Fact]
        public void Hitting_a_live_ball_is_a_roquet_worth_two_shots()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0) });
            ParkOthers(g, 0, 1);

            var r = g.Play(new Vec2(1, 0), 3.0);

            Assert.Equal(1, r.Roqueted);
            Assert.Equal(2, r.ShotsLeft);
            Assert.Equal(StrokeKind.Bonus, g.Stroke);
            Assert.Equal(1, g.RoquetedBall);
            Assert.False(g.IsAlive(1));
        }

        [Fact]
        public void A_wicket_before_a_contact_means_the_contact_is_ignored()
        {
            // "When the striker ball scores a wicket and then in the same shot
            // hits another ball, only the wicket counts and the striker has
            // earned only the one bonus shot for scoring the wicket."
            var g = NewGame();
            ParkOthers(g, 0, 1);
            var h = g.World.Field.Hoops[0];
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.4, h.Center.Y);
            g.World.Balls[1].Pos = new Vec2(h.Center.X + 0.9, h.Center.Y);

            var r = g.Play(new Vec2(1, 0), 2.4);

            Assert.Equal(new[] { 0 }, r.PointsScored);
            Assert.Equal(-1, r.Roqueted);
            Assert.Equal(1, r.TouchedButNoRoquet);
            Assert.Equal(1, r.ShotsLeft);                  // one, for the wicket
            Assert.Equal(StrokeKind.Ordinary, g.Stroke);   // no bonus placement owed
        }

        [Fact]
        public void A_contact_before_a_wicket_means_the_wicket_does_not_count()
        {
            // "When the striker ball roquets another ball and then goes through
            // a wicket, the wicket has not been scored but the striker earns
            // two bonus shots for the roquet."
            var g = NewGame();
            ParkOthers(g, 0, 1);
            var h = g.World.Field.Hoops[0];
            g.World.Balls[1].Pos = new Vec2(h.Center.X - 0.6, h.Center.Y);
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 1.7, h.Center.Y);

            var r = g.Play(new Vec2(1, 0), 3.2);

            Assert.Equal(1, r.Roqueted);
            Assert.Empty(r.PointsScored);
            Assert.Equal(0, g.States[0].Point);            // wicket 1 still to do
            Assert.Equal(2, r.ShotsLeft);
        }

        [Fact]
        public void Hitting_a_ball_you_are_already_dead_on_earns_nothing_but_costs_nothing()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0) });
            ParkOthers(g, 0, 1);
            g.States[0].Dead.Add(1);

            var r = g.Play(new Vec2(1, 0), 3.0);

            Assert.Equal(-1, r.Roqueted);
            Assert.Equal(1, r.TouchedButNoRoquet);
            Assert.True(r.TurnEnded);
            Assert.Equal(1, g.Striker);
        }

        [Fact]
        public void Deadness_lapses_at_the_start_of_the_next_turn_under_the_basic_rule()
        {
            // Basic: dead until you clear your next wicket OR the start of your
            // next turn, whichever comes first.
            Opts = RuleOptions.Basic;
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0) });
            ParkOthers(g, 0, 1);

            g.Play(new Vec2(1, 0), 3.0);                              // roquet
            Assert.False(g.IsAlive(1));
            g.PlayBonus(BonusWay.WhereItLies, Vec2.Zero, new Vec2(0, 1), 0.6);
            g.Play(new Vec2(0, 1), 0.6);                              // continuation, nothing
            Assert.True(g.Striker != 0);

            while (g.Striker != 0) g.Play(new Vec2(0, 1), 0.5);       // back round to blue

            Assert.Empty(g.States[0].Dead);
            Assert.True(g.IsAlive(1));
        }

        [Fact]
        public void Under_carry_over_deadness_it_survives_the_turn()
        {
            // Option 1, and the house default: deadness lifts only when the
            // ball clears its next wicket -- never merely because a new turn
            // has begun.
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0) });
            ParkOthers(g, 0, 1);
            Assert.True(g.Options.CarryOverDeadness);

            g.Play(new Vec2(1, 0), 3.0);                                  // roquet
            g.PlayBonus(BonusWay.WhereItLies, Vec2.Zero, new Vec2(0, 1), 0.6);
            g.Play(new Vec2(0, 1), 0.6);                                  // nothing; turn over

            while (g.Striker != 0) g.Play(new Vec2(0, 1), 0.5);           // round to blue again

            Assert.Contains(1, g.States[0].Dead);
            Assert.False(g.IsAlive(1));
        }

        [Fact]
        public void Carry_over_deadness_still_lifts_on_clearing_a_wicket()
        {
            var g = NewGame();
            ParkOthers(g, 0);
            Assert.True(g.Options.CarryOverDeadness);
            g.States[0].Dead.Add(1);
            g.World.Balls[0].Pos = InFrontOf(g.World.Field, 0);

            g.Play(new Vec2(1, 0), 1.3);

            Assert.Empty(g.States[0].Dead);
        }

        [Fact]
        public void Running_a_wicket_revives_you_for_later_strokes_in_the_turn()
        {
            var g = NewGame();
            ParkOthers(g, 0);
            g.States[0].Dead.Add(1);
            g.States[0].Dead.Add(2);
            g.World.Balls[0].Pos = InFrontOf(g.World.Field, 0);

            g.Play(new Vec2(1, 0), 1.3);

            Assert.Empty(g.States[0].Dead);
            Assert.True(g.IsAlive(1));
        }

        // ---- the four bonus ways ------------------------------------------

        Game AfterRoquet()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (7.0, 7.0) });
            ParkOthers(g, 0, 1);
            g.Play(new Vec2(1, 0), 3.0);
            Assert.Equal(StrokeKind.Bonus, g.Stroke);
            return g;
        }

        [Fact]
        public void A_mallet_head_shot_stands_a_mallet_head_clear()
        {
            var g = AfterRoquet();
            var at = g.BonusPlacement(BonusWay.MalletHead, new Vec2(-1, 0));
            double gap = (at - g.World.Balls[1].Pos).Length;
            Assert.Equal(g.World.Spec.BallRadius * 2 + g.World.Spec.MalletHead, gap, 9);
        }

        [Fact]
        public void A_croquet_shot_and_a_foot_shot_start_in_contact()
        {
            foreach (var way in new[] { BonusWay.CroquetShot, BonusWay.FootShot })
            {
                var g = AfterRoquet();
                var at = g.BonusPlacement(way, new Vec2(-1, 0));
                Assert.Equal(g.World.Spec.BallRadius * 2, (at - g.World.Balls[1].Pos).Length, 9);
            }
        }

        [Fact]
        public void Playing_where_it_lies_does_not_move_the_striker()
        {
            var g = AfterRoquet();
            var rest = g.World.Balls[0].Pos;
            Assert.Equal(rest, g.BonusPlacement(BonusWay.WhereItLies, new Vec2(-1, 0)));
        }

        [Fact]
        public void A_croquet_shot_sends_both_balls()
        {
            var g = AfterRoquet();
            var was = g.World.Balls[1].Pos;

            g.PlayBonus(BonusWay.CroquetShot, new Vec2(-1, 0), new Vec2(1, 0), 3.0);

            Assert.True(g.World.Balls[1].Pos.X > was.X + 0.1, "the croqueted ball should travel");
            Assert.True(g.World.Balls[0].Pos.X > was.X - 0.3, "and so should the striker");
        }

        [Fact]
        public void A_foot_shot_sends_the_other_ball_and_holds_the_striker()
        {
            var g = AfterRoquet();
            var placed = g.BonusPlacement(BonusWay.FootShot, new Vec2(-1, 0));
            var was = g.World.Balls[1].Pos;

            g.PlayBonus(BonusWay.FootShot, new Vec2(-1, 0), new Vec2(1, 0), 3.0);

            Assert.True(g.World.Balls[1].Pos.X > was.X + 1.0, "the ball should be sent");
            Assert.Equal(placed, g.World.Balls[0].Pos);       // held under the foot
        }

        [Theory]
        [InlineData(BonusWay.MalletHead)]
        [InlineData(BonusWay.CroquetShot)]
        [InlineData(BonusWay.FootShot)]
        [InlineData(BonusWay.WhereItLies)]
        public void Every_bonus_way_leaves_a_continuation_shot(BonusWay way)
        {
            var g = AfterRoquet();

            var r = g.PlayBonus(way, new Vec2(-1, 0), new Vec2(0, 1), 1.0);

            Assert.False(r.TurnEnded);
            Assert.Equal(1, r.ShotsLeft);
            Assert.Equal(StrokeKind.Ordinary, g.Stroke);
            Assert.Equal(0, g.Striker);
        }

        [Fact]
        public void A_bonus_shot_that_scores_a_wicket_leaves_one_shot_not_two()
        {
            // Q16: "What happens when, after receiving two bonus shots, my
            // first bonus shot clears a wicket? A: You have one shot."
            var g = NewGame();
            ParkOthers(g, 0, 1);
            var h = g.World.Field.Hoops[0];
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 2.2, h.Center.Y - 0.6);
            g.World.Balls[1].Pos = new Vec2(h.Center.X - 1.4, h.Center.Y - 0.6);

            var roq = g.Play(new Vec2(1, 0), 2.4);
            Assert.Equal(1, roq.Roqueted);
            Assert.Equal(2, roq.ShotsLeft);

            // Park the roqueted ball below the line of the hoop and take the
            // mallet-head shot from above it. Placing on the near side instead
            // would leave that ball sitting between the striker and the hoop.
            g.World.Balls[1].Pos = new Vec2(2.8, h.Center.Y - 0.72);
            var toHoop = new Vec2(h.Center.X - 2.8, 0.72 - g.World.Spec.MalletHead
                                                          - g.World.Spec.BallRadius * 2);
            var r = g.PlayBonus(BonusWay.MalletHead, new Vec2(0, 1), toHoop, 1.8);

            Assert.Contains(0, r.PointsScored);
            Assert.Equal(1, r.ShotsLeft);          // forfeited the accumulated one
        }

        [Fact]
        public void An_ordinary_stroke_cannot_be_played_while_a_bonus_is_owed()
        {
            var g = AfterRoquet();
            Assert.Throws<System.InvalidOperationException>(() => g.Play(new Vec2(1, 0), 1.0));
        }

        [Fact]
        public void The_same_ball_cannot_be_roqueted_twice_before_a_wicket()
        {
            var g = AfterRoquet();
            g.PlayBonus(BonusWay.CroquetShot, new Vec2(-1, 0), new Vec2(1, 0), 0.8);
            var r = g.Play(new Vec2(1, 0), 3.0);

            Assert.Equal(-1, r.Roqueted);
            Assert.True(r.TurnEnded);
        }

        // ---- boundaries ---------------------------------------------------

        /// <summary>Runs wicket 3 and carries on off the far end of the lawn.</summary>
        Game ShotThatRunsOffTheEnd(out StrokeResult r)
        {
            var g = NewGame();
            ParkOthers(g, 0);
            g.States[0].Point = 2;
            g.World.Balls[0].Pos = InFrontOf(g.World.Field, 2);
            r = g.Play(new Vec2(1, 0), 7.0);
            return g;
        }

        [Fact]
        public void Under_the_basic_rule_going_out_costs_nothing()
        {
            // Q17: "If I send a ball over the boundary, is there a penalty?
            // A: No." The wicket it scored on the way still stands, and the
            // bonus shot that wicket earned is still owed.
            Opts = RuleOptions.Basic;
            var g = ShotThatRunsOffTheEnd(out var r);

            Assert.Contains(2, r.PointsScored);
            Assert.Contains(0, r.BroughtIn);
            Assert.False(r.TurnEnded, "the basic rules have no penalty for going out");
            Assert.False(r.EndedByOutOfBounds);
        }

        [Fact]
        public void Under_option_2A_going_out_ends_the_turn()
        {
            // The house default. The point still counts and the ball still
            // comes back in -- but the turn is over, even though the wicket
            // had earned a bonus shot.
            var g = ShotThatRunsOffTheEnd(out var r);
            Assert.True(g.Options.OutOfBoundsEndsTurn);

            Assert.Contains(2, r.PointsScored);
            Assert.Contains(0, r.BroughtIn);
            Assert.True(r.EndedByOutOfBounds);
            Assert.True(r.TurnEnded);
            Assert.Equal(0, r.ShotsLeft);
            Assert.Equal(1, g.Striker);
        }

        [Fact]
        public void A_ball_that_goes_out_is_replaced_a_mallet_length_in()
        {
            var g = ShotThatRunsOffTheEnd(out _);
            Assert.Equal(g.World.Spec.Width - g.World.Spec.BoundaryReturn,
                         g.World.Balls[0].Pos.X, 6);
        }

        [Fact]
        public void Sending_someone_elses_ball_out_also_ends_the_turn()
        {
            // Off the centre line: the turning peg sits on it, and a ball
            // driven down that lane rebounds off the peg instead of going out.
            var g = NewGame(at: new[] { (26.0, 13.0), (28.0, 13.0) });
            ParkOthers(g, 0, 1);

            var r = g.Play(new Vec2(1, 0), 6.0);

            Assert.Equal(1, r.Roqueted);

            Assert.Contains(1, r.BroughtIn);
            Assert.True(r.EndedByOutOfBounds);
            Assert.True(r.TurnEnded, "the roquet earned two shots, but the turn is over anyway");
        }

        [Fact]
        public void A_ball_brought_in_comes_square_to_the_line_it_crossed()
        {
            var g = NewGame();
            ParkOthers(g, 0);
            var c = g.World.Spec;
            g.World.Balls[0].Pos = new Vec2(8.0, 1.0);

            g.Play(new Vec2(0, -1), 4.0);            // off the near side

            Assert.Equal(c.BoundaryReturn, g.World.Balls[0].Pos.Y, 6);
            Assert.Equal(8.0, g.World.Balls[0].Pos.X, 6);   // no sideways shift
        }

        // ---- turns and coming on ------------------------------------------

        [Fact]
        public void Only_the_first_ball_is_on_the_lawn_at_the_start()
        {
            var g = FreshGame(4);
            Assert.True(g.World.Balls[0].InPlay);
            for (int i = 1; i < 4; i++) Assert.False(g.World.Balls[i].InPlay);
        }

        [Fact]
        public void Balls_start_halfway_between_the_stake_and_wicket_one()
        {
            var g = FreshGame(4);
            var f = g.World.Field;
            var expected = new Vec2((f.HomePeg.X + f.Hoops[0].Center.X) / 2, f.HomePeg.Y);
            Assert.Equal(expected, g.World.Field.StartSpot);
            Assert.Equal(expected, g.World.Balls[0].Pos);
        }

        [Fact]
        public void Every_ball_comes_on_from_the_same_spot_as_its_turn_arrives()
        {
            var g = FreshGame(4);
            var spot = g.World.Field.StartSpot;
            var away = new[] { new Vec2(0, 1), new Vec2(0, -1), new Vec2(-1, -1) };

            for (int i = 1; i < 4; i++)
            {
                g.Play(away[i - 1], 1.5);
                Assert.Equal(i, g.Striker);
                Assert.True(g.World.Balls[i].InPlay);
                Assert.Equal(spot, g.World.Balls[i].Pos);
            }
        }

        [Fact]
        public void A_ball_coming_on_does_not_land_on_one_already_there()
        {
            var g = FreshGame(4);
            g.Play(new Vec2(0, 1), 0.05);
            double apart = (g.World.Balls[1].Pos - g.World.Balls[0].Pos).Length;
            Assert.True(apart >= g.World.Spec.BallRadius * 2);
        }

        [Fact]
        public void The_turn_wraps_round_the_ball_order()
        {
            var g = NewGame(at: new[] { (5.0, 7.0), (8.0, 2.0), (11.0, 2.0), (14.0, 2.0) });
            for (int expected = 1; expected <= 4; expected++)
            {
                g.Play(new Vec2(0, 1), 0.6);
                Assert.Equal(expected % 4, g.Striker);
            }
        }

        [Fact]
        public void A_staked_out_ball_is_skipped()
        {
            var g = NewGame(4, new[] { 0, 1, 0, 1 });
            ParkOthers(g, 0);
            g.States[1].Point = g.World.Field.TotalPoints;
            g.World.Balls[1].InPlay = false;

            var h = g.World.Field.Hoops[0];
            g.World.Balls[0].Pos = new Vec2(h.Center.X - 0.45, h.Center.Y + 1.5);
            g.Play(new Vec2(1, 0), 1.3);

            Assert.Equal(2, g.Striker);
        }

        // ---- winning ------------------------------------------------------

        [Fact]
        public void A_side_wins_when_all_of_its_balls_are_round()
        {
            var g = NewGame(4, new[] { 0, 1, 0, 1 });
            ParkOthers(g, 0);
            var f = g.World.Field;
            g.States[2].Point = g.World.Field.TotalPoints;
            g.World.Balls[2].InPlay = false;

            g.States[0].Point = Field.HomePegPoint;
            g.World.Balls[0].Pos = new Vec2(f.HomePeg.X + 1.0, f.HomePeg.Y);

            g.Play(new Vec2(-1, 0), 2.0);

            Assert.Equal(new[] { 0, 2 }, g.Winner);
        }

        [Fact]
        public void One_ball_round_is_not_a_win_for_the_side()
        {
            var g = NewGame(4, new[] { 0, 1, 0, 1 });
            ParkOthers(g, 0);
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
            ParkOthers(g, 0);
            var f = g.World.Field;
            g.States[0].Point = Field.HomePegPoint;
            g.World.Balls[0].Pos = new Vec2(f.HomePeg.X + 1.0, f.HomePeg.Y);

            g.Play(new Vec2(-1, 0), 2.0);

            Assert.Equal(new[] { 0 }, g.Winner);
        }

        [Fact]
        public void A_ball_can_be_walked_all_the_way_round()
        {
            var g = NewGame(1);
            var f = g.World.Field;

            for (int p = 0; p < g.World.Field.TotalPoints; p++)
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
