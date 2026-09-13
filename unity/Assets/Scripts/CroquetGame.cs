using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Croquet.Core;
using UnityEngine;

/// <summary>
/// Who plays a ball. Human means this device; the rest are the machine.
///
/// <c>Net</c> is Casual's hand with the learned network mixed into its
/// judgement -- the same striking, a different opinion about what to attempt.
/// It is last so that adding it did not renumber the others, which the menu
/// cycles through by index.
/// </summary>
public enum Hand { Human, Beginner, Casual, Expert, Net }

/// <summary>What the game is doing, which is what everything else keys off.</summary>
public enum Phase
{
    /// <summary>Set aside: the pause screen is up and nothing is playing.</summary>
    Paused,

    /// <summary>Waiting for the person to line up and strike.</summary>
    Aiming,

    /// <summary>The machine is searching for a stroke.</summary>
    Thinking,

    /// <summary>The machine is taking its aim, visibly.</summary>
    Addressing,

    /// <summary>A struck shot is being played back.</summary>
    Rolling,

    /// <summary>A beat between strokes, so a turn can be followed.</summary>
    Between,

    Over
}

/// <summary>
/// The game itself: it owns the rules, the balls on screen, and the turn.
///
/// Nothing here decides anything about croquet. Croquet.Core plays the stroke
/// and applies the rules; this drives the loop, animates what came back, and
/// hands the machine its turns. The one rule it enforces is the project's:
/// every position drawn came out of the simulation, so a ball is never on
/// screen anywhere the rules did not put it.
///
/// A stroke is settled the instant it is struck and then replayed for the eye
/// -- see <see cref="Replay"/>. The animation is a rendering of an outcome that
/// is already decided, which is the same shape online play and the bot's search
/// will use.
/// </summary>
[RequireComponent(typeof(CourtView))]
public class CroquetGame : MonoBehaviour
{
    public static readonly string[] Names =
        { "Blue", "Red", "Black", "Yellow", "Green", "Orange" };

    static readonly Color[] BallColours =
    {
        new Color(0.20f, 0.42f, 0.78f),   // blue
        new Color(0.80f, 0.20f, 0.20f),   // red
        new Color(0.13f, 0.13f, 0.13f),   // black
        new Color(0.95f, 0.80f, 0.15f),   // yellow
        new Color(0.20f, 0.55f, 0.28f),   // green
        new Color(0.90f, 0.45f, 0.12f)    // orange
    };

    [Header("The game")]
    public Variant variant = Variant.NineWicket;

    [Range(2, 6)] public int ballCount = 6;

    /// <summary>0 is every ball for itself. Association croquet is always 2.</summary>
    [Range(0, 3)] public int teams = 0;

    /// <summary>
    /// Who plays each ball, in playing order. Blue is yours and the rest are
    /// the machine's, so a freshly opened game has an opponent without being
    /// asked for one.
    /// </summary>
    public Hand[] hands =
    {
        Hand.Human, Hand.Casual, Hand.Casual, Hand.Casual, Hand.Casual, Hand.Casual
    };

    [Header("Feel")]
    /// <summary>
    /// Rolling deceleration. Low is a keen lawn, high is heavy grass.
    ///
    /// Higher than CourtSpec's own default, which is only what the tests build
    /// against. What this number really sets is how LONG a stroke takes: a ball
    /// struck to roll d metres takes sqrt(2d/a) seconds to stop, so the length
    /// of the court at 0.6 is a ten-second glide. Around 1.6 puts a full-stretch
    /// shot at about six seconds, which is what one looks like.
    /// </summary>
    [Tooltip("Rolling deceleration. Low is a keen lawn, high is heavy grass.")]
    [Range(0.15f, 2.5f)] public float friction = 1.6f;

    [Range(0.3f, 1f)] public float restitution = 0.8f;
    [Range(0.1f, 0.95f)] public float obstacleRestitution = 0.5f;

    [Tooltip("How far the mallet's follow-through carries the back ball of a croquet stroke. See CourtSpec.CroquetFollow.")]
    [Range(0f, 0.6f)] public float croquetFollow = 0.2f;

    /// <summary>
    /// How far a full-strength strike rolls on an empty lawn, in metres.
    ///
    /// A distance, not a speed. Distance goes as the SQUARE of speed, so a pull
    /// that was linear in speed put four times the roll in the top half of the
    /// meter as the bottom -- every shot a player actually wants was crammed
    /// into the first quarter of the drag and everything past halfway crossed
    /// the whole court. The bot always sampled power this way (see Bot.Speed);
    /// this is the hand doing the same.
    ///
    /// A little over the long axis of the court, so the court can just be
    /// crossed at full stretch and not by accident.
    /// </summary>
    [Range(8f, 45f)] public float maxRoll = 32f;

    [Tooltip("Warn in the console if a hoop is scored by a ball that went round the outside.")]
    public bool auditScoring = true;

    [Header("Presentation")]
    [Range(0.25f, 4f)] public float playbackSpeed = 1f;

    [Range(0f, 2f)] public float pauseBetweenStrokes = 0.5f;

    /// <summary>
    /// How close the view comes in once a game starts. The menu shows the whole
    /// court behind it, which is the right picture for choosing a court and the
    /// wrong one for playing on it.
    /// </summary>
    [Range(1f, 8f)] public float playZoom = 2.6f;

    /// <summary>
    /// A ball is never drawn smaller than this on screen. At full zoom-out a
    /// true-scale ball is under a pixel, and six invisible balls is not a view
    /// of a game. Drawing only; the simulation always gets the real radius.
    /// </summary>
    [Range(2f, 20f)] public float minBallPixels = 7f;

    [Tooltip("The ring round the hoop the striker is for.")]
    public Color targetPaint = new Color(1f, 0.88f, 0.52f, 0.42f);

    [Tooltip("Degrees a second the target ring turns.")]
    [Range(-40f, 40f)] public float targetSpin = 22f;

    public Game Game { get; private set; }
    public CourtSpec Court { get; private set; }
    public Phase Phase { get; private set; } = Phase.Paused;

    /// <summary>Whether a game exists to go back to, rather than only to start.</summary>
    public bool InProgress => Game != null && Game.Winner == null && Phase != Phase.Paused;

    /// <summary>The stroke just played, for the HUD to report. Null before the first.</summary>
    public Replay Last { get; private set; }

    // Both of these are state within a turn, not settings: they are chosen
    // afresh for every bonus stroke and meaningless between them. NonSerialized
    // keeps them out of the inspector and out of the scene file, where a stale
    // value could only ever be wrong.

    /// <summary>How the person has chosen to take the bonus stroke that is owed.</summary>
    [System.NonSerialized] public BonusWay? BonusChoice;

    /// <summary>Direction from the roqueted ball to where the striker is set down.</summary>
    [System.NonSerialized] public Vec2 Placement = new Vec2(-1, 0);

    public CourtCamera Eye { get; private set; }

    CourtView court;
    readonly List<SpriteRenderer> discs = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> rims = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> glints = new List<SpriteRenderer>();

    /// <summary>The tight dark patch under each ball, where it presses in.</summary>
    readonly List<SpriteRenderer> seats = new List<SpriteRenderer>();
    SpriteRenderer marker, target, arrow;

    /// <summary>An arrow over the hoop each ball is playing for, and a dark rim behind it.</summary>
    readonly List<SpriteRenderer> bound = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> boundRim = new List<SpriteRenderer>();
    bool[] boundShown = new bool[0];

    readonly Dictionary<Hand, Bot> bots = new Dictionary<Hand, Bot>();

    [Header("The learned net")]
    [Tooltip("How loudly the net speaks against the hand-tuned weights, in hoops "
           + "a point. Zero is the net alone; training reports which value won.")]
    [Range(0f, 4f)] public float netBlend = 0.35f;

    Replay pending;
    Coroutine loop;

    /// <summary>Live search progress, so the HUD can show the machine working.</summary>
    public Bot Thinking { get; private set; }

    public bool IsBot(int ball) =>
        ball >= 0 && ball < hands.Length && hands[ball] != Hand.Human;

    public bool WaitingForYou =>
        Phase == Phase.Aiming && Game != null && !IsBot(Game.Striker);

    /// <summary>
    /// Whose stroke the interface should be describing, and which point it was
    /// for.
    ///
    /// Not the same as asking the Game, and this is the whole of why. A stroke
    /// is settled the instant it is struck: the rules run, the hoop is scored
    /// and the striker is already playing for the next one -- all before a
    /// single frame of the ball rolling has been drawn. Reading the Game live
    /// meant the marker jumped to the next hoop as the ball left the mallet,
    /// announcing the result of a shot still in the air.
    ///
    /// So while a shot is on screen these describe the world as it was when the
    /// ball was struck, and catch up when it stops.
    /// </summary>
    public int ShownStriker =>
        Phase == Phase.Rolling && Last != null ? Last.Striker
        : Game == null ? 0 : Game.Striker;

    public int ShownPoint =>
        Phase == Phase.Rolling && Last != null ? Last.PointBefore
        : Game == null ? 0 : Game.States[Game.Striker].Point;

    void Awake()
    {
        court = GetComponent<CourtView>();
        Eye = Camera.main == null ? null : Camera.main.GetComponent<CourtCamera>();
        if (Eye == null && Camera.main != null)
            Eye = Camera.main.gameObject.AddComponent<CourtCamera>();
    }

    /// <summary>
    /// The game begins as soon as the scene does.
    ///
    /// Choosing what to play is the start menu's job, in its own scene; by the
    /// time this exists the choice has been made and loading the court IS
    /// starting the game. Nothing waits for a button here.
    /// </summary>
    void Start()
    {
        variant = MatchSettings.Variant;
        ballCount = MatchSettings.Balls;
        teams = MatchSettings.Teams;
        if (MatchSettings.Hands != null && MatchSettings.Hands.Length >= 6)
            hands = (Hand[])MatchSettings.Hands.Clone();

        NewGame();
    }

    /// <summary>Sets the game aside. The pause screen is over it, not instead of it.</summary>
    public void Pause()
    {
        if (loop != null) { StopCoroutine(loop); loop = null; }
        Phase = Phase.Paused;
        pending = null;
    }

    /// <summary>Picks a paused game back up where it was left.</summary>
    public void Resume()
    {
        if (Game == null || Game.Winner != null) return;
        if (loop != null) StopCoroutine(loop);
        BonusChoice = null;
        pending = null;
        loop = StartCoroutine(Turns());
    }

    /// <summary>Starts a game from the settings as they now stand.</summary>
    public void NewGame()
    {
        if (loop != null) StopCoroutine(loop);

        // Association croquet is a four-ball game in two sides; nine wicket
        // takes whatever split divides the balls evenly.
        int count = variant == Variant.SixWicket ? 4 : Mathf.Clamp(ballCount, 2, 6);
        int sides = variant == Variant.SixWicket ? 2 : teams;
        if (sides < 2 || count % sides != 0) sides = 0;
        ballCount = count;

        Court = Field.CourtFor(variant);
        ApplyFeel();

        var balls = new Ball[count];
        for (int i = 0; i < count; i++) balls[i] = new Ball(Vec2.Zero);

        // Sides are cut ACROSS the playing order rather than along it, the way
        // partners alternate on a lawn: with four balls and two sides that is
        // blue and black against red and yellow, as the laws set it out.
        int[] side = null;
        if (sides >= 2)
        {
            side = new int[count];
            for (int i = 0; i < count; i++) side[i] = i % sides;
        }

        var options = variant == Variant.SixWicket ? RuleOptions.Basic : new RuleOptions();
        Game = new Game(new World(balls, Field.For(variant), Court), side, options);

        court.Rebuild(Game.World.Field, Court);
        BuildBalls(count);

        if (Eye != null)
        {
            Eye.zoom = playZoom;
            Eye.pan = Vector2.zero;
            Eye.Watch(Court);
            Eye.LookAt(Game.World.Balls[Game.Striker].Pos, snap: true);
        }

        Last = null;
        BonusChoice = null;
        pending = null;
        loop = StartCoroutine(Turns());
    }

    /// <summary>
    /// Feel is tuned by hand while playing, so this writes to the live spec
    /// rather than starting a new game. That is the whole point of having the
    /// numbers on screen.
    /// </summary>
    void ApplyFeel()
    {
        if (Court == null) return;
        Court.Friction = friction;
        Court.Restitution = restitution;
        Court.ObstacleRestitution = obstacleRestitution;
        Court.CroquetFollow = croquetFollow;
    }

    // ---- the loop ---------------------------------------------------------

    IEnumerator Turns()
    {
        while (true)
        {
            if (Over()) yield break;

            if (IsBot(Game.Striker)) yield return MachineStroke();
            else yield return YourStroke();

            // Checked HERE as well as at the top, and this is the one that
            // matters: the pause between strokes is there to let the eye catch
            // up before the next player addresses the ball, and when the game
            // has just been won there is no next player. Waiting it out left
            // the winning ball sitting on the lawn for half a second with
            // nothing happening, which reads as the game having failed to
            // notice.
            if (Over()) yield break;

            Phase = Phase.Between;
            yield return new WaitForSeconds(pauseBetweenStrokes);
        }
    }

    bool Over()
    {
        if (Game.Winner == null) return false;
        Phase = Phase.Over;
        return true;
    }

    IEnumerator YourStroke()
    {
        Phase = Phase.Aiming;
        pending = null;
        BonusChoice = null;

        if (Eye != null) Eye.LookAt(StrikerPoint());

        while (pending == null)
        {
            if (Game.Winner != null) yield break;

            // Seats can change mid-game, and this is the one place that would
            // not notice: handing a ball to the machine while its own turn was
            // waiting on a person left the game sitting there for ever, with
            // nobody able to play and nothing to say so.
            if (IsBot(Game.Striker)) yield break;

            yield return null;
        }

        var shot = pending;
        pending = null;
        yield return Roll(shot);
    }

    IEnumerator MachineStroke()
    {
        Phase = Phase.Thinking;
        var bot = BotFor(Game.Striker);
        Thinking = bot;

        if (Eye != null) Eye.LookAt(StrikerPoint());

        // Searched on a worker thread: a stroke takes about a hundred
        // milliseconds, which is six dropped frames if it runs on the main one.
        // It searches a CLONE, so nothing the worker touches is the live game
        // and the main thread can go on drawing it.
        var snapshot = Game.Clone();
        var task = Task.Run(() => bot.Choose(snapshot));
        while (!task.IsCompleted) yield return null;

        Thinking = null;

        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception);
            yield break;
        }

        var move = task.Result;
        if (move == null) yield break;

        // Taken with the same furniture a person uses -- the same ring, line
        // and mallet, drawn back the same distance. An earlier version had the
        // machine sight several candidate lines instead; it was dropped because
        // a second visual language for the identical act made the opponent read
        // as a different kind of thing from the player.
        Phase = Phase.Addressing;
        var aim = FindAnyObjectByType<AimControl>();
        if (aim != null) yield return aim.Address(move);

        yield return Roll(Replay.Play(Game, move));
    }

    Bot BotFor(int ball)
    {
        var hand = hands[ball];
        if (!bots.TryGetValue(hand, out var bot))
        {
            // Seeded from the hand so a level plays the same way every session,
            // and one bot per level reused: they hold no per-game state.
            bot = hand switch
            {
                Hand.Beginner => Bot.Beginner(),
                Hand.Expert => Bot.Expert(),
                Hand.Net => Learned(Bot.Casual()),
                _ => Bot.Casual()
            };
            bots[hand] = bot;
        }
        return bot;
    }

    /// <summary>
    /// Casual, with the learned network added to its judgement.
    ///
    /// The net is read here rather than in <c>core</c> because the core does no
    /// file IO at all -- that is what keeps it consumable under IL2CPP -- so
    /// the engine loads the text and hands it over as a string, which is the
    /// same shape of arrangement as pasting weights into a constant, minus the
    /// six hundred kilobytes of generated source.
    ///
    /// A missing or mismatched file is not an error worth stopping for: the bot
    /// simply plays the hand-tuned weights, which is what every other level
    /// does, and says so once in the console.
    /// </summary>
    Bot Learned(Bot bot)
    {
        if (!lookedForNet)
        {
            lookedForNet = true;

            var asset = Resources.Load<TextAsset>("net");
            if (asset == null)
                Debug.LogWarning("Hand.Net: no Assets/Resources/net.txt -- "
                               + "playing the hand-tuned weights instead");
            else
            {
                learned = Net.FromText(asset.text);
                if (learned == null)
                    Debug.LogWarning("Hand.Net: net.txt was written for a different "
                                   + "encoding than this build reads -- "
                                   + "playing the hand-tuned weights instead");
            }
        }

        bot.Net = learned;
        bot.NetBlend = netBlend;
        return bot;
    }

    Net learned;
    bool lookedForNet;

    /// <summary>
    /// Plays back a shot whose outcome is already settled. Positions come
    /// straight from the frames, interpolated between them so the playback is
    /// as smooth as the display rather than as smooth as the simulation.
    /// </summary>
    IEnumerator Roll(Replay shot)
    {
        Last = shot;
        Phase = Phase.Rolling;
        BonusChoice = null;

        if (auditScoring) AuditScoring(shot);

        float f = 0;
        int last = shot.FrameCount - 1;

        int n = Mathf.Min(discs.Count, shot.Frames[0].Length);

        // A ball this stroke pegged out is off the lawn as far as the rules are
        // concerned before a single frame of it has been drawn -- which is why
        // it used to vanish the moment it was struck. So find the frame where
        // it actually reaches its peg, show it getting there, and let it sink
        // away against the peg rather than blinking out.
        var touch = StakedAt(shot, n);
        var sunk = new float[n];

        while (f < last)
        {
            f += Time.deltaTime * Replay.FramesPerSecond * Mathf.Max(0.05f, playbackSpeed);

            int i = Mathf.Min(last, Mathf.FloorToInt(f));
            int j = Mathf.Min(last, i + 1);
            float t = Mathf.Clamp01(f - i);

            var a = shot.Frames[i];
            var b = shot.Frames[j];

            for (int k = 0; k < n; k++)
            {
                var at = Vector2.Lerp(ToVector(a[k]), ToVector(b[k]), t);

                if (touch[k] < 0) Place(k, at);
                else if (i < touch[k]) Place(k, at, force: true);
                else Sink(k, shot, touch[k], sunk);
            }

            // Follows the ball that is travelling, which on a foot shot is not
            // the striker -- and stays on the peg once it has got there, rather
            // than following the ball the film has bouncing away from it.
            if (Eye != null && shot.Struck >= 0 && shot.Struck < n)
            {
                int s = shot.Struck;
                bool staked = touch[s] >= 0 && i >= touch[s];
                Eye.LookAt(staked ? ToVector(shot.Frames[touch[s]][s])
                                  : Vector2.Lerp(ToVector(a[s]), ToVector(b[s]), t));
            }

            yield return null;
        }

        // A ball that met its peg near the end of the stroke is still sinking;
        // let it finish rather than cutting it off.
        for (bool going = true; going;)
        {
            going = false;
            for (int k = 0; k < n; k++)
                if (touch[k] >= 0 && sunk[k] < SinkSeconds)
                {
                    Sink(k, shot, touch[k], sunk);
                    going = true;
                }
            if (going) yield return null;
        }

        // Settle onto the real state rather than the last frame. A ball that
        // went off the lawn is brought back a mallet's length in by the rules
        // after the rolling stopped, so the truth is in the Game, not the film.
        ShowLive();
    }

    /// <summary>How long a pegged-out ball takes to sink away against its peg.</summary>
    const float SinkSeconds = 0.45f;

    /// <summary>
    /// Holds a pegged-out ball where it met its peg, fading and shrinking a
    /// little, then gone. The film carries on with it bouncing off the peg, but
    /// by the rules it left the game on contact, so that is where it stays.
    /// </summary>
    void Sink(int k, Replay shot, int frame, float[] sunk)
    {
        sunk[k] += Time.deltaTime;
        float fade = 1f - Mathf.Clamp01(sunk[k] / SinkSeconds);
        Place(k, ToVector(shot.Frames[frame][k]), force: fade > 0f, fade: fade);
    }

    /// <summary>
    /// For each ball, the frame it meets the peg that finished it -- or -1 for
    /// every ball this stroke did not peg out.
    ///
    /// Usually the striker, but not only: a rover driven into its own peg by
    /// somebody else is finished by that stroke too.
    /// </summary>
    int[] StakedAt(Replay shot, int n)
    {
        var at = new int[n];
        for (int k = 0; k < n; k++) at[k] = -1;

        var field = Game.World.Field;
        double reach = Court.BallRadius + field.PegRadius + 0.005;

        void Find(int ball)
        {
            if (ball < 0 || ball >= n || Game.World.Balls[ball].InPlay) return;

            int point = Game.States[ball].Total - 1;
            if (!field.IsPeg(point)) return;

            var peg = field.PegFor(point);
            at[ball] = shot.FrameCount - 1;

            for (int f = 0; f < shot.FrameCount; f++)
                if ((shot.Frames[f][ball] - peg).Length <= reach) { at[ball] = f; return; }
        }

        if (shot.Result.PeggedOut) Find(shot.Striker);

        foreach (var (ball, point) in shot.Result.OthersScored)
            if (field.IsPeg(point) && Game.States[ball].Finished) Find(ball);

        return at;
    }

    /// <summary>
    /// Complains, in the console, if a hoop was scored by a ball that was not
    /// between the uprights when it crossed the hoop's line.
    ///
    /// This is the check for "the ball went a yard wide and it counted". It
    /// reads the ANIMATION -- the frames the eye actually saw -- against the
    /// POINT the rules awarded, so if the two ever disagree it says so at the
    /// moment it happens, with the numbers, rather than leaving it as something
    /// remembered afterwards. Four full games of it under `dotnet test` have
    /// never tripped it; if it fires here, the difference is in this project
    /// and not in the rules, and that is worth knowing immediately.
    /// </summary>
    void AuditScoring(Replay shot)
    {
        var field = Game.World.Field;

        foreach (int point in shot.Result.PointsScored)
        {
            int index = field.HoopFor(point);
            if (index < 0) continue;                      // a peg is hit, not run

            var hoop = field.Hoops[index];
            double worst = 0;

            for (int f = 1; f < shot.Frames.Count; f++)
            {
                double x0 = shot.Frames[f - 1][shot.Striker].X;
                double x1 = shot.Frames[f][shot.Striker].X;

                if ((x0 - hoop.Center.X) * (x1 - hoop.Center.X) > 0 || x1 == x0) continue;

                double at = (hoop.Center.X - x0) / (x1 - x0);
                double y = shot.Frames[f - 1][shot.Striker].Y
                         + at * (shot.Frames[f][shot.Striker].Y
                                 - shot.Frames[f - 1][shot.Striker].Y);

                worst = System.Math.Max(worst, System.Math.Abs(y - hoop.Center.Y));
            }

            if (worst <= hoop.HalfGap + Game.World.Spec.BallRadius) continue;

            Debug.LogWarning(
                $"SCORING: {NameOf(shot.Striker)} was given {field.Labels[point]} but crossed " +
                $"its line {worst:0.00} m off centre -- the gap is only " +
                $"{hoop.HalfGap * 2:0.00} m wide. Hoop at ({hoop.Center.X:0.00}, " +
                $"{hoop.Center.Y:0.00}); ball finished at " +
                $"({Game.World.Balls[shot.Striker].Pos.X:0.00}, " +
                $"{Game.World.Balls[shot.Striker].Pos.Y:0.00}).");
        }
    }

    // ---- what a person hands in -------------------------------------------

    /// <summary>
    /// Strikes. Called by <see cref="AimControl"/> on release, and the only way
    /// a person's stroke enters the game.
    /// </summary>
    public bool Strike(Vec2 aim, double power)
    {
        if (Phase != Phase.Aiming || Game.Winner != null) return false;
        if (IsBot(Game.Striker)) return false;
        if (pending != null) return false;

        if (Game.Stroke == StrokeKind.Bonus)
        {
            if (BonusChoice == null) return false;
            pending = Replay.PlayBonus(Game, BonusChoice.Value, Placement, aim, power);
        }
        else
        {
            pending = Replay.Play(Game, aim, power);
        }
        return true;
    }

    /// <summary>
    /// Where the striker will actually be struck from: where it lies, or the
    /// bonus placement once one has been chosen.
    /// </summary>
    public Vec2 StrikerPoint()
    {
        var me = Game.World.Balls[Game.Striker];
        if (Game.Stroke != StrokeKind.Bonus || BonusChoice == null) return me.Pos;
        if (BonusChoice == BonusWay.WhereItLies) return me.Pos;
        return Game.BonusPlacement(BonusChoice.Value, Placement);
    }

    /// <summary>Which ways the bonus stroke may be taken in this game's laws.</summary>
    public BonusWay[] BonusWays => Game.Laws.FourWaysToTakeCroquet
        ? new[] { BonusWay.MalletHead, BonusWay.FootShot, BonusWay.CroquetShot, BonusWay.WhereItLies }
        : new[] { BonusWay.CroquetShot };

    public Color ColourOf(int ball) => BallColours[ball % BallColours.Length];

    public static string NameOf(int ball) => Names[ball % Names.Length];

    // ---- the balls on screen ----------------------------------------------

    void BuildBalls(int count)
    {
        // Under a holder of their own: the court clears and redraws its pieces
        // whenever the variant changes, and anything sharing that heap goes
        // with it.
        var root = Shapes.Holder(transform, "Balls");

        foreach (var d in discs) if (d != null) Destroy(d.gameObject);
        foreach (var r in rims) if (r != null) Destroy(r.gameObject);
        foreach (var s in seats) if (s != null) Destroy(s.gameObject);
        foreach (var g in glints) if (g != null) Destroy(g.gameObject);
        foreach (var b in bound) if (b != null) Destroy(b.gameObject);
        foreach (var b in boundRim) if (b != null) Destroy(b.gameObject);
        discs.Clear();
        rims.Clear();
        seats.Clear();
        glints.Clear();
        bound.Clear();
        boundRim.Clear();

        for (int i = 0; i < count; i++)
        {
            // Four pieces to a ball: the shadow it throws, the dark seat it
            // presses into the grass, the shaded ball itself, and the glint on
            // top. The shading comes free -- the sprite is a lit white sphere
            // and the renderer multiplies the colour through it, so one piece
            // of artwork serves all six.
            rims.Add(Shapes.Piece(root, "Ball " + i + " shadow", Shapes.Shadow,
                                  new Color(0, 0, 0, 0.45f), Layer.BallShadow));
            seats.Add(Shapes.Piece(root, "Ball " + i + " seat", Shapes.Shadow,
                                   new Color(0, 0, 0, 0.42f), Layer.BallShadow));
            discs.Add(Shapes.Piece(root, "Ball " + i, Shapes.Sphere, ColourOf(i), Layer.Ball));

            // Never tinted: multiplying can only darken, and a highlight has to
            // be brighter than the ball it sits on.
            glints.Add(Shapes.Piece(root, "Ball " + i + " glint", Shapes.Gloss,
                                    new Color(1, 1, 1, 0.5f), Layer.Gloss));

            // The ball's own colour, just short of opaque, on a dark rim that
            // keeps white and yellow readable on pale grass. The SHAPE is what
            // says "a mark, not a ball" -- a triangle among discs -- so the
            // colour can stay true. It was washed toward pale grey and made half
            // see-through first, and red came out orange.
            boundRim.Add(Shapes.Piece(root, "Ball " + i + " bound rim", Shapes.Triangle,
                                      new Color(0, 0, 0, 0.45f), Layer.BoundRim));
            bound.Add(Shapes.Piece(root, "Ball " + i + " bound", Shapes.Triangle,
                                   Marked(ColourOf(i)), Layer.Bound));
        }

        if (marker == null)
            marker = Shapes.Piece(root, "Striker", Shapes.Ring,
                                  new Color(1, 1, 1, 0.9f), Layer.Striker);

        if (target == null)
            target = Shapes.Piece(root, "Target", Shapes.Dashes, targetPaint, Layer.Target);

        if (arrow == null)
            arrow = Shapes.Piece(root, "Target direction", Shapes.Triangle, targetPaint, Layer.Target);
    }

    /// <summary>
    /// A ball's colour for a mark: the same hue and strength, just short of
    /// opaque. Blending toward any grey moves the hue as well as the strength
    /// -- a warm grey drags red toward orange -- and the more see-through it is,
    /// the more the green lawn underneath drags it further.
    /// </summary>
    static Color Marked(Color c)
    {
        c.a = 0.9f;
        return c;
    }

    /// <summary>
    /// The dashed ring's diameter before it breathes: a metre, or a size that
    /// can be seen when the whole court is in view. One number so that the ring
    /// and everything placed against it agree.
    /// </summary>
    float RingDiameter => Mathf.Max(1.0f, Eye == null ? 0f : 26f * Eye.MetresPerPixel);

    /// <summary>
    /// How far from a hoop's centre, across the line it is run along, its
    /// drawn wire reaches -- the post's true offset plus half its drawn size,
    /// which CourtView exaggerates and gives a pixel floor.
    /// </summary>
    float BarReach(Hoop h)
    {
        float px = Eye == null ? 0.01f : Eye.MetresPerPixel;
        float drawn = court == null
            ? (float)(h.WireRadius * 2)
            : Mathf.Max((float)(h.WireRadius * 2) * court.hoopScale, court.minFurniturePixels * px);
        return (float)(h.HalfGap + h.WireRadius) + drawn * 0.5f;
    }

    void LateUpdate()
    {
        ApplyFeel();
        if (Phase != Phase.Rolling) ShowLive();
        ShowMarker();
        ShowTarget();
        ShowBound();
    }

    /// <summary>
    /// A ring round the point the striker is playing for.
    ///
    /// Nine wickets look alike and the course doubles back on itself, so which
    /// one is next is not something the lawn can tell you -- the HUD names it,
    /// and this says where it is. It breathes slowly so it reads as a marker
    /// rather than as something painted on the grass.
    /// </summary>
    void ShowTarget()
    {
        if (target == null || arrow == null || Game == null) return;

        var field = Game.World.Field;
        int point = ShownPoint;

        bool show = Game.Winner == null && Phase != Phase.Paused
                    && !field.IsFinished(point);
        target.gameObject.SetActive(show);
        arrow.gameObject.SetActive(show);
        if (!show) return;

        var at = ToVector(field.TargetFor(point));

        // A slow turn and a shallow breath. Neither is loud on its own, and
        // together they are what makes a faint dashed ring findable at the edge
        // of vision without it ever competing with the balls: movement is the
        // thing the eye catches, so the marker can give up brightness for it.
        float breathe = 1f + 0.05f * Mathf.Sin(Time.time * 2.2f);
        float d = RingDiameter * breathe;

        target.Put(at.x, at.y, d, d);
        target.transform.localRotation =
            Quaternion.Euler(0, 0, Time.time * targetSpin);

        var paint = targetPaint;
        paint.a *= 0.72f + 0.28f * Mathf.Sin(Time.time * 2.2f) * 0.5f;
        target.color = paint;

        // Which way through. The course runs most hoops both ways at different
        // stages, so a ring says which hoop and nothing about the direction --
        // and the direction is the half that decides where to stand. On the
        // NEAR side, pointing in through the hoop, so it reads as "come from
        // here". It was on the far side first, to keep clear of a ball sitting
        // in front of the hoop, and read as pointing past the hoop rather than
        // through it. It stays under the balls, so a ball parked there covers
        // it rather than wearing it.
        int dir = field.IsPeg(point) ? 0 : field.DirectionFor(point);
        arrow.gameObject.SetActive(dir != 0);
        if (dir == 0) return;

        // A small filled triangle, the size of the marks over the other hoops,
        // rather than a chevron a quarter the width of the ring: it is saying
        // "this way", and the ring has already said "this one".
        float size = 11f * (Eye == null ? 0.01f : Eye.MetresPerPixel);
        arrow.transform.localPosition =
            new Vector3(at.x - dir * d * 0.3f, at.y, arrow.transform.localPosition.z);
        arrow.transform.localRotation = Quaternion.Euler(0, 0, dir > 0 ? 0f : 180f);
        arrow.transform.localScale = new Vector3(size, size, 1);

        var tip = paint;
        tip.a = Mathf.Min(1f, paint.a * 1.3f);
        arrow.color = tip;
    }

    /// <summary>
    /// A small triangular arrow in every OTHER ball's faded colour beside the
    /// hoop that ball is playing for, pointing the way it has to run it.
    ///
    /// Where everyone else is going decides most of where to leave your own
    /// ball -- in front of their hoop is in their way, near it is handing them
    /// a roquet -- and nine hoops that look alike give no clue.
    ///
    /// Always the same place for a given hoop and direction: ABOVE the hoop for
    /// a ball running it rightward, BELOW for leftward, halfway between the
    /// drawn wire and where the target ring's edge is -- whether or not the ring
    /// is actually round that hoop. A mark that moved depending on whose target
    /// the hoop was had no fixed place to be looked for. Balls on the same side
    /// of the same hoop sit side by side. A peg has no way through, so a mark
    /// for one sits above it pointing down.
    /// </summary>
    void ShowBound()
    {
        if (Game == null || bound.Count == 0) return;

        var field = Game.World.Field;
        int n = Mathf.Min(bound.Count, Game.World.Balls.Length);
        if (boundShown.Length < n) boundShown = new bool[n];

        bool live = Game.Winner == null && Phase != Phase.Paused;

        for (int b = 0; b < n; b++)
        {
            boundShown[b] = live && b != ShownStriker && Game.World.Balls[b].InPlay
                            && !field.IsFinished(Game.States[b].Point);
            bound[b].gameObject.SetActive(boundShown[b]);
            boundRim[b].gameObject.SetActive(boundShown[b]);
        }

        float px = Eye == null ? 0.01f : Eye.MetresPerPixel;
        float size = 11f * px;
        float spacing = 14f * px;
        float ring = RingDiameter * 0.5f;

        for (int b = 0; b < n; b++)
        {
            if (!boundShown[b]) continue;

            int point = Game.States[b].Point;
            int key = Where(point), side = Side(point);

            // Side by side with the others on the SAME side of the same hoop;
            // a ball running it the other way is on the other side and does not
            // push this one along.
            int slot = 0, count = 0;
            for (int o = 0; o < n; o++)
            {
                if (!boundShown[o]) continue;
                int op = Game.States[o].Point;
                if (Where(op) != key || Side(op) != side) continue;
                if (o < b) slot++;
                count++;
            }

            float reach = field.IsPeg(point)
                ? Mathf.Max((float)(field.PegRadius * 2) * (court == null ? 1f : court.pegScale),
                            (court == null ? 6f : court.minFurniturePixels) * px) * 0.5f
                : BarReach(field.Hoops[field.HoopFor(point)]);

            var at = ToVector(field.TargetFor(point));
            float across = (reach + ring) * 0.5f;
            var p = at + new Vector2((slot - (count - 1) * 0.5f) * spacing, side * across);

            var turn = Quaternion.Euler(0, 0, field.IsPeg(point) ? -90f
                                             : field.DirectionFor(point) > 0 ? 0f : 180f);
            bound[b].transform.localRotation = turn;
            boundRim[b].transform.localRotation = turn;

            boundRim[b].Put(p.x, p.y, size * 1.55f, size * 1.55f);
            bound[b].Put(p.x, p.y, size, size);
        }

        // Hoops by index and pegs below zero.
        int Where(int pt) => field.IsPeg(pt) ? -1 - field.PegIndexFor(pt) : field.HoopFor(pt);

        // Above for rightward and for a peg, below for leftward.
        int Side(int pt) => field.IsPeg(pt) || field.DirectionFor(pt) > 0 ? 1 : -1;
    }

    /// <summary>Draws the balls where the rules currently have them.</summary>
    void ShowLive()
    {
        if (Game == null) return;
        for (int i = 0; i < discs.Count && i < Game.World.Balls.Length; i++)
            Place(i, ToVector(Game.World.Balls[i].Pos));

        // Once a bonus stroke has been chosen the striker is set down against
        // the roqueted ball, and it is struck from there. Drawing it where it
        // came to rest would leave the mallet addressing an empty patch of
        // grass a foot from the ball it is about to hit.
        if (Game.Stroke == StrokeKind.Bonus && BonusChoice != null && Phase != Phase.Rolling)
            Place(Game.Striker, ToVector(StrikerPoint()));
    }

    /// <param name="force">
    /// Draw it even though the rules have already taken it off the lawn -- a
    /// ball pegging out is out of play the instant the stroke resolves, which
    /// is before a single frame of it rolling to the peg has been shown.
    /// </param>
    /// <param name="fade">1 is a ball on the lawn; toward 0 it sinks away.</param>
    void Place(int i, Vector2 at, bool force = false, float fade = 1f)
    {
        bool on = force || Game.World.Balls[i].InPlay;
        discs[i].gameObject.SetActive(on);
        rims[i].gameObject.SetActive(on);
        seats[i].gameObject.SetActive(on);
        glints[i].gameObject.SetActive(on);
        if (!on) return;

        // Set every time rather than once, so a ball that faded out at the peg
        // cannot come back into a later game still transparent.
        var paint = ColourOf(i);
        paint.a = fade;
        discs[i].color = paint;
        rims[i].color = new Color(0, 0, 0, 0.45f * fade);
        seats[i].color = new Color(0, 0, 0, 0.42f * fade);
        glints[i].color = new Color(1, 1, 1, 0.5f * fade);

        float d = BallDiameter * (0.78f + 0.22f * fade);
        discs[i].Put(at.x, at.y, d, d);

        // Two shadows, and they are doing different jobs.
        //
        // The cast shadow says where the light is. The SEAT -- small, dark,
        // barely offset, tucked under the ball -- says the ball is touching
        // something. That is the one that was missing: with only a shadow
        // thrown clear of it, a ball reads as hanging above its own shadow,
        // which is exactly what "floating" looks like. Where a ball meets a
        // lawn the grass around the contact is in shade from every direction at
        // once, and no directional shadow can say that.
        var cast = at - Shapes.Light * (d * 0.6f);
        rims[i].Put(cast.x, cast.y, d * 1.5f, d * 1.5f);

        var seat = at - Shapes.Light * (d * 0.10f);
        seats[i].Put(seat.x, seat.y, d * 0.94f, d * 0.94f);

        // The glint sits toward the light, well inside the edge.
        var lit = at + Shapes.Light * (d * 0.30f);
        glints[i].Put(lit.x, lit.y, d * 0.42f, d * 0.42f);
    }

    /// <summary>
    /// True size, but never so small on screen that it cannot be seen. This is
    /// the one place the drawing is allowed to disagree with the simulation,
    /// and it disagrees in exactly one direction.
    /// </summary>
    float BallDiameter
    {
        get
        {
            float real = (float)(Court.BallRadius * 2);
            float floor = Eye == null ? 0 : minBallPixels * Eye.MetresPerPixel;
            return Mathf.Max(real, floor);
        }
    }

    /// <summary>A ring round whoever is to play, so a turn never has to be guessed at.</summary>
    void ShowMarker()
    {
        if (marker == null || Game == null) return;

        bool show = Game.Winner == null && Phase != Phase.Rolling
                    && Game.World.Balls[Game.Striker].InPlay;
        marker.gameObject.SetActive(show);
        if (!show) return;

        var at = ToVector(StrikerPoint());
        float d = BallDiameter * 2.4f;
        marker.Put(at.x, at.y, d, d);
        marker.color = new Color(1, 1, 1, WaitingForYou ? 0.95f : 0.45f);
    }

    public static Vector2 ToVector(Vec2 v) => new Vector2((float)v.X, (float)v.Y);
}
