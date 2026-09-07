using Croquet.Core;
using UnityEngine;

/// <summary>
/// Where the game is watched from.
///
/// A croquet court is 30 metres of lawn and the ball on it is 92 millimetres.
/// Framed whole, honestly, the ball is a third of one per cent of the screen --
/// so the view has to be able to come in close, and then it has to follow the
/// ball, because at any useful magnification a decent stroke travels several
/// screens.
///
/// It watches the ball that is actually moving rather than the one that was
/// struck: a foot shot holds the striker still under the mallet and sends the
/// other ball, so following the striker would be a long steady look at a ball
/// that never moves.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CourtCamera : MonoBehaviour
{
    /// <summary>1 shows the whole court; higher comes in closer.</summary>
    [Range(1f, 8f)] public float zoom = 2.6f;

    public float minZoom = 1f;
    public float maxZoom = 8f;

    /// <summary>Metres per second the view chases its target. Zero snaps.</summary>
    public float follow = 14f;

    public Color surround = new Color(0.85f, 0.82f, 0.77f);

    /// <summary>
    /// A look around, in metres off the ball. Zoomed in far enough to aim
    /// precisely there is no longer a target hoop on screen, so the view has to
    /// be movable without moving the game. Cleared when a stroke is struck.
    /// </summary>
    [HideInInspector] public Vector2 pan;

    Camera cam;
    CourtSpec court;
    Vector2 target;
    bool placed;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;
        cam.backgroundColor = surround;
    }

    /// <summary>Which court is being looked at. Centres on it the first time.</summary>
    public void Watch(CourtSpec c)
    {
        court = c;
        if (!placed)
        {
            target = new Vector2((float)(c.Width / 2), (float)(c.Height / 2));
            LookAt(target, snap: true);
            placed = true;
        }
    }

    /// <summary>Point the view at somewhere on the lawn.</summary>
    public void LookAt(Vec2 p, bool snap = false) =>
        LookAt(new Vector2((float)p.X, (float)p.Y), snap);

    public void LookAt(Vector2 p, bool snap = false)
    {
        target = p;
        if (snap) Apply(1f);
    }

    void LateUpdate()
    {
        if (court == null) return;
        Apply(follow <= 0 ? 1f : 1f - Mathf.Exp(-follow * Time.deltaTime));
    }

    void Apply(float t)
    {
        if (court == null || cam == null) return;

        cam.orthographicSize = SizeFor(zoom);

        // Clamped so the view never slides off the lawn and leaves half the
        // screen showing surround. When the view is wider than the court in a
        // dimension -- which it is at zoom 1 -- there is nothing to clamp and
        // the court is simply centred.
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        float W = (float)court.Width, H = (float)court.Height;

        var want = new Vector2(Clamp(target.x + pan.x, halfW, W),
                               Clamp(target.y + pan.y, halfH, H));
        var now = (Vector2)transform.position;
        var next = Vector2.Lerp(now, want, Mathf.Clamp01(t));

        transform.position = new Vector3(next.x, next.y, -10f);
    }

    static float Clamp(float v, float half, float extent) =>
        extent <= half * 2 ? extent / 2 : Mathf.Clamp(v, half, extent - half);

    /// <summary>
    /// The orthographic size that shows the court divided by the zoom, fitted
    /// to whatever aspect the screen happens to be. At zoom 1 that is the whole
    /// court with a little air, on a phone or on a monitor.
    /// </summary>
    float SizeFor(float z)
    {
        float W = (float)court.Width / z, H = (float)court.Height / z;
        float aspect = Mathf.Max(0.01f, cam.aspect);
        return Mathf.Max(H / 2, W / (2 * aspect)) * 1.04f;
    }

    /// <summary>
    /// How many metres one screen pixel covers. The aiming gesture is measured
    /// in pixels -- how far back the mallet is dragged is a movement of the
    /// hand, not a distance on the lawn -- so everything it draws has to be
    /// converted through this to stay the same size on screen at any zoom.
    /// </summary>
    public float MetresPerPixel =>
        cam == null || Screen.height <= 0 ? 0.01f : cam.orthographicSize * 2f / Screen.height;

    /// <summary>
    /// Metres across the SHORTER side of the screen.
    ///
    /// Anything sized as a fraction of the view -- the aiming ring, the mallet
    /// -- has to be a fraction of this rather than of the height. On a portrait
    /// screen a 2:1 court is fitted by its width, which makes the view very
    /// tall, and a mallet head that was a fixed share of that came out four
    /// times the size of the ball it was addressing.
    /// </summary>
    public float ViewSpan
    {
        get
        {
            if (cam == null) return 1f;
            float h = cam.orthographicSize * 2f;
            return Mathf.Min(h, h * Mathf.Max(0.01f, cam.aspect));
        }
    }

    public Vector2 ToLawn(Vector2 screen) => cam.ScreenToWorldPoint(screen);

    public Vector2 ToScreen(Vec2 lawn) =>
        cam.WorldToScreenPoint(new Vector3((float)lawn.X, (float)lawn.Y, 0));

    public void Zoom(float factor) =>
        zoom = Mathf.Clamp(zoom * factor, minZoom, maxZoom);
}
