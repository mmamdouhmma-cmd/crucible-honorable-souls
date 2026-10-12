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

    /// <summary>True once the current activation has touched a fighter (one hit per swing).</summary>
    public bool HasHit => hasHit;

    [SerializeField] private bool debugLogs = true;

    [Tooltip("True when this detector sits on the weapon mesh: the trigger box stays where it was authored " +
             "(along the blade) and only gets switched on for the active frames, instead of being re-shaped " +
             "from the attack's HitboxRange/Offset/Size in front of the fighter.")]
    [SerializeField] private bool followWeapon = false;

    /// <summary>True when the box is the weapon's own blade box rather than the authored front box.</summary>
    public bool FollowWeapon { get => followWeapon; set => followWeapon = value; }

    [Tooltip("Weapon boxes only: radius of the swept capsule between the blade's position last frame and now, " +
             "so a fast swing cannot pass through the opponent between two physics steps. World units.")]
    [SerializeField] private float sweepRadius = 0.25f;

    [Tooltip("Weapon boxes only: the blade tip must move at least this fast (world units per second) for a touch " +
             "to count, so a weapon resting against the opponent in the guard does not 'hit' the moment it arms.")]
    [SerializeField] private float minBladeSpeed = 3f;

    private Vector3 lastBase, lastTip;
    private bool hasLast;
    private float tipSpeed;

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

        if (attack != null && !followWeapon)
        {
            GetLocalBox(attack, out Vector3 center, out Vector3 size);

            // Attack data is authored in world units. The fighter root is usually scaled to
            // size its model (e.g. 3x or 4x), and a child collider inherits that scale, so
            // divide it back out. Otherwise a 1.5 unit hitbox on a 4x fighter reaches 6 units.
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localPosition = Divide(center, parentScale);
            hitbox.center = Vector3.zero;
            hitbox.size = Divide(size, transform.lossyScale);
        }

        hitbox.enabled = true;
        hasLast = false;

        if (debugLogs)
        {
            Debug.Log($"Hitbox activated ({(attack != null ? attack.AttackName : "<null>")})");
        }
    }

    /// <summary>Blade box ends in world space: the box's local extent along its longest axis.</summary>
    private void BladeSegment(out Vector3 a, out Vector3 b)
    {
        Vector3 s = hitbox.size;
        Vector3 axis = s.x >= s.y && s.x >= s.z ? Vector3.right : (s.y >= s.z ? Vector3.up : Vector3.forward);
        float half = Mathf.Max(s.x, s.y, s.z) * 0.5f;
        a = transform.TransformPoint(hitbox.center - axis * half);
        b = transform.TransformPoint(hitbox.center + axis * half);
    }

    /// <summary>
    /// Weapon boxes: on top of the trigger, sweep a capsule from last frame's blade to this frame's blade
    /// (both ends), so a swing that crosses the opponent between two frames still registers.
    /// </summary>
    private void Update()
    {
        if (!followWeapon || hitbox == null || !hitbox.enabled || hasHit || owner == null) return;
        BladeSegment(out Vector3 bse, out Vector3 tip);
        if (hasLast)
        {
            tipSpeed = Time.deltaTime > 1e-5f ? (tip - lastTip).magnitude / Time.deltaTime : 0f;
            if (tipSpeed >= minBladeSpeed)
            {
                TrySweep(lastTip, tip);
                TrySweep(lastBase, bse);
                TrySweep((lastBase + lastTip) * 0.5f, (bse + tip) * 0.5f);
                TrySweep(bse, tip);
            }
        }
        lastBase = bse; lastTip = tip; hasLast = true;
    }

    private static readonly Collider[] sweepHits = new Collider[16];

    private void TrySweep(Vector3 a, Vector3 b)
    {
        if (hasHit) return;
        int n = Physics.OverlapCapsuleNonAlloc(a, b, sweepRadius, sweepHits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var other = sweepHits[i];
            if (other == null || other.isTrigger) continue;
            Fighter f = other.GetComponent<Fighter>() ?? other.GetComponentInParent<Fighter>();
            if (f == null || f == ownerFighter) continue;
            hasHit = true;
            if (debugLogs) Debug.Log($"Blade sweep hit {f.name}");
            owner.OnHit(f);
            return;
        }
    }

    /// <summary>
    /// Hitbox for <paramref name="attack"/> relative to the fighter's position and facing,
    /// in world units (the fighter's model scale is ignored): it starts at the attacker
    /// (shifted by HitboxOffset) and reaches HitboxRange forward, with HitboxSize giving
    /// its width (X) and height (Y).
    /// </summary>
    public static void GetLocalBox(AttackData attack, out Vector3 center, out Vector3 size)
    {
        float range = Mathf.Max(attack.HitboxRange, 0.01f);
        center = attack.HitboxOffset + Vector3.forward * (range * 0.5f);
        size = new Vector3(attack.HitboxSize.x, attack.HitboxSize.y, range);
    }

    /// <summary>Component-wise a / b, leaving a component alone when its scale is zero.</summary>
    private static Vector3 Divide(Vector3 a, Vector3 b)
    {
        return new Vector3(
            Mathf.Approximately(b.x, 0f) ? a.x : a.x / b.x,
            Mathf.Approximately(b.y, 0f) ? a.y : a.y / b.y,
            Mathf.Approximately(b.z, 0f) ? a.z : a.z / b.z);
    }

    /// <summary>Draws the live hitbox in the Scene view while it is active.</summary>
    private void OnDrawGizmos()
    {
        if (hitbox == null || !hitbox.enabled) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
        Gizmos.DrawCube(hitbox.center, hitbox.size);
        if (followWeapon && hasLast)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
            Gizmos.DrawLine(lastTip, lastBase);
            Gizmos.DrawWireSphere(lastTip, sweepRadius);
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
        if (other.isTrigger) return; // only bodies can be hit, not the opponent's own hitbox
        if (followWeapon && tipSpeed < minBladeSpeed) return; // a resting blade does not hit; the sweep handles the swing

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
