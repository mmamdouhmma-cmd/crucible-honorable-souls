using UnityEngine;

/// <summary>
/// Runtime stats for an active fighter — values that change during a match.
/// Constructed from a <see cref="FighterData"/> archetype at fight start.
/// All values are integers for deterministic balance tuning.
/// </summary>
[System.Serializable]
public class FighterStats
{
    public int currentHealth;
    public int maxHealth;

    public int currentStamina;
    public int maxStamina;

    public int armor; // 0-100 percent damage reduction

    public int currentSuperBar;
    public int maxSuperBar;

    public bool signatureUsed; // True if signature already used this match

    /// <summary>Health recovered from defensive play this round, for the optional cap.</summary>
    public int healedThisRound;

    private float staminaRecoveryRemainder; // Sub-1 stamina regen carried between frames
    private float staminaDrainRemainder;    // Sub-1 stamina drain (block hold) carried between frames
    private float healthRegenRemainder;     // Sub-1 health regen carried between frames

    /// <summary>
    /// Builds a fresh stats block at full health/stamina with an empty super bar.
    /// </summary>
    public FighterStats(int maxHP, int maxStam, int armorValue)
    {
        maxHealth = maxHP;
        currentHealth = maxHP;

        maxStamina = maxStam;
        currentStamina = maxStam;

        armor = armorValue;

        currentSuperBar = 0;
        maxSuperBar = BalanceConfig.SUPER_BAR_MAX;

        signatureUsed = false;
    }

    /// <summary>
    /// Reduces current health by <paramref name="amount"/>, clamped to 0.
    /// </summary>
    public void TakeDamage(int amount)
    {
        currentHealth -= amount;
        if (currentHealth < 0) currentHealth = 0;
    }

    /// <summary>
    /// Restores health up to <see cref="maxHealth"/>.
    /// </summary>
    public void Heal(int amount)
    {
        currentHealth += amount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
    }

    /// <summary>
    /// Heals a FRACTION of max health (defensive recovery) and returns the points actually given,
    /// which is 0 when already full or the round's cap is spent. Rounds up, so the smallest reward
    /// is always worth at least 1 point however small the bar.
    /// </summary>
    /// <param name="fraction">Share of max health to restore, e.g. 0.02 for 2%.</param>
    /// <param name="capFraction">Share of max health allowed per round in total; 0 = no cap.</param>
    public int HealFraction(float fraction, float capFraction)
    {
        if (!IsAlive() || fraction <= 0f || currentHealth >= maxHealth) return 0;

        int amount = Mathf.Max(1, Mathf.CeilToInt(maxHealth * fraction));
        if (capFraction > 0f)
        {
            int budget = Mathf.RoundToInt(maxHealth * capFraction) - healedThisRound;
            if (budget <= 0) return 0;
            amount = Mathf.Min(amount, budget);
        }

        int before = currentHealth;
        Heal(amount);
        int given = currentHealth - before;
        healedThisRound += given;
        return given;
    }

    /// <summary>
    /// True while the fighter still has health remaining.
    /// </summary>
    public bool IsAlive()
    {
        return currentHealth > 0;
    }

    /// <summary>
    /// Spends stamina. Allowed to dip negative briefly to drive posture-break states.
    /// </summary>
    public void UseStamina(int amount)
    {
        currentStamina -= amount;
    }

    /// <summary>Gives back whole stamina points at once (a refund), capped at max.</summary>
    public void AddStamina(int amount)
    {
        currentStamina = Mathf.Min(maxStamina, currentStamina + Mathf.Max(amount, 0));
    }

    /// <summary>
    /// Drains stamina across <paramref name="deltaTime"/> seconds at
    /// <paramref name="perSecond"/> (e.g. holding block), never below 0.
    /// Returns true if this call emptied the bar.
    /// </summary>
    public bool DrainStamina(float deltaTime, float perSecond)
    {
        if (currentStamina <= 0)
        {
            staminaDrainRemainder = 0f;
            return false;
        }

        // Same fractional carry as recovery: a single frame's drain is well under 1.
        staminaDrainRemainder += Mathf.Max(perSecond, 0f) * deltaTime;
        int whole = Mathf.FloorToInt(staminaDrainRemainder);
        staminaDrainRemainder -= whole;
        currentStamina -= whole;

        if (currentStamina <= 0)
        {
            currentStamina = 0;
            staminaDrainRemainder = 0f;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Regenerates stamina across <paramref name="deltaTime"/> seconds at
    /// <paramref name="perSecond"/>, capped at max.
    /// </summary>
    public void RecoverStamina(float deltaTime, float perSecond)
    {
        // Carry the fractional part between frames — at 60fps a single frame's
        // regen is ~0.25, which would otherwise round to 0 every time.
        staminaRecoveryRemainder += Mathf.Max(perSecond, 0f) * deltaTime;
        int whole = Mathf.FloorToInt(staminaRecoveryRemainder);
        currentStamina += whole;
        staminaRecoveryRemainder -= whole;

        if (currentStamina >= maxStamina)
        {
            currentStamina = maxStamina;
            staminaRecoveryRemainder = 0f;
        }
    }

    /// <summary>
    /// Slowly restores health across <paramref name="deltaTime"/> seconds at
    /// <paramref name="perSecond"/>, capped at max. A defeated fighter does not regenerate.
    /// </summary>
    public void RegenerateHealth(float deltaTime, float perSecond)
    {
        if (!IsAlive() || currentHealth >= maxHealth)
        {
            healthRegenRemainder = 0f;
            return;
        }

        healthRegenRemainder += Mathf.Max(perSecond, 0f) * deltaTime;
        int whole = Mathf.FloorToInt(healthRegenRemainder);
        healthRegenRemainder -= whole;
        Heal(whole);
    }

    /// <summary>
    /// True if the fighter can afford <paramref name="amount"/> stamina.
    /// </summary>
    public bool HasStamina(int amount)
    {
        return currentStamina >= amount;
    }

    /// <summary>
    /// True when HP is at or below the desperation threshold (signature unlock condition).
    /// </summary>
    public bool IsLowHealth()
    {
        return currentHealth <= Mathf.RoundToInt(maxHealth * BalanceConfig.DESPERATION_HP_THRESHOLD);
    }

    /// <summary>
    /// Adds super-bar charge, clamped to <see cref="maxSuperBar"/>.
    /// </summary>
    public void AddSuperBar(int amount)
    {
        currentSuperBar += amount;
        if (currentSuperBar > maxSuperBar) currentSuperBar = maxSuperBar;
    }

    /// <summary>
    /// Drains the super bar back to zero (after spending a signature).
    /// </summary>
    public void ResetSuperBar()
    {
        currentSuperBar = 0;
    }

    /// <summary>
    /// Signature is available when the bar is full OR the fighter is in desperation,
    /// the signature has not been spent yet, and stamina is full.
    /// </summary>
    public bool CanUseSignature()
    {
        bool meterReady = currentSuperBar >= maxSuperBar || IsLowHealth();
        return meterReady && !signatureUsed && currentStamina >= maxStamina;
    }
}
