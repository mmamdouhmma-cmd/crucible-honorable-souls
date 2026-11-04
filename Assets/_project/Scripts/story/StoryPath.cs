using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A walking route over the village for the story's guides (the Sensei and the Partner leading Musashi to the plaza).
/// Grid A* on the ground, judged by the SAME rules as the player's free walk (<see cref="ExploreWalker"/>): the feet
/// follow the highest ground no more than a step above the current one, and a body-sized capsule from the step height
/// up must be clear of walls and props. The grid path is then pulled straight wherever a direct walk is clear.
/// Fighters are ignored (they are not obstacles to a guide; puppets have no colliders at all).
/// </summary>
public static class StoryPath
{
    public const float Cell = 1.0f;
    public const float StepHeight = 0.95f;   // ExploreWalker.stepHeight
    public const float Radius = 0.35f;
    const float ProbeTop = 2.0f;             // BalanceConfig.OBSTACLE_PROBE_TOP
    const int MaxNodes = 40000;

    static readonly RaycastHit[] hits = new RaycastHit[16];
    static readonly Collider[] overlaps = new Collider[16];

    /// <summary>The ground under <paramref name="p"/> reachable from height <paramref name="fromY"/> (no higher than a
    /// step up, at most 6 down), or null.</summary>
    public static float? GroundFrom(Vector3 p, float fromY)
    {
        int n = Physics.RaycastNonAlloc(new Vector3(p.x, fromY + StepHeight + 0.2f, p.z), Vector3.down, hits, StepHeight + 6.2f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.GetComponentInParent<Fighter>() != null) continue;
            if (hits[i].point.y > best) best = hits[i].point.y;
        }
        return float.IsNegativeInfinity(best) ? (float?)null : best;
    }

    /// <summary>True when a body standing with its feet at <paramref name="feet"/> is inside a wall or prop.</summary>
    public static bool Inside(Vector3 feet)
    {
        int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (StepHeight + Radius),
            feet + Vector3.up * Mathf.Max(StepHeight + Radius, ProbeTop - Radius), Radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (overlaps[i].GetComponentInParent<Fighter>() == null) return true;
        return false;
    }

    /// <summary>Walks a straight line in 0.5 steps; returns the ground at the far end, or null if it cannot be walked.</summary>
    static float? Walkable(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; d.y = 0f;
        int steps = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / 0.5f));
        float y = from.y;
        for (int i = 1; i <= steps; i++)
        {
            Vector3 p = from + d * (i / (float)steps);
            var g = GroundFrom(p, y);
            if (g == null) return null;
            y = g.Value;
            if (Inside(new Vector3(p.x, y, p.z))) return null;
        }
        return y;
    }

    /// <summary>A route from <paramref name="from"/> to <paramref name="to"/> (ground points), or null if none was found.</summary>
    public static List<Vector3> Find(Vector3 from, Vector3 to, float margin = 30f)
    {
        var fg = GroundFrom(from, from.y + 0.5f);
        if (fg != null) from.y = fg.Value;
        Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x / Cell), Mathf.RoundToInt(p.z / Cell));
        Vector3 Centre(Vector2Int k) => new Vector3(k.x * Cell, 0f, k.y * Cell);
        float minX = Mathf.Min(from.x, to.x) - margin, maxX = Mathf.Max(from.x, to.x) + margin;
        float minZ = Mathf.Min(from.z, to.z) - margin, maxZ = Mathf.Max(from.z, to.z) + margin;

        var start = Key(from); var goal = Key(to);
        var open = new SortedSet<(float f, int id)>();
        var ids = new Dictionary<Vector2Int, int>();
        var keys = new List<Vector2Int>(); var ys = new List<float>(); var g = new List<float>(); var parent = new List<int>();
        var closed = new HashSet<int>();
        int Add(Vector2Int k, float y, float cost, int par)
        {
            int id = keys.Count; keys.Add(k); ys.Add(y); g.Add(cost); parent.Add(par); ids[k] = id; return id;
        }
        float H(Vector2Int k) => Vector2.Distance(k, goal) * Cell;
        int s0 = Add(start, from.y, 0f, -1);
        open.Add((H(start), s0));
        int found = -1;
        var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
                           new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) };
        while (open.Count > 0 && keys.Count < MaxNodes)
        {
            var top = open.Min; open.Remove(top);
            int cur = top.id;
            if (!closed.Add(cur)) continue;
            if (keys[cur] == goal || Vector2.Distance(keys[cur], goal) * Cell < 1.5f) { found = cur; break; }
            foreach (var dkey in dirs)
            {
                var nk = keys[cur] + dkey;
                var c = Centre(nk);
                if (c.x < minX || c.x > maxX || c.z < minZ || c.z > maxZ) continue;
                var curPos = Centre(keys[cur]); curPos.y = ys[cur];
                var ny = Walkable(curPos, new Vector3(c.x, ys[cur], c.z));
                if (ny == null) continue;
                float cost = g[cur] + dkey.magnitude * Cell + Mathf.Abs(ny.Value - ys[cur]) * 0.5f;
                if (ids.TryGetValue(nk, out int nid))
                {
                    if (closed.Contains(nid) || cost >= g[nid]) continue;
                    open.Remove((g[nid] + H(nk), nid));
                    g[nid] = cost; ys[nid] = ny.Value; parent[nid] = cur;
                }
                else nid = Add(nk, ny.Value, cost, cur);
                open.Add((cost + H(nk), nid));
            }
        }
        if (found < 0) { Debug.LogWarning($"StoryPath: no route from {from:F1} to {to:F1} ({keys.Count} cells searched)"); return null; }

        var raw = new List<Vector3>();
        for (int i = found; i >= 0; i = parent[i]) { var p = Centre(keys[i]); p.y = ys[i]; raw.Add(p); }
        raw.Reverse();
        raw[0] = from;
        var endY = Walkable(raw[raw.Count - 1], to);
        if (endY != null) { to.y = endY.Value; raw.Add(to); }

        // pull it straight: from each kept point, jump to the furthest one a direct walk reaches
        var path = new List<Vector3> { raw[0] };
        int at = 0;
        while (at < raw.Count - 1)
        {
            int best = at + 1;
            for (int j = raw.Count - 1; j > at + 1; j--)
                if (Walkable(raw[at], raw[j]) != null) { best = j; break; }
            path.Add(raw[best]);
            at = best;
        }
        return path;
    }
}
