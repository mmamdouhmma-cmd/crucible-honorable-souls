using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records the combat events of one exchange, in order, for a lesson to read. Every judgment a lesson makes
/// comes from here - a resolved hit, a block, an evade, an attack starting or ending - never from a button
/// press or an animator state.
/// </summary>
public class CombatLog : IDisposable
{
    public enum Kind { Start, End, Hit }

    public struct Entry
    {
        public Kind kind;
        public float time;
        public Fighter fighter;     // Start / End: the attacker
        public AttackData attack;
        public int depth;           // Start: string depth
        public bool touched;        // End: hit, blocked or parried
        public HitReport hit;       // Hit
    }

    readonly List<Entry> entries = new List<Entry>();
    int read;

    public CombatLog()
    {
        CombatEvents.AttackStarted += OnStart;
        CombatEvents.AttackEnded += OnEnd;
        CombatEvents.HitResolved += OnHit;
    }

    public void Dispose()
    {
        CombatEvents.AttackStarted -= OnStart;
        CombatEvents.AttackEnded -= OnEnd;
        CombatEvents.HitResolved -= OnHit;
    }

    void OnStart(Fighter f, AttackData a, int d) => entries.Add(new Entry { kind = Kind.Start, time = Time.time, fighter = f, attack = a, depth = d });
    void OnEnd(Fighter f, AttackData a, bool t) => entries.Add(new Entry { kind = Kind.End, time = Time.time, fighter = f, attack = a, touched = t });
    void OnHit(HitReport r) => entries.Add(new Entry { kind = Kind.Hit, time = Time.time, fighter = r.attacker, attack = r.attack, hit = r });

    /// <summary>The next unread event, if any.</summary>
    public bool Next(out Entry e)
    {
        if (read < entries.Count) { e = entries[read++]; return true; }
        e = default;
        return false;
    }

    public void Clear() { entries.Clear(); read = 0; }
}

/// <summary>What the three Sensei lessons share: setting up the dojo exercise, resetting between exchanges,
/// the knockdown, and taking it all down again.</summary>
public abstract class SenseiLesson : StoryStepLogic
{
    protected ChapterRunner chapter;
    protected ChapterStep step;
    protected Fighter musashi, sensei;
    protected TrainingDriver training;
    protected CombatLog log;

    /// <summary>The lesson's opening distance when the step gives none.</summary>
    protected virtual float DefaultGap => 3.2f;
    float Gap => step.gap > 0f ? step.gap : DefaultGap;

    public override IEnumerator Run(ChapterRunner c, ChapterStep s)
    {
        chapter = c; step = s;
        musashi = c.Cast(s.cast[0]);
        sensei = c.Cast(s.cast[1]);
        if (musashi == null || sensei == null) { Debug.LogError($"Story: {s.id} has no cast"); yield break; }
        training = c.Training;
        c.PlacePair(s, Gap, refill: true);
        c.BeginEncounter(s, showOpponentBars: false);   // the Sensei has no bars (the user's call)
        training.Begin(musashi, sensei);
        log = new CombatLog();
        c.Subtitles?.SetGuide(StoryControls.GuideFor(s.kind));
        var demo = new StoryDemo(c, musashi, sensei);
        try
        {
            // the slow-motion demonstration, then Ready (the user's call); nothing in it counts. The pause menu can
            // ask for it again mid-lesson: the exercise is dropped, both walk back, and the demonstration replays.
            while (true)
            {
                demo.Begin();
                yield return Demo(demo);
                yield return demo.ReadyAndReset(step, Gap, training);
                sensei.Stats.currentStamina = sensei.Stats.maxStamina;
                log.Clear();
                bool again = false;
                c.CanReplayDemo = true;
                yield return ChapterRunner.RunUntil(Lesson(), () => c.DemoReplayRequested, stopped => again = stopped);
                c.CanReplayDemo = false;
                if (!again) break;
                c.ConsumeDemoReplay();
                training.CancelAttack();
                yield return demo.BackToMarks(step, Gap, training);
                log.Clear();
            }
        }
        finally
        {
            c.CanReplayDemo = false;
            c.ConsumeDemoReplay();
            demo.End();
            c.Subtitles?.SetGuide(null);
            log.Dispose();
            training.End();
        }
    }

    protected abstract IEnumerator Lesson();

    /// <summary>The lesson done right, in slow motion, with captions.</summary>
    protected abstract IEnumerator Demo(StoryDemo d);

    protected void Objective(int progress) => chapter.SetObjective(step.objective, progress, step.target);

    /// <summary>Waits until both have finished swinging and dashing (or the time runs out).</summary>
    protected IEnumerator Settle(float maxSeconds = 2.5f)
    {
        float until = Time.time + maxSeconds, quietSince = Time.time;
        while (Time.time < until)
        {
            bool busy = musashi.Combat.IsAttacking || sensei.Combat.IsAttacking || musashi.IsDashing || training.SenseiBusy;
            if (busy) quietSince = Time.time;
            else if (Time.time - quietSince > 0.3f) yield break;
            yield return null;
        }
    }

    /// <summary>Both WALK back to their marks - no cut (the user's call). Health and stamina kept, the Sensei's
    /// stamina refilled.</summary>
    protected IEnumerator ResetExchange()
    {
        chapter.HoldEncounter();
        yield return chapter.WalkPairBack(step, Gap, refill: false);
        training.ApplyFloors();
        sensei.Stats.currentStamina = sensei.Stats.maxStamina;   // C3 (a): his guard is honest, so refill it each exchange
        log.Clear();
        chapter.ResumeEncounter();
    }

    /// <summary>If Musashi has been brought to the floor, runs the knockdown and the reset. True if it did.</summary>
    protected bool KnockedDown => training.PlayerKnockedDown;

    protected IEnumerator KnockdownAndReset()
    {
        yield return chapter.Knockdown(musashi);
        yield return ResetExchange();
    }

    protected bool IsMusashi(Fighter f) => f == musashi;
    protected bool IsSensei(Fighter f) => f == sensei;
    protected static bool GuardContact(HitOutcome o) => o == HitOutcome.Blocked || o == HitOutcome.GuardBroken;
    protected static bool IsPlainLight(AttackData a) => a != null && a.AttackType == AttackType.Light && !a.IsSpecial;
    protected static bool IsSpecialLight(AttackData a) => a != null && a.IsSpecial && a.SpecialButton == AttackType.Light;
}

/// <summary>
/// Lesson 1: three Light Attacks on the Sensei's guard. The first strike of each attempt that meets his guard is
/// judged: a plain Light counts (progress is never taken away); anything else is blocked just the same and earns a
/// reprimand. Then both reset to their marks.
/// </summary>
public class LessonLight : SenseiLesson
{
    protected override IEnumerator Demo(StoryDemo d)
    {
        d.Caption($"Strike his guard with a <b>Light Attack</b> ({StoryControls.Button(GameAction.Light)}).\nHe will block it - that is the lesson.");
        yield return d.Read(2.2f);
        yield return d.Strike(AttackType.Light);
        d.Caption("Quick, and it leaves you safe.\n<b>Only Light Attacks count</b> - three of them.");
        yield return d.Read(2.6f);
    }

    protected override IEnumerator Lesson()
    {
        int done = 0;
        Objective(done);
        while (done < step.target)
        {
            if (chapter.CompleteRequested) yield break;
            if (!log.Next(out var e)) { yield return null; continue; }
            if (e.kind != CombatLog.Kind.Hit || !IsMusashi(e.fighter) || !IsSensei(e.hit.target)) continue;

            bool ok = IsPlainLight(e.attack) && e.hit.outcome == HitOutcome.Blocked && e.hit.stringDepth == 0;
            if (ok) { done++; Objective(done); if (done < step.target) chapter.Bark("ch1.good"); }
            else if (GuardContact(e.hit.outcome) || e.hit.outcome == HitOutcome.Hit) chapter.Bark("ch1.lesson1.wrong");
            Debug.Log($"Story lesson 1: {e.attack.AttackName} {e.hit.outcome} -> {(ok ? "counts" : "does not count")} ({done}/{step.target})");
            yield return Settle();
            if (done < step.target) yield return ResetExchange();
        }
    }
}

/// <summary>
/// Lesson 2: three CONSECUTIVE exchanges. The Sensei throws his overhead (Mountain Splitter); Musashi must dodge
/// STRAIGHT BACK (the combat system's own evade judgment), then counter with a Special Light that meets the guard
/// within <see cref="CounterWindow"/> seconds of his dash ending. A sideways dodge also evades an overhead, so the
/// Sensei follows it with a real strike toward that side (no tracking, no forced hit) and the streak resets;
/// a hit, a block, a wrong counter, a whiff or hesitation reset it too.
/// </summary>
public class LessonDodgeCounter : SenseiLesson
{
    /// <summary>Counted from the end of the dodge. The user's "~1 s" (decision 2) became 1.5 on 2026-11-04 (the user
    /// allowed more convenient lessons): a back dash leaves Musashi ~4 away, out of his Special Light's reach, so the
    /// exchange is "dodge back, step in, counter", and the step needs the time.</summary>
    public const float CounterWindow = 1.5f;
    /// <summary>2.4 (was 1.8): "a little further" (the user, 2026-11-04). The counter is a dash forward and the Special
    /// Light (the user: back dash, then forward dash + attack, is the intended shape).</summary>
    protected override float DefaultGap => 2.4f;

    protected override IEnumerator Demo(StoryDemo d)
    {
        d.Caption("He will strike with his <b>overhead</b>.\nThe <color=#7FCBF2>blue window</color> on the bar is when a dash makes you untouchable.");
        yield return d.Read(2.8f);
        yield return d.Tip(StoryDemo.Window.Dodge);
        chapter.StartCoroutine(training.SenseiAttack(AttackType.Heavy, false));
        yield return d.UntilMoment(sensei, StoryDemo.Window.Dodge, 8f);
        d.Caption("<b>NOW</b> - dash <b>BACK</b>.");
        yield return d.DashBack();
        d.Caption("Then dash <b>IN</b> at once,\nand counter with a <b>Special Light</b> on his guard.");
        while (training.SenseiBusy) yield return null;
        yield return d.DashIn();
        if (d.Gap > 2.6f) yield return d.Approach(2.6f);
        yield return d.Strike(AttackType.Light, special: true);
        d.HideBar();
        d.Caption("Three exchanges <b>in a row</b>.\nA sideways dodge, getting hit, or a slow or wrong counter starts the count again.");
        yield return d.Read(3.2f);
    }

    protected override IEnumerator Lesson()
    {
        int streak = 0;
        Objective(streak);
        while (streak < step.target)
        {
            if (chapter.CompleteRequested) yield break;
            yield return chapter.Wait(0.9f);
            log.Clear();
            chapter.StartCoroutine(training.SenseiAttack(AttackType.Heavy, false));

            // --- the overhead: what became of it
            string dodge = null; bool struck = false, ended = false;
            while (!ended && !KnockedDown && !training.AttackGaveUp)
            {
                while (log.Next(out var e))
                {
                    if (e.kind == CombatLog.Kind.Hit && IsSensei(e.fighter) && IsMusashi(e.hit.target))
                    {
                        if (e.hit.outcome == HitOutcome.Evaded) { if (dodge == null) dodge = e.hit.dodgeDirection ?? ""; }
                        else struck = true;   // hit, blocked or parried: he did not dodge it
                        ended = true;          // decided - stop here so the counter's events stay unread
                        break;
                    }
                    if (e.kind == CombatLog.Kind.End && IsSensei(e.fighter)) { ended = true; break; }
                }
                yield return null;
            }

            if (!ended && training.AttackGaveUp) { yield return ResetExchange(); continue; }   // his strike never came out
            bool success = false;
            string why;
            if (KnockedDown) { yield return KnockdownAndReset(); streak = 0; Objective(streak); continue; }
            if (struck) why = "did not dodge";
            else if (dodge == null) why = "dodged too early, or not at all";
            else if (dodge != "back")
            {
                why = $"dodged {dodge}";
                // A real strike toward the side he chose. The Sensei's only horizontal cut, Temple Bell, starts on
                // his left and arrives on Musashi's RIGHT; a dodge to the left is answered by his quick overhead.
                bool right = dodge.Contains("right");
                yield return Settle(1.5f);
                if (!KnockedDown) yield return training.SenseiAttack(right ? AttackType.Medium : AttackType.Light, false);
            }
            else
            {
                // --- the counter
                while (musashi.IsDashing) yield return null;
                float deadline = Time.time + CounterWindow;
                AttackData counter = null; why = "hesitated";
                bool decided = false;
                while (!decided && !KnockedDown)
                {
                    while (!decided && log.Next(out var e))
                    {
                        if (!IsMusashi(e.fighter)) continue;
                        if (e.kind == CombatLog.Kind.Start && counter == null)
                        {
                            if (!IsSpecialLight(e.attack)) { why = $"countered with {e.attack.AttackName}"; decided = true; }
                            else counter = e.attack;
                        }
                        else if (e.kind == CombatLog.Kind.Hit && e.attack == counter && IsSensei(e.hit.target))
                        {
                            success = GuardContact(e.hit.outcome);
                            why = success ? "clean" : $"counter {e.hit.outcome}";
                            decided = true;
                        }
                        else if (e.kind == CombatLog.Kind.End && e.attack == counter && !e.touched) { why = "counter missed"; decided = true; }
                    }
                    if (!decided && counter == null && Time.time > deadline) decided = true;   // why = hesitated
                    if (!decided) yield return null;
                }
            }

            if (KnockedDown) { yield return KnockdownAndReset(); streak = 0; Objective(streak); continue; }
            streak = success ? streak + 1 : 0;
            Objective(streak);
            Debug.Log($"Story lesson 2: {(success ? "exchange done" : "streak broken: " + why)} ({streak}/{step.target})");
            yield return Settle();
            if (success) { if (streak < step.target) chapter.Bark("ch1.good"); }
            else chapter.Bark("ch1.lesson2.wrong");
            if (streak < step.target) yield return ResetExchange();
        }
    }
}

/// <summary>
/// Lesson 3: three times, evade the Sensei's spin (Circle of Willow) by ANY dash that genuinely evades it, then put
/// three strikes in one chain on his guard (Light, Special Light, Medium, Heavy; a combo string or strikes thrown
/// back to back). His guard breaks on the 3rd strike of a string and that strike lands - it still counts.
/// Progress is kept after mistakes.
/// </summary>
public class LessonSpin : SenseiLesson
{
    public const float FirstStrikeWindow = 1.5f;   // after the dodge ends: time to step back in
    public const float ChainGap = 1.0f;            // between one strike ending and the next starting
    protected override float DefaultGap => 1.8f;   // a back dash adds ~1.7 and the counter lunges 0.9: it must still reach

    protected override IEnumerator Demo(StoryDemo d)
    {
        d.Caption("His <b>spinning attack</b>. Any dash that clears it will do -\nthe <color=#7FCBF2>blue window</color> is when to go.");
        yield return d.Read(2.8f);
        yield return d.Tip(StoryDemo.Window.Dodge);
        chapter.StartCoroutine(training.SenseiAttack(AttackType.Medium, true));
        yield return d.UntilMoment(sensei, StoryDemo.Window.Dodge, 8f);
        d.Caption("<b>NOW</b> - dodge.");
        yield return d.DashBack();
        d.Caption("<b>Dash IN</b> straight away - it is the fastest way back to him.");
        while (training.SenseiBusy) yield return null;
        yield return d.DashIn();
        if (d.Gap > 2.4f) yield return d.Approach(2.4f);
        d.Caption("Then <b>three strikes</b> on his guard, back to back.\nThe third breaks it.");
        yield return d.Chain(AttackType.Light, AttackType.Medium, AttackType.Heavy);
        d.HideBar();
        yield return d.Read(2.4f);
    }

    protected override IEnumerator Lesson()
    {
        int done = 0;
        Objective(done);
        while (done < step.target)
        {
            if (chapter.CompleteRequested) yield break;
            yield return chapter.Wait(0.9f);
            log.Clear();
            chapter.StartCoroutine(training.SenseiAttack(AttackType.Medium, true));

            bool evaded = false, struck = false, ended = false;
            while (!ended && !KnockedDown && !training.AttackGaveUp)
            {
                while (log.Next(out var e))
                {
                    if (e.kind == CombatLog.Kind.Hit && IsSensei(e.fighter) && IsMusashi(e.hit.target))
                    {
                        if (e.hit.outcome == HitOutcome.Evaded) evaded = true; else struck = true;
                        ended = true;
                        break;
                    }
                    if (e.kind == CombatLog.Kind.End && IsSensei(e.fighter)) { ended = true; break; }
                }
                yield return null;
            }

            if (!ended && training.AttackGaveUp) { yield return ResetExchange(); continue; }
            bool success = false;
            string why = struck ? "did not evade the spin" : !evaded ? "dodged too early, or not at all" : "";
            if (!KnockedDown && evaded && !struck)
            {
                while (musashi.IsDashing) yield return null;
                float lastEnd = Time.time, window = FirstStrikeWindow;
                int touches = 0; bool swinging = false, decided = false;
                while (!decided && !KnockedDown)
                {
                    while (!decided && log.Next(out var e))
                    {
                        if (!IsMusashi(e.fighter)) continue;
                        if (e.kind == CombatLog.Kind.Start) swinging = true;
                        else if (e.kind == CombatLog.Kind.Hit && IsSensei(e.hit.target))
                        {
                            if (GuardContact(e.hit.outcome) || e.hit.outcome == HitOutcome.Hit) touches++;
                            if (touches >= 3) { success = true; why = "clean"; decided = true; }
                        }
                        else if (e.kind == CombatLog.Kind.End)
                        {
                            swinging = false; lastEnd = Time.time; window = ChainGap;
                            if (!e.touched) { why = $"{e.attack.AttackName} missed ({touches} of 3)"; decided = true; }
                        }
                    }
                    if (!decided && !swinging && !musashi.Combat.IsAttacking && Time.time - lastEnd > window)
                    { why = touches == 0 ? "no counter" : $"the chain stopped at {touches}"; decided = true; }
                    if (!decided) yield return null;
                }
            }

            if (KnockedDown) { yield return KnockdownAndReset(); continue; }
            if (success) done++;
            Objective(done);
            Debug.Log($"Story lesson 3: {(success ? "exchange done" : "no progress: " + why)} ({done}/{step.target})");
            yield return Settle();
            if (success) { if (done < step.target) chapter.Bark("ch1.good"); }
            else chapter.Bark("ch1.lesson2.wrong");
            if (done < step.target) yield return ResetExchange();
        }
    }
}
