using UnityEngine;

/// <summary>
/// The handful of sprites the whole game is drawn from, built in code.
///
/// There is no art in this project yet and none of it is waiting on any: a
/// court is a rectangle, a ball is a disc, a mallet is a rounded bar. Building
/// them here means nothing to import, nothing to wire in an inspector, and no
/// prefab that can be lost -- every scene object can be rebuilt from source.
///
/// They are shared and cached rather than made per object, because a court has
/// a couple of hundred pieces and each one owning its own copy of the same
/// 128-pixel disc is a texture upload per hoop post.
/// </summary>
/// <summary>
/// What is drawn over what, in one place.
///
/// Sorting orders scattered across five files is how a shadow ends up on top
/// of the ball casting it, and the fix is always somewhere other than where the
/// problem looks like it is.
/// </summary>
public static class Layer
{
    public const int Lawn = 0;
    public const int Stripe = 1;
    public const int Grass = 2;

    /// <summary>
    /// The shaded patch the aim is taken from. On the TURF, under everything
    /// that stands on it -- it is a change in the grass, not a film over the
    /// balls, and drawn on top it dulled every ball it covered.
    /// </summary>
    public const int AimPatch = 3;

    public const int Line = 4;
    public const int Target = 5;
    public const int FurnitureShadow = 6;
    public const int Furniture = 7;
    public const int FurnitureShine = 8;
    public const int BallShadow = 9;
    public const int Ball = 10;
    public const int Gloss = 11;
    public const int Striker = 12;
    public const int AimRing = 13;
    public const int AimLine = 14;
    public const int Mallet = 15;

    /// <summary>
    /// How far towards the camera everything above the turf is lifted, in
    /// metres. Comfortably more than a blade is tall, and far less than the
    /// camera's ten.
    /// </summary>
    public const float Above = -0.4f;
}

public static class Shapes
{
    static Sprite solid, disc, ring, sphere, cylinder, wood, shadow, gloss, rounded;
    static Sprite chevron, fade, line, dashes, gust, grass;
    static Material flat;

    /// <summary>
    /// Where the light comes from: over the player's left shoulder.
    ///
    /// One direction, shared by everything, because a court lit from two
    /// directions reads as a collage. Shadows fall the other way, and the
    /// highlight on a ball sits on this side of it.
    /// </summary>
    public static readonly Vector2 Light = new Vector2(-0.78f, 0.44f);

    /// <summary>
    /// How high the sun is, against the across-the-lawn direction above.
    ///
    /// Low. Overhead light flattens everything: a ball lit from straight up is
    /// a bright disc with a thin dark rim and no sense of being round at all,
    /// and its shadow hides underneath it. Raking light gives a proper
    /// terminator across the ball and throws the shadow out where it can be
    /// seen -- which is the whole of what makes a top-down court look lit
    /// rather than coloured in.
    /// </summary>
    public const float Elevation = 0.70f;

    /// <summary>
    /// How big the round sprites are baked, in pixels across.
    ///
    /// Generous on purpose. These are stretched to whatever the zoom asks for,
    /// and the aiming ring in particular is drawn three metres wide -- a couple
    /// of hundred pixels on screen. Baked at 128 it was being magnified nearly
    /// twice and every edge in the game looked soft. Four of these at 512 is a
    /// few megabytes, once, for the life of the process.
    /// </summary>
    const int Res = 512;

    /// <summary>
    /// Whether a cached sprite still has anything in it.
    ///
    /// Checking the sprite alone is not enough, and getting this wrong cost a
    /// whole court. These are built with HideFlags.DontSave so they never end
    /// up serialised into the scene, and that flag makes Unity destroy the
    /// TEXTURE when the editor crosses in or out of play mode. The Sprite
    /// wrapping it has no such flag and survives -- so a plain null check on
    /// the sprite passes, and every piece drawn from it renders as nothing at
    /// all, with no error anywhere to say why.
    /// </summary>
    static bool Spent(Sprite s) => s == null || s.texture == null;

    /// <summary>One white pixel, one world unit across. Scale gives it its size.</summary>
    public static Sprite Solid
    {
        get
        {
            if (Spent(solid))
            {
                var t = New(1, 1);
                t.SetPixel(0, 0, Color.white);
                t.Apply();
                solid = Named(Sprite.Create(t, new Rect(0, 0, 1, 1), Half, 1f), "Solid");
            }
            return solid;
        }
    }

    /// <summary>A soft-edged circle, one unit across.</summary>
    public static Sprite Disc
    {
        get
        {
            if (Spent(disc)) disc = Named(Circle(0f), "Disc");
            return disc;
        }
    }

    /// <summary>A hollow circle, one unit across, for the aiming ring.</summary>
    public static Sprite Ring
    {
        get
        {
            if (Spent(ring)) ring = Named(Circle(0.86f), "Ring");
            return ring;
        }
    }

    /// <summary>
    /// A straight line, one unit long and one across, with FEATHERED long
    /// edges.
    ///
    /// Drawn from a single white pixel it steps: a thin quad turned off the
    /// horizontal has hard edges, nothing is anti-aliasing them, and what you
    /// see is a staircase of horizontal runs rather than a line. The fix is in
    /// the texture, not the geometry -- an alpha ramp across the width, so the
    /// edge is soft before it is ever rotated, and the ramp scales with the
    /// line's thickness so it stays about a pixel at any size.
    /// </summary>
    public static Sprite Line
    {
        get
        {
            if (Spent(line))
            {
                // Square, because a sprite has ONE pixels-per-unit: a tall thin
                // texture would come out a tall thin sprite, and Put would then
                // be stretching something that was never one unit by one.
                const int size = 64;
                var t = New(size, size);

                for (int y = 0; y < size; y++)
                {
                    // Distance from the middle of the width, 0 at the centre
                    // and 1 at the edge.
                    float off = Mathf.Abs(y + 0.5f - size / 2f) / (size / 2f);
                    float a = Mathf.Clamp01((1f - off) / 0.16f);

                    for (int x = 0; x < size; x++) t.SetPixel(x, y, new Color(1, 1, 1, a));
                }

                t.Apply();
                line = Named(Sprite.Create(t, new Rect(0, 0, size, size), Half, size), "Line");
            }
            return line;
        }
    }

    /// <summary>
    /// A ring of dashes, one unit across: the marker for the hoop being played
    /// for.
    ///
    /// A solid ring round a hoop is the loudest thing on the court and it is
    /// only saying "this one". Broken into dashes it reads as a marker rather
    /// than as paint on the grass, and -- because a dashed ring has a visible
    /// phase -- it can be turned slowly, which the eye picks up at the edge of
    /// vision without it ever having to be bright.
    /// </summary>
    public static Sprite Dashes
    {
        get
        {
            if (Spent(dashes)) dashes = Named(Broken(0.945f, 12, 0.45f), "Dashes");
            return dashes;
        }
    }

    /// <summary>
    /// A lit sphere, in greyscale, one unit across.
    ///
    /// The trick that makes this worth having is that a SpriteRenderer
    /// MULTIPLIES its colour through the sprite -- so one shaded white ball,
    /// tinted, gives six shaded coloured balls with no per-colour artwork. The
    /// values run from about a third on the far side to full at the lit side,
    /// which is the shading; the white glint on top of it is a separate sprite,
    /// because multiplying can only ever darken and a specular highlight has to
    /// be brighter than the thing it sits on.
    /// </summary>
    /// <summary>How many grooves are cut round the ball, over a full turn.</summary>
    const float Grooves = 46f;

    /// <summary>How deeply the milling shades. Small: it is texture, not pattern.</summary>
    const float Milling = 0.11f;

    public static Sprite Sphere
    {
        get
        {
            if (!Spent(sphere)) return sphere;

            var t = New(Res, Res);
            float r = Res / 2f;

            // Up out of the lawn, so the ball is lit from above and to one side
            // rather than edge-on.
            var lit = new Vector3(Light.x, Light.y, Elevation).normalized;

            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    float nx = (x + 0.5f - r) / r;
                    float ny = (y + 0.5f - r) / r;
                    float flat2 = nx * nx + ny * ny;

                    if (flat2 >= 1f) { t.SetPixel(x, y, new Color(1, 1, 1, 0)); continue; }

                    // The surface normal of a sphere seen from directly above.
                    float flat = Mathf.Sqrt(flat2);
                    float nz = Mathf.Sqrt(1f - flat2);
                    float lambert = Mathf.Max(0f, nx * lit.x + ny * lit.y + nz * lit.z);

                    // Ambient keeps the dark side a ball rather than a hole, and
                    // the shoulder darkens the very edge so it turns away.
                    float shade = 0.34f + 0.66f * lambert;
                    shade *= Mathf.Lerp(0.82f, 1f, nz);

                    // The milling: fine grooves cut round the ball's middle, as
                    // every real croquet ball has. Spaced evenly in ANGLE round
                    // the sphere rather than evenly across the picture, so they
                    // crowd together toward the rim exactly the way a real one's
                    // do when you look down on it -- which is most of what says
                    // "sphere" rather than "circle".
                    //
                    // Only the band round the middle is milled; the crown is
                    // smooth, so it fades in rather than reaching the centre.
                    float theta = Mathf.Asin(Mathf.Clamp01(flat));
                    float cut = 0.5f - 0.5f * Mathf.Cos(theta * Grooves);
                    shade *= 1f - Milling * Mathf.SmoothStep(0.30f, 0.72f, flat) * cut;

                    float a = Mathf.Clamp01((1f - flat) * r);
                    t.SetPixel(x, y, new Color(shade, shade, shade, a));
                }

            t.Apply();
            sphere = Named(Sprite.Create(t, new Rect(0, 0, Res, Res), Half, Res), "Sphere");
            return sphere;
        }
    }

    /// <summary>
    /// A cylinder seen from above: a hoop upright or a peg, not a ball.
    ///
    /// Shading these as spheres was wrong twice over. A post is a tube with a
    /// flat top, so from overhead it is a nearly even disc -- and shading it
    /// round made the hoops read as beads resting on the grass rather than
    /// wire driven into it.
    ///
    /// What says "cylinder" is the edge, not the middle: a flat top with only
    /// the faintest gradient across it, and a firm dark rim where the top face
    /// turns over into the side. The height comes from the shadow, which is the
    /// only place it can come from in a view looking straight down.
    /// </summary>
    public static Sprite Cylinder
    {
        get
        {
            if (!Spent(cylinder)) return cylinder;

            var t = New(Res, Res);
            float r = Res / 2f;
            var lit = Light.normalized;

            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    float nx = (x + 0.5f - r) / r;
                    float ny = (y + 0.5f - r) / r;
                    float flat2 = nx * nx + ny * ny;

                    if (flat2 >= 1f) { t.SetPixel(x, y, new Color(1, 1, 1, 0)); continue; }

                    float d = Mathf.Sqrt(flat2);

                    // The top face is flat, so it takes one even light. Just
                    // enough tilt across it to stop it reading as a paper disc.
                    float shade = 1f + 0.02f * (nx * lit.x + ny * lit.y);

                    // The rim: the turn from the top face into the side wall.
                    // This is the whole of what makes it a tube.
                    //
                    // Kept thin and shallow, and that is not a taste call. A
                    // hoop post is a handful of pixels across, so a rim starting
                    // at four fifths of the radius is a third of its whole area
                    // -- which turned white wire into grey dots once it was
                    // minified. Everything the eye reads as "post" comes from
                    // the shadow instead.
                    shade *= Mathf.Lerp(1f, 0.74f, Mathf.SmoothStep(0.90f, 1f, d));

                    // And a hint of the far wall catching less light, which puts
                    // the top face a little above the ground.
                    shade *= 1f - 0.05f * Mathf.SmoothStep(0.74f, 1f, d)
                                        * Mathf.Clamp01(-(nx * lit.x + ny * lit.y));

                    float a = Mathf.Clamp01((1f - d) * r);
                    t.SetPixel(x, y, new Color(shade, shade, shade, a));
                }

            t.Apply();
            cylinder = Named(Sprite.Create(t, new Rect(0, 0, Res, Res), Half, Res), "Cylinder");
            return cylinder;
        }
    }

    /// <summary>
    /// The top of a wooden peg: a cylinder with end grain on it.
    ///
    /// Looking straight down at a peg you are looking at the end of a piece of
    /// wood, so what you see is growth rings -- not the long grain you would
    /// see from the side. Off-centre, because a peg is rarely turned from the
    /// exact middle of the tree, and that asymmetry is most of what stops it
    /// reading as a printed target.
    /// </summary>
    public static Sprite Wood
    {
        get
        {
            if (!Spent(wood)) return wood;

            var t = New(Res, Res);
            float r = Res / 2f;
            var lit = Light.normalized;

            // Where the heart of the tree sits, off to one side.
            const float heartX = -0.22f, heartY = 0.13f;

            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    float nx = (x + 0.5f - r) / r;
                    float ny = (y + 0.5f - r) / r;
                    float flat2 = nx * nx + ny * ny;

                    if (flat2 >= 1f) { t.SetPixel(x, y, new Color(1, 1, 1, 0)); continue; }

                    float d = Mathf.Sqrt(flat2);
                    float shade = 1f + 0.02f * (nx * lit.x + ny * lit.y);

                    // Growth rings about the heart, tightening outward the way
                    // real ones do.
                    float hx = nx - heartX, hy = ny - heartY;
                    float fromHeart = Mathf.Sqrt(hx * hx + hy * hy);
                    // Few enough to survive being drawn at forty pixels. Rings
                    // finer than that are gone by the time anybody sees them.
                    float rings = 0.5f - 0.5f * Mathf.Cos(fromHeart * 13f + fromHeart * fromHeart * 6f);
                    shade *= 1f - 0.15f * rings;

                    // The same rim as any other post: this is still a cylinder.
                    shade *= Mathf.Lerp(1f, 0.74f, Mathf.SmoothStep(0.90f, 1f, d));
                    shade *= 1f - 0.05f * Mathf.SmoothStep(0.74f, 1f, d)
                                        * Mathf.Clamp01(-(nx * lit.x + ny * lit.y));

                    float a = Mathf.Clamp01((1f - d) * r);
                    t.SetPixel(x, y, new Color(shade, shade, shade, a));
                }

            t.Apply();
            wood = Named(Sprite.Create(t, new Rect(0, 0, Res, Res), Half, Res), "Wood");
            return wood;
        }
    }

    /// <summary>
    /// Mown grass, as a seamless tile laid over the whole lawn.
    ///
    /// A flat green rectangle reads as paper however well the rest is lit. This
    /// is a few octaves of value noise on a lattice that WRAPS -- which is the
    /// only fiddly part, because a tile whose edges do not meet shows a grid
    /// across the court, and a grid is far worse than no texture at all.
    ///
    /// Greyscale around a half, drawn at low opacity, so it both lightens and
    /// darkens rather than only frosting the surface.
    /// </summary>
    /// <summary>
    /// The lawn, baked once and left alone.
    ///
    /// It swayed for a while -- two bakes of the same blades curved opposite
    /// ways, cross-faded -- and it was wrong. A croquet lawn is a still thing
    /// to look at while you are judging a line across it, and grass that
    /// breathes pulls the eye off the shot every second of the game. It also
    /// cost two 512-pixel bakes at startup instead of one, for a movement
    /// nobody wanted.
    ///
    /// The blades still CURVE, in coherent patches, because that is what grass
    /// does lying under its own weight. It just does not change.
    /// </summary>
    public static Sprite Grass
    {
        get
        {
            if (Spent(grass)) grass = Named(Turf(0.5f), "Grass");
            return grass;
        }
    }

    /// <summary>
    /// Blades as lit TUBES that cast their own shadows.
    ///
    /// The objection this answers is a fair one: a flat stroke of a single
    /// brightness is a picture of a blade, and no amount of it stops a lawn
    /// reading as paint. What makes real turf look three-dimensional from
    /// directly overhead is not that the blades stand up -- seen from above, a
    /// vertical blade is a dot. It is that they LEAN, so you see along their
    /// sides, and that every one throws a shadow onto the turf beside it.
    ///
    /// So each blade is drawn twice. Once as a shadow, sheared away from the
    /// light by an amount growing from nothing at the root to its full height
    /// at the tip -- which is what a shadow does, and is why it fans out from
    /// the base instead of sitting under the blade like a sticker. Then again
    /// as a tube, shaded ACROSS its width from a real surface normal: bright
    /// down the side facing the light, dark down the other, rounded between.
    /// That cross-section is the whole difference between a stroke and a
    /// cylinder.
    ///
    /// And they OVERLAP rather than average. A pixel takes the topmost blade
    /// covering it, not the mean of every blade touching it. Averaging is what
    /// makes a crowd of blades dissolve into a wash: two crossing blades come
    /// out as one mid-grey smear instead of one in front of the other.
    /// </summary>
    static Sprite Turf(float lean)
    {
        const int size = 512;

        // Many, and short. A croquet lawn is mown, and a mown lawn is dense
        // and low -- long sparse blades read as a meadow nobody has touched.
        const int blades = 13000;

        var shadow = new float[size * size];
        var value = new float[size * size];
        var cover = new float[size * size];

        var rng = new System.Random(7);
        float Next() => (float)rng.NextDouble();

        // The light as a direction in SPACE, not on the page: the flat Light
        // everything else uses, tipped up by Elevation. Without that third
        // component there is no such thing as a surface facing the sky, and
        // every blade comes out lit the same whichever way it is turned.
        var sun = new Vector3(Light.x, Light.y, Elevation).normalized;

        for (int b = 0; b < blades; b++)
        {
            var root = new Vector2(Next() * size, Next() * size);

            // Lying around the mow, both ways, loosely.
            float turn = (Next() - 0.5f) * 1.3f;
            float angle = (Next() < 0.5f ? 0f : Mathf.PI) + turn;
            var run = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var side = new Vector2(-run.y, run.x);

            float span = 9f + Next() * 9f;
            float tall = 1.4f + Next() * 3.4f;

            // How far this blade is pushed over. Coarse noise over the tile,
            // so a whole patch leans together rather than each blade going its
            // own way -- the lie a lawn has after it has been rolled.
            float gust = Noise(root.x / size, root.y / size, 3, 3, 21) - 0.5f;
            float curve = lean * gust * span * 0.75f;

            int steps = Mathf.CeilToInt(span * 1.4f);

            // Its shadow first. It has to follow the bend, or a swaying blade
            // slides about over a shadow nailed to the ground.
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var on = root + run * (span * t) + side * (curve * t * t);
                var cast = on - new Vector2(Light.x, Light.y) * (tall * t);

                Smudge(shadow, size, cast.x, cast.y, 1.15f, 0.40f * (0.4f + 0.6f * t));
            }

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var spine = root + run * (span * t) + side * (curve * t * t);

                float half = 1.0f * (1 - t * 0.6f);

                for (int k = -2; k <= 2; k++)
                {
                    float u = k / 2f;
                    var on = spine + side * (u * half);

                    // The normal across the tube: leaning out to the side at
                    // the edges, facing the sky along the middle.
                    float up = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
                    var n = new Vector3(side.x * u, side.y * u, up);

                    float lit = 0.16f + 0.84f * Mathf.Max(0f, Vector3.Dot(n, sun));
                    lit *= 0.62f + 0.5f * t;      // tips catch more than roots

                    Lay(value, cover, size, on.x, on.y, 0.8f, lit);
                }
            }
        }

        // SetPixels once rather than SetPixel a quarter of a million times.
        // With two of these baked at startup the difference is seconds.
        var pixels = new Color[size * size];

        for (int i = 0; i < pixels.Length; i++)
        {
            float c = Mathf.Clamp01(cover[i]);
            float dark = Mathf.Clamp01(shadow[i]);

            // Bare turf is ground in shade; a blade is its own brightness,
            // dimmed by whatever stands over it.
            float ground = 0.34f * (1 - dark * 0.75f);
            float blade = Mathf.Clamp01(value[i]) * (1 - dark * 0.35f);

            float v = Mathf.Lerp(ground, blade, c);
            pixels[i] = new Color(v, v, v, Mathf.Max(0.40f, c));
        }

        var tex = New(size, size, repeat: true);
        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), Half, size,
                             0, SpriteMeshType.FullRect);
    }

    /// <summary>A soft dot of shade, wrapping at the tile edges.</summary>
    static void Smudge(float[] into, int size, float cx, float cy,
                       float radius, float weight)
    {
        int reach = Mathf.CeilToInt(radius);

        for (int oy = -reach; oy <= reach; oy++)
            for (int ox = -reach; ox <= reach; ox++)
            {
                float px = Mathf.Floor(cx) + ox + 0.5f, py = Mathf.Floor(cy) + oy + 0.5f;
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));

                float a = Mathf.Clamp01((radius - d) / radius) * weight;
                if (a <= 0) continue;

                int ix = ((Mathf.FloorToInt(px) % size) + size) % size;
                int iy = ((Mathf.FloorToInt(py) % size) + size) % size;

                int j = iy * size + ix;
                into[j] = into[j] + a * (1 - into[j]);       // shade, never past black
            }
    }

    /// <summary>
    /// Lays a piece of blade down, TOPMOST WINS rather than averaged, so one
    /// blade crossing another hides it instead of blending with it.
    /// </summary>
    static void Lay(float[] value, float[] cover, int size,
                    float cx, float cy, float radius, float lit)
    {
        int reach = Mathf.CeilToInt(radius);

        for (int oy = -reach; oy <= reach; oy++)
            for (int ox = -reach; ox <= reach; ox++)
            {
                float px = Mathf.Floor(cx) + ox + 0.5f, py = Mathf.Floor(cy) + oy + 0.5f;
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));

                float a = Mathf.Clamp01((radius - d) / Mathf.Max(0.4f, radius));
                if (a <= 0.01f) continue;

                int ix = ((Mathf.FloorToInt(px) % size) + size) % size;
                int iy = ((Mathf.FloorToInt(py) % size) + size) % size;
                int j = iy * size + ix;

                if (a <= cover[j]) continue;
                cover[j] = a;
                value[j] = lit;
            }
    }

    /// <summary>
    /// A broad, soft, seamless pattern for the wind to drag across the lawn.
    ///
    /// Wind on grass, seen from above, is not blades waving -- at this distance
    /// no blade is a pixel. It is BANDS of light and dark sweeping over the
    /// turf, because a gust lays a whole patch of grass over and the light
    /// catches its side instead of its top. So the thing that moves is not the
    /// grass texture, which would read as the whole lawn sliding, but a slow
    /// pattern of shading over the top of it.
    ///
    /// Much lower frequency than the grass and much softer, for the same
    /// reason: a gust is metres across, not millimetres.
    /// </summary>
    public static Sprite Gust
    {
        get
        {
            if (!Spent(gust)) return gust;

            const int size = 128;
            var t = New(size, size, repeat: true);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;

                    // Drawn out along the mow like the grass, so a gust runs
                    // with the stripes rather than across them.
                    float n = 0.62f * Noise(u, v, 2, 5, 11)
                            + 0.38f * Noise(u, v, 4, 11, 12);

                    float shade = Mathf.Clamp01(0.5f + (n - 0.5f) * 1.15f);
                    t.SetPixel(x, y, new Color(shade, shade, shade, 1f));
                }

            t.Apply();
            gust = Named(Sprite.Create(t, new Rect(0, 0, size, size), Half, size,
                                       0, SpriteMeshType.FullRect), "Gust");
            return gust;
        }
    }

    /// <summary>
    /// Value noise on a lattice, wrapping at the edges. The two periods are
    /// separate so the noise can be stretched one way -- a low period across x
    /// and a high one across y draws it out into streaks along x.
    /// </summary>
    static float Noise(float u, float v, int px, int py, int seed)
    {
        float fx = u * px, fy = v * py;
        int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0, ty = fy - y0;

        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);

        float a = Lattice(x0, y0, px, py, seed);
        float b = Lattice(x0 + 1, y0, px, py, seed);
        float c = Lattice(x0, y0 + 1, px, py, seed);
        float d = Lattice(x0 + 1, y0 + 1, px, py, seed);

        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    /// <summary>
    /// A repeatable random at a lattice point. The modulo is what makes the
    /// tile seamless: the point past the right edge IS the point at the left.
    /// </summary>
    static float Lattice(int x, int y, int px, int py, int seed)
    {
        x = ((x % px) + px) % px;
        y = ((y % py) + py) % py;

        unchecked
        {
            int h = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    /// <summary>
    /// The striker: a chevron pointing along +x, tapering to fine points.
    ///
    /// One sprite rather than two rotated bars. Bars meet at a hard corner and
    /// hold the same thickness the whole way out, which is what made the first
    /// attempt look like a piece of clip art; a shape that thickens toward the
    /// point and thins to nothing at the tips reads as something moving, and
    /// costs the same to draw.
    /// </summary>
    public static Sprite Chevron
    {
        get
        {
            if (!Spent(chevron)) return chevron;

            const int size = 256;
            var t = New(size, size);

            var apex = new Vector2(0.90f, 0.5f);
            var tipA = new Vector2(0.16f, 0.03f);
            var tipB = new Vector2(0.16f, 0.97f);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);

                    float a = Limb(p, apex, tipA, out float ta);
                    float b = Limb(p, apex, tipB, out float tb);

                    // Whichever arm is nearer, and how far out along it -- the
                    // taper is a function of that distance from the point.
                    bool first = a <= b;
                    float d = first ? a : b;
                    float along = first ? ta : tb;

                    float width = Mathf.Lerp(0.085f, 0.012f, along * along);
                    float alpha = Mathf.Clamp01((width - d) * size * 0.35f);

                    t.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }

            t.Apply();
            chevron = Named(Sprite.Create(t, new Rect(0, 0, size, size), Half, size), "Chevron");
            return chevron;
        }
    }

    /// <summary>Distance from a point to a segment, and how far along it that was.</summary>
    static float Limb(Vector2 p, Vector2 a, Vector2 b, out float along)
    {
        var ab = b - a;
        along = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * along);
    }

    /// <summary>
    /// A one-way fade, opaque at its left edge and gone by its right, pivoted
    /// on that left edge so it can be pinned at one end and stretched to the
    /// other. The swing's streak, and anything else that has to trail off.
    /// </summary>
    public static Sprite Fade
    {
        get
        {
            if (!Spent(fade)) return fade;

            const int w = 128;
            var t = New(w, 4);

            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                float a = 1f - u;
                a *= a;                                  // gone quickly, not evenly

                for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(1, 1, 1, a));
            }

            t.Apply();
            fade = Named(Sprite.Create(t, new Rect(0, 0, w, 4), new Vector2(0f, 0.5f), w),
                         "Fade");
            return fade;
        }
    }

    /// <summary>
    /// A rounded rectangle for the interface, nine-sliced so the corners keep
    /// their radius at any size. Use it with <c>Image.type = Sliced</c>.
    /// </summary>
    public static Sprite Rounded
    {
        get
        {
            if (!Spent(rounded)) return rounded;

            const int size = 96;
            const int radius = 28;
            var t = New(size, size);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance outside the rounded rectangle, which is zero
                    // everywhere except the corners.
                    float dx = Mathf.Max(0, Mathf.Max(radius - (x + 0.5f),
                                                      (x + 0.5f) - (size - radius)));
                    float dy = Mathf.Max(0, Mathf.Max(radius - (y + 0.5f),
                                                      (y + 0.5f) - (size - radius)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - d)));
                }

            t.Apply();
            var border = new Vector4(radius, radius, radius, radius);
            rounded = Named(Sprite.Create(t, new Rect(0, 0, size, size), Half, size,
                                          0, SpriteMeshType.FullRect, border), "Rounded");
            return rounded;
        }
    }

    /// <summary>
    /// The glint, drawn over a ball and never tinted. Offset toward the light
    /// by whoever places it.
    /// </summary>
    public static Sprite Gloss
    {
        get
        {
            if (!Spent(gloss)) return gloss;

            const int size = 64;
            var t = New(size, size);
            float r = size / 2f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1, 1, 1, a * a * a));   // tight, soft-edged
                }

            t.Apply();
            gloss = Named(Sprite.Create(t, new Rect(0, 0, size, size), Half, size), "Gloss");
            return gloss;
        }
    }

    /// <summary>
    /// A soft shadow: no edge at all, just a falloff. Drawn in black at low
    /// opacity and offset away from the light, which is the whole of what makes
    /// a flat disc look like a ball sitting on grass rather than a dot painted
    /// on it.
    /// </summary>
    public static Sprite Shadow
    {
        get
        {
            if (!Spent(shadow)) return shadow;

            // Half resolution is plenty: it is a gradient with no edge in it,
            // so there is no detail to lose.
            const int size = Res / 2;
            var t = New(size, size);
            float r = size / 2f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                    float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));

                    // Smoothstep rather than linear: a linear falloff still has
                    // a visible rim where it reaches zero.
                    float a = 1f - (d * d * (3f - 2f * d));
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }

            t.Apply();
            shadow = Named(Sprite.Create(t, new Rect(0, 0, size, size), Half, size), "Shadow");
            return shadow;
        }
    }

    static Sprite Named(Sprite s, string name)
    {
        s.name = name;
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    /// <summary>
    /// Unlit, deliberately. The default sprite material under the 2D renderer
    /// is LIT, which means two things one does not want here: the colours come
    /// out tinted by whatever 2D light happens to be in the scene rather than
    /// the values asked for, and if that light is ever removed the whole court
    /// silently renders black with nothing to indicate why. A flat top-down
    /// court has no use for lighting.
    /// </summary>
    public static Material Flat
    {
        get
        {
            if (flat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                      ?? Shader.Find("Sprites/Default");
                flat = new Material(sh) { hideFlags = HideFlags.DontSave };
            }
            return flat;
        }
    }

    static readonly Vector2 Half = new Vector2(0.5f, 0.5f);

    static Texture2D New(int w, int h, bool repeat = false) => new Texture2D(w, h)
    {
        filterMode = FilterMode.Bilinear,

        // Clamp by default, or a feathered edge samples the far side of itself.
        // Grass is the exception: it is laid down as a tile and has to meet.
        wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
        hideFlags = HideFlags.DontSave
    };

    /// <summary>
    /// A disc, or an annulus if an inner radius is given. Drawn at a good size
    /// and scaled down rather than sized to fit: a ball on a full-court view is
    /// only a few pixels, and letting the mip chain do the shrinking is what
    /// keeps it a round dot instead of a flickering speck.
    /// </summary>
    static Sprite Circle(float inner, int size = Res)
    {
        var t = New(size, size);
        float r = size / 2f;
        float ri = r * inner;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                // One pixel of feather on each edge, so nothing is a jagged blob.
                float a = Mathf.Clamp01(r - d);
                if (inner > 0) a = Mathf.Min(a, Mathf.Clamp01(d - ri));

                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }

        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, size, size), Half, size);
    }

    /// <summary>
    /// An annulus cut into <paramref name="count"/> dashes, each covering
    /// <paramref name="duty"/> of its share of the circle.
    ///
    /// Trigonometry is fine here: this bakes a picture once, and nothing about
    /// it reaches the simulation.
    /// </summary>
    static Sprite Broken(float inner, int count, float duty, int size = Res)
    {
        var t = New(size, size);
        float r = size / 2f;
        float ri = r * inner;

        float slice = Mathf.PI * 2f / count;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                float a = Mathf.Min(Mathf.Clamp01(r - d), Mathf.Clamp01(d - ri));
                if (a > 0)
                {
                    // Where this pixel falls within its dash, 0 to 1.
                    float turn = Mathf.Atan2(dy, dx);
                    float phase = Mathf.Repeat(turn, slice) / slice;

                    // Feathered along the arc as well, by roughly the width of
                    // a pixel at this radius, or the dash ends are as jagged as
                    // the ring used to be.
                    float edge = Mathf.Max(0.02f, 1f / Mathf.Max(1f, d * slice));
                    a = Mathf.Min(a, Mathf.Min(Mathf.Clamp01(phase / edge),
                                               Mathf.Clamp01((duty - phase) / edge)));
                }

                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }

        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, size, size), Half, size);
    }

    /// <summary>
    /// A named child to hang one thing's pieces under, made if it is not there.
    ///
    /// The court, the balls and the aiming furniture are drawn by three
    /// components on one object, and each of them clears and rebuilds its own
    /// pieces. Without a holder each they are siblings in one heap, and the
    /// first one to rebuild destroys the other two -- which is not a subtle
    /// failure but it is an invisible one, because what is left still looks
    /// like a court.
    /// </summary>
    public static Transform Holder(Transform parent, string name)
    {
        var found = parent.Find(name);
        if (found != null) return found;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// <summary>A sprite object parented to <paramref name="under"/>, ready to place.</summary>
    public static SpriteRenderer Piece(Transform under, string name, Sprite sprite,
                                       Color colour, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(under, false);

        // Anything standing ON the turf is lifted towards the camera, clear of
        // the blades. The grass is real geometry now and writes depth, so a
        // ball left flat on z = 0 is genuinely BEHIND the grass in front of it
        // and gets cut in half by it. Lifting is the honest fix: the ball
        // really is above the grass.
        go.transform.localPosition = new Vector3(0, 0, order > Layer.Grass ? Layer.Above : 0f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = colour;
        sr.sortingOrder = order;
        sr.sharedMaterial = Flat;
        return sr;
    }

    /// <summary>Places a piece at a point on the lawn, sized in metres.</summary>
    public static void Put(this SpriteRenderer sr, float x, float y, float w, float h)
    {
        sr.transform.localPosition = new Vector3(x, y, sr.transform.localPosition.z);
        sr.transform.localScale = new Vector3(w, h, 1);
    }

    /// <summary>Places a piece along a line, centred on it.</summary>
    public static void PutRotated(this SpriteRenderer sr, Vector2 at, float degrees,
                                  float length, float thickness)
    {
        sr.transform.localPosition = new Vector3(at.x, at.y, sr.transform.localPosition.z);
        sr.transform.localRotation = Quaternion.Euler(0, 0, degrees);
        sr.transform.localScale = new Vector3(length, thickness, 1);
    }

    /// <summary>
    /// Places a left-pivoted piece -- <see cref="Fade"/> -- starting at a point
    /// and running out along a bearing.
    /// </summary>
    public static void PutFrom(this SpriteRenderer sr, Vector2 at, float degrees,
                               float length, float thickness)
    {
        sr.transform.localPosition = new Vector3(at.x, at.y, sr.transform.localPosition.z);
        sr.transform.localRotation = Quaternion.Euler(0, 0, degrees);
        sr.transform.localScale = new Vector3(length, thickness, 1);
    }
}
