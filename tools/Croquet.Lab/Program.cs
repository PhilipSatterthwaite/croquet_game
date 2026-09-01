using System.Diagnostics;
using Croquet.Core;

// ---------------------------------------------------------------------------
// The lab: a browser front end onto the real simulation and the real rules.
//
// This exists because `dotnet test` proves the physics is CORRECT and says
// nothing about whether it FEELS right, and feel is decided by hand. Rather
// than wait for Unity, the page drives Croquet.Core directly -- the very code
// the game will ship -- so numbers found here transfer with no translation.
//
// Shots are simulated whole on the strike and returned as frames for the
// browser to play back. That is not a shortcut: a deterministic sim knows the
// outcome the moment the ball is struck, so the real game will work the same
// way, and so will online play and the AI's search.
//
// One game, one player, no sessions. It is a dev tool.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

const double FrameDt = 1.0 / 60.0;   // playback rate; browser speed is separate
const int MaxFrames = 3600;

var spec = new CourtSpec();
Game game = NewGame(6, spec);

app.MapGet("/api/field", () =>
{
    var f = game.World.Field;
    return Results.Ok(new
    {
        width = spec.Width,
        height = spec.Height,
        ballRadius = spec.BallRadius,
        pegRadius = f.PegRadius,
        malletHead = spec.MalletHead,
        startSpot = new[] { f.StartSpot.X, f.StartSpot.Y },
        homePeg = new[] { f.HomePeg.X, f.HomePeg.Y },
        turningPeg = new[] { f.TurningPeg.X, f.TurningPeg.Y },
        hoops = f.Hoops.Select(h => new
        {
            x = h.Center.X,
            y = h.Center.Y,
            halfGap = h.HalfGap,
            wire = h.WireRadius
        }),
        // Which hoop each course point is, so the page can point at the target.
        points = Enumerable.Range(0, Field.TotalPoints).Select(p => new
        {
            label = Course.Labels[p],
            peg = f.IsPeg(p),
            hoop = f.HoopFor(p),
            dir = f.DirectionFor(p),
            x = f.TargetFor(p).X,
            y = f.TargetFor(p).Y
        })
    });
});

app.MapGet("/api/state", () => Results.Ok(Snapshot()));

app.MapPost("/api/new", (NewRequest r) =>
{
    game = NewGame(Math.Clamp(r.Balls, 2, 6), spec);
    return Results.Ok(Snapshot());
});

app.MapPost("/api/feel", (FeelRequest r) =>
{
    // Tuning mid-game is the point of the lab, so this deliberately mutates
    // the live spec rather than starting a new game.
    spec.Friction = r.Friction;
    spec.Restitution = r.Restitution;
    spec.ObstacleRestitution = r.ObstacleRestitution;
    return Results.Ok(Snapshot());
});

app.MapPost("/api/play", (PlayRequest r) =>
{
    if (game.Winner != null) return Results.BadRequest("the game is over");

    int striker = game.Striker;
    StrokeKind was = game.Stroke;

    // Where every ball stands as the stroke begins, and which one is about to
    // be set moving. A bonus stroke moves the striker to its placement first,
    // and a foot shot drives the OTHER ball while the striker is held.
    var before = game.World.Balls.Select(b => b.Pos).ToArray();
    var wasInPlay = game.World.Balls.Select(b => b.InPlay).ToArray();
    int moved = striker;

    StrokeResult result;
    if (was == StrokeKind.Bonus)
    {
        if (!Enum.TryParse<BonusWay>(r.Way, true, out var way))
            return Results.BadRequest(
                "a bonus stroke needs a way: malletHead, footShot, croquetShot or whereItLies");

        var place = new Vec2(r.PlaceX, r.PlaceY);
        before[striker] = game.BonusPlacement(way, place);
        if (way == BonusWay.FootShot) moved = game.RoquetedBall;

        result = game.PlayBonus(way, place, new Vec2(r.Dx, r.Dy), r.Power);
    }
    else
    {
        result = game.Play(new Vec2(r.Dx, r.Dy), r.Power);
    }

    // Replay the settled shot frame by frame for the animation. The rules have
    // already been applied to the world, so the frames are rebuilt from the
    // same starting state and the same input -- which lands in exactly the same
    // place, because the sim is deterministic. That equality is asserted in
    // SimTests.Replay_survives_being_split_across_frames.
    var scratch = new Ball[before.Length];
    for (int i = 0; i < before.Length; i++)
    {
        scratch[i] = new Ball(before[i]);
        scratch[i].InPlay = wasInPlay[i];
    }

    var scratchWorld = new World(scratch, game.World.Field, spec);
    scratchWorld.ClearShot();
    scratch[moved].Vel = new Vec2(r.Dx, r.Dy).Normalized * r.Power;

    var frames = new List<double[]>(256) { Snap(scratch) };
    int steps = 0;
    while (steps < MaxFrames && Sim.Step(scratchWorld, FrameDt))
    {
        steps++;
        frames.Add(Snap(scratch));
    }

    return Results.Ok(new
    {
        frames,
        seconds = steps * FrameDt,
        stroke = was.ToString(),
        scored = result.PointsScored.Select(p => Course.Labels[p]),
        othersScored = result.OthersScored.Select(o => new { ball = o.Ball, label = Course.Labels[o.Point] }),
        roqueted = result.Roqueted,
        touched = result.TouchedButNoRoquet,
        broughtIn = result.BroughtIn,
        peggedOut = result.PeggedOut,
        shotsLeft = result.ShotsLeft,
        endedByOutOfBounds = result.EndedByOutOfBounds,
        turnEnded = result.TurnEnded,
        state = Snapshot()
    });

    static double[] Snap(Ball[] b)
    {
        var f = new double[b.Length * 2];
        for (int i = 0; i < b.Length; i++)
        {
            f[i * 2] = Math.Round(b[i].Pos.X, 4);
            f[i * 2 + 1] = Math.Round(b[i].Pos.Y, 4);
        }
        return f;
    }
});

const string url = "http://localhost:5055";
app.Urls.Add(url);

Console.WriteLine($"Croquet lab  ->  {url}");
Console.WriteLine("Ctrl+C to stop.");
if (!args.Contains("--no-open"))
{
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch { /* no browser here; the URL above still works */ }
}

app.Run();

object Snapshot() => new
{
    striker = game.Striker,
    stroke = game.Stroke.ToString(),
    roquetedBall = game.RoquetedBall,
    shotsLeft = game.ShotsLeft,
    winner = game.Winner,
    friction = spec.Friction,
    restitution = spec.Restitution,
    obstacleRestitution = spec.ObstacleRestitution,
    balls = game.World.Balls.Select((b, i) => new
    {
        x = b.Pos.X,
        y = b.Pos.Y,
        inPlay = b.InPlay,
        started = game.States[i].Started,
        finished = game.States[i].Finished,
        point = game.States[i].Point,
        target = Course.IsFinished(game.States[i].Point)
                 ? "round" : Course.Labels[game.States[i].Point],
        dead = game.States[i].Dead.OrderBy(d => d).ToArray()
    })
};

// Positions are left to Game: balls are not laid out on the lawn at all, and
// each comes on from the starting spot as its first turn arrives.
static Game NewGame(int count, CourtSpec spec)
{
    var balls = new Ball[count];
    for (int i = 0; i < count; i++) balls[i] = new Ball(Vec2.Zero);
    // House rules: deadness carries over, and sending a ball out ends the turn.
    return new Game(new World(balls, Field.NineWicket(), spec), null, new RuleOptions());
}

record NewRequest(int Balls);
record FeelRequest(double Friction, double Restitution, double ObstacleRestitution);

/// <param name="Way">
/// malletHead, footShot, croquetShot or whereItLies — only for a bonus stroke.
/// </param>
/// <param name="PlaceX">
/// Direction from the roqueted ball to where the striker is set down. The angle
/// between this and the aim is what makes a croquet shot split.
/// </param>
record PlayRequest(double Dx, double Dy, double Power,
                   string Way = null, double PlaceX = -1, double PlaceY = 0);
