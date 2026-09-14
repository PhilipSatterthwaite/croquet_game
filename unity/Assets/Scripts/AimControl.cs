using System.Collections;
using Croquet.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Lining up and striking: the ring, the line, the mallet, and the hand.
///
/// Aiming and pulling are separate gestures. Dragging anywhere in the ring
/// swings the aim; dragging the mallet itself draws it back and letting go
/// strikes. Keeping them apart is what lets a shot be lined up, looked at and
/// reconsidered without ever going off by accident -- one gesture that did both
/// meant every attempt to pull back swung the aim round to point behind the
/// ball.
///
/// The area the aim is taken from is a patch of GRASS, not a number of pixels.
/// Aiming by clicking a ball across the court would be a lever the length of
/// the lawn and so perfectly accurate at any range; a short lever leaves the
/// angle only as fine as the sighting allows, and long shots have to be judged.
/// It is also what makes zooming worth doing -- the same few metres of lawn is
/// a bigger arc magnified, so coming in close buys real precision rather than a
/// closer look at the same accuracy.
///
/// The machine aims through this same object, with the same patch, line and
/// swing drawn back the same distance. An earlier version had it sight several
/// candidate lines instead; that was dropped because a second visual language
/// for the identical act made the opponent read as a different kind of thing
/// from the player.
/// </summary>
public class AimControl : MonoBehaviour
{
    /// <summary>
    /// Drag needed for a full-strength strike, as a fraction of screen height.
    ///
    /// Short on purpose. It was a third of the screen when the bar was the only
    /// thing saying how hard the stroke was, and a long sweep is what you want
    /// when the gesture itself has to carry the precision. It does not: the bar
    /// is marked off in divisions now and can be read while the thumb is still
    /// down, so the drag only has to be long enough to be steady. Anything more
    /// is a stroke that takes half the screen to set.
    /// </summary>
    [Header("The gesture, as a fraction of screen height")]
    [Range(0.1f, 0.8f)] public float pullSpan = 0.18f;

    [Tooltip("Where the mallet head rests off the ball.")]
    [Range(0.005f, 0.08f)] public float restGap = 0.03f;

    [Tooltip("How near the mallet a drag has to start to be a pull.")]
    [Range(0.02f, 0.15f)] public float grab = 0.065f;

    /// <summary>
    /// The one distance the aim is good for, in metres of lawn.
    ///
    /// It is both the patch the aim may be sighted from AND how far up the
    /// court the guide line runs, because those are the same claim made twice.
    /// The angle is only as fine as the sighting that set it, so a line drawn
    /// further than the sighting reaches would be promising a precision the
    /// stroke has not got. Everything past it is judged, which is the game.
    ///
    /// They were two numbers and drifted apart, which left a line poking out of
    /// a circle that was supposed to bound it.
    /// </summary>
    [Header("The aim")]
    [Range(1f, 20f)] public float guideReach = 2.5f;

    [Tooltip("...but the patch is never smaller than this fraction of the view.")]
    [Range(0.03f, 0.3f)] public float aimFloor = 0.14f;

    [Header("The stroke")]
    [Tooltip("How the pull maps to distance. 1 is straight; higher gives short strokes more of the drag.")]
    [Range(1f, 3f)] public float rollCurve = 2f;

    [Header("Looking around")]
    public float zoomStep = 1.12f;

    CroquetGame game;
    CourtCamera eye;
    PauseMenu pause;

    enum Drag { None, Aiming, Pulling, Placing, Panning }
    Drag drag;

    Vec2 aim;
    bool haveAim;
    float pull;               // pixels the mallet is drawn back
    Vector2 panFrom;

    // The machine's stroke while it is taking it. Null whenever a person plays.
    bool robot;
    Vec2 robotFrom;

    /// <summary>The stroke is on its way through. Hands off until it lands.</summary>
    bool swinging;

    SpriteRenderer ring, ghost, align;

    // The guide: where the stroke goes, what it meets, and what the two of them
    // do about it.
    SpriteRenderer line, contact, onward, carries;

    // The swing: a chevron coming down the line.
    SpriteRenderer swing;

    float PullSpan => Screen.height * pullSpan;
    float RestGap => Screen.height * restGap;
    float Grab => Screen.height * grab;
    float AimRadius => Mathf.Max(Span * aimFloor, guideReach);
    float Mpp => eye == null ? 0.01f : eye.MetresPerPixel;

    /// <summary>
    /// Metres across the short side of the view. Everything drawn on the lawn
    /// is a fraction of this, so it keeps its size on screen as the zoom
    /// changes without swelling on a tall, narrow view.
    /// </summary>
    float Span => eye == null ? 8f : eye.ViewSpan;

    public float PullFraction => Mathf.Clamp01(pull / PullSpan);

    /// <summary>
    /// How far this pull would roll the ball on an empty lawn, in metres.
    ///
    /// The pull is a DISTANCE -- the thing a player is actually judging: "reach
    /// that ball", "get in front of the hoop" -- but it is not linear in it.
    /// Most croquet is played at close quarters, so the short strokes get most
    /// of the drag: squared, the first two fifths of the pull cover the first
    /// five metres, where a straight mapping gave them a sixth of it.
    ///
    /// This is very nearly the curve an earlier version had by ACCIDENT, when
    /// the pull was linear in speed and distance therefore went as its square.
    /// What made that unusable was not the curve, it was the ceiling: full pull
    /// rolled forty-one metres on a thirty-metre court, so the whole upper half
    /// of the drag was shots that ran off the lawn. With the top of the range
    /// set to a court's length the same curve is all playable -- half the drag
    /// is eight metres and the rest reaches the far boundary.
    /// </summary>
    public double Roll => game.maxRoll * Mathf.Pow(PullFraction, rollCurve);

    /// <summary>
    /// The speed that rolls that far, from v^2 = 2ad -- the same conversion the
    /// bot samples power with (Bot.Speed), so a person and the machine are
    /// choosing from the same quantity.
    ///
    /// Doing it the other way round, with the pull linear in SPEED, is what made
    /// the ball glide: distance goes as the square, so half a pull was a quarter
    /// of the roll and the top half of the meter was all longer than the court.
    /// </summary>
    public double Power => System.Math.Sqrt(2 * Friction * Roll);

    /// <summary>The lawn as it is now: the dial is live, so this is read each time.</summary>
    float Friction =>
        game.Court != null ? (float)game.Court.Friction : Mathf.Max(0.05f, game.friction);

    void Awake()
    {
        game = FindAnyObjectByType<CroquetGame>();
        pause = FindAnyObjectByType<PauseMenu>();
        eye = Camera.main == null ? null : Camera.main.GetComponent<CourtCamera>();

        // Its own holder, because the court destroys and redraws everything
        // under its when the variant changes.
        var root = Shapes.Holder(transform, "Aim");

        // A patch of shade rather than a drawn ring: a hard white circle round
        // the ball was the loudest object on the court, and it is the least
        // important thing on it.
        //
        // Evenly shaded with a clean rim, though, not a soft blob. The whole
        // job of this is to show where the aim stops being taken, and on a
        // gradient there is no "where" -- an aim that silently stops following
        // the pointer is the thing it exists to prevent.
        ring = Shapes.Piece(root, "Aim patch", Shapes.Disc,
                            new Color(0, 0, 0, 0.10f), Layer.AimPatch);
        ghost = Shapes.Piece(root, "Placement", Shapes.Ring,
                             new Color(1, 1, 1, 0.5f), Layer.AimRing);
        align = Shapes.Piece(root, "Alignment", Shapes.Line,
                             new Color(1f, 0.88f, 0.45f, 0.55f), Layer.AimLine);

        line = Shapes.Piece(root, "Aim line", Shapes.Line,
                            new Color(1, 1, 1, 0.72f), Layer.AimLine);
        contact = Shapes.Piece(root, "Contact", Shapes.Ring,
                               new Color(1, 1, 1, 0.85f), Layer.AimLine);
        onward = Shapes.Piece(root, "Onward", Shapes.Line,
                              new Color(1f, 0.88f, 0.45f, 0.95f), Layer.AimLine);
        carries = Shapes.Piece(root, "Carries", Shapes.Line,
                               new Color(1, 1, 1, 0.34f), Layer.AimLine);

        // A chevron rather than anything ball-shaped: a disc back here reads as
        // a seventh ball sitting behind the striker, which is the one thing it
        // must not look like.
        swing = Shapes.Piece(root, "Swing", Shapes.Chevron,
                             new Color(1f, 0.98f, 0.90f), Layer.Mallet);

        Hide();
    }

    void Update()
    {
        if (game == null || game.Game == null) return;

        // Nothing on the lawn responds while the menu is up -- not even the
        // zoom, or scrolling a settings list would pull the court about
        // underneath it.
        if (pause != null && pause.IsUp) { drag = Drag.None; Hide(); return; }

        Look();

        // Not your turn to aim -- a shot rolling, or the machine's turn -- so a
        // drag on the lawn can only mean looking around.
        if (robot || swinging || !game.WaitingForYou) Watch();

        if (robot || swinging) return;           // a stroke is under way; hands off

        if (game.WaitingForYou) Hand();
        else { Hide(); }
    }

    // ---- looking around ---------------------------------------------------

    void Look()
    {
        var mouse = Mouse.current;
        if (mouse == null || eye == null) return;

        float wheel = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(wheel) > 0.01f)
        {
            // Zooming while a shot plays or the machine is at the ball takes
            // the view, or the next frame's follow drags it straight back.
            if (!game.WaitingForYou || robot || swinging) eye.Take();
            eye.Zoom(wheel > 0 ? zoomStep : 1f / zoomStep);
        }

        // Right-drag looks around. Allowed at any time, including mid-shot and
        // after the game is over: it changes nothing about the game.
        if (mouse.rightButton.wasPressedThisFrame)
        {
            if (!game.WaitingForYou || robot || swinging) eye.Take();
            drag = Drag.Panning;
            panFrom = mouse.position.ReadValue();
        }
        else if (drag == Drag.Panning && mouse.rightButton.isPressed)
        {
            var now = mouse.position.ReadValue();
            eye.pan -= (now - panFrom) * Mpp;    // the lawn follows the hand
            panFrom = now;
        }
        else if (drag == Drag.Panning && !mouse.rightButton.isPressed)
        {
            drag = Drag.None;
        }
    }

    /// <summary>
    /// A plain drag on the lawn while it is not your turn to aim looks around.
    ///
    /// Nothing else can be meant by it then -- there is no stroke to line up --
    /// and it is the only way to look around on a phone, which has no right
    /// button. It takes the view from the game, so the camera stops following
    /// the ball until the next stroke. A drag begun on a button is left alone.
    /// </summary>
    void Watch()
    {
        var p = Pointer.current;
        if (p == null || eye == null) return;

        if (p.press.wasPressedThisFrame && drag == Drag.None && !OverUi)
        {
            drag = Drag.Panning;
            panFrom = p.position.ReadValue();
            eye.Take();
        }
        else if (drag == Drag.Panning && p.press.isPressed)
        {
            var now = p.position.ReadValue();
            eye.pan -= (now - panFrom) * Mpp;    // the lawn follows the hand
            panFrom = now;
        }
        else if (drag == Drag.Panning && !p.press.isPressed &&
                 (Mouse.current == null || !Mouse.current.rightButton.isPressed))
        {
            drag = Drag.None;
        }
    }

    // ---- a person's stroke ------------------------------------------------

    void Hand()
    {
        var p = Pointer.current;
        if (p == null || eye == null) return;

        // A bonus stroke cannot be aimed until it is known how it is being
        // taken -- the ball is not standing where it will be struck from.
        if (game.Game.Stroke == StrokeKind.Bonus && game.BonusChoice == null)
        {
            Hide();
            return;
        }

        Vector2 screen = p.position.ReadValue();
        var from = game.StrikerPoint();
        Vector2 ballOnScreen = eye.ToScreen(from);

        if (p.press.wasPressedThisFrame && drag != Drag.Panning && !OverUi)
            Begin(screen, ballOnScreen);
        if (p.press.isPressed && drag != Drag.None && drag != Drag.Panning) Move(screen, ballOnScreen);
        if (p.press.wasReleasedThisFrame) Release();

        Draw(from, haveAim ? aim : default, haveAim, pull);
    }

    /// <summary>
    /// Whether the pointer is over a button rather than the lawn.
    ///
    /// The lawn is behind the whole interface, so without this every press on a
    /// button is also a press on the grass under it: pressing "Place it here"
    /// set the striker down AND, in the same frame, took that click as a fresh
    /// placement at the bottom of the screen, so the ball jumped to where the
    /// button was. Only the START of a gesture is tested -- a drag begun on the
    /// grass and carried over a panel is still that drag.
    /// </summary>
    static bool OverUi =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    void Begin(Vector2 screen, Vector2 ball)
    {
        // Placing comes first: while the striker is being set down against the
        // roqueted ball there is nothing else the drag could mean.
        if (Placing)
        {
            drag = Drag.Placing;
            Move(screen, ball);
            return;
        }

        if (haveAim)
        {
            Vector2 head = eye.ToScreen(BackswingAt(game.StrikerPoint(), aim, pull));
            if (Vector2.Distance(screen, head) < Grab) { drag = Drag.Pulling; return; }
        }

        drag = Drag.Aiming;
        Move(screen, ball);
    }

    void Move(Vector2 screen, Vector2 ball)
    {
        if (drag == Drag.Placing)
        {
            var on = eye.ToLawn(screen);
            var other = game.Game.World.Balls[game.Game.RoquetedBall].Pos;
            var d = new Vec2(on.x - other.X, on.y - other.Y);
            if (d.LengthSq > 1e-9) game.Placement = d;
            return;
        }

        Vector2 v = screen - ball;

        if (drag == Drag.Aiming)
        {
            float reach = v.magnitude;
            if (reach < 4) return;                          // too close to mean a direction
            if (reach * Mpp > AimRadius) return;            // too far to be a fair sighting
            aim = new Vec2(v.x, v.y).Normalized;
            haveAim = true;
            pull = 0;
        }
        else if (drag == Drag.Pulling && haveAim)
        {
            // Only the component straight behind the ball counts, so sliding
            // sideways while pulling does not quietly change the strength.
            float back = -(v.x * (float)aim.X + v.y * (float)aim.Y) - RestGap;
            pull = Mathf.Clamp(back, 0, PullSpan);
        }
    }

    void Release()
    {
        var was = drag;
        drag = Drag.None;
        if (was != Drag.Pulling) return;

        // Let go without meaning it: never mind.
        if (pull < PullSpan * 0.03f) { pull = 0; return; }

        StartCoroutine(Strike());
    }

    /// <summary>
    /// Swing through, then play. In that order, so the ball is seen to be hit
    /// rather than to start moving of its own accord.
    /// </summary>
    IEnumerator Strike()
    {
        var shot = aim;
        double power = Power;

        yield return Swing();

        if (eye != null) eye.Release();
        if (!game.Strike(shot, power)) Hide();
    }

    /// <summary>Whether the striker still has to be set down against the roqueted ball.</summary>
    bool Placing =>
        game.Game.Stroke == StrokeKind.Bonus &&
        game.BonusChoice != null &&
        game.BonusChoice != BonusWay.WhereItLies &&
        placing;

    bool placing;

    /// <summary>Called by the HUD: the next drag sets the striker down.</summary>
    public void StartPlacing() { placing = true; haveAim = false; pull = 0; }

    /// <summary>Called by the HUD when the placement is accepted.</summary>
    public void DonePlacing() { placing = false; }

    // ---- the machine taking its aim ---------------------------------------

    /// <summary>
    /// The machine addressing the ball: lining up, drawing back to the strength
    /// it wants, and going. Trigonometry is fine here -- this runs before the
    /// stroke and never touches the simulation.
    /// </summary>
    public IEnumerator Address(BotMove move)
    {
        if (eye == null) yield break;

        robot = true;
        placing = false;

        robotFrom = move.IsBonus
            ? game.Game.BonusPlacement(move.Way, move.Placement)
            : game.Game.World.Balls[game.Game.Striker].Pos;

        if (move.IsBonus) { game.BonusChoice = move.Way; game.Placement = move.Placement; }

        eye.LookAt(robotFrom);

        float baseAngle = Mathf.Atan2((float)move.Aim.Y, (float)move.Aim.X);
        // Back the other way: the stroke it chose is a speed, so it becomes the
        // distance that speed rolls, and then the pull that asks for it -- the
        // exact inverse of Roll, curve and all. Without the root the machine
        // would draw back to a length that meant something else entirely.
        double chosenRoll = move.Power * move.Power / (2 * Mathf.Max(0.05f, Friction));
        float share = Mathf.Clamp01((float)(chosenRoll / Mathf.Max(0.01f, game.maxRoll)));
        float drag = Mathf.Pow(share, 1f / Mathf.Max(0.05f, rollCurve));
        float finalPull = Mathf.Clamp(drag * PullSpan, PullSpan * 0.04f, PullSpan);

        // The aim is set once and does not move again.
        //
        // It used to waggle -- across the line for a firm stroke, in and out
        // along it for a delicate one -- on the theory that a hand hovering
        // over its shot is what a player looks like. On a lawn that reads as
        // deliberation; from directly overhead, with the line and the ghost
        // ring swinging with it, it read as the machine changing its mind, and
        // the guide furniture flicked about for a second before every stroke.
        // It had already decided. This shows that.
        SetAngle(baseAngle);

        // Drawing back: one smooth pull to the strength it wants.
        yield return Sweep(0.5f / Pace, t =>
        {
            pull = finalPull * (t * t * (3 - 2 * t));
        });

        // Committed: a beat, and then the same swing through that a person's
        // stroke gets. The machine plays with the same hands.
        pull = finalPull;
        Draw(robotFrom, aim, true, pull);
        yield return new WaitForSeconds(0.18f / Pace);

        haveAim = true;
        yield return Swing();

        robot = false;
        pull = 0;
        Hide();
    }

    void SetAngle(float a) => aim = new Vec2(Mathf.Cos(a), Mathf.Sin(a));

    /// <summary>The bot speed for the machine at the ball, read live so a change takes at once.</summary>
    float Pace => game.PaceFor(game.Game.Striker);

    IEnumerator Sweep(float seconds, System.Action<float> step)
    {
        for (float e = 0; e < seconds; e += Time.deltaTime)
        {
            float t = Mathf.Clamp01(e / seconds);
            step(t);
            Draw(robotFrom, aim, true, pull);
            yield return null;
        }
        step(1f);
    }

    // ---- drawing ----------------------------------------------------------

    /// <summary>Where the swing has been drawn back to, given the pull.</summary>
    Vec2 BackswingAt(Vec2 from, Vec2 dir, float pullPx)
    {
        double d = (RestGap + pullPx) * Mpp;
        return new Vec2(from.X - dir.X * d, from.Y - dir.Y * d);
    }

    void Draw(Vec2 from, Vec2 dir, bool aimed, float pullPx) =>
        Draw(CroquetGame.ToVector(from), dir, aimed, pullPx);

    void Draw(Vector2 origin, Vec2 dir, bool aimed, float pullPx)
    {
        var at = origin;

        // The patch the aim can be taken from, so the limit is visible rather
        // than felt as the aim mysteriously refusing to follow the pointer. The
        // guide line reaches exactly to its edge, because they are the same
        // distance.
        ring.gameObject.SetActive(true);
        ring.Put(at.x, at.y, AimRadius * 2, AimRadius * 2);

        ghost.gameObject.SetActive(Placing);
        align.gameObject.SetActive(Placing);
        if (Placing)
        {
            float d = (float)(game.Court.BallRadius * 2) * 1.6f;
            ghost.Put(at.x, at.y, d, d);
            DrawAlignment(at);
        }

        bool show = aimed && !Placing;
        if (!show) { HideAim(); return; }

        float angle = Mathf.Atan2((float)dir.Y, (float)dir.X) * Mathf.Rad2Deg;
        var unit = new Vector2((float)dir.X, (float)dir.Y);

        DrawGuide(at, unit, angle);
        DrawSwing(at, unit, angle, pullPx);
    }

    /// <summary>
    /// The line the two balls are standing on, drawn out to the boundary.
    ///
    /// Setting the striker down is choosing an ANGLE, and the whole of what it
    /// changes is where the other ball is sent. The gesture moves the ball
    /// itself by at most one ball width -- two balls in contact is what a
    /// placement is -- which at any sane zoom is a handful of pixels, so with
    /// nothing but the two discs to look at the placement reads as doing
    /// nothing at all whichever way it is dragged. It is not the ball that has
    /// to be visible here, it is the line it makes.
    ///
    /// It runs the length of the accuracy zone past the ball it is lined up on,
    /// exactly as the aim line does. It went to the boundary at first, on the
    /// grounds that where two balls stand is geometry rather than a claim about
    /// the stroke -- true, and beside the point: a line the length of the lawn
    /// still reads as a promise about where the ball ends up, and this one has
    /// a whole croquet stroke's worth of error waiting behind it.
    /// </summary>
    void DrawAlignment(Vector2 at)
    {
        int hit = game.Game.RoquetedBall;
        if (hit < 0) { align.gameObject.SetActive(false); return; }

        var other = CroquetGame.ToVector(game.Game.World.Balls[hit].Pos);

        var span = other - at;
        if (span.sqrMagnitude < 1e-8f) { align.gameObject.SetActive(false); return; }

        var unit = span.normalized;
        float run = Mathf.Min(AimRadius, AimGuide.ToEdge(game.Court, other, unit));
        var end = other + unit * run;

        float thin = Span * 0.0035f;
        align.PutRotated((at + end) * 0.5f,
                         Mathf.Atan2(unit.y, unit.x) * Mathf.Rad2Deg,
                         Vector2.Distance(at, end), thin);
    }

    /// <summary>
    /// Where the stroke goes, and what happens when it gets there.
    ///
    /// One clean line to the first thing in the way, a ring where the ball
    /// would be at that moment, and out of it the two directions the collision
    /// actually produces -- which is the aid every pool game has and which
    /// croquet, being a game about hitting other balls on purpose, wants more
    /// than pool does.
    ///
    /// It is not a hint or an estimate: see AimGuide.
    /// </summary>
    void DrawGuide(Vector2 at, Vector2 unit, float angle)
    {
        float thin = Span * 0.0048f;

        // A constant length, stopping early only for something actually in the
        // way. The line answers "where" and the meter answers "how hard";
        // tying the two together made the line move while the strength was
        // being set, which is the moment it most needs to hold still.
        int croquet = game.Game.Stroke == StrokeKind.Bonus &&
                      game.BonusChoice == BonusWay.CroquetShot
            ? game.Game.RoquetedBall : -1;
        var guide = AimGuide.Trace(game.Game, game.Game.Striker, at, unit, guideReach, croquet);
        float travel = Vector2.Distance(at, guide.To);

        line.gameObject.SetActive(true);
        line.PutRotated(at + unit * (travel / 2), angle, travel, thin);

        bool onto = guide.Hit == AimGuide.Meeting.Ball;

        // Off a hoop upright or a peg the ball comes back, and where it comes
        // back to is as worth knowing before the stroke as where a struck ball
        // goes -- a leg of the hoop is the commonest thing to hit in the game.
        bool bounce = guide.Hit == AimGuide.Meeting.Obstacle
                      && guide.Carries.sqrMagnitude > 0.5f;

        // The ghost: where the ball would be at the moment it arrives.
        contact.gameObject.SetActive(true);
        float d = (float)(game.Court.BallRadius * 2);
        contact.Put(guide.To.x, guide.To.y, d, d);
        contact.color = onto || bounce ? new Color(1, 1, 1, 0.9f) : new Color(1, 1, 1, 0.4f);

        onward.gameObject.SetActive(onto);
        carries.gameObject.SetActive(onto || bounce);

        // Brighter when it is the only thing coming out of the contact.
        carries.color = bounce ? new Color(1, 1, 1, 0.62f) : new Color(1, 1, 1, 0.34f);

        float budget = Mathf.Max(0.9f, Span * 0.15f);

        // A floor, so the short one still says which way it went. Below about a
        // ball's width a line is a dot with an opinion.
        const float least = 0.1f;

        if (bounce)
        {
            // As long as the share of its roll the ball keeps: a glancing touch
            // runs on nearly the full length, a square hit on a leg comes back
            // at a quarter of it. The same measure the split lines use, so a
            // stub means the same thing whatever it was that got hit.
            float back = budget * Mathf.Clamp(guide.CarriesRoll, least, 1f);
            carries.PutRotated(guide.To + guide.Carries * (back / 2),
                               Mathf.Atan2(guide.Carries.y, guide.Carries.x) * Mathf.Rad2Deg,
                               back, thin);
            return;
        }

        if (!onto) return;

        // Where the struck ball goes: along the line of centres, from where it
        // is now rather than from the point of contact, because that is where
        // the eye expects the line to start. And where the striker carries on
        // to, which is the half people forget.
        //
        // Their LENGTHS are the split of the blow. A fixed budget shared out in
        // proportion to how far each ball will actually roll: a full hit puts
        // nearly all of it into the ball in front and leaves the striker a
        // stub, and a thin cut does the reverse. It is the one thing about a
        // contact worth knowing before you play it -- whether you are sending
        // that ball somewhere or following it there -- and it is the same
        // number that decides it, not an illustration of it.
        //
        // Shares of a fixed total, never absolute distances, because the guide
        // must not start moving when the PULL changes. Where a stroke goes and
        // how hard it is struck are answered by different instruments, and this
        // one has held still since it stopped trying to answer both.
        var target = CroquetGame.ToVector(game.Game.World.Balls[guide.Struck].Pos);

        float total = Mathf.Max(1e-4f, guide.OnwardRoll + guide.CarriesRoll);
        float share = Mathf.Clamp(guide.OnwardRoll / total, least, 1f - least);

        float ahead = budget * share;
        float after = budget * (1f - share);

        onward.PutRotated(target + guide.Onward * (ahead / 2),
                          Mathf.Atan2(guide.Onward.y, guide.Onward.x) * Mathf.Rad2Deg,
                          ahead, thin * 1.25f);

        carries.PutRotated(guide.To + guide.Carries * (after / 2),
                           Mathf.Atan2(guide.Carries.y, guide.Carries.x) * Mathf.Rad2Deg,
                           after, thin);
    }

    /// <summary>
    /// The swing, which is all a mallet can honestly be from directly overhead.
    ///
    /// A drawn mallet was the wrong object here: seen from above it is a bar
    /// lying across the line of the shot, and it read as a piece of furniture
    /// parked behind the ball rather than as anything about to move. What a
    /// swing actually looks like from up here is the head coming down the line,
    /// so that is what this is -- a head drawn back along the line, and on
    /// release it travels.
    /// </summary>
    void DrawSwing(Vector2 at, Vector2 unit, float angle, float pullPx)
    {
        var head = at - unit * ((RestGap + pullPx) * Mpp);

        float size = Mathf.Clamp(0.22f, Span * 0.018f, Span * 0.038f);
        float hard = Mathf.Clamp01(pullPx / PullSpan);

        swing.gameObject.SetActive(true);
        swing.transform.localPosition = new Vector3(head.x, head.y, 0);
        swing.transform.localRotation = Quaternion.Euler(0, 0, angle);
        swing.transform.localScale = new Vector3(size, size, 1);
        swing.color = Color.Lerp(new Color(0.96f, 0.94f, 0.88f, 0.55f),
                                 new Color(1f, 0.99f, 0.93f, 1f), hard);

        // No trail behind it. There was a streak back along the path it had
        // been drawn over, and it read as a second line on the lawn -- right
        // beside the aim line, which is the one line that has to be read.
    }

    /// <summary>
    /// The swing coming through: the head runs down the line into the ball.
    ///
    /// Short on purpose -- a tenth of a second. It is the difference between a
    /// ball that simply starts moving and a ball that was hit, and any longer
    /// than that reads as a delay rather than a stroke.
    /// </summary>
    public IEnumerator Swing()
    {
        if (!haveAim || eye == null) yield break;

        swinging = true;

        float from = pull;
        var at = CroquetGame.ToVector(game.StrikerPoint());
        var unit = new Vector2((float)aim.X, (float)aim.Y);
        float angle = Mathf.Atan2(unit.y, unit.x) * Mathf.Rad2Deg;

        // Only the head comes through. The aim line and the split lines stayed
        // up during the swing, and read as a line the head was sliding along --
        // the aim is settled by now, and the head is the only thing moving.
        HideAim();

        // The machine's swing is hurried with the rest of its stroke.
        float time = SwingTime / (robot ? Pace : 1f);

        for (float e = 0; e < time; e += Time.deltaTime)
        {
            float t = Mathf.Clamp01(e / time);

            // Fast into the ball rather than even: a stroke accelerates.
            pull = Mathf.Lerp(from, 0, t * t);
            DrawSwing(at, unit, angle, pull);
            yield return null;
        }

        pull = 0;
        haveAim = false;
        swinging = false;
        Hide();
    }

    const float SwingTime = 0.1f;

    void HideAim()
    {
        line.gameObject.SetActive(false);
        contact.gameObject.SetActive(false);
        onward.gameObject.SetActive(false);
        carries.gameObject.SetActive(false);
        swing.gameObject.SetActive(false);
    }

    void Hide()
    {
        if (ring == null) return;
        ring.gameObject.SetActive(false);
        ghost.gameObject.SetActive(false);
        align.gameObject.SetActive(false);
        HideAim();
    }
}
