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

    // Blend into a special. The same rule the attack transitions already use: fixed duration, no
    // exit time, because the attack's own frame data decides when it starts and ends.
    const float BlendIn = 0.12f;
    const float ExitAt = 0.9f;

    [MenuItem("Crucible/Animation/Wire special and riposte states")]
    public static void Wire()
    {
        Run(P1Ctrl, "Assets/_project/Characters/Bjorn/Attacks", "Bjorn_Idle");
        Run(P2Ctrl, "Assets/_project/Characters/Miyamoto/Attacks", "Miyamoto_Idle");
        if (System.IO.File.Exists(P3Ctrl)) Run(P3Ctrl, "Assets/_project/Characters/Richard/Attacks", "Richard_Idle");
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
            if (!isSpecial && !isRiposte) continue;
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

            // Any State -> the special, on its trigger.
            bool reachable = sm.anyStateTransitions.Any(t => t.destinationState == st);
            if (!reachable)
            {
                var t = sm.AddAnyStateTransition(st);
                t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                t.hasExitTime = false;
                t.hasFixedDuration = true;
                t.duration = BlendIn;
                t.canTransitionToSelf = false;
                Debug.Log($"{stateName}: Any State -> state on {trigger}, {BlendIn:0.00}s blend");
            }

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
