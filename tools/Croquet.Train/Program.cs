using System.Diagnostics;
using Croquet.Core;
using Croquet.Train;

// Croquet.Train -- learns what the bot should be trying to achieve, by playing
// weight sets against each other and keeping whichever wins more games.
//
//   dotnet run --project tools/Croquet.Train -- time
//   dotnet run --project tools/Croquet.Train -- match weights/trained.txt
//   dotnet run --project tools/Croquet.Train -- train --rounds 40 --games 60

string command = args.Length > 0 ? args[0] : "help";

string Arg(string name, string fallback)
{
    int at = Array.IndexOf(args, "--" + name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
}

int Num(string name, int fallback) =>
    int.TryParse(Arg(name, ""), out int v) ? v : fallback;

double Real(string name, double fallback) =>
    double.TryParse(Arg(name, ""), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v)
        ? v : fallback;

string Root()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Croquet.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

BotWeights Load(string path) =>
    File.Exists(path) ? BotWeights.FromText(File.ReadAllText(path)) : BotWeights.Default;

switch (command)
{
    // How long a game takes, which decides how big a training run can be.
    case "time":
    {
        var clock = Stopwatch.StartNew();
        var r = Duel.Series(BotWeights.Default, BotWeights.Default, 8, 1);
        clock.Stop();

        Console.WriteLine($"{r.Played} games in {clock.Elapsed.TotalSeconds:0.0}s " +
                          $"-- {clock.Elapsed.TotalSeconds / r.Played:0.00}s a game " +
                          $"on {Environment.ProcessorCount} cores");
        Console.WriteLine($"self-play, identical weights: {r}   (should be near 50%)");
        break;
    }

    // A trained set against the hand-tuned baseline. The only claim worth
    // making about a training run.
    case "match":
    {
        string path = args.Length > 1 && !args[1].StartsWith("--")
            ? args[1]
            : Path.Combine(Root(), "weights", "trained.txt");
        int games = Num("games", 200);

        // Against the HAND-TUNED set, not against Default. Once a learned set
        // is adopted as the default, measuring against the default asks whether
        // it beats itself -- and the answer is fifty per cent for ever, which
        // looks exactly like training having achieved nothing.
        var learned = Load(path);
        Console.WriteLine($"{Path.GetFileName(path)} vs the hand-tuned original, " +
                          $"{games} games\n");

        var clock = Stopwatch.StartNew();
        var r = Duel.Series(learned, BotWeights.HandTuned, games, 500_000);
        clock.Stop();

        Console.WriteLine($"{r}   in {clock.Elapsed.TotalSeconds:0}s");
        Console.WriteLine(Wilson(r));
        break;
    }

    // Self-play games kept as positions with the winner written on them.
    case "collect":
    {
        string outPath = Arg("out", Path.Combine(Root(), "data", "positions.bin"));
        int games = Num("games", 2000);

        var pattern = Bot.Casual();
        string netPath = Arg("net", "");
        if (netPath != "" && File.Exists(netPath))
        {
            pattern.Net = Net.FromText(File.ReadAllText(netPath));
            Console.WriteLine(pattern.Net == null
                ? "that net does not fit this encoding -- collecting with the weights"
                : $"collecting with {Path.GetFileName(netPath)} playing");
        }

        var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        var watch = Stopwatch.StartNew();
        int kept = Positions.Collect(outPath, games, 7_000, pattern, stop.Token,
            (done, of) =>
            {
                if (done % 8 != 0 && done != of) return;
                double each = watch.Elapsed.TotalSeconds / done;
                var left = TimeSpan.FromSeconds(each * (of - done));
                Console.Write($"\r  {done}/{of} games   ~{left.TotalMinutes:0}m left   ");
            });

        Console.WriteLine($"\r{kept:N0} positions from {games} games " +
                          $"in {watch.Elapsed.TotalMinutes:0.0}m -> {outPath}          ");
        break;
    }

    // Fit the value net to them.
    case "learn":
    {
        string data = Arg("data", Path.Combine(Root(), "data", "positions.bin"));
        string outPath = Arg("out", Path.Combine(Root(), "weights", "net.txt"));

        string wrong = Positions.Trouble(data);
        if (wrong != null)
        {
            Console.WriteLine($"cannot learn from {data}:");
            Console.WriteLine("  " + wrong);
            Console.WriteLine("  run `collect` first.");
            break;
        }

        Console.WriteLine($"reading {data}");
        var (px, py) = Positions.Load(data);

        var start = File.Exists(outPath) && Arg("from", "") != "none"
            ? Net.FromText(File.ReadAllText(outPath)) ?? Net.Fresh(1)
            : Net.Fresh(1);

        var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        var fit = new Learn
        {
            Epochs = Num("epochs", 8),
            Batch = Num("batch", 256),
            Rate = Real("rate", 0.002)
        };

        var trained = fit.Fit(start, px, py, stop.Token, Console.WriteLine);

        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(outPath, trained.ToText());
        Console.WriteLine($"written to {outPath}");
        break;
    }

    // The net against the weights it was learned from. The only test that counts.
    case "netmatch":
    {
        string netPath = args.Length > 1 && !args[1].StartsWith("--")
            ? args[1] : Path.Combine(Root(), "weights", "net.txt");
        int games = Num("games", 200);

        var net = File.Exists(netPath) ? Net.FromText(File.ReadAllText(netPath)) : null;
        if (net == null)
        {
            Console.WriteLine($"no usable net at {netPath}");
            break;
        }

        Console.WriteLine($"{Path.GetFileName(netPath)} vs the learned weights, " +
                          $"{games} games\n");

        var watch = Stopwatch.StartNew();
        // The same shape the net was trained on: six balls, every one for
        // itself. Testing it on a partnership game would be asking it about a
        // game it has never seen.
        var outcome = Duel.Series(BotWeights.Default, BotWeights.Default, games,
                                  500_000, Positions.Balls, default, null, net,
                                  solo: true);
        watch.Stop();

        Console.WriteLine($"{outcome}   in {watch.Elapsed.TotalSeconds:0}s");
        Console.WriteLine(Wilson(outcome));
        break;
    }

    case "train":
    {
        var run = new Trainer
        {
            Rounds = Num("rounds", 30),
            Games = Num("games", 60),
            Children = Num("children", 6),
            Step = Real("step", 0.25),
            Balls = Num("balls", 4),
            Out = Arg("out", Path.Combine(Root(), "weights", "trained.txt"))
        };

        run.Start = Load(Arg("from", run.Out));

        // Ctrl+C asks the run to stop rather than killing it, so it can finish
        // what it is in the middle of, write the champion out and say what it
        // found. A second Ctrl+C is the impatient one and does kill it -- but
        // even then the champion is already on disk, because it is written the
        // moment it is accepted rather than at the end.
        Console.CancelKeyPress += (_, e) =>
        {
            if (run.Quitting) return;      // second press: let it die
            e.Cancel = true;
            Console.WriteLine("\n\nstopping at the end of this series -- " +
                              "press Ctrl+C again to quit at once\n");
            run.Stop();
        };

        run.Run();
        break;
    }

    default:
        Console.WriteLine("""
            Croquet.Train -- what the bot should be trying to achieve.

              time                      how long a self-play game takes
              collect --games N         self-play positions labelled by who won
              learn --epochs N          fit the value net to them
              netmatch [file]           the net against the learned weights
              match [file] --games N    a weight set against the baseline
              train --rounds N --games N --children N --step F --from F --out F

            Fitness is games won and nothing else. Both sides always get the
            same hand, so a match measures judgement rather than striking.
            """);
        break;
}

// How much of the win rate is real and how much is the coin landing that way.
// A 55% result over 40 games is noise; over 400 it is a finding.
static string Wilson(Duel.Result r)
{
    int n = r.Wins + r.Losses;
    if (n == 0) return "no decided games";

    double p = r.Wins / (double)n, z = 1.96;
    double mid = (p + z * z / (2 * n)) / (1 + z * z / n);
    double half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / (1 + z * z / n);

    string verdict = mid - half > 0.5 ? "better than the baseline"
                   : mid + half < 0.5 ? "WORSE than the baseline"
                   : "not distinguishable from the baseline yet";

    return $"95% confident it is between {(mid - half) * 100:0.0}% and " +
           $"{(mid + half) * 100:0.0}% -- {verdict}";
}
