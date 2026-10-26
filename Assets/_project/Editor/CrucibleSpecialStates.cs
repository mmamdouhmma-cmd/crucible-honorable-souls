// Animator wiring for the SPECIALS (SP + button) and the RIPOSTES (the counter windows).
//
// Crucible > Animation > Wire special states: for each fighter controller, makes sure every
// AttackData asset of type Special - and every *_Riposte_* asset, which is an ordinary weight so
// that it clashes normally - has a trigger parameter, a state named exactly after the asset
// (the whole Mixamo pipeline keys off *state name == asset name*) and the two transitions that
// reach it and leave it. Re-runnable: it adds what is missing and leaves the rest alone, so it is
// safe to call after authoring another special.
//
// It deliberately does NOT assign clips or speeds - that is CrucibleMixamoSetup's job, and it only
// re-points states that already exist, which is why this has to run first.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleSpecialStates
{
    const string P1Ctrl = "Assets/_project/Animation/Controllers/P1_Bjorn.controller";
    const string P2Ctrl = "Assets/_project/Animation/Controllers/P2_Miyamoto.controller";
    const string P3Ctrl = "Assets/_project/Animation/Controllers/P3_Richard.controller";
    // His character folder is lowercase on disk; see Folder() in CrucibleBalance.cs.
    const string P4Ctrl = "Assets/_project/Animation/Controllers/P4_Salaadin.controller";
    const string P5Ctrl = "Assets/_project/Animation/Controllers/P5_Genghis.controller";
    const string P6Ctrl = "Assets/_project/Animation/Controllers/P6_Buwaka.controller";

    // Blend into a special. The same rule the attack transitions already use: fixed duration, no
    // exit time, because the attack's own frame data decides when it starts and ends.
    // 0.05 s, down from 0.12. A special's first pose is a long way from the idle, and the blend
    // between them is not the clip: measured on Richard's Twin Hew, the 7-frame cross-fade swung a
    // thigh THROUGH the other leg for 8 frames and dipped his boots 0.16 under the arena while
    // every frame of the clip itself measured clean. Shortening it is also what stops a special
    // reading as melty - a long cross-fade is exactly that.
    const float BlendIn = 0.05f;
    const float ExitAt = 0.9f;

    [MenuItem("Crucible/Animation/Wire special and riposte states")]
    public static void Wire()
    {
        Run(P1Ctrl, "Assets/_project/Characters/Bjorn/Attacks", "Bjorn_Idle");
        Run(P2Ctrl, "Assets/_project/Characters/Miyamoto/Attacks", "Miyamoto_Idle");
        if (System.IO.File.Exists(P3Ctrl)) Run(P3Ctrl, "Assets/_project/Characters/Richard/Attacks", "Richard_Idle");
        // Salaadin was missing from this list entirely, so every shared change made here - the
        // blend duration among them - stopped at three fighters while his stayed as first wired.
        if (System.IO.File.Exists(P4Ctrl)) Run(P4Ctrl, "Assets/_project/Characters/salaadin/Attacks", "Salaadin_Idle");
        if (System.IO.File.Exists(P5Ctrl)) Run(P5Ctrl, "Assets/_project/Characters/Genghis/Attacks", "Genghis_Idle");
        if (System.IO.File.Exists(P6Ctrl)) Run(P6Ctrl, "Assets/_project/Characters/Buwaka/Attacks", "Buwaka_Idle");
        AssetDatabase.SaveAssets();
        Debug.Log("Special and riposte states wired. Run 'Use Mixamo packs' next to give them their clips.");
    }

    static void Run(string ctrlPath, string attackDir, string idleName)
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        if (ac == null) { Debug.LogError($"no controller at {ctrlPath}"); return; }
        var sm = ac.layers[0].stateMachine;

        var idle = sm.states.FirstOrDefault(s => s.state.name == idleName).state;
        if (idle == null) Debug.LogWarning($"{ctrlPath}: no {idleName} state; specials will have no exit");

        // Every Special in this character's folder, with the trigger it fires.
        var specials = new List<(string state, string trigger)>();
        foreach (var guid in AssetDatabase.FindAssets("t:AttackData", new[] { attackDir }))
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(asset);
            bool isSpecial = so.FindProperty("attackType").enumValueIndex == 3;   // AttackType.Special
            // A riposte is reached only through a counter window, never from a library, so it is
            // identified by name rather than by weight - it carries an ordinary weight on purpose.
            bool isRiposte = asset.name.Contains("_Riposte_");
            // A combo ENDER is reached only as a string's last link, never from a library - the
            // same out-of-library pattern as a riposte (Buwaka's Serpent Coil, 2026-10-26).
            bool isEnder = asset.name.Contains("_Ender_");
            if (!isSpecial && !isRiposte && !isEnder) continue;
            specials.Add((asset.name, so.FindProperty("animatorTriggerName").stringValue));
        }
        if (specials.Count == 0) { Debug.LogWarning($"{attackDir}: no Special or Riposte assets found"); return; }

        float y = 500f;
        foreach (var (stateName, trigger) in specials.OrderBy(s => s.state))
        {
            if (string.IsNullOrEmpty(trigger)) { Debug.LogWarning($"{stateName}: no animatorTriggerName"); continue; }
            if (!ac.parameters.Any(p => p.name == trigger))
            {
                ac.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
                Debug.Log($"{stateName}: added trigger {trigger}");
            }

            var st = sm.states.FirstOrDefault(s => s.state.name == stateName).state;
            if (st == null)
            {
                st = sm.AddState(stateName, new Vector3(560f, y, 0f));
                y += 70f;
                Debug.Log($"added state {stateName}");
            }

            // Any State -> the special, on its trigger. The DURATION is written every run, not only
            // when the transition is created: it used to sit inside the "if (!reachable)" branch, so
            // changing BlendIn did nothing to a controller that already had its transitions and the
            // console still reported the new number. Read the controller back after this runs.
            var existing = sm.anyStateTransitions.FirstOrDefault(t => t.destinationState == st);
            if (existing == null)
            {
                existing = sm.AddAnyStateTransition(st);
                existing.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                existing.canTransitionToSelf = false;
                Debug.Log($"{stateName}: Any State -> state on {trigger}");
            }
            existing.hasExitTime = false;
            existing.hasFixedDuration = true;
            existing.duration = BlendIn;

            // ... and back to the guard when it has played out.
            if (idle != null && !st.transitions.Any(t => t.destinationState == idle))
            {
                var back = st.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = ExitAt;
                back.hasFixedDuration = true;
                back.duration = 0.1f;
                Debug.Log($"{stateName}: -> {idleName} at exit {ExitAt}");
            }
        }
        EditorUtility.SetDirty(ac);
    }
}
