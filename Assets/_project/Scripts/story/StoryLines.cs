using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One line of story dialogue (Japanese + English), read from a chapter's JSON table.</summary>
[Serializable]
public class StoryLine
{
    public string id;
    public string speaker;
    public string ja;
    public string en;
    public string group;     // lines sharing a group are picked at random (reprimands, praise)
    public float seconds;    // 0 = from the text length
    public bool draft;       // placeholder text, to be refined

    /// <summary>How long the line stays up, in seconds.</summary>
    public float Duration => seconds > 0f ? seconds
        : Mathf.Clamp(1.4f + 0.045f * Mathf.Max(en?.Length ?? 0, 2 * (ja?.Length ?? 0)), 1.8f, 5f);
}

/// <summary>
/// A chapter's dialogue table: lines by id, lines by group, speaker names. Data, not code - the steps and
/// beats ask for an id ("ch1.lesson1.intro") or a group ("ch1.lesson1.wrong"), never for text. Edit the JSON
/// to change a line; re-run ArtSource/Fonts/subset_jp.py if it adds a kanji.
/// </summary>
public class StoryLines
{
    [Serializable] class Speaker { public string id, ja, en; }
    [Serializable] class Table { public Speaker[] speakers; public StoryLine[] lines; }

    readonly Dictionary<string, StoryLine> byId = new Dictionary<string, StoryLine>();
    readonly Dictionary<string, List<StoryLine>> byGroup = new Dictionary<string, List<StoryLine>>();
    readonly Dictionary<string, Speaker> speakers = new Dictionary<string, Speaker>();
    readonly Dictionary<string, StoryLine> lastPicked = new Dictionary<string, StoryLine>();
    readonly List<StoryLine> ordered = new List<StoryLine>();

    public int Count => byId.Count;

    public static StoryLines Parse(TextAsset json)
    {
        var t = new StoryLines();
        if (json == null) { Debug.LogError("Story: no dialogue table assigned"); return t; }
        var table = JsonUtility.FromJson<Table>(json.text);
        if (table?.speakers != null) foreach (var s in table.speakers) t.speakers[s.id] = s;
        if (table?.lines != null)
            foreach (var l in table.lines)
            {
                if (string.IsNullOrEmpty(l.id)) continue;
                if (t.byId.ContainsKey(l.id)) Debug.LogWarning($"Story: dialogue id {l.id} appears twice; the first wins");
                else { t.byId[l.id] = l; t.ordered.Add(l); }
                if (!string.IsNullOrEmpty(l.group))
                {
                    if (!t.byGroup.TryGetValue(l.group, out var list)) t.byGroup[l.group] = list = new List<StoryLine>();
                    list.Add(l);
                }
            }
        return t;
    }

    public bool Has(string idOrGroup) => byId.ContainsKey(idOrGroup) || byGroup.ContainsKey(idOrGroup);

    /// <summary>The line with this id, or - if the name is a group - a random member, never the one picked last time.</summary>
    public StoryLine Get(string idOrGroup)
    {
        if (string.IsNullOrEmpty(idOrGroup)) return null;
        if (byId.TryGetValue(idOrGroup, out var line)) return line;
        if (!byGroup.TryGetValue(idOrGroup, out var list) || list.Count == 0)
        {
            Debug.LogWarning($"Story: no dialogue line or group '{idOrGroup}'");
            return null;
        }
        lastPicked.TryGetValue(idOrGroup, out var last);
        StoryLine pick;
        int guard = 0;
        do pick = list[UnityEngine.Random.Range(0, list.Count)]; while (list.Count > 1 && pick == last && guard++ < 8);
        lastPicked[idOrGroup] = pick;
        return pick;
    }

    /// <summary>Every line whose id is <paramref name="prefix"/> or starts with "prefix." - in table order
    /// (a beat "ch1.boss.intro" plays ch1.boss.intro.1 then .2).</summary>
    public List<StoryLine> Sequence(string prefix)
    {
        var result = new List<StoryLine>();
        foreach (var l in ordered)
            if (l.id == prefix || l.id.StartsWith(prefix + ".", StringComparison.Ordinal)) result.Add(l);
        return result;
    }

    public string SpeakerName(string id, bool japanese)
    {
        if (string.IsNullOrEmpty(id) || !speakers.TryGetValue(id, out var s)) return "";
        return japanese ? s.ja : s.en;
    }
}

/// <summary>Which subtitles the story shows. Saved on its own key: the duel game never reads it.</summary>
public static class StorySettings
{
    public enum Subtitles { Both, Japanese, English }
    const string Key = "crucible.story.subtitles";

    public static Subtitles SubtitleMode
    {
        get => (Subtitles)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Subtitles.Both), 0, 2);
        set { PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); Changed?.Invoke(); }
    }

    public static event Action Changed;

    public static string Label(Subtitles s) => s == Subtitles.Both ? "Japanese + English" : s == Subtitles.Japanese ? "Japanese" : "English";
}
