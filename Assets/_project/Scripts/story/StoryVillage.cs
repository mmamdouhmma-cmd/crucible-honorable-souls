using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage 3: Musashi walks from the dojo to the village plaza himself - no teleport into the fight. Free walking
/// (<see cref="ExploreWalker"/>), the pause menu available, an objective marker over the plaza, nobody else on the
/// road who can fight (every other fighter is benched; Arima waits in the plaza as a puppet, which cannot be
/// attacked). Reaching the plaza fades to black, switches to the plaza fight space and stands the pair on its marks,
/// so the boss step's intro plays where the duel happens.
/// step.cast[0] = the walker, cast[1] = who waits; spots[0] = where the walk starts, spots[1] = the destination
/// (Musashi's mark in the plaza), spots[2] = the waiting opponent's mark. step.arena of the NEXT step is selected here.
/// </summary>
public class VillageWalk : StoryStepLogic
{
    /// <summary>Within this distance (ground plane) of the destination the walk is over.</summary>
    public const float ArriveRadius = 5.0f;
    const string FightSpace = "Musashi_Plaza";

    public override IEnumerator Run(ChapterRunner c, ChapterStep s)
    {
        var musashi = c.Cast(s.cast[0]);
        var waiting = s.cast.Length > 1 ? c.Cast(s.cast[1]) : null;
        var start = s.spots != null && s.spots.Length > 0 ? c.Spot(s.spots[0]) : null;
        var dest = s.spots != null && s.spots.Length > 1 ? c.Spot(s.spots[1]) : null;
        var oppMark = s.spots != null && s.spots.Length > 2 ? c.Spot(s.spots[2]) : null;
        if (musashi == null || dest == null) { Debug.LogError($"Story: {s.id}: no walker or no destination"); yield break; }

        // nobody else on the road can fight: the other fighters are benched (the puppets stand in)
        foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsInactive.Exclude))
            if (f != musashi) f.gameObject.SetActive(false);
        musashi.gameObject.SetActive(true);
        if (start != null) c.Place(musashi, s.spots[0]);

        c.Menu.StartStoryExplore();
        var walker = ExploreWalker.Begin(musashi);
        c.SetObjective(s.objective);
        c.SetMarker(dest.position);
        var guides = Guides(c, s, musashi, dest, oppMark);
        Debug.Log($"Story {s.id}: walking to {dest.name} ({Flat(musashi.transform.position, dest.position):0.0} away)");

        try
        {
            while (!c.CompleteRequested && Flat(musashi.transform.position, dest.position) > ArriveRadius)
                yield return null;
            Debug.Log($"Story {s.id}: arrived ({Flat(musashi.transform.position, dest.position):0.0} from the mark)");
            c.HoldEncounter();
            c.ClearObjective();
            // under black: the plaza fight space, the pair on its marks, the waiting puppet gone
            yield return c.FadeOut();
            walker.End();
            Object.Destroy(walker);
            if (ArenaDirectory.Instance != null) ArenaDirectory.Instance.SelectRoot(FightSpace);
            // the guides stand on their spots to watch (the boss step stages the same puppets, so nothing pops in)
            c.StagePuppets(new ChapterStep { puppets = WatchPuppets(s) });
            if (waiting != null)
            {
                c.Menu.StoryApplyPair(musashi, waiting);
                c.Place(musashi, s.spots[1]);
                if (oppMark != null) c.Place(waiting, s.spots[2]);
                if (FightCamera.Instance != null) FightCamera.Instance.RetargetToFighters();
            }
            c.Menu.StoryHold();
            yield return null;
            yield return c.FadeIn();
        }
        finally
        {
            if (walker != null) walker.End();
        }
    }

    /// <summary>The guides as idle puppets on their end spots - the boss step's "puppets" must list the same strings.</summary>
    public static string[] WatchPuppets(ChapterStep s)
    {
        if (s.guides == null || s.guides.Length == 0) return null;
        var r = new string[s.guides.Length];
        for (int i = 0; i < r.Length; i++) r[i] = s.guides[i] + ":idle";
        return r;
    }

    /// <summary>The step's guides (the user's call, 2026-11-04: Musashi follows his Sensei and his Partner to the
    /// plaza). Each is the staged puppet of that cast member (made at Musashi's start if there is none), switched to
    /// idle, routed to its spot, and set to lead: the second keeps behind the first, both wait for Musashi.</summary>
    static List<StoryGuide> Guides(ChapterRunner c, ChapterStep s, Fighter musashi, Transform dest, Transform oppMark)
    {
        var list = new List<StoryGuide>();
        if (s.guides == null) return list;
        Vector3 watch = oppMark != null ? (dest.position + oppMark.position) * 0.5f : dest.position;
        StoryGuide ahead = null;
        foreach (var entry in s.guides)
        {
            var parts = entry.Split(':');
            if (parts.Length < 2) continue;
            var source = c.Cast(parts[0]);
            var end = c.Spot(parts[1]);
            if (source == null || end == null) { Debug.LogWarning($"Story {s.id}: guide '{entry}' has no cast member or spot"); continue; }
            var puppet = c.Puppet(parts[0]);
            if (puppet == null)
            {
                var near = new GameObject("guide start").transform;
                near.SetPositionAndRotation(musashi.transform.position + musashi.transform.forward * (2f + list.Count * 1.5f), musashi.transform.rotation);
                puppet = StoryPuppet.Create(source, near, StoryPuppet.Mode.Idle);
                Object.Destroy(near.gameObject);
            }
            puppet.SetMode(StoryPuppet.Mode.Idle);
            var route = StoryPath.Find(puppet.transform.position, end.position);
            if (route == null) continue;
            var g = puppet.gameObject.GetComponent<StoryGuide>() ?? puppet.gameObject.AddComponent<StoryGuide>();
            g.speed = source.WalkSpeed;
            g.follower = musashi.transform;
            g.ahead = ahead;
            g.faceAtEnd = watch;
            g.Lead(route);
            float len = 0f; for (int i = 1; i < route.Count; i++) len += Vector3.Distance(route[i - 1], route[i]);
            Debug.Log($"Story {s.id}: {parts[0]} leads to {parts[1]}, {route.Count} points, {len:0.0} long, at {g.speed:0.00} u/s");
            list.Add(g);
            ahead = g;
        }
        return list;
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
}

/// <summary>
/// Stage 4: Arima Kihei. A normal duel - normal health and stamina, the existing AI on full rules a step above the
/// Partner (step.ai, default Insane), the normal killing blow. Musashi falling restarts the duel from its
/// checkpoint; Arima falling ends it, and the step's outro (the victory lines, the chapter's end) plays.
/// </summary>
public class BossDuel : StoryStepLogic
{
    /// <summary>Real seconds the killing blow's slow motion is left to play before the story takes over.</summary>
    public const float KillShotHold = 2.4f;

    public override IEnumerator Run(ChapterRunner c, ChapterStep s)
    {
        var musashi = c.Cast(s.cast[0]);
        var boss = c.Cast(s.cast[1]);
        if (musashi == null || boss == null) { Debug.LogError($"Story: {s.id} has no cast"); yield break; }
        var difficulty = AiDifficulty.Insane;
        if (!string.IsNullOrEmpty(s.ai) && System.Enum.TryParse(s.ai, true, out AiDifficulty d)) difficulty = d;

        void Start()
        {
            c.BeginEncounter(s, showOpponentBars: true);   // also clears the killing blow's darkness
            if (AiController.Instance != null) AiController.Instance.Activate(difficulty, boss.PlayerNumber, AiController.Rules.Full);
            c.SetObjective(s.objective);
        }

        WeaponSkin.UseAll(musashi, true);   // a real katana against Arima (the cut scene where he takes it comes later)
        Start();
        try
        {
            while (!c.CompleteRequested)
            {
                if (!boss.Stats.IsAlive())
                {
                    Debug.Log($"Story {s.id}: Arima Kihei is dead - Musashi wins");
                    if (AiController.Instance != null) AiController.Instance.Deactivate();
                    yield return new WaitForSecondsRealtime(KillShotHold);
                    c.HoldEncounter();
                    break;
                }
                if (!musashi.Stats.IsAlive())
                {
                    Debug.Log($"Story {s.id}: Musashi has fallen - the duel again from its checkpoint");
                    if (AiController.Instance != null) AiController.Instance.Deactivate();
                    yield return new WaitForSecondsRealtime(KillShotHold);
                    c.HoldEncounter();
                    yield return c.Say("ch1.boss.lost");
                    yield return c.FadeThrough(() =>
                    {
                        c.Place(musashi, s.spots[0]);   // full health and stamina, the fall undone
                        c.Place(boss, s.spots[1]);
                        KillShot.RestoreLights();
                    });
                    Start();
                    continue;
                }
                yield return null;
            }
        }
        finally
        {
            if (AiController.Instance != null && AiController.Instance.Active) AiController.Instance.Deactivate();
        }
    }
}
