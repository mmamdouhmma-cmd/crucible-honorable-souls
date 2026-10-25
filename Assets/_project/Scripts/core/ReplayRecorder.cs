using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using UnityEngine;

/// <summary>
/// The game record: captures the fight exactly as it was rendered and plays it back in the arena.
///
/// Recording samples every transform under both fighting roots, plus the camera, once per rendered
/// frame in LateUpdate - AFTER TwoHandGrip (order 100) and WeaponAim have written their poses, so
/// what is kept is the FINAL pose of the frame, grips and aim included. Frames are timestamped
/// with UNSCALED time, so the kill shot's slow motion replays at the pace the player saw it, and
/// frames where Time.deltaTime is 0 (the pause menu) are skipped rather than recorded as a freeze.
/// Only the last <see cref="MaxSeconds"/> seconds are kept.
///
/// Playback disables everything that writes a pose (the Animator, TwoHandGrip, WeaponAim, the
/// FightCamera) and applies interpolated frames instead; <see cref="EndPlayback"/> puts every bone,
/// both rigidbodies and the camera back exactly where they were, so the K.O. tableau survives a
/// visit to the record screen. The record also carries the fight's EVENTS (2026-10-25): every
/// hit effect (hooked inside HitEffects' spawns), every one-shot sound exactly as played (hooked
/// inside CrucibleAudio.Shot) and each fighter's health and stamina per sample - playback re-fires
/// the effects and sounds and drives the real FightHud by writing the recorded bars into the
/// (frozen) fighters' stats. The kill shot's light dimming is the one presentation not recorded.
///
/// Export writes a Motion-JPEG .avi (each frame a whole JPEG, no external encoder, plays in VLC
/// and Windows) to the user's Videos folder, reading frames straight off the screen - so whoever
/// starts an export must hide any UI that should not be baked into the video; the HUD and the
/// re-fired effects ARE in the frames, kept in lockstep by Time.captureDeltaTime = 1/fps. The
/// soundtrack is MIXED, not captured: the recorded one-shots at their exact moments over the
/// fight-music and wind beds, written as a PCM stream into the same .avi. The main thread only
/// CAPTURES: JPEG encoding runs on worker threads (ImageConversion.EncodeArrayToJPG is
/// thread-safe) and the file is written in frame order by its own thread, so the export runs at
/// whatever rate the game can render instead of stalling ~40 ms per frame on the encode.
///
/// Sits on the GameMangers object; GameMenu drives it.
/// </summary>
[DefaultExecutionOrder(300)]
public class ReplayRecorder : MonoBehaviour
{
    public static ReplayRecorder Instance { get; private set; }

    /// <summary>Seconds of fight kept - generous enough to be the WHOLE fight for any sane match;
    /// a marathon only loses its oldest minutes. Memory is bounded by the 30 Hz sampling below.</summary>
    const float MaxSeconds = 600f;

    /// <summary>Poses are sampled at 30 Hz (playback interpolates between them), which halves the
    /// buffer against per-frame sampling and matches the export's frame rate.</summary>
    const float SampleInterval = 1f / 30f;

    public const int ExportFps = 30;

    /// <summary>Camera the playback (and the export) looks through. AsPlayed replays the match
    /// camera; the rest are locked angles computed from the two fighters each frame.</summary>
    public enum ReplayView { AsPlayed, SideA, SideB, BehindP1, BehindP2, Overhead }

    public ReplayView View = ReplayView.AsPlayed;

    public string ViewName => View == ReplayView.AsPlayed ? "As played"
                            : View == ReplayView.SideA ? "Side A"
                            : View == ReplayView.SideB ? "Side B"
                            : View == ReplayView.BehindP1 ? "Behind Player 1"
                            : View == ReplayView.BehindP2 ? "Behind Player 2" : "Overhead";

    public void CycleView(int dir)
    {
        const int n = 6;
        View = (ReplayView)((((int)View + dir) % n + n) % n);
    }

    class Frame
    {
        public float t;             // seconds of unscaled time since recording began
        public Vector3[] pos;       // per track: localPosition (fighter roots: world position)
        public Quaternion[] rot;    // per track: localRotation (fighter roots: world rotation)
        public Vector3 camPos;
        public Quaternion camRot;
        public int[] hp;            // per cast fighter: health at this sample - drives the HUD in playback
        public int[] st;            // per cast fighter: stamina
    }

    struct Marker
    {
        public float t;             // on the same clock as Frame.t
        public string label;
    }

    /// <summary>The visual effects the record can replay (hooked inside HitEffects' spawns).</summary>
    public enum Fx : byte { Sparks, Parry, Dust, Dodge, Blood }

    struct FxEvent
    {
        public float t;             // on the same clock as Frame.t
        public Fx kind;
        public int a, b;            // Blood: cast indices of target and attacker; -1 otherwise
        public Vector3 point, dir;  // world-space, where the fight record will re-pose the fighters
        public float radius, strength;
        public Color color;
    }

    struct SoundEvent
    {
        public float t;
        public AudioClip clip;      // the exact variant played
        public float volume, pitch; // FINAL values (MasterLevel, Settings and jitter included)
    }

    readonly List<Frame> frames = new List<Frame>();
    readonly List<Marker> markers = new List<Marker>();
    readonly List<FxEvent> fxEvents = new List<FxEvent>();
    readonly List<SoundEvent> soundEvents = new List<SoundEvent>();
    float lastEventT;               // absolute record time of the last Apply, for event firing
    int[] savedHp = new int[0];     // the real stats, remembered across a playback
    int[] savedSt = new int[0];
    Transform[] tracks = new Transform[0];
    bool[] isRoot = new bool[0];
    Fighter[] cast = new Fighter[0];
    Camera cam;
    bool recording;
    float clock;
    float lastSampleT;
    int playhead;                   // cached frame index, so sequential playback never re-searches

    public bool Recording => recording;
    public bool HasRecording => frames.Count > 1;
    public float Duration => frames.Count > 1 ? frames[frames.Count - 1].t - frames[0].t : 0f;

    // ---------------------------------------------------------------- markers

    /// <summary>Tags the current moment of the recording - a counter, a landed combo, a special,
    /// the kill - so the record screen can jump straight to it. Ignored when not recording.</summary>
    public void Mark(string label)
    {
        if (!recording) return;
        markers.Add(new Marker { t = clock, label = label });
    }

    public int MarkerCount => markers.Count;
    public string MarkerLabel(int i) => i >= 0 && i < markers.Count ? markers[i].label : "";
    /// <summary>The marker's moment in playback time (seconds from the start of the record).</summary>
    public float MarkerTime(int i) => i >= 0 && i < markers.Count && frames.Count > 0
        ? Mathf.Clamp(markers[i].t - frames[0].t, 0f, Duration) : 0f;

    // ---------------------------------------------------------------- effect and sound hooks
    // Called from the choke points every effect and one-shot already goes through (HitEffects'
    // spawns, CrucibleAudio.Shot), so every caller is covered without knowing about the recorder.
    // All no-op unless a recording is running and game time is advancing - a pause menu's UI
    // ticks are not part of the fight. Playback has recording off, so re-fired events never
    // re-record themselves.

    static bool Noting => Instance != null && Instance.recording && Time.deltaTime > 0f;

    /// <summary>Records a positional effect (sparks, parry flash, dust, dodge burst).</summary>
    public static void NoteFx(Fx kind, Vector3 point, Vector3 dir, float radius, float strength)
    {
        if (!Noting) return;
        Instance.fxEvents.Add(new FxEvent
        {
            t = Instance.clock, kind = kind, a = -1, b = -1,
            point = point, dir = dir, radius = radius, strength = strength,
        });
    }

    /// <summary>Records a blood spray by its two fighters - playback re-fires it against the
    /// re-posed pair, so the spray leaves the body exactly where it did in the fight.</summary>
    public static void NoteBlood(Fighter target, Fighter attacker, Color color)
    {
        if (!Noting) return;
        var r = Instance;
        int a = r.CastIndex(target), b = r.CastIndex(attacker);
        if (a < 0 || b < 0) return;
        r.fxEvents.Add(new FxEvent { t = r.clock, kind = Fx.Blood, a = a, b = b, color = color });
    }

    /// <summary>Records a one-shot sound exactly as played (clip variant, final volume and pitch).</summary>
    public static void NoteSound(AudioClip clip, float volume, float pitch)
    {
        if (!Noting || clip == null) return;
        Instance.soundEvents.Add(new SoundEvent { t = Instance.clock, clip = clip, volume = volume, pitch = pitch });
    }

    int CastIndex(Fighter f)
    {
        for (int i = 0; i < cast.Length; i++) if (cast[i] == f) return i;
        return -1;
    }

    // ---------------------------------------------------------------- playback bookkeeping

    public bool PlaybackActive { get; private set; }

    readonly List<(Behaviour component, bool wasEnabled)> savedBehaviours = new List<(Behaviour, bool)>();
    readonly List<(Rigidbody rb, bool wasKinematic)> savedBodies = new List<(Rigidbody, bool)>();
    Vector3[] savedPos = new Vector3[0];
    Quaternion[] savedRot = new Quaternion[0];
    Vector3 savedCamPos;
    Quaternion savedCamRot;
    bool savedFightCam;

    // ---------------------------------------------------------------- export status

    public bool Exporting { get; private set; }
    public float ExportProgress { get; private set; }
    public string ExportResult { get; private set; }
    public string ExportError { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    // ================================================================ recording

    /// <summary>Starts a fresh record of the fighters in <paramref name="roster"/> that are
    /// actually in the match (benched ones are inactive and skipped).</summary>
    public void BeginRecording(Fighter[] roster, Camera camera)
    {
        if (PlaybackActive) EndPlayback();

        var fighters = new List<Fighter>();
        foreach (var f in roster)
            if (f != null && f.gameObject.activeInHierarchy) fighters.Add(f);
        fighters.Sort((a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));   // cast[0] IS Player 1

        var list = new List<Transform>();
        var roots = new List<bool>();
        foreach (var f in fighters)
        {
            foreach (var tr in f.GetComponentsInChildren<Transform>(true))
            {
                list.Add(tr);
                roots.Add(tr == f.transform);
            }
        }
        cast = fighters.ToArray();
        tracks = list.ToArray();
        isRoot = roots.ToArray();
        cam = camera != null ? camera : Camera.main;
        frames.Clear();
        markers.Clear();
        fxEvents.Clear();
        soundEvents.Clear();
        playhead = 0;
        clock = 0f;
        lastSampleT = -1f;
        recording = tracks.Length > 0;
    }

    /// <summary>Stops sampling; the buffer is kept until the next <see cref="BeginRecording"/>.</summary>
    public void StopRecording() => recording = false;

    void LateUpdate()
    {
        if (!recording) return;
        // timeScale 0 is the pause menu: the fight is not advancing, so these rendered frames are
        // not part of it. (The kill shot's 0.2 still records, with the slow motion in the clock.)
        if (Time.deltaTime <= 0f) return;
        clock += Time.unscaledDeltaTime;
        if (clock - lastSampleT < SampleInterval - 0.0005f) return;   // 30 Hz is plenty; playback interpolates
        lastSampleT = clock;

        var f = new Frame
        {
            t = clock,
            pos = new Vector3[tracks.Length],
            rot = new Quaternion[tracks.Length],
        };
        for (int i = 0; i < tracks.Length; i++)
        {
            var tr = tracks[i];
            if (tr == null) continue;
            if (isRoot[i]) { f.pos[i] = tr.position; f.rot[i] = tr.rotation; }
            else { f.pos[i] = tr.localPosition; f.rot[i] = tr.localRotation; }
        }
        if (cam != null) { f.camPos = cam.transform.position; f.camRot = cam.transform.rotation; }

        // The bars: health and stamina per fighter, so playback can drive the real HUD.
        f.hp = new int[cast.Length];
        f.st = new int[cast.Length];
        for (int i = 0; i < cast.Length; i++)
        {
            var s = cast[i] != null ? cast[i].Stats : null;
            if (s == null) continue;
            f.hp[i] = s.currentHealth;
            f.st[i] = s.currentStamina;
        }
        frames.Add(f);

        // The ring: trim in batches, since RemoveRange shifts the whole list.
        if (frames.Count > 2 && clock - frames[0].t > MaxSeconds + 5f)
        {
            int cut = 0;
            while (cut < frames.Count - 2 && clock - frames[cut].t > MaxSeconds) cut++;
            if (cut > 0)
            {
                frames.RemoveRange(0, cut);
                markers.RemoveAll(m => m.t < frames[0].t);
                fxEvents.RemoveAll(e => e.t < frames[0].t);
                soundEvents.RemoveAll(e => e.t < frames[0].t);
            }
        }
    }

    // ================================================================ playback

    /// <summary>
    /// Takes the fighters and the camera over for playback. Everything that writes a pose stands
    /// down, and the current tableau is remembered so <see cref="EndPlayback"/> can restore it.
    /// </summary>
    public void BeginPlayback()
    {
        if (PlaybackActive || !HasRecording) return;
        recording = false;
        PlaybackActive = true;

        savedBehaviours.Clear();
        savedBodies.Clear();
        foreach (var f in cast)
        {
            if (f == null) continue;
            TakeOver(f.GetComponent<Animator>());
            foreach (var b in f.GetComponentsInChildren<TwoHandGrip>(true)) TakeOver(b);
            foreach (var b in f.GetComponentsInChildren<WeaponAim>(true)) TakeOver(b);
            var rb = f.GetComponent<Rigidbody>();
            if (rb != null) { savedBodies.Add((rb, rb.isKinematic)); rb.isKinematic = true; }
        }
        if (FightCamera.Instance != null)
        {
            savedFightCam = FightCamera.Instance.enabled;
            FightCamera.Instance.enabled = false;
        }

        savedPos = new Vector3[tracks.Length];
        savedRot = new Quaternion[tracks.Length];
        for (int i = 0; i < tracks.Length; i++)
        {
            var tr = tracks[i];
            if (tr == null) continue;
            if (isRoot[i]) { savedPos[i] = tr.position; savedRot[i] = tr.rotation; }
            else { savedPos[i] = tr.localPosition; savedRot[i] = tr.localRotation; }
        }
        if (cam != null) { savedCamPos = cam.transform.position; savedCamRot = cam.transform.rotation; }

        // The real stats, so playback can drive the HUD with the RECORDED bars and still hand the
        // K.O. tableau back untouched. The fighters are frozen (disabled) outside a match, so the
        // writes are inert everywhere but the HUD, which polls them.
        savedHp = new int[cast.Length];
        savedSt = new int[cast.Length];
        for (int i = 0; i < cast.Length; i++)
        {
            var s = cast[i] != null ? cast[i].Stats : null;
            if (s == null) continue;
            savedHp[i] = s.currentHealth;
            savedSt[i] = s.currentStamina;
        }

        playhead = 0;
        lastEventT = frames.Count > 0 ? frames[0].t : 0f;
        Apply(0f);
    }

    void TakeOver(Behaviour b)
    {
        if (b == null) return;
        savedBehaviours.Add((b, b.enabled));
        b.enabled = false;
    }

    /// <summary>Hands everything back and restores the pose the playback interrupted.</summary>
    public void EndPlayback()
    {
        if (!PlaybackActive) return;
        PlaybackActive = false;

        for (int i = 0; i < tracks.Length; i++)
        {
            var tr = tracks[i];
            if (tr == null || i >= savedPos.Length) continue;
            if (isRoot[i]) { tr.position = savedPos[i]; tr.rotation = savedRot[i]; }
            else { tr.localPosition = savedPos[i]; tr.localRotation = savedRot[i]; }
        }
        foreach (var (b, was) in savedBehaviours) if (b != null) b.enabled = was;
        foreach (var (rb, was) in savedBodies) if (rb != null) rb.isKinematic = was;
        savedBehaviours.Clear();
        savedBodies.Clear();
        for (int i = 0; i < cast.Length && i < savedHp.Length; i++)
        {
            var s = cast[i] != null ? cast[i].Stats : null;
            if (s == null) continue;
            s.currentHealth = savedHp[i];
            s.currentStamina = savedSt[i];
        }
        if (cam != null) { cam.transform.position = savedCamPos; cam.transform.rotation = savedCamRot; }
        if (FightCamera.Instance != null) FightCamera.Instance.enabled = savedFightCam;
        Physics.SyncTransforms();
    }

    /// <summary>Poses the fighters and the camera at <paramref name="time"/> seconds into the
    /// record, interpolating between the two nearest frames.</summary>
    public void Apply(float time)
    {
        if (frames.Count == 0 || tracks.Length == 0) return;
        float t = frames[0].t + Mathf.Clamp(time, 0f, Duration);

        if (playhead >= frames.Count) playhead = frames.Count - 1;
        while (playhead > 0 && frames[playhead].t > t) playhead--;
        while (playhead < frames.Count - 1 && frames[playhead + 1].t <= t) playhead++;

        var a = frames[playhead];
        var b = playhead < frames.Count - 1 ? frames[playhead + 1] : a;
        float k = b.t > a.t ? Mathf.InverseLerp(a.t, b.t, t) : 0f;

        for (int i = 0; i < tracks.Length; i++)
        {
            var tr = tracks[i];
            if (tr == null) continue;
            Vector3 p = Vector3.Lerp(a.pos[i], b.pos[i], k);
            Quaternion q = Quaternion.Slerp(a.rot[i], b.rot[i], k);
            if (isRoot[i]) { tr.position = p; tr.rotation = q; }
            else { tr.localPosition = p; tr.localRotation = q; }
        }
        if (cam != null)
        {
            if (View == ReplayView.AsPlayed)
            {
                cam.transform.position = Vector3.Lerp(a.camPos, b.camPos, k);
                cam.transform.rotation = Quaternion.Slerp(a.camRot, b.camRot, k);
            }
            else ApplyLockedView();
        }

        if (PlaybackActive)
        {
            // The bars: write the recorded health and stamina into the (frozen) fighters, and the
            // live FightHud animates them exactly as it did in the fight. Restored by EndPlayback.
            if (a.hp != null)
            {
                for (int i = 0; i < cast.Length && i < a.hp.Length; i++)
                {
                    var s = cast[i] != null ? cast[i].Stats : null;
                    if (s == null) continue;
                    s.currentHealth = a.hp[i];
                    s.currentStamina = a.st[i];
                }
            }
            FireEvents(t);
        }
    }

    /// <summary>
    /// Re-fires the effects (and, outside an export, the sounds) whose moment just passed. Only a
    /// small FORWARD step fires anything: a scrub, a jump to a marker or the loop back to the
    /// cut's start moves the playhead without replaying half a fight's blood in one frame.
    /// </summary>
    void FireEvents(float absT)
    {
        float step = absT - lastEventT;
        if (step > 0f && step <= 0.5f)
        {
            for (int i = 0; i < fxEvents.Count; i++)
            {
                var e = fxEvents[i];
                if (e.t <= lastEventT || e.t > absT) continue;
                switch (e.kind)
                {
                    case Fx.Blood:
                        if (e.a >= 0 && e.a < cast.Length && e.b >= 0 && e.b < cast.Length &&
                            cast[e.a] != null && cast[e.b] != null)
                            HitEffects.SpawnBlood(cast[e.a], cast[e.b], e.color);
                        break;
                    case Fx.Sparks: HitEffects.SpawnSparks(e.point, e.dir, e.radius, e.strength); break;
                    case Fx.Parry: HitEffects.SpawnParryFlash(e.point, e.dir, e.radius, e.strength); break;
                    case Fx.Dust: HitEffects.SpawnDust(e.point, e.radius, e.strength); break;
                    case Fx.Dodge: HitEffects.SpawnDodgeBurst(e.point, e.radius, e.strength); break;
                }
            }
            // The export mixes its own soundtrack sample-exact instead; live playback hears them.
            if (!Exporting)
            {
                for (int i = 0; i < soundEvents.Count; i++)
                {
                    var e = soundEvents[i];
                    if (e.t <= lastEventT || e.t > absT) continue;
                    CrucibleAudio.PlayExact(e.clip, e.volume, e.pitch);
                }
            }
        }
        lastEventT = absT;
    }

    /// <summary>
    /// A locked camera angle, computed from where the two fighters stand on the frame just
    /// applied: a fixed direction relative to the pair, at a distance that keeps both in shot.
    /// </summary>
    void ApplyLockedView()
    {
        if (cast.Length < 2 || cast[0] == null || cast[1] == null) return;
        Vector3 pa = cast[0].transform.position;
        Vector3 pb = cast[1].transform.position;
        Vector3 mid = (pa + pb) * 0.5f;
        Vector3 axis = pb - pa;
        axis.y = 0f;
        float gap = axis.magnitude;
        axis = gap > 0.1f ? axis / gap : Vector3.right;
        Vector3 side = Vector3.Cross(Vector3.up, axis);

        float dist = Mathf.Max(6f, gap * 0.9f + 4f);
        Vector3 look = mid + Vector3.up * 1.4f;
        Vector3 at;
        switch (View)
        {
            case ReplayView.SideB: at = look - side * dist + Vector3.up * 1.2f; break;
            case ReplayView.BehindP1: at = look - axis * (dist * 1.1f) + Vector3.up * 1.8f; break;
            case ReplayView.BehindP2: at = look + axis * (dist * 1.1f) + Vector3.up * 1.8f; break;
            case ReplayView.Overhead: at = look + side * (dist * 0.5f) + Vector3.up * (dist * 0.9f); break;
            default: at = look + side * dist + Vector3.up * 1.2f; break;   // SideA
        }
        cam.transform.position = at;
        cam.transform.rotation = Quaternion.LookRotation(look - at, Vector3.up);
    }

    // ================================================================ export

    /// <summary>
    /// Writes the cut [<paramref name="from"/>, <paramref name="to"/>] as an .avi, driving the
    /// playback itself one export frame at a time. The caller hides its UI first - frames are read
    /// straight off the screen - and watches <see cref="Exporting"/> / <see cref="ExportResult"/> /
    /// <see cref="ExportError"/>.
    /// </summary>
    public void BeginExport(float from, float to)
        => BeginExport(new List<Vector2> { new Vector2(from, to) });

    /// <summary>Writes several cuts of the record stitched into one video - the highlight reel.
    /// Each segment is (from, to) in playback seconds, played in order.</summary>
    public void BeginExport(List<Vector2> segments)
    {
        if (Exporting || !PlaybackActive || segments == null || segments.Count == 0) return;
        StartCoroutine(ExportRoutine(segments));
    }

    /// <summary>
    /// The record's eventful windows - a few seconds around every marker (parries, counters,
    /// specials, combo links, the kill) and every landed hit, parry flash or successful dodge -
    /// merged where they overlap or nearly touch (a sub-second gap would re-fire its events
    /// across the seam anyway).
    /// Playback-relative seconds, ready for <see cref="BeginExport(List{Vector2})"/>.
    /// </summary>
    public List<Vector2> HighlightSegments(float pre = 2.0f, float post = 1.6f)
    {
        var outList = new List<Vector2>();
        if (frames.Count < 2) return outList;
        float t0 = frames[0].t;
        var times = new List<float>();
        foreach (var m in markers) times.Add(m.t - t0);
        // Fx.Dodge only ever marks a SUCCESSFUL dodge (Fighter.NotifyEvaded); plain dash dust is Fx.Dust.
        foreach (var e in fxEvents) if (e.kind == Fx.Blood || e.kind == Fx.Parry || e.kind == Fx.Dodge) times.Add(e.t - t0);
        times.Sort();
        foreach (float t in times)
        {
            float a = Mathf.Clamp(t - pre, 0f, Duration);
            float b = Mathf.Clamp(t + post, 0f, Duration);
            if (b - a < 0.2f) continue;
            if (outList.Count > 0 && a <= outList[outList.Count - 1].y + 0.75f)
                outList[outList.Count - 1] = new Vector2(outList[outList.Count - 1].x,
                                                         Mathf.Max(outList[outList.Count - 1].y, b));
            else outList.Add(new Vector2(a, b));
        }
        return outList;
    }

    IEnumerator ExportRoutine(List<Vector2> segs)
    {
        Exporting = true;
        ExportProgress = 0f;
        ExportResult = null;
        ExportError = null;

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            // There is no file system a video player could reach from the web build.
            ExportError = "video export needs the desktop build";
            Exporting = false;
            yield break;
        }

        string path = null;
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(dir)) dir = Application.persistentDataPath;
            dir = Path.Combine(dir, "CRUCIBLE");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, $"crucible_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.avi");
        }
        catch (Exception e)
        {
            ExportError = e.Message;
            Exporting = false;
            yield break;
        }

        // Flatten the cuts into one list of capture times: the writer and the audio only ever see
        // the global frame index, so a multi-cut reel streams exactly like a single cut.
        int total = 0;
        var segFrames = new int[segs.Count];
        for (int s = 0; s < segs.Count; s++)
        {
            segFrames[s] = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.1f, segs[s].y - segs[s].x) * ExportFps));
            total += segFrames[s];
        }
        var times = new float[total];
        {
            int gi = 0;
            for (int s = 0; s < segs.Count; s++)
                for (int i = 0; i < segFrames[s]; i++)
                    times[gi++] = segs[s].x + i / (float)ExportFps;
        }

        // The soundtrack is MIXED, not captured: every recorded one-shot at its exact moment
        // (final volume and pitch), under the fight-music and wind beds, sample-accurate against
        // the frames. Mixing can fail (a streaming clip has no readable samples) - the video then
        // ships silent rather than not at all.
        int audioRate = AudioSettings.outputSampleRate;
        if (audioRate < 8000) audioRate = 48000;
        // Each segment is mixed on its own and laid at its own sample offset, so one silent cut
        // cannot cost the whole reel its soundtrack.
        short[] pcm = null;
        try
        {
            long allSamples = (long)total * audioRate / ExportFps;
            int done = 0;
            for (int s = 0; s < segs.Count; s++)
            {
                var part = MixAudio(segs[s].x, segFrames[s], audioRate);
                if (part != null)
                {
                    if (pcm == null) pcm = new short[allSamples * 2];
                    long off = (long)done * audioRate / ExportFps * 2;
                    Array.Copy(part, 0, pcm, off, Math.Min(part.Length, pcm.Length - off));
                }
                done += segFrames[s];
            }
        }
        catch (Exception e) { Debug.LogWarning($"Replay export: no audio track ({e.Message})"); pcm = null; }

        // One rendered frame = exactly 1/fps of game time, so particle effects re-fired by Apply
        // advance in lockstep with the captured frames whatever the real frame rate is. The game
        // is muted meanwhile - what the player hears during the write is not the video's track.
        float savedCapture = Time.captureDeltaTime;
        float savedListener = AudioListener.volume;
        Time.captureDeltaTime = 1f / ExportFps;
        AudioListener.volume = 0f;

        // The pipeline: the MAIN thread only captures raw pixels (Apply + readback), WORKER
        // threads JPEG-encode them (EncodeArrayToJPG takes plain arrays and is thread-safe),
        // and one WRITER thread streams the frames to disk in order with the audio interleaved.
        // Inline, the ~40 ms 1080p encode alone made every export slower than real time.
        MjpegAviWriter writer = null;
        var jobs = new System.Collections.Concurrent.BlockingCollection<EncodeJob>(16);
        var jpegs = new System.Collections.Concurrent.ConcurrentDictionary<int, byte[]>();
        Exception threadError = null;
        bool encodersDone = false;
        var encoders = new List<Thread>();
        Thread writerThread = null;

        void StartThreads()
        {
            int n = Mathf.Clamp(SystemInfo.processorCount - 2, 2, 6);
            for (int t = 0; t < n; t++)
            {
                var enc = new Thread(() =>
                {
                    foreach (var j in jobs.GetConsumingEnumerable())
                    {
                        byte[] jpg = null;
                        if (j.raw != null)
                            try { jpg = ImageConversion.EncodeArrayToJPG(j.raw, j.fmt, (uint)j.w, (uint)j.h, 0, 88); }
                            catch (Exception e) { threadError = e; }
                        jpegs[j.index] = jpg;           // null = a skipped (resized) frame
                    }
                }) { IsBackground = true, Name = "ReplayJpeg" };
                enc.Start();
                encoders.Add(enc);
            }
            writerThread = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < total; i++)
                    {
                        byte[] jpg;
                        while (!jpegs.TryRemove(i, out jpg))
                        {
                            // The capture loop aborted early: this index is never coming.
                            if (encodersDone && !jpegs.ContainsKey(i)) return;
                            Thread.Sleep(1);
                        }
                        if (jpg == null) continue;      // resized frame, dropped as before
                        writer.AddFrame(jpg);
                        if (pcm != null)
                        {
                            long s0 = (long)i * audioRate / ExportFps;
                            long s1 = (long)(i + 1) * audioRate / ExportFps;
                            writer.AddAudio(pcm, s0, (int)(s1 - s0));
                        }
                    }
                }
                catch (Exception e) { threadError = e; }
            }) { IsBackground = true, Name = "ReplayAvi" };
            writerThread.Start();
        }

        for (int i = 0; i < total; i++)
        {
            Apply(times[i]);
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                if (writer == null)
                {
                    writer = new MjpegAviWriter(path, shot.width, shot.height, ExportFps,
                                                pcm != null ? audioRate : 0);
                    StartThreads();
                }
                // A window resize mid-export changes the frame size, which an .avi cannot hold.
                bool skip = shot.width != writer.Width || shot.height != writer.Height;
                jobs.Add(new EncodeJob
                {
                    index = i,
                    raw = skip ? null : shot.GetRawTextureData(),
                    fmt = shot.graphicsFormat,
                    w = shot.width,
                    h = shot.height,
                });
            }
            catch (Exception e)
            {
                ExportError = e.Message;
            }
            finally
            {
                if (shot != null) Destroy(shot);
            }
            if (ExportError != null || threadError != null) break;
            ExportProgress = (i + 1) / (float)total;
        }

        Time.captureDeltaTime = savedCapture;
        AudioListener.volume = savedListener;

        // Drain the pipeline: encoders exit when the queue completes, then the writer either
        // finishes the run or bails on the index that will never arrive.
        jobs.CompleteAdding();
        foreach (var enc in encoders) while (enc.IsAlive) yield return null;
        encodersDone = true;
        if (writerThread != null) while (writerThread.IsAlive) yield return null;
        jobs.Dispose();
        if (threadError != null && ExportError == null) ExportError = threadError.Message;

        try { writer?.Finish(); }
        catch (Exception e) { if (ExportError == null) ExportError = e.Message; }

        if (ExportError == null) ExportResult = path;
        Exporting = false;
    }

    /// <summary>One captured frame on its way to a worker thread. raw == null marks a frame
    /// dropped for a mid-export window resize (the writer skips it, as the inline path did).</summary>
    struct EncodeJob
    {
        public int index;
        public byte[] raw;
        public UnityEngine.Experimental.Rendering.GraphicsFormat fmt;
        public int w, h;
    }

    // ================================================================ records on disk
    // Every finished fight is saved to the records library (gzipped binary, ~1 MB per fight
    // minute), listed on the main menu's Records screen and loaded back for the same playback
    // and export the K.O. card offers. Transforms are stored by their path under each fighter's
    // root and sounds by clip name, so a record binds to the LIVE roster when it is loaded -
    // which is why a record of a fighter who left the build refuses to load rather than
    // half-playing.

    /// <summary>How many fights the library keeps; the oldest file goes when a new one lands.</summary>
    public const int MaxSavedRecords = 20;

    const int FileVersion = 1;
    static readonly byte[] FileMagic = { (byte)'C', (byte)'R', (byte)'E', (byte)'C' };

    public static string RecordsDir => Path.Combine(Application.persistentDataPath, "Records");

    public struct RecordInfo
    {
        public string path, title, arena, castA, castB;
        public DateTime date;
        public float duration;
    }

    /// <summary>Writes the finished recording to the records library. Returns the path, or null
    /// (a failed save never costs the match flow anything but a warning).</summary>
    public string SaveRecord(string title, string arenaName)
    {
        if (!HasRecording || cast.Length == 0) return null;
        try
        {
            Directory.CreateDirectory(RecordsDir);
            string path = Path.Combine(RecordsDir, $"fight_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.crec");
            using (var w = new BinaryWriter(new GZipStream(File.Create(path), System.IO.Compression.CompressionLevel.Fastest)))
            {
                w.Write(FileMagic);
                w.Write(FileVersion);
                w.Write(title ?? "");
                w.Write(DateTime.Now.Ticks);
                w.Write(arenaName ?? "");
                w.Write(Duration);
                w.Write(cast.Length);
                foreach (var f in cast) w.Write(f != null ? f.CharacterName : "");
                w.Write(tracks.Length);
                for (int i = 0; i < tracks.Length; i++)
                {
                    string p = TrackPath(tracks[i], out int ci);
                    w.Write((byte)(ci < 0 ? 255 : ci));
                    w.Write(p);
                }
                w.Write(frames.Count);
                foreach (var f in frames)
                {
                    w.Write(f.t);
                    for (int i = 0; i < tracks.Length; i++)
                    {
                        var p = f.pos[i]; var q = f.rot[i];
                        w.Write(p.x); w.Write(p.y); w.Write(p.z);
                        w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w);
                    }
                    w.Write(f.camPos.x); w.Write(f.camPos.y); w.Write(f.camPos.z);
                    w.Write(f.camRot.x); w.Write(f.camRot.y); w.Write(f.camRot.z); w.Write(f.camRot.w);
                    for (int i = 0; i < cast.Length; i++)
                    {
                        w.Write((short)(f.hp != null && i < f.hp.Length ? f.hp[i] : 0));
                        w.Write((short)(f.st != null && i < f.st.Length ? f.st[i] : 0));
                    }
                }
                w.Write(markers.Count);
                foreach (var m in markers) { w.Write(m.t); w.Write(m.label ?? ""); }
                w.Write(fxEvents.Count);
                foreach (var e in fxEvents)
                {
                    w.Write(e.t); w.Write((byte)e.kind); w.Write(e.a); w.Write(e.b);
                    w.Write(e.point.x); w.Write(e.point.y); w.Write(e.point.z);
                    w.Write(e.dir.x); w.Write(e.dir.y); w.Write(e.dir.z);
                    w.Write(e.radius); w.Write(e.strength);
                    w.Write(e.color.r); w.Write(e.color.g); w.Write(e.color.b); w.Write(e.color.a);
                }
                w.Write(soundEvents.Count);
                foreach (var e in soundEvents)
                {
                    w.Write(e.t);
                    w.Write(e.clip != null ? e.clip.name : "");
                    w.Write(e.volume); w.Write(e.pitch);
                }
            }
            Prune();
            return path;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Record save failed: {e.Message}");
            return null;
        }
    }

    static void Prune()
    {
        var files = Directory.GetFiles(RecordsDir, "*.crec");
        if (files.Length <= MaxSavedRecords) return;
        Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
        for (int i = 0; i < files.Length - MaxSavedRecords; i++) File.Delete(files[i]);
    }

    /// <summary>The track's path under its fighter's root ("" for the root itself), and which
    /// cast member owns it (-1 when it belongs to neither - a destroyed transform).</summary>
    string TrackPath(Transform tr, out int castIdx)
    {
        castIdx = -1;
        if (tr == null) return "";
        for (int c = 0; c < cast.Length; c++)
        {
            if (cast[c] == null) continue;
            var root = cast[c].transform;
            if (!tr.IsChildOf(root)) continue;      // IsChildOf includes the root itself
            castIdx = c;
            if (tr == root) return "";
            string p = tr.name;
            for (var up = tr.parent; up != null && up != root; up = up.parent) p = up.name + "/" + p;
            return p;
        }
        return "";
    }

    /// <summary>The library, newest first. Reads only each file's header, so it is cheap to call
    /// every time the Records screen opens.</summary>
    public static List<RecordInfo> ListRecords()
    {
        var list = new List<RecordInfo>();
        try
        {
            if (!Directory.Exists(RecordsDir)) return list;
            foreach (var path in Directory.GetFiles(RecordsDir, "*.crec"))
            {
                try
                {
                    using (var r = new BinaryReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress)))
                    {
                        if (!ReadMagic(r)) continue;
                        if (r.ReadInt32() != FileVersion) continue;
                        var info = new RecordInfo { path = path };
                        info.title = r.ReadString();
                        info.date = new DateTime(r.ReadInt64());
                        info.arena = r.ReadString();
                        info.duration = r.ReadSingle();
                        int nCast = r.ReadInt32();
                        info.castA = nCast > 0 ? r.ReadString() : "";
                        info.castB = nCast > 1 ? r.ReadString() : "";
                        list.Add(info);
                    }
                }
                catch { /* a truncated or foreign file is simply not listed */ }
            }
        }
        catch { }
        list.Sort((a, b) => b.date.CompareTo(a.date));
        return list;
    }

    static bool ReadMagic(BinaryReader r)
    {
        var m = r.ReadBytes(4);
        return m.Length == 4 && m[0] == 'C' && m[1] == 'R' && m[2] == 'E' && m[3] == 'C';
    }

    /// <summary>
    /// Loads a saved record and binds it to the live roster - the caller has already activated
    /// the recorded pair (<c>ApplySides</c>). After it, <see cref="BeginPlayback"/> and the
    /// exports work exactly as on a fresh recording. On failure the current buffer is left alone
    /// where possible and <paramref name="error"/> says why.
    /// </summary>
    public bool LoadRecord(string path, Fighter[] roster, Camera camera, out string error)
    {
        error = null;
        if (PlaybackActive) EndPlayback();
        recording = false;
        try
        {
            using (var r = new BinaryReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress)))
            {
                if (!ReadMagic(r)) { error = "not a fight record"; return false; }
                if (r.ReadInt32() != FileVersion) { error = "saved by a different version of the game"; return false; }
                r.ReadString(); r.ReadInt64(); r.ReadString(); r.ReadSingle();   // title, date, arena, duration

                int nCast = r.ReadInt32();
                if (nCast < 1 || nCast > 8) { error = "the record is damaged"; return false; }
                var newCast = new Fighter[nCast];
                for (int i = 0; i < nCast; i++)
                {
                    string name = r.ReadString();
                    foreach (var f in roster)
                        if (f != null && f.CharacterName == name && Array.IndexOf(newCast, f) < 0) { newCast[i] = f; break; }
                    if (newCast[i] == null) { error = $"{name} is not in this build"; return false; }
                }

                int nTracks = r.ReadInt32();
                if (nTracks < 1 || nTracks > 4096) { error = "the record is damaged"; return false; }
                var newTracks = new Transform[nTracks];
                var newRoots = new bool[nTracks];
                for (int i = 0; i < nTracks; i++)
                {
                    int ci = r.ReadByte();
                    string p = r.ReadString();
                    if (ci >= nCast) continue;      // a track that belonged to nobody stays null
                    var root = newCast[ci].transform;
                    newTracks[i] = p.Length == 0 ? root : root.Find(p);   // a renamed bone stays null and is skipped
                    newRoots[i] = p.Length == 0;
                }

                int nFrames = r.ReadInt32();
                if (nFrames < 2 || nFrames > 1_000_000) { error = "the record is empty"; return false; }
                var newFrames = new List<Frame>(nFrames);
                for (int fi = 0; fi < nFrames; fi++)
                {
                    var f = new Frame
                    {
                        t = r.ReadSingle(),
                        pos = new Vector3[nTracks],
                        rot = new Quaternion[nTracks],
                        hp = new int[nCast],
                        st = new int[nCast],
                    };
                    for (int i = 0; i < nTracks; i++)
                    {
                        f.pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        f.rot[i] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    }
                    f.camPos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    f.camRot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int i = 0; i < nCast; i++)
                    {
                        f.hp[i] = r.ReadInt16();
                        f.st[i] = r.ReadInt16();
                    }
                    newFrames.Add(f);
                }

                var newMarkers = new List<Marker>();
                int nM = r.ReadInt32();
                for (int i = 0; i < nM; i++)
                    newMarkers.Add(new Marker { t = r.ReadSingle(), label = r.ReadString() });

                var newFx = new List<FxEvent>();
                int nFx = r.ReadInt32();
                for (int i = 0; i < nFx; i++)
                {
                    var e = new FxEvent { t = r.ReadSingle(), kind = (Fx)r.ReadByte(), a = r.ReadInt32(), b = r.ReadInt32() };
                    e.point = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    e.dir = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    e.radius = r.ReadSingle(); e.strength = r.ReadSingle();
                    e.color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    newFx.Add(e);
                }

                var newSounds = new List<SoundEvent>();
                int nS = r.ReadInt32();
                for (int i = 0; i < nS; i++)
                {
                    float t = r.ReadSingle();
                    string clipName = r.ReadString();
                    float vol = r.ReadSingle(), pitch = r.ReadSingle();
                    var clip = CrucibleAudio.FindClip(clipName);
                    if (clip != null) newSounds.Add(new SoundEvent { t = t, clip = clip, volume = vol, pitch = pitch });
                }

                // Everything read clean - only now does the live buffer change hands.
                cast = newCast;
                tracks = newTracks;
                isRoot = newRoots;
                frames.Clear(); frames.AddRange(newFrames);
                markers.Clear(); markers.AddRange(newMarkers);
                fxEvents.Clear(); fxEvents.AddRange(newFx);
                soundEvents.Clear(); soundEvents.AddRange(newSounds);
                cam = camera != null ? camera : Camera.main;
                playhead = 0;
            }
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    // ================================================================ audio mixing

    /// <summary>
    /// Builds the cut's stereo 16-bit soundtrack: the fight-music and wind beds looping under
    /// every recorded one-shot, each placed at its exact moment with its recorded volume and
    /// pitch. Returns null when nothing could be mixed (no readable clips).
    /// </summary>
    short[] MixAudio(float from, int totalFrames, int rate)
    {
        if (frames.Count == 0) return null;
        long totalSamples = (long)totalFrames * rate / ExportFps;
        if (totalSamples <= 0 || totalSamples > 100_000_000) return null;   // ~17 min cap, way past MaxSeconds

        var mix = new float[totalSamples * 2];
        float master = Mathf.Clamp01(AudioListener.volume);
        float t0 = frames[0].t + from;   // the absolute record time of the cut's first sample
        var cache = new Dictionary<AudioClip, float[]>();
        bool any = false;

        if (CrucibleAudio.ExportBeds(out var music, out float musicVol, out var wind, out float windVol))
        {
            any |= AddLoop(mix, music, musicVol * master, t0, rate, cache);
            any |= AddLoop(mix, wind, windVol * master, t0, rate, cache);
        }
        foreach (var e in soundEvents)
        {
            if (e.clip == null) continue;
            double start = (e.t - t0) * rate;
            if (start >= totalSamples) continue;
            any |= AddShot(mix, e.clip, e.volume * master, e.pitch, start, rate, cache);
        }
        if (!any) return null;

        var pcm = new short[mix.Length];
        for (long i = 0; i < mix.Length; i++)
            pcm[i] = (short)Mathf.Clamp(mix[i] * 32767f, -32768f, 32767f);
        return pcm;
    }

    /// <summary>The clip's raw samples, or null when they cannot be read (streaming/compressed-in-memory).</summary>
    static float[] ClipData(AudioClip clip, Dictionary<AudioClip, float[]> cache)
    {
        if (clip == null) return null;
        if (cache.TryGetValue(clip, out var cached)) return cached;
        float[] data = null;
        if (clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.samples > 0)
        {
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            if (clip.loadState == AudioDataLoadState.Loaded)
            {
                var d = new float[(long)clip.samples * clip.channels];
                if (clip.GetData(d, 0)) data = d;
            }
        }
        cache[clip] = data;
        return data;
    }

    /// <summary>Mixes a looping bed across the whole buffer, phase-continuous with the record clock.</summary>
    static bool AddLoop(float[] mix, AudioClip clip, float vol, float t0, int rate, Dictionary<AudioClip, float[]> cache)
    {
        var data = ClipData(clip, cache);
        if (data == null || vol <= 0.0001f) return false;
        int ch = clip.channels;
        double step = (double)clip.frequency / rate;
        double src = (t0 % clip.length) * clip.frequency;
        long n = mix.Length / 2;
        for (long o = 0; o < n; o++)
        {
            long s0 = (long)src % clip.samples;
            long s1 = (s0 + 1) % clip.samples;
            float k = (float)(src - Math.Floor(src));
            for (int c = 0; c < 2; c++)
            {
                int sc = ch == 1 ? 0 : Math.Min(c, ch - 1);
                float v = Mathf.Lerp(data[s0 * ch + sc], data[s1 * ch + sc], k);
                mix[o * 2 + c] += v * vol;
            }
            src += step;
        }
        return true;
    }

    /// <summary>Mixes one one-shot at its moment, resampled by its recorded pitch.</summary>
    static bool AddShot(float[] mix, AudioClip clip, float vol, float pitch, double startSample, int rate,
                        Dictionary<AudioClip, float[]> cache)
    {
        var data = ClipData(clip, cache);
        if (data == null || vol <= 0.0001f || pitch <= 0.01f) return false;
        int ch = clip.channels;
        double step = (double)clip.frequency * pitch / rate;
        long outLen = (long)(clip.samples / step);
        long o0 = Math.Max(0, (long)Math.Ceiling(startSample));
        long o1 = Math.Min(mix.Length / 2, (long)Math.Floor(startSample + outLen));
        for (long o = o0; o < o1; o++)
        {
            double src = (o - startSample) * step;
            long s0 = (long)src;
            if (s0 + 1 >= clip.samples) break;
            float k = (float)(src - s0);
            for (int c = 0; c < 2; c++)
            {
                int sc = ch == 1 ? 0 : Math.Min(c, ch - 1);
                float v = Mathf.Lerp(data[s0 * ch + sc], data[(s0 + 1) * ch + sc], k);
                mix[o * 2 + c] += v * vol;
            }
        }
        return o1 > o0;
    }

    /// <summary>
    /// A minimal Motion-JPEG AVI: RIFF header, one video stream whose every chunk is a whole JPEG,
    /// an optional 16-bit stereo PCM audio stream interleaved per frame, and the idx1 index players
    /// expect. Written incrementally; <see cref="Finish"/> patches the sizes and the counts. All
    /// values little-endian, which is what BinaryWriter writes.
    /// </summary>
    class MjpegAviWriter
    {
        public readonly int Width, Height;
        readonly FileStream file;
        readonly BinaryWriter w;
        readonly List<(long offset, int size, bool audio)> index = new List<(long, int, bool)>();
        readonly long riffSizePos, aviTotalFramesPos, streamLengthPos, moviSizePos, moviStart;
        readonly long audioLengthPos;
        readonly bool hasAudio;
        int videoFrames;
        long audioSamples;

        public MjpegAviWriter(string path, int width, int height, int fps, int audioRate = 0)
        {
            Width = width;
            Height = height;
            hasAudio = audioRate > 0;
            file = new FileStream(path, FileMode.Create, FileAccess.Write);
            w = new BinaryWriter(file);

            Four("RIFF"); riffSizePos = file.Position; w.Write(0); Four("AVI ");

            // hdrl: the avih header and the streams. "hdrl"(4) + avih(8+56) + video strl(8+116)
            // = 192; the audio strl (8 + [4 + (8+56) + (8+16)]) adds 100.
            Four("LIST"); w.Write(hasAudio ? 292 : 192); Four("hdrl");

            Four("avih"); w.Write(56);
            w.Write((uint)(1000000 / fps));          // microseconds per frame
            w.Write((uint)(Width * Height * 3 * fps)); // max bytes per second (generous)
            w.Write(0);                              // padding granularity
            w.Write(0x10);                           // AVIF_HASINDEX
            aviTotalFramesPos = file.Position; w.Write(0);
            w.Write(0);                              // initial frames
            w.Write(hasAudio ? 2 : 1);               // streams
            w.Write(Width * Height * 3);             // suggested buffer size
            w.Write(Width); w.Write(Height);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);

            Four("LIST"); w.Write(116); Four("strl");
            Four("strh"); w.Write(56);
            Four("vids"); Four("MJPG");
            w.Write(0);                              // flags
            w.Write(0);                              // priority + language
            w.Write(0);                              // initial frames
            w.Write(1); w.Write(fps);                // scale / rate = fps
            w.Write(0);                              // start
            streamLengthPos = file.Position; w.Write(0);
            w.Write(Width * Height * 3);             // suggested buffer size
            w.Write(-1);                             // default quality
            w.Write(0);                              // sample size
            w.Write((short)0); w.Write((short)0); w.Write((short)Width); w.Write((short)Height);

            Four("strf"); w.Write(40);               // BITMAPINFOHEADER
            w.Write(40); w.Write(Width); w.Write(Height);
            w.Write((short)1); w.Write((short)24);
            Four("MJPG");
            w.Write(Width * Height * 3);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);

            if (hasAudio)
            {
                Four("LIST"); w.Write(92); Four("strl");
                Four("strh"); w.Write(56);
                Four("auds"); w.Write(0);            // handler: none (plain PCM)
                w.Write(0);                          // flags
                w.Write(0);                          // priority + language
                w.Write(0);                          // initial frames
                w.Write(1); w.Write(audioRate);      // scale / rate = samples per second
                w.Write(0);                          // start
                audioLengthPos = file.Position; w.Write(0);   // length, in samples
                w.Write(audioRate * 4);              // suggested buffer size (one second)
                w.Write(-1);                         // quality
                w.Write(4);                          // sample size = block align (2ch x 16 bit)
                w.Write((short)0); w.Write((short)0); w.Write((short)0); w.Write((short)0);

                Four("strf"); w.Write(16);           // WAVEFORMATEX, PCM, no cbSize
                w.Write((short)1);                   // PCM
                w.Write((short)2);                   // stereo
                w.Write(audioRate);
                w.Write(audioRate * 4);              // bytes per second
                w.Write((short)4);                   // block align
                w.Write((short)16);                  // bits per sample
            }

            Four("LIST"); moviSizePos = file.Position; w.Write(0); Four("movi");
            moviStart = file.Position;
        }

        void Four(string s) { for (int i = 0; i < 4; i++) w.Write((byte)s[i]); }

        public void AddFrame(byte[] jpeg)
        {
            index.Add((file.Position, jpeg.Length, false));
            Four("00dc");
            w.Write(jpeg.Length);
            w.Write(jpeg);
            if ((jpeg.Length & 1) == 1) w.Write((byte)0);   // RIFF chunks are word-aligned
            videoFrames++;
        }

        /// <summary>One frame's slice of the premixed stereo track, interleaved after its picture.</summary>
        public void AddAudio(short[] pcm, long startSample, int sampleCount)
        {
            if (!hasAudio || pcm == null || sampleCount <= 0) return;
            int bytes = sampleCount * 4;                     // 2 channels x 16 bit; always even
            index.Add((file.Position, bytes, true));
            Four("01wb");
            w.Write(bytes);
            long i0 = startSample * 2, i1 = (startSample + sampleCount) * 2;
            for (long i = i0; i < i1 && i < pcm.Length; i++) w.Write(pcm[i]);
            audioSamples += sampleCount;
        }

        public void Finish()
        {
            long moviEnd = file.Position;

            Four("idx1"); w.Write(index.Count * 16);
            foreach (var (offset, size, audio) in index)
            {
                Four(audio ? "01wb" : "00dc");
                w.Write(0x10);                               // keyframe (every chunk stands alone)
                w.Write((int)(offset - moviStart + 4));      // offset from the 'movi' fourcc
                w.Write(size);
            }
            long end = file.Position;

            file.Position = riffSizePos; w.Write((int)(end - riffSizePos - 4));
            file.Position = aviTotalFramesPos; w.Write(videoFrames);
            file.Position = streamLengthPos; w.Write(videoFrames);
            if (hasAudio) { file.Position = audioLengthPos; w.Write((int)audioSamples); }
            file.Position = moviSizePos; w.Write((int)(moviEnd - moviSizePos - 4));
            w.Flush();
            file.Close();
        }
    }
}
