using UnityEngine;

/// <summary>
/// Per-frame input snapshot for a single player.
/// Populated by InputManager and consumed by movement/combat systems.
/// </summary>
[System.Serializable]
public struct InputData
{
    // ==================== ATTACK BUTTONS ====================
    public bool lightAttack;     // Square
    public bool mediumAttack;    // Cross / X
    public bool heavyAttack;     // Triangle
    public bool defenseButton;   // Circle (parry, throw defense)

    // ==================== MODIFIERS ====================
    public bool specialModifier; // L1 (held)
    public bool lowModifier;     // L2 (held)
    public bool blockHeld;       // R2 (held)
    public bool throwButton;     // R1 (pressed)

    // ==================== MOVEMENT ====================
    public float moveDirection;  // -1 to 1 (left/right)
    public bool crouch;          // Down on stick
    public bool taunt;           // Up on stick
    public bool run;             // L3 click

    // ==================== DASHING ====================
    public Vector2 dashDirection; // 8-way dash, Vector2.zero if none

    // ==================== STANCE ====================
    public bool stanceSwitch;    // R3 click

    /// <summary>
    /// True if any attack button was pressed this frame.
    /// </summary>
    public bool HasAttackInput()
    {
        return lightAttack || mediumAttack || heavyAttack || defenseButton;
    }

    /// <summary>
    /// True if any input at all is detected this frame.
    /// </summary>
    public bool HasAnyInput()
    {
        return HasAttackInput()
            || specialModifier || lowModifier || blockHeld || throwButton
            || moveDirection != 0f
            || crouch || taunt || run
            || dashDirection != Vector2.zero
            || stanceSwitch;
    }
}
