using UnityEngine;

/// <summary>
/// Debug helper: holds buttons down for one player for a stretch of game time.
///
/// Why it exists: <see cref="InputManager"/> CONSUMES the AI snapshot every frame
/// (<c>if (ai2) aiInput2 = new InputData();</c>), because the AI rewrites it each frame. So a
/// single SetAiInput call from the console or a test script lasts exactly one frame, which is
/// useless for anything held - block, the parry window, low block, a grab mash. This re-asserts
/// the snapshot every frame for the requested duration.
///
/// Add it to the GameMangers object at runtime, call <see cref="Hold"/>, and it releases itself.
/// Test-only; it has no effect until Hold is called.
/// </summary>
public class InputHold : MonoBehaviour
{
    [Tooltip("Which player's input to write (1 or 2).")]
    public int player = 2;

    [Tooltip("Buttons held while the timer runs.")]
    public bool block;

    [Tooltip("Parry (Defense) button, written for the first frames after the delay only: it is a press, not a hold.")]
    public bool parry;

    [Tooltip("Game-time seconds left to hold (0 = idle).")]
    public float secondsLeft;

    [Tooltip("Game-time seconds to wait before the press. Lets a test put the press at a chosen moment " +
             "inside an opponent's startup, which a console round trip is far too slow to do.")]
    public float delayLeft;

    /// <summary>Gets the helper on <paramref name="go"/>, adding it if it is not there yet.</summary>
    public static InputHold Ensure(GameObject go)
    {
        var existing = go.GetComponent<InputHold>();
        return existing != null ? existing : go.AddComponent<InputHold>();
    }

    /// <summary>
    /// Holds the given buttons for <paramref name="seconds"/> of game time, starting
    /// <paramref name="delay"/> seconds of game time from now.
    /// </summary>
    public void Hold(int who, bool blockHeld, float seconds, float delay = 0f)
    {
        player = who;
        block = blockHeld;
        parry = false;
        secondsLeft = seconds;
        delayLeft = delay;
        if (InputManager.Instance != null) InputManager.Instance.SetAi(player, true);
        Debug.Log($"InputHold: P{player} block={blockHeld} for {seconds:0.00}s, starting in {delay:0.00}s (game time)");
    }

    /// <summary>
    /// Taps the PARRY (Defense) button <paramref name="delay"/> game-seconds from now. The press is
    /// written for a couple of frames only, because Fighter reads it as an edge.
    /// </summary>
    public void Parry(int who, float delay = 0f)
    {
        player = who;
        block = false;
        parry = true;
        secondsLeft = 0.05f;   // a tap: long enough for the press to be seen, short enough not to repeat
        delayLeft = delay;
        if (InputManager.Instance != null) InputManager.Instance.SetAi(player, true);
        Debug.Log($"InputHold: P{player} PARRY tap in {delay:0.00}s (game time)");
    }

    /// <summary>Drops everything and hands the player back to their own controls.</summary>
    public void Release()
    {
        secondsLeft = 0f;
        delayLeft = 0f;
        block = false;
        parry = false;
        if (InputManager.Instance != null) InputManager.Instance.SetAi(player, false);
    }

    private void Update()
    {
        if (InputManager.Instance == null || secondsLeft <= 0f) return;

        if (delayLeft > 0f)
        {
            delayLeft -= Time.deltaTime;
            // Still waiting: write a blank snapshot so the guard is genuinely DOWN until the press.
            InputManager.Instance.SetAiInput(player, new InputData());
            if (delayLeft > 0f) return;
            Debug.Log($"InputHold: P{player} press now (t={Time.time:0.000})");
        }

        secondsLeft -= Time.deltaTime;
        InputManager.Instance.SetAiInput(player, new InputData { blockHeld = block, defenseButton = parry });
        if (secondsLeft <= 0f) Release();
    }
}
