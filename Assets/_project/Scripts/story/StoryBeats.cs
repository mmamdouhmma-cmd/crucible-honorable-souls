using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// The hooks a cutscene system plugs into later. A BEAT is a named story moment ("ch1.boss.intro",
/// "ch1.knockdown") that the chapter plays between exchanges, with the fighters held and input off.
/// Today a beat plays the dialogue lines whose ids start with its name. A future cutscene registers a
/// player for that beat id and is played INSTEAD - no step, objective or encounter changes.
///
/// Safety contract (what the chapter guarantees a beat player): beats only start while the chapter has the
/// fighters held (no attack in flight, input off), and the chapter hands control back itself when the beat
/// ends - a cutscene never has to restore input, and cannot leave the player without controls.
///
/// Chapter One's beats, in order: ch1.open, ch1.sensei_intro, ch1.lesson1.intro, ch1.lesson1_to_2,
/// ch1.lesson2.intro, ch1.lesson2_to_3, ch1.lesson3.intro, ch1.knockdown, ch1.spar.intro, ch1.spar.round1..4,
/// ch1.spar.partner_down, ch1.dismiss, ch1.explore.intro, ch1.boss.intro, ch1.boss.lost, ch1.boss.won, ch1.end.
/// </summary>
public static class StoryBeats
{
    /// <summary>Plays a beat in place of its dialogue. Runs as a coroutine on the chapter.</summary>
    public delegate IEnumerator BeatPlayer(string beatId);

    static readonly Dictionary<string, BeatPlayer> players = new Dictionary<string, BeatPlayer>();

    /// <summary>A beat is starting (before its dialogue or cutscene).</summary>
    public static event Action<string> Started;
    /// <summary>A beat has finished; control is about to return to the chapter.</summary>
    public static event Action<string> Finished;
    /// <summary>A subtitle line went up - the hook for voice-over (play the clip for line.id).</summary>
    public static event Action<StoryLine> LineShown;

    public static void Register(string beatId, BeatPlayer player) { players[beatId] = player; }
    public static void Unregister(string beatId) { players.Remove(beatId); }
    public static bool HasPlayer(string beatId) => players.ContainsKey(beatId);

    internal static BeatPlayer PlayerFor(string beatId) => players.TryGetValue(beatId, out var p) ? p : null;
    internal static void RaiseStarted(string beatId) => Started?.Invoke(beatId);
    internal static void RaiseFinished(string beatId) => Finished?.Invoke(beatId);
    internal static void RaiseLine(StoryLine line) => LineShown?.Invoke(line);
}
