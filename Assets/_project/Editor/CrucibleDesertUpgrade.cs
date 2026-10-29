#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// The Desert Arena's world beyond its walls. The arena itself (the one Tripo mesh, its bounds,
/// marks, keep-outs and ring-out) is NOT touched; everything here is added around it:
///
/// - <c>DesertArena_Surroundings</c>, a root the ArenaDirectory switches on with the Desert Arena
///   (its <c>sharedRoot</c>): four landscape rings (sand apron, dunes, sandstone ridges, far
///   mountains), each sinking under the next so no seam or edge shows, three palms outside the
///   walls, scattered rocks and dry shrubs, and an <c>ArenaAtmosphere</c> with the late-afternoon
///   sun, sky, haze and grade.
/// - Two palms INSIDE the arena, under the arena root so WallCutout sees through them like a wall.
///   Their trunks carry colliders, and an ArenaBounds keep-out circle a little wider than each
///   trunk makes a fighter slide round it instead of stopping dead - so the AI cannot walk into
///   one and stick.
///
/// Every mesh and texture is generated here and written to <c>Environment/DesertSurroundings/</c>;
/// re-running the menu item rebuilds them all (same seeds, same result).
/// </summary>
public static class CrucibleDesertUpgrade
{
    const string Dir = "Assets/_project/Environment/DesertSurroundings";
    const string RootName = "DesertArena_Surroundings";
    const string ArenaRoot = "DesertArena";
    const string InsideName = "Palms_Inside";

    // Fighter-facing palms: (x, z, height, lean). Clear of the spawn line, the keep-outs and the
    // ring-out gap, 5+ units inside the outer bound so no pocket forms against the wall.
    static readonly Vector4[] InsidePalms = { new Vector4(18.2f, -4.9f, 8.6f, 0.10f), new Vector4(-14.0f, -8.9f, 7.6f, 0.16f) };
    const float TrunkRadius = 0.33f;
    const float PalmKeepOut = 0.55f;

    // Outside the walls, seen over them: (angle deg from +z toward +x, radius, height, lean).
    static readonly Vector4[] OutsidePalms = { new Vector4(38f, 31f, 10.5f, 0.12f), new Vector4(148f, 40f, 9.2f, 0.18f), new Vector4(252f, 37f, 11.2f, 0.08f) };

    static Collider arenaCollider;

    // ---------------------------------------------------------------- menu

    [MenuItem("Crucible/Arena/Desert/Build the surroundings")]
    public static void Build()
    {
        var arena = FindRoot(ArenaRoot);
        if (arena == null) { Debug.LogError("Desert upgrade: no DesertArena root in the open scene."); return; }
        // The ground is fitted to the arena by raycasts, and an inactive collider answers none:
        // switch the arena on for the build and leave it as it was found.
        bool wasActive = arena.activeSelf;
        arena.SetActive(true);
        Physics.SyncTransforms();
        try { BuildAround(arena); }
        finally { arena.SetActive(wasActive); }
    }

    static void BuildAround(GameObject arena)
    {
        arenaCollider = arena.GetComponent<MeshCollider>();
        Directory.CreateDirectory(Dir);

        Remove(false);

        // textures + materials
        var sandTex = SaveTex("Desert_Sand", 1024, 1024, SandAlbedo(1024, out var sandH), false);
        var sandNrm = SaveTex("Desert_Sand_N", 1024, 1024, NormalFromHeight(sandH, 1024, 1024, 2.5f), true);
        var rockTex = SaveTex("Desert_Sandstone", 1024, 1024, RockAlbedo(1024, out var rockH), false);
        var rockNrm = SaveTex("Desert_Sandstone_N", 1024, 1024, NormalFromHeight(rockH, 1024, 1024, 5f), true);
        var barkTex = SaveTex("Palm_Bark", 256, 512, BarkAlbedo(256, 512, out var barkH), false);
        var barkNrm = SaveTex("Palm_Bark_N", 256, 512, NormalFromHeight(barkH, 256, 512, 4f), true);
        var palTex = SaveTex("Palm_Palette", 64, 64, Palette(), false, clamp: true);

        var sand = Mat("Desert_Sand", sandTex, sandNrm, 0.08f, 0.6f, specular: false);
        var rock = Mat("Desert_Sandstone", rockTex, rockNrm, 0.12f, 1.0f, specular: false);
        var bark = Mat("Palm_Bark", barkTex, barkNrm, 0.1f, 1.0f, specular: false);
        var leaf = Mat("Palm_Leaf", palTex, null, 0.18f, 1f, specular: true);

        // root
        var root = new GameObject(RootName);
        var landscape = Child(root, "Landscape");
        foreach (var layer in Layers())
        {
            var mesh = SaveMesh(BuildLayer(layer), "Land_" + layer.name);
            var go = Child(landscape, layer.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { sand, rock };
            mr.shadowCastingMode = ShadowCastingMode.Off;   // all of it lies beyond the 40-unit shadow distance
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // palms
        Mesh trunkCol = SaveMesh(TrunkCollider(), "Palm_TrunkCollider");
        var outside = Child(root, "Palms_Outside");
        for (int i = 0; i < OutsidePalms.Length; i++)
        {
            var p = OutsidePalms[i];
            float a = p.x * Mathf.Deg2Rad;
            float x = Mathf.Sin(a) * p.y, z = Mathf.Cos(a) * p.y;
            var m = SaveMesh(Palm(100 + i, p.z, p.w), "Palm_Out" + i);
            PalmObject(outside, "Palm_" + i, m, bark, leaf, new Vector3(x, HeightAt(x, z) - 0.35f, z), 40f + i * 97f);
        }
        var inside = new GameObject(InsideName);
        for (int i = 0; i < InsidePalms.Length; i++)
        {
            var p = InsidePalms[i];
            var m = SaveMesh(Palm(200 + i, p.z, p.w), "Palm_In" + i);
            float y = FloorY(p.x, p.y) - 0.15f;
            var go = PalmObject(inside, "Palm_" + i, m, bark, leaf, new Vector3(p.x, y, p.y), 15f + i * 140f);
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = trunkCol;
            col.convex = true;
        }
        inside.transform.SetParent(arena.transform, true);

        // rocks and shrubs
        var rockMeshes = new Mesh[4];
        for (int i = 0; i < rockMeshes.Length; i++) rockMeshes[i] = SaveMesh(Rock(300 + i), "Rock_" + i);
        var shrubMeshes = new Mesh[3];
        for (int i = 0; i < shrubMeshes.Length; i++) shrubMeshes[i] = SaveMesh(Shrub(400 + i), "Shrub_" + i);
        Scatter(Child(root, "Rocks"), "Rocks_Combined", rockMeshes, rock, 46, 30f, 150f, 0.35f, 3.2f, 11, true);
        Scatter(Child(root, "Shrubs"), "Shrubs_Combined", shrubMeshes, leaf, 70, 27f, 190f, 0.6f, 1.5f, 12, false);

        // lighting, sky, grade
        BuildAtmosphere(root);

        // gameplay wiring: keep-outs for the inside palms, the closed ring-out, and the shared root
        SetPalmKeepOuts(arena, true);
        SealRingOut(arena);
        SetSharedRoot(ArenaRoot, RootName);

        root.SetActive(false);   // ArenaDirectory switches it on with the Desert Arena, like the Musashi village
        EditorSceneManager.MarkSceneDirty(arena.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Desert upgrade: surroundings built (save the scene to keep them).");
    }

    [MenuItem("Crucible/Arena/Desert/Remove the surroundings")]
    public static void RemoveMenu() => Remove(true);

    static void Remove(bool log)
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == RootName) Object.DestroyImmediate(go);
        var arena = FindRoot(ArenaRoot);
        if (arena != null)
        {
            var old = arena.transform.Find(InsideName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            SetPalmKeepOuts(arena, false);
            EditorSceneManager.MarkSceneDirty(arena.scene);
        }
        if (log) { SetSharedRoot(ArenaRoot, ""); Debug.Log("Desert upgrade: surroundings removed."); }
    }

    // ---------------------------------------------------------------- scene wiring

    /// <summary>A root object of the open scene by name, switched on or not.</summary>
    internal static GameObject FindRoot(string name)
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }

    internal static System.Type GameType(string name)
    {
        var t = System.Type.GetType(name + ", Assembly-CSharp");
        if (t == null) throw new Exception($"Desert upgrade: gameplay type {name} not found");
        return t;
    }

    static void SetPalmKeepOuts(GameObject arena, bool add)
    {
        var bounds = arena.GetComponent(GameType("ArenaBounds"));
        var so = new SerializedObject(bounds);
        var list = so.FindProperty("keepOut");
        var keep = new List<Vector3>();
        for (int i = 0; i < list.arraySize; i++)
        {
            Vector3 v = list.GetArrayElementAtIndex(i).vector3Value;
            bool isPalm = false;
            foreach (var p in InsidePalms) if (Mathf.Abs(v.x - p.x) < 0.01f && Mathf.Abs(v.z - p.y) < 0.01f) isPalm = true;
            if (!isPalm) keep.Add(v);
        }
        if (add) foreach (var p in InsidePalms) keep.Add(new Vector3(p.x, PalmKeepOut, p.y));
        list.arraySize = keep.Count;
        for (int i = 0; i < keep.Count; i++) list.GetArrayElementAtIndex(i).vector3Value = keep[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // The opening between the wall end and the statue, which used to be a ring-out (2026-10-27).
    static readonly Vector4 RingOutGap = new Vector4(-17.0f, 9.4f, -12.1f, 11.8f);
    const string SealName = "RingOutSeal";

    /// <summary>
    /// Nobody falls out of this arena (the user's call, 2026-10-29): the ring-out rule is cleared AND
    /// the gap gets an invisible wall, because past it the floor ends inside the outer bound and a
    /// fighter clamped there would simply drop. A box collider, so Fighter.BlockObstacles stops walks,
    /// dashes and knockback at it while WallCutout (MeshColliders only) never opens a hole for it.
    /// </summary>
    [MenuItem("Crucible/Arena/Desert/Seal the ring-out")]
    public static void SealRingOutMenu()
    {
        var arena = FindRoot(ArenaRoot);
        if (arena == null) { Debug.LogError("Desert upgrade: no DesertArena root in the open scene."); return; }
        SealRingOut(arena);
        EditorSceneManager.MarkSceneDirty(arena.scene);
    }

    static void SealRingOut(GameObject arena)
    {
        var so = new SerializedObject(arena.GetComponent(GameType("ArenaBounds")));
        so.FindProperty("ringOutGaps").arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();

        var old = arena.transform.Find(SealName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Vector3 a = new Vector3(RingOutGap.x, 0f, RingOutGap.y), b = new Vector3(RingOutGap.z, 0f, RingOutGap.w);
        Vector3 along = b - a;
        // A little past both ends so it overlaps the wall end and the statue: no slit to squeeze through.
        var seal = new GameObject(SealName);
        seal.transform.position = (a + b) * 0.5f + Vector3.up * 2.5f;
        seal.transform.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, along.normalized));
        seal.AddComponent<BoxCollider>().size = new Vector3(along.magnitude + 2f, 6f, 0.6f);
        seal.transform.SetParent(arena.transform, true);
    }

    /// <summary>Sets the ArenaDirectory entry for <paramref name="arenaRoot"/> to switch <paramref name="shared"/> on with it.</summary>
    internal static void SetSharedRoot(string arenaRoot, string shared)
    {
        var dirType = GameType("ArenaDirectory");
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var d = go.GetComponentInChildren(dirType, true);
            if (d == null) continue;
            var so = new SerializedObject(d);
            var list = so.FindProperty("arenas");
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("rootName").stringValue == arenaRoot)
                    e.FindPropertyRelative("sharedRoot").stringValue = shared;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return;
        }
        Debug.LogWarning("Desert upgrade: no ArenaDirectory found - the surroundings will not switch on with the arena.");
    }

    static void BuildAtmosphere(GameObject root)
    {
        var lighting = Child(root, "Lighting");

        var sunGo = Child(lighting, "Sun");
        sunGo.transform.rotation = Quaternion.Euler(24f, 38f, 0f);   // low in the south-west: long shadows, fighters lit from the camera side
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1.00f, 0.87f, 0.71f);
        sun.intensity = 1.95f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.82f;

        var fillGo = Child(lighting, "Sky Fill");
        fillGo.transform.rotation = Quaternion.Euler(42f, 220f, 0f);
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.64f, 0.74f, 0.93f);
        fill.intensity = 0.38f;
        fill.shadows = LightShadows.None;

        // sky
        string skyPath = Dir + "/Desert_Sky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
        sky.SetFloat("_SunDisk", 2f);
        sky.SetFloat("_SunSize", 0.04f);
        sky.SetFloat("_SunSizeConvergence", 5f);
        sky.SetFloat("_AtmosphereThickness", 1.15f);
        sky.SetColor("_SkyTint", new Color(0.56f, 0.62f, 0.70f));
        sky.SetColor("_GroundColor", new Color(0.62f, 0.54f, 0.44f));
        sky.SetFloat("_Exposure", 1.15f);
        EditorUtility.SetDirty(sky);

        // grade: the scene's Crucible look, a touch cooler and cleaner (the atmosphere switches the
        // scene's own volumes off while the desert is up)
        var look = Child(root, "Desert Look");
        var vol = look.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10;
        vol.sharedProfile = DesertGrade(CloneProfile("Assets/_project/Settings/CrucibleLookURP.asset", Dir + "/Desert_Look.asset"));

        var atm = root.AddComponent(GameType("ArenaAtmosphere"));
        var so = new SerializedObject(atm);
        so.FindProperty("skybox").objectReferenceValue = sky;
        so.FindProperty("sun").objectReferenceValue = sun;
        so.FindProperty("ambientMode").intValue = (int)AmbientMode.Trilight;
        so.FindProperty("ambientSky").colorValue = new Color(0.56f, 0.62f, 0.72f);
        so.FindProperty("ambientEquator").colorValue = new Color(0.62f, 0.56f, 0.47f);
        so.FindProperty("ambientGround").colorValue = new Color(0.38f, 0.32f, 0.25f);
        so.FindProperty("reflectionIntensity").floatValue = 0.7f;
        so.FindProperty("fog").boolValue = true;
        so.FindProperty("fogMode").intValue = (int)FogMode.Linear;
        so.FindProperty("fogColor").colorValue = new Color(0.79f, 0.76f, 0.69f);
        so.FindProperty("fogStart").floatValue = 80f;
        so.FindProperty("fogEnd").floatValue = 900f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>A copy of a volume profile, components and all, written to <paramref name="dstPath"/>.</summary>
    internal static VolumeProfile CloneProfile(string srcPath, string dstPath)
    {
        var src = AssetDatabase.LoadAssetAtPath<VolumeProfile>(srcPath);
        AssetDatabase.DeleteAsset(dstPath);
        var dst = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(dst, dstPath);
        if (src != null)
            foreach (var c in src.components)
            {
                var copy = Object.Instantiate(c);
                copy.name = c.name;
                copy.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(copy, dst);
                dst.components.Add(copy);
            }
        EditorUtility.SetDirty(dst);
        return dst;
    }

    static VolumeProfile DesertGrade(VolumeProfile dst)
    {
        foreach (var c in dst.components)
        {
            if (c is UnityEngine.Rendering.Universal.ColorAdjustments ca)
            {
                ca.colorFilter.Override(new Color(1.00f, 0.99f, 0.97f));
                ca.postExposure.Override(0.05f);
                ca.saturation.Override(-4f);
                ca.contrast.Override(6f);
            }
            if (c is UnityEngine.Rendering.Universal.Bloom b) b.intensity.Override(0.1f);
            if (c is UnityEngine.Rendering.Universal.FilmGrain g) g.intensity.Override(0.14f);
            if (c is UnityEngine.Rendering.Universal.LiftGammaGain lgg) lgg.gain.Override(new Vector4(1.01f, 1.00f, 0.98f, 0.0f));
        }
        EditorUtility.SetDirty(dst);
        return dst;
    }

    // ---------------------------------------------------------------- helpers

    static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    static GameObject PalmObject(GameObject parent, string name, Mesh mesh, Material bark, Material leaf, Vector3 pos, float yaw)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { bark, leaf };
        mr.shadowCastingMode = ShadowCastingMode.On;
        return go;
    }

    /// <summary>The arena floor's height under (x, z), from the arena's own collider.</summary>
    static float FloorY(float x, float z)
    {
        if (arenaCollider != null && arenaCollider.Raycast(new Ray(new Vector3(x, 60f, z), Vector3.down), out var hit, 120f)) return hit.point.y;
        return 0f;
    }

    static bool OverArena(float x, float z, float pad)
    {
        if (arenaCollider == null) return false;
        for (int i = 0; i < 9; i++)
        {
            float ox = i == 0 ? 0 : Mathf.Cos(i * Mathf.PI / 4f) * pad, oz = i == 0 ? 0 : Mathf.Sin(i * Mathf.PI / 4f) * pad;
            if (arenaCollider.Raycast(new Ray(new Vector3(x + ox, 60f, z + oz), Vector3.down), out _, 120f)) return true;
        }
        return false;
    }

    /// <summary>
    /// Scatters copies of <paramref name="meshes"/> round the arena and merges them into ONE mesh,
    /// so a hundred props cost one draw call instead of a hundred.
    /// </summary>
    static void Scatter(GameObject parent, string meshName, Mesh[] meshes, Material mat, int count, float r0, float r1, float s0, float s1, int seed, bool rocks)
    {
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        var parts = new List<CombineInstance>();
        int tries = 0;
        while (parts.Count < count && tries++ < count * 30)
        {
            float a = R() * Mathf.PI * 2f;
            float r = Mathf.Lerp(r0, r1, Mathf.Pow(R(), 1.4f));   // denser near the walls, where they are seen
            float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
            if (OverArena(x, z, 2.5f)) continue;
            float s = Mathf.Lerp(s0, s1, Mathf.Pow(R(), 2f)) * (1f + r / r1);
            var pos = new Vector3(x, HeightAt(x, z) - (rocks ? 0.25f * s : 0.05f), z);
            var rot = Quaternion.Euler(rocks ? (R() - 0.5f) * 16f : 0f, R() * 360f, rocks ? (R() - 0.5f) * 16f : 0f);
            var scale = rocks ? new Vector3(s, s * Mathf.Lerp(0.7f, 1.1f, R()), s * Mathf.Lerp(0.8f, 1.2f, R())) : Vector3.one * s;
            parts.Add(new CombineInstance { mesh = meshes[parts.Count % meshes.Length], transform = Matrix4x4.TRS(pos, rot, scale) });
        }
        var combined = new Mesh { indexFormat = IndexFormat.UInt32 };
        combined.CombineMeshes(parts.ToArray(), true, true);
        combined.RecalculateBounds();
        parent.AddComponent<MeshFilter>().sharedMesh = SaveMesh(combined, meshName);
        var mr = parent.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    internal static Mesh SaveMesh(Mesh m, string name, string dir = Dir)
    {
        string path = dir + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        m.name = name;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    internal static Texture2D SaveTex(string name, int w, int h, Color[] px, bool normal, bool clamp = false, string dir = Dir)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.SetPixels(px);
        t.Apply();
        string path = dir + "/" + name + ".png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.sRGBTexture = !normal;
        imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        imp.mipmapEnabled = true;
        imp.anisoLevel = clamp ? 1 : 4;
        imp.maxTextureSize = Mathf.Max(w, h);
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    internal static Material Mat(string name, Texture2D albedo, Texture2D normal, float smooth, float bump, bool specular, string dir = Dir)
    {
        string path = dir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", albedo);
        m.SetColor("_BaseColor", Color.white);
        if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); }
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_SpecularHighlights", specular ? 1f : 0f);
        if (specular) m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF"); else m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------------------------------------------------------- landscape

    class Layer
    {
        public string name;
        public float r0, r1, ringPow, uvRock;
        public float rockBelow = 0.74f;              // triangles whose normal.y is under this take the sandstone
        public int rings, segs;
        public Func<float, float, float, float> h;   // (x, z, r) -> height
        public bool conform;                          // tuck under the arena mesh
    }

    static float S(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
    static float N(float x, float z, float f, float o) => Mathf.PerlinNoise(x * f + 1000f + o, z * f + 1000f + o * 0.73f);

    static float Fbm(float x, float z, float f, int oct, float o)
    {
        float a = 1f, s = 0f, n = 0f;
        for (int i = 0; i < oct; i++) { s += a * N(x, z, f, o + i * 17.3f); n += a; a *= 0.5f; f *= 2.03f; }
        return s / n;
    }

    static float Ridge(float x, float z, float f, int oct, float o)
    {
        float a = 1f, s = 0f, n = 0f;
        for (int i = 0; i < oct; i++) { float v = 1f - Mathf.Abs(N(x, z, f, o + i * 31.7f) * 2f - 1f); s += a * v * v; n += a; a *= 0.5f; f *= 2.07f; }
        return s / n;
    }

    static float Terrace(float h, float step, float mix)
    {
        float t = h / step, fl = Mathf.Floor(t);
        return Mathf.Lerp(h, (fl + S(0.3f, 0.7f, t - fl)) * step, mix);
    }

    static Layer[] cachedLayers;

    static Layer[] Layers()
    {
        if (cachedLayers != null) return cachedLayers;
        cachedLayers = new[]
        {
            // One sand sheet from under the walls out to the big dunes: flat where it buries the
            // arena's foot, small dunes beyond, sharp-crested big dunes further out. One mesh, so
            // there is no seam between the near sand and the far.
            new Layer { name = "Dunes", r0 = 12f, r1 = 330f, rings = 92, segs = 320, ringPow = 1.7f, uvRock = 24f, rockBelow = 0.70f, conform = true,
                h = (x, z, r) =>
                {
                    float big = S(80f, 200f, r);
                    float amp = Mathf.Lerp(0.25f, 30f, Mathf.Pow(S(26f, 290f, r), 1.15f));
                    float crest = Mathf.Lerp(Ridge(x, z, 1f / 38f, 3, 3f), Ridge(x, z, 1f / 70f, 3, 11f), big);
                    float body = Fbm(x, z, 1f / 140f, 3, 19f);
                    return -1.9f + S(40f, 140f, r) * 5f + amp * (0.55f * crest + 0.6f * (body - 0.3f)) - S(305f, 330f, r) * 45f;
                } },
            // Rocky ridges and mesas, terraced into sandstone ledges, broken up round the ring.
            new Layer { name = "Ridges", r0 = 225f, r1 = 530f, rings = 64, segs = 480, ringPow = 1.1f, uvRock = 30f, rockBelow = 0.95f,
                h = (x, z, r) =>
                {
                    float mask = S(250f, 340f, r) * (0.45f + 0.55f * Fbm(x, z, 1f / 260f, 2, 23f));
                    float raw = mask * (22f + 85f * Ridge(x, z, 1f / 150f, 4, 29f));
                    return Terrace(raw, 9f, 0.4f) - 8f - S(495f, 530f, r) * 70f;
                } },
            // Far layered mountains; the haze takes their contrast down.
            new Layer { name = "Mountains", r0 = 450f, r1 = 860f, rings = 48, segs = 360, ringPow = 1.0f, uvRock = 55f, rockBelow = 1.01f,
                h = (x, z, r) =>
                {
                    float mask = S(460f, 600f, r);
                    float raw = mask * (95f + 170f * Ridge(x, z, 1f / 260f, 5, 37f)) * (0.65f + 0.35f * Fbm(x, z, 1f / 520f, 2, 41f));
                    return Terrace(raw, 16f, 0.3f) - 12f - S(780f, 860f, r) * raw * 0.4f;
                } },
        };
        return cachedLayers;
    }

    /// <summary>The visible landscape height at (x, z): the highest layer there.</summary>
    static float HeightAt(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z), best = -50f;
        foreach (var l in Layers()) if (r >= l.r0 && r <= l.r1) best = Mathf.Max(best, l.h(x, z, r));
        return best;
    }

    static Mesh BuildLayer(Layer l)
    {
        int cols = l.segs + 1, rows = l.rings + 1;
        var pos = new Vector3[cols * rows];
        var nrm = new Vector3[cols * rows];
        var ang = new float[cols * rows];
        for (int i = 0; i < rows; i++)
        {
            float r = Mathf.Lerp(l.r0, l.r1, Mathf.Pow(i / (float)l.rings, l.ringPow));
            for (int j = 0; j < cols; j++)
            {
                float a = j / (float)l.segs * Mathf.PI * 2f;
                float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
                float y = l.h(x, z, r);
                if (l.conform && r < 48f && arenaCollider != null &&
                    arenaCollider.Raycast(new Ray(new Vector3(x, 60f, z), Vector3.down), out var hit, 120f))
                    y = Mathf.Min(y, hit.point.y - 0.15f);
                int k = i * cols + j;
                pos[k] = new Vector3(x, y, z);
                ang[k] = a;
                float e = Mathf.Max(0.5f, r * 0.004f);   // normals from the height function: seamless at the wrap
                float dx = l.h(x + e, z, r) - l.h(x - e, z, r), dz = l.h(x, z + e, r) - l.h(x, z - e, r);
                nrm[k] = new Vector3(-dx, 2f * e, -dz).normalized;
            }
        }

        // Steep triangles take the sandstone, the rest the sand. Rock triangles get their own vertex
        // copies so they can carry strata UVs (arc length x height) while sand maps from above.
        var verts = new List<Vector3>(pos);
        var norms = new List<Vector3>(nrm);
        var uvs = new List<Vector2>();
        foreach (var p in pos) uvs.Add(new Vector2(p.x / 22f, p.z / 22f));
        var rockIndex = new Dictionary<int, int>();
        var sandTris = new List<int>();
        var rockTris = new List<int>();
        int RockVert(int k)
        {
            if (rockIndex.TryGetValue(k, out int v)) return v;
            v = verts.Count;
            verts.Add(pos[k]);
            norms.Add(nrm[k]);
            float r = new Vector2(pos[k].x, pos[k].z).magnitude;
            uvs.Add(new Vector2(ang[k] * r / l.uvRock, pos[k].y / (l.uvRock * 1.2f)));
            rockIndex[k] = v;
            return v;
        }
        for (int i = 0; i < l.rings; i++)
            for (int j = 0; j < l.segs; j++)
            {
                int a = i * cols + j, b = a + 1, c = a + cols, d = c + 1;
                foreach (var t in new[] { new[] { a, c, b }, new[] { b, c, d } })
                {
                    float ny = (nrm[t[0]].y + nrm[t[1]].y + nrm[t[2]].y) / 3f;
                    Vector3 fn = Vector3.Cross(pos[t[1]] - pos[t[0]], pos[t[2]] - pos[t[0]]);
                    int t1 = t[1], t2 = t[2];
                    if (fn.y < 0f) { t1 = t[2]; t2 = t[1]; }   // face up
                    if (ny < l.rockBelow) { rockTris.Add(RockVert(t[0])); rockTris.Add(RockVert(t1)); rockTris.Add(RockVert(t2)); }
                    else { sandTris.Add(t[0]); sandTris.Add(t1); sandTris.Add(t2); }
                }
            }

        var mesh = new Mesh();
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(sandTris, 0);
        mesh.SetTriangles(rockTris, 1);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // ---------------------------------------------------------------- props

    internal class MeshBuilder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int>[] tris = { new List<int>(), new List<int>() };

        public int Add(Vector3 p, Vector3 normal, Vector2 t) { v.Add(p); n.Add(normal); uv.Add(t); return v.Count - 1; }

        /// <summary>Adds a triangle wound so its front faces <paramref name="want"/>.</summary>
        public void Tri(int sub, int a, int b, int c, Vector3 want)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), want) < 0f) { int t = b; b = c; c = t; }
            tris[sub].Add(a); tris[sub].Add(b); tris[sub].Add(c);
        }

        public Mesh ToMesh(bool tangents)
        {
            var m = new Mesh();
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = 2;
            m.SetTriangles(tris[0], 0);
            m.SetTriangles(tris[1], 1);
            m.RecalculateBounds();
            if (tangents) m.RecalculateTangents();
            return m;
        }
    }

    // Palette rows (v): 0-0.5 live fronds (young deep green -> older olive), 0.5-0.75 dry, 0.75-1 dates.
    static Mesh Palm(int seed, float height, float lean)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var mb = new MeshBuilder();
        float la = R(0f, Mathf.PI * 2f);
        Vector3 leanDir = new Vector3(Mathf.Cos(la), 0f, Mathf.Sin(la));
        Vector3 Spine(float t) => leanDir * (lean * height * Mathf.Pow(t, 1.6f)) + Vector3.up * (t * height);

        // trunk
        const int rings = 20, sides = 9;
        Vector3 top = Spine(1f), topT = (Spine(1f) - Spine(0.98f)).normalized;
        for (int i = 0; i <= rings; i++)
        {
            float t = i / (float)rings;
            Vector3 c = Spine(t), T = (Spine(Mathf.Min(1f, t + 0.01f)) - Spine(Mathf.Max(0f, t - 0.01f))).normalized;
            Vector3 right = Vector3.Cross(T, Vector3.forward).normalized, fwd = Vector3.Cross(right, T);
            float rad = Mathf.Lerp(0.34f, 0.24f, t) + 0.22f * Mathf.Pow(1f - t, 8f) + 0.012f * Mathf.Sin(t * height * 11f);
            for (int j = 0; j <= sides; j++)
            {
                float a = j / (float)sides * Mathf.PI * 2f;
                Vector3 d = right * Mathf.Cos(a) + fwd * Mathf.Sin(a);
                mb.Add(c + d * rad, d, new Vector2(j / (float)sides * 2f, t * height / 0.55f));
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * (sides + 1) + j, b = a + 1, c = a + sides + 1, d = c + 1;
                Vector3 outward = mb.n[a];
                mb.Tri(0, a, c, b, outward);
                mb.Tri(0, b, c, d, outward);
            }
        // fibrous crown bulb
        int apex = mb.Add(top + topT * 0.55f, topT, new Vector2(0.5f, (height + 0.5f) / 0.55f));
        int ringStart = rings * (sides + 1);
        for (int j = 0; j < sides; j++)
        {
            int a = ringStart + j, b = a + 1;
            Vector3 outward = (mb.n[a] + topT).normalized;
            mb.Tri(0, a, b, apex, outward);
        }

        // fronds
        const int live = 16, dry = 7;
        for (int k = 0; k < live; k++)
        {
            float yaw = k * 137.5f * Mathf.Deg2Rad + R(-0.15f, 0.15f);
            float el = Mathf.Lerp(58f, -14f, k / (float)(live - 1)) + R(-6f, 6f);
            float L = R(3.3f, 4.3f) * (height / 8.5f);
            float droop = R(0.32f, 0.5f) * (1.25f - el / 120f);
            Frond(mb, top - topT * 0.1f, yaw, el, L, droop, Mathf.Lerp(0.08f, 0.42f, k / (float)live), false, rng);
        }
        for (int k = 0; k < dry; k++)
        {
            float yaw = (k * 137.5f + 60f) * Mathf.Deg2Rad;
            Frond(mb, top - topT * 0.45f, yaw, R(-82f, -62f), R(2.2f, 2.9f), 0.08f, R(0.56f, 0.71f), true, rng);
        }

        // dates
        int clusters = 2 + seed % 2;
        for (int k = 0; k < clusters; k++)
        {
            float yaw = R(0f, Mathf.PI * 2f);
            Vector3 h = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            Ellipsoid(mb, 1, top - topT * 0.55f + h * 0.38f + Vector3.down * 0.3f, new Vector3(0.15f, 0.28f, 0.15f), new Vector2(0.5f, 0.88f));
        }
        return mb.ToMesh(true);
    }

    static void Frond(MeshBuilder mb, Vector3 baseP, float yaw, float elDeg, float L, float droop, float vRow, bool dry, System.Random rng)
    {
        float el = elDeg * Mathf.Deg2Rad;
        Vector3 dirH = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
        Vector3 d0 = dirH * Mathf.Cos(el) + Vector3.up * Mathf.Sin(el);
        Vector3 P(float s) => baseP + dirH * 0.12f + d0 * (L * s) + Vector3.down * (droop * L * s * s);
        Vector3 Up = Vector3.up;

        // midrib, a thin two-sided ribbon
        const int segs = 10;
        for (int i = 0; i < segs; i++)
        {
            float s0 = i / (float)segs, s1 = (i + 1) / (float)segs;
            Vector3 p0 = P(s0), p1 = P(s1), T = (p1 - p0).normalized;
            Vector3 side = Vector3.Cross(Up, T).normalized;
            float w0 = Mathf.Lerp(0.06f, 0.015f, s0), w1 = Mathf.Lerp(0.06f, 0.015f, s1);
            Vector3 fn = Vector3.Cross(T, side); if (fn.y < 0) fn = -fn;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 nn = (fn * sgn * 0.5f + Up).normalized;
                int a = mb.Add(p0 - side * w0, nn, new Vector2(0.05f, vRow)), b = mb.Add(p0 + side * w0, nn, new Vector2(0.05f, vRow));
                int c = mb.Add(p1 - side * w1, nn, new Vector2(0.1f, vRow)), d = mb.Add(p1 + side * w1, nn, new Vector2(0.1f, vRow));
                mb.Tri(1, a, c, b, fn * sgn); mb.Tri(1, b, c, d, fn * sgn);
            }
        }

        // leaflets
        const int count = 18;
        for (int i = 0; i < count; i++)
        {
            float s = Mathf.Lerp(0.14f, 0.97f, i / (float)(count - 1));
            Vector3 p = P(s), T = (P(s + 0.01f) - P(s - 0.01f)).normalized;
            Vector3 side = Vector3.Cross(Up, T).normalized;
            Vector3 upish = Vector3.Cross(T, side); if (upish.y < 0) upish = -upish;
            float len = 0.95f * (L / 3.8f) * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Lerp(0.08f, 0.95f, s)), 0.7f) * Mathf.Lerp(0.85f, 1.1f, (float)rng.NextDouble());
            if (dry) len *= 0.75f;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 dir = (side * sgn * 0.85f + T * 0.5f + upish * (dry ? -0.2f : 0.3f)).normalized;
                Vector3 down = Vector3.down * len * (dry ? 0.55f : 0.26f);
                Vector3 mid = p + dir * len * 0.5f + down * 0.3f, tip = p + dir * len + down;
                Vector3 wv = Vector3.Cross(dir, upish).normalized;
                Vector3 fn = Vector3.Cross(dir, wv); if (fn.y < 0) fn = -fn;
                const float w0 = 0.075f, w1 = 0.06f;
                for (int face = -1; face <= 1; face += 2)
                {
                    Vector3 nn = (fn * face * 0.5f + Up).normalized;
                    int b0 = mb.Add(p - wv * w0, nn, new Vector2(0.15f, vRow)), b1 = mb.Add(p + wv * w0, nn, new Vector2(0.15f, vRow));
                    int m0 = mb.Add(mid - wv * w1, nn, new Vector2(0.55f, vRow)), m1 = mb.Add(mid + wv * w1, nn, new Vector2(0.55f, vRow));
                    int tp = mb.Add(tip, nn, new Vector2(0.95f, vRow));
                    mb.Tri(1, b0, m0, b1, fn * face); mb.Tri(1, b1, m0, m1, fn * face); mb.Tri(1, m0, tp, m1, fn * face);
                }
            }
        }
    }

    static void Ellipsoid(MeshBuilder mb, int sub, Vector3 c, Vector3 r, Vector2 uv)
    {
        const int lon = 8, lat = 6;
        int start = mb.v.Count;
        for (int i = 0; i <= lat; i++)
        {
            float th = i / (float)lat * Mathf.PI;
            for (int j = 0; j <= lon; j++)
            {
                float ph = j / (float)lon * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                float bump = 1f + 0.12f * Mathf.Sin(ph * 3f + th * 5f);
                mb.Add(c + Vector3.Scale(d, r) * bump, d, uv);
            }
        }
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                int a = start + i * (lon + 1) + j, b = a + 1, cc = a + lon + 1, d = cc + 1;
                mb.Tri(sub, a, cc, b, mb.n[a] + mb.n[d]); mb.Tri(sub, b, cc, d, mb.n[a] + mb.n[d]);
            }
    }

    static Mesh TrunkCollider()
    {
        const int sides = 8;
        var v = new List<Vector3>();
        for (int i = 0; i < sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            v.Add(new Vector3(Mathf.Cos(a) * TrunkRadius, 0f, Mathf.Sin(a) * TrunkRadius));
            v.Add(new Vector3(Mathf.Cos(a) * TrunkRadius, 3.6f, Mathf.Sin(a) * TrunkRadius));
        }
        var t = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            int a = i * 2, b = a + 1, c = (a + 2) % (sides * 2), d = c + 1;
            t.AddRange(new[] { a, b, c, c, b, d });
        }
        var m = new Mesh();
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    internal static Mesh Rock(int seed)
    {
        // icosphere, displaced and flattened, sat on a flat-ish base
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new List<Vector3> {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
        var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        for (int s = 0; s < 2; s++)
        {
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int i)) return i;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                return mid[key] = v.Count - 1;
            }
            var nf = new List<int>();
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = nf;
        }
        float o = seed * 13.1f;
        float N3(Vector3 p) => (Mathf.PerlinNoise(p.x + o, p.y + o) + Mathf.PerlinNoise(p.y + o, p.z + o) + Mathf.PerlinNoise(p.z + o, p.x + o)) / 3f;
        var uv = new List<Vector2>();
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 p = v[i];
            float k = 1f + 0.45f * (N3(p * 1.3f + Vector3.one * 5f) - 0.5f) + 0.15f * (N3(p * 3.4f) - 0.5f);
            p *= k;
            p.y *= 0.6f;
            if (p.y < -0.2f) p.y = -0.2f + (p.y + 0.2f) * 0.25f;
            v[i] = p;
            uv.Add(new Vector2(Mathf.Atan2(p.z, p.x) / Mathf.PI, p.y * 0.35f));
        }
        var m = new Mesh();
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetTriangles(f, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        m.RecalculateTangents();
        return m;
    }

    static Mesh Shrub(int seed)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var mb = new MeshBuilder();
        int twigs = 22;
        for (int k = 0; k < twigs; k++)
        {
            float yaw = R(0f, Mathf.PI * 2f), el = R(25f, 75f) * Mathf.Deg2Rad, len = R(0.45f, 0.95f);
            Vector3 d = new Vector3(Mathf.Cos(yaw) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(yaw) * Mathf.Cos(el));
            Vector3 b = new Vector3(R(-0.08f, 0.08f), 0f, R(-0.08f, 0.08f));
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            float vRow = k % 5 == 0 ? 0.47f : R(0.6f, 0.72f);   // a few with a trace of grey-green
            Vector3 prev = b;
            for (int s = 1; s <= 3; s++)
            {
                float u = s / 3f;
                Vector3 p = b + d * len * u + Vector3.down * 0.12f * len * u * u + side * Mathf.Sin(u * 3f + k) * 0.04f;
                float w0 = Mathf.Lerp(0.025f, 0.004f, (s - 1) / 3f), w1 = Mathf.Lerp(0.025f, 0.004f, u);
                Vector3 fn = Vector3.Cross(p - prev, side).normalized; if (fn.y < 0) fn = -fn;
                for (int face = -1; face <= 1; face += 2)
                {
                    Vector3 nn = (fn * face * 0.5f + Vector3.up).normalized;
                    int a = mb.Add(prev - side * w0, nn, new Vector2(0.3f, vRow)), bb = mb.Add(prev + side * w0, nn, new Vector2(0.3f, vRow));
                    int c = mb.Add(p - side * w1, nn, new Vector2(0.7f, vRow)), dd = mb.Add(p + side * w1, nn, new Vector2(0.7f, vRow));
                    mb.Tri(0, a, c, bb, fn * face); mb.Tri(0, bb, c, dd, fn * face);
                }
                prev = p;
            }
        }
        var m = mb.ToMesh(false);
        m.subMeshCount = 1;   // one material: the palette
        m.SetTriangles(mb.tris[0], 0);
        return m;
    }

    // ---------------------------------------------------------------- textures

    internal static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    /// <summary>Value noise that tiles over [0,1) with <paramref name="period"/> cells.</summary>
    static float TNoise(float u, float v, int periodU, int periodV, int seed)
    {
        float x = u * periodU, y = v * periodV;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int Wx(int i) => ((i % periodU) + periodU) % periodU;
        int Wy(int i) => ((i % periodV) + periodV) % periodV;
        float a = Hash(Wx(x0), Wy(y0), seed), b = Hash(Wx(x0 + 1), Wy(y0), seed);
        float c = Hash(Wx(x0), Wy(y0 + 1), seed), d = Hash(Wx(x0 + 1), Wy(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    internal static float TFbm(float u, float v, int pu, int pv, int oct, int seed)
    {
        float a = 1f, s = 0f, n = 0f;
        for (int i = 0; i < oct; i++) { s += a * TNoise(u, v, pu, pv, seed + i * 7); n += a; a *= 0.5f; pu *= 2; pv *= 2; }
        return s / n;
    }

    static Color[] SandAlbedo(int size, out float[] height)
    {
        var px = new Color[size * size];
        height = new float[size * size];
        Color dark = new Color(0.60f, 0.46f, 0.32f), light = new Color(0.76f, 0.62f, 0.45f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float low = TFbm(u, v, 4, 4, 3, 1);
                float warp = TFbm(u, v, 3, 3, 2, 2);
                float ripple = 0.5f + 0.5f * Mathf.Sin((v * 40f + u * 6f + warp * 3.5f) * Mathf.PI * 2f);
                ripple = Mathf.Pow(ripple, 1.6f);
                float grain = Hash(x, y, 9);
                float h = ripple * 0.3f + low * 0.55f + grain * 0.15f;
                height[y * size + x] = h;
                Color c = Color.Lerp(dark, light, Mathf.Clamp01(low * 0.8f + ripple * 0.1f + grain * 0.12f));
                px[y * size + x] = c;
            }
        return px;
    }

    static Color[] RockAlbedo(int size, out float[] height)
    {
        var px = new Color[size * size];
        height = new float[size * size];
        Color[] strata = {
            new Color(0.66f, 0.47f, 0.32f), new Color(0.56f, 0.38f, 0.26f), new Color(0.72f, 0.56f, 0.40f),
            new Color(0.49f, 0.33f, 0.23f), new Color(0.62f, 0.44f, 0.30f), new Color(0.70f, 0.52f, 0.36f) };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float wob = (TFbm(u, v, 4, 2, 3, 21) - 0.5f) * 0.05f;
                float band = (v + wob) * 9f;
                int bi = Mathf.FloorToInt(band);
                float bf = band - bi;
                int idx = (int)(Hash(((bi % 9) + 9) % 9, 0, 33) * strata.Length) % strata.Length;
                Color c = strata[idx];
                float erosion = TFbm(u, v, 16, 8, 4, 41);
                float streak = TFbm(u, v * 0.25f, 48, 2, 2, 51);   // desert-varnish streaks running down
                float ledge = S(0.0f, 0.12f, bf) * (1f - S(0.85f, 1f, bf));
                c *= Mathf.Lerp(0.82f, 1.08f, erosion) * Mathf.Lerp(1f, 0.86f, S(0.6f, 0.9f, streak)) * Mathf.Lerp(0.85f, 1f, ledge);
                c.a = 1f;
                px[y * size + x] = c;
                height[y * size + x] = ledge * 0.6f + erosion * 0.4f;
            }
        return px;
    }

    static Color[] BarkAlbedo(int w, int h, out float[] height)
    {
        var px = new Color[w * h];
        height = new float[w * h];
        Color edge = new Color(0.27f, 0.21f, 0.15f), mid = new Color(0.47f, 0.38f, 0.28f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float cy = v * 10f;
                int row = Mathf.FloorToInt(cy);
                float cx = u * 6f + (row % 2) * 0.5f;
                float fx = cx - Mathf.Floor(cx) - 0.5f, fy = cy - row;
                float scale = 1f - Mathf.Clamp01(Mathf.Abs(fx) * 1.6f + Mathf.Pow(1f - fy, 2f) * 0.9f);
                float n = TFbm(u, v, 8, 16, 3, 61);
                float hh = scale * 0.8f + n * 0.2f;
                height[y * w + x] = hh;
                Color c = Color.Lerp(edge, mid, Mathf.Clamp01(scale * 1.2f)) * Mathf.Lerp(0.85f, 1.1f, n);
                c.a = 1f;
                px[y * w + x] = c;
            }
        return px;
    }

    static Color[] Palette()
    {
        const int s = 64;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = x / (s - 1f), v = (y + 0.5f) / s;
                Color c;
                if (v < 0.5f)
                {
                    float age = v / 0.5f;
                    Color b = Color.Lerp(new Color(0.15f, 0.25f, 0.10f), new Color(0.30f, 0.33f, 0.14f), age);
                    Color t = Color.Lerp(new Color(0.32f, 0.41f, 0.16f), new Color(0.52f, 0.50f, 0.24f), age);
                    c = Color.Lerp(b, t, u);
                }
                else if (v < 0.75f) c = Color.Lerp(new Color(0.30f, 0.22f, 0.13f), new Color(0.52f, 0.42f, 0.26f), u * 0.7f + (v - 0.5f) * 1.2f);
                else c = Color.Lerp(new Color(0.34f, 0.16f, 0.06f), new Color(0.48f, 0.25f, 0.09f), u);
                c.a = 1f;
                px[y * s + x] = c;
            }
        return px;
    }

    internal static Color[] NormalFromHeight(float[] h, int w, int hgt, float strength)
    {
        var px = new Color[w * hgt];
        for (int y = 0; y < hgt; y++)
            for (int x = 0; x < w; x++)
            {
                float l = h[y * w + (x - 1 + w) % w], r = h[y * w + (x + 1) % w];
                float d = h[((y - 1 + hgt) % hgt) * w + x], u = h[((y + 1) % hgt) * w + x];
                Vector3 n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                px[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        return px;
    }
}
#endif
