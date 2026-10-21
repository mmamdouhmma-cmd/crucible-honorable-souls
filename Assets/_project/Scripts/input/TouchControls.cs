using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// On-screen touch controls (faded buttons) for phones and the web build. Built from code on the
/// GameMangers object. Shown on mobile platforms and in WebGL (or when <see cref="forceShow"/> is on for
/// testing with the mouse). Against the AI, Player 1 gets the whole screen: move and dash on the left,
/// strikes and block on the right. In a two-player match on one device each player gets a half, mirrored.
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
        public Text label;
        public bool held;
        public bool tapped;   // set on pointer down, consumed by the read
    }

    class PlayerPad
    {
        public RectTransform root;
        public TouchButton left, right, dashL, dashR, dashIn, dashOut, light, medium, heavy, block, parry, special;
        public float specialUntil;   // the SP latch expires at this unscaled time (0 = not armed)
    }

    [Tooltip("Seconds the touch SP button stays armed after a tap. The special modifier is a HOLD on a " +
             "pad, and a thumb cannot hold it and press a strike at the same time, so on touch it latches.")]
    [Range(0.3f, 3f)] public float specialLatchSeconds = 1f;

    Canvas canvas;
    Font font;
    readonly PlayerPad[] pads = new PlayerPad[2];
    TouchButton pauseButton;
    bool twoPlayers;
    bool visible;
    bool matchRunning;   // a fight is on screen, so the pad belongs up if this is a touch device

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
        d.blockHeld = p.block.held;
        d.defenseButton = Consume(p.parry);   // a parry is a tap, never a hold
        d.lightAttack = Consume(p.light);
        d.mediumAttack = Consume(p.medium);
        d.heavyAttack = Consume(p.heavy);

        // SP latches: a tap arms the modifier for specialLatchSeconds, and the next strike spends it.
        if (Consume(p.special)) p.specialUntil = Time.unscaledTime + specialLatchSeconds;
        bool armed = Time.unscaledTime < p.specialUntil;
        d.specialModifier = armed;
        if (armed && (d.lightAttack || d.mediumAttack || d.heavyAttack)) p.specialUntil = 0f;
        if (Consume(p.dashL)) d.dashDirection = Vector2.left;
        else if (Consume(p.dashR)) d.dashDirection = Vector2.right;
        else if (Consume(p.dashIn)) d.dashDirection = Vector2.up;
        else if (Consume(p.dashOut)) d.dashDirection = Vector2.down;
        return d;
    }

    static bool Consume(TouchButton b) { bool t = b.tapped; b.tapped = false; return t; }

    /// <summary>Every button on a pad except SP, which has its own lit state.</summary>
    static TouchButton[] Buttons(PlayerPad p) => new[]
    {
        p.left, p.right, p.dashL, p.dashR, p.dashIn, p.dashOut,
        p.light, p.medium, p.heavy, p.block, p.parry
    };

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
                var c = b.image.color; c.a = b.held ? pressedAlpha : idleAlpha; b.image.color = c;
            }
            // SP stays lit for as long as it is armed, so you can see the next strike will be special.
            if (p.special != null)
            {
                bool armed = Time.unscaledTime < p.specialUntil;
                var c = p.special.image.color;
                c.a = armed ? 1f : (p.special.held ? pressedAlpha : idleAlpha);
                p.special.image.color = c;
                p.special.label.color = armed ? new Color(0.05f, 0.05f, 0.05f, 1f) : new Color(0.1f, 0.1f, 0.1f, 0.9f);
            }
        }
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

        for (int i = 0; i < 2; i++)
        {
            var pad = new PlayerPad();
            var root = new GameObject("Pad" + (i + 1), typeof(RectTransform));
            root.transform.SetParent(go.transform, false);
            pad.root = root.GetComponent<RectTransform>();
            pad.left = MakeButton(pad.root, "<", 150, 150, new Color(0.9f, 0.9f, 0.9f));
            pad.right = MakeButton(pad.root, ">", 150, 150, new Color(0.9f, 0.9f, 0.9f));
            pad.dashL = MakeButton(pad.root, "<<", 100, 100, new Color(0.6f, 0.8f, 1f));
            pad.dashR = MakeButton(pad.root, ">>", 100, 100, new Color(0.6f, 0.8f, 1f));
            pad.dashIn = MakeButton(pad.root, "^", 100, 100, new Color(0.6f, 0.8f, 1f));
            pad.dashOut = MakeButton(pad.root, "v", 100, 100, new Color(0.6f, 0.8f, 1f));
            pad.light = MakeButton(pad.root, "L", 150, 150, new Color(1f, 0.95f, 0.6f));
            pad.medium = MakeButton(pad.root, "M", 150, 150, new Color(1f, 0.75f, 0.45f));
            pad.heavy = MakeButton(pad.root, "H", 150, 150, new Color(1f, 0.5f, 0.4f));
            pad.block = MakeButton(pad.root, "BLOCK", 190, 110, new Color(0.6f, 0.9f, 0.7f));
            pad.parry = MakeButton(pad.root, "PARRY", 190, 110, new Color(0.45f, 0.8f, 1f));
            pad.special = MakeButton(pad.root, "SP", 110, 110, new Color(0.85f, 0.6f, 1f));
            pads[i] = pad;
        }
        pauseButton = MakeButton(go.GetComponent<RectTransform>(), "II", 90, 90, new Color(0.9f, 0.9f, 0.9f));
        Place(pauseButton, 0.5f, 1f, new Vector2(0, -60));
    }

    TouchButton MakeButton(RectTransform parent, string label, float w, float h, Color color)
    {
        var go = new GameObject("Btn " + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        var img = go.AddComponent<Image>();
        color.a = idleAlpha; img.color = color;
        var b = new TouchButton { image = img };
        var tgo = new GameObject("Label", typeof(RectTransform));
        tgo.transform.SetParent(go.transform, false);
        var t = tgo.AddComponent<Text>();
        t.font = font; t.fontSize = label.Length > 2 ? 30 : 48; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(0.1f, 0.1f, 0.1f, 0.9f); t.text = label; t.raycastTarget = false;
        var trt = tgo.GetComponent<RectTransform>(); trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        b.label = t;
        var trig = go.AddComponent<EventTrigger>();
        AddTrigger(trig, EventTriggerType.PointerDown, () => { b.held = true; b.tapped = true; });
        AddTrigger(trig, EventTriggerType.PointerUp, () => { b.held = false; });
        AddTrigger(trig, EventTriggerType.PointerExit, () => { b.held = false; });
        return b;
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

    /// <summary>Positions the pads: one player = full width; two players = left half (P1) and right half (P2), mirrored.</summary>
    void Layout()
    {
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
            p.root.localScale = Vector3.one;
            // movement cluster (left of the span), strike cluster (right of the span); mirrored for P2 in local play
            bool mirror = twoPlayers && i == 1;
            float moveX = mirror ? x1 : x0, strikeX = mirror ? x0 : x1;
            float dir = mirror ? -1f : 1f;     // +1 = cluster grows toward screen centre from the left edge
            float m = 1f;
            Place(p.left,  moveX, 0f, new Vector2(dir * 120 * m * s, 120 * s));
            Place(p.right, moveX, 0f, new Vector2(dir * 300 * m * s, 120 * s));
            Place(p.dashL, moveX, 0f, new Vector2(dir * 120 * s, 290 * s));
            Place(p.dashR, moveX, 0f, new Vector2(dir * 300 * s, 290 * s));
            Place(p.dashIn, moveX, 0f, new Vector2(dir * 210 * s, 400 * s));
            Place(p.dashOut, moveX, 0f, new Vector2(dir * 210 * s, 180 * s));
            Place(p.light, strikeX, 0f, new Vector2(-dir * 360 * s, 120 * s));
            Place(p.medium, strikeX, 0f, new Vector2(-dir * 220 * s, 220 * s));
            Place(p.heavy, strikeX, 0f, new Vector2(-dir * 110 * s, 370 * s));
            Place(p.block, strikeX, 0f, new Vector2(-dir * 140 * s, 90 * s + 420 * s));
            Place(p.parry, strikeX, 0f, new Vector2(-dir * 340 * s, 90 * s + 420 * s));
            // SP sits inboard of the strikes, under the same thumb: tap it, then tap a strike.
            Place(p.special, strikeX, 0f, new Vector2(-dir * 470 * s, 300 * s));
            foreach (var b in Buttons(p))
                b.image.rectTransform.localScale = Vector3.one * s;
            p.special.image.rectTransform.localScale = Vector3.one * s;
            // the P2 pad labels keep screen semantics (< is screen-left) so no relabeling is needed
        }
    }
}
