using System.Collections.Generic;
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

    [Header("Punishment")]
    [Tooltip("Extra recovery frames (60fps) after this fighter's attack is dodged by a dash: facing locked, " +
             "no walking, no new attack, so the dodger gets a free counter. -1 = BalanceConfig.DODGED_RECOVERY_FRAMES. " +
             "An AttackData can override it per move.")]
    [SerializeField] private int dodgedRecoveryFrames = -1;

    [Header("Hit feel")]
    [Tooltip("Spawn a blood burst on the target when this fighter's strike lands unblocked.")]
    [SerializeField] private bool bloodOnHit = true;
    [SerializeField] private Color bloodColor = new Color(0.42f, 0.015f, 0.02f, 1f);
    [Tooltip("Freeze both fighters for a few frames on impact (BalanceConfig.HIT_STOP_FRAMES).")]
    [SerializeField] private bool hitStop = true;
    [Tooltip("Sparks when this fighter's strike is blocked or clashes.")]
    [SerializeField] private bool sparksOnBlock = false;
    [Tooltip("Blade trail while the hitbox is live (needs a WeaponTrail on the fighter; added automatically).")]
    [SerializeField] private bool bladeTrail = false;
    private WeaponTrail weaponTrail;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private float hitstunTimer; // seconds left in hitstun
    private bool hitboxActive;
    private bool hitboxStarted; // the current attack's hitbox has already been activated once
    private bool currentAttackIsDashCounter; // started inside the dash-counter window after a dodge
    private bool swingEvaded;                // the opponent's dash already made this swing miss
    private Fighter reachTarget;             // opponent checked when the hitbox armed (for the gizmo)
    private Vector3 reachTargetPos;          // position that check used

    // Same-attack chain limiter (BalanceConfig.SAME_ATTACK_CHAIN_LIMIT / ATTACK_CHAIN_LOCKOUT /
    // ATTACK_CHAIN_WINDOW): counts how many times the same attack has been thrown back to back and
    // locks every attack out for a beat once the limit is reached.
    private AttackType chainType;      // attack the current chain is made of
    private int chainCount;            // repeats of chainType back to back (0 = no chain running)
    private float chainIdleTimer;      // seconds since the last attack ended (chain expires past ATTACK_CHAIN_WINDOW)
    private float attackLockoutTimer;  // seconds left of the post-chain lockout

    // Strings. A press that arrives while this fighter is busy is remembered for
    // BalanceConfig.INPUT_BUFFER_FRAMES and comes out on the first frame it is legal - the
    // "dial a combo" behaviour every fighting game has, so a follow-up pressed a few frames
    // early still lands instead of being eaten.
    private AttackType bufferedInput;
    private bool hasBufferedInput;
    private float bufferTimer;
    private bool attackTouched;        // this swing hit or was blocked
    private bool attackHitClean;       // this swing hit unblocked
    private int stringDepth;           // links taken without returning to neutral (0 = a fresh swing)

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

        if (bladeTrail)
        {
            weaponTrail = GetComponent<WeaponTrail>();
            if (weaponTrail == null) weaponTrail = gameObject.AddComponent<WeaponTrail>();
        }
    }

    private void Update()
    {
        // Whichever fighter updates first this frame settles every hit the physics step produced,
        // so both sides of a clash are judged together before either attack advances.
        ResolvePendingHits();

        // Hit-stop: everything below (attack timers, hitstun, animation) holds still for a few frames.
        HitStop.Tick(Time.deltaTime);
        bool frozen = HitStop.IsActive;
        if (animator != null) animator.speed = frozen ? 0f : 1f;
        if (frozen) return;

        // Post-chain lockout and chain expiry. Both run while idle, so they tick before the
        // "not attacking" return below.
        if (attackLockoutTimer > 0f)
        {
            attackLockoutTimer -= Time.deltaTime;
            if (attackLockoutTimer <= 0f)
            {
                attackLockoutTimer = 0f;
                if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: chain lockout OVER");
            }
        }

        if (!isAttacking && chainCount > 0)
        {
            chainIdleTimer += Time.deltaTime;
            if (chainIdleTimer >= BalanceConfig.ATTACK_CHAIN_WINDOW)
            {
                chainCount = 0;
            }
        }

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

        if (bufferTimer > 0f)
        {
            bufferTimer -= Time.deltaTime;
            if (bufferTimer <= 0f) { bufferTimer = 0f; hasBufferedInput = false; }
        }

        if (!isAttacking || currentAttack == null) return;

        currentAttackTime += Time.deltaTime;

        // A buffered press inside the cancel window drops the rest of the recovery.
        if (hasBufferedInput && TryCancelIntoFollowUp()) return;

        float startupEnd = currentAttack.StartupFrames * BalanceConfig.FRAME_TIME;
        float activeEnd = startupEnd + currentAttack.ActiveFrames * BalanceConfig.FRAME_TIME;
        float total = currentAttack.GetTotalDuration() + evadedExtraRecovery;

        // A blade box only hits where the weapon physically is, so it stays armed a few frames either
        // side of the authored active window (the authored front box was a slab that was always touching).
        // One hit per swing still holds, and IsInActivePhase keeps reporting the authored window.
        if (hitDetector != null && hitDetector.FollowWeapon)
        {
            float pad = BalanceConfig.WEAPON_ARM_PAD_FRAMES * BalanceConfig.FRAME_TIME;
            startupEnd = Mathf.Max(0f, startupEnd - pad);
            activeEnd = Mathf.Min(total, activeEnd + pad);
        }

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
            if (weaponTrail != null && weaponTrail.enabled) weaponTrail.Begin();

            // Decide now whether an opponent mid-dash just dodged this swing. Waiting for the
            // trigger would only ever see dodges that are still inside the box when it arms.
            ResolveDodgeAtArm();

            // Leave it live until the next frame at least, so physics gets a step to register the hit.
            return;
        }

        // Slow-frame safety net: when a rendered frame is longer than SLOW_FRAME_SECONDS the blade sweep can
        // miss a whole thrust, so on those frames the authored front box (fighter space, world units) decides
        // the authored active window, exactly as the pre-weapon-box system did. Never used at 60 fps.
        if (hitboxActive && hitDetector != null && hitDetector.FollowWeapon && !hitDetector.HasHit && !swingEvaded &&
            Time.unscaledDeltaTime > BalanceConfig.SLOW_FRAME_SECONDS && IsInActivePhase())
        {
            Fighter opp = fighter != null ? fighter.Opponent : null;
            if (opp != null && !opp.IsDashing && IsWithinReach(currentAttack, opp.transform.position, opp.BodyRadius))
            {
                if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: slow frame ({Time.unscaledDeltaTime * 1000f:0} ms) - front box judged {currentAttack.AttackName}");
                hitDetector.ForceHit(opp);
            }
        }

        if (hitboxActive && currentAttackTime >= activeEnd)
        {
            bool touched = hitDetector != null && hitDetector.HasHit;
            if (hitDetector != null)
            {
                hitDetector.Deactivate();
            }
            hitboxActive = false;
            if (weaponTrail != null) weaponTrail.End();

            if (!touched && !swingEvaded) LogWhiff();
        }

        if (currentAttackTime >= total)
        {
            EndAttack();

            // Hit the repeat limit on a swing that ran its course: no more attacks for a beat.
            if (chainCount >= BalanceConfig.SAME_ATTACK_CHAIN_LIMIT)
            {
                attackLockoutTimer = BalanceConfig.ATTACK_CHAIN_LOCKOUT;
                chainCount = 0;
                if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: {BalanceConfig.SAME_ATTACK_CHAIN_LIMIT}x {chainType} in a row - locked out for {BalanceConfig.ATTACK_CHAIN_LOCKOUT:0.00}s");
            }
        }
    }

    /// <summary>
    /// Attempts to start an attack of the given type. Returns false if the fighter
    /// is already attacking or lacks stamina, or if no matching <see cref="AttackData"/>
    /// is configured. On success the animator trigger fires and the active-phase
    /// hitbox will be raised by <see cref="Update"/>.
    /// </summary>
    /// <summary>
    /// Fires the buffered button as a string follow-up if the current attack links it and the
    /// playhead is inside the cancel window. Returns true when a follow-up started, in which case
    /// the caller must not touch the old attack's state again this frame.
    /// </summary>
    private bool TryCancelIntoFollowUp()
    {
        if (currentAttack == null || !currentAttack.HasComboLinks) return false;

        var link = currentAttack.FindLink(bufferedInput);
        if (link == null) return false;

        int frame = Mathf.FloorToInt(currentAttackTime / BalanceConfig.FRAME_TIME);
        if (frame < currentAttack.CancelStartFrame || frame > currentAttack.CancelEndFrame) return false;

        // Never cancel out of a live hitbox that has not resolved. This runs before the arming and
        // hit-resolution code further down the same Update, so cancelling inside the active window
        // of a swing that has not touched anything yet deletes the opener's own hit: the player
        // presses two buttons and only the second one does damage. Once the swing has connected
        // (hit, blocked or been dodged) the cancel is free, which is the hit-confirm behaviour
        // strings want; a whiff simply waits for the active frames to pass.
        float activeEnd = (currentAttack.StartupFrames + currentAttack.ActiveFrames) * BalanceConfig.FRAME_TIME;
        if (currentAttackTime < activeEnd && !attackTouched && !swingEvaded) return false;

        bool allowed;
        switch (link.requires)
        {
            case CancelRequirement.HitOnly:    allowed = attackHitClean; break;
            case CancelRequirement.HitOrBlock: allowed = attackTouched;  break;
            default:                           allowed = true;           break;
        }
        if (!allowed) return false;

        if (stringDepth + 1 >= BalanceConfig.MAX_STRING_LENGTH)
        {
            if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: string capped at {BalanceConfig.MAX_STRING_LENGTH} hits");
            return false;
        }

        if (fighter == null || fighter.Stats == null) return false;
        if (!fighter.Stats.HasStamina(link.followUp.StaminaCost)) return false;

        string from = currentAttack.AttackName;
        int window0 = currentAttack.CancelStartFrame, window1 = currentAttack.CancelEndFrame;
        int depth = stringDepth + 1;
        EndAttack("CANCELLED into the string");
        BeginAttack(link.followUp, depth);
        hasBufferedInput = false;
        bufferTimer = 0f;

        if (debugLogs)
        {
            Debug.Log($"P{fighter.PlayerNumber}: {from} -> {link.followUp.AttackName} " +
                      $"(cancelled on frame {frame}, window {window0}-{window1}, hit {depth + 1} of the string)");
        }
        return true;
    }

    /// <summary>
    /// Puts an attack into motion: stamina, timers, chain bookkeeping and the animator trigger.
    /// Shared by <see cref="TryAttack"/> and the string cancel, so a follow-up behaves exactly
    /// like a normal swing once it is out.
    /// </summary>
    private void BeginAttack(AttackData attack, int depth)
    {
        isAttacking = true;
        currentAttack = attack;
        currentAttackTime = 0f;
        hitboxActive = false;
        hitboxStarted = false;
        swingEvaded = false;
        evadedExtraRecovery = 0f;
        reachTarget = null;
        attackTouched = false;
        attackHitClean = false;
        stringDepth = depth;
        currentAttackIsDashCounter = fighter.ConsumeDashCounter();
        if (currentAttackIsDashCounter)
        {
            Debug.Log($"P{fighter.PlayerNumber}: DASH COUNTER - {attack.AttackName} will deal x{BalanceConfig.DASH_COUNTER_DAMAGE_MULTIPLIER} damage");
        }

        fighter.Stats.UseStamina(attack.StaminaCost);

        // Count this swing against the same-attack chain. A different attack, or too long a gap
        // after the previous one ended, starts over - so a string, being different attacks by
        // definition, never trips the anti-spam lockout.
        if (chainCount > 0 && attack.AttackType == chainType && chainIdleTimer < BalanceConfig.ATTACK_CHAIN_WINDOW)
        {
            chainCount++;
        }
        else
        {
            chainType = attack.AttackType;
            chainCount = 1;
        }
        chainIdleTimer = 0f;

        if (animator != null && !string.IsNullOrEmpty(attack.AnimatorTriggerName))
        {
            animator.SetTrigger(attack.AnimatorTriggerName);
        }
    }

    /// <summary>Remembers a press that cannot come out yet (see the buffer fields).</summary>
    private void BufferInput(AttackType attackType)
    {
        bufferedInput = attackType;
        hasBufferedInput = true;
        float hold = BalanceConfig.INPUT_BUFFER_FRAMES * BalanceConfig.FRAME_TIME;

        // Dial-a-combo: when the button genuinely continues this string, hold it until the cancel
        // window has passed rather than for the flat buffer. Otherwise a player who taps the
        // follow-up straight after the opener - which is how everyone plays strings - would have it
        // expire a frame or two before the window even opens.
        if (currentAttack != null && currentAttack.FindLink(attackType) != null)
        {
            float windowEnd = (currentAttack.CancelEndFrame + 1) * BalanceConfig.FRAME_TIME;
            hold = Mathf.Max(hold, windowEnd - currentAttackTime);
        }
        bufferTimer = hold;
    }

   public bool TryAttack(AttackType attackType)
{
    int pn = fighter != null ? fighter.PlayerNumber : 0;
    Debug.Log($"P{pn}: TryAttack({attackType}) called");

    if (isAttacking)
    {
        // A mid-attack press is not thrown away: if this attack links the button the cancel comes
        // out now or on the first legal frame, and if it does not the buffer just expires.
        BufferInput(attackType);
        if (TryCancelIntoFollowUp()) return true;
        Debug.Log($"P{pn}: buffered {attackType} for {BalanceConfig.INPUT_BUFFER_FRAMES} f - mid-attack");
        return false;
    }
    if (inHitstun)   { Debug.Log($"P{pn}: BLOCKED - in hitstun"); return false; }
    if (attackLockoutTimer > 0f) { Debug.Log($"P{pn}: BLOCKED - chain lockout, {attackLockoutTimer:0.00}s left"); return false; }
    if (fighter == null)         { Debug.Log($"P{pn}: BLOCKED - fighter null"); return false; }
    if (fighter.Stats == null)   { Debug.Log($"P{pn}: BLOCKED - Stats null"); return false; }

    AttackData found = FindAttack(attackType);
    if (found == null) { Debug.Log($"P{pn}: BLOCKED - no AttackData for {attackType}. attacks.Length={(attacks==null?-1:attacks.Length)}"); return false; }

    if (!fighter.Stats.HasStamina(found.StaminaCost))
    {
        Debug.Log($"P{pn}: BLOCKED - stamina {fighter.Stats.currentStamina}/{found.StaminaCost} for {found.AttackName}");
        return false;
    }

    BeginAttack(found, 0);
    hasBufferedInput = false;
    bufferTimer = 0f;

    if (debugLogs) Debug.Log($"P{pn}: {found.AttackName} STARTED");
    return true;
}

    /// <summary>
    /// True while <see cref="currentAttackTime"/> falls within the active-frame window
    /// of <see cref="currentAttack"/>.
    /// </summary>
    /// <summary>Seconds since the current attack started (0 when not attacking).</summary>
    public float AttackTime => isAttacking ? currentAttackTime : 0f;

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
        chainCount = 0; // a flinch breaks the chain; no lockout on top of the hitstun

        if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: hitstun for {frames} frames ({seconds:0.00}s)");
    }

    /// <summary>The attack currently being performed, or null when idle.</summary>
    public AttackData CurrentAttack => currentAttack;

    // ==================== HIT QUEUE / CLASH ====================
    // Trigger callbacks arrive one at a time, so a hit is not applied on the spot. It is queued
    // and every queued hit is resolved together on the next Update. That is the only way to see
    // that A hit B *and* B hit A on the same frame and treat it as a clash instead of a trade.

    private struct PendingHit
    {
        public CombatSystem attacker;
        public Fighter target;
    }

    private static readonly List<PendingHit> pendingHits = new List<PendingHit>();

    // Added to the current attack's total when the opponent dodged it: the attacker stays locked in the
    // recovery (no turning, no walking) so a well-timed dash leaves them open to the counter.
    private float evadedExtraRecovery;

    /// <summary>
    /// How long a dodged attack stays open, in frames: the attack's own override first, then this
    /// component's setting, then <see cref="BalanceConfig.DODGED_RECOVERY_FRAMES"/>.
    /// </summary>
    private int DodgedRecoveryFramesFor(AttackData attack)
    {
        if (attack != null && attack.DodgedRecoveryFrames >= 0) return attack.DodgedRecoveryFrames;
        if (dodgedRecoveryFrames >= 0) return dodgedRecoveryFrames;
        return BalanceConfig.DODGED_RECOVERY_FRAMES;
    }

    private void MarkEvaded()
    {
        swingEvaded = true;
        if (evadedExtraRecovery <= 0f)
        {
            int frames = DodgedRecoveryFramesFor(currentAttack);
            evadedExtraRecovery = frames * BalanceConfig.FRAME_TIME;
            if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: {currentAttack.AttackName} was DODGED - +{frames} f recovery ({frames * BalanceConfig.FRAME_TIME:F2}s open)");
        }
    }

    /// <summary>Clears any attack, hitstun and queued hits (new match / return to the menu).</summary>
    public void ResetForMatch()
    {
        if (isAttacking) EndAttack("match reset");
        chainCount = 0;
        attackLockoutTimer = 0f;
        inHitstun = false;
        hitstunTimer = 0f;
        evadedExtraRecovery = 0f;
        hasBufferedInput = false;
        bufferTimer = 0f;
        stringDepth = 0;
        attackTouched = false;
        attackHitClean = false;
        pendingHits.Clear();
    }

    /// <summary>
    /// Called by <see cref="HitDetector"/> when its trigger overlaps another fighter
    /// during the active phase. Queues the hit; see <see cref="ResolvePendingHits"/>.
    /// </summary>
    public void OnHit(Fighter target)
    {
        if (target == null || currentAttack == null || fighter == null) return;
        pendingHits.Add(new PendingHit { attacker = this, target = target });
    }

    /// <summary>
    /// Applies every hit queued since the last frame. Two hits that point at each other
    /// are a clash: equal weight cancels both with a shove and a stagger, otherwise the
    /// heavier attack lands and the lighter one is cancelled. Anything else is a normal hit.
    /// </summary>
    private static void ResolvePendingHits()
    {
        if (pendingHits.Count == 0) return;

        // Work on a copy: resolving a hit may end attacks, and a stale attacker is skipped below.
        List<PendingHit> hits = new List<PendingHit>(pendingHits);
        pendingHits.Clear();

        for (int i = 0; i < hits.Count; i++)
        {
            PendingHit a = hits[i];
            if (a.attacker == null) continue;

            int mutual = -1;
            for (int j = i + 1; j < hits.Count; j++)
            {
                PendingHit b = hits[j];
                if (b.attacker != null && b.attacker.fighter == a.target && a.attacker.fighter == b.target)
                {
                    mutual = j;
                    break;
                }
            }

            if (mutual >= 0)
            {
                CombatSystem other = hits[mutual].attacker;
                hits[mutual] = default; // consumed
                a.attacker.ResolveClash(other);
            }
            else
            {
                a.attacker.ResolveHit(a.target);
            }
        }
    }

    /// <summary>Heavier attacks win clashes: Light &lt; Medium &lt; Heavy &lt; Special.</summary>
    private static int ClashWeight(AttackType type)
    {
        switch (type)
        {
            case AttackType.Light:   return 0;
            case AttackType.Medium:  return 1;
            case AttackType.Heavy:   return 2;
            case AttackType.Special: return 3;
            default:                 return 1;
        }
    }

    /// <summary>
    /// This fighter's swing and <paramref name="other"/>'s connected on the same frame.
    /// </summary>
    private void ResolveClash(CombatSystem other)
    {
        // One side may already have been cancelled by something else this frame; then it's a plain hit.
        if (currentAttack == null || !isAttacking)
        {
            if (other.currentAttack != null && other.isAttacking) other.ResolveHit(fighter);
            return;
        }
        if (other.currentAttack == null || !other.isAttacking)
        {
            ResolveHit(other.fighter);
            return;
        }

        int myWeight = ClashWeight(currentAttack.AttackType);
        int theirWeight = ClashWeight(other.currentAttack.AttackType);

        if (myWeight == theirWeight)
        {
            string mine = currentAttack.AttackName;
            string theirs = other.currentAttack.AttackName;

            EndAttack("CLASHED");
            other.EndAttack("CLASHED");

            Vector3 away = fighter.transform.position - other.fighter.transform.position;
            fighter.NotifyClash(away);
            other.fighter.NotifyClash(-away);

            if (sparksOnBlock || other.sparksOnBlock)
            {
                Vector3 mid = (HitEffects.Chest(fighter) + HitEffects.Chest(other.fighter)) * 0.5f;
                HitEffects.SpawnSparks(mid, Vector3.up, fighter.BodyRadius);
            }
            if (FightCamera.Instance != null) FightCamera.Instance.OnClash();

            if (debugLogs || other.debugLogs)
            {
                Debug.Log($"CLASH! P{fighter.PlayerNumber} {mine} met P{other.fighter.PlayerNumber} {theirs} " +
                          $"- no damage, both staggered {BalanceConfig.CLASH_STAGGER_FRAMES} frames");
            }
            return;
        }

        CombatSystem winner = myWeight > theirWeight ? this : other;
        CombatSystem loser = winner == this ? other : this;
        string loserAttack = loser.currentAttack.AttackName;

        if (winner.debugLogs || loser.debugLogs)
        {
            Debug.Log($"CLASH! P{winner.fighter.PlayerNumber} {winner.currentAttack.AttackName} ({winner.currentAttack.AttackType}) " +
                      $"beats P{loser.fighter.PlayerNumber} {loserAttack} ({loser.currentAttack.AttackType})");
        }

        // The lighter swing is cancelled whether or not hits normally interrupt attacks.
        loser.EndAttack("LOST THE CLASH");
        winner.ResolveHit(loser.fighter);
    }

    /// <summary>
    /// Applies this fighter's current attack to <paramref name="target"/>: dash evasion,
    /// blocking, damage, hitstun and the hit animation.
    /// </summary>
    private void ResolveHit(Fighter target)
    {
        if (target == null || currentAttack == null) return;
        if (target.Stats == null || fighter == null || fighter.fighterData == null) return;

        // Already judged a miss when the hitbox armed: a glancing overlap on the way out doesn't count.
        if (swingEvaded)
        {
            if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: {currentAttack.AttackName} brushed P{target.PlayerNumber} but was already dodged");
            return;
        }

        // A dashing defender may have moved out of the strike's path, or straight into it.
        DashHitOutcome dashOutcome = target.ResolveDashAgainst(currentAttack.ComesFrom);
        if (dashOutcome == DashHitOutcome.Evaded || dashOutcome == DashHitOutcome.EvadedNoCounter)
        {
            bool counterAllowed = dashOutcome == DashHitOutcome.Evaded;
            MarkEvaded();
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

        // Remembered for string links that must be earned by connecting.
        attackTouched = true;
        attackHitClean = !blocked;

        if (!blocked && finalDamage > 0 && bloodOnHit)
        {
            HitEffects.SpawnBlood(target, fighter, bloodColor);
        }
        if (blocked && sparksOnBlock)
        {
            Vector3 at = HitEffects.Chest(target) + (fighter.transform.position - target.transform.position).normalized * target.BodyRadius;
            HitEffects.SpawnSparks(at, Vector3.up + (target.transform.position - fighter.transform.position).normalized, target.BodyRadius);
        }
        if (hitStop)
        {
            int frames = blocked ? BalanceConfig.HIT_STOP_BLOCK_FRAMES : BalanceConfig.HIT_STOP_FRAMES;
            HitStop.Request(frames * BalanceConfig.FRAME_TIME);
        }
        if (FightCamera.Instance != null)
        {
            FightCamera.Instance.OnImpact(finalDamage, currentAttack.AttackType, blocked);
        }

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

    /// <summary>
    /// Runs on the frame the hitbox arms. If the opponent is mid-dash and was inside this
    /// attack's reach when that dash began, the dash is judged against the attack's origin
    /// right now. An evade is awarded immediately, so a sidestep that already carried the
    /// dodger clear of the box still counts. Hits still need real trigger contact.
    /// </summary>
    private void ResolveDodgeAtArm()
    {
        Fighter target = fighter != null ? fighter.Opponent : null;
        if (target == null || currentAttack == null || !target.IsDashing) return;

        reachTarget = target;
        reachTargetPos = target.DashStartPosition;
        if (!IsWithinReach(currentAttack, reachTargetPos, target.BodyRadius)) return;

        DashHitOutcome outcome = target.ResolveDashAgainst(currentAttack.ComesFrom);
        if (outcome != DashHitOutcome.Evaded && outcome != DashHitOutcome.EvadedNoCounter) return;

        bool counterAllowed = outcome == DashHitOutcome.Evaded;
        MarkEvaded();
        target.NotifyEvaded(counterAllowed);

        if (debugLogs)
        {
            Debug.Log(
                $"P{target.PlayerNumber} EVADED P{fighter.PlayerNumber}'s {currentAttack.AttackName} " +
                $"(dashed {target.DescribeDash()} vs {currentAttack.ComesFrom}, judged as the blade arrived)" +
                (counterAllowed ? " - counter opens when the dash ends" : " - too far to counter"));
        }
    }

    /// <summary>
    /// The active window closed without touching anyone. Prints how far the opponent was
    /// against this attack's reach so spacing can be tuned from the console.
    /// </summary>
    private void LogWhiff()
    {
        if (!debugLogs || currentAttack == null || fighter == null) return;

        Fighter target = fighter.Opponent;
        if (target == null)
        {
            Debug.Log($"P{fighter.PlayerNumber}: {currentAttack.AttackName} WHIFFED - no opponent");
            return;
        }

        Vector3 a = fighter.transform.position;
        Vector3 b = target.transform.position;
        a.y = 0f;
        b.y = 0f;
        float distance = Vector3.Distance(a, b);

        // Centre-to-centre reach: the box starts at the offset and runs HitboxRange forward,
        // and the opponent's body radius adds to what it can touch.
        float reach = currentAttack.HitboxOffset.z + currentAttack.HitboxRange + target.BodyRadius;
        string why = distance > reach
            ? $"out of reach by {distance - reach:0.00}"
            : target.IsDashing ? "opponent dashed clear" : "box missed the body (height/side?)";

        Debug.Log($"P{fighter.PlayerNumber}: {currentAttack.AttackName} WHIFFED - opponent {distance:0.00} units away, " +
                  $"reach {reach:0.00} ({currentAttack.HitboxRange:0.0} + {target.BodyRadius:0.0} body) - {why}");
    }

    /// <summary>
    /// True if a body of <paramref name="radius"/> centred at <paramref name="worldPos"/> overlaps
    /// the attack's hitbox, measured in world units from this fighter's position and facing
    /// (model scale ignored, height ignored).
    /// </summary>
    private bool IsWithinReach(AttackData attack, Vector3 worldPos, float radius)
    {
        Transform space = (hitDetector != null && hitDetector.transform.parent != null && !hitDetector.FollowWeapon)
            ? hitDetector.transform.parent
            : transform;   // weapon-mounted detectors live under a hand bone: reach/dodge maths stays in fighter space
        Vector3 local = Quaternion.Inverse(space.rotation) * (worldPos - space.position);

        HitDetector.GetLocalBox(attack, out Vector3 center, out Vector3 size);
        float halfX = size.x * 0.5f + radius;
        float halfZ = size.z * 0.5f + radius;
        return Mathf.Abs(local.x - center.x) <= halfX && Mathf.Abs(local.z - center.z) <= halfZ;
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
    /// <summary>
    /// While a swing is live: the reach used for the arm-time dodge check (hitbox grown by the
    /// opponent's body radius) and a sphere where the opponent's dash started. Green = that
    /// dash was judged a dodge, red = it was not, grey = the opponent was not dashing.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !isAttacking || !hitboxStarted || currentAttack == null) return;

        Transform space = (hitDetector != null && hitDetector.transform.parent != null && !hitDetector.FollowWeapon)
            ? hitDetector.transform.parent
            : transform;   // weapon-mounted detectors live under a hand bone: reach/dodge maths stays in fighter space
        float radius = reachTarget != null ? reachTarget.BodyRadius : 0f;
        HitDetector.GetLocalBox(currentAttack, out Vector3 center, out Vector3 size);
        size.x += radius * 2f;
        size.z += radius * 2f;

        Gizmos.color = reachTarget == null ? new Color(0.6f, 0.6f, 0.6f, 0.6f)
                     : swingEvaded          ? new Color(0.2f, 1f, 0.3f, 0.8f)
                                            : new Color(1f, 0.3f, 0.2f, 0.8f);
        Gizmos.matrix = Matrix4x4.TRS(space.position, space.rotation, Vector3.one);
        Gizmos.DrawWireCube(center, size);

        if (reachTarget != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireSphere(reachTargetPos + Vector3.up * center.y, Mathf.Max(radius, 0.1f));
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (attacks == null) return;

        // Hitboxes follow the fighter's position and facing but are authored in world
        // units, so draw them without the fighter's model scale.
        Transform space = (hitDetector != null && hitDetector.transform.parent != null && !hitDetector.FollowWeapon)
            ? hitDetector.transform.parent
            : transform;   // weapon-mounted detectors live under a hand bone: reach/dodge maths stays in fighter space
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
        chainIdleTimer = 0f; // the chain window starts counting from here
    }
}