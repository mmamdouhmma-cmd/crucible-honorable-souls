using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>
/// Front end and match flow, built from code (no prefabs): main menu (Local Play, Controls, Settings, Help,
/// Quit), pause menu (Escape / Options), rebindable controls for both players (keyboard and PS4/PS5 pad),
/// settings (volume, game speed, blood, which player the controller drives), a help screen and the
/// KO / rematch screen. Navigate with arrows or WASD, the d-pad or left stick; confirm with Enter / Space /
/// Cross; back with Escape / Backspace / Circle. The mouse works too. Sits on the GameMangers object.
/// </summary>
public class GameMenu : MonoBehaviour
{
    enum Screen { None, Main, Pause, Settings, Controls, Help, MatchOver }

    [Serializable]
    class SettingsData
    {
        public float volume = 1f;
        public float gameSpeed = 0.85f;
        public bool blood = true;
        public int padPlayer = 2;
    }

    class Item
    {
        public string label;
        public Func<string> value;      // optional, drawn on the right
        public Func<string> value2;     // optional second column (controller binding)
        public Action confirm;
        public Action<int> adjust;      // left (-1) / right (+1)
        public bool header;             // not selectable
        public Text text;
        public Image back;
    }

    const string SettingsKey = "crucible.settings.v1";
    const int VisibleRows = 16;

    static readonly Color PanelColor = new Color(0.07f, 0.06f, 0.06f, 0.97f);
    static readonly Color TextColor = new Color(0.88f, 0.84f, 0.76f);
    static readonly Color DimColor = new Color(0.55f, 0.52f, 0.46f);
    static readonly Color GoldColor = new Color(0.93f, 0.74f, 0.32f);
    static readonly Color SelectColor = new Color(0.55f, 0.12f, 0.12f, 0.85f);

    SettingsData settings = new SettingsData();
    Screen screen = Screen.None;
    Screen previous = Screen.Main;
    bool inMatch;
    float savedTimeScale = 1f;

    Fighter[] fighters = new Fighter[0];
    Vector3[] spawnPos;
    Quaternion[] spawnRot;

    Canvas canvas;
    RectTransform panel;
    Text title, subtitle, footer;
    readonly List<Item> items = new List<Item>();
    readonly List<GameObject> rows = new List<GameObject>();
    int selected;
    int scroll;
    Font font;

    // rebinding
    bool capturing;
    int captureAction = -1, capturePlayer;
    Text captureText;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
        Array.Sort(fighters, (a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
        spawnPos = new Vector3[fighters.Length];
        spawnRot = new Quaternion[fighters.Length];
        for (int i = 0; i < fighters.Length; i++) { spawnPos[i] = fighters[i].transform.position; spawnRot[i] = fighters[i].transform.rotation; }
        LoadSettings();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildCanvas();
    }

    void Start()
    {
        ApplySettings();
        FreezeFighters(true);
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = false;
        Show(Screen.Main);
    }

    void Update()
    {
        var im = InputManager.Instance;
        if (im == null) return;

        if (screen == Screen.None)
        {
            if (inMatch && im.PausePressed()) { Pause(); return; }
            CheckKO();
            return;
        }

        if (capturing) { CaptureRebind(); return; }

        Vector2 nav = im.MenuNavigate();
        if (nav.y > 0f) MoveSelection(-1);
        else if (nav.y < 0f) MoveSelection(1);
        if (nav.x != 0f) Adjust(nav.x > 0f ? 1 : -1);
        if (im.MenuConfirm()) Confirm();
        else if (im.MenuBack()) Back();
    }

    // ------------------------------------------------------------------ match flow

    public void StartMatch()
    {
        for (int i = 0; i < fighters.Length; i++) fighters[i].ResetForMatch(spawnPos[i], spawnRot[i]);
        inMatch = true;
        FreezeFighters(false);
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = true;
        Time.timeScale = GameSpeed();
        Show(Screen.None);
    }

    public void Pause()
    {
        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = false;
        Show(Screen.Pause);
    }

    public void Resume()
    {
        Time.timeScale = savedTimeScale > 0f ? savedTimeScale : GameSpeed();
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = true;
        Show(Screen.None);
    }

    void EndMatchToMenu()
    {
        inMatch = false;
        Time.timeScale = GameSpeed();
        for (int i = 0; i < fighters.Length; i++) fighters[i].ResetForMatch(spawnPos[i], spawnRot[i]);
        FreezeFighters(true);
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = false;
        Show(Screen.Main);
    }

    void CheckKO()
    {
        if (!inMatch) return;
        foreach (var f in fighters)
        {
            if (f.Stats != null && !f.Stats.IsAlive())
            {
                inMatch = false;
                FreezeFighters(true);
                if (InputManager.Instance != null) InputManager.Instance.InputEnabled = false;
                StartCoroutine(ShowMatchOver(f));
                return;
            }
        }
    }

    IEnumerator ShowMatchOver(Fighter loser)
    {
        yield return new WaitForSecondsRealtime(1.6f);
        Fighter winner = null;
        foreach (var f in fighters) if (f != loser) winner = f;
        winnerName = winner != null ? (winner.fighterData != null ? winner.fighterData.FighterName : winner.name) : "Nobody";
        winnerNumber = winner != null ? winner.PlayerNumber : 0;
        Show(Screen.MatchOver);
    }
    string winnerName = "";
    int winnerNumber;

    void FreezeFighters(bool freeze)
    {
        foreach (var f in fighters)
        {
            f.enabled = !freeze;
            var an = f.GetComponent<Animator>();
            if (freeze && an != null && an.runtimeAnimatorController != null) an.SetFloat("MoveSpeed", 0f);
        }
    }

    // ------------------------------------------------------------------ settings

    void LoadSettings()
    {
        string json = PlayerPrefs.GetString(SettingsKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { settings = JsonUtility.FromJson<SettingsData>(json) ?? new SettingsData(); }
            catch { settings = new SettingsData(); }
        }
    }

    void SaveSettings()
    {
        PlayerPrefs.SetString(SettingsKey, JsonUtility.ToJson(settings));
        PlayerPrefs.Save();
    }

    float GameSpeed() => Mathf.Clamp(settings.gameSpeed, 0.5f, 1f);

    void ApplySettings()
    {
        AudioListener.volume = Mathf.Clamp01(settings.volume);
        var dts = GetComponent<DebugTimeScale>();
        if (dts != null) { dts.timeScale = GameSpeed(); dts.enabled = true; }
        if (screen == Screen.None || !inMatch) Time.timeScale = GameSpeed();
        else savedTimeScale = GameSpeed();
        foreach (var cs in FindObjectsByType<CombatSystem>(FindObjectsSortMode.None))
        {
            var fi = typeof(CombatSystem).GetField("bloodOnHit", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null) fi.SetValue(cs, settings.blood);
        }
        if (InputManager.Instance != null)
        {
            InputManager.Instance.Bindings.firstPadPlayer = settings.padPlayer == 1 ? 1 : 2;
        }
    }

    // ------------------------------------------------------------------ screens

    void Show(Screen s)
    {
        if (screen != Screen.None && s != screen) previous = screen;
        screen = s;
        capturing = false;
        canvas.gameObject.SetActive(s != Screen.None);
        if (s == Screen.None) return;
        items.Clear();
        selected = 0; scroll = 0;
        switch (s)
        {
            case Screen.Main:
                title.text = "CRUCIBLE"; subtitle.text = "Honorable Souls";
                items.Add(new Item { label = "Local Play", confirm = StartMatch });
                items.Add(new Item { label = "Controls", confirm = () => Show(Screen.Controls) });
                items.Add(new Item { label = "Settings", confirm = () => Show(Screen.Settings) });
                items.Add(new Item { label = "Help", confirm = () => Show(Screen.Help) });
                items.Add(new Item { label = "Quit", confirm = Quit });
                footer.text = NavHint();
                break;
            case Screen.Pause:
                title.text = "PAUSED"; subtitle.text = "";
                items.Add(new Item { label = "Resume", confirm = Resume });
                items.Add(new Item { label = "Help", confirm = () => Show(Screen.Help) });
                items.Add(new Item { label = "Controls", confirm = () => Show(Screen.Controls) });
                items.Add(new Item { label = "Settings", confirm = () => Show(Screen.Settings) });
                items.Add(new Item { label = "Main Menu", confirm = EndMatchToMenu });
                footer.text = NavHint();
                break;
            case Screen.MatchOver:
                title.text = winnerNumber > 0 ? $"PLAYER {winnerNumber} WINS" : "DRAW"; subtitle.text = winnerName;
                items.Add(new Item { label = "Rematch", confirm = StartMatch });
                items.Add(new Item { label = "Main Menu", confirm = EndMatchToMenu });
                footer.text = NavHint();
                break;
            case Screen.Settings:
                title.text = "SETTINGS"; subtitle.text = "";
                items.Add(new Item { label = "Master volume", value = () => Mathf.RoundToInt(settings.volume * 100) + "%",
                    adjust = d => { settings.volume = Mathf.Clamp01(settings.volume + d * 0.1f); ApplySettings(); SaveSettings(); } });
                items.Add(new Item { label = "Game speed", value = () => Mathf.RoundToInt(settings.gameSpeed * 100) + "%",
                    adjust = d => { settings.gameSpeed = Mathf.Clamp(settings.gameSpeed + d * 0.05f, 0.5f, 1f); ApplySettings(); SaveSettings(); } });
                items.Add(new Item { label = "Blood", value = () => settings.blood ? "On" : "Off",
                    adjust = d => { settings.blood = !settings.blood; ApplySettings(); SaveSettings(); },
                    confirm = () => { settings.blood = !settings.blood; ApplySettings(); SaveSettings(); Refresh(); } });
                items.Add(new Item { label = "Controller drives", value = () => settings.padPlayer == 1 ? "Player 1" : "Player 2",
                    adjust = d => { settings.padPlayer = settings.padPlayer == 1 ? 2 : 1; ApplySettings(); SaveSettings(); },
                    confirm = () => { settings.padPlayer = settings.padPlayer == 1 ? 2 : 1; ApplySettings(); SaveSettings(); Refresh(); } });
                items.Add(new Item { label = "Back", confirm = Back });
                footer.text = "Left / Right to change    " + NavHint();
                break;
            case Screen.Controls:
                title.text = "CONTROLS"; subtitle.text = "Select an action, then press the key or pad button to use for it";
                for (int p = 1; p <= 2; p++)
                {
                    int player = p;
                    items.Add(new Item { label = $"PLAYER {p}", header = true, value = () => "keyboard", value2 = () => "controller" });
                    foreach (GameAction a in Enum.GetValues(typeof(GameAction)))
                    {
                        GameAction act = a;
                        items.Add(new Item
                        {
                            label = "   " + ControlBindings.ActionLabel(act),
                            value = () => ControlBindings.KeyLabel(InputManager.Instance.Bindings.Player(player).Key(act)),
                            value2 = () => ControlBindings.PadLabel(InputManager.Instance.Bindings.Player(player).Pad(act)),
                            confirm = () => BeginCapture(player, act),
                        });
                    }
                }
                items.Add(new Item { label = "Reset all to defaults", confirm = () =>
                {
                    ControlBindings.ResetSaved();
                    InputManager.Instance.ReloadBindings();
                    ApplySettings();
                    Refresh();
                } });
                items.Add(new Item { label = "Back", confirm = Back });
                footer.text = "Dash: hold the dash key + a direction (P1 also 7 8 9 / 4 6 / 1 2 3), or flick the right stick    " + NavHint();
                break;
            case Screen.Help:
                title.text = "HOW TO FIGHT"; subtitle.text = "";
                foreach (string line in HelpLines()) items.Add(new Item { label = line, header = true });
                items.Add(new Item { label = "Back", confirm = Back });
                footer.text = NavHint();
                break;
        }
        if (items.Count > 0 && items[selected].header) MoveSelection(1);
        Refresh();
    }

    static string NavHint() => "Arrows / D-pad: navigate    Enter / Cross: select    Esc / Circle: back";

    string BindingText(int player, GameAction a)
    {
        var b = InputManager.Instance.Bindings.Player(player);
        return $"{ControlBindings.KeyLabel(b.Key(a)),-10}   {ControlBindings.PadLabel(b.Pad(a))}";
    }

    IEnumerable<string> HelpLines()
    {
        yield return "Walk toward or away from your opponent. Light, Medium and Heavy strikes each have a wind-up,";
        yield return "an active window and a recovery. Heavier strikes hit harder and take longer. Every strike";
        yield return "steps forward a little to reach its target.";
        yield return "";
        yield return "BLOCK   Hold Block to absorb a hit for stamina. Holding drains stamina; an empty bar breaks the guard.";
        yield return "DASH    Dash key + direction, or flick the right stick. A dash timed against a strike dodges it.";
        yield return "        A side dash circles your opponent so you stay in range. After a clean dodge you have";
        yield return "        half a second to counter for +35% damage, and the attacker who missed is stuck recovering.";
        yield return "CLASH   Two strikes of equal weight meeting cancel each other. The heavier strike wins otherwise.";
        yield return "KO      Empty your opponent's health bar.";
        yield return "";
        var b = InputManager.Instance != null ? InputManager.Instance.Bindings : ControlBindings.Defaults();
        yield return $"P1 keyboard:  move {ControlBindings.KeyLabel(b.p1.Key(GameAction.MoveLeft))}/{ControlBindings.KeyLabel(b.p1.Key(GameAction.MoveRight))}   " +
                     $"light {ControlBindings.KeyLabel(b.p1.Key(GameAction.Light))}  medium {ControlBindings.KeyLabel(b.p1.Key(GameAction.Medium))}  heavy {ControlBindings.KeyLabel(b.p1.Key(GameAction.Heavy))}   " +
                     $"block {ControlBindings.KeyLabel(b.p1.Key(GameAction.Block))}   dash {ControlBindings.KeyLabel(b.p1.Key(GameAction.Dash))}";
        yield return $"P2 keyboard:  move {ControlBindings.KeyLabel(b.p2.Key(GameAction.MoveLeft))}/{ControlBindings.KeyLabel(b.p2.Key(GameAction.MoveRight))}   " +
                     $"light {ControlBindings.KeyLabel(b.p2.Key(GameAction.Light))}  medium {ControlBindings.KeyLabel(b.p2.Key(GameAction.Medium))}  heavy {ControlBindings.KeyLabel(b.p2.Key(GameAction.Heavy))}   " +
                     $"block {ControlBindings.KeyLabel(b.p2.Key(GameAction.Block))}   dash {ControlBindings.KeyLabel(b.p2.Key(GameAction.Dash))}";
        var pp = b.Player(b.firstPadPlayer);
        yield return $"Controller (Player {b.firstPadPlayer}):  move stick/d-pad   light {ControlBindings.PadLabel(pp.Pad(GameAction.Light))}  medium {ControlBindings.PadLabel(pp.Pad(GameAction.Medium))}  " +
                     $"heavy {ControlBindings.PadLabel(pp.Pad(GameAction.Heavy))}   block {ControlBindings.PadLabel(pp.Pad(GameAction.Block))}   dash: flick right stick   pause: Options";
        yield return "";
    }

    // ------------------------------------------------------------------ navigation

    void MoveSelection(int dir)
    {
        if (items.Count == 0) return;
        int n = items.Count;
        int i = selected;
        for (int k = 0; k < n; k++)
        {
            i = (i + dir + n) % n;
            if (!items[i].header) { selected = i; break; }
        }
        if (selected < scroll) scroll = selected;
        if (selected >= scroll + VisibleRows) scroll = selected - VisibleRows + 1;
        // keep a header visible above the first selectable item
        if (selected == FirstSelectable() ) scroll = 0;
        Refresh();
    }

    int FirstSelectable()
    {
        for (int i = 0; i < items.Count; i++) if (!items[i].header) return i;
        return 0;
    }

    void Adjust(int dir)
    {
        if (selected < 0 || selected >= items.Count) return;
        items[selected].adjust?.Invoke(dir);
        Refresh();
    }

    void Confirm()
    {
        if (selected < 0 || selected >= items.Count) return;
        items[selected].confirm?.Invoke();
    }

    void Back()
    {
        switch (screen)
        {
            case Screen.Main: break;
            case Screen.Pause: Resume(); break;
            case Screen.MatchOver: break;
            default:
                Show(inMatch ? Screen.Pause : Screen.Main);
                break;
        }
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ rebinding

    void BeginCapture(int player, GameAction a)
    {
        capturing = true;
        capturePlayer = player;
        captureAction = (int)a;
        captureText.text = $"Press a key or controller button for  PLAYER {player}: {ControlBindings.ActionLabel(a)}     (Esc cancels)";
        captureText.transform.parent.gameObject.SetActive(true);
    }

    void CaptureRebind()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { EndCapture(); return; }
        var b = InputManager.Instance.Bindings;
        foreach (KeyCode k in ControlBindings.BindableKeys)
        {
            if (Input.GetKeyDown(k))
            {
                b.Player(capturePlayer).keys[captureAction] = (int)k;
                b.Save();
                EndCapture();
                return;
            }
        }
        foreach (var g in Gamepad.all)
        {
            foreach (GamepadButton btn in ControlBindings.BindablePadButtons)
            {
                if (g[btn].wasPressedThisFrame)
                {
                    b.Player(capturePlayer).pad[captureAction] = (int)btn;
                    b.Save();
                    EndCapture();
                    return;
                }
            }
        }
    }

    void EndCapture()
    {
        capturing = false;
        captureText.transform.parent.gameObject.SetActive(false);
        Refresh();
    }

    // ------------------------------------------------------------------ drawing

    void BuildCanvas()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(transform, false);
        }
        var go = new GameObject("Menu Canvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        var dim = MakeImage(go.transform, "Dim", new Color(0f, 0f, 0f, 0.55f));
        Stretch(dim.rectTransform);

        var panelImg = MakeImage(go.transform, "Panel", PanelColor);
        panel = panelImg.rectTransform;
        panel.anchorMin = new Vector2(0.5f, 0.5f); panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(1500, 960);
        panel.anchoredPosition = Vector2.zero;

        var frame = MakeImage(panel, "Frame", GoldColor);
        Stretch(frame.rectTransform); frame.rectTransform.offsetMin = new Vector2(-3, -3); frame.rectTransform.offsetMax = new Vector2(3, 3);
        frame.transform.SetAsFirstSibling();
        var inner = MakeImage(panel, "Inner", PanelColor);
        Stretch(inner.rectTransform);
        inner.transform.SetSiblingIndex(1);

        title = MakeText(panel, "Title", "", 64, TextAnchor.MiddleCenter, GoldColor, FontStyle.Bold);
        Place(title.rectTransform, 0f, 1f, 1f, 1f, new Vector2(40, -110), new Vector2(-40, -30));
        subtitle = MakeText(panel, "Subtitle", "", 24, TextAnchor.MiddleCenter, DimColor, FontStyle.Italic);
        Place(subtitle.rectTransform, 0f, 1f, 1f, 1f, new Vector2(40, -150), new Vector2(-40, -112));
        footer = MakeText(panel, "Footer", "", 20, TextAnchor.MiddleCenter, DimColor, FontStyle.Normal);
        Place(footer.rectTransform, 0f, 0f, 1f, 0f, new Vector2(40, 18), new Vector2(-40, 58));

        for (int i = 0; i < VisibleRows; i++)
        {
            var row = new GameObject("Row" + i, typeof(RectTransform));
            row.transform.SetParent(panel, false);
            var rt = row.GetComponent<RectTransform>();
            float top = -165f - i * 46f;
            Place(rt, 0f, 1f, 1f, 1f, new Vector2(60, top - 42), new Vector2(-60, top));
            var back = row.AddComponent<Image>();
            back.color = new Color(0, 0, 0, 0);
            var btn = row.AddComponent<Button>();
            int rowIndex = i;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { int idx = scroll + rowIndex; if (idx < items.Count && !items[idx].header) { selected = idx; Refresh(); Confirm(); } });
            var label = MakeText(row.transform, "Label", "", 28, TextAnchor.MiddleLeft, TextColor, FontStyle.Normal);
            Place(label.rectTransform, 0f, 0f, 0.6f, 1f, new Vector2(24, 0), new Vector2(0, 0));
            var value = MakeText(row.transform, "Value", "", 26, TextAnchor.MiddleLeft, GoldColor, FontStyle.Normal);
            Place(value.rectTransform, 0.55f, 0f, 0.78f, 1f, new Vector2(0, 0), new Vector2(-8, 0));
            var value2 = MakeText(row.transform, "Value2", "", 26, TextAnchor.MiddleLeft, GoldColor, FontStyle.Normal);
            Place(value2.rectTransform, 0.78f, 0f, 1f, 1f, new Vector2(0, 0), new Vector2(-24, 0));
            rows.Add(row);
        }

        var capBack = MakeImage(go.transform, "Capture", new Color(0.02f, 0.02f, 0.02f, 0.96f));
        Stretch(capBack.rectTransform);
        captureText = MakeText(capBack.transform, "CaptureText", "", 34, TextAnchor.MiddleCenter, GoldColor, FontStyle.Bold);
        Stretch(captureText.rectTransform);
        capBack.gameObject.SetActive(false);

        canvas.gameObject.SetActive(false);
    }

    void Refresh()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            int idx = scroll + i;
            var row = rows[i];
            bool has = idx < items.Count;
            row.SetActive(has);
            if (!has) continue;
            var it = items[idx];
            var label = row.transform.Find("Label").GetComponent<Text>();
            var value = row.transform.Find("Value").GetComponent<Text>();
            var value2 = row.transform.Find("Value2").GetComponent<Text>();
            value2.text = it.value2 != null ? it.value2() : "";
            var back = row.GetComponent<Image>();
            bool sel = idx == selected && !it.header;
            label.text = (sel ? "> " : "   ") + it.label;
            label.color = it.header ? (string.IsNullOrEmpty(it.label) ? TextColor : (it.label.StartsWith("PLAYER") ? GoldColor : TextColor)) : (sel ? Color.white : TextColor);
            label.fontStyle = it.header && it.label.StartsWith("PLAYER") ? FontStyle.Bold : FontStyle.Normal;
            label.fontSize = it.header && !it.label.StartsWith("PLAYER") ? 22 : 28;
            value.text = it.value != null ? it.value() : "";
            if (it.adjust != null && sel) value.text = "<  " + value.text + "  >";
            back.color = sel ? SelectColor : new Color(0, 0, 0, 0);
        }
    }

    // helpers
    Text MakeText(Transform parent, string name, string text, int size, TextAnchor anchor, Color color, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.alignment = anchor; t.color = color; t.fontStyle = style; t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, float ax, float ay, float bx, float by, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(bx, by); rt.offsetMin = offMin; rt.offsetMax = offMax;
    }
}
