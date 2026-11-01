using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Every rebindable game action. Dash is a modifier: hold it and press a direction (keyboard), or flick
/// the right stick (pad). The four diagonal actions dash on one tap, with no modifier. Appended only:
/// the saved key arrays are indexed by this order.
/// </summary>
public enum GameAction
{
    MoveLeft, MoveRight, Up, Down,
    Light, Medium, Heavy, Defense,
    Block, Throw, Special, Low,
    Run, Stance, Dash,
    DashUpLeft, DashUpRight, DashDownLeft, DashDownRight
}

/// <summary>
/// Keyboard and gamepad bindings, saved to PlayerPrefs as JSON. There are THREE key sets: <see cref="solo"/>
/// for one person at the keyboard (against the AI, Practice, Online), and <see cref="p1"/> / <see cref="p2"/>
/// for two players sharing it. Keyboard keys use Unity's legacy KeyCode (read with Input.GetKey); gamepad
/// buttons use the Input System's GamepadButton so PS4/PS5 pads work out of the box (Square = West,
/// Cross = South, Triangle = North, Circle = East). A pad value of -1 means "not bound".
/// </summary>
[Serializable]
public class ControlBindings
{
    public const int ActionCount = 19;
    const string PrefsKey = "crucible.controls.v1";
    /// <summary>Bump when the default keys change: an older save keeps its pad buttons and takes the new keys.</summary>
    const int CurrentLayout = 2;

    [Serializable]
    public class PlayerBindings
    {
        public int[] keys = new int[ActionCount];   // KeyCode per GameAction
        public int[] pad = new int[ActionCount];    // GamepadButton per GameAction, -1 = none

        public KeyCode Key(GameAction a) => (KeyCode)keys[(int)a];
        public int Pad(GameAction a) => pad[(int)a];
    }

    public PlayerBindings p1 = new PlayerBindings();
    public PlayerBindings p2 = new PlayerBindings();
    /// <summary>One person at the keyboard: against the AI, Practice and Online. Keys only; pads stay on p1 / p2.</summary>
    public PlayerBindings solo = new PlayerBindings();
    /// <summary>The alternative solo layout: the sides swapped, moving on the left and attacking on the right.</summary>
    public PlayerBindings soloAlt = new PlayerBindings();
    /// <summary>Which solo layout is in use: 0 = solo, 1 = soloAlt.</summary>
    public int soloLayout;
    /// <summary>Which player the first connected controller drives (1 or 2). A second controller drives the other one.</summary>
    public int firstPadPlayer = 2;
    public int layoutVersion = CurrentLayout;

    /// <summary>1 and 2 are the two-player sets; 0 is the solo layout in use.</summary>
    public PlayerBindings Player(int n) => n == 0 ? (soloLayout == 1 ? soloAlt : solo) : n == 1 ? p1 : p2;

    public string SoloLayoutName => soloLayout == 1 ? "WASD move, B N M attack" : "Arrows move, A S D attack";

    public static ControlBindings Defaults()
    {
        var b = new ControlBindings();
        SetKeys(b.solo, new Dictionary<GameAction, KeyCode>
        {
            { GameAction.MoveLeft, KeyCode.LeftArrow }, { GameAction.MoveRight, KeyCode.RightArrow }, { GameAction.Up, KeyCode.UpArrow }, { GameAction.Down, KeyCode.DownArrow },
            { GameAction.Light, KeyCode.A }, { GameAction.Medium, KeyCode.S }, { GameAction.Heavy, KeyCode.D },
            { GameAction.Defense, KeyCode.Keypad0 }, { GameAction.Block, KeyCode.Keypad0 },
            { GameAction.Special, KeyCode.LeftShift }, { GameAction.Run, KeyCode.Space }, { GameAction.Dash, KeyCode.Space },
        });
        SetKeys(b.soloAlt, new Dictionary<GameAction, KeyCode>
        {
            { GameAction.MoveLeft, KeyCode.A }, { GameAction.MoveRight, KeyCode.D }, { GameAction.Up, KeyCode.W }, { GameAction.Down, KeyCode.S },
            { GameAction.Light, KeyCode.B }, { GameAction.Medium, KeyCode.N }, { GameAction.Heavy, KeyCode.M },
            { GameAction.Defense, KeyCode.RightAlt }, { GameAction.Block, KeyCode.RightAlt },
            { GameAction.Special, KeyCode.RightControl }, { GameAction.Run, KeyCode.Space }, { GameAction.Dash, KeyCode.Space },
        });
        SetKeys(b.p1, new Dictionary<GameAction, KeyCode>
        {
            { GameAction.MoveLeft, KeyCode.A }, { GameAction.MoveRight, KeyCode.D }, { GameAction.Up, KeyCode.W }, { GameAction.Down, KeyCode.X },
            { GameAction.DashUpLeft, KeyCode.Q }, { GameAction.DashUpRight, KeyCode.E }, { GameAction.DashDownLeft, KeyCode.Z }, { GameAction.DashDownRight, KeyCode.C },
            { GameAction.Light, KeyCode.H }, { GameAction.Medium, KeyCode.J }, { GameAction.Heavy, KeyCode.K },
            { GameAction.Defense, KeyCode.O }, { GameAction.Block, KeyCode.O },
            { GameAction.Special, KeyCode.L }, { GameAction.Run, KeyCode.F }, { GameAction.Dash, KeyCode.F },
        });
        SetKeys(b.p2, new Dictionary<GameAction, KeyCode>
        {
            { GameAction.MoveLeft, KeyCode.LeftArrow }, { GameAction.MoveRight, KeyCode.RightArrow }, { GameAction.Up, KeyCode.UpArrow }, { GameAction.Down, KeyCode.DownArrow },
            { GameAction.Light, KeyCode.Keypad4 }, { GameAction.Medium, KeyCode.Keypad5 }, { GameAction.Heavy, KeyCode.Keypad6 },
            { GameAction.Defense, KeyCode.Keypad9 }, { GameAction.Block, KeyCode.Keypad9 },
            { GameAction.Special, KeyCode.KeypadPlus }, { GameAction.Run, KeyCode.Keypad0 }, { GameAction.Dash, KeyCode.Keypad0 },
        });
        foreach (var p in new[] { b.p1, b.p2, b.solo, b.soloAlt }) SetPad(p, DefaultPad());
        return b;
    }

    static Dictionary<GameAction, int> DefaultPad() => new Dictionary<GameAction, int>
    {
        { GameAction.MoveLeft, (int)GamepadButton.DpadLeft }, { GameAction.MoveRight, (int)GamepadButton.DpadRight },
        { GameAction.Up, (int)GamepadButton.DpadUp }, { GameAction.Down, (int)GamepadButton.DpadDown },
        { GameAction.Light, (int)GamepadButton.West }, { GameAction.Medium, (int)GamepadButton.South },
        { GameAction.Heavy, (int)GamepadButton.North }, { GameAction.Defense, (int)GamepadButton.East },
        { GameAction.Block, (int)GamepadButton.RightTrigger }, { GameAction.Throw, (int)GamepadButton.RightShoulder },
        { GameAction.Special, (int)GamepadButton.LeftShoulder }, { GameAction.Low, (int)GamepadButton.LeftTrigger },
        { GameAction.Run, (int)GamepadButton.LeftStick }, { GameAction.Stance, (int)GamepadButton.RightStick },
        { GameAction.Dash, -1 },
    };

    static void SetKeys(PlayerBindings p, Dictionary<GameAction, KeyCode> map)
    {
        for (int i = 0; i < ActionCount; i++) p.keys[i] = (int)KeyCode.None;
        foreach (var kv in map) p.keys[(int)kv.Key] = (int)kv.Value;
    }

    static void SetPad(PlayerBindings p, Dictionary<GameAction, int> map)
    {
        for (int i = 0; i < ActionCount; i++) p.pad[i] = -1;
        foreach (var kv in map) p.pad[(int)kv.Key] = kv.Value;
    }

    public static ControlBindings Load()
    {
        var b = Defaults();
        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return b;
        try
        {
            var saved = JsonUtility.FromJson<ControlBindings>(json);
            if (saved == null || saved.p1 == null || saved.p2 == null) return b;
            if (saved.layoutVersion >= CurrentLayout && saved.solo != null &&
                saved.solo.keys.Length == ActionCount &&
                saved.p1.keys.Length == ActionCount && saved.p2.keys.Length == ActionCount &&
                saved.p1.pad.Length == ActionCount && saved.p2.pad.Length == ActionCount)
            {
                // A save from before the alternative layout existed holds an empty set.
                if (saved.soloAlt == null || Array.TrueForAll(saved.soloAlt.keys, k => k == 0)) saved.soloAlt = b.soloAlt;
                return saved;
            }
            // A save from an older layout: the new keys, but the player's controller choices stay.
            CopyPad(saved.p1, b.p1);
            CopyPad(saved.p2, b.p2);
            b.firstPadPlayer = saved.firstPadPlayer;
        }
        catch (Exception e) { Debug.LogWarning("Controls: saved bindings unreadable, using defaults: " + e.Message); }
        return b;
    }

    static void CopyPad(PlayerBindings from, PlayerBindings to)
    {
        for (int i = 0; i < ActionCount && i < from.pad.Length; i++) to.pad[i] = from.pad[i];
    }

    public void Save()
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
        PlayerPrefs.Save();
    }

    public static void ResetSaved() { PlayerPrefs.DeleteKey(PrefsKey); PlayerPrefs.Save(); }

    /// <summary>The gamepad driving player <paramref name="player"/>, or null.</summary>
    public Gamepad PadFor(int player)
    {
        var all = Gamepad.all;
        if (all.Count == 0) return null;
        int first = firstPadPlayer;
        if (InputManager.Instance != null && InputManager.Instance.IsAi(first)) first = first == 1 ? 2 : 1;
        int index = player == first ? 0 : 1;
        return index < all.Count ? all[index] : null;
    }

    public static string ActionLabel(GameAction a)
    {
        switch (a)
        {
            case GameAction.MoveLeft: return "Move left";
            case GameAction.MoveRight: return "Move right";
            case GameAction.Up: return "Dash up";
            case GameAction.Down: return "Dash down";
            case GameAction.DashUpLeft: return "Dash up-left";
            case GameAction.DashUpRight: return "Dash up-right";
            case GameAction.DashDownLeft: return "Dash down-left";
            case GameAction.DashDownRight: return "Dash down-right";
            case GameAction.Light: return "Light attack";
            case GameAction.Medium: return "Medium attack";
            case GameAction.Heavy: return "Heavy attack";
            case GameAction.Defense: return "Defense (parry)";
            case GameAction.Block: return "Block (hold)";
            case GameAction.Throw: return "Throw";
            case GameAction.Special: return "Special modifier";
            case GameAction.Low: return "Low modifier";
            case GameAction.Run: return "Run (hold)";
            case GameAction.Stance: return "Stance switch";
            case GameAction.Dash: return "Dash modifier";
        }
        return a.ToString();
    }

    public static string KeyLabel(KeyCode k)
    {
        if (k == KeyCode.None) return "-";
        switch (k)
        {
            case KeyCode.Semicolon: return ";";
            case KeyCode.Quote: return "'";
            case KeyCode.Comma: return ",";
            case KeyCode.Period: return ".";
            case KeyCode.Slash: return "/";
            case KeyCode.Backslash: return "\\";
            case KeyCode.RightBracket: return "]";
            case KeyCode.LeftBracket: return "[";
            case KeyCode.Equals: return "=";
            case KeyCode.Minus: return "-";
            case KeyCode.LeftArrow: return "Left";
            case KeyCode.RightArrow: return "Right";
            case KeyCode.UpArrow: return "Up";
            case KeyCode.DownArrow: return "Down";
            case KeyCode.LeftShift: return "L Shift";
            case KeyCode.RightShift: return "R Shift";
            case KeyCode.LeftControl: return "L Ctrl";
            case KeyCode.RightControl: return "R Ctrl";
            case KeyCode.LeftAlt: return "L Alt";
            case KeyCode.RightAlt: return "R Alt";
            case KeyCode.KeypadPlus: return "Num +";
            case KeyCode.KeypadMinus: return "Num -";
            case KeyCode.KeypadMultiply: return "Num *";
            case KeyCode.KeypadDivide: return "Num /";
            case KeyCode.KeypadPeriod: return "Num .";
        }
        string s = k.ToString();
        if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);
        return s.StartsWith("Alpha") ? s.Substring(5) : s;
    }

    /// <summary>PlayStation names for the pad buttons (what the user plays with); Xbox names in brackets where they differ.</summary>
    public static string PadLabel(int button)
    {
        if (button < 0) return "-";
        switch ((GamepadButton)button)
        {
            case GamepadButton.West: return "Square";
            case GamepadButton.South: return "Cross";
            case GamepadButton.North: return "Triangle";
            case GamepadButton.East: return "Circle";
            case GamepadButton.LeftShoulder: return "L1";
            case GamepadButton.RightShoulder: return "R1";
            case GamepadButton.LeftTrigger: return "L2";
            case GamepadButton.RightTrigger: return "R2";
            case GamepadButton.LeftStick: return "L3";
            case GamepadButton.RightStick: return "R3";
            case GamepadButton.DpadUp: return "D-pad up";
            case GamepadButton.DpadDown: return "D-pad down";
            case GamepadButton.DpadLeft: return "D-pad left";
            case GamepadButton.DpadRight: return "D-pad right";
            case GamepadButton.Start: return "Options";
            case GamepadButton.Select: return "Share";
        }
        return ((GamepadButton)button).ToString();
    }

    /// <summary>Buttons a player may bind (the menu reserves Options for pause).</summary>
    public static readonly GamepadButton[] BindablePadButtons =
    {
        GamepadButton.West, GamepadButton.South, GamepadButton.North, GamepadButton.East,
        GamepadButton.LeftShoulder, GamepadButton.RightShoulder, GamepadButton.LeftTrigger, GamepadButton.RightTrigger,
        GamepadButton.LeftStick, GamepadButton.RightStick,
        GamepadButton.DpadUp, GamepadButton.DpadDown, GamepadButton.DpadLeft, GamepadButton.DpadRight,
        GamepadButton.Select,
    };

    static KeyCode[] bindableKeys;
    /// <summary>Keyboard keys a player may bind (letters, digits, arrows, punctuation, modifiers; not Escape or mouse).</summary>
    public static KeyCode[] BindableKeys
    {
        get
        {
            if (bindableKeys != null) return bindableKeys;
            var list = new List<KeyCode>();
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if (k == KeyCode.None || k == KeyCode.Escape) continue;
                string s = k.ToString();
                if (s.StartsWith("Mouse") || s.StartsWith("Joystick")) continue;
                if ((int)k >= (int)KeyCode.JoystickButton0) continue;
                list.Add(k);
            }
            bindableKeys = list.ToArray();
            return bindableKeys;
        }
    }
}
