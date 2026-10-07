using UnityEngine;

/// <summary>
/// Drives attack state for a single Fighter: looks up <see cref="AttackData"/> by
/// <see cref="AttackType"/>, advances startup/active/recovery phases at 60fps, and
/// gates hitbox activation. Sits next to Fighter on the character GameObject.
/// </summary>
[RequireComponent(typeof(Fighter))]
public class CombatSystem : MonoBehaviour
{
    [Header("Combat State")]
    [SerializeField] public bool isAttacking;
    [SerializeField] private bool inHitstun;
    [SerializeField] private AttackData currentAttack;
    [SerializeField] private float currentAttackTime;

    [Header("References")]
    private Fighter fighter;
    private Animator animator;

    [Header("Attack Library")]
    [Tooltip("Attacks available to this fighter. First match by AttackType wins.")]
    [SerializeField] private AttackData[] attacks;

    [Header("Hitbox")]
    [Tooltip("Hit detector on a child object that this combat system drives.")]
    [SerializeField] private HitDetector hitDetector;

    [Header("Hitstun")]
    [Tooltip("If on, getting hit cancels an attack that is still in progress (its hitbox is pulled). " +
             "If off, the attack keeps going under the flinch animation.")]
    [SerializeField] private bool hitInterruptsAttack = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private float hitstunTimer; // seconds left in hitstun
    private bool hitboxActive;
    private bool hitboxStarted; // the current attack's hitbox has already been activated once
    private bool currentAttackIsDashCounter; // started inside the dash-counter window after a dodge

    private void Awake()
    {
        fighter = GetComponent<Fighter>();
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (hitDetector == null)
        {
            hitDetector = GetComponentInChildren<HitDetector>();
        }
    }

    private void Update()
    {
        if (inHitstun)
        {
            hitstunTimer -= Time.deltaTime;
            if (hitstunTimer <= 0f)
            {
                inHitstun = false;
                hitstunTimer = 0f;
                if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: hitstun OVER");
            }
        }

        if (!isAttacking || currentAttack == null) return;

        currentAttackTime += Time.deltaTime;

        float startupEnd = currentAttack.StartupFrames * BalanceConfig.FRAME_TIME;
        float activeEnd = startupEnd + currentAttack.ActiveFrames * BalanceConfig.FRAME_TIME;
        float total = currentAttack.GetTotalDuration();

        // Activate the hitbox once startup is over. This fires even if a slow frame jumped
        // straight past the active window, so an attack can never skip its hitbox.
        if (!hitboxStarted && currentAttackTime >= startupEnd)
        {
            if (hitDetector != null)
            {
                hitDetector.Activate(fighter, this, currentAttack);
            }
            hitboxStarted = true;
            hitboxActive = true;

            // Leave it live until the next frame at least, so physics gets a step to register the hit.
            return;
        }

        if (hitboxActive && currentAttackTime >= activeEnd)
        {
            if (hitDetector != null)
            {
                hitDetector.Deactivate();
            }
            hitboxActive = false;
        }

        if (currentAttackTime >= total)
        {
            EndAttack();
        }
    }

    /// <summary>
    /// Attempts to start an attack of the given type. Returns false if the fighter
    /// is already attacking or lacks stamina, or if no matching <see cref="AttackData"/>
    /// is configured. On success the animator trigger fires and the active-phase
    /// hitbox will be raised by <see cref="Update"/>.
    /// </summary>
   public bool TryAttack(AttackType attackType)
{
    int pn = fighter != null ? fighter.PlayerNumber : 0;
    Debug.Log($"P{pn}: TryAttack({attackType}) called");

    if (isAttacking) { Debug.Log($"P{pn}: BLOCKED - already attacking"); return false; }
    if (inHitstun)   { Debug.Log($"P{pn}: BLOCKED - in hitstun"); return false; }
    if (fighter == null)         { Debug.Log($"P{pn}: BLOCKED - fighter null"); return false; }
    if (fighter.Stats == null)   { Debug.Log($"P{pn}: BLOCKED - Stats null"); return false; }

    AttackData found = FindAttack(attackType);
    if (found == null) { Debug.Log($"P{pn}: BLOCKED - no AttackData for {attackType}. attacks.Length={(attacks==null?-1:attacks.Length)}"); return false; }

    if (!fighter.Stats.HasStamina(found.StaminaCost))
    {
        Debug.Log($"P{pn}: BLOCKED - stamina {fighter.Stats.currentStamina}/{found.StaminaCost} for {found.AttackName}");
        return false;
    }

    isAttacking = true;
    currentAttack = found;
    currentAttackTime = 0f;
    hitboxActive = false;
    hitboxStarted = false;
    currentAttackIsDashCounter = fighter.ConsumeDashCounter();
    if (currentAttackIsDashCounter)
    {
        Debug.Log($"P{pn}: DASH COUNTER - {found.AttackName} will deal x{BalanceConfig.DASH_COUNTER_DAMAGE_MULTIPLIER} damage");
    }

    fighter.Stats.UseStamina(found.StaminaCost);

    if (animator != null && !string.IsNullOrEmpty(found.AnimatorTriggerName))
    {
        animator.SetTrigger(found.AnimatorTriggerName);
    }

    if (debugLogs) Debug.Log($"P{pn}: {found.AttackName} STARTED");
    return true;
}

    /// <summary>
    /// True while <see cref="currentAttackTime"/> falls within the active-frame window
    /// of <see cref="currentAttack"/>.
    /// </summary>
    public bool IsInActivePhase()
    {
        if (!isAttacking || currentAttack == null) return false;

        float startupEnd = currentAttack.StartupFrames * BalanceConfig.FRAME_TIME;
        float activeEnd = startupEnd + currentAttack.ActiveFrames * BalanceConfig.FRAME_TIME;
        return currentAttackTime >= startupEnd && currentAttackTime < activeEnd;
    }

    /// <summary>True if this fighter is mid-attack (any phase).</summary>
    public bool IsAttacking => isAttacking;

    /// <summary>True while this fighter is locked in a get-hit flinch and cannot act.</summary>
    public bool IsInHitstun => inHitstun;

    /// <summary>
    /// Locks this fighter out of attacking (and, via <see cref="Fighter"/>, moving and dashing)
    /// for <paramref name="frames"/> frames at 60fps. A longer hitstun already running is kept.
    /// Optionally cancels an attack in progress so its hitbox cannot land under the flinch.
    /// </summary>
    public void ApplyHitstun(int frames)
    {
        if (hitInterruptsAttack && isAttacking)
        {
            EndAttack("INTERRUPTED by a hit");
        }

        float seconds = Mathf.Max(frames, 1) * BalanceConfig.FRAME_TIME;
        hitstunTimer = Mathf.Max(hitstunTimer, seconds);
        inHitstun = true;

        if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: hitstun for {frames} frames ({seconds:0.00}s)");
    }

    /// <summary>The attack currently being performed, or null when idle.</summary>
    public AttackData CurrentAttack => currentAttack;

    /// <summary>
    /// Called by <see cref="HitDetector"/> when its trigger overlaps another fighter
    /// during the active phase. Computes final damage and applies it to the target.
    /// </summary>
    public void OnHit(Fighter target)
    {
        if (target == null || currentAttack == null) return;
        if (target.Stats == null || fighter == null || fighter.fighterData == null) return;

        // A dashing defender may have moved out of the strike's path, or straight into it.
        DashHitOutcome dashOutcome = target.ResolveDashAgainst(currentAttack.ComesFrom);
        if (dashOutcome == DashHitOutcome.Evaded || dashOutcome == DashHitOutcome.EvadedNoCounter)
        {
            bool counterAllowed = dashOutcome == DashHitOutcome.Evaded;
            target.NotifyEvaded(counterAllowed);

            if (debugLogs)
            {
                Debug.Log(
                    $"P{target.PlayerNumber} EVADED P{fighter.PlayerNumber}'s {currentAttack.AttackName} " +
                    $"(dashed {target.DescribeDash()} vs {currentAttack.ComesFrom})" +
                    (counterAllowed ? " - counter opens when the dash ends" : " - too far to counter"));
            }
            return;
        }

        bool blocked = target.IsBlocking;
        int blockReduction = blocked ? target.BlockDamageReduction : 0;
        float bonus = currentAttackIsDashCounter ? BalanceConfig.DASH_COUNTER_DAMAGE_MULTIPLIER : 1f;
        bool dashedIntoIt = dashOutcome == DashHitOutcome.HitAmplified;
        if (dashedIntoIt)
        {
            bonus *= BalanceConfig.DASH_INTO_ATTACK_DAMAGE_MULTIPLIER;
        }

        int finalDamage = DamageCalculator.CalculateDamage(
            currentAttack.Damage,
            fighter.fighterData.DamageMultiplier,
            target.Stats.armor,
            currentAttack.ArmorPierce,
            blockReduction,
            bonus);

        target.Stats.TakeDamage(finalDamage);
        target.NotifyHit(currentAttack.HitSide, currentAttack.HitstunFrames);

        // Blocking a hit costs the blocker stamina, scaled by how heavy the attack is.
        int blockCost = 0;
        if (blocked)
        {
            blockCost = GetBlockStaminaCost(currentAttack.AttackType);
            target.Stats.UseStamina(blockCost);
        }

        fighter.Stats.AddSuperBar(currentAttackIsDashCounter
            ? BalanceConfig.SUPER_GAIN_DASH_COUNTER
            : BalanceConfig.SUPER_GAIN_LAND_HIT);

        if (debugLogs)
        {
            string counterNote = currentAttackIsDashCounter ? $" [DASH COUNTER x{BalanceConfig.DASH_COUNTER_DAMAGE_MULTIPLIER}]" : "";
            counterNote += dashedIntoIt
                ? $" [DASHED {target.DescribeDash().ToUpper()} INTO {currentAttack.ComesFrom} x{BalanceConfig.DASH_INTO_ATTACK_DAMAGE_MULTIPLIER}]"
                : "";
            string blockNote = blocked ? $" [BLOCKED -{blockReduction}%, cost {blockCost} stamina]" : "";
            Debug.Log(
                $"P{fighter.PlayerNumber} {currentAttack.AttackName} HIT P{target.PlayerNumber} " +
                $"from the {currentAttack.HitSide} for {finalDamage} dmg{counterNote}{blockNote} " +
                $"(target HP: {target.Stats.currentHealth}/{target.Stats.maxHealth}, " +
                $"stamina: {target.Stats.currentStamina}/{target.Stats.maxStamina})");
        }
    }

    private static int GetBlockStaminaCost(AttackType attackType)
    {
        switch (attackType)
        {
            case AttackType.Light:   return BalanceConfig.BLOCK_LIGHT_STAMINA;
            case AttackType.Medium:  return BalanceConfig.BLOCK_MEDIUM_STAMINA;
            case AttackType.Heavy:   return BalanceConfig.BLOCK_HEAVY_STAMINA;
            case AttackType.Special: return BalanceConfig.BLOCK_SPECIAL_STAMINA;
            default:                 return BalanceConfig.BLOCK_MEDIUM_STAMINA;
        }
    }

    /// <summary>
    /// Scene-view preview of every attack's hitbox while this fighter is selected
    /// (yellow = light, orange = medium, red = heavy, magenta = special).
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (attacks == null) return;

        // Hitboxes follow the fighter's position and facing but are authored in world
        // units, so draw them without the fighter's model scale.
        Transform space = (hitDetector != null && hitDetector.transform.parent != null)
            ? hitDetector.transform.parent
            : transform;
        Gizmos.matrix = Matrix4x4.TRS(space.position, space.rotation, Vector3.one);

        foreach (AttackData attack in attacks)
        {
            if (attack == null) continue;

            switch (attack.AttackType)
            {
                case AttackType.Light:  Gizmos.color = Color.yellow; break;
                case AttackType.Medium: Gizmos.color = new Color(1f, 0.5f, 0f); break;
                case AttackType.Heavy:  Gizmos.color = Color.red; break;
                default:                Gizmos.color = Color.magenta; break;
            }

            HitDetector.GetLocalBox(attack, out Vector3 center, out Vector3 size);
            Gizmos.DrawWireCube(center, size);
        }
    }

    private AttackData FindAttack(AttackType attackType)
    {
        if (attacks == null) return null;
        for (int i = 0; i < attacks.Length; i++)
        {
            if (attacks[i] != null && attacks[i].AttackType == attackType)
            {
                return attacks[i];
            }
        }
        return null;
    }

    private void EndAttack(string reason = "ENDED")
    {
        if (hitboxActive && hitDetector != null)
        {
            hitDetector.Deactivate();
        }
        hitboxActive = false;

        if (debugLogs && currentAttack != null)
        {
            Debug.Log($"P{fighter.PlayerNumber}: {currentAttack.AttackName} {reason}");
        }

        isAttacking = false;
        currentAttack = null;
        currentAttackTime = 0f;
        currentAttackIsDashCounter = false;
    }
}