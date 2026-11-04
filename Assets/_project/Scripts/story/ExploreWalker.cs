using UnityEngine;

/// <summary>
/// Free walking for the story's village walk (decision 9: walk anywhere, dashes as in the fight). The duel
/// controller is NOT touched: while this runs the fighter's <see cref="Fighter"/> component is switched off and this
/// moves the body itself - 2-D, relative to the camera, on the same bindings (left stick / move keys, the run, the
/// dash). The Rigidbody goes kinematic and the feet follow the ground by raycast; walls and props refuse the step
/// (the same step-height capsule test the fighters use). A follow camera replaces the fight camera.
/// <see cref="End"/> puts everything back.
/// </summary>
public class ExploreWalker : MonoBehaviour
{
    [Tooltip("Camera distance behind the walker.")] public float camDistance = 7.5f;
    [Tooltip("Camera height above the walker's feet.")] public float camHeight = 3.6f;
    [Tooltip("Where the camera looks, above the feet.")] public float lookHeight = 1.9f;
    [Tooltip("Degrees per second the camera swings round behind a walker who is moving.")] public float camFollowRate = 70f;
    [Tooltip("Turn rate of the walker, degrees per second.")] public float turnRate = 540f;
    [Tooltip("Dash length in seconds and its speed as a multiple of the run.")] public float dashTime = 0.28f, dashSpeedScale = 1.5f;
    [Tooltip("Tallest step he walks up (a sill, a porch step). The fight's knee-height rule is 0.6 - too low for the " +
             "village: the dojo gate's sill is 0.84.")] public float stepHeight = 0.95f;

    Fighter fighter;
    Rigidbody body;
    Animator anim;
    Camera cam;
    FightCamera fightCam;
    FightHud hud;
    bool wasKinematic, active;
    float camYaw, dashUntil, dashReadyAt;
    Vector3 dashDir;

    public bool Active => active;

    public static ExploreWalker Begin(Fighter f)
    {
        var w = f.GetComponent<ExploreWalker>() ?? f.gameObject.AddComponent<ExploreWalker>();
        w.Start(f);
        return w;
    }

    void Start(Fighter f)
    {
        fighter = f;
        body = f.GetComponent<Rigidbody>();
        anim = f.GetComponent<Animator>();
        cam = Camera.main;
        fightCam = FightCamera.Instance;
        if (fightCam != null) fightCam.enabled = false;
        if (body != null) { wasKinematic = body.isKinematic; body.isKinematic = true; }
        fighter.enabled = false;
        camYaw = f.transform.eulerAngles.y;
        active = true;
        SnapCamera();
    }

    /// <summary>Hands the fighter and the camera back to the duel code.</summary>
    public void End()
    {
        if (!active) return;
        active = false;
        if (anim != null && anim.runtimeAnimatorController != null) { anim.SetFloat("MoveSpeed", 0f); anim.SetBool("Running", false); }
        if (body != null) body.isKinematic = wasKinematic;
        fighter.enabled = true;
        if (fightCam != null) fightCam.enabled = true;
    }

    void OnDestroy() => End();

    void Update()
    {
        if (!active) return;
        if (fighter.enabled) fighter.enabled = false;   // the pause menu's resume switches fighters back on
        if (hud == null) hud = FindAnyObjectByType<FightHud>();
        if (hud != null && hud.IsVisible) hud.SetVisible(false);   // ...and the health bars: there is no fight here
        var im = InputManager.Instance;
        InputData input = im != null && im.InputEnabled && Time.timeScale > 0f ? im.GetPlayer1Input() : default;

        // stick / keys relative to the camera, flattened onto the ground
        Vector2 mv = input.moveVector;
        if (mv.sqrMagnitude < 0.0001f && !Mathf.Approximately(input.moveDirection, 0f)) mv = new Vector2(input.moveDirection, 0f);
        Quaternion yawRot = Quaternion.Euler(0f, camYaw, 0f);
        Vector3 want = yawRot * new Vector3(mv.x, 0f, mv.y);
        float amount = Mathf.Clamp01(want.magnitude);
        if (amount > 0.01f) want /= want.magnitude; else want = Vector3.zero;

        bool running = amount > 0.5f && (input.run || input.runHeld);
        float speed = running ? fighter.RunGroundSpeed : fighter.WalkSpeed;

        // dash: a burst along the stick (or straight ahead), as in the fight
        if (input.dashDirection != Vector2.zero && Time.time >= dashReadyAt)
        {
            Vector2 d = input.dashDirection;
            dashDir = want != Vector3.zero ? want : (yawRot * new Vector3(d.x, 0f, d.y)).normalized;
            if (dashDir == Vector3.zero) dashDir = transform.forward;
            dashUntil = Time.time + dashTime;
            dashReadyAt = dashUntil + 0.15f;
            if (anim != null) anim.CrossFadeInFixedTime("Dash_Forward", 0.05f);
        }
        Vector3 step;
        if (Time.time < dashUntil) { step = dashDir * fighter.RunGroundSpeed * dashSpeedScale * Time.deltaTime; want = dashDir; amount = 1f; }
        else step = want * speed * amount * Time.deltaTime;

        Vector3 from = transform.position;
        if (step.sqrMagnitude > 0f && !Blocked(from, step)) transform.position = from + step;
        Ground();

        if (want != Vector3.zero)
        {
            Quaternion face = Quaternion.LookRotation(want, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, face, turnRate * Time.deltaTime);
            // the camera swings round behind a moving walker, gently
            camYaw = Mathf.MoveTowardsAngle(camYaw, transform.eulerAngles.y, camFollowRate * amount * Time.deltaTime);
        }
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            anim.SetFloat("MoveSpeed", amount);
            anim.SetBool("Running", running && Time.time >= dashUntil);
        }
    }

    void LateUpdate()
    {
        if (!active || cam == null) return;
        Vector3 target = transform.position + Vector3.up * lookHeight;
        Vector3 desired = transform.position + Quaternion.Euler(0f, camYaw, 0f) * new Vector3(0f, camHeight, -camDistance);
        // pull in rather than look through a wall or a roof
        Vector3 toCam = desired - target;
        if (Physics.SphereCast(target, 0.3f, toCam.normalized, out var hit, toCam.magnitude, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Fighter>() == null)
            desired = target + toCam.normalized * Mathf.Max(1.5f, hit.distance - 0.1f);
        cam.transform.position = Vector3.Lerp(cam.transform.position, desired, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
        cam.transform.rotation = Quaternion.LookRotation(target - cam.transform.position, Vector3.up);
    }

    void SnapCamera()
    {
        if (cam == null) return;
        Vector3 target = transform.position + Vector3.up * lookHeight;
        cam.transform.position = transform.position + Quaternion.Euler(0f, camYaw, 0f) * new Vector3(0f, camHeight, -camDistance);
        cam.transform.rotation = Quaternion.LookRotation(target - cam.transform.position, Vector3.up);
    }

    // ---- the ground and the walls

    readonly RaycastHit[] hits = new RaycastHit[16];
    readonly Collider[] overlaps = new Collider[16];

    void Ground()
    {
        Vector3 p = transform.position;
        int n = Physics.RaycastNonAlloc(p + Vector3.up * (stepHeight + 0.2f), Vector3.down, hits, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.GetComponentInParent<Fighter>() != null) continue;
            if (hits[i].point.y > best) best = hits[i].point.y;
        }
        if (!float.IsNegativeInfinity(best)) transform.position = new Vector3(p.x, best, p.z);
    }

    bool Blocked(Vector3 from, Vector3 step)
    {
        float dist = step.magnitude;
        Vector3 dir = step / dist;
        float r = Mathf.Max(0.05f, fighter.BodyRadius * 0.9f);
        Vector3 a = from + Vector3.up * (stepHeight + r);
        Vector3 b = from + Vector3.up * Mathf.Max(stepHeight + r, BalanceConfig.OBSTACLE_PROBE_TOP - r);
        int n = Physics.CapsuleCastNonAlloc(a, b, r, dir, hits, dist + 0.02f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (hits[i].distance <= 0f && hits[i].point == Vector3.zero) continue;   // started inside: let him walk out
            if (hits[i].collider.GetComponentInParent<Fighter>() != null) continue;
            return true;
        }
        // the end of the step must be clear too (a cast that starts inside a surface reports nothing),
        // unless he started inside something - then any step is allowed so he can always get out
        return Inside(from + step, r) && !Inside(from, r);
    }

    bool Inside(Vector3 p, float r)
    {
        int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (stepHeight + r),
            p + Vector3.up * Mathf.Max(stepHeight + r, BalanceConfig.OBSTACLE_PROBE_TOP - r), r, overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (overlaps[i].GetComponentInParent<Fighter>() == null) return true;
        return false;
    }
}
