using System.Collections.Generic;
using Croquet.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The screen the game opens on, in a scene of its own.
///
/// A scene rather than a panel over the court. Everything decided here is
/// decided once, before there is a game to decide it about, and pressing Play
/// loads the court -- so the menu is not something the game has to keep hidden
/// and remember to put back, it simply is not there.
///
/// What it does NOT offer is how the lawn plays. Friction and the strength of a
/// full stroke are what the game IS, the same for everybody, and no more a
/// per-match choice than the bounce of a tennis ball. Those live on
/// <see cref="CroquetGame"/>. See <see cref="MatchSettings"/>.
/// </summary>
public class StartMenu : MonoBehaviour
{
    [Tooltip("The scene the court is in.")]
    public string gameScene = "Game";

    Canvas canvas;
    Button nineWicket, association;
    RectTransform ballsRow, sidesRow, players;
    readonly List<Button> ballCounts = new List<Button>();
    readonly List<Button> sideCounts = new List<Button>();
    readonly List<Button> handButtons = new List<Button>();
    Text blurb;

    void Awake()
    {
        // Eight strokes of a pinned match, once, before anything is on screen.
        // It costs a millisecond and it is the only way the IL2CPP answer ever
        // gets measured -- there is no IL2CPP in an editor. `adb logcat` carries
        // it off a phone. See Determinism.
        Debug.Log(Determinism.Report());

        Build();
        Refresh();
    }

    void Build()
    {
        canvas = Ui.Screen(transform, "Start menu", order: 0);

        // The menu owns the whole screen, so it has a ground of its own rather
        // than sitting over whatever happens to be behind it.
        Ui.Block(canvas.transform, "Ground", new Color(0.086f, 0.145f, 0.106f)).Fill();

        var card = Ui.Rect(canvas.transform, "Card");
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(900, 0);

        Ui.Skin(card, Ui.Card);
        Ui.ColumnOn(card, 14, new RectOffset(40, 40, 34, 34));

        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = Ui.Label(card, "CROQUET", 46, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
        Ui.Tall(title.gameObject, 58);

        blurb = Ui.Label(card, "", 16, Ui.Muted, TextAnchor.MiddleCenter);
        Ui.Tall(blurb.gameObject, 24);

        Ui.Gap(card, 10);

        // Two columns rather than one tall list. The screen is landscape, and a
        // single column of six players plus three settings runs off the bottom
        // of it -- which it did.
        var split = Ui.Rect(card, "Split");
        var h = split.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 34;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        h.childAlignment = TextAnchor.UpperCenter;

        var left = Ui.Column(split, "Choices", 6);
        var right = Ui.Column(split, "Players", 6);

        Ui.Heading(left, "The game");
        var games = Ui.Row(left, "Games", 10, 46);
        nineWicket = Ui.Press(games, "Nine wicket", () => Pick(Variant.NineWicket), 17);
        association = Ui.Press(games, "Association", () => Pick(Variant.SixWicket), 17);

        Ui.Gap(left, 6);
        Ui.Heading(left, "Balls");
        // A single digit does not need a third of the panel behind it, so this
        // row does not share its width out and a filler takes the slack.
        ballsRow = Ui.Row(left, "Balls", 10, 46, expand: false);
        foreach (int n in new[] { 2, 4, 6 })
        {
            int count = n;
            ballCounts.Add(Ui.Press(ballsRow, n.ToString(),
                                    () => { MatchSettings.Balls = count; Refresh(); })
                             .Wide(62));
        }
        Ui.Filler(ballsRow);

        Ui.Gap(left, 6);
        Ui.Heading(left, "Sides");
        sidesRow = Ui.Row(left, "Sides", 10, 46);
        foreach (var (label, n) in new[] { ("Each for itself", 0), ("Two sides", 2), ("Three", 3) })
        {
            int sides = n;
            sideCounts.Add(Ui.Press(sidesRow, label,
                                    () => { MatchSettings.Teams = sides; Refresh(); }, 15));
        }

        Ui.Heading(right, "Who plays");
        players = right;
        BuildPlayers();

        Ui.Gap(card, 20);

        // Centred and only as wide as it needs to be. Stretched the full width
        // of the card it read as a banner rather than a button.
        var bottom = Ui.Row(card, "Go", 0, 62, expand: false);
        Ui.Filler(bottom);
        var play = Ui.Press(bottom, "Play", Go, 22, 62).Wide(300);
        Ui.Filler(bottom);
        play.Chosen(true);
    }

    void BuildPlayers()
    {
        for (int i = 0; i < 6; i++)
        {
            int ball = i;
            var row = Ui.Row(players, "Player " + i, 10, 38);

            var dot = Ui.Block(row, "Dot", Colour(i));
            Ui.Soften(dot);
            dot.Wide(20);
            dot.GetComponent<LayoutElement>().minHeight = 20;

            Ui.Label(row, CroquetGame.NameOf(i), 17, Ui.Ink).Wide(90);

            handButtons.Add(Ui.Press(row, "", () =>
            {
                // Round the whole enum, whatever is in it, so adding or
                // dropping a level never leaves a seat that cannot be changed.
                int levels = System.Enum.GetValues(typeof(Hand)).Length;
                MatchSettings.Hands[ball] =
                    (Hand)(((int)MatchSettings.Hands[ball] + 1) % levels);
                Refresh();
            }, 16, 36));
        }
    }

    static Color Colour(int ball)
    {
        // The same six the game uses. Read from there so they cannot drift.
        var c = new[]
        {
            new Color(0.20f, 0.42f, 0.78f), new Color(0.80f, 0.20f, 0.20f),
            new Color(0.13f, 0.13f, 0.13f), new Color(0.95f, 0.80f, 0.15f),
            new Color(0.20f, 0.55f, 0.28f), new Color(0.90f, 0.45f, 0.12f)
        };
        return c[ball % c.Length];
    }

    void Pick(Variant v)
    {
        MatchSettings.Variant = v;
        Refresh();
    }

    void Refresh()
    {
        bool six = MatchSettings.Variant == Variant.SixWicket;

        nineWicket.Chosen(!six);
        association.Chosen(six);

        blurb.text = six
            ? "six hoops run twice, one peg, four balls in two sides"
            : "nine wickets, two stakes, to the USCA basic rules";

        // Association croquet settles both of these itself, so they are not
        // offered rather than offered and ignored.
        ballsRow.gameObject.SetActive(!six);
        sidesRow.gameObject.SetActive(!six);

        int count = MatchSettings.BallsInPlay;
        for (int i = 0; i < ballCounts.Count; i++)
            ballCounts[i].Chosen(new[] { 2, 4, 6 }[i] == count);

        for (int i = 0; i < sideCounts.Count; i++)
        {
            int sides = new[] { 0, 2, 3 }[i];
            bool possible = sides == 0 || count % sides == 0;
            sideCounts[i].interactable = possible;
            sideCounts[i].Chosen(possible && MatchSettings.SidesInPlay == sides);
        }

        for (int i = 0; i < handButtons.Count; i++)
        {
            bool playing = i < count;
            handButtons[i].transform.parent.gameObject.SetActive(playing);
            if (!playing) continue;

            var hand = MatchSettings.Hands[i];
            handButtons[i].SetText(hand == Hand.Human ? "you" : hand.ToString().ToLower());
            handButtons[i].Chosen(hand == Hand.Human);
        }
    }

    void Go() => SceneManager.LoadScene(gameScene);
}
