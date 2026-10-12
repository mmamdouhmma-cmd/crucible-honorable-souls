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

    private Rigidbody rb;
    private Animator animator;
    private bool animatorHasHitParams; // controller defines the HitSide int and GetHit trigger
    private InputData currentInput;
    private bool facingRight;

    private const string ANIM_HIT_SIDE = "HitSide"; // int: 0 = Straight, 1 = Left, 2 = Right
    private const string ANIM_GET_HIT = "GetHit";   // trigger: play the get-hit animation
    private const string ANIM_BLOCKING = "Blocking"; // bool: guard is up (controller holds the block pose)
    private const string ANIM_DASH = "Dash";         // trigger: a dash just started
    private const string ANIM_DASH_DIR = "DashDir";  // int: 0 forward, 1 back, 2 left, 3 right (relative to facing)
    private bool animatorHasBlocking;
    private bool animatorHasDash;

    [Header("Dash")]
    [SerializeField] private float dashDistance = 2.5f;
    [SerializeField] private float dashDuration = 0.25f;   // total dash time in seconds
    [SerializeField] private int dashStaminaCost = 25;
    private bool isDashing = false;
    private float dashTimer = 0f;
    private Vector3 dashVelocity = Vector3.zero;
    private float dashForwardAmount = 0f; // dash direction relative to facing: +1 forward, -1 back
    private float dashRightAmount = 0f;   // +1 right, -1 left
    private Vector3 dashStartPosition;    // where the fighter stood when the current dash began
    public bool IsDashing { get { return isDashing; } }

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

    [Header("Recovery")]
    [Tooltip("Stamina regained per second. Set per character.")]
    [SerializeField] private float staminaRecoveryPerSecond = BalanceConfig.STAMINA_RECOVERY_PER_SECOND;

    [Tooltip("Health regained per second while alive. Keep it small; 0 turns regeneration off. Set per character.")]
    [SerializeField] private float healthRegenPerSecond = BalanceConfig.DEFAULT_HEALTH_REGEN_PER_SECOND;

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
    [SerializeField] private bool dustEffects = false;
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
        animatorHasDash = HasAnimatorParameters(ANIM_DASH, ANIM_DASH_DIR);

        if (fighterData != null)
        {
            stats = new FighterStats(fighterData.MaxHealth, fighterData.MaxStamina, fighterData.Armor);
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

        if (dashCounterTimer > 0f)
        {
            dashCounterTimer -= Time.deltaTime;
        }
        if (clashFlashTimer > 0f)
        {
            clashFlashTimer -= Time.deltaTime;
        }

        HandleFacing();
        HandleKnockback();
        HandleDash();
        HandleMovement();
        HandleAttackLunge();
        HandleStaminaRecovery();
        HandleBlock();
        HandleCombatInput();

    }

    /// <summary>
    /// Block is up while the button is held, unless the fighter is attacking,
    /// dashing, or has no stamina left to absorb a hit. Holding block drains
    /// stamina every frame; when it runs out the guard drops until the bar
    /// recovers above zero.
    /// </summary>
    private void HandleBlock()
    {
        bool wasBlocking = isBlocking;
        bool attacking = combatSystem != null && combatSystem.IsAttacking;
        isBlocking = currentInput.blockHeld && !isDashing && !attacking && stats.currentStamina > 0;

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
    if (fighterData != null) stats = new FighterStats(fighterData.MaxHealth, fighterData.MaxStamina, fighterData.Armor);
    isDashing = false;
    dashTimer = 0f;
    dashCounterTimer = 0f;
    lastDodgeTime = -1f;
    knockbackVelocity = Vector3.zero;
    knockbackTimer = 0f;
    isBlocking = false;
    lungeStarted = false;
    if (combatSystem != null) combatSystem.ResetForMatch();
    transform.SetPositionAndRotation(position, rotation);
    if (rb != null) { rb.position = position; rb.rotation = rotation; rb.linearVelocity = Vector3.zero; }
    Physics.SyncTransforms();
    facingRight = (playerNumber == 1);
    if (animator != null && animator.runtimeAnimatorController != null)
    {
        animator.SetFloat("MoveSpeed", 0f);
        if (animatorHasBlocking) animator.SetBool(ANIM_BLOCKING, false);
    }
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
    public void NotifyHit(HitSide side, int hitstunFrames)
    {
        PlayHitAnimation(side);
        if (combatSystem != null) combatSystem.ApplyHitstun(hitstunFrames);

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
    /// Routes attack-button presses to <see cref="CombatSystem.TryAttack"/>.
    /// </summary>
    private void HandleCombatInput()
    {
        if (combatSystem == null) return;

        if (currentInput.lightAttack)
        {
            combatSystem.TryAttack(AttackType.Light);
        }
        else if (currentInput.mediumAttack)
        {
            combatSystem.TryAttack(AttackType.Medium);
        }
        else if (currentInput.heavyAttack)
        {
            combatSystem.TryAttack(AttackType.Heavy);
        }
    }

/// <summary>
/// Returns the other Fighter in the scene, or null if there isn't one.
/// </summary>
private Fighter FindOpponent()
{
    foreach (Fighter other in FindObjectsOfType<Fighter>())
    {
        if (other != this) return other;
    }
    return null;
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
    if (isDashing)
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

    // Can't initiate a new dash if attacking, flinching from a hit, or no input
    if (currentInput.dashDirection == Vector2.zero) return;
    if (combatSystem != null && (combatSystem.IsAttacking || combatSystem.IsInHitstun)) return;

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
}

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
        if (!isBlocking || staminaRecoversWhileBlocking)
        {
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