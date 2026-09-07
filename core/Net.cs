using System;
using System.Globalization;
using System.Text;

// The trainer needs to reach inside the net to change it; nothing else does,
// and nothing else should. Kept as a crack for one named assembly rather than
// a public surface, so the only thing the game can ask a net is what a position
// is worth.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Croquet.Train")]

namespace Croquet.Core
{
    /// <summary>
    /// A small network that says what a position is worth.
    ///
    /// Deliberately tiny -- two hidden layers of sixty-four -- and that is not
    /// timidity. The features going in are already the game rather than pixels
    /// of it, so there is no representation to discover, only a function to fit;
    /// and it has to run a few thousand times inside a hundred milliseconds on a
    /// phone. Ten thousand weights do that in microseconds. Ten million would
    /// not, and would need a hundred times the games to fit.
    ///
    /// It answers ONE question: how much better off is this side about to be,
    /// over the next turn or two. Points its side is about to gain minus points
    /// the others are, discounted so that the near future counts for most.
    ///
    /// NOT the chance of winning the game, which is what it predicted first and
    /// why it was useless. A stroke barely moves that -- every candidate in a
    /// turn leaves the same scoreboard -- so a net trained on it scored every
    /// stroke alike and the search chose at random. A weird stroke can be worth
    /// a great deal for a turn, and it is over a turn that the difference
    /// between two strokes is actually visible.
    ///
    /// That is the real difference from <see cref="BotWeights"/>, more than
    /// being neural: the linear evaluator scores a STROKE, adding up rewards for
    /// things that just happened, and every one of those rewards had to be
    /// invented. This scores a POSITION, and the only thing it was ever told is
    /// who went on to win.
    /// </summary>
    public sealed class Net
    {
        public const int Hidden = 64;

        /// <summary>
        /// What a finished game is worth, in the same points the net predicts.
        ///
        /// Not predicted -- KNOWN, and so never asked of the network. Comfortably
        /// more than any run of hoops could be worth over the horizon, so that
        /// winning always outranks playing on.
        /// </summary>
        public const double Won = 12.0;

        readonly double[] w1, b1, w2, b2, w3;
        double b3;

        public Net(double[] w1, double[] b1, double[] w2, double[] b2, double[] w3, double b3)
        {
            this.w1 = w1; this.b1 = b1;
            this.w2 = w2; this.b2 = b2;
            this.w3 = w3; this.b3 = b3;
        }

        public static int Weights =>
            Sight.Size * Hidden + Hidden + Hidden * Hidden + Hidden + Hidden + 1;

        /// <summary>
        /// A net that has learned nothing, for training to start from.
        ///
        /// The scale of the random start matters more than it looks: too large
        /// and every unit saturates before it has seen anything, too small and
        /// the signal dies going forward. One over the square root of the fan-in
        /// keeps the variance of a layer's output about the variance of its
        /// input, which is the whole trick.
        /// </summary>
        public static Net Fresh(int seed)
        {
            var rng = new Random(seed);

            double[] Fill(int n, int fanIn)
            {
                double scale = Math.Sqrt(2.0 / Math.Max(1, fanIn));   // ReLU's half
                var a = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                    double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
                    a[i] = g * scale;
                }
                return a;
            }

            return new Net(
                Fill(Sight.Size * Hidden, Sight.Size), new double[Hidden],
                Fill(Hidden * Hidden, Hidden), new double[Hidden],
                Fill(Hidden, Hidden), 0);
        }

        // ---- what it thinks -------------------------------------------------

        /// <summary>
        /// What <paramref name="me"/>'s side is about to gain from here.
        ///
        /// A finished game is not asked about. The network would give some
        /// number and be roughly right, and "roughly right" about the only
        /// certainty in the game is worse than useless -- a search weighing a
        /// certain win against a good position would decline to win.
        /// </summary>
        public double Value(Game game, int me)
        {
            if (game.Winner != null)
            {
                foreach (int w in game.Winner) if (Bot.SameSide(game, me, w)) return Won;
                return -Won;
            }

            var x = Sight.Buffer();
            Sight.Read(game, me, x);
            return Value(x);
        }

        /// <summary>The forward pass, on an already-encoded position.</summary>
        public double Value(double[] x)
        {
            var h1 = new double[Hidden];
            var h2 = new double[Hidden];
            Forward(x, h1, h2, out double y);
            return y;
        }

        /// <summary>
        /// Forward, keeping the hidden layers, which training needs to go
        /// backwards through and inference throws away.
        /// </summary>
        public void Forward(double[] x, double[] h1, double[] h2, out double y)
        {
            for (int j = 0; j < Hidden; j++)
            {
                double s = b1[j];
                int at = j * Sight.Size;
                for (int i = 0; i < Sight.Size; i++) s += w1[at + i] * x[i];
                h1[j] = s > 0 ? s : 0;
            }

            for (int j = 0; j < Hidden; j++)
            {
                double s = b2[j];
                int at = j * Hidden;
                for (int i = 0; i < Hidden; i++) s += w2[at + i] * h1[i];
                h2[j] = s > 0 ? s : 0;
            }

            double o = b3;
            for (int i = 0; i < Hidden; i++) o += w3[i] * h2[i];

            // Left as it is. A return in points is not bounded to nought and
            // one, and squashing it would flatten exactly the differences worth
            // seeing. Fitted by least squares, whose gradient at the output is
            // the same one line as before.
            y = o;
        }

        static double Clamp(double v) => v > 30 ? 30 : v < -30 ? -30 : v;

        // ---- the parts, for training ---------------------------------------

        internal double[] W1 => w1;
        internal double[] B1 => b1;
        internal double[] W2 => w2;
        internal double[] B2 => b2;
        internal double[] W3 => w3;
        internal double B3 { get => b3; set => b3 = value; }

        // ---- reading and writing -------------------------------------------

        /// <summary>
        /// Every weight, one a line, with a header saying what shape they are.
        ///
        /// Text rather than a binary blob for the same reason the linear weights
        /// are text: a trained thing that cannot be diffed, eyeballed or
        /// committed sensibly is a thing nobody can reason about later.
        /// </summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("# croquet value net\n");
            sb.Append($"inputs {Sight.Size}\nhidden {Hidden}\n\n");

            void Put(string name, double[] a)
            {
                sb.Append(name).Append('\n');
                for (int i = 0; i < a.Length; i++)
                    sb.Append(a[i].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }

            Put("w1", w1); Put("b1", b1);
            Put("w2", w2); Put("b2", b2);
            Put("w3", w3);
            sb.Append("b3\n").Append(b3.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Reads back what ToText wrote, or null if it does not fit.</summary>
        public static Net FromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var lines = text.Split('\n');
            int inputs = 0, hidden = 0;

            var w1 = new double[0]; var b1 = new double[0];
            var w2 = new double[0]; var b2 = new double[0];
            var w3 = new double[0]; double b3 = 0;

            double[] into = null;
            int fill = 0;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line.StartsWith("inputs ")) { int.TryParse(line.Substring(7), out inputs); continue; }
                if (line.StartsWith("hidden ")) { int.TryParse(line.Substring(7), out hidden); continue; }

                switch (line)
                {
                    case "w1": into = w1 = new double[inputs * hidden]; fill = 0; continue;
                    case "b1": into = b1 = new double[hidden]; fill = 0; continue;
                    case "w2": into = w2 = new double[hidden * hidden]; fill = 0; continue;
                    case "b2": into = b2 = new double[hidden]; fill = 0; continue;
                    case "w3": into = w3 = new double[hidden]; fill = 0; continue;
                    case "b3": into = null; continue;
                }

                if (!double.TryParse(line, NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out double v)) continue;

                if (into == null) b3 = v;
                else if (fill < into.Length) into[fill++] = v;
            }

            // A net trained against a different encoding is not a net that has
            // learned less -- it is reading numbers that mean something else,
            // and would play nonsense with total confidence.
            if (inputs != Sight.Size || hidden != Hidden) return null;
            if (w1.Length == 0 || w2.Length == 0 || w3.Length == 0) return null;

            return new Net(w1, b1, w2, b2, w3, b3);
        }
    }
}
