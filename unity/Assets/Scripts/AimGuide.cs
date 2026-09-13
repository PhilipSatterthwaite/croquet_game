using Croquet.Core;
using UnityEngine;

/// <summary>
/// Where a stroke would actually go, worked out exactly.
///
/// This can be exact, and that is worth saying plainly. Friction in this
/// simulation slows a ball without bending it, so a struck ball travels in a
/// straight line until it meets something -- which makes the first contact a
/// ray test rather than a guess, and no cheaper approximation is needed.
///
/// What happens AT the contact is exact too. Sim.ResolvePair gives equal masses
/// an impulse along the line of centres, so the struck ball leaves along that
/// line and the striker keeps everything at right angles to it plus whatever
/// the restitution left behind. Both are computed here from the same numbers
/// the simulation uses, so the guide cannot drift from the game: if it ever
/// disagrees, one of them is wrong and it is worth knowing which.
/// </summary>
public readonly struct AimGuide
{
    public enum Meeting { Nothing, Ball, Obstacle, Edge }

    /// <summary>What the ball ran into, if anything.</summary>
    public readonly Meeting Hit;

    /// <summary>Where the striker's centre is at the moment of contact, or where it stops.</summary>
    public readonly Vector2 To;

    /// <summary>Which ball was struck, or -1.</summary>
    public readonly int Struck;

    /// <summary>Where the struck ball goes: the line of centres.</summary>
    public readonly Vector2 Onward;

    /// <summary>Where the striker goes afterwards.</summary>
    public readonly Vector2 Carries;

    /// <summary>
    /// How far each ball rolls after the contact, as a share of what the
    /// striker would have rolled had nothing been in the way.
    ///
    /// Distances, not speeds, so they can be compared directly and drawn as
    /// lengths. Distance goes as the SQUARE of speed (v^2 = 2ad), which is
    /// what makes a thin cut so lopsided: a striker glancing off at four
    /// fifths of its speed keeps nearly two thirds of its roll, while the ball
    /// it just brushed gets a hundredth of it. The pair of them add up to less
    /// than one, and should -- a contact takes energy out of the game.
    /// </summary>
    public readonly float OnwardRoll, CarriesRoll;

    AimGuide(Meeting hit, Vector2 to, int struck, Vector2 onward, Vector2 carries,
             float onwardRoll = 0, float carriesRoll = 0)
    {
        Hit = hit;
        To = to;
        Struck = struck;
        Onward = onward;
        Carries = carries;
        OnwardRoll = onwardRoll;
        CarriesRoll = carriesRoll;
    }

    /// <summary>
    /// Traces the line of a stroke as far as it is worth trusting.
    ///
    /// The length is fixed -- <paramref name="reach"/> -- and says nothing
    /// about how hard the ball is struck. Where a stroke is AIMED and how far
    /// it would travel are two different questions, and tying the line to the
    /// strength answered neither: it shortened and grew while the pull was
    /// being set, which is exactly the moment it needs to hold still.
    ///
    /// Nor does it run to whatever is in the way however far off that is. A
    /// line drawn twenty metres up the court claims an accuracy the stroke does
    /// not have; the aim is only as fine as the sighting that set it, so the
    /// line stops where that stops being true.
    /// </summary>
    public static AimGuide Trace(Game game, int striker, Vector2 from, Vector2 aim, float reach,
                                 int croquetWith = -1)
    {
        var world = game.World;
        float r = (float)world.Spec.BallRadius;

        var dir = aim.normalized;
        if (dir.sqrMagnitude < 0.5f) return new AimGuide(Meeting.Nothing, from, -1, default, default);

        // Whichever comes first: the end of the honest range, or the boundary.
        var meets = Meeting.Nothing;
        float best = reach;

        float edge = ToEdge(world.Spec, from, dir);
        if (edge < best) { best = edge; meets = Meeting.Edge; }

        int hitBall = -1;
        Vector2 hitCentre = Vector2.zero;

        // Another ball: contact when the centres are two radii apart.
        for (int i = 0; i < world.Balls.Length; i++)
        {
            if (i == striker || !world.Balls[i].InPlay) continue;

            var c = CroquetGame.ToVector(world.Balls[i].Pos);
            if (!Sweep(from, dir, c, r * 2, best, out float t)) continue;

            best = t;
            hitBall = i;
            hitCentre = c;
            meets = Meeting.Ball;
        }

        // The furniture: a hoop's uprights and the pegs. Nothing is sent on, but
        // the ball comes back off them, and where it goes is worth showing.
        Vector2 post = Vector2.zero;

        foreach (var hoop in world.Field.Hoops)
        {
            float wire = (float)hoop.WireRadius + r;
            var left = CroquetGame.ToVector(hoop.LeftPost);
            var right = CroquetGame.ToVector(hoop.RightPost);

            if (Sweep(from, dir, left, wire, best, out float tl))
            { best = tl; hitBall = -1; meets = Meeting.Obstacle; post = left; }
            if (Sweep(from, dir, right, wire, best, out float tr))
            { best = tr; hitBall = -1; meets = Meeting.Obstacle; post = right; }
        }

        foreach (var peg in world.Field.Pegs)
        {
            var at = CroquetGame.ToVector(peg);
            if (Sweep(from, dir, at, (float)world.Field.PegRadius + r, best, out float tp))
            { best = tp; hitBall = -1; meets = Meeting.Obstacle; post = at; }
        }

        var stop = from + dir * best;

        // Off a post, the same bounce Sim.Deflect gives: the normal runs from
        // the post's centre through the ball's, the part of the velocity going
        // INTO the post is turned round and cut by the obstacle's restitution,
        // and everything along its surface is kept. So a glancing touch barely
        // bends the line and loses almost nothing, while a full hit comes
        // straight back at a quarter of the roll -- which is why a hoop leg is
        // a place a ball goes to die.
        if (meets == Meeting.Obstacle)
        {
            var normal = (stop - post).normalized;
            float into = Vector2.Dot(dir, normal);
            var off = into < 0
                ? dir - normal * ((1f + (float)world.Spec.ObstacleRestitution) * into)
                : dir;

            return new AimGuide(Meeting.Obstacle, stop, -1, default,
                                off.sqrMagnitude > 1e-6f ? off.normalized : Vector2.zero,
                                0, off.sqrMagnitude);
        }

        if (hitBall < 0) return new AimGuide(meets, stop, -1, default, default);

        // Equal masses, impulse along the line of centres. The struck ball
        // leaves along it; the striker loses that much of its own speed along
        // it and keeps the rest, which is why the two do not part at a right
        // angle unless the balls are perfectly elastic.
        var n = (hitCentre - stop).normalized;
        float e = (float)world.Spec.Restitution;

        float along = Vector2.Dot(dir, n);
        var onward = n;

        // On a croquet stroke the mallet follows the striker into the other
        // ball and keeps pushing: Sim hands back a share of the blow along the
        // line of centres, and so does this, or the guide would show the back
        // ball stopping dead where the game sends it on.
        float follow = hitBall == croquetWith ? (float)world.Spec.CroquetFollow : 0f;
        var carries = dir - n * ((1f + e) * along * 0.5f - follow * along);

        // Both balls leave at some fraction of the incoming speed, and each
        // then rolls as the square of it.
        float onwardSpeed = (1f + e) * 0.5f * along;
        float carriesSpeed = carries.magnitude;

        return new AimGuide(Meeting.Ball, stop, hitBall, onward,
                            carries.sqrMagnitude > 1e-6f ? carries.normalized : Vector2.zero,
                            onwardSpeed * onwardSpeed, carriesSpeed * carriesSpeed);
    }

    /// <summary>
    /// How far the ball can go before it leaves the court. Not a collision --
    /// out of bounds carries no penalty here -- but it is where the line has to
    /// stop, so that there is always something at the end of it.
    /// </summary>
    public static float ToEdge(CourtSpec court, Vector2 from, Vector2 dir)
    {
        float best = float.MaxValue;

        best = Mathf.Min(best, Edge(from.x, dir.x, 0));
        best = Mathf.Min(best, Edge(from.x, dir.x, (float)court.Width));
        best = Mathf.Min(best, Edge(from.y, dir.y, 0));
        best = Mathf.Min(best, Edge(from.y, dir.y, (float)court.Height));

        return best == float.MaxValue ? (float)(court.Width + court.Height) : best;

        static float Edge(float at, float step, float line)
        {
            if (Mathf.Abs(step) < 1e-6f) return float.MaxValue;
            float t = (line - at) / step;
            return t > 0 ? t : float.MaxValue;
        }
    }

    /// <summary>
    /// Where a point travelling from <paramref name="from"/> first comes within
    /// <paramref name="radius"/> of <paramref name="centre"/>, if it does so
    /// within <paramref name="limit"/>.
    /// </summary>
    static bool Sweep(Vector2 from, Vector2 dir, Vector2 centre, float radius,
                      float limit, out float t)
    {
        t = 0;

        var toCentre = centre - from;
        float along = Vector2.Dot(toCentre, dir);
        if (along < 0) return false;                       // behind the stroke

        float offSq = toCentre.sqrMagnitude - along * along;
        float rSq = radius * radius;
        if (offSq > rSq) return false;                     // passes wide

        float back = Mathf.Sqrt(Mathf.Max(0, rSq - offSq));
        t = along - back;

        // A hair of slack at the start, for a ball already touching the one in
        // front -- the croquet stroke -- where rounding can put the contact a
        // few microns behind the ball it belongs in front of.
        if (t < 0 && t > -1e-4f) t = 0;

        return t >= 0 && t <= limit;
    }
}
