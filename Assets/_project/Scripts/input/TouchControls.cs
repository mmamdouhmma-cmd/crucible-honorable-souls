using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// On-screen touch controls (faded buttons) for phones and the web build. Built from code on the
/// GameMangers object. Shown on mobile platforms and in WebGL (or when <see cref="forceShow"/> is on for
/// testing with the mouse). Against the AI, Player 1 gets the whole screen: walk and the special modifier
/// on the left, the four PlayStation face buttons and the eight dashes above them on the right. In a
/// two-player match on one device each player gets a half, mirrored. Sizes come from Settings.
/// <see cref="InputManager"/> merges the snapshot with keyboard and pad input.
/// </summary>
public class TouchControls : MonoBehaviour
{
    public static TouchControls Instance { get; private set; }

    [Tooltip("Show the touch layer even on desktop (testing with the mouse).")]
    public bool forceShow = false;
    [Range(0.1f, 1f)] public float idleAlpha = 0.35f;
    [Range(0.1f, 1f)] public float pressedAlpha = 0.75f;

    class TouchButton
    {
        public Image image;
        public Image icon;
        public Text label;
        public bool held;
        public bool tapped;   // set on pointer down, consumed by the read
    }

    class PlayerPad
    {
        public RectTransform root;
        public TouchButton left, right, light, medium, heavy, guard, special;
        public readonly TouchButton[] dash = new TouchButton[8];   // in the order of DashDirs
        public float specialUntil;   // the SP latch expires at this unscaled time (0 = not armed)
    }

    // Screen left/right in x, into the screen in y - the same convention as a flick of the right stick.
    static readonly Vector2[] DashDirs =
    {
        new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f),
        new Vector2(-1f, 0f), new Vector2(1f, 0f),
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f),
    };

    [Tooltip("Seconds the touch SP button stays armed after a tap. Holding it works too, but the same " +
             "thumb walks, so on touch it also latches: tap it, then tap a strike with the other hand.")]
    [Range(0.3f, 3f)] public float specialLatchSeconds = 1f;

    Canvas canvas;
    Font font;
    readonly PlayerPad[] pads = new PlayerPad[2];
    TouchButton pauseButton;
    bool twoPlayers;
    bool visible;
    bool matchRunning;   // a fight is on screen, so the pad belongs up if this is a touch device

    // Settings' button sizes, as fractions of normal.
    float moveSize = 1f, attackSize = 1f, dashSize = 1f;

    /// <summary>Settings changed a button size: re-lays the pads straight away.</summary>
    public void SetSizes(float move, float attack, float dash)
    {
        moveSize = move; attackSize = attack; dashSize = dash;
        if (pads[0] != null) Layout();
    }

    /// <summary>
    /// What Settings says about the on-screen pad: 0 = auto (touch devices only), 1 = always on,
    /// 2 = always off. The auto check can only guess from the browser, so the player gets the last word.
    /// </summary>
    public static int Mode { get; private set; }

    /// <summary>Settings changed the pad mode: remember it and bring the layer in line immediately.</summary>
    public static void SetMode(int mode)
    {
        Mode = Mathf.Clamp(mode, 0, 2);
        if (Instance != null) Instance.ApplyVisibility();
    }

    void ApplyVisibility() => SetVisible(matchRunning && WantsTouch);

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int CrucibleIsTouchDevice();
#endif

    // -1 not worked out yet, 0 desktop, 1 touch device. Settled once per run, and flipped to 1 if a
    // real finger ever lands (a touchscreen laptop, or a browser whose user agent we did not match).
    static int touchDevice = -1;

    /// <summary>
    /// True only where the on-screen pad belongs: a phone or tablet (including a touch browser in the
    /// web build), or when <see cref="forceShow"/> is on for mouse testing. A desktop browser running
    /// the WebGL build gets no buttons: it has a keyboard.
    /// </summary>
    public static bool WantsTouch =>
        Mode == 1 || (Mode != 2 && ((Instance != null && Instance.forceShow) || IsTouchDevice));

    /// <summary>
    /// Whether this is a phone or tablet, as the PAGE reports it rather than as the build target
    /// implies - a PC browser runs the WebGL build too. Public because the render settings have to
    /// make the same call: see <see cref="MobileQuality"/>.
    /// </summary>
    public static bool IsTouchDevice
    {
        get
        {
            if (touchDevice < 0)
            {
                touchDevice = Detect() ? 1 : 0;
                Debug.Log($"TouchControls: touch device = {touchDevice == 1} (mode {Mode})");
            }
            return touchDevice == 1;
        }
    }

    static bool Detect()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // Ask the page, because the WebGL player itself cannot tell a phone from a PC.
        try { return CrucibleIsTouchDevice() == 1; }
        catch (System.Exception) { return Application.isMobilePlatform; }
#else
        return Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld;
#endif
    }


    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
        SetVisible(false);
    }

    /// <summary>Show the layer for a fight. <paramref name="players"/> = 1 (vs AI / online) or 2 (local on one device).</summary>
    public void ShowForMatch(int players)
    {
        matchRunning = true;
        twoPlayers = players == 2;
        Layout();
        SetVisible(WantsTouch);
    }

    public void Hide() { matchRunning = false; SetVisible(false); }

    void SetVisible(bool on)
    {
        visible = on;
        if (canvas != null) canvas.gameObject.SetActive(on);
    }

    /// <summary>Pause button tapped this frame.</summary>
    public bool PauseTapped()
    {
        if (!visible || pauseButton == null) return false;
        bool t = pauseButton.tapped; pauseButton.tapped = false; return t;
    }

    /// <summary>Touch snapshot for a player, merged by the InputManager. Taps fire once.</summary>
    public InputData Read(int player)
    {
        var d = new InputData();
        if (!visible) return d;
        if (player == 2 && !twoPlayers) return d;
        var p = pads[player - 1];
        if (p == null) return d;
        d.moveDirection = (p.right.held ? 1f : 0f) - (p.left.held ? 1f : 0f);
        // One button for both: raising the guard opens the parry window, holding it is the block.
        d.blockHeld = p.guard.held;
        p.guard.tapped = false;
        d.lightAttack = Consume(p.light);
        d.mediumAttack = Consume(p.medium);
        d.heavyAttack = Consume(p.heavy);

        // SP latches: a tap arms the modifier for specialLatchSeconds, and the next strike spends it.
        if (Consume(p.special)) p.specialUntil = Time.unscaledTime + specialLatchSeconds;
        bool armed = Time.unscaledTime < p.specialUntil || p.special.held;
        d.specialModifier = armed;
        if (armed && (d.lightAttack || d.mediumAttack || d.heavyAttack)) p.specialUntil = 0f;
        for (int i = 0; i < p.dash.Length; i++)
            if (Consume(p.dash[i]) && d.dashDirection == Vector2.zero) d.dashDirection = DashDirs[i].normalized;
        return d;
    }

    static bool Consume(TouchButton b) { bool t = b.tapped; b.tapped = false; return t; }

    /// <summary>Every button on a pad except SP, which has its own lit state.</summary>
    static IEnumerable<TouchButton> Buttons(PlayerPad p)
    {
        yield return p.left; yield return p.right;
        yield return p.light; yield return p.medium; yield return p.heavy; yield return p.guard;
        foreach (var b in p.dash) yield return b;
    }

    void Update()
    {
        // A finger on a device we called a desktop (touchscreen laptop, unmatched user agent):
        // believe the finger and bring the pad up for the rest of the run.
        if (touchDevice != 1 && Input.touchCount > 0)
        {
            touchDevice = 1;
            ApplyVisibility();
            // The render settings were decided at startup on the same (wrong) answer, so they get
            // another look now that a real finger has settled it.
            var quality = FindFirstObjectByType<MobileQuality>();
            if (quality != null) quality.Apply();
        }

        if (!visible) return;

        foreach (var p in pads)
        {
            if (p == null) continue;
            foreach (var b in Buttons(p))
            {
                SetAlpha(b, b.held ? pressedAlpha : idleAlpha);
            }
            // SP stays lit for as long as it is armed, so you can see the next strike will be special.
            if (p.special != null)
            {
                bool armed = Time.unscaledTime < p.specialUntil || p.special.held;
                SetAlpha(p.special, armed ? 1f : idleAlpha);
            }
        }
    }

    static void SetAlpha(TouchButton b, float a)
    {
        var c = b.image.color; c.a = a; b.image.color = c;
        // The symbol and name stay brighter than the disc behind them, so a faded pad is still readable.
        float ink = Mathf.Min(1f, a + 0.4f);
        if (b.icon != null) { var i = b.icon.color; i.a = ink; b.icon.color = i; }
        if (b.label != null) { var l = b.label.color; l.a = ink; b.label.color = l; }
    }

    // ------------------------------------------------------------------ building

    void Build()
    {
        var go = new GameObject("Touch Canvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        var white = new Color(0.9f, 0.9f, 0.9f);
        var dashColor = new Color(0.6f, 0.8f, 1f);
        for (int i = 0; i < 2; i++)
        {
            var pad = new PlayerPad();
            var root = new GameObject("Pad" + (i + 1), typeof(RectTransform));
            root.transform.SetParent(go.transform, false);
            pad.root = root.GetComponent<RectTransform>();
            pad.left = MakeButton(pad.root, "Left", null, Arrow, 90f, white);
            pad.right = MakeButton(pad.root, "Right", null, Arrow, -90f, white);
            pad.special = MakeButton(pad.root, "Special", "SPECIAL", null, 0f, new Color(0.85f, 0.6f, 1f));
            // The four PlayStation face buttons, in the colours and the places they have on the pad.
            pad.heavy = MakeButton(pad.root, "Heavy", "HEAVY", GlyphSprite(Glyph.Triangle), 0f, new Color(0.35f, 0.8f, 0.6f));
            pad.medium = MakeButton(pad.root, "Medium", "MEDIUM", GlyphSprite(Glyph.Cross), 0f, new Color(0.4f, 0.6f, 1f));
            pad.light = MakeButton(pad.root, "Light", "LIGHT", GlyphSprite(Glyph.Square), 0f, new Color(0.95f, 0.5f, 0.75f));
            pad.guard = MakeButton(pad.root, "Parry Block", "PARRY/BLOCK", GlyphSprite(Glyph.Ring), 0f, new Color(0.95f, 0.35f, 0.35f));
            for (int d = 0; d < pad.dash.Length; d++)
            {
                float angle = -Mathf.Atan2(DashDirs[d].x, DashDirs[d].y) * Mathf.Rad2Deg;
                pad.dash[d] = MakeButton(pad.root, "Dash" + d, null, Arrow, angle, dashColor);
            }
            pads[i] = pad;
        }
        pauseButton = MakeButton(go.GetComponent<RectTransform>(), "Pause", "II", null, 0f, white);
        pauseButton.image.rectTransform.sizeDelta = new Vector2(90f, 90f);
        Place(pauseButton, 0.5f, 1f, new Vector2(0, -160));   // under the duel clock
    }

    static Sprite Arrow => GlyphSprite(Glyph.Arrow);

    /// <summary>A round button: an optional symbol (rotated by <paramref name="iconAngle"/>) over an optional name.</summary>
    TouchButton MakeButton(RectTransform parent, string name, string label, Sprite icon, float iconAngle, Color color)
    {
        var go = new GameObject("Btn " + name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = GlyphSprite(Glyph.Disc);
        color.a = idleAlpha; img.color = color;
        var b = new TouchButton { image = img };

        if (icon != null)
        {
            var igo = new GameObject("Icon", typeof(RectTransform));
            igo.transform.SetParent(go.transform, false);
            b.icon = igo.AddComponent<Image>();
            b.icon.sprite = icon;
            b.icon.color = Color.white;
            b.icon.raycastTarget = false;
            var irt = igo.GetComponent<RectTransform>();
            // Named face buttons keep the symbol in the upper part and the name under it.
            irt.anchorMin = label != null ? new Vector2(0.27f, 0.4f) : new Vector2(0.18f, 0.18f);
            irt.anchorMax = label != null ? new Vector2(0.73f, 0.86f) : new Vector2(0.82f, 0.82f);
            irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
            irt.localEulerAngles = new Vector3(0f, 0f, iconAngle);
        }
        if (label != null)
        {
            var tgo = new GameObject("Label", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var t = tgo.AddComponent<Text>();
            t.font = font; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = 30;
            t.color = Color.white; t.text = label; t.raycastTarget = false;
            var outline = tgo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = icon != null ? new Vector2(0.03f, 0.12f) : new Vector2(0.08f, 0.2f);
            trt.anchorMax = icon != null ? new Vector2(0.97f, 0.4f) : new Vector2(0.92f, 0.8f);
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            b.label = t;
        }
        var trig = go.AddComponent<EventTrigger>();
        AddTrigger(trig, EventTriggerType.PointerDown, () => { b.held = true; b.tapped = true; });
        AddTrigger(trig, EventTriggerType.PointerUp, () => { b.held = false; });
        AddTrigger(trig, EventTriggerType.PointerExit, () => { b.held = false; });
        return b;
    }

    enum Glyph { Disc, Arrow, Triangle, Cross, Square, Ring }

    static readonly Dictionary<Glyph, Sprite> glyphs = new Dictionary<Glyph, Sprite>();

    /// <summary>Draws the button shapes once, from their distance functions, so no art is needed for them.</summary>
    static Sprite GlyphSprite(Glyph kind)
    {
        if (glyphs.TryGetValue(kind, out var sprite)) return sprite;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                float a = Mathf.Clamp01(0.5f - GlyphDistance(kind, p) * size * 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        glyphs[kind] = sprite;
        return sprite;
    }

    /// <summary>Signed distance to a shape in -1..1 space: negative inside.</summary>
    static float GlyphDistance(Glyph kind, Vector2 p)
    {
        const float stroke = 0.13f;
        switch (kind)
        {
            case Glyph.Disc: return p.magnitude - 0.97f;
            case Glyph.Ring: return Mathf.Abs(p.magnitude - 0.72f) - stroke;
            case Glyph.Square: return Mathf.Abs(Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) - 0.68f) - stroke;
            case Glyph.Cross: return Mathf.Min(Segment(p, new Vector2(-0.72f, -0.72f), new Vector2(0.72f, 0.72f)),
                                               Segment(p, new Vector2(-0.72f, 0.72f), new Vector2(0.72f, -0.72f))) - stroke;
            case Glyph.Triangle: return Mathf.Abs(TriangleDistance(p, new Vector2(0f, 0.8f), new Vector2(0.82f, -0.62f), new Vector2(-0.82f, -0.62f))) - stroke;
            // The arrow: a narrow dart pointing up. A plain triangle looks the same turned by 45 degrees,
            // and the diagonal dashes have to read at a glance.
            default: return Mathf.Max(TriangleDistance(p, new Vector2(0f, 0.86f), new Vector2(0.62f, -0.78f), new Vector2(-0.62f, -0.78f)),
                                      -TriangleDistance(p, new Vector2(0f, -0.22f), new Vector2(0.72f, -1.1f), new Vector2(-0.72f, -1.1f)));
        }
    }

    static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);
        return (p - (a + (b - a) * t)).magnitude;
    }

    static float TriangleDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
        Mathf.Max(Edge(p, a, b), Edge(p, b, c), Edge(p, c, a));

    // Distance past the edge a->b; the triangle's corners run clockwise, so inside is negative.
    static float Edge(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 d = (b - a).normalized;
        return Vector2.Dot(p - a, new Vector2(-d.y, d.x));
    }

    static void AddTrigger(EventTrigger trig, EventTriggerType type, System.Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trig.triggers.Add(entry);
    }

    static void Place(TouchButton b, float ax, float ay, Vector2 pos)
    {
        var rt = b.image.rectTransform;
        rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(ax, ay); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
    }

    static void Place2(TouchButton b, float anchorX, Vector2 pos, float size)
    {
        Place(b, anchorX, 0f, pos);
        b.image.rectTransform.sizeDelta = new Vector2(size, size);
    }

    /// <summary>
    /// Positions the pads: one player = full width; two players = left half (P1) and right half (P2),
    /// mirrored. Left side: walk, with Special above it. Right side: the face buttons in their diamond,
    /// with the eight dashes in a grid above them. Everything is measured up from the bottom corners,
    /// and if the chosen sizes would run a column into the health bars the whole column shrinks to fit.
    /// </summary>
    void Layout()
    {
        // The canvas isn't laid out while hidden, so work its size out the way its scaler does.
        float uiScale = Mathf.Max(Mathf.Sqrt(Screen.width / 1920f * (Screen.height / 1080f)), 0.01f);
        float canvasHeight = Screen.height / uiScale;
        float notch = Mathf.Max(Screen.safeArea.xMin, Screen.width - Screen.safeArea.xMax) / uiScale;
        const float hudClearance = 170f;
        float room = canvasHeight - hudClearance;

        for (int i = 0; i < 2; i++)
        {
            var p = pads[i];
            bool show = i == 0 || twoPlayers;
            p.root.gameObject.SetActive(show);
            if (!show) continue;
            p.root.anchorMin = Vector2.zero; p.root.anchorMax = Vector2.one; p.root.offsetMin = Vector2.zero; p.root.offsetMax = Vector2.zero;
            // horizontal span of this pad in canvas fractions
            float x0 = twoPlayers ? (i == 0 ? 0f : 0.5f) : 0f;
            float x1 = twoPlayers ? (i == 0 ? 0.5f : 1f) : 1f;
            float s = twoPlayers ? 0.8f : 1f;   // scale when sharing the screen
            bool mirror = twoPlayers && i == 1;
            float moveX = mirror ? x1 : x0, strikeX = mirror ? x0 : x1;
            float dir = mirror ? -1f : 1f;      // +1 = the move side grows from the left edge
            float marginX = 36f + notch, marginY = 30f, gap = 18f * s;

            // Move side: the two walk buttons, Special over the middle of them.
            float walk = 175f * s * moveSize, sp = 135f * s * moveSize;
            float moveFit = Mathf.Min(1f, room / (marginY + walk + gap + sp));
            walk *= moveFit; sp *= moveFit;
            Place2(p.left, moveX, new Vector2(dir * (marginX + walk * 0.5f), marginY + walk * 0.5f), walk);
            Place2(p.right, moveX, new Vector2(dir * (marginX + walk * 1.5f + gap), marginY + walk * 0.5f), walk);
            Place2(p.special, moveX, new Vector2(dir * (marginX + walk + gap * 0.5f), marginY + walk + gap + sp * 0.5f), sp);

            // Strike side: diamond first, dashes above.
            float face = 135f * s * attackSize, dash = 92f * s * dashSize;
            float reach = face * 0.86f, step = dash * 1.1f;
            float strikeFit = Mathf.Min(1f, room / (marginY + reach * 2f + face + gap + step * 2f + dash));
            face *= strikeFit; dash *= strikeFit; reach *= strikeFit; step *= strikeFit;
            float cx = -dir * (marginX + Mathf.Max(reach + face * 0.5f, step + dash * 0.5f));
            float cy = marginY + reach + face * 0.5f;
            Place2(p.heavy, strikeX, new Vector2(cx, cy + reach), face);    // triangle, top
            Place2(p.medium, strikeX, new Vector2(cx, cy - reach), face);   // cross, bottom
            Place2(p.light, strikeX, new Vector2(cx - reach, cy), face);    // square, left
            Place2(p.guard, strikeX, new Vector2(cx + reach, cy), face);    // circle, right
            float dashY = cy + reach + face * 0.5f + gap * strikeFit + step + dash * 0.5f;
            for (int d = 0; d < p.dash.Length; d++)
                Place2(p.dash[d], strikeX, new Vector2(cx + DashDirs[d].x * step, dashY + DashDirs[d].y * step), dash);
        }
    }
}
