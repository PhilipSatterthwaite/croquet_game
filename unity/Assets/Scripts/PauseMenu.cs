using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The pause screen: resume, or give the game up and go back to the menu.
///
/// Deliberately only those two things. Everything else that could be on it is
/// either a decision that belongs before a game -- which is
/// <see cref="StartMenu"/>'s, in its own scene -- or a setting that is the same
/// for every game and belongs to nobody.
///
/// It sets the turn aside rather than freezing time: the machine's search
/// stops, the clock does not, and nothing about the game moves until Resume.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Tooltip("The scene the start menu is in.")]
    public string menuScene = "Menu";

    CroquetGame game;
    Canvas canvas;

    public bool IsUp => canvas != null && canvas.gameObject.activeSelf;

    void Awake()
    {
        game = GetComponent<CroquetGame>();
        Build();
        canvas.gameObject.SetActive(false);
    }

    void Update()
    {
        if (canvas == null) return;

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            if (IsUp) Resume(); else Open();
        }
    }

    public void Open()
    {
        if (game.Game != null && game.Game.Winner != null) return;   // nothing to pause
        game.Pause();
        canvas.gameObject.SetActive(true);
    }

    public void Resume()
    {
        canvas.gameObject.SetActive(false);
        game.Resume();
    }

    void Quit() => SceneManager.LoadScene(menuScene);

    void Build()
    {
        canvas = Ui.Screen(transform, "Pause", order: 30);

        // Enough of a wash to say the game is stopped, not enough to hide it.
        Ui.Block(canvas.transform, "Wash", new Color(0.04f, 0.07f, 0.05f, 0.66f)).Fill();

        var card = Ui.Rect(canvas.transform, "Card");
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(380, 0);

        Ui.Skin(card, Ui.Card);
        Ui.ColumnOn(card, 12, new RectOffset(28, 28, 26, 26));

        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = Ui.Label(card, "Paused", 30, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
        Ui.Tall(title.gameObject, 44);

        Ui.Gap(card, 6);

        Ui.Press(card, "Resume", Resume, 19, 52).Chosen(true);
        Ui.Press(card, "End game", Quit, 18, 48);
    }
}
