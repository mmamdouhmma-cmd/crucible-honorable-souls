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

    private Animator animator;

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

    private void OnAnimatorIK(int layerIndex)
    {
        if (leftGrip == null || animator == null) return;
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
