using UnityEngine;

/// <summary>
/// Pins a planted foot to the ground while the fighter walks or runs (the user's call, 2026-11-05:
/// "make sure it does not happen to any character"). Playing the walk at ground speed / stride
/// (Fighter.UpdateLocomotionPace) gets the AVERAGE right, but a shared mocap clip retargeted onto
/// differently-proportioned legs still lets each planted foot creep: measured with the root moved at
/// exactly the matched speed, 3-27% of the walk speed and 13-24% of the run speed. So, per foot:
/// the moment it is the lower foot AND the clip moves it slower than <see cref="plantSpeedShare"/> of
/// the body's ground speed (a swinging foot outruns the body; a planted one barely moves) its spot on
/// the ground is remembered, and until it lifts the leg is bent - two-bone IK, the same analytic
/// solver TwoHandGrip uses, since Unity's humanoid IK does nothing on these rigs - to keep the ankle
/// on that spot. The animated height and foot rotation are kept, so heel-roll and terrain still read.
/// Lock blends in fast and out over <see cref="releaseTime"/>; if the clip carries the foot further than
/// <see cref="maxCorrection"/> from its spot, the spot creeps after it (better a tiny slide than a
/// stretched leg or a snap).
/// Runs after the Animator, WeaponAim and TwoHandGrip (100); ReplayRecorder (300) records the result.
/// Added by Fighter at startup, so no scene change is needed.
/// </summary>
[DefaultExecutionOrder(150)]
public class FootLock : MonoBehaviour
{
    [Tooltip("A foot counts as planted while the CLIP moves it slower than this share of the body's ground speed (a swinging foot moves faster than the body).")]
    public float plantSpeedShare = 0.8f;
    [Tooltip("A planted foot lets go once the clip moves it faster than this share of the body's ground speed.")]
    public float releaseSpeedShare = 1.6f;
    [Tooltip("Ground speed assumed when the body is slower than this, units/s (keeps the thresholds sane at a standstill).")]
    public float minGroundSpeed = 1.0f;
    [Tooltip("A foot may only plant while it is no more than this far above the other foot, world units.")]
    public float lowerFootMargin = 0.06f;
    [Tooltip("World units above its landing height at which a planted foot lets go (generous: a back step rolls the heel up ~11 cm while still planted).")]
    public float liftHeight = 0.2f;
    [Tooltip("Furthest the lock may hold a foot away from where the clip puts it, world units.")]
    public float maxCorrection = 0.45f;
    [Tooltip("Seconds to blend the lock in when the foot lands.")]
    public float plantTime = 0.04f;
    [Tooltip("Seconds to blend the lock out when the foot lifts.")]
    public float releaseTime = 0.10f;

    private Fighter fighter;
    private Animator animator;
    private Leg left, right;

    private class Leg
    {
        public Transform upper, lower, foot;
        public bool planted;
        public bool hasPrev;
        public Vector3 prev;                    // last frame's ANIMATED ankle (before this lock)
        public float speed;                     // the clip's ankle speed over the ground, units/s
        public float height;                    // animated ankle height above the root
        public float plantHeight;               // height it landed at
        public Vector3 anchor;                  // world spot the ankle is held on (x, z)
        public float weight;
    }

    private void Awake()
    {
        fighter = GetComponent<Fighter>();
        animator = GetComponent<Animator>();
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        left = Bind(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        right = Bind(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        if (left == null || right == null) enabled = false;
    }

    private Leg Bind(HumanBodyBones u, HumanBodyBones l, HumanBodyBones f)
    {
        var leg = new Leg { upper = animator.GetBoneTransform(u), lower = animator.GetBoneTransform(l), foot = animator.GetBoneTransform(f) };
        return leg.upper != null && leg.lower != null && leg.foot != null ? leg : null;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;   // paused or hit-stop: hold whatever was solved last frame's pose
        bool active = fighter != null && fighter.isActiveAndEnabled && fighter.FeetLockable;
        // Read BOTH animated feet first (the Animator has just written them; nothing here has yet).
        Sense(left, dt);
        Sense(right, dt);
        float ground = Mathf.Max(minGroundSpeed, fighter != null ? Mathf.Abs(fighter.GroundSpeed) : 0f);
        Step(left, right, active, ground, dt);
        Step(right, left, active, ground, dt);
    }

    private void Sense(Leg leg, float dt)
    {
        Vector3 p = leg.foot.position;
        Vector3 v = leg.hasPrev ? (p - leg.prev) / dt : Vector3.zero;
        v.y = 0f;
        leg.speed = v.magnitude;
        leg.prev = p;
        leg.hasPrev = true;
        leg.height = p.y - transform.position.y;
    }

    private void Step(Leg leg, Leg other, bool active, float ground, float dt)
    {
        Vector3 animated = leg.prev;
        bool slow = leg.speed < plantSpeedShare * ground;
        bool fast = leg.speed > releaseSpeedShare * ground;

        if (!active)
        {
            leg.planted = false;
        }
        else if (!leg.planted)
        {
            if (slow && (leg.speed <= other.speed || other.planted) && leg.height <= other.height + lowerFootMargin)
            {
                leg.planted = true;
                leg.anchor = animated;
                leg.plantHeight = leg.height;
            }
        }
        else if (fast || leg.height > leg.plantHeight + liftHeight)
        {
            leg.planted = false;
        }

        if (leg.planted)
        {
            Vector2 off = new Vector2(animated.x - leg.anchor.x, animated.z - leg.anchor.z);
            // The clip has wandered further than the leg should bend to follow: drag the anchor
            // after it (a slow creep) rather than letting go (which snapped the foot back).
            float m = off.magnitude;
            if (m > maxCorrection)
            {
                float k = (m - maxCorrection) / m;
                leg.anchor.x += off.x * k;
                leg.anchor.z += off.y * k;
            }
        }

        float goal = leg.planted ? 1f : 0f;
        float rate = leg.planted ? 1f / Mathf.Max(0.001f, plantTime) : 1f / Mathf.Max(0.001f, releaseTime);
        leg.weight = Mathf.MoveTowards(leg.weight, goal, rate * dt);
        if (leg.weight <= 0f) return;

        Vector3 target = new Vector3(leg.anchor.x, animated.y, leg.anchor.z);
        Quaternion footWas = leg.foot.rotation;
        // The knee bends in the plane the clip already poses (no pole), so it never flips.
        TwoHandGrip.SolveTwoBone(leg.upper, leg.lower, leg.foot, target, false, Vector3.zero, leg.weight);
        leg.foot.rotation = footWas;
    }
}
