using System;
using System.IO;
using System.Linq;
using Croquet.Core;
using Xunit;
using Xunit.Abstractions;

namespace Croquet.Core.Tests
{
    /// <summary>
    /// Can the net tell one stroke from another?
    ///
    /// Predicting a WINNER well and choosing a STROKE well are different jobs,
    /// and a net can be good at the first and useless at the second. Within one
    /// turn every candidate leaves nearly the same position -- same points, same
    /// balls, a metre here or there -- so if the net keys mostly on who is ahead
    /// it will score them all alike, and the search picking the largest of a
    /// thousand nearly equal numbers is picking at random.
    /// </summary>
    public class NetTests
    {
        readonly ITestOutputHelper log;
        public NetTests(ITestOutputHelper log) { this.log = log; }

        static string Root()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Croquet.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? ".";
        }

        [Fact]
        public void How_much_does_the_net_separate_strokes()
        {
            string path = Path.Combine(Root(), "weights", "net.txt");
            if (!File.Exists(path))
            {
                log.WriteLine("no net trained yet -- nothing to measure");
                return;
            }

            var net = Net.FromText(File.ReadAllText(path));
            if (net == null)
            {
                log.WriteLine("the net on disk does not fit this encoding");
                return;
            }

            var spec = new CourtSpec
            {
                Width = 30.48, Height = 15.24, Friction = 1.6,
                Restitution = 0.8, ObstacleRestitution = 0.5
            };

            // The SAME shape it was trained on: six balls, every one for
            // itself. Measured on a four-ball partnership game -- which this
            // was, until the training moved -- it is being asked about a game
            // it has never seen, and a bad answer says nothing.
            const int balls = 6;
            var arr = new Ball[balls];
            for (int i = 0; i < balls; i++) arr[i] = new Ball(Vec2.Zero);

            var game = new Game(new World(arr, Field.NineWicket(), spec), null,
                                RuleOptions.Basic);
            for (int i = 0; i < balls; i++)
            {
                game.States[i].Started = true;
                game.World.Balls[i].InPlay = true;
                game.World.Balls[i].Pos = game.World.Field.StartSpot
                                        + new Vec2(i * 0.7, (i - 2.5) * 0.5);
            }

            // A spread of strokes from one position: every direction, three
            // strengths. This is the set the search is choosing among.
            var values = new System.Collections.Generic.List<double>();
            var rng = new Random(4);

            for (int a = 0; a < 48; a++)
                foreach (double roll in new[] { 1.5, 6.0, 16.0 })
                {
                    double angle = a * Math.PI * 2 / 48;
                    var clone = game.Clone();
                    try
                    {
                        clone.Play(new Vec2(Math.Cos(angle), Math.Sin(angle)),
                                   Math.Sqrt(2 * spec.Friction * roll));
                    }
                    catch (InvalidOperationException) { continue; }

                    values.Add(net.Value(clone, 0));
                }

            values.Sort();
            double lo = values[0], hi = values[values.Count - 1];
            double mean = values.Average();
            double sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);

            log.WriteLine($"{values.Count} strokes from one position");
            log.WriteLine($"  net value: lowest {lo:0.0000}, highest {hi:0.0000}");
            log.WriteLine($"  spread {hi - lo:0.0000}, standard deviation {sd:0.0000}");

            // And the same set through the linear evaluator, for scale.
            var scores = new System.Collections.Generic.List<double>();
            for (int a = 0; a < 48; a++)
                foreach (double roll in new[] { 1.5, 6.0, 16.0 })
                {
                    double angle = a * Math.PI * 2 / 48;
                    var clone = game.Clone();
                    StrokeResult r;
                    try
                    {
                        r = clone.Play(new Vec2(Math.Cos(angle), Math.Sin(angle)),
                                       Math.Sqrt(2 * spec.Friction * roll));
                    }
                    catch (InvalidOperationException) { continue; }

                    scores.Add(Bot.Evaluate(game, clone, r, 0, BotWeights.Default));
                }

            double slo = scores.Min(), shi = scores.Max(), smean = scores.Average();
            double ssd = Math.Sqrt(scores.Sum(v => (v - smean) * (v - smean)) / scores.Count);

            log.WriteLine($"  weights:   lowest {slo:0.0}, highest {shi:0.0}");
            log.WriteLine($"  spread {shi - slo:0.0}, standard deviation {ssd:0.0}");
            log.WriteLine($"  relative spread -- net {(hi - lo) / Math.Max(1e-9, mean):0.000}"
                        + $", weights {(shi - slo) / Math.Max(1e-9, Math.Abs(smean)):0.000}");

            // The bar a judge has to clear to be usable for CHOOSING. Below
            // about a hundredth of spread the search is picking the largest of
            // a set of numbers that are all the same, which is picking at
            // random -- and random play loses every game, which is exactly what
            // the first trained net did.
            Assert.True(hi - lo > 0.5,
                $"the net separates 144 very different strokes by only {hi - lo:0.000} points; " +
                "it cannot be used to choose between them");
        }
    }
}
