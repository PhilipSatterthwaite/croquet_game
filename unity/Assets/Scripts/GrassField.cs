using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grass as actual geometry: every blade a ribbon standing up out of the lawn.
///
/// Everything before this was a picture of grass painted on a plane, and no
/// amount of shading fixes that -- the giveaway is that it cannot be looked at
/// from anywhere else. This can. `tilt` turns the camera off the vertical so
/// the lawn can be seen from the side, and if the blades stand up when you do,
/// they were really there; if they smear, they were never anything but a
/// texture. That test is the whole reason to build it this way.
///
/// The camera is orthographic at z = -10 looking towards +z, so **up out of the
/// lawn is -z**. A blade runs from its root at z = 0 to its tip at -height,
/// leaning across the ground as it goes.
///
/// Three things keep it affordable:
///
/// * **Patches, shared and repeated.** A handful of meshes are built once and
///   dropped over the lawn in a grid, with a quarter turn each so the repeat
///   does not read. Unity culls the ones off screen, so a close view draws a
///   dozen patches rather than the whole court.
/// * **Lit at build time**, into the vertex colours -- see Blade.shader. One
///   sun over the whole game, and nothing per frame.
/// * **Swayed by moving the tips only**, on the mesh, so a gust bends the grass
///   rather than sliding it.
/// </summary>
public class GrassField : MonoBehaviour
{
    [Tooltip("Metres across one patch. Smaller culls better and repeats more.")]
    [Range(1f, 8f)] public float patch = 3f;

    [Tooltip("Blades in each patch.")]
    [Range(200, 6000)] public int perPatch = 2200;

    [Tooltip("Different patches built, to break up the repeat.")]
    [Range(1, 8)] public int variants = 4;

    [Tooltip("How far the grass runs past the court, in metres.")]
    [Range(0f, 20f)] public float beyond = 5f;

    [Header("A blade")]
    [Range(0.01f, 0.25f)] public float height = 0.055f;
    [Range(0f, 1.5f)] public float lean = 0.75f;
    [Range(0.002f, 0.03f)] public float width = 0.009f;

    [Header("Colour")]
    public Color deep = new Color(0.13f, 0.26f, 0.11f);
    public Color lit = new Color(0.55f, 0.78f, 0.36f);

    /// <summary>
    /// Off the vertical, in degrees, for looking at the lawn from the side.
    ///
    /// A diagnostic, not a feature: the game is played from directly above and
    /// tilting ruins the aim, because distance foreshortens and a long roquet
    /// stops being judgeable. It is here so that the grass can be PROVED to be
    /// three-dimensional rather than argued about.
    /// </summary>
    [Header("Diagnostic")]
    [Range(0f, 85f)] public float tilt;

    readonly List<Mesh> meshes = new List<Mesh>();
    Material paint;
    Transform root;

    void OnDestroy()
    {
        foreach (var m in meshes) if (m != null) DestroyImmediate(m);
        if (paint != null) DestroyImmediate(paint);
    }

    /// <summary>Lays a field of grass over a court of this size.</summary>
    public void Sow(float width_m, float height_m, int seed = 5)
    {
        Clear();

        var shader = Shader.Find("Croquet/Blade");
        if (shader == null) return;                    // not imported yet

        paint = new Material(shader) { hideFlags = HideFlags.DontSave };
        root = new GameObject("Grass").transform;
        root.SetParent(transform, false);

        for (int i = 0; i < variants; i++) meshes.Add(Patch(seed + i * 7919));

        int across = Mathf.CeilToInt((width_m + beyond * 2) / patch);
        int up = Mathf.CeilToInt((height_m + beyond * 2) / patch);

        var rng = new System.Random(seed);

        for (int y = 0; y < up; y++)
            for (int x = 0; x < across; x++)
            {
                var go = new GameObject("Patch");
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(
                    -beyond + x * patch, -beyond + y * patch, 0);

                // A quarter turn at random. Four meshes and four turnings is
                // sixteen looks, which is enough that the eye stops finding the
                // grid before it finds the blades.
                go.transform.localRotation = Quaternion.Euler(0, 0, rng.Next(4) * 90);

                go.AddComponent<MeshFilter>().sharedMesh = meshes[rng.Next(meshes.Count)];

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = paint;

                // Sorted in with the sprites, so it lands on top of the painted
                // lawn and its stripes and under everything standing on it.
                mr.sortingOrder = Layer.Grass;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
    }

    /// <summary>
    /// Takes the field up, and does it AT ONCE rather than at the end of the
    /// frame.
    ///
    /// Destroy is deferred in play mode, so the old patches outlive the call
    /// that removed them -- and the meshes and material they are drawn from are
    /// gone by then, because this frees those immediately. What is left for a
    /// frame is a set of renderers with no mesh and no material, which is
    /// exactly what a field of grass that never appeared looks like from the
    /// outside. They go together or not at all.
    /// </summary>
    public void Clear()
    {
        if (root != null)
        {
            DestroyImmediate(root.gameObject);
            root = null;
        }

        foreach (var m in meshes) if (m != null) DestroyImmediate(m);
        meshes.Clear();

        if (paint != null) { DestroyImmediate(paint); paint = null; }
    }

    /// <summary>
    /// One patch of blades, as a single mesh.
    ///
    /// Each blade is four quads up its length, tapering to a point, so it can
    /// curve instead of being a flat spike. Five rows of two vertices, and the
    /// last row pinched together into the tip.
    /// </summary>
    Mesh Patch(int seed)
    {
        const int rows = 5;

        var rng = new System.Random(seed);
        float Next() => (float)rng.NextDouble();

        int n = perPatch;
        var verts = new Vector3[n * (rows * 2 - 1)];
        var cols = new Color[verts.Length];
        var tris = new int[n * ((rows - 2) * 6 + 3)];

        // The sun as a direction in space. Up out of the lawn is -z, so the
        // light comes from above as a NEGATIVE z, matching Shapes.Elevation.
        var sun = new Vector3(Shapes.Light.x, Shapes.Light.y, -Shapes.Elevation).normalized;

        int v = 0, t = 0;

        for (int b = 0; b < n; b++)
        {
            var root2 = new Vector2(Next() * patch, Next() * patch);

            float angle = Next() * Mathf.PI * 2;
            var run = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var side = new Vector2(-run.y, run.x);

            float tall = height * (0.55f + Next() * 0.9f);
            float bend = lean * (0.4f + Next() * 1.2f);
            float half = width * 0.5f * (0.7f + Next() * 0.6f);

            // The face of the ribbon: across the blade and up out of the
            // ground. This is what catches the light, and it is why a blade
            // lying across the sun is bright and one pointing at it is not.
            var face = new Vector3(side.x, side.y, 0);
            var along = new Vector3(run.x * bend * tall, run.y * bend * tall, -tall);
            var normal = Vector3.Cross(along, face).normalized;

            float lambert = Mathf.Abs(Vector3.Dot(normal, sun));
            float shade = 0.22f + 0.78f * lambert;

            int first = v;

            for (int r = 0; r < rows; r++)
            {
                float f = r / (float)(rows - 1);

                // Curved: the tip leans further than the middle, which is what
                // a blade under its own weight does.
                var spine = new Vector3(
                    root2.x + run.x * bend * tall * f * f,
                    root2.y + run.y * bend * tall * f * f,
                    -tall * f);

                // Roots sit down in the mat and get less light than tips.
                float depth = shade * (0.35f + 0.65f * f);
                var colour = Color.Lerp(deep, lit, Mathf.Clamp01(depth));

                if (r == rows - 1)
                {
                    verts[v] = spine;
                    cols[v++] = colour;
                }
                else
                {
                    float taper = half * (1 - f * 0.7f);
                    verts[v] = spine + (Vector3)(side * taper) * 1f;
                    cols[v++] = colour;
                    verts[v] = spine - (Vector3)(side * taper) * 1f;
                    cols[v++] = colour;
                }
            }

            for (int r = 0; r < rows - 2; r++)
            {
                int a = first + r * 2, c = first + (r + 1) * 2;
                tris[t++] = a; tris[t++] = c; tris[t++] = a + 1;
                tris[t++] = a + 1; tris[t++] = c; tris[t++] = c + 1;
            }

            int last = first + (rows - 2) * 2;
            tris[t++] = last; tris[t++] = first + (rows * 2 - 2); tris[t++] = last + 1;
        }

        var mesh = new Mesh
        {
            name = "Grass patch",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            hideFlags = HideFlags.DontSave
        };

        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}
