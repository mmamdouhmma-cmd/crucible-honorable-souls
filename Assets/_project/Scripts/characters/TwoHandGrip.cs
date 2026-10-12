using UnityEngine;

/// <summary>
/// Keeps the off hand on the weapon in every animation: Humanoid IK pulls the left hand to a socket
/// transform on the weapon ("LeftGrip", placed on the haft / tsuka by the character setup). Lets a
/// one-handed Mixamo clip read as a two-handed grip. Needs "IK Pass" on the animator layer; the
/// setup menu turns it on.
/// </summary>
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
    private Fighter me, opponent;
    private Transform weapon;
    private float bladeLength;
    private float armLength;
    private float reachFade;

    /// <summary>Debug: how many times OnAnimatorIK has run on this component (0 = the layer has no IK pass at runtime).</summary>
    public int IkCalls { get; private set; }
    /// <summary>Debug: last distance from the animated left hand to the socket before IK was applied.</summary>
    public float LastPreIkDistance { get; private set; }

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
        if (opponent == null)
        {
            foreach (var f in FindObjectsByType<Fighter>(FindObjectsSortMode.None)) if (f != me) { opponent = f; break; }
            if (opponent == null) return;
        }
        var rend = opponent.GetComponentInChildren<SkinnedMeshRenderer>();
        Vector3 target = rend != null
            ? new Vector3(opponent.transform.position.x, Mathf.Lerp(rend.bounds.min.y, rend.bounds.max.y, targetHeight), opponent.transform.position.z)
            : opponent.transform.position + Vector3.up * 1.5f;
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
        LastPreIkDistance = (animator.GetIKPosition(AvatarIKGoal.LeftHand) - leftGrip.position).magnitude;
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, positionWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, leftGrip.position);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rotationWeight);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, leftGrip.rotation);
        if (leftElbowHint != null)
        {
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 1f);
            animator.SetIKHintPosition(AvatarIKHint.LeftElbow, leftElbowHint.position);
        }
    }
}
