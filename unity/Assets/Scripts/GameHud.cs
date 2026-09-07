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
    Button place, menuButton;
    readonly List<Button> wayButtons = new List<Button>();
    readonly List<BonusWay> wayKinds = new List<BonusWay>();

    RectTransform deadPanel;
    Text deadHeading;
    readonly List<RectTransform> deadRows = new List<RectTransform>();
    readonly List<Image> deadOwner = new List<Image>();
    readonly List<Image[]> deadChips = new List<Image[]>();
    readonly List<bool[]> wasDead = new List<bool[]>();
    readonly List<float[]> litAt = new List<float[]>();

    RectTransform over, winner, finalRows;
    Text winnerText;
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
        // held a word.
        meterRow.Pin(new Vector2(0, 1), new Vector2(18, -18), new Vector2(300, 20));

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
    /// The divisions are of the DRAG, not of distance -- they are evenly spaced
    /// because the pull is what your hand is doing, and the curve that turns it
    /// into a roll is not something to be read off a ruler. They are here so a
    /// stroke can be repeated: "that one was four ticks" is the whole of what
    /// they have to support, and a number of metres never told anybody that.
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
    /// Who is dead on whom, bottom right, said in colours.
    ///
    /// One row a ball, and in it a chip for each of the OTHER five. A ball
    /// cannot roquet itself, so its own column was a permanently dark chip in
    /// every row -- a diagonal of nothing, six slots wide, saying only that the
    /// chart knew which row it was on.
    ///
    /// The striker's row is lit so the corner doubles as whose turn it is,
    /// which is the other thing the lawn only half says.
    /// </summary>
    void BuildDeadness()
    {
        deadPanel = Ui.Rect(canvas.transform, "Deadness");
        deadPanel.anchorMin = deadPanel.anchorMax = new Vector2(1, 0);
        deadPanel.pivot = new Vector2(1, 0);
        deadPanel.anchoredPosition = new Vector2(-18, 18);
        deadPanel.sizeDelta = new Vector2(150, 0);

        Ui.Skin(deadPanel, Ui.Panel);
        Ui.ColumnOn(deadPanel, 4, new RectOffset(12, 12, 10, 12));

        var fit = deadPanel.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        deadHeading = Ui.Label(deadPanel, "DEADNESS", 11, Ui.Muted);
        Ui.Tall(deadHeading.gameObject, 15);

        for (int i = 0; i < MaxBalls; i++)
        {
            var row = Ui.Row(deadPanel, "Dead " + i, 5, 22, expand: false);

            // The ball itself, then one small ball for every ball in the game.
            // Same sprite as the balls on the lawn, so the chart is made of the
            // same things the court is and needs no key.
            deadOwner.Add(Ui.Dot(row, "Owner", Color.white, 20));

            var chips = new Image[Others];
            for (int k = 0; k < Others; k++)
                chips[k] = Ui.Dot(row, "On " + k, Color.white, 13);

            Ui.Filler(row);

            deadChips.Add(chips);
            deadRows.Add(row);
            wasDead.Add(new bool[Others]);
            litAt.Add(new float[Others]);
        }
    }

    /// <summary>Which ball slot <paramref name="k"/> of row <paramref name="i"/> stands for.</summary>
    static int Other(int i, int k) => k < i ? k : k + 1;

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

        bool ended = g.Winner != null;
        over.gameObject.SetActive(ended);

        // The rest of the HUD is about the stroke in front of you, and there
        // is not one any more.
        meterRow.gameObject.SetActive(!ended);
        deadPanel.gameObject.SetActive(!ended);
        menuButton.gameObject.SetActive(!ended);

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
        float t = game.WaitingForYou && aim != null ? aim.PullFraction : 0;

        // The bar takes the striker's colour, so the corner says whose stroke
        // it is without spending a word on it.
        var c = game.ColourOf(game.ShownStriker);
        meterFill.color = Color.Lerp(c, Ui.Accent, 0.35f);

        var mf = (RectTransform)meterFill.transform;
        mf.anchorMax = new Vector2(t, 1);

        Scale(t);
    }

    /// <summary>
    /// A little chart, always up: a row for every ball, and in each row a small
    /// ball for every ball in the game.
    ///
    /// Every slot keeps the same place whatever happens, so the chart has a
    /// SHAPE that can be learned -- which a list that packs up and moves about
    /// does not.
    ///
    /// A live chip is DARK, not a faint version of its colour. Dimming the
    /// colour left six muted discs against six bright ones, and the chart had
    /// to be read rather than glanced at; against near-black, colour means
    /// exactly one thing, and it arrives with a small swell so a deadness
    /// picked up while you were watching the ball is not silent.
    /// </summary>
    void Deadness()
    {
        var g = game.Game;
        int count = g.World.Balls.Length;

        for (int i = 0; i < MaxBalls; i++)
        {
            bool playing = i < count && !g.States[i].Finished;
            deadRows[i].gameObject.SetActive(playing);
            if (!playing) continue;

            bool striking = i == game.ShownStriker;
            var own = game.ColourOf(i);

            // The striker's own ball is full strength; everyone else's is
            // dimmed, so whose turn it is falls out of the same chart.
            deadOwner[i].color = striking ? own : own * 0.55f;

            for (int k = 0; k < Others; k++)
            {
                int j = Other(i, k);

                deadChips[i][k].gameObject.SetActive(j < count);
                if (j >= count) continue;

                bool dead = g.States[i].Dead.Contains(j);
                if (dead && !wasDead[i][k]) litAt[i][k] = Time.time;
                wasDead[i][k] = dead;

                var c = game.ColourOf(j);
                deadChips[i][k].color = dead
                    ? c
                    : new Color(c.r * 0.17f, c.g * 0.17f, c.b * 0.17f, 1f);

                // Scale, not layout: the chip swells in place and its
                // neighbours do not shuffle along to make room.
                float since = Time.time - litAt[i][k];
                float swell = dead && since < Pop ? 1f + 0.55f * (1f - since / Pop) : 1f;
                deadChips[i][k].rectTransform.localScale = Vector3.one * swell;
            }
        }
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
