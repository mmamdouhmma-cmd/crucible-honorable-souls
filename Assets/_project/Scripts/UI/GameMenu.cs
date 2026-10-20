using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    enum Screen { None, Main, Pause, Settings, Controls, Help, Moves, MatchOver, AiSelect, Online, OnlineHost, OnlineJoin }

    [Serializable]
    class SettingsData
    {
        public float volume = 1f;
        public float gameSpeed = 0.85f;
        public bool blood = true;
        public int padPlayer = 2;
        public int touchButtons = 0;   // 0 auto (touch devices only), 1 always on, 2 off
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
    bool vsAi;
    NetSession.Role netRole = NetSession.Role.None;
    float nextOnlineRefresh;
    NetSession Net { get { if (NetSession.Instance == null) gameObject.AddComponent<NetSession>(); return NetSession.Instance; } }
    AiDifficulty aiDifficulty = AiDifficulty.Moderate;
    float savedTimeScale = 1f;

    void SetAi(bool on)
    {
        var ai = AiController.Instance != null ? AiController.Instance : GetComponent<AiController>();
        if (ai == null && on) ai = gameObject.AddComponent<AiController>();
        if (ai == null) return;
        if (on) ai.Activate(aiDifficulty, 2); else ai.Deactivate();
    }

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
        if (Net != null)
        {
            Net.OnStartReceived += () =>
            {
                // guest: the host started or restarted the fight
                netRole = NetSession.Role.Guest;
                vsAi = false;
                for (int i = 0; i < fighters.Length; i++) fighters[i].ResetForMatch(spawnPos[i], spawnRot[i]);
                inMatch = true;
                FreezeFighters(false);
                Net.GuestEnterMatch();
                if (TouchControls.Instance != null) TouchControls.Instance.ShowForMatch(1);
                if (InputManager.Instance != null) InputManager.Instance.InputEnabled = true;
                Time.timeScale = GameSpeed();
                Show(Screen.None);
            };
            Net.OnPauseReceived += on =>
            {
                if (netRole == NetSession.Role.Host) { if (on) Pause(); else Resume(); }
                else { if (on) PauseLocal(); else ResumeLocal(); }
            };
            Net.OnRematchRequested += () => { if (netRole == NetSession.Role.Host && !inMatch) StartMatch(); };
            Net.OnPeerLeft += () => { if (netRole != NetSession.Role.None) { Net.Leave(); netRole = NetSession.Role.None; EndMatchToMenu(); subtitle.text = "The other player left"; } };
        }
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

        if ((screen == Screen.OnlineHost || screen == Screen.OnlineJoin) && Time.unscaledTime >= nextOnlineRefresh)
        {
            nextOnlineRefresh = Time.unscaledTime + 1f;
            int keep = selected;
            Show(screen);
            selected = Mathf.Min(keep, Mathf.Max(0, items.Count - 1));
            Refresh();
        }

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
        if (netRole == NetSession.Role.Guest) { Net?.SendRematchRequest(); return; }
        for (int i = 0; i < fighters.Length; i++) fighters[i].ResetForMatch(spawnPos[i], spawnRot[i]);
        SetAi(vsAi);
        if (netRole == NetSession.Role.Host) Net?.HostStartMatch();
        if (TouchControls.Instance != null) TouchControls.Instance.ShowForMatch(vsAi || netRole != NetSession.Role.None ? 1 : 2);
        inMatch = true;
        FreezeFighters(false);
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = true;
        Time.timeScale = GameSpeed();
        Show(Screen.None);
    }

    public void Pause()
    {
        if (netRole == NetSession.Role.Guest) { Net?.SendPause(true); return; }
        if (netRole == NetSession.Role.Host) Net?.SendPause(true);
        PauseLocal();
    }

    public void Resume()
    {
        if (netRole == NetSession.Role.Guest) { Net?.SendPause(false); return; }
        if (netRole == NetSession.Role.Host) Net?.SendPause(false);
        ResumeLocal();
    }

    void PauseLocal()
    {
        if (TouchControls.Instance != null) TouchControls.Instance.Hide();
        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = false;
        Show(Screen.Pause);
    }

    void ResumeLocal()
    {
        if (TouchControls.Instance != null) TouchControls.Instance.ShowForMatch(netRole != NetSession.Role.None || vsAi ? 1 : 2);
        Time.timeScale = savedTimeScale > 0f ? savedTimeScale : GameSpeed();
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = true;
        Show(Screen.None);
    }

    void EndMatchToMenu()
    {
        inMatch = false;
        SetAi(false);
        if (netRole != NetSession.Role.None) { Net?.GuestLeaveMatch(); Net?.Leave(); netRole = NetSession.Role.None; }
        if (TouchControls.Instance != null) TouchControls.Instance.Hide();
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
                if (TouchControls.Instance != null) TouchControls.Instance.Hide();
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
        TouchControls.SetMode(settings.touchButtons);
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
                items.Add(new Item { label = "Local Play", confirm = () => { vsAi = false; StartMatch(); } });
                items.Add(new Item { label = "Against AI", confirm = () => Show(Screen.AiSelect) });
                items.Add(new Item { label = "Same WiFi", confirm = () => Show(Screen.Online) });
                items.Add(new Item { label = "Controls", confirm = () => Show(Screen.Controls) });
                items.Add(new Item { label = "Settings", confirm = () => Show(Screen.Settings) });
                items.Add(new Item { label = "Moves", confirm = () => Show(Screen.Moves) });
                items.Add(new Item { label = "Help", confirm = () => Show(Screen.Help) });
                items.Add(new Item { label = "Quit", confirm = Quit });
                footer.text = NavHint();
                break;
            case Screen.AiSelect:
                title.text = "AGAINST AI"; subtitle.text = "You play Player 1 (Bjorn); the AI plays Player 2 (Miyamoto)";
                items.Add(new Item { label = "Chill", value = () => "slow, sloppy, forgiving", confirm = () => { vsAi = true; aiDifficulty = AiDifficulty.Chill; StartMatch(); } });
                items.Add(new Item { label = "Moderate", value = () => "reads wind-ups, blocks and dodges half the time", confirm = () => { vsAi = true; aiDifficulty = AiDifficulty.Moderate; StartMatch(); } });
                items.Add(new Item { label = "Insane", value = () => "reacts in frames, punishes every whiff, counters", confirm = () => { vsAi = true; aiDifficulty = AiDifficulty.Insane; StartMatch(); } });
                items.Add(new Item { label = "Back", confirm = Back });
                footer.text = NavHint();
                break;
            case Screen.Online:
                title.text = "SAME WIFI"; subtitle.text = "Both phones open " + NetSession.LanHint() + " (the PC runs Server/relay.js)";
                items.Add(new Item { label = "Host a game", value = () => "you are Bjorn", confirm = () =>
                {
                    if (Net == null) return;
                    Net.Host(System.Environment.MachineName.Length > 0 ? System.Environment.MachineName : "room");
                    Show(Screen.OnlineHost);
                } });
                items.Add(new Item { label = "Join a game", value = () => "you are Miyamoto", confirm = () => { if (Net == null) return; Net.BrowseRooms(); Show(Screen.OnlineJoin); } });
                items.Add(new Item { label = "Back", confirm = Back });
                footer.text = NavHint();
                break;
            case Screen.OnlineHost:
                title.text = "HOSTING"; subtitle.text = Net != null ? Net.Status : "";
                items.Add(new Item { label = Net != null && Net.PeerConnected ? "Start the fight" : "Waiting for a player...", confirm = () => { if (Net != null && Net.PeerConnected) { vsAi = false; netRole = NetSession.Role.Host; StartMatch(); } } });
                items.Add(new Item { label = "Cancel", confirm = () => { Net?.Leave(); netRole = NetSession.Role.None; Show(Screen.Online); } });
                footer.text = NavHint();
                break;
            case Screen.OnlineJoin:
                title.text = "JOIN A GAME"; subtitle.text = Net != null ? Net.Status : "";
                if (Net != null)
                {
                    if (Net.Rooms.Count == 0) items.Add(new Item { label = "   (no open games yet; the host must press Host a game)", header = true });
                    foreach (var r in Net.Rooms) { string room = r; items.Add(new Item { label = "Join " + room, confirm = () => Net.Join(room) }); }
                }
                items.Add(new Item { label = "Back", confirm = () => { Net?.Leave(); netRole = NetSession.Role.None; Show(Screen.Online); } });
                footer.text = NavHint();
                break;
            case Screen.Pause:
                title.text = "PAUSED"; subtitle.text = vsAi ? $"vs AI ({aiDifficulty})" : (netRole != NetSession.Role.None ? "same WiFi" : "");
                items.Add(new Item { label = "Resume", confirm = Resume });
                items.Add(new Item { label = "Moves", confirm = () => Show(Screen.Moves) });
                items.Add(new Item { label = "Help", confirm = () => Show(Screen.Help) });
                items.Add(new Item { label = "Controls", confirm = () => Show(Screen.Controls) });
                items.Add(new Item { label = "Settings", confirm = () => Show(Screen.Settings) });
                items.Add(new Item { label = "Main Menu", confirm = EndMatchToMenu });
                footer.text = NavHint();
                break;
            case Screen.MatchOver:
                title.text = winnerNumber > 0 ? (vsAi ? (winnerNumber == 1 ? "YOU WIN" : "THE AI WINS") : $"PLAYER {winnerNumber} WINS") : "DRAW";
                subtitle.text = netRole == NetSession.Role.Guest ? winnerName + "   (you are Player 2)" : winnerName;
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
                items.Add(new Item { label = "On-screen buttons",
                    value = () => settings.touchButtons == 1 ? "Always" : settings.touchButtons == 2 ? "Off" : "Auto (phones)",
                    adjust = d => { settings.touchButtons = (settings.touchButtons + (d > 0 ? 1 : 2)) % 3; ApplySettings(); SaveSettings(); },
                    confirm = () => { settings.touchButtons = (settings.touchButtons + 1) % 3; ApplySettings(); SaveSettings(); Refresh(); } });
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
            case Screen.Moves:
            {
                int pages = Mathf.Max(1, MovesRoster().Count * 2);
                movesPage = ((movesPage % pages) + pages) % pages;
                title.text = "MOVES"; subtitle.text = "read from the live attack data, so this list cannot go stale";
                if (pages > 1)
                    items.Add(new Item
                    {
                        label = "Showing",
                        value = () => MovesPageName(movesPage),
                        adjust = d => { int n = Mathf.Max(1, MovesRoster().Count * 2); movesPage = (movesPage + (d > 0 ? 1 : n - 1)) % n; Refresh(); }
                    });
                foreach (string line in MoveLines(movesPage)) items.Add(new Item { label = line, header = true });
                items.Add(new Item { label = "Back", confirm = Back });
                footer.text = NavHint();
                break;
            }
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

    static string NavHint() => TouchControls.WantsTouch ? "Tap an item" : "Arrows / D-pad: navigate    Enter / Cross: select    Esc / Circle: back";

    string BindingText(int player, GameAction a)
    {
        var b = InputManager.Instance.Bindings.Player(player);
        return $"{ControlBindings.KeyLabel(b.Key(a)),-10}   {ControlBindings.PadLabel(b.Pad(a))}";
    }

    int movesPage;   // fighter * 2 + (0 = its moves, 1 = its strings)

    /// <summary>Fighters the move list can page through, in player order.</summary>
    List<(string name, CombatSystem combat, int player)> MovesRoster()
    {
        var roster = new List<(string, CombatSystem, int)>();
        foreach (var fighter in UnityEngine.Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include)
                                .OrderBy(f => f.PlayerNumber))
        {
            var combat = fighter.GetComponent<CombatSystem>();
            if (combat == null) continue;
            // Both fighters currently share one FighterData ("test Fighter"), so that name tells
            // the two apart for nobody. The identity this project really uses is the attack asset
            // prefix (Bjorn_Light_Slash -> Bjorn), which also keys the animator states and clip maps.
            string name = CharacterName(combat) ??
                          (fighter.fighterData != null ? fighter.fighterData.FighterName : fighter.name);
            roster.Add((name, combat, fighter.PlayerNumber));
        }
        return roster;
    }

    string MovesPageName(int page)
    {
        var roster = MovesRoster();
        if (roster.Count == 0) return "-";
        var entry = roster[Mathf.Clamp(page / 2, 0, roster.Count - 1)];
        return entry.name + (page % 2 == 0 ? " - moves" : " - strings");
    }

    /// <summary>
    /// One page of the move list, walked from the live <see cref="AttackData"/> every time the
    /// screen opens. Only the LONGEST form of each string is listed - a two-hit that can be
    /// extended is shown as the three-hit, since stopping early is always allowed. Mirrors the two
    /// engine rules a walk has to know about: a link marked openerOnly is offered only from the
    /// string's first move, and a string carries at most one special.
    /// </summary>
    IEnumerable<string> MoveLines(int page)
    {
        var roster = MovesRoster();
        if (roster.Count == 0) { yield return "No fighters in the scene."; yield break; }
        var entry = roster[Mathf.Clamp(page / 2, 0, roster.Count - 1)];
        var b = InputManager.Instance != null ? InputManager.Instance.Bindings : ControlBindings.Defaults();
        var keys = b.Player(entry.player);

        if (page % 2 == 0)
        {
            yield return $"{entry.name.ToUpper()}   (Player {entry.player})   damage / stamina / startup-active-recovery";
            yield return "";
            foreach (var attack in Library(entry.combat))
                yield return $"   {Press(keys, attack),-14} {attack.AttackName,-21} {attack.Damage,2} dmg   " +
                             $"{attack.StaminaCost,2} stam   {attack.StartupFrames}/{attack.ActiveFrames}/{attack.RecoveryFrames}";
            foreach (var r in Ripostes(entry.combat))
            {
                // A riposte is not on a button: a counter window re-points the strike buttons at
                // it. Its own follow-up is shown inline rather than on the strings page, which is
                // already as long as the panel can hold.
                string next = "";
                var link = r.attack.ComboLinks != null && r.attack.ComboLinks.Length > 0 ? r.attack.ComboLinks[0] : null;
                if (link != null && link.followUp != null)
                    next = "   > " + PressLabel(keys, link.input, link.withSpecialModifier) + "  " + link.followUp.AttackName;
                yield return $"   {"[" + r.label + "]",-14} {r.attack.AttackName,-21} {r.attack.Damage,2} dmg   " +
                             $"{r.attack.StaminaCost,2} stam   {r.attack.StartupFrames}/{r.attack.ActiveFrames}/{r.attack.RecoveryFrames}{next}";
            }
            yield return "";
            yield return "   A special costs about 2.5x the stamina of the strike it shares a button with. A dodge or";
            yield return "   a parry re-points the strike buttons at that fighter's riposte, and gives health back.";
            yield break;
        }

        var strings = new List<(string buttons, string names)>();
        foreach (var opener in Library(entry.combat))
            WalkMoves(opener, keys, Press(keys, opener), opener.AttackName, 0, opener.IsSpecial, strings);
        yield return $"{entry.name.ToUpper()}   (Player {entry.player})   {strings.Count} strings";
        yield return "";
        foreach (var line in strings)
            yield return $"   {line.buttons,-30} {line.names}";
        yield return "";
        yield return "   Press the next button before the move finishes; stopping early is always allowed.";
    }

    /// <summary>
    /// Collects only the strings that cannot be continued any further, so each branch is listed
    /// once at full length instead of once per prefix.
    /// </summary>
    void WalkMoves(AttackData attack, ControlBindings.PlayerBindings keys, string buttons, string names,
                   int depth, bool usedSpecial, List<(string, string)> into)
    {
        bool extended = false;
        if (attack != null && attack.ComboLinks != null && depth + 2 <= BalanceConfig.MAX_STRING_LENGTH)
        {
            foreach (var link in attack.ComboLinks)
            {
                if (link == null || link.followUp == null) continue;
                if (link.openerOnly && depth > 0) continue;
                if (usedSpecial && link.followUp.IsSpecial) continue;
                extended = true;
                WalkMoves(link.followUp, keys,
                          buttons + " > " + PressLabel(keys, link.input, link.withSpecialModifier),
                          names + " > " + link.followUp.AttackName,
                          depth + 1, usedSpecial || link.followUp.IsSpecial, into);
            }
        }
        if (!extended && depth > 0) into.Add((buttons, names));
    }

    /// <summary>
    /// The counter-window moves, which no button produces on its own. Listed with what opens them
    /// rather than with a key.
    /// </summary>
    static IEnumerable<(string label, AttackData attack)> Ripostes(CombatSystem combat)
    {
        if (combat.DashRiposte != null) yield return ("after a dodge", combat.DashRiposte);
        if (combat.ParryRiposte != null) yield return ("after a parry", combat.ParryRiposte);
    }

    static string CharacterName(CombatSystem combat)
    {
        foreach (var attack in Library(combat))
        {
            int cut = attack.name.IndexOf('_');
            if (cut > 0) return attack.name.Substring(0, cut);
        }
        return null;
    }

    static IEnumerable<AttackData> Library(CombatSystem combat)
    {
        foreach (var a in combat.Attacks ?? new AttackData[0]) if (a != null) yield return a;
        foreach (var a in combat.Specials ?? new AttackData[0]) if (a != null) yield return a;
    }

    static string Press(ControlBindings.PlayerBindings keys, AttackData attack) =>
        PressLabel(keys, attack.IsSpecial ? attack.SpecialButton : attack.AttackType, attack.IsSpecial);

    static string PressLabel(ControlBindings.PlayerBindings keys, AttackType button, bool special)
    {
        GameAction action = button == AttackType.Light ? GameAction.Light
                          : button == AttackType.Medium ? GameAction.Medium : GameAction.Heavy;
        string key = ControlBindings.KeyLabel(keys.Key(action));
        // Bracketed, because Player 2's strikes are punctuation (, . /) and the modifier is a
        // two-word key: without them a string reads as ",  >  .  >  R Ctrl+," and nobody can
        // tell where one press ends and the next begins.
        return special ? $"[{ControlBindings.KeyLabel(keys.Key(GameAction.Special))} + {key}]" : $"[{key}]";
    }

    IEnumerable<string> HelpLines()
    {
        yield return "Walk toward or away from your opponent. Light, Medium and Heavy strikes each have a wind-up,";
        yield return "an active window and a recovery. Heavier strikes hit harder and take longer. Every strike";
        yield return "steps forward a little to reach its target.";
        yield return "";
        yield return "BLOCK   Hold Block to absorb a hit for stamina. Holding drains stamina; an empty bar breaks the guard.";
        yield return "PARRY   Tap Parry just as a strike lands to deflect it: no damage, no stamina lost, and the attacker";
        yield return "        is stunned long enough to punish, with half a second to counter for +35% damage. It is its";
        yield return "        own button, never the guard: it costs a little stamina and about a second before you can";
        yield return "        try again, so read the swing rather than tapping it on every one.";
        yield return "DASH    Dash key + direction, or flick the right stick. A dash timed against a strike dodges it.";
        yield return "        A side dash circles your opponent so you stay in range. After a clean dodge you have";
        yield return "        half a second to counter for +35% damage, and the attacker who missed is stuck recovering.";
        yield return "CLASH   Two strikes of equal weight meeting cancel each other. The heavier strike wins otherwise.";
        yield return "SPECIAL Hold the Special modifier and press a strike for that character's special version of it:";
        yield return "        three per fighter, one on each button. They hit harder and reach further, cost about two";
        yield return "        and a half times the stamina, and leave a beat afterwards in which you cannot attack, so";
        yield return "        a whiffed special is punished. On a phone, tap SP once and it stays lit for your next strike.";
        yield return "KO      Empty your opponent's health bar.";
        yield return "";
        var b = InputManager.Instance != null ? InputManager.Instance.Bindings : ControlBindings.Defaults();
        yield return $"P1 keyboard:  move {ControlBindings.KeyLabel(b.p1.Key(GameAction.MoveLeft))}/{ControlBindings.KeyLabel(b.p1.Key(GameAction.MoveRight))}   " +
                     $"light {ControlBindings.KeyLabel(b.p1.Key(GameAction.Light))}  medium {ControlBindings.KeyLabel(b.p1.Key(GameAction.Medium))}  heavy {ControlBindings.KeyLabel(b.p1.Key(GameAction.Heavy))}   " +
                     $"block {ControlBindings.KeyLabel(b.p1.Key(GameAction.Block))}  parry {ControlBindings.KeyLabel(b.p1.Key(GameAction.Defense))}   " +
                     $"special {ControlBindings.KeyLabel(b.p1.Key(GameAction.Special))}+strike   dash {ControlBindings.KeyLabel(b.p1.Key(GameAction.Dash))}";
        yield return $"P2 keyboard:  move {ControlBindings.KeyLabel(b.p2.Key(GameAction.MoveLeft))}/{ControlBindings.KeyLabel(b.p2.Key(GameAction.MoveRight))}   " +
                     $"light {ControlBindings.KeyLabel(b.p2.Key(GameAction.Light))}  medium {ControlBindings.KeyLabel(b.p2.Key(GameAction.Medium))}  heavy {ControlBindings.KeyLabel(b.p2.Key(GameAction.Heavy))}   " +
                     $"block {ControlBindings.KeyLabel(b.p2.Key(GameAction.Block))}  parry {ControlBindings.KeyLabel(b.p2.Key(GameAction.Defense))}   " +
                     $"special {ControlBindings.KeyLabel(b.p2.Key(GameAction.Special))}+strike   dash {ControlBindings.KeyLabel(b.p2.Key(GameAction.Dash))}";
        var pp = b.Player(b.firstPadPlayer);
        yield return $"Controller (Player {b.firstPadPlayer}):  move stick/d-pad   light {ControlBindings.PadLabel(pp.Pad(GameAction.Light))}  medium {ControlBindings.PadLabel(pp.Pad(GameAction.Medium))}  " +
                     $"heavy {ControlBindings.PadLabel(pp.Pad(GameAction.Heavy))}   block {ControlBindings.PadLabel(pp.Pad(GameAction.Block))}  parry {ControlBindings.PadLabel(pp.Pad(GameAction.Defense))}   " +
                     $"special {ControlBindings.PadLabel(pp.Pad(GameAction.Special))}+strike   dash: flick right stick   pause: Options";
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
