// Crucible > Animation > Wire the block-walk legs
//
// Blocking used to hold the full-body Block_Hold pose, so a guarding fighter who walked just
// floated across the sand. This adds a "BlockLegs" layer to every fighter's controller: a
// lower-body avatar mask (legs only) over a 1D MoveSpeed blend of the fighter's own walk clips,
// slowed to read as a wary, guarded step. The layer's weight starts at 0 and is driven at runtime
// by Fighter.UpdateBlockLegs - up only while the guard is up AND the fighter is moving, so a
// standing block keeps the authored stance head to toe.
//
// The walk clips come from each controller's own Walk_Forward / Walk_Back states (all four packs
// carry walks; left/right strafes exist too but movement is only ever toward/away from the
// opponent, so forward/back is the whole vocabulary). Idempotent: re-running replaces the layer.

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleBlockLegs
{
    private const string LayerName = "BlockLegs";
    private const string MaskPath = "Assets/_project/Animation/Controllers/BlockLegsLower.mask";
    private const float LegPace = 0.8f;   // walk-cycle timescale: slower feet under the guard

    private static readonly string[] Controllers =
    {
        "Assets/_project/Animation/Controllers/P1_Bjorn.controller",
        "Assets/_project/Animation/Controllers/P2_Miyamoto.controller",
        "Assets/_project/Animation/Controllers/P3_Richard.controller",
        "Assets/_project/Animation/Controllers/P4_Salaadin.controller",
    };

    [MenuItem("Crucible/Animation/Wire the block-walk legs")]
    public static void Wire()
    {
        AvatarMask mask = BuildLowerBodyMask();

        foreach (string path in Controllers)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) { Debug.LogWarning($"BlockLegs: no controller at {path}"); continue; }

            Motion idle = null, walkF = null, walkB = null;
            foreach (var st in controller.layers[0].stateMachine.states)
            {
                string n = st.state.name;
                if (n.EndsWith("_Walk_Forward")) walkF = st.state.motion;
                else if (n.EndsWith("_Walk_Back")) walkB = st.state.motion;
            }
            idle = controller.layers[0].stateMachine.defaultState != null
                 ? controller.layers[0].stateMachine.defaultState.motion : null;

            if (walkF == null || walkB == null)
            {
                Debug.LogWarning($"BlockLegs: {controller.name} has no Walk_Forward/Walk_Back states - skipped.");
                continue;
            }

            // Replace any previous version of the layer.
            for (int i = controller.layers.Length - 1; i >= 1; i--)
                if (controller.layers[i].name == LayerName) controller.RemoveLayer(i);

            var layer = new AnimatorControllerLayer
            {
                name = LayerName,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                avatarMask = mask,
                stateMachine = new AnimatorStateMachine
                {
                    name = LayerName,
                    hideFlags = HideFlags.HideInHierarchy,
                },
            };
            AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
            controller.AddLayer(layer);

            BlendTree tree;
            AnimatorState state = controller.CreateBlendTreeInController("BlockWalk", out tree,
                                                                         controller.layers.Length - 1);
            tree.blendParameter = "MoveSpeed";
            tree.blendType = BlendTreeType.Simple1D;
            tree.useAutomaticThresholds = false;
            tree.AddChild(walkB, -1f);
            if (idle != null) tree.AddChild(idle, 0f);
            tree.AddChild(walkF, 1f);

            // Slow the walk cycles; the idle (threshold 0) keeps its own time.
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
                if (!Mathf.Approximately(children[i].threshold, 0f)) children[i].timeScale = LegPace;
            tree.children = children;

            state.name = "BlockWalk";
            EditorUtility.SetDirty(controller);
            Debug.Log($"BlockLegs: {controller.name} wired - back={walkB.name}, idle={(idle != null ? idle.name : "none")}, " +
                      $"forward={walkF.name}, pace x{LegPace}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("BlockLegs: done. Fighter drives the layer weight while blocking and moving.");
    }

    /// <summary>Legs-only humanoid mask (saved once, shared by all four controllers).</summary>
    private static AvatarMask BuildLowerBodyMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        bool fresh = mask == null;
        if (fresh) mask = new AvatarMask();

        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, true);

        if (fresh) AssetDatabase.CreateAsset(mask, MaskPath);
        else EditorUtility.SetDirty(mask);
        return mask;
    }
}
