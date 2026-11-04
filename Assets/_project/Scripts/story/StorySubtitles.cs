using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The story's on-screen text: one subtitle block at the bottom (speaker, Japanese line, English line - which
/// of the two show is <see cref="StorySettings.SubtitleMode"/>) and the objective at the top centre. Built from
/// code like the HUD. One line at a time: a new line replaces the old, and a bark is dropped while a line is
/// still up or the same bark was heard a moment ago, so reprimands never stack or nag.
/// Sits between the HUD (100) and the menus (200), so the pause menu covers it.
/// </summary>
public class StorySubtitles : MonoBehaviour
{
    [Tooltip("Noto Sans JP, cut down by ArtSource/Fonts/subset_jp.py. Used for both languages.")]
    [SerializeField] Font japaneseFont;
    [Tooltip("A bark (a reprimand, a word of praise) is skipped if another bark played less than this many seconds ago.")]
    [SerializeField] float barkCooldown = 2.5f;

    RectTransform root, subBlock, objBlock, marker;
    Canvas canvasRef;
    Text markerText;
    Vector3 markerWorld;
    bool markerOn;
    Text speakerText, jaText, enText, objLabel, objText, objCount, objExtra;
    // the demonstration (caption + timing bar), the Ready prompt and the controls guide
    RectTransform demoBlock, barBlock, barZone, barCursor, barStrike, guideBlock;
    Text demoTitle, demoText, barZoneText, readyText, guideTitle, guideText;
    System.Func<string> guideSource;
    float guideRefreshAt;
    Image subBack, fade;
    StoryLine current;
    float hideAt = -1f, lastBarkAt = -99f, clock;
    Coroutine objectivePulse;

    public bool IsShowing => current != null;
    public StoryLines Lines { get; set; }

    void Awake()
    {
        Build();
        StorySettings.Changed += Refresh;
    }

    void OnDestroy() => StorySettings.Changed -= Refresh;

    void Update()
    {
        // The story's clock: real seconds, but stopped while the game is paused (timeScale 0).
        if (Time.timeScale > 0f) clock += Time.unscaledDeltaTime;
        if (current != null && hideAt >= 0f && clock >= hideAt) Clear();
        StoryControls.Track();
        if (guideSource != null && Time.unscaledTime >= guideRefreshAt) RefreshGuide();
        if (readyText != null && readyText.gameObject.activeSelf)
            readyText.color = MenuArt.Fade(MenuArt.BronzeLit, 0.65f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f)));
    }

    /// <summary>Seconds on the story clock (unscaled by game speed, frozen by the pause menu).</summary>
    public float Clock => clock;

    /// <summary>Waits <paramref name="seconds"/> on the story clock.</summary>
    public IEnumerator Wait(float seconds)
    {
        float until = clock + seconds;
        while (clock < until) yield return null;
    }

    /// <summary>Shows a line and waits until it has been read.</summary>
    public IEnumerator Say(StoryLine line)
    {
        if (line == null) yield break;
        Show(line);
        yield return Wait(line.Duration);
        if (current == line) Clear();
        yield return Wait(0.15f);   // a breath between consecutive lines
    }

    /// <summary>Shows a line; it hides itself after its duration.</summary>
    public void Show(StoryLine line)
    {
        if (line == null) return;
        current = line;
        hideAt = clock + line.Duration;
        StoryBeats.RaiseLine(line);
        Refresh();
    }

    /// <summary>A short remark during play (reprimand, praise). Dropped rather than stacked: if any line is up,
    /// or another bark played within <see cref="barkCooldown"/>, nothing happens. Returns whether it played.</summary>
    public bool Bark(string idOrGroup)
    {
        if (Lines == null || current != null || clock - lastBarkAt < barkCooldown) return false;
        var line = Lines.Get(idOrGroup);
        if (line == null) return false;
        lastBarkAt = clock;
        Show(line);
        return true;
    }

    /// <summary>Takes the line down now (an exchange reset, the encounter ending).</summary>
    public void Clear()
    {
        current = null;
        hideAt = -1f;
        if (subBlock != null) subBlock.gameObject.SetActive(false);
    }

    /// <summary>Puts up the objective. <paramref name="target"/> 0 = no counter.</summary>
    public void SetObjective(string text, int progress = 0, int target = 0, string extra = null)
    {
        if (objExtra != null) objExtra.text = extra ?? "";
        if (objBlock == null) return;
        bool on = !string.IsNullOrEmpty(text);
        objBlock.gameObject.SetActive(on);
        if (!on) return;
        bool changed = objText.text != text;
        objText.text = text;
        objCount.text = target > 0 ? $"{Mathf.Min(progress, target)} / {target}" : "";
        if (changed || target > 0) Pulse();
    }

    /// <summary>Fades the screen to <paramref name="alpha"/> black over <paramref name="seconds"/> (story clock).</summary>
    public IEnumerator Fade(float alpha, float seconds)
    {
        if (fade == null) yield break;
        fade.gameObject.SetActive(true);
        float from = fade.color.a, t = 0f;
        while (t < seconds)
        {
            if (Time.timeScale > 0f) t += Time.unscaledDeltaTime;
            fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, alpha, seconds > 0f ? t / seconds : 1f));
            yield return null;
        }
        fade.color = new Color(0f, 0f, 0f, alpha);
        if (alpha <= 0f) fade.gameObject.SetActive(false);
    }

    public void ClearObjective() { if (objBlock != null) objBlock.gameObject.SetActive(false); }

    public string ObjectiveText => objBlock != null && objBlock.gameObject.activeSelf ? objText.text : "";
    public string ObjectiveCount => objBlock != null && objBlock.gameObject.activeSelf ? objCount.text : "";
    public string ObjectiveExtra => objBlock != null && objBlock.gameObject.activeSelf && objExtra != null ? objExtra.text : "";
    public string ShownJapanese => current != null && jaText.gameObject.activeSelf ? jaText.text : "";
    public string ShownEnglish => current != null && enText.gameObject.activeSelf ? enText.text : "";

    void Refresh()
    {
        if (subBlock == null) return;
        if (current == null) { subBlock.gameObject.SetActive(false); return; }
        var mode = StorySettings.SubtitleMode;
        bool ja = mode != StorySettings.Subtitles.English && !string.IsNullOrEmpty(current.ja);
        bool en = mode != StorySettings.Subtitles.Japanese && !string.IsNullOrEmpty(current.en);
        if (!ja && !en) en = true;   // a line with no Japanese still shows its English
        string who = Lines != null ? Lines.SpeakerName(current.speaker, mode == StorySettings.Subtitles.Japanese) : "";
        bool title = current.speaker == "title";
        speakerText.text = who;
        speakerText.gameObject.SetActive(who.Length > 0);
        jaText.text = current.ja;
        enText.text = current.en;
        jaText.gameObject.SetActive(ja);
        enText.gameObject.SetActive(en);
        jaText.fontSize = title ? 56 : 44;
        enText.fontSize = title ? (ja ? 30 : 48) : (ja ? 30 : 36);
        enText.color = ja ? MenuArt.Fade(MenuArt.Bone, 0.82f) : MenuArt.Bone;
        subBack.color = new Color(0f, 0f, 0f, title ? 0f : 0.42f);
        subBlock.gameObject.SetActive(true);
    }

    void Pulse()
    {
        if (objectivePulse != null) StopCoroutine(objectivePulse);
        objectivePulse = StartCoroutine(PulseRoutine());
    }

    IEnumerator PulseRoutine()
    {
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            float k = 1f - t / 0.5f;
            objCount.color = Color.Lerp(MenuArt.BronzeLit, Color.white, k);
            objBlock.localScale = Vector3.one * (1f + 0.06f * k);
            yield return null;
        }
        objCount.color = MenuArt.BronzeLit;
        objBlock.localScale = Vector3.one;
        objectivePulse = null;
    }

    // ------------------------------------------------------------------ build

    void Build()
    {
        Font face = japaneseFont;
        if (face == null) { Debug.LogError("Story: StorySubtitles has no Japanese font - Japanese lines will not render"); face = MenuArt.Face; }

        var canvasGo = new GameObject("Story Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvasRef = canvas;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 170;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root = (RectTransform)canvasGo.transform;

        fade = MenuArt.Img(root, "Fade", null, new Color(0f, 0f, 0f, 0f));   // first child: drawn under the text
        MenuArt.Stretch(fade.rectTransform);
        fade.gameObject.SetActive(false);

        // --- subtitles: bottom centre, stacked upward from the bottom edge
        subBlock = MenuArt.Rect(root, "Subtitle");
        subBlock.anchorMin = subBlock.anchorMax = subBlock.pivot = new Vector2(0.5f, 0f);
        subBlock.anchoredPosition = new Vector2(0f, 64f);
        subBlock.sizeDelta = new Vector2(1500f, 10f);
        subBack = MenuArt.Img(subBlock, "Back", null, new Color(0f, 0f, 0f, 0.42f));
        var fit = subBlock.gameObject.AddComponent<VerticalLayoutGroup>();
        fit.childAlignment = TextAnchor.LowerCenter;
        fit.padding = new RectOffset(40, 40, 14, 18);
        fit.spacing = 4f;
        fit.childControlHeight = true; fit.childControlWidth = true;
        fit.childForceExpandHeight = false; fit.childForceExpandWidth = true;
        subBlock.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var backLe = subBack.gameObject.AddComponent<LayoutElement>();
        backLe.ignoreLayout = true;
        MenuArt.Stretch(subBack.rectTransform);

        speakerText = Line(subBlock, "Speaker", face, 24, MenuArt.Bronze);
        jaText = Line(subBlock, "Japanese", face, 44, MenuArt.Bone);
        enText = Line(subBlock, "English", face, 30, MenuArt.Bone);
        subBlock.gameObject.SetActive(false);

        // --- objective: top centre, between the two health blocks
        objBlock = MenuArt.Rect(root, "Objective");
        objBlock.anchorMin = objBlock.anchorMax = objBlock.pivot = new Vector2(0.5f, 1f);
        objBlock.anchoredPosition = new Vector2(0f, -150f);   // under the health blocks, which reach ~140 down
        objBlock.sizeDelta = new Vector2(1000f, 110f);
        objLabel = MenuArt.Shadowed(objBlock, "Label", "OBJECTIVE", 18, TextAnchor.UpperCenter, MenuArt.Bronze);
        objLabel.font = face;
        Place(objLabel.rectTransform, 0f, 24f);
        objText = MenuArt.Shadowed(objBlock, "Text", "", 28, TextAnchor.UpperCenter, MenuArt.Bone);
        objText.font = face;
        objText.horizontalOverflow = HorizontalWrapMode.Wrap;
        Place(objText.rectTransform, -24f, 40f);
        objCount = MenuArt.Shadowed(objBlock, "Count", "", 30, TextAnchor.UpperCenter, MenuArt.BronzeLit, FontStyle.Bold);
        objCount.font = face;
        Place(objCount.rectTransform, -64f, 40f);
        objExtra = MenuArt.Shadowed(objBlock, "Extra", "", 26, TextAnchor.UpperCenter, MenuArt.Bone);
        objExtra.font = face;
        Place(objExtra.rectTransform, -102f, 36f);
        objBlock.gameObject.SetActive(false);

        // --- demonstration: a caption under the objective, and the timing bar under it
        demoBlock = MenuArt.Rect(root, "Demo");
        demoBlock.anchorMin = demoBlock.anchorMax = demoBlock.pivot = new Vector2(0.5f, 1f);
        demoBlock.anchoredPosition = new Vector2(0f, -300f);
        demoBlock.sizeDelta = new Vector2(1300f, 250f);
        var demoBack = MenuArt.Img(demoBlock, "Back", null, new Color(0f, 0f, 0f, 0.5f));
        MenuArt.Stretch(demoBack.rectTransform);
        demoTitle = MenuArt.Shadowed(demoBlock, "Title", "DEMONSTRATION  (slow motion)", 20, TextAnchor.UpperCenter, MenuArt.BloodLit, FontStyle.Bold);
        demoTitle.font = face;
        Place(demoTitle.rectTransform, -12f, 28f);
        demoText = MenuArt.Shadowed(demoBlock, "Text", "", 34, TextAnchor.UpperCenter, MenuArt.Bone);
        demoText.font = face;
        demoText.supportRichText = true;
        demoText.horizontalOverflow = HorizontalWrapMode.Wrap;
        Place(demoText.rectTransform, -46f, 100f);
        // the bar: the incoming strike's wind-up left to right, the window lit, a cursor running along it
        barBlock = MenuArt.Rect(demoBlock, "Bar");
        barBlock.anchorMin = barBlock.anchorMax = barBlock.pivot = new Vector2(0.5f, 0f);
        barBlock.anchoredPosition = new Vector2(0f, 46f);
        barBlock.sizeDelta = new Vector2(900f, 22f);
        MenuArt.Stretch(MenuArt.Img(barBlock, "Track", null, MenuArt.Fade(MenuArt.Charcoal, 0.95f)).rectTransform);
        barZone = MenuArt.Img(barBlock, "Window", null, new Color(0.35f, 0.75f, 0.95f, 0.85f)).rectTransform;
        barZone.anchorMin = new Vector2(0f, 0f); barZone.anchorMax = new Vector2(0f, 1f); barZone.pivot = new Vector2(0f, 0.5f);
        barZoneText = MenuArt.Shadowed(barZone, "Label", "", 22, TextAnchor.LowerCenter, new Color(0.6f, 0.88f, 1f), FontStyle.Bold);
        barZoneText.font = face;
        barZoneText.rectTransform.anchorMin = new Vector2(0f, 1f); barZoneText.rectTransform.anchorMax = new Vector2(1f, 1f);
        barZoneText.rectTransform.pivot = new Vector2(0.5f, 0f);
        barZoneText.rectTransform.anchoredPosition = new Vector2(0f, 4f);
        barZoneText.rectTransform.sizeDelta = new Vector2(200f, 28f);
        barZoneText.horizontalOverflow = HorizontalWrapMode.Overflow;
        barStrike = MenuArt.Img(barBlock, "Strike", null, MenuArt.BloodLit).rectTransform;
        barStrike.anchorMin = new Vector2(0f, 0f); barStrike.anchorMax = new Vector2(0f, 1f); barStrike.pivot = new Vector2(0.5f, 0.5f);
        barStrike.sizeDelta = new Vector2(6f, 16f);
        var strikeLabel = MenuArt.Shadowed(barStrike, "Label", "BLADE LANDS", 18, TextAnchor.UpperCenter, MenuArt.BloodLit, FontStyle.Bold);
        strikeLabel.font = face;
        strikeLabel.rectTransform.anchorMin = new Vector2(0.5f, 0f); strikeLabel.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        strikeLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        strikeLabel.rectTransform.anchoredPosition = new Vector2(0f, -6f);
        strikeLabel.rectTransform.sizeDelta = new Vector2(200f, 24f);
        barCursor = MenuArt.Img(barBlock, "Cursor", null, MenuArt.Bone).rectTransform;
        barCursor.anchorMin = new Vector2(0f, 0f); barCursor.anchorMax = new Vector2(0f, 1f); barCursor.pivot = new Vector2(0.5f, 0.5f);
        barCursor.sizeDelta = new Vector2(5f, 20f);
        barBlock.gameObject.SetActive(false);
        demoBlock.gameObject.SetActive(false);

        // --- the Ready prompt: centre of the screen
        readyText = MenuArt.Shadowed(root, "Ready", "", 40, TextAnchor.MiddleCenter, MenuArt.BronzeLit, FontStyle.Bold);
        readyText.font = face;
        readyText.supportRichText = true;
        readyText.rectTransform.anchorMin = readyText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        readyText.rectTransform.sizeDelta = new Vector2(1400f, 120f);
        readyText.rectTransform.anchoredPosition = new Vector2(0f, -40f);
        readyText.gameObject.SetActive(false);

        // --- the controls guide: the right edge, the current lesson's buttons only
        guideBlock = MenuArt.Rect(root, "Guide");
        guideBlock.anchorMin = guideBlock.anchorMax = guideBlock.pivot = new Vector2(1f, 0.5f);
        guideBlock.anchoredPosition = new Vector2(-28f, 40f);
        guideBlock.sizeDelta = new Vector2(440f, 360f);
        var guideBack = MenuArt.Img(guideBlock, "Back", null, new Color(0f, 0f, 0f, 0.45f));
        MenuArt.Stretch(guideBack.rectTransform);
        guideTitle = MenuArt.Shadowed(guideBlock, "Title", "CONTROLS", 20, TextAnchor.UpperLeft, MenuArt.Bronze, FontStyle.Bold);
        guideTitle.font = face;
        guideTitle.rectTransform.anchorMin = new Vector2(0f, 1f); guideTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
        guideTitle.rectTransform.pivot = new Vector2(0f, 1f);
        guideTitle.rectTransform.anchoredPosition = new Vector2(22f, -14f);
        guideTitle.rectTransform.sizeDelta = new Vector2(-44f, 28f);
        guideText = MenuArt.Shadowed(guideBlock, "Lines", "", 24, TextAnchor.UpperLeft, MenuArt.Bone);
        guideText.font = face;
        guideText.supportRichText = true;
        guideText.lineSpacing = 1.15f;
        guideText.horizontalOverflow = HorizontalWrapMode.Wrap;
        guideText.verticalOverflow = VerticalWrapMode.Overflow;
        guideText.rectTransform.anchorMin = new Vector2(0f, 0f); guideText.rectTransform.anchorMax = new Vector2(1f, 1f);
        guideText.rectTransform.offsetMin = new Vector2(22f, 14f); guideText.rectTransform.offsetMax = new Vector2(-18f, -48f);
        guideBlock.gameObject.SetActive(false);

        // --- objective marker: a small diamond over the place to go, with the distance; held to the screen edge
        // when the place is off screen or behind the camera
        marker = MenuArt.Rect(root, "Marker");
        marker.anchorMin = marker.anchorMax = Vector2.zero;
        marker.pivot = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(120f, 70f);
        var diamond = MenuArt.Img(marker, "Diamond", null, MenuArt.Fade(MenuArt.BronzeLit, 0.9f));
        diamond.rectTransform.sizeDelta = new Vector2(18f, 18f);
        diamond.rectTransform.anchoredPosition = new Vector2(0f, 10f);
        diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        markerText = MenuArt.Shadowed(marker, "Distance", "", 22, TextAnchor.UpperCenter, MenuArt.Bone);
        markerText.rectTransform.anchoredPosition = new Vector2(0f, -18f);
        markerText.rectTransform.sizeDelta = new Vector2(120f, 30f);
        marker.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ demonstration, Ready, guide

    /// <summary>Shows the demonstration caption (rich text). Null hides the panel and its bar.</summary>
    public void SetDemo(string caption)
    {
        if (demoBlock == null) return;
        bool on = caption != null;
        demoBlock.gameObject.SetActive(on);
        if (!on) { barBlock.gameObject.SetActive(false); return; }
        demoText.text = caption;
    }

    /// <summary>The timing bar: the incoming strike from its first frame (0) to <paramref name="total"/> seconds,
    /// the window [from, to] lit and labelled, the blade's landing marked at <paramref name="strike"/>, and the
    /// cursor at <paramref name="now"/>. total 0 or less hides it.</summary>
    public void SetDemoBar(float total, float from, float to, float strike, float now, string label, Color zone)
    {
        if (barBlock == null) return;
        bool on = total > 0f && demoBlock.gameObject.activeSelf;
        barBlock.gameObject.SetActive(on);
        if (!on) return;
        float w = barBlock.sizeDelta.x;
        float X(float t) => Mathf.Clamp01(t / total) * w;
        barZone.anchoredPosition = new Vector2(X(from), 0f);
        barZone.sizeDelta = new Vector2(Mathf.Max(4f, X(to) - X(from)), 0f);
        barZone.GetComponent<Image>().color = zone;
        barZoneText.text = label;
        barZoneText.color = Color.Lerp(zone, Color.white, 0.5f);
        barStrike.anchoredPosition = new Vector2(X(strike), 0f);
        barCursor.anchoredPosition = new Vector2(X(now), 0f);
    }

    public string DemoCaption => demoBlock != null && demoBlock.gameObject.activeSelf ? demoText.text : "";
    public bool DemoBarShown => barBlock != null && barBlock.gameObject.activeInHierarchy;

    /// <summary>The centred prompt (rich text); null hides it.</summary>
    public void SetReady(string text)
    {
        if (readyText == null) return;
        readyText.gameObject.SetActive(text != null);
        if (text != null) readyText.text = text;
    }

    public string ReadyShown => readyText != null && readyText.gameObject.activeSelf ? readyText.text : "";

    /// <summary>The controls guide at the right edge: <paramref name="source"/> builds its lines (re-asked a few times
    /// a second, so the labels follow a switch between pad and keyboard). Null hides it.</summary>
    public void SetGuide(System.Func<string> source)
    {
        guideSource = source;
        guideRefreshAt = 0f;
        if (guideBlock != null) guideBlock.gameObject.SetActive(source != null);
        RefreshGuide();
    }

    public string GuideShown => guideBlock != null && guideBlock.gameObject.activeSelf ? guideText.text : "";

    void RefreshGuide()
    {
        if (guideSource == null || guideText == null) return;
        guideText.text = guideSource();
        guideRefreshAt = Time.unscaledTime + 0.25f;
    }

    /// <summary>Shows the objective marker over <paramref name="world"/> (null hides it).</summary>
    public void SetMarker(Vector3? world)
    {
        markerOn = world.HasValue;
        if (markerOn) markerWorld = world.Value;
        if (marker != null) marker.gameObject.SetActive(markerOn);
    }

    public bool MarkerShown => markerOn && marker != null && marker.gameObject.activeSelf;
    public string MarkerDistance => markerText != null ? markerText.text : "";

    void LateUpdate()
    {
        if (!markerOn || marker == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(markerWorld + Vector3.up * 3.2f);
        if (sp.z < 0f) { sp.x = Screen.width - sp.x; sp.y = 0f; }   // behind: pin to the bottom edge, mirrored
        float m = 60f;
        sp.x = Mathf.Clamp(sp.x, m, Screen.width - m);
        sp.y = Mathf.Clamp(sp.y, m, Screen.height - m);
        float k = canvasRef != null && canvasRef.scaleFactor > 0f ? canvasRef.scaleFactor : 1f;
        marker.anchoredPosition = new Vector2(sp.x / k, sp.y / k);
        float d = Vector3.Distance(cam.transform.position, markerWorld);
        var player = FindAnyObjectByType<ExploreWalker>();
        if (player != null) d = Vector3.Distance(player.transform.position, markerWorld);
        markerText.text = Mathf.RoundToInt(d) + " m";
    }

    static Text Line(RectTransform parent, string name, Font face, int size, Color color)
    {
        var t = MenuArt.Shadowed(parent, name, "", size, TextAnchor.LowerCenter, color);
        t.font = face;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void Place(RectTransform rt, float y, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(0f, h);
    }
}
