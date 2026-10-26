using UnityEngine;

/// <summary>What an incoming strike does to a fighter, given how they were dashing.</summary>
public enum DashHitOutcome
{
    Hit,             // normal damage
    HitAmplified,    // dashed into the strike: extra damage
    Evaded,          // strike missed; counter window opens when the dash ends
    EvadedNoCounter  // strike missed, but the dash carried the fighter too far to counter
}

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

    [Header("Vitals")]
    [Tooltip("Max health for THIS fighter. -1 takes the FighterData value. Both fighters share one " +
             "FighterData asset today, so this is how they get different vitals; the HUD shows a " +
             "percentage, so the numbers need not match each other.")]
    [SerializeField] private int maxHealth = -1;

    [Tooltip("Max stamina for THIS fighter. -1 takes the FighterData value.")]
    [SerializeField] private int maxStamina = -1;

    [Tooltip("Damage reduction percent (0-100) for THIS fighter. -1 takes the FighterData value.")]
    [SerializeField] private int armor = -1;

    /// <summary>Max health this fighter starts a match with: its own value, else the archetype's.</summary>
    public int MaxHealth => maxHealth > 0 ? maxHealth : (fighterData != null ? fighterData.MaxHealth : 100);

    /// <summary>Max stamina this fighter starts a match with: its own value, else the archetype's.</summary>
    public int MaxStamina => maxStamina > 0 ? maxStamina : (fighterData != null ? fighterData.MaxStamina : 100);

    /// <summary>Armor percent for this fighter: its own value, else the archetype's.</summary>
    public int Armor => armor >= 0 ? armor : (fighterData != null ? fighterData.Armor : 0);

    [Header("Runtime State")]
    [SerializeField] private FighterStats stats;

    /// <summary>Live runtime stats for this fighter.</summary>
    public FighterStats Stats { get { return stats; } private set { stats = value; } }

    [Header("Combat")]
    [SerializeField] private CombatSystem combatSystem;

    /// <summary>Combat driver for this fighter (frame-data attacks + hitboxes).</summary>
    public CombatSystem Combat => combatSystem;

    /// <summary>Player slot (1 = P1 / keyboard, 2 = P2 / controller).</summary>
    public int PlayerNumber => playerNumber;

    [Tooltip("Display name. Blank = worked out from this fighter's attack assets (Bjorn_Light_Slash -> Bjorn).")]
    [SerializeField] private string characterName = "";
    private string resolvedName;

    /// <summary>
    /// Who this fighter IS, as the menus and the HUD show it. Both fighters currently share one
    /// <see cref="FighterData"/> asset called "test Fighter", which tells the two apart for nobody,
    /// so the identity this project really uses is the attack asset prefix - the same prefix that
    /// keys the animator states and the Mixamo clip maps.
    /// </summary>
    public string CharacterName
    {
        get
        {
            if (!string.IsNullOrEmpty(characterName)) return characterName;
            if (!string.IsNullOrEmpty(resolvedName)) return resolvedName;

            if (combatSystem != null && combatSystem.Attacks != null)
            {
                foreach (var attack in combatSystem.Attacks)
                {
                    if (attack == null) continue;
                    int cut = attack.name.IndexOf('_');
                    if (cut > 0) { resolvedName = attack.name.Substring(0, cut); return resolvedName; }
                }
            }
            resolvedName = fighterData != null && !string.IsNullOrEmpty(fighterData.FighterName)
                         ? fighterData.FighterName : name;
            return resolvedName;
        }
    }

    /// <summary>
    /// Moves this fighter to the other player's controls. Used by character select, where choosing
    /// the "other" fighter swaps which side each player drives rather than cloning a rig. The caller
    /// is responsible for putting them on the matching spawn mark and for rebuilding the HUD.
    /// </summary>
    public void SetPlayerNumber(int number)
    {
        playerNumber = Mathf.Clamp(number, 1, 2);
        facingRight = (playerNumber == 1);
        currentInput = new InputData();
    }

    private Rigidbody rb;
    private Animator animator;
    private bool animatorHasHitParams; // controller defines the HitSide int and GetHit trigger
    private InputData currentInput;
    private bool facingRight;

    private const string ANIM_HIT_SIDE = "HitSide"; // int: 0 = Straight, 1 = Left, 2 = Right
    private const string ANIM_GET_HIT = "GetHit";   // trigger: play the get-hit animation
    private const string ANIM_BLOCKING = "Blocking"; // bool: guard is up (controller holds the block pose)
    // Lower-body-masked layer (wired by Crucible > Animation > Wire the block-walk legs): a walk
    // blend that plays UNDER the block pose, so a guarding fighter steps instead of floating.
    // Weight is driven here - up only while blocking AND moving, so a standing guard keeps the
    // authored block stance head to toe.
    private const string BLOCK_LEGS_LAYER = "BlockLegs";
    private int blockLegsLayer = -1;
    private float blockLegsWeight;
    private const string ANIM_DASH = "Dash";         // trigger: a dash just started
    private const string ANIM_DASH_DIR = "DashDir";  // int: 0 forward, 1 back, 2 left, 3 right (relative to facing)
    private const string ANIM_RUNNING = "Running";   // bool: held double-flick run toward the opponent
    private const string ANIM_PARRY_SUCCESS = "ParrySuccess"; // trigger: a parry caught a strike
    private const string ANIM_PARRY_SPEED = "ParrySpeed";     // float: Parry_Success playback speed
    private const string ANIM_DEATH = "Death";                // trigger: health reached zero
    private bool animatorHasBlocking;
    private bool animatorHasParry;
    private bool animatorHasDeath;
    private bool animatorHasDash;
    private bool animatorHasRunning;

    [Header("Dash")]
    [SerializeField] private float dashDistance = 2.5f;
    [SerializeField] private float dashDuration = 0.25f;   // total dash time in seconds
    [SerializeField] private int dashStaminaCost = 25;

    [Tooltip("Seconds from the start of a dash during which it can still EVADE (the i-frames). " +
             "Past this the dash's tail is an ordinary target: it no longer dodges, and a forward " +
             "dash still runs into the blade. What matters is COVERAGE - this time as a fraction " +
             "of dashDuration - which CrucibleBalance sets per fighter (Plan_Balance 2.5).")]
    [SerializeField] private float dashIFrameDuration = BalanceConfig.DASH_IFRAME_DURATION;
    private bool isDashing = false;
    private float dashTimer = 0f;
    private int dashStunSerial;        // the stun serial this dash began under (combo-stun dodges)
    private Vector3 dashVelocity = Vector3.zero;
    private float dashForwardAmount = 0f; // dash direction relative to facing: +1 forward, -1 back
    private float dashRightAmount = 0f;   // +1 right, -1 left
    private Vector3 dashStartPosition;    // where the fighter stood when the current dash began
    public bool IsDashing { get { return isDashing; } }

    // Run: double-flick of the right stick toward the opponent, held on the second flick
    // (InputData.runHeld / runDirection, latched in InputManager). The run itself is free -
    // getting into it costs the first flick's dash - and ends the moment the stick returns,
    // anything interrupts the fighter, or the opponent's body is reached.
    private bool isRunning = false;

    /// <summary>True while the held double-flick run is carrying this fighter forward.</summary>
    public bool IsRunning => isRunning;
    /// <summary>True while the counter window after a clean dodge is open.</summary>
    public bool DashCounterOpen => dashCounterTimer > 0f;

    /// <summary>World position this fighter was at when the current dash started (only meaningful while dashing).</summary>
    public Vector3 DashStartPosition => dashStartPosition;

    /// <summary>The fighter this one is fighting (null until both are in the scene).</summary>
    public Fighter Opponent => opponent;

    // Dash counter: a dash made while the opponent is attacking (a dodge) opens a short
    // window in which the next attack deals bonus damage.
    private bool dodgedAttackThisDash = false;
    private bool hitDuringDash = false;
    private float dashCounterTimer = 0f;
    private float lastDodgeTime = -1f; // Time.time of the last dodge still waiting for its counter attack; -1 = none

    [Header("Block")]
    [Tooltip("Percent of damage removed while blocking (0 = block does nothing, 100 = no damage). Set per character.")]
    [Range(0, 100)]
    [SerializeField] private int blockDamageReduction = BalanceConfig.DEFAULT_BLOCK_DAMAGE_REDUCTION;

    [Tooltip("Stamina drained per second while block is held, on top of the per-hit cost paid when a strike is blocked. 0 = holding block is free.")]
    [Min(0f)]
    [SerializeField] private float blockStaminaDrainPerSecond = BalanceConfig.BLOCK_STAMINA_DRAIN_PER_SECOND;

    [Header("Parry")]
    [Tooltip("Seconds after the guard goes up during which an incoming strike is PARRIED instead of blocked: " +
             "no damage, no stamina cost, and the attacker is stunned. Timing it beats holding it.")]
    [Min(0f)]
    [SerializeField] private float parryWindow = BalanceConfig.PARRY_WINDOW;

    [Tooltip("Seconds before another parry can be attempted. This cooldown is the parry's whole cost on top of the stamina: " +
             "there is no recovery lockout, so a miss never freezes the fighter - it just cannot be tried again straight away.")]
    [Min(0f)]
    [SerializeField] private float parryCooldown = 1f;

    [Tooltip("Stamina spent when a parry CONNECTS. Opening the window (raising block / Defense) is free; the cooldown paces attempts.")]
    [Min(0)]
    [SerializeField] private int parryStaminaCost = 8;

    [Tooltip("Playback speed of the Parry_Success animation. 2 = double speed. Needs the 'ParrySpeed' float parameter on the controller.")]
    [SerializeField] private float parryAnimSpeed = 2f;

    [Tooltip("Seconds of stun the ATTACKER eats when parried: the swing ends and they cannot act, which is the " +
             "parrier's free punish.")]
    [Min(0f)]
    [SerializeField] private float parryStunDuration = BalanceConfig.PARRY_STUN_DURATION;

    [Tooltip("Seconds after a parry during which this fighter's next attack counts as a parry counter.")]
    [Min(0f)]
    [SerializeField] private float parryCounterWindow = BalanceConfig.PARRY_COUNTER_WINDOW;

    [Tooltip("Damage multiplier on an attack started inside the parry-counter window.")]
    [Min(1f)]
    [SerializeField] private float parryCounterDamageMultiplier = BalanceConfig.PARRY_COUNTER_DAMAGE_MULTIPLIER;

    [Tooltip("Super-bar charge awarded for a parry.")]
    [Min(0)]
    [SerializeField] private int parrySuperGain = BalanceConfig.SUPER_GAIN_PARRY;

    [Tooltip("If off, stamina does not regenerate while block is held, so a turtling fighter eventually runs dry.")]
    [SerializeField] private bool staminaRecoversWhileBlocking = false;
    private bool isBlocking = false;

    /// <summary>True while block is held and the fighter is able to block (not attacking, dashing or out of stamina).</summary>
    public bool IsBlocking { get { return isBlocking; } }

    /// <summary>Percent of damage this fighter's block removes (0-100).</summary>
    public int BlockDamageReduction => blockDamageReduction;

    /// <summary>True while locked in a get-hit flinch: no attacking, dashing or walking.</summary>
    public bool IsInHitstun => combatSystem != null && combatSystem.IsInHitstun;

    /// <summary>Seconds left in the dash-counter window (0 when no counter is ready).</summary>
    public float DashCounterTimeLeft => Mathf.Max(dashCounterTimer, 0f);

    // Clash: a shove away from the opponent plus a stagger, applied when two equal attacks meet.
    private Vector3 knockbackVelocity = Vector3.zero;
    private float knockbackTimer = 0f;
    private float clashFlashTimer = 0f;

    /// <summary>Seconds left for the HUD to show CLASH (0 when none is pending).</summary>
    public float ClashFlashTimeLeft => Mathf.Max(clashFlashTimer, 0f);

    // Parry. The user's call (2026-10-24, reversing the dedicated-button design): RAISING BLOCK
    // in time with the incoming attack IS the parry - the guard's first parryWindow seconds
    // deflect a strike outright, after which the raise settles into an ordinary block. The
    // Defense button (Circle) still opens the same window, because the AI schedules its parries
    // through it. A raise costs nothing (block drain is 0 now, blocking is meant to be free);
    // the stamina cost is charged when a parry CONNECTS, and parryCooldown gates how soon the
    // window can re-open, so dropping and re-raising the guard on every swing is paced. There is
    // deliberately NO recovery lockout - a missed window is just a block.
    private float parryActiveTimer = 0f;
    private float parryCooldownTimer = 0f;
    private float parryCounterTimer = 0f;
    private float parryFlashTimer = 0f;
    private bool prevBlockHeld = false;   // last frame's block input, for the rising edge

    /// <summary>
    /// True while the parry's active window is open, so an incoming strike is deflected. Read by
    /// CombatSystem before blocking is considered.
    /// </summary>
    public bool ParryReady => parryActiveTimer > 0f;

    /// <summary>Seconds left of the parry cooldown (0 when a parry can be attempted).</summary>
    public float ParryCooldownTimeLeft => Mathf.Max(parryCooldownTimer, 0f);

    /// <summary>Seconds left in the parry-counter window (0 when no counter is ready).</summary>
    public float ParryCounterTimeLeft => Mathf.Max(parryCounterTimer, 0f);

    /// <summary>Seconds left for the HUD to show PARRY (0 when none is pending).</summary>
    public float ParryFlashTimeLeft => Mathf.Max(parryFlashTimer, 0f);

    /// <summary>True once health has hit zero and the death animation has been fired.</summary>
    public bool IsDead => isDead;
    private bool isDead = false;

    /// <summary>Frames of stun an attacker eats when this fighter parries them.</summary>
    public int ParryStunFrames => Mathf.Max(1, Mathf.RoundToInt(parryStunDuration / BalanceConfig.FRAME_TIME));

    [Header("Recovery")]
    [Tooltip("Stamina regained per second. Set per character.")]
    [SerializeField] private float staminaRecoveryPerSecond = BalanceConfig.STAMINA_RECOVERY_PER_SECOND;

    [Tooltip("Health regained per second while alive. Keep it small; 0 turns regeneration off. Set per character.")]
    [SerializeField] private float healthRegenPerSecond = BalanceConfig.DEFAULT_HEALTH_REGEN_PER_SECOND;

    [Header("Defensive recovery")]
    [Tooltip("Health given back for a dash that made a strike miss, as a fraction of THIS fighter's max health.")]
    [SerializeField] private float healOnDodge = BalanceConfig.HEAL_ON_DODGE;

    [Tooltip("Health given back for a parry, as a fraction of max health.")]
    [SerializeField] private float healOnParry = BalanceConfig.HEAL_ON_PARRY;

    [Tooltip("Health given back for landing the counter a dodge or parry earned, as a fraction of max health.")]
    [SerializeField] private float healOnCounterHit = BalanceConfig.HEAL_ON_COUNTER_HIT;

    [Tooltip("Total recovery allowed in one round, as a fraction of max health. 0 = no cap.")]
    [SerializeField] private float healCapPerRound = BalanceConfig.HEAL_CAP_PER_ROUND;

    [Tooltip("Seconds the HUD shows the \"+N%\" recovery float.")]
    [SerializeField] private float healFlashDuration = 1.1f;

    private float healFlashTimer = 0f;
    private int healFlashAmount = 0;

    /// <summary>Seconds left on the HUD's "+N%" recovery float (0 when nothing to show).</summary>
    public float HealFlashTimeLeft => Mathf.Max(healFlashTimer, 0f);

    /// <summary>Health points given by the most recent defensive recovery.</summary>
    public int HealFlashAmount => healFlashAmount;

    [Tooltip("Seconds after taking a hit (blocked or not) before health regeneration starts again.")]
    [SerializeField] private float healthRegenDelay = BalanceConfig.DEFAULT_HEALTH_REGEN_DELAY;
    private float healthRegenDelayTimer = 0f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    [Header("Facing")]
    [Tooltip("How fast the fighter turns to face the opponent, in degrees per second.")]
    [SerializeField] private float turnSpeed = 720f;

    [Tooltip("Turn speed while dashing and during the dash-counter window, so a sidestep squares up on the opponent at once.")]
    [SerializeField] private float dodgeTurnSpeed = 2000f;

    [Header("Effects")]
    [Tooltip("Dust puffs on dash push-off and landing. Off: blood is the only effect in the fight.")]
    [SerializeField] private bool dustEffects = true;

    [Tooltip("Dust strength for the puff thrown when a dash makes an attack MISS. Louder than the ordinary dash dust (1) so a successful dodge reads at a glance.")]
    [Range(1f, 6f)]
    [SerializeField] private float dodgeDustStrength = 3f;

    [Tooltip("Animator state to snap back to on a rematch. Blank: worked out from the FighterData name (e.g. Bjorn_Idle), then plain Idle.")]
    [SerializeField] private string idleStateName = "";
    private int idleStateHash;                 // resolved in Awake; 0 = fall back to Animator.Rebind()
    private float lastDodgeDustTime = -99f;    // both evade paths can fire in the same frame
    private const float FACING_FLIP_DEAD_ZONE = 0.1f; // min left/right gap before input sides swap
    private Fighter opponent;
    private Transform cameraTransform;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogWarning($"Fighter on {name}: no Rigidbody found.");
        }

        if (combatSystem == null)
        {
            combatSystem = GetComponent<CombatSystem>();
        }

        CacheBodyRadius();

        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        animatorHasHitParams = HasAnimatorParameters(ANIM_HIT_SIDE, ANIM_GET_HIT);
        animatorHasBlocking = HasAnimatorParameters(ANIM_BLOCKING);
        blockLegsLayer = animator != null && animator.runtimeAnimatorController != null
            ? animator.GetLayerIndex(BLOCK_LEGS_LAYER) : -1;
        animatorHasDash = HasAnimatorParameters(ANIM_DASH, ANIM_DASH_DIR);
        animatorHasRunning = HasAnimatorParameters(ANIM_RUNNING);
        animatorHasParry = HasAnimatorParameters(ANIM_PARRY_SUCCESS);
        animatorHasDeath = HasAnimatorParameters(ANIM_DEATH);
        ResolveIdleState();

        if (fighterData != null)
        {
            stats = new FighterStats(MaxHealth, MaxStamina, Armor);
        }

        // Starting side based on player number; HandleFacing keeps it up to date after that
        facingRight = (playerNumber == 1);
    }

    private void Start()
    {
        if (InputManager.Instance == null)
        {
            Debug.LogError($"Fighter on {name}: InputManager.Instance is missing.");
        }

        opponent = FindOpponent();
        if (opponent == null)
        {
            Debug.LogWarning($"Fighter on {name}: no opponent Fighter found, facing will not update.");
        }
        else
        {
            IgnoreOpponentBodyCollision();
        }

        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (fighterData == null)
        {
            Debug.LogError($"Fighter on {name}: fighterData is not assigned.");
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

        // Hit-stop: hold still (movement, dash, timers) while the impact freeze runs.
        if (HitStop.IsActive) return;

        // Dead: no input, no sliding corpse. The Animator keeps running, so the death clip plays
        // out under the KO screen; ResetForMatch is what puts this fighter back on their feet.
        if (isDead)
        {
            dashVelocity = Vector3.zero;
            knockbackVelocity = Vector3.zero;
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetFloat("MoveSpeed", 0f);
            return;
        }

        if (dashCounterTimer > 0f)
        {
            dashCounterTimer -= Time.deltaTime;
        }
        if (clashFlashTimer > 0f)
        {
            clashFlashTimer -= Time.deltaTime;
        }
        if (healFlashTimer > 0f) healFlashTimer -= Time.deltaTime;

        if (parryCounterTimer > 0f)
        {
            parryCounterTimer -= Time.deltaTime;
        }
        if (parryFlashTimer > 0f)
        {
            parryFlashTimer -= Time.deltaTime;
        }

        HandleFacing();
        HandleKnockback();
        HandleDash();
        HandleRun();
        HandleMovement();
        HandleAttackLunge();
        HandleStaminaRecovery();
        HandleParry();
        HandleBlock();
        UpdateBlockLegs();
        HandleCombatInput();
        ClampToArena();

    }

    /// <summary>
    /// Block is up while the button is held, unless the fighter is attacking,
    /// dashing, or has no stamina left to absorb a hit. Holding block drains
    /// stamina every frame; when it runs out the guard drops until the bar
    /// recovers above zero.
    /// </summary>
    /// <summary>
    /// Keeps this fighter inside the arena, after every move this frame. It has to be done here
    /// rather than with wall colliders: the Rigidbody freezes position X and Z, so physics cannot
    /// push the fighter out of anything (see <see cref="ArenaBounds"/>).
    /// </summary>
    private void ClampToArena()
    {
        if (ArenaBounds.Instance == null) return;

        Vector3 p = transform.position;
        Vector3 inside = ArenaBounds.Instance.Clamp(p, BodyRadius);
        if ((inside - p).sqrMagnitude < 1e-6f) return;

        // Match how the movement code writes position (transform only; the Rigidbody follows).
        transform.position = inside;
        if (isDashing) dashVelocity = Vector3.zero;   // a dash into the edge stops there
        knockbackVelocity = Vector3.zero;

        if (ArenaBounds.Instance.DebugLogs && !loggedArenaEdge)
        {
            loggedArenaEdge = true;
            Debug.Log($"P{playerNumber}: held inside the arena edge at {inside}");
        }
    }

    private bool loggedArenaEdge = false;

    /// <summary>
    /// The parry: RAISING the guard in time with the attack. The block input's rising edge (and
    /// the Defense button, which the AI presses) opens <see cref="parryWindow"/> seconds in which
    /// an incoming strike is deflected (see <see cref="NotifyParry"/>); after that the raise is
    /// an ordinary block. Opening the window is free - the stamina cost is charged only when a
    /// parry connects - and <see cref="parryCooldown"/> paces how soon the window can re-open:
    /// a parry that catches nothing leaves the fighter free to block, dash or swing immediately.
    /// An attempt is refused while attacking, dashing or in hitstun.
    /// </summary>
    private void HandleParry()
    {
        bool blockRaised = currentInput.blockHeld && !prevBlockHeld;
        prevBlockHeld = currentInput.blockHeld;

        if (parryCooldownTimer > 0f) parryCooldownTimer = Mathf.Max(0f, parryCooldownTimer - Time.deltaTime);
        if (parryActiveTimer > 0f)
        {
            parryActiveTimer = Mathf.Max(0f, parryActiveTimer - Time.deltaTime);
            return;
        }

        if (!currentInput.defenseButton && !blockRaised) return;

        bool busy = combatSystem != null && (combatSystem.IsAttacking || combatSystem.IsInHitstun || combatSystem.GuardBreakRecovering);
        if (busy || isDashing) return;

        if (parryCooldownTimer > 0f)
        {
            if (debugLogs) Debug.Log($"P{playerNumber}: no parry window - {parryCooldownTimer:0.00}s of cooldown left");
            return;
        }

        parryActiveTimer = parryWindow;
        parryCooldownTimer = parryCooldown;
        if (debugLogs) Debug.Log($"P{playerNumber}: PARRY window open - {parryWindow:0.00}s, {parryCooldown:0.00}s cooldown");
    }

    private void HandleBlock()
    {
        bool wasBlocking = isBlocking;
        bool attacking = combatSystem != null && combatSystem.IsAttacking;

        // A stunned fighter cannot hold or raise the guard (the user's call, 2026-10-24): under
        // hitstun the only out left is the combo-stun dodge, handled in HandleDash. The one
        // exception is the stun from a hit he BLOCKED (GuardStun, the user's call, 2026-10-25):
        // the guard stays up through it, so a combo cannot break a block that can still pay.
        bool stunLocksGuard = IsInHitstun && !(combatSystem != null && combatSystem.GuardStun);
        // A guard broken by stamina stays down through the break's stun AND the recovery after it.
        bool guardBroken = combatSystem != null && combatSystem.GuardBreakRecovering;
        isBlocking = currentInput.blockHeld && !isDashing && !attacking && !stunLocksGuard && !guardBroken && stats.currentStamina > 0;

        if (isBlocking && stats.DrainStamina(Time.deltaTime, blockStaminaDrainPerSecond))
        {
            // The hold itself emptied the bar: guard drops this frame.
            isBlocking = false;
        }

        if (wasBlocking && !isBlocking && currentInput.blockHeld && stats.currentStamina <= 0)
        {
            Debug.Log($"P{playerNumber}: Block BROKEN - out of stamina");
        }

        if (animatorHasBlocking && animator != null && wasBlocking != isBlocking)
        {
            animator.SetBool(ANIM_BLOCKING, isBlocking);
        }
    }

    /// <summary>
    /// Drives the BlockLegs layer: while the guard is up AND the fighter is walking, the
    /// lower-body walk blend fades in over the block pose, so the feet step instead of sliding.
    /// Standing still (or doing anything else) fades it straight back out and the full authored
    /// block stance returns.
    /// </summary>
    private void UpdateBlockLegs()
    {
        if (blockLegsLayer < 0 || animator == null) return;
        bool walking = isBlocking && !IsInHitstun && Mathf.Abs(currentInput.moveDirection) > 0.05f;
        float target = walking ? 1f : 0f;
        blockLegsWeight = Mathf.MoveTowards(blockLegsWeight, target, Time.deltaTime / 0.12f);
        animator.SetLayerWeight(blockLegsLayer, blockLegsWeight);
    }

    /// <summary>
    /// Called when an attack starts. Returns true (and uses up the window) if the
    /// attack was started inside the dash-counter window after a dodge.
    /// </summary>
    public bool ConsumeDashCounter()
    {
        bool ready = dashCounterTimer > 0f;

        // Tell the tester how late the attack was when a dodge's window has already closed.
        float sinceDodge = Time.time - lastDodgeTime;
        if (!ready && lastDodgeTime >= 0f && sinceDodge < 3f)
        {
            Debug.Log($"P{playerNumber}: dash counter MISSED - attack came {sinceDodge:0.00}s after the dodge " +
                      $"(window is {BalanceConfig.DASH_COUNTER_WINDOW}s)");
        }

        dashCounterTimer = 0f;
        lastDodgeTime = -1f;
        return ready;
    }

    /// <summary>
    /// Called when this fighter PARRIED an incoming strike. No damage and no stamina cost were
    /// taken; this plays the guard jolt, opens the counter window and charges the super bar.
    /// The attacker's side is <see cref="NotifyParried"/>.
    /// </summary>
    public void NotifyParry()
    {
        // The window has done its job. The cooldown set when the attempt started stands, so the
        // reward is being instantly actionable with a counter open, not a free second attempt.
        parryActiveTimer = 0f;

        // Raising the guard is free; the parry pays only when it CONNECTS (so block drain can be
        // zero without every guard raise taxing the bar).
        if (parryStaminaCost > 0 && stats != null) stats.UseStamina(parryStaminaCost);

        if (animatorHasParry && animator != null)
        {
            animator.SetFloat(ANIM_PARRY_SPEED, Mathf.Max(0.1f, parryAnimSpeed));
            animator.SetTrigger(ANIM_PARRY_SUCCESS);
        }
        else PlayHitAnimation(HitSide.Straight);   // no Parry_Success state yet: guard jolt instead

        parryCounterTimer = parryCounterWindow;
        parryFlashTimer = BalanceConfig.CLASH_HUD_FLASH_DURATION;
        if (stats != null && parrySuperGain > 0) stats.AddSuperBar(parrySuperGain);
        RecoverHealth(healOnParry, "parry");

        // A parry is a clean defence, so it deliberately leaves any pending dash counter alone.
        Debug.Log($"P{playerNumber}: PARRY - counter window {parryCounterWindow:0.00}s, +{parrySuperGain} super");
    }

    /// <summary>
    /// Gives health back for a successful defence and flashes it on the HUD. One place, so dodge,
    /// parry and counter hit cannot drift apart, and so the cap is applied to all three.
    /// </summary>
    /// <param name="fraction">Share of max health, from the serialized per-fighter values.</param>
    /// <param name="reason">What earned it, for the log.</param>
    public void RecoverHealth(float fraction, string reason)
    {
        if (stats == null || isDead) return;
        int given = stats.HealFraction(fraction, healCapPerRound);
        if (given <= 0) return;

        healFlashAmount = given;
        healFlashTimer = healFlashDuration;
        if (debugLogs)
        {
            Debug.Log($"P{playerNumber}: +{given} hp from the {reason} " +
                      $"({stats.currentHealth}/{stats.maxHealth})");
        }
    }

    /// <summary>
    /// Called instead of <see cref="NotifyHit"/> when a strike takes this fighter to zero health:
    /// plays the death animation rather than a flinch. The KO screen freezes the Fighter component
    /// a moment later, which does not stop the Animator, so the clip plays out.
    /// </summary>
    public void NotifyDeath()
    {
        if (isDead) return;
        isDead = true;
        isBlocking = false;
        isDashing = false;
        isRunning = false;
        dashVelocity = Vector3.zero;
        knockbackVelocity = Vector3.zero;

        if (animatorHasBlocking && animator != null) animator.SetBool(ANIM_BLOCKING, false);
        if (animatorHasRunning && animator != null) animator.SetBool(ANIM_RUNNING, false);
        if (blockLegsLayer >= 0 && animator != null) { blockLegsWeight = 0f; animator.SetLayerWeight(blockLegsLayer, 0f); }
        if (animatorHasDeath && animator != null) animator.SetTrigger(ANIM_DEATH);
        else Debug.Log($"P{playerNumber}: DEAD (animator has no '{ANIM_DEATH}' trigger yet)");

        Debug.Log($"P{playerNumber}: DEAD");
    }

    /// <summary>
    /// Called on the ATTACKER when their strike was parried: the straight stagger plus
    /// <paramref name="stunFrames"/> of hitstun, which is what leaves them open.
    /// </summary>
    public void NotifyParried(int stunFrames)
    {
        PlayHitAnimation(HitSide.Straight);
        if (combatSystem != null) combatSystem.ApplyHitstun(stunFrames);

        dashCounterTimer = 0f;
        lastDodgeTime = -1f;
    }

    /// <summary>
    /// Called when an attack starts. Returns true (and uses up the window) if the attack was
    /// started inside the counter window opened by a parry.
    /// </summary>
    public bool ConsumeParryCounter()
    {
        bool ready = parryCounterTimer > 0f;
        parryCounterTimer = 0f;
        return ready;
    }

    /// <summary>Damage multiplier for an attack started inside the parry-counter window.</summary>
    public float ParryCounterDamageMultiplier => parryCounterDamageMultiplier;

    /// <summary>
    /// Called when this fighter's attack met an equal one. Cancels the swing (done by
    /// <see cref="CombatSystem"/>), plays the straight flinch as a recoil, locks the fighter
    /// for the clash stagger and shoves it <paramref name="awayFromOpponent"/>.
    /// </summary>
    public void NotifyClash(Vector3 awayFromOpponent)
    {
        PlayHitAnimation(HitSide.Straight);
        if (combatSystem != null) combatSystem.ApplyHitstun(BalanceConfig.CLASH_STAGGER_FRAMES);

        awayFromOpponent.y = 0f;
        if (awayFromOpponent.sqrMagnitude > 0.0001f && BalanceConfig.CLASH_PUSHBACK_DURATION > 0f)
        {
            knockbackVelocity = awayFromOpponent.normalized *
                                (BalanceConfig.CLASH_PUSHBACK_DISTANCE / BalanceConfig.CLASH_PUSHBACK_DURATION);
            knockbackTimer = BalanceConfig.CLASH_PUSHBACK_DURATION;
        }

        clashFlashTimer = BalanceConfig.CLASH_HUD_FLASH_DURATION;
    }

    /// <summary>Slides the fighter along the clash knockback for its duration.</summary>
    private bool lungeStarted;
private float lungeTotal;    // distance the lunge covers in all
private float lungeCovered;  // distance moved so far
private float lungeDuration; // seconds: startup + half the active window, eased out

/// <summary>Fresh stats, no dash/knockback/block/attack, back on the spawn mark (new match or back to the menu).</summary>
public void ResetForMatch(Vector3 position, Quaternion rotation)
{
    stats = new FighterStats(MaxHealth, MaxStamina, Armor);
    isDashing = false;
    isRunning = false;
    dashTimer = 0f;
    dashCounterTimer = 0f;
    lastDodgeTime = -1f;
    knockbackVelocity = Vector3.zero;
    knockbackTimer = 0f;
    isBlocking = false;
    parryCounterTimer = 0f;
    parryFlashTimer = 0f;
    parryActiveTimer = 0f;
    parryCooldownTimer = 0f;
    healFlashTimer = 0f;
    healFlashAmount = 0;
    isDead = false;
    lastDodgeDustTime = -99f;
    lungeStarted = false;
    if (combatSystem != null) combatSystem.ResetForMatch();
    transform.SetPositionAndRotation(position, rotation);
    if (rb != null) { rb.position = position; rb.rotation = rotation; rb.linearVelocity = Vector3.zero; }
    Physics.SyncTransforms();
    facingRight = (playerNumber == 1);
    ResetAnimator();
}

/// <summary>
/// Puts the state machine back on the idle. This is not optional housekeeping: the Death state is
/// deliberately a dead end (Any State -> Death with no exit transition) so a corpse stays down,
/// which means nothing would ever leave it again - without this a rematch, or a whole new match,
/// started with a fighter lying on the floor.
/// </summary>
private void ResetAnimator()
{
    if (animator == null || animator.runtimeAnimatorController == null) return;

    // Clear every queued trigger first. A Death or GetHit trigger still sitting in the queue would
    // fire again on the next frame and drop the fighter straight back down.
    AnimatorControllerParameter[] parameters = animator.parameters;
    for (int i = 0; i < parameters.Length; i++)
    {
        if (parameters[i].type == AnimatorControllerParameterType.Trigger) animator.ResetTrigger(parameters[i].name);
    }

    animator.SetFloat("MoveSpeed", 0f);
    if (animatorHasBlocking) animator.SetBool(ANIM_BLOCKING, false);
    if (animatorHasRunning) animator.SetBool(ANIM_RUNNING, false);
    if (blockLegsLayer >= 0) { blockLegsWeight = 0f; animator.SetLayerWeight(blockLegsLayer, 0f); }

    if (idleStateHash != 0) animator.Play(idleStateHash, 0, 0f);
    else animator.Rebind();          // no idle state on this controller: full reset to the layer default
    animator.Update(0f);             // apply it now, so nobody sees a frame of the old pose
}

/// <summary>Finds the idle state once, by name, for <see cref="ResetAnimator"/>.</summary>
private void ResolveIdleState()
{
    idleStateHash = 0;
    if (animator == null || animator.runtimeAnimatorController == null) return;

    string[] candidates =
    {
        idleStateName,
        // The controllers name their states after the CHARACTER (Bjorn_Idle), not after the shared
        // "test Fighter" FighterData, so without this every match reset fell through to
        // Animator.Rebind() - the fallback the dead-fighter-stays-dead fix exists to avoid.
        CharacterName + "_Idle",
        fighterData != null ? fighterData.FighterName + "_Idle" : null,
        fighterData != null ? fighterData.name + "_Idle" : null,
        "Idle"
    };
    for (int i = 0; i < candidates.Length; i++)
    {
        if (string.IsNullOrEmpty(candidates[i])) continue;
        int hash = Animator.StringToHash(candidates[i]);
        if (!animator.HasState(0, hash)) continue;
        idleStateHash = hash;
        if (debugLogs) Debug.Log($"P{playerNumber}: match reset will return to the '{candidates[i]}' state");
        return;
    }
    Debug.LogWarning($"Fighter on {name}: no idle animator state found (tried the FighterData name and 'Idle'); " +
                     "a match reset will fall back to Animator.Rebind(). Set idleStateName if the state has another name.");
}

private bool InAttackStartup()
{
    if (combatSystem == null || !combatSystem.IsAttacking || combatSystem.CurrentAttack == null) return false;
    return combatSystem.AttackTime < combatSystem.CurrentAttack.StartupFrames * BalanceConfig.FRAME_TIME;
}

/// <summary>
/// Attack lunge: on the first frame of an attack, work out how far the fighter must close so the blade
/// lands at the attack's stop distance, then cover it evenly over the startup frames. The stock swings
/// reach different distances, so without this a strike only connects from point-blank range.
/// </summary>
private void HandleAttackLunge()
{
    if (combatSystem == null || opponent == null) return;
    if (!combatSystem.IsAttacking || combatSystem.CurrentAttack == null)
    {
        lungeStarted = false;
        return;
    }
    AttackData a = combatSystem.CurrentAttack;
    if (!lungeStarted)
    {
        lungeStarted = true;
        Vector3 gap = opponent.transform.position - transform.position;
        gap.y = 0f;
        float dist = gap.magnitude;
        lungeTotal = Mathf.Clamp(dist - a.LungeStopDistance, 0f, a.LungeMaxDistance);
        lungeCovered = 0f;
        lungeDuration = (a.StartupFrames + a.ActiveFrames * 0.5f) * BalanceConfig.FRAME_TIME;
        lungeTotal = Mathf.Min(lungeTotal, BalanceConfig.ATTACK_LUNGE_MAX_SPEED * lungeDuration);
        if (debugLogs && lungeTotal > 0.01f)
            Debug.Log($"P{playerNumber}: {a.AttackName} lunge {lungeTotal:0.00} units (gap {dist:0.00} -> {a.LungeStopDistance:0.00}) over {lungeDuration:0.00}s");
    }
    if (lungeTotal <= 0f || lungeCovered >= lungeTotal || IsInHitstun) return;
    Vector3 toOpp = opponent.transform.position - transform.position;
    toOpp.y = 0f;
    if (toOpp.sqrMagnitude < 1e-4f) return;
    // Ease-out: fast off the mark, settling into the strike (quadratic), so it reads as a step, not a slide.
    float u = lungeDuration > 1e-4f ? Mathf.Clamp01(combatSystem.AttackTime / lungeDuration) : 1f;
    float wanted = lungeTotal * (1f - (1f - u) * (1f - u));
    float step = wanted - lungeCovered;
    // Never closer than the stop distance even if the opponent walked in
    step = Mathf.Min(step, Mathf.Max(0f, toOpp.magnitude - a.LungeStopDistance));
    if (step <= 0f) return;
    transform.position += toOpp.normalized * step;
    lungeCovered += step;
}

private void HandleKnockback()
    {
        if (knockbackTimer <= 0f) return;

        float step = Mathf.Min(Time.deltaTime, knockbackTimer);
        knockbackTimer -= Time.deltaTime;
        transform.position += knockbackVelocity * step;

        if (knockbackTimer <= 0f)
        {
            knockbackTimer = 0f;
            knockbackVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Called when this fighter takes a hit. <paramref name="side"/> is authored on the
    /// attack and picks the get-hit animation; <paramref name="hitstunFrames"/> locks the
    /// fighter out of attacking, dashing and walking while the flinch plays. Getting hit
    /// also means the dash did not dodge anything, so any pending dash counter is lost.
    /// </summary>
    public void NotifyHit(HitSide side, int hitstunFrames, bool dodgeable = false)
    {
        PlayHitAnimation(side);
        if (combatSystem != null) combatSystem.ApplyHitstun(hitstunFrames, dodgeable);

        if (isDashing) hitDuringDash = true;

        if (dashCounterTimer > 0f)
        {
            Debug.Log($"P{playerNumber}: dash counter LOST - got hit before attacking");
        }
        dashCounterTimer = 0f;
        lastDodgeTime = -1f;

        healthRegenDelayTimer = healthRegenDelay;
    }

    /// <summary>
    /// Called when this fighter BLOCKS a hit (the user's call, 2026-10-25): the stun locks
    /// attacking, walking and new raises like any other, but the guard it is holding stays UP
    /// (no flinch animation - the block pose is the point), and when the blocked move can string
    /// on, a dash is still allowed so the defender can dodge out of the combo instead of paying
    /// the escalating block costs. Away from the strike only - comesFrom judges the direction.
    /// </summary>
    public void NotifyBlockedHit(int hitstunFrames, bool dodgeable)
    {
        if (combatSystem != null) combatSystem.ApplyHitstun(hitstunFrames, dodgeable, guardHolds: true);
        healthRegenDelayTimer = healthRegenDelay;
    }

    /// <summary>
    /// Sets the animator's HitSide int from <paramref name="side"/> and fires the GetHit
    /// trigger. Does nothing until the controller defines both parameters, so the
    /// animation can be wired up later without warning spam.
    /// </summary>
    private void PlayHitAnimation(HitSide side)
    {
        if (animator == null) return;

        if (!animatorHasHitParams)
        {
            Debug.Log($"P{playerNumber}: hit from the {side} (animator has no '{ANIM_HIT_SIDE}' int / '{ANIM_GET_HIT}' trigger yet)");
            return;
        }

        animator.SetInteger(ANIM_HIT_SIDE, (int)side);
        animator.SetTrigger(ANIM_GET_HIT);
    }

    /// <summary>True if the animator controller defines every parameter in <paramref name="names"/>.</summary>
    private bool HasAnimatorParameters(params string[] names)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;

        foreach (string name in names)
        {
            bool found = false;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name == name) { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>
    /// Routes attack-button presses to <see cref="CombatSystem.TryAttack"/>. The special modifier
    /// (L1 on the pad, U on the keyboard, the latching SP button on touch) is a HOLD, so it is read
    /// on the frame the attack button is pressed: modifier + button picks a special, the bare button
    /// picks the normal.
    /// </summary>
    private void HandleCombatInput()
    {
        if (combatSystem == null) return;

        bool special = currentInput.specialModifier;

        if (currentInput.lightAttack)
        {
            combatSystem.TryAttack(AttackType.Light, special);
        }
        else if (currentInput.mediumAttack)
        {
            combatSystem.TryAttack(AttackType.Medium, special);
        }
        else if (currentInput.heavyAttack)
        {
            combatSystem.TryAttack(AttackType.Heavy, special);
        }
    }

/// <summary>
/// Returns the other Fighter in the scene, or null if there isn't one.
/// </summary>
/// <summary>
/// Picks the fighter on the OTHER side. The scene holds the whole roster and the menu benches
/// everyone who is not in this match, so an active fighter with the other player number is the
/// opponent; anyone switched off is ignored. Call <see cref="RefreshOpponent"/> after the pair
/// changes - picking the first Fighter in the scene used to hand Richard a benched Bjorn.
/// </summary>
private Fighter FindOpponent()
{
    Fighter fallback = null;
    foreach (Fighter other in FindObjectsByType<Fighter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
    {
        if (other == this) continue;
        if (other.PlayerNumber != playerNumber) return other;
        fallback = fallback != null ? fallback : other;
    }
    return fallback;
}

/// <summary>Re-reads who this fighter is up against, after the menu has picked the pair.</summary>
public void RefreshOpponent()
{
    opponent = FindOpponent();
    if (opponent != null) IgnoreOpponentBodyCollision();
}

/// <summary>
/// Rotates the fighter on the ground plane to face the opponent. Facing is locked while
/// attacking. During a dash and the counter window that follows a dodge it turns much faster,
/// so a sidestep squares up on the opponent immediately instead of drifting past them.
/// </summary>
private void HandleFacing()
{
    if (opponent == null) return;
    // Facing is locked from the active frames on; during the startup the attacker still squares up so
    // every strike, however the clip swings, is thrown at the opponent's centre.
    if (combatSystem != null && combatSystem.IsAttacking && !InAttackStartup()) return;
    float speed = (isDashing || dashCounterTimer > 0f) ? dodgeTurnSpeed : turnSpeed;

    Vector3 toOpponent = opponent.transform.position - transform.position;
    toOpponent.y = 0f;
    if (toOpponent.sqrMagnitude < 0.0001f) return;

    // Which screen side the opponent is on decides how left/right input maps to
    // forward/backward. Measured along the camera's right so it stays correct as the
    // camera orbits; the dead zone stops it flickering when lined up in depth.
    float screenSide = cameraTransform != null
        ? Vector3.Dot(toOpponent, cameraTransform.right)
        : toOpponent.x;
    if (Mathf.Abs(screenSide) > FACING_FLIP_DEAD_ZONE)
    {
        facingRight = screenSide > 0f;
    }

    Quaternion targetRotation = Quaternion.LookRotation(toOpponent);
    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, speed * Time.deltaTime);
}

private void HandleMovement()
{
    float moveSpeed = fighterData.MovementSpeed / 10f;

    // Guard up = slower feet (the BlockLegs layer animates them; see UpdateBlockLegs).
    if (isBlocking) moveSpeed *= BalanceConfig.BLOCK_WALK_SPEED_MULTIPLIER;

    // Input is screen-based (-1 = left, +1 = right); convert it to toward/away
    // from the opponent along the fighter's facing.
    float forwardAmount = facingRight ? currentInput.moveDirection : -currentInput.moveDirection;
    Vector3 forward = transform.forward;
    forward.y = 0f;
    Vector3 desiredMove = forward.normalized * forwardAmount * moveSpeed * Time.deltaTime;

    // No walking while flinching from a hit (the get-hit animation would slide along the ground)
    if (IsInHitstun)
    {
        desiredMove = Vector3.zero;
        forwardAmount = 0f; // also stops the walk animation blending under the flinch
    }

    // No walking while locked into a dash (otherwise it stacks on top of dash distance)
    // or while running (HandleRun owns the movement then)
    if (isDashing || isRunning)
    {
        desiredMove = Vector3.zero;
    }

    // Block movement if it would walk into the opponent
    if (currentInput.moveDirection != 0f && WouldCollideWithOpponent(desiredMove))
    {
        desiredMove = Vector3.zero;
    }

    transform.position += desiredMove;

    // Send movement to Animator for walk animations
    if (animator != null)
    {
        animator.SetFloat("MoveSpeed", forwardAmount);
    }
}

/// <summary>
/// The held double-flick RUN (the user's call, 2026-10-25): one flick of the right stick is a
/// dash, a second flick toward the opponent inside the window - held - keeps the fighter
/// running at RUN_SPEED_MULTIPLIER times his walk until the stick returns to centre. The
/// second flick starts the run instead of a second dash (no extra stamina, no i-frames), and
/// the run ends on anything that interrupts the fighter: a dash, an attack, raising the
/// guard, hitstun, or reaching the opponent's body. Keyboard route: Run key held + a
/// direction toward the opponent. Runs AFTER HandleDash, so a run latched during a forward
/// dash picks up seamlessly the frame the dash ends.
/// </summary>
private void HandleRun()
{
    bool wantsRun = RunInputTowardOpponent();

    if (isRunning)
    {
        bool busy = combatSystem != null && (combatSystem.IsAttacking || combatSystem.IsInHitstun);
        if (!wantsRun || busy || isDashing || isBlocking || isDead)
        {
            StopRun(!wantsRun ? "stick released" : "interrupted");
            return;
        }

        // The run moves at this fighter's own dash speed, so the dash table is the run table.
        float runSpeed = dashDistance / Mathf.Max(dashDuration, 0.05f);
        Vector3 forward = transform.forward;
        forward.y = 0f;
        Vector3 step = forward.normalized * runSpeed * Time.deltaTime;
        if (WouldCollideWithOpponent(step))
        {
            StopRun("reached the opponent");
            return;
        }

        // Running costs stamina per unit of ground covered (DrainStamina is a plain multiply, so
        // distance x per-unit works exactly like seconds x per-second, fractional carry and all).
        // Regen is paused while running (HandleStaminaRecovery); an empty bar ends the run.
        if (stats != null && stats.DrainStamina(step.magnitude, BalanceConfig.RUN_STAMINA_PER_UNIT))
        {
            StopRun("out of stamina");
            return;
        }
        transform.position += step;
        return;
    }

    if (!wantsRun || isDashing || isBlocking || isDead || IsInHitstun) return;
    if (combatSystem != null && combatSystem.IsAttacking) return;
    if (stats == null || stats.currentStamina <= 0) return;   // no starting a run on an empty bar

    // Don't start a run whose first step is already blocked by the opponent's body. Without this
    // the run that stopped at contact restarts the very next frame (the stick is still held) and
    // stops again, flapping the Running bool - and the animator - every other frame.
    Vector3 firstStep = transform.forward;
    firstStep.y = 0f;
    firstStep = firstStep.normalized * (dashDistance / Mathf.Max(dashDuration, 0.05f))
                * Mathf.Max(Time.deltaTime, BalanceConfig.FRAME_TIME);
    if (WouldCollideWithOpponent(firstStep)) return;

    StartRun();
}

/// <summary>True while the run input is held and points at the opponent (screen-space, like the dash).</summary>
private bool RunInputTowardOpponent()
{
    if (!currentInput.runHeld) return false;
    float forwardAmount = facingRight ? currentInput.runDirection.x : -currentInput.runDirection.x;
    return forwardAmount >= BalanceConfig.RUN_MIN_FORWARD;
}

private void StartRun()
{
    isRunning = true;
    if (animatorHasRunning && animator != null) animator.SetBool(ANIM_RUNNING, true);
    if (debugLogs) Debug.Log($"P{playerNumber}: RUN started");
}

private void StopRun(string reason)
{
    if (!isRunning) return;
    isRunning = false;
    if (animatorHasRunning && animator != null) animator.SetBool(ANIM_RUNNING, false);
    if (debugLogs) Debug.Log($"P{playerNumber}: run ended - {reason}");
}

/// <summary>
/// Handles 8-way ground dashing (forward, back, left, right and the four diagonals,
/// relative to the fighter's facing). Reads dashDirection from currentInput,
/// applies stamina cost, locks the fighter into a dash state, and moves
/// the transform by dashVelocity each frame until the dash duration expires.
/// </summary>
private void HandleDash()
{
    // If currently dashing, advance the dash
    if (isDashing)
    {
        // A hit mid-dash (the punishable tail) stops the slide: hitstun means you stop moving.
        // Without this the dash carried the fighter ~2 world units THROUGH the stun (measured),
        // which is what let a stunned player drift out of a parry counter's reach. The serial
        // tells a NEW hit from the stun the dash legitimately STARTED under (a combo-stun dodge):
        // only a new hit ends the dash.
        if (IsInHitstun && combatSystem != null && combatSystem.StunSerial != dashStunSerial)
        {
            EndDash("hit mid-dash");
            return;
        }
        // Trim the last frame so the dash covers exactly dashDistance, never more
        float frameTime = Mathf.Min(Time.deltaTime, dashDuration - dashTimer);
        dashTimer += Time.deltaTime;
        // A side dash arcs around the opponent instead of running off on a tangent, so the dodge ends at
        // the same distance, squared up, ready to counter.
        if (opponent != null && Mathf.Abs(dashRightAmount) > Mathf.Abs(dashForwardAmount))
        {
            Vector3 toOpp = opponent.transform.position - transform.position;
            toOpp.y = 0f;
            if (toOpp.sqrMagnitude > 1e-4f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, toOpp.normalized);
                float sign = Vector3.Dot(dashVelocity, side) >= 0f ? 1f : -1f;
                dashVelocity = side * sign * dashVelocity.magnitude;
            }
        }

        // Move only as far as the opponent's body; reaching it ends the dash early
        Vector3 step = dashVelocity * frameTime;
        float allowed = OpponentContactFraction(step);
        transform.position += step * allowed;

        if (allowed < 1f)
        {
            EndDash("stopped at opponent");
        }
        else if (dashTimer >= dashDuration)
        {
            EndDash("ENDED");
        }
        return;
    }

    // Can't initiate a new dash if attacking, flinching from a hit, or no input. The one
    // exception is a COMBO stun (a clean, combo-able hit): the victim cannot block or walk,
    // but a timed dash is still allowed - that is the whole escape the combo rule leaves open.
    if (currentInput.dashDirection == Vector2.zero) return;
    if (combatSystem != null && (combatSystem.IsAttacking ||
                                 (combatSystem.IsInHitstun && !combatSystem.ComboStun))) return;

    // Check stamina
    if (Stats == null || !Stats.HasStamina(dashStaminaCost))
    {
        Debug.Log($"P{playerNumber}: Dash BLOCKED - not enough stamina");
        return;
    }

    // START THE DASH
    Stats.UseStamina(dashStaminaCost);
    isDashing = true;
    dashTimer = 0f;
    dashStunSerial = combatSystem != null ? combatSystem.StunSerial : 0;
    dashStartPosition = transform.position;
    dodgedAttackThisDash = false;
    hitDuringDash = false;
    dashCounterTimer = 0f;
    lastDodgeTime = -1f;

    // Convert 2D input into a fighter-relative direction on the ground plane (never Y).
    // Horizontal input = forward/backward along the fighter's facing,
    // vertical input = sidestep (up = into the screen, down = toward the camera).
    dashForwardAmount = facingRight ? currentInput.dashDirection.x : -currentInput.dashDirection.x;
    dashRightAmount = facingRight ? -currentInput.dashDirection.y : currentInput.dashDirection.y;

    Vector3 forward = transform.forward;
    Vector3 right = transform.right;
    forward.y = 0f;
    right.y = 0f;

    Vector3 dir3D = (forward.normalized * dashForwardAmount + right.normalized * dashRightAmount).normalized;
    dashVelocity = dir3D * (dashDistance / dashDuration);

    if (animatorHasDash && animator != null)
    {
        int dir = Mathf.Abs(dashRightAmount) > Mathf.Abs(dashForwardAmount)
            ? (dashRightAmount > 0f ? 3 : 2)
            : (dashForwardAmount >= 0f ? 0 : 1);
        animator.SetInteger(ANIM_DASH_DIR, dir);
        animator.SetTrigger(ANIM_DASH);
    }

    if (dustEffects) HitEffects.SpawnDust(FeetPosition(), BodyRadius, 1f);
    Debug.Log($"P{playerNumber}: Dash STARTED {DescribeDash()} velocity={dashVelocity}");
}

/// <summary>
/// Decides what an incoming strike does to this fighter while dashing, from the
/// strike's origin and the dash direction (both relative to this fighter's facing):
///   Overhead  - sidestep evades and opens a counter; back-dash evades but is too far to counter.
///   Sideways  - dash away from the incoming side (or straight back) evades and opens a counter;
///               dash toward it runs into the blade.
///   Thrust    - only a sidestep evades; backing off doesn't get out of its way.
///   Forward   - dashing into any strike takes extra damage.
/// Not dashing means a normal hit.
/// </summary>
public DashHitOutcome ResolveDashAgainst(AttackOrigin origin)
{
    if (!isDashing) return DashHitOutcome.Hit;

    const float threshold = 0.3f; // a diagonal dash has ~0.7 on each axis, a straight one 1.0
    bool forward = dashForwardAmount > threshold;
    bool back = dashForwardAmount < -threshold;
    bool sideways = Mathf.Abs(dashRightAmount) > threshold;

    if (forward && !sideways) return DashHitOutcome.HitAmplified;

    // Only the HEAD of the dash evades (Plan_Balance 2.5): past the i-frames the tail is an
    // ordinary target, so a dash nobody can hit does not exist and COVERAGE is the per-fighter
    // dial. Until 2026-10-23 DASH_IFRAME_DURATION was dead code and the whole dash evaded.
    // The forward-into-the-blade amplification above deliberately stays live for the whole
    // dash - running onto a strike late is still running onto it.
    if (dashTimer > dashIFrameDuration) return DashHitOutcome.Hit;

    switch (origin)
    {
        case AttackOrigin.Overhead:
            if (back) return DashHitOutcome.EvadedNoCounter;
            return DashHitOutcome.Evaded; // left, right, forward-left, forward-right

        case AttackOrigin.Thrust:
            if (back) return DashHitOutcome.Hit;
            return DashHitOutcome.Evaded;

        default:
            // Horizontal swing. The attacker's right arrives at our left, since we face each other.
            float incomingSide = origin == AttackOrigin.AttackerRight ? -1f : 1f;
            if (!sideways) return DashHitOutcome.Evaded; // straight back
            bool towardBlade = dashRightAmount * incomingSide > 0f;
            return towardBlade ? DashHitOutcome.HitAmplified : DashHitOutcome.Evaded;
    }
}

/// <summary>
/// Called when this fighter's dash made an attack miss. A clean evade sets up the
/// counter window that opens when the dash ends.
/// </summary>
public void NotifyEvaded(bool counterAllowed)
{
    if (counterAllowed) dodgedAttackThisDash = true;

    // Both evade paths (hitbox arming and trigger contact) can report the SAME dodge, so the puff
    // and the health it gives back are gated together on one short guard - otherwise a dodge pays
    // out twice.
    bool firstReport = Time.time - lastDodgeDustTime > 0.15f;
    if (!firstReport) return;
    lastDodgeDustTime = Time.time;

    // A successful dodge gets its own burst (pale cloud + cyan ring), not the warm dash dust -
    // bigger and colour-contrasted on the user's call (2026-10-23): the plain puff was invisible.
    if (dustEffects) HitEffects.SpawnDodgeBurst(FeetPosition(), BodyRadius, Mathf.Max(1f, dodgeDustStrength));
    RecoverHealth(healOnDodge, "dodge");

    // A SUCCESSFUL dash costs nothing: the dodge refunds the dash's own stamina (the user's
    // call, 2026-10-23). Behind the firstReport guard above, so the two evade paths (hitbox
    // arming and trigger contact) cannot pay one dodge out twice.
    if (Stats != null)
    {
        Stats.AddStamina(dashStaminaCost);
        Debug.Log($"P{playerNumber}: successful dodge - {dashStaminaCost} dash stamina refunded");
    }
}

/// <summary>Health for landing the punish a dodge or parry earned (called by CombatSystem).</summary>
public void RecoverHealthForCounterHit() => RecoverHealth(healOnCounterHit, "counter hit");

/// <summary>Human-readable dash direction for logs, e.g. "forward-left".</summary>
public string DescribeDash()
{
    const float threshold = 0.3f;
    string along = dashForwardAmount > threshold ? "forward" : dashForwardAmount < -threshold ? "back" : "";
    string side = dashRightAmount > threshold ? "right" : dashRightAmount < -threshold ? "left" : "";
    if (along != "" && side != "") return along + "-" + side;
    return along + side;
}

private void EndDash(string reason)
{
    isDashing = false;
    dashTimer = 0f;
    dashVelocity = Vector3.zero;
    if (dustEffects) HitEffects.SpawnDust(FeetPosition(), BodyRadius, 0.8f);
    Debug.Log($"P{playerNumber}: Dash {reason}");

    // A dash that made a strike miss (and wasn't hit by anything else) opens the counter window
    bool dodged = dodgedAttackThisDash && !hitDuringDash;
    dodgedAttackThisDash = false;
    hitDuringDash = false;
    if (dodged)
    {
        lastDodgeTime = Time.time;
        dashCounterTimer = BalanceConfig.DASH_COUNTER_WINDOW;
        Debug.Log($"P{playerNumber}: DODGE - dash counter window open for {BalanceConfig.DASH_COUNTER_WINDOW}s");
    }
}

[Header("Collision")]
[SerializeField] private float fighterRadius = 0.6f;

/// <summary>Ground point between the feet (from the Humanoid avatar), for dust and landing effects.</summary>
public Vector3 FeetPosition()
{
    if (animator != null && animator.isHuman)
    {
        Transform l = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform r = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (l != null && r != null)
        {
            Vector3 p = (l.position + r.position) * 0.5f;
            p.y = transform.position.y;
            return p;
        }
    }
    return transform.position;
}

/// <summary>Standoff radius in world units: how close fighters may walk or dash to each other.</summary>
public float Radius => fighterRadius;

private float bodyRadius; // world-unit radius of the body capsule hitboxes actually touch

/// <summary>
/// Radius of the body collider in world units (capsule radius times the root scale).
/// This is what an attack's hitbox has to reach; falls back to <see cref="Radius"/> without a capsule.
/// </summary>
public float BodyRadius => bodyRadius > 0f ? bodyRadius : fighterRadius;

private void CacheBodyRadius()
{
    CapsuleCollider capsule = GetComponent<CapsuleCollider>();
    if (capsule == null) capsule = GetComponentInChildren<CapsuleCollider>();
    if (capsule == null) { bodyRadius = 0f; return; }

    Vector3 s = capsule.transform.lossyScale;
    bodyRadius = capsule.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
}

/// <summary>
/// How much of <paramref name="step"/> (0-1) this fighter can travel before touching
/// the opponent's body radius. 1 means the whole step is clear. Moves that don't
/// close the gap are never limited, so overlapping fighters can still separate.
/// </summary>
private float OpponentContactFraction(Vector3 step)
{
    if (opponent == null) return 1f;

    // Ground-plane circle test: solve |offset + t * move| = contact distance for t
    Vector2 offset = new Vector2(
        transform.position.x - opponent.transform.position.x,
        transform.position.z - opponent.transform.position.z);
    Vector2 move = new Vector2(step.x, step.z);
    float contactDistance = fighterRadius * 2f;

    float a = Vector2.Dot(move, move);
    float b = 2f * Vector2.Dot(offset, move);
    float c = Vector2.Dot(offset, offset) - contactDistance * contactDistance;

    if (a < 0.000001f || b >= 0f) return 1f; // not moving, or not moving toward the opponent
    if (c <= 0f) return 0f;                  // already touching and trying to move closer

    float discriminant = b * b - 4f * a * c;
    if (discriminant < 0f) return 1f;        // path misses the opponent entirely

    float t = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
    return Mathf.Clamp01(t);
}

/// <summary>
/// Turns off physics contact between the two fighters' body colliders. Spacing is
/// handled in code (<see cref="fighterRadius"/>), so physics never shoves or lifts
/// a fighter. Trigger colliders (hitboxes) are left alone so hits still register.
/// </summary>
private void IgnoreOpponentBodyCollision()
{
    Collider[] myColliders = GetComponentsInChildren<Collider>();
    Collider[] theirColliders = opponent.GetComponentsInChildren<Collider>();

    foreach (Collider mine in myColliders)
    {
        if (mine.isTrigger) continue;

        foreach (Collider theirs in theirColliders)
        {
            if (theirs.isTrigger) continue;
            Physics.IgnoreCollision(mine, theirs, true);
        }
    }
}

/// <summary>
/// Returns true if moving by <paramref name="desiredMove"/> would put this
/// fighter inside the opponent's body radius. Blocks fighters from walking
/// through or pushing each other.
/// </summary>
/// 
private bool WouldCollideWithOpponent(Vector3 desiredMove)
{
    Fighter[] allFighters = FindObjectsOfType<Fighter>();
    Vector3 myXZ = new Vector3(transform.position.x, 0f, transform.position.z);
    Vector3 myFutureXZ = new Vector3(
        transform.position.x + desiredMove.x,
        0f,
        transform.position.z + desiredMove.z);

    foreach (Fighter other in allFighters)
    {
        if (other == this) continue;

        Vector3 otherXZ = new Vector3(other.transform.position.x, 0f, other.transform.position.z);
        float distance = Vector3.Distance(myFutureXZ, otherXZ);

        // Only block moves that close the gap, so fighters that already overlap
        // (e.g. after a dash) can still walk apart instead of getting stuck.
        if (distance < fighterRadius * 2f && distance < Vector3.Distance(myXZ, otherXZ))
        {
            return true;
        }
    }
    return false;
}
    /// <summary>
    /// Ticks passive stamina recovery and health regeneration each frame,
    /// at this fighter's own rates. Health regeneration waits out a delay after each hit.
    /// </summary>
    private void HandleStaminaRecovery()
    {
        // Guarding pauses recovery (unless allowed per character), so block has a real cost.
        // Running pauses it too, or the 15/s regen would out-earn the run drain and running
        // would cost nothing.
        if ((!isBlocking || staminaRecoversWhileBlocking) && !isRunning)
        {
            // Recovery runs at ONE rate however low the bar is. The cost of running it down is
            // paid on the attacks instead: below BalanceConfig.WEAKENED_STAMINA_FRACTION a swing
            // is cheaper, weaker and slower (see CombatSystem, Plan_Balance 2.3).
            stats.RecoverStamina(Time.deltaTime, staminaRecoveryPerSecond);
        }

        if (healthRegenDelayTimer > 0f)
        {
            healthRegenDelayTimer -= Time.deltaTime;
        }
        else
        {
            stats.RegenerateHealth(Time.deltaTime, healthRegenPerSecond);
        }
    }
    /// <summary>
    /// True if this fighter is currently facing right (toward +X).
    /// </summary>
    public bool IsFacingRight()
    {
        return facingRight;
    }
}