using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Croquet.Core
{
    /// <summary>
    /// What the bot thinks a position is worth, as numbers that can be changed.
    ///
    /// These are the bot's JUDGEMENT -- not how well it strikes the ball, which
    /// is <see cref="Bot.AimError"/> and friends, but what it is trying to
    /// achieve. Running a hoop is worth twelve hundred and shoving an opponent
    /// a metre off its line is worth twenty-two, and the ratio between those two
    /// numbers is a claim about croquet: that scoring is worth roughly fifty
    /// times a small disruption. Every number here is a claim of that kind.
    ///
    /// They started as guesses. Pulling them out into one vector is what lets
    /// them be MEASURED instead -- see tools/Croquet.Train, which plays weight
    /// sets against each other and keeps whichever wins more games. The features
    /// are hand-built because they encode croquet; only their sizes are learned.
    ///
    /// Winning itself is deliberately NOT in here. A weight on winning would be
    /// a preference to be traded off against other preferences, and it is not
    /// one -- it is the thing the other numbers exist to bring about, and the
    /// only thing training scores. It stays fixed and enormous.
    /// </summary>
    public sealed class BotWeights
    {
        /// <summary>The numbers, in the order <see cref="Names"/> gives them.</summary>
        readonly double[] w;

        /// <summary>
        /// What each number means. Ordered, because a training run writes them
        /// out as a list and something has to say which is which.
        /// </summary>
        public static readonly string[] Names =
        {
            "hoop",             // 0  a point scored by the striker
            "partnerScored",    // 1  a point scored for a partner by this stroke
            "opponentScored",   // 2  ...or handed to an opponent
            "peggedOut",        // 3  a ball staked out
            "roquet",           // 4  a hit, which is two more strokes
            "turnEnded",        // 5  the stroke gave the turn away
            "wentOut",          // 6  ...and did it by going off the lawn
            "toPoint",          // 7  per metre from the striker's own next point
            "inFront",          // 8  standing in front of the hoop, square to it
            "inFrontDepth",     // 9  how far back that bonus reaches, in metres
            "inFrontWidth",     // 10 ...and how far off the line, in metres
            "toNearest",        // 11 per metre to the nearest ball still live
            "nearestCap",       // 12 beyond which being further changes nothing
            "partnerCloser",    // 13 per metre a partner was moved toward its point
            "opponentCloser",   // 14 per metre an opponent was moved toward its own
            "shoveCap",         // 15 metres beyond which moving a ball stops paying
            "partnerSentOff",   // 16 driving a partner off the lawn
            "wastedShot",       // 17 a stroke in hand that achieved nothing
            "edgeBand",         // 18 how near the boundary counts as near, in metres
            "edgePenalty"       // 19 ...and what being there costs
        };

        public static int Count => Names.Length;

        /// <summary>
        /// What the bot plays by, learned by self-play.
        ///
        /// Measured, not guessed: 34,000 games under tools/Croquet.Train, then
        /// **73.5% over 400 games against the hand-tuned set below** (95%
        /// confident: 69.0% to 77.6%). Pasted in rather than loaded from a file
        /// because core/ does no file IO -- that is what keeps it consumable by
        /// Unity under IL2CPP.
        ///
        /// Read against <see cref="HandTuned"/> it says three things, and all
        /// three are about croquet rather than about software:
        ///
        /// * **Position is worth far more than I thought.** Standing square in
        ///   front of your hoop scores 1127 against a hoop's 1200 -- because
        ///   being in front of a hoop IS a hoop next stroke, plus the
        ///   continuation that comes with it. I had it at a fifth of that.
        /// * **Defence beats offence.** Letting an opponent score is 2907, which
        ///   is 2.4 times what scoring yourself is worth. Nothing in the hand
        ///   tuning came close to saying that.
        /// * **Losing the turn was double-counted.** It is the one number that
        ///   went DOWN, by a third: the positional terms already carry what a
        ///   lost turn costs, so the flat penalty on top was charging twice.
        ///
        /// **Retrained once the search could convert a roquet.** The first set
        /// was learned while the bot could not see the croquet shot or value the
        /// strokes a roquet buys, so it was knowingly stale. A second run under
        /// the fixed search, interrupted part way, then played the first set
        /// head to head: **58.3% over 400 games** (95% confident: 53.4% to
        /// 63.0%). Against the set it replaced, not against the hand-tuned
        /// guesses -- beating those is a bar the old set already cleared by 73%.
        ///
        /// The flat bonus for a roquet fell from 640 to 375. The likely reading
        /// is that the free plies now score the two strokes a roquet earns, so a
        /// large flat bonus on top was counting them twice -- but that is an
        /// interpretation, and the measurement is only the win rate.
        /// </summary>
        public static BotWeights Default => new BotWeights(new double[]
        {
            1200,       // hoop -- pinned; every other number is read against it
            1684.2557,  // partnerScored
            2899.3314,  // opponentScored
            13829.2669, // peggedOut
            374.7699,   // roquet
            630.719,    // turnEnded
            1894.8499,  // wentOut
            57.1192,    // toPoint
            1072.9582,  // inFront
            8.3475,     // inFrontDepth
            0.7639,     // inFrontWidth
            29.9134,    // toNearest
            9.0311,     // nearestCap
            135.1622,   // partnerCloser
            57.4843,    // opponentCloser
            1.6821,     // shoveCap
            1486.9752,  // partnerSentOff
            891.4685,   // wastedShot
            0.6823,     // edgeBand
            546.2455    // edgePenalty
        });

        /// <summary>
        /// Where this started: numbers picked by hand, by reasoning about
        /// croquet rather than by playing it.
        ///
        /// Kept so the claim above stays checkable. A training run reports
        /// against whatever Default currently is, which means that once a
        /// learned set is adopted the improvement it represents becomes
        /// invisible -- the new baseline scores 50% against itself. This is the
        /// fixed point that does not move, so "is the bot better than it was
        /// before any of this" remains a question with an answer.
        /// </summary>
        public static BotWeights HandTuned => new BotWeights(new double[]
        {
            1200, 900, 750, 3000, 450, 700, 500, 22, 260, 3.0,
            0.9, 6, 12, 30, 22, 3.0, 250, 150, 1.0, 120
        });

        /// <summary>
        /// Floors for the numbers that are DISTANCES rather than values.
        ///
        /// A weight may go to zero -- that is the training saying it does not
        /// matter -- but a band a metre wide may not, because zero width means
        /// the term silently stops existing rather than stops mattering, and a
        /// negative one means it fires inside out.
        /// </summary>
        static readonly Dictionary<int, double> Floors = new Dictionary<int, double>
        {
            [9] = 0.3,    // inFrontDepth
            [10] = 0.15,  // inFrontWidth
            [12] = 1.0,   // nearestCap
            [15] = 0.5,   // shoveCap
            [18] = 0.2    // edgeBand
        };

        /// <summary>
        /// Which of these are METRES rather than points.
        ///
        /// The distinction matters the moment anything rescales the vector: the
        /// values are only meaningful against each other and may all be doubled
        /// with no effect, but a band 0.9 metres wide is 0.9 metres wide because
        /// that is the size of a croquet lawn.
        /// </summary>
        static bool IsDistance(int i) => Floors.ContainsKey(i);

        public BotWeights(double[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length != Count)
                throw new ArgumentException($"expected {Count} weights, got {values.Length}");

            w = (double[])values.Clone();
            Clamp();
        }

        /// <summary>
        /// The same judgement, expressed with a hoop worth exactly what it is
        /// worth by default.
        ///
        /// Only RATIOS between the values mean anything -- doubling every one of
        /// them picks the same stroke every time -- so the scale is free, and a
        /// free parameter in a search is one it will wander along. Worse than
        /// wandering: the terminal win bonus is deliberately fixed and enormous,
        /// so shrinking everything else is a way to make winning relatively
        /// louder, and the cheapest path to that is a bot that values nothing at
        /// all until it can win on the stroke in front of it.
        ///
        /// Pinning the hoop closes that door and makes the numbers readable
        /// besides: every one of them is now "what this is worth compared to
        /// running a hoop", which is how a croquet player would say it.
        /// </summary>
        public BotWeights Normalised()
        {
            double hoop = Math.Abs(w[0]);
            if (hoop < 1e-6) return this;

            double scale = Default[0] / hoop;
            if (Math.Abs(scale - 1) < 1e-9) return this;

            var v = ToArray();
            for (int i = 0; i < v.Length; i++)
                if (!IsDistance(i)) v[i] *= scale;

            return new BotWeights(v);
        }

        public double this[int i] => w[i];

        public BotWeights Clone() => new BotWeights(w);

        public double[] ToArray() => (double[])w.Clone();

        /// <summary>Keeps the distances positive. Values may be anything.</summary>
        void Clamp()
        {
            foreach (var pair in Floors)
                if (w[pair.Key] < pair.Value) w[pair.Key] = pair.Value;
        }

        // ---- named, so the evaluator reads as prose ------------------------

        public double Hoop => w[0];
        public double PartnerScored => w[1];
        public double OpponentScored => w[2];
        public double PeggedOut => w[3];
        public double Roquet => w[4];
        public double TurnEnded => w[5];
        public double WentOut => w[6];
        public double ToPoint => w[7];
        public double InFront => w[8];
        public double InFrontDepth => w[9];
        public double InFrontWidth => w[10];
        public double ToNearest => w[11];
        public double NearestCap => w[12];
        public double PartnerCloser => w[13];
        public double OpponentCloser => w[14];
        public double ShoveCap => w[15];
        public double PartnerSentOff => w[16];
        public double WastedShot => w[17];
        public double EdgeBand => w[18];
        public double EdgePenalty => w[19];

        // ---- reading and writing them --------------------------------------

        /// <summary>
        /// One "name = value" a line. Text, and culture-invariant, because a
        /// trained set is a thing to read, argue with and commit -- not a blob.
        /// </summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Count; i++)
                sb.Append(Names[i]).Append(" = ")
                  .Append(w[i].ToString("0.####", CultureInfo.InvariantCulture))
                  .Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Reads what ToText wrote. Anything missing keeps its default, so a
        /// file written before a weight existed still loads.
        /// </summary>
        public static BotWeights FromText(string text)
        {
            var v = Default.ToArray();
            if (string.IsNullOrWhiteSpace(text)) return new BotWeights(v);

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string name = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                int at = Array.IndexOf(Names, name);
                if (at < 0) continue;

                if (double.TryParse(value, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out double d))
                    v[at] = d;
            }

            return new BotWeights(v);
        }

        /// <summary>
        /// How far this set has drifted from another, per weight, as a
        /// percentage. What a training run is actually reporting.
        /// </summary>
        public string CompareTo(BotWeights other)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                double from = other[i], to = w[i];
                double change = Math.Abs(from) < 1e-9 ? 0 : (to - from) / Math.Abs(from) * 100;

                sb.Append($"{Names[i],-16} {from,10:0.###} -> {to,10:0.###}");
                if (Math.Abs(change) >= 1)
                    sb.Append($"   {(change > 0 ? "+" : "")}{change:0}%");
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
