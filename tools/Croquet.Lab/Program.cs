using System.Diagnostics;
using Croquet.Core;

// ---------------------------------------------------------------------------
// The lab: a browser front end onto the real simulation.
//
// This exists because `dotnet test` proves the physics is CORRECT and says
// nothing about whether it FEELS right, and feel is decided by hand. Rather
// than wait for Unity, the page drives Croquet.Core directly -- the very code
// the game will ship -- so numbers found here transfer with no translation.
//
// The whole shot is simulated in one call and returned as a list of frames for
// the browser to play back. That is not a shortcut: it is exactly how the game
// will work, because a deterministic sim means the result is known the instant
// the shot is struck and the animation is only presentation. It is also how
// online play will work, and how the AI will search.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Frames are produced at this rate. Playback speed in the browser is a separate
// thing -- slowing the animation down must not change the physics.
const double FrameDt = 1.0 / 60.0;
const int MaxFrames = 3600;      // a minute of rolling; a real shot is a few seconds

app.MapPost("/api/shot", (ShotRequest r) =>
{
    var spec = new CourtSpec
    {
        Width = r.Spec.Width,
        Height = r.Spec.Height,
        BallRadius = r.Spec.BallRadius,
        Friction = r.Spec.Friction,
        Restitution = r.Spec.Restitution,
        SleepSpeed = r.Spec.SleepSpeed
    };

    var balls = new Ball[r.Balls.Length];
    for (int i = 0; i < balls.Length; i++)
    {
        balls[i] = new Ball(new Vec2(r.Balls[i].X, r.Balls[i].Y));
        balls[i].InPlay = r.Balls[i].InPlay;
    }

    if (r.Striker < 0 || r.Striker >= balls.Length)
        return Results.BadRequest("striker out of range");

    // Direction is normalised here so the browser can hand over a raw drag
    // vector without minding its length. Power is the only magnitude.
    var dir = new Vec2(r.Dx, r.Dy).Normalized;
    balls[r.Striker].Vel = dir * r.Power;

    var frames = new List<double[]>(256) { Snapshot(balls) };
    int steps = 0;
    while (steps < MaxFrames && Sim.Step(balls, spec, FrameDt))
    {
        steps++;
        frames.Add(Snapshot(balls));
    }

    var wentOut = new bool[balls.Length];
    var travelled = new double[balls.Length];
    for (int i = 0; i < balls.Length; i++)
    {
        wentOut[i] = balls[i].WentOut;
        travelled[i] = Math.Round(
            (balls[i].Pos - new Vec2(r.Balls[i].X, r.Balls[i].Y)).Length, 3);
    }

    return Results.Ok(new ShotResponse(frames, steps * FrameDt, wentOut, travelled,
                                       steps < MaxFrames));

    // Flat [x0,y0,x1,y1,...] rather than nested pairs: a third of the JSON for
    // the same information, and a long shot runs to a few hundred frames.
    static double[] Snapshot(Ball[] b)
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

record BallDto(double X, double Y, bool InPlay);
record SpecDto(double Width, double Height, double BallRadius,
               double Friction, double Restitution, double SleepSpeed);
record ShotRequest(BallDto[] Balls, int Striker, double Dx, double Dy, double Power, SpecDto Spec);
record ShotResponse(List<double[]> Frames, double Seconds, bool[] WentOut,
                    double[] Travelled, bool Settled);
