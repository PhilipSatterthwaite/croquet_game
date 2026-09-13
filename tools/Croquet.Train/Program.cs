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

        // --against default asks the question that decides ADOPTION: does this
        // beat what the game ships today? Beating the hand-tuned originals only
        // says it is not worse than a guess, which the shipped set already
        // cleared by 73% -- a retrain that drifted backwards would pass that
        // test comfortably.
        bool vsDefault = Arg("against", "") == "default";
        var baseline = vsDefault ? BotWeights.Default : BotWeights.HandTuned;

        Console.WriteLine($"{Path.GetFileName(path)} vs " +
                          (vsDefault ? "the shipped default" : "the hand-tuned original") +
                          $", {games} games\n");

        var clock = Stopwatch.StartNew();
        var r = Duel.Series(learned, baseline, games, 500_000);
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

        double blend = Real("blend", 1.0);
        Positions.Teams = Num("teams", 2);

        Console.WriteLine($"{Path.GetFileName(netPath)} vs the learned weights, " +
                          $"{games} games");
        Console.WriteLine(blend > 0
            ? $"side A is the weights PLUS {blend:0.##} hoops a point of net\n"
            : "side A is the net alone\n");

        var watch = Stopwatch.StartNew();
        // The same shape the net was trained on: six balls, every one for
        // itself. Testing it on a partnership game would be asking it about a
        // game it has never seen.
        var outcome = Duel.Series(BotWeights.Default, BotWeights.Default, games,
                                  500_000, Positions.Balls, default, null, net,
                                  solo: Positions.Teams < 2, blend: blend);
        watch.Stop();

        Console.WriteLine($"{outcome}   in {watch.Elapsed.TotalSeconds:0}s");
        Console.WriteLine(Wilson(outcome));
        break;
    }

    // Generations of self-play: collect, learn, measure, promote, repeat.
    //
    // This is the piece that was missing, and without it the whole approach is
    // capped. A net learned from games the LINEAR WEIGHTS played can at best
    // predict how well those weights do -- it is being taught their judgement,
    // and imitating a teacher does not beat the teacher. It only exceeds them
    // by playing its own games and learning from those, so that what it is
    // fitting is the value of ITS play, which then improves the play, which
    // then improves the value. That loop is the entire idea.
    //
    // Each generation is gated on beating the weights over real games, and only
    // a generation that does is promoted to weights/net.txt. A generation that
    // does not is kept under its own name and the next one starts from the last
    // net that did, so a bad round costs time rather than progress.
    case "cycle":
    {
        int generations = Num("generations", 4);
        int games = Num("games", 1500);
        int keep = Math.Max(1, Num("keep", 2));
        int judged = Num("judge", 200);

        // What the reward is made of, and how varied the games that produce it
        // are. All four are the levers worth turning between runs.
        Positions.Teams = Num("teams", 2);
        Positions.WinBonus = Real("win", 2.0);
        Positions.Scatter = Real("scatter", 0.5);
        Positions.AgainstWeights = Real("spar", 0.5);
        double explore = Real("explore", 0.2);
        double blend = Real("blend", 1.0);

        // Which kind of label this run produces. Rollouts measure every
        // candidate from one position under matched conditions and keep the
        // DIFFERENCE; the default measures one return per position and keeps
        // that. See Rollouts for why the difference is the whole game.
        bool ranking = Array.IndexOf(args, "--rollouts") >= 0;
        Rollouts.Candidates = Num("candidates", 8);
        Rollouts.Horizon = Num("horizon", 12);
        Rollouts.RootEvery = Num("every", 20);
        Rollouts.Steady = Arg("wobbly", "") == "";

        // Both namable, because they were not and a smoke run wrote its
        // sixteen-game files over an overnight collection under the same fixed
        // names. Per-generation nets now land beside whatever --out names
        // rather than always in weights/, so a throwaway run is throwaway all
        // the way through.
        string dataDir = Arg("data", Path.Combine(Root(), "data"));
        string bestPath = Arg("out", Path.Combine(Root(), "weights", "net.txt"));
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(bestPath))!);

        var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        // Two different nets, and conflating them stalls the whole loop.
        //
        // `latest` is what PLAYS the next generation's games, and it is always
        // the newest one trained, beaten or not. `best` is what SHIPS, and only
        // a generation that won its match becomes it.
        //
        // Playing with the best rather than the latest sounds safer and is a
        // trap: the first net is unlikely to beat weights that took 34,000
        // games to tune, so nothing is ever promoted, every generation collects
        // with the linear bot again, and the run is an expensive way to imitate
        // the teacher four times. Self-play has to be allowed to be worse than
        // the baseline on its way past it.
        var best = File.Exists(bestPath) ? Net.FromText(File.ReadAllText(bestPath)) : null;
        var latest = best;
        double bestRate = 0, bestAt = 0;

        Console.WriteLine($"{generations} generations, {games} games each, " +
                          $"learning from the last {keep}");
        Console.WriteLine($"{Sight.Size} inputs, {Net.Hidden} hidden, " +
                          $"{Net.Weights:N0} weights");
        Console.WriteLine(Positions.Teams >= 2
            ? $"{Positions.Teams} sides of {Positions.Balls / Positions.Teams}; " +
              "reward is my side's points less theirs"
            : "cutthroat; reward is my points less the average of the rest");
        Console.WriteLine($"winning adds {Positions.WinBonus:0.#} -- " +
                          $"{Positions.Scatter * 100:0}% of games start scattered, " +
                          $"{explore * 100:0}% of strokes explore, " +
                          $"{Positions.AgainstWeights * 100:0}% spar the weights");
        Console.WriteLine(ranking
            ? $"ROLLOUTS: {Rollouts.Candidates} candidates a root, "
            + $"{Rollouts.Horizon} strokes each, a root every {Rollouts.RootEvery}"
            + $" -- centred advantages, {(Rollouts.Steady ? "steady" : "shaky")} hands"
            : "labels are discounted returns");
        Console.WriteLine(best == null
            ? "starting from the linear weights -- generation 1 learns from their games\n"
            : $"starting from {Path.GetFileName(bestPath)}\n");

        var whole = Stopwatch.StartNew();

        for (int g = 1; g <= generations && !stop.IsCancellationRequested; g++)
        {
            Console.WriteLine($"=== generation {g}/{generations} " +
                              $"({whole.Elapsed.TotalMinutes:0}m in) ===");

            // ---- play ----
            var pattern = Bot.Casual();
            pattern.Net = latest;                   // null plays the linear weights
            pattern.NetBlend = blend;               // mixed with them, not replacing
            pattern.Explore = explore;              // and it tries things

            string stem = ranking ? "advantage" : "positions";
            string data = Path.Combine(dataDir, $"{stem}-gen{g}.bin");
            var clock = Stopwatch.StartNew();

            void Tick(int done, int of)
            {
                if (done % 8 != 0 && done != of) return;
                var left = TimeSpan.FromSeconds(
                    clock.Elapsed.TotalSeconds / done * (of - done));
                Console.Write($"\r  playing {done}/{of}   ~{left.TotalMinutes:0}m left    ");
            }

            int kept = ranking
                ? Rollouts.Collect(data, games, 7_000 + g * 100_000, latest,
                                   stop.Token, Tick)
                : Positions.Collect(data, games, 7_000 + g * 100_000, pattern,
                                    stop.Token, Tick);

            if (stop.IsCancellationRequested) { Console.WriteLine(); break; }
            Console.WriteLine($"\r  {kept:N0} positions in " +
                              $"{clock.Elapsed.TotalMinutes:0.0}m                    ");

            // ---- learn ----
            var batch = new List<string>();
            for (int back = 0; back < keep; back++)
                if (g - back >= 1)
                    batch.Add(Path.Combine(dataDir, $"{stem}-gen{g - back}.bin"));

            var (px, py) = Positions.LoadMany(batch);

            // Three, not ten. Held-back error was best at epoch ONE in the last
            // run and rose every epoch after it -- nine tenths of the training
            // time was spent memorising games. The positions are far more
            // correlated than their count suggests: six viewpoints of the same
            // lawn, and every fifth stroke of the same game.
            var fit = new Learn
            {
                Epochs = Num("epochs", 3),
                Batch = Num("batch", 256),
                Rate = Real("rate", 0.002)
            };

            // From the best net so far rather than from scratch: a generation
            // is meant to be an improvement on the last one, and throwing the
            // weights away each round spends most of every run relearning what
            // was already known.
            var trained = fit.Fit(latest ?? Net.Fresh(g), px, py, stop.Token,
                                  s => Console.WriteLine("  " + s));

            string genPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(bestPath))!,
                Path.GetFileNameWithoutExtension(bestPath) + $"-gen{g}.txt");
            File.WriteAllText(genPath, trained.ToText());
            latest = trained;

            if (stop.IsCancellationRequested) break;

            // ---- and does it actually play better ----
            Console.WriteLine($"\n  {judged} games against the linear weights...");

            // Judged at the same shape it was trained on. A net taught to play
            // for a partner, measured in a free-for-all, is being asked about a
            // game it has never seen -- and would look bad for a reason that is
            // nothing to do with how well it learned.
            //
            // Swept over how loudly the network speaks, because that is one
            // free number and there is no way to reason it out: too quiet and
            // it changes nothing, too loud and its noise drowns weights that
            // took 34,000 games to tune. Measured on the same seeds each time,
            // so the comparison is between blends and not between draws.
            // Roughly 6 minutes a rung against a couple of hours of collection.
            // Zero is in the ladder for a ranking run, because an advantage
            // net is BUILT to choose between strokes on its own -- that is the
            // whole object -- and the sweep is how we find out whether it can
            // yet. A returns net never could, and starts quiet.
            // Zero -- the net alone -- is NOT in the ranking ladder, and the
            // first run is why. It went 0 from 200 there, and that was a fault
            // rather than weak learning: a centred advantage has no absolute
            // scale, so comparing one against another from a DIFFERENT root,
            // which is what deepening does, compares two numbers measured from
            // two different zeroes. Until the search knows that a centred
            // label is reward-like and must be added rather than replace, the
            // net is only meaningful alongside something that does have a
            // scale.
            var ladder = Arg("blend", "") != "" ? new[] { blend }
                       : ranking                ? new[] { 0.5, 1.5, 4.0 }
                                                : new[] { 0.15, 0.35, 0.8 };

            Duel.Result outcome = default;
            double rate = 0, chosen = ladder[0];

            foreach (double b in ladder)
            {
                if (stop.IsCancellationRequested) break;

                var round = Duel.Series(BotWeights.Default, BotWeights.Default, judged,
                                        900_000 + g * 10_000, Positions.Balls,
                                        stop.Token, null, trained,
                                        solo: Positions.Teams < 2, blend: b);

                Console.WriteLine($"  blend {b,4:0.##}: {round}");
                if (round.Rate > rate) { rate = round.Rate; chosen = b; outcome = round; }
            }

            Console.WriteLine($"  generation {g} at its best blend " +
                              $"({chosen:0.##}): {outcome}");
            Console.WriteLine("  " + Wilson(outcome));

            if (rate > bestRate && rate > 0.5)
            {
                bestRate = rate;
                bestAt = chosen;
                best = trained;
                File.WriteAllText(bestPath, trained.ToText());

                // The blend is half of what was promoted -- a net without the
                // number saying how loudly to read it is not a usable bot.
                File.WriteAllText(Path.ChangeExtension(bestPath, ".blend"),
                                  chosen.ToString("0.###") + "\n");

                Console.WriteLine($"  promoted -> {bestPath} at blend {chosen:0.##}\n");
            }
            else
            {
                Console.WriteLine($"  kept as {Path.GetFileName(genPath)}; " +
                                  "the next generation starts from the last good one\n");
            }
        }

        Console.WriteLine(bestRate > 0
            ? $"best generation played {bestRate * 100:0.0}% against the weights "
            + $"at blend {bestAt:0.##} -- {bestPath}"
            : "no generation beat the linear weights; nothing promoted");
        Console.WriteLine($"total {whole.Elapsed.TotalHours:0.0}h");
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
              cycle --generations N     play, learn, measure, promote, repeat
              cycle --rollouts          ...measuring every candidate instead
              collect --games N         self-play positions, labelled
              learn --epochs N          fit the value net to them
              netmatch [file]           the net against the learned weights
              match [file] --games N    a weight set against the baseline
              train --rounds N --games N --children N --step F --from F --out F

            `cycle` is the one to run. The other three are its steps, kept
            separately for when one of them needs looking at on its own.

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
