using Croquet.Core;
using Xunit;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// USCA Challenging Option 11, "Wicketed Ball" (page 10 of the rulebook in
    /// the repo root):
    ///
    ///   If the striker's ball becomes "wicketed" (stuck in the jaws of the
    ///   wicket), the next player may not roquet the wicketed ball. If the
    ///   opponent's ball roquets a wicketed ball, the balls are replaced and the
    ///   opponents lose their next turn. The striker may cannon the wicketed
    ///   ball with another ball without penalty. Example: if Red is wicketed and
    ///   then Black roquets Red, Red and Black are replaced, and then Yellow
    ///   plays, Blue loses its turn, and then Red plays.
    ///
    /// Red is put in the jaws the way <see cref="WicketTests"/> does it -- tapped
    /// in from just short of wicket 1 -- so it is left there by its own stroke,
    /// which is what the rule is about.
    /// </summary>
    public class WicketedBallTests
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

        /// <summary>Blue and Black against Red and Yellow.</summary>
        static readonly int[] Sides = { 0, 1, 0, 1 };

        static RuleOptions Option(bool on)
        {
            var o = RuleOptions.Basic;
            o.WicketedBall = on;
            return o;
        }

        /// <summary>Every ball on the lawn, parked along the top edge clear of everything.</summary>
        static Game NewGame(int balls, int[] side, RuleOptions options)
        {
            var arr = new Ball[balls];
            for (int i = 0; i < balls; i++) arr[i] = new Ball(Vec2.Zero);

            var g = new Game(new World(arr, Field.NineWicket(), Lawn()), side, options);
            for (int i = 0; i < balls; i++)
            {
                g.States[i].Started = true;
                g.World.Balls[i].InPlay = true;
                g.World.Balls[i].Pos = new Vec2(1.0 + i * 0.7, 14.0);
            }
            return g;
        }

        static Hoop First(Game g) => g.World.Field.Hoops[g.World.Field.HoopFor(0)];

        /// <summary>A stroke that does nothing and ends the turn.</summary>
        static StrokeResult Pass(Game g)
        {
            var r = g.Play(new Vec2(-1, 0), 0.3);
            Assert.True(r.TurnEnded, "a pass should end the turn");
            return r;
        }

        /// <summary>Blue passes, then Red's own stroke leaves it stuck in wicket 1.</summary>
        static Hoop RedStuckInTheJaws(Game g)
        {
            var h = First(g);

            Pass(g);
            Assert.Equal(1, g.Striker);

            g.World.Balls[1].Pos = new Vec2(h.Center.X - 0.30, h.Center.Y);
            var r = g.Play(new Vec2(1, 0), 0.74);

            Assert.True(r.TurnEnded);
            Assert.Equal(g.World.Field.HoopFor(0), g.World.JawsOf(1));
            return h;
        }

        /// <summary>The striker lined up a metre short of wicket 1, straight at Red.</summary>
        static StrokeResult RoquetRed(Game g, Hoop h)
        {
            g.World.Balls[g.Striker].Pos = new Vec2(h.Center.X - 1.0, h.Center.Y);
            return g.Play(new Vec2(1, 0), 2.0);
        }

        [Fact]
        public void The_rulebooks_example_Black_roquets_wicketed_Red_and_Blue_loses_its_turn()
        {
            var g = NewGame(4, Sides, Option(true));
            var h = RedStuckInTheJaws(g);

            Assert.Equal(1, g.Wicketed);
            Assert.Equal(2, g.Striker);

            g.World.Balls[2].Pos = new Vec2(h.Center.X - 1.0, h.Center.Y);
            var before = new Vec2[4];
            for (int i = 0; i < 4; i++) before[i] = g.World.Balls[i].Pos;

            var r = g.Play(new Vec2(1, 0), 2.0);

            // Red and Black are replaced...
            Assert.Equal(1, r.WicketedFoul);
            for (int i = 0; i < 4; i++) Assert.Equal(before[i], g.World.Balls[i].Pos);

            // ...and nothing the stroke did counts: no roquet, no deadness, and
            // Red was not scored through the wicket it was knocked out of.
            Assert.Equal(-1, r.Roqueted);
            Assert.Empty(g.States[2].Dead);
            Assert.Empty(r.OthersScored);
            Assert.Equal(0, g.States[1].Point);
            Assert.True(r.TurnEnded);
            Assert.Equal(0, r.ShotsLeft);

            // ...and then Yellow plays, Blue loses its turn, and then Red plays.
            Assert.Equal(3, g.Striker);
            Assert.Equal(0, g.LosesTurn);

            var yellow = Pass(g);
            Assert.Equal(0, yellow.TurnLost);
            Assert.Equal(1, g.Striker);
            Assert.Equal(-1, g.LosesTurn);
        }

        [Fact]
        public void Cannoning_a_wicketed_ball_with_another_ball_is_no_foul()
        {
            var g = NewGame(4, Sides, Option(true));
            var h = RedStuckInTheJaws(g);
            var redAt = g.World.Balls[1].Pos;

            // Black roquets Yellow, and Yellow runs on into Red.
            g.World.Balls[3].Pos = new Vec2(h.Center.X - 0.5, h.Center.Y);
            g.World.Balls[2].Pos = new Vec2(h.Center.X - 1.2, h.Center.Y);
            var r = g.Play(new Vec2(1, 0), 2.0);

            Assert.Equal(3, r.Roqueted);
            Assert.Equal(-1, r.WicketedFoul);
            Assert.False(r.TurnEnded);
            Assert.Equal(-1, g.LosesTurn);

            // Red was knocked, so it is no longer the ball its owner left there.
            Assert.NotEqual(redAt, g.World.Balls[1].Pos);
            Assert.Equal(-1, g.Wicketed);
        }

        [Fact]
        public void The_protection_lasts_only_for_the_next_players_turn()
        {
            var g = NewGame(4, Sides, Option(true));
            var h = RedStuckInTheJaws(g);

            Pass(g);                          // Black leaves it alone
            Assert.Equal(-1, g.Wicketed);
            Pass(g);                          // Yellow

            Assert.Equal(0, g.Striker);       // and Blue may roquet it
            var r = RoquetRed(g, h);

            Assert.Equal(1, r.Roqueted);
            Assert.Equal(-1, r.WicketedFoul);
            Assert.False(r.TurnEnded);
        }

        [Fact]
        public void Without_the_option_a_wicketed_ball_is_an_ordinary_roquet()
        {
            var g = NewGame(4, Sides, Option(false));
            var h = RedStuckInTheJaws(g);

            Assert.Equal(-1, g.Wicketed);
            var r = RoquetRed(g, h);

            Assert.Equal(1, r.Roqueted);
            Assert.Equal(-1, r.WicketedFoul);
            Assert.False(r.TurnEnded);
        }

        [Fact]
        public void Every_ball_for_itself_the_offender_loses_its_own_next_turn()
        {
            var g = NewGame(3, null, Option(true));
            var h = RedStuckInTheJaws(g);

            var r = RoquetRed(g, h);
            Assert.Equal(1, r.WicketedFoul);
            Assert.Equal(2, g.LosesTurn);
            Assert.Equal(0, g.Striker);

            Assert.Equal(-1, Pass(g).TurnLost);   // Blue plays
            Assert.Equal(1, g.Striker);

            Assert.Equal(2, Pass(g).TurnLost);    // Red plays; Black's turn is passed over
            Assert.Equal(0, g.Striker);
        }

        [Fact]
        public void A_clone_carries_the_protection_and_the_lost_turn()
        {
            var g = NewGame(4, Sides, Option(true));
            var h = RedStuckInTheJaws(g);

            var protectedCopy = g.Clone();
            Assert.Equal(1, protectedCopy.Wicketed);
            Assert.Equal(Checksum.Of(g), Checksum.Of(protectedCopy));

            RoquetRed(g, h);

            var owing = g.Clone();
            Assert.Equal(0, owing.LosesTurn);
            Assert.Equal(Checksum.Of(g), Checksum.Of(owing));

            Assert.Equal(0, Pass(owing).TurnLost);
            Assert.Equal(1, owing.Striker);
        }

        [Fact]
        public void The_bot_does_not_aim_at_a_protected_ball()
        {
            var g = NewGame(4, Sides, Option(true));
            RedStuckInTheJaws(g);

            Assert.True(g.IsProtected(1));
            var r = Bot.Casual(seed: 3).PlayStroke(g);

            Assert.Equal(-1, r.WicketedFoul);
        }
    }
}
