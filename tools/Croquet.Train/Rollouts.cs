using Croquet.Core;

namespace Croquet.Train;

/// <summary>
/// One position, several candidate strokes, everything else held identical --
/// and what each stroke was actually worth, measured rather than predicted.
///
/// This is the answer to the thing that sank three attempts, and the reasoning
/// is worth keeping because the failure was subtle and the numbers were plain.
///
/// A value net was trained to predict the discounted points a side goes on to
/// score. Measured on the last run: labels vary by 0.664 points across
/// positions; the net's error on a position it had not seen was 0.551; and the
/// candidate strokes it must choose between INSIDE ONE TURN differ by 0.068.
/// Its error was eight times the gap it was being asked to resolve, so ranking
/// candidates on it was close to drawing lots -- and it lost 78 games from 78.
///
/// That ceiling is in the LABEL, not the model. One rollout of a six-player
/// game is enormously variable, and no amount of capacity or epochs fits noise.
///
/// So measure the difference directly instead. From one root position, play
/// EVERY candidate stroke, then let the same players carry on from each with
/// the SAME SEEDS. The only thing that differs between those futures is the
/// stroke, so what comes back is attributable to the stroke -- most of the luck
/// is common to all of them and cancels. Then subtract the mean of the group,
/// which deletes the root's own worth entirely and leaves the one quantity the
/// search actually needs: how much BETTER this stroke is than the alternatives
/// from here.
///
/// That last step is the whole trick. The baseline it removes -- which side is
/// ahead, how far round everyone is -- is the 0.664 that drowned everything,
/// and it is identical for every candidate in a turn, so it was never anything
/// but nuisance. What is left is centred on zero and is the size of the
/// decision.
///
/// The cost is real: a root is worth roughly a hundred strokes of play instead
/// of nothing. It buys a signal the cheap way could not produce at any volume.
/// </summary>
public static class Rollouts
{
    /// <summary>Candidate strokes played out from each root position.</summary>
    public static int Candidates = 8;

    /// <summary>
    /// Strokes of real play measured from each candidate before the net is
    /// asked for the rest.
    ///
    /// Twelve rather than to the end of the game, and that is arithmetic rather
    /// than a compromise. At <see cref="Positions.Fade"/> of 0.88 the first
    /// twelve strokes carry 78% of all the weight there will ever be, and
    /// everything past stroke twenty-five carries 4%. The tail is bootstrapped
    /// from the net, so even that is not thrown away -- and playing to the peg
    /// instead would cost fifteen times as much for the last few per cent of a
    /// number that is mostly other people's luck anyway.
    /// </summary>
    public static int Horizon = 12;

    /// <summary>Strokes of ordinary play between one root position and the next.</summary>
    public static int RootEvery = 20;

    /// <summary>
    /// Plays games, stopping every so often to measure what each candidate
    /// stroke is worth. Returns how many samples were written.
    /// </summary>
    public static int Collect(string path, int games, int fromSeed, Net boot,
                              CancellationToken quit, Action<int, int> progress)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var write = new BinaryWriter(file);

        Positions.WriteHeader(write, Positions.MarkAdvantage);

        var pen = new object();
        int kept = 0, done = 0;

        Parallel.For(0, games, new ParallelOptions { CancellationToken = quit }, g =>
        {
            var got = new List<(float[] X, float Y)>();
            PlayOne(fromSeed + g, boot, got, quit);

            lock (pen)
            {
                foreach (var (x, y) in got)
                {
                    foreach (float v in x) write.Write(v);
                    write.Write(y);
                    kept++;
                }
            }

            progress?.Invoke(Interlocked.Increment(ref done), games);
        });

        return kept;
    }

    /// <summary>
    /// One game, played by the linear weights, harvested every so often.
    ///
    /// The MAIN LINE is played by the weights rather than by the net, for the
    /// same reason half the ordinary collection spars them: six copies of an
    /// unproven net wander for six hundred strokes without finishing, and a
    /// root position drawn from that is a position no real game reaches. The
    /// weights finish in about 230 strokes and the positions along the way are
    /// recognisable croquet.
    /// </summary>
    static void PlayOne(int seed, Net boot, List<(float[], float)> got,
                        CancellationToken quit)
    {
        var dice = new Random(seed * 7919 + 13);

        var arr = new Ball[Positions.Balls];
        for (int i = 0; i < Positions.Balls; i++) arr[i] = new Ball(Vec2.Zero);

        var game = new Game(new World(arr, Field.NineWicket(), Positions.Lawn()),
                            Positions.Sides(), RuleOptions.Basic);

        Positions.Deal(game, dice);

        var bots = new Bot[Positions.Balls];
        for (int i = 0; i < Positions.Balls; i++)
            bots[i] = Bot.Casual(seed * 131 + i * 17);

        int strokes = 0;

        while (game.Winner == null && strokes < 600)
        {
            if (quit.IsCancellationRequested) return;

            if (strokes % RootEvery == 0) Harvest(game, seed * 977 + strokes, boot, got, quit);

            try { bots[game.Striker].PlayStroke(game); }
            catch (InvalidOperationException) { break; }
            strokes++;
        }
    }

    /// <summary>
    /// The measurement. Every candidate from one position, each played out
    /// under identical conditions, scored and then centred.
    /// </summary>
    static void Harvest(Game root, int seed, Net boot, List<(float[], float)> got,
                        CancellationToken quit)
    {
        int me = root.Striker;

        // A scout with a steady hand, because the candidates are meant to be
        // the IDEAS available from here. The wobble belongs on the stroke a
        // player actually attempts, and it is applied by whoever plays it --
        // putting it here would measure eight blurred versions of the same
        // shot instead of eight different shots.
        var scout = Bot.Casual(seed);
        scout.RiskChecks = Math.Max(scout.RiskChecks, Candidates + 4);

        var moves = scout.Shortlist(root, Candidates);
        if (moves.Count < 2) return;

        var seen = new List<float[]>(moves.Count);
        var worth = new List<double>(moves.Count);

        foreach (var m in moves)
        {
            if (quit.IsCancellationRequested) return;

            var line = root.Clone();
            try { Bot.Apply(line, m); }
            catch (InvalidOperationException) { continue; }

            var x = new double[Sight.Size];
            Sight.Read(line, me, x);

            var packed = new float[Sight.Size];
            for (int i = 0; i < Sight.Size; i++) packed[i] = (float)x[i];

            seen.Add(packed);
            worth.Add(Roll(line, me, seed, boot, quit));
        }

        if (seen.Count < 2) return;

        double mean = 0;
        foreach (double w in worth) mean += w;
        mean /= worth.Count;

        // Centred, which is the point. What survives is how much better this
        // stroke was than the others available from the SAME position -- and
        // the position's own worth, which is identical for all of them and was
        // the whole of the variance that used to drown this, is gone.
        for (int i = 0; i < seen.Count; i++)
            got.Add((seen[i], (float)(worth[i] - mean)));
    }

    /// <summary>
    /// What happened next, discounted, from one candidate.
    ///
    /// The continuation bots are seeded IDENTICALLY for every candidate out of
    /// the same root -- common random numbers. Each of them draws its shaky
    /// hand from the same stream, so a candidate does not come out ahead
    /// because its rollout happened to be dealt better striking. The positions
    /// diverge, so the coupling is not perfect; it does not have to be, it only
    /// has to remove most of the noise that is nothing to do with the choice.
    /// </summary>
    static double Roll(Game game, int me, int seed, Net boot, CancellationToken quit)
    {
        int balls = game.World.Balls.Length;

        var bots = new Bot[balls];
        for (int i = 0; i < balls; i++) bots[i] = Bot.Casual(seed * 31 + i * 17);

        var before = new double[balls];
        double sum = 0, weight = 1;

        for (int n = 0; n < Horizon && game.Winner == null; n++)
        {
            if (quit.IsCancellationRequested) break;

            for (int i = 0; i < balls; i++) before[i] = game.States[i].Point;

            try { bots[game.Striker].PlayStroke(game); }
            catch (InvalidOperationException) { break; }

            double mine = 0, theirs = 0;
            int ours = 0, them = 0;

            for (int b = 0; b < balls; b++)
            {
                double gained = game.States[b].Point - before[b];
                if (Bot.SameSide(game, me, b)) { mine += gained; ours++; }
                else { theirs += gained; them++; }
            }

            if (game.Winner != null)
                foreach (int w in game.Winner)
                {
                    if (Bot.SameSide(game, me, w)) mine += Positions.WinBonus;
                    else theirs += Positions.WinBonus;
                }

            sum += weight * (mine / Math.Max(1, ours) - theirs / Math.Max(1, them));
            weight *= Positions.Fade;
        }

        // Whatever is left after the horizon, at the discount it has decayed
        // to. A game already over needs no estimate: it has an answer.
        if (game.Winner == null && boot != null) sum += weight * boot.Value(game, me);

        return sum;
    }
}
