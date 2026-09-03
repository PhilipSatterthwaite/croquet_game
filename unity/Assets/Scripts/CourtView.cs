using Croquet.Core;
using UnityEngine;

/// <summary>
/// Draws the court from the real Field, and nothing else.
///
/// This is the seam between the two halves of the project, and it is
/// deliberately the first thing built: every dimension on screen comes from
/// Croquet.Core, the same code the tests run against. If the hoops are in the
/// wrong place here, they are in the wrong place in the game.
///
/// It builds its sprites in code rather than from art assets, so there is
/// nothing to import and nothing to wire up in the inspector -- which also
/// means the whole scene can be rebuilt from this file if it is ever lost.
/// </summary>
[ExecuteAlways]
public class CourtView : MonoBehaviour
{
    [Header("Which rules")]
    public Variant variant = Variant.NineWicket;

    [Header("Colours")]
    public Color lawn = new Color(0.247f, 0.478f, 0.275f);
    public Color line = new Color(1f, 1f, 1f, 0.55f);
    public Color hoopWire = new Color(0.93f, 0.93f, 0.90f);
    public Color pegPaint = new Color(0.95f, 0.89f, 0.78f);

    static readonly Color[] BallColours =
    {
        new Color(0.20f, 0.42f, 0.78f),   // blue
        new Color(0.80f, 0.20f, 0.20f),   // red
        new Color(0.13f, 0.13f, 0.13f),   // black
        new Color(0.95f, 0.80f, 0.15f),   // yellow
        new Color(0.20f, 0.55f, 0.28f),   // green
        new Color(0.90f, 0.45f, 0.12f)    // orange
    };

    Sprite square, disc;
    Material flat;

    void OnEnable() { Rebuild(); }

    // So that changing the variant in the inspector redraws immediately, which
    // is the fastest way to check both courts are right.
    void OnValidate() { if (isActiveAndEnabled) Rebuild(); }

    public void Rebuild()
    {
        Clear();

        square = SolidSprite();
        disc = DiscSprite();
        flat = FlatMaterial();

        var field = Field.For(variant);
        var court = Field.CourtFor(variant);

        DrawLawn(court);
        DrawBoundary(court);
        foreach (var h in field.Hoops) DrawHoop(h);
        foreach (var p in field.Pegs) DrawPeg(p, field.PegRadius);
        DrawStartingBalls(field, court);

        FrameTheCourt(court);
    }

    void Clear()
    {
        // Backwards, because destroying forwards renumbers the children under
        // your feet and silently leaves half of them behind.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
    }

    // ---- the pieces -------------------------------------------------------

    void DrawLawn(CourtSpec c) =>
        Quad("Lawn", (float)(c.Width / 2), (float)(c.Height / 2),
             (float)c.Width, (float)c.Height, lawn, order: 0);

    /// <summary>The boundary, one line width in from the edge as it is mown.</summary>
    void DrawBoundary(CourtSpec c)
    {
        const float w = 0.05f;
        float W = (float)c.Width, H = (float)c.Height;
        Quad("Boundary S", W / 2, 0, W, w, line, 1);
        Quad("Boundary N", W / 2, H, W, w, line, 1);
        Quad("Boundary W", 0, H / 2, w, H, line, 1);
        Quad("Boundary E", W, H / 2, w, H, line, 1);
    }

    /// <summary>
    /// Two posts and no crossbar. A hoop is only its uprights as far as the
    /// simulation is concerned -- a ball passes over the top of the arch -- so
    /// drawing a bar would show an obstacle that is not there.
    /// </summary>
    void DrawHoop(Hoop h)
    {
        float d = (float)(h.WireRadius * 2);
        Disc("Hoop post", (float)h.LeftPost.X, (float)h.LeftPost.Y, d, hoopWire, 3);
        Disc("Hoop post", (float)h.RightPost.X, (float)h.RightPost.Y, d, hoopWire, 3);
    }

    void DrawPeg(Vec2 p, double radius) =>
        Disc("Peg", (float)p.X, (float)p.Y, (float)(radius * 2), pegPaint, 3);

    void DrawStartingBalls(Field field, CourtSpec c)
    {
        int n = variant == Variant.SixWicket ? 4 : 6;
        float d = (float)(c.BallRadius * 2);
        for (int i = 0; i < n; i++)
        {
            // Fanned out along the start spot so they are all visible; the game
            // brings each ball on as its first turn comes round.
            var at = field.StartSpot;
            Disc("Ball " + i, (float)at.X, (float)at.Y + (i - (n - 1) / 2f) * d * 1.4f,
                 d, BallColours[i % BallColours.Length], 4);
        }
    }

    /// <summary>Fits the whole court on screen whatever the aspect ratio.</summary>
    void FrameTheCourt(CourtSpec c)
    {
        var cam = Camera.main;
        if (cam == null) return;

        cam.orthographic = true;
        cam.transform.position = new Vector3((float)(c.Width / 2), (float)(c.Height / 2), -10f);

        float half = (float)(c.Height / 2) * 1.06f;                  // a little air
        float needed = (float)(c.Width / 2) / Mathf.Max(0.01f, cam.aspect) * 1.06f;
        cam.orthographicSize = Mathf.Max(half, needed);
        cam.backgroundColor = new Color(0.85f, 0.82f, 0.77f);
    }

    // ---- primitives -------------------------------------------------------

    GameObject Piece(string name, float x, float y, Color colour, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(x, y, 0);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.color = colour;
        sr.sortingOrder = order;
        sr.sharedMaterial = flat;
        return go;
    }

    /// <summary>
    /// Unlit, deliberately. The default sprite material under the 2D renderer is
    /// LIT, which means two things one does not want here: the colours come out
    /// tinted by whatever 2D light happens to be in the scene rather than the
    /// values written above, and if that light is ever removed the entire court
    /// silently renders black with nothing to indicate why. A flat top-down
    /// court has no use for lighting.
    /// </summary>
    static Material FlatMaterial()
    {
        var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
              ?? Shader.Find("Sprites/Default");
        return new Material(sh) { hideFlags = HideFlags.DontSave };
    }

    void Quad(string name, float x, float y, float w, float h, Color colour, int order)
    {
        var go = Piece(name, x, y, colour, order);
        go.GetComponent<SpriteRenderer>().sprite = square;
        go.transform.localScale = new Vector3(w, h, 1);
    }

    void Disc(string name, float x, float y, float diameter, Color colour, int order)
    {
        var go = Piece(name, x, y, colour, order);
        go.GetComponent<SpriteRenderer>().sprite = disc;
        go.transform.localScale = new Vector3(diameter, diameter, 1);
    }

    /// <summary>One white pixel, one world unit across. Scale gives it its size.</summary>
    static Sprite SolidSprite()
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    /// <summary>A soft-edged circle, likewise one unit across.</summary>
    static Sprite DiscSprite(int size = 64)
    {
        var t = new Texture2D(size, size) { filterMode = FilterMode.Bilinear };
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) +
                                     (y + 0.5f - r) * (y + 0.5f - r));
                // One pixel of feather, so a small ball is not a jagged blob.
                float a = Mathf.Clamp01(r - d);
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
