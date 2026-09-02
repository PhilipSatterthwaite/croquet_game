namespace Croquet.Core
{
    /// <summary>What ending the turn when a ball leaves the court applies to.</summary>
    public enum OutPenalty
    {
        /// <summary>No penalty at all. The USCA basic rule.</summary>
        None,

        /// <summary>Only the striker's own ball leaving ends the turn. Association croquet.</summary>
        Striker,

        /// <summary>Sending anything off ends the turn. USCA Challenging Option 2A.</summary>
        AnyBall
    }

    /// <summary>
    /// Where the two games genuinely differ. Everything not listed here they do
    /// the same way, which is more than you would expect: a roquet is worth two
    /// strokes in both, a hoop is worth one, the croqueted ball is dead until
    /// the striker scores, and the turn ends when nothing is earned.
    ///
    /// Kept apart from <see cref="RuleOptions"/>, which is about variations a
    /// house chooses. These are not choices — they are what each game is.
    /// </summary>
    public sealed class Laws
    {
        /// <summary>
        /// AC Law 19.3: however many hoop points a stroke scores, it earns ONE
        /// continuation. The USCA rules instead give two for two wickets in a
        /// stroke.
        /// </summary>
        public bool ContinuationsAreNonCumulative;

        /// <summary>
        /// AC Law 21.2: running a hoop and then hitting a live ball beyond it
        /// scores the hoop AND makes the roquet. The USCA rules say the wicket
        /// counts and the contact is ignored.
        ///
        /// (The laws qualify this by where the other ball stood relative to the
        /// jaws at the start of the stroke; that distinction is not modelled.)
        /// </summary>
        public bool HoopAndRoquetBothCount;

        /// <summary>
        /// AC allows exactly one way to take croquet: the striker's ball in
        /// contact with the roqueted ball. The USCA rules offer four.
        /// </summary>
        public bool FourWaysToTakeCroquet;

        /// <summary>
        /// Association croquet plays each turn with either ball of the side,
        /// once all four are in the game (AC Law 12.1).
        /// </summary>
        public bool ChooseEitherBall;

        public OutPenalty OutOfBounds;

        public static Laws For(Variant v, RuleOptions options) =>
            v == Variant.SixWicket
                ? new Laws
                {
                    ContinuationsAreNonCumulative = true,
                    HoopAndRoquetBothCount = true,
                    FourWaysToTakeCroquet = false,
                    ChooseEitherBall = true,
                    // AC Law 18.7 / 7.6: it is the striker's own ball going off
                    // that ends the turn, not any ball it sends off.
                    OutOfBounds = OutPenalty.Striker
                }
                : new Laws
                {
                    ContinuationsAreNonCumulative = false,
                    HoopAndRoquetBothCount = false,
                    FourWaysToTakeCroquet = true,
                    ChooseEitherBall = false,
                    OutOfBounds = options.OutOfBoundsEndsTurn ? OutPenalty.AnyBall : OutPenalty.None
                };
    }
}
