using System.Diagnostics;
using System.Linq;
using Croquet.Core;

namespace Croquet.Train;

/// <summary>
/// Fitting the value net to what actually happened.
///
/// Ordinary supervised learning, and worth saying plainly because "neural
/// network" suggests something more exotic: there are positions, there is a
/// label saying how many points that ball went on to gain, and the weights are
/// nudged until the network's guess matches the label more closely. No
/// reinforcement learning machinery, no policy gradients, no search inside the
/// training loop.
///
/// Least squares, because the output is no longer a probability but a NUMBER OF
/// POINTS -- how far ahead this ball is about to be. It was cross-entropy
/// against "did this side win", which was the right loss for the wrong target.
/// </summary>
public sealed class Learn
{
    public int Epochs = 8;
    public int Batch = 256;
    public double Rate = 0.002;

    /// <summary>Share of the data held back, never trained on.</summary>
    public double Held = 0.1;

    readonly Random rng = new(20260906);

    /// <summary>
    /// Adam, which is the default for a reason worth knowing: it keeps a
    /// running scale for every weight separately, so a weight that only matters
    /// in rare positions still moves at a sensible speed instead of being
    /// drowned by the ones that fire constantly. Plain gradient descent on this
    /// data trains, but it trains several times slower for no saving.
    /// </summary>
    double[] mW1, vW1, mB1, vB1, mW2, vW2, mB2, vB2, mW3, vW3;
    double mB3, vB3;
    int step;

    public Net Fit(Net net, float[] x, float[] y, CancellationToken quit,
                   Action<string> say)
    {
        int n = y.Length;
        int test = Math.Max(1, (int)(n * Held));
        int train = n - test;

        Ready(net);

        say($"{n:N0} positions -- {train:N0} to learn from, {test:N0} held back");
        say($"{Net.Weights:N0} weights, batches of {Batch}, learning rate {Rate}");
        say($"labels vary by {SpreadOf(y, train, n):0.000} points; guessing the "
          + $"average is out by {Loss(net, x, y, train, n, flat: true):0.000}\n");

        var order = new int[train];
        for (int i = 0; i < train; i++) order[i] = i;

        var clock = Stopwatch.StartNew();
        double bestHeld = double.MaxValue;
        Net best = net;

        for (int epoch = 1; epoch <= Epochs && !quit.IsCancellationRequested; epoch++)
        {
            Shuffle(order);

            for (int at = 0; at + Batch <= train && !quit.IsCancellationRequested; at += Batch)
                Descend(net, x, y, order, at, Batch);

            double onTrain = Loss(net, x, y, 0, train);
            double onHeld = Loss(net, x, y, train, n);
            double spread = Spread(net, x, train, n);

            // The held-back set is the only honest number here. Training loss
            // falls whatever happens -- a big enough network memorises the
            // games it was shown and learns nothing about croquet -- and the
            // moment the two part company is the moment to stop.
            string flag = onHeld < bestHeld ? "  <- best" : "";
            if (onHeld < bestHeld) { bestHeld = onHeld; best = Copy(net); }

            say($"epoch {epoch,2}/{Epochs}  train {onTrain:0.000}  " +
                $"held {onHeld:0.000}  its answers vary by {spread:0.000}" +
                $"  {clock.Elapsed.TotalMinutes:0.0}m{flag}");
        }

        say($"\nbest held-back error {bestHeld:0.000} points");
        return best;
    }

    // ---- one step -------------------------------------------------------

    void Descend(Net net, float[] x, float[] y, int[] order, int at, int count)
    {
        int inputs = Sight.Size, hidden = Net.Hidden;

        var gW1 = new double[inputs * hidden]; var gB1 = new double[hidden];
        var gW2 = new double[hidden * hidden]; var gB2 = new double[hidden];
        var gW3 = new double[hidden]; double gB3 = 0;

        var xi = new double[inputs];
        var h1 = new double[hidden];
        var h2 = new double[hidden];
        var d1 = new double[hidden];
        var d2 = new double[hidden];

        for (int b = 0; b < count; b++)
        {
            int i = order[at + b];
            for (int j = 0; j < inputs; j++) xi[j] = x[(long)i * inputs + j];

            net.Forward(xi, h1, h2, out double guess);

            // Least squares through a linear output collapses to the same one
            // line cross-entropy through a sigmoid did: the gradient is how
            // wrong the guess was. Two different losses, one gradient, because
            // each is paired with the output it belongs to.
            double wrong = guess - y[i];

            gB3 += wrong;
            for (int j = 0; j < hidden; j++)
            {
                gW3[j] += wrong * h2[j];
                d2[j] = h2[j] > 0 ? wrong * net.W3[j] : 0;   // ReLU passes or blocks
            }

            for (int j = 0; j < hidden; j++)
            {
                gB2[j] += d2[j];
                int row = j * hidden;
                for (int k = 0; k < hidden; k++) gW2[row + k] += d2[j] * h1[k];
            }

            for (int k = 0; k < hidden; k++)
            {
                double s = 0;
                for (int j = 0; j < hidden; j++) s += net.W2[j * hidden + k] * d2[j];
                d1[k] = h1[k] > 0 ? s : 0;
            }

            for (int j = 0; j < hidden; j++)
            {
                gB1[j] += d1[j];
                int row = j * inputs;
                for (int k = 0; k < inputs; k++) gW1[row + k] += d1[j] * xi[k];
            }
        }

        step++;
        double scale = 1.0 / count;

        Apply(net.W1, gW1, mW1, vW1, scale);
        Apply(net.B1, gB1, mB1, vB1, scale);
        Apply(net.W2, gW2, mW2, vW2, scale);
        Apply(net.B2, gB2, mB2, vB2, scale);
        Apply(net.W3, gW3, mW3, vW3, scale);

        // The one lone scalar, by hand.
        double g = gB3 * scale;
        mB3 = 0.9 * mB3 + 0.1 * g;
        vB3 = 0.999 * vB3 + 0.001 * g * g;
        net.B3 -= Rate * (mB3 / (1 - Math.Pow(0.9, step)))
                / (Math.Sqrt(vB3 / (1 - Math.Pow(0.999, step))) + 1e-8);
    }

    void Apply(double[] w, double[] g, double[] m, double[] v, double scale)
    {
        double bias1 = 1 - Math.Pow(0.9, step);
        double bias2 = 1 - Math.Pow(0.999, step);

        for (int i = 0; i < w.Length; i++)
        {
            double gi = g[i] * scale;
            m[i] = 0.9 * m[i] + 0.1 * gi;
            v[i] = 0.999 * v[i] + 0.001 * gi * gi;
            w[i] -= Rate * (m[i] / bias1) / (Math.Sqrt(v[i] / bias2) + 1e-8);
        }
    }

    void Ready(Net net)
    {
        mW1 = new double[net.W1.Length]; vW1 = new double[net.W1.Length];
        mB1 = new double[net.B1.Length]; vB1 = new double[net.B1.Length];
        mW2 = new double[net.W2.Length]; vW2 = new double[net.W2.Length];
        mB2 = new double[net.B2.Length]; vB2 = new double[net.B2.Length];
        mW3 = new double[net.W3.Length]; vW3 = new double[net.W3.Length];
        mB3 = vB3 = 0;
        step = 0;
    }

    // ---- how well it is doing -------------------------------------------

    /// <summary>
    /// Root mean squared error, in points -- the same units the label is in, so
    /// "out by 0.4 of a point" means something without translation.
    /// </summary>
    static double Loss(Net net, float[] x, float[] y, int from, int to,
                       bool flat = false)
    {
        double average = 0;
        if (flat)
        {
            for (int i = from; i < to; i++) average += y[i];
            average /= Math.Max(1, to - from);
        }

        var xi = new double[Sight.Size];
        double sum = 0;
        int n = 0;

        for (int i = from; i < to; i += 7)          // a seventh is plenty to measure
        {
            double guess;
            if (flat) guess = average;
            else
            {
                for (int j = 0; j < Sight.Size; j++) xi[j] = x[(long)i * Sight.Size + j];
                guess = net.Value(xi);
            }

            double off = guess - y[i];
            sum += off * off;
            n++;
        }

        return Math.Sqrt(sum / Math.Max(1, n));
    }

    /// <summary>
    /// How WIDELY it answers, which is the number the first net failed on.
    ///
    /// A judge that returns nearly the same value whatever it is shown is
    /// accurate on average and useless for choosing, and no loss will say so --
    /// predicting the mean everywhere scores respectably and plays at random.
    /// This is the standard deviation of its own answers, and it wants to be
    /// somewhere near the standard deviation of the labels.
    /// </summary>
    static double Spread(Net net, float[] x, int from, int to)
    {
        var xi = new double[Sight.Size];
        var seen = new List<double>();

        for (int i = from; i < to; i += 7)
        {
            for (int j = 0; j < Sight.Size; j++) xi[j] = x[(long)i * Sight.Size + j];
            seen.Add(net.Value(xi));
        }

        if (seen.Count < 2) return 0;
        double mean = seen.Average();
        return Math.Sqrt(seen.Sum(v => (v - mean) * (v - mean)) / seen.Count);
    }

    /// <summary>The same, for the labels, so the two can be compared.</summary>
    static double SpreadOf(float[] y, int from, int to)
    {
        double mean = 0;
        int n = 0;
        for (int i = from; i < to; i += 7) { mean += y[i]; n++; }
        if (n < 2) return 0;
        mean /= n;

        double sum = 0;
        for (int i = from; i < to; i += 7) sum += (y[i] - mean) * (y[i] - mean);
        return Math.Sqrt(sum / n);
    }

    void Shuffle(int[] a)
    {
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    static Net Copy(Net n) => Net.FromText(n.ToText());
}
