using UnityEngine;

/// <summary>
/// Pure damage math. No state, no side effects — given attack and defender values,
/// returns the integer HP a hit should subtract. Centralised so combat tweaks live
/// in one spot and unit tests can hit it directly.
/// </summary>
public static class DamageCalculator
{
    /// <summary>
    /// Computes final integer damage for a single hit.
    ///
    /// Pipeline:
    ///   1. Scale base damage by the attacker's damage multiplier (100 = 1.0x) and
    ///      any bonus multiplier (e.g. dash counter).
    ///   2. Reduce defender armor by the attack's armor pierce percentage.
    ///   3. Apply remaining armor as a flat percentage damage reduction.
    ///   4. Apply the defender's block reduction percentage (0 when not blocking).
    ///   5. Round to the nearest integer; clamp to a minimum of 1 if the hit landed.
    /// </summary>
    public static int CalculateDamage(
        int baseDamage,
        int attackerDamageMultiplier,
        int defenderArmor,
        int armorPierce,
        int blockReductionPercent,
        float bonusMultiplier = 1f)
    {
        if (baseDamage <= 0) return 0;

        // Step 1: attacker's damage multiplier (100 = 1.0x) and situational bonus.
        float damage = baseDamage * (attackerDamageMultiplier / 100f) * bonusMultiplier;

        // Step 2: effective armor after pierce.
        int pierce = Mathf.Clamp(armorPierce, 0, 100);
        int armor = Mathf.Clamp(defenderArmor, 0, 100);
        float effectiveArmor = armor * (100 - pierce) / 100f;

        // Step 3: armor as flat percent reduction.
        damage *= 1f - (effectiveArmor / 100f);

        // Step 4: blocking shaves off another chunk.
        int block = Mathf.Clamp(blockReductionPercent, 0, 100);
        damage *= 1f - (block / 100f);

        int finalDamage = Mathf.RoundToInt(damage);
        if (finalDamage < 1) finalDamage = 1;
        return finalDamage;
    }

    /// <summary>
    /// True when the attacker is positioned behind the defender's facing direction.
    /// Reserved for unblockable rear-attack logic (future feature).
    /// </summary>
    public static bool IsHitFromBehind(Vector3 attackerPos, Vector3 defenderPos, Vector3 defenderForward)
    {
        Vector3 toAttacker = (attackerPos - defenderPos).normalized;
        return Vector3.Dot(defenderForward, toAttacker) < 0f;
    }
}
