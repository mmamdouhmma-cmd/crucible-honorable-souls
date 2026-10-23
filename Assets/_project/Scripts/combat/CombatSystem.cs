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

    [Tooltip("Specials, thrown with the special modifier held (L1 on the pad, U on the keyboard). " +
             "Kept apart from the normals so a plain press can never produce one: the button is read " +
             "from each asset's Special Button. String follow-ups are not listed here either - they " +
             "hang off their opener's Combo Links.")]
    [SerializeField] private AttackData[] specials;

    [Header("Ripostes")]
    [Tooltip("Thrown instead of a normal strike while the dash-counter window is open, so a counter " +
             "LOOKS like one. Leave empty to fall back to the ordinary attack.")]
    [SerializeField] private AttackData dashRiposte;

    [Tooltip("Thrown instead of a normal strike while the parry-counter window is open.")]
    [SerializeField] private AttackData parryRiposte;

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

    [Tooltip("Spark burst when this fighter PARRIES a strike. Separate from sparksOnBlock, which is off by design: a parry is the one moment that always deserves a flash.")]
    [SerializeField] private bool sparksOnParry = true;

    [Tooltip("Size of the parry flash. 1 = the ordinary block spark; higher adds particles, spread and a ring of embers where the blades met.")]
    [Range(1f, 5f)]
    [SerializeField] private float parrySparkStrength = 3f;
    [Tooltip("Blade trail while the hitbox is live (needs a WeaponTrail on the fighter; added automatically).")]
    [SerializeField] private bool bladeTrail = false;
    private WeaponTrail weaponTrail;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private float hitstunTimer; // seconds left in hitstun
    private bool hitboxActive;
    private bool hitboxStarted; // the current attack's hitbox has already been activated once
    private bool currentAttackIsDashCounter; // started inside the dash-counter window after a dodge
    private bool currentAttackIsParryCounter; // started inside the counter window opened by a parry
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
    private bool bufferedSpecial;      // the buffered press carried the special modifier
    private bool hasBufferedInput;
    private float bufferTimer;
    private bool attackTouched;        // this swing hit or was blocked
    private bool attackHitClean;       // this swing hit unblocked
    /// <summary>The moves a plain button press can produce. Read-only; the move list reads this.</summary>
    public AttackData[] Attacks => attacks;

    /// <summary>The moves the special modifier produces, keyed by AttackData.specialButton.</summary>
    public AttackData[] Specials => specials;

    /// <summary>The move a plain press gives while a dash counter is open (null = the normal).</summary>
    public AttackData DashRiposte => dashRiposte;

    /// <summary>The move a plain press gives while a parry counter is open (null = the normal).</summary>
    public AttackData ParryRiposte => parryRiposte;

    private int stringDepth;           // links taken without returning to neutral (0 = a fresh swing)
    private bool stringUsedSpecial;    // a string carries at most ONE special, and it ends there

    [Header("Anti-spam")]
    [Tooltip("Seconds with no attacking after a STRING finishes its last move (0 = use BalanceConfig.STRING_LOCKOUT).")]
    [SerializeField] private float stringLockout = 0f;

    [Tooltip("Seconds with no attacking after the same attack is thrown back to back to the limit (0 = use BalanceConfig.ATTACK_CHAIN_LOCKOUT).")]
    [SerializeField] private float sameAttackLockout = 0f;

    [Tooltip("Seconds with no attacking after a SPECIAL runs its course (0 = use BalanceConfig.SPECIAL_LOCKOUT).")]
    [SerializeField] private float specialLockout = 0f;

    private float StringLockout => stringLockout > 0f ? stringLockout : BalanceConfig.STRING_LOCKOUT;
    private float SameAttackLockout => sameAttackLockout > 0f ? sameAttackLockout : BalanceConfig.ATTACK_CHAIN_LOCKOUT;
    private float SpecialLockout => specialLockout > 0f ? specialLockout : BalanceConfig.SPECIAL_LOCKOUT;

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
        // A weakened swing (started under the low-stamina line) plays slower too, so what the
        // player sees stays in step with the slowed frame clock below.
        if (animator != null)
        {
            animator.speed = frozen ? 0f
                : (isAttacking && currentAttackWeakened) ? BalanceConfig.WEAKENED_ATTACK_PACE_MULTIPLIER
                : 1f;
        }
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

        // The attack clock and the animator slow together for a weakened swing, so startup,
        // active and recovery all stretch and the animation stays on the frame windows.
        currentAttackTime += Time.deltaTime *
            (currentAttackWeakened ? BalanceConfig.WEAKENED_ATTACK_PACE_MULTIPLIER : 1f);

        // A buffered press inside the cancel window drops the rest of the recovery.
        if (hasBufferedInput && TryCancelIntoFollowUp()) return;

        float startupEnd = currentAttack.StartupFrames * BalanceConfig.FRAME_TIME;
        float activeEnd = startupEnd + currentAttack.ActiveFrames * BalanceConfig.FRAME_TIME;
        float total = currentAttack.GetTotalDuration() + evadedExtraRecovery + whiffExtraRecovery;

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

        // The authored front box (fighter space, world units) decides the hit in two cases. A KICK,
        // because hits are otherwise judged by sweeping the blade and a carried blade never catches
        // one. And a SLOW FRAME: when a rendered frame is longer than SLOW_FRAME_SECONDS the sweep
        // can skip a whole thrust, so the box stands in, exactly as the pre-weapon-box system did.
        // The second case is never used at 60 fps.
        bool bodyAttack = currentAttack != null && !currentAttack.HitsWithWeapon;
        bool slowFrame = Time.unscaledDeltaTime > BalanceConfig.SLOW_FRAME_SECONDS;
        if (hitboxActive && hitDetector != null && hitDetector.FollowWeapon && !hitDetector.HasHit && !swingEvaded &&
            (bodyAttack || slowFrame) && IsInActivePhase())
        {
            Fighter opp = fighter != null ? fighter.Opponent : null;
            if (opp != null && !opp.IsDashing && IsWithinReach(currentAttack, opp.transform.position, opp.BodyRadius))
            {
                if (debugLogs)
                {
                    string why = bodyAttack ? "body attack" : $"slow frame ({Time.unscaledDeltaTime * 1000f:0} ms)";
                    Debug.Log($"P{fighter.PlayerNumber}: {why} - front box judged {currentAttack.AttackName}");
                }
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

            // A whiff extends the move's OWN recovery by a fraction of itself, so a missed
            // heavy is held open longer than a missed light (Plan_Balance 2.1). A dodged swing
            // already pays the longer dodge recovery instead, so the two never stack.
            if (!touched && !swingEvaded)
            {
                int extra = Mathf.RoundToInt(currentAttack.RecoveryFrames * BalanceConfig.WHIFF_RECOVERY_MULTIPLIER);
                if (extra > 0 && whiffExtraRecovery <= 0f)
                {
                    whiffExtraRecovery = extra * BalanceConfig.FRAME_TIME;
                    if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: {currentAttack.AttackName} WHIFFED - +{extra} f recovery ({whiffExtraRecovery:F2}s open)");
                }
                LogWhiff();
            }
        }

        if (currentAttackTime >= total)
        {
            int endedDepth = stringDepth;   // EndAttack clears it, and the lockout below needs it
            bool endedSpecial = currentAttack.IsSpecial;
            EndAttack();

            // A special is the most expensive thing a fighter can throw, so it costs a beat after it
            // as well as the stamina: that is what makes a whiffed one punishable instead of simply
            // repeatable while the bar lasts.
            if (endedSpecial)
            {
                attackLockoutTimer = Mathf.Max(attackLockoutTimer, SpecialLockout);
                chainCount = 0;
                if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: special finished - locked out for {SpecialLockout:0.00}s");
            }

            // A string that ran to the end of its last move costs a beat, however it was built: a
            // combo is not a way to attack more often than a single swing. Checked before the repeat
            // limiter so the longer of the two wins.
            if (endedDepth > 0)
            {
                attackLockoutTimer = Mathf.Max(attackLockoutTimer, StringLockout);
                chainCount = 0;
                if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: {endedDepth + 1}-hit string finished - locked out for {StringLockout:0.00}s");
            }

            // Hit the repeat limit on a swing that ran its course: no more attacks for a beat.
            if (chainCount >= BalanceConfig.SAME_ATTACK_CHAIN_LIMIT)
            {
                attackLockoutTimer = Mathf.Max(attackLockoutTimer, SameAttackLockout);
                chainCount = 0;
                if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: {BalanceConfig.SAME_ATTACK_CHAIN_LIMIT}x {chainType} in a row - locked out for {SameAttackLockout:0.00}s");
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

        var link = currentAttack.FindLink(bufferedInput, bufferedSpecial, stringDepth);
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

        // At most one special per string, which is what the authored shapes say: normal -> special,
        // normal -> normal -> special, special -> normal. Without this a special that opens a string
        // reaches a normal whose own special link fires, so one input chain could spend two specials
        // (~86 stamina) for three big hits.
        if (stringUsedSpecial && link.followUp.IsSpecial)
        {
            // FindLink prefers the modifier branch, so the same button may still have a plain one
            // that is perfectly legal here; only give up when it does not.
            var plain = currentAttack.FindLink(bufferedInput, false, stringDepth);
            if (plain == null || plain.followUp == null || plain.followUp.IsSpecial)
            {
                // Drop the press rather than hold it: it can never come out, and a held buffer
                // would re-test (and re-log) it on every frame of the cancel window.
                if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: no {link.followUp.AttackName} - a string carries only one special");
                hasBufferedInput = false;
                bufferTimer = 0f;
                return false;
            }
            link = plain;
        }

        if (stringDepth + 1 >= BalanceConfig.MAX_STRING_LENGTH)
        {
            // Likewise final: nothing can continue a string that is already at full length.
            if (debugLogs) Debug.Log($"P{fighter.PlayerNumber}: string capped at {BalanceConfig.MAX_STRING_LENGTH} hits");
            hasBufferedInput = false;
            bufferTimer = 0f;
            return false;
        }

        if (fighter == null || fighter.Stats == null) return false;
        if (!fighter.Stats.HasStamina(EffectiveStaminaCost(link.followUp))) return false;

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
        whiffExtraRecovery = 0f;
        reachTarget = null;
        attackTouched = false;
        attackHitClean = false;
        stringDepth = depth;
        // A fresh swing starts the count over; a link inherits what the string has already spent.
        stringUsedSpecial = (depth > 0 && stringUsedSpecial) || attack.IsSpecial;
        currentAttackIsDashCounter = fighter.ConsumeDashCounter();
        if (currentAttackIsDashCounter)
        {
            Debug.Log($"P{fighter.PlayerNumber}: DASH COUNTER - {attack.AttackName} will deal x{BalanceConfig.DASH_COUNTER_DAMAGE_MULTIPLIER} damage");
        }
        currentAttackIsParryCounter = fighter.ConsumeParryCounter();
        if (currentAttackIsParryCounter)
        {
            Debug.Log($"P{fighter.PlayerNumber}: PARRY COUNTER - {attack.AttackName} will deal x{fighter.ParryCounterDamageMultiplier} damage");
        }

        // Judged ONCE, at the moment the swing starts and before the cost comes off the bar,
        // and held for the whole swing: recovering past the line mid-swing does not speed it up.
        currentAttackWeakened = IsWeakened();
        if (currentAttackWeakened && debugLogs)
        {
            Debug.Log($"P{fighter.PlayerNumber}: {attack.AttackName} thrown WEAKENED " +
                      $"(stamina {fighter.Stats.currentStamina}/{fighter.Stats.maxStamina}: " +
                      $"x{BalanceConfig.WEAKENED_STAMINA_COST_MULTIPLIER} cost, " +
                      $"x{BalanceConfig.WEAKENED_DAMAGE_MULTIPLIER} damage, " +
                      $"x{BalanceConfig.WEAKENED_ATTACK_PACE_MULTIPLIER} pace)");
        }

        fighter.Stats.UseStamina(EffectiveStaminaCost(attack));

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
    private void BufferInput(AttackType attackType, bool special)
    {
        bufferedInput = attackType;
        bufferedSpecial = special;
        hasBufferedInput = true;
        float hold = BalanceConfig.INPUT_BUFFER_FRAMES * BalanceConfig.FRAME_TIME;

        // Dial-a-combo: when the button genuinely continues this string, hold it until the cancel
        // window has passed rather than for the flat buffer. Otherwise a player who taps the
        // follow-up straight after the opener - which is how everyone plays strings - would have it
        // expire a frame or two before the window even opens.
        if (currentAttack != null && currentAttack.FindLink(attackType, special, stringDepth) != null)
        {
            float windowEnd = (currentAttack.CancelEndFrame + 1) * BalanceConfig.FRAME_TIME;
            hold = Mathf.Max(hold, windowEnd - currentAttackTime);
        }
        bufferTimer = hold;
    }

   public bool TryAttack(AttackType attackType, bool special = false)
{
    int pn = fighter != null ? fighter.PlayerNumber : 0;
    Debug.Log($"P{pn}: TryAttack({attackType}{(special ? " + SPECIAL" : "")}) called");

    if (isAttacking)
    {
        // A mid-attack press is not thrown away: if this attack links the button the cancel comes
        // out now or on the first legal frame, and if it does not the buffer just expires.
        BufferInput(attackType, special);
        if (TryCancelIntoFollowUp()) return true;
        Debug.Log($"P{pn}: buffered {attackType} for {BalanceConfig.INPUT_BUFFER_FRAMES} f - mid-attack");
        return false;
    }
    if (fighter != null && fighter.IsDead) { Debug.Log($"P{pn}: BLOCKED - dead"); return false; }
    if (inHitstun)   { Debug.Log($"P{pn}: BLOCKED - in hitstun"); return false; }
    if (attackLockoutTimer > 0f) { Debug.Log($"P{pn}: BLOCKED - chain lockout, {attackLockoutTimer:0.00}s left"); return false; }
    if (fighter == null)         { Debug.Log($"P{pn}: BLOCKED - fighter null"); return false; }
    if (fighter.Stats == null)   { Debug.Log($"P{pn}: BLOCKED - Stats null"); return false; }

    // A counter window re-points the strike buttons at a RIPOSTE: the reward for a dodge or a
    // parry should read as its own move, not as a normal swing with a multiplier hidden on it. The
    // riposte is not in either library, so it is unreachable any other way. Holding the special
    // modifier is the deliberate opt-out - that still buys a special, for spending the window on a
    // bigger punish.
    AttackData found = null;
    if (!special && fighter != null)
    {
        if (fighter.ParryCounterTimeLeft > 0f && parryRiposte != null) found = parryRiposte;
        else if (fighter.DashCounterOpen && dashRiposte != null) found = dashRiposte;
        if (found != null) Debug.Log($"P{pn}: RIPOSTE - {found.AttackName} instead of the {attackType}");
    }
    if (found == null) found = FindAttack(attackType, special);

    // The modifier is held, but this fighter has no special on that button: fall through to the
    // normal rather than eating the press. A player holding the modifier should still be able to
    // fight, and it keeps every character playable before all three specials are authored.
    if (found == null && special)
    {
        found = FindAttack(attackType, false);
        if (found != null) Debug.Log($"P{pn}: no special on {attackType} - throwing the normal instead");
    }
    if (found == null)
    {
        int n = special ? (specials == null ? -1 : specials.Length) : (attacks == null ? -1 : attacks.Length);
        Debug.Log($"P{pn}: BLOCKED - no AttackData for {attackType}{(special ? " + SPECIAL" : "")}. library length={n}");
        return false;
    }

    int needed = EffectiveStaminaCost(found);
    if (!fighter.Stats.HasStamina(needed))
    {
        Debug.Log($"P{pn}: BLOCKED - stamina {fighter.Stats.currentStamina}/{needed} for {found.AttackName}");
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

    // Added to the current attack's total when its active window closed without touching anyone:
    // the whiff tax (Plan_Balance 2.1). Mutually exclusive with evadedExtraRecovery.
    private float whiffExtraRecovery;

    // True while the current swing was started under the low-stamina line: it cost less, deals
    // less, and runs slower - clock and animator together (Plan_Balance 2.3).
    private bool currentAttackWeakened;

    /// <summary>True when an attack thrown RIGHT NOW would be weakened (bar under the line).</summary>
    private bool IsWeakened()
    {
        return fighter != null && fighter.Stats != null && fighter.Stats.maxStamina > 0 &&
               fighter.Stats.currentStamina < fighter.Stats.maxStamina * BalanceConfig.WEAKENED_STAMINA_FRACTION;
    }

    /// <summary>What <paramref name="attack"/> costs right now: the authored cost, cut below the line.</summary>
    private int EffectiveStaminaCost(AttackData attack)
    {
        int cost = attack != null ? attack.StaminaCost : 0;
        if (IsWeakened()) cost = Mathf.RoundToInt(cost * BalanceConfig.WEAKENED_STAMINA_COST_MULTIPLIER);
        return cost;
    }

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
        // Belt and braces: the blow that ended the last match was live when the match stopped, and
        // a hitbox left armed through the teleport to the spawn marks would register a hit on the
        // first frame of the new one.
        if (hitDetector != null) hitDetector.Deactivate();
        hitboxActive = false;
        chainCount = 0;
        attackLockoutTimer = 0f;
        inHitstun = false;
        hitstunTimer = 0f;
        evadedExtraRecovery = 0f;
        whiffExtraRecovery = 0f;
        currentAttackWeakened = false;
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

        // PARRY beats blocking: the guard went up inside the parry window, so this strike does
        // nothing at all and the attacker is left stunned and open. Checked before the block branch
        // because a parry IS a block as far as Fighter.isBlocking is concerned.
        if (target.ParryReady)
        {
            string parried = currentAttack.AttackName;
            int stunFrames = target.ParryStunFrames;

            target.NotifyParry();
            attackTouched = true;    // the blade was met, so a string may continue (hitstun will stop it)
            attackHitClean = false;

            if (sparksOnParry)
            {
                // A parry is the one moment that always deserves a spark, so this is its own toggle
                // rather than riding on sparksOnBlock (which is off by design).
                Vector3 at = HitEffects.Chest(target) + (fighter.transform.position - target.transform.position).normalized * target.BodyRadius;
                HitEffects.SpawnParryFlash(at, Vector3.up + (target.transform.position - fighter.transform.position).normalized,
                                           target.BodyRadius, Mathf.Max(1f, parrySparkStrength));
            }
            if (hitStop) HitStop.Request(BalanceConfig.HIT_STOP_FRAMES * BalanceConfig.FRAME_TIME);
            if (FightCamera.Instance != null) FightCamera.Instance.OnImpact(0, currentAttack.AttackType, true);

            if (debugLogs)
            {
                Debug.Log($"P{target.PlayerNumber} PARRIED P{fighter.PlayerNumber}'s {parried} - " +
                          $"no damage, attacker stunned {stunFrames} f");
            }

            EndAttack("PARRIED");
            fighter.NotifyParried(stunFrames);
            return;
        }

        bool blocked = target.IsBlocking;
        int blockReduction = blocked ? target.BlockDamageReduction : 0;
        float bonus = currentAttackIsDashCounter ? BalanceConfig.DASH_COUNTER_DAMAGE_MULTIPLIER : 1f;
        if (currentAttackIsParryCounter) bonus *= fighter.ParryCounterDamageMultiplier;
        bool dashedIntoIt = dashOutcome == DashHitOutcome.HitAmplified;
        if (dashedIntoIt)
        {
            bonus *= BalanceConfig.DASH_INTO_ATTACK_DAMAGE_MULTIPLIER;
        }
        // Thrown from under the low-stamina line: the damage cut is at least as deep as the cost
        // cut, so running the bar down can never IMPROVE damage-per-stamina (Plan_Balance 2.3).
        if (currentAttackWeakened) bonus *= BalanceConfig.WEAKENED_DAMAGE_MULTIPLIER;

        int finalDamage = DamageCalculator.CalculateDamage(
            currentAttack.Damage,
            fighter.fighterData.DamageMultiplier,
            target.Stats.armor,
            currentAttack.ArmorPierce,
            blockReduction,
            bonus);

        target.Stats.TakeDamage(finalDamage);
        if (!target.Stats.IsAlive()) target.NotifyDeath();   // death animation instead of a flinch
        else target.NotifyHit(currentAttack.HitSide, currentAttack.HitstunFrames);

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

        // Landing the punish a dodge or parry earned pays the attacker health back. Deliberately on
        // the HIT, not on starting the counter: the window alone is not the reward, cashing it is.
        if (currentAttackIsDashCounter || currentAttackIsParryCounter) fighter.RecoverHealthForCounterHit();

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

    /// <summary>
    /// The move a press produces. A plain press matches the normals by <see cref="AttackType"/>;
    /// a press with the special modifier held matches the specials by their Special Button, since
    /// all three share <see cref="AttackType.Special"/>.
    /// </summary>
    private AttackData FindAttack(AttackType attackType, bool special)
    {
        AttackData[] library = special ? specials : attacks;
        if (library == null) return null;
        for (int i = 0; i < library.Length; i++)
        {
            if (library[i] == null) continue;
            bool match = special
                ? library[i].IsSpecial && library[i].SpecialButton == attackType
                : library[i].AttackType == attackType;
            if (match) return library[i];
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
        currentAttackWeakened = false;
        currentAttackIsDashCounter = false;
        currentAttackIsParryCounter = false;
        chainIdleTimer = 0f; // the chain window starts counting from here
    }
}