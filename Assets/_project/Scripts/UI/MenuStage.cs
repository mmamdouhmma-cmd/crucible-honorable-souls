using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The animated chrome every front-end screen sits in, and the transitions between them.
///
/// Three layers, all built in code on the menu canvas:
///   back   - the dark field, a huge dim logo watermark, drifting embers and a vignette;
///   (the menu's own panels are drawn between these by <see cref="GameMenu"/>);
///   front  - film grain, then the wipe panels and the announcement text, above everything.
///
/// Everything here runs on unscaled time, because the pause menu lives at Time.timeScale 0.
/// </summary>
[DisallowMultipleComponent]
public class MenuStage : MonoBehaviour
{
    public enum Backdrop
    {
        /// <summary>Solid dark field with the logo watermark - the menus outside a fight.</summary>
        Menu,
        /// <summary>A lighter wash, so the arena and the fighters stay readable behind a pause.</summary>
        Pause,
        /// <summary>Only a dimming veil: character and arena select show the real fighters behind it.</summary>
        Stage,
        /// <summary>Nothing but the wipe layer - used while a match is running.</summary>
        None,
        /// <summary>
        /// Pure black and nothing else - no watermark, no bronze glow, no grain, no embers. The
        /// landing page, so the logo fades up out of darkness rather than out of an ember field
        /// that is already lit. APPENDED on purpose: `backdrop` is a serialized field, so the
        /// values above it have to keep their indices.
        /// </summary>
        Black,
    }

    // Serializable so the array survives a domain reload. Without it, recompiling while the game
    // is playing left `embers` null while the plain Image references beside it survived, and the
    // backdrop then threw once per frame.
    [System.Serializable]
    class Ember
    {
        public RectTransform rt;
        public Image img;
        public Vector2 pos, vel;
        public float life, age, size, flicker;
    }

    RectTransform backLayer, frontLayer;
    Image field, watermarkGlow, vignette, grain;
    RawImage watermark;
    Ember[] embers;
    RectTransform wipeLeft, wipeRight;
    Image wipeLeftImg, wipeRightImg;
    MenuArt.Chisel announce;
    RectTransform announceRect;
    CanvasGroup announceGroup;
    Text announceSub;

    Coroutine announceRoutine;
    float fieldTarget = 1f, fieldNow = 1f;
    Backdrop backdrop = Backdrop.Menu;
    float t;

    /// <summary>True while a wipe is covering or uncovering the screen.</summary>
    public bool Wiping { get; private set; }

    // ---------------------------------------------------------------- build

    /// <summary>
    /// Builds both layers inside <paramref name="canvas"/>. The caller is expected to insert its
    /// own content between them (see <see cref="BackLayer"/> / <see cref="FrontLayer"/>).
    /// </summary>
    public void Build(RectTransform canvas)
    {
        backLayer = MenuArt.Rect(canvas, "Stage Back");
        MenuArt.Stretch(backLayer);
        backLayer.SetAsFirstSibling();

        field = MenuArt.Img(backLayer, "Field", MenuArt.Solid, MenuArt.Ink);
        MenuArt.Stretch(field.rectTransform);

        // the logo, enormous and almost invisible, as the wall this whole menu is carved into
        if (MenuArt.Logo != null)
        {
            watermark = MenuArt.Raw(backLayer, "Watermark", MenuArt.Logo, MenuArt.Fade(Color.white, 0.10f));
            MenuArt.Place(watermark.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, Vector2.zero, Vector2.zero);
            // Small enough that the whole wordmark fits on screen: oversized, it read as two
            // enormous cropped letters either side of the panel rather than as a texture.
            watermark.rectTransform.sizeDelta = new Vector2(1480f, 833f);
            watermark.rectTransform.anchoredPosition = new Vector2(0f, 30f);
        }

        watermarkGlow = MenuArt.Img(backLayer, "Hearth", MenuArt.SoftDot, MenuArt.Fade(new Color(0.55f, 0.26f, 0.09f), 0.22f));
        MenuArt.Box(watermarkGlow.rectTransform, new Vector2(0.5f, 0f), new Vector2(2200f, 1400f), new Vector2(0f, 120f));

        embers = new Ember[26];
        for (int i = 0; i < embers.Length; i++)
        {
            var img = MenuArt.Img(backLayer, "Ember" + i, MenuArt.SoftDot, Color.clear);
            var e = new Ember { rt = img.rectTransform, img = img };
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            Respawn(e, UnityEngine.Random.Range(0f, 1f));
            embers[i] = e;
        }

        vignette = MenuArt.Img(backLayer, "Vignette", MenuArt.Vignette, Color.white);
        MenuArt.Stretch(vignette.rectTransform);

        // ---- front
        frontLayer = MenuArt.Rect(canvas, "Stage Front");
        MenuArt.Stretch(frontLayer);
        frontLayer.SetAsLastSibling();

        grain = MenuArt.Img(frontLayer, "Grain", MenuArt.Scratches, MenuArt.Fade(Color.white, 0.055f));
        MenuArt.Stretch(grain.rectTransform);

        wipeLeft = MenuArt.Rect(frontLayer, "Wipe L");
        MenuArt.Place(wipeLeft, 0f, 0f, 0.5f, 1f, Vector2.zero, Vector2.zero);
        wipeLeftImg = MenuArt.Img(wipeLeft, "Fill", MenuArt.Solid, MenuArt.Ink);
        MenuArt.Stretch(wipeLeftImg.rectTransform);
        var edgeL = MenuArt.Img(wipeLeft, "Edge", MenuArt.Solid, MenuArt.Bronze);
        MenuArt.Place(edgeL.rectTransform, 1f, 0f, 1f, 1f, new Vector2(-5f, 0f), new Vector2(0f, 0f));

        wipeRight = MenuArt.Rect(frontLayer, "Wipe R");
        MenuArt.Place(wipeRight, 0.5f, 0f, 1f, 1f, Vector2.zero, Vector2.zero);
        wipeRightImg = MenuArt.Img(wipeRight, "Fill", MenuArt.Solid, MenuArt.Ink);
        MenuArt.Stretch(wipeRightImg.rectTransform);
        var edgeR = MenuArt.Img(wipeRight, "Edge", MenuArt.Solid, MenuArt.Bronze);
        MenuArt.Place(edgeR.rectTransform, 0f, 0f, 0f, 1f, new Vector2(0f, 0f), new Vector2(5f, 0f));

        SetWipe(0f);

        announceRect = MenuArt.Rect(frontLayer, "Announce");
        MenuArt.Place(announceRect, 0f, 0.5f, 1f, 0.5f, new Vector2(0f, -140f), new Vector2(0f, 140f));
        announceGroup = announceRect.gameObject.AddComponent<CanvasGroup>();
        announceGroup.alpha = 0f;
        announce = MenuArt.Chiselled(announceRect, "Text", 150, TextAnchor.MiddleCenter);
        announceSub = MenuArt.Shadowed(announceRect, "Sub", "", 30, TextAnchor.MiddleCenter, MenuArt.Bone, FontStyle.Italic);
        MenuArt.Place(announceSub.rectTransform, 0f, 0f, 1f, 0f, new Vector2(0f, -26f), new Vector2(0f, 14f));

        SetBackdrop(Backdrop.Menu, true);
    }

    /// <summary>Hides the giant logo watermark - the boot screen shows the real logo instead.</summary>
    public void ShowWatermark(bool on)
    {
        if (watermark != null) watermark.enabled = on;
        if (watermarkGlow != null) watermarkGlow.enabled = on;
    }

    /// <summary>Menu content parents here so it draws over the backdrop but under the grain.</summary>
    public RectTransform BackLayer => backLayer;

    /// <summary>The wipe and announcement layer; nothing else should parent here.</summary>
    public RectTransform FrontLayer => frontLayer;

    // ---------------------------------------------------------------- backdrop

    public void SetBackdrop(Backdrop b, bool instant = false)
    {
        backdrop = b;
        fieldTarget = b == Backdrop.Menu || b == Backdrop.Black ? 1f
                    : b == Backdrop.Pause ? 0.78f
                    : b == Backdrop.Stage ? 0.42f : 0f;
        if (instant) fieldNow = fieldTarget;
        ApplyBackdrop();
    }

    void ApplyBackdrop()
    {
        if (field == null) return;

        if (backdrop == Backdrop.Black)
        {
            // Everything the Menu field layers over the ink - the bronze glow at 0.22, the logo
            // watermark, the grain and the drifting embers - is what kept the landing page from
            // being black. All of it is held at zero here, and the field itself is true black
            // rather than MenuArt.Ink (0.039, 0.031, 0.027), which is dark but still warm.
            field.color = MenuArt.Fade(Color.black, fieldNow);
            if (watermark != null) watermark.color = MenuArt.Fade(Color.white, 0f);
            watermarkGlow.color = MenuArt.Fade(Color.black, 0f);
            vignette.color = MenuArt.Fade(Color.white, 1f);
            grain.color = MenuArt.Fade(Color.white, 0f);
            if (embers != null)
                foreach (var e in embers)
                    if (e != null && e.img != null) e.img.enabled = false;
            return;
        }

        bool menu = backdrop == Backdrop.Menu;
        field.color = MenuArt.Fade(MenuArt.Ink, fieldNow * (menu ? 1f : 0.88f));
        if (watermark != null) watermark.color = MenuArt.Fade(Color.white, fieldNow * (menu ? 0.055f : 0.02f));
        watermarkGlow.color = MenuArt.Fade(new Color(0.55f, 0.26f, 0.09f), fieldNow * (menu ? 0.22f : 0.05f));
        vignette.color = MenuArt.Fade(Color.white, Mathf.Lerp(0.55f, 1f, fieldNow));
        grain.color = MenuArt.Fade(Color.white, menu ? 0.03f : 0.012f);
        if (embers != null)
            foreach (var e in embers)
                if (e != null && e.img != null) e.img.enabled = fieldNow > 0.2f;
    }

    // ---------------------------------------------------------------- embers

    void Respawn(Ember e, float startLife)
    {
        float w = 1700f, h = 1000f;
        e.pos = new Vector2(UnityEngine.Random.Range(-w * 0.5f, w * 0.5f), UnityEngine.Random.Range(-h * 0.6f, h * 0.2f));
        e.vel = new Vector2(UnityEngine.Random.Range(-14f, 14f), UnityEngine.Random.Range(16f, 46f));
        e.life = UnityEngine.Random.Range(5f, 11f);
        e.age = startLife * e.life;
        e.size = UnityEngine.Random.Range(3f, 9f);
        e.flicker = UnityEngine.Random.Range(1.5f, 4.5f);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        t += dt;

        if (!Mathf.Approximately(fieldNow, fieldTarget))
        {
            fieldNow = Mathf.MoveTowards(fieldNow, fieldTarget, dt * 2.6f);
            ApplyBackdrop();
        }

        if (embers == null || fieldNow <= 0.2f) return;
        foreach (var e in embers)
        {
            if (e == null || e.img == null) continue;
            e.age += dt;
            if (e.age >= e.life) Respawn(e, 0f);
            e.pos += e.vel * dt;
            e.vel.x += Mathf.Sin((t + e.flicker) * 1.7f) * dt * 9f;
            float k = e.age / e.life;
            // fade in over the first fifth, out over the last third
            float a = Mathf.Min(k / 0.2f, Mathf.Clamp01((1f - k) / 0.33f));
            float glow = 0.55f + 0.45f * Mathf.Sin((t * e.flicker) * 2.3f);
            e.rt.anchoredPosition = e.pos;
            e.rt.sizeDelta = Vector2.one * e.size;
            e.img.color = MenuArt.Fade(Color.Lerp(MenuArt.Blood, MenuArt.BronzeLit, glow), a * 0.55f * fieldNow);
        }
    }

    // ---------------------------------------------------------------- wipe

    void SetWipe(float cover)
    {
        // cover 0 = panels fully off screen, 1 = screen covered
        float half = 0.5f;
        wipeLeft.anchorMin = new Vector2(0f, 0f);
        wipeLeft.anchorMax = new Vector2(half, 1f);
        wipeLeft.offsetMin = new Vector2(-2000f * (1f - cover), 0f);
        wipeLeft.offsetMax = new Vector2(-2000f * (1f - cover), 0f);

        wipeRight.anchorMin = new Vector2(half, 0f);
        wipeRight.anchorMax = new Vector2(1f, 1f);
        wipeRight.offsetMin = new Vector2(2000f * (1f - cover), 0f);
        wipeRight.offsetMax = new Vector2(2000f * (1f - cover), 0f);

        bool on = cover > 0.001f;
        wipeLeft.gameObject.SetActive(on);
        wipeRight.gameObject.SetActive(on);
    }

    /// <summary>
    /// Covers the screen with two converging panels, runs <paramref name="atBlack"/> while nothing
    /// can be seen, then opens them again. Returns immediately; the work happens in a coroutine.
    /// </summary>
    public Coroutine Wipe(Action atBlack, float closeTime = 0.24f, float hold = 0.06f, float openTime = 0.30f)
    {
        return StartCoroutine(WipeRoutine(atBlack, closeTime, hold, openTime));
    }

    IEnumerator WipeRoutine(Action atBlack, float closeTime, float hold, float openTime)
    {
        Wiping = true;
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / Mathf.Max(0.01f, closeTime))
        {
            SetWipe(MenuArt.EaseOut(k));
            yield return null;
        }
        SetWipe(1f);

        Exception failure = null;
        try { atBlack?.Invoke(); }
        catch (Exception e) { failure = e; }      // never strand the screen behind a black wipe

        if (hold > 0f) yield return new WaitForSecondsRealtime(hold);

        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / Mathf.Max(0.01f, openTime))
        {
            SetWipe(1f - MenuArt.EaseInOut(k));
            yield return null;
        }
        SetWipe(0f);
        Wiping = false;

        if (failure != null) Debug.LogException(failure);
    }

    // ---------------------------------------------------------------- announcements

    /// <summary>
    /// Big tracked caps that punch in and fade - "FIGHT", "K.O.", the round call. Cosmetic only;
    /// callers do not wait for it.
    /// </summary>

    /// <summary>Caps and wide tracking, line by line, the way every announcement is set.</summary>
    static string Shape(string text)
    {
        string[] lines = text.ToUpperInvariant().Split('\n');
        for (int i = 0; i < lines.Length; i++) lines[i] = MenuArt.Track(lines[i], 2);
        return string.Join("\n", lines);
    }

    public void Announce(string text, Color color, float seconds = 1.1f, string sub = null, int size = 150)
    {
        if (announce == null) return;
        // A handle, not StopCoroutine(string): this was never started by name, so stopping it by
        // name does nothing and two overlapping announcements fight over the same alpha.
        if (announceRoutine != null) StopCoroutine(announceRoutine);
        announce.SetSize(size);
        announce.Set(Shape(text));
        announce.SetFace(color);
        announce.FitWidth(1500f);
        announceSub.text = sub ?? "";
        announceRoutine = StartCoroutine(AnnounceRoutine(seconds));
    }


    /// <summary>
    /// One beat of the count-in: the word lands large and settles, and stays up until the next
    /// beat replaces it. Unlike <see cref="Announce"/> it does not fade itself out, so READY -> 3
    /// -> 2 -> 1 -> FIGHT reads as one continuous count rather than five separate flashes.
    /// </summary>
    public void Beat(string text, Color color, int size, string sub = null)
    {
        if (announce == null) return;
        if (announceRoutine != null) StopCoroutine(announceRoutine);
        announce.SetSize(size);
        announce.Set(Shape(text));
        announce.SetFace(color);
        announce.FitWidth(1500f);
        announceSub.text = sub ?? "";
        announceRoutine = StartCoroutine(BeatRoutine());
    }

    IEnumerator BeatRoutine()
    {
        const float inT = 0.14f;
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / inT)
        {
            float e = MenuArt.EaseOut(k);
            announceGroup.alpha = Mathf.Min(1f, e * 1.6f);
            announceRect.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, e);
            yield return null;
        }
        announceGroup.alpha = 1f;
        announceRect.localScale = Vector3.one;
        announceRoutine = null;
    }

    /// <summary>Clears whatever beat is on screen.</summary>
    public void ClearBeat()
    {
        if (announce == null) return;
        if (announceRoutine != null) { StopCoroutine(announceRoutine); announceRoutine = null; }
        announceGroup.alpha = 0f;
        announceRect.localScale = Vector3.one;
        announceSub.text = "";
    }

    IEnumerator AnnounceRoutine(float seconds)
    {
        float inT = 0.16f, outT = 0.34f;
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / inT)
        {
            float e = MenuArt.EaseOut(k);
            announceGroup.alpha = e;
            announceRect.localScale = Vector3.one * Mathf.Lerp(1.45f, 1f, e);
            yield return null;
        }
        announceGroup.alpha = 1f;
        announceRect.localScale = Vector3.one;
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, seconds - inT - outT));
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / outT)
        {
            announceGroup.alpha = 1f - k;
            announceRect.localScale = Vector3.one * Mathf.Lerp(1f, 1.12f, k);
            yield return null;
        }
        announceGroup.alpha = 0f;
        announceRect.localScale = Vector3.one;
    }
}
