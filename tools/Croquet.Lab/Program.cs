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

    // A croquet stroke starts with the striker placed against the ball it
    // roqueted. It is placed on the side the striker is already on, which is
    // the natural read and saves the player an interaction.
    if (game.Stroke == StrokeKind.Croquet)
    {
        var from = game.World.Balls[game.Striker].Pos - game.World.Balls[game.CroquetFrom].Pos;
        game.TakeCroquet(from);
    }

    int striker = game.Striker;
    StrokeKind was = game.Stroke;
    var before = game.World.Balls.Select(b => b.Pos).ToArray();

    // Replay the settled shot frame by frame for the animation. The rules have
    // already been applied to the world by Play, so the frames are rebuilt from
    // the same starting state and the same input -- which lands in exactly the
    // same place, because the sim is deterministic. That equality is asserted
    // in SimTests.Replay_survives_being_split_across_frames.
    var scratch = new Ball[before.Length];
    for (int i = 0; i < before.Length; i++)
    {
        scratch[i] = new Ball(before[i]);
        scratch[i].InPlay = game.World.Balls[i].InPlay;
    }

    var result = game.Play(new Vec2(r.Dx, r.Dy), r.Power);

    var scratchWorld = new World(scratch, game.World.Field, spec);
    scratchWorld.ClearShot();
    scratch[striker].Vel = new Vec2(r.Dx, r.Dy).Normalized * r.Power;

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
        roqueted = result.Roqueted,
        wentOut = result.WentOut,
        peggedOut = result.PeggedOut,
        faulted = result.Faulted,
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
    croquetFrom = game.CroquetFrom,
    winner = game.Winner,
    friction = spec.Friction,
    restitution = spec.Restitution,
    obstacleRestitution = spec.ObstacleRestitution,
    balls = game.World.Balls.Select((b, i) => new
    {
        x = b.Pos.X,
        y = b.Pos.Y,
        inPlay = b.InPlay,
        point = game.States[i].Point,
        target = Course.IsFinished(game.States[i].Point)
                 ? "round" : Course.Labels[game.States[i].Point],
        dead = game.States[i].Dead.OrderBy(d => d).ToArray()
    })
};

static Game NewGame(int count, CourtSpec spec)
{
    var field = Field.NineWicket();
    var balls = new Ball[count];

    // Nine-wicket starts from between the home peg and the first wicket,
    // spread across the centre line so nobody starts touching.
    double x = (field.HomePeg.X + field.Hoops[0].Center.X) / 2;
    double y0 = field.HomePeg.Y - (count - 1) * 0.16;
    for (int i = 0; i < count; i++) balls[i] = new Ball(new Vec2(x, y0 + i * 0.32));

    return new Game(new World(balls, field, spec));
}

record NewRequest(int Balls);
record FeelRequest(double Friction, double Restitution, double ObstacleRestitution);
record PlayRequest(double Dx, double Dy, double Power);
