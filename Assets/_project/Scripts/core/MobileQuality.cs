using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Lowers the render cost on phones and in the web build so the test build runs at a playable frame rate:
/// render scale, shadows, post-processing and anti-aliasing. Desktop editor/standalone untouched.
/// </summary>
public class MobileQuality : MonoBehaviour
{
    [Tooltip("Apply the low settings even on desktop (to preview the mobile look).")]
    public bool force = false;
    [Range(0.4f, 1f)] public float renderScale = 0.7f;
    public float shadowDistance = 18f;
    public bool postProcessing = false;

    void Awake()
    {
        bool mobile = Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer || force;
        if (!mobile) return;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            urp.renderScale = renderScale;
            urp.shadowDistance = shadowDistance;
            urp.msaaSampleCount = 1;
        }
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) continue;
            data.renderPostProcessing = postProcessing;
            data.antialiasing = AntialiasingMode.None;
        }
        Debug.Log($"MobileQuality: render scale {renderScale}, shadows {shadowDistance}, post {postProcessing}");
    }
}
