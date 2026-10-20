using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The front end's look, built entirely in code like the rest of this project's UI and VFX.
///
/// The palette and the textures are taken from the game's logo (Resources/ui/crucible_logo):
/// a near-black field, chiselled letters with warm bronze edges and dried-blood spatter. Nothing
/// here is an imported art asset except the logo and the two concept-art portraits, so the whole
/// front end survives a fresh clone with no missing-reference pink.
///
/// Every texture is generated once, lazily, and marked <see cref="HideFlags.HideAndDontSave"/> so
/// a domain reload does not leak copies into the scene.
/// </summary>
public static class MenuArt
{
    // ---------------------------------------------------------------- palette

    /// <summary>The logo's background: near black, very slightly warm.</summary>
    public static readonly Color Ink = new Color(0.039f, 0.031f, 0.027f, 1f);

    /// <summary>Panel fill - charcoal with a little more warmth than the background.</summary>
    public static readonly Color Charcoal = new Color(0.082f, 0.067f, 0.055f, 1f);

    /// <summary>The logo's letter edges.</summary>
    public static readonly Color Bronze = new Color(0.722f, 0.529f, 0.247f, 1f);

    /// <summary>The highlight along the top of each letter.</summary>
    public static readonly Color BronzeLit = new Color(0.910f, 0.788f, 0.541f, 1f);

    /// <summary>Old blood, as it sits in the logo's crevices.</summary>
    public static readonly Color Blood = new Color(0.494f, 0.110f, 0.078f, 1f);

    /// <summary>Fresh blood, for the selection bar and KO text.</summary>
    public static readonly Color BloodLit = new Color(0.659f, 0.161f, 0.110f, 1f);

    /// <summary>Body text: weathered bone rather than white.</summary>
    public static readonly Color Bone = new Color(0.851f, 0.812f, 0.745f, 1f);

    /// <summary>Secondary text and hints.</summary>
    public static readonly Color Dim = new Color(0.431f, 0.388f, 0.337f, 1f);

    /// <summary>Player colours, used on the select screen and the VS card.</summary>
    public static readonly Color P1 = new Color(0.78f, 0.36f, 0.20f, 1f);
    public static readonly Color P2 = new Color(0.42f, 0.55f, 0.72f, 1f);

    public static Color Fade(Color c, float a) { c.a = a; return c; }

    // ---------------------------------------------------------------- font

    static Font font;

    /// <summary>The only font the project ships. Caps and wide tracking carry the period feel.</summary>
    public static Font Face
    {
        get
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    /// <summary>
    /// Spaces a string out letter by letter. The one typographic trick available with a single
    /// sans font: wide tracking in caps reads as inscription rather than as UI.
    /// </summary>
    public static string Track(string s, int spaces = 1)
    {
        if (string.IsNullOrEmpty(s)) return s;
        string gap = new string(' ', Mathf.Max(1, spaces));
        var sb = new System.Text.StringBuilder(s.Length * (1 + spaces));
        for (int i = 0; i < s.Length; i++)
        {
            sb.Append(s[i]);
            if (i < s.Length - 1 && s[i] != ' ') sb.Append(gap);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- textures

    static Sprite solid, panel, vignette, softDot, frame, rule, diamond, scratch, bar;

    static Texture2D NewTex(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        return t;
    }

    static Sprite Make(Texture2D t, Vector4 border = default)
    {
        var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0,
                              SpriteMeshType.FullRect, border);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    /// <summary>A single white pixel: the fill for plain coloured blocks.</summary>
    public static Sprite Solid
    {
        get
        {
            if (solid == null)
            {
                var t = NewTex(4, 4);
                var px = new Color[16];
                for (int i = 0; i < px.Length; i++) px[i] = Color.white;
                t.SetPixels(px); t.Apply();
                solid = Make(t);
            }
            return solid;
        }
    }

    /// <summary>
    /// Stone slab: mottled dark rock with a faint diagonal grain and darker edges, nine-sliced so
    /// one 128px tile dresses a panel of any size.
    /// </summary>
    public static Sprite Panel
    {
        get
        {
            if (panel == null)
            {
                const int N = 128;
                var t = NewTex(N, N);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // two octaves of value noise, plus a diagonal grain like cut stone
                    float n = Mathf.PerlinNoise(x * 0.055f, y * 0.055f) * 0.65f
                            + Mathf.PerlinNoise(x * 0.17f, y * 0.17f) * 0.35f;
                    float grain = Mathf.PerlinNoise((x + y) * 0.9f, (x - y) * 0.06f) * 0.12f;
                    float v = 0.55f + (n - 0.5f) * 0.8f + (grain - 0.06f);

                    // darken toward the border so the slab has depth without a drawn edge
                    float ex = Mathf.Min(x, N - 1 - x) / (N * 0.22f);
                    float ey = Mathf.Min(y, N - 1 - y) / (N * 0.22f);
                    v *= Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(Mathf.Min(ex, ey)));

                    px[y * N + x] = new Color(v, v * 0.97f, v * 0.92f, 1f);
                }
                t.SetPixels(px); t.Apply();
                panel = Make(t, new Vector4(28, 28, 28, 28));
            }
            return panel;
        }
    }

    /// <summary>Radial darkening for the screen edges - black at the corners, clear in the middle.</summary>
    public static Sprite Vignette
    {
        get
        {
            if (vignette == null)
            {
                const int N = 256;
                var t = NewTex(N, N);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x / (float)(N - 1) - 0.5f) * 2f;
                    float dy = (y / (float)(N - 1) - 0.5f) * 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.35f) / 0.65f));
                    px[y * N + x] = new Color(0f, 0f, 0f, a * 0.92f);
                }
                t.SetPixels(px); t.Apply();
                vignette = Make(t);
            }
            return vignette;
        }
    }

    /// <summary>Soft round dot: embers, glows and the bloom behind the logo.</summary>
    public static Sprite SoftDot
    {
        get
        {
            if (softDot == null)
            {
                const int N = 64;
                var t = NewTex(N, N);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x / (float)(N - 1) - 0.5f, dy = y / (float)(N - 1) - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * N + x] = new Color(1f, 1f, 1f, a * a * a);
                }
                t.SetPixels(px); t.Apply();
                softDot = Make(t);
            }
            return softDot;
        }
    }

    /// <summary>
    /// Nine-sliced border: a bright bronze hairline, a dark gap and a heavier outer rail, so a
    /// panel reads as metal banding rather than a flat rectangle outline.
    /// </summary>
    public static Sprite Frame
    {
        get
        {
            if (frame == null)
            {
                const int N = 48;
                var t = NewTex(N, N);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int e = Mathf.Min(Mathf.Min(x, N - 1 - x), Mathf.Min(y, N - 1 - y));
                    Color c;
                    if (e == 0) c = new Color(0.20f, 0.14f, 0.09f, 1f);            // dark outer lip
                    else if (e <= 2) c = Bronze;                                    // outer rail
                    else if (e == 3) c = new Color(0.12f, 0.09f, 0.07f, 1f);        // shadow gap
                    else if (e == 4) c = BronzeLit;                                 // bright hairline
                    else c = new Color(0f, 0f, 0f, 0f);                             // open middle
                    px[y * N + x] = c;
                }
                t.SetPixels(px); t.Apply();
                frame = Make(t, new Vector4(12, 12, 12, 12));
            }
            return frame;
        }
    }

    /// <summary>A horizontal bronze rule that fades out at both ends.</summary>
    public static Sprite Rule
    {
        get
        {
            if (rule == null)
            {
                const int W = 128, H = 8;
                var t = NewTex(W, H);
                var px = new Color[W * H];
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fade = Mathf.Sin(Mathf.PI * (x / (float)(W - 1)));
                    float band = y == H / 2 || y == H / 2 - 1 ? 1f : (y == H / 2 + 1 || y == H / 2 - 2 ? 0.35f : 0f);
                    px[y * W + x] = new Color(1f, 1f, 1f, fade * band);
                }
                t.SetPixels(px); t.Apply();
                rule = Make(t);
            }
            return rule;
        }
    }

    /// <summary>A small filled diamond - the centre ornament on a rule, and the row bullet.</summary>
    public static Sprite Diamond
    {
        get
        {
            if (diamond == null)
            {
                const int N = 32;
                var t = NewTex(N, N);
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = Mathf.Abs(x / (float)(N - 1) - 0.5f);
                    float dy = Mathf.Abs(y / (float)(N - 1) - 0.5f);
                    float d = (dx + dy) * 2f;
                    px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01((0.9f - d) * 6f));
                }
                t.SetPixels(px); t.Apply();
                diamond = Make(t);
            }
            return diamond;
        }
    }

    /// <summary>Sparse dust and scratches, laid over everything at low alpha for an aged print.</summary>
    public static Sprite Scratches
    {
        get
        {
            if (scratch == null)
            {
                const int N = 256;
                var t = NewTex(N, N);
                var px = new Color[N * N];
                var rng = new System.Random(20261020);
                for (int i = 0; i < px.Length; i++)
                {
                    float v = (float)rng.NextDouble();
                    float a = v > 0.9975f ? (v - 0.9975f) / 0.0025f * 0.35f : 0f;
                    px[i] = new Color(1f, 0.96f, 0.9f, a);
                }
                // a handful of hairline scratches
                for (int s = 0; s < 8; s++)
                {
                    int x0 = rng.Next(N), y0 = rng.Next(N);
                    int len = rng.Next(12, 70);
                    float ang = (float)(rng.NextDouble() * Mathf.PI);
                    for (int i = 0; i < len; i++)
                    {
                        int x = x0 + Mathf.RoundToInt(Mathf.Cos(ang) * i);
                        int y = y0 + Mathf.RoundToInt(Mathf.Sin(ang) * i);
                        if (x < 0 || y < 0 || x >= N || y >= N) break;
                        px[y * N + x] = new Color(1f, 0.96f, 0.9f, 0.10f);
                    }
                }
                t.SetPixels(px); t.Apply();
                t.wrapMode = TextureWrapMode.Repeat;
                scratch = Make(t);
            }
            return scratch;
        }
    }

    /// <summary>A top-to-bottom gradient, used to sink panels and portraits into the background.</summary>
    public static Sprite FadeDown
    {
        get
        {
            if (bar == null)
            {
                const int H = 64;
                var t = NewTex(4, H);
                var px = new Color[4 * H];
                for (int y = 0; y < H; y++)
                {
                    float a = 1f - y / (float)(H - 1);
                    for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a * a);
                }
                t.SetPixels(px); t.Apply();
                bar = Make(t);
            }
            return bar;
        }
    }

    // ---------------------------------------------------------------- loaded art

    static Texture2D logo, bjorn, miyamoto;
    static bool logoTried, bjornTried, miyamotoTried;

    /// <summary>The game's logo, or null if it is missing from Resources/ui.</summary>
    public static Texture2D Logo
    {
        get
        {
            if (!logoTried) { logo = Resources.Load<Texture2D>("ui/crucible_logo"); logoTried = true; }
            return logo;
        }
    }


    // ---------------------------------------------------------------- builders

    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    public static Image Img(Transform parent, string name, Sprite sprite, Color color, Image.Type type = Image.Type.Simple)
    {
        var rt = Rect(parent, name);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.type = type;
        img.raycastTarget = false;
        return img;
    }

    public static RawImage Raw(Transform parent, string name, Texture tex, Color color)
    {
        var rt = Rect(parent, name);
        var img = rt.gameObject.AddComponent<RawImage>();
        img.texture = tex;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    public static Text Label(Transform parent, string name, string text, int size, TextAnchor anchor,
                             Color color, FontStyle style = FontStyle.Normal)
    {
        var rt = Rect(parent, name);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Face;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.fontStyle = style;
        t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>A drop shadow dark enough to keep bone text legible over a lit arena.</summary>
    public static Text Shadowed(Transform parent, string name, string text, int size, TextAnchor anchor,
                                Color color, FontStyle style = FontStyle.Normal)
    {
        var t = Label(parent, name, text, size, anchor, color, style);
        var sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
        sh.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    public static void Stretch(RectTransform rt, float pad = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
    }

    public static void Place(RectTransform rt, float ax, float ay, float bx, float by, Vector2 offMin, Vector2 offMax)
    {
        rt.anchorMin = new Vector2(ax, ay);
        rt.anchorMax = new Vector2(bx, by);
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
    }

    /// <summary>Anchors a fixed-size box at a point in the parent, measured from its centre.</summary>
    public static void Box(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;
    }

    /// <summary>A bronze rule with a diamond at its centre, as a divider under a title.</summary>
    public static RectTransform Divider(Transform parent, string name, Color color, float width = 420f)
    {
        var holder = Rect(parent, name);
        var line = Img(holder, "Line", Rule, color);
        Stretch(line.rectTransform);
        var gem = Img(holder, "Gem", Diamond, color);
        Box(gem.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(14f, 14f), Vector2.zero);
        holder.sizeDelta = new Vector2(width, 10f);
        return holder;
    }

    /// <summary>Eased 0..1 used by every fade and slide in the front end.</summary>
    public static float EaseOut(float t) => 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t));

    public static float EaseInOut(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
    }

    // ---------------------------------------------------------------- chiselled text

    /// <summary>
    /// Stacked copies of one line of text, offset into the distance, so flat uGUI text reads as
    /// carved and lit the way the logo's wordmark is. The deepest copies are drawn first (furthest
    /// back), then a pale copy offset the other way for the lit top edge, then the face on top.
    /// Every layer carries the same string, so only the face colour is ever chosen by a caller.
    /// </summary>
    public class Chisel
    {
        public RectTransform rt;
        public Text face;
        public Text[] layers;          // [0] deepest ... [last] the face
        int baseSize;

        public void Set(string text)
        {
            for (int i = 0; i < layers.Length; i++) layers[i].text = text;
        }

        public void SetSize(int size)
        {
            baseSize = size;
            for (int i = 0; i < layers.Length; i++) layers[i].fontSize = size;
        }

        public void SetFace(Color c)
        {
            face.color = c;
            // The extrusion is the face colour walked down into the dark, so bronze extrudes
            // bronze and blood extrudes blood instead of every word sitting on the same grey slab.
            int body = layers.Length - 2;
            for (int i = 0; i < body; i++)
            {
                float k = (i + 1f) / (body + 1f);           // 0 at the back, 1 nearest the face
                layers[i].color = Fade(Color.Lerp(new Color(0.04f, 0.03f, 0.02f), c * 0.45f, k), 1f);
            }
            if (body >= 0 && body < layers.Length) layers[body].color = Fade(Bone, 0.33f);   // lit edge
        }

        /// <summary>
        /// Shrinks the line until it fits the width it is given. Names vary in length - "K.O." and
        /// "MIYAMOTO DIED" cannot share one font size - and tracked caps run wide, so without this
        /// the longer announcements ran off both sides of the screen.
        /// </summary>
        public void FitWidth(float maxWidth)
        {
            int size = baseSize;
            while (size > 36 && face.preferredWidth > maxWidth)
            {
                size -= 6;
                for (int i = 0; i < layers.Length; i++) layers[i].fontSize = size;
            }
        }
    }

    public static Chisel Chiselled(Transform parent, string name, int size, TextAnchor anchor, int depth = 6)
    {
        var c = new Chisel();
        c.rt = Rect(parent, name);
        Stretch(c.rt);
        var list = new System.Collections.Generic.List<Text>();

        // Extrusion, deepest first: down and to the right, away from an upper-left light.
        for (int i = depth; i >= 1; i--)
        {
            var t = Label(c.rt, "Depth" + i, "", size, anchor, Color.black);
            Stretch(t.rectTransform);
            t.rectTransform.anchoredPosition = new Vector2(i * 1.6f, -i * 1.6f);
            list.Add(t);
        }

        // The lit top edge, peeking out the other side.
        var lit = Label(c.rt, "Lit", "", size, anchor, Fade(Bone, 0.33f));
        Stretch(lit.rectTransform);
        lit.rectTransform.anchoredPosition = new Vector2(-1.6f, 1.6f);
        list.Add(lit);

        var faceText = Label(c.rt, "Face", "", size, anchor, BronzeLit, FontStyle.Bold);
        Stretch(faceText.rectTransform);
        var outline = faceText.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        list.Add(faceText);

        c.face = faceText;
        c.layers = list.ToArray();
        c.SetSize(size);
        c.SetFace(BronzeLit);
        return c;
    }
}
