using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

/// <summary>
/// The look of the interface, and the few pieces it is built from.
///
/// Built in code like everything else here, for the same reason: there is no
/// art and no prefab to lose, and a screen can be read as a method rather than
/// hunted for across an inspector. What this buys over immediate mode is a real
/// layout that scales to a phone, buttons that know they are pressed, and one
/// palette instead of whatever GUI.skin happens to be.
///
/// The palette is the court's own -- the same greens and bone whites -- so the
/// interface sits on the lawn rather than on top of it.
/// </summary>
public static class Ui
{
    public static readonly Color Ink = new Color(0.949f, 0.937f, 0.902f);
    public static readonly Color Muted = new Color(0.949f, 0.937f, 0.902f, 0.55f);
    public static readonly Color Panel = new Color(0.063f, 0.098f, 0.075f, 0.90f);
    public static readonly Color Card = new Color(0.086f, 0.133f, 0.102f, 0.97f);
    public static readonly Color Accent = new Color(0.494f, 0.757f, 0.416f);
    public static readonly Color Face = new Color(1f, 1f, 1f, 0.07f);
    public static readonly Color Sunk = new Color(0f, 0f, 0f, 0.28f);

    static Font font;

    /// <summary>
    /// The built-in runtime font. Deliberately not TextMeshPro: TMP needs its
    /// essentials imported into the project before a single label will draw,
    /// and an interface that cannot be built from source alone is one more
    /// thing to go missing.
    /// </summary>
    public static Font Font
    {
        get
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    // ---- the surfaces -----------------------------------------------------

    /// <summary>
    /// A screen-space canvas that scales with the screen.
    ///
    /// Expand, not match-by-height. Matching on height means a tall phone
    /// scales everything by its 1920 pixels and a card designed 560 wide comes
    /// out wider than the 1080-pixel screen it is on -- which is exactly what
    /// happened, and it looked like the interface had been drawn for a
    /// different device. Expand takes the smaller of the two ratios, so the
    /// reference frame always fits inside the screen whatever shape it is.
    /// </summary>
    public static Canvas Screen(Transform under, string name, int order)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler),
                                typeof(GraphicRaycaster));
        go.transform.SetParent(under, false);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1000, 700);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        EnsureEventSystem();
        return canvas;
    }

    /// <summary>
    /// Without one of these nothing on a canvas can be clicked at all, and the
    /// failure looks exactly like buttons that do not work. It has to be the
    /// Input System module, because this project has the old input backend
    /// switched off entirely.
    /// </summary>
    static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem),
                                typeof(InputSystemUIInputModule));
        go.transform.SetParent(null);
    }

    public static RectTransform Rect(Transform under, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(under, false);
        return (RectTransform)go.transform;
    }

    /// <summary>
    /// Colours a rect itself rather than adding a child to it.
    ///
    /// A card carries a layout group, and a layout group lays out every child
    /// it has -- so a background added as a child gets stacked in with the
    /// content and pushes it about. Painting the card's own object keeps the
    /// backdrop out of the layout entirely.
    /// </summary>
    public static Image Skin(RectTransform r, Color colour, bool round = true)
    {
        var img = r.gameObject.AddComponent<Image>();
        img.color = colour;

        // Cards carry a bigger corner than buttons do -- a panel can afford it,
        // and it is what makes the two read as different kinds of surface.
        if (round) Soften(img, 0.7f);
        return img;
    }

    /// <summary>
    /// Gives a panel or a button rounded corners.
    ///
    /// Nine-sliced, so the corner radius is the same on a wide button and a
    /// tall card rather than stretching with them -- which is the difference
    /// between rounded and merely squashed.
    ///
    /// <paramref name="radius"/> is a multiple of the default corner. Keep it
    /// modest: a radius near half the height turns every button into a lozenge,
    /// and a row of lozenges reads as horizontally stretched however square the
    /// button underneath actually is. Rounded corners, not pills.
    /// </summary>
    public static Image Soften(Image img, float radius = 0.42f)
    {
        img.sprite = Shapes.Rounded;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = Mathf.Max(0.05f, 1f / Mathf.Max(0.05f, radius));
        return img;
    }

    /// <summary>
    /// A little ball, for saying something about a ball.
    ///
    /// The same shaded sprite the balls on the lawn are drawn from, so a chart
    /// made of these is made of the same things the court is and needs no key
    /// explaining that a circle means a ball.
    /// </summary>
    public static Image Dot(Transform under, string name, Color colour, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(under, false);

        var img = go.GetComponent<Image>();
        img.sprite = Shapes.Sphere;
        img.color = colour;
        img.preserveAspect = true;

        var le = go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = size;
        le.minHeight = le.preferredHeight = size;
        le.flexibleWidth = le.flexibleHeight = 0;
        return img;
    }

    /// <summary>A flat block of colour.</summary>
    public static Image Block(Transform under, string name, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(under, false);
        var img = go.GetComponent<Image>();
        img.color = colour;
        return img;
    }

    /// <summary>Fills its parent completely.</summary>
    public static T Fill<T>(this T c) where T : Component
    {
        var r = (RectTransform)c.transform;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return c;
    }

    /// <summary>Pins to a corner or edge, with a margin and a size.</summary>
    public static T Pin<T>(this T c, Vector2 anchor, Vector2 offset, Vector2 size)
        where T : Component
    {
        var r = (RectTransform)c.transform;
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = anchor;
        r.anchoredPosition = offset;
        r.sizeDelta = size;
        return c;
    }

    // ---- the pieces -------------------------------------------------------

    public static Text Label(Transform under, string text, int size, Color colour,
                             TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(under, false);

        var t = go.GetComponent<Text>();
        t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.color = colour;
        t.alignment = align;
        t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>A button. Returns it so the caller can keep hold of its label.</summary>
    public static Button Press(Transform under, string text, Action onClick,
                               int size = 19, float height = 42)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(under, false);

        var img = go.GetComponent<Image>();
        img.color = Color.white;
        Soften(img);

        var b = go.GetComponent<Button>();
        b.targetGraphic = img;
        b.colors = Scheme(Face);
        if (onClick != null) b.onClick.AddListener(() => onClick());

        var label = Label(go.transform, text, size, Ink, TextAnchor.MiddleCenter);
        label.Fill();
        label.name = "Text";

        Tall(go, height);
        return b;
    }

    /// <summary>Repaints a button as chosen or not, which is how every setting here reads.</summary>
    public static void Chosen(this Button b, bool on)
    {
        b.colors = Scheme(on ? Accent : Face);
        var t = b.GetComponentInChildren<Text>();
        if (t != null)
        {
            t.color = on ? new Color(0.06f, 0.12f, 0.07f) : Ink;
            t.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
        }
    }

    public static void SetText(this Button b, string text)
    {
        var t = b.GetComponentInChildren<Text>();
        if (t != null) t.text = text;
    }

    /// <summary>The button colours for a given resting face.</summary>
    public static ColorBlock Scheme(Color face)
    {
        var c = ColorBlock.defaultColorBlock;
        c.normalColor = face;
        c.highlightedColor = face * 1.45f + new Color(0, 0, 0, 0.10f);
        c.pressedColor = face * 0.75f + new Color(0, 0, 0, 0.16f);
        c.selectedColor = face;
        c.disabledColor = new Color(1, 1, 1, 0.03f);
        c.fadeDuration = 0.08f;
        return c;
    }

    /// <summary>A labelled slider, for the numbers that decide how the lawn plays.</summary>
    public static Slider Dial(Transform under, string name, float value, float lo, float hi,
                              Action<float> onChange, string format = "0.00")
    {
        var row = Rect(under, name + " row");
        var v = row.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 2;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandHeight = false;

        var head = Rect(row, "head");
        var h = head.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childControlWidth = h.childControlHeight = true;
        head.gameObject.AddComponent<LayoutElement>().minHeight = 22;

        Label(head, name, 16, Muted);
        var read = Label(head, value.ToString(format), 16, Ink, TextAnchor.MiddleRight);

        var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(row, false);
        go.AddComponent<LayoutElement>().minHeight = 22;

        var track = Block(go.transform, "Track", Sunk);
        var tr = (RectTransform)track.transform;
        tr.anchorMin = new Vector2(0, 0.5f);
        tr.anchorMax = new Vector2(1, 0.5f);
        tr.sizeDelta = new Vector2(0, 6);
        tr.anchoredPosition = Vector2.zero;

        var fillArea = Rect(go.transform, "Fill Area");
        fillArea.anchorMin = new Vector2(0, 0.5f);
        fillArea.anchorMax = new Vector2(1, 0.5f);
        fillArea.sizeDelta = new Vector2(-14, 6);
        var fill = Block(fillArea, "Fill", Accent);
        ((RectTransform)fill.transform).sizeDelta = new Vector2(14, 0);

        var handleArea = Rect(go.transform, "Handle Slide Area");
        handleArea.anchorMin = new Vector2(0, 0);
        handleArea.anchorMax = new Vector2(1, 1);
        handleArea.sizeDelta = new Vector2(-14, 0);
        var handle = Block(handleArea, "Handle", Ink);
        ((RectTransform)handle.transform).sizeDelta = new Vector2(14, 18);

        var s = go.GetComponent<Slider>();
        s.fillRect = (RectTransform)fill.transform;
        s.handleRect = (RectTransform)handle.transform;
        s.targetGraphic = handle;
        s.minValue = lo;
        s.maxValue = hi;
        s.SetValueWithoutNotify(value);
        s.onValueChanged.AddListener(x =>
        {
            read.text = x.ToString(format);
            onChange?.Invoke(x);
        });
        return s;
    }

    // ---- layout -----------------------------------------------------------

    /// <summary>
    /// Fixes a height and refuses to stretch.
    ///
    /// flexibleHeight defaults to "no opinion", and a vertical layout then
    /// hands the spare space to whatever it likes -- which turned the Play
    /// button into a green slab eight hundred pixels tall. Saying zero is the
    /// difference between a row that is 52 high and a row that is the rest of
    /// the screen.
    /// </summary>
    public static LayoutElement Tall(GameObject go, float height)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
        le.flexibleHeight = 0;
        return le;
    }

    /// <summary>Turns an existing rect into a column, rather than making a new one.</summary>
    public static VerticalLayoutGroup ColumnOn(RectTransform r, float spacing = 8,
                                               RectOffset pad = null)
    {
        var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = pad ?? new RectOffset(0, 0, 0, 0);
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
        return v;
    }

    public static RectTransform Column(Transform under, string name, float spacing = 8,
                                       RectOffset pad = null)
    {
        var r = Rect(under, name);
        var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = pad ?? new RectOffset(0, 0, 0, 0);
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
        return r;
    }

    /// <summary>
    /// A row of things side by side.
    ///
    /// <paramref name="expand"/> false leaves each child at whatever width it
    /// asked for. Left true, the row shares its whole width out among them --
    /// which is what put a single digit in the middle of a slab a third of the
    /// panel wide, and made every button look horizontally stretched however
    /// square its corners were. Pair it with a <see cref="Filler"/> to take the
    /// slack.
    /// </summary>
    public static RectTransform Row(Transform under, string name, float spacing = 8,
                                    float height = 42, bool expand = true)
    {
        var r = Rect(under, name);
        var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandHeight = true;
        h.childForceExpandWidth = expand;
        h.childAlignment = TextAnchor.MiddleLeft;
        Tall(r.gameObject, height);
        return r;
    }

    /// <summary>Empty, and takes whatever width is left over.</summary>
    public static RectTransform Filler(Transform under)
    {
        var r = Rect(under, "filler");
        var le = r.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.minWidth = 0;
        return r;
    }

    /// <summary>
    /// A scrolling area, returning the column to put content in.
    ///
    /// A settings screen's height depends on what is on it -- six players is
    /// four rows taller than two -- and a card that simply grows runs off the
    /// top and bottom of a phone. Scrolling is the only answer that holds for
    /// content whose size is not known in advance.
    /// </summary>
    public static RectTransform Scroll(Transform under, string name, float spacing = 8)
    {
        var area = Rect(under, name);
        var scroll = area.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 26;

        var viewport = Rect(area, "Viewport");
        viewport.Fill();
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = Column(viewport, "Content", spacing);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = new Vector2(0, content.offsetMin.y);
        content.offsetMax = new Vector2(0, content.offsetMax.y);

        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = content;
        return content;
    }

    /// <summary>A small heading above a group of settings.</summary>
    public static void Heading(Transform under, string text)
    {
        var l = Label(under, text.ToUpper(), 14, Muted);
        Tall(l.gameObject, 20);
    }

    public static void Gap(Transform under, float height)
    {
        var r = Rect(under, "gap");
        Tall(r.gameObject, height);
    }

    /// <summary>Fixes a width on something inside a row.</summary>
    public static T Wide<T>(this T c, float width) where T : Component
    {
        var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = width;
        le.flexibleWidth = 0;
        return c;
    }
}
