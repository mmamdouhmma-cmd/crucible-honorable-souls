using UnityEngine;

/// <summary>
/// One-shot combat effects built from code (no prefabs to maintain). Blood is the only effect on
/// by default: a spray of velocity-stretched droplets from the wound plus a splat that lands on the
/// ground and fades. Sparks and dust are kept for later but nothing calls them unless enabled.
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

    /// <summary>Metal-on-metal sparks (not used by default).</summary>
    public static void SpawnSparks(Vector3 point, Vector3 direction, float bodyRadius)
    {
        float r = Mathf.Max(bodyRadius, 0.2f);
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.up;
        ParticleSystem ps = NewSystem("Sparks", point, Quaternion.LookRotation(direction.normalized), new Color(1f, 0.85f, 0.45f), 0.8f, VfxMaterials.SoftDot(), true);
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(r * 8f, r * 18f);
        main.startSize = new ParticleSystem.MinMaxCurve(r * 0.03f, r * 0.08f);
        main.gravityModifier = 1.5f;
        Burst(ps, 24, 40, 0f);
        Cone(ps, 55f, r * 0.05f);
        Stretch(ps, 0.08f, 2f);
        ps.Play();
    }

    /// <summary>Dust kicked up at <paramref name="point"/> (not used by default).</summary>
    public static void SpawnDust(Vector3 point, float bodyRadius, float strength = 1f)
    {
        float r = Mathf.Max(bodyRadius, 0.2f);
        ParticleSystem ps = NewSystem("Dust", point, Quaternion.LookRotation(Vector3.up), new Color(0.72f, 0.62f, 0.48f, 0.45f), 1.6f, VfxMaterials.SoftDot());
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(r * 1.2f * strength, r * 3f * strength);
        main.startSize = new ParticleSystem.MinMaxCurve(r * 0.6f, r * 1.3f);
        main.gravityModifier = 0.25f;
        Burst(ps, Mathf.RoundToInt(10 * strength), Mathf.RoundToInt(18 * strength), 0f);
        Cone(ps, 70f, r * 0.5f);
        ps.Play();
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
