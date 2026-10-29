using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// An arena that brings its own sky and light. The Tripo arenas all share the scene's sun, sky, fog
/// and grade; the Musashi village (Dojo Courtyard / Village Plaza) was lit for a late afternoon of
/// its own, with a cloudy sky, haze and a forest ring that only reads in that haze. This sits on the
/// arena's (shared) root, which ArenaDirectory switches on only while one of its arenas is chosen.
///
/// While enabled it takes over the render settings (skybox, sun, ambient, fog, reflections) and
/// switches OFF every other light and volume in the scene that was on, so the scene's own sun and
/// grade do not stack on top of the arena's (those live under this root and stay on). Disabling it
/// puts every one of those back exactly as it found them, so the other arenas are untouched.
/// </summary>
public class ArenaAtmosphere : MonoBehaviour
{
    [Header("Sky and sun")]
    [SerializeField] private Material skybox;
    [Tooltip("The arena's own directional light (under this root).")]
    [SerializeField] private Light sun;

    [Header("Ambient")]
    [SerializeField] private AmbientMode ambientMode = AmbientMode.Trilight;
    [SerializeField] private Color ambientSky = new Color(0.56f, 0.62f, 0.72f);
    [SerializeField] private Color ambientEquator = new Color(0.56f, 0.52f, 0.46f);
    [SerializeField] private Color ambientGround = new Color(0.26f, 0.23f, 0.19f);

    [Header("Reflections")]
    [Tooltip("The sky's reflection cubemap, baked in the arena's own scene. Empty = keep the scene's.")]
    [SerializeField] private Cubemap reflection;
    [SerializeField] private float reflectionIntensity = 0.6f;

    [Header("Fog")]
    [SerializeField] private bool fog = true;
    [SerializeField] private FogMode fogMode = FogMode.Linear;
    [SerializeField] private Color fogColor = new Color(0.64f, 0.67f, 0.70f);
    [SerializeField] private float fogStart = 90f;
    [SerializeField] private float fogEnd = 520f;
    [SerializeField] private float fogDensity = 0.01f;

    // what was there before, restored on disable
    private bool captured;
    private Material prevSkybox;
    private Light prevSun;
    private AmbientMode prevAmbientMode;
    private Color prevSky, prevEquator, prevGround, prevAmbientLight, prevFogColor;
    private float prevAmbientIntensity, prevReflIntensity, prevFogStart, prevFogEnd, prevFogDensity;
    private DefaultReflectionMode prevReflMode;
    private Texture prevCustomReflection;
    private bool prevFog;
    private FogMode prevFogMode;
    private readonly List<Light> switchedLights = new List<Light>();
    private readonly List<Volume> switchedVolumes = new List<Volume>();

    private void OnEnable()
    {
        Capture();

        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.enabled && !l.transform.IsChildOf(transform)) { l.enabled = false; switchedLights.Add(l); }
        foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.enabled && !v.transform.IsChildOf(transform)) { v.enabled = false; switchedVolumes.Add(v); }

        if (skybox != null) RenderSettings.skybox = skybox;
        if (sun != null) RenderSettings.sun = sun;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.ambientLight = ambientSky;
        if (reflection != null)
        {
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = reflection;
        }
        RenderSettings.reflectionIntensity = reflectionIntensity;
        RenderSettings.fog = fog;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.fogDensity = fogDensity;
        // No DynamicGI.UpdateEnvironment(): Trilight colours set the probe by themselves, and the
        // call left the scene's Skybox ambient regenerating from the sky after we handed it back
        // (testarena's probe went 0.222 -> 0.274, every other arena a quarter brighter).
    }

    private void OnDisable()
    {
        foreach (var l in switchedLights) if (l != null) l.enabled = true;
        foreach (var v in switchedVolumes) if (v != null) v.enabled = true;
        switchedLights.Clear();
        switchedVolumes.Clear();
        if (!captured) return;
        captured = false;

        RenderSettings.skybox = prevSkybox;
        RenderSettings.sun = prevSun;
        // Putting the mode back restores the scene's own probe; never write ambientProbe here - in
        // Skybox mode the setter does not round-trip (0.222 written reads back 0.274).
        RenderSettings.ambientMode = prevAmbientMode;
        RenderSettings.ambientSkyColor = prevSky;
        RenderSettings.ambientEquatorColor = prevEquator;
        RenderSettings.ambientGroundColor = prevGround;
        RenderSettings.ambientLight = prevAmbientLight;
        RenderSettings.ambientIntensity = prevAmbientIntensity;
        RenderSettings.defaultReflectionMode = prevReflMode;
        RenderSettings.customReflectionTexture = prevCustomReflection;
        RenderSettings.reflectionIntensity = prevReflIntensity;
        RenderSettings.fog = prevFog;
        RenderSettings.fogMode = prevFogMode;
        RenderSettings.fogColor = prevFogColor;
        RenderSettings.fogStartDistance = prevFogStart;
        RenderSettings.fogEndDistance = prevFogEnd;
        RenderSettings.fogDensity = prevFogDensity;
    }

    private void Capture()
    {
        prevSkybox = RenderSettings.skybox;
        prevSun = RenderSettings.sun;
        prevAmbientMode = RenderSettings.ambientMode;
        prevSky = RenderSettings.ambientSkyColor;
        prevEquator = RenderSettings.ambientEquatorColor;
        prevGround = RenderSettings.ambientGroundColor;
        prevAmbientLight = RenderSettings.ambientLight;
        prevAmbientIntensity = RenderSettings.ambientIntensity;
        prevReflMode = RenderSettings.defaultReflectionMode;
        prevCustomReflection = RenderSettings.customReflectionTexture;
        prevReflIntensity = RenderSettings.reflectionIntensity;
        prevFog = RenderSettings.fog;
        prevFogMode = RenderSettings.fogMode;
        prevFogColor = RenderSettings.fogColor;
        prevFogStart = RenderSettings.fogStartDistance;
        prevFogEnd = RenderSettings.fogEndDistance;
        prevFogDensity = RenderSettings.fogDensity;
        captured = true;
    }
}
