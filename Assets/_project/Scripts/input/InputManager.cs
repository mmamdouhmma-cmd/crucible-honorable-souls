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
    private bool p1PadFlickHeld, p2PadFlickHeld;

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
        player1Input = Read(1, ref p1PadFlickHeld);
        player2Input = Read(2, ref p2PadFlickHeld);

        if (debugLogs)
        {
            LogActiveInputs();
        }
    }

    // ----- fighters -------------------------------------------------------------------------------

    private InputData Read(int player, ref bool padFlickHeld)
    {
        var b = Bindings.Player(player);
        var pad = Bindings.PadFor(player);
        InputData data = new InputData();

        // Movement: screen left/right. Keyboard keys, d-pad buttons or the left stick.
        float move = 0f;
        if (Held(b, pad, GameAction.MoveLeft)) move -= 1f;
        if (Held(b, pad, GameAction.MoveRight)) move += 1f;
        if (pad != null && Mathf.Approximately(move, 0f))
        {
            float x = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(x) >= stickDeadZone) move = Mathf.Sign(x);
        }
        data.moveDirection = move;

        data.taunt = Held(b, pad, GameAction.Up) || (pad != null && pad.leftStick.y.ReadValue() >= stickDeadZone);
        data.crouch = Held(b, pad, GameAction.Down) || (pad != null && pad.leftStick.y.ReadValue() <= -stickDeadZone);

        data.run = Held(b, pad, GameAction.Run);
        data.specialModifier = Held(b, pad, GameAction.Special);
        data.lowModifier = Held(b, pad, GameAction.Low);
        data.blockHeld = Held(b, pad, GameAction.Block);

        data.lightAttack = Pressed(b, pad, GameAction.Light);
        data.mediumAttack = Pressed(b, pad, GameAction.Medium);
        data.heavyAttack = Pressed(b, pad, GameAction.Heavy);
        data.defenseButton = Pressed(b, pad, GameAction.Defense);
        data.throwButton = Pressed(b, pad, GameAction.Throw);
        data.stanceSwitch = Pressed(b, pad, GameAction.Stance);

        data.dashDirection = ReadDash(player, b, pad, ref padFlickHeld);
        return data;
    }

    /// <summary>
    /// Dash, 8-way (x = screen left/right, y = up = into the screen): keyboard = dash key held + a direction key
    /// pressed (or the dash key pressed with directions held); Player 1 also keeps the number-row layout
    /// (7 8 9 / 4 6 / 1 2 3). Pad = a flick of the right stick, or the pad dash button with the left stick.
    /// </summary>
    private Vector2 ReadDash(int player, ControlBindings.PlayerBindings b, Gamepad pad, ref bool padFlickHeld)
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

        if (player == 1)
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
            bool flick = beyond && !padFlickHeld;
            padFlickHeld = beyond;
            if (flick) return rs.normalized;

            int dashBtn = b.Pad(GameAction.Dash);
            if (dashBtn >= 0 && pad[(GamepadButton)dashBtn].wasPressedThisFrame)
            {
                Vector2 ls = pad.leftStick.ReadValue();
                if (ls.magnitude >= dashThreshold) return ls.normalized;
            }
        }
        return Vector2.zero;
    }

    private static bool Held(ControlBindings.PlayerBindings b, Gamepad pad, GameAction a)
    {
        KeyCode k = b.Key(a);
        if (k != KeyCode.None && Input.GetKey(k)) return true;
        int btn = b.Pad(a);
        return pad != null && btn >= 0 && pad[(GamepadButton)btn].isPressed;
    }

    private static bool Pressed(ControlBindings.PlayerBindings b, Gamepad pad, GameAction a)
    {
        KeyCode k = b.Key(a);
        if (k != KeyCode.None && Input.GetKeyDown(k)) return true;
        int btn = b.Pad(a);
        return pad != null && btn >= 0 && pad[(GamepadButton)btn].wasPressedThisFrame;
    }

    // ----- menus ----------------------------------------------------------------------------------

    /// <summary>Escape or any pad's Options button, this frame.</summary>
    public bool PausePressed()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) return true;
        foreach (var g in Gamepad.all) if (g.startButton.wasPressedThisFrame) return true;
        return false;
    }

    public bool MenuConfirm()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) return true;
        foreach (var g in Gamepad.all) if (g.buttonSouth.wasPressedThisFrame) return true;
        return false;
    }

    public bool MenuBack()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace)) return true;
        foreach (var g in Gamepad.all) if (g.buttonEast.wasPressedThisFrame || g.startButton.wasPressedThisFrame) return true;
        return false;
    }

    private Vector2 lastMenuStick;
    /// <summary>Menu navigation this frame: y = +1 up / -1 down, x = +1 right / -1 left (edge-triggered).</summary>
    public Vector2 MenuNavigate()
    {
        float x = 0f, y = 0f;
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
