using Croquet.Core;

namespace Croquet.Train;

/// <summary>
/// Self-play games, kept as positions with the answer written on them.
///
/// One sample is: what the lawn looked like from one ball's point of view, and
/// how well that ball did OVER THE NEXT TURN OR TWO -- points it gained against
/// what its opponents gained, discounted so the next stroke counts for most and
/// ten strokes away barely counts at all.
///
/// It was "did that ball's side win the game", and that was the whole reason
/// the first net was useless. One stroke does not move the odds of winning a
/// two hundred stroke game, so every candidate in a turn carried nearly the
/// same label; the network learned to score them all alike, and was accurate
/// about the game while blind to the stroke. A weird stroke can be worth a
/// great deal for a turn, and a turn is the scale at which the difference
/// between two strokes is visible at all.
///
/// Winning still dominates -- a win inside the horizon enters at
/// <see cref="Net.Won"/>, discounted like everything else -- so the network
/// learns that finishing beats any run of hoops. It simply no longer has to
/// infer that through two hundred strokes of noise.
///
/// The LABEL comes from the rules rather than from any evaluator's opinion,
/// which is what lets the network end up better than the bot whose games it
/// learned from.
/// </summary>
public static class Positions
{
    /// <summary>
    /// Six balls, every one for itself, which is how the game is played by
    /// default. Partnerships come later and will want their own training: what
    /// a ball should do for a partner is a different question, and mixing the
    /// two would teach it neither.
    /// </summary>
    public const int Balls = 6;

    /// <summary>Every nth stroke is kept. Consecutive positions are nearly the
    /// same position, and a hundred copies of one lawn is one example.</summary>
    const int Every = 5;

    /// <summary>
    /// How much a point one stroke further off is worth against one now.
    ///
    /// 0.88 puts half the weight inside six strokes and almost none past
    /// twenty -- about a turn or two, which is the horizon over which a stroke
    /// can be said to have worked.
    /// </summary>
    const double Fade = 0.88;

    public static CourtSpec Lawn() => new CourtSpec
    {
        Width = 30.48,
        Height = 15.24,
        Friction = 1.6,
        Restitution = 0.8,
        ObstacleRestitution = 0.5
    };

    /// <summary>
    /// Plays games and writes what it saw. Returns how many samples were kept.
    ///
    /// Every position is taken from ALL SIX points of view, which costs nothing
    /// and teaches the network the thing it most needs to know: that the game
    /// looks the same whoever is playing and only the labels differ. Without
    /// it, what it learns as one ball has to be learned again as the next.
    /// </summary>
    public static int Collect(string path, int games, int fromSeed, Bot pattern,
                              CancellationToken quit, Action<int, int> progress)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var write = new BinaryWriter(file);

        // Stamped with the encoding it was written for. Without this a file
        // written by one version is read by the next as though the records were
        // a different length -- and nothing complains. What comes out is the
        // tail of one position spliced to the head of the next, with some
        // arbitrary number in the label slot, and it TRAINS: the misalignment
        // is a deterministic pattern and a network fits it happily. A whole
        // night went that way, and the result looked good.
        write.Write(Mark);
        write.Write(Sight.Size);

        var pen = new object();
        int kept = 0, done = 0;

        Parallel.For(0, games, new ParallelOptions { CancellationToken = quit }, g =>
        {
            var seen = new List<(float[] X, int Who, int At)>();
            var ledger = new List<double[]>();

            if (PlayOne(fromSeed + g, pattern, seen, ledger, quit))
            {
                lock (pen)
                {
                    foreach (var (x, who, at) in seen)
                    {
                        foreach (float v in x) write.Write(v);
                        write.Write((float)Ahead(ledger, at, who));
                        kept++;
                    }
                }
            }

            progress?.Invoke(Interlocked.Increment(ref done), games);
        });

        return kept;
    }

    /// <summary>
    /// What one ball gains from a position, discounted into the future.
    ///
    /// Its own points less the AVERAGE of its opponents', not their total: with
    /// six players a point to one of five rivals is a fifth of the harm that a
    /// point to your only rival would be, and scoring it as the full amount
    /// would teach a bot that everything is hopeless.
    /// </summary>
    static double Ahead(List<double[]> ledger, int at, int who)
    {
        double sum = 0, weight = 1;

        for (int i = at; i < ledger.Count; i++)
        {
            var gains = ledger[i];

            double others = 0;
            for (int b = 0; b < gains.Length; b++) if (b != who) others += gains[b];

            sum += weight * (gains[who] - others / Math.Max(1, gains.Length - 1));

            weight *= Fade;
            if (weight < 0.01) break;          // past here it cannot matter
        }

        return sum;
    }

    /// <summary>
    /// One game. Fills <paramref name="seen"/> with positions and
    /// <paramref name="ledger"/> with what every ball gained on every stroke.
    ///
    /// Unfinished games are WELCOME now, where before they had to be thrown
    /// away whole. The label only reaches a turn or two ahead, so a game cut
    /// off at six hundred strokes is a hundred and twenty perfectly good
    /// samples and a handful at the end whose future is short -- against the
    /// old label, the eventual winner, which for an unfinished game did not
    /// exist at all.
    /// </summary>
    static bool PlayOne(int seed, Bot pattern, List<(float[], int, int)> seen,
                        List<double[]> ledger, CancellationToken quit)
    {
        var arr = new Ball[Balls];
        for (int i = 0; i < Balls; i++) arr[i] = new Ball(Vec2.Zero);

        // Null sides is cutthroat: every ball its own side, which is what
        // Bot.SameSide reads it as.
        var game = new Game(new World(arr, Field.NineWicket(), Lawn()), null,
                            RuleOptions.Basic);

        for (int i = 0; i < Balls; i++)
        {
            game.States[i].Started = true;
            game.World.Balls[i].InPlay = true;
            game.World.Balls[i].Pos = game.World.Field.StartSpot
                                    + new Vec2(0, (i - (Balls - 1) / 2.0) * 0.35);
        }

        var bots = new Bot[Balls];
        for (int i = 0; i < Balls; i++)
        {
            bots[i] = new Bot(seed * 131 + i * 17)
            {
                Lookahead = pattern.Lookahead,
                SweepAngles = pattern.SweepAngles,
                PlacementAngles = pattern.PlacementAngles,
                Deepen = pattern.Deepen,
                FreePlies = pattern.FreePlies,
                RiskChecks = pattern.RiskChecks,
                AimError = pattern.AimError,
                PowerError = pattern.PowerError,
                RangeError = pattern.RangeError,
                Weights = pattern.Weights,
                Net = pattern.Net
            };
        }

        var buffer = Sight.Buffer();
        var before = new double[Balls];
        int strokes = 0;

        while (game.Winner == null && strokes < 600)
        {
            if (quit.IsCancellationRequested) return false;

            for (int i = 0; i < Balls; i++) before[i] = game.States[i].Point;

            try { bots[game.Striker].PlayStroke(game); }
            catch (InvalidOperationException) { break; }
            strokes++;

            // What each ball gained. Read per ball rather than from the striker
            // because a point can be scored FOR a ball by somebody else driving
            // it through its own hoop, and that counts for it.
            var gains = new double[Balls];
            for (int i = 0; i < Balls; i++) gains[i] = game.States[i].Point - before[i];

            // And the game ending is the biggest gain there is.
            if (game.Winner != null)
                foreach (int w in game.Winner) gains[w] += Net.Won;

            ledger.Add(gains);

            if (strokes % Every != 0) continue;

            for (int who = 0; who < Balls; who++)
            {
                Sight.Read(game, who, buffer);
                var x = new float[Sight.Size];
                for (int i = 0; i < Sight.Size; i++) x[i] = (float)buffer[i];
                seen.Add((x, who, strokes));
            }
        }

        return seen.Count > 0;
    }

    /// <summary>Four bytes saying this is a croquet position file.</summary>
    const int Mark = 0x43524F51;          // "CROQ"

    const int Header = 2 * sizeof(int);

    /// <summary>How many samples a file holds, or -1 if it is not one of ours.</summary>
    public static int Count(string path)
    {
        if (!File.Exists(path)) return -1;

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var read = new BinaryReader(file);

        if (file.Length < Header) return -1;
        if (read.ReadInt32() != Mark) return -1;
        if (read.ReadInt32() != Sight.Size) return -1;

        long body = file.Length - Header;
        long record = (Sight.Size + 1) * sizeof(float);

        return body % record == 0 ? (int)(body / record) : -1;
    }

    /// <summary>
    /// Why a file cannot be used, or null if it can.
    ///
    /// Said out loud rather than guessed at, because the failure this guards
    /// against is silent by nature: a file of the wrong shape reads as numbers
    /// either way, and only the results are wrong.
    /// </summary>
    public static string Trouble(string path)
    {
        if (!File.Exists(path)) return "there is no file at " + path;

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var read = new BinaryReader(file);

        if (file.Length < Header) return "the file is too short to be positions";
        if (read.ReadInt32() != Mark) return "that is not a positions file";

        int wrote = read.ReadInt32();
        if (wrote != Sight.Size)
            return $"those positions were written for an encoding of {wrote} numbers "
                 + $"and this build reads {Sight.Size} -- collect them again";

        long body = file.Length - Header;
        long record = (Sight.Size + 1) * sizeof(float);
        if (body % record != 0) return "the file ends part way through a position";

        return null;
    }

    /// <summary>Reads the lot into memory. A million samples is 230 MB.</summary>
    public static (float[] X, float[] Y) Load(string path)
    {
        string wrong = Trouble(path);
        if (wrong != null) throw new InvalidDataException(wrong);

        int n = Count(path);
        var x = new float[(long)n * Sight.Size];
        var y = new float[n];

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var read = new BinaryReader(file);
        read.ReadInt32();
        read.ReadInt32();

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < Sight.Size; j++) x[(long)i * Sight.Size + j] = read.ReadSingle();
            y[i] = read.ReadSingle();
        }

        return (x, y);
    }
}
