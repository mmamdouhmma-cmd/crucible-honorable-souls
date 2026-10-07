using UnityEngine;

/// <summary>
/// All game balance values in one place.
/// Modify these to tweak gameplay without editing code.
/// </summary>
public static class BalanceConfig
{
    // ==================== STAMINA ====================
    public const int DEFAULT_MAX_STAMINA = 100;
    public const float STAMINA_RECOVERY_PER_SECOND = 15f;          // default; each Fighter can override it in the Inspector
    public const float DEFAULT_HEALTH_REGEN_PER_SECOND = 1f;       // default; each Fighter can override it in the Inspector
    public const float DEFAULT_HEALTH_REGEN_DELAY = 3f;            // seconds after a hit before regen resumes; per-Fighter override
    public const float BLOCK_STAMINA_DRAIN_PER_SECOND = 4f;
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
    public const float PARRY_WINDOW = 0.15f;        // 9 frames at 60fps
    public const float DASH_IFRAME_DURATION = 0.15f;
    public const float DASH_COUNTER_WINDOW = 0.5f;  // time after a dodge-dash to start a counter attack
    public const float PARRY_COUNTER_WINDOW = 0.5f;
    public const float COMBO_INPUT_WINDOW = 0.5f;
    
    // ==================== DAMAGE MODIFIERS ====================
    public const int DEFAULT_BLOCK_DAMAGE_REDUCTION = 50;   // percent; each Fighter can override it in the Inspector
    public const float DASH_COUNTER_DAMAGE_MULTIPLIER = 1.35f; // +35% on the attack after a clean dodge
    public const float DASH_INTO_ATTACK_DAMAGE_MULTIPLIER = 1.25f; // +25% taken when dashing into a strike
    public const float MOVEMENT_INTO_SLASH_BONUS = 1.1f;    // +10%
    public const float MOVEMENT_INTO_THRUST_BONUS = 1.15f;  // +15%
    public const float PARRY_STUN_DURATION = 1.0f;
    
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
    public const int SUPER_GAIN_LAND_HIT = 5;
    public const int SUPER_GAIN_COMBO = 10;
    public const int SUPER_GAIN_PARRY = 15;
    public const int SUPER_GAIN_DASH_COUNTER = 12;
    public const int SUPER_GAIN_GRAB = 8;
    
    // ==================== FRAME RATE ====================
    public const int TARGET_FPS = 60;
    public const float FRAME_TIME = 1f / 60f; // 16.67ms
}