using UnityEngine;

/// <summary>
/// Main character controller. Pulls per-frame input from <see cref="InputManager"/>,
/// owns a runtime <see cref="FighterStats"/> block built from a <see cref="FighterData"/>
/// archetype, and drives basic movement / stamina recovery.
/// </summary>
public class Fighter : MonoBehaviour
{
    [Header("Fighter Configuration")]
    public FighterData fighterData;

    [Tooltip("1 = Player 1 (keyboard), 2 = Player 2 (controller).")]
    public int playerNumber = 1;

    [Header("Runtime State")]
    [SerializeField] private FighterStats stats;

    /// <summary>Live runtime stats for this fighter.</summary>
    public FighterStats Stats { get { return stats; } private set { stats = value; } }

    private Rigidbody rb;
    private InputData currentInput;
    private bool facingRight;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogWarning($"Fighter on {name}: no Rigidbody found — physics-driven movement will be unavailable.");
        }

        if (fighterData != null)
        {
            stats = new FighterStats(fighterData.MaxHealth, fighterData.MaxStamina, fighterData.Armor);
        }

        // P1 starts on the left facing right; P2 starts on the right facing left.
        facingRight = (playerNumber == 1);
        transform.rotation = Quaternion.Euler(0f, facingRight ? 90f : -90f, 0f);
    }

    private void Start()
    {
        if (InputManager.Instance == null)
        {
            Debug.LogError($"Fighter on {name}: InputManager.Instance is missing — add an InputManager to the scene.");
        }

        if (fighterData == null)
        {
            Debug.LogError($"Fighter on {name}: fighterData is not assigned — create a FighterData asset and assign it.");
            return;
        }

        Debug.Log($"Fighter initialized: {fighterData.FighterName} for Player {playerNumber}");
    }

    private void Update()
    {
        if (InputManager.Instance == null || fighterData == null) return;

        currentInput = (playerNumber == 1)
            ? InputManager.Instance.GetPlayer1Input()
            : InputManager.Instance.GetPlayer2Input();

        HandleMovement();
        HandleStaminaRecovery();
    }

    /// <summary>
    /// Applies horizontal movement from the current input snapshot and logs
    /// crouch/taunt states (placeholder until full state machine is in).
    /// </summary>
    private void HandleMovement()
    {
        float moveSpeed = fighterData.MovementSpeed / 10f;
        transform.position += new Vector3(currentInput.moveDirection * moveSpeed * Time.deltaTime, 0f, 0f);

        if (currentInput.crouch)
        {
            Debug.Log($"P{playerNumber}: Ducking");
        }

        if (currentInput.taunt)
        {
            Debug.Log($"P{playerNumber}: Taunting");
        }
    }

    /// <summary>
    /// Ticks passive stamina regeneration each frame.
    /// </summary>
    private void HandleStaminaRecovery()
    {
        stats.RecoverStamina(Time.deltaTime);
    }

    /// <summary>
    /// True if this fighter is currently facing right (toward +X).
    /// </summary>
    public bool IsFacingRight()
    {
        return facingRight;
    }
}
