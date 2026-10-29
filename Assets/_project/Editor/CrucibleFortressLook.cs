#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// The Medieval Fortress's sky and light, and nothing else: the arena mesh, its material, bounds,
/// marks and collision are not touched, and nothing is added to the scene but lights, a volume and
/// the <c>ArenaAtmosphere</c> that swaps them in.
///
/// - A generated partly-cloudy panorama (soft blue gradient, white and light-grey clouds lit from
///   the sun's side, a warm glow where the sun actually is) on <c>Skybox/Panoramic</c>.
/// - A late-afternoon sun high enough that the 8-13 unit walls do not throw the whole floor into
///   shadow, soft shadows not quite black, a blue sky fill, and Trilight ambient with a bright
///   equator so the arcades read instead of going black.
/// - A reflection cubemap rendered from the new sky (the one baked piece - see the note on
///   lightmaps in Build), light blue-grey haze, and a grade that takes the orange out of the stone.
///
/// All of it lives on <c>MedievalFortress_Atmosphere</c>, the Fortress entry's sharedRoot, so the
/// other arenas keep the scene's own lighting.
/// </summary>
public static class CrucibleFortressLook
{
    const string Dir = "Assets/_project/Environment/FortressLook";
    const string RootName = "MedievalFortress_Atmosphere";
    const string ArenaRoot = "MedievalFortress";

    // Golden hour from the west-south-west: across the default camera, so the floor, the arches and
    // the fighters get modelling instead of flat front light. 30 deg is as low as it goes before the
    // 8-13 unit walls shadow most of the floor.
    static readonly Vector3 SunEuler = new Vector3(30f, 68f, 0f);

    [MenuItem("Crucible/Arena/Fortress/Build the sky and lighting")]
    public static void Build()
    {
        // Lightmaps were considered and left out on purpose: the six arenas share one scene and a
        // scene holds ONE lighting bake, so a Fortress bake would be overwritten by (or overwrite)
        // any other arena's, and the 600k-triangle Tripo mesh has no lightmap UVs - adding them is
        // a reimport of the arena itself. One shadowed sun plus an unshadowed fill is what the scene
        // already ran; the reflection cubemap is baked.
        var arena = CrucibleDesertUpgrade.FindRoot(ArenaRoot);
        if (arena == null) { Debug.LogError("Fortress look: no MedievalFortress root in the open scene."); return; }
        Directory.CreateDirectory(Dir);

        foreach (var go in arena.scene.GetRootGameObjects())
            if (go.name == RootName) Object.DestroyImmediate(go);

        Quaternion sunRot = Quaternion.Euler(SunEuler);
        Vector3 toSun = -(sunRot * Vector3.forward);
        var sky = SkyMaterial(toSun);
        var reflection = BakeReflection(sky);

        var root = new GameObject(RootName);
        var lighting = new GameObject("Lighting");
        lighting.transform.SetParent(root.transform, false);

        var sunGo = new GameObject("Sun");
        sunGo.transform.SetParent(lighting.transform, false);
        sunGo.transform.rotation = sunRot;
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1.00f, 0.80f, 0.57f);
        sun.intensity = 1.95f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;   // the sky fills shadows; never pure black

        var fillGo = new GameObject("Sky Fill");
        fillGo.transform.SetParent(lighting.transform, false);
        fillGo.transform.rotation = Quaternion.Euler(50f, 250f, 0f);
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.62f, 0.72f, 0.92f);
        fill.intensity = 0.32f;
        fill.shadows = LightShadows.None;

        var lookGo = new GameObject("Fortress Look");
        lookGo.transform.SetParent(root.transform, false);
        var vol = lookGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10;
        vol.sharedProfile = FortressGrade(CrucibleDesertUpgrade.CloneProfile("Assets/_project/Settings/CrucibleLookURP.asset", Dir + "/Fortress_Look.asset"));

        var atm = root.AddComponent(CrucibleDesertUpgrade.GameType("ArenaAtmosphere"));
        var so = new SerializedObject(atm);
        so.FindProperty("skybox").objectReferenceValue = sky;
        so.FindProperty("sun").objectReferenceValue = sun;
        so.FindProperty("ambientMode").intValue = (int)AmbientMode.Trilight;
        so.FindProperty("ambientSky").colorValue = new Color(0.52f, 0.60f, 0.72f);
        so.FindProperty("ambientEquator").colorValue = new Color(0.64f, 0.57f, 0.47f);   // walls and arches, warmed by the low sun
        so.FindProperty("ambientGround").colorValue = new Color(0.44f, 0.35f, 0.26f);
        so.FindProperty("reflection").objectReferenceValue = reflection;
        so.FindProperty("reflectionIntensity").floatValue = 0.8f;
        so.FindProperty("fog").boolValue = true;
        so.FindProperty("fogMode").intValue = (int)FogMode.Linear;
        so.FindProperty("fogColor").colorValue = new Color(0.84f, 0.76f, 0.62f);
        so.FindProperty("fogStart").floatValue = 45f;
        so.FindProperty("fogEnd").floatValue = 420f;
        so.ApplyModifiedPropertiesWithoutUndo();

        CrucibleDesertUpgrade.SetSharedRoot(ArenaRoot, RootName);
        root.SetActive(false);   // ArenaDirectory switches it on with the Fortress
        EditorSceneManager.MarkSceneDirty(arena.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Fortress look: sky and lighting built (save the scene to keep them).");
    }

    [MenuItem("Crucible/Arena/Fortress/Remove the sky and lighting")]
    public static void Remove()
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == RootName) { EditorSceneManager.MarkSceneDirty(go.scene); Object.DestroyImmediate(go); }
        CrucibleDesertUpgrade.SetSharedRoot(ArenaRoot, "");
    }

    static VolumeProfile FortressGrade(VolumeProfile p)
    {
        foreach (var c in p.components)
        {
            if (c is ColorAdjustments ca)
            {
                ca.postExposure.Override(0.0f);
                ca.contrast.Override(13f);
                ca.saturation.Override(-5f);
                ca.colorFilter.Override(new Color(1.00f, 0.99f, 0.98f));
            }
            if (c is LiftGammaGain lgg)
            {
                lgg.lift.Override(new Vector4(0.99f, 1.00f, 1.02f, 0.02f));   // cool, slightly lifted shadows: depth without black
                lgg.gain.Override(new Vector4(1.00f, 1.00f, 0.99f, 0f));
            }
            if (c is Bloom b) b.intensity.Override(0.12f);
            if (c is FilmGrain g) g.intensity.Override(0.15f);
        }
        if (!p.TryGet<WhiteBalance>(out var wb))
        {
            wb = p.Add<WhiteBalance>(true);
            wb.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(wb, p);
        }
        wb.temperature.Override(-2f);   // golden, but the sandstone is warm already - a full warm grade on it went orange
        EditorUtility.SetDirty(p);
        return p;
    }

    // ---------------------------------------------------------------- sky

    static Material SkyMaterial(Vector3 toSun)
    {
        const int W = 4096, H = 2048;
        var px = new Color32[W * H];
        Vector2 sunPlane = new Vector2(toSun.x, toSun.z).normalized;
        Color zenith = new Color(0.25f, 0.42f, 0.72f), horizon = new Color(0.84f, 0.79f, 0.70f);
        Color haze = new Color(0.86f, 0.78f, 0.64f), ground = new Color(0.42f, 0.37f, 0.30f);
        Color warm = new Color(1.00f, 0.74f, 0.45f);
        float discCos = Mathf.Cos(0.7f * Mathf.Deg2Rad);

        for (int y = 0; y < H; y++)
        {
            // Skybox/Panoramic: v = 1 - latitude/pi, u = 0.5 - atan2(z, x)/(2 pi)
            float lat = (1f - (y + 0.5f) / H) * Mathf.PI;
            float sinLat = Mathf.Sin(lat), cosLat = Mathf.Cos(lat);
            for (int x = 0; x < W; x++)
            {
                float lon = (0.5f - (x + 0.5f) / W) * Mathf.PI * 2f;
                var dir = new Vector3(sinLat * Mathf.Cos(lon), cosLat, sinLat * Mathf.Sin(lon));
                Color c;
                if (dir.y < 0f)
                {
                    c = Color.Lerp(haze, ground, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-dir.y / 0.25f)));
                }
                else
                {
                    float t = dir.y;
                    float cosA = Vector3.Dot(dir, toSun);
                    float az = Vector2.Dot(new Vector2(dir.x, dir.z).normalized, sunPlane);
                    c = Color.Lerp(horizon, zenith, Mathf.Pow(t, 0.55f));
                    c += warm * (0.32f * Mathf.Pow(Mathf.Max(0f, az), 2f) * Mathf.Pow(1f - t, 3f));
                    c += warm * (0.40f * Mathf.Pow(Mathf.Max(0f, cosA), 6f));

                    // clouds on a layer overhead: flattened toward the horizon by perspective
                    if (t > 0.005f)
                    {
                        float k = 1f / (t + 0.12f);
                        Vector2 q = new Vector2(dir.x * k, dir.z * k) * 0.9f;
                        float warp = Fbm(q * 0.5f + new Vector2(31.7f, 11.3f), 3);
                        q += new Vector2(warp, warp * 0.7f) * 0.35f;
                        float dens = Fbm(q, 6);
                        float cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.72f, dens));
                        if (cover > 0.001f)
                        {
                            float towardSun = Fbm(q + sunPlane * 0.06f, 6);
                            float lit = Mathf.Clamp01(0.68f + (dens - towardSun) * 5f);
                            Color cloud = Color.Lerp(new Color(0.58f, 0.55f, 0.60f), new Color(1.00f, 0.90f, 0.76f), lit);
                            cloud *= Mathf.Lerp(1f, 0.86f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.9f, dens)));
                            cloud += warm * (0.45f * Mathf.Pow(Mathf.Max(0f, cosA), 8f) * (1f - cover) + 0.18f * Mathf.Pow(Mathf.Max(0f, az), 2f));   // silver lining near the sun
                            cover *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.06f));
                            c = Color.Lerp(c, cloud, cover * 0.95f);
                        }
                    }
                    c = Color.Lerp(c, haze, Mathf.Pow(1f - t, 7f) * 0.6f);
                    if (cosA > discCos) c = new Color(1f, 0.93f, 0.78f);
                }
                c.a = 1f;
                px[y * W + x] = c;
            }
        }

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        string path = Dir + "/Fortress_Sky.jpg";
        File.WriteAllBytes(path, tex.EncodeToJPG(95));
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.mipmapEnabled = false;   // mips put a seam down the panorama's wrap
        imp.wrapModeU = TextureWrapMode.Repeat;
        imp.wrapModeV = TextureWrapMode.Clamp;
        imp.maxTextureSize = 4096;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();

        string matPath = Dir + "/Fortress_Sky.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(mat, matPath); }
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        mat.SetFloat("_Mapping", 1f);     // latitude-longitude
        mat.SetFloat("_ImageType", 0f);   // 360
        mat.SetFloat("_Exposure", 1.0f);
        mat.SetFloat("_Rotation", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static float Fbm(Vector2 p, int oct)
    {
        float a = 1f, s = 0f, n = 0f, f = 1f;
        for (int i = 0; i < oct; i++)
        {
            s += a * Mathf.PerlinNoise(p.x * f + 500f + i * 13.7f, p.y * f + 500f + i * 7.9f);
            n += a; a *= 0.5f; f *= 2.03f;
        }
        return s / n;
    }

    /// <summary>The new sky rendered into a small cubemap for the stone's reflections and specular.</summary>
    static Cubemap BakeReflection(Material sky)
    {
        string path = Dir + "/Fortress_Reflection.asset";
        var prevSky = RenderSettings.skybox;
        var go = new GameObject("_fortress_reflection_cam");
        try
        {
            RenderSettings.skybox = sky;
            var cam = go.AddComponent<Camera>();
            cam.cullingMask = 0;   // the sky alone
            cam.clearFlags = CameraClearFlags.Skybox;
            go.transform.position = new Vector3(0f, 10f, 0f);
            var cube = new Cubemap(128, TextureFormat.RGBAHalf, true);
            cam.RenderToCubemap(cube);
            cube.Apply(true);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(cube, path);
            return cube;
        }
        finally
        {
            RenderSettings.skybox = prevSky;
            Object.DestroyImmediate(go);
        }
    }
}
#endif
