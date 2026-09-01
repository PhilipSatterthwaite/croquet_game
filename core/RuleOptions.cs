namespace Croquet.Core
{
    /// <summary>
    /// Variations from the USCA "Challenging Optional Rules" section. The basic
    /// game has all of these off; a house game turns on whichever it likes, and
    /// every one of them has to be agreed before play starts.
    ///
    /// They live here rather than as constants in <see cref="Game"/> so a test
    /// can pin the exact combination it means, and so turning one on is a
    /// decision recorded in one place rather than an edit buried in the engine.
    /// </summary>
    public sealed class RuleOptions
    {
        /// <summary>
        /// Option 1, "carry over deadness". Deadness survives the end of the
        /// turn and lifts only when the ball clears its next wicket.
        ///
        /// Off (the basic rule) it also lapses at the start of the next turn,
        /// which makes roquets cheap and turns short. On, a ball that has used
        /// up its roquets stays used up, and clearing the next wicket becomes
        /// the thing the whole turn is about.
        /// </summary>
        public bool CarryOverDeadness = true;

        /// <summary>
        /// Option 2A. Sending any ball off the lawn ends the turn, whatever the
        /// stroke had earned. Off (the basic rule) there is no penalty at all.
        /// The ball is brought back in either way.
        /// </summary>
        public bool OutOfBoundsEndsTurn = true;

        /// <summary>The basic game: no options in force.</summary>
        public static RuleOptions Basic => new RuleOptions
        {
            CarryOverDeadness = false,
            OutOfBoundsEndsTurn = false
        };
    }
}
