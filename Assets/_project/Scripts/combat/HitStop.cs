using UnityEngine;

/// <summary>
/// Global hit-stop: a few frames where both fighters freeze on impact so a hit reads as a hit.
/// <see cref="CombatSystem"/> requests it when a strike lands and ticks it once per frame;
/// <see cref="Fighter"/> and <see cref="CombatSystem"/> skip their updates while it is active.
/// </summary>
public static class HitStop
{
    private static float remaining;
    private static int lastTickFrame = -1;

    /// <summary>True while the freeze is running.</summary>
    public static bool IsActive => remaining > 0f;

    /// <summary>Freeze for at least <paramref name="seconds"/> (a longer freeze already running is kept).</summary>
    public static void Request(float seconds)
    {
        remaining = Mathf.Max(remaining, seconds);
    }

    /// <summary>Advances the freeze timer. Safe to call from several components; it only counts once per frame.</summary>
    public static void Tick(float deltaTime)
    {
        if (Time.frameCount == lastTickFrame) return;
        lastTickFrame = Time.frameCount;
        if (remaining > 0f) remaining -= deltaTime;
    }
}
