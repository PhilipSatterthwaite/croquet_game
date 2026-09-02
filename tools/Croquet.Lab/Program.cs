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

// Never let a browser cache the page.
//
// A cached index.html is indistinguishable from a broken one: the server can be
// answering perfectly while the page driving it is from an older build and does
// not even know to ask. That cost real debugging time, and on a dev tool served
// from localhost there is nothing to gain by caching anyway.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
    ctx.Response.Headers["Pragma"] = "no-cache";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

// Stamped at startup and shown in the page, so a stale page is visible at a
// glance rather than something to be deduced.
var started = DateTime.Now;

const double FrameDt = 1.0 / 60.0;   // playback rate; browser speed is separate
const int MaxFrames = 3600;

var variant = Variant.NineWicket;
var spec = Field.CourtFor(variant);
Game game = NewGame(variant, 6, 0, spec);

// Which balls the machine plays and how well it plays each. A ball missing
// from this is yours. Everything but Blue by default, so a freshly opened page
// has an opponent without being asked for one.
var seats = new Dictionary<int, string>();
for (int i = 1; i < game.World.Balls.Length; i++) seats[i] = "normal";

// One bot per strength, reused: they hold no per-game state.
var bots = new Dictionary<string, Bot>
{
    ["fast"] = Bot.Fast(),
    ["normal"] = new Bot(),
    ["strong"] = Bot.Strong()
};
Bot BotFor(int ball) =>
    seats.TryGetValue(ball, out var s) && bots.ContainsKey(s) ? bots[s] : bots["normal"];

app.MapGet("/api/field", () =>
{
    var f = game.World.Field;
    return Results.Ok(new
    {
        started = started.ToString("HH:mm:ss"),
        variant = f.Variant.ToString(),
        // Which ways the first bonus stroke may be taken. Association croquet
        // allows only the croquet shot; the USCA rules allow all four.
        bonusWays = game.Laws.FourWaysToTakeCroquet
            ? new[] { "malletHead", "footShot", "croquetShot", "whereItLies" }
            : new[] { "croquetShot" },
        width = spec.Width,
        height = spec.Height,
        ballRadius = spec.BallRadius,
        pegRadius = f.PegRadius,
        malletHead = spec.MalletHead,
        startSpot = new[] { f.StartSpot.X, f.StartSpot.Y },
        pegs = f.Pegs.Select(p => new[] { p.X, p.Y }),
        hoops = f.Hoops.Select(h => new
        {
            x = h.Center.X,
            y = h.Center.Y,
            halfGap = h.HalfGap,
            wire = h.WireRadius
        }),
        // Which hoop each course point is, so the page can point at the target.
        points = Enumerable.Range(0, f.TotalPoints).Select(p => new
        {
            label = f.Labels[p],
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
    if (!string.IsNullOrEmpty(r.Variant) && Enum.TryParse<Variant>(r.Variant, true, out var v))
        variant = v;

    // The court changes with the variant, but the feel does not: friction and
    // bounce are how the grass plays, and carry across.
    var next = Field.CourtFor(variant);
    next.Friction = spec.Friction;
    next.Restitution = spec.Restitution;
    next.ObstacleRestitution = spec.ObstacleRestitution;
    spec = next;

    // Association croquet is a four-ball game in two sides; nine wicket takes
    // whatever split divides the balls evenly.
    int count = variant == Variant.SixWicket ? 4 : Math.Clamp(r.Balls, 2, 6);
    int teams = variant == Variant.SixWicket ? 2 : r.Teams;
    if (teams < 2 || count % teams != 0) teams = 0;      // 0 means every ball for itself

    game = NewGame(variant, count, teams, spec);

    // Braced deliberately: without them the else binds to the inner if rather
    // than the outer one, and asking for no seats silently gives you none while
    // asking for some can hand the machine everything.
    var kept = new Dictionary<int, string>(seats);
    seats.Clear();
    if (r.Seats != null)
    {
        foreach (var s in r.Seats)
            if (s.Ball >= 0 && s.Ball < count) seats[s.Ball] = Strength(s.Strength);
    }
    else
    {
        for (int i = 1; i < count; i++)
            seats[i] = kept.TryGetValue(i, out var was) ? was : "normal";
    }

    return Results.Ok(Snapshot());
});

// Who the machine plays and how well, changeable mid-game rather than only
// when starting one.
app.MapPost("/api/seats", (SeatsRequest r) =>
{
    seats.Clear();
    if (r.Seats != null)
        foreach (var s in r.Seats)
            if (s.Ball >= 0 && s.Ball < game.World.Balls.Length)
                seats[s.Ball] = Strength(s.Strength);

    return Results.Ok(Snapshot());
});

static string Strength(string s) =>
    s == "fast" || s == "strong" ? s : "normal";

// One stroke by the machine, in the same shape as /api/play so the page can
// animate it identically. The browser calls this while it is a bot's turn.
app.MapPost("/api/bot", () =>
{
    if (game.Winner != null) return Results.BadRequest("the game is over");
    if (!seats.ContainsKey(game.Striker)) return Results.BadRequest("not a bot's turn");

    var move = BotFor(game.Striker).Choose(game);
    // Returned straight through: PlayMove already produces an IResult, and
    // wrapping it again buries the whole response under a "value" field.
    return PlayMove(new PlayRequest(move.Aim.X, move.Aim.Y, move.Power,
                                    move.Way.ToString(),
                                    move.Placement.X, move.Placement.Y),
                    move.Note, move.Score);
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
    if (seats.ContainsKey(game.Striker)) return Results.BadRequest("that ball is the machine's");
    return PlayMove(r, null, 0);
});

// Shared by the human and the machine, so a bot stroke and a played one go
// through exactly the same path and animate the same way.
IResult PlayMove(PlayRequest r, string note, double score)
{
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
        scored = result.PointsScored.Select(p => game.World.Field.Labels[p]),
        othersScored = result.OthersScored.Select(
            o => new { ball = o.Ball, label = game.World.Field.Labels[o.Point] }),
        roqueted = result.Roqueted,
        touched = result.TouchedButNoRoquet,
        broughtIn = result.BroughtIn,
        peggedOut = result.PeggedOut,
        shotsLeft = result.ShotsLeft,
        endedByOutOfBounds = result.EndedByOutOfBounds,
        turnEnded = result.TurnEnded,
        by = striker,
        note,
        score,
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
}

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
    strikerIsBot = seats.ContainsKey(game.Striker),
    // Ball -> strength, or null where it is yours.
    seats = Enumerable.Range(0, game.World.Balls.Length)
                      .Select(i => seats.TryGetValue(i, out var s) ? s : null).ToArray(),
    teams = game.Side == null ? 0 : game.Side.Distinct().Count(),
    side = game.Side,
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
        target = game.States[i].Finished
                 ? "round" : game.World.Field.Labels[game.States[i].Point],
        dead = game.States[i].Dead.OrderBy(d => d).ToArray()
    })
};

// Positions are left to Game: balls are not laid out on the lawn at all, and
// each comes on from the starting spot as its first turn arrives.
static Game NewGame(Variant variant, int count, int teams, CourtSpec spec)
{
    var balls = new Ball[count];
    for (int i = 0; i < count; i++) balls[i] = new Ball(Vec2.Zero);

    // Sides are cut ACROSS the playing order rather than along it, the way
    // partners alternate on a lawn: with four balls and two sides that is blue
    // and black against red and yellow, exactly as the laws set it out.
    int[] side = null;
    if (teams >= 2)
    {
        side = new int[count];
        for (int i = 0; i < count; i++) side[i] = i % teams;
    }

    var options = variant == Variant.SixWicket ? RuleOptions.Basic : new RuleOptions();
    return new Game(new World(balls, Field.For(variant), spec), side, options);
}

record Seat(int Ball, string Strength);
record NewRequest(int Balls, string Variant = null, Seat[] Seats = null, int Teams = 0);
record SeatsRequest(Seat[] Seats);
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
