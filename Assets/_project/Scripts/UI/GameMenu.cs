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
/// The whole front end and the match flow, built from code (no prefabs), dressed to match the
/// game's logo: a near-black field, bronze inscription, dried blood for the selection.
///
/// Boot      - the logo, then "press any button".
/// Main      - Battle Now (1 or 2 players, character select, arena select), Online, Practice,
///             Move List, Settings, Quit.
/// In match  - pause (Options / Escape), the KO card, rematch.
///
/// Navigate with arrows or WASD, the d-pad or the left stick; confirm with Enter / Space / Cross;
/// back with Escape / Backspace / Circle. Taps work too. Everything animates on UNSCALED time,
/// because the pause menu runs at Time.timeScale 0.
///
/// Sits on the GameMangers object.
/// </summary>
public class GameMenu : MonoBehaviour
{
    enum Screen
    {
        None, Boot, Main,
        LocalSetup, CharSelect, ArenaSelect, Difficulty, PracticeRules, Vs,
        Pause, Settings, Controls, Help, Moves, MatchOver,
        Online, OnlineHost, OnlineJoin,
        Replay,
        PickDevices,    // 2-player: who holds what (appended - keep the order)
        Records,        // the saved-fights library on the main menu (appended - keep the order)
    }

    /// <summary>Which path through the select screens we are on.</summary>
    enum Flow { None, Local, Practice, Online }

    [Serializable]
    class SettingsData
    {
        public float volume = 1f;
        public float musicVolume = 0.8f;
        public float effectsVolume = 1f;
        public float voiceVolume = 1f;
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
        public bool quiet;              // header drawn small and dim (body copy)
    }

    class Row
    {
        public GameObject go;
        public RectTransform rt;
        public Image back;
        public Image gem;
        public Text label, value, value2;
        public float slide;             // entrance animation, 0..1
    }

    const string SettingsKey = "crucible.settings.v1";
    const int RowPool = 26;

    // ---------------------------------------------------------------- state

    SettingsData settings = new SettingsData();

    Screen screen = Screen.None;
    readonly List<Screen> history = new List<Screen>();
    Flow flow = Flow.None;

    bool inMatch;
    bool vsAi;
    int playerCount = 1;

    // The device-choice screen's state: -1 = keyboard, otherwise an index into Gamepad.all.
    int p1Device = -1, p2Device = -1;
    int p1Character;                    // roster index Player 1 drives
    int p2Character = 1;                // roster index Player 2 (or the computer) drives
    bool pickingP2;                     // the select screen is on the second pick (1P flow)

    // -------- the circle select (2026-10-26, the user's design): the roster stands in a ring,
    // a camera near the centre orbits to whoever the cursor is on, and a 2-player match splits
    // the screen so each pad drives its own half at the same time.
    bool splitSelect;                   // 2P: both players pick at once on a split screen
    bool lock1, lock2;                  // per-player confirmed picks (split mode)
    float camA1, camA2;                 // each camera's CURRENT orbit angle, degrees, continuous
    float targA1, targA2;               // where each camera is headed (accumulates, so it sweeps
                                        // the way the player scrolled and loops without snapping)
    Camera selectCam2;                  // right-half camera, created on demand, menu-only
    Image splitDivider;                 // the seam between the two halves
    bool mergingSelect;                 // both locked: the halves are animating into one
    const float CircleRadius = 5.2f;    // fighters' ring around the marks' midpoint
    const float CircleCamIn = 0.9f;     // camera's own ring, just off centre
    static Vector3 CircleDir(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad));
    int arenaChoice;
    AiDifficulty aiDifficulty = AiDifficulty.Moderate;
    AiController.Rules practiceRules = AiController.Rules.Full;
    bool practiceInfiniteHealth = true, practiceInfiniteStamina = true;

    NetSession.Role netRole = NetSession.Role.None;
    float nextOnlineRefresh;
    NetSession Net { get { if (NetSession.Instance == null) gameObject.AddComponent<NetSession>(); return NetSession.Instance; } }

    float savedTimeScale = 1f;
    float inputGuard;                   // seconds of unscaled time before this screen accepts confirm/back
    float screenAge;
    bool rebuilding;                    // Show() is refreshing the screen in place, not entering it

    // fighters, in scene order; index 0 is whoever starts the scene as Player 1
    Fighter[] roster = new Fighter[0];
    Vector3[] sideMark = new Vector3[0];     // spawn marks BY SIDE (0 = P1's side)

    // The two start marks, held HERE rather than read off the fighters. They used to be wherever
    // the first two roster entries stood at scene load, so moving fighters in the scene to compare
    // sizes moved the match spawns and the character-select ring with them (2026-10-27). The
    // defaults are Player1's and Player2's committed spots; the match spreads them to
    // MATCH_START_GAP about their midpoint, and the ring is centred on that midpoint.
    [Header("Start marks (independent of where the fighters stand)")]
    [SerializeField] Vector3 p1Mark = new Vector3(-0.217f, 0.24f, -0.209f);
    [SerializeField] float p1Yaw = 90f;
    [SerializeField] Vector3 p2Mark = new Vector3(2.078f, 0.75f, -0.065f);
    [SerializeField] float p2Yaw = -90f;
    Quaternion[] sideRot = new Quaternion[0];
    float[] ownY = new float[0];             // each fighter keeps its own height; the roots differ

    // A MIRROR match (both players on one fighter, 2026-10-26): the scene holds one rig per
    // character, so Player 2 gets a clone of it in the alternate colours (MirrorSkin). It is made
    // on demand, benched (not destroyed) when the pair stops being a mirror, and replaced when a
    // mirror of a DIFFERENT fighter is picked. It is not in `roster` - the select ring counts
    // characters off that array - so anything that walks the fighters IN PLAY uses Everyone().
    Fighter twin;
    int twinOf = -1;
    Fighter[] everyone = new Fighter[0];

    bool Mirror => p1Character == p2Character;
    Fighter P1Fighter => roster[p1Character];
    Fighter P2Fighter => Mirror && twin != null ? twin : roster[p2Character];
    /// <summary>The roster plus the mirror twin, if one exists. Benched ones are in it too.</summary>
    Fighter[] Everyone() => everyone;
    void RebuildEveryone()
    {
        if (twin == null) { everyone = roster; return; }
        everyone = new Fighter[roster.Length + 1];
        Array.Copy(roster, everyone, roster.Length);
        everyone[roster.Length] = twin;
    }

    /// <summary>
    /// Player 2's copy of roster entry <paramref name="index"/>, cloned once and kept until a
    /// mirror of someone else replaces it. The clone is made from the source while the source is
    /// SWITCHED OFF, so it is born inactive and its Awake/Start run only when ApplySides wakes it
    /// as Player 2 - otherwise it would wake as a second Player 1 and grab Player 1's opponent.
    /// </summary>
    void EnsureTwin(int index)
    {
        if (twin != null && twinOf == index) return;
        // Off first: Destroy only lands at the end of the frame, and until then the old twin is
        // an active Player 2 that RefreshOpponent below would happily pick.
        if (twin != null) { twin.gameObject.SetActive(false); Destroy(twin.gameObject); }

        var src = roster[index];
        bool wasOn = src.gameObject.activeSelf;
        src.gameObject.SetActive(false);
        var go = Instantiate(src.gameObject, src.transform.parent);
        src.gameObject.SetActive(wasOn);
        go.name = src.name + "_Mirror";

        twin = go.GetComponent<Fighter>();
        twinOf = index;
        twin.SetPlayerNumber(2);
        MirrorSkin.Apply(twin);
        RebuildEveryone();
    }
    float OwnY(Fighter f)
    {
        if (f == twin) return twinOf >= 0 ? ownY[twinOf] : f.transform.position.y;
        int i = Array.IndexOf(roster, f);
        return i >= 0 ? ownY[i] : f.transform.position.y;
    }

    // ---------------------------------------------------------------- widgets

    Canvas canvas;
    RectTransform canvasRect, content;
    MenuStage stage;

    RectTransform panel;
    Text title, subtitle, footerText;
    RectTransform header, footer, divider;
    RawImage titleLogo;
    readonly List<Item> items = new List<Item>();
    readonly List<Row> rows = new List<Row>();
    int selected, scroll, visibleRows = 16;
    float rowHeight = 46f, rowFont = 28f;

    RectTransform bootView, selectView, arenaView, vsView;
    Text bootPrompt, bootTitle;
    RawImage bootLogo;
    CharacterCard[] cards = new CharacterCard[0];
    CharacterCard[] vsCards = new CharacterCard[0];
    bool staged;
    Text vsWord;
    Text arenaName, arenaBlurb, arenaCount;
    RectTransform arenaPips;

    RectTransform captureBack;
    Text captureText;
    bool capturing;
    int captureAction = -1, capturePlayer;

    /// <summary>
    /// A name plate that rides under a fighter on the select screens. There is no portrait: the
    /// fighter himself is standing in the arena behind the UI, playing his idle.
    /// </summary>
    class CharacterCard
    {
        public RectTransform rt;
        public Image slab, frame, tag;
        public Text name, tagText, stats, tagline;
        public Transform follow;        // the fighter this plate sits under
    }

    string winnerName = "";
    int winnerNumber;
    int movesPage;

    // the K.O. card's pending "are you sure" question (null = the normal card)
    string confirmAsk;
    Action confirmYes;

    // the game record screen
    bool replayPlaying;
    float replayTime, trimStart, trimEnd;
    int replayMarker;                   // which event marker "Jump to" is on
    bool exportingUi;                   // the menu is hidden while a video is being written

    ReplayRecorder Rec
    {
        get
        {
            if (ReplayRecorder.Instance == null) gameObject.AddComponent<ReplayRecorder>();
            return ReplayRecorder.Instance;
        }
    }

    // ================================================================ lifecycle

    void Awake()
    {
        var found = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
        Array.Sort(found, (a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
        roster = found;
        everyone = roster;

        int n = roster.Length;
        sideMark = new Vector3[n]; sideRot = new Quaternion[n]; ownY = new float[n];
        for (int i = 0; i < n; i++)
        {
            sideMark[i] = roster[i].transform.position;
            sideRot[i] = roster[i].transform.rotation;
            ownY[i] = roster[i].transform.position.y;
        }
        if (n > 0) { sideMark[0] = p1Mark; sideRot[0] = Quaternion.Euler(0f, p1Yaw, 0f); }
        if (n > 1) { sideMark[1] = p2Mark; sideRot[1] = Quaternion.Euler(0f, p2Yaw, 0f); }

        LoadSettings();
        BuildCanvas();
    }

    void Start()
    {
        if (Net != null)
        {
            Net.OnStartReceived += () =>
            {
                netRole = NetSession.Role.Guest;
                vsAi = false;
                PlaceFighters();
                inMatch = true;
                FreezeFighters(false);
                Rec.BeginRecording(Everyone(), Camera.main);
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
        if (ArenaDirectory.Instance != null) arenaChoice = ArenaDirectory.Instance.LoadSaved();
        FreezeFighters(true);
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = false;
        Show(Screen.Boot);
    }

    void Update()
    {
        // Nothing may stay selected in the EventSystem: with a selection it would answer Enter,
        // Space and the arrow keys itself, in parallel with this menu.
        var ev = EventSystem.current;
        if (ev != null && ev.currentSelectedGameObject != null) ev.SetSelectedGameObject(null);

        float dt = Time.unscaledDeltaTime;
        screenAge += dt;
        if (inputGuard > 0f) inputGuard -= dt;

        AnimateRows(dt);
        if (screen == Screen.Vs) TrackPlates(vsCards, -385f);   // select cards are fixed to their halves now

        var im = InputManager.Instance;
        if (im == null) return;

        if (screen == Screen.None)
        {
            if (inMatch && im.PausePressed()) { Pause(); return; }
            CheckKO();
            return;
        }

        if (stage != null && stage.Wiping) return;     // ignore input while the screen is covered
        if (capturing) { CaptureRebind(); return; }

        if (screen == Screen.Boot) { UpdateBoot(); return; }
        if (screen == Screen.Vs) return;               // runs itself, then starts the fight
        if (screen == Screen.CharSelect) { UpdateCharSelect(); return; }   // the circle runs its own input + cameras
        if (screen == Screen.Replay && UpdateReplay()) return;   // exporting: input lands nowhere

        if ((screen == Screen.OnlineHost || screen == Screen.OnlineJoin) && Time.unscaledTime >= nextOnlineRefresh)
        {
            nextOnlineRefresh = Time.unscaledTime + 1f;
            Rebuild();
        }

        Vector2 nav = im.MenuNavigate();
        if (nav.y > 0f) MoveSelection(-1);
        else if (nav.y < 0f) MoveSelection(1);
        if (nav.x != 0f) Adjust(nav.x > 0f ? 1 : -1);

        // The guard exists because the button that OPENED this screen is still down on the frame it
        // appears: pausing with Options, whose pad button is also the menu's back, used to resume
        // the fight the instant the pause menu was drawn.
        if (inputGuard > 0f) return;

        // Options / Escape toggles the pause off again. Circle deliberately does NOT: it is also
        // the parry button, and resuming the fight by brushing it was the whole complaint.
        if (screen == Screen.Pause && im.PausePressed()) { Resume(); return; }

        if (im.MenuConfirm()) Confirm();
        else if (im.MenuBack()) Back();
    }

    // ================================================================ match flow

    /// <summary>
    /// The y that stands a fighter ON the arena under <paramref name="at"/>, rather than at the
    /// height his root happens to be authored at.
    /// Since the capsule was fitted to the body (2026-10-22) the bottom of every fighter's
    /// CapsuleCollider IS his root origin, so the root goes straight onto the floor. The authored
    /// scene heights (0.24 Bjorn / 0.75 the rest) were measured when the capsule hung up to 0.75
    /// BELOW the feet and the root therefore floated by exactly that much: left in place they spawn
    /// a fighter up to 0.86 in the air and drop him through the count-in, and they stand him in the
    /// air on the select screens. A raycast also survives an arena swap, which one authored height
    /// cannot - the floor under the marks is not flat and not y=0 (it runs -0.065 to -0.267).
    /// Triggers are ignored (the hitboxes are triggers) and so is anything belonging to a fighter.
    /// </summary>
    float GroundedY(Vector3 at, float fallback)
    {
        var hits = Physics.RaycastAll(new Vector3(at.x, at.y + 3f, at.z), Vector3.down, 20f,
                                      ~0, QueryTriggerInteraction.Ignore);
        float best = float.NaN;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.GetComponentInParent<Fighter>() != null) continue;
            if (float.IsNaN(best) || hits[i].point.y > best) best = hits[i].point.y;
        }
        return float.IsNaN(best) ? fallback : best;
    }

    /// <summary>
    /// Spawn marks for the sides as they are now, each fighter keeping its own height.
    /// The fighting pair is pushed out to <see cref="BalanceConfig.MATCH_START_GAP"/> about the
    /// midpoint of the two authored marks, along the line those marks already establish - the
    /// authored pair sit 2.30 apart, inside everybody's reach, so a swing on the first frame of
    /// FIGHT landed before either man had moved. Taking the axis and the midpoint from the marks
    /// rather than hard-coding one keeps this working in an arena whose marks are somewhere else
    /// or at an angle, and the height still comes from a raycast at the NEW spot.
    /// </summary>
    void SpawnFor(Fighter f, out Vector3 pos, out Quaternion rot)
    {
        Vector3 markA = sideMark.Length > 0 ? sideMark[0] : Vector3.zero;
        Vector3 markB = sideMark.Length > 1 ? sideMark[1] : Vector3.zero;
        Vector3 mid = (markA + markB) * 0.5f;
        Vector3 axis = markB - markA;
        axis.y = 0f;
        if (axis.sqrMagnitude < 0.0001f) axis = Vector3.right;   // marks stacked: pick an axis
        axis.Normalize();
        float half = BalanceConfig.MATCH_START_GAP * 0.5f;

        int side = Mathf.Clamp(f.PlayerNumber - 1, 0, Mathf.Max(0, sideMark.Length - 1));
        // Only the two FIGHTING sides are spread; anything else keeps its own mark, since a
        // benched fighter is switched off and never placed anyway.
        Vector3 mark = side == 0 ? mid - axis * half
                     : side == 1 ? mid + axis * half
                     : sideMark[side];
        // X/Z from the mark, Y from a raycast with this fighter's own root as the fallback:
        // the two roots sit at different heights because each model has its own ground
        // offset, and averaging them is what left both fighters floating once before.
        pos = new Vector3(mark.x, GroundedY(mark, OwnY(f)), mark.z);
        rot = sideRot[side];
    }

    /// <summary>Everyone back in the scene: the select screens scroll through the whole roster.</summary>
    void ShowWholeRoster()
    {
        if (twin != null && twin.gameObject.activeSelf) twin.gameObject.SetActive(false);   // the ring is one of each
        foreach (var f in roster) if (!f.gameObject.activeSelf) f.gameObject.SetActive(true);
    }

    void PlaceFighters()
    {
        foreach (var f in Everyone())
        {
            // Benched fighters stay where they are: resetting one would poke an Animator on an
            // inactive object, which Unity warns about twice per call.
            if (!f.gameObject.activeSelf) continue;
            SpawnFor(f, out var pos, out var rot);
            f.ResetForMatch(pos, rot);
        }
        // The pair is on its marks: let the camera re-find it. It resolves Player 1 and Player 2
        // once in Start, and with three rigs the numbers move between matches, so without this it
        // keeps framing whoever held the number at scene load - a benched fighter on his mark.
        if (FightCamera.Instance != null) FightCamera.Instance.RetargetToFighters();
    }

    /// <summary>
    /// Hands Player 1 the roster entry <paramref name="first"/> and Player 2 <paramref name="second"/>.
    /// With more than two rigs in the scene everyone else is switched OFF, so only the chosen pair
    /// is in the arena, in the HUD and in front of the AI. The same entry twice is a MIRROR
    /// match: Player 1 keeps the rig and Player 2 gets its clone in the alternate colours.
    /// </summary>
    void ApplySides(int first, int second)
    {
        if (roster.Length < 2) return;
        p1Character = Mathf.Clamp(first, 0, roster.Length - 1);
        p2Character = Mathf.Clamp(second, 0, roster.Length - 1);
        if (Mirror) EnsureTwin(p1Character);
        // Twin first: it must be OFF before the roster entries change number, and wake (if at
        // all) only once its source is Player 1, so neither finds a stale opponent.
        if (twin != null && !Mirror && twin.gameObject.activeSelf) twin.gameObject.SetActive(false);
        for (int i = 0; i < roster.Length; i++)
        {
            bool fighting = i == p1Character || i == p2Character;
            if (roster[i].gameObject.activeSelf != fighting) roster[i].gameObject.SetActive(fighting);
            if (fighting) roster[i].SetPlayerNumber(i == p1Character ? 1 : 2);
        }
        if (Mirror)
        {
            twin.SetPlayerNumber(2);
            if (!twin.gameObject.activeSelf) twin.gameObject.SetActive(true);
        }
        // Both numbers are set now, so each of the two can look the other up. Without this they
        // keep whoever they found in Start, which with three rigs in the scene is the wrong one.
        P1Fighter.RefreshOpponent();
        P2Fighter.RefreshOpponent();

        var hud = FindAnyObjectByType<FightHud>();
        if (hud != null) hud.Rebuild();
        if (AiController.Instance != null && AiController.Instance.Active) AiController.Instance.Activate(aiDifficulty, 2, practiceRules);
    }

    void SetAi(bool on, AiController.Rules rules)
    {
        var ai = AiController.Instance != null ? AiController.Instance : GetComponent<AiController>();
        if (ai == null && on) ai = gameObject.AddComponent<AiController>();
        if (ai == null) return;
        if (on) ai.Activate(aiDifficulty, 2, rules); else ai.Deactivate();
    }

    public void StartMatch()
    {
        if (netRole == NetSession.Role.Guest) { Net?.SendRematchRequest(); return; }

        if (ArenaDirectory.Instance != null) ArenaDirectory.Instance.Select(arenaChoice);
        PlaceFighters();
        SetAi(false, AiController.Rules.Full);     // the computer wakes on FIGHT, with the players

        var practice = PracticeMode.Instance != null ? PracticeMode.Instance : gameObject.AddComponent<PracticeMode>();
        if (flow == Flow.Practice)
        {
            practice.infiniteHealth = practiceInfiniteHealth;
            practice.infiniteStamina = practiceInfiniteStamina;
            practice.Begin(PlaceFighters);
        }
        else practice.End();

        if (netRole == NetSession.Role.Host) Net?.HostStartMatch();
        if (TouchControls.Instance != null) TouchControls.Instance.ShowForMatch(playerCount >= 2 && !vsAi && netRole == NetSession.Role.None ? 2 : 1);

        // Held still, over the arena, until the count reaches FIGHT. inMatch stays false so a
        // K.O. cannot be declared during the count either.
        inMatch = false;
        FreezeFighters(true);
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = false;
        KillShot.RestoreLights();   // a rematch starts under full light again
        Time.timeScale = GameSpeed();
        Show(Screen.None);

        if (countIn != null) StopCoroutine(countIn);
        countIn = StartCoroutine(CountInRoutine());
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
        history.Clear();
        Show(Screen.Pause);
    }

    void ResumeLocal()
    {
        if (TouchControls.Instance != null) TouchControls.Instance.ShowForMatch(playerCount >= 2 && !vsAi && netRole == NetSession.Role.None ? 2 : 1);
        Time.timeScale = savedTimeScale > 0f ? savedTimeScale : GameSpeed();
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = true;
        Show(Screen.None);
    }

    void EndMatchToMenu()
    {
        inMatch = false;
        // Leaving during the count-in used to strand the coroutine (it only counts down while the
        // match is on screen), so "FIGHT" stayed up over the main menu until the next fight.
        if (countIn != null) { StopCoroutine(countIn); countIn = null; }
        if (stage != null) stage.ClearBeat();
        if (ReplayRecorder.Instance != null) ReplayRecorder.Instance.StopRecording();
        SetAi(false, AiController.Rules.Full);
        if (PracticeMode.Instance != null) PracticeMode.Instance.End();
        if (netRole != NetSession.Role.None) { Net?.GuestLeaveMatch(); Net?.Leave(); netRole = NetSession.Role.None; }
        if (TouchControls.Instance != null) TouchControls.Instance.Hide();
        Time.timeScale = GameSpeed();
        // Bench everyone the select screens may have woken (ShowWholeRoster): left active, the
        // extras land stacked on the two marks, push each other off the ground, and stand in the
        // arena behind every later screen until the next fight.
        ApplySides(p1Character, p2Character);
        PlaceFighters();
        FreezeFighters(true);
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = false;
        flow = Flow.None;
        history.Clear();
        Show(Screen.Main);
    }

    /// <summary>
    /// READY - 3 - 2 - 1 - FIGHT over the arena, with both fighters frozen until the last beat.
    /// Runs on unscaled time and holds while the game is paused, so pausing mid-count does not
    /// burn the count down behind the pause menu.
    /// </summary>
    IEnumerator CountInRoutine()
    {
        string[] beats = { "Ready", "3", "2", "1" };
        float[] holds = { 0.80f, 0.55f, 0.55f, 0.55f };

        for (int i = 0; i < beats.Length; i++)
        {
            if (stage != null) stage.Beat(beats[i], i == 0 ? MenuArt.Bone : MenuArt.BronzeLit, i == 0 ? 130 : 240);
            CrucibleAudio.Ui(CrucibleAudio.UiSound.CountBeat);
            yield return WaitWhilePlaying(holds[i]);
        }

        inMatch = true;
        FreezeFighters(false);
        if (InputManager.Instance != null) InputManager.Instance.InputEnabled = true;
        SetAi(vsAi, flow == Flow.Practice ? practiceRules : AiController.Rules.Full);
        Rec.BeginRecording(Everyone(), Camera.main);   // the game record starts at FIGHT, like the players

        if (stage != null)
            stage.Beat("Fight!", MenuArt.BloodLit, 210, flow == Flow.Practice ? "pause for the rules" : null);
        CrucibleAudio.Ui(CrucibleAudio.UiSound.CountFight);
        yield return WaitWhilePlaying(0.55f);
        if (stage != null) stage.ClearBeat();
        countIn = null;
    }

    /// <summary>Counts down <paramref name="seconds"/> of unscaled time, but only while the match
    /// is actually on screen.</summary>
    IEnumerator WaitWhilePlaying(float seconds)
    {
        float left = seconds;
        while (left > 0f)
        {
            if (screen == Screen.None) left -= Time.unscaledDeltaTime;
            yield return null;
        }
    }

    Coroutine countIn;

    void CheckKO()
    {
        if (!inMatch) return;
        foreach (var f in Everyone())
        {
            if (!f.gameObject.activeInHierarchy) continue;    // a benched fighter is not in this match
            if (f.Stats != null && !f.Stats.IsAlive())
            {
                inMatch = false;
                if (countIn != null) { StopCoroutine(countIn); countIn = null; }
                FreezeFighters(true);
                if (TouchControls.Instance != null) TouchControls.Instance.Hide();
                if (InputManager.Instance != null) InputManager.Instance.InputEnabled = false;
                // The ending names the WINNER, not the fallen (the user's call, 2026-10-24).
                Fighter standing = null;
                foreach (var w in Everyone()) if (w != f && w.gameObject.activeInHierarchy) standing = w;
                if (stage != null) stage.Announce((standing != null ? standing.CharacterName : "Nobody") + "\nwins",
                                                  MenuArt.BloodLit, 2.0f, null, 140);
                StartCoroutine(ShowMatchOver(f));
                return;
            }
        }
    }

    IEnumerator ShowMatchOver(Fighter loser)
    {
        yield return new WaitForSecondsRealtime(1.9f);
        // Stop here, not at the K.O. itself: the death and the kill shot belong in the record.
        if (ReplayRecorder.Instance != null) ReplayRecorder.Instance.StopRecording();
        Fighter winner = null;
        foreach (var f in Everyone()) if (f != loser && f.gameObject.activeInHierarchy) winner = f;
        winnerName = winner != null ? winner.CharacterName : "Nobody";
        winnerNumber = winner != null ? winner.PlayerNumber : 0;
        // Every finished fight lands in the records library, so the main menu can replay it later.
        if (ReplayRecorder.Instance != null && ReplayRecorder.Instance.HasRecording
            && ReplayRecorder.Instance.Duration >= 3f)
        {
            string arena = ArenaDirectory.Instance != null ? ArenaDirectory.Instance.NameOf(arenaChoice) : "";
            string a = roster[p1Character] != null ? roster[p1Character].CharacterName : "?";
            string b = roster[p2Character] != null ? roster[p2Character].CharacterName : "?";
            ReplayRecorder.Instance.SaveRecord($"{a} vs {b} - {winnerName} wins", arena);
        }
        history.Clear();
        Show(Screen.MatchOver);
    }

    void FreezeFighters(bool freeze)
    {
        foreach (var f in Everyone())
        {
            f.enabled = !freeze;
            var an = f.GetComponent<Animator>();
            if (freeze && an != null && an.runtimeAnimatorController != null) an.SetFloat("MoveSpeed", 0f);
        }
    }

    // ================================================================ settings

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
        CrucibleAudio.SetVolumes(settings.musicVolume, settings.effectsVolume, settings.voiceVolume);
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
        if (InputManager.Instance != null) InputManager.Instance.Bindings.firstPadPlayer = settings.padPlayer == 1 ? 1 : 2;
    }

    // ================================================================ screens

    /// <summary>Rebuilds the current screen in place, keeping the cursor where it was.</summary>
    void Rebuild()
    {
        int keepSel = selected, keepScroll = scroll;
        float keepAge = screenAge;
        rebuilding = true;
        Show(screen, push: false);
        rebuilding = false;
        screenAge = keepAge;                    // a toggle must not replay the entrance animation
        selected = Mathf.Clamp(keepSel, 0, Mathf.Max(0, items.Count - 1));
        scroll = Mathf.Clamp(keepScroll, 0, Mathf.Max(0, items.Count - visibleRows));
        if (items.Count > 0 && items[selected].header) MoveSelection(1, sound: false); else Refresh();
    }

    /// <summary>Changes screen behind a wipe, for the transitions between the big views.</summary>
    void GoWiped(Screen s)
    {
        if (stage == null) { Show(s); return; }
        CrucibleAudio.Ui(CrucibleAudio.UiSound.Wipe);
        stage.Wipe(() => Show(s));
    }

    void Show(Screen s, bool push = true)
    {
        bool leavingStage = screen == Screen.CharSelect || screen == Screen.ArenaSelect || screen == Screen.Vs;
        if (push && screen != Screen.None && screen != s && screen != Screen.Boot) history.Add(screen);

        if (s == Screen.CharSelect && screen != Screen.CharSelect && !rebuilding)
        {
            pickingP2 = false;
            lock1 = lock2 = false;
            mergingSelect = false;
        }
        // Leaving the character select hands the screen back to one camera, whatever state the
        // split was in (merge finished, backed out, or a direct jump elsewhere).
        if (screen == Screen.CharSelect && s != Screen.CharSelect) RestoreSelectCameras();
        // Leaving the record screen hands the fighters, their components and the camera back,
        // and restores the K.O. tableau the playback interrupted.
        if (screen == Screen.Replay && s != Screen.Replay && ReplayRecorder.Instance != null)
            ReplayRecorder.Instance.EndPlayback();
        // Leaving the records area outside a match puts the SELECTED pair back on their sides -
        // a record watched from the library may have woken a different pair.
        if ((screen == Screen.Replay || screen == Screen.Records)
            && s != Screen.Replay && s != Screen.Records && !rebuilding && !inMatch)
            ApplySides(p1Character, p2Character);
        if (s != Screen.MatchOver) { confirmAsk = null; confirmYes = null; }
        // The kill-shot darkness holds through the K.O. screen (the corpse under a black sky IS
        // the shot); any real menu screen brings the arena lights back for the select views.
        if (s != Screen.None && s != Screen.Pause && s != Screen.MatchOver) KillShot.RestoreLights();
        screen = s;
        capturing = false;
        captureBack.gameObject.SetActive(false);
        screenAge = 0f;
        inputGuard = rebuilding ? inputGuard : 0.22f;

        // Music follows the screen: the fight track and the low wind bed while the match is on
        // screen, the menu track everywhere else. Pause and the K.O. screen sit OVER the match, so
        // they keep the fight underneath and duck it instead of switching tracks.
        if (s == Screen.None)
        {
            CrucibleAudio.Scene(CrucibleAudio.MusicMode.Fight);
            CrucibleAudio.Ducked(false);
        }
        else if (s == Screen.Pause || s == Screen.MatchOver || s == Screen.Replay)
        {
            CrucibleAudio.Ducked(true);
        }
        else
        {
            CrucibleAudio.Scene(CrucibleAudio.MusicMode.Menu);
            CrucibleAudio.Ducked(false);
        }

        bool menuVisible = s != Screen.None;
        bool onStage = s == Screen.CharSelect || s == Screen.ArenaSelect || s == Screen.Vs;
        content.gameObject.SetActive(menuVisible);
        if (stage != null)
        {
            stage.SetBackdrop(s == Screen.None || s == Screen.Replay ? MenuStage.Backdrop.None
                            : s == Screen.Boot ? MenuStage.Backdrop.Black
                            : onStage ? MenuStage.Backdrop.Stage
                            : s == Screen.Pause || s == Screen.MatchOver ? MenuStage.Backdrop.Pause
                            : MenuStage.Backdrop.Menu,
                              // Instant for the landing page: the field otherwise RAMPS toward
                              // its target, so the boot screen opened on the lit arena and sank
                              // to black over about a second. Measured mid-ramp at alpha 0.670
                              // with the arena plainly visible through it. Every other screen
                              // keeps the ramp - that gradual settle is what makes the wipes read.
                              // Leaving the stage screens for an ordinary menu is instant too: the
                              // ramp up from the stage's 0.42 left the posed fighters showing
                              // through the menu for a second (the user's call, 2026-10-26: the
                              // menu must come back exactly as it starts).
                              s == Screen.Boot || (leavingStage && !onStage));
            stage.ShowWatermark(s != Screen.Boot && !onStage && s != Screen.Replay);
        }

        // The select screens show the REAL fighters idling in the arena, so the pair has to be
        // posed for the camera instead of squared up against each other.
        if (onStage) StageFighters(s == Screen.Vs, s == Screen.ArenaSelect);
        else if (staged) Unstage();
        if (s == Screen.CharSelect) BeginCharSelect();

        bootView.gameObject.SetActive(s == Screen.Boot);
        selectView.gameObject.SetActive(s == Screen.CharSelect);
        arenaView.gameObject.SetActive(s == Screen.ArenaSelect);
        vsView.gameObject.SetActive(s == Screen.Vs);
        panel.gameObject.SetActive(menuVisible && s != Screen.Boot && s != Screen.Vs && !onStage);
        header.gameObject.SetActive(menuVisible && s != Screen.Boot && s != Screen.Vs);
        footer.gameObject.SetActive(menuVisible && s != Screen.Boot && s != Screen.Vs);
        bool logoTitle = s == Screen.Main && MenuArt.Logo != null;
        titleLogo.gameObject.SetActive(logoTitle);
        title.gameObject.SetActive(!logoTitle);
        divider.gameObject.SetActive(!logoTitle);
        subtitle.gameObject.SetActive(!logoTitle);

        var hud = FindAnyObjectByType<FightHud>();
        // The record screen keeps the HUD up: playback drives the bars with the RECORDED health
        // and stamina, and the export bakes them into the video (the user's call, 2026-10-25).
        if (hud != null) hud.SetVisible(s == Screen.None || s == Screen.Replay);

        if (s == Screen.None) return;
        if (s == Screen.Boot) { BuildBoot(); return; }
        if (s == Screen.Vs) { StartCoroutine(VsRoutine()); return; }

        items.Clear();
        selected = 0; scroll = 0;
        visibleRows = 16; rowHeight = 46f; rowFont = 28;
        subtitle.text = "";
        BuildScreen(s);

        LayoutPanel();
        LayoutRows();
        if (items.Count > 0 && items[selected].header) MoveSelection(1, sound: false);
        Refresh();
        if (!rebuilding) for (int i = 0; i < rows.Count; i++) rows[i].slide = 0f;   // stagger the entrance
    }

    void BuildScreen(Screen s)
    {
        switch (s)
        {
            // ------------------------------------------------------------ main
            case Screen.Main:
                SetTitle("Crucible", "Honourable Souls");
                items.Add(new Item { label = "Battle Now", value = () => "1 or 2 players on this device", confirm = () => { flow = Flow.Local; GoWiped(Screen.LocalSetup); } });
                items.Add(new Item { label = "Online", value = () => "play over the link", confirm = () => { flow = Flow.Online; InputManager.Instance?.ClearPlayerDevices(); Show(Screen.Online); } });
                items.Add(new Item { label = "Practice", value = () => "a dummy that obeys your rules", confirm = () => { flow = Flow.Practice; playerCount = 1; InputManager.Instance?.ClearPlayerDevices(); GoWiped(Screen.CharSelect); } });
                items.Add(new Item { label = "Move List", confirm = () => Show(Screen.Moves) });
                items.Add(new Item { label = "Records", value = () => "watch and export saved fights", confirm = () => Show(Screen.Records) });
                items.Add(new Item { label = "Settings", confirm = () => Show(Screen.Settings) });
                items.Add(new Item { label = "Quit", confirm = Quit });
                footerText.text = NavHint();
                break;

            // ------------------------------------------------------------ local setup
            case Screen.LocalSetup:
                SetTitle("Battle Now", "how many of you are there?");
                items.Add(new Item { label = "One Player", value = () => "you against the computer", confirm = () => { playerCount = 1; InputManager.Instance?.ClearPlayerDevices(); GoWiped(Screen.CharSelect); } });
                items.Add(new Item { label = "Two Players", value = () => "both fighters on this device", confirm = () => { playerCount = 2; DefaultDeviceChoices(); GoWiped(Screen.PickDevices); } });
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = NavHint();
                break;

            // ------------------------------------------------------------ 2P device choice
            // Who holds what (the user's call, 2026-10-25): every CONNECTED controller is offered
            // by name, plus the keyboard; the two players may not share a pad, two keyboards are
            // fine (each has his own key set). Picking a pad pulses its rumble, so two identical
            // DualSenses - or a live pad next to a Bluetooth ghost of itself - tell themselves
            // apart in the hand. The choice pins InputManager's routing for the whole session.
            case Screen.PickDevices:
                SetTitle("Choose Controls", InputManager.AnyPad
                    ? "pick a device each - a chosen pad answers with a rumble"
                    : "no controller connected - both of you are on the keyboard");
                items.Add(new Item { label = "Player 1", value = () => DeviceName(1),
                                     adjust = d => CycleDevice(1, d), confirm = () => CycleDevice(1, 1) });
                items.Add(new Item { label = "Player 2", value = () => DeviceName(2),
                                     adjust = d => CycleDevice(2, d), confirm = () => CycleDevice(2, 1) });
                items.Add(new Item { label = "", header = true });
                items.Add(new Item { label = "Begin", confirm = () => { ApplyDeviceChoices(); GoWiped(Screen.CharSelect); } });
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = "Left / Right to change    " + NavHint();
                break;

            // ------------------------------------------------------------ difficulty
            case Screen.Difficulty:
                SetTitle("The Opponent", "how hard should it push back?");
                items.Add(new Item { label = "Chill", value = () => "slow, sloppy, forgiving", confirm = () => ChooseDifficulty(AiDifficulty.Chill) });
                items.Add(new Item { label = "Moderate", value = () => "reads wind-ups, blocks and dodges half the time", confirm = () => ChooseDifficulty(AiDifficulty.Moderate) });
                items.Add(new Item { label = "Insane", value = () => "reacts in frames, punishes every whiff", confirm = () => ChooseDifficulty(AiDifficulty.Insane) });
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = NavHint();
                break;

            // ------------------------------------------------------------ practice
            case Screen.PracticeRules:
                SetTitle("Practice", "decide what the dummy is allowed to do");
                items.Add(new Item { label = "Opponent", value = () => RulesSummary(), adjust = d => { CycleRulesPreset(d); Rebuild(); }, confirm = () => { CycleRulesPreset(1); Rebuild(); } });
                items.Add(new Item { label = "   Walks and keeps spacing", value = () => OnOff(practiceRules.walk), adjust = d => { practiceRules.walk = !practiceRules.walk; Rebuild(); }, confirm = () => { practiceRules.walk = !practiceRules.walk; Rebuild(); } });
                items.Add(new Item { label = "   Attacks", value = () => OnOff(practiceRules.attack), adjust = d => { practiceRules.attack = !practiceRules.attack; Rebuild(); }, confirm = () => { practiceRules.attack = !practiceRules.attack; Rebuild(); } });
                items.Add(new Item { label = "   Blocks", value = () => OnOff(practiceRules.block), adjust = d => { practiceRules.block = !practiceRules.block; Rebuild(); }, confirm = () => { practiceRules.block = !practiceRules.block; Rebuild(); } });
                items.Add(new Item { label = "   Dodges", value = () => OnOff(practiceRules.dodge), adjust = d => { practiceRules.dodge = !practiceRules.dodge; Rebuild(); }, confirm = () => { practiceRules.dodge = !practiceRules.dodge; Rebuild(); } });
                items.Add(new Item { label = "   Punishes and counters", value = () => OnOff(practiceRules.punish), adjust = d => { practiceRules.punish = !practiceRules.punish; Rebuild(); }, confirm = () => { practiceRules.punish = !practiceRules.punish; Rebuild(); } });
                items.Add(new Item { label = "Skill", value = () => aiDifficulty.ToString(), adjust = d => { aiDifficulty = (AiDifficulty)(((int)aiDifficulty + (d > 0 ? 1 : 2)) % 3); Rebuild(); } });
                items.Add(new Item { label = "", header = true });
                items.Add(new Item { label = "Infinite health", value = () => OnOff(practiceInfiniteHealth), adjust = d => { practiceInfiniteHealth = !practiceInfiniteHealth; Rebuild(); }, confirm = () => { practiceInfiniteHealth = !practiceInfiniteHealth; Rebuild(); } });
                items.Add(new Item { label = "Infinite stamina", value = () => OnOff(practiceInfiniteStamina), adjust = d => { practiceInfiniteStamina = !practiceInfiniteStamina; Rebuild(); }, confirm = () => { practiceInfiniteStamina = !practiceInfiniteStamina; Rebuild(); } });
                items.Add(new Item { label = "", header = true });
                items.Add(new Item { label = "Begin", confirm = () => { vsAi = true; GoWiped(Screen.Vs); } });
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = "Left / Right to change    " + NavHint();
                break;

            // ------------------------------------------------------------ online
            case Screen.Online:
                SetTitle("Online", "both players open " + NetSession.LanHint());
                items.Add(new Item { label = "Host a game", value = () => "you fight as Player 1", confirm = () =>
                {
                    if (Net == null) return;
                    Net.Host(Environment.MachineName.Length > 0 ? Environment.MachineName : "room");
                    Show(Screen.OnlineHost);
                } });
                items.Add(new Item { label = "Join a game", value = () => "you fight as Player 2", confirm = () => { if (Net == null) return; Net.BrowseRooms(); Show(Screen.OnlineJoin); } });
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = NavHint();
                break;

            case Screen.OnlineHost:
                SetTitle("Hosting", Net != null ? Net.Status : "");
                items.Add(new Item { label = Net != null && Net.PeerConnected ? "Start the fight" : "Waiting for a player...",
                                     confirm = () => { if (Net != null && Net.PeerConnected) { vsAi = false; playerCount = 2; netRole = NetSession.Role.Host; StartMatch(); } } });
                items.Add(new Item { label = "Cancel", confirm = () => { Net?.Leave(); netRole = NetSession.Role.None; Show(Screen.Online, push: false); } });
                footerText.text = NavHint();
                break;

            case Screen.OnlineJoin:
                SetTitle("Join a Game", Net != null ? Net.Status : "");
                if (Net != null)
                {
                    if (Net.Rooms.Count == 0) items.Add(new Item { label = "(no open games yet - the host must press Host a game)", header = true, quiet = true });
                    foreach (var r in Net.Rooms) { string room = r; items.Add(new Item { label = "Join " + room, confirm = () => Net.Join(room) }); }
                }
                items.Add(new Item { label = "Back", confirm = () => { Net?.Leave(); netRole = NetSession.Role.None; Show(Screen.Online, push: false); } });
                footerText.text = NavHint();
                break;

            // ------------------------------------------------------------ pause
            case Screen.Pause:
                SetTitle("Paused", PauseSubtitle());
                items.Add(new Item { label = "Resume", confirm = Resume });
                items.Add(new Item { label = "Move List", value = () => "strikes, specials and strings", confirm = () => Show(Screen.Moves) });
                items.Add(new Item { label = "How to Fight", confirm = () => Show(Screen.Help) });
                items.Add(new Item { label = "Controls", confirm = () => Show(Screen.Controls) });
                items.Add(new Item { label = "Settings", confirm = () => Show(Screen.Settings) });
                if (flow == Flow.Practice)
                    items.Add(new Item { label = "Practice rules", confirm = () => Show(Screen.PracticeRules) });
                items.Add(new Item { label = "Restart the fight", confirm = () => { if (stage != null) stage.Wipe(StartMatch); else StartMatch(); } });
                items.Add(new Item { label = "Main Menu", confirm = () => { if (stage != null) stage.Wipe(EndMatchToMenu); else EndMatchToMenu(); } });
                footerText.text = "Options / Esc resumes    " + NavHint();
                break;

            // ------------------------------------------------------------ KO
            case Screen.MatchOver:
                SetTitle(winnerNumber > 0 ? (vsAi ? (winnerNumber == 1 ? "You Win" : "You Lose") : $"Player {winnerNumber} Wins") : "Draw",
                         winnerNumber > 0 ? winnerName + " is left standing" : "");
                if (confirmAsk != null)
                {
                    // One honest question before anything that throws the result away: a stray
                    // confirm on the K.O. card used to restart the fight on the spot.
                    subtitle.text = confirmAsk;
                    items.Add(new Item { label = "Yes", confirm = () =>
                    {
                        var yes = confirmYes;
                        confirmAsk = null; confirmYes = null;
                        yes?.Invoke();
                    } });
                    items.Add(new Item { label = "No, not yet", confirm = CancelMatchOverAsk });
                    footerText.text = NavHint();
                    break;
                }
                items.Add(new Item { label = "Rematch", confirm = () =>
                    AskMatchOver("Fight the same pair again?", () => { if (stage != null) stage.Wipe(StartMatch); else StartMatch(); }) });
                items.Add(new Item { label = "Change fighters", confirm = () =>
                    AskMatchOver("Leave for the character select?", () => { inMatch = false; GoWiped(Screen.CharSelect); }) });
                if (ReplayRecorder.Instance != null && ReplayRecorder.Instance.HasRecording)
                    items.Add(new Item { label = "Watch the game record", value = () => "kept in Records - watch, trim and export",
                                         confirm = () => Show(Screen.Replay) });
                items.Add(new Item { label = "Main Menu", confirm = () =>
                    AskMatchOver("Back to the main menu?", () => { if (stage != null) stage.Wipe(EndMatchToMenu); else EndMatchToMenu(); }) });
                footerText.text = NavHint();
                break;

            // ------------------------------------------------------------ the game record
            case Screen.Replay:
            {
                var rec = ReplayRecorder.Instance;
                bool has = rec != null && rec.HasRecording;
                SetTitle("Game Record", has ? "the fight as it was played - HUD, effects and sound in the export" : "");
                if (!has)
                {
                    items.Add(new Item { label = "Nothing recorded yet.", header = true, quiet = true });
                    items.Add(new Item { label = "Back", confirm = Back });
                    footerText.text = NavHint();
                    break;
                }
                if (!rec.PlaybackActive)
                {
                    rec.BeginPlayback();
                    trimStart = 0f;
                    trimEnd = rec.Duration;
                    replayTime = 0f;
                    replayMarker = 0;
                    replayPlaying = true;
                    exportingUi = false;
                }
                items.Add(new Item
                {
                    label = "Playback",
                    value = () => (replayPlaying ? "Playing" : "Paused") + $"   {Clock(replayTime)} / {Clock(rec.Duration)}",
                    confirm = () => replayPlaying = !replayPlaying,
                    adjust = d => { replayPlaying = false; replayTime = Mathf.Clamp(replayTime + d, trimStart, trimEnd); },
                });
                if (rec.MarkerCount > 0)
                    items.Add(new Item
                    {
                        label = "Jump to",
                        value = () => $"{rec.MarkerLabel(replayMarker)}  ({replayMarker + 1} of {rec.MarkerCount})",
                        adjust = d => { replayMarker = ((replayMarker + d) % rec.MarkerCount + rec.MarkerCount) % rec.MarkerCount; JumpToMarker(rec); },
                        confirm = () => { replayMarker = (replayMarker + 1) % rec.MarkerCount; JumpToMarker(rec); },
                    });
                items.Add(new Item
                {
                    label = "Camera",
                    value = () => rec.ViewName,
                    adjust = d => rec.CycleView(d),
                    confirm = () => rec.CycleView(1),
                });
                items.Add(new Item
                {
                    label = "Cut from",
                    value = () => Clock(trimStart),
                    adjust = d => { trimStart = Mathf.Clamp(trimStart + d * 0.5f, 0f, trimEnd - 0.5f); replayTime = Mathf.Max(replayTime, trimStart); },
                    confirm = () => trimStart = Mathf.Min(replayTime, trimEnd - 0.5f),
                });
                items.Add(new Item
                {
                    label = "Cut to",
                    value = () => Clock(trimEnd),
                    adjust = d => { trimEnd = Mathf.Clamp(trimEnd + d * 0.5f, trimStart + 0.5f, rec.Duration); replayTime = Mathf.Min(replayTime, trimEnd); },
                    confirm = () => trimEnd = Mathf.Max(replayTime, trimStart + 0.5f),
                });
                items.Add(new Item
                {
                    label = "Export as video",
                    value = () => $"the {Clock(trimEnd - trimStart)} cut, {ReplayRecorder.ExportFps} fps .avi",
                    confirm = BeginVideoExport,
                });
                var moments = rec.HighlightSegments();
                if (moments.Count > 0)
                {
                    float highlightLen = 0f;
                    foreach (var m in moments) highlightLen += m.y - m.x;
                    items.Add(new Item
                    {
                        label = "Export the highlights",
                        value = () => $"{moments.Count} moments, {Clock(highlightLen)} .avi",
                        confirm = () => BeginSegmentsExport(moments),
                    });
                }
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = "Left / Right scrubs and trims    Confirm on a cut marks the current moment    " + NavHint();
                break;
            }

            // ------------------------------------------------------------ the records library
            case Screen.Records:
            {
                SetTitle("Records", "every finished fight, kept on this machine");
                var saved = ReplayRecorder.ListRecords();
                if (saved.Count == 0)
                    items.Add(new Item { label = "No fights on record yet - finish a match and it lands here.", header = true, quiet = true });
                int shown = Mathf.Min(saved.Count, 12);
                for (int i = 0; i < shown; i++)
                {
                    var r = saved[i];
                    items.Add(new Item
                    {
                        label = r.title,
                        value = () => $"{r.date:MMM d, HH:mm}   {Clock(r.duration)}   {r.arena}",
                        confirm = () => OpenRecord(r),
                    });
                }
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = NavHint();
                break;
            }

            // ------------------------------------------------------------ settings
            case Screen.Settings:
                SetTitle("Settings", "");
                items.Add(new Item { label = "Master volume", value = () => Mathf.RoundToInt(settings.volume * 100) + "%",
                    adjust = d => { settings.volume = Mathf.Clamp01(settings.volume + d * 0.1f); ApplySettings(); SaveSettings(); } });
                items.Add(new Item { label = "Music", value = () => Mathf.RoundToInt(settings.musicVolume * 100) + "%",
                    adjust = d => { settings.musicVolume = Mathf.Clamp01(settings.musicVolume + d * 0.1f); ApplySettings(); SaveSettings(); } });
                items.Add(new Item { label = "Sound effects", value = () => Mathf.RoundToInt(settings.effectsVolume * 100) + "%",
                    adjust = d => { settings.effectsVolume = Mathf.Clamp01(settings.effectsVolume + d * 0.1f); ApplySettings(); SaveSettings(); } });
                items.Add(new Item { label = "Voices", value = () => Mathf.RoundToInt(settings.voiceVolume * 100) + "%",
                    adjust = d => { settings.voiceVolume = Mathf.Clamp01(settings.voiceVolume + d * 0.1f); ApplySettings(); SaveSettings(); } });
                items.Add(new Item { label = "Game speed", value = () => Mathf.RoundToInt(settings.gameSpeed * 100) + "%",
                    adjust = d => { settings.gameSpeed = Mathf.Clamp(settings.gameSpeed + d * 0.05f, 0.5f, 1f); ApplySettings(); SaveSettings(); } });
                items.Add(new Item { label = "Blood", value = () => OnOff(settings.blood),
                    adjust = d => { settings.blood = !settings.blood; ApplySettings(); SaveSettings(); },
                    confirm = () => { settings.blood = !settings.blood; ApplySettings(); SaveSettings(); Rebuild(); } });
                items.Add(new Item { label = "On-screen buttons",
                    value = () => settings.touchButtons == 1 ? "Always" : settings.touchButtons == 2 ? "Off" : "Auto (phones)",
                    adjust = d => { settings.touchButtons = (settings.touchButtons + (d > 0 ? 1 : 2)) % 3; ApplySettings(); SaveSettings(); },
                    confirm = () => { settings.touchButtons = (settings.touchButtons + 1) % 3; ApplySettings(); SaveSettings(); Rebuild(); } });
                items.Add(new Item { label = "Controller drives", value = () => settings.padPlayer == 1 ? "Player 1" : "Player 2",
                    adjust = d => { settings.padPlayer = settings.padPlayer == 1 ? 2 : 1; ApplySettings(); SaveSettings(); },
                    confirm = () => { settings.padPlayer = settings.padPlayer == 1 ? 2 : 1; ApplySettings(); SaveSettings(); Rebuild(); } });
                items.Add(new Item { label = "", header = true });
                items.Add(new Item { label = "Controls", value = () => "rebind every key and button", confirm = () => Show(Screen.Controls) });
                items.Add(new Item { label = "How to Fight", confirm = () => Show(Screen.Help) });
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = "Left / Right to change    " + NavHint();
                break;

            // ------------------------------------------------------------ controls
            case Screen.Controls:
                SetTitle("Controls", "pick an action, then press the key or pad button for it");
                visibleRows = 20; rowHeight = 37f; rowFont = 23;
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
                    Rebuild();
                } });
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = "Dash: hold the dash key + a direction, or flick the right stick    " + NavHint();
                break;

            // ------------------------------------------------------------ moves
            case Screen.Moves:
            {
                visibleRows = 22; rowHeight = 33f; rowFont = 21;
                if (!movesShowPadInit) { movesShowPadInit = true; movesShowPad = Gamepad.all.Count > 0; }
                int pages = Mathf.Max(1, MovesRoster().Count * 2);
                movesPage = ((movesPage % pages) + pages) % pages;
                SetTitle("Move List", "read from the live attack data, so this list cannot go stale");
                if (pages > 1)
                    items.Add(new Item
                    {
                        label = "Showing",
                        value = () => MovesPageName(movesPage),
                        // Rebuild, not Refresh: the page's lines are baked into `items` when the
                        // screen is built, so only a rebuild actually changes what is on it. This
                        // is the bug that made the page label flip while the moves stayed put.
                        adjust = d => { int n = Mathf.Max(1, MovesRoster().Count * 2); movesPage = (movesPage + (d > 0 ? 1 : n - 1)) % n; Rebuild(); },
                        confirm = () => { int n = Mathf.Max(1, MovesRoster().Count * 2); movesPage = (movesPage + 1) % n; Rebuild(); },
                    });
                items.Add(new Item
                {
                    label = "Controls shown",
                    value = () => movesShowPad ? "PS controller" : "Keyboard",
                    adjust = d => { movesShowPad = !movesShowPad; Rebuild(); },
                    confirm = () => { movesShowPad = !movesShowPad; Rebuild(); },
                });
                foreach (string line in MoveLines(movesPage)) items.Add(new Item { label = line, header = true, quiet = true });
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = "Left / Right changes the page    " + NavHint();
                break;
            }

            // ------------------------------------------------------------ help
            case Screen.Help:
                visibleRows = 22; rowHeight = 33f; rowFont = 21;
                SetTitle("How to Fight", "");
                foreach (string line in HelpLines()) items.Add(new Item { label = line, header = true, quiet = true });
                items.Add(new Item { label = "Back", confirm = Back });
                footerText.text = NavHint();
                break;

            // ------------------------------------------------------------ the custom views
            case Screen.CharSelect:
                SetTitle(playerCount >= 2 && flow != Flow.Practice ? "Choose Your Fighters" : "Choose Your Fighter",
                         playerCount >= 2 && flow != Flow.Practice
                           ? "both players pick at once"
                           : roster.Length > 2
                             ? (pickingP2 ? (flow == Flow.Practice ? "now who the dummy will be" : "now who the computer brings")
                                          : "Player 1 picks first")
                             : "the computer takes the other");
                BuildCharSelect();
                footerText.text = playerCount >= 2 && flow != Flow.Practice
                    ? "Left / Right to choose    Cross / Space / Enter to lock in    Circle to unlock    Esc to go back"
                    : "Left / Right to choose    Enter / Cross to lock in    Esc to go back";
                break;

            case Screen.ArenaSelect:
                SetTitle("Choose Your Ground", "");
                BuildArenaSelect();
                footerText.text = "Left / Right to choose    Enter / Cross to fight    Esc to go back";
                break;
        }
    }

    void SetTitle(string t, string sub)
    {
        title.text = MenuArt.Track(t.ToUpperInvariant(), 1);
        subtitle.text = sub ?? "";
    }

    static string OnOff(bool b) => b ? "On" : "Off";

    string PauseSubtitle()
    {
        if (flow == Flow.Practice) return "practice - nothing here can kill you";
        if (netRole != NetSession.Role.None) return "online";
        if (vsAi) return $"versus the computer ({aiDifficulty})";
        return "two players";
    }

    void ChooseDifficulty(AiDifficulty d)
    {
        aiDifficulty = d;
        vsAi = true;
        GoWiped(Screen.Vs);
    }

    void CycleRulesPreset(int dir)
    {
        // three useful shapes, in order: a standing dummy, a dummy that only defends, a full fight
        bool dummy = practiceRules.IsDummy;
        bool defends = !practiceRules.attack && (practiceRules.block || practiceRules.dodge);
        int now = dummy ? 0 : defends ? 1 : 2;
        int next = ((now + (dir > 0 ? 1 : 2)) % 3);
        practiceRules = next == 0 ? AiController.Rules.Dummy
                      : next == 1 ? new AiController.Rules { block = true, dodge = true }
                      : AiController.Rules.Full;
    }

    string RulesSummary()
    {
        if (practiceRules.IsDummy) return "Standing dummy";
        if (!practiceRules.attack && (practiceRules.block || practiceRules.dodge)) return "Defends only";
        if (practiceRules.attack && !practiceRules.block && !practiceRules.dodge) return "Attacks only";
        var r = practiceRules;
        return r.walk && r.attack && r.block && r.dodge && r.punish ? "Full fight" : "Custom";
    }

    static string NavHint() => TouchControls.WantsTouch
        ? "Tap an item"
        : "Arrows / D-pad: move    Enter / Cross: select    Esc / Circle: back";

    // ================================================================ boot

    void BuildBoot()
    {
        bootPrompt.text = MenuArt.Track("PRESS ANY BUTTON", 2);
        bootExiting = false;                   // a second visit must not start muted
        bootReady = false;                     // ...nor with the prompt already lit on the black
        if (bootRoutine != null) StopCoroutine(bootRoutine);
        bootRoutine = StartCoroutine(BootRoutine());
    }

    IEnumerator BootRoutine()
    {
        var logoRect = bootLogo != null ? bootLogo.rectTransform : null;
        bootPrompt.color = MenuArt.Fade(MenuArt.Bone, 0f);
        if (bootLogo != null) bootLogo.color = MenuArt.Fade(Color.white, 0f);
        if (bootTitle != null) bootTitle.color = MenuArt.Fade(MenuArt.BronzeLit, 0f);
        if (logoRect != null) logoRect.localScale = Vector3.one * 1.08f;

        // Hold on black first. The fade below already starts the logo at alpha 0, but without a
        // beat of nothing the page and the logo arrive together and there is no "then" to it.
        for (float t = 0f; t < BootBlackHold; t += Time.unscaledDeltaTime) yield return null;

        float fade = 1.1f;
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / fade)
        {
            float e = MenuArt.EaseOut(k);
            if (bootLogo != null) bootLogo.color = MenuArt.Fade(Color.white, e);
            if (bootTitle != null) bootTitle.color = MenuArt.Fade(MenuArt.BronzeLit, e);
            if (logoRect != null) logoRect.localScale = Vector3.one * Mathf.Lerp(1.08f, 1f, e);
            yield return null;
        }
        if (bootLogo != null) { bootLogo.color = Color.white; logoRect.localScale = Vector3.one; }
        bootReady = true;
    }

    bool bootReady;
    bool bootExiting;
    Coroutine bootRoutine;

    /// <summary>Seconds for one full dim-bright-dim breath of the "PRESS ANY BUTTON" prompt.</summary>
    const float BootPromptPeriod = 4f;

    /// <summary>Seconds of black before the logo starts to fade up, so the two read as a sequence.</summary>
    const float BootBlackHold = 0.5f;

    void UpdateBoot()
    {
        if (bootExiting) return;               // the exit fade owns the colours now

        // A slow breathe, not a blink. The old Abs(Sin(t * 1.6)) was fast twice over: Abs folds
        // the sine's negative half up, which HALVES the period to ~1.96 s, and it leaves a hard
        // cusp at the bottom that reads as a blink rather than a fade. A plain sine mapped into
        // 0..1 has neither, and the period is then just BootPromptPeriod.
        float phase = Time.unscaledTime * (Mathf.PI * 2f / BootPromptPeriod);
        float a = bootReady ? 0.30f + 0.70f * (0.5f + 0.5f * Mathf.Sin(phase)) : 0f;
        bootPrompt.color = MenuArt.Fade(MenuArt.Bone, a);

        if (!bootReady || inputGuard > 0f) return;
        if (!AnyButton()) return;

        bootReady = false;
        bootExiting = true;
        CrucibleAudio.Ui(CrucibleAudio.UiSound.Boot);   // one deep hit, under the 0.45 s fade-out
        if (bootRoutine != null) StopCoroutine(bootRoutine);
        bootRoutine = StartCoroutine(BootExitRoutine());
    }

    /// <summary>
    /// The other half of <see cref="BootRoutine"/>. The logo fades IN on arrival and used to be
    /// cut away mid-glow by the wipe; a press now fades it back out first, so the landing page
    /// breathes out. The logo drifts very slightly larger as it goes, the mirror of the 1.08 -> 1
    /// settle on the way in.
    /// </summary>
    IEnumerator BootExitRoutine()
    {
        var logoRect = bootLogo != null ? bootLogo.rectTransform : null;
        float fade = 0.45f;
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / fade)
        {
            float e = MenuArt.EaseOut(k);
            float a = 1f - e;
            if (bootLogo != null) bootLogo.color = MenuArt.Fade(Color.white, a);
            if (bootTitle != null) bootTitle.color = MenuArt.Fade(MenuArt.BronzeLit, a);
            if (bootPrompt != null) bootPrompt.color = MenuArt.Fade(MenuArt.Bone, a);
            if (logoRect != null) logoRect.localScale = Vector3.one * Mathf.Lerp(1f, 1.04f, e);
            yield return null;
        }
        if (bootLogo != null) bootLogo.color = MenuArt.Fade(Color.white, 0f);
        if (bootTitle != null) bootTitle.color = MenuArt.Fade(MenuArt.BronzeLit, 0f);
        if (bootPrompt != null) bootPrompt.color = MenuArt.Fade(MenuArt.Bone, 0f);

        bootExiting = false;
        history.Clear();
        GoWiped(Screen.Main);
    }

    static bool AnyButton()
    {
        if (Input.anyKeyDown) return true;
        if (Input.touchCount > 0) return true;
        foreach (var g in Gamepad.all)
        {
            if (g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame ||
                g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame ||
                g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame) return true;
        }
        return false;
    }

    // ================================================================ character select

    static string StatsLine(Fighter f)
    {
        var combat = f.Combat;
        var dmg = new List<string>();
        if (combat != null && combat.Attacks != null)
            foreach (var a in combat.Attacks) if (a != null) dmg.Add(a.Damage.ToString());
        string strikes = dmg.Count > 0 ? string.Join(" / ", dmg) : "-";
        return $"HEALTH {f.MaxHealth}      STAMINA {f.MaxStamina}      ARMOUR {f.Armor}%\nSTRIKES {strikes}";
    }

    static string Tagline(string character)
    {
        if (string.IsNullOrEmpty(character)) return "";
        if (character.StartsWith("Miy", StringComparison.OrdinalIgnoreCase))
            return "Katana. Quick, exact, unforgiving of a wasted swing.";
        if (character.StartsWith("Ric", StringComparison.OrdinalIgnoreCase))
            return "Sword and kite shield. The hardest man to put down.";
        if (character.StartsWith("Bj", StringComparison.OrdinalIgnoreCase))
            return "Dane axe. Slow to start, heavy to stop, longest reach.";
        if (character.StartsWith("Sal", StringComparison.OrdinalIgnoreCase))
            return "Falchion and round shield. His strings flow where others stop.";
        if (character.StartsWith("Gen", StringComparison.OrdinalIgnoreCase))
            return "Two-handed saber. Catch his guard and no man punishes harder.";
        if (character.StartsWith("Buw", StringComparison.OrdinalIgnoreCase))
            return "Spear and hide shield. The longest reach, and he keeps it.";
        return "";
    }

    void ConfirmCharacter()
    {
        // The 1P flow (the split screen never comes here): two locks in sequence - the player's
        // own fighter, then who the computer (or the practice dummy) brings.
        if (!pickingP2 && roster.Length > 2)
        {
            pickingP2 = true;
            SetTitle("Choose Your Fighter", flow == Flow.Practice ? "now who the dummy will be" : "now who the computer brings");
            // The camera swings to the second cursor by the short way round.
            targA1 = camA1 + Mathf.DeltaAngle(camA1, CircleAngleOf(p2Character));
            RefreshCircleCards();
            return;
        }
        ApplySides(p1Character, p2Character);
        GoWiped(Screen.ArenaSelect);
    }

    // ================================================================ arena select

    void BuildArenaSelect()
    {
        var dir = ArenaDirectory.Instance;
        int count = dir != null ? dir.Selectable.Count : 0;
        arenaChoice = count > 0 ? Mathf.Clamp(arenaChoice, 0, count - 1) : 0;
        RefreshArena();
    }

    void RefreshArena()
    {
        var dir = ArenaDirectory.Instance;
        int count = dir != null ? dir.Selectable.Count : 0;
        arenaName.text = MenuArt.Track(count > 0 ? dir.NameOf(arenaChoice).ToUpperInvariant() : "NO ARENA", 2);
        arenaBlurb.text = count > 0 ? dir.BlurbOf(arenaChoice) : "This build ships no selectable arena.";
        arenaCount.text = count > 1 ? $"{arenaChoice + 1} of {count}" : "";

        foreach (Transform child in arenaPips) Destroy(child.gameObject);
        for (int i = 0; i < count; i++)
        {
            var pip = MenuArt.Img(arenaPips, "Pip" + i, MenuArt.Diamond, i == arenaChoice ? MenuArt.BronzeLit : MenuArt.Fade(MenuArt.Bronze, 0.3f));
            MenuArt.Box(pip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(i == arenaChoice ? 16f : 11f, i == arenaChoice ? 16f : 11f),
                        new Vector2((i - (count - 1) * 0.5f) * 34f, 0f));
        }
    }

    void MoveArena(int dir)
    {
        var d = ArenaDirectory.Instance;
        int count = d != null ? d.Selectable.Count : 0;
        if (count <= 1) return;
        arenaChoice = (arenaChoice + (dir > 0 ? 1 : count - 1)) % count;
        // Switch the real arena, so choosing one means looking at it. Not remembered yet: that
        // happens when the choice is confirmed.
        d.Select(arenaChoice, remember: false);
        StageFighters(false, true);        // the new floor may sit at a different height
        RefreshArena();
    }

    void ConfirmArena()
    {
        if (flow == Flow.Practice) { GoWiped(Screen.PracticeRules); return; }
        if (playerCount == 1) { GoWiped(Screen.Difficulty); return; }
        vsAi = false;
        GoWiped(Screen.Vs);
    }

    // ================================================================ navigation

    void MoveSelection(int dir, bool sound = true)
    {
        if (items.Count == 0) return;
        if (sound) CrucibleAudio.Ui(CrucibleAudio.UiSound.Navigate);

        // Screens that are nearly all body copy (How to Fight, the move list) have one or two
        // selectable rows, so following the selection would never scroll them. Scroll the view.
        int selectable = 0;
        foreach (var it in items) if (!it.header) selectable++;
        if (selectable <= 2 && items.Count > visibleRows)
        {
            scroll = Mathf.Clamp(scroll + dir, 0, items.Count - visibleRows);
            Refresh();
            return;
        }

        int n = items.Count;
        int i = selected;
        for (int k = 0; k < n; k++)
        {
            i = (i + dir + n) % n;
            if (!items[i].header) { selected = i; break; }
        }
        if (selected < scroll) scroll = selected;
        if (selected >= scroll + visibleRows) scroll = selected - visibleRows + 1;
        if (selected == FirstSelectable()) scroll = 0;
        scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, items.Count - visibleRows));
        Refresh();
    }

    int FirstSelectable()
    {
        for (int i = 0; i < items.Count; i++) if (!items[i].header) return i;
        return 0;
    }

    void Adjust(int dir)
    {
        if (screen == Screen.ArenaSelect) { CrucibleAudio.Ui(CrucibleAudio.UiSound.Navigate); MoveArena(dir); return; }
        if (selected < 0 || selected >= items.Count) return;
        if (items[selected].adjust != null) CrucibleAudio.Ui(CrucibleAudio.UiSound.Navigate);
        items[selected].adjust?.Invoke(dir);
        Refresh();
    }

    void Confirm()
    {
        if (screen == Screen.ArenaSelect) { CrucibleAudio.Ui(CrucibleAudio.UiSound.Select); ConfirmArena(); return; }
        if (selected < 0 || selected >= items.Count) return;
        if (items[selected].confirm != null) CrucibleAudio.Ui(CrucibleAudio.UiSound.Select);
        items[selected].confirm?.Invoke();
    }

    void Back()
    {
        if (screen == Screen.CharSelect && pickingP2)   // back to Player 1 before leaving the screen
        {
            pickingP2 = false;
            targA1 = camA1 + Mathf.DeltaAngle(camA1, CircleAngleOf(p1Character));
            SetTitle("Choose Your Fighter", "Player 1 picks first");
            RefreshCircleCards();
            return;
        }

        switch (screen)
        {
            case Screen.MatchOver:
                // Backing out of the confirmation question is a "No"; the card itself has no back.
                if (confirmAsk != null) { CrucibleAudio.Ui(CrucibleAudio.UiSound.Back); CancelMatchOverAsk(); }
                return;
            case Screen.Main:
                return;
            case Screen.Pause:
                // Deliberately NOT a resume: Circle is the parry button, and a stray press used to
                // throw you back into the fight. Options / Escape resumes (handled in Update).
                return;
        }

        CrucibleAudio.Ui(CrucibleAudio.UiSound.Back);
        if (history.Count > 0)
        {
            var back = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            Show(back, push: false);
            return;
        }
        Show(inMatch ? Screen.Pause : Screen.Main, push: false);
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ================================================================ K.O. confirmation

    /// <summary>Turns the K.O. card into one question with Yes / No before <paramref name="yes"/>
    /// runs - every way off that card throws the result away, so each asks first.</summary>
    void AskMatchOver(string question, Action yes)
    {
        confirmAsk = question;
        confirmYes = yes;
        Show(Screen.MatchOver, push: false);
    }

    void CancelMatchOverAsk()
    {
        confirmAsk = null;
        confirmYes = null;
        Show(Screen.MatchOver, push: false);
    }

    // ================================================================ game record

    static string Clock(float s) => $"{(int)(Mathf.Max(0f, s) / 60f)}:{Mathf.Max(0f, s) % 60f:00.0}";

    // ================================================================ 2P device choice

    /// <summary>Sensible defaults when the screen opens: two pads = one each, one pad = keyboard
    /// and pad (the game's historic P1 keyboard / P2 pad split), none = two keyboards.</summary>
    void DefaultDeviceChoices()
    {
        int pads = UnityEngine.InputSystem.Gamepad.all.Count;
        if (pads >= 2) { p1Device = 0; p2Device = 1; }
        else if (pads == 1) { p1Device = -1; p2Device = 0; }
        else { p1Device = -1; p2Device = -1; }
    }

    string DeviceName(int player)
    {
        int choice = player == 1 ? p1Device : p2Device;
        var all = UnityEngine.InputSystem.Gamepad.all;
        if (choice < 0 || choice >= all.Count) return "Keyboard";
        return $"{all[choice].displayName}  (pad {choice + 1})";
    }

    /// <summary>Cycles a player through Keyboard and every connected pad, skipping the pad the
    /// OTHER player holds. Landing on a pad pulses its rumble - that is how two identical
    /// controllers (or a live pad beside its Bluetooth ghost) tell themselves apart.</summary>
    void CycleDevice(int player, int dir)
    {
        var all = UnityEngine.InputSystem.Gamepad.all;
        int other = player == 1 ? p2Device : p1Device;
        int choice = player == 1 ? p1Device : p2Device;
        int options = all.Count + 1;                       // keyboard + each pad
        for (int step = 0; step < options; step++)
        {
            choice += dir >= 0 ? 1 : -1;
            if (choice >= all.Count) choice = -1;          // wrap past the last pad to keyboard
            else if (choice < -1) choice = all.Count - 1;  // wrap back from keyboard to the last pad
            if (choice < 0 || choice != other) break;      // keyboard is shareable; a pad is not
        }
        if (player == 1) p1Device = choice; else p2Device = choice;
        if (choice >= 0 && choice < all.Count && InputManager.Instance != null)
            InputManager.Instance.IdentifyPulse(all[choice]);
        Refresh();
    }

    /// <summary>Pins InputManager to the chosen devices for the match (and any rematches).</summary>
    void ApplyDeviceChoices()
    {
        var im = InputManager.Instance;
        if (im == null) return;
        var all = UnityEngine.InputSystem.Gamepad.all;
        // A pad unplugged while the screen was up: fall back to the keyboard rather than a ghost.
        if (p1Device >= all.Count) p1Device = -1;
        if (p2Device >= all.Count) p2Device = -1;
        if (p1Device >= 0 && p1Device == p2Device) p2Device = -1;
        im.SetPlayerDevice(1, p1Device < 0 ? null : all[p1Device]);
        im.SetPlayerDevice(2, p2Device < 0 ? null : all[p2Device]);
    }

    /// <summary>
    /// Drives the replay while the Game Record screen is up. Returns true while a video is being
    /// written: the menu is hidden then (anything drawn would be baked into the video) and input
    /// must land nowhere.
    /// </summary>
    bool UpdateReplay()
    {
        var rec = ReplayRecorder.Instance;
        if (rec == null) return false;
        if (rec.Exporting) { exportingUi = true; return true; }
        if (exportingUi)
        {
            // the export just finished: bring the menu back and say where the file went
            exportingUi = false;
            content.gameObject.SetActive(true);
            subtitle.text = rec.ExportError != null ? "Export failed - " + rec.ExportError
                          : "Saved to " + rec.ExportResult;
        }
        if (!rec.PlaybackActive) return false;
        if (replayPlaying)
        {
            replayTime += Time.unscaledDeltaTime;
            if (replayTime > trimEnd) replayTime = trimStart;      // loop the cut
        }
        rec.Apply(replayTime);
        Refresh();                                                 // the clock readout ticks live
        return false;
    }

    /// <summary>Puts the playhead a moment before the chosen event marker and lets it play.</summary>
    void JumpToMarker(ReplayRecorder rec)
    {
        replayTime = Mathf.Clamp(rec.MarkerTime(replayMarker) - 1.5f, 0f, rec.Duration);
        replayPlaying = true;
    }

    void BeginVideoExport() => BeginSegmentsExport(null);

    /// <summary>Starts an export - the trimmed cut when <paramref name="segments"/> is null, or
    /// the given cuts stitched into one video (the highlight reel).</summary>
    void BeginSegmentsExport(System.Collections.Generic.List<Vector2> segments)
    {
        var rec = ReplayRecorder.Instance;
        if (rec == null || rec.Exporting || !rec.PlaybackActive) return;
        replayPlaying = false;
        // The export reads frames straight off the screen, so the whole menu disappears while it
        // runs; UpdateReplay brings it back and reports the file when Exporting drops.
        content.gameObject.SetActive(false);
        exportingUi = true;
        if (segments != null) rec.BeginExport(segments);
        else rec.BeginExport(trimStart, trimEnd);
    }

    /// <summary>
    /// Loads a saved fight from the library: the recorded pair takes the arena, the record's
    /// arena goes up behind them, and the ordinary Game Record screen plays it. Leaving the
    /// records area puts the selected pair back (the teardown in Show).
    /// </summary>
    void OpenRecord(ReplayRecorder.RecordInfo r)
    {
        int ia = -1, ib = -1;
        for (int i = 0; i < roster.Length; i++)
        {
            if (roster[i] == null) continue;
            if (ia < 0 && roster[i].CharacterName == r.castA) ia = i;
            else if (ib < 0 && roster[i].CharacterName == r.castB) ib = i;
        }
        if (ia >= 0 && ib < 0 && r.castA == r.castB) ib = ia;   // a mirror match: the twin plays Player 2
        if (ia < 0 || ib < 0) { subtitle.text = "A fighter in this record is not in this build."; return; }

        var dir = ArenaDirectory.Instance;
        if (dir != null && !string.IsNullOrEmpty(r.arena))
        {
            var sel = dir.Selectable;
            for (int i = 0; i < sel.Count; i++)
                if (sel[i].displayName == r.arena) { dir.Select(i, remember: false); break; }
        }

        ApplySides(ia, ib);
        FreezeFighters(true);
        if (Rec.LoadRecord(r.path, Everyone(), Camera.main, out string err)) GoWiped(Screen.Replay);
        else subtitle.text = "Could not load the record - " + err;
    }

    // ================================================================ rebinding

    void BeginCapture(int player, GameAction a)
    {
        capturing = true;
        capturePlayer = player;
        captureAction = (int)a;
        captureText.text = $"Press a key or controller button for\n\nPLAYER {player}   {ControlBindings.ActionLabel(a)}\n\n(Esc cancels)";
        captureBack.gameObject.SetActive(true);
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
        captureBack.gameObject.SetActive(false);
        inputGuard = 0.2f;
        Refresh();
    }

    // ================================================================ canvas

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
        canvasRect = (RectTransform)go.transform;

        content = MenuArt.Rect(canvasRect, "Content");
        MenuArt.Stretch(content);

        BuildChrome();
        BuildBootView();
        BuildSelectView();
        BuildArenaView();
        BuildVsView();

        stage = go.AddComponent<MenuStage>();
        stage.Build(canvasRect);

        captureBack = MenuArt.Rect(canvasRect, "Capture");
        MenuArt.Stretch(captureBack);
        var capFill = MenuArt.Img(captureBack, "Fill", MenuArt.Solid, MenuArt.Fade(MenuArt.Ink, 0.96f));
        MenuArt.Stretch(capFill.rectTransform);
        captureText = MenuArt.Shadowed(captureBack, "Text", "", 34, TextAnchor.MiddleCenter, MenuArt.BronzeLit, FontStyle.Bold);
        MenuArt.Stretch(captureText.rectTransform);
        captureText.horizontalOverflow = HorizontalWrapMode.Wrap;
        captureBack.SetAsLastSibling();
        captureBack.gameObject.SetActive(false);

        content.SetSiblingIndex(1);      // between the stage's back and front layers
    }

    /// <summary>
    /// The header, the slab and the footer. Header and footer live OUTSIDE the slab, so the select
    /// screens - which have no slab, because the live arena is behind them - still get a title and
    /// a hint line in the same place.
    /// </summary>
    void BuildChrome()
    {
        header = MenuArt.Rect(content, "Header");
        MenuArt.Place(header, 0f, 1f, 1f, 1f, new Vector2(0f, -210f), new Vector2(0f, 0f));

        // The logo art is a 16:9 plate with the wordmark floating in the middle of a lot of black,
        // so the title crops to the wordmark itself - otherwise most of the header is padding and
        // the words run off the top of the screen.
        titleLogo = MenuArt.Raw(header, "TitleLogo", MenuArt.Logo, Color.white);
        // Measured off the art: the wordmark occupies x 138-1796, y 390-750 of the 1920x1080
        // plate, so the crop (plus a small margin) is 4.42:1 and the rect has to match it.
        titleLogo.uvRect = new Rect(0.055f, 0.292f, 0.895f, 0.360f);
        MenuArt.Box(titleLogo.rectTransform, new Vector2(0.5f, 1f), new Vector2(860f, 195f), new Vector2(0f, -116f));

        title = MenuArt.Shadowed(header, "Title", "", 58, TextAnchor.MiddleCenter, MenuArt.BronzeLit, FontStyle.Bold);
        MenuArt.Place(title.rectTransform, 0f, 1f, 1f, 1f, new Vector2(40, -118), new Vector2(-40, -40));

        divider = MenuArt.Divider(header, "Divider", MenuArt.Bronze, 540f);
        divider.anchorMin = divider.anchorMax = new Vector2(0.5f, 1f);
        divider.anchoredPosition = new Vector2(0f, -130f);

        subtitle = MenuArt.Label(header, "Subtitle", "", 23, TextAnchor.MiddleCenter, MenuArt.Dim, FontStyle.Italic);
        MenuArt.Place(subtitle.rectTransform, 0f, 1f, 1f, 1f, new Vector2(40, -176), new Vector2(-40, -142));

        panel = MenuArt.Rect(content, "Panel");
        MenuArt.Box(panel, new Vector2(0.5f, 0.5f), new Vector2(1380f, 620f), new Vector2(0f, -56f));

        var slab = MenuArt.Img(panel, "Slab", MenuArt.Panel, new Color(0.15f, 0.125f, 0.105f, 0.97f), Image.Type.Sliced);
        MenuArt.Stretch(slab.rectTransform);
        var sink = MenuArt.Img(panel, "Sink", MenuArt.FadeDown, MenuArt.Fade(Color.black, 0.45f));
        MenuArt.Stretch(sink.rectTransform);
        var frame = MenuArt.Img(panel, "Frame", MenuArt.Frame, Color.white, Image.Type.Sliced);
        MenuArt.Stretch(frame.rectTransform, -6f);

        footer = MenuArt.Rect(content, "Footer");
        MenuArt.Place(footer, 0f, 0f, 1f, 0f, new Vector2(0f, 16f), new Vector2(0f, 96f));
        var footRule = MenuArt.Img(footer, "FootRule", MenuArt.Rule, MenuArt.Fade(MenuArt.Bronze, 0.45f));
        MenuArt.Place(footRule.rectTransform, 0.5f, 1f, 0.5f, 1f, new Vector2(-320f, -8f), new Vector2(320f, 0f));
        footerText = MenuArt.Shadowed(footer, "Text", "", 19, TextAnchor.MiddleCenter, MenuArt.Dim);
        MenuArt.Place(footerText.rectTransform, 0f, 0f, 1f, 0f, new Vector2(40, 18), new Vector2(-40, 56));

        for (int i = 0; i < RowPool; i++) rows.Add(BuildRow(i));
    }

    /// <summary>
    /// Sizes the slab to the rows it actually holds: a six-item main menu in a fixed 920-tall box
    /// is mostly empty box.
    /// </summary>
    void LayoutPanel()
    {
        int shown = Mathf.Min(items.Count, visibleRows);
        float height = Mathf.Clamp(shown * rowHeight + 64f, 180f, 640f);
        panel.sizeDelta = new Vector2(1380f, height);
        // The record screen's controls sit low, so the replay playing behind them stays in view.
        panel.anchoredPosition = new Vector2(0f, screen == Screen.Replay ? -540f + height * 0.5f + 48f : -56f);
    }

    Row BuildRow(int index)
    {
        var row = new Row();
        row.rt = MenuArt.Rect(panel, "Row" + index);
        row.go = row.rt.gameObject;

        row.back = row.go.AddComponent<Image>();
        row.back.sprite = MenuArt.Solid;
        row.back.color = Color.clear;
        row.back.raycastTarget = true;

        var btn = row.go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        // No EventSystem navigation: this menu tracks its own selection and draws its own gem.
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        int rowIndex = index;
        btn.onClick.AddListener(() =>
        {
            int idx = scroll + rowIndex;
            // Clicking a Selectable leaves it selected in the EventSystem, and the next Enter would
            // then fire THIS row again - whatever it happens to say on the screen you moved on to.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (idx < items.Count && !items[idx].header) { selected = idx; Refresh(); Confirm(); }
        });

        row.gem = MenuArt.Img(row.rt, "Gem", MenuArt.Diamond, Color.clear);
        MenuArt.Box(row.gem.rectTransform, new Vector2(0f, 0.5f), new Vector2(12f, 12f), new Vector2(26f, 0f));

        row.label = MenuArt.Label(row.rt, "Label", "", 28, TextAnchor.MiddleLeft, MenuArt.Bone);
        MenuArt.Place(row.label.rectTransform, 0f, 0f, 0.62f, 1f, new Vector2(48, 0), new Vector2(0, 0));

        row.value = MenuArt.Label(row.rt, "Value", "", 24, TextAnchor.MiddleRight, MenuArt.Bronze);
        MenuArt.Place(row.value.rectTransform, 0.6f, 0f, 0.86f, 1f, new Vector2(0, 0), new Vector2(0, 0));

        row.value2 = MenuArt.Label(row.rt, "Value2", "", 24, TextAnchor.MiddleRight, MenuArt.Bronze);
        MenuArt.Place(row.value2.rectTransform, 0.86f, 0f, 1f, 1f, new Vector2(0, 0), new Vector2(-26, 0));

        return row;
    }

    void LayoutRows()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            float top = -32f - i * rowHeight;
            MenuArt.Place(rows[i].rt, 0f, 1f, 1f, 1f, new Vector2(54, top - rowHeight + 6f), new Vector2(-54, top));
            rows[i].label.fontSize = Mathf.RoundToInt(rowFont);
            rows[i].value.fontSize = Mathf.RoundToInt(rowFont * 0.86f);
            rows[i].value2.fontSize = Mathf.RoundToInt(rowFont * 0.86f);
        }
    }

    void Refresh()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            int idx = scroll + i;
            var row = rows[i];
            bool has = i < visibleRows && idx < items.Count;
            if (row.go.activeSelf != has) row.go.SetActive(has);
            if (!has) continue;

            var it = items[idx];
            bool sel = idx == selected && !it.header;

            row.label.text = it.label;
            row.value.text = it.value != null ? it.value() : "";
            row.value2.text = it.value2 != null ? it.value2() : "";
            if (it.adjust != null && sel) row.value.text = "<   " + row.value.text + "   >";

            bool sectionHead = it.header && !it.quiet && !string.IsNullOrEmpty(it.label);
            row.label.color = it.header
                            ? (sectionHead ? MenuArt.Bronze : MenuArt.Fade(MenuArt.Bone, 0.72f))
                            : (sel ? MenuArt.BronzeLit : MenuArt.Bone);
            row.label.fontStyle = sectionHead ? FontStyle.Bold : FontStyle.Normal;
            row.label.fontSize = Mathf.RoundToInt(it.quiet ? rowFont * 0.95f : rowFont);
            row.value.color = sel ? MenuArt.BronzeLit : MenuArt.Fade(MenuArt.Bronze, 0.8f);
            row.value2.color = row.value.color;
            row.gem.color = sel ? MenuArt.BronzeLit : Color.clear;
            row.back.color = sel ? MenuArt.Fade(MenuArt.Blood, 0.62f) : Color.clear;
            row.back.raycastTarget = !it.header;
        }
    }

    /// <summary>Rows slide in from the left in sequence, and the selected one breathes.</summary>
    void AnimateRows(float dt)
    {
        if (screen == Screen.None || screen == Screen.Boot || screen == Screen.Vs) return;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!row.go.activeSelf) continue;
            float delay = i * 0.028f;
            float target = Mathf.Clamp01((screenAge - delay) / 0.2f);
            row.slide = Mathf.Max(row.slide, target);
            float e = MenuArt.EaseOut(row.slide);
            var p = row.rt.anchoredPosition;
            row.rt.anchoredPosition = new Vector2(Mathf.Lerp(-40f, 0f, e), p.y);

            int idx = scroll + i;
            if (idx == selected && idx < items.Count && !items[idx].header)
            {
                float pulse = 0.52f + 0.1f * Mathf.Sin(Time.unscaledTime * 4.2f);
                row.back.color = MenuArt.Fade(MenuArt.Blood, pulse * e);
                row.gem.rectTransform.anchoredPosition = new Vector2(26f + Mathf.Sin(Time.unscaledTime * 4.2f) * 3f, 0f);
            }
            else if (row.back.color.a > 0f && e >= 1f)
            {
                var c = row.back.color;
                row.back.color = MenuArt.Fade(c, Mathf.Min(c.a, 0f));
            }
        }
    }

    // ================================================================ custom views

    void BuildBootView()
    {
        bootView = MenuArt.Rect(content, "Boot");
        MenuArt.Stretch(bootView);

        if (MenuArt.Logo != null)
        {
            // Full bleed: the logo art has an opaque near-black background, so at any smaller size
            // its edge draws a visible rectangle against the field behind it.
            bootLogo = MenuArt.Raw(bootView, "Logo", MenuArt.Logo, MenuArt.Fade(Color.white, 0f));
            MenuArt.Box(bootLogo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1920f, 1080f), Vector2.zero);
        }
        else
        {
            bootTitle = MenuArt.Shadowed(bootView, "Title", MenuArt.Track("CRUCIBLE", 3), 120, TextAnchor.MiddleCenter, MenuArt.Fade(MenuArt.BronzeLit, 0f), FontStyle.Bold);
            MenuArt.Box(bootTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1600f, 180f), new Vector2(0f, 90f));
            var sub = MenuArt.Label(bootView, "Sub", MenuArt.Track("HONOURABLE SOULS", 3), 34, TextAnchor.MiddleCenter, MenuArt.Bronze);
            MenuArt.Box(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1600f, 60f), new Vector2(0f, -10f));
        }

        bootPrompt = MenuArt.Shadowed(bootView, "Prompt", "", 30, TextAnchor.MiddleCenter, MenuArt.Bone);
        MenuArt.Box(bootPrompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(1200f, 60f), new Vector2(0f, 150f));

        bootView.gameObject.SetActive(false);
    }

    void BuildSelectView()
    {
        selectView = MenuArt.Rect(content, "Select");
        MenuArt.Stretch(selectView);

        cards = new CharacterCard[Mathf.Max(2, roster.Length)];
        for (int i = 0; i < cards.Length; i++) cards[i] = BuildPlate(selectView, "Card" + i);

        // The seam between the two halves of the split select.
        splitDivider = MenuArt.Img(selectView, "Divider", MenuArt.Solid, MenuArt.Fade(MenuArt.Bronze, 0.75f));
        // Stops under the header so it never cuts through the title.
        MenuArt.Box(splitDivider.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(4f, 860f), new Vector2(0f, -110f));
        splitDivider.gameObject.SetActive(false);

        selectView.gameObject.SetActive(false);
    }

    /// <summary>A plate: tag, name, one line of character, and the numbers that matter.</summary>
    CharacterCard BuildPlate(RectTransform parent, string name)
    {
        var card = new CharacterCard();
        card.rt = MenuArt.Rect(parent, name);
        MenuArt.Box(card.rt, new Vector2(0.5f, 0.5f), new Vector2(470f, 142f), Vector2.zero);

        card.slab = MenuArt.Img(card.rt, "Slab", MenuArt.Panel, new Color(0.11f, 0.09f, 0.08f, 0.9f), Image.Type.Sliced);
        MenuArt.Stretch(card.slab.rectTransform);
        card.frame = MenuArt.Img(card.rt, "Frame", MenuArt.Frame, MenuArt.Bronze, Image.Type.Sliced);
        MenuArt.Stretch(card.frame.rectTransform, -4f);

        card.tag = MenuArt.Img(card.rt, "Tag", MenuArt.Solid, MenuArt.P1);
        MenuArt.Box(card.tag.rectTransform, new Vector2(0.5f, 1f), new Vector2(180f, 30f), new Vector2(0f, 14f));
        card.tagText = MenuArt.Label(card.tag.rectTransform, "TagText", "", 19, TextAnchor.MiddleCenter, MenuArt.Ink, FontStyle.Bold);
        MenuArt.Stretch(card.tagText.rectTransform);

        card.name = MenuArt.Shadowed(card.rt, "Name", "", 36, TextAnchor.MiddleCenter, MenuArt.BronzeLit, FontStyle.Bold);
        MenuArt.Place(card.name.rectTransform, 0f, 1f, 1f, 1f, new Vector2(0f, -54f), new Vector2(0f, -8f));

        card.tagline = MenuArt.Label(card.rt, "Tagline", "", 18, TextAnchor.UpperCenter, MenuArt.Dim, FontStyle.Italic);
        MenuArt.Place(card.tagline.rectTransform, 0f, 1f, 1f, 1f, new Vector2(14f, -82f), new Vector2(-14f, -54f));
        card.tagline.horizontalOverflow = HorizontalWrapMode.Wrap;

        card.stats = MenuArt.Label(card.rt, "Stats", "", 17, TextAnchor.UpperCenter, MenuArt.Fade(MenuArt.Bone, 0.88f));
        MenuArt.Place(card.stats.rectTransform, 0f, 1f, 1f, 1f, new Vector2(10f, -136f), new Vector2(-10f, -84f));

        return card;
    }

    void BuildCharSelect()
    {
        if (roster.Length == 0) return;
        p1Character = Mathf.Clamp(p1Character, 0, roster.Length - 1);
        p2Character = Mathf.Clamp(p2Character, 0, roster.Length - 1);
        RefreshCircleCards();
    }

    /// <summary>
    /// The circle select shows ONE plate per cursor, fixed to its half of the screen (split) or
    /// bottom-centre (1P) - the camera frames one fighter at a time, so a plate per roster slot
    /// has nothing to stand under any more.
    /// </summary>
    void RefreshCircleCards()
    {
        foreach (var c in cards) if (c != null) c.rt.gameObject.SetActive(false);
        if (roster.Length == 0 || cards.Length == 0) return;

        if (splitSelect)
        {
            FixPlate(cards[0], p1Character, lock1 ? "PLAYER 1  READY" : "PLAYER 1", MenuArt.P1,
                     new Vector2(-480f, -385f), lock1);
            if (cards.Length > 1)
                FixPlate(cards[1], p2Character, lock2 ? "PLAYER 2  READY" : "PLAYER 2", MenuArt.P2,
                         new Vector2(480f, -385f), lock2);
        }
        else
        {
            string tag = pickingP2 ? (flow == Flow.Practice ? "DUMMY" : "COMPUTER") : "PLAYER 1";
            FixPlate(cards[0], pickingP2 ? p2Character : p1Character, tag,
                     pickingP2 ? MenuArt.P2 : MenuArt.P1, new Vector2(0f, -385f), false);
        }
    }

    void FixPlate(CharacterCard card, int rosterIndex, string tag, Color tagColor, Vector2 at, bool locked)
    {
        if (card == null) return;
        SetPlate(card, rosterIndex, tag, tagColor);
        card.follow = null;                              // fixed to its half, never tracked
        card.rt.anchoredPosition = at;
        card.frame.color = locked ? tagColor : MenuArt.BronzeLit;   // a locked plate wears its player's colour
    }

    void BuildArenaView()
    {
        arenaView = MenuArt.Rect(content, "Arena");
        MenuArt.Stretch(arenaView);

        var plate = MenuArt.Rect(arenaView, "Plate");
        MenuArt.Box(plate, new Vector2(0.5f, 0f), new Vector2(1060f, 228f), new Vector2(0f, 206f));
        var slab = MenuArt.Img(plate, "Slab", MenuArt.Panel, new Color(0.14f, 0.115f, 0.1f, 0.96f), Image.Type.Sliced);
        MenuArt.Stretch(slab.rectTransform);
        var frame = MenuArt.Img(plate, "Frame", MenuArt.Frame, MenuArt.Bronze, Image.Type.Sliced);
        MenuArt.Stretch(frame.rectTransform, -5f);

        arenaName = MenuArt.Shadowed(plate, "Name", "", 56, TextAnchor.MiddleCenter, MenuArt.BronzeLit, FontStyle.Bold);
        MenuArt.Place(arenaName.rectTransform, 0f, 0.5f, 1f, 0.5f, new Vector2(40f, 20f), new Vector2(-40f, 84f));

        var rule = MenuArt.Divider(plate, "Rule", MenuArt.Bronze, 420f);
        rule.anchorMin = rule.anchorMax = new Vector2(0.5f, 0.5f);
        rule.anchoredPosition = new Vector2(0f, 8f);

        arenaBlurb = MenuArt.Label(plate, "Blurb", "", 24, TextAnchor.MiddleCenter, MenuArt.Bone, FontStyle.Italic);
        MenuArt.Place(arenaBlurb.rectTransform, 0f, 0.5f, 1f, 0.5f, new Vector2(40f, -44f), new Vector2(-40f, -6f));

        arenaCount = MenuArt.Label(plate, "Count", "", 20, TextAnchor.MiddleCenter, MenuArt.Dim);
        MenuArt.Place(arenaCount.rectTransform, 0f, 0f, 1f, 0f, new Vector2(0f, 16f), new Vector2(0f, 46f));

        var leftArrow = MenuArt.Label(arenaView, "Left", "<", 72, TextAnchor.MiddleCenter, MenuArt.Fade(MenuArt.Bronze, 0.8f), FontStyle.Bold);
        MenuArt.Box(leftArrow.rectTransform, new Vector2(0.5f, 0f), new Vector2(90f, 90f), new Vector2(-610f, 206f));
        var rightArrow = MenuArt.Label(arenaView, "Right", ">", 72, TextAnchor.MiddleCenter, MenuArt.Fade(MenuArt.Bronze, 0.8f), FontStyle.Bold);
        MenuArt.Box(rightArrow.rectTransform, new Vector2(0.5f, 0f), new Vector2(90f, 90f), new Vector2(610f, 206f));

        arenaPips = MenuArt.Rect(arenaView, "Pips");
        MenuArt.Box(arenaPips, new Vector2(0.5f, 0f), new Vector2(400f, 30f), new Vector2(0f, 352f));

        arenaView.gameObject.SetActive(false);
    }

    void BuildVsView()
    {
        vsView = MenuArt.Rect(content, "Vs");
        MenuArt.Stretch(vsView);

        vsCards = new CharacterCard[2];
        for (int i = 0; i < 2; i++) vsCards[i] = BuildPlate(vsView, "VsCard" + i);

        vsWord = MenuArt.Shadowed(vsView, "Word", "VS", 190, TextAnchor.MiddleCenter, MenuArt.BloodLit, FontStyle.Bold);
        MenuArt.Box(vsWord.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(600f, 240f), new Vector2(0f, 40f));

        vsView.gameObject.SetActive(false);
    }

    void SetPlate(CharacterCard card, int rosterIndex, string tag, Color tagColor)
    {
        if (card == null || rosterIndex < 0 || rosterIndex >= roster.Length) return;
        card.rt.gameObject.SetActive(true);
        card.follow = roster[rosterIndex].transform;
        card.name.text = MenuArt.Track(roster[rosterIndex].CharacterName.ToUpperInvariant(), 1);
        card.name.color = MenuArt.BronzeLit;
        card.tagText.text = tag;
        card.tag.color = tagColor;
        card.frame.color = MenuArt.BronzeLit;
        card.slab.color = new Color(0.11f, 0.09f, 0.08f, 0.9f);
        card.stats.text = StatsLine(roster[rosterIndex]);
        card.tagline.text = Tagline(roster[rosterIndex].CharacterName);
        card.rt.localScale = Vector3.one;
    }

    // ================================================================ versus card

    IEnumerator VsRoutine()
    {
        ApplySides(p1Character, p2Character);
        StageFighters(true);                       // ApplySides may have swapped which side is whose

        SetPlate(vsCards[0], p1Character, "PLAYER 1", MenuArt.P1);
        SetPlate(vsCards[1], p2Character,
                 playerCount >= 2 && !vsAi ? "PLAYER 2" : flow == Flow.Practice ? "DUMMY" : "COMPUTER",
                 MenuArt.P2);
        vsCards[1].follow = P2Fighter.transform;   // SetPlate points at the roster rig; a mirror's P2 is the twin

        const float punch = 0.45f;
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / punch)
        {
            float e = MenuArt.EaseOut(k);
            vsWord.color = MenuArt.Fade(MenuArt.BloodLit, e);
            vsWord.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.1f, 1f, e);
            yield return null;
        }
        vsWord.color = MenuArt.BloodLit;
        vsWord.rectTransform.localScale = Vector3.one;

        yield return new WaitForSecondsRealtime(0.95f);
        if (stage != null) stage.Wipe(StartMatch); else StartMatch();
    }

    // ================================================================ staging the real fighters

    /// <summary>
    /// Poses the two fighters for the camera and parks the camera on them, so the select screens
    /// show the actual rigs playing their idle instead of a picture of them. Nothing here is worth
    /// preserving: <see cref="PlaceFighters"/> puts both back on their marks when the match starts.
    /// </summary>
    void StageFighters(bool vs, bool wide = false)
    {
        if (roster.Length < 2 || sideMark.Length < 2) return;
        var cam = Camera.main;
        if (cam == null) return;

        var fightCam = cam.GetComponent<FightCamera>();
        if (fightCam != null) fightCam.enabled = false;     // it would drag the view back every frame

        if (!vs && !wide) { StageCircle(); return; }        // character select: the ring

        Vector3 mid = (sideMark[0] + sideMark[1]) * 0.5f;
        Vector3 camDir = cam.transform.position - mid;      // keep the arena's established viewpoint
        camDir.y = 0f;
        if (camDir.sqrMagnitude < 0.01f) camDir = Vector3.back;
        camDir.Normalize();
        Vector3 screenRight = Vector3.Cross(Vector3.up, -camDir).normalized;

        float spread = vs ? 2.0f : 2.4f;
        float turn = vs ? 60f : 18f;                        // VS squares them up against each other

        // Frame first, pose second: a fighter stood 2.4 units off to the side does not face the
        // camera by facing the stage centre - that error was 31 degrees, a near profile.
        float fov = cam.fieldOfView * Mathf.Deg2Rad;
        float aspect = cam.aspect > 0.1f ? cam.aspect : 16f / 9f;
        // Arena select pulls right back: the thing being chosen is the ground, not the fighters -
        // and it shows ONLY the picked pair now (the user's call, 2026-10-26): once the fighters
        // are chosen, nobody else stands around.
        float wantHeight = wide ? 17f : 5.0f;               // fighters are 2.9 units tall
        float wantWidth = wide ? 34f : spread + 3.6f;
        float distV = (wantHeight * 0.5f) / Mathf.Tan(fov * 0.5f);
        float distH = (wantWidth * 0.5f) / Mathf.Tan(Mathf.Atan(Mathf.Tan(fov * 0.5f) * aspect));
        float dist = Mathf.Max(distV, distH);

        Vector3 look = mid + Vector3.up * (wide ? 2.6f : 1.45f);
        Vector3 camPos = look + camDir * dist + Vector3.up * (wide ? 4.5f : 0.55f);
        cam.transform.position = camPos;
        cam.transform.rotation = Quaternion.LookRotation(look - camPos, Vector3.up);

        // The yaw is applied AFTER LookRotation(toCam), so it turns each fighter off
        // camera-facing - and the SIGN was inverted once. A positive Y rotation carries +Z toward
        // +X, while screenRight works out to -X when the camera sits on +Z, so "+turn" swung
        // the screen-LEFT fighter further left and "-turn" swung the right one further right:
        // both outward, shoulders to the middle, which is the back-to-back look.
        // Negated, P1 (screen left) turns right and P2 turns left.
        PlaceStaged(P1Fighter, mid - screenRight * spread, camPos, -turn);
        PlaceStaged(P2Fighter, mid + screenRight * spread, camPos, turn);    // the twin, in a mirror
        staged = true;
    }

    /// <summary>
    /// The character-select ring (the user's design, 2026-10-26): the whole roster stands in a
    /// circle around the marks' midpoint, each man facing the centre, and the select camera(s)
    /// orbit just off that centre, looking outward - so each half of the screen frames exactly
    /// one fighter, and scrolling sweeps the camera past the others to the next.
    /// </summary>
    void StageCircle()
    {
        ShowWholeRoster();
        Vector3 mid = (sideMark[0] + sideMark[1]) * 0.5f;
        int n = roster.Length;
        float step = 360f / n;
        for (int i = 0; i < n; i++)
            PlaceStaged(roster[i], mid + CircleDir(i * step) * CircleRadius, mid, 0f);   // toCam = the centre
        staged = true;
        UpdateSelectCameras(true);
    }

    float CircleAngleOf(int rosterIndex) => rosterIndex * 360f / Mathf.Max(roster.Length, 1);

    /// <summary>Eases each select camera toward its target fighter and parks it on the inner ring.</summary>
    void UpdateSelectCameras(bool snap)
    {
        var cam = Camera.main;
        if (cam == null || sideMark.Length < 2) return;
        Vector3 mid = (sideMark[0] + sideMark[1]) * 0.5f;
        float ease = snap ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 7f);
        camA1 = Mathf.Lerp(camA1, targA1, ease);
        camA2 = Mathf.Lerp(camA2, targA2, ease);
        PlaceSelectCam(cam, mid, camA1, splitSelect && lock1);
        if (splitSelect && selectCam2 != null && selectCam2.enabled)
            PlaceSelectCam(selectCam2, mid, camA2, lock2);
    }

    void PlaceSelectCam(Camera c, Vector3 mid, float angle, bool lockedPush)
    {
        Vector3 dir = CircleDir(angle);
        Vector3 fighterAt = mid + dir * CircleRadius;
        fighterAt.y = GroundedY(fighterAt, mid.y);
        // A locked half steps in on its man - the quiet "you are done" beat before the merge.
        Vector3 pos = mid + dir * (lockedPush ? CircleCamIn + 0.8f : CircleCamIn);
        pos.y = fighterAt.y + 1.55f;
        Vector3 look = fighterAt + Vector3.up * 1.45f;
        c.transform.position = pos;
        c.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
    }

    /// <summary>Entering the select screen: split or single camera, angles on the cursors.</summary>
    void BeginCharSelect()
    {
        splitSelect = playerCount >= 2 && flow != Flow.Practice;
        lock1 = lock2 = false;
        mergingSelect = false;
        camA1 = targA1 = CircleAngleOf(splitSelect || !pickingP2 ? p1Character : p2Character);
        camA2 = targA2 = CircleAngleOf(p2Character);
        var cam = Camera.main;
        if (splitSelect)
        {
            EnsureSelectCam2();
            if (cam != null) cam.rect = new Rect(0f, 0f, 0.5f, 1f);
            selectCam2.enabled = true;
            selectCam2.rect = new Rect(0.5f, 0f, 0.5f, 1f);
            if (splitDivider != null)
            {
                splitDivider.gameObject.SetActive(true);
                splitDivider.rectTransform.anchoredPosition = new Vector2(0f, -110f);
            }
        }
        else RestoreSelectCameras();
        UpdateSelectCameras(true);
        RefreshCircleCards();
    }

    void EnsureSelectCam2()
    {
        if (selectCam2 != null) return;
        var main = Camera.main;
        var go = new GameObject("SelectCam2");
        selectCam2 = go.AddComponent<Camera>();
        if (main != null) selectCam2.CopyFrom(main);        // projection, clear flags, culling
        selectCam2.rect = new Rect(0.5f, 0f, 0.5f, 1f);
    }

    /// <summary>One full-screen camera again, however the split ended.</summary>
    void RestoreSelectCameras()
    {
        var cam = Camera.main;
        if (cam != null) cam.rect = new Rect(0f, 0f, 1f, 1f);
        if (selectCam2 != null) selectCam2.enabled = false;
        if (splitDivider != null) splitDivider.gameObject.SetActive(false);
    }

    /// <summary>The select screen's own input + cameras: per-player in split, the old single cursor in 1P.</summary>
    void UpdateCharSelect()
    {
        UpdateSelectCameras(false);
        if (mergingSelect) return;                          // the merge routine owns the screen
        var im = InputManager.Instance;

        if (splitSelect)
        {
            for (int p = 1; p <= 2; p++)
            {
                bool locked = p == 1 ? lock1 : lock2;
                Vector2 nav = im.MenuNavigateFor(p);        // read every frame: the stick edges latch inside
                if (!locked && nav.x != 0f)
                {
                    CrucibleAudio.Ui(CrucibleAudio.UiSound.Navigate);
                    MoveCursor(p, nav.x > 0f ? 1 : -1);
                }
                if (inputGuard > 0f) continue;
                if (im.MenuConfirmFor(p)) LockPlayer(p);
                else if (im.MenuBackFor(p))
                {
                    if (locked) UnlockPlayer(p);
                    else if (!lock1 && !lock2) { Back(); return; }
                }
            }
            // Escape is the whole-screen back: it unwinds the most recent lock first.
            if (inputGuard <= 0f && Input.GetKeyDown(KeyCode.Escape))
            {
                if (lock2) UnlockPlayer(2);
                else if (lock1) UnlockPlayer(1);
                else Back();
            }
            return;
        }

        Vector2 one = im.MenuNavigate();
        if (one.x != 0f)
        {
            CrucibleAudio.Ui(CrucibleAudio.UiSound.Navigate);
            MoveCursor(pickingP2 ? 2 : 1, one.x > 0f ? 1 : -1);
        }
        if (inputGuard > 0f) return;
        if (im.MenuConfirm()) { CrucibleAudio.Ui(CrucibleAudio.UiSound.Select); ConfirmCharacter(); }
        else if (im.MenuBack()) Back();
    }

    /// <summary>
    /// Moves one player's cursor around the ring, one fighter per step. Nobody is walked past any
    /// more: the other side's pick is fair game, and picking it makes a mirror match. The
    /// camera's target angle ACCUMULATES, so the sweep follows the direction scrolled and the
    /// ring loops without ever snapping the long way round.
    /// </summary>
    void MoveCursor(int player, int dir)
    {
        int n = roster.Length;
        if (n < 2) return;
        int cur = player == 1 ? p1Character : p2Character;
        int next = (cur + dir + n) % n;
        if (player == 1) p1Character = next; else p2Character = next;

        float delta = dir * (360f / n);
        if (splitSelect && player == 2) targA2 += delta;
        else targA1 += delta;
        RefreshCircleCards();
    }

    void LockPlayer(int p)
    {
        if (p == 1 ? lock1 : lock2) return;
        // The same man on both sides is allowed: Player 2 fights his twin in the other colours.
        if (p == 1) lock1 = true; else lock2 = true;
        CrucibleAudio.Ui(CrucibleAudio.UiSound.Select);
        RefreshCircleCards();
        if (lock1 && lock2) StartCoroutine(MergeSelectRoutine());
    }

    void UnlockPlayer(int p)
    {
        if (p == 1) lock1 = false; else lock2 = false;
        CrucibleAudio.Ui(CrucibleAudio.UiSound.Back);
        RefreshCircleCards();
    }

    /// <summary>
    /// Both players locked: hold a beat on the two picks, slide the seam away so the halves
    /// become one view, then on to the arena - where only the chosen pair stands.
    /// </summary>
    IEnumerator MergeSelectRoutine()
    {
        mergingSelect = true;
        yield return new WaitForSecondsRealtime(0.55f);
        var cam = Camera.main;
        const float dur = 0.45f;
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / dur)
        {
            float e = MenuArt.EaseOut(Mathf.Clamp01(k));
            if (cam != null) cam.rect = new Rect(0f, 0f, Mathf.Lerp(0.5f, 1f, e), 1f);
            if (selectCam2 != null) selectCam2.rect = new Rect(Mathf.Lerp(0.5f, 1f, e), 0f, Mathf.Lerp(0.5f, 0f, e), 1f);
            if (splitDivider != null) splitDivider.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(0f, 980f, e), -110f);
            yield return null;
            if (screen != Screen.CharSelect) { mergingSelect = false; yield break; }   // left mid-merge
        }
        mergingSelect = false;
        if (screen != Screen.CharSelect || !lock1 || !lock2) { RestoreSelectCameras(); yield break; }
        ApplySides(p1Character, p2Character);
        GoWiped(Screen.ArenaSelect);                         // Show() restores the single camera
    }

    void PlaceStaged(Fighter f, Vector3 pos, Vector3 camPos, float yaw)
    {
        if (f == null) return;
        pos.y = GroundedY(pos, OwnY(f));                     // on the floor, whatever arena is up
        Vector3 toCam = camPos - pos;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 0.01f) toCam = Vector3.back;
        var rot = Quaternion.LookRotation(toCam.normalized, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
        // A full reset, not just a move: coming here from a K.O. the loser is still dead and face
        // down, and a corpse on the character-select screen then stayed down all the way back to
        // the next match. ResetForMatch also syncs the rigidbody and the transform for us.
        f.ResetForMatch(pos, rot);
    }

    void Unstage()
    {
        staged = false;
        RestoreSelectCameras();
        var cam = Camera.main;
        var fightCam = cam != null ? cam.GetComponent<FightCamera>() : null;
        if (fightCam != null) fightCam.enabled = true;
        // The select screens woke the whole roster; everyone not fighting goes back to the bench,
        // or the extras end up stacked on the two spawn marks, climbing each other - the floating
        // bystanders that haunted the menus until the next fight.
        ApplySides(p1Character, p2Character);
        PlaceFighters();
    }

    /// <summary>Slides each plate under the fighter it belongs to, wherever the camera put him.</summary>
    void TrackPlates(CharacterCard[] set, float y)
    {
        var cam = Camera.main;
        if (cam == null || set == null) return;
        foreach (var card in set)
        {
            if (card == null || card.follow == null || !card.rt.gameObject.activeSelf) continue;
            Vector3 screenPoint = cam.WorldToScreenPoint(card.follow.position);
            if (screenPoint.z < 0f) continue;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out local);
            card.rt.anchoredPosition = new Vector2(Mathf.Clamp(local.x, -640f, 640f), y);
        }
    }

    // ================================================================ move list

    // Which device the move list spells its buttons in. Defaults to the pad when one is plugged
    // in; the row on the Moves screen toggles it. Labels always come from the LIVE bindings, so a
    // rebind shows up here immediately on either device.
    bool movesShowPad;
    bool movesShowPadInit;

    List<(string name, CombatSystem combat, int player)> MovesRoster()
    {
        var list = new List<(string, CombatSystem, int)>();
        foreach (var fighter in Everyone().OrderBy(f => f.PlayerNumber))
        {
            if (fighter == twin && !fighter.gameObject.activeInHierarchy) continue;   // a benched mirror twin
            // Mid-match (and from the pause menu) only the pair actually fighting is listed -
            // ApplySides switched the benched fighters' objects off.
            if (inMatch && !fighter.gameObject.activeInHierarchy) continue;
            var combat = fighter.GetComponent<CombatSystem>();
            if (combat == null) continue;
            list.Add((fighter.CharacterName, combat, fighter.PlayerNumber));
        }
        return list;
    }

    string MovesPageName(int page)
    {
        var r = MovesRoster();
        if (r.Count == 0) return "-";
        var entry = r[Mathf.Clamp(page / 2, 0, r.Count - 1)];
        return entry.name + (page % 2 == 0 ? "  -  moves" : "  -  strings");
    }

    /// <summary>
    /// One page of the move list, walked from the live <see cref="AttackData"/> every time the
    /// screen is built. Only the LONGEST form of each string is listed - a two-hit that can be
    /// extended is shown as the three-hit, since stopping early is always allowed. Mirrors the two
    /// engine rules a walk has to know about: a link marked openerOnly is offered only from the
    /// string's first move, and a string carries at most one special.
    /// </summary>
    IEnumerable<string> MoveLines(int page)
    {
        var r = MovesRoster();
        if (r.Count == 0) { yield return "No fighters in the scene."; yield break; }
        var entry = r[Mathf.Clamp(page / 2, 0, r.Count - 1)];
        var b = InputManager.Instance != null ? InputManager.Instance.Bindings : ControlBindings.Defaults();
        var keys = b.Player(entry.player);

        if (page % 2 == 0)
        {
            yield return $"{entry.name.ToUpperInvariant()}   (Player {entry.player})      damage / stamina / startup-active-recovery";
            yield return "";
            foreach (var attack in Library(entry.combat))
                yield return $"   {Press(keys, attack, movesShowPad),-16} {attack.AttackName,-23} {attack.Damage,2} dmg   " +
                             $"{attack.StaminaCost,2} stam   {attack.StartupFrames}/{attack.ActiveFrames}/{attack.RecoveryFrames}";
            foreach (var rp in Ripostes(entry.combat))
            {
                string next = "";
                var link = rp.attack.ComboLinks != null && rp.attack.ComboLinks.Length > 0 ? rp.attack.ComboLinks[0] : null;
                if (link != null && link.followUp != null)
                    next = "   > " + PressLabel(keys, link.input, link.withSpecialModifier, movesShowPad) + "  " + link.followUp.AttackName;
                yield return $"   {"[" + rp.label + "]",-16} {rp.attack.AttackName,-23} {rp.attack.Damage,2} dmg   " +
                             $"{rp.attack.StaminaCost,2} stam   {rp.attack.StartupFrames}/{rp.attack.ActiveFrames}/{rp.attack.RecoveryFrames}{next}";
            }
            yield return "";
            yield return "   A special costs about 2.5x the stamina of the strike it shares a button with. Any strike";
            yield return "   thrown right after a dodge or a parry is a counter: extra damage, and health back.";
            yield break;
        }

        var strings = new List<(string buttons, string names)>();
        foreach (var opener in Library(entry.combat))
            WalkMoves(opener, keys, Press(keys, opener, movesShowPad), opener.AttackName, 0, opener.IsSpecial, strings);
        yield return $"{entry.name.ToUpperInvariant()}   (Player {entry.player})      {strings.Count} strings";
        yield return "";
        foreach (var line in strings)
            yield return $"   {line.buttons,-32} {line.names}";
        yield return "";
        yield return "   Press the next button before the move finishes; stopping early is always allowed.";
    }

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
                          buttons + " > " + PressLabel(keys, link.input, link.withSpecialModifier, movesShowPad),
                          names + " > " + link.followUp.AttackName,
                          depth + 1, usedSpecial || link.followUp.IsSpecial, into);
            }
        }
        if (!extended && depth > 0) into.Add((buttons, names));
    }

    static IEnumerable<(string label, AttackData attack)> Ripostes(CombatSystem combat)
    {
        if (combat.DashRiposte != null) yield return ("after a dodge", combat.DashRiposte);
        if (combat.ParryRiposte != null) yield return ("after a parry", combat.ParryRiposte);
    }

    static IEnumerable<AttackData> Library(CombatSystem combat)
    {
        foreach (var a in combat.Attacks ?? new AttackData[0]) if (a != null) yield return a;
        foreach (var a in combat.Specials ?? new AttackData[0]) if (a != null) yield return a;
    }

    static string Press(ControlBindings.PlayerBindings keys, AttackData attack, bool pad) =>
        PressLabel(keys, attack.IsSpecial ? attack.SpecialButton : attack.AttackType, attack.IsSpecial, pad);

    static string PressLabel(ControlBindings.PlayerBindings keys, AttackType button, bool special, bool pad)
    {
        GameAction action = button == AttackType.Light ? GameAction.Light
                          : button == AttackType.Medium ? GameAction.Medium : GameAction.Heavy;
        string key = pad ? ControlBindings.PadLabel(keys.Pad(action)) : ControlBindings.KeyLabel(keys.Key(action));
        string mod = pad ? ControlBindings.PadLabel(keys.Pad(GameAction.Special)) : ControlBindings.KeyLabel(keys.Key(GameAction.Special));
        return special ? $"[{mod} + {key}]" : $"[{key}]";
    }

    IEnumerable<string> HelpLines()
    {
        yield return "Walk toward or away from your opponent. Light, Medium and Heavy strikes each have a wind-up,";
        yield return "an active window and a recovery. Heavier strikes hit harder and take longer. Every strike";
        yield return "steps forward a little to reach its target.";
        yield return "";
        yield return "BLOCK    Hold Block to absorb a hit for stamina. Holding drains stamina; an empty bar breaks the guard.";
        yield return "PARRY    Tap Parry just as a strike lands to deflect it: no damage, no stamina lost, and the attacker";
        yield return "         is stunned long enough to punish, with a window to counter for extra damage. It is its own";
        yield return "         button, never the guard: it costs stamina and a second of cooldown, so read the swing.";
        yield return "DASH     Dash key + direction, or flick the right stick. A dash timed against a strike dodges it.";
        yield return "         A side dash circles your opponent so you stay in range. After a clean dodge you have";
        yield return "         a moment to counter for extra damage, and the attacker who missed is stuck recovering.";
        yield return "CLASH    Two strikes of equal weight meeting cancel each other. The heavier strike wins otherwise.";
        yield return "SPECIAL  Hold the Special modifier and press a strike for that character's special version of it:";
        yield return "         three per fighter. They hit harder, cost about two and a half times the stamina, and";
        yield return "         leave a beat afterwards in which you cannot attack. On a phone, tap SP then the strike.";
        yield return "STRINGS  Press the next button before the current move finishes to cancel into it. The Move List";
        yield return "         shows every string each fighter has.";
        yield return "HEALTH   You get a little health back for a dodge, a parry, and for landing the counter they earn.";
        yield return "         Go a few seconds without being hit and you start slowly regenerating.";
        yield return "KO       Empty your opponent's health bar.";
        yield return "";
        var b = InputManager.Instance != null ? InputManager.Instance.Bindings : ControlBindings.Defaults();
        yield return KeyboardLine("P1", b.p1);
        yield return KeyboardLine("P2", b.p2);
        var pp = b.Player(b.firstPadPlayer);
        yield return $"Controller (Player {b.firstPadPlayer}):  move stick/d-pad   light {ControlBindings.PadLabel(pp.Pad(GameAction.Light))}  " +
                     $"medium {ControlBindings.PadLabel(pp.Pad(GameAction.Medium))}  heavy {ControlBindings.PadLabel(pp.Pad(GameAction.Heavy))}   " +
                     $"block {ControlBindings.PadLabel(pp.Pad(GameAction.Block))}  parry {ControlBindings.PadLabel(pp.Pad(GameAction.Defense))}   " +
                     $"special {ControlBindings.PadLabel(pp.Pad(GameAction.Special))}+strike   dash: flick right stick   pause: Options";
    }

    static string KeyboardLine(string who, ControlBindings.PlayerBindings k) =>
        $"{who} keyboard:  move {ControlBindings.KeyLabel(k.Key(GameAction.MoveLeft))}/{ControlBindings.KeyLabel(k.Key(GameAction.MoveRight))}   " +
        $"light {ControlBindings.KeyLabel(k.Key(GameAction.Light))}  medium {ControlBindings.KeyLabel(k.Key(GameAction.Medium))}  " +
        $"heavy {ControlBindings.KeyLabel(k.Key(GameAction.Heavy))}   block {ControlBindings.KeyLabel(k.Key(GameAction.Block))}  " +
        $"parry {ControlBindings.KeyLabel(k.Key(GameAction.Defense))}   special {ControlBindings.KeyLabel(k.Key(GameAction.Special))}+strike   " +
        $"dash {ControlBindings.KeyLabel(k.Key(GameAction.Dash))}";
}
