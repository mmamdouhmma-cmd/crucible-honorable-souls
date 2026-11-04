// Story-only animator states: the nonlethal KNOCKDOWN (the user's call, 2026-11-03: kneel, then
// `crouch to standing idle`). Both clips are AxePack files no fighter used, imported Generic; this
// makes them Humanoid (like every pack clip) and adds two states to the controllers the story's
// knockable fighters run:
//   Story_Kneel  - `crouch idle`, looping; entered from code (Animator.CrossFadeInFixedTime)
//   Story_GetUp  - `crouch to standing idle`; leaves for the idle on its own at the end
// Nothing in a duel ever enters them. Re-runnable. Musashi and the Partner run P2_Miyamoto.
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleStoryAnims
{
    const string Pack = "Assets/_project/Animation/Clips/Mixamo/AxePack/";
    static readonly (string file, string state, bool loop)[] Clips =
    {
        ("crouch idle.fbx", "Story_Kneel", true),
        ("crouch to standing idle.fbx", "Story_GetUp", false),
    };
    static readonly string[] Controllers = { "Assets/_project/Animation/Controllers/P2_Miyamoto.controller" };

    [MenuItem("Crucible/Story/Wire the knockdown states")]
    public static void Wire()
    {
        foreach (var (file, state, loop) in Clips) Import(Pack + file, state, loop);
        foreach (var path in Controllers)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) { Debug.LogError($"Story anims: no controller {path}"); continue; }
            var sm = ac.layers[0].stateMachine;
            AnimatorState kneel = null, getUp = null;
            for (int i = 0; i < Clips.Length; i++)
            {
                var (file, name, _) = Clips[i];
                var clip = AssetDatabase.LoadAllAssetsAtPath(Pack + file).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
                if (clip == null) { Debug.LogError($"Story anims: {file} has no clip '{name}'"); return; }
                var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name)
                         ?? sm.AddState(name, new Vector3(760f, 420f + 60f * i, 0f));
                st.motion = clip;
                st.writeDefaultValues = true;
                if (i == 0) kneel = st; else getUp = st;
            }
            // Get-up -> the idle when the clip ends.
            if (!getUp.transitions.Any(t => t.destinationState == sm.defaultState))
            {
                var t = getUp.AddTransition(sm.defaultState);
                t.hasExitTime = true; t.exitTime = 0.92f; t.hasFixedDuration = true; t.duration = 0.2f;
            }
            EditorUtility.SetDirty(ac);
            Debug.Log($"Story anims: {ac.name} has Story_Kneel ({kneel.motion.name}) and Story_GetUp ({getUp.motion.name})");
        }
        AssetDatabase.SaveAssets();
    }

    static void Import(string path, string clipName, bool loop)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        if (mi == null) { Debug.LogError($"Story anims: no importer {path}"); return; }
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        var take = mi.defaultClipAnimations.FirstOrDefault() ?? mi.clipAnimations.FirstOrDefault();
        if (take == null)
        {
            mi.SaveAndReimport();   // a Generic import may list no take until it is Humanoid
            mi = (ModelImporter)AssetImporter.GetAtPath(path);
            take = mi.defaultClipAnimations.FirstOrDefault();
        }
        if (take == null) { Debug.LogError($"Story anims: {path} has no take"); return; }
        take.name = clipName;
        take.loopTime = loop;
        take.loopPose = false;
        take.lockRootRotation = true;
        take.keepOriginalOrientation = false;   // a pack clip: Based Upon body orientation, like the rest
        take.lockRootHeightY = true;
        take.keepOriginalPositionY = true;
        take.lockRootPositionXZ = true;         // root motion is discarded anyway; keep him on his mark
        mi.clipAnimations = new[] { take };
        mi.SaveAndReimport();
    }
}
