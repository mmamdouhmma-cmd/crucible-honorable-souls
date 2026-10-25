using UnityEngine;

/// <summary>
/// One-shot combat effects built from code (no prefabs to maintain). Blood is the only effect on
/// by default: a spray of velocity-stretched droplets from the wound plus a splat that lands on the
/// ground and fades. Sparks fire on a parry (and on a block when that toggle is on) and dust on every
/// dash, loudest when a dash dodges a strike.
/// Sizes and speeds scale with the fighter's body radius so they read the same on a 3x and a 4x character.
/// </summary>
public static class HitEffects
{
    /// <summary>
    /// Blood spray from <paramref name="target"/>'s chest on the side facing <paramref name="attacker"/>,
    /// thrown away from the blow, and a stain on the ground where it lands.
    /// </summary>
    public static void SpawnBlood(Fighter target, Fighter attacker, Color color)
    {
        if (target == null || attacker == null) return;
        // The game record replays this call against the re-posed fighters (no-op unless recording).
        ReplayRecorder.NoteBlood(target, attacker, color);

        float r = Mathf.Max(target.BodyRadius, 0.2f);
        Vector3 toAttacker = Flat(attacker.transform.position - target.transform.position, -target.transform.forward);
        Vector3 away = -toAttacker;
        Vector3 origin = Chest(target) + toAttacker * (r * 0.8f);
        Color dark = new Color(color.r * 0.55f, color.g * 0.4f, color.b * 0.4f, 1f);

        // Fine spray: many small droplets, fast, short-lived.
        ParticleSystem spray = NewSystem("BloodSpray", origin, Quaternion.LookRotation(away + Vector3.up * 0.35f), color, 1.5f, VfxMaterials.SoftDot());
        ParticleSystem.MainModule m = spray.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(r * 6f, r * 14f);
        m.startSize = new ParticleSystem.MinMaxCurve(r * 0.05f, r * 0.14f);
        m.gravityModifier = 2.6f;
        m.startRotation = 0f;
        Burst(spray, 40, 60, 0f);
        Burst(spray, 10, 18, 0.04f);
        Cone(spray, 26f, r * 0.08f);
        Stretch(spray, 0.12f, 2.2f);
        FadeOut(spray, color, dark);
        spray.Play();

        // Heavy drops: fewer, bigger, slower, arcing down.
        ParticleSystem drops = NewSystem("BloodDrops", origin, Quaternion.LookRotation(away + Vector3.up * 0.9f), color, 1.6f, VfxMaterials.SoftDot());
        m = drops.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(r * 2.5f, r * 6f);
        m.startSize = new ParticleSystem.MinMaxCurve(r * 0.16f, r * 0.34f);
        m.gravityModifier = 3.2f;
        m.startRotation = 0f;
        Burst(drops, 8, 14, 0f);
        Cone(drops, 40f, r * 0.1f);
        Stretch(drops, 0.08f, 1.6f);
        FadeOut(drops, color, dark);
        drops.Play();

        // Ground stain roughly where the drops come down.
        Vector3 ground = target.transform.position + away * (r * 1.6f) + Vector3.up * 0.02f;
        SpawnSplat(ground, r * Random.Range(1.4f, 2.4f), dark);
        if (Random.value < 0.6f)
        {
            SpawnSplat(ground + away * (r * 1.2f) + Vector3.Cross(away, Vector3.up) * Random.Range(-r, r), r * Random.Range(0.6f, 1.2f), dark);
        }
    }

    /// <summary>A blood stain quad flat on the ground that fades away over a few seconds.</summary>
    public static void SpawnSplat(Vector3 position, float size, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "BloodSplat";
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
        go.transform.localScale = new Vector3(size, size * Random.Range(0.7f, 1.1f), 1f);
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        mr.material = VfxMaterials.Unlit(color, false, VfxMaterials.Splat());
        FadeAndDestroy fade = go.AddComponent<FadeAndDestroy>();
        fade.hold = 6f;
        fade.fade = 4f;
    }

    /// <summary>
    /// Metal-on-metal sparks. <paramref name="strength"/> scales the whole burst: the particle count
    /// grows with it and the size, speed and lifetime with its square root, so a loud flash reads as
    /// a bigger shower rather than a handful of giant dots. 1 = the old burst.
    /// </summary>
    public static void SpawnSparks(Vector3 point, Vector3 direction, float bodyRadius, float strength = 1f)
    {
        ReplayRecorder.NoteFx(ReplayRecorder.Fx.Sparks, point, direction, bodyRadius, strength);
        SparksCore(point, direction, bodyRadius, strength);
    }

    // The body, shared with the parry flash so ITS sparks are not recorded a second time
    // (the parry event alone replays the whole flash, sparks included).
    static void SparksCore(Vector3 point, Vector3 direction, float bodyRadius, float strength)
    {
        float s = Mathf.Max(0.1f, strength);
        float scale = Mathf.Sqrt(s);
        float r = Mathf.Max(bodyRadius, 0.2f);
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.up;
        ParticleSystem ps = NewSystem("Sparks", point, Quaternion.LookRotation(direction.normalized), new Color(1f, 0.85f, 0.45f), 0.8f * scale, VfxMaterials.SoftDot(), true);
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f * scale, 0.35f * scale);
        main.startSpeed = new ParticleSystem.MinMaxCurve(r * 8f * scale, r * 18f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(r * 0.03f * scale, r * 0.08f * scale);
        main.gravityModifier = 1.5f;
        main.maxParticles = Mathf.Max(160, Mathf.RoundToInt(60 * s));
        Burst(ps, Mathf.RoundToInt(24 * s), Mathf.RoundToInt(40 * s), 0f);
        Cone(ps, 55f, r * 0.05f);
        Stretch(ps, 0.08f, 2f);
        ps.Play();
    }

    /// <summary>
    /// A parry's flash: the gold spark shower plus a COLD core - a blue-white pop of light where
    /// the blades met and a flat expanding ring of cyan streaks. The whole game is lit warm
    /// (embers, torches, sand), so the cold flash is the one thing on screen that colour, and a
    /// parry reads at a glance even when the camera is moving.
    /// </summary>
    public static void SpawnParryFlash(Vector3 point, Vector3 direction, float bodyRadius, float strength)
    {
        ReplayRecorder.NoteFx(ReplayRecorder.Fx.Parry, point, direction, bodyRadius, strength);
        float s = Mathf.Max(0.1f, strength);
        float scale = Mathf.Sqrt(s);
        SparksCore(point, direction, bodyRadius, s);

        float r = Mathf.Max(bodyRadius, 0.2f);
        Color steel = new Color(0.55f, 0.85f, 1f);

        // The core pop: a few big additive blue-white dots that flare and die in a tenth of a
        // second - the "camera flash" that makes the moment unmissable.
        ParticleSystem flash = NewSystem("ParryFlashCore", point, Quaternion.LookRotation(Vector3.up), Color.white, 0.5f, VfxMaterials.SoftDot(), true);
        ParticleSystem.MainModule main = flash.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(r * 1.2f * scale, r * 2.2f * scale);
        main.gravityModifier = 0f;
        Burst(flash, 3, 5, 0f);
        ParticleSystem.ShapeModule core = flash.shape;
        core.shapeType = ParticleSystemShapeType.Sphere;
        core.radius = r * 0.1f;
        flash.Play();

        // The shockwave: a flat horizontal ring of stretched cyan streaks thrown outward from the
        // contact, so the deflection still reads when the core pop is over.
        ParticleSystem ring = NewSystem("ParryRing", point, Quaternion.LookRotation(Vector3.up), steel, 0.7f, VfxMaterials.SoftDot(), true);
        main = ring.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(r * 7f * scale, r * 12f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(r * 0.1f * scale, r * 0.2f * scale);
        main.gravityModifier = 0f;
        Burst(ring, Mathf.RoundToInt(14 * s), Mathf.RoundToInt(22 * s), 0f);
        ParticleSystem.ShapeModule shape = ring.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;    // local XY; the system faces up, so the ring lies level
        shape.radius = r * 0.15f;
        Stretch(ring, 0.1f, 2.6f);
        ring.Play();

        // Slower blue embers that hang where the blades met, the afterglow of the pop.
        ParticleSystem embers = NewSystem("ParryFlash", point, Quaternion.LookRotation(Vector3.up), steel, 0.8f, VfxMaterials.SoftDot(), true);
        main = embers.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(r * 1.5f * s, r * 4f * s);
        main.startSize = new ParticleSystem.MinMaxCurve(r * 0.14f * s, r * 0.3f * s);
        main.gravityModifier = 0.1f;
        Burst(embers, Mathf.RoundToInt(10 * s), Mathf.RoundToInt(16 * s), 0f);
        ParticleSystem.ShapeModule halo = embers.shape;
        halo.shapeType = ParticleSystemShapeType.Sphere;
        halo.radius = r * 0.12f;
        embers.Play();
    }

    /// <summary>
    /// Dust kicked up at <paramref name="point"/>. <paramref name="strength"/> 1 is the ordinary
    /// dash push-off; above that the cloud gets more grains, a wider spread and more opacity (speed
    /// and size grow with the square root so a big puff billows instead of shooting outward).
    /// </summary>
    public static void SpawnDust(Vector3 point, float bodyRadius, float strength = 1f)
    {
        ReplayRecorder.NoteFx(ReplayRecorder.Fx.Dust, point, Vector3.up, bodyRadius, strength);
        float s = Mathf.Max(0.1f, strength);
        float scale = Mathf.Sqrt(s);
        float r = Mathf.Max(bodyRadius, 0.2f);
        float alpha = Mathf.Clamp(0.45f * Mathf.Lerp(1f, 1.6f, Mathf.Clamp01(s - 1f)), 0.1f, 0.8f);
        ParticleSystem ps = NewSystem("Dust", point, Quaternion.LookRotation(Vector3.up), new Color(0.72f, 0.62f, 0.48f, alpha), 1.6f + 0.4f * (scale - 1f), VfxMaterials.SoftDot());
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.0f * scale);
        main.startSpeed = new ParticleSystem.MinMaxCurve(r * 1.2f * scale, r * 3f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(r * 0.6f * scale, r * 1.3f * scale);
        main.gravityModifier = 0.25f;
        main.maxParticles = Mathf.Max(160, Mathf.RoundToInt(40 * s));
        Burst(ps, Mathf.RoundToInt(10 * s), Mathf.RoundToInt(18 * s), 0f);
        Cone(ps, Mathf.Min(85f, 70f + 8f * (s - 1f)), r * 0.5f * scale);
        ps.Play();
    }

    /// <summary>
    /// The burst for a SUCCESSFUL dodge - a dash that made a strike miss. Deliberately not the
    /// warm dash dust: a big pale blue-white cloud at the feet plus a flat ring of additive
    /// streaks snapped outward, cold against the sand and stone so the evade reads at a glance.
    /// Ordinary dash push-off keeps <see cref="SpawnDust"/>.
    /// </summary>
    public static void SpawnDodgeBurst(Vector3 point, float bodyRadius, float strength = 1f)
    {
        ReplayRecorder.NoteFx(ReplayRecorder.Fx.Dodge, point, Vector3.up, bodyRadius, strength);
        float s = Mathf.Max(0.1f, strength);
        float scale = Mathf.Sqrt(s);
        float r = Mathf.Max(bodyRadius, 0.2f);

        // The cloud: like dash dust but paler, bigger and more opaque.
        Color pale = new Color(0.8f, 0.9f, 1f, 0.85f);
        ParticleSystem cloud = NewSystem("DodgeCloud", point, Quaternion.LookRotation(Vector3.up), pale, 1.8f, VfxMaterials.SoftDot());
        ParticleSystem.MainModule main = cloud.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f * scale, 0.9f * scale);
        main.startSpeed = new ParticleSystem.MinMaxCurve(r * 2f * scale, r * 5f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(r * 0.9f * scale, r * 1.8f * scale);
        main.gravityModifier = 0.12f;
        main.maxParticles = Mathf.Max(160, Mathf.RoundToInt(40 * s));
        Burst(cloud, Mathf.RoundToInt(12 * s), Mathf.RoundToInt(20 * s), 0f);
        Cone(cloud, 80f, r * 0.6f * scale);
        cloud.Play();

        // The ring: cyan speed-streaks thrown flat outward at knee height - the "he was HERE"
        // snap that no amount of brown dust ever gave.
        ParticleSystem ring = NewSystem("DodgeRing", point + Vector3.up * (r * 0.6f), Quaternion.LookRotation(Vector3.up), new Color(0.6f, 0.9f, 1f), 0.8f, VfxMaterials.SoftDot(), true);
        main = ring.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(r * 8f * scale, r * 14f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(r * 0.1f * scale, r * 0.2f * scale);
        main.gravityModifier = 0f;
        Burst(ring, Mathf.RoundToInt(12 * s), Mathf.RoundToInt(18 * s), 0f);
        ParticleSystem.ShapeModule shape = ring.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;    // local XY; the system faces up, so the ring lies level
        shape.radius = r * 0.3f;
        Stretch(ring, 0.1f, 2.6f);
        ring.Play();
    }

    // ---------------------------------------------------------------- helpers

    private static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        v.y = 0f;
        return v.sqrMagnitude < 0.0001f ? fallback.normalized : v.normalized;
    }

    /// <summary>Chest height from the body capsule when there is one, otherwise from the radius.</summary>
    public static Vector3 Chest(Fighter f)
    {
        CapsuleCollider capsule = f.GetComponent<CapsuleCollider>();
        if (capsule != null) return capsule.transform.TransformPoint(capsule.center);
        return f.transform.position + Vector3.up * (Mathf.Max(f.BodyRadius, 0.2f) * 4f);
    }

    private static ParticleSystem NewSystem(string name, Vector3 pos, Quaternion rot, Color color, float life, Texture2D sprite, bool additive = false)
    {
        GameObject go = new GameObject(name);
        go.transform.position = pos;
        go.transform.rotation = rot;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.playOnAwake = false;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;

        ParticleSystemRenderer rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.material = VfxMaterials.Unlit(Color.white, additive, sprite);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Object.Destroy(go, life);
        return ps;
    }

    private static void Burst(ParticleSystem ps, int min, int max, float time)
    {
        ParticleSystem.EmissionModule emission = ps.emission;
        int n = emission.burstCount;
        ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[n + 1];
        for (int i = 0; i < n; i++) bursts[i] = emission.GetBurst(i);
        bursts[n] = new ParticleSystem.Burst(time, (short)min, (short)max);
        emission.SetBursts(bursts);
    }

    private static void Cone(ParticleSystem ps, float angle, float radius)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
    }

    /// <summary>Stretch each particle along its velocity so a dot becomes a droplet.</summary>
    private static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
    {
        ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = velocityScale;
        rend.lengthScale = lengthScale;
    }

    private static void FadeOut(ParticleSystem ps, Color from, Color to)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 0.6f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 0.9f), new Keyframe(1f, 0.4f)));
    }
}

/// <summary>Holds, then fades a renderer's material alpha to zero and destroys the object.</summary>
public class FadeAndDestroy : MonoBehaviour
{
    public float hold = 6f;
    public float fade = 4f;
    private float t;
    private Renderer rend;
    private Color baseColor;
    private string colorProp;

    private void Start()
    {
        rend = GetComponent<Renderer>();
        Material m = rend.material;   // instance, so the fade doesn't touch the shared one
        colorProp = m.HasProperty("_BaseColor") ? "_BaseColor" : m.HasProperty("_UnlitColor") ? "_UnlitColor" : m.HasProperty("_TintColor") ? "_TintColor" : "_Color";
        baseColor = m.GetColor(colorProp);
    }

    private void Update()
    {
        t += Time.deltaTime;
        if (t <= hold) return;
        float a = Mathf.Clamp01(1f - (t - hold) / Mathf.Max(fade, 0.01f));
        rend.material.SetColor(colorProp, new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * a));
        if (a <= 0f) Destroy(gameObject);
    }
}
