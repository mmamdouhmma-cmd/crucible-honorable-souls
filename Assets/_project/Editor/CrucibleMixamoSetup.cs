// Stop-gap animation: wire the fighter controllers to Mixamo packs (Animation/Clips/Mixamo/<Pack>/*.fbx)
// until the hand-keyed clip set exists.
//
// Crucible > Animation > Use Mixamo packs:      imports every pack clip as Humanoid (own avatar, root motion
//     kept on the root so an Animator without "Apply Root Motion" plays walks in place, Y and orientation
//     baked, loops on idle/walk/block), then re-points the P1/P2 controller states at the mapped clips and
//     sets each attack state's speed so the clip spans the attack's startup+active+recovery frames.
// Crucible > Animation > Restore custom clips:  points the states back at Animation/Clips/Custom/*.fbx.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleMixamoSetup
{
    const string MixamoDir = "Assets/_project/Animation/Clips/Mixamo";
    const string CustomDir = "Assets/_project/Animation/Clips/Custom";
    const string P1Ctrl = "Assets/_project/Animation/Controllers/P1_Bjorn.controller";
    const string P2Ctrl = "Assets/_project/Animation/Controllers/P2_Miyamoto.controller";

    // state name -> (pack, clip file stem, loop)
    static readonly (string state, string pack, string clip, bool loop)[] P1Map =
    {
        ("Bjorn_Idle",                   "AxePack", "standing idle",                     true),
        ("Bjorn_Walk_Forward",           "AxePack", "standing walk forward",             true),
        ("Bjorn_Walk_Back",              "AxePack", "standing walk back",                true),
        ("Bjorn_Light_Slash",            "AxePack", "standing melee attack horizontal",  false),
        ("Bjorn_Medium_SpinningSlash",   "AxePack", "standing melee attack 360 high",    false),
        ("Bjorn_Heavy_OverheadDoubleAxe","AxePack", "standing melee attack downward",    false),
        ("React_Front",                  "AxePack", "standing react large gut",          false),
        ("React_Left",                   "AxePack", "standing react large from left",    false),
        ("React_Right",                  "AxePack", "standing react large from right",   false),
        ("Block_Hold",                   "AxePack", "standing block idle",               true),
        ("Block_Impact",                 "AxePack", "standing block react large",        false),
    };
    static readonly (string state, string pack, string clip, bool loop)[] P2Map =
    {
        ("Miyamoto_Idle",                "GreatSwordPack", "great sword idle",          true),
        ("Miyamoto_Walk_Forward",        "GreatSwordPack", "great sword walk",          true),
        ("Miyamoto_Walk_Back",           "GreatSwordPack", "great sword walk (2)",      true),
        ("Miyamoto_Light_Thrust",        "GreatSwordPack", "great sword slash",         false),
        ("Miyamoto_Medium_SideSlash",    "GreatSwordPack", "great sword slash (2)",     false),
        ("Miyamoto_Heavy_Overhead",      "GreatSwordPack", "great sword attack",        false),
        ("React_Front",                  "GreatSwordPack", "great sword impact",        false),
        ("React_Left",                   "GreatSwordPack", "great sword impact (2)",    false),
        ("React_Right",                  "GreatSwordPack", "great sword impact (3)",    false),
        ("Block_Hold",                   "GreatSwordPack", "great sword blocking",      true),
        ("Block_Impact",                 "GreatSwordPack", "great sword blocking (2)",  false),
    };

    // Attack clips are trimmed to their swing core (measured in Blender: Animation/Clips/Mixamo/swing_trims.json,
    // peak right-hand speed + core window per clip) so the peak lands on the attack's hit frame.
    static Dictionary<string, (float first, float last, int frames)> Trims = new Dictionary<string, (float, float, int)>();

    static void ComputeTrims((string state, string pack, string clip, bool loop)[] map, string attackDir)
    {
        string jsonPath = $"{MixamoDir}/swing_trims.json";
        if (!File.Exists(jsonPath)) { Debug.LogWarning("no swing_trims.json; attack clips will be fitted whole"); return; }
        string json = File.ReadAllText(jsonPath);
        var attacks = AssetDatabase.FindAssets("t:AttackData", new[] { attackDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g))).ToList();
        foreach (var m in map)
        {
            var atk = attacks.FirstOrDefault(a => a.name == m.state);
            if (atk == null) continue;
            var so = new SerializedObject(atk);
            int s = so.FindProperty("startupFrames").intValue, a = so.FindProperty("activeFrames").intValue, r = so.FindProperty("recoveryFrames").intValue;
            int total = s + a + r;
            var block = System.Text.RegularExpressions.Regex.Match(json, "\"" + System.Text.RegularExpressions.Regex.Escape(m.clip) + "\"\\s*:\\s*\\{([^}]*)\\}");
            if (!block.Success) { Debug.LogWarning($"{m.clip}: not in swing_trims.json"); continue; }
            int Get(string key) => int.Parse(System.Text.RegularExpressions.Regex.Match(block.Groups[1].Value, "\"" + key + "\"\\s*:\\s*(-?\\d+)").Groups[1].Value);
            int first = Get("first"), last = Get("last"), peak = Get("peak"), cs = Get("core_start"), ce = Get("core_end");
            float hitFrac = (s + a * 0.5f) / total;
            float len = Mathf.Max(ce - cs, 6);
            float start = peak - hitFrac * len;
            if (start < first) start = first;
            float end = Mathf.Min(start + len, last);
            Trims[m.state] = (start, end, total);
            Debug.Log($"{m.state}: clip {m.clip} frames {first}-{last}, peak {peak}, core {cs}-{ce} -> window {start:F0}-{end:F0} for {total} f (hit at {hitFrac:P0})");
        }
    }

    [MenuItem("Crucible/Animation/Use Mixamo packs")]
    public static void UseMixamo()
    {
        Trims.Clear();
        ComputeTrims(P1Map, "Assets/_project/Characters/Bjorn/Attacks");
        ComputeTrims(P2Map, "Assets/_project/Characters/Miyamoto/Attacks");
        ImportPack("AxePack", P1Map); ImportPack("GreatSwordPack", P2Map);
        Wire(P1Ctrl, P1Map, "Assets/_project/Characters/Bjorn/Attacks");
        Wire(P2Ctrl, P2Map, "Assets/_project/Characters/Miyamoto/Attacks");
        AssetDatabase.SaveAssets();
        Debug.Log("Mixamo packs wired into P1_Bjorn and P2_Miyamoto. Play to check; Dash_* states keep the custom clips.");
    }

    [MenuItem("Crucible/Animation/Restore custom clips")]
    public static void RestoreCustom()
    {
        foreach (var (ctrlPath, prefix) in new[] { (P1Ctrl, "Bjorn"), (P2Ctrl, "Miyamoto") })
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            foreach (var cs in ac.layers[0].stateMachine.states)
            {
                var st = cs.state;
                string[] candidates = { $"{CustomDir}/{st.name}.fbx", $"{CustomDir}/{prefix}_{st.name}.fbx" };
                var clip = candidates.Select(LoadClip).FirstOrDefault(c => c != null);
                if (clip == null) { Debug.LogWarning($"{st.name}: no custom clip found"); continue; }
                st.motion = clip; st.speed = 1f;
            }
            EditorUtility.SetDirty(ac);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Custom clips restored on both controllers.");
    }

    static void ImportPack(string pack, (string state, string pack, string clip, bool loop)[] map)
    {
        foreach (var m in map)
        {
            string path = $"{MixamoDir}/{m.pack}/{m.clip}.fbx";
            if (!File.Exists(path)) { Debug.LogError($"missing {path}"); continue; }
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            bool dirty = false;
            if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; dirty = true; }
            if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            var clips = mi.defaultClipAnimations;
            if (clips.Length == 0) { Debug.LogError($"{path}: no takes"); continue; }
            var c = clips[0];
            c.name = m.state;
            c.loopTime = m.loop;
            c.loopPose = false;
            c.lockRootRotation = true;      // bake orientation into the pose
            c.keepOriginalOrientation = false; // based upon body orientation
            c.lockRootHeightY = true;       // bake Y
            c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = false;   // keep XZ as root motion: not applied by the Animator, so in place
            c.keepOriginalPositionXZ = false;
            if (Trims.TryGetValue(m.state, out var t)) { c.firstFrame = t.first; c.lastFrame = t.last; }
            var current = mi.clipAnimations;
            if (current.Length != 1 || current[0].name != c.name || current[0].loopTime != c.loopTime || current[0].lockRootPositionXZ != c.lockRootPositionXZ
                || current[0].lockRootRotation != c.lockRootRotation || Mathf.Abs(current[0].firstFrame - c.firstFrame) > 0.01f || Mathf.Abs(current[0].lastFrame - c.lastFrame) > 0.01f)
            { mi.clipAnimations = new[] { c }; dirty = true; }
            if (dirty) mi.SaveAndReimport();
        }
    }

    static void Wire(string ctrlPath, (string state, string pack, string clip, bool loop)[] map, string attackDir)
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        var attacks = AssetDatabase.FindAssets("t:AttackData", new[] { attackDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g))).ToList();
        foreach (var m in map)
        {
            var cs = ac.layers[0].stateMachine.states.FirstOrDefault(s => s.state.name == m.state);
            if (cs.state == null) { Debug.LogWarning($"{Path.GetFileName(ctrlPath)}: no state {m.state}"); continue; }
            var clip = LoadClip($"{MixamoDir}/{m.pack}/{m.clip}.fbx");
            if (clip == null) { Debug.LogError($"{m.clip}: clip failed to import"); continue; }
            cs.state.motion = clip;
            cs.state.speed = 1f;
            // attack states: fit the clip to the AttackData frame count
            var atk = attacks.FirstOrDefault(a => a.name == m.state);
            if (atk != null)
            {
                var so = new SerializedObject(atk);
                int frames = so.FindProperty("startupFrames").intValue + so.FindProperty("activeFrames").intValue + so.FindProperty("recoveryFrames").intValue;
                float wanted = frames / 60f;
                if (wanted > 0.05f && clip.length > 0.05f)
                {
                    cs.state.speed = clip.length / wanted;
                    Debug.Log($"{m.state}: clip {clip.length:F2}s (trimmed={Trims.ContainsKey(m.state)}) fitted to {frames} f ({wanted:F2}s), speed x{cs.state.speed:F2}");
                }
            }
        }
        EditorUtility.SetDirty(ac);
    }

    static AnimationClip LoadClip(string path)
    {
        if (!File.Exists(path)) return null;
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
    }
}
