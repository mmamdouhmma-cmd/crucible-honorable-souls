// Crucible > Look > Switch to URP (laptop)
// Puts the project back on URP and lays down the same art direction with URP tools: noon sun,
// sky fill, procedural sky, distance fog, ACES grade, bloom, vignette, grain, SMAA. Converts the
// character and arena materials to URP/Lit (the HDRP mask map's R and A channels are exactly
// URP's metallic/smoothness map, so the baked textures carry over). Cosmetic only.
// "Crucible > Look > Run all three steps" switches back to HDRP.
#if URP_PRESENT
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CrucibleUrpLook
{
    private const string SettingsDir = "Assets/_project/Settings";
    private const string UrpAssetPath = SettingsDir + "/PC_RPAsset.asset";
    private const string ProfilePath = SettingsDir + "/CrucibleLookURP.asset";
    private const string SkyMatPath = SettingsDir + "/CrucibleSky.mat";
    private const string VolumeObjectName = "Crucible Look Volume";

    [MenuItem("Crucible/Look/Switch to URP (laptop-friendly look)")]
    public static void SwitchToUrp()
    {
        UniversalRenderPipelineAsset urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
        if (urp == null) { Debug.LogError($"Crucible: {UrpAssetPath} not found."); return; }

        // Pipeline on graphics + every quality level.
        GraphicsSettings.defaultRenderPipeline = urp;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(current, false);
        QualitySettings.vSyncCount = 0;

        // URP asset: HDR, soft 2K shadows over a sensible distance, no MSAA (SMAA on the camera instead).
        SerializedObject so = new SerializedObject(urp);
        Set(so, "m_SupportsHDR", 1);
        Set(so, "m_MSAA", 1);
        Set(so, "m_MainLightShadowmapResolution", 2048);
        Set(so, "m_ShadowCascadeCount", 4);
        SetF(so, "m_ShadowDistance", 120f);
        Set(so, "m_SoftShadowsSupported", 1);
        Set(so, "m_ColorGradingMode", 1);          // HDR grading
        Set(so, "m_ColorGradingLutSize", 32);
        Set(so, "m_SupportsCameraDepthTexture", 1);
        Set(so, "m_SupportsCameraOpaqueTexture", 0);
        SetF(so, "m_RenderScale", 1f);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(urp);

        ConvertMaterialsToUrp();
        ApplySceneLookUrp();

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("Crucible: project is on URP with the laptop look. Save the scene (Ctrl+S). " +
                  "Optional: select Settings/PC_Renderer and add the 'Screen Space Ambient Occlusion' renderer feature for contact shading.");
    }

    /// <summary>True when URP is the active pipeline.</summary>
    public static bool IsActive => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;

    public static void ConvertMaterialsToUrp()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_project/Characters", "Assets/_project/Environment", "Assets/_project/Settings" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null || !mat.shader.name.StartsWith("HDRP/")) continue;

            Texture baseMap = mat.GetTexture("_BaseColorMap");
            Texture normal = mat.GetTexture("_NormalMap");
            Texture mask = mat.GetTexture("_MaskMap");
            Color baseColor = mat.GetColor("_BaseColor");
            float metallic = mat.GetFloat("_Metallic");
            float smoothness = mask != null ? mat.GetFloat("_SmoothnessRemapMax") : mat.GetFloat("_Smoothness");

            mat.shader = lit;
            mat.SetColor("_BaseColor", baseColor);
            if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
            if (mask != null)
            {
                // HDRP mask: R metallic, A smoothness == URP metallic-gloss map layout.
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.SetFloat("_Metallic", 1f);
                mat.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
            }
            mat.SetFloat("_Surface", 0f);
            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat);
            n++;
            Debug.Log($"Crucible: {path} -> URP/Lit");
        }
        Debug.Log($"Crucible: {n} material(s) converted to URP.");
    }

    private static void ApplySceneLookUrp()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            UniversalAdditionalCameraData cd = cam.GetComponent<UniversalAdditionalCameraData>();
            if (cd == null) cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;
            cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cd.antialiasingQuality = AntialiasingQuality.High;
            cd.dithering = true;
            cd.renderShadows = true;
            cam.allowHDR = true;
            cam.allowDynamicResolution = false;
        }

        // Lights: URP intensities are unitless. HDRP left lux values on Light.intensity, so reset them.
        Light sun = null, fill = null;
        foreach (Light l in Object.FindObjectsOfType<Light>())
        {
            if (l.type != LightType.Directional) continue;
            if (l.name == "Fill Light") fill = l; else if (sun == null) sun = l;
        }
        if (sun == null) { sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; }
        sun.name = "Sun";
        sun.color = new Color(1.0f, 0.96f, 0.88f);
        sun.intensity = 2.1f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.shadowBias = 0.02f;
        sun.shadowNormalBias = 0.5f;
        sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.16f, -0.914f, 0.37f).normalized, Vector3.up);   // noon, from the back-left
        if (fill == null) { fill = new GameObject("Fill Light").AddComponent<Light>(); fill.type = LightType.Directional; }
        fill.color = new Color(0.70f, 0.78f, 0.95f);
        fill.intensity = 0.45f;
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.LookRotation(new Vector3(-0.5f, -0.7f, -0.6f).normalized, Vector3.up);

        // Sky, ambient and fog come from RenderSettings under URP.
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            AssetDatabase.CreateAsset(sky, SkyMatPath);
        }
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_SunSizeConvergence", 6f);
        sky.SetFloat("_AtmosphereThickness", 1.15f);
        sky.SetColor("_SkyTint", new Color(0.55f, 0.65f, 0.85f));
        sky.SetColor("_GroundColor", new Color(0.52f, 0.45f, 0.35f));
        sky.SetFloat("_Exposure", 1.25f);
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.86f, 0.80f, 0.70f);
        RenderSettings.fogStartDistance = 70f;
        RenderSettings.fogEndDistance = 320f;
        DynamicGI.UpdateEnvironment();

        // Post volume (URP components).
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        profile.components.RemoveAll(c => c == null);
        Tonemapping tone = GetOrAdd<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.ACES);
        ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
        color.postExposure.Override(0.15f);
        color.contrast.Override(8f);
        color.saturation.Override(-6f);
        color.colorFilter.Override(new Color(1f, 0.98f, 0.95f));
        LiftGammaGain lgg = GetOrAdd<LiftGammaGain>(profile);
        lgg.lift.Override(new Vector4(1.01f, 1f, 0.98f, 0f));
        lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f));
        lgg.gain.Override(new Vector4(1.02f, 1f, 0.96f, 0.02f));
        Bloom bloom = GetOrAdd<Bloom>(profile);
        bloom.intensity.Override(0.15f);
        bloom.threshold.Override(0.95f);
        bloom.scatter.Override(0.65f);
        Vignette vig = GetOrAdd<Vignette>(profile);
        vig.intensity.Override(0.18f);
        vig.smoothness.Override(0.5f);
        FilmGrain grain = GetOrAdd<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Medium2);
        grain.intensity.Override(0.22f);
        grain.response.Override(0.75f);
        ChromaticAberration ca = GetOrAdd<ChromaticAberration>(profile);
        ca.intensity.Override(0.06f);
        EditorUtility.SetDirty(profile);

        GameObject volGo = GameObject.Find(VolumeObjectName);
        if (volGo == null) volGo = new GameObject(VolumeObjectName);
        Volume vol = volGo.GetComponent<Volume>();
        if (vol == null) vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        vol.sharedProfile = profile;
        vol.enabled = true;
        foreach (Volume v in Object.FindObjectsOfType<Volume>())
        {
            if (v != vol && v.sharedProfile != profile) v.enabled = false;
        }
    }

    private static T GetOrAdd<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (!p.TryGet(out T c))
        {
            c = p.Add<T>(true);
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
        }
        EditorUtility.SetDirty(c);
        return c;
    }

    private static void Set(SerializedObject so, string path, int value)
    {
        SerializedProperty prop = so.FindProperty(path);
        if (prop == null) { Debug.LogWarning($"Crucible: URP asset property '{path}' not found."); return; }
        if (prop.propertyType == SerializedPropertyType.Boolean) prop.boolValue = value != 0;
        else prop.intValue = value;
    }

    private static void SetF(SerializedObject so, string path, float value)
    {
        SerializedProperty prop = so.FindProperty(path);
        if (prop == null) { Debug.LogWarning($"Crucible: URP asset property '{path}' not found."); return; }
        prop.floatValue = value;
    }
}
#endif
