using UnityEngine;

/// <summary>
/// Trigger-driven hitbox attached to a child of a fighter. The owning
/// <see cref="CombatSystem"/> activates this on the first active frame and
/// deactivates it when the active window closes. Hits register at most once
/// per activation to prevent multi-hits from a single swing.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class HitDetector : MonoBehaviour
{
    private BoxCollider hitbox;
    private CombatSystem owner;
    private Fighter ownerFighter;
    private bool hasHit;

    [SerializeField] private bool debugLogs = true;

    private void Awake()
    {
        hitbox = GetComponent<BoxCollider>();
        if (hitbox == null)
        {
            hitbox = gameObject.AddComponent<BoxCollider>();
        }
        hitbox.isTrigger = true;
        hitbox.enabled = false;
    }

    /// <summary>
    /// Arms the hitbox for the active frames of <paramref name="attack"/>. Positions
    /// and sizes the trigger from the attack's local-space data and resets the
    /// "already hit" flag so the next swing can register a fresh hit.
    /// </summary>
    public void Activate(Fighter ownerFighterRef, CombatSystem ownerCombat, AttackData attack)
    {
        owner = ownerCombat;
        ownerFighter = ownerFighterRef;
        hasHit = false;

        if (attack != null)
        {
            transform.localPosition = attack.HitboxOffset;
            hitbox.center = Vector3.zero;
            hitbox.size = attack.HitboxSize;
        }

        hitbox.enabled = true;

        if (debugLogs)
        {
            Debug.Log($"Hitbox activated ({(attack != null ? attack.AttackName : "<null>")})");
        }
    }

    /// <summary>Disables the trigger collider so it can no longer register hits.</summary>
    public void Deactivate()
    {
        hitbox.enabled = false;

        if (debugLogs)
        {
            Debug.Log("Hitbox deactivated");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasHit) return;
        if (owner == null) return;

        Fighter otherFighter = other.GetComponent<Fighter>();
        if (otherFighter == null)
        {
            otherFighter = other.GetComponentInParent<Fighter>();
        }

        if (otherFighter == null) return;
        if (otherFighter == ownerFighter) return;

        hasHit = true;
        owner.OnHit(otherFighter);
    }
}
