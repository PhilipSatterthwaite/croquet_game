using Croquet.Core;
using UnityEngine;

/// <summary>
/// Draws the court from the real Field, and nothing else.
///
/// This is the seam between the two halves of the project, and it was
/// deliberately the first thing built: every dimension on screen comes from
/// Croquet.Core, the same code the tests run against. If the hoops are in the
/// wrong place here, they are in the wrong place in the game.
///
/// The furniture only -- lawn, boundary, hoops, pegs. Balls move and belong to
/// <see cref="CroquetGame"/>; where the camera looks belongs to
/// <see cref="CourtCamera"/>. This draws the things that never change once a
/// game has started, so it draws them once.
///
/// It is ExecuteAlways so the court is visible in the scene view without
/// entering play mode, which is the quickest way to check a layout change.
/// </summary>
[ExecuteAlways]
public class CourtView : MonoBehaviour
{
    [Header("Which rules")]
    public Variant variant = Variant.NineWicket;

    [Header("Colours")]
    public Color lawn = new Color(0.247f, 0.478f, 0.275f);

    [Tooltip("The lighter mown band. Barely there on purpose.")]
    public Color stripe = new Color(1f, 1f, 1f, 0.05f);

    [Tooltip("How far the grass runs past the court, as a share of it.")]
    [Range(0f, 1.5f)] public float margin = 0.6f;

    /// <summary>
    /// How strongly the blades show over the lawn beneath them.
    ///
    /// The sprite carries its own alpha -- opaque where a blade is, clear where
    /// the ground shows through -- so this adds blades rather than fog, and it
    /// went to 0.62 on that reasoning. Too far: measured against a bare lawn,
    /// the turf changed by 2.65 grey levels from one pixel to the next where
    /// the lawn alone changed by 0.06, and forty times the local contrast is
    /// not texture, it is grain. A blade three centimetres long is a few
    /// pixels, and a few pixels of hard light and shadow, thousands of times
    /// over, is noise however carefully each one was drawn.
    ///
    /// Low enough that the turf reads as having a surface and not as static.
    /// </summary>
    [Range(0f, 1f)] public float grain = 0.26f;

    /// <summary>
    /// What a blade is made of. Lighter and yellower than the lawn under it,
    /// because a blade catching the light is the brightest thing on a lawn and
    /// grass goes towards yellow as it does, never towards white.
    /// </summary>
    public Color bladePaint = new Color(0.72f, 0.88f, 0.50f);

    /// <summary>
    /// Metres across one tile of grass, along the mow.
    ///
    /// This is now what sets the SIZE OF A BLADE, which is the number that
    /// decides whether the lawn reads as grass or as noise: the tile holds
    /// blades eleven to twenty-two pixels long out of five hundred and twelve,
    /// so at 1.6 m a blade is about four centimetres, and a few pixels on
    /// screen. Turn it up and they become straw; down and they are fuzz again.
    /// </summary>
    [Range(0.3f, 6f)] public float grainScale = 1.6f;

    /// <summary>
    /// How much the tile is squashed across the mow. One leaves it square.
    ///
    /// Was 2.6, and had to come back to 1. Squashing the tile was how the old
    /// NOISE got its direction -- drawn out into streaks that lay the way the
    /// mower ran. Blades do not need it and are ruined by it: a squash flattens
    /// every blade towards the horizontal, so a lawn with a good spread of
    /// angles in the texture comes out as one lawn-wide comb. The blades carry
    /// the mow themselves now, by being dealt around the mow direction rather
    /// than squeezed into it.
    /// </summary>
    [Range(1f, 5f)] public float grainStretch = 1f;

    [Tooltip("The court's own boundary, painted on the grass.")]
    public Color line = new Color(1f, 1f, 1f, 0.8f);

    [Tooltip("Painted white steel. Brighter than white so the shine has somewhere to go.")]
    public Color hoopWire = new Color(1f, 1f, 0.99f);

    [Tooltip("Turned hardwood, sunlit.")]
    public Color pegPaint = new Color(0.98f, 0.79f, 0.52f);

    [Header("Light")]
    /// <summary>
    /// How far a post throws its shadow, as a share of its own width.
    ///
    /// Short. A long throw with a big soft blob under it reads as something
    /// hovering above the grass, which is exactly what the hoops looked like:
    /// a post is driven INTO the lawn, so its shadow starts at its foot.
    /// </summary>
    /// <summary>
    /// There is a narrow band to hit here. Too short and the post sits on top
    /// of its own shadow and you cannot see it at all; too long and the post
    /// reads as hovering above the lawn. Far enough to clear itself, and no
    /// further.
    /// </summary>
    [Range(0f, 1.5f)] public float shadowThrow = 0.7f;

    [Range(1f, 2.5f)] public float shadowSpread = 1.3f;

    [Range(0f, 1f)] public float shadowStrength = 0.78f;

    /// <summary>
    /// Hoops and pegs need different exaggerations, not one shared number.
    ///
    /// A hoop wire is 6 mm and a peg is 38 mm across -- six times thicker. One
    /// multiplier for both leaves the hoops as specks whenever the pegs look
    /// right, and croquet is a game about hoops.
    /// </summary>
    [Header("Legibility")]
    [Range(1f, 14f)] public float hoopScale = 7f;

    [Range(1f, 6f)] public float pegScale = 2.5f;

    /// <summary>
    /// ...and never smaller than this on screen, whatever the zoom. Three times
    /// life size is still a third of a pixel when the whole court is in view,
    /// so the exaggeration has to follow the camera rather than be baked in
    /// once. Same bargain as the balls: honest position, legible size.
    /// </summary>
    [Range(2f, 16f)] public float minFurniturePixels = 6f;

    /// <summary>Named Layout rather than Field so it cannot be read as the type.</summary>
    public Field Layout { get; private set; }
    public CourtSpec Court { get; private set; }

    void OnEnable() { if (!Application.isPlaying) Rebuild(); }

    /// <summary>
    /// So that changing the variant in the inspector redraws immediately,
    /// which is the fastest way to check both courts are right.
    ///
    /// Deferred rather than done here: OnValidate runs inside the editor's own
    /// consistency pass, and building a couple of hundred GameObjects during it
    /// makes Unity log a SendMessage complaint for every component on every one
    /// of them. In play mode it is skipped entirely -- the game owns the court
    /// then, and rebuilding it underneath a running game would drop the balls.
    /// </summary>
    void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || Application.isPlaying || !isActiveAndEnabled) return;
            Rebuild();
        };
#endif
    }

    /// <summary>Draws the court for this component's variant.</summary>
    public void Rebuild() => Rebuild(Field.For(variant), Field.CourtFor(variant));

    /// <summary>
    /// Draws a court the game has already built. The game owns the Field and
    /// the CourtSpec once one is running -- the spec especially, since feel is
    /// tuned on the live one -- so it passes them in rather than having this
    /// make a second pair that could differ.
    /// </summary>
    public void Rebuild(Field field, CourtSpec court)
    {
        root = Shapes.Holder(transform, "Court");
        Clear();
        furniture.Clear();
        shadows.Clear();
        shines.Clear();
        trueSizes.Clear();
        Layout = field;
        Court = court;

        DrawLawn(court);
        DrawBoundary(court);
        foreach (var h in field.Hoops) DrawHoop(h);
        foreach (var p in field.Pegs) DrawPeg(p, field.PegRadius);
    }

    /// <summary>The court's own pieces, kept apart from the balls and the aim.</summary>
    Transform root;

    void Clear()
    {
        // Backwards, because destroying forwards renumbers the children under
        // your feet and silently leaves half of them behind.
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var c = root.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
    }

    // ---- the pieces -------------------------------------------------------

    /// <summary>
    /// Grass runs past the court on every side.
    ///
    /// A lawn cut off exactly at the boundary makes the court look like a green
    /// card lying on a table -- and the surround is the one part of the picture
    /// that is not the game. The grass goes to the edges of the screen and the
    /// court is marked out on it in white, which is what a croquet lawn
    /// actually looks like.
    ///
    /// The margin is a share of the court on each side; it only has to cover
    /// what the camera can see, and CourtCamera keeps the view centred on the
    /// court, so half a court's worth is plenty on any aspect.
    /// </summary>
    void DrawLawn(CourtSpec c)
    {
        float W = (float)c.Width, H = (float)c.Height;
        float mx = W * margin, my = H * margin;

        Quad("Lawn", W / 2, H / 2, W + mx * 2, H + my * 2, lawn, Layer.Lawn);

        // Mown stripes, one mallet-mower width: exactly six feet, alternating.
        // They double as a ruler -- every light band is six feet across, so a
        // distance can be read straight off the grass rather than guessed.
        //
        // Phased so a band starts exactly on the court's own edge, since that
        // is the line the eye measures from.
        const float mower = 6 * 0.3048f;
        float pitch = mower * 2;
        float first = -Mathf.Ceil(mx / pitch) * pitch;

        for (float x = first; x < W + mx; x += pitch)
        {
            float from = Mathf.Max(x, -mx);
            float to = Mathf.Min(x + mower, W + mx);
            if (to <= from) continue;

            Quad("Stripe", (from + to) / 2, H / 2, to - from, H + my * 2,
                 stripe, Layer.Stripe);
        }

        DrawGrain(W + mx * 2, H + my * 2, W / 2, H / 2);
    }

    /// <summary>
    /// The grass itself, tiled over everything below it.
    ///
    /// Tiled rather than stretched, which means a SpriteRenderer in Tiled draw
    /// mode -- and that takes its extent from `size`, not from the transform
    /// scale, so this cannot go through Quad like everything else.
    /// </summary>
    /// <summary>
    /// The painted turf: blades drawn into a tile, cross-faded between two
    /// bent states so they sway.
    ///
    /// Still a picture on a plane, and known to be. `GrassField` next to this
    /// builds the same lawn as real geometry -- see the note there for why it
    /// is not switched on.
    /// </summary>
    void DrawGrain(float w, float h, float cx, float cy)
    {
        if (grain <= 0.001f) return;

        float sx = grainScale, sy = grainScale / grainStretch;

        var paint = bladePaint;
        paint.a = grain;

        var sr = Shapes.Piece(root, "Grass", Shapes.Grass, paint, Layer.Grass);

        sr.drawMode = SpriteDrawMode.Tiled;
        sr.tileMode = SpriteTileMode.Continuous;
        sr.transform.localPosition = new Vector3(cx, cy, 0);
        sr.transform.localScale = new Vector3(sx, sy, 1);

        // size is in the sprite's own units, so the scale above has to come
        // back out of it or the tiles come out the wrong size.
        sr.size = new Vector2(w / sx, h / sy);
    }

    /// <summary>The boundary, one line width in from the edge as it is mown.</summary>
    void DrawBoundary(CourtSpec c)
    {
        const float w = 0.06f;
        float W = (float)c.Width, H = (float)c.Height;
        Quad("Boundary S", W / 2, 0, W, w, line, Layer.Line);
        Quad("Boundary N", W / 2, H, W, w, line, Layer.Line);
        Quad("Boundary W", 0, H / 2, w, H, line, Layer.Line);
        Quad("Boundary E", W, H / 2, w, H, line, Layer.Line);
    }

    /// <summary>
    /// Two posts and no crossbar. A hoop is only its uprights as far as the
    /// simulation is concerned -- a ball passes over the top of the arch -- so
    /// drawing a bar would show an obstacle that is not there.
    ///
    /// The posts stay where the Field puts them and only their thickness is
    /// exaggerated, so the gap a ball has to run stays honest.
    /// </summary>
    void DrawHoop(Hoop h)
    {
        float d = (float)(h.WireRadius * 2);
        Post("Hoop post", (float)h.LeftPost.X, (float)h.LeftPost.Y, d, hoopWire, hoopScale,
             Shapes.Cylinder, shine: true);
        Post("Hoop post", (float)h.RightPost.X, (float)h.RightPost.Y, d, hoopWire, hoopScale,
             Shapes.Cylinder, shine: true);
    }

    void DrawPeg(Vec2 p, double radius) =>
        Post("Peg", (float)p.X, (float)p.Y, (float)(radius * 2), pegPaint, pegScale,
             Shapes.Wood, shine: false);

    /// <summary>
    /// A round piece standing on the grass: a shadow thrown away from the
    /// light, and a shaded top over it. Its size is exaggerated but its
    /// position is not, and both are kept so they can be re-sized as the camera
    /// zooms.
    /// </summary>
    void Post(string name, float x, float y, float trueDiameter, Color colour, float scale,
              Sprite face, bool shine)
    {
        var cast = Shapes.Piece(root, name + " shadow", Shapes.Shadow,
                                new Color(0, 0, 0, shadowStrength), Layer.FurnitureShadow);
        cast.transform.localPosition = new Vector3(x, y, 0);

        var sr = Shapes.Piece(root, name, face, colour, Layer.Furniture);
        sr.transform.localPosition = new Vector3(x, y, 0);

        // Painted steel catches the light; a wooden peg does not, in the same
        // hard way. Never tinted -- a highlight has to be brighter than the
        // thing it sits on, and multiplying can only darken.
        SpriteRenderer glint = null;
        if (shine)
            glint = Shapes.Piece(root, name + " shine", Shapes.Gloss,
                                 new Color(1, 1, 1, 0.75f), Layer.FurnitureShine);

        furniture.Add(sr);
        shadows.Add(cast);
        shines.Add(glint);
        trueSizes.Add(trueDiameter * scale);
    }

    readonly System.Collections.Generic.List<SpriteRenderer> furniture =
        new System.Collections.Generic.List<SpriteRenderer>();

    readonly System.Collections.Generic.List<SpriteRenderer> shadows =
        new System.Collections.Generic.List<SpriteRenderer>();

    /// <summary>One per piece of furniture, null where it does not catch light.</summary>
    readonly System.Collections.Generic.List<SpriteRenderer> shines =
        new System.Collections.Generic.List<SpriteRenderer>();

    readonly System.Collections.Generic.List<float> trueSizes =
        new System.Collections.Generic.List<float>();

    CourtCamera eye;

    void LateUpdate()
    {

        if (eye == null)
        {
            if (Camera.main == null) return;
            eye = Camera.main.GetComponent<CourtCamera>();
            if (eye == null) return;
        }

        float floor = minFurniturePixels * eye.MetresPerPixel;
        var throwOff = -Shapes.Light * shadowThrow;

        for (int i = 0; i < furniture.Count; i++)
        {
            if (furniture[i] == null) continue;

            // trueSizes already carries the piece's own exaggeration; the floor
            // is the last resort for when the whole court is in view.
            float d = Mathf.Max(trueSizes[i], floor);
            var at = furniture[i].transform.localPosition;
            furniture[i].transform.localScale = new Vector3(d, d, 1);

            if (shines[i] != null)
            {
                var lit = new Vector2(at.x, at.y) + Shapes.Light * (d * 0.24f);
                shines[i].Put(lit.x, lit.y, d * 0.40f, d * 0.40f);
            }

            if (shadows[i] == null) continue;

            // A little bigger than the thing casting it and thrown only a
            // little way. A big soft blob thrown a long way reads as something
            // hovering; a post is driven into the lawn.
            float s = d * shadowSpread;
            shadows[i].transform.localPosition =
                new Vector3(at.x + throwOff.x * d, at.y + throwOff.y * d, 0);
            shadows[i].transform.localScale = new Vector3(s, s, 1);
        }
    }

    // ---- primitives -------------------------------------------------------

    void Quad(string name, float x, float y, float w, float h, Color colour, int order) =>
        Shapes.Piece(root, name, Shapes.Solid, colour, order).Put(x, y, w, h);

    void Disc(string name, float x, float y, float diameter, Color colour, int order) =>
        Shapes.Piece(root, name, Shapes.Disc, colour, order).Put(x, y, diameter, diameter);
}
