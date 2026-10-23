using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes the wall see-through when a fighter is pressed into it. Each arena is ONE mesh, so this
/// cannot fade a material; instead, while the straight line from the camera to a fighter's chest
/// is blocked by the arena, the arena is swapped onto the Crucible/ArenaCutout shader, which
/// dissolves a soft hole along that sight line. The strength fades in and out per fighter, and the
/// original material comes back the moment both fighters are clear, so the arena renders exactly
/// as authored whenever nothing is hidden.
/// Lives on GameMangers; works against whichever arena is active (it carries ArenaBounds).
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

    // Per-arena swap state: the renderer whose material is currently replaced.
    private Renderer swapped;
    private Material original;
    private Material cutout;
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
        Renderer arenaRenderer = arena != null ? arena.GetComponent<Renderer>() : null;
        Collider arenaCollider = arena != null ? arena.GetComponent<Collider>() : null;

        if (cam == null || arenaRenderer == null || arenaCollider == null)
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
            bool occluded = dist > 0.5f &&
                arenaCollider.Raycast(new Ray(camPos, toChest / dist), out _, dist - 0.2f);

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
        if (anyCut)
        {
            EnsureSwapped(arenaRenderer);
            if (cutout != null)
            {
                cutout.SetVector(CutB0, cut0);
                cutout.SetVector(CutB1, cut1);
                cutout.SetFloat(CutRadius, cutRadius);
            }
        }
        else RestoreArena();
    }

    private void Fade(int slot, float target)
    {
        strength[slot] = Mathf.MoveTowards(strength[slot], target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
    }

    private void EnsureSwapped(Renderer arenaRenderer)
    {
        if (swapped == arenaRenderer && cutout != null) return;
        RestoreArena();

        Material source = arenaRenderer.sharedMaterial;
        if (source == null) return;
        if (!cutoutCache.TryGetValue(source, out Material mat) || mat == null)
        {
            if (cutoutShader == null) cutoutShader = Shader.Find("Crucible/ArenaCutout");
            if (cutoutShader == null)
            {
                if (debugLogs) Debug.LogWarning("WallCutout: Crucible/ArenaCutout shader not found.");
                return;
            }
            mat = new Material(cutoutShader) { name = source.name + " (cutout)" };
            if (source.HasProperty(BaseMap)) mat.SetTexture(BaseMap, source.GetTexture(BaseMap));
            if (source.HasProperty(BaseColor)) mat.SetColor(BaseColor, source.GetColor(BaseColor));
            if (source.HasProperty(BumpMap)) mat.SetTexture(BumpMap, source.GetTexture(BumpMap));
            cutoutCache[source] = mat;
        }

        swapped = arenaRenderer;
        original = source;
        cutout = mat;
        arenaRenderer.sharedMaterial = cutout;
        if (debugLogs) Debug.Log($"WallCutout: seeing through {arenaRenderer.gameObject.name}");
    }

    private void RestoreArena()
    {
        if (swapped != null && original != null) swapped.sharedMaterial = original;
        swapped = null;
        original = null;
        cutout = null;
    }
}
