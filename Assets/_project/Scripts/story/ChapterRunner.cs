using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One step of a chapter (a lesson, a sparring round, a walk, a boss): data from the chapter's steps JSON.</summary>
[Serializable]
public class ChapterStep
{
    public string id;
    public string kind;
    public string title;
    public string[] cast;       // [Player 1, Player 2] by character name
    public string[] spots;      // their marks, children of the chapter's spot root
    public string[] intro;      // beats before the step
    public string[] outro;      // beats after it completes
    public string objective;
    public int target;
    public float gap;           // > 0: the pair start this far apart on the line between their spots
    public string[] puppets;    // background cast, "Name:Spot:mode" (mode practice / idle); none = clear them
    public string arena;        // fight space (ArenaDirectory root name) to switch to before the step; empty = keep
    public string ai;           // opponent AI difficulty for steps that use one: Chill / Moderate / Insane
    public string[] guides;     // explore: who leads the way, "Name:Spot" (the spot they end on); led in order
}

/// <summary>
/// What a step does. One class per kind, registered in <see cref="StoryStepKinds"/>; the runner never knows
/// what a lesson is. <see cref="Run"/> owns the step from its intro beats (already played) to completion -
/// a failed exercise restarts INSIDE Run, so the runner only ever sees a step finish.
/// </summary>
public abstract class StoryStepLogic
{
    public abstract IEnumerator Run(ChapterRunner chapter, ChapterStep step);
}

/// <summary>Step kind name -> logic. Later phases add their kinds here; an unknown kind runs as a placeholder.</summary>
public static class StoryStepKinds
{
    static readonly Dictionary<string, Func<StoryStepLogic>> kinds = new Dictionary<string, Func<StoryStepLogic>>
    {
        { "beats", () => new BeatsOnlyStep() },
        { "lesson_light", () => new LessonLight() },
        { "lesson_dodge_counter", () => new LessonDodgeCounter() },
        { "lesson_spin", () => new LessonSpin() },
        { "spar_dodge", () => new SparDodgeCounter() },
        { "spar_parry", () => new SparParryCounter() },
        { "spar_clean", () => new SparAccuracy() },
        { "spar_bout", () => new SparBout() },
        { "explore", () => new VillageWalk() },
        { "boss", () => new BossDuel() },
    };

    public static void Register(string kind, Func<StoryStepLogic> make) { kinds[kind] = make; }
    public static bool IsBuilt(string kind) => kind != null && kinds.ContainsKey(kind);
    public static StoryStepLogic Make(string kind) => IsBuilt(kind) ? kinds[kind]() : new PlaceholderStep();
}

/// <summary>A step that is only its beats (the opening).</summary>
public class BeatsOnlyStep : StoryStepLogic
{
    public override IEnumerator Run(ChapterRunner chapter, ChapterStep step) { yield break; }
}

/// <summary>Stands in for a step whose logic is not built yet: the pair fight freely under the objective, and
/// the step completes when <see cref="ChapterRunner.CompleteCurrentStep"/> is called (F9 in the Editor).</summary>
public class PlaceholderStep : StoryStepLogic
{
    public override IEnumerator Run(ChapterRunner chapter, ChapterStep step)
    {
        chapter.BeginEncounter(step, showOpponentBars: true);
        chapter.SetObjective((step.objective ?? step.title) + "   [not built yet - F9 completes it]");
        while (!chapter.CompleteRequested) yield return null;
        chapter.EndEncounter();
    }
}

/// <summary>
/// Runs a story chapter: its steps in order from the first one not done, their beats, checkpoints, and the
/// way back to the main menu. Data in two JSON tables (steps, dialogue); each step kind's logic lives in its
/// own class. Lives in the chapter's scene on the "Story" object with <see cref="StorySubtitles"/>.
/// </summary>
public class ChapterRunner : MonoBehaviour
{
    [SerializeField] TextAsset stepsJson;
    [SerializeField] TextAsset linesJson;
    [Tooltip("Parent of the named marks the steps stand the cast on (Spot_MusashiStart, ...).")]
    [SerializeField] Transform spotRoot;
    [SerializeField] StorySubtitles subtitles;
    [Tooltip("Chapter number this one unlocks when it is completed.")]
    [SerializeField] int unlocksChapter = 2;
    [SerializeField] bool debugLogs = true;
    [Tooltip("Cast member whose damage is scaled for the whole chapter (young Musashi - the user's call), and the scale.")]
    [SerializeField] string scaledFighter = "Musashi";
    [SerializeField] float scaledDamage = 0.7f;

    [Serializable] class StepTable { public string chapter; public string title; public ChapterStep[] steps; }

    StepTable table;
    GameMenu menu;
    Coroutine running;

    public StoryLines Lines { get; private set; }
    public StoryProgress Progress { get; private set; }
    public StorySubtitles Subtitles => subtitles;
    public ChapterStep CurrentStep { get; private set; }
    public bool CompleteRequested { get; private set; }
    public bool InEncounter { get; private set; }
    public bool PlayingBeat { get; private set; }
    public string ChapterId => table?.chapter;
    public IReadOnlyList<ChapterStep> Steps => table?.steps;

    /// <summary>Fired when a step starts (after its checkpoint is written) and when it completes.</summary>
    public static event Action<ChapterStep> StepStarted, StepCompleted;
    /// <summary>Fired once the chapter's last step is done and the chapter is saved complete.</summary>
    public static event Action<string> ChapterCompleted;

    void Awake()
    {
        table = stepsJson != null ? JsonUtility.FromJson<StepTable>(stepsJson.text) : null;
        if (table == null || table.steps == null || table.steps.Length == 0) Debug.LogError("Story: no chapter steps");
        Lines = StoryLines.Parse(linesJson);
        if (subtitles != null) subtitles.Lines = Lines;
        GameMenu.StoryLeftToMenu += OnLeftToMenu;
    }

    void OnDestroy() => GameMenu.StoryLeftToMenu -= OnLeftToMenu;

    IEnumerator Start()
    {
        menu = FindAnyObjectByType<GameMenu>();
        yield return null;   // the menu and the HUD build in their own Start
        if (table == null || table.steps == null) yield break;
        if (StoryProgress.StartFreshRequested) { StoryProgress.Erase(table.chapter); StoryProgress.StartFreshRequested = false; }
        var young = Cast(scaledFighter);
        if (young != null) young.ExtraDamageScale = scaledDamage;
        Progress = StoryProgress.Load(table.chapter);
        if (Progress.chapterComplete) { Progress = new StoryProgress { chapter = table.chapter }; Progress.Save(); }
        running = StartCoroutine(RunChapter());
    }

    void Update()
    {
#if UNITY_EDITOR
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f9Key.wasPressedThisFrame && CurrentStep != null)
        {
            Log($"F9: completing '{CurrentStep.id}'");
            CompleteCurrentStep();
        }
#endif
    }

    IEnumerator RunChapter()
    {
        var steps = table.steps;
        int first = 0;
        while (first < steps.Length && Progress.IsDone(steps[first].id)) first++;
        Log(first == 0 ? "starting from the beginning" : $"resuming at '{(first < steps.Length ? steps[first].id : "the end")}' ({first} step(s) done)");

        for (int i = first; i < steps.Length; i++)
        {
            var step = steps[i];
            CurrentStep = step;
            CompleteRequested = false;
            StepStarted?.Invoke(step);
            Log($"step '{step.id}' ({step.kind}{(StoryStepKinds.IsBuilt(step.kind) ? "" : ", placeholder")})");

            if (!string.IsNullOrEmpty(step.arena) && ArenaDirectory.Instance != null && !ArenaDirectory.Instance.SelectRoot(step.arena))
                Debug.LogWarning($"Story: step {step.id}: no arena '{step.arena}'");
            StagePuppets(step);
            StageCast(step);
            yield return PlayBeats(step.intro);
            yield return StoryStepKinds.Make(step.kind).Run(this, step);
            if (InEncounter) EndEncounter();
            ClearObjective();
            yield return PlayBeats(step.outro);

            string next = i + 1 < steps.Length ? steps[i + 1].title : "";
            Progress.MarkDone(step.id, next);   // the checkpoint: this step will not be replayed
            StepCompleted?.Invoke(step);
            Log($"step '{step.id}' done, saved");
        }

        CurrentStep = null;
        Progress.chapterComplete = true;
        Progress.nextTitle = "";
        Progress.Save();
        StoryProgress.ChaptersUnlocked = unlocksChapter;
        ChapterCompleted?.Invoke(table.chapter);
        Log("chapter complete, saved");
        yield return subtitles != null ? subtitles.Wait(1.0f) : null;
        if (menu != null) menu.LeaveStory();
    }

    // ------------------------------------------------------------------ the API steps use

    /// <summary>Ends the current step as if its objective were met (the Editor's F9, tests).</summary>
    public void CompleteCurrentStep() { CompleteRequested = true; }

    // ------------------------------------------------------------------ replaying the demonstration (pause menu)

    /// <summary>True while a lesson or training round with a demonstration is being played (the pause menu offers
    /// "Replay the demonstration" then). Set by the step logic.</summary>
    public bool CanReplayDemo { get; set; }
    /// <summary>The player asked to see the demonstration again; the step stops its exercise and replays it.</summary>
    public bool DemoReplayRequested { get; private set; }
    public void RequestDemoReplay() { if (CanReplayDemo) { DemoReplayRequested = true; Log("demonstration replay requested"); } }
    public void ConsumeDemoReplay() { DemoReplayRequested = false; }

    /// <summary>Runs <paramref name="body"/> like a nested coroutine, but abandons it the frame <paramref name="stop"/>
    /// turns true - disposing every nested level, so their finally blocks run. <paramref name="stopped"/> reports
    /// which way it ended.</summary>
    public static IEnumerator RunUntil(IEnumerator body, Func<bool> stop, Action<bool> stopped)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(body);
        bool wasStopped = false;
        while (stack.Count > 0)
        {
            if (stop())
            {
                wasStopped = true;
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                break;
            }
            var top = stack.Peek();
            if (!top.MoveNext()) { stack.Pop(); continue; }
            if (top.Current is IEnumerator inner) { stack.Push(inner); continue; }
            yield return top.Current;
        }
        stopped?.Invoke(wasStopped);
    }

    /// <summary>Finds a cast member by character name ("Musashi", "Sensei", "Arima" matches "Arima Kihei"), active or benched.</summary>
    public Fighter Cast(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var f in Resources.FindObjectsOfTypeAll<Fighter>())
        {
            if (!f.gameObject.scene.IsValid() || f.gameObject.scene != gameObject.scene) continue;
            string cn = f.CharacterName ?? "";
            if (cn.Equals(name, StringComparison.OrdinalIgnoreCase) || cn.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)) return f;
        }
        return null;
    }

    /// <summary>A mark under the chapter's spot root, or "Root/Child/..." anywhere in the scene (switched-off roots
    /// included - e.g. "Musashi_Plaza/Mark_P1", the boss's marks in the plaza fight space).</summary>
    public Transform Spot(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var t = spotRoot != null ? spotRoot.Find(name) : null;
        if (t != null || name.IndexOf('/') < 0) return t;
        int cut = name.IndexOf('/');
        string rootName = name.Substring(0, cut), rest = name.Substring(cut + 1);
        foreach (var go in gameObject.scene.GetRootGameObjects())
            if (go.name == rootName) { var found = go.transform.Find(rest); if (found != null) return found; }
        return null;
    }

    /// <summary>Makes the step's cast the fighting pair and stands them on its spots, held (no input, no AI).</summary>
    public void StageCast(ChapterStep step)
    {
        if (menu == null || step.cast == null || step.cast.Length < 2) { if (menu != null) menu.StoryHold(); return; }
        if (step.kind == "explore")
        {
            // a walk, not a fight: only the walker is placed; the step benches the rest and handles the camera
            var walker = Cast(step.cast[0]);
            if (walker != null && step.spots != null && step.spots.Length > 0) Place(walker, step.spots[0]);
            menu.StoryHold();
            return;
        }
        Fighter a = Cast(step.cast[0]), b = Cast(step.cast[1]);
        if (a == null || b == null) { Debug.LogError($"Story: step {step.id}: cast '{step.cast[0]}' / '{step.cast[1]}' not in the scene"); return; }
        menu.StoryApplyPair(a, b);
        Place(a, step.spots != null && step.spots.Length > 0 ? step.spots[0] : null);
        Place(b, step.spots != null && step.spots.Length > 1 ? step.spots[1] : null);
        if (FightCamera.Instance != null) FightCamera.Instance.RetargetToFighters();
        menu.StoryHold();
    }

    // ------------------------------------------------------------------ placing

    /// <summary>The training driver on this object (created on first use).</summary>
    public TrainingDriver Training => GetComponent<TrainingDriver>() ?? gameObject.AddComponent<TrainingDriver>();

    /// <summary>Stands the step's pair on the line between their two spots, <paramref name="gap"/> apart about the
    /// spots' midpoint, facing each other. Attack, stun and dash are cleared; health and stamina are KEPT unless
    /// <paramref name="refill"/> (a fresh exchange is not a free stamina refill).</summary>
    public void PlacePair(ChapterStep step, float gap, bool refill)
    {
        if (!PairMarks(step, gap, out var a, out var b, out var pa, out var pb, out var axis)) return;
        Reposition(a, pa, Quaternion.LookRotation(axis), refill);
        Reposition(b, pb, Quaternion.LookRotation(-axis), refill);
        if (FightCamera.Instance != null) FightCamera.Instance.RetargetToFighters();
    }

    /// <summary>Where <see cref="PlacePair"/> stands the step's pair: on the line between their spots, gap apart.</summary>
    public bool PairMarks(ChapterStep step, float gap, out Fighter a, out Fighter b, out Vector3 pa, out Vector3 pb, out Vector3 axis)
    {
        a = Cast(step.cast[0]); b = Cast(step.cast[1]);
        pa = pb = Vector3.zero; axis = Vector3.right;
        Transform sa = Spot(step.spots[0]), sb = Spot(step.spots[1]);
        if (a == null || b == null || sa == null || sb == null) return false;
        Vector3 mid = (sa.position + sb.position) * 0.5f;
        axis = sb.position - sa.position;
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-4f) axis = Vector3.right;
        axis.Normalize();
        float half = (gap > 0f ? gap : Vector3.Distance(sa.position, sb.position)) * 0.5f;
        pa = mid - axis * half; pb = mid + axis * half;
        pa.y = sa.position.y; pb.y = sb.position.y;
        return true;
    }

    /// <summary>The reset between exchanges WITHOUT a cut (the user's call): the fight is held, and both WALK back to
    /// their marks - backing up while they face each other, turning to walk when the way back is mostly sideways -
    /// then square up. Health and stamina are kept unless <paramref name="refill"/>. Call with the fight held.</summary>
    public IEnumerator WalkPairBack(ChapterStep step, float gap, bool refill, float maxSeconds = 4f)
    {
        if (!PairMarks(step, gap, out var a, out var b, out var pa, out var pb, out var axis)) yield break;
        // clear attacks, stun and dash, keep the bars and where they stand; the animator returns to the idle
        Reposition(a, a.transform.position, a.transform.rotation, refill: false);
        Reposition(b, b.transform.position, b.transform.rotation, refill: false);
        var ma = new WalkBack(a, pa, b); var mb = new WalkBack(b, pb, a);
        float t = 0f;
        while ((!ma.Done || !mb.Done) && t < maxSeconds)
        {
            if (Time.timeScale > 0f) { float dt = Time.deltaTime; ma.Step(dt); mb.Step(dt); t += dt; }
            yield return null;
        }
        // square up to face each other on the marks
        for (float k = 0f; k < 0.25f; k += Time.deltaTime)
        {
            ma.Face(axis, Time.deltaTime); mb.Face(-axis, Time.deltaTime);
            yield return null;
        }
        ma.Finish(); mb.Finish();
        Reposition(a, pa, Quaternion.LookRotation(axis), refill);
        Reposition(b, pb, Quaternion.LookRotation(-axis), refill);
    }

    /// <summary>One fighter walking himself back to a mark while the fight is held (his Fighter is off).</summary>
    class WalkBack
    {
        readonly Fighter f, other;
        readonly Vector3 target;
        readonly Rigidbody rb;
        readonly Animator anim;
        readonly bool wasKinematic;
        public bool Done { get; private set; }

        public WalkBack(Fighter f, Vector3 target, Fighter other)
        {
            this.f = f; this.target = target; this.other = other;
            rb = f.GetComponent<Rigidbody>();
            anim = f.GetComponent<Animator>();
            if (rb != null) { wasKinematic = rb.isKinematic; rb.isKinematic = true; }
        }

        public void Step(float dt)
        {
            if (Done) return;
            Vector3 p = f.transform.position, to = target - p;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.04f) { Done = true; SetMove(0f); return; }
            Vector3 dir = to / dist;
            Vector3 toOther = other.transform.position - p; toOther.y = 0f;
            toOther = toOther.sqrMagnitude > 1e-4f ? toOther.normalized : f.transform.forward;
            // back up facing him; turn and walk when the way is mostly sideways and long
            bool sideways = Mathf.Abs(Vector3.Dot(dir, toOther)) < 0.5f && dist > 1.0f;
            Vector3 face = sideways ? dir : toOther;
            f.transform.rotation = Quaternion.RotateTowards(f.transform.rotation, Quaternion.LookRotation(face), 420f * dt);
            float fwd = Vector3.Dot(dir, f.transform.forward);
            float speed = Mathf.Max(2.2f, f.WalkSpeed) * (fwd < 0f ? BalanceConfig.BACK_WALK_SPEED_MULTIPLIER : 1f);
            float stepLen = Mathf.Min(dist, speed * dt);
            Vector3 np = p + dir * stepLen;
            np.y = Mathf.MoveTowards(p.y, target.y, 2f * dt);
            f.transform.position = np;
            if (rb != null) rb.position = np;
            SetMove(Mathf.Clamp(fwd, -1f, 1f));
        }

        public void Face(Vector3 dir, float dt)
        {
            f.transform.rotation = Quaternion.RotateTowards(f.transform.rotation, Quaternion.LookRotation(dir), 420f * dt);
            SetMove(0f);
        }

        void SetMove(float v) { if (anim != null && anim.runtimeAnimatorController != null) anim.SetFloat("MoveSpeed", v); }

        public void Finish()
        {
            SetMove(0f);
            if (rb != null) rb.isKinematic = wasKinematic;
        }
    }

    /// <summary>ResetForMatch (clears attack, stun, dash, animator) without its fresh stats, unless refilling.</summary>
    public void Reposition(Fighter f, Vector3 pos, Quaternion rot, bool refill)
    {
        if (f == null) return;
        var old = f.Stats;
        int hp = old.currentHealth, st = old.currentStamina, floor = old.minHealth;
        f.ResetForMatch(pos, rot);
        f.Stats.minHealth = floor;
        if (!refill) { f.Stats.currentHealth = Mathf.Max(hp, floor); f.Stats.currentStamina = st; }
    }

    readonly List<StoryPuppet> puppets = new List<StoryPuppet>();
    string puppetKey = "";

    /// <summary>Makes the step's background cast (and only it) stand on their spots. Consecutive steps with the
    /// same list keep the same puppets, so the Partner does not restart his drill between lessons.</summary>
/// <summary>The staged background puppet of <paramref name="name"/> ("Sensei", "Partner"), or null.</summary>
    public StoryPuppet Puppet(string name)
    {
        foreach (var p in puppets) if (p != null && p.Source != null && p.Source.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

        public void StagePuppets(ChapterStep step)
    {
        string key = step.puppets != null ? string.Join("|", step.puppets) : "";
        if (key == puppetKey) return;
        foreach (var p in puppets) if (p != null) Destroy(p.gameObject);
        puppets.Clear();
        puppetKey = key;
        if (step.puppets == null) return;
        foreach (var w in step.puppets)
        {
            var parts = w.Split(':');
            var who = Cast(parts[0]);
            var spot = parts.Length > 1 ? Spot(parts[1]) : null;
            if (who == null || spot == null) { Debug.LogWarning($"Story: puppet '{w}': no such cast or spot"); continue; }
            var mode = parts.Length > 2 && parts[2] == "practice" ? StoryPuppet.Mode.Practice : StoryPuppet.Mode.Idle;
            puppets.Add(StoryPuppet.Create(who, spot, mode));
        }
    }

    /// <summary>The nonlethal knockdown (the user's call: kneel, then get back up). The fight is held first, so the
    /// opponent cannot keep attacking. Beat "ch1.knockdown" (the Sensei stops it, tells him to get up) plays over the
    /// kneel; then the get-up, then "Again!", and the fighter is back to full health.</summary>
    public IEnumerator Knockdown(Fighter downed, string beatId = "ch1.knockdown", bool getUp = true)
    {
        HoldEncounter();
        var anim = downed != null ? downed.GetComponent<Animator>() : null;
        if (anim != null) anim.CrossFadeInFixedTime("Story_Kneel", 0.25f);
        yield return PlayBeat(beatId);
        if (!getUp) yield break;   // the fight is over with him down (the end of sparring): the next beat plays over it
        if (anim != null) anim.CrossFadeInFixedTime("Story_GetUp", 0.2f);
        yield return Wait(1.0f);   // the get-up clip is 0.6 s, then it blends to the idle
        yield return Say("ch1.retry");
        if (downed != null) downed.Stats.currentHealth = downed.Stats.maxHealth;
    }

    bool readyPressed;
    /// <summary>Answers the Ready prompt as if the player pressed it (tests).</summary>
    public void PressReady() { readyPressed = true; }
    public bool WaitingForReady { get; private set; }

    /// <summary>Holds on the Ready prompt until Cross / Space / Enter (or <see cref="PressReady"/>).</summary>
    public IEnumerator WaitReady()
    {
        readyPressed = false;
        WaitingForReady = true;
        yield return null;   // a press still down from the demonstration does not count
        while (!readyPressed)
        {
            if (subtitles != null) subtitles.SetReady(StoryControls.ReadyPrompt);
            if (Time.timeScale > 0f && InputManager.Instance != null && InputManager.Instance.MenuConfirm()) break;
            yield return null;
        }
        readyPressed = false;
        WaitingForReady = false;
        if (subtitles != null) subtitles.SetReady(null);
        yield return null;   // the press that answered does not also swing
    }

    public IEnumerator FadeOut(float seconds = 0.4f) { if (subtitles != null) yield return subtitles.Fade(1f, seconds); }
    public IEnumerator FadeIn(float seconds = 0.4f) { if (subtitles != null) yield return subtitles.Fade(0f, seconds); }

    /// <summary>A short fade to black and back around <paramref name="during"/> (an exchange reset).</summary>
    public IEnumerator FadeThrough(System.Action during, float seconds = 0.25f)
    {
        if (subtitles != null) yield return subtitles.Fade(1f, seconds);
        during?.Invoke();
        yield return null;
        if (subtitles != null) yield return subtitles.Fade(0f, seconds);
    }

    /// <summary>Puts a fighter back on a mark at full health and stamina, attack and stun cleared.</summary>
    public void Place(Fighter f, string spotName)
    {
        var spot = Spot(spotName);
        if (f == null) return;
        if (spot == null) { if (spotName != null) Debug.LogWarning($"Story: no spot '{spotName}'"); return; }
        f.ResetForMatch(spot.position, spot.rotation);
    }

    /// <summary>Starts the step's pair fighting (no clock, no K.O. card). The Sensei's lessons hide his bars.</summary>
    public void BeginEncounter(ChapterStep step, bool showOpponentBars)
    {
        if (menu == null) return;
        menu.StartStoryEncounter();
        var hud = FindAnyObjectByType<FightHud>();
        if (hud != null) { hud.SetVisible(true); hud.SetPlayerBarsVisible(1, true); hud.SetPlayerBarsVisible(2, showOpponentBars); }
        InEncounter = true;
        Held = false;
    }

    /// <summary>Stops the fight: fighters frozen, input off, the line on screen taken down.</summary>
    public void EndEncounter()
    {
        if (menu != null) menu.EndStoryEncounter();
        var hud = FindAnyObjectByType<FightHud>();
        if (hud != null) hud.SetVisible(false);
        if (subtitles != null) subtitles.Clear();
        InEncounter = false;
        Held = false;
    }

    /// <summary>Holds the fight between exchanges (a reset, a beat); <see cref="ResumeEncounter"/> hands it back.
    /// The fight is held by a step (a knockdown, a reset). A beat played meanwhile must NOT hand control back
    /// when it ends - only the step that held it resumes it.</summary>
    public bool Held { get; private set; }
    public void HoldEncounter() { Held = true; if (menu != null) menu.StoryHold(); }
    public void ResumeEncounter() { Held = false; if (menu != null && InEncounter) menu.StoryResume(); }

    public void SetObjective(string text, int progress = 0, int target = 0, string extra = null) { if (subtitles != null) subtitles.SetObjective(text, progress, target, extra); }
    public void ClearObjective() { if (subtitles != null) { subtitles.ClearObjective(); subtitles.SetMarker(null); } }
    public void SetMarker(Vector3? world) { if (subtitles != null) subtitles.SetMarker(world); }
    public GameMenu Menu => menu;
    public bool Bark(string idOrGroup) => subtitles != null && subtitles.Bark(idOrGroup);
    public IEnumerator Say(string id) { if (subtitles != null) yield return subtitles.Say(Lines.Get(id)); }
    public IEnumerator Wait(float seconds) { if (subtitles != null) yield return subtitles.Wait(seconds); }

    /// <summary>Plays beats in order. Each one holds the fight first (the cutscene safety contract in
    /// <see cref="StoryBeats"/>) and resumes it afterwards if an encounter was running.</summary>
    public IEnumerator PlayBeats(string[] beats)
    {
        if (beats == null) yield break;
        foreach (var b in beats) yield return PlayBeat(b);
    }

    public IEnumerator PlayBeat(string beatId)
    {
        if (string.IsNullOrEmpty(beatId)) yield break;
        bool wasLive = InEncounter && !Held;
        HoldEncounter();
        PlayingBeat = true;
        StoryBeats.RaiseStarted(beatId);
        var player = StoryBeats.PlayerFor(beatId);
        if (player != null) yield return player(beatId);
        else
        {
            var seq = Lines.Sequence(beatId);
            if (seq.Count == 0) Debug.LogWarning($"Story: beat '{beatId}' has no lines and no player");
            if (subtitles != null) subtitles.Clear();
            foreach (var line in seq) if (subtitles != null) yield return subtitles.Say(line);
        }
        StoryBeats.RaiseFinished(beatId);
        PlayingBeat = false;
        if (wasLive) ResumeEncounter();
    }

    void OnLeftToMenu()
    {
        // The pause menu's Main Menu: stop where we are. The step in progress is not saved as done, so
        // Continue restarts it from its beginning.
        if (running != null) StopCoroutine(running);
        running = null;
        if (GetComponent<TrainingDriver>() is TrainingDriver td && td.Active) td.End();
        foreach (var w in FindObjectsByType<ExploreWalker>(FindObjectsInactive.Include)) w.End();
        if (AiController.Instance != null && AiController.Instance.Active) AiController.Instance.Deactivate();
        if (subtitles != null) subtitles.SetMarker(null);
        Log($"left to the menu during '{CurrentStep?.id}'");
    }

    void Log(string msg) { if (debugLogs) Debug.Log($"Story [{table?.chapter}]: {msg}"); }
}
