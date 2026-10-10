using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime materials and textures for code-built effects. Picks the unlit particle shader of the
/// ACTIVE render pipeline (URP, HDRP or built-in) by looking at the current pipeline asset, so an
/// installed-but-inactive pipeline never leaks in (that is what rendered pink under URP).
/// Game scripts never reference a pipeline assembly.
/// Note for builds: the shader must be referenced somewhere or listed under
/// Project Settings > Graphics > Always Included Shaders, or Shader.Find returns null.
/// </summary>
public static class VfxMaterials
{
    private static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
    private static Texture2D softDot;
    private static Texture2D splat;

    private enum Pipeline { BuiltIn, Urp, Hdrp }

    private static Pipeline Active()
    {
        RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) return Pipeline.BuiltIn;
        string n = rp.GetType().Name;
        if (n.Contains("Universal")) return Pipeline.Urp;
        if (n.Contains("HDRenderPipeline")) return Pipeline.Hdrp;
        return Pipeline.BuiltIn;
    }

    /// <summary>Alpha-blended (or additive) unlit material tinted <paramref name="color"/>, with an optional sprite.</summary>
    public static Material Unlit(Color color, bool additive = false, Texture2D sprite = null)
    {
        string key = $"{Active()}:{(additive ? "add" : "blend")}:{ColorUtility.ToHtmlStringRGBA(color)}:{(sprite != null ? sprite.name : "-")}";
        if (cache.TryGetValue(key, out Material cached) && cached != null) return cached;

        Material mat;
        switch (Active())
        {
            case Pipeline.Urp:
            {
                Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
                mat = new Material(s);
                mat.SetColor("_BaseColor", color);
                if (sprite != null) mat.SetTexture("_BaseMap", sprite);
                mat.SetFloat("_Surface", 1f);                      // transparent
                mat.SetFloat("_Blend", additive ? 1f : 0f);        // alpha / additive
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_Cull", 0f);
                mat.SetFloat("_ColorMode", 0f);                    // multiply vertex colour
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (additive) mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
                break;
            }
            case Pipeline.Hdrp:
            {
                mat = new Material(Shader.Find("HDRP/Unlit"));
                mat.SetColor("_UnlitColor", color);
                if (sprite != null) mat.SetTexture("_UnlitColorMap", sprite);
                mat.SetFloat("_SurfaceType", 1f);
                mat.SetFloat("_BlendMode", additive ? 1f : 0f);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_SrcBlend", 1f);
                mat.SetFloat("_DstBlend", additive ? 1f : 10f);
                mat.SetFloat("_AlphaSrcBlend", 1f);
                mat.SetFloat("_AlphaDstBlend", additive ? 1f : 10f);
                mat.SetFloat("_TransparentCullMode", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
                break;
            }
            default:
            {
                Shader s = Shader.Find(additive ? "Legacy Shaders/Particles/Additive" : "Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
                mat = new Material(s);
                if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                if (sprite != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", sprite);
                break;
            }
        }
        mat.name = "VFX " + key;
        cache[key] = mat;
        return mat;
    }

    /// <summary>A soft round sprite: opaque centre, smooth falloff to transparent. Reads as a droplet when stretched.</summary>
    public static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int n = 64;
        softDot = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "VFX SoftDot", wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;         // 0 centre, 1 edge
                float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.55f, 1f, d));
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        softDot.SetPixels(px);
        softDot.Apply(true);
        return softDot;
    }

    /// <summary>An irregular splat: a blob with ragged edges and a few satellite drops, for ground stains.</summary>
    public static Texture2D Splat()
    {
        if (splat != null) return splat;
        const int n = 128;
        splat = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "VFX Splat", wrapMode = TextureWrapMode.Clamp };
        var rng = new System.Random(1337);
        var blobs = new List<(float x, float y, float r)> { (0.5f, 0.5f, 0.30f) };
        for (int i = 0; i < 9; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = 0.12f + (float)rng.NextDouble() * 0.3f;
            blobs.Add((0.5f + Mathf.Cos(ang) * dist, 0.5f + Mathf.Sin(ang) * dist, 0.04f + (float)rng.NextDouble() * 0.12f));
        }
        Color[] px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float field = 0f;
                foreach (var b in blobs)
                {
                    float dx = u - b.x, dy = v - b.y;
                    field += (b.r * b.r) / (dx * dx + dy * dy + 1e-4f);
                }
                float noise = Mathf.PerlinNoise(u * 9f, v * 9f) * 0.5f;
                float a = Mathf.Clamp01((field - 1.2f - noise) * 1.5f);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        splat.SetPixels(px);
        splat.Apply(true);
        return splat;
    }
}
