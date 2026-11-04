using System.Collections;
using UnityEngine;

/// <summary>
/// What the four sparring rounds share: the Partner on the existing AI with the round's rules, the health floor
/// on both (nobody dies in the dojo), fast training regen in rounds 1-3, the knockdowns, and taking it all down.
/// Every count comes from <see cref="CombatLog"/>.
/// </summary>
public abstract class SparRound : StoryStepLogic
{
    protected ChapterRunner chapter;
    protected ChapterStep step;
    protected Fighter musashi, partner;
    protected TrainingDriver training;
    protected CombatLog log;

    protected abstract AiController.Rules Rules { get; }
    protected virtual AiDifficulty Difficulty => AiDifficulty.Chill;
    protected virtual bool FastRegen => true;
    protected virtual bool ShowPartnerBars => false;   // the user's call: the Partner has bars in round 4 only
    protected virtual float Gap => 4.0f;
    /// <summary>Seconds the Partner holds his attacks after Musashi's stun from a hit ends (training rounds).
    /// Without it his quick thrust lands again before the stun is over and a beginner is stun-locked with no way to
    /// defend - found by the scripted test. 0 = the normal game (round 4).</summary>
    protected virtual float Breather => 0.5f;
    /// <summary>The Partner's attack rhythm in the training rounds (the user: he was far too passive, and only struck
    /// from point blank). x..y seconds between attacks, thrown from up to <see cref="Reach"/>, standing at a distance
    /// re-picked in <see cref="StandRange"/> after each one - so some come from his full reach, some from close.
    /// Zero = the AI's own way (round 4).</summary>
    protected virtual Vector2 AttackRhythm => new Vector2(1.5f, 2.0f);
    protected virtual float Reach => 3.0f;
    protected virtual Vector2 StandRange => new Vector2(1.9f, 2.9f);

    public override IEnumerator Run(ChapterRunner c, ChapterStep s)
    {
        chapter = c; step = s;
        musashi = c.Cast(s.cast[0]);
        partner = c.Cast(s.cast[1]);
        if (musashi == null || partner == null) { Debug.LogError($"Story: {s.id} has no cast"); yield break; }
        training = c.Training;
        c.PlacePair(s, Gap, refill: true);
        c.BeginEncounter(s, ShowPartnerBars);
        training.Begin(musashi, partner, senseiRules: false, fastRegen: FastRegen);
        log = new CombatLog();
        c.Subtitles?.SetGuide(StoryControls.GuideFor(s.kind));
        var demo = new StoryDemo(c, musashi, partner);
        Coroutine breather = null;
        try
        {
            while (true)
            {
                if (HasDemo)
                {
                    // the slow-motion demonstration (the Partner scripted, his AI not yet on), then Ready
                    var im = InputManager.Instance;
                    if (im != null) im.SetAi(partner.PlayerNumber, true);
                    demo.Begin();
                    yield return Demo(demo);
                    yield return demo.ReadyAndReset(step, Gap, training);
                }
                Ai(true);
                breather = Breather > 0f ? c.StartCoroutine(BreatherLoop()) : null;
                log.Clear();
                // the pause menu can ask for the demonstration again mid-round: the round restarts after it
                bool again = false;
                c.CanReplayDemo = HasDemo;
                yield return ChapterRunner.RunUntil(Round(), () => c.DemoReplayRequested, stopped => again = stopped);
                c.CanReplayDemo = false;
                if (!again) break;
                c.ConsumeDemoReplay();
                if (breather != null) { c.StopCoroutine(breather); breather = null; }
                Ai(false);
                yield return demo.BackToMarks(step, Gap, training);
            }
        }
        finally
        {
            c.CanReplayDemo = false;
            c.ConsumeDemoReplay();
            demo.End();
            c.Subtitles?.SetGuide(null);
            if (breather != null) c.StopCoroutine(breather);
            log.Dispose();
            Ai(false);
            training.End();
        }
    }

    protected abstract IEnumerator Round();

    protected virtual bool HasDemo => false;
    protected virtual IEnumerator Demo(StoryDemo d) { yield break; }

    /// <summary>The Partner throws his thrust (his Light) for a demonstration.</summary>
    protected IEnumerator PartnerThrust()
    {
        partner.Stats.currentStamina = partner.Stats.maxStamina;
        partner.Combat.TryAttack(AttackType.Light, false);
        yield return null;
    }

    IEnumerator BreatherLoop()
    {
        float releaseAt = 0f;
        while (true)
        {
            var ai = AiController.Instance;
            if (ai != null && ai.Active)
            {
                if (musashi.Combat.IsInHitstun) releaseAt = Time.time + Breather;
                bool hold = Time.time < releaseAt;
                ai.rules.attack = Rules.attack && !hold;
            }
            yield return null;
        }
    }

    void Ai(bool on)
    {
        var ai = AiController.Instance;
        if (ai == null) { Debug.LogError("Story: no AiController"); return; }
        if (!on) { ai.Deactivate(); return; }
        ai.Activate(Difficulty, partner.PlayerNumber, Rules);
        ai.attackInterval = AttackRhythm;
        ai.attackReach = AttackRhythm.x > 0f ? Reach : 0f;
        ai.standRange = AttackRhythm.x > 0f ? StandRange : Vector2.zero;
    }

    /// <summary>Both WALK back to their marks (no cut - the user's call), refilled, the log emptied, the fight handed back.</summary>
    protected IEnumerator Reset()
    {
        chapter.HoldEncounter();
        yield return chapter.WalkPairBack(step, Gap, refill: true);
        training.ApplyFloors();
        log.Clear();
        chapter.ResumeEncounter();
    }

    /// <summary>Handles a knockdown on either side (the Sensei stops it, the fallen one gets up, both reset).
    /// True if there was one.</summary>
    protected IEnumerator HandleKnockdowns(System.Action onMusashiDown = null)
    {
        if (training.PlayerKnockedDown)
        {
            onMusashiDown?.Invoke();
            yield return chapter.Knockdown(musashi);
            yield return Reset();
        }
        else if (training.OpponentKnockedDown)
        {
            yield return chapter.Knockdown(partner, "ch1.spar.partner_down");
            yield return Reset();
        }
    }

    protected bool AnyDown => training.PlayerKnockedDown || training.OpponentKnockedDown;
    protected bool IsMusashi(Fighter f) => f == musashi;
    protected bool IsPartner(Fighter f) => f == partner;
    protected static bool Lands(HitOutcome o) => o == HitOutcome.Hit || o == HitOutcome.GuardBroken;
}

/// <summary>
/// The shape of rounds 1 and 2: a DEFENCE by Musashi against the Partner's strike (judged by the combat system),
/// then Musashi's next attack, started within <see cref="CounterWindow"/> seconds, must land. Progress is kept
/// after mistakes; a failed defence or a missed counter simply adds nothing.
/// </summary>
public abstract class SparDefendAndCounter : SparRound
{
    /// <summary>From the defence to the counter starting (a dash or parry, then a step in).</summary>
    public const float CounterWindow = 2.0f;

    protected abstract HitOutcome Defence { get; }
    protected abstract string Label { get; }

    protected override AiController.Rules Rules => new AiController.Rules { walk = true, attack = true };

    protected override IEnumerator Round()
    {
        int done = 0;
        float defendedAt = -99f;
        AttackData counter = null;
        chapter.SetObjective(Label, done, step.target);
        while (done < step.target)
        {
            if (chapter.CompleteRequested) yield break;
            if (AnyDown) { yield return HandleKnockdowns(); defendedAt = -99f; counter = null; continue; }
            while (log.Next(out var e))
            {
                if (e.kind == CombatLog.Kind.Hit && IsPartner(e.fighter) && IsMusashi(e.hit.target) && e.hit.outcome == Defence)
                { defendedAt = e.time; counter = null; }
                else if (e.kind == CombatLog.Kind.Start && IsMusashi(e.fighter) && counter == null && e.time - defendedAt <= CounterWindow)
                { counter = e.attack; defendedAt = -99f; }
                else if (e.kind == CombatLog.Kind.Hit && IsMusashi(e.fighter) && e.attack == counter)
                {
                    bool ok = Lands(e.hit.outcome);
                    if (ok) { done++; chapter.SetObjective(Label, done, step.target); }
                    Debug.Log($"Story {step.id}: counter {e.attack.AttackName} {e.hit.outcome} -> {(ok ? "counts" : "no")} ({done}/{step.target})");
                    counter = null;
                }
                else if (e.kind == CombatLog.Kind.End && IsMusashi(e.fighter) && e.attack == counter && !e.touched)
                { Debug.Log($"Story {step.id}: counter {e.attack.AttackName} missed ({done}/{step.target})"); counter = null; }
            }
            yield return null;
        }
    }
}

/// <summary>Round 1: dodge (any direction that genuinely evades) and counter, five times.</summary>
public class SparDodgeCounter : SparDefendAndCounter
{
    protected override HitOutcome Defence => HitOutcome.Evaded;
    protected override string Label => "Dodge & Counter";
    protected override bool HasDemo => true;

    protected override IEnumerator Demo(StoryDemo d)
    {
        d.Caption("He attacks for real now. A <b>thrust</b> comes straight at you:\n<b>sidestep</b> it - a back dash cannot outrun a thrust.");
        yield return d.Read(3.0f);
        yield return d.Approach(3.0f);
        yield return d.Tip(StoryDemo.Window.Dodge);
        chapter.StartCoroutine(PartnerThrust());
        yield return d.UntilMoment(partner, StoryDemo.Window.Dodge, 7f);
        d.Caption("<b>NOW</b> - sidestep.");
        yield return d.Sidestep();
        d.Caption("Step in and <b>counter</b> while he recovers.");
        while (partner.Combat.IsAttacking) yield return null;
        yield return d.Approach(2.3f);
        yield return d.Strike(AttackType.Light);
        d.HideBar();
        d.Caption("Against a <b>cut</b> from the side, a back dash is safest.\nOnly a counter that LANDS counts.");
        yield return d.Read(3.0f);
    }
}

/// <summary>Round 2: parry (the combat system's timed guard raise) and counter, five times.</summary>
public class SparParryCounter : SparDefendAndCounter
{
    protected override HitOutcome Defence => HitOutcome.Parried;
    protected override string Label => "Parry & Counter";
    protected override bool HasDemo => true;

    protected override IEnumerator Demo(StoryDemo d)
    {
        d.Caption("A parry is your guard raised in the <b>last instant</b> before the blade lands -\nthe <color=#F2C64D>gold window</color>. Too early is only a block.");
        yield return d.Read(3.2f);
        yield return d.Approach(3.0f);
        yield return d.Tip(StoryDemo.Window.Parry);
        chapter.StartCoroutine(PartnerThrust());
        yield return d.UntilMoment(partner, StoryDemo.Window.Parry, 11f);
        d.Caption("<b>NOW</b> - raise the guard.");
        yield return d.Press(new InputData { blockHeld = true }, 30);
        d.Caption("<b>Parried</b> - he is stunned. <b>Counter</b> now.");
        yield return d.Approach(2.3f);
        yield return d.Strike(AttackType.Light);
        d.HideBar();
        yield return d.Read(2.4f);
    }
}

/// <summary>
/// Round 3: land ten attacks (clean hits or guard breaks - blocked strikes do not count) while taking fewer than five.
/// The FIFTH hit received fails the round on the spot: the Sensei stops it, both counters reset, round 3 again.
/// A hit Musashi blocks is not a hit received (the default the tracker records as still open).
/// </summary>
public class SparAccuracy : SparRound
{
    public const int MaxHits = 5;
    protected override AiDifficulty Difficulty => AiDifficulty.Moderate;
    protected override AiController.Rules Rules => new AiController.Rules { walk = true, attack = true, block = true };
    protected override Vector2 AttackRhythm => new Vector2(1.5f, 2.2f);

    protected override IEnumerator Round()
    {
        int landed = 0, taken = 0;
        void Show() => chapter.SetObjective("Attacks Landed", landed, step.target, $"Hits Received: {taken} / {MaxHits}");
        Show();
        while (landed < step.target)
        {
            if (chapter.CompleteRequested) yield break;
            if (AnyDown) { yield return HandleKnockdowns(() => { landed = 0; taken = 0; }); Show(); continue; }
            bool failed = false;
            while (!failed && log.Next(out var e))
            {
                if (e.kind != CombatLog.Kind.Hit || !Lands(e.hit.outcome)) continue;
                if (IsMusashi(e.fighter) && IsPartner(e.hit.target)) landed++;
                else if (IsPartner(e.fighter) && IsMusashi(e.hit.target)) { taken++; failed = taken >= MaxHits; }
                Show();
                if (landed >= step.target) break;
            }
            if (failed)
            {
                Debug.Log($"Story {step.id}: {MaxHits} hits received - round failed ({landed}/{step.target} landed)");
                chapter.HoldEncounter();
                yield return chapter.PlayBeat("ch1.knockdown.stop");   // the Sensei stops the sparring
                yield return chapter.Say("ch1.spar.round_failed");
                landed = 0; taken = 0;
                Show();
                yield return Reset();
                continue;
            }
            yield return null;
        }
        Debug.Log($"Story {step.id}: {landed} landed, {taken} received - round won");
    }
}

/// <summary>
/// Round 4: a full duel on the normal rules (existing AI, no training regen, both bars). Nobody dies: if Musashi
/// falls he is knocked down and the round starts again; when the Partner falls the Sensei stops it at once and the
/// step's outro (the Sensei's call, the dismissal) plays over him kneeling.
/// </summary>
public class SparBout : SparRound
{
    protected override AiDifficulty Difficulty => AiDifficulty.Moderate;
    protected override AiController.Rules Rules => AiController.Rules.Full;
    protected override bool FastRegen => false;
    protected override bool ShowPartnerBars => true;
    protected override float Breather => 0f;
    protected override Vector2 AttackRhythm => Vector2.zero;   // a real bout: the AI's own way

    protected override IEnumerator Round()
    {
        chapter.SetObjective(step.objective);
        while (true)
        {
            if (chapter.CompleteRequested) yield break;
            if (training.OpponentKnockedDown)
            {
                Debug.Log($"Story {step.id}: the Partner is down - Musashi wins");
                chapter.HoldEncounter();   // no attack after the deciding one
                yield return chapter.Knockdown(partner, null, getUp: false);
                yield break;
            }
            if (training.PlayerKnockedDown)
            {
                Debug.Log($"Story {step.id}: Musashi is down - round 4 again");
                yield return chapter.Knockdown(musashi);
                yield return Reset();
                continue;
            }
            yield return null;
        }
    }
}
