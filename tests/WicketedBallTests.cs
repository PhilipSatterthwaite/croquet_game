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
    ///   ball with another ball without penalty.
    ///
    /// Played here WITHOUT the lost turn, by choice: the balls are replaced and
    /// the turn ends, and play carries on in the ordinary order.
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
        static Game NewGame(RuleOptions options)
        {
            var arr = new Ball[4];
            for (int i = 0; i < 4; i++) arr[i] = new Ball(Vec2.Zero);

            var g = new Game(new World(arr, Field.NineWicket(), Lawn()), Sides, options);
            for (int i = 0; i < 4; i++)
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
        public void Roqueting_a_wicketed_ball_puts_the_balls_back_and_ends_the_turn()
        {
            var g = NewGame(Option(true));
            var h = RedStuckInTheJaws(g);

            Assert.Equal(1, g.Wicketed);
            Assert.Equal(2, g.Striker);

            g.World.Balls[2].Pos = new Vec2(h.Center.X - 1.0, h.Center.Y);
            var before = new Vec2[4];
            for (int i = 0; i < 4; i++) before[i] = g.World.Balls[i].Pos;

            var r = g.Play(new Vec2(1, 0), 2.0);

            // The balls are replaced...
            Assert.Equal(1, r.WicketedFoul);
            for (int i = 0; i < 4; i++) Assert.Equal(before[i], g.World.Balls[i].Pos);

            // ...nothing the stroke did counts: no roquet, no deadness, and Red
            // was not scored through the wicket it was knocked out of...
            Assert.Equal(-1, r.Roqueted);
            Assert.Empty(g.States[2].Dead);
            Assert.Empty(r.OthersScored);
            Assert.Equal(0, g.States[1].Point);

            // ...and the turn is over, with nobody's next turn taken away.
            Assert.True(r.TurnEnded);
            Assert.Equal(0, r.ShotsLeft);
            Assert.Equal(3, g.Striker);       // Yellow
            Pass(g);
            Assert.Equal(0, g.Striker);       // Blue, as ever
        }

        [Fact]
        public void Cannoning_a_wicketed_ball_with_another_ball_is_no_foul()
        {
            var g = NewGame(Option(true));
            var h = RedStuckInTheJaws(g);
            var redAt = g.World.Balls[1].Pos;

            // Black roquets Yellow, and Yellow runs on into Red.
            g.World.Balls[3].Pos = new Vec2(h.Center.X - 0.5, h.Center.Y);
            g.World.Balls[2].Pos = new Vec2(h.Center.X - 1.2, h.Center.Y);
            var r = g.Play(new Vec2(1, 0), 2.0);

            Assert.Equal(3, r.Roqueted);
            Assert.Equal(-1, r.WicketedFoul);
            Assert.False(r.TurnEnded);

            // Red was knocked, so it is no longer the ball its owner left there.
            Assert.NotEqual(redAt, g.World.Balls[1].Pos);
            Assert.Equal(-1, g.Wicketed);
        }

        [Fact]
        public void The_protection_lasts_only_for_the_next_players_turn()
        {
            var g = NewGame(Option(true));
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
            var g = NewGame(Option(false));
            var h = RedStuckInTheJaws(g);

            Assert.Equal(-1, g.Wicketed);
            var r = RoquetRed(g, h);

            Assert.Equal(1, r.Roqueted);
            Assert.Equal(-1, r.WicketedFoul);
            Assert.False(r.TurnEnded);
        }

        [Fact]
        public void A_clone_carries_the_protection()
        {
            var g = NewGame(Option(true));
            var h = RedStuckInTheJaws(g);

            var copy = g.Clone();
            Assert.Equal(1, copy.Wicketed);
            Assert.Equal(Checksum.Of(g), Checksum.Of(copy));

            // And the copy enforces it, not merely remembers it.
            Assert.Equal(1, RoquetRed(copy, h).WicketedFoul);
        }

        [Fact]
        public void A_replay_remembers_the_bridged_ball_after_the_turn_moves_on()
        {
            // The badge on a bridged ball must not vanish the moment the next
            // player strikes: ending that stroke's turn has already moved on.
            var g = NewGame(Option(true));
            RedStuckInTheJaws(g);

            var r = Replay.Play(g, new Vec2(-1, 0), 0.3);   // Black passes

            Assert.True(r.Result.TurnEnded);
            Assert.Equal(-1, g.Wicketed);
            Assert.Equal(1, r.WicketedBefore);
        }

        [Fact]
        public void The_bot_does_not_aim_at_a_protected_ball()
        {
            var g = NewGame(Option(true));
            RedStuckInTheJaws(g);

            Assert.True(g.IsProtected(1));
            var r = Bot.Casual(seed: 3).PlayStroke(g);

            Assert.Equal(-1, r.WicketedFoul);
        }
    }
}
