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
    public const float BLOCK_STAMINA_DRAIN_PER_SECOND = 0f;        // holding block is FREE (the user, 2026-10-24); the parry pays on connect instead
    // Walking with the guard up is slower (the user's call, 2026-10-24: -12%). The legs animate on
    // the BlockLegs animator layer while the upper body holds the block pose.
    public const float BLOCK_WALK_SPEED_MULTIPLIER = 0.88f;
    public const float POSTURE_BREAK_DURATION = 0.6f;
    
    // ==================== ATTACK STAMINA COSTS ====================
    public const int LIGHT_ATTACK_STAMINA = 10;
    public const int MEDIUM_ATTACK_STAMINA = 20;
    public const int HEAVY_ATTACK_STAMINA = 35;
    public const int SPECIAL_ATTACK_STAMINA = 50;
    public const int TACKLE_STAMINA = 55;
    public const int DASH_STAMINA = 18;

    // ==================== RUN ====================
    // Double-flick the right stick TOWARD the opponent and HOLD the second flick: the fighter
    // runs until the stick is released (or a dash, attack, block, hit or the opponent's body
    // ends it). One flick stays a dash; the SECOND flick starts the run instead of paying for a
    // second dash, so a run costs one dash's stamina and nothing while it lasts. Keyboard: hold
    // the Run key while walking toward the opponent.
    // The run moves at the fighter's OWN DASH SPEED (dashDistance / dashDuration, the user's call
    // 2026-10-25 - it replaced a walk multiplier, 1.3x at its last value), so the per-fighter dash
    // table in CrucibleBalance is also the run table: Miyamoto 11.8 u/s, Salaadin 8.8, Bjorn 7.3,
    // Richard 5.9. No multiplier constant exists any more; tune a fighter's run by tuning his dash.
    public const float RUN_DOUBLE_FLICK_WINDOW = 0.5f;   // max seconds between the two flicks (0.35 proved too tight to land deliberately)
    public const float RUN_MIN_FORWARD = 0.5f;           // how squarely toward the opponent the held stick must point
    // Running costs stamina per DISTANCE covered (the user's call, 2026-10-25), so a fast runner
    // and a slow one pay the same for the same ground - the fast one just pays it sooner. The
    // price rides the speed: it was 2/unit when the run moved 1.3x walk, and moving it to dash
    // speed raised the roster's average run speed by ~61%, so the cost went up by the same ratio
    // (the user's call). For scale: a dash is 7-8 stamina per unit WITH i-frames; crossing the
    // 6.5-unit opening gap at a run costs ~21. Regen is paused while running - without the pause
    // the recovery would out-earn the drain on a slow fighter. Emptying the bar ends the run.
    public const float RUN_STAMINA_PER_UNIT = 3.2f;

    // ==================== BLOCK STAMINA COSTS (paid by the blocker per blocked hit) ====================
    public const int BLOCK_LIGHT_STAMINA = 5;
    public const int BLOCK_MEDIUM_STAMINA = 10;
    public const int BLOCK_HEAVY_STAMINA = 18;
    public const int BLOCK_SPECIAL_STAMINA = 25;

    // ==================== COMBAT TIMING ====================
    // The FALLBACK only - the roster's live value is 0.2 s, written per fighter by CrucibleBalance.
    // Cut from 0.9 on the user's call (2026-10-25): with a 0.9 window and ~1.0 cooldown, tapping
    // block rhythmically kept a parry window open half the time, so spam parried everything. 0.2
    // means the guard must go up in the LAST fifth of a second before contact - the end of the
    // incoming swing, not its wind-up - and parryCooldown (0.8 roster-wide) paces the attempts.
    public const float PARRY_WINDOW = 0.2f;

    // String follow-ups play FASTER when dialed fast (the user's call, 2026-10-25): the 2nd/3rd
    // hit of a string runs its attack clock and animation at a pace between MIN (press came late
    // in the cancel window) and MAX (press was already buffered when the cancel opened - the
    // dialed combo), judged by how many seconds after the cancel first opened the link fired,
    // against RESPONSE_WINDOW. MAX is the speed limit the user asked for. Fresh swings are x1.
    public const float COMBO_FOLLOWUP_MIN_PACE = 1.1f;
    public const float COMBO_FOLLOWUP_MAX_PACE = 1.3f;
    public const float COMBO_FOLLOWUP_RESPONSE_WINDOW = 0.25f;
    // Dodging TOWARD a string follow-up (or too late): the hit lands and the stun is a full
    // second, not dodgeable - the punished read of the combo-escape rule (user, 2026-10-24).
    public const int COMBO_WRONG_DODGE_STUN_FRAMES = 60;
    // The FALLBACK for Fighter.dashIFrameDuration only - the real per-fighter values live in
    // CrucibleBalance (Plan_Balance 2.5; coverage of the dash, not absolute time, is the dial).
    // Before 2026-10-23 this constant was dead code and the WHOLE dash evaded.
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

    // ==================== OPENING DISTANCE ====================
    /// <summary>
    /// How far apart, in world units, the two fighters stand when FIGHT is called. The arena's
    /// authored marks sit 2.30 apart, which is INSIDE every fighter's reach (3.10 Miyamoto and the
    /// two shield fighters, 3.50 Bjorn), so a swing thrown on the first frame connected before
    /// anyone had moved. 6.5 clears the worst case in the roster outright: the longest reach is
    /// Bjorn's 3.50 and the longest lunge 2.60, so even a fully committed special lands him 3.90
    /// away and falls 0.40 short. NOTHING connects from the opening marks - both fighters have to
    /// close first, by walking or by dashing.
    /// </summary>
    public const float MATCH_START_GAP = 6.5f;

    // ==================== LOW STAMINA ====================
    // Below WEAKENED_STAMINA_FRACTION of the bar an attack costs less, deals less and comes out
    // slower (Plan_Balance 2.3, agreed 2026-10-23). This REPLACES the old slow-regen rule
    // (recovery at x0.75 under 20% of the bar): a drained fighter stays in the fight but stops
    // threatening, and the stamina spiral itself is what teaches pacing a round.
    // The damage cut must be AT LEAST as large as the cost cut, or damage-per-stamina IMPROVES
    // below the line and the optimal play becomes camping at 39% of the bar. Equal percentages
    // leave efficiency flat and cost only tempo, which is the intent.
    public const float WEAKENED_STAMINA_FRACTION = 0.40f;
    public const float WEAKENED_STAMINA_COST_MULTIPLIER = 0.75f;
    public const float WEAKENED_DAMAGE_MULTIPLIER = 0.75f;
    public const float WEAKENED_ATTACK_PACE_MULTIPLIER = 0.85f; // attack clock AND animator run at this rate

    // ==================== UNEVEN GROUND ====================
    // Arena floors are neither flat nor level (the test arena's runs -0.065 to -0.267 under the
    // marks, and the shipped arenas slope), and the swept blade is physical: an opponent standing
    // downhill can be passed clean over, and one standing uphill slips under the box. When the two
    // fighters' roots differ in height by more than this (world units; statures are ~2.6-2.9), the
    // authored front box - which ignores height on purpose - judges the active window instead,
    // exactly as it already does for kicks and slow frames. On level ground nothing changes.
    public const float UNEVEN_GROUND_HEIGHT_GAP = 0.2f;

    // ==================== WHIFF ====================
    // An attack whose active window closes without touching anyone (not hit, not blocked, not
    // dodged - a dash-dodge costs DODGED_RECOVERY_FRAMES instead) extends its own recovery by
    // this fraction of the move's recoveryFrames, so a whiffed heavy is punished harder than a
    // whiffed light without a second table (Plan_Balance 2.1). The cancel window is NOT
    // stretched: a combo link escapes the authored recovery only, never the whiff tail.
    public const float WHIFF_RECOVERY_MULTIPLIER = 1.0f;

    // ==================== ATTACK CHAIN LIMIT (anti-spam) ====================
    // The string and special lockouts are the same beat (0.5 s, the user's call on 2026-10-23,
    // down from the 0.85 s set on 2026-10-21): a finished string or special reads as the same
    // commitment gap whichever it was.
    // The SAME MOVE can be thrown at most SAME_ATTACK_CHAIN_LIMIT times in a row. After the last
    // repeat finishes, THAT MOVE ALONE is barred for SAME_ATTACK_LOCKOUT seconds - every other
    // attack stays free (the 1.5 rule, restored on the user's call 2026-10-23: the 0.5 s
    // lock-everything version plus a 0.6 s chain window meant "medium, step back, medium" never
    // tripped anything, which made Miyamoto's medium a spam button).
    // A different attack, or a gap longer than ATTACK_CHAIN_WINDOW after the previous attack
    // ended, starts a fresh chain and is free to swing.
    public const int SAME_ATTACK_CHAIN_LIMIT = 2;
    public const float SAME_ATTACK_LOCKOUT = 1.5f;   // seconds the repeated move itself is barred

    /// <summary>
    /// Seconds a fighter cannot attack after finishing a STRING (combo). A combo must not be a way
    /// to swing more often than single attacks do - that is what made the two-way Miyamoto links
    /// spammable.
    /// </summary>
    public const float STRING_LOCKOUT = 0.5f;  // seconds locked out of every attack after the last repeat
    // Gap after an attack ends that still counts as the same chain. 0.6 s was shorter than a step
    // back and a step in, so spacing between repeats reset the count; 2.0 s remembers the repeat
    // across that spacing, and the per-move bar above is mild enough to afford the longer memory.
    public const float ATTACK_CHAIN_WINDOW = 2.0f;

    /// <summary>
    /// Seconds a fighter cannot attack after a SPECIAL runs its course. A special already costs
    /// 2.5x the stamina of the matching normal; the lockout is what stops it being opened with
    /// every time the bar allows, and leaves the beat that makes a whiffed one punishable.
    /// </summary>
    public const float SPECIAL_LOCKOUT = 0.5f;

    // ---- Strings (combos) ----
    // Every attack thrown as COMBO PRESSURE pays a surcharge on top of its authored cost, as a
    // fraction of the fighter's MAX stamina, by the attack's own weight (the user's call,
    // 2026-10-24). "Combo pressure" is any swing that starts within COMBO_PRESSURE_GAP of the
    // previous swing ending - which covers string follow-ups AND the raw rotation the 1.5 rule
    // cannot touch (light x2, medium x2, heavy x2, repeat: each pair ducks under its own per-move
    // bar). Sustained offense now drains the bar fast enough that the rotation ends; the first
    // swing after a real pause pays nothing extra.
    public const float COMBO_SURCHARGE_LIGHT = 0.05f;
    public const float COMBO_SURCHARGE_MEDIUM = 0.07f;
    public const float COMBO_SURCHARGE_HEAVY = 0.10f;
    // The PRESSURE WINDOW all three free-form combo rules share: a swing that starts within this
    // long of the previous swing ending pays the surcharge above, must FLOW (never the same
    // SwingFlow geometry twice in a row - a right cut cannot follow a right cut, but a left cut,
    // a spin, a thrust or an overhead all may, and vice versa), and counts against the cap below.
    // A swing after a longer pause starts a fresh sequence, free of all three.
    public const float COMBO_PRESSURE_GAP = 1.0f;    // seconds of idle after a swing before the next one is free again
    // The authored strings cap at MAX_STRING_LENGTH (3); a FREE-FORM run of quick swings caps
    // here (the user's call, 2026-10-24) - the 5th back-to-back swing is refused until a pause.
    public const int FREEFORM_COMBO_CAP = 4;

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
    public const float PARRY_STUN_DURATION = 0.5f;   // every stun in the game is 0.5 s (the user, 2026-10-24)
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