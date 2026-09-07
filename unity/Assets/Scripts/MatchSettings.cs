using Croquet.Core;

/// <summary>
/// What the start menu chose, carried into the game.
///
/// The menu and the game are separate scenes, so something has to survive the
/// load between them. A handful of statics is the honest answer here: this is
/// a choice made once and read once, and a ScriptableObject or a DontDestroyOnLoad
/// singleton would be more machinery around the same four values.
///
/// **Physics is deliberately not here.** How the lawn plays is what the game
/// IS -- the same everywhere, the same for everybody, and not something one
/// player sets before a match any more than a tennis ball's bounce is. Those
/// numbers live on <see cref="CroquetGame"/> where they can be tuned during
/// development and then left alone.
/// </summary>
public static class MatchSettings
{
    public static Variant Variant = Variant.NineWicket;

    /// <summary>Balls in play. Association croquet settles this itself.</summary>
    public static int Balls = 6;

    /// <summary>0 is every ball for itself.</summary>
    public static int Teams = 0;

    /// <summary>
    /// Who plays each ball, in playing order. Blue is yours and the rest are
    /// the machine's, so pressing Play without touching anything gives a game
    /// with an opponent in it.
    /// </summary>
    public static Hand[] Hands =
    {
        Hand.Human, Hand.Casual, Hand.Casual, Hand.Casual, Hand.Casual, Hand.Casual
    };

    /// <summary>How many balls this variant actually plays with.</summary>
    public static int BallsInPlay =>
        Variant == Variant.SixWicket ? 4 : UnityEngine.Mathf.Clamp(Balls, 2, 6);

    /// <summary>Sides, with a split that cannot divide the balls treated as none.</summary>
    public static int SidesInPlay
    {
        get
        {
            if (Variant == Variant.SixWicket) return 2;
            return Teams >= 2 && BallsInPlay % Teams == 0 ? Teams : 0;
        }
    }
}
