using UnityEngine;

/// <summary>
/// Training-room rules. Added to the GameMangers object by <see cref="GameMenu"/> when a practice
/// session starts and switched off again when it ends.
///
/// It deliberately works by topping the bars back up rather than by making damage a no-op: every
/// hit still resolves exactly as it does in a real fight (hitstun, clash, counters, the damage
/// numbers in the log), so what is being drilled is the real thing. Only the consequence is removed.
/// </summary>
[DisallowMultipleComponent]
public class PracticeMode : MonoBehaviour
{
    public static PracticeMode Instance { get; private set; }

    [Tooltip("Keep both fighters' health bars full, so a drill is never interrupted by a KO.")]
    public bool infiniteHealth = true;

    [Tooltip("Keep both fighters' stamina full, so specials and dashes are always affordable.")]
    public bool infiniteStamina = true;

    [Tooltip("Return both fighters to their marks when they drift further apart than this. 0 = never.")]
    public float autoResetDistance = 0f;

    /// <summary>True while a practice session is running.</summary>
    public bool Running { get; private set; }

    private Fighter[] fighters = new Fighter[0];
    private System.Action resetPositions;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    /// <summary>Starts the session. <paramref name="putOnMarks"/> is the menu's own spawn reset.</summary>
    public void Begin(System.Action putOnMarks)
    {
        fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
        resetPositions = putOnMarks;
        Running = true;
        enabled = true;
    }

    /// <summary>Back on the marks (a ring-out in practice resets instead of ending the drill).</summary>
    public void ResetPositions() => resetPositions?.Invoke();

    public void End()
    {
        Running = false;
        enabled = false;
    }

    private void LateUpdate()
    {
        if (!Running || fighters == null) return;

        foreach (var f in fighters)
        {
            if (f == null || f.Stats == null) continue;
            var s = f.Stats;
            // Only while alive: a fighter that has already fallen should stay down until the menu
            // resets the match, or the death animation would be interrupted by a full bar.
            if (infiniteHealth && s.currentHealth > 0 && s.currentHealth < s.maxHealth) s.currentHealth = s.maxHealth;
            if (infiniteStamina && s.currentStamina < s.maxStamina) s.currentStamina = s.maxStamina;
        }

        if (autoResetDistance > 0.1f && fighters.Length >= 2 && fighters[0] != null && fighters[1] != null)
        {
            Vector3 a = fighters[0].transform.position, b = fighters[1].transform.position;
            a.y = b.y = 0f;
            if (Vector3.Distance(a, b) > autoResetDistance) resetPositions?.Invoke();
        }
    }
}
