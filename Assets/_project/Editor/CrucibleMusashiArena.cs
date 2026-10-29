// Crucible > Story > Musashi Chapter One > Build the arena scene
//
// Builds Assets/_project/Scenes/Story/Musashi_Chapter01_Arena.unity from scratch: the Tripo village
// terrain, the circular dojo, the two-story house and the pine (all four prepped by
// ArtSource/Blender/prep_env_lod.py into Environment/Musashi_Ch01/), their URP/Lit materials, the
// late-afternoon light, the cloudy sky, the grade, a forested hill ring that closes the horizon,
// and the two fight spaces (the dojo courtyard and the village plaza, each an arena root with its
// own ArenaBounds, spawn marks and fight-camera home). Every run regenerates the objects this file
// owns (OwnedRoots) and leaves everything else in the scene alone - the game runtime and fighters
// that 'Add the game' moved in from testarena survive a rebuild. THIS FILE is the layout: move a
// house or a tree in the tables below and run the menu again, never by hand in the scene.
//
// World frame: the dojo courtyard's centre is the origin and its (levelled) floor is y = 0, the
// same convention as the other arenas, so the fight marks and the camera framing carry over. The
// village's rear row of houses is +Z, the terrain end the dojo stands on is -X, and the fight
// camera looks down +Z with the rear buildings behind the fighters - the reference composition.
//
// 'Add the game' opens testarena additively and MOVES the runtime out of it in memory, then closes
// it WITHOUT saving, so testarena on disk is never written.
#if URP_PRESENT
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CrucibleMusashiArena
{
    const string Root = "Assets/_project/Environment/Musashi_Ch01";
    const string ScenePath = "Assets/_project/Scenes/Story/Musashi_Chapter01_Arena.unity";
    const string ProfilePath = Root + "/Musashi_Ch01_Look.asset";
    const string SkyPath = Root + "/Musashi_Ch01_Sky.mat";

    // The prepped terrain is long along its own Z with the rear row on -X; +90 puts the rear row
    // on +Z and the dojo's end of the village on -X. The dojo's closed halls face -X/-Y in its own
    // frame; 180 puts them at the back and on the left, so its open, low-railed side (and the
    // entrance steps) face the fight camera and the village square.
    const float TerrainYaw = 90f, DojoYaw = 180f;

    // Sun: 30 deg up, travelling toward +X/+Z (it sits behind the fight camera's left shoulder).
    const float SunPitch = 30f, SunYaw = 40f;
    static readonly Color Haze = new Color(0.64f, 0.67f, 0.70f);   // fog and the sky below the horizon

    // ---- the two-story houses: (x, z, yaw, scale, colour variant 0/1/2) ----
    // Placed on footprints that cover no existing village building (measured against the
    // terrain's height field: nothing over 4 units above the local ground inside the footprint).
    // The house's long balcony facade is its local +X, so yaw picks which way it faces.
    static readonly (float x, float z, float yaw, float scale, int variant)[] Houses =
    {
        (-49.0f,  18.0f,  90f, 1.00f, 1),   // left end, behind the dojo's shoulder
        ( 54.0f,  14.0f, 180f, 0.85f, 0),   // rear, facing the square
        ( 84.5f,  18.0f,  90f, 1.05f, 2),   // rear right
        (108.0f,  -3.0f,  90f, 0.95f, 1),   // far end of the village
        ( 38.0f, -47.0f, 270f, 0.95f, 2),   // front row, facing in
    };

    // ---- the pines: trunk positions; scale and yaw come from a seeded random, so they are stable ----
    // Rocky edges, the rear perimeter, between the rear houses and behind the dojo; the square and
    // the dojo's entrance side are kept clear. Trunks stand on bare ground (no rock within 1.6).
    static readonly (float x, float z, float scale)[] Pines =
    {
        (-21.0f,  25.0f, 0.70f), (  3.0f,  25.5f, 0.62f), ( 13.5f,  24.0f, 0.66f), ( 24.0f,  23.5f, 0.72f),   // behind the dojo
        (  4.5f,  35.5f, 0f), ( 10.5f,  44.5f, 0f), ( 25.5f,  35.5f, 0f), ( 24.0f,  46.0f, 0f), (-25.5f, 46.0f, 0f), (46.5f, 44.5f, 0f),   // rear row
        (-54.0f,  38.5f, 0f), (-45.0f,  46.0f, 0f), (105.0f,  46.0f, 0f), (115.5f, 37.0f, 0f), (117.0f, 20.5f, 0f),   // rear corners
        (-55.5f,  -6.5f, 0f), (-55.5f, -20.0f, 0f), (-57.0f, -30.5f, 0f), (-55.5f, -45.5f, 0f), (-45.0f, -54.5f, 0f),   // left rocky edge
        (-25.5f, -53.0f, 0f), (  4.5f, -53.0f, 0f), ( 21.0f, -53.0f, 0f), ( 75.0f, -53.0f, 0f), ( 94.5f, -53.0f, 0f), (106.5f, -53.0f, 0f),   // front edge
    };

    // ---- the two fight spaces: root name, select-screen name, blurb, centre, floor y, radius ----
    // Dojo: the courtyard floor is flat to +-0.15 out to radius 10 and the first prop (a lantern
    // stand) is at 10.4. Plaza: the clear stretch where the village paths cross, on the terrain's
    // own ground (0.4 +- 0.7 over the disc; nothing taller than 0.8 above the local ground). It was
    // levelled once with prep_env_lod.py's "plateau" and that stretched a bank at the rim into a
    // long smear, so it is left as authored - the fighters ground by raycast anyway.
    static readonly (string root, string title, string blurb, Vector3 centre, float radius)[] FightSpaces =
    {
        ("Musashi_Dojo",  "Dojo Courtyard", "Packed earth inside the dojo's rail.",       new Vector3( 0f, 0.00f,  0f), 9.5f),
        ("Musashi_Plaza", "Village Plaza",  "Where the paths cross. The boss waits here.", new Vector3(57f, 0.40f, -5f), 10f),
    };
    static readonly string[] OwnedRoots = { "Environment", "Lighting", "Cameras", "Musashi_Dojo", "Musashi_Plaza" };

    // ---- the forest ring: a hill rising outward from just under the terrain's rim ----
    // The terrain is a diorama slab (x -62..123, z -59..52 in world) with nothing past its edge.
    // The hill starts 2 inside that rectangle, below the rim, and climbs HillRise over HillDepth;
    // rows of pines stand on it, each row higher than the last, so from the fight spaces the
    // horizon is forest on a slope instead of sky under the slab.
    const float SlabMinX = -60f, SlabMaxX = 121f, SlabMinZ = -57f, SlabMaxZ = 50f;
    const float HillDepth = 80f, HillRise = 30f;
    static readonly float[] RingRows = { 5f, 14f, 23f, 33f, 44f, 56f };
    const float RingSpacing = 10f;

    // Ground the dojo stands on: prep_env_lod.py flattened the terrain under its footprint and put
    // that flat 0.35 below the courtyard floor, so the dojo's own base sinks into the earth.

    [MenuItem("Crucible/Story/Musashi Chapter One/Build the arena scene")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("Musashi Ch01: stop play mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        ConfigureImporters();
        var mats = BuildMaterials();

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        var scene = File.Exists(ScenePath)
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (var go in scene.GetRootGameObjects())
            if (OwnedRoots.Contains(go.name)) Object.DestroyImmediate(go);

        var env = new GameObject("Environment").transform;
        var terrain = Place("Ch01_Terrain", env, "Terrain", Vector3.zero, TerrainYaw, 1f, mats["Terrain"]);
        var dojo = Place("Ch01_Dojo", env, "Dojo", Vector3.zero, DojoYaw, 1f, mats["Dojo"]);
        AddMeshCollider(terrain); AddMeshCollider(dojo);
        Physics.SyncTransforms();

        // Exact ground heights come from the full-detail terrain, not its coarse collider.
        var probe = terrain.Find("Ch01_Terrain_LOD0").gameObject.AddComponent<MeshCollider>();
        Physics.SyncTransforms();

        var large = new GameObject("LargeBuildings").transform; large.SetParent(env, false);
        for (int i = 0; i < Houses.Length; i++)
        {
            var h = Houses[i];
            float y = GroundUnder(probe, h.x, h.z, 7f * h.scale, 9f * h.scale, h.yaw, lowest: true) - 0.4f;   // sills sit in the earth, never hang over it
            Place("Ch01_House", large, $"House_{i + 1:00}", new Vector3(h.x, y, h.z), h.yaw, h.scale, mats[$"House{h.variant}"]);
        }

        var trees = new GameObject("Trees").transform; trees.SetParent(env, false);
        var rng = new System.Random(1596);   // the year of the first duel
        for (int i = 0; i < Pines.Length; i++)
        {
            var p = Pines[i];
            float scale = p.scale > 0f ? p.scale : 0.78f + (float)rng.NextDouble() * 0.42f;
            float yaw = (float)rng.NextDouble() * 360f;
            float y = GroundUnder(probe, p.x, p.z, 1.2f, 1.2f, 0f, lowest: true) - 0.3f;
            Place("Ch01_Pine", trees, $"Pine_{i + 1:00}", new Vector3(p.x, y, p.z), yaw, scale, mats["Pine"]);
        }
        BuildForestRing(env, probe, mats);
        Object.DestroyImmediate(probe);

        BuildLighting(mats);
        BuildCameras();
        BuildFightSpaces(env);
        WireGame(scene);

        foreach (var r in env.GetComponentsInChildren<Transform>(true))
            // No static batching: it would copy every LOD of every high-poly house into one buffer.
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"Musashi Ch01: built {ScenePath} - {Houses.Length} houses, {Pines.Length} pines. " +
                  "Run 'Bake the sky lighting' once for the ambient and reflections.");
    }

    /// <summary>
    /// Environment-only lighting bake: no lightmaps, just the sky's ambient probe and reflection
    /// cubemap, which URP otherwise leaves black for a scene that has never been baked.
    /// </summary>
    [MenuItem("Crucible/Story/Musashi Chapter One/Bake the sky lighting")]
    public static void BakeSky()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath) { Debug.LogError($"Musashi Ch01: open {ScenePath} first."); return; }
        string path = Root + "/Musashi_Ch01_Lighting.lighting";
        var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
        if (settings == null) { settings = new LightingSettings { name = "Musashi_Ch01_Lighting" }; AssetDatabase.CreateAsset(settings, path); }
        settings.bakedGI = false;      // no lightmaps: the Tripo meshes have no lightmap UVs
        settings.realtimeGI = false;
        EditorUtility.SetDirty(settings);
        Lightmapping.lightingSettings = settings;
        Lightmapping.bakeCompleted -= SaveAfterBake;
        Lightmapping.bakeCompleted += SaveAfterBake;
        Lightmapping.BakeAsync();
    }

    static void SaveAfterBake()
    {
        Lightmapping.bakeCompleted -= SaveAfterBake;
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("Musashi Ch01: sky lighting baked and the scene saved.");
    }

    // ---------------------------------------------------------------- importers and materials

    static void ConfigureImporters()
    {
        foreach (var n in new[] { "Terrain", "Dojo", "House", "Pine" })
        {
            var mi = AssetImporter.GetAtPath(Fbx(n)) as ModelImporter;
            if (mi == null) { Debug.LogError($"Musashi Ch01: missing {Fbx(n)} - run prep_env_lod.py first"); continue; }
            bool readable = n == "Terrain" || n == "Dojo";   // ground probing and MeshColliders
            bool dirty = mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.isReadable != readable ||
                         mi.importCameras || mi.importLights || mi.animationType != ModelImporterAnimationType.None;
            if (!dirty) continue;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.isReadable = readable;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.SaveAndReimport();
        }
    }

    static string Fbx(string n) => $"{Root}/Ch01_{n}/Ch01_{n}.fbx";
    static string Fbm(string n) => $"{Root}/Ch01_{n}/Ch01_{n}.fbm";

    static Dictionary<string, Material> BuildMaterials()
    {
        // Tints pull the four Tripo models onto one palette: dark weathered timber, charcoal tile,
        // earthy ground, deep green pine. They multiply the baked colour, so the detail stays.
        var m = new Dictionary<string, Material>
        {
            ["Terrain"] = Mat("Terrain", "", new Color(0.93f, 0.91f, 0.88f), 0.55f),   // Tripo left the stones glossy
            ["Hill"] = HillMat(),
            ["Dojo"] = Mat("Dojo", "", new Color(0.86f, 0.80f, 0.74f), 1.0f),
            ["House0"] = Mat("House", "", new Color(0.88f, 0.84f, 0.80f), 1.0f),
            ["House1"] = Mat("House", "_b", Color.white, 1.0f),
            ["House2"] = Mat("House", "_c", new Color(0.94f, 0.92f, 0.90f), 1.0f),
            ["Pine"] = Mat("Pine", "", new Color(0.80f, 0.90f, 0.78f), 1.0f),
        };
        AssetDatabase.SaveAssets();
        return m;
    }

    static Material Mat(string n, string variant, Color tint, float smoothScale)
    {
        string path = $"{Root}/Ch01_{n}/Ch01_{n}{variant}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        string lname = $"ch01_{n.ToLower()}";
        // 4K only where the camera gets close: the terrain and dojo colour and normal maps. The
        // roughness/metal masks are low-frequency, and the house and pine are mostly seen at LOD1-2.
        bool hero = n == "Terrain" || n == "Dojo";
        var baseMap = Tex($"{Fbm(n)}/{lname}_basecolor{variant}.jpg", srgb: true, normal: false, hero ? 4096 : 2048);
        var nrm = Tex($"{Fbm(n)}/{lname}_normal.png", srgb: false, normal: true, hero ? 4096 : 2048);
        var mask = Tex($"{Fbm(n)}/{lname}_mask.png", srgb: false, normal: false, 2048);
        mat.SetTexture("_BaseMap", baseMap);
        mat.SetColor("_BaseColor", tint);
        if (nrm) { mat.SetTexture("_BumpMap", nrm); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
        if (mask)
        {
            // R metallic, A smoothness (1 - Tripo roughness); _Smoothness scales the map's alpha.
            mat.SetTexture("_MetallicGlossMap", mask); mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", smoothScale); mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        mat.enableInstancing = true;   // repeated houses and pines; the SRP Batcher takes precedence where it applies
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>The forest hill under the ring pines: a dark moss floor, no texture - it is mostly
    /// under canopy and in the fog. Plain URP/Lit with no smoothness.</summary>
    static Material HillMat()
    {
        string path = $"{Root}/Musashi_Ch01_Hill.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_BaseColor", new Color(0.16f, 0.19f, 0.11f));
        mat.SetFloat("_Smoothness", 0.08f);
        mat.SetFloat("_Metallic", 0f);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D Tex(string path, bool srgb, bool normal, int maxSize)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"Musashi Ch01: missing {path}"); return null; }
        bool ch = false;
        if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ch = true; }
        if (!normal && ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; ch = true; }
        if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; ch = true; }
        if (ti.mipmapEnabled == false) { ti.mipmapEnabled = true; ch = true; }
        if (ti.anisoLevel < 4) { ti.anisoLevel = 4; ch = true; }   // the ground is seen at a grazing angle
        if (ch) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------------------------------------------------------- placement

    static Transform Place(string asset, Transform parent, string name, Vector3 pos, float yaw, float scale, Material mat)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx(asset.Replace("Ch01_", "")));
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.name = name;
        go.transform.SetLocalPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        go.transform.localScale = Vector3.one * scale;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = r.name.EndsWith("_LOD2") || r.name.EndsWith("_LOD3") ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }
        // A hero mesh with a _SHD proxy (terrain, dojo: ~800k triangles each) casts its shadows
        // through the proxy (~100k) instead, so the full mesh is drawn once, not once per cascade.
        // Measured in play from the fight view: 12.4 fps with both full meshes casting, 15.4 without.
        var shd = go.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name.EndsWith("_SHD"));
        if (shd != null)
        {
            shd.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                if (r != shd) r.shadowCastingMode = ShadowCastingMode.Off;
        }
        var lod = go.GetComponent<LODGroup>();
        if (lod != null)
        {
            // Screen-height thresholds: full detail up close, the 1/4 and 1/16 meshes as the
            // building or tree shrinks; culled only when it is a few pixels tall.
            var lods = lod.GetLODs();
            float[] cut = lods.Length >= 4 ? new[] { 0.35f, 0.12f, 0.03f, 0.004f } : new[] { 0.35f, 0.12f, 0.012f };
            for (int i = 0; i < lods.Length && i < cut.Length; i++) lods[i].screenRelativeTransitionHeight = cut[i];
            lod.SetLODs(lods);
            lod.fadeMode = LODFadeMode.None;
        }
        return go.transform;
    }

    /// <summary>The coarse _COL mesh becomes the MeshCollider; its renderer goes.</summary>
    static void AddMeshCollider(Transform t)
    {
        var col = t.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name.EndsWith("_COL"));
        if (col == null) return;
        var r = col.GetComponent<MeshRenderer>();
        if (r != null) Object.DestroyImmediate(r);
        col.gameObject.AddComponent<MeshCollider>().sharedMesh = col.sharedMesh;
    }

    /// <summary>Ground height under a footprint (raycast grid against the given collider).</summary>
    static float GroundUnder(Collider c, float x, float z, float hx, float hz, float yaw, bool lowest)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float best = lowest ? float.MaxValue : float.MinValue; int hits = 0;
        for (int i = -2; i <= 2; i++)
        for (int j = -2; j <= 2; j++)
        {
            Vector3 p = new Vector3(x, 0f, z) + rot * new Vector3(hx * i / 2f, 0f, hz * j / 2f);
            if (!c.Raycast(new Ray(p + Vector3.up * 200f, Vector3.down), out var hit, 400f)) continue;
            best = lowest ? Mathf.Min(best, hit.point.y) : Mathf.Max(best, hit.point.y); hits++;
        }
        if (hits == 0) { Debug.LogWarning($"Musashi Ch01: no ground under ({x}, {z})"); return 0f; }
        return best;
    }

    // ---------------------------------------------------------------- light, sky, grade

    static void BuildLighting(Dictionary<string, Material> mats)
    {
        var lighting = new GameObject("Lighting").transform;

        // Late afternoon: a low golden sun from the front-left of the fight view, over the dojo's
        // open, low-railed side - so the courtyard and the fighters' faces are lit, and the long
        // shadows fall away from the camera onto the halls behind them.
        var sunGo = new GameObject("Sun"); sunGo.transform.SetParent(lighting, false);
        sunGo.transform.rotation = Quaternion.Euler(SunPitch, SunYaw, 0f);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1.00f, 0.83f, 0.62f);
        sun.intensity = 2.4f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.shadowNormalBias = 0.6f;

        // Sky bounce from the opposite side, no shadows: keeps the shadowed faces readable.
        var fillGo = new GameObject("Sky Fill"); fillGo.transform.SetParent(lighting, false);
        fillGo.transform.rotation = Quaternion.Euler(45f, SunYaw + 180f, 0f);
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.62f, 0.72f, 0.92f);
        fill.intensity = 0.45f;
        fill.shadows = LightShadows.None;

        // Sky material, with its sun disc where the light actually is. Below the horizon it fades
        // to the same cool haze as the fog, so the village reads as a hilltop above a misty valley
        // rather than a slab floating in a brown void.
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null) { sky = new Material(Shader.Find("Crucible/CloudySky")); AssetDatabase.CreateAsset(sky, SkyPath); }
        sky.SetVector("_SunDir", -sunGo.transform.forward);
        sky.SetColor("_ZenithColor", new Color(0.38f, 0.50f, 0.67f));
        sky.SetColor("_HorizonColor", new Color(0.82f, 0.77f, 0.70f));
        sky.SetColor("_GroundColor", Haze);
        sky.SetColor("_SunColor", new Color(1.00f, 0.80f, 0.56f));
        sky.SetColor("_CloudLit", new Color(1.00f, 0.91f, 0.80f));
        sky.SetColor("_CloudShade", new Color(0.46f, 0.50f, 0.58f));
        sky.SetFloat("_CloudCoverage", 0.5f);
        EditorUtility.SetDirty(sky);

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;            // explicit, so it holds without a bake
        RenderSettings.ambientSkyColor = new Color(0.56f, 0.62f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.56f, 0.52f, 0.46f);
        RenderSettings.ambientGroundColor = new Color(0.26f, 0.23f, 0.19f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 0.6f;
        // Thin cool haze that only touches the far end of the village and the slab's edges.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Haze;
        RenderSettings.fogStartDistance = 90f;
        RenderSettings.fogEndDistance = 520f;

        // The dojo's two paper lantern stands on the courtyard rim (heads measured off the mesh at
        // y 4.0 and 4.4): a warm practical glow, no shadows, small range - accents, not a light rig.
        foreach (var (pos, name) in new[] { (new Vector3(-10.6f, 3.5f, -3.3f), "Lantern_West"), (new Vector3(8.7f, 3.8f, 9.8f), "Lantern_North") })
        {
            var l = new GameObject(name); l.transform.SetParent(lighting, false); l.transform.position = pos;
            var pl = l.AddComponent<Light>();
            pl.type = LightType.Point; pl.color = new Color(1f, 0.62f, 0.32f); pl.intensity = 4f; pl.range = 7f; pl.shadows = LightShadows.None;
        }

        // Grade: neutral-leaning ACES, a touch of warmth and contrast, very light bloom and vignette.
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
        Get<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
        var ca = Get<ColorAdjustments>(profile);
        ca.postExposure.Override(0.25f); ca.contrast.Override(10f); ca.saturation.Override(-8f); ca.colorFilter.Override(new Color(1f, 0.97f, 0.93f));
        var wb = Get<WhiteBalance>(profile); wb.temperature.Override(6f); wb.tint.Override(0f);
        var bloom = Get<Bloom>(profile); bloom.intensity.Override(0.12f); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.6f);
        var vig = Get<Vignette>(profile); vig.intensity.Override(0.16f); vig.smoothness.Override(0.5f);
        EditorUtility.SetDirty(profile);

        var volGo = new GameObject("Global Volume"); volGo.transform.SetParent(lighting, false);
        var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = profile;
    }

    static T Get<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (!p.TryGet(out T c)) { c = p.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, p); }
        c.active = true;
        return c;
    }

    // ---------------------------------------------------------------- cameras

    static void BuildCameras()
    {
        var cams = new GameObject("Cameras").transform;

        // Showcase: the reference diorama's own angle - high over the front edge, looking at the
        // square, with the dojo on the left and the rear houses along the top. A preview only
        // (disabled, untagged): the game's Main Camera is the one that renders in play.
        var show = new GameObject("Showcase Camera"); show.transform.SetParent(cams, false);
        show.transform.position = new Vector3(30f, 78f, -122f);
        show.transform.LookAt(new Vector3(30f, 0f, 4f));
        var c = show.AddComponent<Camera>();
        c.fieldOfView = 40f; c.nearClipPlane = 0.3f; c.farClipPlane = 1200f; c.enabled = false;
        var data = show.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
    }

    // ---------------------------------------------------------------- fight spaces

    /// <summary>
    /// One root per fight space, which ArenaDirectory switches like any arena: an ArenaBounds
    /// (circle) whose scenery is the whole Environment, the two spawn marks MATCH_START_GAP apart
    /// along X, and the fight camera's home - testarena's framing (0.93 right, 2.44 up, 6.74 back
    /// from the pair's midpoint, looking down +Z at the rear of the village), with a disabled
    /// preview Camera on it to look through in the editor.
    /// </summary>
    static void BuildFightSpaces(Transform env)
    {
        for (int k = 0; k < FightSpaces.Length; k++)
        {
            var fs = FightSpaces[k];
            var root = new GameObject(fs.root);
            root.transform.position = fs.centre;
            var p1 = new GameObject("Mark_P1").transform; p1.SetParent(root.transform, false); p1.localPosition = new Vector3(-3.25f, 0f, 0f);
            var p2 = new GameObject("Mark_P2").transform; p2.SetParent(root.transform, false); p2.localPosition = new Vector3(3.25f, 0f, 0f);
            var home = new GameObject("Fight Camera Home").transform; home.SetParent(root.transform, false);
            home.localPosition = new Vector3(0.93f, 2.44f, -6.74f);
            home.localRotation = Quaternion.Euler(-0.3f, 0f, 0f);
            var pc = home.gameObject.AddComponent<Camera>();
            pc.fieldOfView = 60f; pc.nearClipPlane = 0.3f; pc.farClipPlane = 1000f; pc.enabled = false;
            home.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

            var bounds = root.AddComponent(GameType("ArenaBounds"));
            var so = new SerializedObject(bounds);
            so.FindProperty("shape").enumValueIndex = 0;   // Circle
            so.FindProperty("centre").vector3Value = fs.centre;
            so.FindProperty("radius").floatValue = fs.radius;
            so.FindProperty("scenery").objectReferenceValue = env;
            so.FindProperty("markP1").objectReferenceValue = p1;
            so.FindProperty("markP2").objectReferenceValue = p2;
            so.FindProperty("cameraHome").objectReferenceValue = home;
            so.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(k == 0);   // ArenaDirectory switches them; the dojo is the default
        }
    }

    // ---------------------------------------------------------------- forest ring

    /// <summary>Nearest point on the inner rectangle and the outward direction from it.</summary>
    static Vector3 RingPoint(float t, out Vector3 outward)
    {
        // walk the rectangle's perimeter; t in [0, 1)
        float w = SlabMaxX - SlabMinX, h = SlabMaxZ - SlabMinZ, per = 2f * (w + h);
        float d = Mathf.Repeat(t, 1f) * per;
        if (d < w) { outward = Vector3.back; return new Vector3(SlabMinX + d, 0f, SlabMinZ); }
        d -= w;
        if (d < h) { outward = Vector3.right; return new Vector3(SlabMaxX, 0f, SlabMinZ + d); }
        d -= h;
        if (d < w) { outward = Vector3.forward; return new Vector3(SlabMaxX - d, 0f, SlabMaxZ); }
        d -= w;
        outward = Vector3.left; return new Vector3(SlabMinX, 0f, SlabMaxZ - d);
    }

    /// <summary>
    /// Height of the hill at a world point: below the terrain rim at the inner rectangle, rising
    /// with distance outside it (distance to the rectangle, so the corners round off), with a slow
    /// two-octave wobble so the skyline is not a ruled line.
    /// </summary>
    static float HillHeight(float x, float z, float rimY)
    {
        float dx = Mathf.Max(SlabMinX - x, 0f, x - SlabMaxX);
        float dz = Mathf.Max(SlabMinZ - z, 0f, z - SlabMaxZ);
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        float t = Mathf.Clamp01(d / HillDepth);
        float rise = HillRise * t * t * (3f - 2f * t);
        float wobble = (Mathf.PerlinNoise(x * 0.018f + 3.1f, z * 0.018f + 7.7f) - 0.5f) * 10f * t
                     + (Mathf.PerlinNoise(x * 0.06f, z * 0.06f) - 0.5f) * 2.5f;
        return rimY + rise + wobble;
    }

    static void BuildForestRing(Transform env, Collider terrainProbe, Dictionary<string, Material> mats)
    {
        var ring = new GameObject("ForestRing").transform; ring.SetParent(env, false);

        // The rim height: the hill's inner edge sits 1.5 under the median of the terrain's own edge
        // (measured -2.5 to -5 along the four sides), so it tucks under the slab.
        var edge = new List<float>();
        for (int i = 0; i < 120; i++)
        {
            Vector3 p = RingPoint(i / 120f, out _);
            if (terrainProbe.Raycast(new Ray(p + Vector3.up * 200f, Vector3.down), out var hit, 400f)) edge.Add(hit.point.y);
        }
        edge.Sort();
        float rimY = (edge.Count > 0 ? edge[edge.Count / 2] : -3.5f) - 1.5f;

        // Hill mesh: a grid over the whole ring's bounding box with the slab's interior cut out.
        const float step = 5f;
        float minX = SlabMinX - HillDepth - 10f, maxX = SlabMaxX + HillDepth + 10f;
        float minZ = SlabMinZ - HillDepth - 10f, maxZ = SlabMaxZ + HillDepth + 10f;
        int nx = Mathf.CeilToInt((maxX - minX) / step) + 1, nz = Mathf.CeilToInt((maxZ - minZ) / step) + 1;
        var verts = new Vector3[nx * nz];
        var uvs = new Vector2[nx * nz];
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
        {
            float x = minX + ix * step, z = minZ + iz * step;
            verts[iz * nx + ix] = new Vector3(x, HillHeight(x, z, rimY), z);
            uvs[iz * nx + ix] = new Vector2(x, z) / 12f;
        }
        var tris = new List<int>();
        for (int iz = 0; iz < nz - 1; iz++)
        for (int ix = 0; ix < nx - 1; ix++)
        {
            float x = minX + ix * step, z = minZ + iz * step;
            // drop cells wholly inside the slab (2 units in from the rectangle, under the terrain)
            if (x >= SlabMinX + 2f && x + step <= SlabMaxX - 2f && z >= SlabMinZ + 2f && z + step <= SlabMaxZ - 2f) continue;
            int a = iz * nx + ix, b = a + 1, c2 = a + nx, d = c2 + 1;
            tris.Add(a); tris.Add(c2); tris.Add(b);
            tris.Add(b); tris.Add(c2); tris.Add(d);
        }
        string meshPath = Root + "/Ch01_ForestHill.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) { mesh = new Mesh { name = "Ch01_ForestHill" }; AssetDatabase.CreateAsset(mesh, meshPath); }
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);

        var hill = new GameObject("ForestHill"); hill.transform.SetParent(ring, false);
        hill.AddComponent<MeshFilter>().sharedMesh = mesh;
        var hr = hill.AddComponent<MeshRenderer>(); hr.sharedMaterial = mats["Hill"];
        hr.shadowCastingMode = ShadowCastingMode.Off;
        var hc = hill.AddComponent<MeshCollider>(); hc.sharedMesh = mesh;   // so the camera's see-through also opens it
        Physics.SyncTransforms();

        // Rows of pines on the hill, staggered and jittered, a little bigger the further out they
        // stand. No shadows: they are scenery at the edge of the fog, and there are a few hundred.
        var rng = new System.Random(1612);   // Ganryujima
        int count = 0;
        var trees = new GameObject("RingPines").transform; trees.SetParent(ring, false);
        for (int row = 0; row < RingRows.Length; row++)
        {
            float d = RingRows[row];
            float w = SlabMaxX - SlabMinX + 2f * d, h = SlabMaxZ - SlabMinZ + 2f * d;
            int n = Mathf.Max(8, Mathf.RoundToInt(2f * (w + h) / RingSpacing));
            float phase = (float)rng.NextDouble();
            for (int i = 0; i < n; i++)
            {
                Vector3 p = RingPoint((i + phase + (row % 2) * 0.5f) / n, out Vector3 outward);
                p += outward * (d + ((float)rng.NextDouble() - 0.5f) * 4f);
                p += Vector3.Cross(Vector3.up, outward) * (((float)rng.NextDouble() - 0.5f) * RingSpacing * 0.6f);
                if (!hc.Raycast(new Ray(new Vector3(p.x, 300f, p.z), Vector3.down), out var hit, 600f)) continue;
                float scale = Mathf.Lerp(0.9f, 1.45f, row / (RingRows.Length - 1f)) * (0.85f + (float)rng.NextDouble() * 0.3f);
                var t = Place("Ch01_Pine", trees, $"RingPine_{count++:000}", new Vector3(p.x, hit.point.y - 0.5f, p.z),
                              (float)rng.NextDouble() * 360f, scale, mats["Pine"]);
                // Every ring pine stands 45+ units from both fight spaces, where the ~3k-triangle LOD3
                // reads the same as the rest; a LODGroup kept them on the 10k LOD2 out to ~500 units
                // (measured: 7.4 fps in a plaza match with the group, 11.4 without the ring at all).
                Object.DestroyImmediate(t.GetComponent<LODGroup>());
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.name.EndsWith("_LOD3")) { r.enabled = false; continue; }
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }
        Debug.Log($"Musashi Ch01: forest ring - hill rim at y {rimY:F2}, {count} pines in {RingRows.Length} rows");
    }

    // ---------------------------------------------------------------- the game

    /// <summary>
    /// When the scene holds the game runtime (moved in by 'Add the game'), point it at this
    /// scene's fight spaces: the ArenaDirectory lists the dojo and the plaza, the menu opens on
    /// One Player / Two Players, and the Main Camera starts on the dojo's fight view.
    /// </summary>
    static void WireGame(UnityEngine.SceneManagement.Scene scene)
    {
        var menuGo = scene.GetRootGameObjects().FirstOrDefault(g => g.GetComponent(GameType("GameMenu")) != null);
        if (menuGo == null) return;

        var dir = menuGo.GetComponent(GameType("ArenaDirectory")) ?? menuGo.AddComponent(GameType("ArenaDirectory"));
        var so = new SerializedObject(dir);
        var list = so.FindProperty("arenas");
        list.arraySize = FightSpaces.Length;
        for (int k = 0; k < FightSpaces.Length; k++)
        {
            var e = list.GetArrayElementAtIndex(k);
            e.FindPropertyRelative("displayName").stringValue = FightSpaces[k].title;
            e.FindPropertyRelative("rootName").stringValue = FightSpaces[k].root;
            e.FindPropertyRelative("blurb").stringValue = FightSpaces[k].blurb;
            e.FindPropertyRelative("shipped").boolValue = true;
        }
        so.FindProperty("testGroundName").stringValue = "";
        so.ApplyModifiedPropertiesWithoutUndo();

        var mso = new SerializedObject(menuGo.GetComponent(GameType("GameMenu")));
        mso.FindProperty("openOnBattleSetup").boolValue = true;
        // scene-wide marks (used before an arena is chosen, e.g. the select ring): the dojo's
        mso.FindProperty("p1Mark").vector3Value = FightSpaces[0].centre + new Vector3(-3.25f, 0f, 0f);
        mso.FindProperty("p2Mark").vector3Value = FightSpaces[0].centre + new Vector3(3.25f, 0f, 0f);
        mso.ApplyModifiedPropertiesWithoutUndo();

        var cam = scene.GetRootGameObjects().FirstOrDefault(g => g.GetComponent(GameType("FightCamera")) != null);
        if (cam != null)
            cam.transform.SetPositionAndRotation(FightSpaces[0].centre + new Vector3(0.93f, 2.44f, -6.74f), Quaternion.Euler(-0.3f, 0f, 0f));
    }

    /// <summary>A gameplay type by name: this editor assembly cannot reference Assembly-CSharp.</summary>
    static System.Type GameType(string name)
    {
        var t = System.Type.GetType(name + ", Assembly-CSharp");
        if (t == null) throw new System.Exception($"Musashi Ch01: gameplay type {name} not found");
        return t;
    }

    /// <summary>The fighters this test scene takes from testarena (root names there).</summary>
    static readonly string[] GameRoots = { "Main Camera", "GameMangers", "Player1", "Player2", "Player4" };

    [MenuItem("Crucible/Story/Musashi Chapter One/Add the game (Bjorn, Miyamoto, Salaadin)")]
    public static void AddGame()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("Musashi Ch01: stop play mode first."); return; }
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(g => g.GetComponent(GameType("GameMenu")) != null))
        {
            Debug.Log("Musashi Ch01: the game is already in this scene.");
            WireGame(scene); EditorSceneManager.SaveScene(scene); return;
        }

        var src = EditorSceneManager.OpenScene("Assets/_project/Scenes/testarena.unity", OpenSceneMode.Additive);
        try
        {
            var move = src.GetRootGameObjects().Where(g => GameRoots.Contains(g.name)).ToList();
            if (move.Count != GameRoots.Length) { Debug.LogError("Musashi Ch01: testarena is missing " + string.Join(", ", GameRoots.Except(move.Select(g => g.name)))); return; }

            // Every reference from what moves to what stays behind would be lost on save - list them.
            var moving = new HashSet<GameObject>(move.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject));
            foreach (var comp in move.SelectMany(g => g.GetComponentsInChildren<Component>(true)))
            {
                if (comp == null) continue;
                var it = new SerializedObject(comp).GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    var target = it.objectReferenceValue as GameObject ?? (it.objectReferenceValue as Component)?.gameObject;
                    if (target != null && target.scene == src && !moving.Contains(target))
                        Debug.LogWarning($"Musashi Ch01: {comp.gameObject.name}.{comp.GetType().Name}.{it.propertyPath} points at {target.name}, which stays in testarena");
                }
            }
            foreach (var g in move) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g, scene);
        }
        finally
        {
            EditorSceneManager.CloseScene(src, true);   // discards the in-memory change: testarena on disk is untouched
        }
        EditorSceneManager.SetActiveScene(scene);
        WireGame(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Musashi Ch01: the game runtime and Bjorn, Miyamoto and Salaadin are in the scene. Press Play.");
    }

    // ---------------------------------------------------------------- the shipped game

    const string GameScene = "Assets/_project/Scenes/testarena.unity";
    const string VillageRoot = "Musashi_Village";
    const string ReflectionPath = Root + "/Musashi_Ch01_SkyReflection.exr";

    /// <summary>
    /// Puts the two fight spaces into the REAL game scene as two more arenas, appended after the
    /// existing ones (so every arena index the online protocol sends keeps its meaning). The
    /// village (Environment + its lights and grade) becomes one shared root, Musashi_Village, which
    /// ArenaDirectory switches on for either of the two and which an ArenaAtmosphere uses to swap in
    /// the village's sky, ambient, fog and reflections while it is on. Re-runnable: it replaces what
    /// an earlier run put there. Run 'Build the arena scene' and 'Bake the sky lighting' first.
    /// </summary>
    [MenuItem("Crucible/Story/Musashi Chapter One/Add the two arenas to the game (testarena)")]
    public static void AddArenasToGame()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("Musashi Ch01: stop play mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // The sky's reflection cubemap travels with the arena, not with the story scene's bake.
        string baked = ScenePath.Replace(".unity", "") + "/ReflectionProbe-0.exr";
        if (!File.Exists(baked)) { Debug.LogError($"Musashi Ch01: {baked} missing - run 'Bake the sky lighting' first."); return; }
        AssetDatabase.DeleteAsset(ReflectionPath);
        AssetDatabase.CopyAsset(baked, ReflectionPath);
        var reflection = AssetDatabase.LoadAssetAtPath<Cubemap>(ReflectionPath);

        var game = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
        foreach (var go in game.GetRootGameObjects())
            if (go.name == VillageRoot || FightSpaces.Any(f => f.root == go.name)) Object.DestroyImmediate(go);

        var src = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var roots = src.GetRootGameObjects();
            GameObject Find(string n) => roots.FirstOrDefault(g => g.name == n);
            var env = Find("Environment"); var lighting = Find("Lighting");
            if (env == null || lighting == null) { Debug.LogError("Musashi Ch01: the story scene has no Environment/Lighting - build it first."); return; }

            var village = new GameObject(VillageRoot);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(village, src);
            env.transform.SetParent(village.transform, true);
            lighting.transform.SetParent(village.transform, true);

            var atm = village.AddComponent(GameType("ArenaAtmosphere"));
            var aso = new SerializedObject(atm);
            aso.FindProperty("skybox").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            aso.FindProperty("sun").objectReferenceValue = lighting.transform.Find("Sun").GetComponent<Light>();
            aso.FindProperty("reflection").objectReferenceValue = reflection;
            aso.FindProperty("fogColor").colorValue = Haze;
            aso.ApplyModifiedPropertiesWithoutUndo();

            // Moved, not copied: references between these objects (the bounds' scenery) stay live.
            var move = new List<GameObject> { village };
            foreach (var f in FightSpaces) { var g = Find(f.root); if (g != null) move.Add(g); }
            foreach (var g in move) { g.SetActive(false); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g, game); }
        }
        finally
        {
            EditorSceneManager.CloseScene(src, true);   // discards the in-memory change: the story scene on disk is untouched
        }

        // Append to the game's arena list (keeping every existing entry and its index).
        var menuGo = game.GetRootGameObjects().First(g => g.GetComponentInChildren(GameType("ArenaDirectory"), true) != null);
        var so = new SerializedObject(menuGo.GetComponentInChildren(GameType("ArenaDirectory"), true));
        var list = so.FindProperty("arenas");
        foreach (var f in FightSpaces)
        {
            int at = -1;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("rootName").stringValue == f.root) at = i;
            if (at < 0) { at = list.arraySize; list.arraySize++; }
            var e = list.GetArrayElementAtIndex(at);
            e.FindPropertyRelative("displayName").stringValue = f.title;
            e.FindPropertyRelative("rootName").stringValue = f.root;
            e.FindPropertyRelative("blurb").stringValue = f.blurb;
            e.FindPropertyRelative("shipped").boolValue = true;
            e.FindPropertyRelative("sharedRoot").stringValue = VillageRoot;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(game);
        EditorSceneManager.SaveScene(game);
        Debug.Log("Musashi Ch01: Dojo Courtyard and Village Plaza added to testarena's arenas.");
    }
}
#endif
