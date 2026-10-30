using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The rules of one fight, set on the Game Settings screen before it starts: an optional time limit
/// and damage, health and stamina as percentages of normal. <see cref="Active"/> is what the fight on
/// screen uses; the host of an online fight sends his to the guest. Practice always runs the defaults.
/// </summary>
[Serializable]
public class DuelRules
{
    public const int MaxMinutes = 15;
    public const int MinPercent = 50, MaxPercent = 200, PercentStep = 5;   // the same range and step as game speed

    public bool timed;
    public int minutes = 3;
    public int damagePercent = 100;
    public int healthPercent = 100;
    public int staminaPercent = 100;

    /// <summary>The rules of the current fight. Never null.</summary>
    public static DuelRules Active = new DuelRules();

    public float DamageScale => damagePercent / 100f;
    public int Health(int normal) => Mathf.Max(1, Mathf.RoundToInt(normal * healthPercent / 100f));
    public int Stamina(int normal) => Mathf.Max(1, Mathf.RoundToInt(normal * staminaPercent / 100f));

    /// <summary>Space-separated, for the online start and rules messages. See <see cref="Parse"/>.</summary>
    public string Wire() => string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4}",
        timed ? 1 : 0, minutes, damagePercent, healthPercent, staminaPercent);

    /// <summary>Reads the five fields of <see cref="Wire"/> from <paramref name="p"/>, starting at <paramref name="at"/>.</summary>
    public static DuelRules Parse(string[] p, int at)
    {
        var d = new DuelRules();
        if (p.Length < at + 5) return d;
        d.timed = p[at] == "1";
        d.minutes = Mathf.Clamp(Int(p[at + 1], d.minutes), 1, MaxMinutes);
        d.damagePercent = Mathf.Clamp(Int(p[at + 2], 100), MinPercent, MaxPercent);
        d.healthPercent = Mathf.Clamp(Int(p[at + 3], 100), MinPercent, MaxPercent);
        d.staminaPercent = Mathf.Clamp(Int(p[at + 4], 100), MinPercent, MaxPercent);
        return d;
    }

    static int Int(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    /// <summary>One line for the online subtitles.</summary>
    public string Summary() =>
        (timed ? minutes + " min" : "no time limit") +
        $", damage {damagePercent}%, health {healthPercent}%, stamina {staminaPercent}%";
}
