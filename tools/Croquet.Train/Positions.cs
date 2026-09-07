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
    public const int Balls = 6;

    /// <summary>
    /// Sides the six balls are split into. Two makes three a side.
    ///
    /// Zero is cutthroat, every ball for itself, which is what this trained on
    /// first. A partnership is a different game and not a variation on the same
    /// one: half the value of a stroke is what it does for a ball you are not
    /// playing, and none of that exists when every ball is a rival.
    /// </summary>
    public static int Teams = 2;

    /// <summary>
    /// What winning adds to the label, in points.
    ///
    /// Deliberately SMALL, and this is the opposite of an oversight. The label
    /// is meant to say what a stroke is worth over the next turn or two, and a
    /// win worth twelve points swamps everything a stroke can actually
    /// influence -- so the network spends its capacity on the handful of
    /// positions near the end of a game and learns little about the several
    /// hundred strokes that got there.
    ///
    /// This is NOT <see cref="Net.Won"/>, and the difference matters. That one
    /// is what the SEARCH is handed for a game already over, and it stays large
    /// so that a win always outranks any position the network could predict --
    /// a bot that declines to win because a break looks promising is the
    /// failure this guards against. Small in the label, large at the terminal:
    /// the network never learns to predict a number near twelve, so a real win
    /// beats every prediction it can make.
    /// </summary>
    public static double WinBonus = 2.0;

    /// <summary>
    /// Share of games that start from a scattered lawn rather than the opening.
    ///
    /// Every game from the same opening means the network sees the first few
    /// strokes of croquet a hundred thousand times and the middle of a close
    /// game rarely -- and the middle is where nearly every real decision is
    /// made. Scattering costs nothing and roughly doubles the variety of
    /// positions a run produces.
    ///
    /// The labels stay honest, because they are measured from what the balls
    /// actually go on to do from wherever they were put.
    /// </summary>
    public static double Scatter = 0.5;

    /// <summary>
    /// Share of games played against the linear weights rather than against
    /// more copies of the network.
    ///
    /// Pure self-play is the textbook answer and it went badly here, for a
    /// reason the numbers made plain: generation two averaged 706 kept
    /// positions a game against a ceiling of 720, meaning virtually every game
    /// ran to the six-hundred-stroke cap without anybody finishing. Six copies
    /// of a mediocre network flail, and a network learned from flailing learns
    /// what flailing is worth. The linear weights finish a game in about 230
    /// strokes.
    ///
    /// So half the games are played against them. Those games end, they contain
    /// breaks and hoops and real endgames, and they give the network positions
    /// from competent croquet to have an opinion about. The other half stay
    /// pure self-play, because that is where anything BETTER than the weights
    /// can come from -- a network that only ever sees their games can only ever
    /// learn their judgement.
    /// </summary>
    public static double AgainstWeights = 0.5;

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
    public const double Fade = 0.88;

    /// <summary>
    /// Which side each ball is on, or null for cutthroat.
    ///
    /// Alternating rather than blocked, so a side's balls are not consecutive
    /// in playing order. That is the real game, and it is also the only
    /// arrangement in which playing for a partner means anything: the
    /// opponents strike in between, so a ball left in a good place has to
    /// survive their turn to be worth leaving there.
    /// </summary>
    public static int[] Sides()
    {
        if (Teams < 2) return null;

        var s = new int[Balls];
        for (int i = 0; i < Balls; i++) s[i] = i % Teams;
        return s;
    }

    /// <summary>Is this spot clear of the balls already placed and the furniture?</summary>
    static bool Room(Game game, int upTo, Vec2 at, double radius)
    {
        for (int j = 0; j < upTo; j++)
            if ((game.World.Balls[j].Pos - at).Length < radius * 4) return false;

        var field = game.World.Field;

        foreach (var hoop in field.Hoops)
        {
            if ((hoop.LeftPost - at).Length < radius + hoop.WireRadius * 2) return false;
            if ((hoop.RightPost - at).Length < radius + hoop.WireRadius * 2) return false;
        }

        foreach (var peg in field.Pegs)
            if ((peg - at).Length < radius + field.PegRadius * 2) return false;

        return true;
    }

    /// <summary>
    /// Puts the balls out: the ordinary opening, or a lawn part way through a
    /// game if this one is to be scattered.
    ///
    /// Scattered balls are placed clear of each other and of the furniture and
    /// given a course point somewhere short of finished. Nothing here has to be
    /// a position real play would reach -- what keeps a label honest is that it
    /// is measured from what the balls actually do NEXT, and they play on from
    /// here under the ordinary rules whatever the arrangement.
    /// </summary>
    public static void Deal(Game game, Random dice)
    {
        var spec = game.World.Spec;

        for (int i = 0; i < Balls; i++)
        {
            game.States[i].Started = true;
            game.World.Balls[i].InPlay = true;
            game.World.Balls[i].Pos = game.World.Field.StartSpot
                                    + new Vec2(0, (i - (Balls - 1) / 2.0) * 0.35);
        }

        if (dice.NextDouble() >= Scatter) return;

        for (int i = 0; i < Balls; i++)
        {
            game.States[i].Point = dice.Next(0, Math.Max(1, game.States[i].Total - 1));

            for (int tries = 0; tries < 40; tries++)
            {
                var at = new Vec2(spec.BallRadius + dice.NextDouble()
                                    * (spec.Width - spec.BallRadius * 2),
                                  spec.BallRadius + dice.NextDouble()
                                    * (spec.Height - spec.BallRadius * 2));

                if (Room(game, i, at, spec.BallRadius))
                {
                    game.World.Balls[i].Pos = at;
                    break;
                }
            }
        }
    }

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
        WriteHeader(write, Mark);

        // The net that is PLAYING is also the one that finishes the labels, and
        // it is held fixed for the whole collection -- a target network. Letting
        // the labels chase a net that is moving is the standard way to make this
        // diverge.
        var boot = pattern.Net;

        var pen = new object();
        int kept = 0, done = 0;

        Parallel.For(0, games, new ParallelOptions { CancellationToken = quit }, g =>
        {
            var seen = new List<(float[] X, int Who, int At)>();
            var ledger = new List<double[]>();

            if (PlayOne(fromSeed + g, pattern, seen, ledger, quit))
            {
                // Worked out before the lock, because scoring a position
                // through the network is the expensive part and holding the
                // pen while every thread does it would serialise the run.
                var labels = new float[seen.Count];
                for (int j = 0; j < seen.Count; j++) labels[j] = Label(seen, ledger, j, boot);

                lock (pen)
                {
                    for (int j = 0; j < seen.Count; j++)
                    {
                        foreach (float v in seen[j].X) write.Write(v);
                        write.Write(labels[j]);
                        kept++;
                    }
                }
            }

            progress?.Invoke(Interlocked.Increment(ref done), games);
        });

        return kept;
    }

    /// <summary>
    /// What a position is worth to <paramref name="who"/>'s SIDE, discounted
    /// into the future.
    ///
    /// Points my side goes on to score, less the points the other side scores,
    /// over the next turn or two. That is the whole reward, and it is
    /// deliberately about wickets rather than about winning: a stroke is worth
    /// what it does to the flow of points on both sides, and the game is a few
    /// hundred strokes long, so "did this side eventually win" is a fact about
    /// the game and barely a fact about the stroke at all.
    ///
    /// A partner's hoop counts as much as my own, because it is worth as much:
    /// the side's score is what wins, and a stroke that sets a partner up for
    /// three hoops is better than one that gets me one. That is precisely the
    /// judgement no amount of "how well is MY ball doing" can express, and the
    /// reason a partnership needs training of its own.
    ///
    /// Averaged per ball on each side rather than totalled, so that cutthroat
    /// -- where it is one of me and five of them -- lands on the same scale as
    /// three a side rather than teaching a bot that everything is hopeless.
    /// </summary>
    /// <summary>
    /// What one kept position is worth: a few strokes of what really happened,
    /// then the network's own opinion of where that left things.
    ///
    /// This is the fix for the measurement that sank the last run. A label that
    /// sums thirty-odd strokes of a six-player game is one sample of something
    /// enormously variable -- the network fitted it to a held-back error of
    /// 0.55 points and could get no closer, because 0.55 IS roughly the noise.
    /// Meanwhile the candidates it has to choose between inside a single turn
    /// differ by about 0.07. Ranking them with a judge whose error is eight
    /// times the gap is close to drawing lots, and no amount of capacity or
    /// epochs helps: the ceiling is in the label, not the model.
    ///
    /// Bootstrapping cuts the variance instead. Five strokes of real outcome
    /// carry most of what a stroke actually caused, and everything past that is
    /// replaced by one number the network already believes -- so the noise of
    /// twenty-five further strokes of other people's luck never enters the
    /// label at all.
    ///
    /// With no net -- the first generation -- there is nothing to bootstrap
    /// from and it falls back to the full discounted return.
    /// </summary>
    static float Label(List<(float[] X, int Who, int At)> seen,
                       List<double[]> ledger, int j, Net boot)
    {
        var (_, who, at) = seen[j];
        var sides = Sides();

        // The same viewpoint's next kept position is exactly Balls further on,
        // because every kept stroke is written once per ball in ball order.
        int next = j + Balls;

        if (boot == null || next >= seen.Count)
            return (float)Ahead(ledger, at, who, sides, 0);

        var x = new double[Sight.Size];
        var from = seen[next].X;
        for (int i = 0; i < Sight.Size; i++) x[i] = from[i];

        return (float)(Ahead(ledger, at, who, sides, Every)
                     + Math.Pow(Fade, Every) * boot.Value(x));
    }

    /// <summary>
    /// <paramref name="horizon"/> strokes of it, or all of it when zero.
    ///
    /// A short horizon is only half a label -- the rest is what the position is
    /// worth at the end of it, which the network itself supplies. See
    /// <see cref="Collect"/>.
    /// </summary>
    static double Ahead(List<double[]> ledger, int at, int who, int[] sides,
                        int horizon)
    {
        double sum = 0, weight = 1;
        int end = horizon > 0 ? Math.Min(ledger.Count, at + horizon) : ledger.Count;

        for (int i = at; i < end; i++)
        {
            var gains = ledger[i];

            double mine = 0, theirs = 0;
            int ours = 0, them = 0;

            for (int b = 0; b < gains.Length; b++)
            {
                bool together = sides == null ? b == who : sides[b] == sides[who];
                if (together) { mine += gains[b]; ours++; }
                else { theirs += gains[b]; them++; }
            }

            sum += weight * (mine / Math.Max(1, ours) - theirs / Math.Max(1, them));

            weight *= Fade;
            if (horizon <= 0 && weight < 0.01) break;   // past here it cannot matter
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
        var dice = new Random(seed * 7919 + 13);

        var arr = new Ball[Balls];
        for (int i = 0; i < Balls; i++) arr[i] = new Ball(Vec2.Zero);

        // Null sides is cutthroat: every ball its own side, which is what
        // Bot.SameSide reads it as. Otherwise alternating, so the balls of a
        // side are not consecutive in playing order -- which is the real game
        // and also the only arrangement where a partner is ever worth playing
        // for, since the opponents strike in between.
        var game = new Game(new World(arr, Field.NineWicket(), Lawn()), Sides(),
                            RuleOptions.Basic);

        Deal(game, dice);

        // Half the games are the network against the linear weights, and in
        // those it plays side 0 while side 1 plays the weights alone. The rest
        // are the network against itself. See AgainstWeights.
        bool sparring = pattern.Net != null && dice.NextDouble() < AgainstWeights;

        var bots = new Bot[Balls];
        for (int i = 0; i < Balls; i++)
        {
            bool netPlays = !sparring || i % 2 == 0;

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
                Net = netPlays ? pattern.Net : null,
                NetBlend = pattern.NetBlend,
                Explore = pattern.Explore,
                ExploreTop = pattern.ExploreTop
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

            // And the game ending, worth a couple of hoops rather than a
            // dozen -- see WinBonus. The search still treats a finished game as
            // worth Net.Won, which is much larger; this is only what the
            // network is asked to learn to see coming.
            if (game.Winner != null)
                foreach (int w in game.Winner) gains[w] += WinBonus;

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
    public const int Mark = 0x43524F51;          // "CROQ" -- returns

    /// <summary>
    /// The same shape of file, holding a different QUANTITY: an advantage,
    /// centred within its position, rather than a return.
    ///
    /// A separate mark because the two are indistinguishable as numbers and
    /// training on the wrong one produces a net that is confidently wrong --
    /// which has already cost this project one overnight run. Both are
    /// readable; what is refused is confusing them.
    /// </summary>
    public const int MarkAdvantage = 0x43524F41; // "CROA" -- advantages

    public const int Header = 2 * sizeof(int);

    /// <summary>Stamps a file with its kind and the encoding it was written for.</summary>
    public static void WriteHeader(BinaryWriter w, int mark)
    {
        w.Write(mark);
        w.Write(Sight.Size);
    }

    /// <summary>Which kind of file this is, or 0 if it is not one of ours.</summary>
    public static int MarkOf(string path)
    {
        if (!File.Exists(path)) return 0;

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var read = new BinaryReader(file);
        if (file.Length < Header) return 0;

        int mark = read.ReadInt32();
        return mark == Mark || mark == MarkAdvantage ? mark : 0;
    }

    /// <summary>How many samples a file holds, or -1 if it is not one of ours.</summary>
    public static int Count(string path)
    {
        if (!File.Exists(path)) return -1;

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var read = new BinaryReader(file);

        if (file.Length < Header) return -1;

        int mark = read.ReadInt32();
        if (mark != Mark && mark != MarkAdvantage) return -1;
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

        int mark = read.ReadInt32();
        if (mark != Mark && mark != MarkAdvantage) return "that is not a positions file";

        int wrote = read.ReadInt32();
        if (wrote != Sight.Size)
            return $"those positions were written for an encoding of {wrote} numbers "
                 + $"and this build reads {Sight.Size} -- collect them again";

        long body = file.Length - Header;
        long record = (Sight.Size + 1) * sizeof(float);
        if (body % record != 0) return "the file ends part way through a position";

        return null;
    }

    /// <summary>
    /// Reads several files as one set, which is what a generation past the
    /// first should learn from.
    ///
    /// Training each generation only on its own games is how a self-play loop
    /// oscillates: the network chases whatever the newest policy happened to do
    /// and forgets the position it held last time, so the two take turns being
    /// wrong instead of converging. Keeping the last few generations in the
    /// batch is the cheap standard fix -- a replay buffer, spelled as files --
    /// and it costs nothing but disk.
    ///
    /// Files that are missing or of the wrong encoding are skipped rather than
    /// fatal, because the natural way to use this is "the last three
    /// generations" at a point where only one of them exists.
    /// </summary>
    public static (float[] X, float[] Y) LoadMany(IEnumerable<string> paths)
    {
        var good = new List<string>();
        int total = 0, kind = 0;

        foreach (var p in paths)
        {
            if (Trouble(p) != null) continue;

            // Returns and advantages are different quantities that look
            // identical as numbers, and a batch holding both would train a net
            // to predict their average -- which is nothing. Refused rather than
            // silently averaged.
            int mark = MarkOf(p);
            if (kind == 0) kind = mark;
            else if (mark != kind)
                throw new InvalidDataException(
                    "those files hold different kinds of label -- "
                  + "returns and advantages cannot be learned from together");

            good.Add(p);
            total += Count(p);
        }

        if (good.Count == 0) throw new InvalidDataException("no usable position files");

        var x = new float[(long)total * Sight.Size];
        var y = new float[total];
        int at = 0;

        foreach (var p in good)
        {
            var (px, py) = Load(p);
            Array.Copy(px, 0, x, (long)at * Sight.Size, px.LongLength);
            Array.Copy(py, 0, y, at, py.Length);
            at += py.Length;
        }

        return (x, y);
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
