// Crucible > Animation > Measure locomotion strides   (PLAY mode)
//
// The foot-sliding fix (2026-11-05) plays every walk and run at ground speed / stride, so the stride
// has to be MEASURED on the live body, never typed: the old hand table under-read the shield walk by a
// third and the shield fighters over-stepped by 25-55%. For each fighter in the open scene this plays
// his own controller exactly as the game layers it (base walk state + the BlockLegs legs layer at
// weight 1, WalkPace 1; then Run_Forward at RunPace 1) with the root held still, and reads the PLANTED
// foot's speed against the root: that is the ground the clip covers at x1. Written into his FighterData
// per unit of root scale (Fighter multiplies its own scale back in), so a scene that sizes him
// differently stays right.
//
// Re-run after changing a body's size, a walk or run clip, or the BlockLegs wiring. Run it in each scene
// whose fighters use a FighterData no other scene measures (Arima: the Musashi chapter scene).
// Validated against a ground-truth slip test (feet's WORLD velocity with the root moved at game speed).

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class CrucibleStrides
{
    const float Planted = 0.02f;   // world units above the foot's own lowest point that still count as planted

    [MenuItem("Crucible/Animation/Measure locomotion strides")]
    public static void Measure()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("Measure locomotion strides: enter Play mode first (it poses the live rigs).");
            return;
        }
        var log = new StringBuilder("Locomotion strides (world u/s at x1; stored / root scale):\n");
        foreach (var an in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include))
        {
            var fighter = an.GetComponent("Fighter");
            if (fighter == null) continue;
            var ctrl = an.runtimeAnimatorController as AnimatorController;
            var dataObj = new SerializedObject(fighter).FindProperty("fighterData")?.objectReferenceValue;
            if (ctrl == null || dataObj == null) { log.AppendLine($"  {an.name}: no controller or FighterData - skipped"); continue; }

            string fwd = null, back = null;
            foreach (var s in ctrl.layers[0].stateMachine.states)
            {
                if (s.state.name.EndsWith("_Walk_Forward")) fwd = s.state.name;
                else if (s.state.name.EndsWith("_Walk_Back")) back = s.state.name;
            }
            int legs = -1;
            for (int i = 0; i < ctrl.layers.Length; i++) if (ctrl.layers[i].name == "BlockLegs") legs = i;
            if (fwd == null || back == null || legs < 0) { log.AppendLine($"  {an.name}: walk states or BlockLegs layer missing - skipped"); continue; }

            bool wasActive = an.gameObject.activeSelf;
            an.gameObject.SetActive(true);
            var fighterBehaviour = (Behaviour)fighter;
            bool wasEnabled = fighterBehaviour.enabled;
            fighterBehaviour.enabled = false;   // nothing else writes his parameters while we step him

            float scale = Mathf.Max(0.01f, an.transform.lossyScale.y);
            Vector2 f = Sample(an, fwd, 1f, legs, false);
            Vector2 b = Sample(an, back, -1f, legs, false);
            Vector2 r = Sample(an, "Run_Forward", 1f, -1, true);

            an.SetFloat("MoveSpeed", 0f);
            an.SetBool("Running", false);
            if (legs >= 0) an.SetLayerWeight(legs, 0f);
            fighterBehaviour.enabled = wasEnabled;
            an.gameObject.SetActive(wasActive);

            var so = new SerializedObject(dataObj);
            so.FindProperty("walkStrideForward").floatValue = Mathf.Max(0f, f.y) / scale;
            so.FindProperty("walkStrideBack").floatValue = Mathf.Max(0f, -b.y) / scale;
            so.FindProperty("runStride").floatValue = Mathf.Max(0f, r.y) / scale;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(dataObj);
            log.AppendLine($"  {an.name} ({dataObj.name}): walk fwd {f.y:F2} (side {f.x:+0.00;-0.00}), back {-b.y:F2} (side {b.x:+0.00;-0.00}), run {r.y:F2} (side {r.x:+0.00;-0.00}), root scale {scale:F2}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    /// <summary>Plays one state of the controller with the root held still and returns the body's
    /// velocity over the ground implied by the planted foot (x = sideways, y = forward), world u/s.</summary>
    static Vector2 Sample(Animator an, string state, float moveSpeed, int legsLayer, bool running)
    {
        // The fighter's OWN Animator, stepped by hand: the exact pipeline the game runs (layers,
        // masks, state speeds x pace parameter, retarget), with the root held where it is.
        an.SetFloat("MoveSpeed", moveSpeed);
        an.SetFloat(CrucibleBlockLegs.WalkPaceParam, 1f);
        an.SetFloat(CrucibleBlockLegs.RunPaceParam, 1f);
        an.SetBool("Running", running);
        for (int i = 1; i < an.layerCount; i++) an.SetLayerWeight(i, i == legsLayer ? 1f : 0f);
        an.Play(state, 0, 0f);
        if (legsLayer >= 0) an.Play("BlockWalk", legsLayer, 0f);

        Transform root = an.transform;
        Vector3 rootPos = root.position;
        Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
        const int settle = 20, n = 200;
        var L = new Vector3[n]; var R = new Vector3[n];
        an.Update(0f);
        for (int i = 0; i < settle; i++) an.Update(1f / 60f);
        for (int i = 0; i < n; i++)
        {
            an.Update(1f / 60f);
            root.position = rootPos;
            // root-local axes, world units
            L[i] = root.InverseTransformDirection(lf.position - root.position);
            R[i] = root.InverseTransformDirection(rf.position - root.position);
        }

        float minL = float.MaxValue, minR = float.MaxValue;
        for (int i = 0; i < n; i++) { minL = Mathf.Min(minL, L[i].y); minR = Mathf.Min(minR, R[i].y); }
        var fw = new List<float>(); var sd = new List<float>();
        for (int i = 1; i < n; i++)
        {
            bool lp = L[i].y < minL + Planted && L[i].y <= R[i].y;
            bool rp = R[i].y < minR + Planted && R[i].y < L[i].y;
            if (!lp && !rp) continue;
            Vector3 d = (lp ? L[i] - L[i - 1] : R[i] - R[i - 1]) * 60f;
            fw.Add(-d.z); sd.Add(-d.x);   // the body moves opposite to its planted foot
        }
        if (fw.Count == 0) return Vector2.zero;
        fw.Sort(); sd.Sort();
        return new Vector2(sd[sd.Count / 2], fw[fw.Count / 2]);
    }
}
