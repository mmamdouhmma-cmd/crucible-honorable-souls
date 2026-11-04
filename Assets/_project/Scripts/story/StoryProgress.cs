using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A chapter's saved progress: which steps are done, the title of the next one, whether the chapter is
/// complete. A step is marked done only when it COMPLETES, so a reload resumes at the step that was in
/// progress and never replays a finished lesson or round. PlayerPrefs + JSON, like the game's settings.
/// </summary>
[Serializable]
public class StoryProgress
{
    public int version = 1;
    public string chapter;
    public List<string> completed = new List<string>();
    public string nextTitle = "";
    public bool chapterComplete;

    /// <summary>The highest chapter the player may start (2 = Chapter Two unlocked). Ready for a chapter select.</summary>
    public static int ChaptersUnlocked
    {
        get => Mathf.Max(1, PlayerPrefs.GetInt("crucible.story.unlocked", 1));
        set { PlayerPrefs.SetInt("crucible.story.unlocked", Mathf.Max(ChaptersUnlocked, value)); PlayerPrefs.Save(); }
    }

    /// <summary>Set by the main menu's "Start from the beginning": the next chapter load erases its progress first.</summary>
    public static bool StartFreshRequested;

    static string Key(string chapter) => "crucible.story." + chapter;

    public static bool Exists(string chapter) => PlayerPrefs.HasKey(Key(chapter));

    public static StoryProgress Load(string chapter)
    {
        StoryProgress p = null;
        string json = PlayerPrefs.GetString(Key(chapter), "");
        if (json.Length > 0)
        {
            try { p = JsonUtility.FromJson<StoryProgress>(json); }
            catch (Exception e) { Debug.LogWarning($"Story: progress for {chapter} unreadable, starting over ({e.Message})"); }
        }
        if (p == null) p = new StoryProgress();
        p.chapter = chapter;
        if (p.completed == null) p.completed = new List<string>();
        return p;
    }

    public static void Erase(string chapter)
    {
        PlayerPrefs.DeleteKey(Key(chapter));
        PlayerPrefs.Save();
    }

    public bool IsDone(string stepId) => completed.Contains(stepId);

    public void MarkDone(string stepId, string nextStepTitle)
    {
        if (!completed.Contains(stepId)) completed.Add(stepId);
        nextTitle = nextStepTitle ?? "";
        Save();
    }

    public void Save()
    {
        PlayerPrefs.SetString(Key(chapter), JsonUtility.ToJson(this));
        PlayerPrefs.Save();
    }
}
