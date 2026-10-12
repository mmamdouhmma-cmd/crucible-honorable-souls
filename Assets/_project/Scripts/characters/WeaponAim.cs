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
    [Range(0f, 90f)] [SerializeField] private float maxAngle = 55f;
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

        var cap = opponent.GetComponent<CapsuleCollider>();
        Vector3 target = cap != null
            ? new Vector3(cap.bounds.center.x, Mathf.Lerp(cap.bounds.min.y, cap.bounds.max.y, targetHeight), cap.bounds.center.z)
            : opponent.transform.position + Vector3.up * 1.5f;
        Vector3 grip = weapon.position;
        Vector3 current = weapon.TransformDirection(bladeAxisLocal).normalized;
        Vector3 desired = (target - grip).normalized;
        float ang = Vector3.Angle(current, desired);
        if (ang < 0.5f) return;
        float applied = Mathf.Min(ang, maxAngle) * w;
        Quaternion rot = Quaternion.AngleAxis(applied, Vector3.Cross(current, desired).normalized);
        weapon.rotation = rot * weapon.rotation;
        if (debugLogs) Debug.Log($"{name} aim: off by {ang:F0} deg, applied {applied:F0} (w {w:F2})");
    }
}
