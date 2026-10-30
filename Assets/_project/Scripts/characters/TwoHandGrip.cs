using UnityEngine;

/// <summary>
/// Keeps the off hand on the weapon in every animation: the left arm is solved onto a socket
/// transform on the weapon ("LeftGrip", placed on the haft / tsuka by the character setup). Lets a
/// one-handed Mixamo clip read as a two-handed grip.
///
/// The arm is solved HERE, analytically, not by Unity's humanoid IK. SetIKPosition registers the
/// goal on these rigs but never moves the arm: measured 2026-10-21 on all four fighters, with the
/// layer's IK Pass on, layer weight 1, OnAnimatorIK running and GetIKPosition reading the goal
/// back correctly, a 15 cm lift asked of the left hand moved it 0 cm. That is what left the off
/// hand holding air. A two-bone solve in LateUpdate is deterministic and rig-independent.
///
/// Runs after WeaponAim (execution order) so the grip lands on the weapon's FINAL position each
/// frame; the other way round the hand chases where the weapon was.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Animator))]
public class TwoHandGrip : MonoBehaviour
{
    [Tooltip("Socket on the weapon where the left hand holds the haft. Found by name under the model if empty.")]
    [SerializeField] private Transform leftGrip;
    [Tooltip("Position weight of the left-hand IK goal (1 = always on the haft).")]
    [Range(0f, 1f)] [SerializeField] private float positionWeight = 1f;
    [Tooltip("Rotation weight: 0 keeps the animated hand orientation, 1 aligns the palm to the socket.")]
    [Range(0f, 1f)] [SerializeField] private float rotationWeight = 0f;
    [Tooltip("Optional elbow hint so the left arm bends outward instead of through the chest.")]
    [SerializeField] private Transform leftElbowHint;
    [SerializeField] private string socketName = "LeftGrip";
    [Tooltip("Base-layer states in which the off hand takes the grip. Empty = always (the roster's two-handers). " +
             "The story Sensei fights one-handed and grips with both hands only on his overhead.")]
    [SerializeField] private string[] onlyInStates = new string[0];
    [Tooltip("Seconds to blend the grip in or out when onlyInStates is in use.")]
    [SerializeField] private float stateBlendTime = 0.12f;
    private float stateWeight = 1f;

    [Header("Strike reach")]
    [Tooltip("During an attack, pull the weapon hand toward the opponent so the blade's middle reaches their chest, " +
             "whatever the stock swing does. 0 = off. The weapon aim then points the blade; together every strike is central.")]
    [Range(0f, 1f)] [SerializeField] private float strikeReachWeight = 0f;
    [Tooltip("Fraction of the blade length placed past the target point (0.5 = the blade's middle lands on the chest).")]
    [Range(0f, 1f)] [SerializeField] private float bladeOverlap = 0.5f;
    [Tooltip("Height on the opponent's body the strike reaches for (fraction of body height).")]
    [Range(0f, 1f)] [SerializeField] private float targetHeight = 0.55f;
    [Tooltip("Max fraction of the arm length the hand is allowed to extend (1 = fully straight).")]
    [Range(0.5f, 1f)] [SerializeField] private float maxArmExtension = 0.95f;
    [SerializeField] private float reachFadeOut = 0.12f;

    private Animator animator;
    private CombatSystem combat;
    private Fighter me;
    private Transform weapon;
    private float bladeLength;
    private float armLength;
    private float reachFade;

    /// <summary>Debug: how many times OnAnimatorIK has run on this component (0 = the layer has no IK pass at runtime).</summary>
    public int IkCalls { get; private set; }
    /// <summary>Debug: last distance from the animated left hand to the socket before IK was applied.</summary>
    public float LastPreIkDistance { get; private set; }

    /// <summary>The off hand as the CLIP poses it, before this IK pulls it onto the grip. WeaponAim's carry
    /// reads it so that "haft along the hand line" cannot chase its own IK (the weapon moves the hand, the
    /// hand moves the weapon: a 15 deg tilt came out as 53 deg).</summary>
    public Vector3 PreIkOffHand { get; private set; }
    public bool HasPreIkOffHand { get; private set; }
    private Transform offHandBone;
    [Tooltip("Where the haft sits in the OFF hand's own bone space (the palm), in the hand bone's local " +
             "units. The right hand's weapon mount is at (0.0001, 0.0005, 0); the left is its mirror.")]
    [SerializeField] private Vector3 offHandPalmLocal = Vector3.zero;

    [Tooltip("Close the off hand's fingers on the haft. 0 = leave them in the clip's pose. Does nothing on a rig " +
             "with no finger bones.")]
    [Range(0f, 1f)] [SerializeField] private float fingerCurlWeight = 1f;
    [Tooltip("Most a finger may close, in degrees, summed over its three joints.")]
    [SerializeField] private float fingerCurl = 150f;
    [Tooltip("Most the thumb may close. It lies across the haft rather than round it, so it takes less.")]
    [SerializeField] private float thumbCurl = 95f;
    [Tooltip("Haft radius in world units. 0 = work it out from the hand's own size, which keeps it right " +
             "whatever the fighter is scaled to in the scene.")]
    [SerializeField] private float haftRadius = 0f;
    [Range(0.1f, 1.5f)] [SerializeField] private float haftRadiusScale = 0.45f;
    [Tooltip("Where the knuckles ride, as a multiple of the haft radius. 1 = knuckles on the surface; a little " +
             "over 1 leaves the thickness of the fingers between the bone and the wood.")]
    [Range(0.5f, 3f)] [SerializeField] private float knuckleStandoff = 1.25f;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (leftGrip == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == socketName) { leftGrip = t; break; }
            }
        }
    }

    private void Start()
    {
        combat = GetComponent<CombatSystem>();
        me = GetComponent<Fighter>();
        foreach (var hd in GetComponentsInChildren<HitDetector>(true))
            if (hd.FollowWeapon) { weapon = hd.transform; break; }
        if (weapon != null)
        {
            var box = weapon.GetComponent<BoxCollider>();
            if (box != null)
            {
                Vector3 s = Vector3.Scale(box.size, weapon.lossyScale);
                bladeLength = Mathf.Max(s.x, s.y, s.z);
            }
        }
        var sh = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var el = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        var ha = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (sh && el && ha) armLength = Vector3.Distance(sh.position, el.position) + Vector3.Distance(el.position, ha.position);
    }

    /// <summary>0..1 strike weight: ramps through startup, 1 on the active frames, fades after.</summary>
    private float StrikeWeight()
    {
        float w = 0f;
        if (combat != null && combat.IsAttacking)
        {
            var a = combat.CurrentAttack;
            float t = combat.AttackTime;
            float startup = a.StartupFrames * BalanceConfig.FRAME_TIME;
            float activeEnd = startup + a.ActiveFrames * BalanceConfig.FRAME_TIME;
            if (t < startup) w = Mathf.SmoothStep(0f, 1f, startup > 1e-4f ? t / startup : 1f);
            else if (t < activeEnd) w = 1f;
            else w = Mathf.Clamp01(1f - (t - activeEnd) / Mathf.Max(reachFadeOut, 1e-3f));
            reachFade = w;
        }
        else if (reachFade > 0f)
        {
            reachFade = Mathf.MoveTowards(reachFade, 0f, Time.deltaTime / Mathf.Max(reachFadeOut, 1e-3f));
            w = reachFade;
        }
        return w;
    }

    private void StrikeReach()
    {
        if (strikeReachWeight <= 0f || armLength <= 0f) return;
        float w = StrikeWeight() * strikeReachWeight;
        if (w <= 1e-3f) return;
        // me.Opponent, refreshed by ApplySides - a cached first-other-Fighter points at a benched
        // bystander once the roster is bigger than two (the WeaponAim lesson, 2026-10-26).
        var opponent = me != null ? me.Opponent : null;
        if (opponent == null) return;
        var rend = opponent.GetComponentInChildren<SkinnedMeshRenderer>();
        Vector3 target = rend != null
            ? new Vector3(opponent.transform.position.x, Mathf.Lerp(rend.bounds.min.y, rend.bounds.max.y, targetHeight), opponent.transform.position.z)
            : opponent.transform.position + Vector3.up * 1.5f;
        target = me.StrikeAimPoint(target);   // a dodged combo reaches straight ahead
        var shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (shoulder == null) return;
        Vector3 toTarget = target - shoulder.position;
        float dist = toTarget.magnitude;
        Vector3 dir = toTarget / Mathf.Max(dist, 1e-4f);
        // hand distance along the line so the blade overlaps the target, clamped to the arm's reach
        float handDist = Mathf.Clamp(dist - bladeLength * (1f - bladeOverlap), 0.2f, armLength * maxArmExtension);
        Vector3 goal = shoulder.position + dir * handDist;
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
        animator.SetIKPosition(AvatarIKGoal.RightHand, goal);
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;
        StrikeReach();
        if (leftGrip == null) return;
        IkCalls++;
        // The BONE inside the IK pass is the clip's pose; GetIKPosition would hand back last frame's goal
        // (the grip socket), which is the very thing the carry must not feed on.
        if (offHandBone == null) offHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (offHandBone != null) { PreIkOffHand = offHandBone.position; HasPreIkOffHand = true; }
        LastPreIkDistance = (PreIkOffHand - leftGrip.position).magnitude;
    }

    /// <summary>
    /// Where the hand BONE must end up. Aim the PALM at the haft, not the wrist joint: the bone sits
    /// at the wrist, so putting that on the haft centreline leaves the shaft running through the side
    /// of the hand (the baked fist then grips air next to it). The weapon mount tells us where the
    /// right hand holds the haft in its own bone space - the off hand is the mirror of that.
    /// </summary>
    private Vector3 OffHandGoal()
    {
        Vector3 goal = leftGrip.position;
        if (offHandBone != null && offHandPalmLocal.sqrMagnitude > 1e-12f)
            goal -= offHandBone.TransformVector(offHandPalmLocal);
        return goal;
    }

    private void LateUpdate()
    {
        if (animator == null || leftGrip == null || positionWeight <= 0f) return;
        if (onlyInStates != null && onlyInStates.Length > 0)
        {
            bool want = InGripState(animator.GetCurrentAnimatorStateInfo(0)) ||
                        (animator.IsInTransition(0) && InGripState(animator.GetNextAnimatorStateInfo(0)));
            stateWeight = Mathf.MoveTowards(stateWeight, want ? 1f : 0f, Time.deltaTime / Mathf.Max(stateBlendTime, 1e-3f));
            if (stateWeight <= 0f) { OffHandMiss = 0f; return; }
        }
        else stateWeight = 1f;
        float w = positionWeight * stateWeight;
        if (offHandBone == null) offHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var fore = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        if (upper == null || fore == null || offHandBone == null) return;

        Vector3 goal = OffHandGoal();
        Vector3 pole = leftElbowHint != null ? leftElbowHint.position : Vector3.zero;
        SolveTwoBone(upper, fore, offHandBone, goal, leftElbowHint != null, pole, w);
        if (rotationWeight > 0f)
            offHandBone.rotation = Quaternion.Slerp(offHandBone.rotation, leftGrip.rotation, rotationWeight * stateWeight);

        // Put the PALM on the haft, not just the wrist near it. offHandPalmLocal is a fixed guess
        // made in the hand's own space; once there are knuckles to look at we can see where the
        // grip actually landed, and the answer was that it landed beside the haft - the knuckles
        // sat 0.09-0.26 world units off the axis with fingers only 0.06-0.11 long, so no amount
        // of closing could ever have reached it. Measure the miss and re-solve onto it.
        for (int pass = 0; pass < 2; pass++)
        {
            Vector3 corr = PalmOffset();
            if (corr.sqrMagnitude < 1e-8f) break;
            goal -= corr;
            SolveTwoBone(upper, fore, offHandBone, goal, leftElbowHint != null, pole, w);
        }

        OffHandMiss = (offHandBone.position - goal).magnitude;
        CloseFingers();
    }

    private bool InGripState(AnimatorStateInfo info)
    {
        foreach (var n in onlyInStates) if (!string.IsNullOrEmpty(n) && info.IsName(n)) return true;
        return false;
    }

    /// <summary>
    /// How far the knuckle line is from where it should sit on the haft, as a world vector
    /// perpendicular to the haft. Zero when the fighter has no fingers to go by.
    /// </summary>
    private Vector3 PalmOffset()
    {
        if (!fingersChecked) FindFingers();
        if (fingers == null || fingers.Length < 2) return Vector3.zero;
        Vector3 axis = HaftAxis();
        Vector3 onHaft = leftGrip.position;

        // the four finger knuckles; the thumb is last and sits across the grip, so leave it out
        int n = fingers.Length == 5 ? 4 : fingers.Length;
        Vector3 palm = Vector3.zero;
        for (int i = 0; i < n; i++) palm += fingers[i][0].position;
        palm /= n;

        Vector3 v = palm - onHaft;
        v -= axis * Vector3.Dot(v, axis);
        float cur = v.magnitude;
        if (cur < 1e-5f) return Vector3.zero;

        float want = (haftRadius > 0f ? haftRadius : hand_to_knuckle() * haftRadiusScale) * knuckleStandoff;
        return v / cur * (cur - want);
    }

    // ---- closing the off hand on the haft -------------------------------------------------
    //
    // Bjorn and Miyamoto were rigged by Tripo with no finger bones at all: the hand was one rigid
    // mitt, sculpted half open, so the off hand could sit exactly on the haft and still read as
    // holding air. They now carry a traced five-finger chain in the left hand (add_fingers.py) -
    // but nothing animates it, because every clip in the game predates those bones. So the grip
    // closes them here.
    //
    // Each phalanx turns about the HAFT'S OWN AXIS, which is what a finger wrapping a cylinder
    // does. That axis is measured from the two hands, both of which are on the haft, so this needs
    // no agreement with the rig about which local axis bends a finger - the one thing a Tripo rig,
    // a Mixamo rig and a hand-built chain would each answer differently.

    private Transform[][] fingers;      // [finger][proximal, intermediate, distal]
    private float[] fingerSpan;         // rest distal-from-knuckle, to scale each finger's curl
    private bool fingersChecked;

    private static readonly HumanBodyBones[][] LeftFingerBones =
    {
        new[]{ HumanBodyBones.LeftIndexProximal,  HumanBodyBones.LeftIndexIntermediate,  HumanBodyBones.LeftIndexDistal  },
        new[]{ HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
        new[]{ HumanBodyBones.LeftRingProximal,   HumanBodyBones.LeftRingIntermediate,   HumanBodyBones.LeftRingDistal   },
        new[]{ HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal },
        new[]{ HumanBodyBones.LeftThumbProximal,  HumanBodyBones.LeftThumbIntermediate,  HumanBodyBones.LeftThumbDistal  },
    };

    private void FindFingers()
    {
        fingersChecked = true;
        var found = new System.Collections.Generic.List<Transform[]>();
        foreach (var chain in LeftFingerBones)
        {
            var t = new Transform[3];
            bool all = true;
            for (int j = 0; j < 3; j++)
            {
                t[j] = animator.GetBoneTransform(chain[j]);
                if (t[j] == null) { all = false; break; }
            }
            if (all) found.Add(t);
        }
        if (found.Count == 0) return;                   // a rig with no fingers: nothing to close
        fingers = found.ToArray();
        fingerSpan = new float[fingers.Length];
        for (int i = 0; i < fingers.Length; i++)
            fingerSpan[i] = Vector3.Distance(fingers[i][0].position, fingers[i][2].position);
    }

    /// <summary>The haft's direction, taken from the two points we know are on it: the main hand
    /// and the off-hand socket. Falls back to the weapon's own long axis if they coincide.</summary>
    private Vector3 HaftAxis()
    {
        var main = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (main != null)
        {
            Vector3 d = leftGrip.position - main.position;
            if (d.sqrMagnitude > 1e-6f) return d.normalized;
        }
        return weapon != null ? weapon.up : transform.up;
    }

    private void CloseFingers()
    {
        if (fingerCurlWeight <= 0f) return;
        if (!fingersChecked) FindFingers();
        if (fingers == null) return;

        Vector3 axis = HaftAxis();
        Vector3 onHaft = leftGrip.position;
        float radius = haftRadius > 0f
            ? haftRadius
            : hand_to_knuckle() * haftRadiusScale;

        for (int i = 0; i < fingers.Length; i++)
        {
            bool thumb = i == fingers.Length - 1 && fingers.Length == 5;
            Curl(fingers[i], axis, onHaft, radius, thumb ? thumbCurl : fingerCurl);
        }
    }

    private float hand_to_knuckle()
    {
        var idx = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
        return idx != null && offHandBone != null
            ? Vector3.Distance(idx.position, offHandBone.position)
            : 0f;
    }

    /// <summary>
    /// Turn a finger about the haft until its last joint lies on the haft's surface. The angle is
    /// found on a copy of the joint positions - no transform is written until the answer is known,
    /// so the search costs a handful of vector operations instead of a hierarchy update per step.
    /// </summary>
    private void Curl(Transform[] f, Vector3 haft, Vector3 onHaft, float radius, float maxDeg)
    {
        Vector3[] rest = { f[0].position, f[1].position, f[2].position };
        float at0 = PerpDistance(rest[2], onHaft, haft);
        if (at0 <= radius) return;                      // already on the haft (or inside it)

        // The finger bends in the plane that actually faces the haft. Turning it about the haft's
        // own direction only sweeps it round a cone, and these fingers lie partly ALONG the haft,
        // so that barely closed them at all: the plane through the finger and the shaft is the one
        // that carries the tip at the wood.
        Vector3 dir = rest[2] - rest[0];
        Vector3 to = onHaft - rest[0];
        to -= haft * Vector3.Dot(to, haft);             // straight at the shaft, square to it
        Vector3 bend = Vector3.Cross(dir, to);
        if (bend.sqrMagnitude < 1e-10f) bend = Vector3.Cross(dir, haft);
        if (bend.sqrMagnitude < 1e-10f) return;
        bend.Normalize();

        // sweep and keep the angle that seats the last joint on the surface
        float bestA = 0f, bestErr = Mathf.Abs(at0 - radius);
        const int STEPS = 24;
        for (int s = 1; s <= STEPS; s++)
        {
            float a = maxDeg * s / STEPS;
            float err = Mathf.Abs(PerpDistance(Simulate(rest, bend, a)[2], onHaft, haft) - radius);
            if (err < bestErr) { bestErr = err; bestA = a; }
        }
        float total = bestA * fingerCurlWeight;
        if (total <= 0.5f) return;

        float[] share = Split(total);
        for (int j = 0; j < 3; j++)
            f[j].rotation = Quaternion.AngleAxis(share[j], bend) * f[j].rotation;
    }

    private static float[] Split(float total)
    {
        // a finger does not bend evenly: the middle knuckle closes hardest, the last the least
        const float a = 0.40f, b = 0.38f, c = 0.22f;
        return new[] { total * a, total * b, total * c };
    }

    /// <summary>Joint positions after turning each phalanx about <paramref name="axis"/>, as pure
    /// maths on a copy - the real bones are only written once, with the answer.</summary>
    private static Vector3[] Simulate(Vector3[] rest, Vector3 axis, float total)
    {
        float[] share = Split(total);
        Vector3[] p = { rest[0], rest[1], rest[2] };
        for (int j = 0; j < 3; j++)
        {
            var r = Quaternion.AngleAxis(share[j], axis);
            for (int k = j + 1; k < 3; k++) p[k] = r * (p[k] - p[j]) + p[j];
        }
        return p;
    }

    private static float PerpDistance(Vector3 p, Vector3 onAxis, Vector3 axis)
    {
        Vector3 d = p - onAxis;
        return (d - axis * Vector3.Dot(d, axis)).magnitude;
    }

    /// <summary>Debug: how far each finger's last joint is from the haft surface after closing.</summary>
    public float FingerGapMax()
    {
        if (fingers == null || leftGrip == null) return -1f;
        Vector3 axis = HaftAxis();
        float radius = haftRadius > 0f ? haftRadius : hand_to_knuckle() * haftRadiusScale;
        float worst = 0f;
        foreach (var f in fingers)
            worst = Mathf.Max(worst, Mathf.Abs(PerpDistance(f[2].position, leftGrip.position, axis) - radius));
        return worst;
    }

    /// <summary>Debug: how far the off hand still is from the haft after the solve. ~0 when it grips.</summary>
    public float OffHandMiss { get; private set; }

    /// <summary>
    /// Analytic two-bone IK on a shoulder -> elbow -> wrist chain, in world space.
    /// Places the elbow on the circle where both bone lengths are satisfied, swung toward
    /// <paramref name="pole"/>, then aims each bone in turn - so the wrist lands exactly on
    /// <paramref name="target"/> whenever the target is inside the arm's reach, and the arm
    /// straightens toward it when it is not.
    /// </summary>
    private static void SolveTwoBone(Transform root, Transform mid, Transform tip,
                                     Vector3 target, bool usePole, Vector3 pole, float weight)
    {
        Vector3 a = root.position, b = mid.position, c = tip.position;
        float lab = Vector3.Distance(a, b);
        float lcb = Vector3.Distance(b, c);
        if (lab < 1e-5f || lcb < 1e-5f) return;

        Quaternion rootWas = root.rotation, midWas = mid.rotation;

        Vector3 toTarget = target - a;
        float lat = Mathf.Clamp(toTarget.magnitude, 1e-4f, lab + lcb - 1e-4f);
        Vector3 dir = toTarget.sqrMagnitude > 1e-10f ? toTarget.normalized : (c - a).normalized;

        // the elbow sits at distance d along the shoulder->target line, h out to the side of it
        float d = (lat * lat + lab * lab - lcb * lcb) / (2f * lat);
        float h = Mathf.Sqrt(Mathf.Max(0f, lab * lab - d * d));

        // which side it swings to: the hint if there is one, otherwise the bend plane the clip
        // already poses, so a missing or degenerate hint never flips the elbow inside out.
        Vector3 side = usePole ? pole - a : b - a;
        side -= dir * Vector3.Dot(side, dir);
        if (side.sqrMagnitude < 1e-8f)
        {
            side = (b - a) - dir * Vector3.Dot(b - a, dir);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(dir, root.up);
            if (side.sqrMagnitude < 1e-8f) return;
        }
        Vector3 elbow = a + dir * d + side.normalized * h;

        root.rotation = Quaternion.FromToRotation(b - a, elbow - a) * root.rotation;
        mid.rotation = Quaternion.FromToRotation(tip.position - mid.position, target - elbow) * mid.rotation;

        if (weight < 1f)
        {
            root.rotation = Quaternion.Slerp(rootWas, root.rotation, weight);
            mid.rotation = Quaternion.Slerp(midWas, mid.rotation, weight);
        }
    }
}
