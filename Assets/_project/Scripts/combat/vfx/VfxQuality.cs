using UnityEngine;

/// <summary>The four combat-effect quality presets (Settings > Effects quality). Cosmetic only.</summary>
public enum VfxQualityLevel { Low, Medium, High, Cinematic }

/// <summary>
/// Global combat-VFX quality. Read by <see cref="WeaponTrail"/> and <see cref="HitEffects"/>; never
/// read by anything that decides a hit, a timing or a number, so changing it cannot change the fight.
/// Readability floor: every level keeps the base trail layer, and parry/dodge bursts never drop
/// below 80% of their particles (see <see cref="EssentialScale"/>).
/// </summary>
public static class VfxQuality
{
    public static VfxQualityLevel Level = Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer
        ? VfxQualityLevel.Medium : VfxQualityLevel.High;

    /// <summary>Multiplier on every one-shot particle burst.</summary>
    public static float ParticleScale => Level switch
    {
        VfxQualityLevel.Low => 0.4f,
        VfxQualityLevel.Medium => 0.7f,
        VfxQualityLevel.High => 1f,
        _ => 1.3f,
    };

    /// <summary>Parry / dodge / guard-break: the bursts that carry gameplay information.</summary>
    public static float EssentialScale => Mathf.Max(0.8f, ParticleScale);

    /// <summary>How many trail layers a tier may draw (body, core, wave).</summary>
    public static int MaxTrailLayers => Level switch
    {
        VfxQualityLevel.Low => 1,
        VfxQualityLevel.Medium => 2,
        _ => 3,
    };

    /// <summary>Catmull-Rom subdivisions between two sampled blade positions (smoother arcs).</summary>
    public static int TrailSubdivisions => Level switch
    {
        VfxQualityLevel.Low => 1,
        VfxQualityLevel.Medium => 2,
        VfxQualityLevel.High => 3,
        _ => 4,
    };

    /// <summary>Multiplier on the directional streak particles a swing throws off its tip.</summary>
    public static float StreakScale => Level switch
    {
        VfxQualityLevel.Low => 0f,
        VfxQualityLevel.Medium => 0.5f,
        VfxQualityLevel.High => 1f,
        _ => 1.5f,
    };

    /// <summary>Cinematic only: atmospheric extras (dust lifted by a special's wind-up).</summary>
    public static bool Atmospherics => Level == VfxQualityLevel.Cinematic;

    public static string Label(VfxQualityLevel l) => l.ToString();
}
