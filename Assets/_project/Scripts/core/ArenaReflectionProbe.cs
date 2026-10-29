using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One reflection probe per arena, captured ONCE each time its arena switches on.
/// Six arenas share one scene and one lighting bake, and each arena's sky, sun and fog only exist
/// while its <see cref="ArenaAtmosphere"/> is enabled - so a baked probe would capture whichever
/// arena happened to be active at bake time. Capturing on enable (after the atmosphere has taken
/// over the render settings) gets the right sky and walls, for one six-face render at arena
/// switch and nothing per frame.
/// The capture is a synchronous <c>RenderToCubemap</c> with every fighter's renderers forced off
/// for that call only, so the fighters never reflect in their own armour (or as wherever the
/// select screens had posed them) and the main camera never sees them disappear.
/// Placed by <c>Crucible > Look > Visual quality pass</c>.
/// </summary>
[RequireComponent(typeof(ReflectionProbe))]
public class ArenaReflectionProbe : MonoBehaviour
{
    [Tooltip("Frames to wait after the arena switches on, so ArenaAtmosphere has applied its sky first.")]
    [SerializeField] private int settleFrames = 2;
    [SerializeField] private int resolution = 256;
    [Tooltip("Capture point height above the arena floor found under the probe (a raycast at capture time: " +
             "the arena roots do not sit on their floors).")]
    [SerializeField] private float heightAboveFloor = 2.5f;

    private ReflectionProbe probe;
    private RenderTexture cube;

    private void Awake()
    {
        probe = GetComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
    }

    private void OnEnable() => StartCoroutine(CaptureSoon());

    private void OnDestroy()
    {
        if (cube != null) { cube.Release(); Destroy(cube); }
    }

    private IEnumerator CaptureSoon()
    {
        for (int i = 0; i < settleFrames; i++) yield return null;
        Capture();
    }

    public void Capture()
    {
        if (probe == null || !isActiveAndEnabled) return;
        if (cube == null)
        {
            cube = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGBHalf)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
                name = name + "_Cube",
            };
            cube.Create();
        }

        var hidden = new System.Collections.Generic.List<Renderer>();
        foreach (var f in FindObjectsByType<Fighter>(FindObjectsInactive.Exclude))
            foreach (var r in f.GetComponentsInChildren<Renderer>())
                if (!r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }

        float floor = transform.position.y - heightAboveFloor;
        bool found = false;
        foreach (var hit in Physics.RaycastAll(transform.position + Vector3.up * 40f, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<Fighter>() != null) continue;
            if (!found || hit.point.y > floor) { floor = hit.point.y; found = true; }
        }
        transform.position = new Vector3(transform.position.x, floor + heightAboveFloor, transform.position.z);

        var go = new GameObject("ArenaProbeCapture") { hideFlags = HideFlags.HideAndDontSave };
        go.transform.position = transform.position;
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = Mathf.Max(1000f, probe.farClipPlane);
        cam.clearFlags = CameraClearFlags.Skybox;
        try { cam.RenderToCubemap(cube); }
        finally
        {
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            Destroy(go);
        }
        probe.customBakedTexture = cube;
    }
}
