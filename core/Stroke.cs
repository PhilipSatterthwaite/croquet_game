using System;

namespace Croquet.Core
{
    /// <summary>
    /// One stroke, as a single value: everything a player decides and nothing
    /// else.
    ///
    /// This is the unit the whole game is built to move around. A deterministic
    /// simulation means the outcome of a stroke is already implied by the
    /// stroke, so a turn can be sent, recorded or replayed as these few numbers
    /// rather than as a list of where every ball ended up -- about forty bytes
    /// instead of a state dump, and the same forty bytes whether the shot moved
    /// one ball or all six.
    ///
    /// It is deliberately a plain readonly struct with no behaviour. What a
    /// stroke DOES is <see cref="Replay"/>'s business and what it MEANS is
    /// <see cref="Game"/>'s; this only has to survive being written down.
    /// </summary>
    public readonly struct Stroke : IEquatable<Stroke>
    {
        /// <summary>Whether this is the first bonus stroke after a roquet.</summary>
        public readonly bool IsBonus;

        /// <summary>How the bonus is taken. Meaningless unless IsBonus.</summary>
        public readonly BonusWay Way;

        /// <summary>
        /// Direction from the roqueted ball to where the striker is set down.
        /// Meaningless unless IsBonus, and ignored for WhereItLies.
        /// </summary>
        public readonly Vec2 Placement;

        /// <summary>Which way it is struck. Need not be normalised.</summary>
        public readonly Vec2 Aim;

        /// <summary>Speed given to the ball, metres per second.</summary>
        public readonly double Power;

        Stroke(bool isBonus, BonusWay way, Vec2 placement, Vec2 aim, double power)
        {
            IsBonus = isBonus;
            Way = way;
            Placement = placement;
            Aim = aim;
            Power = power;
        }

        /// <summary>An ordinary stroke, struck from where the ball lies.</summary>
        public static Stroke Ordinary(Vec2 aim, double power) =>
            new Stroke(false, BonusWay.WhereItLies, Vec2.Zero, aim, power);

        /// <summary>The first bonus stroke after a roquet, taken one of the four ways.</summary>
        public static Stroke Bonus(BonusWay way, Vec2 placement, Vec2 aim, double power) =>
            new Stroke(true, way, placement, aim, power);

        /// <summary>The stroke a bot chose, as the same value a person's would be.</summary>
        public static Stroke Of(BotMove move) =>
            move == null ? throw new ArgumentNullException(nameof(move))
            : move.IsBonus ? Bonus(move.Way, move.Placement, move.Aim, move.Power)
                           : Ordinary(move.Aim, move.Power);

        /// <summary>
        /// Whether this stroke is the right SHAPE for the game as it stands --
        /// an ordinary stroke when one is due, a bonus when one is owed.
        ///
        /// Not a judgement about whether it is a good stroke, or a legal aim.
        /// It is the check a server makes before applying something a client
        /// sent, so that a malformed message is refused rather than throwing
        /// inside the rules.
        /// </summary>
        public bool Fits(Game game) =>
            game != null &&
            game.Winner == null &&
            IsBonus == (game.Stroke == StrokeKind.Bonus) &&
            Aim.LengthSq > 0 &&
            Power > 0 &&
            !double.IsNaN(Power) && !double.IsInfinity(Power);

        public bool Equals(Stroke other) =>
            IsBonus == other.IsBonus &&
            (!IsBonus || (Way == other.Way && Placement.Equals(other.Placement))) &&
            Aim.Equals(other.Aim) &&
            Power.Equals(other.Power);

        public override bool Equals(object obj) => obj is Stroke s && Equals(s);

        public override int GetHashCode()
        {
            int h = IsBonus ? 17 : 3;
            h = h * 397 ^ Aim.GetHashCode();
            h = h * 397 ^ Power.GetHashCode();
            if (IsBonus) h = h * 397 ^ ((int)Way * 31 ^ Placement.GetHashCode());
            return h;
        }

        public override string ToString() =>
            IsBonus
                ? $"{Way} from {Placement} aim {Aim} at {Power:0.###}"
                : $"aim {Aim} at {Power:0.###}";
    }
}
