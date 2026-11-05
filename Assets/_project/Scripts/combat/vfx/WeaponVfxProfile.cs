using System;
using UnityEngine;

/// <summary>Trail look for one attack tier (Light / Medium / Heavy / Special).</summary>
[Serializable]
public class TrailTier
{
    [Tooltip("Seconds a point of the arc stays visible behind the blade.")]
    [Range(0.05f, 0.6f)] public float lifetime = 0.2f;
    [Tooltip("Peak opacity of the body layer (the smoky steel blur).")]
    [Range(0f, 1f)] public float opacity = 0.5f;
    [Tooltip("Where the ribbon starts along grip->tip (0 = at the hand, 1 = at the tip). Lower = a broader arc.")]
    [Range(0f, 0.95f)] public float innerFraction = 0.4f;
    [Tooltip("Layers drawn: 1 = body, 2 = + bright core near the edge, 3 = + a slower outer wave. Capped by the quality setting.")]
    [Range(1, 3)] public int layers = 1;
    [Tooltip("Directional streak particles thrown off the tip per second of swing (0 = none). Scaled by quality.")]
    [Range(0f, 200f)] public float streakRate = 0f;
    [Tooltip("Strength of the contact burst when this tier lands, is blocked or clashes (1 = the ordinary spark).")]
    [Range(0f, 3f)] public float impactStrength = 1f;
    [Tooltip("Grit/debris on a clean hit (heavy weapon mass).")]
    public bool impactDebris = false;
    [Tooltip("Seconds the blade keeps drawing after the live window closes (follow-through).")]
    [Range(0f, 0.15f)] public float tailSeconds = 0.03f;

    public TrailTier Clone() => (TrailTier)MemberwiseClone();
}

/// <summary>
/// One fighter's weapon-VFX identity: trail colours and shape per attack tier, and how its contacts
/// look. Pure presentation - nothing here is read by hit detection, damage or timing. Assign on the
/// fighter's CombatSystem (Vfx Profile); with none assigned, <see cref="ForCharacter"/> builds the
/// default for that character at runtime. `Crucible > Look > Create weapon VFX profiles` writes these
/// defaults out as editable assets.
/// </summary>
[CreateAssetMenu(menuName = "Crucible/Weapon VFX Profile", fileName = "Fighter_Vfx")]
public class WeaponVfxProfile : ScriptableObject
{
    [Header("Colours")]
    [Tooltip("The body layer: the blurred steel of the swing.")]
    public Color bodyColor = new Color(0.88f, 0.9f, 0.95f, 1f);
    [Tooltip("The core layer: a thin bright highlight along the edge (additive).")]
    public Color coreColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("The wave layer (Special tier): the slower outer sweep that lingers.")]
    public Color waveColor = new Color(0.8f, 0.85f, 0.95f, 1f);
    [Tooltip("Directional streak particles.")]
    public Color streakColor = new Color(1f, 0.95f, 0.85f, 1f);

    [Header("Shape")]
    [Tooltip("Multiplier on every tier's lifetime (a katana fades faster than an axe).")]
    [Range(0.5f, 1.5f)] public float lifetimeScale = 1f;
    [Tooltip("Spear-like weapon: the ribbon hugs the point and streaks run along the thrust.")]
    public bool thrustWeapon = false;

    [Header("Contact")]
    [Tooltip("Steel on steel: sparks on block/clash/hit. Off for wooden weapons (dull chips instead).")]
    public bool metallic = true;
    [Tooltip("Multiplier on every contact burst this fighter's weapon makes.")]
    [Range(0.25f, 2.5f)] public float impactScale = 1f;
    [Tooltip("Ground dust on a clean Heavy/Special hit (Bjorn's weight, Buwaka's footwork).")]
    public bool impactDust = false;

    [Header("Tiers")]
    public TrailTier light = new TrailTier { lifetime = 0.14f, opacity = 0.32f, innerFraction = 0.55f, layers = 1, streakRate = 0f, impactStrength = 0.6f };
    public TrailTier medium = new TrailTier { lifetime = 0.2f, opacity = 0.45f, innerFraction = 0.42f, layers = 2, streakRate = 18f, impactStrength = 0.9f };
    public TrailTier heavy = new TrailTier { lifetime = 0.28f, opacity = 0.58f, innerFraction = 0.3f, layers = 2, streakRate = 35f, impactStrength = 1.4f, impactDebris = true, tailSeconds = 0.05f };
    public TrailTier special = new TrailTier { lifetime = 0.34f, opacity = 0.7f, innerFraction = 0.22f, layers = 3, streakRate = 60f, impactStrength = 1.8f, impactDebris = true, tailSeconds = 0.06f };

    public TrailTier Tier(AttackType t) => t switch
    {
        AttackType.Light => light,
        AttackType.Medium => medium,
        AttackType.Heavy => heavy,
        _ => special,
    };

    // ------------------------------------------------------------ defaults

    /// <summary>
    /// The built-in profile for a character (by <see cref="Fighter.CharacterName"/>), from the art
    /// direction: Miyamoto thin white-blue and quick, Salaadin silver with warm highlights, Bjorn broad
    /// steel with amber and dust, Genghis steel-white with restrained warmth, Richard broad silver and
    /// heavy sparks, Buwaka narrow thrust trails, Sensei a pale wooden bokken (no sparks).
    /// </summary>
    public static WeaponVfxProfile ForCharacter(string name)
    {
        var p = CreateInstance<WeaponVfxProfile>();
        p.name = (string.IsNullOrEmpty(name) ? "Default" : name) + "_Vfx (runtime)";
        string n = (name ?? "").ToLowerInvariant();

        if (n.Contains("miyamoto") || n.Contains("musashi"))
        {
            p.bodyColor = new Color(0.78f, 0.88f, 1f);
            p.coreColor = new Color(0.9f, 0.96f, 1f);
            p.waveColor = new Color(0.62f, 0.78f, 1f);
            p.streakColor = new Color(0.85f, 0.93f, 1f);
            p.lifetimeScale = 0.8f;
            Narrow(p, 0.08f);
        }
        else if (n.Contains("salaadin") || n.Contains("saladin"))
        {
            p.bodyColor = new Color(0.9f, 0.9f, 0.93f);
            p.coreColor = new Color(1f, 0.9f, 0.74f);
            p.waveColor = new Color(0.95f, 0.86f, 0.72f);
            p.streakColor = new Color(1f, 0.92f, 0.8f);
            p.lifetimeScale = 1.05f;
        }
        else if (n.Contains("bjorn"))
        {
            p.bodyColor = new Color(0.82f, 0.8f, 0.76f);
            p.coreColor = new Color(1f, 0.74f, 0.42f);
            p.waveColor = new Color(0.95f, 0.7f, 0.45f);
            p.streakColor = new Color(1f, 0.78f, 0.5f);
            p.lifetimeScale = 1.15f;
            p.impactDust = true;
            p.impactScale = 1.15f;
            Narrow(p, -0.12f);   // broad: the axe sweeps from low on the haft
        }
        else if (n.Contains("genghis"))
        {
            p.bodyColor = new Color(0.93f, 0.94f, 0.97f);
            p.coreColor = new Color(1f, 0.88f, 0.72f);
            p.waveColor = new Color(0.92f, 0.9f, 0.86f);
            p.streakColor = new Color(1f, 0.93f, 0.85f);
        }
        else if (n.Contains("richard"))
        {
            p.bodyColor = new Color(0.86f, 0.87f, 0.9f);
            p.coreColor = new Color(1f, 0.97f, 0.92f);
            p.waveColor = new Color(0.82f, 0.84f, 0.9f);
            p.streakColor = new Color(1f, 0.9f, 0.7f);
            p.lifetimeScale = 1.1f;
            p.impactScale = 1.5f;
            Narrow(p, -0.08f);
        }
        else if (n.Contains("buwaka"))
        {
            p.bodyColor = new Color(0.88f, 0.86f, 0.8f);
            p.coreColor = new Color(1f, 0.96f, 0.88f);
            p.waveColor = new Color(0.86f, 0.8f, 0.7f);
            p.streakColor = new Color(0.95f, 0.88f, 0.75f);
            p.thrustWeapon = true;
            p.impactDust = true;
            p.lifetimeScale = 0.85f;
            Narrow(p, 0.3f);     // the point only: a thrust, not a sweeping arc
        }
        else if (n.Contains("sensei"))
        {
            p.bodyColor = new Color(0.93f, 0.9f, 0.84f);
            p.coreColor = new Color(1f, 0.97f, 0.9f);
            p.waveColor = new Color(0.9f, 0.86f, 0.78f);
            p.streakColor = new Color(0.95f, 0.9f, 0.8f);
            p.metallic = false;  // a bokken: no steel sparks
            p.impactScale = 0.8f;
        }
        return p;
    }

    static void Narrow(WeaponVfxProfile p, float d)
    {
        foreach (var t in new[] { p.light, p.medium, p.heavy, p.special })
            t.innerFraction = Mathf.Clamp(t.innerFraction + d, 0f, 0.9f);
    }
}
