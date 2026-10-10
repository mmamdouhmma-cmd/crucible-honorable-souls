// Crucible > Look > Apply HDRP Trailer Look
// One-shot editor command that moves the project to HDRP and lays down the trailer's look:
// pipeline asset, character materials, sun + fill, warm dust fog, physically based sky, TAA and
// a global Volume with the grade. Everything is cosmetic; no gameplay object is touched.
// Compiles only when the HDRP package is present (HDRP_PRESENT from the asmdef version define).
#if HDRP_PRESENT
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class CrucibleLookSetup
{
    private const string SettingsDir = "Assets/_project/Settings";
    private const string PipelineAssetPath = SettingsDir + "/HDRP_PC.asset";
    private const string ProfilePath = SettingsDir + "/CrucibleLook.asset";
    private const string VolumeObjectName = "Crucible Look Volume";

    // Scene units are ~4x metres (the fighters are 8 units tall), so distances below are scaled.
    private const float U = 4f;

    [MenuItem("Crucible/Look/1. Switch project to HDRP")]
    public static void SwitchToHdrp()
    {
        HDRenderPipelineAsset asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(PipelineAssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            AssetDatabase.CreateAsset(asset, PipelineAssetPath);
        }

        // Features the look needs. Set through the serialized object because the settings struct is internal.
        SerializedObject so = new SerializedObject(asset);
        SetBool(so, "m_RenderPipelineSettings.supportSSR", true);
        SetBool(so, "m_RenderPipelineSettings.supportSSRTransparent", true);
        SetBool(so, "m_RenderPipelineSettings.supportSSAO", true);
        SetBool(so, "m_RenderPipelineSettings.supportVolumetrics", true);
        SetBool(so, "m_RenderPipelineSettings.supportMotionVectors", true);
        SetBool(so, "m_RenderPipelineSettings.supportSubsurfaceScattering", true);
        SetBool(so, "m_RenderPipelineSettings.supportShadowMask", true);
        SetBool(so, "m_RenderPipelineSettings.supportDecals", true);
        SetBool(so, "m_RenderPipelineSettings.supportLightLayers", true);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);

        GraphicsSettings.defaultRenderPipeline = asset;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
        QualitySettings.SetQualityLevel(current, false);

        PlayerSettings.colorSpace = ColorSpace.Linear;
        AssetDatabase.SaveAssets();
        Debug.Log("Crucible: HDRP asset assigned (Graphics + every quality level). If the HDRP Wizard reports " +
                  "anything, open Window > Rendering > HDRP Wizard and press Fix All.");
    }

    [MenuItem("Crucible/Look/2. Convert character materials to HDRP Lit")]
    public static void ConvertMaterials()
    {
        Shader lit = Shader.Find("HDRP/Lit");
        if (lit == null) { Debug.LogError("Crucible: HDRP/Lit shader not found. Run step 1 first."); return; }

        int converted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_project/Characters" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == lit) continue;
            if (!mat.shader.name.StartsWith("Universal Render Pipeline")) continue;

            Texture baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
            Texture normal = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            Color baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;

            mat.shader = lit;
            mat.SetColor("_BaseColor", baseColor);
            if (baseMap != null) mat.SetTexture("_BaseColorMap", baseMap);
            if (normal != null)
            {
                mat.SetTexture("_NormalMap", normal);
                mat.SetFloat("_NormalScale", 1f);
            }

            // Packed mask map (R metallic, G AO, B detail, A smoothness) written by prep_character.py next to the textures.
            Texture2D mask = FindMaskMap(path);
            if (mask != null)
            {
                mat.SetTexture("_MaskMap", mask);
                mat.SetFloat("_Metallic", 1f);
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_SmoothnessRemapMin", 0.05f);
                mat.SetFloat("_SmoothnessRemapMax", 0.75f);
                mat.SetFloat("_AORemapMin", 0.2f);
            }
            else
            {
                mat.SetFloat("_Metallic", 0.1f);
                mat.SetFloat("_Smoothness", 0.35f);
            }
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            converted++;
            Debug.Log($"Crucible: {path} -> HDRP/Lit (mask map: {(mask != null ? mask.name : "none")})");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Crucible: {converted} material(s) converted.");
    }

    [MenuItem("Crucible/Look/3. Apply trailer lighting, sky, fog and grade to the open scene")]
    public static void ApplySceneLook()
    {
        Camera cam = Camera.main;
        if (cam == null) { Debug.LogError("Crucible: no Main Camera in the open scene."); return; }

        // --- camera: TAA, physical-ish exposure handled by the volume
        HDAdditionalCameraData camData = cam.GetComponent<HDAdditionalCameraData>();
        if (camData == null) camData = cam.gameObject.AddComponent<HDAdditionalCameraData>();
        // SMAA, not TAA: temporal AA ghosts and smears fast motion when the frame rate dips,
        // which read as "Matrix" trails on the fighters. SMAA is per-frame and stays crisp.
        camData.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camData.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High;
        camData.dithering = true;
        camData.stopNaNs = true;
        camData.volumeLayerMask = ~0;
        camData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
        cam.allowHDR = true;

        // --- sun: warm, low, from behind the fighters (relative to the camera) so they rim-light
        Light sun = null;
        foreach (Light l in Object.FindObjectsOfType<Light>())
        {
            if (l.type == LightType.Directional && l.name != "Fill Light") { sun = l; break; }
        }
        if (sun == null)
        {
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.name = "Sun";
        Vector3 camFwd = cam.transform.forward; camFwd.y = 0f; camFwd.Normalize();
        // Light travels toward the camera, offset 35 degrees to one side, and DOWN 16 degrees
        // (the sun sits 16 degrees above the horizon behind the fighters). Rotating about
        // Cross(up, dir) by a positive angle tilts the direction downward.
        Vector3 sunDir = Quaternion.AngleAxis(35f, Vector3.up) * (-camFwd);
        sunDir = (Quaternion.AngleAxis(66f, Vector3.Cross(Vector3.up, sunDir)) * sunDir).normalized;   // high noon-ish
        if (sunDir.y > 0f) sunDir.y = -sunDir.y;   // never let the sun end up below the horizon
        sun.transform.rotation = Quaternion.LookRotation(sunDir, Vector3.up);
        sun.color = new Color(1.0f, 0.96f, 0.88f);     // noon: near-white, a hint of warmth
        sun.shadows = LightShadows.Soft;
        HDAdditionalLightData sunData = sun.GetComponent<HDAdditionalLightData>();
        if (sunData == null) sunData = sun.gameObject.AddComponent<HDAdditionalLightData>();
        sunData.SetIntensity(100000f, LightUnit.Lux);  // noon sun; the physically based sky scales with it
        sunData.angularDiameter = 1.2f;
        sunData.EnableShadows(true);
        sunData.SetShadowResolutionOverride(true);
        sunData.SetShadowResolution(2048);
        sunData.shadowDimmer = 1f;
        sunData.volumetricDimmer = 1f;
        sunData.useContactShadow.useOverride = true;
        sunData.useContactShadow.@override = true;

        // --- fill: cool, from the camera side, no shadows, lifts the shadow side like the trailer's bounce
        Light fill = GameObject.Find("Fill Light")?.GetComponent<Light>();
        if (fill == null)
        {
            fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional;
        }
        Vector3 fillDir = Quaternion.AngleAxis(-30f, Vector3.up) * camFwd;
        fillDir = (Quaternion.AngleAxis(40f, Vector3.Cross(Vector3.up, fillDir)) * fillDir).normalized;
        if (fillDir.y > 0f) fillDir.y = -fillDir.y;
        fill.transform.rotation = Quaternion.LookRotation(fillDir, Vector3.up);
        fill.color = new Color(0.62f, 0.72f, 0.95f);
        fill.shadows = LightShadows.None;
        HDAdditionalLightData fillData = fill.GetComponent<HDAdditionalLightData>();
        if (fillData == null) fillData = fill.gameObject.AddComponent<HDAdditionalLightData>();
        fillData.SetIntensity(12000f, LightUnit.Lux);  // sky bounce: keeps noon shadows open, not black
        fillData.EnableShadows(false);
        fillData.affectSpecular = false;

        // --- global volume with the grade
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        FillProfile(profile);
        EditorUtility.SetDirty(profile);

        GameObject volGo = GameObject.Find(VolumeObjectName);
        if (volGo == null) volGo = new GameObject(VolumeObjectName);
        Volume vol = volGo.GetComponent<Volume>();
        if (vol == null) vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        vol.sharedProfile = profile;

        // Renderers still on a URP shader (the ground uses URP's package default material, which
        // cannot be converted in place) get a sandy HDRP arena material instead.
        Material ground = AssetDatabase.LoadAssetAtPath<Material>(SettingsDir + "/ArenaGround.mat");
        if (ground == null)
        {
            ground = new Material(Shader.Find("HDRP/Lit"));
            ground.SetColor("_BaseColor", new Color(0.62f, 0.52f, 0.38f));
            ground.SetFloat("_Metallic", 0f);
            ground.SetFloat("_Smoothness", 0.12f);
            HDMaterial.ValidateMaterial(ground);
            AssetDatabase.CreateAsset(ground, SettingsDir + "/ArenaGround.mat");
        }
        int reassigned = 0;
        foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || m.shader == null) continue;
                string s = m.shader.name;
                if (s.StartsWith("Universal Render Pipeline") || s == "Standard" || s.StartsWith("Legacy Shaders"))
                {
                    mats[i] = ground;
                    changed = true;
                }
            }
            if (changed)
            {
                r.sharedMaterials = mats;
                reassigned++;
                Debug.Log($"Crucible: '{r.gameObject.name}' was on a URP/built-in material, now ArenaGround (HDRP/Lit).");
            }
        }

        // Old URP volumes in the scene would show missing components under HDRP: retire them.
        foreach (Volume v in Object.FindObjectsOfType<Volume>())
        {
            if (v != vol && v.sharedProfile != null && v.sharedProfile != profile)
            {
                v.enabled = false;
                Debug.Log($"Crucible: disabled old volume on '{v.gameObject.name}' (profile {v.sharedProfile.name}).");
            }
        }

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"Crucible: scene look applied ({profile.components.Count} volume components saved). Save the scene (Ctrl+S).");
        Diagnose();
    }

    [MenuItem("Crucible/Look/Run all three steps")]
    public static void RunAll()
    {
        SwitchToHdrp();
        ConvertMaterials();
        ApplySceneLook();
    }

    // ---------------------------------------------------------------- quality tiers
    private const string LaptopAssetPath = SettingsDir + "/HDRP_Laptop.asset";

    [MenuItem("Crucible/Look/Quality: Laptop (integrated GPU, half-res upscaled)")]
    public static void UseLaptopQuality()
    {
        HDRenderPipelineAsset pc = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(PipelineAssetPath);
        if (pc == null) { Debug.LogError("Crucible: run step 1 first."); return; }

        HDRenderPipelineAsset laptop = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(LaptopAssetPath);
        if (laptop == null)
        {
            laptop = Object.Instantiate(pc);
            AssetDatabase.CreateAsset(laptop, LaptopAssetPath);
        }
        SerializedObject so = new SerializedObject(laptop);
        // Render at 55% and upscale with FSR: the single biggest win on an integrated GPU.
        SetBool(so, "m_RenderPipelineSettings.dynamicResolutionSettings.enabled", true);
        SetFloat(so, "m_RenderPipelineSettings.dynamicResolutionSettings.minPercentage", 55f);
        SetFloat(so, "m_RenderPipelineSettings.dynamicResolutionSettings.maxPercentage", 55f);
        SetBool(so, "m_RenderPipelineSettings.dynamicResolutionSettings.forceResolution", true);
        SetFloat(so, "m_RenderPipelineSettings.dynamicResolutionSettings.forcedPercentage", 55f);
        SetInt(so, "m_RenderPipelineSettings.dynamicResolutionSettings.upsampleFilter", (int)DynamicResUpscaleFilter.EdgeAdaptiveScalingUpres);
        SetBool(so, "m_RenderPipelineSettings.dynamicResolutionSettings.useMipBias", true);
        // Features an integrated GPU should not pay for.
        SetBool(so, "m_RenderPipelineSettings.supportSSR", false);
        SetBool(so, "m_RenderPipelineSettings.supportSSRTransparent", false);
        SetBool(so, "m_RenderPipelineSettings.supportVolumetrics", false);
        SetBool(so, "m_RenderPipelineSettings.supportSubsurfaceScattering", false);
        SetBool(so, "m_RenderPipelineSettings.supportLightLayers", false);
        SetBool(so, "m_RenderPipelineSettings.supportDecals", false);
        SetBool(so, "m_RenderPipelineSettings.supportMotionVectors", false);
        SetInt(so, "m_RenderPipelineSettings.hdShadowInitParams.directionalShadowsDepthBits", 16);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(laptop);

        int level = EnsureQualityLevel("Laptop");
        QualitySettings.SetQualityLevel(level, true);
        QualitySettings.renderPipeline = laptop;
        QualitySettings.vSyncCount = 0;

        // Cheap scene-side overrides too.
        Light sun = GameObject.Find("Sun")?.GetComponent<Light>();
        HDAdditionalLightData sd = sun != null ? sun.GetComponent<HDAdditionalLightData>() : null;
        if (sd != null) { sd.SetShadowResolution(1024); }
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile != null)
        {
            if (profile.TryGet(out HDShadowSettings sh)) { sh.cascadeShadowSplitCount.Override(2); sh.maxShadowDistance.Override(45f * U); }
            if (profile.TryGet(out Fog fog)) fog.enableVolumetricFog.Override(false);
            if (profile.TryGet(out ScreenSpaceAmbientOcclusion ao)) ao.active = false;
            if (profile.TryGet(out ContactShadows cs)) cs.enable.Override(false);
            EditorUtility.SetDirty(profile);
        }
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.allowDynamicResolution = true;
            HDAdditionalCameraData cd = cam.GetComponent<HDAdditionalCameraData>();
            if (cd != null) { cd.allowDynamicResolution = true; cd.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.Low; }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Crucible: quality level 'Laptop' active (55% render scale + FSR, 1024 shadows, 2 cascades, no volumetrics/AO/contact shadows). " +
                  "Also set the Game view to 1920x1080 or Free Aspect and close the Scene tab while playing.");
    }

    [MenuItem("Crucible/Look/Quality: PC (full look)")]
    public static void UsePcQuality()
    {
        HDRenderPipelineAsset pc = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(PipelineAssetPath);
        if (pc == null) { Debug.LogError("Crucible: run step 1 first."); return; }
        int level = EnsureQualityLevel("PC");
        QualitySettings.SetQualityLevel(level, true);
        QualitySettings.renderPipeline = pc;
        Light sun = GameObject.Find("Sun")?.GetComponent<Light>();
        HDAdditionalLightData sd = sun != null ? sun.GetComponent<HDAdditionalLightData>() : null;
        if (sd != null) sd.SetShadowResolution(2048);
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.allowDynamicResolution = false;
            HDAdditionalCameraData cd = cam.GetComponent<HDAdditionalCameraData>();
            if (cd != null) { cd.allowDynamicResolution = false; cd.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High; }
        }
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile != null) FillProfile(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("Crucible: quality level 'PC' active (full look restored).");
    }

    private static int EnsureQualityLevel(string name)
    {
        string[] names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++) if (names[i] == name) return i;
        // Add a level by duplicating the current one through the serialized quality settings.
        SerializedObject so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        SerializedProperty levels = so.FindProperty("m_QualitySettings");
        int src = QualitySettings.GetQualityLevel();
        levels.InsertArrayElementAtIndex(levels.arraySize);
        SerializedProperty added = levels.GetArrayElementAtIndex(levels.arraySize - 1);
        added.FindPropertyRelative("name").stringValue = name;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        return levels.arraySize - 1;
    }

    private static void SetFloat(SerializedObject so, string path, float value)
    {
        SerializedProperty prop = so.FindProperty(path);
        if (prop != null && prop.propertyType == SerializedPropertyType.Float) prop.floatValue = value;
        else Debug.LogWarning($"Crucible: HDRP asset property '{path}' not found; leaving default.");
    }

    private static void SetInt(SerializedObject so, string path, int value)
    {
        SerializedProperty prop = so.FindProperty(path);
        if (prop != null && (prop.propertyType == SerializedPropertyType.Integer || prop.propertyType == SerializedPropertyType.Enum)) prop.intValue = value;
        else Debug.LogWarning($"Crucible: HDRP asset property '{path}' not found; leaving default.");
    }

    [MenuItem("Crucible/Look/Diagnose (print the look state to the console)")]
    public static void Diagnose()
    {
        var sb = new System.Text.StringBuilder("Crucible look diagnose\n");
        sb.AppendLine($"  pipeline: {(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "NONE (built-in)")}");

        Camera cam = Camera.main;
        if (cam == null) sb.AppendLine("  camera: NO MAIN CAMERA");
        else
        {
            HDAdditionalCameraData cd = cam.GetComponent<HDAdditionalCameraData>();
            sb.AppendLine($"  camera: '{cam.name}' fov {cam.fieldOfView} hdData {(cd != null ? $"yes aa={cd.antialiasing} clear={cd.clearColorMode} layers={cd.volumeLayerMask.value}" : "MISSING")}");
        }

        foreach (Light l in Object.FindObjectsOfType<Light>())
        {
            HDAdditionalLightData ld = l.GetComponent<HDAdditionalLightData>();
            sb.AppendLine($"  light '{l.name}': type {l.type} enabled {l.enabled} Light.intensity {l.intensity} colour {l.color} " +
                          $"hdData {(ld != null ? $"yes unit={ld.lightUnit} shadows={l.shadows}" : "MISSING")} euler {l.transform.eulerAngles}");
        }

        foreach (Volume v in Object.FindObjectsOfType<Volume>())
        {
            VolumeProfile p = v.sharedProfile;
            sb.AppendLine($"  volume '{v.gameObject.name}': enabled {v.enabled} global {v.isGlobal} priority {v.priority} profile {(p != null ? p.name : "NONE")}");
            if (p == null) continue;
            int nulls = 0;
            foreach (VolumeComponent c in p.components)
            {
                if (c == null) { nulls++; continue; }
                sb.AppendLine($"      - {c.GetType().Name} active {c.active}");
            }
            if (nulls > 0) sb.AppendLine($"      - {nulls} NULL component(s): the profile lost its data, re-run step 3");
            if (p.TryGet(out Exposure e)) sb.AppendLine($"      exposure mode {e.mode.value} fixed {e.fixedExposure.value} (override {e.fixedExposure.overrideState})");
            if (p.TryGet(out VisualEnvironment ve)) sb.AppendLine($"      sky type {ve.skyType.value} (override {ve.skyType.overrideState})");
            if (p.TryGet(out Fog f)) sb.AppendLine($"      fog enabled {f.enabled.value} meanFreePath {f.meanFreePath.value} volumetric {f.enableVolumetricFog.value}");
        }
        Debug.Log(sb.ToString());
    }

    // ---------------------------------------------------------------- volume profile

    private static void FillProfile(VolumeProfile p)
    {
        // Sky and environment lighting
        VisualEnvironment env = GetOrAdd<VisualEnvironment>(p);
        env.skyType.Override((int)SkyType.PhysicallyBased);
        env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
        PhysicallyBasedSky sky = GetOrAdd<PhysicallyBasedSky>(p);
        sky.exposure.Override(0f);
        sky.multiplier.Override(1.4f);               // brighter sky = brighter ambient in the shadows
        IndirectLightingController indirect = GetOrAdd<IndirectLightingController>(p);
        indirect.indirectDiffuseLightingMultiplier.Override(1.6f);
        sky.groundTint.Override(new Color(0.55f, 0.45f, 0.32f));
        sky.aerosolDensity.Override(0.03f);           // dusty, warm horizon
        sky.aerosolTint.Override(new Color(0.9f, 0.8f, 0.65f));

        // Exposure: fixed so the fight never pumps while the camera moves
        Exposure exposure = GetOrAdd<Exposure>(p);
        // Automatic within a tight band: it can never crush to black, and the band keeps the
        // fight from pumping as the camera swings between the sunlit wall and the shadow side.
        exposure.mode.Override(ExposureMode.Automatic);
        exposure.meteringMode.Override(MeteringMode.CenterWeighted);
        exposure.limitMin.Override(11f);
        exposure.limitMax.Override(16f);
        exposure.compensation.Override(0.3f);
        exposure.adaptationSpeedDarkToLight.Override(3f);
        exposure.adaptationSpeedLightToDark.Override(1.5f);

        // Dust in the air: warm fog + volumetrics so the sun draws through it
        Fog fog = GetOrAdd<Fog>(p);
        fog.enabled.Override(true);
        fog.meanFreePath.Override(90f * U);
        fog.baseHeight.Override(0f);
        fog.maximumHeight.Override(12f * U);
        fog.albedo.Override(new Color(0.94f, 0.90f, 0.82f));   // pale dust haze under a high sun
        fog.enableVolumetricFog.Override(true);
        fog.anisotropy.Override(0.45f);
        fog.globalLightProbeDimmer.Override(0.8f);
        fog.depthExtent.Override(60f * U);
        fog.quality.Override(0);                        // low: the cheap volumetric tier; the dust still reads

        // Shadows and contact
        HDShadowSettings shadows = GetOrAdd<HDShadowSettings>(p);
        shadows.maxShadowDistance.Override(70f * U);
        shadows.cascadeShadowSplitCount.Override(4);
        ContactShadows contact = GetOrAdd<ContactShadows>(p);
        contact.enable.Override(true);
        contact.length.Override(0.35f * U);
        contact.opacity.Override(0.8f);
        contact.quality.Override(0);
        MicroShadowing micro = GetOrAdd<MicroShadowing>(p);
        micro.enable.Override(false);                   // pretty but costly; off until the frame rate is proven

        // Ambient occlusion (cheap tier). Screen-space reflections stay off: nothing in the arena
        // is glossy enough to pay for them yet; re-enable for wet stone / blood pools later.
        ScreenSpaceAmbientOcclusion ao = GetOrAdd<ScreenSpaceAmbientOcclusion>(p);
        ao.intensity.Override(0.8f);
        ao.radius.Override(0.6f * U);
        ao.directLightingStrength.Override(0.25f);
        ao.quality.Override(0);
        ScreenSpaceReflection ssr = GetOrAdd<ScreenSpaceReflection>(p);
        ssr.enabled.Override(false);

        // Grade: ACES, warm, contrasty, slightly desaturated, crushed warm shadows
        Tonemapping tone = GetOrAdd<Tonemapping>(p);
        tone.mode.Override(TonemappingMode.ACES);
        ColorAdjustments color = GetOrAdd<ColorAdjustments>(p);
        color.postExposure.Override(0.1f);
        color.contrast.Override(8f);
        color.saturation.Override(-8f);
        color.colorFilter.Override(new Color(1f, 0.98f, 0.95f));
        LiftGammaGain lgg = GetOrAdd<LiftGammaGain>(p);
        lgg.lift.Override(new Vector4(1.01f, 1f, 0.98f, 0f));
        lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f));
        lgg.gain.Override(new Vector4(1.02f, 1f, 0.96f, 0.02f));
        Bloom bloom = GetOrAdd<Bloom>(p);
        bloom.intensity.Override(0.12f);
        bloom.threshold.Override(0.9f);
        bloom.scatter.Override(0.65f);
        Vignette vig = GetOrAdd<Vignette>(p);
        vig.intensity.Override(0.18f);
        vig.smoothness.Override(0.5f);
        vig.color.Override(Color.black);
        FilmGrain grain = GetOrAdd<FilmGrain>(p);
        grain.type.Override(FilmGrainLookup.Medium2);
        grain.intensity.Override(0.28f);
        grain.response.Override(0.75f);
        ChromaticAberration ca = GetOrAdd<ChromaticAberration>(p);
        ca.intensity.Override(0.08f);

        // Lens effects are OFF for now. Depth of field and motion blur both smear fast fighters when
        // the frame rate is not locked, which reads as ghosting. Re-enable once the game holds 60.
        DepthOfField dof = GetOrAdd<DepthOfField>(p);
        dof.active = false;
        dof.focusMode.Override(DepthOfFieldMode.Manual);
        dof.quality.Override(0);
        dof.nearFocusStart.Override(0f);
        dof.nearFocusEnd.Override(0.5f * U);
        dof.farFocusStart.Override(9f * U);
        dof.farFocusEnd.Override(28f * U);
        MotionBlur blur = GetOrAdd<MotionBlur>(p);
        blur.active = false;
        blur.intensity.Override(0f);
        blur.sampleCount = 6;
        blur.quality.Override(0);
        blur.minimumVelocity.Override(6f);
        blur.maximumVelocity.Override(60f);
        blur.cameraRotationVelocityClamp.Override(0.03f);
    }

    private static T GetOrAdd<T>(VolumeProfile p) where T : VolumeComponent
    {
        // Components lost on a domain reload (never saved as sub-assets) show up as nulls: drop them.
        p.components.RemoveAll(x => x == null);
        if (!p.TryGet(out T c))
        {
            c = p.Add<T>(true);
            // VolumeProfile.Add only creates the object in memory; it has to be stored inside the
            // profile asset or it is gone after the next recompile / reopen.
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
        }
        EditorUtility.SetDirty(c);
        return c;
    }

    private static Texture2D FindMaskMap(string materialPath)
    {
        string dir = Path.GetDirectoryName(materialPath);
        foreach (string file in Directory.GetFiles(dir, "*_mask.png", SearchOption.AllDirectories))
        {
            string assetPath = file.Replace('\\', '/');
            TextureImporter imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (imp != null && imp.sRGBTexture)
            {
                imp.sRGBTexture = false;   // packed data, not colour
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
        return null;
    }

    private static void SetBool(SerializedObject so, string path, bool value)
    {
        SerializedProperty prop = so.FindProperty(path);
        if (prop != null && prop.propertyType == SerializedPropertyType.Boolean) prop.boolValue = value;
        else Debug.LogWarning($"Crucible: HDRP asset property '{path}' not found; leaving default.");
    }
}
#endif
