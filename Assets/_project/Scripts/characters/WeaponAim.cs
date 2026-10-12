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
    [SerializeField] private bool debugLogs = false;

    private CombatSystem combat;
    private Fighter me, opponent;
    private Vector3 bladeAxisLocal = Vector3.forward;
    private float fadeTimer;
    // The weapon is a static child of the hand bone: the Animator never rewrites its local rotation, so any
    // aim applied one frame would stay. Remember the rest orientation and restore it before each aim.
    private Quaternion restLocalRotation;
    private bool hasRest;

    private void Awake()
    {
        combat = GetComponent<CombatSystem>();
        me = GetComponent<Fighter>();
        if (weapon == null)
        {
            foreach (var hd in GetComponentsInChildren<HitDetector>(true))
                if (hd.FollowWeapon) { weapon = hd.transform; break; }
        }
        if (weapon != null)
        {
            restLocalRotation = weapon.localRotation; hasRest = true;
            var box = weapon.GetComponent<BoxCollider>();
            if (box != null)
            {
                Vector3 s = box.size;
                bladeAxisLocal = s.x >= s.y && s.x >= s.z ? Vector3.right : (s.y >= s.z ? Vector3.up : Vector3.forward);
            }
        }
    }

    private void LateUpdate()
    {
        if (weapon == null || combat == null) return;
        if (hasRest) weapon.localRotation = restLocalRotation;   // undo last frame's aim before computing this one
        if (opponent == null)
        {
            foreach (var f in FindObjectsByType<Fighter>(FindObjectsSortMode.None)) if (f != me) { opponent = f; break; }
            if (opponent == null) return;
        }
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
        var atk = combat.CurrentAttack;
        AimMode mode = atk != null ? atk.AimMode : AimMode.Full;
        if (mode == AimMode.Off) return;
        float cap = atk != null && atk.AimMaxAngle >= 0f ? atk.AimMaxAngle : maxAngle;
        Vector3 grip = weapon.position;
        Vector3 current = weapon.TransformDirection(bladeAxisLocal).normalized;
        Vector3 desired = (target - grip).normalized;
        float ang, applied;
        Quaternion rot;
        if (mode == AimMode.YawOnly)
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
        weapon.rotation = rot * weapon.rotation;
        if (debugLogs) Debug.Log($"{name} aim: off by {ang:F0} deg, applied {applied:F0} (w {w:F2})");
    }
}
