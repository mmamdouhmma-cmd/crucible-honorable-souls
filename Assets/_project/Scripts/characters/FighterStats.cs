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

    /// <summary>
    /// Regenerates stamina across <paramref name="deltaTime"/> seconds, capped at max.
    /// </summary>
    public void RecoverStamina(float deltaTime)
    {
        currentStamina += Mathf.RoundToInt(BalanceConfig.STAMINA_RECOVERY_PER_SECOND * deltaTime);
        if (currentStamina > maxStamina) currentStamina = maxStamina;
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
