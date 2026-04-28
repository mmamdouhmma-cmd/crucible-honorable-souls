using UnityEngine;

/// <summary>
/// Centralized input reader for both players.
/// Polls Unity's legacy Input system each frame and exposes
/// per-player <see cref="InputData"/> snapshots for downstream systems.
/// </summary>
public class InputManager : MonoBehaviour
{
    /// <summary>Global singleton instance.</summary>
    public static InputManager Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    [Header("Input Tuning")]
    [Tooltip("Minimum arrow-key axis magnitude required to register a dash.")]
    [SerializeField] private float dashThreshold = 0.7f;

    private InputData player1Input;
    private InputData player2Input;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Application.targetFrameRate = BalanceConfig.TARGET_FPS;
    }

    private void Update()
    {
        ReadPlayer1Input();
        ReadPlayer2Input();

        if (debugLogs)
        {
            LogActiveInputs();
        }
    }

    /// <summary>
    /// Reads Player 1 input from the keyboard (development/testing layout).
    /// </summary>
    private void ReadPlayer1Input()
    {
        InputData data = new InputData();

        // ----- Movement (A/D) -----
        float move = 0f;
        if (Input.GetKey(KeyCode.A)) move -= 1f;
        if (Input.GetKey(KeyCode.D)) move += 1f;
        data.moveDirection = move;

        // ----- Stick directions -----
        data.taunt  = Input.GetKey(KeyCode.W);
        data.crouch = Input.GetKey(KeyCode.S);

        // ----- Held modifiers / buttons -----
        data.run             = Input.GetKey(KeyCode.LeftShift); // L3
        data.specialModifier = Input.GetKey(KeyCode.U);         // L1
        data.lowModifier     = Input.GetKey(KeyCode.I);         // L2
        data.blockHeld       = Input.GetKey(KeyCode.P);         // R2

        // ----- Pressed buttons -----
        data.lightAttack   = Input.GetKeyDown(KeyCode.J);         // Square
        data.mediumAttack  = Input.GetKeyDown(KeyCode.K);         // Cross
        data.heavyAttack   = Input.GetKeyDown(KeyCode.L);         // Triangle
        data.defenseButton = Input.GetKeyDown(KeyCode.Semicolon); // Circle
        data.throwButton   = Input.GetKeyDown(KeyCode.O);         // R1
        data.stanceSwitch  = Input.GetKeyDown(KeyCode.R);         // R3

        // ----- Dash (8-way arrow keys, fires only on press) -----
        bool dashJustPressed = Input.GetKeyDown(KeyCode.UpArrow) ||
                               Input.GetKeyDown(KeyCode.DownArrow) ||
                               Input.GetKeyDown(KeyCode.LeftArrow) ||
                               Input.GetKeyDown(KeyCode.RightArrow);

        if (dashJustPressed)
        {
            // Read all currently held arrow keys (for diagonal support)
            float dashX = 0f;
            float dashY = 0f;
            if (Input.GetKey(KeyCode.LeftArrow))  dashX -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) dashX += 1f;
            if (Input.GetKey(KeyCode.UpArrow))    dashY += 1f;
            if (Input.GetKey(KeyCode.DownArrow))  dashY -= 1f;

            Vector2 dash = new Vector2(dashX, dashY);
            data.dashDirection = dash.magnitude >= dashThreshold ? dash.normalized : Vector2.zero;
        }
        else
        {
            data.dashDirection = Vector2.zero;
        }

        player1Input = data;
    }

    /// <summary>
    /// Reads Player 2 input. Empty for now — DualSense / DualShock controller
    /// support is the next step in the input module.
    /// </summary>
    private void ReadPlayer2Input()
    {
        // TODO: Wire up PS4/PS5 controller (DualShock/DualSense) for Player 2.
        player2Input = default;
    }

    /// <summary>
    /// Emits Debug.Log entries for notable Player 1 inputs each frame.
    /// </summary>
    private void LogActiveInputs()
    {
        if (player1Input.lightAttack)   Debug.Log("P1: Light Attack");
        if (player1Input.mediumAttack)  Debug.Log("P1: Medium Attack");
        if (player1Input.heavyAttack)   Debug.Log("P1: Heavy Attack");
        if (player1Input.defenseButton) Debug.Log("P1: Defense Button");
        if (player1Input.throwButton)   Debug.Log("P1: Throw");
        if (player1Input.stanceSwitch)  Debug.Log("P1: Stance Switch");

        if (player1Input.dashDirection != Vector2.zero)
        {
            Debug.Log($"P1: Dash ({player1Input.dashDirection})");
        }
    }

    /// <summary>
    /// Returns the latest input snapshot for Player 1.
    /// </summary>
    public InputData GetPlayer1Input()
    {
        return player1Input;
    }

    /// <summary>
    /// Returns the latest input snapshot for Player 2.
    /// </summary>
    public InputData GetPlayer2Input()
    {
        return player2Input;
    }
}