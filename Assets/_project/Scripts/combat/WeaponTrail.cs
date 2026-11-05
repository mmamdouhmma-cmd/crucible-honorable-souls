using System.Collections.Generic;
using UnityEngine;

/// <summary>A cosmetic-only blade for dual wielding / weapons without a hitbox: grip and tip transforms.</summary>
[System.Serializable]
public class WeaponAnchor
{
    [Tooltip("Where the blade leaves the hand (or the haft's grip).")]
    public Transform grip;
    [Tooltip("The weapon's tip / striking end.")]
    public Transform tip;
}

/// <summary>
/// Animation-synchronised weapon trail. Each frame it samples the REAL blade - the two ends of the
/// weapon's own hitbox (<see cref="HitDetector.GetBladeSegment"/>) and the grip hand - and draws a
/// ribbon mesh through the recent samples, Catmull-Rom smoothed so the arc stays curved even at a low
/// frame rate. It draws only while the owner's hitbox is live (the attack's padded active window,
/// read from <see cref="CombatSystem.HitboxLive"/>) plus a short follow-through, then fades out.
///
/// Purely cosmetic: it reads the blade and the combat state and writes nothing back - no collider,
/// no timing, no hit. Look comes from a <see cref="WeaponVfxProfile"/> (per fighter) and the
/// attack's tier (Light/Medium/Heavy/Special), capped by <see cref="VfxQuality"/>.
/// Up to three layers: BODY (the smoky steel blur), CORE (a thin additive highlight along the
/// edge), WAVE (Special: a wider, slower sweep that lingers behind the cut).
/// </summary>
public class WeaponTrail : MonoBehaviour
{
    [Tooltip("Look of this fighter's trails and contacts. Empty = the built-in default for the character.")]
    [SerializeField] private WeaponVfxProfile profile;
    [Tooltip("Extra cosmetic blades (an off-hand weapon). They trail with the main swing; they never hit.")]
    [SerializeField] private WeaponAnchor[] extraWeapons = new WeaponAnchor[0];
    [Tooltip("Fallback when the fighter has no weapon box: how far past the hand (in body radii) the tip is.")]
    [SerializeField] private float fallbackBladeRadii = 2.2f;

    private Fighter fighter;
    private CombatSystem combat;
    private Animator animator;
    private readonly List<Blade> blades = new List<Blade>();
    private bool built;

    private bool emitting;
    private float tailLeft;
    private AttackType tier;
    private float clock;                 // trail time: stands still during hit-stop, so the arc freezes with the blow
    private Vector3 lastRoot;
    private bool wasLive;                // local: the hitbox was live last frame (edge detection)
    private AttackData liveAtk;          // local: the attack that stroke belongs to

    // Remote drive: a replay or the online guest has no live combat to read, so the stroke comes
    // from the record / the host instead (see SetRemoteStroke, RemoteBegin, RemoteEnd).
    private int remoteStroke = -1, shownStroke = -1;
    private bool remoteLive, remoteCut;
    private AttackType remoteTier;
    private float remoteDt = -1f;        // replay: the playback's own step (export, pause, scrub)
    private float remoteDeadline;        // guest: a lost "end" line can't leave a trail drawing
    private static Transform root;       // scene-level parent of every ribbon (world space, unscaled)
    private static Texture2D ribbonTex;

    /// <summary>The resolved profile (assigned asset, or the character default).</summary>
    public WeaponVfxProfile Profile
    {
        get
        {
            if (profile == null) profile = WeaponVfxProfile.ForCharacter(fighter != null ? fighter.CharacterName : name);
            return profile;
        }
    }

    /// <summary>Sets the profile (CombatSystem hands over its serialized one when it adds this component).</summary>
    public void SetProfile(WeaponVfxProfile p) { if (p != null) profile = p; }

    /// <summary>World velocity of the main blade's tip this frame (for directional contact bursts).</summary>
    public Vector3 TipVelocity => blades.Count > 0 ? blades[0].tipVel : Vector3.zero;

    /// <summary>True while a trail is drawing or still fading.</summary>
    public bool Visible { get; private set; }

    /// <summary>True when trails follow a record or the host rather than this machine's combat:
    /// replay playback, and the online guest (whose CombatSystems are off).</summary>
    public static bool RemoteDriven =>
        (ReplayRecorder.Instance != null && ReplayRecorder.Instance.PlaybackActive) || NetSession.GuestPlaying;

    /// <summary>Replay: the stroke state at the playhead. A new <paramref name="stroke"/> id starts a
    /// new arc; <paramref name="cut"/> (when not live) ends the tail at once, as an interrupted swing
    /// did in the fight. <paramref name="dt"/> is the playback's step this frame (0 = paused).</summary>
    public void SetRemoteStroke(int stroke, bool live, AttackType type, bool cut, float dt)
    {
        remoteStroke = stroke;
        remoteLive = live;
        remoteTier = type;
        remoteCut = cut;
        remoteDt = dt;
        remoteDeadline = float.MaxValue;
    }

    /// <summary>Online guest: the host's swing went live.</summary>
    public void RemoteBegin(AttackType type)
    {
        SetRemoteStroke(remoteStroke + 1, true, type, false, -1f);
        remoteDeadline = Time.unscaledTime + 4f;
    }

    /// <summary>Online guest: the host's swing stopped being live (<paramref name="cut"/>: interrupted).</summary>
    public void RemoteEnd(bool cut)
    {
        remoteLive = false;
        remoteCut = cut;
    }

    /// <summary>The stroke edges go to the record and (host) to the guest; both no-op otherwise.</summary>
    private void Publish(bool on, AttackType type, bool cut)
    {
        if (fighter == null) return;
        ReplayRecorder.NoteTrail(fighter, on, type, cut);
        NetSession.ForwardTrail(fighter, on, type, cut);
    }

    // ------------------------------------------------------------ data

    struct Sample { public Vector3 grip, tip; public float t; }

    class Layer
    {
        public GameObject go;
        public Mesh mesh;
        public MeshRenderer mr;
        public float inner, outer, alpha, life;
        public Color color;
        public bool on;
        public readonly List<Vector3> verts = new List<Vector3>(512);
        public readonly List<Color> cols = new List<Color>(512);
        public readonly List<Vector2> uvs = new List<Vector2>(512);
        public readonly List<int> tris = new List<int>(1536);
    }

    class Blade
    {
        public HitDetector detector;
        public WeaponAnchor anchor;
        public Transform hand, forearm;
        public Sample[] ring = new Sample[128];
        public int head = -1, count;
        public Vector3 lastTip, tipVel;
        public bool hasLast;
        public Layer[] layers = new Layer[3];
        public ParticleSystem streaks;
        public float streakDebt;
    }

    // ------------------------------------------------------------ lifecycle

    private void Awake()
    {
        fighter = GetComponent<Fighter>();
        combat = GetComponent<CombatSystem>();
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    private void Build()
    {
        built = true;
        Transform rh = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        Transform lh = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        Transform rf = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightLowerArm) : null;

        foreach (var d in GetComponentsInChildren<HitDetector>(true))
        {
            if (!d.FollowWeapon) continue;
            var b = new Blade { detector = d };
            d.GetBladeSegment(out Vector3 e0, out Vector3 e1);
            Vector3 mid = (e0 + e1) * 0.5f;
            b.hand = rh;
            if (lh != null && (rh == null || (lh.position - mid).sqrMagnitude < (rh.position - mid).sqrMagnitude)) b.hand = lh;
            blades.Add(b);
        }
        if (extraWeapons != null)
            foreach (var a in extraWeapons)
                if (a != null && a.grip != null && a.tip != null) blades.Add(new Blade { anchor = a });
        if (blades.Count == 0 && rh != null && rf != null)
            blades.Add(new Blade { hand = rh, forearm = rf });   // no weapon box: extrapolate along the forearm

        if (root == null) root = new GameObject("WeaponTrails").transform;
        for (int i = 0; i < blades.Count; i++)
        {
            for (int l = 0; l < 3; l++) blades[i].layers[l] = NewLayer($"{name}_Trail{i}_{l}", l == 1);
            blades[i].streaks = NewStreaks($"{name}_Streaks{i}");
        }
        lastRoot = transform.position;
    }

    private Layer NewLayer(string n, bool additive)
    {
        var go = new GameObject(n);
        go.transform.SetParent(root, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.sharedMaterial = VfxMaterials.Unlit(Color.white, additive, RibbonTexture());
        var mesh = new Mesh { name = n };
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;
        mr.enabled = false;
        return new Layer { go = go, mesh = mesh, mr = mr };
    }

    private ParticleSystem NewStreaks(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(root, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 96;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        main.gravityModifier = 0f;
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.06f;
        rend.lengthScale = 2.5f;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.sharedMaterial = VfxMaterials.Unlit(Color.white, true, VfxMaterials.SoftDot());
        return ps;
    }

    private void OnDisable()
    {
        if (wasLive) Publish(false, tier, true);
        wasLive = false;
        liveAtk = null;
        emitting = false;
        foreach (var b in blades) { b.count = 0; b.hasLast = false; foreach (var l in b.layers) if (l != null) { if (l.mr != null) l.mr.enabled = false; l.on = false; } }   // the ribbons may already be gone (scene teardown)
        Visible = false;
    }

    private void OnDestroy()
    {
        foreach (var b in blades)
        {
            foreach (var l in b.layers) if (l != null) { if (l.mesh != null) Destroy(l.mesh); if (l.go != null) Destroy(l.go); }
            if (b.streaks != null) Destroy(b.streaks.gameObject);
        }
        blades.Clear();
    }

    // ------------------------------------------------------------ per frame

    private void LateUpdate()
    {
        if (!built) Build();
        if (blades.Count == 0) return;

        bool remote = RemoteDriven;
        // Replay: the trail ages only with the playhead (a frame with no step is a paused replay);
        // the guest runs on its own clock, which follows the host's game speed.
        bool replaying = remote && ReplayRecorder.Instance != null && ReplayRecorder.Instance.PlaybackActive;
        float dt = replaying ? (remoteDt >= 0f ? Mathf.Min(remoteDt, 1f) : 0f) : Time.deltaTime;
        remoteDt = -1f;
        if (!HitStop.IsActive) clock += dt;
        float r = fighter != null ? Mathf.Max(fighter.BodyRadius, 0.2f) : 0.6f;

        // Teleport / round reset / replay re-pose: a jump of the root is never a swing.
        bool teleported = (transform.position - lastRoot).sqrMagnitude > (r * 5f) * (r * 5f);
        lastRoot = transform.position;

        // Emission follows the hitbox's live window - the same frames that can hit, so the trail
        // never lies about when the blade is dangerous. Kicks (no weapon) draw nothing.
        bool live, cut, newStroke;
        AttackType type;
        if (remote)
        {
            if (remoteLive && Time.unscaledTime > remoteDeadline) remoteLive = false;
            live = remoteLive;
            type = remoteTier;
            cut = remoteCut;
            newStroke = live && remoteStroke != shownStroke;
            shownStroke = remoteStroke;
            // The guest records its own replays: its edges go to its recorder (the host's forward is
            // a no-op there). Replay playback never records, so this is inert there.
            if (live != wasLive || newStroke)
            {
                if (wasLive) Publish(false, tier, cut);
                if (live) Publish(true, type, false);
            }
            wasLive = live;
            liveAtk = null;
        }
        else
        {
            AttackData atk = combat != null && combat.enabled ? combat.CurrentAttack : null;
            live = atk != null && combat.HitboxLive && atk.HitsWithWeapon;
            type = atk != null ? atk.AttackType : tier;
            cut = atk == null;                                      // the attack is gone: interrupted
            newStroke = live && (!wasLive || atk != liveAtk);       // a cancel into a new attack is a new arc
            if (wasLive && (!live || newStroke)) Publish(false, tier, cut);
            if (newStroke) Publish(true, type, false);
            wasLive = live;
            liveAtk = live ? atk : null;
        }

        WeaponVfxProfile prof = Profile;
        if (newStroke) StartStroke(type, r, remote);
        if (live)
        {
            tailLeft = prof.Tier(tier).tailSeconds;
        }
        else if (emitting)
        {
            tailLeft -= dt;
            if (tailLeft <= 0f || cut) emitting = false;   // an interrupted swing stops at once
        }

        TrailTier t = prof.Tier(tier);
        float life = Mathf.Max(0.04f, t.lifetime * prof.lifetimeScale);
        bool any = false;
        foreach (var b in blades)
        {
            Locate(b, r, out Vector3 grip, out Vector3 tip);
            if (b.hasLast && dt > 1e-5f) b.tipVel = (tip - b.lastTip) / dt;
            if (teleported || (b.hasLast && (tip - b.lastTip).sqrMagnitude > (r * 12f) * (r * 12f)))
            {
                b.count = 0;
                b.tipVel = Vector3.zero;
            }
            b.lastTip = tip;
            b.hasLast = true;

            if (emitting && !HitStop.IsActive)
            {
                Push(b, grip, tip);
                EmitStreaks(b, prof, t, grip, tip, r, dt);
            }
            any |= Draw(b, prof, t, life);
        }
        Visible = any;
    }

    private void StartStroke(AttackType type, float r, bool remote)
    {
        emitting = true;
        tier = type;
        foreach (var b in blades) b.count = 0;   // a new swing never joins the last one's fading arc
        // Remote: the dust was a recorded / forwarded effect of its own - don't raise it twice.
        if (!remote && type == AttackType.Special && VfxQuality.Atmospherics && fighter != null)
            HitEffects.SpawnDust(fighter.FeetPosition(), r, 0.6f);   // Cinematic: the wind-up lifts the dust
    }

    private void Locate(Blade b, float r, out Vector3 grip, out Vector3 tip)
    {
        if (b.anchor != null)
        {
            grip = b.anchor.grip.position;
            tip = b.anchor.tip.position;
            return;
        }
        if (b.detector != null)
        {
            b.detector.GetBladeSegment(out Vector3 e0, out Vector3 e1);
            Vector3 h = b.hand != null ? b.hand.position : (e0 + e1) * 0.5f;
            // The far end from the hand is the tip; the ribbon runs hand -> tip, so an axe's arc
            // covers the haft as well as the head and a sword's starts at the guard.
            tip = (e0 - h).sqrMagnitude > (e1 - h).sqrMagnitude ? e0 : e1;
            grip = h;
            return;
        }
        Vector3 along = (b.hand.position - b.forearm.position).normalized;
        grip = b.hand.position;
        tip = grip + along * (r * fallbackBladeRadii);
    }

    private void Push(Blade b, Vector3 grip, Vector3 tip)
    {
        // At most ~90 samples per second of game time: slow motion or a very high frame rate would
        // otherwise pack the ring with near-duplicate points (Catmull-Rom does the smoothing).
        if (b.count > 1 && clock - b.ring[b.head].t < 1f / 90f)
        {
            b.ring[b.head].grip = grip;
            b.ring[b.head].tip = tip;
            return;
        }
        b.head = (b.head + 1) % b.ring.Length;
        b.ring[b.head] = new Sample { grip = grip, tip = tip, t = clock };
        if (b.count < b.ring.Length) b.count++;
    }

    private Sample Get(Blade b, int i) => b.ring[(b.head - i + b.ring.Length * 2) % b.ring.Length];   // 0 = newest

    private void EmitStreaks(Blade b, WeaponVfxProfile prof, TrailTier t, Vector3 grip, Vector3 tip, float r, float dt)
    {
        float rate = t.streakRate * VfxQuality.StreakScale;
        if (rate <= 0f || b.streaks == null) return;
        float speed = b.tipVel.magnitude;
        if (speed < r * 2f) return;   // a resting blade throws nothing
        b.streakDebt += rate * dt;
        int n = Mathf.Min(8, (int)b.streakDebt);
        b.streakDebt -= n;
        Vector3 axis = (tip - grip).normalized;
        for (int i = 0; i < n; i++)
        {
            float f = Random.Range(prof.thrustWeapon ? 0.85f : 0.6f, 1f);
            Vector3 dir = prof.thrustWeapon ? axis * speed * 0.5f : b.tipVel * Random.Range(0.25f, 0.45f);
            var ep = new ParticleSystem.EmitParams
            {
                position = Vector3.Lerp(grip, tip, f) + Random.insideUnitSphere * (r * 0.04f),
                velocity = dir + Random.insideUnitSphere * (r * 0.5f),
                startSize = r * Random.Range(0.02f, 0.045f),
                startColor = prof.streakColor,
                startLifetime = Random.Range(0.08f, 0.16f),
                applyShapeToPosition = false,
            };
            b.streaks.Emit(ep, 1);
        }
    }

    // ------------------------------------------------------------ ribbon mesh

    private bool Draw(Blade b, WeaponVfxProfile prof, TrailTier t, float life)
    {
        int layers = Mathf.Clamp(Mathf.Min(t.layers, VfxQuality.MaxTrailLayers), 1, 3);
        float inner = t.innerFraction;
        // body, core, wave
        Setup(b.layers[0], true, inner, 1f, t.opacity, life, prof.bodyColor);
        Setup(b.layers[1], layers >= 2, Mathf.Lerp(inner, 1f, 0.72f), 1f, Mathf.Clamp01(t.opacity * 1.15f), life * 0.6f, prof.coreColor);
        Setup(b.layers[2], layers >= 3, inner * 0.7f, 0.97f, t.opacity * 0.4f, life * 1.6f, prof.waveColor);

        bool any = false;
        for (int l = 0; l < 3; l++) any |= Build(b, b.layers[l]);
        return any;
    }

    private static void Setup(Layer L, bool on, float inner, float outer, float alpha, float life, Color c)
    {
        L.on = on; L.inner = inner; L.outer = outer; L.alpha = alpha; L.life = life; L.color = c;
    }

    private bool Build(Blade b, Layer L)
    {
        L.verts.Clear(); L.cols.Clear(); L.uvs.Clear(); L.tris.Clear();
        if (!L.on || b.count < 2)
        {
            if (L.mr.enabled) { L.mr.enabled = false; L.mesh.Clear(); }
            return false;
        }

        // Points still alive for this layer, newest first.
        int alive = 0;
        while (alive < b.count && clock - Get(b, alive).t < L.life) alive++;
        if (alive < 2)
        {
            if (L.mr.enabled) { L.mr.enabled = false; L.mesh.Clear(); }
            return false;
        }

        int sub = VfxQuality.TrailSubdivisions;
        for (int i = 0; i < alive - 1; i++)
        {
            Sample p0 = Get(b, Mathf.Max(0, i - 1)), p1 = Get(b, i), p2 = Get(b, i + 1), p3 = Get(b, Mathf.Min(alive - 1, i + 2));
            int steps = i == alive - 2 ? sub + 1 : sub;   // the last segment also closes on its end point
            for (int s = 0; s < steps; s++)
            {
                float u = s / (float)sub;
                Vector3 g = CatmullRom(p0.grip, p1.grip, p2.grip, p3.grip, u);
                Vector3 tp = CatmullRom(p0.tip, p1.tip, p2.tip, p3.tip, u);
                float age = Mathf.Lerp(clock - p1.t, clock - p2.t, u);
                AddPair(L, g, tp, age);
            }
        }

        int pairs = L.verts.Count / 2;
        for (int i = 0; i < pairs - 1; i++)
        {
            int a = i * 2;
            L.tris.Add(a); L.tris.Add(a + 1); L.tris.Add(a + 2);
            L.tris.Add(a + 1); L.tris.Add(a + 3); L.tris.Add(a + 2);
        }
        Mesh m = L.mesh;
        m.Clear();
        m.SetVertices(L.verts);
        m.SetColors(L.cols);
        m.SetUVs(0, L.uvs);
        m.SetTriangles(L.tris, 0, true);
        L.mr.enabled = true;
        return true;
    }

    private static void AddPair(Layer L, Vector3 grip, Vector3 tip, float age)
    {
        float k = Mathf.Clamp01(age / L.life);
        float fade = (1f - k) * (1f - k);                          // quick, smooth tail-off
        float head = Mathf.Clamp01(age / 0.02f) * 0.6f + 0.4f;      // the newest sliver is softer: no hard edge at the blade
        Color c = L.color; c.a = L.alpha * fade * head;
        L.verts.Add(Vector3.Lerp(grip, tip, L.inner));
        L.verts.Add(Vector3.Lerp(grip, tip, L.outer));
        L.cols.Add(c); L.cols.Add(c);
        L.uvs.Add(new Vector2(k, 0f)); L.uvs.Add(new Vector2(k, 1f));
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>Across the ribbon (v): soft at the grip side, brightest just inside the edge.</summary>
    private static Texture2D RibbonTexture()
    {
        if (ribbonTex != null) return ribbonTex;
        const int h = 64;
        ribbonTex = new Texture2D(4, h, TextureFormat.RGBA32, false) { name = "VFX TrailRibbon", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[4 * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float a = Mathf.SmoothStep(0f, 1f, v / 0.7f);                // rises toward the edge
            if (v > 0.9f) a *= Mathf.Lerp(1f, 0.35f, (v - 0.9f) / 0.1f); // and softens right at it
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a);
        }
        ribbonTex.SetPixels(px);
        ribbonTex.Apply(false);
        return ribbonTex;
    }
}
