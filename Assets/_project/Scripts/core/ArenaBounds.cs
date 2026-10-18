using UnityEngine;

/// <summary>
/// Keeps the fighters inside the arena.
///
/// This is a script clamp rather than wall colliders on purpose: each fighter's Rigidbody freezes
/// position X and Z (movement is written straight to the transform by Fighter), so PhysX can never
/// push a fighter out of a wall - depenetration cannot move a frozen axis. Wall colliders simply
/// do nothing against them, however well sized.
///
/// One of these belongs on the active arena; the Crucible > Arena menu items set the shape when
/// they place one. <see cref="Fighter"/> calls <see cref="ClampPoint"/> at the end of its Update,
/// after every move (walk, dash, lunge, knockback).
/// </summary>
public class ArenaBounds : MonoBehaviour
{
    public enum Shape { Circle, Rect }

    [Tooltip("Circle suits the Tripo arenas (a ring of floor); Rect suits the Al-Quds courtyard.")]
    [SerializeField] private Shape shape = Shape.Circle;

    [Tooltip("Centre of the fighting area in world space (XZ; Y is ignored).")]
    [SerializeField] private Vector3 centre = Vector3.zero;

    [Tooltip("Circle only: how far from the centre a fighter may stand, in world units.")]
    [Min(1f)]
    [SerializeField] private float radius = 24f;

    [Tooltip("Rect only: half the width (X) and half the depth (Z), in world units.")]
    [SerializeField] private Vector2 halfExtents = new Vector2(29f, 19f);

    [Tooltip("Log the first time each fighter is pushed back in.")]
    [SerializeField] private bool debugLogs = false;

    /// <summary>The bounds in use, or null when no arena provides any (fighters are then unbounded).</summary>
    public static ArenaBounds Instance { get; private set; }

    private void OnEnable() { Instance = this; }
    private void OnDisable() { if (Instance == this) Instance = null; }

    /// <summary>
    /// The nearest point inside the arena for a body of <paramref name="bodyRadius"/>, keeping the
    /// given Y. Returns the point unchanged when it is already inside.
    /// </summary>
    public Vector3 Clamp(Vector3 p, float bodyRadius)
    {
        if (shape == Shape.Circle)
        {
            Vector2 offset = new Vector2(p.x - centre.x, p.z - centre.z);
            float limit = Mathf.Max(0.1f, radius - bodyRadius);
            if (offset.sqrMagnitude <= limit * limit) return p;

            offset = offset.normalized * limit;
            return new Vector3(centre.x + offset.x, p.y, centre.z + offset.y);
        }

        float limX = Mathf.Max(0.1f, halfExtents.x - bodyRadius);
        float limZ = Mathf.Max(0.1f, halfExtents.y - bodyRadius);
        float x = Mathf.Clamp(p.x - centre.x, -limX, limX) + centre.x;
        float z = Mathf.Clamp(p.z - centre.z, -limZ, limZ) + centre.z;
        if (Mathf.Approximately(x, p.x) && Mathf.Approximately(z, p.z)) return p;
        return new Vector3(x, p.y, z);
    }

    /// <summary>Clamps against the active bounds, or returns the point untouched when there are none.</summary>
    public static Vector3 ClampPoint(Vector3 p, float bodyRadius)
    {
        return Instance != null ? Instance.Clamp(p, bodyRadius) : p;
    }

    /// <summary>Called by Fighter so a push-back can be logged once per fighter.</summary>
    public bool DebugLogs => debugLogs;

    /// <summary>Sets the shape from code (used by the arena placement menus).</summary>
    public void Configure(Shape newShape, Vector3 newCentre, float newRadius, Vector2 newHalfExtents)
    {
        shape = newShape;
        centre = newCentre;
        radius = newRadius;
        halfExtents = newHalfExtents;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        if (shape == Shape.Circle)
        {
            const int steps = 48;
            Vector3 prev = centre + new Vector3(radius, 0.2f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float a = i * 2f * Mathf.PI / steps;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * radius, 0.2f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
        else
        {
            Gizmos.DrawWireCube(centre + Vector3.up * 0.2f, new Vector3(halfExtents.x * 2f, 0.4f, halfExtents.y * 2f));
        }
    }
}
