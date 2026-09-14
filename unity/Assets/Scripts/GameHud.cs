using System.Collections.Generic;
using System.Linq;
using Croquet.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The little the game needs to say while it is being played.
///
/// Deliberately almost nothing. An earlier version narrated the turn in a
/// paragraph top-left and reported every stroke along the bottom, and both were
/// reading out things the lawn was already showing -- the ball is right there,
/// with a ring round it and a marker on the hoop it is for.
///
/// What is left is what the lawn genuinely cannot show: how hard the mallet is
/// drawn back, and who is dead on whom. Deadness is the one thing in croquet
/// with no physical sign at all -- two balls sitting a foot apart look exactly
/// the same whether hitting one is worth two strokes or nothing -- so it gets a
/// permanent corner of its own, in colours rather than words.
///
/// Built once and then only updated: rebuilding a canvas every frame is how an
/// interface starts costing more than the simulation it is describing.
/// </summary>
public class GameHud : MonoBehaviour
{
    CroquetGame game;
    AimControl aim;
    PauseMenu pause;

    Canvas canvas;

    RectTransform meterRow;
    Image meterFill;

    /// <summary>How many divisions the pull is marked off in.</summary>
    const int Divisions = 10;

    readonly List<Image> ticks = new List<Image>();

    RectTransform ways;
    Button place, menuButton, speedButton;
    readonly List<Button> wayButtons = new List<Button>();
    readonly List<BonusWay> wayKinds = new List<BonusWay>();

    RectTransform deadPanel;
    readonly List<RectTransform> deadRows = new List<RectTransform>();
    readonly List<Image> deadOwner = new List<Image>();
    readonly List<Image[]> deadChips = new List<Image[]>();
    readonly List<bool[]> wasDead = new List<bool[]>();
    readonly List<float[]> litAt = new List<float[]>();

    RectTransform over, winner, finalRows;
    Text winnerText;

    RectTransform notice;
    Text noticeText;

    /// <summary>The shot the notice last spoke about, and when it finished rolling.</summary>
    Replay told;
    float toldAt;

    /// <summary>How long a notice stays up.</summary>
    const float NoticeSeconds = 4f;
    readonly List<Image> finalDots = new List<Image>();
    readonly List<Text> finalScores = new List<Text>();

    /// <summary>Set while the striker is being set down against the roqueted ball.</summary>
    bool placing;

    const int MaxBalls = 6;

    /// <summary>Chips in a row: everyone but the ball whose row it is.</summary>
    const int Others = MaxBalls - 1;

    /// <summary>How long a chip stays swollen after it lights.</summary>
    const float Pop = 0.3f;

    void Awake()
    {
        game = GetComponent<CroquetGame>();
        aim = GetComponent<AimControl>();
        pause = GetComponent<PauseMenu>();
        Build();
    }

    // ---- building it ------------------------------------------------------

    void Build()
    {
        canvas = Ui.Screen(transform, "Hud", order: 10);

        BuildMeter();
        BuildWays();
        BuildDeadness();

        menuButton = Ui.Press(canvas.transform, "Menu", () => pause.Open(), 15, 36);
        menuButton.Pin(new Vector2(1, 1), new Vector2(-18, -18), new Vector2(96, 36));
        menuButton.colors = Ui.Scheme(Ui.Panel);

        // Beside it, how fast the machine's strokes play. One button stepping
        // round 1x, 2x and 4x rather than three of them: it is changed now and
        // then and read at a glance, and three buttons is a control panel.
        speedButton = Ui.Press(canvas.transform, SpeedLabel(), () =>
        {
            CroquetGame.NextBotSpeed();
            speedButton.SetText(SpeedLabel());
        }, 15, 36);
        speedButton.Pin(new Vector2(1, 1), new Vector2(-(18 + 96 + SpeedGap), -18),
                        new Vector2(SpeedWidth, 36));
        speedButton.colors = Ui.Scheme(Ui.Panel);

        BuildNotice();
        BuildWinner();
    }

    /// <summary>
    /// The one thing that has to be constant: how hard the stroke is. It keeps
    /// the same corner whether it is your pull or the machine's search, because
    /// a gauge that moves about is one you have to find before you can read it.
    /// </summary>
    void BuildMeter()
    {
        meterRow = Ui.Rect(canvas.transform, "Power");

        // Long and thin. Wide because it is read against a scale -- every extra
        // pixel is finer tuning per division -- and shallow because none of
        // that reading happens vertically. Height was left over from when it
        // held a word. It was 300 wide; with the scale now in distance rather
        // than drag, the short strokes live in the first few divisions and
        // want the room.
        meterRow.Pin(new Vector2(0, 1), new Vector2(18, -18), new Vector2(460, 20));

        // A far tighter corner than a card gets. A gauge is read from its ENDS
        // -- empty, full, and how near either you are -- and a generous radius
        // eats the last few per cent at both, so the one part of the bar that
        // has to be unambiguous is the part that gets rounded away.
        Ui.Soften(Ui.Skin(meterRow, Ui.Panel), 0.2f);

        var track = Ui.Rect(meterRow, "Track");
        track.anchorMin = Vector2.zero;
        track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(3, 3);
        track.offsetMax = new Vector2(-3, -3);

        meterFill = Ui.Block(track, "Fill", Ui.Accent);

        // Tighter still than the bar around it. The fill's leading edge is the
        // needle -- it is what gets read against the ticks -- and a rounded cap
        // on it has no single place where the value is.
        Ui.Soften(meterFill, 0.05f);
        var mf = (RectTransform)meterFill.transform;
        mf.anchorMin = Vector2.zero;
        mf.anchorMax = new Vector2(0, 1);
        mf.offsetMin = mf.offsetMax = Vector2.zero;

        Ticks(track);
    }

    /// <summary>
    /// A plain scale across the bar, added after the fill so it stays legible
    /// over it.
    ///
    /// The divisions are of the STROKE -- tenths of a full-length roll -- so the
    /// scale is linear in what the ball will do, while the drag that sets it
    /// stays curved for control. They are here so a stroke can be repeated:
    /// "that one was four ticks" is the whole of what they have to support, and
    /// a number of metres never told anybody that.
    /// </summary>
    void Ticks(RectTransform track)
    {
        for (int i = 1; i < Divisions; i++)
        {
            float at = i / (float)Divisions;
            bool half = i * 2 == Divisions;

            var tick = Ui.Block(track, "Tick", Color.clear);
            tick.raycastTarget = false;
            ticks.Add(tick);

            // They rise from the bottom edge like a ruler, and the midpoint
            // runs the whole way up: half strength is the one division worth
            // finding without counting to it.
            var r = (RectTransform)tick.transform;
            r.anchorMin = new Vector2(at, 0f);
            r.anchorMax = new Vector2(at, half ? 1f : 0.42f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(half ? 2.5f : 1.5f, 0);
        }
    }

    /// <summary>
    /// Ticks past the fill are drawn light on the dark track; ticks the fill has
    /// covered are drawn dark on it. Without this a white scale disappears the
    /// moment a white or yellow ball's bar rolls over it, which is exactly when
    /// the low divisions are being read.
    /// </summary>
    void Scale(float t)
    {
        for (int i = 0; i < ticks.Count; i++)
        {
            float at = (i + 1) / (float)Divisions;
            bool half = (i + 1) * 2 == Divisions;
            bool under = at <= t;

            ticks[i].color = under ? new Color(0, 0, 0, half ? 0.34f : 0.19f)
                                   : new Color(1, 1, 1, half ? 0.32f : 0.16f);
        }
    }

    /// <summary>
    /// The four ways a bonus stroke may be taken. Not a permanent fixture -- it
    /// is a question, asked only when there is one, and gone again after.
    /// </summary>
    void BuildWays()
    {
        ways = Ui.Rect(canvas.transform, "Ways");
        ways.anchorMin = new Vector2(0.5f, 0);
        ways.anchorMax = new Vector2(0.5f, 0);
        ways.pivot = new Vector2(0.5f, 0);
        ways.anchoredPosition = new Vector2(0, 26);
        ways.sizeDelta = new Vector2(640, 0);

        Ui.Skin(ways, Ui.Panel);
        Ui.ColumnOn(ways, 6, new RectOffset(14, 14, 14, 14));

        var fit = ways.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var row = Ui.Row(ways, "Choices", 8, 42);
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            wayButtons.Add(Ui.Press(row, "", () => Choose(wayKinds[slot]), 15, 42));
            wayKinds.Add(BonusWay.CroquetShot);
        }

        place = Ui.Press(ways, "Place it here", () => { placing = false; aim.DonePlacing(); },
                         16, 40);
        place.Chosen(true);
    }

    /// <summary>
    /// Who is dead on whom, top centre, said in colours.
    ///
    /// One pair a ball, side by side in a strip: a square in its colour, and
    /// beside it a bar. When that
    /// ball becomes dead on another, the other ball pops into the bar in its own
    /// colour; an empty bar is a ball dead on nobody. Nothing is drawn for a ball
    /// it is NOT dead on -- no dark chip, no faint one -- so the bar only ever
    /// shows the one thing it is for.
    ///
    /// The chips pack from the left of the bar in BALL order, not the order the
    /// deadness came in: dead on one ball, it sits at the left end; dead on
    /// three, they read in playing order. A fixed slot per ball left a lone chip
    /// stranded partway along an empty bar.
    ///
    /// In line with the power bar, starting just to the right of it, so the top
    /// edge reads as one strip of instruments. Where the screen is too narrow
    /// for it to clear the Menu button it is scaled down to fit rather than
    /// running under the button -- see <see cref="FitDeadness"/>.
    /// </summary>
    void BuildDeadness()
    {
        deadPanel = Ui.Rect(canvas.transform, "Deadness");
        deadPanel.anchorMin = deadPanel.anchorMax = new Vector2(0, 1);
        deadPanel.pivot = new Vector2(0, 0.5f);

        // Level with the power bar's middle, just past its right-hand end.
        deadPanel.anchoredPosition = new Vector2(DeadLeft, -(18 + 20 / 2f));
        deadPanel.sizeDelta = new Vector2(0, Square);

        // The pairs laid side by side rather than stacked, and the strip sized
        // to however many balls are playing.
        var across = deadPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
        across.spacing = UnitGap;
        across.childAlignment = TextAnchor.MiddleCenter;
        across.childControlWidth = across.childControlHeight = true;
        across.childForceExpandWidth = across.childForceExpandHeight = false;

        var fit = deadPanel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        for (int i = 0; i < MaxBalls; i++)
        {
            var row = Ui.Row(deadPanel, "Dead " + i, RowGap, Square, expand: false);

            // The ball's own colour in a slightly rounded square, inside a thin
            // white border -- which is what keeps the black ball's square from
            // disappearing into anything dark around it.
            var frame = Ui.Rect(row, "Owner");
            Size(frame.gameObject, Square, Square);
            Ui.Soften(Ui.Skin(frame, Color.white, round: false), 0.14f);

            var owner = Ui.Block(frame, "Colour", Color.white);
            var inset = (RectTransform)owner.transform;
            inset.anchorMin = Vector2.zero;
            inset.anchorMax = Vector2.one;
            inset.offsetMin = new Vector2(Border, Border);
            inset.offsetMax = new Vector2(-Border, -Border);
            Ui.Soften(owner, 0.1f);
            deadOwner.Add(owner);

            // The bar the balls it is dead on arrive in. Light grey, because a
            // dark bar swallowed the black ball's chip. Painted on its own object
            // so the backdrop stays out of the layout inside it.
            var bar = Ui.Rect(row, "Bar");
            Size(bar.gameObject, BarWidth, Square);
            Ui.Soften(Ui.Skin(bar, BarGrey, round: false), 0.2f);

            var slots = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            slots.spacing = ChipGap;
            slots.padding = new RectOffset((int)BarPad, (int)BarPad, 0, 0);
            slots.childAlignment = TextAnchor.MiddleLeft;
            slots.childControlWidth = slots.childControlHeight = true;
            slots.childForceExpandWidth = slots.childForceExpandHeight = false;

            var chips = new Image[Others];
            for (int k = 0; k < Others; k++)
                chips[k] = Ui.Dot(bar, "On " + k, Color.white, Chip);

            deadChips.Add(chips);
            deadRows.Add(row);
            wasDead.Add(new bool[Others]);
            litAt.Add(new float[Others]);
        }
    }

    const float Square = 18f, RowGap = 6f, Chip = 12f, ChipGap = 3f, BarPad = 3f, Border = 2f,
                UnitGap = 14f;

    /// <summary>Where the strip starts: the power bar's margin and width, and a gap.</summary>
    const float DeadLeft = 18 + 460 + 16;

    /// <summary>The bot speed toggle's width, and the gap between it and the Menu button.</summary>
    const float SpeedWidth = 56f, SpeedGap = 8f;

    static string SpeedLabel() => CroquetGame.BotSpeed + "x";

    /// <summary>
    /// What the buttons take off the right-hand end: the margin, the Menu
    /// button, the speed toggle beside it, and a gap.
    /// </summary>
    const float MenuRoom = 18 + 96 + SpeedGap + SpeedWidth + 16;

    /// <summary>
    /// Scales the strip down when the space between the power bar and the Menu
    /// button is narrower than it is. On a 16:9 screen that space is about 560
    /// canvas units against a strip of about 680; on a wider phone it fits at
    /// full size and this leaves it alone.
    /// </summary>
    void FitDeadness()
    {
        float room = ((RectTransform)canvas.transform).rect.width - DeadLeft - MenuRoom;
        float wide = LayoutUtility.GetPreferredWidth(deadPanel);
        float s = wide > 1f && room < wide ? Mathf.Max(0.4f, room / wide) : 1f;
        deadPanel.localScale = new Vector3(s, s, 1f);
    }

    /// <summary>The bar behind the chips: light enough that a black ball shows on it.</summary>
    static readonly Color BarGrey = new Color(0.84f, 0.84f, 0.82f, 0.92f);

    /// <summary>A bar exactly wide enough for a slot for every other ball.</summary>
    static float BarWidth => BarPad * 2 + Others * Chip + (Others - 1) * ChipGap;

    /// <summary>Fixes a piece of the layout at one size, neither growing nor shrinking.</summary>
    static void Size(GameObject go, float w, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = w;
        le.minHeight = le.preferredHeight = h;
        le.flexibleWidth = le.flexibleHeight = 0;
    }

    /// <summary>Which ball slot <paramref name="k"/> of row <paramref name="i"/> stands for.</summary>
    static int Other(int i, int k) => k < i ? k : k + 1;

    /// <summary>
    /// A line under the top strip for the one kind of event the lawn cannot
    /// explain by itself: a wicketed-ball foul, where the balls roll, slide
    /// back to where they were, and the turn passes. Without a word that looks
    /// like the game misbehaving.
    ///
    /// Said once the shot has finished rolling, never while it is still moving:
    /// the rules decided the moment the ball was struck, and saying so early
    /// gives the ending away.
    /// </summary>
    void BuildNotice()
    {
        notice = Ui.Rect(canvas.transform, "Notice");
        notice.anchorMin = notice.anchorMax = new Vector2(0.5f, 1);
        notice.pivot = new Vector2(0.5f, 1);
        notice.anchoredPosition = new Vector2(0, -(18 + 20 + 14));   // just under the top strip
        notice.sizeDelta = new Vector2(600, 0);

        Ui.Skin(notice, Ui.Card);
        Ui.ColumnOn(notice, 0, new RectOffset(18, 18, 10, 10));

        var fit = notice.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        noticeText = Ui.Label(notice, "", 17, Ui.Ink, TextAnchor.MiddleCenter);
        Ui.Tall(noticeText.gameObject, 44);

        notice.gameObject.SetActive(false);
    }

    /// <summary>
    /// The end of the game, over the whole screen.
    ///
    /// It was a card floating on the live lawn with the power bar, the deadness
    /// chart and the Menu button still up around it, which reads as one more
    /// thing the game is telling you rather than as the game being over. So the
    /// court goes behind a scrim and everything else in the HUD goes away: at
    /// this point there is no stroke to judge and nobody to be dead on.
    /// </summary>
    void BuildWinner()
    {
        over = Ui.Rect(canvas.transform, "Over");
        over.Fill();

        var scrim = Ui.Block(over, "Scrim", new Color(0.04f, 0.09f, 0.06f, 0.72f));
        ((RectTransform)scrim.transform).Fill();

        winner = Ui.Rect(over, "Winner");
        winner.anchorMin = winner.anchorMax = new Vector2(0.5f, 0.5f);
        winner.pivot = new Vector2(0.5f, 0.5f);
        winner.sizeDelta = new Vector2(480, 0);

        Ui.Skin(winner, Ui.Card);
        Ui.ColumnOn(winner, 14, new RectOffset(28, 28, 26, 26));

        var fit = winner.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        winnerText = Ui.Label(winner, "", 27, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
        Ui.Tall(winnerText.gameObject, 64);

        // How everyone finished, in course order. A game of croquet is long
        // enough that "who won" on its own is a thin answer to it.
        finalRows = Ui.Column(winner, "Standing", 6);

        for (int i = 0; i < MaxBalls; i++)
        {
            var row = Ui.Row(finalRows, "Ball " + i, 10, 26, expand: false);
            finalDots.Add(Ui.Dot(row, "Owner", Color.white, 18));
            finalScores.Add(Ui.Label(row, "", 17, Ui.Ink, TextAnchor.MiddleLeft));
            Ui.Filler(row);
        }

        // The game is over, so there is nothing to pause and nothing to resume.
        // Another game on the same settings, or back to change them.
        Ui.Press(winner, "Play again", () => game.NewGame(), 19, 50).Chosen(true);
        Ui.Press(winner, "Back to menu",
                 () => UnityEngine.SceneManagement.SceneManager.LoadScene("Menu"),
                 19, 46);
    }

    // ---- keeping it true --------------------------------------------------

    void Update()
    {
        // Nothing is built until Awake has run, and a script reload in the
        // middle of a play session leaves these empty without re-running it.
        if (canvas == null || game == null) return;

        bool playing = game.Game != null && (pause == null || !pause.IsUp);
        if (canvas.gameObject.activeSelf != playing) canvas.gameObject.SetActive(playing);
        if (!playing) return;

        var g = game.Game;

        // The game clears the choice at the start of every stroke, so this
        // follows it rather than keeping a second opinion about whether a ball
        // is still being set down.
        if (game.BonusChoice == null) placing = false;

        // Not the moment the rules know it is won -- that is when the winning
        // stroke is struck -- but once that stroke has finished playing.
        bool ended = game.ShownOver;
        over.gameObject.SetActive(ended);

        // The rest of the HUD is about the stroke in front of you, and there
        // is not one any more.
        meterRow.gameObject.SetActive(!ended);
        deadPanel.gameObject.SetActive(!ended);
        menuButton.gameObject.SetActive(!ended);
        speedButton.gameObject.SetActive(!ended && game.HasBots);

        Notice(ended);

        if (ended) { Final(g); return; }

        Ways();
        Meter();
        Deadness();
    }

    /// <summary>Who won, and how far round everybody got.</summary>
    void Final(Game g)
    {
        winnerText.text = Won(g);

        int count = g.World.Balls.Length;
        for (int i = 0; i < MaxBalls; i++)
        {
            bool playing = i < count;
            finalDots[i].transform.parent.gameObject.SetActive(playing);
            if (!playing) continue;

            var state = g.States[i];
            finalDots[i].color = game.ColourOf(i);
            finalScores[i].text = state.Finished
                ? CroquetGame.NameOf(i) + " — round"
                : CroquetGame.NameOf(i) + " — " + state.Point + " of " + state.Total;
        }
    }

    void Ways()
    {
        var g = game.Game;
        bool choosing = game.WaitingForYou && g.Stroke == StrokeKind.Bonus
                        && game.BonusChoice == null;

        ways.gameObject.SetActive(choosing || (game.WaitingForYou && placing));

        var options = game.BonusWays;
        for (int i = 0; i < wayButtons.Count; i++)
        {
            bool used = choosing && i < options.Length;
            wayButtons[i].gameObject.SetActive(used);
            if (!used) continue;
            wayKinds[i] = options[i];
            wayButtons[i].SetText(Pretty(options[i]));
        }

        wayButtons[0].transform.parent.gameObject.SetActive(choosing);
        place.gameObject.SetActive(game.WaitingForYou && placing);
    }

    /// <summary>
    /// The bar shows ONE thing: how far back your own stroke is drawn.
    ///
    /// It used to double as the machine's search progress, which swept the
    /// whole bar in the striker's colour in about a tenth of a second and read
    /// as the corner of the screen flashing at the start of every turn. Two
    /// quantities sharing a gauge is one of them lying: a bar that is filling
    /// has to mean the stroke is getting harder, or it means nothing at all.
    /// How long the machine is taking is not something anyone is waiting to
    /// read off a scale.
    ///
    /// No words in here either. It carried the striker's name, "thinking" and
    /// "pull to strike" in turn, and every one of them was saying something the
    /// bar itself already said -- it is in the striker's colour, it is filling
    /// on its own, and it is the thing under your thumb. A word in the gauge
    /// you are reading against a scale is just something in the way.
    /// </summary>
    void Meter()
    {
        // Linear in the STROKE, not in the thumb. The drag is still curved --
        // its first half buys the first quarter of the court, so a short stroke
        // is a slow, fine draw -- but the gauge shows what that drag produces:
        // half the bar is half a full-length roll, and every tick is a tenth of
        // one. The fill creeps at the start of a pull and races at the end,
        // which is exactly where the precision is and is not.
        float t = 0;
        if (game.WaitingForYou && aim != null && game.maxRoll > 0)
            t = Mathf.Clamp01((float)(aim.Roll / game.maxRoll));

        // The bar takes the striker's colour, so the corner says whose stroke
        // it is without spending a word on it.
        var c = game.ColourOf(game.ShownStriker);
        meterFill.color = Color.Lerp(c, Ui.Accent, 0.35f);

        var mf = (RectTransform)meterFill.transform;
        mf.anchorMax = new Vector2(t, 1);

        Scale(t);
    }

    /// <summary>
    /// Brings a ball into a bar the moment the row's ball becomes dead on it,
    /// with a small swell so a deadness picked up while watching the ball is not
    /// silent.
    ///
    /// True colours only. The square is its ball's colour, a chip is the colour
    /// of the ball it stands for, and there is no dimming anywhere -- it used to
    /// dim every square but the striker's, and dark or faint stand-ins for live
    /// balls, and all of those were colours that had to be told apart from the
    /// real thing.
    /// </summary>
    void Deadness()
    {
        FitDeadness();

        var g = game.Game;
        int count = g.World.Balls.Length;

        for (int i = 0; i < MaxBalls; i++)
        {
            // What the shot on screen has got to, not what the rules already
            // know: see CroquetGame.ShownDead.
            bool playing = i < count && !game.ShownFinished(i);
            deadRows[i].gameObject.SetActive(playing);
            if (!playing) continue;

            deadOwner[i].color = game.ColourOf(i);

            for (int k = 0; k < Others; k++)
            {
                int j = Other(i, k);

                bool dead = j < count && game.ShownDead(i, j);
                if (dead && !wasDead[i][k]) litAt[i][k] = Time.time;
                wasDead[i][k] = dead;

                // Only the balls it is dead on are in the bar at all, so they
                // pack from the left. The chips were made in ball order, so they
                // build up in ball order whatever order the deadness came in.
                deadChips[i][k].gameObject.SetActive(dead);
                if (!dead) continue;

                deadChips[i][k].color = game.ColourOf(j);

                // Scale, not layout: the chip swells in place.
                float since = Time.time - litAt[i][k];
                float swell = since < Pop ? 1f + 0.55f * (1f - since / Pop) : 1f;
                deadChips[i][k].rectTransform.localScale = Vector3.one * swell;
            }
        }
    }

    /// <summary>Picks up a finished shot that has something to say, and times the notice out.</summary>
    void Notice(bool ended)
    {
        var shot = game.Last;
        if (shot != null && shot != told && game.Phase != Phase.Rolling)
        {
            told = shot;
            toldAt = Time.time;
            noticeText.text = Says(shot.Result);
        }

        bool show = !ended && told != null && noticeText.text.Length > 0 &&
                    Time.time - toldAt < NoticeSeconds;
        if (notice.gameObject.activeSelf != show) notice.gameObject.SetActive(show);
    }

    /// <summary>What a stroke did that the lawn cannot show, or nothing.</summary>
    string Says(StrokeResult r)
    {
        if (r.DeadFoul >= 0)
            return CroquetGame.NameOf(r.Striker) + " hit " + CroquetGame.NameOf(r.DeadFoul) +
                   ", which it is dead on. The balls go back and the turn is over.";

        if (r.WicketedFoul >= 0)
            return CroquetGame.NameOf(r.Striker) + " roqueted " + CroquetGame.NameOf(r.WicketedFoul) +
                   " while it was stuck in the wicket. The balls go back and the turn is over.";

        return "";
    }

    void Choose(BonusWay way)
    {
        game.BonusChoice = way;
        if (way == BonusWay.WhereItLies) { placing = false; aim.DonePlacing(); }
        else { placing = true; aim.StartPlacing(); }
    }

    static string Pretty(BonusWay w) => w switch
    {
        BonusWay.MalletHead => "Mallet head",
        BonusWay.FootShot => "Foot shot",
        BonusWay.CroquetShot => "Croquet shot",
        _ => "Where it lies"
    };

    string Won(Game g) =>
        g.Winner.Length == 1
            ? CroquetGame.NameOf(g.Winner[0]) + " is round and has won"
            : string.Join(" and ", g.Winner.Select(CroquetGame.NameOf)) + " have won";
}
