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

    [Tooltip("Keep-out circles INSIDE the arena (x, z = centre, y = radius, world units). Fighters " +
             "are pushed to the rim and walk around - used for the desert hill, which they used to " +
             "climb and float on. A script clamp for the same reason the outer bound is one: the " +
             "fighters' frozen X/Z axes ignore physical colliders.")]
    [SerializeField] private Vector3[] keepOut = new Vector3[0];

    [Tooltip("Ring-out gaps: openings a fighter can FALL out of (x, y = one end, z, w = the other, " +
             "world XZ). Crossing one by ringOutDepth loses the match, walking out included (the " +
             "user's call, 2026-10-27). The desert has one, between the wall end and the statue.")]
    [SerializeField] private Vector4[] ringOutGaps = new Vector4[0];

    [Tooltip("How far past a gap's line (world units) the fighter's centre must be to be out - " +
             "the floor's lip, where the drop starts.")]
    [SerializeField] private float ringOutDepth = 2f;

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
            if (offset.sqrMagnitude > limit * limit)
            {
                offset = offset.normalized * limit;
                p = new Vector3(centre.x + offset.x, p.y, centre.z + offset.y);
            }
            return PushOutOfKeepOut(p, bodyRadius);
        }

        float limX = Mathf.Max(0.1f, halfExtents.x - bodyRadius);
        float limZ = Mathf.Max(0.1f, halfExtents.y - bodyRadius);
        float x = Mathf.Clamp(p.x - centre.x, -limX, limX) + centre.x;
        float z = Mathf.Clamp(p.z - centre.z, -limZ, limZ) + centre.z;
        return PushOutOfKeepOut(new Vector3(x, p.y, z), bodyRadius);
    }

    /// <summary>
    /// Pushes a point outside every keep-out circle (nearest rim, same Y). A fighter walking at a
    /// zone slides along its rim, so "walk around it" happens on its own.
    /// </summary>
    private Vector3 PushOutOfKeepOut(Vector3 p, float bodyRadius)
    {
        for (int i = 0; i < keepOut.Length; i++)
        {
            Vector3 zone = keepOut[i];
            float keep = zone.y + bodyRadius;
            Vector2 offset = new Vector2(p.x - zone.x, p.z - zone.z);
            float sq = offset.sqrMagnitude;
            if (sq >= keep * keep || zone.y <= 0f) continue;

            // Dead centre has no outward direction: push toward the arena centre's opposite.
            if (sq < 1e-6f) offset = new Vector2(zone.x - centre.x, zone.z - centre.z).normalized;
            if (offset.sqrMagnitude < 1e-6f) offset = Vector2.right;

            offset = offset.normalized * keep;
            p = new Vector3(zone.x + offset.x, p.y, zone.z + offset.y);
        }
        return p;
    }

    /// <summary>Clamps against the active bounds, or returns the point untouched when there are none.</summary>
    public static Vector3 ClampPoint(Vector3 p, float bodyRadius)
    {
        return Instance != null ? Instance.Clamp(p, bodyRadius) : p;
    }

    /// <summary>
    /// True when <paramref name="p"/> has gone out through a ring-out gap; <paramref name="outward"/>
    /// is the direction away from the arena through that gap (for the fall).
    /// </summary>
    public bool IsRingOut(Vector3 p, out Vector3 outward)
    {
        outward = Vector3.zero;
        for (int i = 0; i < ringOutGaps.Length; i++)
        {
            Vector4 g = ringOutGaps[i];
            Vector2 a = new Vector2(g.x, g.y), b = new Vector2(g.z, g.w);
            Vector2 along = b - a;
            float len = along.magnitude;
            if (len < 0.01f) continue;
            along /= len;
            Vector2 normal = new Vector2(-along.y, along.x);
            Vector2 mid = (a + b) * 0.5f;
            if (Vector2.Dot(normal, mid - new Vector2(centre.x, centre.z)) < 0f) normal = -normal;

            Vector2 rel = new Vector2(p.x, p.z) - a;
            float t = Vector2.Dot(rel, along);
            if (t < 0f || t > len) continue;
            if (Vector2.Dot(rel, normal) < ringOutDepth) continue;

            outward = new Vector3(normal.x, 0f, normal.y);
            return true;
        }
        return false;
    }

    /// <summary>Static form of <see cref="IsRingOut(Vector3, out Vector3)"/> against the active bounds.</summary>
    public static bool RingOutAt(Vector3 p, out Vector3 outward)
    {
        outward = Vector3.zero;
        return Instance != null && Instance.IsRingOut(p, out outward);
    }

    /// <summary>Sets the ring-out gaps from code (setup evals).</summary>
    public void SetRingOutGaps(float depth, params Vector4[] gaps)
    {
        ringOutDepth = depth;
        ringOutGaps = gaps ?? new Vector4[0];
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

    /// <summary>Adds or replaces the keep-out circles from code (arena placement menus / setup evals).</summary>
    public void SetKeepOut(params Vector3[] zones)
    {
        keepOut = zones ?? new Vector3[0];
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        foreach (var zone in keepOut)
        {
            const int steps = 32;
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
            Vector3 prev = new Vector3(zone.x + zone.y, 0.2f, zone.z);
            for (int i = 1; i <= steps; i++)
            {
                float a = i * 2f * Mathf.PI / steps;
                Vector3 next = new Vector3(zone.x + Mathf.Cos(a) * zone.y, 0.2f, zone.z + Mathf.Sin(a) * zone.y);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.9f);
        foreach (var g in ringOutGaps) Gizmos.DrawLine(new Vector3(g.x, 0.3f, g.y), new Vector3(g.z, 0.3f, g.w));
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
