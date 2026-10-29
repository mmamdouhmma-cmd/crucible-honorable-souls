#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using D = CrucibleDesertUpgrade;
using MB = CrucibleDesertUpgrade.MeshBuilder;

/// <summary>
/// The forest round the Forest Clearing. The arena (its one Tripo mesh, bounds, marks) is NOT
/// touched and nothing here has a collider: everything sits outside the arena mesh, well past the
/// 16-unit fight circle, under <c>ForestClearing_Surroundings</c> - the Forest entry's sharedRoot.
///
/// - Woodland floor (r 26-140, tucked under the arena mesh by raycast) and a far CANOPY sheet
///   (r 96-420) that rises out of the floor into rolling forest crowns, so every gap between the
///   arena's trees looks into more forest instead of the void. Each sinks under the next.
/// - Two rings of trees in the arena's own style (flared trunks, exposed roots, arching branches
///   ending in flat lobed leaf pads), the near ring casting shadows into the clearing.
/// - Moss boulders, ferns, bushes, fallen logs and purple mushrooms in the strip behind the trees.
/// - The late-afternoon sun, sky, haze and grade on an ArenaAtmosphere.
///
/// Every prop group is merged into one mesh per material. All of it is generated here into
/// <c>Environment/ForestSurroundings/</c>; same seeds, same result on every run.
/// </summary>
public static class CrucibleForestUpgrade
{
    const string Dir = "Assets/_project/Environment/ForestSurroundings";
    const string RootName = "ForestClearing_Surroundings";
    const string ArenaRoot = "ForestClearing";

    // Golden hour from the west: low enough to glow and to slant through the trunks, high enough
    // that the arena's own 20-unit trees do not put the whole clearing in shade.
    static readonly Vector3 SunEuler = new Vector3(44f, 58f, 0f);

    static Collider arenaCollider;

    [MenuItem("Crucible/Arena/Forest/Build the surroundings")]
    public static void Build()
    {
        var arena = D.FindRoot(ArenaRoot);
        if (arena == null) { Debug.LogError("Forest upgrade: no ForestClearing root in the open scene."); return; }
        bool was = arena.activeSelf;
        arena.SetActive(true);   // raycasts need an active collider
        Physics.SyncTransforms();
        try { BuildAround(arena); }
        finally { arena.SetActive(was); arenaCollider = null; }
    }

    [MenuItem("Crucible/Arena/Forest/Remove the surroundings")]
    public static void RemoveMenu()
    {
        Remove();
        D.SetSharedRoot(ArenaRoot, "");
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    static void Remove()
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == RootName) Object.DestroyImmediate(go);
    }

    static void BuildAround(GameObject arena)
    {
        Remove();
        Directory.CreateDirectory(Dir);
        foreach (var mc in arena.GetComponentsInChildren<MeshCollider>())
            if (!mc.isTrigger) { arenaCollider = mc; break; }

        // ---- textures and materials (one each, shared by everything)
        var ground = D.Mat("Forest_Ground", Tex("Forest_Ground", 1024, 1024, GroundAlbedo, 4f), TexN, 0.10f, 0.9f, false, Dir);
        var bark = D.Mat("Forest_Bark", Tex("Forest_Bark", 256, 512, BarkAlbedo, 6f), TexN, 0.18f, 1.0f, false, Dir);
        var moss = D.Mat("Forest_MossRock", Tex("Forest_MossRock", 512, 512, MossAlbedo, 5f), TexN, 0.12f, 1.0f, false, Dir);
        var canopy = D.Mat("Forest_Canopy", Tex("Forest_Canopy", 1024, 1024, CanopyAlbedo, 7f), TexN, 0.08f, 1.0f, false, Dir);
        var leaf = D.Mat("Forest_Foliage", D.SaveTex("Forest_Palette", 64, 64, Palette(), false, true, Dir), null, 0.22f, 0f, true, Dir);

        var root = new GameObject(RootName);

        // ---- land
        var landGo = Child(root, "Land");
        Renderer(Child(landGo, "Floor"), D.SaveMesh(Land(26f, 140f, 90, 400, 1.5f, FloorH, true, 9f), "Land_Floor", Dir), ShadowCastingMode.Off, ground);
        Renderer(Child(landGo, "Canopy"), D.SaveMesh(Land(96f, 420f, 72, 480, 1.6f, CanopyH, false, 40f), "Land_Canopy", Dir), ShadowCastingMode.Off, canopy);

        // ---- trees
        var trees = new Mesh[6];
        for (int i = 0; i < trees.Length; i++) trees[i] = D.SaveMesh(Tree(11 + i * 7, Mathf.Lerp(20f, 30f, i / 5f)), "Tree_" + i, Dir);
        var placed = new List<Vector3>();
        PlaceTrees(Child(root, "Trees_Near"), trees, bark, leaf, 34, 37f, 62f, 0.85f, 1.2f, 7f, 5, placed, ShadowCastingMode.On);
        PlaceTrees(Child(root, "Trees_Far"), trees, bark, leaf, 85, 60f, 108f, 0.95f, 1.45f, 8.5f, 9, placed, ShadowCastingMode.Off);

        // ---- undergrowth, behind the arena's own trees
        var under = Child(root, "Undergrowth");
        Scatter(Child(under, "Boulders"), new[] { D.Rock(3), D.Rock(8), D.Rock(15), D.Rock(21) }, "Boulders_Combined", moss, 70, 33f, 75f, 1.0f, 3.2f, 21, 2.5f, true, placed);
        Scatter(Child(under, "Ferns"), new[] { Fern(1), Fern(2), Fern(3) }, "Ferns_Combined", leaf, 260, 32f, 80f, 0.9f, 1.8f, 22, 1.5f, false, placed);
        Scatter(Child(under, "Bushes"), new[] { Bush(1), Bush(2), Bush(3) }, "Bushes_Combined", leaf, 110, 33f, 90f, 1.0f, 2.2f, 23, 2f, false, placed);
        Scatter(Child(under, "Mushrooms"), new[] { Mushrooms(1), Mushrooms(2), Mushrooms(3), Orbs(4) }, "Mushrooms_Combined", leaf, 70, 32f, 62f, 0.8f, 1.6f, 24, 1.5f, false, placed);
        Scatter(Child(under, "Logs"), new[] { Log(1), Log(2), Log(3) }, "Logs_Combined", bark, 16, 36f, 70f, 0.9f, 1.3f, 25, 4f, false, placed);

        BuildAtmosphere(root);
        D.SetSharedRoot(ArenaRoot, RootName);
        root.SetActive(false);   // ArenaDirectory switches it on with the Forest Clearing
        EditorSceneManager.MarkSceneDirty(arena.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Forest upgrade: surroundings built (save the scene to keep them).");
    }

    // ---------------------------------------------------------------- atmosphere

    static void BuildAtmosphere(GameObject root)
    {
        var lighting = Child(root, "Lighting");
        var sunGo = Child(lighting, "Sun");
        sunGo.transform.rotation = Quaternion.Euler(SunEuler);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1.00f, 0.83f, 0.62f);
        sun.intensity = 1.9f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.8f;

        var fillGo = Child(lighting, "Sky Fill");
        fillGo.transform.rotation = Quaternion.Euler(50f, 245f, 0f);
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.62f, 0.72f, 0.86f);
        fill.intensity = 0.3f;
        fill.shadows = LightShadows.None;

        string skyPath = Dir + "/Forest_Sky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
        sky.SetFloat("_SunDisk", 2f);
        sky.SetFloat("_SunSize", 0.05f);
        sky.SetFloat("_SunSizeConvergence", 6f);
        sky.SetFloat("_AtmosphereThickness", 1.25f);
        sky.SetColor("_SkyTint", new Color(0.54f, 0.60f, 0.68f));
        sky.SetColor("_GroundColor", new Color(0.42f, 0.40f, 0.30f));
        sky.SetFloat("_Exposure", 1.2f);
        EditorUtility.SetDirty(sky);

        var lookGo = Child(root, "Forest Look");
        var vol = lookGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10;
        var profile = D.CloneProfile("Assets/_project/Settings/CrucibleLookURP.asset", Dir + "/Forest_Look.asset");
        foreach (var c in profile.components)
        {
            if (c is ColorAdjustments ca)
            {
                ca.postExposure.Override(0.15f);
                ca.contrast.Override(8f);
                ca.saturation.Override(-10f);   // the clearing's grass and orbs are saturated already
                ca.colorFilter.Override(new Color(1.00f, 0.98f, 0.94f));
            }
            if (c is LiftGammaGain lgg) lgg.lift.Override(new Vector4(1.00f, 1.00f, 1.02f, 0.015f));
            if (c is Bloom b) b.intensity.Override(0.12f);
            if (c is FilmGrain g) g.intensity.Override(0.14f);
        }
        EditorUtility.SetDirty(profile);
        vol.sharedProfile = profile;

        var atm = root.AddComponent(D.GameType("ArenaAtmosphere"));
        var so = new SerializedObject(atm);
        so.FindProperty("skybox").objectReferenceValue = sky;
        so.FindProperty("sun").objectReferenceValue = sun;
        so.FindProperty("ambientMode").intValue = (int)AmbientMode.Trilight;
        so.FindProperty("ambientSky").colorValue = new Color(0.62f, 0.67f, 0.72f);
        so.FindProperty("ambientEquator").colorValue = new Color(0.62f, 0.60f, 0.46f);   // light under the canopy
        so.FindProperty("ambientGround").colorValue = new Color(0.27f, 0.26f, 0.17f);
        so.FindProperty("reflectionIntensity").floatValue = 0.6f;
        so.FindProperty("fog").boolValue = true;
        so.FindProperty("fogMode").intValue = (int)FogMode.Linear;
        so.FindProperty("fogColor").colorValue = new Color(0.78f, 0.72f, 0.56f);
        so.FindProperty("fogStart").floatValue = 30f;
        so.FindProperty("fogEnd").floatValue = 440f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- land

    static float S(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

    static float Fbm(float x, float z, float f, int oct, float o)
    {
        float a = 1f, s = 0f, n = 0f;
        for (int i = 0; i < oct; i++)
        {
            s += a * Mathf.PerlinNoise(x * f + 1000f + o + i * 17.3f, z * f + 1000f + o * 0.7f + i * 9.1f);
            n += a; a *= 0.5f; f *= 2.07f;
        }
        return s / n;
    }

    static float FloorH(float x, float z, float r) =>
        -0.7f + S(34f, 115f, r) * 7f + (Fbm(x, z, 0.035f, 3, 1f) - 0.5f) * 3f * S(30f, 55f, r) - S(126f, 140f, r) * 40f;

    static float CanopyH(float x, float z, float r)
    {
        float floor = FloorH(x, z, Mathf.Min(r, 120f));
        float puff = Mathf.Pow(Fbm(x, z, 0.045f, 4, 3f), 1.6f);
        float hills = Fbm(x, z, 0.006f, 3, 9f);
        float crown = 20f + S(120f, 280f, r) * 18f + puff * 14f + hills * 35f * S(110f, 300f, r);
        return Mathf.Lerp(floor - 1.5f, crown, S(96f, 128f, r)) - S(380f, 420f, r) * 120f;
    }

    static float HeightAt(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float h = r <= 140f ? FloorH(x, z, r) : -50f;
        if (r >= 96f) h = Mathf.Max(h, CanopyH(x, z, r));
        return h;
    }

    /// <summary>A polar-grid ring of land; normals come from the height function so the wrap has no seam.</summary>
    static Mesh Land(float r0, float r1, int rings, int segs, float pow, Func<float, float, float, float> h, bool conform, float uvScale)
    {
        int cols = segs + 1, rows = rings + 1;
        var v = new Vector3[cols * rows];
        var n = new Vector3[cols * rows];
        var uv = new Vector2[cols * rows];
        for (int i = 0; i < rows; i++)
        {
            float r = Mathf.Lerp(r0, r1, Mathf.Pow(i / (float)rings, pow));
            for (int j = 0; j < cols; j++)
            {
                float a = j / (float)segs * Mathf.PI * 2f;
                float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
                float y = h(x, z, r);
                if (conform && r < 48f && arenaCollider != null &&
                    arenaCollider.Raycast(new Ray(new Vector3(x, 80f, z), Vector3.down), out var hit, 160f))
                    y = Mathf.Min(y, hit.point.y - 0.15f);
                int k = i * cols + j;
                v[k] = new Vector3(x, y, z);
                float e = Mathf.Max(0.5f, r * 0.004f);
                n[k] = new Vector3(-(h(x + e, z, r) - h(x - e, z, r)), 2f * e, -(h(x, z + e, r) - h(x, z - e, r))).normalized;
                uv[k] = new Vector2(x / uvScale, z / uvScale);
            }
        }
        var t = new List<int>(rings * segs * 6);
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
            {
                int a = i * cols + j, b = a + 1, c = a + cols, d = c + 1;
                AddUp(t, v, a, c, b); AddUp(t, v, b, c, d);
            }
        var m = new Mesh { indexFormat = IndexFormat.UInt32 };
        m.vertices = v; m.normals = n; m.uv = uv;
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        m.RecalculateTangents();
        return m;
    }

    static void AddUp(List<int> t, Vector3[] v, int a, int b, int c)
    {
        if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y < 0f) { int s = b; b = c; c = s; }
        t.Add(a); t.Add(b); t.Add(c);
    }

    // ---------------------------------------------------------------- placement

    static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    static void Renderer(GameObject go, Mesh mesh, ShadowCastingMode shadows, params Material[] mats)
    {
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = shadows;
    }

    static bool OverArena(float x, float z, float pad)
    {
        if (arenaCollider == null) return false;
        for (int i = 0; i < 9; i++)
        {
            float ox = i == 0 ? 0 : Mathf.Cos(i * Mathf.PI / 4f) * pad, oz = i == 0 ? 0 : Mathf.Sin(i * Mathf.PI / 4f) * pad;
            if (arenaCollider.Raycast(new Ray(new Vector3(x + ox, 80f, z + oz), Vector3.down), out _, 160f)) return true;
        }
        return false;
    }

    static bool Crowded(List<Vector3> placed, float x, float z, float gap)
    {
        foreach (var p in placed) if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < gap * gap) return true;
        return false;
    }

    /// <summary>Places trees and merges them into one mesh with two submeshes: bark and foliage.</summary>
    static void PlaceTrees(GameObject go, Mesh[] variants, Material bark, Material leaf, int count, float r0, float r1,
                           float s0, float s1, float gap, int seed, List<Vector3> placed, ShadowCastingMode shadows)
    {
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        var barkParts = new List<CombineInstance>();
        var leafParts = new List<CombineInstance>();
        int n = 0, tries = 0;
        while (n < count && tries++ < count * 60)
        {
            float a = R() * Mathf.PI * 2f, r = Mathf.Lerp(r0, r1, R());
            float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
            if (Crowded(placed, x, z, gap) || OverArena(x, z, 3f)) continue;
            float s = Mathf.Lerp(s0, s1, R());
            var m = Matrix4x4.TRS(new Vector3(x, HeightAt(x, z), z), Quaternion.Euler(0f, R() * 360f, 0f), new Vector3(s, s * Mathf.Lerp(0.92f, 1.1f, R()), s));
            var mesh = variants[n % variants.Length];
            barkParts.Add(new CombineInstance { mesh = mesh, subMeshIndex = 0, transform = m });
            leafParts.Add(new CombineInstance { mesh = mesh, subMeshIndex = 1, transform = m });
            placed.Add(new Vector3(x, gap * 0.6f, z));
            n++;
        }
        var bm = new Mesh { indexFormat = IndexFormat.UInt32 }; bm.CombineMeshes(barkParts.ToArray(), true, true);
        var lm = new Mesh { indexFormat = IndexFormat.UInt32 }; lm.CombineMeshes(leafParts.ToArray(), true, true);
        var all = new Mesh { indexFormat = IndexFormat.UInt32 };
        all.CombineMeshes(new[] { new CombineInstance { mesh = bm, transform = Matrix4x4.identity }, new CombineInstance { mesh = lm, transform = Matrix4x4.identity } }, false, false);
        all.RecalculateBounds();
        Object.DestroyImmediate(bm); Object.DestroyImmediate(lm);
        Renderer(go, D.SaveMesh(all, go.name + "_Combined", Dir), shadows, bark, leaf);
    }

    /// <summary>Scatters props round the clearing (never over the arena mesh, never inside a tree) and merges them into one mesh.</summary>
    static void Scatter(GameObject go, Mesh[] meshes, string meshName, Material mat, int count, float r0, float r1,
                        float s0, float s1, int seed, float pad, bool sink, List<Vector3> placed)
    {
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        var parts = new List<CombineInstance>();
        int tries = 0;
        while (parts.Count < count && tries++ < count * 40)
        {
            float a = R() * Mathf.PI * 2f, r = Mathf.Lerp(r0, r1, Mathf.Pow(R(), 1.3f));
            float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r;
            if (OverArena(x, z, pad)) continue;
            bool inTree = false;
            foreach (var p in placed) if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < 1.6f * 1.6f) { inTree = true; break; }
            if (inTree) continue;
            float s = Mathf.Lerp(s0, s1, Mathf.Pow(R(), 1.5f));
            var pos = new Vector3(x, HeightAt(x, z) - (sink ? 0.3f * s : 0.05f), z);
            var rot = Quaternion.Euler(sink ? (R() - 0.5f) * 14f : (R() - 0.5f) * 6f, R() * 360f, sink ? (R() - 0.5f) * 14f : (R() - 0.5f) * 6f);
            var scale = sink ? new Vector3(s, s * Mathf.Lerp(0.6f, 1.0f, R()), s * Mathf.Lerp(0.8f, 1.2f, R())) : Vector3.one * s;
            parts.Add(new CombineInstance { mesh = meshes[parts.Count % meshes.Length], transform = Matrix4x4.TRS(pos, rot, scale) });
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32 };
        m.CombineMeshes(parts.ToArray(), true, true);
        m.RecalculateBounds();
        Renderer(go, D.SaveMesh(m, meshName, Dir), ShadowCastingMode.Off, mat);
    }

    // ---------------------------------------------------------------- props

    /// <summary>A tube along <paramref name="path"/>; <paramref name="mod"/> scales the radius per (ring, angle).</summary>
    static void Tube(MB mb, int sub, IList<Vector3> path, IList<float> radius, int sides, float texel, Func<int, float, float> mod = null)
    {
        int start = mb.v.Count;
        float len = 0f, uRep = Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * radius[0] / texel));
        Vector3 side = Vector3.zero;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 t = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
            if (i == 0) side = Vector3.Cross(t, Mathf.Abs(t.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            else { side = (side - t * Vector3.Dot(side, t)).normalized; len += Vector3.Distance(path[i], path[i - 1]); }
            Vector3 up = Vector3.Cross(t, side);
            for (int k = 0; k <= sides; k++)
            {
                float ang = k / (float)sides * Mathf.PI * 2f;
                Vector3 d = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                float r = radius[i] * (mod != null ? mod(i, ang) : 1f);
                mb.Add(path[i] + d * r, d, new Vector2(k / (float)sides * uRep, len / texel));
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
            for (int k = 0; k < sides; k++)
            {
                int a = start + i * (sides + 1) + k, b = a + 1, c = a + sides + 1, d = c + 1;
                mb.Tri(sub, a, c, b, mb.n[a] + mb.n[d]);
                mb.Tri(sub, b, c, d, mb.n[a] + mb.n[d]);
            }
    }

    /// <summary>
    /// A lobed, drooping leaf pad - the flat foliage the arena's trees carry - or, with a large
    /// <paramref name="th"/>, a rounded blob (bush, cap, orb). uv.x is the shade (dark underside ->
    /// sunlit top), uv.y the palette row.
    /// </summary>
    static void Pad(MB mb, int sub, Vector3 c, float r, float th, float row, System.Random rng, int lobes = 9, float lobeAmp = 0.3f, float droop = 0.55f)
    {
        const int lon = 22, lat = 6;
        float phase = (float)rng.NextDouble() * 6.28f, tilt = ((float)rng.NextDouble() - 0.5f) * 0.3f;
        int start = mb.v.Count;
        for (int i = 0; i <= lat; i++)
        {
            float th0 = i / (float)lat * Mathf.PI, st = Mathf.Sin(th0), ct = Mathf.Cos(th0);
            for (int j = 0; j <= lon; j++)
            {
                float ph = j / (float)lon * Mathf.PI * 2f;
                float saw = Mathf.Abs(Mathf.Sin(lobes * 0.5f * ph + phase));   // pointed leaf tips, like the arena's pads
                float lobe = 1f + lobeAmp * (saw * 2f - 1f) * st + lobeAmp * 0.35f * Mathf.Sin(ph * 3f + phase * 2f) * st;
                var d = new Vector3(st * Mathf.Cos(ph), ct, st * Mathf.Sin(ph));
                var p = new Vector3(d.x * r * lobe, d.y * th - droop * th * st * st + tilt * d.x * r, d.z * r * lobe);
                var nrm = new Vector3(d.x / r, d.y / Mathf.Max(th, 0.01f), d.z / r).normalized;
                float shade = Mathf.Clamp01(0.15f + 0.8f * (nrm.y * 0.5f + 0.5f) + ((float)rng.NextDouble() - 0.5f) * 0.1f);
                mb.Add(c + p, nrm, new Vector2(shade, row));
            }
        }
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                int a = start + i * (lon + 1) + j, b = a + 1, cc = a + lon + 1, d = cc + 1;
                mb.Tri(sub, a, cc, b, mb.n[a] + mb.n[d]);
                mb.Tri(sub, b, cc, d, mb.n[a] + mb.n[d]);
            }
    }

    // Palette rows (uv.y): 0.03-0.45 tree foliage, 0.5-0.6 fern, 0.64-0.73 mushroom cap, 0.77-0.85 stem, 0.89-0.98 bush.
    static float FoliageRow(System.Random rng) => Mathf.Lerp(0.03f, 0.45f, (float)rng.NextDouble());

    static Mesh Tree(int seed, float H)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var mb = new MB();
        float R0 = H * 0.055f, hs = H * R(0.36f, 0.46f);
        var lean = new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)).normalized * H * R(0.02f, 0.07f);

        var trunk = new List<Vector3>(); var tr = new List<float>();
        for (int i = 0; i <= 8; i++)
        {
            float t = i / 8f;
            trunk.Add(Vector3.up * (-0.6f + (hs + 0.6f) * t) + lean * t * t + new Vector3(Mathf.Sin(t * 5f + seed), 0f, Mathf.Cos(t * 4f + seed)) * R0 * 0.08f);
            tr.Add(R0 * Mathf.Lerp(1f, 0.62f, t));
        }
        float ribOff = R(0f, 6.28f);
        Tube(mb, 0, trunk, tr, 12, 2.2f, (i, ang) =>
        {
            float t = i / 8f;
            return t > 0.3f ? 1f : 1f + 1.5f * Mathf.Pow(1f - t / 0.3f, 2f) * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(5f * (ang + ribOff))), 3f);
        });
        Vector3 top = trunk[8];

        int roots = rng.Next(4, 7);
        for (int k = 0; k < roots; k++)
        {
            float yaw = k * Mathf.PI * 2f / roots + R(-0.3f, 0.3f), reach = R(3.4f, 4.6f);
            var d = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            Tube(mb, 0, new[] { d * R0 * 0.5f + Vector3.up * R0 * 1.6f, d * R0 * 1.6f + Vector3.up * R0 * 0.6f, d * R0 * 2.7f + Vector3.up * 0.05f, d * R0 * reach + Vector3.down * 0.4f },
                 new[] { R0 * 0.45f, R0 * 0.32f, R0 * 0.2f, R0 * 0.05f }, 7, 2.2f);
        }

        int branches = rng.Next(2, 5);
        for (int b = 0; b < branches; b++)
        {
            float yaw = b * Mathf.PI * 2f / branches + R(-0.4f, 0.4f), rise = R(0.65f, 1.15f), L = H * R(0.38f, 0.52f);
            var h = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            var side = Vector3.Cross(Vector3.up, h);
            var path = new List<Vector3>(); var br = new List<float>();
            for (int i = 0; i <= 6; i++)
            {
                float u = i / 6f;
                path.Add(top + h * L * 0.8f * u + Vector3.up * L * (0.9f * u - 0.35f * u * u) * rise);
                br.Add(R0 * Mathf.Lerp(0.55f, 0.14f, u));
            }
            Tube(mb, 0, path, br, 8, 2.2f);
            Vector3 end = path[6];
            int pads = rng.Next(2, 4);
            for (int p = 0; p < pads; p++)
            {
                float rp = H * R(0.10f, 0.15f);
                var c = end + h * H * R(-0.03f, 0.07f) + side * H * R(-0.07f, 0.07f) + Vector3.up * H * R(0.0f, 0.06f) * p;
                Pad(mb, 1, c, rp, rp * R(0.22f, 0.32f), FoliageRow(rng), rng);
            }
            // a twig off the middle of the branch, with its own pad
            var mid = path[3];
            var twigEnd = mid + (h * 0.4f + side * R(-0.8f, 0.8f) + Vector3.up * 0.7f).normalized * L * 0.4f;
            Tube(mb, 0, new[] { mid, Vector3.Lerp(mid, twigEnd, 0.5f) + Vector3.up * 0.3f, twigEnd }, new[] { br[3] * 0.6f, br[3] * 0.4f, br[3] * 0.2f }, 6, 2.2f);
            float rt = H * R(0.07f, 0.1f);
            Pad(mb, 1, twigEnd + Vector3.up * 0.2f, rt, rt * 0.3f, FoliageRow(rng), rng);
        }
        return mb.ToMesh(true);
    }

    static Mesh Single(MB mb)
    {
        var m = mb.ToMesh(false);
        m.subMeshCount = 1;
        m.SetTriangles(mb.tris[0], 0);
        m.RecalculateBounds();
        return m;
    }

    static Mesh Fern(int seed)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var mb = new MB();
        int fronds = rng.Next(9, 15);
        float row = R(0.5f, 0.6f);
        for (int f = 0; f < fronds; f++)
        {
            float yaw = f * Mathf.PI * 2f / fronds + R(-0.25f, 0.25f), el = R(30f, 65f) * Mathf.Deg2Rad, L = R(0.8f, 1.5f);
            var h = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            var side = Vector3.Cross(Vector3.up, h);
            const int segs = 8;
            Vector3 P(float u) => h * L * u * Mathf.Cos(el) + Vector3.up * (L * u * Mathf.Sin(el) - L * 0.55f * u * u);
            for (int s = 0; s < segs; s++)
            {
                float u0 = s / (float)segs, u1 = (s + 1) / (float)segs;
                float W(float u) => 0.17f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Min(u, 0.97f)), 0.7f) * (0.75f + 0.5f * Mathf.Abs(Mathf.Sin(u * 30f)));
                Vector3 p0 = P(u0), p1 = P(u1);
                var nrm = Vector3.Cross(p1 - p0, side).normalized; if (nrm.y < 0f) nrm = -nrm;
                for (int face = -1; face <= 1; face += 2)
                {
                    var nn = nrm * face;
                    float sh0 = 0.35f + 0.6f * u0, sh1 = 0.35f + 0.6f * u1;
                    int a = mb.Add(p0 - side * W(u0), nn, new Vector2(sh0, row)), b = mb.Add(p0 + side * W(u0), nn, new Vector2(sh0, row));
                    int c = mb.Add(p1 - side * W(u1), nn, new Vector2(sh1, row)), d = mb.Add(p1 + side * W(u1), nn, new Vector2(sh1, row));
                    mb.Tri(0, a, c, b, nn); mb.Tri(0, b, c, d, nn);
                }
            }
        }
        return Single(mb);
    }

    static Mesh Bush(int seed)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var mb = new MB();
        int blobs = rng.Next(4, 8);
        for (int i = 0; i < blobs; i++)
        {
            float r = R(0.5f, 1.0f), a = R(0f, 6.28f), d = R(0f, 0.8f);
            Pad(mb, 0, new Vector3(Mathf.Cos(a) * d, r * 0.55f + R(0f, 0.4f), Mathf.Sin(a) * d), r, r * 0.7f, R(0.89f, 0.98f), rng, 9, 0.12f, 0.25f);
        }
        return Single(mb);
    }

    static Mesh Mushrooms(int seed)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var mb = new MB();
        int n = rng.Next(1, 4);
        for (int i = 0; i < n; i++)
        {
            float a = R(0f, 6.28f), d = i == 0 ? 0f : R(0.3f, 0.6f), hgt = R(0.35f, 0.75f), r = hgt * R(0.5f, 0.8f);
            var b = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            var top = b + new Vector3(R(-0.06f, 0.06f), hgt, R(-0.06f, 0.06f));
            Tube(mb, 0, new[] { b, Vector3.Lerp(b, top, 0.5f), top }, new[] { r * 0.32f, r * 0.24f, r * 0.2f }, 7, 1f);
            for (int k = mb.uv.Count - 24; k < mb.uv.Count; k++) mb.uv[k] = new Vector2(0.4f + 0.5f * mb.n[k].y + 0.3f, R(0.77f, 0.85f));
            Pad(mb, 0, top, r, r * 0.45f, R(0.64f, 0.73f), rng, 5, 0.06f, 0.5f);
        }
        return Single(mb);
    }

    /// <summary>The arena's glossy purple orbs, in twos and threes.</summary>
    static Mesh Orbs(int seed)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var mb = new MB();
        int n = rng.Next(2, 4);
        for (int i = 0; i < n; i++)
        {
            float r = R(0.35f, 0.6f), a = i * 2.3f;
            Pad(mb, 0, new Vector3(Mathf.Cos(a) * r * 0.9f * (i > 0 ? 1 : 0), r * 0.85f, Mathf.Sin(a) * r * 0.9f * (i > 0 ? 1 : 0)), r, r * 0.95f, R(0.64f, 0.73f), rng, 3, 0.03f, 0f);
        }
        return Single(mb);
    }

    static Mesh Log(int seed)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        var mb = new MB();
        float L = R(4f, 7f), r = R(0.4f, 0.6f), bend = R(-0.3f, 0.3f);
        var path = new List<Vector3>(); var rad = new List<float>();
        for (int i = 0; i <= 6; i++)
        {
            float u = i / 6f;
            path.Add(new Vector3((u - 0.5f) * L, r * 0.75f, Mathf.Sin(u * Mathf.PI) * bend));
            rad.Add(r * Mathf.Lerp(1f, 0.75f, u));
        }
        Tube(mb, 0, path, rad, 10, 1.4f, (i, ang) => 1f + 0.06f * Mathf.Sin(ang * 5f + i));
        // ends: flat discs
        for (int e = 0; e < 2; e++)
        {
            var c = path[e == 0 ? 0 : 6]; float rr = rad[e == 0 ? 0 : 6];
            var nrm = e == 0 ? Vector3.left : Vector3.right;
            int ci = mb.Add(c, nrm, new Vector2(0.5f, 0.5f));
            for (int k = 0; k < 10; k++)
            {
                float a0 = k / 10f * Mathf.PI * 2f, a1 = (k + 1) / 10f * Mathf.PI * 2f;
                int p = mb.Add(c + new Vector3(0f, Mathf.Cos(a0), Mathf.Sin(a0)) * rr, nrm, new Vector2(0.5f + 0.2f * Mathf.Cos(a0), 0.5f));
                int q = mb.Add(c + new Vector3(0f, Mathf.Cos(a1), Mathf.Sin(a1)) * rr, nrm, new Vector2(0.5f + 0.2f * Mathf.Cos(a1), 0.5f));
                mb.Tri(0, ci, p, q, nrm);
            }
        }
        // a broken branch stub
        var at = path[rng.Next(2, 5)];
        Tube(mb, 0, new[] { at, at + new Vector3(R(-0.3f, 0.3f), r * 1.4f, R(0.2f, 0.5f)) }, new[] { r * 0.35f, r * 0.18f }, 6, 1.4f);
        return Single(mb);
    }

    // ---------------------------------------------------------------- textures

    delegate Color[] Gen(int w, int h, out float[] height);
    static Texture2D lastNormal;
    static Texture2D TexN => lastNormal;

    /// <summary>Writes an albedo and its normal map; the normal is handed back through <see cref="TexN"/>.</summary>
    static Texture2D Tex(string name, int w, int h, Gen gen, float bump)
    {
        var px = gen(w, h, out var height);
        var albedo = D.SaveTex(name, w, h, px, false, false, Dir);
        lastNormal = D.SaveTex(name + "_N", w, h, D.NormalFromHeight(height, w, h, bump), true, false, Dir);
        return albedo;
    }

    static Color[] GroundAlbedo(int w, int h, out float[] height)
    {
        var px = new Color[w * h]; height = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float big = D.TFbm(u, v, 6, 6, 4, 31), fine = D.TFbm(u, v, 48, 48, 3, 37);
                float litter = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.62f, big));
                var mossC = Color.Lerp(new Color(0.15f, 0.21f, 0.07f), new Color(0.30f, 0.38f, 0.12f), fine);
                var leafC = Color.Lerp(new Color(0.21f, 0.15f, 0.09f), new Color(0.37f, 0.27f, 0.15f), fine);
                var c = Color.Lerp(mossC, leafC, litter * 0.75f);
                if (D.Hash(x, y, 41) > 0.992f) c = Color.Lerp(c, new Color(0.45f, 0.36f, 0.18f), 0.6f);
                c.a = 1f;
                px[y * w + x] = c;
                height[y * w + x] = fine * 0.6f + litter * 0.4f;
            }
        return px;
    }

    static Color[] BarkAlbedo(int w, int h, out float[] height)
    {
        var px = new Color[w * h]; height = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float streak = D.TFbm(u, v, 16, 3, 4, 51), knot = D.TFbm(u, v, 4, 8, 3, 53);
                var c = Color.Lerp(new Color(0.17f, 0.12f, 0.09f), new Color(0.33f, 0.24f, 0.17f), streak * 0.8f + knot * 0.2f);
                c.a = 1f;
                px[y * w + x] = c;
                height[y * w + x] = streak;
            }
        return px;
    }

    static Color[] MossAlbedo(int w, int h, out float[] height)
    {
        var px = new Color[w * h]; height = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float stone = D.TFbm(u, v, 8, 8, 5, 61), patch = D.TFbm(u, v, 5, 5, 4, 67);
                var sC = Color.Lerp(new Color(0.29f, 0.28f, 0.24f), new Color(0.46f, 0.44f, 0.38f), stone);
                var mC = Color.Lerp(new Color(0.18f, 0.26f, 0.08f), new Color(0.33f, 0.42f, 0.14f), stone);
                float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.56f, patch));
                var c = Color.Lerp(sC, mC, m);
                c.a = 1f;
                px[y * w + x] = c;
                height[y * w + x] = stone + m * 0.3f;
            }
        return px;
    }

    static Color[] CanopyAlbedo(int w, int h, out float[] height)
    {
        var px = new Color[w * h]; height = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float clump = D.TFbm(u, v, 20, 20, 4, 71), tint = D.TFbm(u, v, 3, 3, 3, 73);
                var c = Color.Lerp(new Color(0.06f, 0.08f, 0.03f), new Color(0.26f, 0.34f, 0.12f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.72f, clump)));
                c = Color.Lerp(c, c * new Color(1.15f, 1.05f, 0.8f), tint);
                c.a = 1f;
                px[y * w + x] = c;
                height[y * w + x] = clump;
            }
        return px;
    }

    static Color[] Palette()
    {
        const int N = 64;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
        {
            float v = (y + 0.5f) / N;
            Color dark, light;
            if (v < 0.48f)
            {
                float k = Mathf.Sin(v * 37f) * 0.5f + 0.5f;   // a little hue variety from row to row
                dark = Color.Lerp(new Color(0.06f, 0.10f, 0.04f), new Color(0.09f, 0.11f, 0.04f), k);
                light = Color.Lerp(new Color(0.27f, 0.38f, 0.12f), new Color(0.34f, 0.40f, 0.14f), k);
            }
            else if (v < 0.62f) { dark = new Color(0.13f, 0.22f, 0.06f); light = new Color(0.42f, 0.58f, 0.17f); }
            else if (v < 0.75f) { dark = new Color(0.16f, 0.06f, 0.19f); light = new Color(0.52f, 0.24f, 0.50f); }
            else if (v < 0.87f) { dark = new Color(0.50f, 0.46f, 0.36f); light = new Color(0.86f, 0.80f, 0.66f); }
            else { dark = new Color(0.07f, 0.11f, 0.04f); light = new Color(0.27f, 0.34f, 0.12f); }
            for (int x = 0; x < N; x++)
            {
                var c = Color.Lerp(dark, light, (x + 0.5f) / N);
                c.a = 1f;
                px[y * N + x] = c;
            }
        }
        return px;
    }
}
#endif
