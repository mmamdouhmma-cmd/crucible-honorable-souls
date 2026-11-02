using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The killing-blow presentation. The moment a hit takes the last of a fighter's health:
///  - time slows to a fifth of match speed so the final swing and the fall read, then ramps back;
///  - every light in the scene and the ambient dim to near-black, so the arena goes dark while the
///    blood keeps its colour (the blood particles are UNLIT, so they ignore the dimming);
///  - an extra gout of blood marks the blow itself.
/// <see cref="CombatSystem"/> calls <see cref="Begin"/>; <see cref="GameMenu"/> calls
/// <see cref="RestoreLights"/> when a new match starts or a menu screen comes back up. The K.O.
/// screen itself stays dark on purpose - the corpse under a black sky is the shot.
/// </summary>
public static class KillShot
{
    private const float SLOW_FRACTION = 0.2f;   // of the match speed, while the kill plays out
    private const float SLOW_HOLD = 0.9f;       // real seconds held at full slow
    private const float SLOW_RAMP = 0.8f;       // real seconds ramping back up to match speed
    private const float DARKEN_TIME = 0.35f;    // real seconds fading the lights down
    private const float RESTORE_TIME = 0.6f;    // real seconds fading them back
    private const float LIGHT_KEEP = 0.10f;     // fraction of each light's intensity kept
    private const float AMBIENT_KEEP = 0.15f;   // fraction of the ambient kept

    /// <summary>Hidden coroutine host; scene-bound so everything dies with the scene.</summary>
    private class Runner : MonoBehaviour { }

    private static Runner runner;
    private static readonly List<(Light light, float intensity)> dimmed = new List<(Light, float)>();
    private static float ambientBefore;
    private static Material skyboxBefore;      // the original asset; a clone is dimmed in its place
    private static Material skyboxClone;
    private static bool dark;
    private static Coroutine timeRoutine, lightRoutine;

    /// <summary>True from the killing blow until the lights are restored.</summary>
    public static bool IsActive => dark;

    public static void Begin(Fighter loser, Fighter attacker, Color bloodColor)
    {
        if (dark) return;   // a double K.O. gets one presentation
        EnsureRunner();
        NetSession.ForwardKill(loser, attacker, bloodColor);   // the online guest runs its own, blood and all

        // The blow itself bleeds, blocked or not - a killing hit through a guard still kills.
        if (loser != null && attacker != null)
        {
            NetSession.MuteForward = true;
            HitEffects.SpawnBlood(loser, attacker, bloodColor);
            HitEffects.SpawnBlood(loser, attacker, bloodColor);
            NetSession.MuteForward = false;
        }

        dark = true;
        float matchScale = Mathf.Max(0.05f, Time.timeScale);
        if (timeRoutine != null) runner.StopCoroutine(timeRoutine);
        if (lightRoutine != null) runner.StopCoroutine(lightRoutine);
        timeRoutine = runner.StartCoroutine(SlowMo(matchScale));
        lightRoutine = runner.StartCoroutine(FadeLights(LIGHT_KEEP, AMBIENT_KEEP, DARKEN_TIME, capture: true));
    }

    /// <summary>Brings the lights (and the sky) back. Safe to call any time; does nothing when not dark.</summary>
    public static void RestoreLights()
    {
        if (!dark) return;
        dark = false;
        if (runner == null) { RestoreNow(); return; }
        if (lightRoutine != null) runner.StopCoroutine(lightRoutine);
        lightRoutine = runner.StartCoroutine(FadeLights(1f, 1f, RESTORE_TIME, capture: false, thenRelease: true));
    }

    private static void EnsureRunner()
    {
        if (runner != null) return;
        runner = new GameObject("KillShot").AddComponent<Runner>();
    }

    private static IEnumerator SlowMo(float matchScale)
    {
        Time.timeScale = matchScale * SLOW_FRACTION;
        yield return new WaitForSecondsRealtime(SLOW_HOLD);
        float t = 0f;
        while (t < SLOW_RAMP)
        {
            t += Time.unscaledDeltaTime;
            Time.timeScale = Mathf.Lerp(matchScale * SLOW_FRACTION, matchScale, t / SLOW_RAMP);
            yield return null;
        }
        Time.timeScale = matchScale;
        timeRoutine = null;
    }

    private static IEnumerator FadeLights(float lightKeep, float ambientKeep, float seconds, bool capture, bool thenRelease = false)
    {
        if (capture)
        {
            dimmed.Clear();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.isActiveAndEnabled) dimmed.Add((l, l.intensity));
            ambientBefore = RenderSettings.ambientIntensity;

            // The sky is not lit by the lights, so it gets its own dimming - on a CLONE, because
            // in the Editor a runtime write to the skybox material would edit the asset on disk.
            skyboxBefore = RenderSettings.skybox;
            if (skyboxBefore != null && skyboxBefore.HasProperty("_Exposure"))
            {
                skyboxClone = new Material(skyboxBefore);
                RenderSettings.skybox = skyboxClone;
            }
            else skyboxClone = null;
        }

        float skyBefore = skyboxClone != null ? skyboxClone.GetFloat("_Exposure") : 0f;
        float skyTarget = skyboxBefore != null && skyboxBefore.HasProperty("_Exposure")
            ? skyboxBefore.GetFloat("_Exposure") * ambientKeep : 0f;

        var from = new List<float>(dimmed.Count);
        foreach (var d in dimmed) from.Add(d.light != null ? d.light.intensity : 0f);
        float ambientFrom = RenderSettings.ambientIntensity;

        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
            for (int i = 0; i < dimmed.Count; i++)
                if (dimmed[i].light != null)
                    dimmed[i].light.intensity = Mathf.Lerp(from[i], dimmed[i].intensity * lightKeep, k);
            RenderSettings.ambientIntensity = Mathf.Lerp(ambientFrom, ambientBefore * ambientKeep, k);
            if (skyboxClone != null)
                skyboxClone.SetFloat("_Exposure", Mathf.Lerp(skyBefore, skyTarget, k));
            yield return null;
        }

        if (thenRelease) RestoreNow();
        lightRoutine = null;
    }

    /// <summary>Puts every captured value back exactly and lets the clone go.</summary>
    private static void RestoreNow()
    {
        foreach (var d in dimmed) if (d.light != null) d.light.intensity = d.intensity;
        dimmed.Clear();
        if (ambientBefore > 0f) RenderSettings.ambientIntensity = ambientBefore;
        if (skyboxBefore != null) RenderSettings.skybox = skyboxBefore;
        if (skyboxClone != null) { Object.Destroy(skyboxClone); skyboxClone = null; }
        skyboxBefore = null;
        dark = false;
    }
}
