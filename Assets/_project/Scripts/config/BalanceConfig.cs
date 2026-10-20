using UnityEngine;

/// <summary>
/// All game balance values in one place.
/// Modify these to tweak gameplay without editing code.
/// </summary>
public static class BalanceConfig
{
    // ==================== STAMINA ====================
    public const int DEFAULT_MAX_STAMINA = 100;

    // Weapon-mounted hit detection (HitDetector.followWeapon): the blade box is armed this many frames before the
    // authored active window starts and after it ends, because a physical blade is only inside the target for a
    // few frames of a fast swing. One hit per swing still holds; hitstun/damage come from the AttackData.
    public const int WEAPON_ARM_PAD_FRAMES = 5;
    // Frames longer than this (real seconds) are too coarse for the swept blade test (a thrust can go out and
    // back inside one frame): on such frames the authored front box judges the active window instead.
    public const float SLOW_FRAME_SECONDS = 1f / 30f;

    // ATTACK LUNGE: during an attack's startup the fighter closes toward the opponent so the blade lands
    // at ATTACK_LUNGE_STOP (centre to centre, world units) instead of relying on the stock swing's reach.
    // Never more than ATTACK_LUNGE_MAX units per attack. AttackData can override both per move.
    public const float ATTACK_LUNGE_MAX = 0.9f;
    public const float ATTACK_LUNGE_STOP = 1.7f;
    // Average lunge speed cap (units/s): a short-startup light steps less than a heavy, instead of jumping.
    public const float ATTACK_LUNGE_MAX_SPEED = 4f;
    public const float STAMINA_RECOVERY_PER_SECOND = 15f;          // default; each Fighter can override it in the Inspector
    public const float DEFAULT_HEALTH_REGEN_PER_SECOND = 1f;       // default; each Fighter can override it in the Inspector
    public const float DEFAULT_HEALTH_REGEN_DELAY = 3f;            // seconds after a hit before regen resumes; per-Fighter override
    public const float BLOCK_STAMINA_DRAIN_PER_SECOND = 4f;        // default drain while holding block; each Fighter can override it in the Inspector
    public const float POSTURE_BREAK_DURATION = 0.6f;
    
    // ==================== ATTACK STAMINA COSTS ====================
    public const int LIGHT_ATTACK_STAMINA = 10;
    public const int MEDIUM_ATTACK_STAMINA = 20;
    public const int HEAVY_ATTACK_STAMINA = 35;
    public const int SPECIAL_ATTACK_STAMINA = 50;
    public const int TACKLE_STAMINA = 55;
    public const int DASH_STAMINA = 18;

    // ==================== BLOCK STAMINA COSTS (paid by the blocker per blocked hit) ====================
    public const int BLOCK_LIGHT_STAMINA = 5;
    public const int BLOCK_MEDIUM_STAMINA = 10;
    public const int BLOCK_HEAVY_STAMINA = 18;
    public const int BLOCK_SPECIAL_STAMINA = 25;

    // ==================== COMBAT TIMING ====================
    public const float PARRY_WINDOW = 0.12f;        // 9 frames at 60fps
    public const float DASH_IFRAME_DURATION = 0.15f;
    // Time after a dodge-dash in which the counter attack must be STARTED. Matched to
    // DODGED_RECOVERY_FRAMES (0.8s): the dodged attacker is held open for 0.8s, so a shorter
    // window here closed before the punish it exists to allow - which is why a dodge rarely
    // produced a counter in play even though the multiplier worked.
    public const float DASH_COUNTER_WINDOW = 0.8f;
    // Extra recovery (facing locked, no walking, no new attack) for an attack that was dodged by a dash:
    // 48 frames = 0.8s of counter window. Overridable per fighter (CombatSystem.dodgedRecoveryFrames)
    // and per move (AttackData.dodgedRecoveryFrames).
    public const int DODGED_RECOVERY_FRAMES = 48;
    public const float PARRY_COUNTER_WINDOW = 0.5f;
    public const float COMBO_INPUT_WINDOW = 0.5f;

    // ==================== ATTACK CHAIN LIMIT (anti-spam) ====================
    // The same attack can be thrown at most SAME_ATTACK_CHAIN_LIMIT times in a row. After the last
    // repeat lands the fighter cannot start ANY attack for ATTACK_CHAIN_LOCKOUT seconds, so a button
    // cannot be machine-gunned: two swings, then a commitment gap the opponent can punish.
    // A different attack, or a gap longer than ATTACK_CHAIN_WINDOW after the previous attack ended,
    // starts a fresh chain and is free to swing.
    public const int SAME_ATTACK_CHAIN_LIMIT = 2;
    public const float ATTACK_CHAIN_LOCKOUT = 1.5f;

    /// <summary>
    /// Seconds a fighter cannot attack after finishing a STRING (combo). A combo must not be a way
    /// to swing more often than single attacks do - that is what made the two-way Miyamoto links
    /// spammable.
    /// </summary>
    public const float STRING_LOCKOUT = 1.5f;   // seconds locked out of every attack after the last repeat
    public const float ATTACK_CHAIN_WINDOW = 0.6f;    // gap after an attack ends that still counts as the same chain

    /// <summary>
    /// Seconds a fighter cannot attack after a SPECIAL runs its course. A special already costs
    /// 2.5x the stamina of the matching normal; the lockout is what stops it being opened with
    /// every time the bar allows, and leaves the beat that makes a whiffed one punishable.
    /// </summary>
    public const float SPECIAL_LOCKOUT = 1.0f;

    // ---- Strings (combos) ----
    // A press that arrives while the fighter is mid-attack is held for this long and comes out on
    // the first frame the cancel window allows, so a follow-up pressed slightly early is not eaten.
    // Fighting games sit around 6-10 frames; wider feels loose and starts firing moves the player
    // has stopped wanting.
    public const int INPUT_BUFFER_FRAMES = 8;

    // Hits in one string before it must return to neutral, counting the opener.
    public const int MAX_STRING_LENGTH = 3;
    
    // ==================== CLASH (both attacks connect on the same frame) ====================
    // Equal weight: no damage, both fighters are shoved apart and staggered.
    // Unequal weight: the heavier attack lands normally and the lighter one is cancelled.
    public const float CLASH_PUSHBACK_DISTANCE = 2.0f;      // world units each fighter slides away from the other
    public const float CLASH_PUSHBACK_DURATION = 0.15f;     // seconds the slide takes
    public const int CLASH_STAGGER_FRAMES = 15;             // frames neither fighter can act after an equal clash
    public const float CLASH_HUD_FLASH_DURATION = 0.6f;     // seconds the HUD shows CLASH

    // ==================== HIT FEEL ====================
    public const int HIT_STOP_FRAMES = 4;         // both fighters freeze this long when a strike lands
    public const int HIT_STOP_BLOCK_FRAMES = 2;   // shorter freeze on a blocked strike

    // ==================== DAMAGE MODIFIERS ====================
    public const int DEFAULT_BLOCK_DAMAGE_REDUCTION = 50;   // percent; each Fighter can override it in the Inspector
    public const float DASH_COUNTER_DAMAGE_MULTIPLIER = 1.35f; // +35% on the attack after a clean dodge
    public const float DASH_INTO_ATTACK_DAMAGE_MULTIPLIER = 1.25f; // +25% taken when dashing into a strike
    public const float MOVEMENT_INTO_SLASH_BONUS = 1.1f;    // +10%
    public const float MOVEMENT_INTO_THRUST_BONUS = 1.15f;  // +15%
    public const float PARRY_STUN_DURATION = 1.0f;
    // Same as the dash counter (user's call, 2026-10-18): a parry is harder to time, but the free
    // 1.0 s stun it buys is already the reward. Serialized per fighter.
    public const float PARRY_COUNTER_DAMAGE_MULTIPLIER = 1.35f;
    
    // ==================== COMBO MULTIPLIERS ====================
    public const float COMBO_MULTIPLIER_2_HIT = 1.1f;
    public const float COMBO_MULTIPLIER_3_HIT = 1.2f;
    public const float COMBO_MULTIPLIER_4_HIT = 1.3f;
    public const float COMBO_MULTIPLIER_5_PLUS = 1.4f;
    
    // ==================== SIGNATURE MOVE ====================
    public const float SIGNATURE_DAMAGE_PERCENT = 0.30f;   // 30% of opponent HP
    public const int SUPER_BAR_MAX = 100;
    public const float DESPERATION_HP_THRESHOLD = 0.15f;   // 15%
    
    // Super bar charging
    // ==================== DEFENSIVE HEALTH RECOVERY ====================
    // Defending well gives a little health back, so a patient fighter is not simply losing slower
    // than an aggressive one. Fractions of the defender's OWN max health, so a 90 hp fighter and
    // an 85 hp one recover the same share of their bar.
    public const float HEAL_ON_DODGE = 0.02f;        // a dash that made a strike miss
    public const float HEAL_ON_PARRY = 0.02f;        // a parried strike
    public const float HEAL_ON_COUNTER_HIT = 0.03f;  // landing the punish the dodge or parry earned

    // Total recovery allowed in one round, as a fraction of max health. 0 = no cap, which is where
    // this starts: the cap exists for when it proves abusable, not before.
    public const float HEAL_CAP_PER_ROUND = 0f;

    public const int SUPER_GAIN_LAND_HIT = 5;
    public const int SUPER_GAIN_COMBO = 10;
    public const int SUPER_GAIN_PARRY = 15;
    public const int SUPER_GAIN_DASH_COUNTER = 12;
    public const int SUPER_GAIN_GRAB = 8;
    
    // ==================== FRAME RATE ====================
    public const int TARGET_FPS = 60;
    public const float FRAME_TIME = 1f / 60f; // 16.67ms
}