namespace Croquet.Core
{
    /// <summary>
    /// The nine-wicket course: one entry per point a ball can score, in the
    /// order it scores them. A ball's whole progress is one integer -- how far
    /// down this list it has got -- which is the same model the scorekeeper app
    /// uses, and it is worth keeping that way. Fourteen wicket points and two
    /// pegs, sixteen in all.
    ///
    /// Field maps each of these to the hoop that carries it and the direction
    /// it must be run in.
    /// </summary>
    public static class Course
    {
        public static readonly string[] Labels =
        {
            "Wicket 1", "Wicket 2", "Wicket 3", "Wicket 4", "Wicket 5", "Wicket 6",
            "Wicket 7",
            "Turning post",
            "Wicket 8", "Wicket 9", "Wicket 10", "Wicket 11", "Wicket 12",
            "Wicket 13", "Wicket 14",
            "Home post"
        };

        public static int Length => Labels.Length;

        /// <summary>A ball that has scored every point is round and pegged out.</summary>
        public static bool IsFinished(int pos) => pos >= Labels.Length;
    }
}
