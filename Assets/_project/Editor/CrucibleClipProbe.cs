using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Crucible > Debug > Probe stance clips: samples the custom clips on the fighters in the open scene and
/// writes what Unity's Humanoid retarget actually produces (hand/elbow positions relative to the hips,
/// muscle curve ranges, avatar muscle limits) to ClipProbe.txt in the project root and the console.
/// Used to compare against the Blender pose when a clip looks different in game.
/// </summary>
public static class CrucibleClipProbe
{
    const string ClipDir = "Assets/_project/Animation/Clips/Custom/";

    static readonly string[] Clips =
    {
        "Bjorn_Idle", "Bjorn_Walk_Forward", "Bjorn_Block_Hold", "Bjorn_Light_Slash",
        "Miyamoto_Idle", "Miyamoto_Walk_Forward", "Miyamoto_Block_Hold", "Miyamoto_Light_Thrust",
    };

    static readonly string[] MuscleFilter =
    {
        "Arm Down-Up", "Arm Front-Back", "Arm Twist", "Forearm Stretch", "Forearm Twist", "Hand Down-Up", "Hand In-Out",
        "Shoulder",
    };

    // Drop a file named ClipProbe.request in the project root and the probe runs on the next domain reload
    // (i.e. when the editor regains focus), so it can be triggered without clicking the menu.
    [InitializeOnLoadMethod]
    static void AutoRun()
    {
        var req = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ClipProbe.request");
        if (!File.Exists(req)) return;
        double start = EditorApplication.timeSinceStartup;
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            if (!File.Exists(req)) { EditorApplication.update -= tick; return; }
            // Wait until the asset database is idle and the first clip actually resolves, then run once.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.timeSinceStartup - start < 2.0) return;
            bool ready = AssetDatabase.LoadAllAssetsAtPath(ClipDir + Clips[0] + ".fbx").Length > 0;
            if (!ready && EditorApplication.timeSinceStartup - start < 60.0) return;
            EditorApplication.update -= tick;
            File.Delete(req);
            Probe();
        };
        EditorApplication.update += tick;
    }

    const string ModelAvatarPath = "Assets/_project/Characters/Bjorn/Model/tripo_convert_5f0ff4d2-acaf-4853-81f5-20c59d424db7.fbx";

    // Drop ClipMap.request in the project root: on the next domain reload every clip importer gets an explicit
    // human bone map copied from the Bjorn model avatar (same skeleton), so Unity's auto-mapper can no longer
    // pick a twist bone as the hand. Then the clips reimport and the probe runs.
    [InitializeOnLoadMethod]
    static void AutoFixMaps()
    {
        var req = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ClipMap.request");
        if (!File.Exists(req)) return;
        double start = EditorApplication.timeSinceStartup;
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            if (!File.Exists(req)) { EditorApplication.update -= tick; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.timeSinceStartup - start < 2.0) return;
            EditorApplication.update -= tick;
            File.Delete(req);
            FixClipHumanMaps();
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ClipProbe.request"), "probe");
            AutoRun();
        };
        EditorApplication.update += tick;
    }

    [MenuItem("Crucible/Debug/Fix clip human bone maps (copy from Bjorn model)")]
    public static void FixClipHumanMaps()
    {
        Avatar model = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelAvatarPath)) if (o is Avatar av) model = av;
        if (model == null) { Debug.LogError("FixClipHumanMaps: model avatar not found at " + ModelAvatarPath); return; }
        var human = model.humanDescription.human;
        var log = new StringBuilder();
        int changed = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ClipDir.TrimEnd('/') }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null || imp.animationType != ModelImporterAnimationType.Human) continue;
            Avatar clipAvatar = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Avatar av) clipAvatar = av;
            var hd = imp.humanDescription;
            // Keep the clip's own skeleton (its node hierarchy), replace the human map with the model's.
            if (clipAvatar != null && (hd.skeleton == null || hd.skeleton.Length == 0)) hd.skeleton = clipAvatar.humanDescription.skeleton;
            hd.human = human;
            hd.upperArmTwist = model.humanDescription.upperArmTwist;
            hd.lowerArmTwist = model.humanDescription.lowerArmTwist;
            hd.upperLegTwist = model.humanDescription.upperLegTwist;
            hd.lowerLegTwist = model.humanDescription.lowerLegTwist;
            imp.humanDescription = hd;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
            changed++;
            log.AppendLine(path);
        }
        Debug.Log($"FixClipHumanMaps: wrote explicit human map ({human.Length} bones) into {changed} clip importers\n{log}");
    }

    [MenuItem("Crucible/Debug/Probe stance clips (writes ClipProbe.txt)")]
    public static void Probe()
    {
        var sb = new StringBuilder();
        var fighters = new Dictionary<string, Animator>();
        foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
        {
            if (a.avatar == null || !a.avatar.isHuman) continue;
            fighters[a.gameObject.name] = a;
            sb.AppendLine($"FIGHTER {a.gameObject.name}: avatar {a.avatar.name} human={a.avatar.isHuman} valid={a.avatar.isValid} rootScale={a.transform.lossyScale} controller={(a.runtimeAnimatorController ? a.runtimeAnimatorController.name : "none")}");
            DumpLimits(sb, a.avatar, "  ");
        }

        // Controller side: does each state's motion still resolve to a clip asset?
        foreach (var ctrlPath in new[] { "Assets/_project/Animation/Controllers/P1_Bjorn.controller", "Assets/_project/Animation/Controllers/P2_Miyamoto.controller" })
        {
            // The controllers were never reimported after the clips came back from a failed import, so their
            // in-memory state motions can still be null. Force them to re-resolve.
            AssetDatabase.ImportAsset(ctrlPath, ImportAssetOptions.ForceUpdate);
            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ctrlPath);
            if (ctrl == null) { sb.AppendLine($"CONTROLLER {ctrlPath}: not loaded"); continue; }
            sb.AppendLine($"CONTROLLER {ctrl.name} ({ctrlPath}) default={ctrl.layers[0].stateMachine.defaultState?.name}");
            foreach (var cs in ctrl.layers[0].stateMachine.states)
            {
                var m = cs.state.motion;
                var mPath = m ? AssetDatabase.GetAssetPath(m) : "";
                sb.AppendLine($"  state {cs.state.name,-30} motion={(m ? m.name : "NULL")} asset={mPath}");
            }
        }

        foreach (var name in Clips)
        {
            var path = ClipDir + name + ".fbx";
            AnimationClip clip = null;
            Avatar clipAvatar = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) clip = c;
                if (o is Avatar av) clipAvatar = av;
            }
            if (clip == null) { sb.AppendLine($"CLIP {name}: NOT FOUND at {path}"); continue; }
            sb.AppendLine($"CLIP {name}: length={clip.length:F3}s humanMotion={clip.humanMotion} legacy={clip.legacy} clipAvatar={(clipAvatar ? clipAvatar.name : "none")} valid={(clipAvatar ? clipAvatar.isValid.ToString() : "-")}");
            if (clipAvatar != null) DumpLimits(sb, clipAvatar, "  clipAvatar ");

            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                bool interesting = false;
                foreach (var f in MuscleFilter) if (b.propertyName.Contains(f)) { interesting = true; break; }
                if (!interesting) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                float mn = float.MaxValue, mx = float.MinValue;
                foreach (var k in curve.keys) { mn = Mathf.Min(mn, k.value); mx = Mathf.Max(mx, k.value); }
                sb.AppendLine($"  muscle {b.propertyName,-28} min {mn,7:F2} max {mx,7:F2}");
            }

            var fighterName = name.StartsWith("Bjorn") ? "Player1" : "Player2";
            if (!fighters.TryGetValue(fighterName, out var anim))
            {
                foreach (var kv in fighters) { anim = kv.Value; break; }
            }
            if (anim == null) { sb.AppendLine("  (no humanoid fighter in scene to sample on)"); continue; }
            SampleOn(sb, anim, clip, Mathf.Min(0.1f, clip.length * 0.5f));
        }

        var outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ClipProbe.txt");
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log("ClipProbe written to " + outPath + "\n" + sb);
    }

    static void DumpLimits(StringBuilder sb, Avatar avatar, string indent)
    {
        var hd = avatar.humanDescription;
        sb.AppendLine($"{indent}limits: armTwist={hd.upperArmTwist:F2} forearmTwist={hd.lowerArmTwist:F2} humanBones={hd.human.Length}");
        foreach (var hb in hd.human)
        {
            if (!(hb.humanName.Contains("Arm") || hb.humanName.Contains("Hand") || hb.humanName.Contains("Shoulder"))) continue;
            if (hb.humanName.Contains("Index") || hb.humanName.Contains("Thumb") || hb.humanName.Contains("Middle") || hb.humanName.Contains("Ring") || hb.humanName.Contains("Little")) continue;
            var l = hb.limit;
            sb.AppendLine($"{indent}  {hb.humanName,-14} <- {hb.boneName,-18} defaults={l.useDefaultValues} min={l.min} max={l.max} center={l.center}");
        }
    }

    static void SampleOn(StringBuilder sb, Animator anim, AnimationClip clip, float time)
    {
        var root = anim.transform;
        var saved = new List<(Transform t, Vector3 p, Quaternion r, Vector3 s)>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) saved.Add((t, t.localPosition, t.localRotation, t.localScale));

        var graph = PlayableGraph.Create("CrucibleClipProbe");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "out", anim);
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);
        output.SetSourcePlayable(playable);
        playable.SetTime(time);
        graph.Evaluate(0f);

        var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        sb.AppendLine($"  sampled on {anim.gameObject.name} at t={time:F2}s (positions = root-local, metres of the model file, relative to hips; fwd=+Z of root)");
        foreach (var b in new[] { HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                                  HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot })
        {
            var t = anim.GetBoneTransform(b);
            if (t == null || hips == null) { sb.AppendLine($"    {b}: missing"); continue; }
            var p = root.InverseTransformPoint(t.position) - root.InverseTransformPoint(hips.position);
            sb.AppendLine($"    {b,-14} fwd {p.z,6:F3}  left {-p.x,6:F3}  up {p.y,6:F3}");
        }
        var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand); var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
        var lf = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); var rf = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        if (lh && rh && lf && rf)
        {
            var dl = root.InverseTransformDirection(lh.position - lf.position).normalized;
            var dr = root.InverseTransformDirection(rh.position - rf.position).normalized;
            sb.AppendLine($"    forearm dirs L (fwd {dl.z:F2} left {-dl.x:F2} up {dl.y:F2})  R (fwd {dr.z:F2} left {-dr.x:F2} up {dr.y:F2})");
        }

        graph.Destroy();
        foreach (var (t, p, r, s) in saved) { t.localPosition = p; t.localRotation = r; t.localScale = s; }
    }
}

// probe build marker: 035529
// probe build marker: 041448
// probe build marker: 042723
