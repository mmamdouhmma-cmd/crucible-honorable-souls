using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Fighting-game camera. Stays side-on to the line between the two fighters,
/// centred on their midpoint, and pulls back as they move apart. The framing set
/// up in the scene (height, distance, tilt) is kept as the starting view.
/// Attach to the Main Camera.
/// </summary>
public class FightCamera : MonoBehaviour
{
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

    private Vector3 localOffset;      // camera position relative to the pair at start
    private Quaternion localRotation; // camera rotation relative to the pair at start
    private float startSeparation;
    private Vector3 sideDirection;    // ground direction from the midpoint toward the camera
    private bool initialized;

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

        // Frame-rate independent smoothing.
        float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPosition, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
    }
}
