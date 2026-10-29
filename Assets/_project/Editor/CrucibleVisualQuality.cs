// Crucible > Look > Visual quality pass - the 2026-10-29 visual-fidelity fixes, re-runnable.
//
//  - The WEB build is split off onto its own copies of the pipeline asset and renderer
//    (Web_RPAsset / Web_Renderer, made once from PC_* as they stood) and assigned to the "Mobile"
//    quality level, which is WebGL's default. Everything below then changes the Windows look only.
//  - Shadows: 45 m at 4096 over 4 cascades (was 120 m at 2048) - the fight camera never sees past
//    ~40 m of shadowed ground, so the same texels land on a third of the distance.
//  - MSAA 4x on the PC asset and the match camera (SMAA stays on the camera for shader edges).
//  - SSAO 0.4 -> 0.6 on PC_Renderer.
//  - Grade: every arena Look profile keeps ACES; film grain 0.05, chromatic aberration off,
//    saturation 0 (Forest -3, its grass and orbs are saturated already), contrast eased.
//  - Reflections: a 256 sky reflection, and one ArenaReflectionProbe per arena root in the open scene.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CrucibleVisualQuality
{
    const string Settings = "Assets/_project/Settings";
    const string PcAsset = Settings + "/PC_RPAsset.asset";
    const string PcRenderer = Settings + "/PC_Renderer.asset";
    const string WebAsset = Settings + "/Web_RPAsset.asset";
    const string WebRenderer = Settings + "/Web_Renderer.asset";

    public const float ShadowDistance = 45f;
    public const int ShadowResolution = 4096;
    public const int Msaa = 4;
    public const float SsaoIntensity = 0.6f;
    public const float GrainIntensity = 0.05f;
    public const int ReflectionResolution = 256;
    public const float ProbeHeight = 2.5f;
    public static readonly Vector3 ProbeBox = new Vector3(70f, 30f, 70f);

    /// <summary>Every arena grade and the contrast / saturation it should carry after the pass.</summary>
    public static readonly (string path, float contrast, float saturation)[] Looks =
    {
        (Settings + "/CrucibleLookURP.asset", 5f, 0f),
        ("Assets/_project/Environment/DesertSurroundings/Desert_Look.asset", 4f, 0f),
        ("Assets/_project/Environment/FortressLook/Fortress_Look.asset", 8f, 0f),
        ("Assets/_project/Environment/ForestSurroundings/Forest_Look.asset", 5f, -3f),
        ("Assets/_project/Environment/Musashi_Ch01/Musashi_Ch01_Look.asset", 6f, 0f),
    };

    [MenuItem("Crucible/Look/Visual quality pass (Windows look; web unchanged)")]
    public static void Run()
    {
        SplitWeb();
        Pipeline();
        Grades();
        Reflections();
        AssetDatabase.SaveAssets();
        Debug.Log("Crucible: visual quality pass applied (shadows 45 m @ 4096, MSAA 4x, SSAO 0.6, grades eased, " +
                  "reflection probes). Save the scene.");
    }

    static void SplitWeb()
    {
        if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(WebAsset) == null)
        {
            AssetDatabase.CopyAsset(PcRenderer, WebRenderer);
            AssetDatabase.CopyAsset(PcAsset, WebAsset);
            var web = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(WebAsset);
            var so = new SerializedObject(web);
            var list = so.FindProperty("m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(WebRenderer);
            so.FindProperty("m_DefaultRendererIndex").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(web);
        }
        var webAsset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(WebAsset);
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            if (QualitySettings.names[i] != "Mobile") continue;
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = webAsset;
        }
        QualitySettings.SetQualityLevel(current, false);
    }

    static void Pipeline()
    {
        var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcAsset);
        pc.shadowDistance = ShadowDistance;
        pc.shadowCascadeCount = 4;
        pc.msaaSampleCount = Msaa;
        var so = new SerializedObject(pc);
        so.FindProperty("m_MainLightShadowmapResolution").intValue = ShadowResolution;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pc);

        var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(PcRenderer);
        foreach (var f in renderer.rendererFeatures)
        {
            if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
            var fso = new SerializedObject(f);
            fso.FindProperty("m_Settings.Intensity").floatValue = SsaoIntensity;
            fso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(f);
        }

        // The match camera disallowed MSAA; the select screen's second camera copies it at runtime.
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            if (cam.allowMSAA) continue;
            cam.allowMSAA = true;
            EditorUtility.SetDirty(cam);
        }
    }

    static void Grades()
    {
        foreach (var (path, contrast, saturation) in Looks)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) continue;
            Grade(profile, contrast, saturation);
        }
    }

    /// <summary>Toned-down ACES: the arena scripts call this too, so a rebuild keeps the pass.</summary>
    public static void Grade(VolumeProfile profile, float contrast, float saturation)
    {
        foreach (var c in profile.components)
        {
            if (c is Tonemapping t) t.mode.Override(TonemappingMode.ACES);
            if (c is ColorAdjustments ca) { ca.contrast.Override(contrast); ca.saturation.Override(saturation); }
            if (c is FilmGrain g) g.intensity.Override(GrainIntensity);
            if (c is ChromaticAberration ab) { ab.intensity.Override(0f); ab.active = false; }
        }
        EditorUtility.SetDirty(profile);
    }

    static void Reflections()
    {
        RenderSettings.defaultReflectionResolution = ReflectionResolution;
        var boundsType = CrucibleDesertUpgrade.GameType("ArenaBounds");
        var probeType = CrucibleDesertUpgrade.GameType("ArenaReflectionProbe");
        var seen = new HashSet<GameObject>();
        foreach (Component b in Resources.FindObjectsOfTypeAll(boundsType))
        {
            if (!b.gameObject.scene.IsValid() || EditorUtility.IsPersistent(b)) continue;
            var root = b.transform.root.gameObject;
            if (!seen.Add(root)) continue;
            var t = root.transform.Find("ReflectionProbe");
            if (t == null) { t = new GameObject("ReflectionProbe").transform; t.SetParent(root.transform, false); }
            t.position = b.transform.position + Vector3.up * ProbeHeight;
            var p = t.GetComponent<ReflectionProbe>();
            if (p == null) p = t.gameObject.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Custom;
            p.resolution = ReflectionResolution;
            p.hdr = true;
            p.size = ProbeBox;
            p.boxProjection = false;
            p.blendDistance = 4f;
            p.importance = 1;
            p.farClipPlane = 1000f;
            if (t.GetComponent(probeType) == null) t.gameObject.AddComponent(probeType);
            EditorUtility.SetDirty(root);
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Crucible: reflection probes on {seen.Count} arena root(s).");
    }
}
