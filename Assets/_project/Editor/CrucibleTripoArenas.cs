// Place one of the Tripo arenas prepared by ArtSource/Blender/prep_env.py (Environment/Tripo/<Name>/<Name>.fbx)
// in the open scene: scaled to the fight space, floor at y = 0, mesh collider, URP/Lit material from its
// .fbm maps, and the other arenas (Al-Quds, Ground, other Tripo arenas) hidden. Crucible > Arena > Tripo > ...
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CrucibleTripoArenas
{
    const string Root = "Assets/_project/Environment/Tripo";
    const float ArenaWidth = 70f;   // world units across (the Al-Quds courtyard is 64 x 44)

    [MenuItem("Crucible/Arena/Tripo/Desert arena")]      public static void Desert() => Place("DesertArena");
    [MenuItem("Crucible/Arena/Tripo/Medieval fortress")] public static void Fortress() => Place("MedievalFortress");
    [MenuItem("Crucible/Arena/Tripo/Forest clearing")]   public static void Forest() => Place("ForestClearing");
    [MenuItem("Crucible/Arena/Tripo/Back to Al-Quds")]   public static void AlQuds() => Place(null);

    static void Place(string name)
    {
        // hide every arena we know about
        foreach (var n in new[] { "Arena_AlQuds", "AlQuds_Arena", "AlQuds_Arena(Clone)", "DesertArena", "MedievalFortress", "ForestClearing" })
            foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(g => g.name == n && g.scene.IsValid()))
            { Undo.RecordObject(go, "arena"); go.SetActive(false); }
        var ground = GameObject.Find("Ground");
        if (name == null)
        {
            foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(g => (g.name.StartsWith("AlQuds_Arena") || g.name == "Arena_AlQuds") && g.scene.IsValid()))
                go.SetActive(true);
            Debug.Log("Al-Quds arena back on.");
            return;
        }
        if (ground != null && ground.activeSelf) { Undo.RecordObject(ground, "arena"); ground.SetActive(false); }

        string fbx = $"{Root}/{name}/{name}.fbx";
        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (mi == null) { Debug.LogError($"missing {fbx}"); return; }
        bool dirty = false;
        if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
        if (!mi.isReadable) { mi.isReadable = true; dirty = true; }
        if (dirty) mi.SaveAndReimport();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);

        var existing = GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(g => g.name == name && g.scene.IsValid());
        GameObject inst = existing;
        if (inst == null)
        {
            inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = name;
            Undo.RegisterCreatedObjectUndo(inst, "place arena");
        }
        inst.SetActive(true);
        inst.transform.localScale = Vector3.one;
        inst.transform.position = Vector3.zero;
        // keep the prefab root rotation: it carries the FBX axis conversion (Blender Z-up -> Unity Y-up)
        inst.transform.rotation = prefab.transform.rotation;
        var rends = inst.GetComponentsInChildren<Renderer>(true);
        var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        float scale = ArenaWidth / Mathf.Max(b.size.x, b.size.z, 1e-3f);
        inst.transform.localScale = Vector3.one * scale;
        b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        inst.transform.position = new Vector3(-b.center.x, -b.min.y, -b.center.z);   // centred, lowest point on y = 0
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
        {
            var mc = mf.GetComponent<MeshCollider>();
            if (mc == null) mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
        }
        // Tripo arenas are dioramas: the fighting floor is the largest flat, upward-facing area of the mesh.
        // Histogram the area of up-facing triangles by world height and take the biggest bin.
        float floorY = FindFloorHeight(inst);
        if (!float.IsNaN(floorY))
            inst.transform.position += Vector3.down * floorY;
        else
            Debug.LogWarning($"{name}: no flat floor found; left with its lowest point on y = 0");
        GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);

        // material
        string matDir = $"{Root}/{name}";
        var mat = BuildMaterial($"{Root}/{name}/{name}.fbm", name.ToLower(), $"{matDir}/{name}.mat");
        foreach (var r in rends) { Undo.RecordObject(r, "arena material"); r.sharedMaterial = mat; }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(inst.scene);
        Debug.Log($"{name} placed: scale {scale:F2}, {ArenaWidth} wide, floor at the centre found at local y {floorY:F2} and moved to y = 0 (arena y = {inst.transform.position.y:F2}).");
    }

    /// <summary>World height of the largest flat upward-facing area (0.25-unit bins, area-weighted).</summary>
    static float FindFloorHeight(GameObject inst)
    {
        var bins = new System.Collections.Generic.Dictionary<int, float>();
        const float binSize = 0.25f;
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh; if (mesh == null || !mesh.isReadable) continue;
            var v = mesh.vertices; var tris = mesh.triangles; var m = mf.transform.localToWorldMatrix;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[tris[i]]), b = m.MultiplyPoint3x4(v[tris[i + 1]]), c = m.MultiplyPoint3x4(v[tris[i + 2]]);
                Vector3 n = Vector3.Cross(b - a, c - a);
                float area = n.magnitude * 0.5f;
                if (area < 1e-6f) continue;
                if (n.y / n.magnitude < 0.9f) continue;   // only near-horizontal, facing up
                float y = (a.y + b.y + c.y) / 3f;
                int bin = Mathf.RoundToInt(y / binSize);
                bins[bin] = (bins.TryGetValue(bin, out float acc) ? acc : 0f) + area;
            }
        }
        if (bins.Count == 0) return float.NaN;
        var best = bins.OrderByDescending(kv => kv.Value).First();
        Debug.Log($"floor bins: {string.Join(", ", bins.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"y={kv.Key * binSize:F2}:{kv.Value:F0}"))}");
        return best.Key * binSize;
    }

    static Material BuildMaterial(string fbmDir, string baseName, string matPath)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, matPath); }
        Texture2D Load(string suffix, bool srgb, bool normal)
        {
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string p = $"{fbmDir}/{baseName}{suffix}{ext}";
                if (!File.Exists(p)) continue;
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti != null)
                {
                    bool ch = false;
                    if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ch = true; }
                    if (!normal && ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; ch = true; }
                    if (ti.maxTextureSize < 4096) { ti.maxTextureSize = 4096; ch = true; }
                    if (ch) ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }
        var baseMap = Load("_basecolor", true, false);
        var nrm = Load("_normal", false, true);
        var mask = Load("_mask", false, false);
        if (baseMap) mat.SetTexture("_BaseMap", baseMap);
        mat.SetColor("_BaseColor", Color.white);
        if (nrm) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
        if (mask) { mat.SetTexture("_MetallicGlossMap", mask); mat.SetFloat("_Metallic", 1f); mat.SetFloat("_Smoothness", 1f); mat.SetFloat("_SmoothnessTextureChannel", 0f); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); }
        else { mat.SetFloat("_Metallic", 0f); mat.SetFloat("_Smoothness", 0.3f); }
        EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
        return mat;
    }
}
