using UnityEngine;

/// <summary>
/// A blade trail that only shows while a strike's hitbox is live. Built at runtime from the
/// Humanoid avatar: the emitter sits past the weapon hand along the forearm line, roughly
/// where the blade is, and a <see cref="TrailRenderer"/> draws the arc. Cosmetic only.
/// </summary>
public class WeaponTrail : MonoBehaviour
{
    [Tooltip("How far past the hand (in body radii) the trail is emitted: the blade's reach.")]
    [SerializeField] private float bladeLengthRadii = 2.2f;
    [Tooltip("Seconds the arc stays visible behind the blade.")]
    [SerializeField] private float trailTime = 0.12f;
    [SerializeField] private Color trailColor = new Color(1f, 0.95f, 0.85f, 0.55f);
    [SerializeField] private bool twoHanded = false;

    private Fighter fighter;
    private Transform hand, forearm, otherHand;
    private TrailRenderer trail;
    private Transform emitter;

    private void Awake()
    {
        fighter = GetComponent<Fighter>();
        Animator animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) { enabled = false; return; }

        hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        forearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        otherHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (hand == null || forearm == null) { enabled = false; return; }

        emitter = new GameObject("BladeTrail").transform;
        emitter.SetParent(transform, false);
        trail = emitter.gameObject.AddComponent<TrailRenderer>();
        trail.time = trailTime;
        trail.minVertexDistance = 0.02f;
        trail.autodestruct = false;
        trail.emitting = false;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.material = VfxMaterials.Unlit(trailColor);
        trail.startColor = trailColor;
        trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);

        float r = fighter != null ? Mathf.Max(fighter.BodyRadius, 0.2f) : 0.6f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, r * 0.55f), new Keyframe(1f, r * 0.05f));
        trail.widthMultiplier = 1f;
    }

    /// <summary>Called by <see cref="CombatSystem"/> when the hitbox arms.</summary>
    public void Begin()
    {
        if (trail == null) return;
        Place();
        trail.Clear();
        trail.emitting = true;
    }

    /// <summary>Called when the active window closes.</summary>
    public void End()
    {
        if (trail != null) trail.emitting = false;
    }

    private void LateUpdate()
    {
        if (trail != null && trail.emitting) Place();
    }

    private void Place()
    {
        float r = fighter != null ? Mathf.Max(fighter.BodyRadius, 0.2f) : 0.6f;
        Vector3 alongArm = (hand.position - forearm.position).normalized;
        Vector3 origin = hand.position;
        if (twoHanded && otherHand != null)
        {
            origin = (hand.position + otherHand.position) * 0.5f;
        }
        emitter.position = origin + alongArm * (r * bladeLengthRadii);
    }
}
