using System.Collections;
using UnityEngine;

/// <summary>
/// The slow-motion demonstration before a lesson or a training round (the user's call): Musashi is driven through
/// the exercise done RIGHT while a caption explains it, and a timing bar shows the incoming strike's wind-up with
/// the dodge or parry window lit. Then both walk back to their marks and the player presses Ready to begin.
/// Nothing that happens in a demonstration counts toward the objective (the lesson clears its log afterwards).
/// </summary>
public class StoryDemo
{
    /// <summary>Game speed during a demonstration.</summary>
    public const float Slow = 0.35f;

    readonly ChapterRunner chapter;
    readonly Fighter musashi, other;
    readonly InputManager im;
    float oldScale = 1f;
    bool running;
    Coroutine bar;

    public StoryDemo(ChapterRunner chapter, Fighter musashi, Fighter other)
    {
        this.chapter = chapter; this.musashi = musashi; this.other = other;
        im = InputManager.Instance;
    }

    StorySubtitles Subs => chapter.Subtitles;

    public void Begin()
    {
        running = true;
        oldScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = Slow;
        if (im != null) im.SetAi(musashi.PlayerNumber, true);   // the player's hands are off: Musashi is shown
        Debug.Log("Story demo: begins");
    }

    public void Caption(string text) => Subs?.SetDemo(text);

    /// <summary>Takes the demonstration down (safe to call twice).</summary>
    public void End()
    {
        if (!running) return;
        running = false;
        StopBar();
        Subs?.SetDemo(null);
        if (Time.timeScale > 0f) Time.timeScale = oldScale;
        if (im != null) im.SetAi(musashi.PlayerNumber, false);
        Debug.Log("Story demo: ends");
    }

    /// <summary>A pause to read, in real seconds (the game is slowed; the reader is not). Frozen by the pause menu.</summary>
    public IEnumerator Read(float seconds) => chapter.Wait(seconds);

    /// <summary>Holds an input on Musashi for <paramref name="frames"/> frames (the AI slot is read once per frame).</summary>
    public IEnumerator Press(InputData d, int frames = 2)
    {
        for (int i = 0; i < frames; i++) { im?.SetAiInput(musashi.PlayerNumber, d); yield return null; }
        im?.SetAiInput(musashi.PlayerNumber, new InputData());
    }

    public float Toward => musashi.IsFacingRight() ? 1f : -1f;
    public float Gap { get { var d = other.transform.position - musashi.transform.position; d.y = 0f; return d.magnitude; } }

    public IEnumerator DashBack() { yield return Press(new InputData { dashDirection = new Vector2(-Toward, 0f) }); yield return UntilDashDone(); }
    public IEnumerator DashIn() { yield return Press(new InputData { dashDirection = new Vector2(Toward, 0f) }); yield return UntilDashDone(); }
    public IEnumerator Sidestep() { yield return Press(new InputData { dashDirection = new Vector2(0f, Toward) }); yield return UntilDashDone(); }

    IEnumerator UntilDashDone()
    {
        float until = Time.time + 1.5f;
        while (musashi.IsDashing && Time.time < until) yield return null;
    }

    /// <summary>Walks Musashi in until he is within <paramref name="gap"/>.</summary>
    public IEnumerator Approach(float gap)
    {
        float until = Time.time + 1.5f;
        while (Gap > gap && Time.time < until) { im?.SetAiInput(musashi.PlayerNumber, new InputData { moveDirection = Toward }); yield return null; }
        im?.SetAiInput(musashi.PlayerNumber, new InputData());
    }

    /// <summary>Throws an attack and waits for it to finish.</summary>
    public IEnumerator Strike(AttackType t, bool special = false)
    {
        musashi.Combat.TryAttack(t, special);
        float until = Time.time + 3f;
        while (musashi.Combat.IsAttacking && Time.time < until) yield return null;
    }

    /// <summary>Three strikes back to back (each pressed during the one before, so they chain).</summary>
    public IEnumerator Chain(params AttackType[] moves)
    {
        foreach (var t in moves)
        {
            musashi.Combat.TryAttack(t, false);
            // a press mid-swing buffers into the next; timed in GAME seconds - the demonstration is slowed
            float next = Time.time + 25f * BalanceConfig.FRAME_TIME;
            while (musashi.Combat.IsAttacking && Time.time < next) yield return null;
            if (!musashi.Combat.IsAttacking) { float gapEnd = Time.time + 2f * BalanceConfig.FRAME_TIME; while (Time.time < gapEnd) yield return null; }
        }
        float until = Time.time + 3f;
        while (musashi.Combat.IsAttacking && Time.time < until) yield return null;
    }

    // ------------------------------------------------------------------ the incoming strike and its window

    public enum Window { Dodge, Parry }

    /// <summary>When the blade lands, seconds into the attacker's swing (the hit is judged a couple of frames before
    /// the authored startup ends).</summary>
    public static float StrikeAt(AttackData a) => Mathf.Max(1, a.StartupFrames - 2) * BalanceConfig.FRAME_TIME;

    /// <summary>The window, seconds into the swing: a dash whose untouchable head covers the landing, or a guard raised
    /// inside the parry window before it.</summary>
    public (float from, float to) WindowFor(AttackData a, Window w)
    {
        float strike = StrikeAt(a);
        if (w == Window.Parry) return (Mathf.Max(0f, strike - BalanceConfig.PARRY_WINDOW), strike);
        return (Mathf.Max(0f, strike - musashi.DashIFrames + 2f * BalanceConfig.FRAME_TIME), strike - BalanceConfig.FRAME_TIME);
    }

    /// <summary>Waits for <paramref name="attacker"/>'s swing to start, runs the bar over it, and returns when the
    /// swing reaches <paramref name="pressAt"/> seconds (the demonstration's chosen moment, inside the window).</summary>
    public IEnumerator UntilMoment(Fighter attacker, Window w, float framesBeforeStartupEnds)
    {
        float until = Time.time + 4f;
        while (!attacker.Combat.IsAttacking && Time.time < until) yield return null;
        var a = attacker.Combat.CurrentAttack;
        if (a == null) yield break;
        StopBar();
        bar = chapter.StartCoroutine(BarLoop(attacker, a, w));
        float pressAt = (a.StartupFrames - framesBeforeStartupEnds) * BalanceConfig.FRAME_TIME;
        while (attacker.Combat.IsAttacking && attacker.Combat.AttackTime < pressAt) yield return null;
    }

    IEnumerator BarLoop(Fighter attacker, AttackData a, Window w)
    {
        var (from, to) = WindowFor(a, w);
        float strike = StrikeAt(a);
        float total = (a.StartupFrames + a.ActiveFrames) * BalanceConfig.FRAME_TIME;
        string label = w == Window.Dodge ? "DODGE" : "PARRY";
        Color c = w == Window.Dodge ? new Color(0.35f, 0.75f, 0.95f, 0.9f) : new Color(0.95f, 0.78f, 0.30f, 0.9f);
        float now = 0f;
        while (attacker.Combat.IsAttacking && attacker.Combat.CurrentAttack == a)
        {
            now = attacker.Combat.AttackTime;
            Subs?.SetDemoBar(total, from, to, strike, now, label, c);
            yield return null;
        }
        Subs?.SetDemoBar(total, from, to, strike, total, label, c);   // the bar stays up, full, for the caption after
        bar = null;
    }

    void StopBar()
    {
        if (bar != null) chapter.StopCoroutine(bar);
        bar = null;
    }

    public void HideBar() { StopBar(); Subs?.SetDemoBar(0f, 0f, 0f, 0f, 0f, "", Color.clear); }

    // ------------------------------------------------------------------ after it

    /// <summary>The demonstration asked for again from the pause menu: whatever the exercise left behind (a line, a
    /// knockdown, a hold) is cleared, and both walk back to their marks, refilled, ready for <see cref="Begin"/>.</summary>
    public IEnumerator BackToMarks(ChapterStep step, float gap, TrainingDriver training)
    {
        Subs?.Clear();
        HideBar();
        chapter.HoldEncounter();
        yield return chapter.WalkPairBack(step, gap, refill: true);
        if (training != null) training.ApplyFloors();
        chapter.ResumeEncounter();
        Debug.Log("Story demo: replaying");
    }

    /// <summary>The tip shown while the incoming strike winds up (the user's call): watch the WEAPON.</summary>
    public static string WatchTheWeapon(Window w) => w == Window.Parry
        ? "<b>Tip:</b> watch his <b>weapon</b>, not his body.\nRaise the guard as the blade arrives - not when he starts to move."
        : "<b>Tip:</b> watch his <b>weapon</b>, not his body.\nDash as the blade starts to come at you - not when he starts to move.";

    /// <summary>Puts the tip up and gives it time to be read before the strike is thrown.</summary>
    public IEnumerator Tip(Window w)
    {
        Caption(WatchTheWeapon(w));
        yield return Read(2.4f);
    }

    /// <summary>Ends the demonstration, walks both back to their marks (refilled), and holds there until the player
    /// presses Ready (or <see cref="ChapterRunner.PressReady"/>). The fight is handed back after.</summary>
    public IEnumerator ReadyAndReset(ChapterStep step, float gap, TrainingDriver training)
    {
        End();
        chapter.HoldEncounter();
        yield return chapter.WalkPairBack(step, gap, refill: true);
        if (training != null) training.ApplyFloors();
        yield return chapter.WaitReady();
        chapter.ResumeEncounter();
    }
}
