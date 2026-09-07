using System.Diagnostics;
using Croquet.Core;

namespace Croquet.Train;

/// <summary>
/// The training loop, which is deliberately the simplest thing that can work.
///
/// Hold a champion set of weights. Each round, make a few mutants by nudging
/// every weight a little, play each one against the CHAMPION, and if the best
/// of them wins the series, it becomes champion. Repeat.
///
/// That is an evolution strategy, and it is the right family for this problem
/// for one reason: the thing being optimised -- games won -- has no gradient.
/// There is no derivative of "won the game" with respect to "how much a roquet
/// is worth", so anything that needs one is out, and what is left is to try
/// changes and keep what works.
///
/// Two decisions do most of the work:
///
/// **Mutants play the champion, not a fixed opponent.** Fitness is a HEAD TO
/// HEAD, so the comparison is paired: both sides meet the same openings, the
/// same seeds, the same hands. Scoring each candidate against some third party
/// and comparing scores would need far more games to see the same difference,
/// because most of the variance is in the games rather than in the weights.
///
/// **A challenger has to actually win, not merely draw.** Weights that cannot
/// beat the champion do not replace it even when they score above half by a
/// hair, because at sixty games a hair is noise, and a champion that drifts on
/// noise wanders instead of climbing.
/// </summary>
public sealed class Trainer
{
    public int Rounds = 30;

    /// <summary>Games in each challenger's series. More is slower and surer.</summary>
    public int Games = 60;

    /// <summary>Challengers a round. More explores wider at the same cost each.</summary>
    public int Children = 6;

    /// <summary>
    /// How hard each weight is nudged, as a share of its own size.
    ///
    /// Relative rather than absolute, because these numbers live on wildly
    /// different scales -- a hoop is worth 1200 and a band is 0.9 metres wide.
    /// One absolute step would be a rounding error to the first and a total
    /// rewrite of the second.
    /// </summary>
    public double Step = 0.25;

    public int Balls = 4;
    public BotWeights Start = BotWeights.Default;
    public string Out = "weights/trained.txt";

    readonly Random rng = new(20260905);

    /// <summary>
    /// Set by Ctrl+C. A run of this length WILL be interrupted, so being
    /// interrupted is a normal way for it to end rather than an accident: it
    /// stops at the end of whatever it is doing, writes the champion out and
    /// says what it found. Nothing is lost by quitting.
    /// </summary>
    readonly CancellationTokenSource quit = new();

    public void Stop() => quit.Cancel();
    public bool Quitting => quit.IsCancellationRequested;

    public void Run()
    {
        var champion = Start;
        var baseline = BotWeights.Default;

        int total = Rounds * (Children + 1) * Games;
        Console.WriteLine($"{Rounds} rounds, {Children} challengers a round, " +
                          $"{Games} games each, plus a confirming series");
        Console.WriteLine($"about {total:N0} games on {Environment.ProcessorCount} cores");
        Console.WriteLine($"writing to {Out}");
        Console.WriteLine("Ctrl+C stops it and keeps what it has learned so far.\n");

        var clock = Stopwatch.StartNew();
        int accepted = 0, round = 0, seed = 1_000, sofar = 0;

        for (round = 1; round <= Rounds && !Quitting; round++)
        {
            var challengers = new BotWeights[Children];
            for (int i = 0; i < Children; i++) challengers[i] = Mutate(champion);

            var scores = new Duel.Result[Children];
            for (int i = 0; i < Children && !Quitting; i++)
            {
                // Every challenger in a round faces the same seeds, so they are
                // compared with each other on equal terms as well as with the
                // champion.
                int at = i, was = sofar;
                scores[i] = Duel.Series(challengers[i], champion, Games, seed, Balls,
                    quit.Token,
                    (done, of) => Tick($"round {round}/{Rounds}  challenger {at + 1}/{Children}",
                                       done, of, was, total, clock));

                sofar += scores[i].Played;
                Line($"  round {round,3}  challenger {i + 1}/{Children}: {scores[i]}");
            }

            if (Quitting) break;

            int best = 0;
            for (int i = 1; i < Children; i++)
                if (scores[i].Rate > scores[best].Rate) best = i;

            // The winner's curse, and it is the thing most likely to make a run
            // like this go quietly nowhere. Taking the BEST of six noisy scores
            // does not find the best challenger, it finds the luckiest one --
            // six coins flipped sixty times each will hand you one at 58% every
            // round, for ever, on no merit at all. So the winner is re-played on
            // seeds it has never seen, and has to win twice.
            bool promising = scores[best].Rate > 0.5;
            var confirm = default(Duel.Result);

            if (promising)
            {
                int was = sofar;
                confirm = Duel.Series(challengers[best], champion, Games,
                    seed + 500_000, Balls, quit.Token,
                    (done, of) => Tick($"round {round}/{Rounds}  confirming #{best + 1}",
                                       done, of, was, total, clock));
                sofar += confirm.Played;
            }
            seed += Games;

            bool take = promising && !Quitting && confirm.Rate > 0.5 + Margin(confirm);
            if (take) { champion = challengers[best]; accepted++; Save(champion); }

            Line($"round {round,3}  best was #{best + 1} at {scores[best].Rate * 100:0.0}%" +
                 (promising ? $", confirmed {confirm}" : ", nothing above half") +
                 $"   ->  {(take ? "ACCEPTED" : "champion held")}");
            Console.WriteLine();
        }

        clock.Stop();
        int ran = Math.Max(0, Math.Min(round - 1, Rounds));

        Line(Quitting
            ? $"stopped during round {ran + 1}; {ran} rounds finished, " +
              $"{accepted} of them an improvement"
            : $"{accepted} of {Rounds} rounds improved on the one before");
        Console.WriteLine($"{sofar:N0} games in {clock.Elapsed.TotalMinutes:0.0} minutes");

        Save(champion);
        Console.WriteLine($"\nwritten to {Out}\n");
        Console.WriteLine(champion.CompareTo(baseline));

        // The champion beat the set before it, which is not the same as being
        // any good: a chain of small wins can still drift somewhere worse than
        // where it started. So a finished run plays what it learned against what
        // it started from, over more games than any single round.
        //
        // Skipped when interrupted, because somebody who has just pressed Ctrl+C
        // is not waiting three more minutes to be told a number. The command is
        // printed instead, to be run whenever.
        if (Quitting)
        {
            Console.WriteLine("Check it against the baseline whenever you like:");
            Console.WriteLine("  dotnet run --project tools/Croquet.Train -c Release -- " +
                              "match \"" + Out + "\" --games 400");
            return;
        }

        Console.WriteLine("learned vs the hand-tuned baseline, 300 games:");
        var final = Duel.Series(champion, baseline, 300, 900_000, Balls, quit.Token,
            (done, of) => Tick("final check", done, of, 0, 0, clock));
        Line($"  {final}");
    }

    // ---- saying what it is doing ------------------------------------------

    /// <summary>
    /// The live line, rewritten in place.
    ///
    /// A round is minutes long and a whole run is hours, so a tool that prints
    /// nothing until a round ends is indistinguishable from one that has hung --
    /// and the only thing to do about a run you cannot tell is working is kill
    /// it, which is the one outcome worth designing against.
    /// </summary>
    static void Tick(string what, int done, int of, int before, int total, Stopwatch clock)
    {
        // Every fourth game. This is called from every worker thread as it
        // finishes, and a console write costs more than it looks.
        if (done % 4 != 0 && done != of) return;

        string eta = "";
        if (total > 0 && before + done > 12)
        {
            double each = clock.Elapsed.TotalSeconds / (before + done);
            var left = TimeSpan.FromSeconds(each * Math.Max(0, total - before - done));
            eta = left.TotalHours >= 1 ? $"  ~{left.TotalHours:0.0}h left"
                                       : $"  ~{left.TotalMinutes:0}m left";
        }

        Write($"  {what}  {Bar(done / (double)of, 22)} {done,3}/{of}{eta}");
    }

    static string Bar(double part, int width)
    {
        int full = (int)Math.Round(Math.Clamp(part, 0, 1) * width);
        return "[" + new string('#', full) + new string('.', width - full) + "]";
    }

    static readonly object Pen = new();
    static int painted;

    /// <summary>Overwrites the live line, padding away whatever was longer.</summary>
    static void Write(string s)
    {
        lock (Pen)
        {
            Console.Write("\r" + s.PadRight(Math.Max(painted, s.Length)));
            painted = s.Length;
        }
    }

    /// <summary>Clears the live line, then writes something that stays.</summary>
    static void Line(string s)
    {
        lock (Pen)
        {
            if (painted > 0) Console.Write("\r" + new string(' ', painted) + "\r");
            painted = 0;
            Console.WriteLine(s);
        }
    }

    /// <summary>
    /// How far above half a challenger has to be before it is believed.
    ///
    /// One standard error of the win rate, so the bar rises when the sample is
    /// small and falls when it is large -- a challenger at 55% over 60 games is
    /// inside the noise and stays out; the same rate over 600 games is real.
    /// </summary>
    static double Margin(Duel.Result r)
    {
        int n = r.Wins + r.Losses;
        return n < 2 ? 1 : 0.5 / Math.Sqrt(n);
    }

    /// <summary>
    /// Nudges every weight at once, by a random share of its own size.
    ///
    /// All of them together rather than one at a time, because the weights are
    /// not independent: raising what a hoop is worth changes what the right
    /// value of a roquet is, and a search that moves one at a time has to walk
    /// around that corner instead of through it.
    /// </summary>
    BotWeights Mutate(BotWeights from)
    {
        var v = from.ToArray();
        var basis = BotWeights.Default.ToArray();

        for (int i = 0; i < v.Length; i++)
        {
            // Scaled against the DEFAULT rather than the current value, so a
            // weight that has drifted near zero can still climb back out. Scaled
            // against itself, zero is a trap with no exit.
            double size = Math.Max(Math.Abs(basis[i]), 1e-6);
            v[i] += Gauss() * Step * size;
        }

        // Back onto the standard scale, so the only thing that changed is what
        // the weights say relative to each other -- which is the only thing
        // they can say. See BotWeights.Normalised.
        return new BotWeights(v).Normalised();
    }

    double Gauss()
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    void Save(BotWeights w)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(Out));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        File.WriteAllText(Out,
            "# Learned by tools/Croquet.Train: weight sets played against each\n" +
            "# other, fitness is games won. See core/BotWeights.cs for what\n" +
            "# each of these means.\n\n" + w.ToText());
    }
}
