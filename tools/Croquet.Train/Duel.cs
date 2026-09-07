using Croquet.Core;

namespace Croquet.Train;

/// <summary>
/// Two sets of weights playing each other, which is the only measurement that
/// matters here.
///
/// Not "which scores higher on the evaluator" -- both sides believe their own
/// evaluator, so that question is circular. Not "which scores more points" --
/// a set of weights that piles up points and loses is worse than one that wins
/// ugly. Games won, and nothing else.
/// </summary>
public static class Duel
{
    /// <summary>
    /// One game between two weight sets, alternating which of them plays which
    /// balls. Returns true if <paramref name="a"/>'s side won.
    ///
    /// Both sides get the SAME hand, from the same seed, so the only difference
    /// between them is judgement. A match where one side also strikes the ball
    /// better measures nothing.
    /// </summary>
    public static bool? Play(BotWeights a, BotWeights b, int seed, bool swap,
                             int balls = 4, int maxStrokes = 600,
                             CancellationToken quit = default, Net net = null,
                             bool solo = false)
    {
        var spec = new CourtSpec
        {
            Width = 30.48,
            Height = 15.24,
            Friction = 1.6,
            Restitution = 0.8,
            ObstacleRestitution = 0.5
        };

        var arr = new Ball[balls];
        for (int i = 0; i < balls; i++) arr[i] = new Ball(Vec2.Zero);

        // Every ball for itself when solo, which is how the game is played by
        // default and how the net is trained; otherwise two sides alternating,
        // the partnership arrangement, which is the only one in which "put a
        // partner in position" is a thing that can be learned at all.
        int[] sides = null;
        if (!solo)
        {
            sides = new int[balls];
            for (int i = 0; i < balls; i++) sides[i] = i % 2;
        }

        var game = new Game(new World(arr, Field.NineWicket(), spec), sides,
                            RuleOptions.Basic);

        for (int i = 0; i < balls; i++)
        {
            game.States[i].Started = true;
            game.World.Balls[i].InPlay = true;
            game.World.Balls[i].Pos = game.World.Field.StartSpot
                                    + new Vec2(0, (i - (balls - 1) / 2.0) * 0.4);
        }

        // One bot per ball, so each keeps its own stream of wobble -- otherwise
        // the sides share a random sequence and the hands are correlated.
        var bots = new Bot[balls];
        for (int i = 0; i < balls; i++)
        {
            bots[i] = Bot.Casual(seed * 131 + i * 17);

            // Alternate balls belong to A whether they are a partnership or six
            // strangers, so a series is always half the field against the other
            // half and the comparison is even either way.
            bool mine = (i % 2 == 0) != swap;
            bots[i].Weights = mine ? a : b;

            // A net given here plays for side A only, so the series is the net
            // against the weights rather than against itself.
            if (net != null && mine) bots[i].Net = net;
        }

        int strokes = 0;
        while (game.Winner == null && strokes < maxStrokes)
        {
            // Checked every stroke, so an interrupted run stops in about the
            // time one stroke takes rather than however long is left of the
            // games already in flight. On a multi-hour run that is the
            // difference between Ctrl+C working and Ctrl+C seeming not to.
            if (quit.IsCancellationRequested) return null;

            try { bots[game.Striker].PlayStroke(game); }
            catch (InvalidOperationException) { break; }
            strokes++;
        }

        // A game nobody finished is not a win for anybody. Counting it as a
        // draw rather than picking whoever was ahead keeps the fitness honest:
        // being ahead on points at stroke six hundred is exactly the thing
        // that is not being optimised for.
        if (game.Winner == null) return null;

        return (game.Winner[0] % 2 == 0) != swap;
    }

    /// <summary>The outcome of a series, as wins, losses and unfinished games.</summary>
    public readonly record struct Result(int Wins, int Losses, int Drawn)
    {
        public int Played => Wins + Losses + Drawn;

        /// <summary>
        /// Share of DECIDED games won. Unfinished ones are left out rather than
        /// counted as half: they are a measurement failure, not a result, and
        /// burying them in the average hides that they happened.
        /// </summary>
        public double Rate => Wins + Losses == 0 ? 0.5 : Wins / (double)(Wins + Losses);

        public override string ToString() =>
            $"{Wins}-{Losses}" + (Drawn > 0 ? $" ({Drawn} unfinished)" : "")
            + $"  {Rate * 100:0.0}%";
    }

    /// <summary>
    /// A series, played in parallel. Every seed is played TWICE with the sides
    /// swapped, so a lucky opening cannot favour one set of weights: both play
    /// it, once from each end.
    /// </summary>
    public static Result Series(BotWeights a, BotWeights b, int games, int fromSeed,
                                int balls = 4, CancellationToken quit = default,
                                Action<int, int>? played = null, Net net = null,
                                bool solo = false)
    {
        int pairs = Math.Max(1, games / 2);
        var outcomes = new bool?[pairs * 2];
        int done = 0;

        // Over every GAME rather than every pair. Both halves of a pair are the
        // same opening from opposite ends and neither needs the other's result,
        // so pairing them into one work item just halves the number of cores
        // that can be busy.
        try
        {
            Parallel.For(0, pairs * 2, new ParallelOptions { CancellationToken = quit }, g =>
            {
                outcomes[g] = Play(a, b, fromSeed + g / 2, swap: g % 2 == 1, balls,
                                   quit: quit, net: net, solo: solo);
                played?.Invoke(Interlocked.Increment(ref done), pairs * 2);
            });
        }
        catch (OperationCanceledException)
        {
            // Whatever finished before the interruption is still an honest
            // sample; it is just a smaller one than was asked for. The caller
            // decides whether that is enough to act on.
        }

        int wins = 0, losses = 0, drawn = 0;
        foreach (var o in outcomes)
        {
            if (o == null) drawn++;
            else if (o.Value) wins++;
            else losses++;
        }

        // Games never started are not unfinished games -- they are games that
        // did not happen, and counting them as draws would report a cancelled
        // series as a much worse one.
        return new Result(wins, losses, Math.Max(0, drawn - (pairs * 2 - done)));
    }
}
