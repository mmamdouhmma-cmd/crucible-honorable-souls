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
    [SerializeField] private bool isAttacking;
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

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private bool hitboxActive;

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
        if (!isAttacking || currentAttack == null) return;

        currentAttackTime += Time.deltaTime;

        float startupEnd = currentAttack.StartupFrames * BalanceConfig.FRAME_TIME;
        float activeEnd = startupEnd + currentAttack.ActiveFrames * BalanceConfig.FRAME_TIME;
        float total = currentAttack.GetTotalDuration();

        // Activate hitbox at the start of the active phase, deactivate when it ends.
        bool shouldBeActive = currentAttackTime >= startupEnd && currentAttackTime < activeEnd;

        if (shouldBeActive && !hitboxActive)
        {
            if (hitDetector != null)
            {
                hitDetector.Activate(fighter, this, currentAttack);
            }
            hitboxActive = true;
        }
        else if (!shouldBeActive && hitboxActive)
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
        if (isAttacking) return false;
        if (inHitstun) return false;
        if (fighter == null || fighter.Stats == null) return false;

        AttackData found = FindAttack(attackType);
        if (found == null) return false;

        if (!fighter.Stats.HasStamina(found.StaminaCost)) return false;

        isAttacking = true;
        currentAttack = found;
        currentAttackTime = 0f;
        hitboxActive = false;

        fighter.Stats.UseStamina(found.StaminaCost);

        if (animator != null && !string.IsNullOrEmpty(found.AnimatorTriggerName))
        {
            animator.SetTrigger(found.AnimatorTriggerName);
        }

        if (debugLogs)
        {
            Debug.Log($"P{fighter.PlayerNumber}: {found.AttackName} STARTED");
        }
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

        int finalDamage = DamageCalculator.CalculateDamage(
            currentAttack.Damage,
            fighter.fighterData.DamageMultiplier,
            target.Stats.armor,
            currentAttack.ArmorPierce,
            isBlocking: false);

        target.Stats.TakeDamage(finalDamage);
        fighter.Stats.AddSuperBar(BalanceConfig.SUPER_GAIN_LAND_HIT);

        if (debugLogs)
        {
            Debug.Log(
                $"P{fighter.PlayerNumber} {currentAttack.AttackName} HIT P{target.PlayerNumber} " +
                $"for {finalDamage} dmg (target HP: {target.Stats.currentHealth}/{target.Stats.maxHealth})");
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

    private void EndAttack()
    {
        if (hitboxActive && hitDetector != null)
        {
            hitDetector.Deactivate();
        }
        hitboxActive = false;

        if (debugLogs && currentAttack != null)
        {
            Debug.Log($"P{fighter.PlayerNumber}: {currentAttack.AttackName} ENDED");
        }

        isAttacking = false;
        currentAttack = null;
        currentAttackTime = 0f;
    }
}