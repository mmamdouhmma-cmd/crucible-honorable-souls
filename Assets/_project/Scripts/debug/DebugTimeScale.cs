using UnityEngine;

/// <summary>
/// Debug slow-motion: scales Time.timeScale so animation and the frame-data timers slow together,
/// which makes it possible to watch whether a weapon actually crosses the target on its active frames.
/// Sits on the GameMangers object; untick to restore real time.
/// </summary>
public class DebugTimeScale : MonoBehaviour
{
    [Tooltip("Game speed. 0.85 = 15% slower than real time.")]
    [Range(0.1f, 2f)] public float timeScale = 0.85f;

    private void OnEnable() { Apply(); }
    private void OnValidate() { if (Application.isPlaying && enabled) Apply(); }
    private void OnDisable() { Time.timeScale = 1f; }
    // FightCamera's slow-mo beats reset timeScale to 1 when they end; re-apply the debug scale then.
    private void LateUpdate() { if (Mathf.Approximately(Time.timeScale, 1f)) Apply(); }

    private void Apply()
    {
        Time.timeScale = timeScale;
    }
}
