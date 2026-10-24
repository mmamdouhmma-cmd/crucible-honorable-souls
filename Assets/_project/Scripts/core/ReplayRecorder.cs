using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
/// visit to the record screen. Hit effects and lights are not recorded - the replay is the motion.
///
/// Export writes a Motion-JPEG .avi (each frame a whole JPEG, no external encoder, plays in VLC
/// and Windows) to the user's Videos folder, reading frames straight off the screen - so whoever
/// starts an export must hide any UI that should not be baked into the video. No sound.
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
    }

    struct Marker
    {
        public float t;             // on the same clock as Frame.t
        public string label;
    }

    readonly List<Frame> frames = new List<Frame>();
    readonly List<Marker> markers = new List<Marker>();
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

        playhead = 0;
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
    {
        if (Exporting || !PlaybackActive) return;
        StartCoroutine(ExportRoutine(from, to));
    }

    IEnumerator ExportRoutine(float from, float to)
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

        MjpegAviWriter writer = null;
        int total = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.1f, to - from) * ExportFps));
        for (int i = 0; i < total; i++)
        {
            Apply(from + i / (float)ExportFps);
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                if (writer == null) writer = new MjpegAviWriter(path, shot.width, shot.height, ExportFps);
                // A window resize mid-export changes the frame size, which an .avi cannot hold.
                if (shot.width == writer.Width && shot.height == writer.Height)
                    writer.AddFrame(shot.EncodeToJPG(88));
            }
            catch (Exception e)
            {
                ExportError = e.Message;
            }
            finally
            {
                if (shot != null) Destroy(shot);
            }
            if (ExportError != null) break;
            ExportProgress = (i + 1) / (float)total;
        }

        try { writer?.Finish(); }
        catch (Exception e) { if (ExportError == null) ExportError = e.Message; }

        if (ExportError == null) ExportResult = path;
        Exporting = false;
    }

    /// <summary>
    /// A minimal Motion-JPEG AVI: RIFF header, one video stream whose every chunk is a whole JPEG,
    /// and the idx1 index players expect. Written incrementally; <see cref="Finish"/> patches the
    /// sizes and the frame counts. All values little-endian, which is what BinaryWriter writes.
    /// </summary>
    class MjpegAviWriter
    {
        public readonly int Width, Height;
        readonly FileStream file;
        readonly BinaryWriter w;
        readonly List<(long offset, int size)> index = new List<(long, int)>();
        readonly long riffSizePos, aviTotalFramesPos, streamLengthPos, moviSizePos, moviStart;

        public MjpegAviWriter(string path, int width, int height, int fps)
        {
            Width = width;
            Height = height;
            file = new FileStream(path, FileMode.Create, FileAccess.Write);
            w = new BinaryWriter(file);

            Four("RIFF"); riffSizePos = file.Position; w.Write(0); Four("AVI ");

            // hdrl: the avih header and one stream (strh + strf). 192 = "hdrl" + (8+56) + (8+116).
            Four("LIST"); w.Write(192); Four("hdrl");

            Four("avih"); w.Write(56);
            w.Write((uint)(1000000 / fps));          // microseconds per frame
            w.Write((uint)(Width * Height * 3 * fps)); // max bytes per second (generous)
            w.Write(0);                              // padding granularity
            w.Write(0x10);                           // AVIF_HASINDEX
            aviTotalFramesPos = file.Position; w.Write(0);
            w.Write(0);                              // initial frames
            w.Write(1);                              // one stream
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

            Four("LIST"); moviSizePos = file.Position; w.Write(0); Four("movi");
            moviStart = file.Position;
        }

        void Four(string s) { for (int i = 0; i < 4; i++) w.Write((byte)s[i]); }

        public void AddFrame(byte[] jpeg)
        {
            index.Add((file.Position, jpeg.Length));
            Four("00dc");
            w.Write(jpeg.Length);
            w.Write(jpeg);
            if ((jpeg.Length & 1) == 1) w.Write((byte)0);   // RIFF chunks are word-aligned
        }

        public void Finish()
        {
            long moviEnd = file.Position;

            Four("idx1"); w.Write(index.Count * 16);
            foreach (var (offset, size) in index)
            {
                Four("00dc");
                w.Write(0x10);                               // keyframe (every JPEG is one)
                w.Write((int)(offset - moviStart + 4));      // offset from the 'movi' fourcc
                w.Write(size);
            }
            long end = file.Position;

            file.Position = riffSizePos; w.Write((int)(end - riffSizePos - 4));
            file.Position = aviTotalFramesPos; w.Write(index.Count);
            file.Position = streamLengthPos; w.Write(index.Count);
            file.Position = moviSizePos; w.Write((int)(moviEnd - moviSizePos - 4));
            w.Flush();
            file.Close();
        }
    }
}
