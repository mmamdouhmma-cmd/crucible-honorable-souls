using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Centralized input reader for both players. Keyboard keys come from <see cref="ControlBindings"/>
/// (legacy KeyCode polling), gamepads from the Input System's <see cref="Gamepad"/> (PS4/PS5 pads work
/// without any extra setup; the first pad drives Player 2 by default, a second pad drives Player 1).
/// Exposes per-player <see cref="InputData"/> snapshots plus the menu/pause inputs the <see cref="GameMenu"/>
/// uses. While <see cref="InputEnabled"/> is false the fighters receive empty input.
/// </summary>
public class InputManager : MonoBehaviour
{
    /// <summary>Global singleton instance.</summary>
    public static InputManager Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    [Header("Input Tuning")]
    [Tooltip("Minimum stick / key-combination magnitude required to register a dash.")]
    [SerializeField] private float dashThreshold = 0.7f;
    [Tooltip("Left-stick dead zone for walking.")]
    [SerializeField] private float stickDeadZone = 0.3f;

    /// <summary>Bindings in use; edited by the Controls menu and saved by it.</summary>
    public ControlBindings Bindings { get; private set; }

    /// <summary>False while a menu owns the input: fighters get an empty snapshot.</summary>
    public bool InputEnabled { get; set; } = true;

    private InputData player1Input;
    private InputData player2Input;

    /// <summary>
    /// Per-player right-stick state: flick edge detection for the dash, plus the double-flick
    /// run latch (two flicks the same way inside RUN_DOUBLE_FLICK_WINDOW, second one held =
    /// run until the stick returns to centre).
    /// </summary>
    private class PadFlickState
    {
        public bool held;                   // stick is beyond the dash threshold right now
        public float lastFlickTime = -99f;  // unscaled time of the last flick
        public Vector2 lastFlickDir;
        public bool runLatch;               // a second flick landed in the window and is still held
    }
    private readonly PadFlickState p1Flick = new PadFlickState();
    private readonly PadFlickState p2Flick = new PadFlickState();
    private bool ai1, ai2;
    private InputData aiInput1, aiInput2;

    // ---------------------------------------------------------------- explicit device choice
    // Set by the "Choose Controls" screen before a 2-player match (the user's call, 2026-10-25:
    // with two identical pads - or one pad listed twice over Bluetooth AND the cable - the index
    // guess in ControlBindings.PadFor hands a player a dead device). A chosen player reads ONLY
    // his device: his keyboard keys when Keyboard was picked, only that pad when a pad was.
    // Unchosen players keep the legacy auto routing (keyboard + PadFor).

    private bool device1Chosen, device2Chosen;
    private Gamepad device1Pad, device2Pad;      // null while chosen = keyboard only

    /// <summary>Pins a player to one device: a specific pad, or the keyboard (pad = null).</summary>
    public void SetPlayerDevice(int player, Gamepad pad)
    {
        if (player == 1) { device1Chosen = true; device1Pad = pad; }
        else { device2Chosen = true; device2Pad = pad; }
        Debug.Log($"Controls: P{player} <- {(pad != null ? pad.displayName : "keyboard")} (chosen)");
    }

    /// <summary>Back to the automatic routing (1-player modes, and anything not local-2P).</summary>
    public void ClearPlayerDevices()
    {
        device1Chosen = device2Chosen = false;
        device1Pad = device2Pad = null;
    }

    /// <summary>
    /// The pad this player reads, honouring an explicit choice. A chosen KEYBOARD returns null
    /// with keys allowed; a chosen pad returns it with keys OFF; a chosen pad that has since
    /// disconnected falls back to the automatic routing so the player is never dead in a fight.
    /// </summary>
    private Gamepad PadForPlayer(int player, out bool keysAllowed)
    {
        bool chosen = player == 1 ? device1Chosen : device2Chosen;
        if (chosen)
        {
            var p = player == 1 ? device1Pad : device2Pad;
            if (p == null) { keysAllowed = true; return null; }          // keyboard by choice
            if (p.added) { keysAllowed = false; return p; }              // exactly this pad
        }
        keysAllowed = true;
        return Bindings.PadFor(player);
    }

    /// <summary>A short rumble on one pad, so a player can tell which physical controller a menu
    /// row just picked (two DualSenses look identical in a list).</summary>
    public void IdentifyPulse(Gamepad pad)
    {
        if (pad == null) return;
        StopCoroutine(nameof(PulseRoutine));
        StartCoroutine(PulseRoutine(pad));
    }

    private System.Collections.IEnumerator PulseRoutine(Gamepad pad)
    {
        pad.SetMotorSpeeds(0.4f, 0.6f);
        yield return new WaitForSecondsRealtime(0.25f);
        pad.SetMotorSpeeds(0f, 0f);
    }

    /// <summary>Hand a player over to the AI (its snapshot replaces the human one) or back.</summary>
    public void SetAi(int player, bool on)
    {
        if (player == 1) { ai1 = on; aiInput1 = new InputData(); } else { ai2 = on; aiInput2 = new InputData(); }
    }

    /// <summary>The AI's snapshot for this frame; consumed on the next read so pressed flags fire once.</summary>
    public void SetAiInput(int player, InputData data)
    {
        if (player == 1) aiInput1 = data; else aiInput2 = data;
    }

    public bool IsAi(int player) => player == 1 ? ai1 : ai2;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Bindings = ControlBindings.Load();

        Application.targetFrameRate = BalanceConfig.TARGET_FPS;

        InputSystem.onDeviceChange += OnDeviceChange;
        LogPads("startup");
    }

    private void OnDestroy()
    {
        if (Instance == this) InputSystem.onDeviceChange -= OnDeviceChange;
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is Gamepad) LogPads(change.ToString());
    }

    /// <summary>
    /// Logs every connected pad and which player it drives - the Player.log evidence for
    /// "my second controller does nothing" (a pad on a charge-only cable never appears here).
    /// </summary>
    private void LogPads(string why)
    {
        var all = Gamepad.all;
        string p1 = Bindings != null && Bindings.PadFor(1) != null ? Bindings.PadFor(1).displayName : "keyboard only";
        string p2 = Bindings != null && Bindings.PadFor(2) != null ? Bindings.PadFor(2).displayName : "keyboard only";
        Debug.Log($"Pads ({why}): {all.Count} connected - P1 <- {p1}, P2 <- {p2}");
    }

    public void ReloadBindings() { Bindings = ControlBindings.Load(); }

    private void Update()
    {
        if (!InputEnabled)
        {
            player1Input = new InputData();
            player2Input = new InputData();
            return;
        }
        player1Input = ai1 ? aiInput1 : Read(1, p1Flick);
        player2Input = ai2 ? aiInput2 : Read(2, p2Flick);
        if (ai1) aiInput1 = new InputData();
        if (ai2) aiInput2 = new InputData();

        if (debugLogs)
        {
            LogActiveInputs();
        }
    }

    // ----- fighters -------------------------------------------------------------------------------

    private InputData Read(int player, PadFlickState flick)
    {
        var b = Bindings.Player(player);
        var pad = PadForPlayer(player, out bool keys);
        InputData data = new InputData();

        // Movement: screen left/right. Keyboard keys, d-pad buttons or the left stick.
        float move = 0f;
        if (Held(b, pad, GameAction.MoveLeft, keys)) move -= 1f;
        if (Held(b, pad, GameAction.MoveRight, keys)) move += 1f;
        if (pad != null && Mathf.Approximately(move, 0f))
        {
            float x = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(x) >= stickDeadZone) move = Mathf.Sign(x);
        }
        data.moveDirection = move;

        data.taunt = Held(b, pad, GameAction.Up, keys) || (pad != null && pad.leftStick.y.ReadValue() >= stickDeadZone);
        data.crouch = Held(b, pad, GameAction.Down, keys) || (pad != null && pad.leftStick.y.ReadValue() <= -stickDeadZone);

        data.run = Held(b, pad, GameAction.Run, keys);
        data.specialModifier = Held(b, pad, GameAction.Special, keys);
        data.lowModifier = Held(b, pad, GameAction.Low, keys);
        data.blockHeld = Held(b, pad, GameAction.Block, keys);

        data.lightAttack = Pressed(b, pad, GameAction.Light, keys);
        data.mediumAttack = Pressed(b, pad, GameAction.Medium, keys);
        data.heavyAttack = Pressed(b, pad, GameAction.Heavy, keys);
        data.defenseButton = Pressed(b, pad, GameAction.Defense, keys);
        data.throwButton = Pressed(b, pad, GameAction.Throw, keys);
        data.stanceSwitch = Pressed(b, pad, GameAction.Stance, keys);

        data.dashDirection = ReadDash(player, b, pad, keys, flick);

        // Run: the double-flick latch (set in ReadDash) while the stick is still held, or the
        // Run key held while walking (the keyboard route - P1 is on the keyboard).
        if (pad != null && flick.runLatch)
        {
            Vector2 rs = pad.rightStick.ReadValue();
            data.runHeld = true;
            data.runDirection = rs.normalized;
        }
        else if (data.run && !Mathf.Approximately(data.moveDirection, 0f))
        {
            data.runHeld = true;
            data.runDirection = new Vector2(data.moveDirection, 0f);
        }

        // on-screen touch buttons (phones / web build)
        if (TouchControls.Instance != null)
        {
            InputData t = TouchControls.Instance.Read(player);
            if (!Mathf.Approximately(t.moveDirection, 0f)) data.moveDirection = t.moveDirection;
            data.blockHeld |= t.blockHeld;
            data.lightAttack |= t.lightAttack;
            data.mediumAttack |= t.mediumAttack;
            data.heavyAttack |= t.heavyAttack;
            data.defenseButton |= t.defenseButton;
            data.specialModifier |= t.specialModifier;
            if (t.dashDirection != Vector2.zero) data.dashDirection = t.dashDirection;
        }
        return data;
    }

    /// <summary>
    /// Dash, 8-way (x = screen left/right, y = up = into the screen): keyboard = dash key held + a direction key
    /// pressed (or the dash key pressed with directions held); Player 1 also keeps the number-row layout
    /// (7 8 9 / 4 6 / 1 2 3). Pad = a flick of the right stick, or the pad dash button with the left stick.
    /// </summary>
    private Vector2 ReadDash(int player, ControlBindings.PlayerBindings b, Gamepad pad, bool keys, PadFlickState flickState)
    {
        if (keys)
        {
            KeyCode dashKey = b.Key(GameAction.Dash);
            KeyCode l = b.Key(GameAction.MoveLeft), r = b.Key(GameAction.MoveRight), u = b.Key(GameAction.Up), d = b.Key(GameAction.Down);
            bool dirDown = Input.GetKeyDown(l) || Input.GetKeyDown(r) || Input.GetKeyDown(u) || Input.GetKeyDown(d);
            bool dirHeld = Input.GetKey(l) || Input.GetKey(r) || Input.GetKey(u) || Input.GetKey(d);
            bool fire = dashKey != KeyCode.None && ((Input.GetKey(dashKey) && dirDown) || (Input.GetKeyDown(dashKey) && dirHeld));
            if (fire)
            {
                Vector2 v = new Vector2((Input.GetKey(r) ? 1f : 0f) - (Input.GetKey(l) ? 1f : 0f), (Input.GetKey(u) ? 1f : 0f) - (Input.GetKey(d) ? 1f : 0f));
                if (v.magnitude >= dashThreshold) return v.normalized;
            }
        }

        if (player == 1 && keys)
        {
            bool numDown = Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Alpha3) ||
                           Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Alpha7) ||
                           Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Alpha9);
            if (numDown)
            {
                float x = 0f, y = 0f;
                if (Input.GetKey(KeyCode.Alpha1) || Input.GetKey(KeyCode.Alpha4) || Input.GetKey(KeyCode.Alpha7)) x -= 1f;
                if (Input.GetKey(KeyCode.Alpha3) || Input.GetKey(KeyCode.Alpha6) || Input.GetKey(KeyCode.Alpha9)) x += 1f;
                if (Input.GetKey(KeyCode.Alpha7) || Input.GetKey(KeyCode.Alpha8) || Input.GetKey(KeyCode.Alpha9)) y += 1f;
                if (Input.GetKey(KeyCode.Alpha1) || Input.GetKey(KeyCode.Alpha2) || Input.GetKey(KeyCode.Alpha3)) y -= 1f;
                Vector2 v = new Vector2(x, y);
                if (v.magnitude >= dashThreshold) return v.normalized;
            }
        }

        if (pad != null)
        {
            Vector2 rs = pad.rightStick.ReadValue();
            bool beyond = rs.magnitude >= dashThreshold;
            bool flick = beyond && !flickState.held;
            flickState.held = beyond;
            if (!beyond) flickState.runLatch = false;   // stick back to centre = the run input ends
            if (flick)
            {
                // A SECOND flick the same way inside the window starts the RUN instead of paying
                // for a second dash (Fighter checks it points at the opponent). Unscaled time, so
                // the window does not stretch with the Settings game speed.
                bool second = Time.unscaledTime - flickState.lastFlickTime <= BalanceConfig.RUN_DOUBLE_FLICK_WINDOW
                              && Vector2.Dot(rs.normalized, flickState.lastFlickDir) >= 0.5f;
                flickState.lastFlickTime = Time.unscaledTime;
                flickState.lastFlickDir = rs.normalized;
                if (second) { flickState.runLatch = true; return Vector2.zero; }
                return rs.normalized;
            }

            int dashBtn = b.Pad(GameAction.Dash);
            if (dashBtn >= 0 && pad[(GamepadButton)dashBtn].wasPressedThisFrame)
            {
                Vector2 ls = pad.leftStick.ReadValue();
                if (ls.magnitude >= dashThreshold) return ls.normalized;
            }
        }
        return Vector2.zero;
    }

    private static bool Held(ControlBindings.PlayerBindings b, Gamepad pad, GameAction a, bool keys = true)
    {
        KeyCode k = b.Key(a);
        if (keys && k != KeyCode.None && Input.GetKey(k)) return true;
        int btn = b.Pad(a);
        return pad != null && btn >= 0 && pad[(GamepadButton)btn].isPressed;
    }

    private static bool Pressed(ControlBindings.PlayerBindings b, Gamepad pad, GameAction a, bool keys = true)
    {
        KeyCode k = b.Key(a);
        if (keys && k != KeyCode.None && Input.GetKeyDown(k)) return true;
        int btn = b.Pad(a);
        return pad != null && btn >= 0 && pad[(GamepadButton)btn].wasPressedThisFrame;
    }

    // ----- menus ----------------------------------------------------------------------------------

    /// <summary>Escape or any pad's Options button, this frame.</summary>
    public bool PausePressed()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) return true;
        if (TouchControls.Instance != null && TouchControls.Instance.PauseTapped()) return true;
        foreach (var g in Gamepad.all) if (g.startButton.wasPressedThisFrame) return true;
        return false;
    }

    public bool MenuConfirm()
    {
        if (TakeTouchConfirm(1)) return true;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) return true;
        foreach (var g in Gamepad.all) if (g.buttonSouth.wasPressedThisFrame) return true;
        return false;
    }

    public bool MenuBack()
    {
        if (TakeTouchBack(1)) return true;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace)) return true;
        foreach (var g in Gamepad.all) if (g.buttonEast.wasPressedThisFrame || g.startButton.wasPressedThisFrame) return true;
        return false;
    }

    // Presses from the on-screen select buttons and swipes, waiting for the menu to read them.
    // Slot 1 is also what the whole-screen reads below take; slot 2 is the right half of a split select.
    private readonly Vector2[] touchNav = new Vector2[3];
    private readonly bool[] touchConfirm = new bool[3], touchBack = new bool[3];

    /// <summary>Queues a menu press from the touch screen for <paramref name="player"/> (1 or 2).</summary>
    public void PushMenuTouch(int player, Vector2 nav, bool confirm, bool back)
    {
        touchNav[player] = nav;
        touchConfirm[player] |= confirm;
        touchBack[player] |= back;
    }

    public void ClearMenuTouches()
    {
        for (int i = 1; i <= 2; i++) { touchNav[i] = Vector2.zero; touchConfirm[i] = touchBack[i] = false; }
    }

    private Vector2 TakeTouchNav(int player) { var v = touchNav[player]; touchNav[player] = Vector2.zero; return v; }
    private bool TakeTouchConfirm(int player) { bool v = touchConfirm[player]; touchConfirm[player] = false; return v; }
    private bool TakeTouchBack(int player) { bool v = touchBack[player]; touchBack[player] = false; return v; }

    private Vector2 lastMenuStick;
    /// <summary>Menu navigation this frame: y = +1 up / -1 down, x = +1 right / -1 left (edge-triggered).</summary>
    public Vector2 MenuNavigate()
    {
        Vector2 touch = TakeTouchNav(1);
        float x = touch.x, y = touch.y;
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) y += 1f;
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) y -= 1f;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) x -= 1f;
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) x += 1f;
        Vector2 stick = Vector2.zero;
        foreach (var g in Gamepad.all)
        {
            if (g.dpad.up.wasPressedThisFrame) y += 1f;
            if (g.dpad.down.wasPressedThisFrame) y -= 1f;
            if (g.dpad.left.wasPressedThisFrame) x -= 1f;
            if (g.dpad.right.wasPressedThisFrame) x += 1f;
            Vector2 s = g.leftStick.ReadValue();
            if (s.magnitude > stick.magnitude) stick = s;
        }
        // stick edges: fire once when it leaves the dead zone
        Vector2 sd = new Vector2(Mathf.Abs(stick.x) > 0.6f ? Mathf.Sign(stick.x) : 0f, Mathf.Abs(stick.y) > 0.6f ? Mathf.Sign(stick.y) : 0f);
        if (sd.y != 0f && lastMenuStick.y == 0f) y += sd.y;
        if (sd.x != 0f && lastMenuStick.x == 0f) x += sd.x;
        lastMenuStick = sd;
        return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
    }

    // ---------------------------------------------------------------- per-player menu input
    // The split character select (2026-10-26): each player drives his OWN half, so the polling
    // has to be per device, not the all-pads union above. A player reads the pad he was pinned
    // to on the Choose Controls screen (PadForPlayer honours that, with the auto routing as the
    // fallback) plus his half of the keyboard when keys are allowed: P1 = WASD + Space,
    // P2 = arrows + Enter - the same split the fight bindings use.

    private Vector2 lastMenuStick1, lastMenuStick2;

    /// <summary>Menu navigation for ONE player (edge-triggered, like <see cref="MenuNavigate"/>).</summary>
    public Vector2 MenuNavigateFor(int player)
    {
        Vector2 touch = TakeTouchNav(player);
        float x = touch.x, y = touch.y;
        var pad = PadForPlayer(player, out bool keys);
        if (keys)
        {
            if (player == 1)
            {
                if (Input.GetKeyDown(KeyCode.W)) y += 1f;
                if (Input.GetKeyDown(KeyCode.S)) y -= 1f;
                if (Input.GetKeyDown(KeyCode.A)) x -= 1f;
                if (Input.GetKeyDown(KeyCode.D)) x += 1f;
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.UpArrow)) y += 1f;
                if (Input.GetKeyDown(KeyCode.DownArrow)) y -= 1f;
                if (Input.GetKeyDown(KeyCode.LeftArrow)) x -= 1f;
                if (Input.GetKeyDown(KeyCode.RightArrow)) x += 1f;
            }
        }
        if (pad != null)
        {
            if (pad.dpad.up.wasPressedThisFrame) y += 1f;
            if (pad.dpad.down.wasPressedThisFrame) y -= 1f;
            if (pad.dpad.left.wasPressedThisFrame) x -= 1f;
            if (pad.dpad.right.wasPressedThisFrame) x += 1f;
            Vector2 s = pad.leftStick.ReadValue();
            Vector2 sd = new Vector2(Mathf.Abs(s.x) > 0.6f ? Mathf.Sign(s.x) : 0f,
                                     Mathf.Abs(s.y) > 0.6f ? Mathf.Sign(s.y) : 0f);
            Vector2 last = player == 1 ? lastMenuStick1 : lastMenuStick2;
            if (sd.y != 0f && last.y == 0f) y += sd.y;
            if (sd.x != 0f && last.x == 0f) x += sd.x;
            if (player == 1) lastMenuStick1 = sd; else lastMenuStick2 = sd;
        }
        return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
    }

    /// <summary>Confirm for ONE player: his pad's Cross, or his half of the keyboard.</summary>
    public bool MenuConfirmFor(int player)
    {
        if (TakeTouchConfirm(player)) return true;
        var pad = PadForPlayer(player, out bool keys);
        if (keys)
        {
            if (player == 1 && Input.GetKeyDown(KeyCode.Space)) return true;
            if (player == 2 && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) return true;
        }
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }

    /// <summary>Back for ONE player: his pad's Circle. (Escape stays the whole-screen back.)</summary>
    public bool MenuBackFor(int player)
    {
        if (TakeTouchBack(player)) return true;
        var pad = PadForPlayer(player, out bool keys);
        if (keys && player == 1 && Input.GetKeyDown(KeyCode.Backspace)) return true;
        if (keys && player == 2 && Input.GetKeyDown(KeyCode.Delete)) return true;
        return pad != null && pad.buttonEast.wasPressedThisFrame;
    }

    /// <summary>True if any gamepad is connected.</summary>
    public static bool AnyPad => Gamepad.all.Count > 0;

    private void LogActiveInputs()
    {
        if (player1Input.lightAttack) Debug.Log("P1: Light Attack");
        if (player1Input.mediumAttack) Debug.Log("P1: Medium Attack");
        if (player1Input.heavyAttack) Debug.Log("P1: Heavy Attack");
        if (player1Input.defenseButton) Debug.Log("P1: Defense Button");
        if (player1Input.throwButton) Debug.Log("P1: Throw");
        if (player1Input.stanceSwitch) Debug.Log("P1: Stance Switch");
        if (player1Input.dashDirection != Vector2.zero) Debug.Log($"P1: Dash ({player1Input.dashDirection})");
    }

    /// <summary>Returns the latest input snapshot for Player 1.</summary>
    public InputData GetPlayer1Input() => player1Input;

    /// <summary>Returns the latest input snapshot for Player 2.</summary>
    public InputData GetPlayer2Input() => player2Input;
}
