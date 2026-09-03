using System;
using System.Collections.Generic;
using System.Linq;
using Croquet.Core;
using Xunit;
using Xunit.Abstractions;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// Not "does it obey the rules" -- "does it play like somebody sane". These
    /// are the complaints a person makes watching it: it dribbled the ball two
    /// inches for no reason, it went for a shot no one would take. Both are
    /// invisible to a rules test and obvious from the sofa.
    /// </summary>
    public class BotQualityTests
    {
        readonly ITestOutputHelper output;
        public BotQualityTests(ITestOutputHelper output) { this.output = output; }

        static Game NewGame(int balls = 4)
        {
            var arr = new Ball[balls];
            for (int i = 0; i < balls; i++) arr[i] = new Ball(Vec2.Zero);
            var g = new Game(new World(arr, Field.NineWicket(), new CourtSpec()),
                             null, new RuleOptions());
            for (int i = 0; i < balls; i++)
            {
                g.States[i].Started = true;
                g.World.Balls[i].InPlay = true;
                g.World.Balls[i].Pos = new Vec2(2.0 + i * 0.8, 14.0);
            }
            return g;
        }

        /// <summary>How far a stroke of this strength would roll, in metres.</summary>
        static double Run(double power, CourtSpec c) =>
            power * power / (2 * Math.Max(0.05, c.Friction));

        record Stroke(int Ball, string Level, double Power, double Run,
                      double ToTarget, string Note);

        List<Stroke> PlayOut(Bot bot, string level, int strokes)
        {
            var g = NewGame();
            var log = new List<Stroke>();
            var c = g.World.Spec;

            for (int n = 0; n < strokes && g.Winner == null; n++)
            {
                int me = g.Striker;
                var field = g.World.Field;
                int point = g.States[me].Point;
                double toTarget = field.IsFinished(point)
                    ? 0
                    : (field.TargetFor(point) - g.World.Balls[me].Pos).Length;

                var m = bot.Choose(g);
                log.Add(new Stroke(me, level, m.Power, Run(m.Power, c), toTarget, m.Note));
                try { Bot.Apply(g, m); }
                catch (InvalidOperationException) { break; }
            }
            return log;
        }

        // A stroke that rolls a few centimetres is a stroke thrown away. It
        // happens when every candidate looks bad and the evaluator would rather
        // preserve the position it has than risk a worse one -- which is a
        // reasonable-sounding rule that produces a player poking at the ball.
        [Fact]
        public void It_does_not_dribble_the_ball_for_no_reason()
        {
            var levels = new[]
            {
                ("beginner", Bot.Beginner()), ("casual", Bot.Casual()),
                ("steady", Bot.Steady()),     ("expert", Bot.Expert())
            };

            var limp = new List<Stroke>();
            int total = 0;

            foreach (var (name, bot) in levels)
            {
                var log = PlayOut(bot, name, 40);
                total += log.Count;
                // A stroke is limp if it rolls under a fifth of a metre while
                // its own target is a long way off. Tapping a ball through a
                // hoop it is sitting in front of is not limp, it is correct.
                limp.AddRange(log.Where(s => s.Run < 0.20 && s.ToTarget > 1.0));
            }

            foreach (var s in limp)
                output.WriteLine(
                    $"{s.Level,-9} ball {s.Ball}: rolled {s.Run:N2}m " +
                    $"with its point {s.ToTarget:N1}m away -- {s.Note}");

            output.WriteLine($"{limp.Count} limp strokes out of {total}");
            Assert.True(limp.Count * 20 <= total,
                $"{limp.Count} of {total} strokes went nowhere for no reason");
        }

        // The search plays every candidate perfectly, so a roquet across the
        // court always goes in and always looks better than a quiet positioning
        // shot. Then the wobble is applied and it misses. A player who knows
        // their own hand does not take that shot.
        [Fact]
        public void It_does_not_keep_taking_shots_its_own_hand_cannot_play()
        {
            var log = new List<Stroke>();
            foreach (var (name, bot) in new[]
                     { ("casual", Bot.Casual()), ("steady", Bot.Steady()) })
                log.AddRange(PlayOut(bot, name, 60));

            // A nine-metre roquet is an ordinary shot on a thirty-metre court,
            // so that is not the complaint. The complaint is strokes that cross
            // the whole court, which no position justifies: the ball ends up
            // against a boundary whatever it hits on the way.
            var heaves = log.Where(s => s.Run > 18.0).ToList();
            foreach (var s in heaves)
                output.WriteLine($"{s.Level,-7} ball {s.Ball}: {s.Run:N1}m -- {s.Note}");

            int longish = log.Count(s => s.Run > 9.0);
            output.WriteLine($"{heaves.Count} strokes over 18m, {longish} over 9m, " +
                             $"out of {log.Count}");

            Assert.True(heaves.Count * 12 <= log.Count,
                $"{heaves.Count} of {log.Count} strokes crossed the whole court");
        }

        /// <summary>
        /// A ball sitting square in front of its hoop, close enough that the
        /// line is not in doubt. Nobody misses this -- not a beginner, not a
        /// child. If a level misses it, the level is wrong, because difficulty
        /// belongs on the shots that are genuinely hard.
        /// </summary>
        static Game InFrontOfWicketOne(double back)
        {
            var arr = new Ball[2];
            for (int i = 0; i < 2; i++) arr[i] = new Ball(Vec2.Zero);
            var field = Field.NineWicket();
            var g = new Game(new World(arr, field, new CourtSpec()),
                             null, new RuleOptions());
            for (int i = 0; i < 2; i++)
            {
                g.States[i].Started = true;
                g.World.Balls[i].InPlay = true;
            }

            var t = field.TargetFor(g.States[0].Point);
            int dir = field.DirectionFor(g.States[0].Point);
            g.World.Balls[0].Pos = new Vec2(t.X - dir * back, t.Y);
            g.World.Balls[1].Pos = new Vec2(1.0, 1.0);       // out of the way
            return g;
        }

        [Fact]
        public void Every_level_runs_a_hoop_it_is_sitting_in_front_of()
        {
            var levels = new (string Name, Func<int, Bot> Make)[]
            {
                ("beginner", Bot.Beginner), ("casual", Bot.Casual),
                ("steady",   Bot.Steady),   ("expert", Bot.Expert)
            };

            foreach (var back in new[] { 0.3, 0.6, 1.0 })
                foreach (var (name, make) in levels)
                {
                    int made = 0;
                    const int tries = 12;

                    for (int t = 0; t < tries; t++)
                    {
                        // A fresh bot per attempt, seeded differently, so this
                        // is twelve independent hands rather than one sequence
                        // replayed -- the bot is deterministic, so without this
                        // all twelve attempts would be the same attempt.
                        var bot = make(1000 + t * 97);
                        var g = InFrontOfWicketOne(back);
                        int before = g.States[0].Point;
                        for (int s = 0; s < 3 && g.Striker == 0 && g.Winner == null; s++)
                            bot.PlayStroke(g);
                        if (g.States[0].Point > before) made++;
                    }

                    output.WriteLine($"{name,-9} from {back:N1}m: {made}/{tries}");
                    Assert.True(made >= tries - 1,
                        $"{name} missed a hoop from {back:N1}m {tries - made} times out of {tries}");
                }
        }
    }
}
