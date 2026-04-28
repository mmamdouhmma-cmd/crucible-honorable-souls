using UnityEngine;

/// <summary>
/// Base archetype data for a fighter. Authored as a ScriptableObject asset
/// so designers can tune characters in the inspector without code changes.
/// Runtime mutable values live in <see cref="FighterStats"/>.
/// </summary>
[CreateAssetMenu(fileName = "NewFighterData", menuName = "CRUCIBLE/Fighter Data")]
public class FighterData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string fighterName = "New Fighter";
    [SerializeField] private string archetype = "Berserker";

    [Header("Vitals")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int maxStamina = 100;
    [SerializeField] private int armor = 15;

    [Header("Combat")]
    [Tooltip("Damage scalar where 100 = 1.0x. 110 = 1.1x, 90 = 0.9x.")]
    [SerializeField] private int damageMultiplier = 100;
    [SerializeField] private int attackSpeed = 100;

    [Header("Movement")]
    [SerializeField] private int movementSpeed = 40;
    [SerializeField] private int dashDistance = 30;

    /// <summary>Display name (e.g. "Bjorn Ironside").</summary>
    public string FighterName => fighterName;

    /// <summary>Archetype label (e.g. "Berserker", "Duelist").</summary>
    public string Archetype => archetype;

    /// <summary>Starting and max HP value.</summary>
    public int MaxHealth => maxHealth;

    /// <summary>Starting and max stamina value.</summary>
    public int MaxStamina => maxStamina;

    /// <summary>Armor percentage (0-100).</summary>
    public int Armor => armor;

    /// <summary>Damage multiplier where 100 = 1.0x.</summary>
    public int DamageMultiplier => damageMultiplier;

    /// <summary>Walk/run speed (raw integer, divided by 10 at runtime).</summary>
    public int MovementSpeed => movementSpeed;

    /// <summary>Attack animation speed scalar where 100 = 1.0x.</summary>
    public int AttackSpeed => attackSpeed;

    /// <summary>Dash distance (raw integer for designer tuning).</summary>
    public int DashDistance => dashDistance;
}
