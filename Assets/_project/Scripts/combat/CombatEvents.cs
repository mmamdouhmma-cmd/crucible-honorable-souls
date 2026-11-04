using System;

/// <summary>How one strike was resolved against its target.</summary>
public enum HitOutcome
{
    Hit,          // landed clean (damage, flinch or armour)
    Blocked,      // met a raised guard (chip damage, guard held)
    GuardBroken,  // met a guard that could not hold: full damage
    Parried,      // deflected by a parry: no damage, attacker stunned
    Evaded        // dodged by a dash
}

/// <summary>Everything a listener may want to know about one resolved strike.</summary>
public struct HitReport
{
    public Fighter attacker;
    public Fighter target;
    public AttackData attack;
    public HitOutcome outcome;
    public int damage;
    public int stringDepth;        // 0 = a fresh swing, 1 = first follow-up, ...
    public bool dashCounter;       // thrown inside the attacker's dash counter window
    public bool parryCounter;      // thrown inside the attacker's parry counter window
    public bool evadeOpensCounter; // Evaded only: did the dodge earn a counter window
    public string dodgeDirection;  // Evaded only: Fighter.DescribeDash() of the dodger, e.g. "back"
}

/// <summary>
/// Read-only combat notifications for layers built ON TOP of the fight (story objectives,
/// tutorials). Raised by CombatSystem at the exact points the combat result is decided;
/// subscribing changes nothing about how the fight plays. With no listeners they cost nothing.
/// </summary>
public static class CombatEvents
{
    /// <summary>An attack has started (raw swing or string follow-up).</summary>
    public static event Action<Fighter, AttackData, int> AttackStarted;   // attacker, attack, stringDepth

    /// <summary>An attack has ended. touched = it hit, was blocked or parried.</summary>
    public static event Action<Fighter, AttackData, bool> AttackEnded;    // attacker, attack, touched

    /// <summary>A strike was resolved against its target.</summary>
    public static event Action<HitReport> HitResolved;

    internal static void RaiseAttackStarted(Fighter f, AttackData a, int depth) { AttackStarted?.Invoke(f, a, depth); }
    internal static void RaiseAttackEnded(Fighter f, AttackData a, bool touched) { AttackEnded?.Invoke(f, a, touched); }
    internal static void RaiseHit(HitReport r) { HitResolved?.Invoke(r); }

    /// <summary>Drops every listener (a scene change).</summary>
    public static void Clear() { AttackStarted = null; AttackEnded = null; HitResolved = null; }
}
