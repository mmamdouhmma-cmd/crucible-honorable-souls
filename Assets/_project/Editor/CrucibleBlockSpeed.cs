// Crucible > Animation > Set the block speed
//
// The guard itself is up the frame the button is (Fighter.HandleBlock); what the player SEES is the
// animator: the Any State -> Block_Hold blend, the Block_Hold -> idle blend, and the Block_Impact jolt
// a blocked hit plays. All of them run BlockSpeedUp faster than the durations they were authored at
// (the user's call, 2026-10-27: "speed up the block by 20%"). Written from the BASE numbers, so
// re-running never compounds. Use Mixamo packs also writes Block_Impact's speed from BlockSpeedUp.

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleBlockSpeed
{
    public const float BlockSpeedUp = 1.2f;

    // authored durations, seconds, before the speed-up
    const float RaiseBlend = 0.12f;   // Any State -> Block_Hold
    const float LowerBlend = 0.15f;   // Block_Hold -> idle
    const float ImpactBlend = 0.02f;  // Block_Hold -> Block_Impact
    const float SettleBlend = 0.08f;  // Block_Impact -> Block_Hold

    static readonly string[] Controllers =
    {
        "Assets/_project/Animation/Controllers/P1_Bjorn.controller",
        "Assets/_project/Animation/Controllers/P2_Miyamoto.controller",
        "Assets/_project/Animation/Controllers/P3_Richard.controller",
        "Assets/_project/Animation/Controllers/P4_Salaadin.controller",
        "Assets/_project/Animation/Controllers/P5_Genghis.controller",
        "Assets/_project/Animation/Controllers/P6_Buwaka.controller",
    };

    [MenuItem("Crucible/Animation/Set the block speed")]
    public static void Apply()
    {
        foreach (string path in Controllers)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ac == null) { Debug.LogWarning($"BlockSpeed: no controller at {path}"); continue; }
            var sm = ac.layers[0].stateMachine;
            int n = 0;
            foreach (var t in sm.anyStateTransitions)
                if (t.destinationState != null && t.destinationState.name == "Block_Hold") { t.duration = RaiseBlend / BlockSpeedUp; n++; }
            foreach (var cs in sm.states)
            {
                var s = cs.state;
                if (s.name == "Block_Impact") s.speed = BlockSpeedUp;
                foreach (var t in s.transitions)
                {
                    if (t.destinationState == null) continue;
                    if (s.name == "Block_Hold" && t.destinationState.name == "Block_Impact") { t.duration = ImpactBlend / BlockSpeedUp; n++; }
                    else if (s.name == "Block_Hold") { t.duration = LowerBlend / BlockSpeedUp; n++; }
                    else if (s.name == "Block_Impact" && t.destinationState.name == "Block_Hold") { t.duration = SettleBlend / BlockSpeedUp; n++; }
                }
            }
            EditorUtility.SetDirty(ac);
            Debug.Log($"BlockSpeed: {ac.name} - {n} guard transitions and Block_Impact at x{BlockSpeedUp}");
        }
        AssetDatabase.SaveAssets();
    }
}
