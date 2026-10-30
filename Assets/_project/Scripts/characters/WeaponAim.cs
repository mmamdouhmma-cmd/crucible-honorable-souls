using UnityEngine;

/// <summary>
/// During an attack, tilts the weapon about its grip so the blade points at the opponent: the stock
/// (Mixamo) swings were made for other weapons and bodies, so a katana can end up pointing at the sky and
/// an axe chop can land a foot to the left. Weight ramps up through the startup, holds through the active
/// frames and fades out in recovery; the angle is capped so the wrist never looks broken. Runs after the
/// animation and IK (LateUpdate). The blade hit detection reads the final pose, so hits follow the aim.
/// </summary>
[RequireComponent(typeof(CombatSystem))]
public class WeaponAim : MonoBehaviour
{
    [Tooltip("Weapon transform (the object carrying the blade HitDetector). Found automatically if empty.")]
    [SerializeField] private Transform weapon;
    [Tooltip("Largest correction applied, degrees.")]
    [Range(0f, 150f)] [SerializeField] private float maxAngle = 110f;
    [Tooltip("Aim weight during the active frames; startup ramps to this, recovery fades from it.")]
    [Range(0f, 1f)] [SerializeField] private float weight = 1f;
    [Tooltip("Height on the opponent the blade aims at, as a fraction of their body height (0 feet, 1 head).")]
    [Range(0f, 1f)] [SerializeField] private float targetHeight = 0.55f;
    [Tooltip("Fade length in seconds after the active window ends.")]
    [SerializeField] private float fadeOut = 0.12f;

    [Header("Carry (while not attacking)")]
    [Tooltip("Hold the weapon on a fixed guard line while idle/walking. The Mixamo carry's WRIST rotates " +
             "through the cycle, and the weapon is parented to it, so without this the haft swings off the " +
             "guard on its own (head drifting behind the shoulder, butt poking out in front).")]
    [SerializeField] private bool carryPose = false;
    [Tooltip("Lay the haft along the line between the two hands, so a two-handed grip always has BOTH " +
             "hands on it whatever the clip is doing. Falls back to carryDirection if there is no left hand.")]
    [SerializeField] private bool carryAlongHands = true;
    [Tooltip("Guard line from the grip toward the blade, in fighter space (x right, y up, z forward), used " +
             "when carryAlongHands is off. Bjorn's axe guard: butt at the left hip, head over the right shoulder.")]
    [SerializeField] private Vector3 carryDirection = new Vector3(0.35f, 0.92f, 0.1f);
    [Range(0f, 1f)] [SerializeField] private float carryWeight = 1f;
    [Tooltip("Largest correction the carry may apply, degrees.")]
    [Range(0f, 180f)] [SerializeField] private float carryMaxAngle = 140f;
    [Tooltip("Extra lean added to the carry line, degrees, positive = blade leans further to the fighter's " +
             "right (butt swings toward the left hip). The hand line alone is as upright as the clip's arms.")]
    [Range(-60f, 60f)] [SerializeField] private float carryExtraTilt = 0f;
    [Tooltip("Extra lean added to the carry line, degrees, positive = blade leans FORWARD over the front " +
             "foot (butt swings back).")]
    [Range(-60f, 60f)] [SerializeField] private float carryForwardTilt = 0f;

    [Header("Wrists")]
    [Tooltip("Turn each hand so the fist baked around the haft actually lines up with it. The fists were " +
             "baked in Blender around the weapon's ORIGINAL mount axis, so re-mounting the axe left the " +
             "hands gripping air.")]
    [SerializeField] private bool alignWrists = false;
    [Tooltip("Direction the right fist grips along, in RIGHT HAND bone space (the original mount's haft axis).")]
    [SerializeField] private Vector3 fistGripAxisRight = new Vector3(-0.295f, 0f, 0.955f);
    [Tooltip("Direction the left fist grips along, in LEFT HAND bone space.")]
    [SerializeField] private Vector3 fistGripAxisLeft = new Vector3(-0.295f, 0f, 0.955f);
    [Range(0f, 1f)] [SerializeField] private float wristAlignWeight = 1f;
    [Tooltip("Largest wrist correction, degrees - past this the wrist starts to look broken.")]
    [Range(0f, 120f)] [SerializeField] private float wristMaxAngle = 70f;

    [Tooltip("Weapon-local axis running from the butt to the head. The weapon meshes keep Blender axes, " +
             "where Z is the handle axis (see CrucibleCharacterSetup) - the blade box's longest axis is no " +
             "use here because an axe head is widest ACROSS the haft.")]
    [SerializeField] private Vector3 haftAxisLocal = Vector3.forward;
    [SerializeField] private bool debugLogs = false;

    private CombatSystem combat;
    private Fighter me;
    private Vector3 bladeAxisLocal = Vector3.forward;
    private float fadeTimer;
    // The weapon is a static child of the hand bone: the Animator never rewrites its local rotation, so any
    // aim applied one frame would stay. Remember the rest orientation and restore it before each aim.
    private Quaternion restLocalRotation;
    private Vector3 restLocalPosition;
    private bool hasRest;
    // The weapon origin is not always the grip (Bjorn's axe is mounted part-way up the haft), so every
    // rotation pivots about the hand bone instead of the weapon's own origin.
    private Transform handBone;
    private Transform offHandBone;
    private TwoHandGrip twoHandGrip;

    private void Awake()
    {
        combat = GetComponent<CombatSystem>();
        me = GetComponent<Fighter>();
        twoHandGrip = GetComponent<TwoHandGrip>();
        if (weapon == null)
        {
            foreach (var hd in GetComponentsInChildren<HitDetector>(true))
                if (hd.FollowWeapon) { weapon = hd.transform; break; }
        }
        if (weapon != null)
        {
            restLocalRotation = weapon.localRotation; restLocalPosition = weapon.localPosition; hasRest = true;
            var box = weapon.GetComponent<BoxCollider>();
            if (box != null)
            {
                Vector3 s = box.size;
                bladeAxisLocal = s.x >= s.y && s.x >= s.z ? Vector3.right : (s.y >= s.z ? Vector3.up : Vector3.forward);
            }
        }
    }

    /// <summary>Rotates the weapon about a world pivot, carrying its position round with it.</summary>
    private void RotateAbout(Vector3 pivot, Quaternion rot)
    {
        weapon.rotation = rot * weapon.rotation;
        weapon.position = pivot + rot * (weapon.position - pivot);
    }

    private Vector3 Pivot => handBone != null ? handBone.position : weapon.position;

    /// <summary>Rolls a hand so the axis its fist was baked around points along the haft.</summary>
    private void AlignWrist(Transform hand, Vector3 gripAxisLocal, Vector3 haftDir, float w)
    {
        if (hand == null || w <= 1e-3f || gripAxisLocal.sqrMagnitude < 1e-6f) return;
        Vector3 cur = hand.TransformDirection(gripAxisLocal.normalized).normalized;
        float ang = Vector3.Angle(cur, haftDir);
        if (ang < 0.5f) return;
        var axis = Vector3.Cross(cur, haftDir);
        if (axis.sqrMagnitude < 1e-6f) return;
        hand.rotation = Quaternion.AngleAxis(Mathf.Min(ang, wristMaxAngle) * w, axis.normalized) * hand.rotation;
    }

    private void LateUpdate()
    {
        if (weapon == null || combat == null) return;
        if (handBone == null)
        {
            var anim = GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                handBone = anim.GetBoneTransform(HumanBodyBones.RightHand);
                offHandBone = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            }
        }
        if (hasRest) { weapon.localRotation = restLocalRotation; weapon.localPosition = restLocalPosition; }
        // The fighter's own Opponent, which ApplySides refreshes - a cached first-other-Fighter
        // here aimed every blade at a BENCHED bystander once the roster went past two (found
        // 2026-10-26: Buwaka's whole kit whiffed by ~1 unit with a perfect mount, because his
        // spear was pointing at Bjorn standing on his spawn mark).
        var opponent = me != null ? me.Opponent : null;
        if (opponent == null) return;
        float w = 0f;
        if (combat.IsAttacking)
        {
            var a = combat.CurrentAttack;
            float t = combat.AttackTime;
            float startup = a.StartupFrames * BalanceConfig.FRAME_TIME;
            float activeEnd = startup + a.ActiveFrames * BalanceConfig.FRAME_TIME;
            if (t < startup) w = Mathf.SmoothStep(0f, 1f, startup > 1e-4f ? t / startup : 1f);
            else if (t < activeEnd) w = 1f;
            else w = Mathf.Clamp01(1f - (t - activeEnd) / Mathf.Max(fadeOut, 1e-3f));
            fadeTimer = w;
        }
        else if (fadeTimer > 0f)
        {
            fadeTimer = Mathf.MoveTowards(fadeTimer, 0f, Time.deltaTime / Mathf.Max(fadeOut, 1e-3f));
            w = fadeTimer;
        }
        w *= weight;

        // Guard carry: whatever the clip's wrist is doing, put the haft back on the guard line. Fades out
        // as an attack's aim weight comes up, so the swing animation is never fought.
        if (carryPose && carryWeight > 1e-3f && w < 1f)
        {
            float cw = (1f - w) * carryWeight;
            Vector3 des = transform.TransformDirection(carryDirection.normalized);
            if (carryAlongHands && handBone != null && offHandBone != null)
            {
                // Butt-to-head runs from the off hand up through the gripping hand: both hands end up on
                // the haft however the clip moves them, which a fixed line cannot promise.
                // The off hand as the CLIP poses it, never the one this carry has already moved: the grip IK
                // pulls the hand onto the haft and the haft follows the hands, so reading the settled hand
                // feeds that loop back on itself (with a wide grip it walked both hands onto one side of the
                // body). Clip -> haft -> IK, one way only.
                Vector3 off = twoHandGrip != null && twoHandGrip.HasPreIkOffHand
                    ? twoHandGrip.PreIkOffHand : offHandBone.position;
                Vector3 line = handBone.position - off;
                if (line.sqrMagnitude > 1e-6f) des = line.normalized;
            }
            if (Mathf.Abs(carryExtraTilt) > 0.01f)
                des = Quaternion.AngleAxis(-carryExtraTilt * cw, transform.forward) * des;
            if (Mathf.Abs(carryForwardTilt) > 0.01f)
                des = Quaternion.AngleAxis(carryForwardTilt * cw, transform.right) * des;

            // Wrists first: they turn the hands only (bone origins do not move, so the carry line is the
            // same), and the weapon's world pose is rebuilt from the line right after.
            if (alignWrists && wristAlignWeight > 1e-3f)
            {
                AlignWrist(handBone, fistGripAxisRight, des, cw * wristAlignWeight);
                AlignWrist(offHandBone, fistGripAxisLeft, des, cw * wristAlignWeight);
            }

            Vector3 cur = weapon.TransformDirection(haftAxisLocal).normalized;
            float ca = Vector3.Angle(cur, des);
            if (ca > 0.5f)
            {
                var axis = Vector3.Cross(cur, des);
                if (axis.sqrMagnitude > 1e-6f)
                    RotateAbout(Pivot, Quaternion.AngleAxis(Mathf.Min(ca, carryMaxAngle) * cw, axis.normalized));
            }
        }

        if (w <= 1e-3f) return;

        // Aim at the visible body, not the physics capsule (that one is 2.1 x root scale tall, well above the head).
        var rend = opponent.GetComponentInChildren<SkinnedMeshRenderer>();
        Vector3 target;
        if (rend != null)
        {
            var b = rend.bounds;
            target = new Vector3(opponent.transform.position.x, Mathf.Lerp(b.min.y, b.max.y, targetHeight), opponent.transform.position.z);
        }
        else target = opponent.transform.position + Vector3.up * 1.5f;
        // A dodged combo aims straight ahead, not at where the dodger went.
        target = me.StrikeAimPoint(target);
        var atk = combat.CurrentAttack;
        AimMode mode = atk != null ? atk.AimMode : AimMode.Full;
        if (mode == AimMode.Off) { ApplyRoll(atk, w); return; }
        float cap = atk != null && atk.AimMaxAngle >= 0f ? atk.AimMaxAngle : maxAngle;
        Vector3 grip = weapon.position;
        Vector3 current = weapon.TransformDirection(bladeAxisLocal).normalized;
        Vector3 desired = (target - grip).normalized;
        float ang, applied;
        Quaternion rot;
        if (mode == AimMode.Level)
        {
            // Hold the blade LEVEL and pointed at the opponent. A spin whose blade wheels through
            // the vertical reads as a back fist rather than a cut - Kuruma's swings from -36 to +39
            // degrees - and neither Full nor YawOnly fixes it: Full aims at the chest, which from a
            // raised hand points the tip DOWN (measured -41 to -7), and YawOnly leaves the clip's
            // own wheel alone. This flattens the desired direction to the horizon first.
            Vector3 flat = desired; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) return;
            desired = flat.normalized;
            ang = Vector3.Angle(current, desired);
            if (ang < 0.5f) return;
            applied = Mathf.Min(ang, cap) * w;
            rot = Quaternion.AngleAxis(applied, Vector3.Cross(current, desired).normalized);
        }
        else if (mode == AimMode.YawOnly)
        {
            // Turn the blade about the vertical axis only, so a chop stays a chop but lands on the centre line.
            Vector3 c = current; c.y = 0f;
            Vector3 d = desired; d.y = 0f;
            if (c.sqrMagnitude < 1e-4f || d.sqrMagnitude < 1e-4f) return;
            ang = Vector3.SignedAngle(c.normalized, d.normalized, Vector3.up);
            if (Mathf.Abs(ang) < 0.5f) return;
            applied = Mathf.Clamp(ang, -cap, cap) * w;
            rot = Quaternion.AngleAxis(applied, Vector3.up);
        }
        else
        {
            ang = Vector3.Angle(current, desired);
            if (ang < 0.5f) return;
            applied = Mathf.Min(ang, cap) * w;
            rot = Quaternion.AngleAxis(applied, Vector3.Cross(current, desired).normalized);
        }
        RotateAbout(weapon.position, rot);   // the mount puts the origin at the hand, so this is the grip
        ApplyRoll(atk, w);
        if (debugLogs) Debug.Log($"{name} aim: off by {ang:F0} deg, applied {applied:F0} (w {w:F2}), roll {atk.AimRollDegrees * w:F0}");
    }

    /// <summary>
    /// Rolls the blade about its OWN long axis, so the edge - not the flat - leads the cut.
    /// The aim modes only choose where the tip POINTS; none of them turns the sword about its spine,
    /// which is why a spin can be aimed perfectly and still slap with the side of the blade. Rolling
    /// about the blade axis cannot move the tip, so it never fights the aim above, and it pivots on
    /// the hand so the grip stays put.
    /// </summary>
    private void ApplyRoll(AttackData atk, float w)
    {
        if (atk == null) return;
        float roll = atk.AimRollDegrees * w;
        if (Mathf.Abs(roll) < 0.01f) return;
        Vector3 axis = weapon.TransformDirection(bladeAxisLocal).normalized;
        if (axis.sqrMagnitude < 1e-6f) return;
        RotateAbout(Pivot, Quaternion.AngleAxis(roll, axis));
    }
}
