// The held double-flick RUN (the user's call, 2026-10-25): wires the animator side on all four
// controllers. One menu item, idempotent:
//   - a "Running" bool parameter,
//   - a looping "Run_Forward" state (its motion is assigned by Crucible > Animation > Use Mixamo
//     packs, whose maps carry a Run_Forward entry per fighter - run that AFTER this),
//   - Any State -> Run_Forward while Running is true (appended, so every trigger transition that
//     already exists - attacks, reacts, dashes, death - evaluates FIRST and still wins its frame),
//   - Run_Forward -> the layer's default state (the idle) when Running drops.
// Fighter.HandleRun drives the bool; the gameplay movement is code, like walking - the clip's own
// root motion is discarded as everywhere else.
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleRun
{
    static readonly string[] Controllers =
    {
        "Assets/_project/Animation/Controllers/P1_Bjorn.controller",
        "Assets/_project/Animation/Controllers/P2_Miyamoto.controller",
        "Assets/_project/Animation/Controllers/P3_Richard.controller",
        "Assets/_project/Animation/Controllers/P4_Salaadin.controller",
    };

    const string Param = "Running";
    const string State = "Run_Forward";

    [MenuItem("Crucible/Animation/Wire the run state")]
    public static void WireRun()
    {
        foreach (string path in Controllers)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) { Debug.LogWarning($"{path}: no controller"); continue; }

            if (!ac.parameters.Any(p => p.name == Param))
                ac.AddParameter(Param, AnimatorControllerParameterType.Bool);

            var sm = ac.layers[0].stateMachine;
            var run = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == State);
            if (run == null) run = sm.AddState(State, new Vector3(300f, 420f, 0f));

            // Any State -> Run while the bool is up. canTransitionToSelf OFF or the state restarts
            // every frame the bool is true and the run never leaves its first stride.
            if (!sm.anyStateTransitions.Any(t => t.destinationState == run))
            {
                var into = sm.AddAnyStateTransition(run);
                into.hasExitTime = false;
                into.hasFixedDuration = true;
                into.duration = 0.12f;
                into.canTransitionToSelf = false;
                into.AddCondition(AnimatorConditionMode.If, 0f, Param);
            }

            // Run -> the idle (the layer default) the moment the bool drops.
            if (!run.transitions.Any(t => t.destinationState == sm.defaultState))
            {
                var outOf = run.AddTransition(sm.defaultState);
                outOf.hasExitTime = false;
                outOf.hasFixedDuration = true;
                outOf.duration = 0.2f;
                outOf.AddCondition(AnimatorConditionMode.IfNot, 0f, Param);
            }

            EditorUtility.SetDirty(ac);
            Debug.Log($"{System.IO.Path.GetFileName(path)}: Running param + {State} state wired " +
                      $"(motion {(run.motion != null ? run.motion.name : "UNSET - run Use Mixamo packs")})");
        }
        AssetDatabase.SaveAssets();
    }
}
