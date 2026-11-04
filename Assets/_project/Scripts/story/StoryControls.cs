using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The story's button names and the side guide for each lesson and round (the user's call: the current exercise's
/// buttons only, never in a duel). Names come from the live bindings (<see cref="InputManager.KeysFor"/>), shown for
/// whichever device the player touched last: PlayStation names on a pad, key names on the keyboard.
/// </summary>
public static class StoryControls
{
    static bool usingPad = true, decided;

    /// <summary>Called every frame (StorySubtitles): follows the last device used.</summary>
    public static void Track()
    {
        if (!decided) { usingPad = Gamepad.all.Count > 0; decided = true; }
        var kb = Keyboard.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame) usingPad = false;
        foreach (var g in Gamepad.all)
        {
            if (g.buttonSouth.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame ||
                g.buttonEast.wasPressedThisFrame || g.leftShoulder.wasPressedThisFrame || g.rightTrigger.wasPressedThisFrame ||
                g.leftStick.ReadValue().sqrMagnitude > 0.25f || g.rightStick.ReadValue().sqrMagnitude > 0.25f)
            { usingPad = true; break; }
        }
    }

    public static bool UsingPad => usingPad;

    static ControlBindings.PlayerBindings B => InputManager.Instance != null ? InputManager.Instance.KeysFor(1) : null;

    /// <summary>The button name for an action on the device in use.</summary>
    public static string Button(GameAction a)
    {
        var b = B;
        if (b == null) return a.ToString();
        if (usingPad) { int p = b.Pad(a); return p >= 0 ? ControlBindings.PadLabel(p) : "-"; }
        return ControlBindings.KeyLabel(b.Key(a));
    }

    // dashing: a right-stick flick on the pad, the dash modifier + a direction on the keyboard
    public static string DashAway => usingPad ? "Flick R stick AWAY" : $"{Button(GameAction.Dash)} + {Away}";
    public static string DashToward => usingPad ? "Flick R stick TOWARD him" : $"{Button(GameAction.Dash)} + {Toward}";
    public static string Sidestep => usingPad ? "Flick R stick UP / DOWN" : $"{Button(GameAction.Up)} / {Button(GameAction.Down)}";
    public static string Special(GameAction attack) => $"{Button(GameAction.Special)} + {Button(attack)}";
    public static string ParryButton => usingPad ? $"{Button(GameAction.Defense)}  or raise {Button(GameAction.Block)}" : Button(GameAction.Defense);
    public static string BlockHold => $"hold {Button(GameAction.Block)}";
    public static string MoveKeys => usingPad ? "Left stick" : $"{Button(GameAction.MoveLeft)} / {Button(GameAction.MoveRight)}";
    static string Away => "direction away";
    static string Toward => "direction toward";

    /// <summary>"Button  Action" lines for the guide; the button in bronze.</summary>
    static string Lines(params (string button, string what)[] rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
            sb.Append("<color=#E8C98A><b>").Append(r.button).Append("</b></color>\n   ").Append(r.what).Append('\n');
        return sb.ToString();
    }

    /// <summary>The guide's closing tip wherever a strike must be timed (the user's call).</summary>
    const string WatchTip = "\n<color=#9FD3F0><b>Tip:</b> watch his WEAPON, not his body -\ntime the dash or parry off the blade.</color>\n";
    static System.Func<string> WithTip(System.Func<string> guide) => () => guide() + WatchTip;

    /// <summary>The guide for a step kind, or null (round 4, the walk, the boss: no guide).</summary>
    public static System.Func<string> GuideFor(string kind)
    {
        switch (kind)
        {
            case "lesson_light": return () => Lines(
                (Button(GameAction.Light), "Light Attack - the only strike that counts here"));
            case "lesson_dodge_counter": return WithTip(() => Lines(
                (DashAway, "Dash BACK out of the overhead"),
                (DashToward, "Dash back IN"),
                (Special(GameAction.Light), "Special Light - the counter")));
            case "lesson_spin": return WithTip(() => Lines(
                (DashAway + "  /  " + Sidestep, "Dodge the spin (any dash that clears it)"),
                (DashToward, "Dash IN - the fastest way back"),
                ($"{Button(GameAction.Light)}  {Button(GameAction.Medium)}  {Button(GameAction.Heavy)}", "Three strikes on his guard, back to back")));
            case "spar_dodge": return WithTip(() => Lines(
                (Sidestep, "Sidestep - beats thrusts and overheads"),
                (DashAway, "Dash back - beats cuts, NOT thrusts"),
                ($"{Button(GameAction.Light)}  {Button(GameAction.Medium)}  {Button(GameAction.Heavy)}", "Counter while he recovers"),
                (MoveKeys, "Step in")));
            case "spar_parry": return WithTip(() => Lines(
                (ParryButton, "Parry - just before the blade lands"),
                ($"{Button(GameAction.Light)}  {Button(GameAction.Medium)}  {Button(GameAction.Heavy)}", "Counter while he is stunned")));
            case "spar_clean": return WithTip(() => Lines(
                (BlockHold, "Block (a blocked hit is not counted against you)"),
                (ParryButton, "Parry"),
                (Sidestep + "  /  " + DashAway, "Dodge"),
                ($"{Button(GameAction.Light)}  {Button(GameAction.Medium)}  {Button(GameAction.Heavy)}", "Attacks - only clean hits count"),
                (Special(GameAction.Light).Replace(Button(GameAction.Light), "attack"), "Special attacks")));
        }
        return null;
    }

    /// <summary>The Ready prompt after a demonstration.</summary>
    public static string ReadyPrompt => $"Ready?   Press  <b>{(usingPad ? "Cross" : "Space / Enter")}</b>  to begin";
}
