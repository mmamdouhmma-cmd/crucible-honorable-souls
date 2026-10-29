using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes the wall see-through when a fighter is pressed into it. An arena mesh cannot simply fade
/// (walls and floor share a renderer), so while the straight line from the camera to a fighter's
/// chest is blocked, the meshes along that line are swapped onto the Crucible/ArenaCutout shader,
/// which dissolves a soft hole along the sight line. The strength fades in and out per fighter,
/// and the original materials come back the moment both fighters are clear, so the arena renders
/// exactly as authored whenever nothing is hidden.
/// Lives on GameMangers; works against whichever arena is active (it carries ArenaBounds). The
/// arena's meshes are everything under <see cref="ArenaBounds.Scenery"/>: the root of a one-mesh
/// Tripo arena, or a whole set-dressed environment (the Musashi village). Occluders are its
/// non-trigger MeshColliders - the box colliders that fence the one-mesh arenas are invisible, so
/// they never open a hole.
/// </summary>
public class WallCutout : MonoBehaviour
{
    [Tooltip("Radius of the see-through hole, in world units. Fighters stand ~2.7 tall.")]
    [SerializeField] private float cutRadius = 2.4f;

    [Tooltip("Seconds for the hole to fade fully in (and out).")]
    [SerializeField] private float fadeTime = 0.18f;

    [Tooltip("The Crucible/ArenaCutout shader. Serialized so builds cannot strip it; found by name when empty.")]
    [SerializeField] private Shader cutoutShader;

    [SerializeField] private bool debugLogs = false;

    private readonly List<Fighter> fighters = new List<Fighter>(4);
    private float rosterRefreshAt;
    private readonly float[] strength = new float[2];

    // The active arena's pieces, gathered once per arena.
    private Transform arenaRoot;
    private readonly List<Renderer> arenaRenderers = new List<Renderer>();
    private readonly List<MeshCollider> arenaColliders = new List<MeshCollider>();

    // Swap state: each renderer whose materials are replaced, with its own originals.
    private readonly Dictionary<Renderer, Material[]> swapped = new Dictionary<Renderer, Material[]>();
    private readonly List<Renderer> touched = new List<Renderer>();
    private readonly List<Renderer> dropped = new List<Renderer>();
    private static readonly Dictionary<Material, Material> cutoutCache = new Dictionary<Material, Material>();

    private static readonly int CutB0 = Shader.PropertyToID("_CutB0");
    private static readonly int CutB1 = Shader.PropertyToID("_CutB1");
    private static readonly int CutRadius = Shader.PropertyToID("_CutRadius");
    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");

    private void OnDisable() { RestoreArena(); }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        ArenaBounds arena = ArenaBounds.Instance;
        Transform root = arena != null ? arena.Scenery : null;
        if (root != arenaRoot) GatherArena(root);

        if (cam == null || arenaRenderers.Count == 0 || arenaColliders.Count == 0)
        {
            Fade(0, 0f); Fade(1, 0f);
            RestoreArena();
            return;
        }

        if (Time.unscaledTime >= rosterRefreshAt)
        {
            rosterRefreshAt = Time.unscaledTime + 0.5f;
            fighters.Clear();
            foreach (var f in FindObjectsByType<Fighter>(FindObjectsSortMode.None))
                if (f.gameObject.activeInHierarchy) fighters.Add(f);
        }

        Vector3 camPos = cam.transform.position;
        Vector4 cut0 = Vector4.zero, cut1 = Vector4.zero;
        int slot = 0;
        foreach (var f in fighters)
        {
            if (slot >= 2) break;
            if (f == null || !f.gameObject.activeInHierarchy) continue;

            Vector3 chest = HitEffects.Chest(f);
            Vector3 toChest = chest - camPos;
            float dist = toChest.magnitude;
            bool occluded = dist > 0.5f && Blocked(new Ray(camPos, toChest / dist), dist - 0.2f);

            Fade(slot, occluded ? 1f : 0f);
            if (strength[slot] > 0.001f)
            {
                // The hole runs a little past the chest, so a body half-sunk INTO the wall is
                // opened up whole rather than sliced at the torso.
                Vector3 end = chest + toChest / dist * 1.2f;
                Vector4 v = new Vector4(end.x, end.y, end.z, strength[slot]);
                if (slot == 0) cut0 = v; else cut1 = v;
            }
            slot++;
        }
        for (; slot < 2; slot++) Fade(slot, 0f);

        bool anyCut = strength[0] > 0.001f || strength[1] > 0.001f;
        if (anyCut) SwapAlong(camPos, cut0, cut1);
        else RestoreArena();
    }

    private void GatherArena(Transform root)
    {
        RestoreArena();
        arenaRoot = root;
        arenaRenderers.Clear();
        arenaColliders.Clear();
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) arenaRenderers.Add(r);
        foreach (var c in root.GetComponentsInChildren<MeshCollider>(true))
            if (!c.isTrigger) arenaColliders.Add(c);
        if (debugLogs) Debug.Log($"WallCutout: arena {root.name}, {arenaRenderers.Count} renderers, {arenaColliders.Count} occluders");
    }

    private bool Blocked(Ray ray, float maxDistance)
    {
        foreach (var c in arenaColliders)
            if (c != null && c.enabled && c.gameObject.activeInHierarchy && c.Raycast(ray, out _, maxDistance)) return true;
        return false;
    }

    /// <summary>
    /// Swaps every visible arena renderer a cut can touch (the capsule from the camera to the
    /// fighter's end, radius cutRadius), restores the ones it has left, and feeds the cuts in.
    /// On a one-mesh arena that is the whole arena, exactly as before.
    /// </summary>
    private void SwapAlong(Vector3 camPos, Vector4 cut0, Vector4 cut1)
    {
        touched.Clear();
        foreach (var r in arenaRenderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (Touches(r.bounds, camPos, cut0) || Touches(r.bounds, camPos, cut1)) touched.Add(r);
        }

        dropped.Clear();
        foreach (var kv in swapped) if (!touched.Contains(kv.Key)) dropped.Add(kv.Key);
        foreach (var r in dropped) { if (r != null) r.sharedMaterials = swapped[r]; swapped.Remove(r); }

        foreach (var r in touched)
        {
            if (!swapped.ContainsKey(r))
            {
                Material[] originals = r.sharedMaterials;
                var cuts = new Material[originals.Length];
                bool any = false;
                for (int i = 0; i < originals.Length; i++)
                {
                    cuts[i] = CutoutFor(originals[i]);
                    if (cuts[i] == null) cuts[i] = originals[i]; else any = true;
                }
                if (!any) continue;
                swapped[r] = originals;
                r.sharedMaterials = cuts;
                if (debugLogs) Debug.Log($"WallCutout: seeing through {r.gameObject.name}");
            }
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader != cutoutShader) continue;
                m.SetVector(CutB0, cut0);
                m.SetVector(CutB1, cut1);
                m.SetFloat(CutRadius, cutRadius);
            }
        }
    }

    private bool Touches(Bounds b, Vector3 camPos, Vector4 cut)
    {
        if (cut.w <= 0.001f) return false;
        Vector3 seg = new Vector3(cut.x, cut.y, cut.z) - camPos;
        float len = seg.magnitude;
        if (len < 1e-4f) return false;
        b.Expand(cutRadius * 2f);
        if (b.Contains(camPos)) return true;
        return b.IntersectRay(new Ray(camPos, seg / len), out float hit) && hit <= len;
    }

    private void Fade(int slot, float target)
    {
        strength[slot] = Mathf.MoveTowards(strength[slot], target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
    }

    private Material CutoutFor(Material source)
    {
        if (source == null) return null;
        if (cutoutCache.TryGetValue(source, out Material mat) && mat != null) return mat;
        if (cutoutShader == null) cutoutShader = Shader.Find("Crucible/ArenaCutout");
        if (cutoutShader == null)
        {
            if (debugLogs) Debug.LogWarning("WallCutout: Crucible/ArenaCutout shader not found.");
            return null;
        }
        mat = new Material(cutoutShader) { name = source.name + " (cutout)" };
        if (source.HasProperty(BaseMap)) mat.SetTexture(BaseMap, source.GetTexture(BaseMap));
        if (source.HasProperty(BaseColor)) mat.SetColor(BaseColor, source.GetColor(BaseColor));
        if (source.HasProperty(BumpMap)) mat.SetTexture(BumpMap, source.GetTexture(BumpMap));
        cutoutCache[source] = mat;
        return mat;
    }

    private void RestoreArena()
    {
        foreach (var kv in swapped)
            if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
        swapped.Clear();
    }
}
