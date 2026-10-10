// Crucible > Arena > Place Al-Quds arena
// Drops the Blender-built arena (Assets/_project/Environment/AlQuds/AlQuds_Arena.fbx) into the open
// scene at the origin, builds HDRP/Lit materials from the baked textures, hides the old test ground
// and aims the sun from behind the far wall so the dome and minaret rim-light. Cosmetic only.
#if HDRP_PRESENT
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public static class CrucibleArenaSetup
{
    private const string ArenaDir = "Assets/_project/Environment/AlQuds";
    private const string ArenaFbx = ArenaDir + "/AlQuds_Arena.fbx";
    private const string TexDir = ArenaDir + "/Textures";
    private const string MatDir = ArenaDir + "/Materials";
    private const string InstanceName = "Arena_AlQuds";

    // Material name in the FBX -> baked texture basename (null = flat colour material)
    private static readonly (string mat, string tex, Color tint, float smoothMax)[] Surfaces =
    {
        ("JerusalemStone", "JerusalemStone", Color.white, 0.45f),
        ("Flagstone",      "Flagstone",      Color.white, 0.35f),
        ("MinaretStone",   "MinaretStone",   Color.white, 0.45f),
        ("Backdrop",       "Backdrop",       new Color(0.95f, 0.92f, 0.88f), 0.4f),
        ("GildedDome",     null,             new Color(1.0f, 0.72f, 0.28f), 0.8f),
    };

    [MenuItem("Crucible/Arena/Place Al-Quds arena in the open scene")]
    public static void PlaceArena()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaFbx);
        if (prefab == null) { Debug.LogError($"Crucible: {ArenaFbx} not found. Run ArtSource/Blender/build_arena_alquds.py first."); return; }

        ModelImporter imp = AssetImporter.GetAtPath(ArenaFbx) as ModelImporter;
        if (imp != null && (imp.animationType != ModelImporterAnimationType.None || imp.materialImportMode != ModelImporterMaterialImportMode.None))
        {
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.generateSecondaryUV = false;
            imp.isReadable = false;
            imp.SaveAndReimport();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaFbx);
        }

        Directory.CreateDirectory(MatDir);
        var mats = new System.Collections.Generic.Dictionary<string, Material>();
        foreach (var s in Surfaces) mats[s.mat] = BuildMaterial(s.mat, s.tex, s.tint, s.smoothMax);

        GameObject old = GameObject.Find(InstanceName);
        if (old != null) Object.DestroyImmediate(old);
        GameObject arena = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        arena.name = InstanceName;
        arena.transform.position = Vector3.zero;
        arena.transform.rotation = Quaternion.identity;
        arena.transform.localScale = Vector3.one;
        GameObjectUtility.SetStaticEditorFlags(arena, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic);

        foreach (MeshRenderer r in arena.GetComponentsInChildren<MeshRenderer>())
        {
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            // Pick the material by the object name (one object per material in the FBX).
            string key = r.gameObject.name;
            Material m = null;
            if (key.StartsWith("Floor")) m = mats["Flagstone"];
            else if (key.StartsWith("Walls")) m = mats["JerusalemStone"];
            else if (key.StartsWith("Dome")) m = mats["GildedDome"];
            else if (key.StartsWith("Minaret")) m = mats["MinaretStone"];
            else if (key.StartsWith("Backdrop")) m = mats["Backdrop"];
            if (m != null)
            {
                Material[] arr = r.sharedMaterials;
                for (int i = 0; i < arr.Length; i++) arr[i] = m;
                r.sharedMaterials = arr;
            }
            // The floor needs a collider for anything physics-based; walls block nothing by design (code keeps fighters in).
            if (key.StartsWith("Floor") && r.GetComponent<Collider>() == null) r.gameObject.AddComponent<MeshCollider>();
        }

        // The flat test ground is replaced by the arena floor.
        GameObject ground = GameObject.Find("Ground");
        if (ground != null && ground.transform.parent != arena.transform)
        {
            ground.SetActive(false);
            Debug.Log("Crucible: old 'Ground' disabled (the arena floor replaces it).");
        }

        // Sun from behind the far wall (-Z), low, travelling toward +Z where the camera usually sits,
        // slightly from the side so the arcade and towers get shape.
        Light sun = GameObject.Find("Sun")?.GetComponent<Light>();
        if (sun != null)
        {
            // Noon: 66 degrees above the horizon, a little from the back-left so walls still get shape.
            Vector3 dir = new Vector3(0.16f, -0.914f, 0.37f).normalized;
            sun.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
        Light fill = GameObject.Find("Fill Light")?.GetComponent<Light>();
        if (fill != null)
        {
            fill.transform.rotation = Quaternion.LookRotation(new Vector3(-0.5f, -0.7f, -0.6f).normalized, Vector3.up);
        }

#if URP_PRESENT
        // Placed while URP is active: the materials above were built for HDRP, convert them now.
        if (CrucibleUrpLook.IsActive) CrucibleUrpLook.ConvertMaterialsToUrp();
#endif

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("Crucible: Al-Quds arena placed. Save the scene (Ctrl+S).");
    }

    private static Material BuildMaterial(string name, string texBase, Color tint, float smoothMax)
    {
        string path = $"{MatDir}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("HDRP/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find("HDRP/Lit");
        mat.SetColor("_BaseColor", tint);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.2f);

        if (texBase != null)
        {
            Texture2D baseMap = LoadTex($"{TexDir}/{texBase}_BaseColor.png", TextureImporterType.Default, true);
            Texture2D normal = LoadTex($"{TexDir}/{texBase}_Normal.png", TextureImporterType.NormalMap, false);
            Texture2D mask = LoadTex($"{TexDir}/{texBase}_Mask.png", TextureImporterType.Default, false);
            if (baseMap != null) mat.SetTexture("_BaseColorMap", baseMap);
            if (normal != null) { mat.SetTexture("_NormalMap", normal); mat.SetFloat("_NormalScale", 1f); }
            if (mask != null)
            {
                mat.SetTexture("_MaskMap", mask);
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_SmoothnessRemapMin", 0.05f);
                mat.SetFloat("_SmoothnessRemapMax", smoothMax);
            }
            // UVs were laid out in world units / tile in Blender, so no extra tiling here.
            mat.SetTextureScale("_BaseColorMap", Vector2.one);
        }
        else
        {
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", smoothMax);
        }
        HDMaterial.ValidateMaterial(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Texture2D LoadTex(string path, TextureImporterType type, bool srgb)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"Crucible: texture missing {path}"); return null; }
        bool dirty = false;
        if (ti.textureType != type) { ti.textureType = type; dirty = true; }
        if (ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; dirty = true; }
        if (ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; dirty = true; }
        if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (dirty) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
#endif
