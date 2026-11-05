// Crucible > Animation > Wire the block-walk legs
//
// Blocking used to hold the full-body Block_Hold pose, so a guarding fighter who walked just
// floated across the sand. This adds a "BlockLegs" layer to every fighter's controller: a
// lower-body avatar mask (legs only) over a 1D MoveSpeed blend of the fighter's own walk clips,
// slowed to read as a wary, guarded step. The layer's weight starts at 0 and is driven at runtime
// by Fighter.UpdateBlockLegs - up only while the guard is up AND the fighter is moving, so a
// standing block keeps the authored stance head to toe.
//
// SINCE 2026-11-05 (foot sliding, the user's call) the layer is up whenever the fighter WALKS, guard or
// not, and its legs are the SwordShieldPack walks for EVERY fighter - the same clips every base walk
// state now plays. Measured on all seven bodies those two step within 1-14 deg of straight; the
// AxePack2H walks the two-handed fighters had stepped 26-58 deg off it, so their feet slid sideways at
// ~2 units/s whatever the playback speed. The state's speed is the WalkPace parameter
// (Fighter.UpdateLocomotionPace: ground speed / stride), so the old fixed guard pace (x0.8) is gone -
// a slower guard walk simply measures slower.
// Movement is only ever toward/away from the opponent, so forward/back is the whole vocabulary.
// Idempotent: re-running replaces the layer.

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleBlockLegs
{
    private const string LayerName = "BlockLegs";
    private const string MaskPath = "Assets/_project/Animation/Controllers/BlockLegsLower.mask";
    public const string WalkPaceParam = "WalkPace";
    public const string RunPaceParam = "RunPace";
    private const string LegsForward = "Assets/_project/Animation/Clips/Mixamo/SwordShieldPack/sword and shield walk.fbx";
    private const string LegsBack = "Assets/_project/Animation/Clips/Mixamo/SwordShieldPack/sword and shield walk (2).fbx";

    /// <summary>The walk every fighter's LEGS play (one take of the shared pack file; all its takes are
    /// the same motion). Null if the pack has not been imported with a take yet.</summary>
    public static AnimationClip LegsClip(bool back)
    {
        AnimationClip best = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(back ? LegsBack : LegsForward))
            if (o is AnimationClip c && !c.name.StartsWith("__preview") && (best == null || string.CompareOrdinal(c.name, best.name) < 0))
                best = c;
        return best;
    }

    /// <summary>Adds a float parameter (default 1) if the controller does not have it.</summary>
    public static void EnsureFloat(AnimatorController controller, string name)
    {
        foreach (var p in controller.parameters) if (p.name == name) return;
        controller.AddParameter(new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
    }

    private static readonly string[] Controllers =
    {
        "Assets/_project/Animation/Controllers/P1_Bjorn.controller",
        "Assets/_project/Animation/Controllers/P2_Miyamoto.controller",
        "Assets/_project/Animation/Controllers/P3_Richard.controller",
        "Assets/_project/Animation/Controllers/P4_Salaadin.controller",
        "Assets/_project/Animation/Controllers/P5_Genghis.controller",
        "Assets/_project/Animation/Controllers/P6_Buwaka.controller",
        "Assets/_project/Animation/Controllers/P7_Sensei.controller",   // story Sensei (Ch1_Test_Sensei only)
        "Assets/_project/Animation/Controllers/P8_Arima.controller",    // story boss Arima (Musashi_Chapter01 only)
    };

    [MenuItem("Crucible/Animation/Wire the block-walk legs")]
    public static void Wire()
    {
        AvatarMask mask = BuildLowerBodyMask();

        foreach (string path in Controllers)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) { Debug.LogWarning($"BlockLegs: no controller at {path}"); continue; }

            Motion idle = null, walkF = LegsClip(false), walkB = LegsClip(true);
            idle = controller.layers[0].stateMachine.defaultState != null
                 ? controller.layers[0].stateMachine.defaultState.motion : null;

            if (walkF == null || walkB == null)
            {
                Debug.LogWarning($"BlockLegs: the SwordShieldPack walk takes are missing - run Use Mixamo packs first. {controller.name} skipped.");
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

            EnsureFloat(controller, WalkPaceParam);
            state.name = "BlockWalk";
            state.speed = 1f;
            state.speedParameterActive = true;
            state.speedParameter = WalkPaceParam;
            EditorUtility.SetDirty(controller);
            Debug.Log($"BlockLegs: {controller.name} wired - back={walkB.name}, idle={(idle != null ? idle.name : "none")}, " +
                      $"forward={walkF.name}, pace = {WalkPaceParam}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("BlockLegs: done. Fighter drives the layer weight whenever he walks; run Measure locomotion strides next.");
    }

    /// <summary>Legs-only humanoid mask (saved once, shared by all four controllers).</summary>
    private static AvatarMask BuildLowerBodyMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        bool fresh = mask == null;
        if (fresh) mask = new AvatarMask();

        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        // Legs only: the hips come from the base layer, which is the straight shield-pack walk itself
        // on a plain walk and the motionless Block_Hold on a guard walk - so they never fight the legs.
        // (Overlaying these legs on the two-handed walks' rocking hips is what kept them sliding.)
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, true);

        if (fresh) AssetDatabase.CreateAsset(mask, MaskPath);
        else EditorUtility.SetDirty(mask);
        return mask;
    }
}
