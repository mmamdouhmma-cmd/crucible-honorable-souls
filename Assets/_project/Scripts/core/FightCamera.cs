using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Fighting-game camera. Stays side-on to the line between the two fighters,
/// centred on their midpoint, and pulls back as they move apart. The framing set
/// up in the scene (height, distance, tilt) is kept as the starting view.
/// Also owns the impact feel: shake, punch-in and a short slow-motion beat, all
/// cosmetic (they never touch fighter state or frame data). Attach to the Main Camera.
/// </summary>
public class FightCamera : MonoBehaviour
{
    public static FightCamera Instance { get; private set; }

    [Header("Targets")]
    [Tooltip("Leave empty to find the fighters in the scene automatically by their Player Number.")]
    [FormerlySerializedAs("fighterA")]
    [SerializeField] private Transform player1;
    [FormerlySerializedAs("fighterB")]
    [SerializeField] private Transform player2;

    [Header("Follow")]
    [Tooltip("How quickly the camera catches up with the fighters. Higher = snappier.")]
    [SerializeField] private float followSharpness = 8f;

    [Tooltip("Camera stops re-angling while the fighters are closer than this, " +
             "so a dash through the opponent swaps screen sides instead of spinning the camera.")]
    [SerializeField] private float passThroughDistance = 1.5f;

    [Header("Zoom")]
    [Tooltip("Extra camera distance per unit the fighters move apart beyond their starting gap.")]
    [SerializeField] private float zoomOutPerUnit = 0.5f;
    [SerializeField] private float maxZoomOut = 10f;

    [Header("Impact feel")]
    [Tooltip("Shake amplitude (world units) for a hit that does 10 damage. Scales with damage.")]
    [SerializeField] private float shakePerTenDamage = 0.12f;
    [SerializeField] private float shakeDuration = 0.18f;
    [Tooltip("Degrees of field-of-view punch-in on a heavy hit.")]
    [SerializeField] private float heavyPunchIn = 4f;
    [SerializeField] private float punchDuration = 0.22f;
    [Tooltip("Time scale during the slow-motion beat on a heavy hit or a clash (1 = off).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float heavySlowMo = 0.45f;
    [Tooltip("Real seconds the slow-motion beat lasts.")]
    [SerializeField] private float slowMoDuration = 0.14f;
    [SerializeField] private bool punchInOnHeavy = false;
    [SerializeField] private bool slowMoOnHeavy = false;
    [SerializeField] private bool clashShake = false;

    private Vector3 localOffset;      // camera position relative to the pair at start
    private Quaternion localRotation; // camera rotation relative to the pair at start
    private float startSeparation;
    private Vector3 sideDirection;    // ground direction from the midpoint toward the camera
    private bool initialized;

    private Camera cam;
    private float baseFov;
    private float shakeAmount;
    private float shakeTimer;
    private float punchAmount;
    private float punchTimer;
    private float slowMoTimer;        // unscaled seconds left
    private bool slowMoRunning;

    private void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        if (cam != null) baseFov = cam.fieldOfView;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (slowMoRunning) Time.timeScale = 1f;
    }

    private void Start()
    {
        if (player1 == null || player2 == null)
        {
            foreach (Fighter fighter in FindObjectsOfType<Fighter>())
            {
                if (player1 == null && fighter.PlayerNumber == 1) player1 = fighter.transform;
                else if (player2 == null && fighter.PlayerNumber == 2) player2 = fighter.transform;
            }
        }

        if (player1 == null || player2 == null)
        {
            Debug.LogWarning($"FightCamera on {name}: needs two fighters to follow, camera will stay still.");
            return;
        }

        Vector3 midpoint = (player1.position + player2.position) * 0.5f;
        Vector3 toPlayer2 = player2.position - player1.position;
        toPlayer2.y = 0f;
        startSeparation = toPlayer2.magnitude;

        // Fighters stacked on the same spot: fall back to the camera's own right as their line.
        Vector3 axis = startSeparation > 0.001f ? toPlayer2 / startSeparation : transform.right;

        sideDirection = Vector3.Cross(Vector3.up, axis).normalized;
        if (Vector3.Dot(transform.position - midpoint, sideDirection) < 0f)
        {
            sideDirection = -sideDirection;
        }

        // Remember the scene's framing relative to the pair so it can be replayed as they move.
        Quaternion frame = Quaternion.LookRotation(-sideDirection, Vector3.up);
        localOffset = Quaternion.Inverse(frame) * (transform.position - midpoint);
        localRotation = Quaternion.Inverse(frame) * transform.rotation;

        initialized = true;
    }

    // ==================== impact feel ====================

    /// <summary>
    /// Called by <see cref="CombatSystem"/> when a strike lands. Shake scales with damage; a
    /// heavy that connects unblocked also punches the lens in and plays a slow-motion beat.
    /// </summary>
    public void OnImpact(int damage, AttackType type, bool blocked)
    {
        // Only an unblocked heavy (or special) moves the camera. Punch-in and slow motion are
        // available (punchIn / slowMo toggles) but off: the fight reads cleaner without them.
        if (blocked || (type != AttackType.Heavy && type != AttackType.Special)) return;

        Shake(shakePerTenDamage * (Mathf.Max(damage, 1) / 10f), shakeDuration);
        if (punchInOnHeavy) PunchIn(heavyPunchIn, punchDuration);
        if (slowMoOnHeavy) SlowMo(heavySlowMo, slowMoDuration);
    }

    /// <summary>Called when two attacks clash. Off by default (clashShake).</summary>
    public void OnClash()
    {
        if (!clashShake) return;
        Shake(shakePerTenDamage * 2f, shakeDuration * 1.3f);
    }

    public void Shake(float amplitude, float seconds)
    {
        if (amplitude <= 0f) return;
        shakeAmount = Mathf.Max(shakeAmount, amplitude);
        shakeTimer = Mathf.Max(shakeTimer, seconds);
    }

    public void PunchIn(float degrees, float seconds)
    {
        punchAmount = Mathf.Max(punchAmount, degrees);
        punchTimer = Mathf.Max(punchTimer, seconds);
    }

    public void SlowMo(float timeScale, float seconds)
    {
        if (timeScale >= 1f || seconds <= 0f) return;
        Time.timeScale = Mathf.Min(Time.timeScale, timeScale);
        slowMoTimer = Mathf.Max(slowMoTimer, seconds);
        slowMoRunning = true;
    }

    private void Update()
    {
        // Slow motion runs on unscaled time so it ends on schedule regardless of the time scale.
        if (slowMoRunning)
        {
            slowMoTimer -= Time.unscaledDeltaTime;
            if (slowMoTimer <= 0f)
            {
                Time.timeScale = 1f;
                slowMoRunning = false;
            }
        }
    }

    // LateUpdate so the fighters have already moved this frame.
    private void LateUpdate()
    {
        if (!initialized || player1 == null || player2 == null) return;

        Vector3 midpoint = (player1.position + player2.position) * 0.5f;
        Vector3 toPlayer2 = player2.position - player1.position;
        toPlayer2.y = 0f;
        float separation = toPlayer2.magnitude;

        if (separation > passThroughDistance)
        {
            Vector3 side = Vector3.Cross(Vector3.up, toPlayer2 / separation);

            // Stay on the side of the fight the camera is already on.
            if (Vector3.Dot(side, sideDirection) < 0f)
            {
                side = -side;
            }
            sideDirection = side;
        }

        Quaternion frame = Quaternion.LookRotation(-sideDirection, Vector3.up);
        float zoomOut = Mathf.Clamp((separation - startSeparation) * zoomOutPerUnit, 0f, maxZoomOut);

        Vector3 targetPosition = midpoint + frame * (localOffset + Vector3.back * zoomOut);
        Quaternion targetRotation = frame * localRotation;

        // Frame-rate independent smoothing (unscaled so slow motion doesn't make the camera lag).
        float dt = Time.unscaledDeltaTime;
        float t = 1f - Mathf.Exp(-followSharpness * dt);
        transform.position = Vector3.Lerp(transform.position, targetPosition, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);

        // Shake: decaying random offset in the camera's plane.
        if (shakeTimer > 0f)
        {
            shakeTimer -= dt;
            float k = Mathf.Clamp01(shakeTimer / shakeDuration);
            Vector2 r = Random.insideUnitCircle * shakeAmount * k * k;
            transform.position += transform.right * r.x + transform.up * r.y;
            if (shakeTimer <= 0f) shakeAmount = 0f;
        }

        // Punch-in: quick zoom that eases back out.
        if (cam != null)
        {
            float punch = 0f;
            if (punchTimer > 0f)
            {
                punchTimer -= dt;
                float k = Mathf.Clamp01(punchTimer / punchDuration);
                punch = punchAmount * Mathf.Sin(k * Mathf.PI * 0.5f);
                if (punchTimer <= 0f) punchAmount = 0f;
            }
            cam.fieldOfView = baseFov - punch;
        }
    }
}
