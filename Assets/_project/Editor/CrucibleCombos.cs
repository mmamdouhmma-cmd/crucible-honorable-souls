// The combo tables.
//
// Crucible > Combat > Wire combo strings writes every AttackData's comboLinks from the tables
// below, so the strings live in one reviewable place instead of being hand-edited into YAML with
// raw GUIDs. Re-runnable: it REPLACES each listed attack's links, so editing a row here and
// running the menu item is the way to change a string.
//
// The shapes (the user's design):
//     normal -> special
//     normal -> normal -> special
//     special -> normal
// A plain normal-into-normal link is therefore marked openerOnly, which means it is offered only
// when that attack OPENED the string. Without it a mid-string normal would offer its own
// continuation, giving three normals in a row and letting strings double back on themselves.
//
// Special enders require HitOrBlock: a whiffed light must not buy a free special. The engine's
// cancel fires the instant a blade connects, so a hit-confirmed string still feels immediate.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CrucibleCombos
{
    // AttackType
    const int L = 0, M = 1, H = 2;
    // CancelRequirement
    const int Always = 0, HitOrBlock = 1;

    struct Link
    {
        public int input;          // button that continues the string
        public bool sp;            // the button must carry the special modifier
        public string followUp;    // AttackData asset name
        public bool openerOnly;    // only offered when this attack opened the string
        public int requires;
        public Link(int input, bool sp, string followUp, bool openerOnly, int requires)
        { this.input = input; this.sp = sp; this.followUp = followUp; this.openerOnly = openerOnly; this.requires = requires; }
    }

    // Bjorn. Light is the main opener with three branches; its Heavy branch is the filmed
    // light -> 360 overhead, which is why that ender is the dedicated SpinOverhead asset.
    static readonly (string attack, Link[] links)[] Bjorn =
    {
        ("Bjorn_Light_Slash", new[]
        {
            new Link(H, false, "Bjorn_Heavy_SpinOverhead",       true,  Always),
            new Link(M, false, "Bjorn_Medium_SpinningSlash",     true,  Always),
            new Link(M, true,  "Bjorn_Special_HewingCharge",     false, HitOrBlock),
        }),
        ("Bjorn_Medium_SpinningSlash", new[]
        {
            new Link(L, false, "Bjorn_Light_Slash",              true,  Always),
            new Link(H, true,  "Bjorn_Special_BloodEagle",       false, HitOrBlock),
        }),
        ("Bjorn_Heavy_OverheadDoubleAxe", new[]
        {
            new Link(M, false, "Bjorn_Medium_SpinningSlash",     true,  Always),
            new Link(L, true,  "Bjorn_Special_HaftStrike",       false, HitOrBlock),
        }),
        // The spin ender can itself be capped with a special, which is what makes
        // Light -> Heavy -> SP+Light a three-hit string.
        ("Bjorn_Heavy_SpinOverhead", new[]
        {
            new Link(L, true,  "Bjorn_Special_HaftStrike",       false, HitOrBlock),
        }),
        // A riposte replaces the normal strike inside a counter window, so without a link of its
        // own a counter could no longer open a string - which it could before ripostes existed.
        ("Bjorn_Riposte_Dash",        new[] { new Link(L, false, "Bjorn_Light_Slash",             true, Always) }),
        ("Bjorn_Riposte_Parry",       new[] { new Link(L, false, "Bjorn_Light_Slash",             true, Always) }),
        ("Bjorn_Special_HaftStrike",   new[] { new Link(L, false, "Bjorn_Light_Slash",             true, Always) }),
        ("Bjorn_Special_HewingCharge", new[] { new Link(H, false, "Bjorn_Heavy_OverheadDoubleAxe", true, Always) }),
        ("Bjorn_Special_BloodEagle",   new[] { new Link(L, false, "Bjorn_Light_Slash",             true, Always) }),
    };

    // Miyamoto. The three plain links are the strings verified on 2026-10-18; the specials are new.
    static readonly (string attack, Link[] links)[] Miyamoto =
    {
        ("Miyamoto_Light_Thrust", new[]
        {
            new Link(M, false, "Miyamoto_Medium_SideSlash",      true,  Always),
            new Link(H, true,  "Miyamoto_Special_Iaijutsu",      false, HitOrBlock),
        }),
        ("Miyamoto_Medium_SideSlash", new[]
        {
            new Link(L, false, "Miyamoto_Light_Thrust",          true,  Always),
            new Link(H, false, "Miyamoto_Heavy_Overhead",        true,  Always),
            new Link(L, true,  "Miyamoto_Special_Nukitsuke",     false, HitOrBlock),
        }),
        ("Miyamoto_Heavy_Overhead", new[]
        {
            new Link(L, false, "Miyamoto_Light_Thrust",          true,  Always),
            new Link(M, true,  "Miyamoto_Special_NitoCross",     false, HitOrBlock),
        }),
        ("Miyamoto_Riposte_Dash",     new[] { new Link(L, false, "Miyamoto_Light_Thrust",     true, Always) }),
        ("Miyamoto_Riposte_Parry",    new[] { new Link(L, false, "Miyamoto_Light_Thrust",     true, Always) }),
        ("Miyamoto_Special_Nukitsuke", new[] { new Link(M, false, "Miyamoto_Medium_SideSlash", true, Always) }),
        ("Miyamoto_Special_NitoCross", new[] { new Link(L, false, "Miyamoto_Light_Thrust",     true, Always) }),
        ("Miyamoto_Special_Iaijutsu",  new[] { new Link(L, false, "Miyamoto_Light_Thrust",     true, Always) }),
    };


    // Richard. The tank opens with the thrust or the cross slash; every three-hit string is paid
    // for by landing the two normals first, and each special hands him back a normal so a counter
    // can keep going.
    static readonly (string attack, Link[] links)[] Richard =
    {
        ("Richard_Light_Thrust", new[]
        {
            new Link(M, false, "Richard_Medium_Slash",          true,  Always),
            new Link(H, true,  "Richard_Special_DoubleHigh",    false, HitOrBlock),
        }),
        ("Richard_Medium_Slash", new[]
        {
            new Link(H, false, "Richard_Heavy_Overhead",        true,  Always),
            new Link(M, true,  "Richard_Special_ShieldBash",    false, HitOrBlock),
        }),
        ("Richard_Heavy_Overhead", new[]
        {
            new Link(L, false, "Richard_Light_Thrust",          true,  Always),
            new Link(L, true,  "Richard_Special_ShieldCharge",  false, HitOrBlock),
        }),
        ("Richard_Riposte_Dash",           new[] { new Link(L, false, "Richard_Light_Thrust", true, Always) }),
        ("Richard_Riposte_Parry",          new[] { new Link(L, false, "Richard_Light_Thrust", true, Always) }),
        ("Richard_Special_ShieldCharge",   new[] { new Link(M, false, "Richard_Medium_Slash", true, Always) }),
        ("Richard_Special_ShieldBash",     new[] { new Link(L, false, "Richard_Light_Thrust", true, Always) }),
        ("Richard_Special_DoubleHigh",     new[] { new Link(L, false, "Richard_Light_Thrust", true, Always) }),
    };

    // Salaadin. Same three shapes, but every special he can dial is a kick or a turn rather than a
    // shield blow: the light buys the kick, the medium buys the spin, the heavy buys the rush, and
    // each special hands a normal back so a riposte can keep going.
    static readonly (string attack, Link[] links)[] Salaadin =
    {
        ("Salaadin_Light_SaifCut", new[]
        {
            new Link(M, false, "Salaadin_Medium_CrescentSlash", true,  Always),
            new Link(M, true,  "Salaadin_Special_BreakingKick", false, HitOrBlock),
        }),
        ("Salaadin_Medium_CrescentSlash", new[]
        {
            new Link(H, false, "Salaadin_Heavy_HattinCleave",   true,  Always),
            new Link(H, true,  "Salaadin_Special_WhirlingSaif", false, HitOrBlock),
        }),
        ("Salaadin_Heavy_HattinCleave", new[]
        {
            new Link(L, false, "Salaadin_Light_SaifCut",        true,  Always),
            new Link(L, true,  "Salaadin_Special_VeiledRush",   false, HitOrBlock),
        }),
        ("Salaadin_Riposte_Dash",          new[] { new Link(L, false, "Salaadin_Light_SaifCut", true, Always) }),
        ("Salaadin_Riposte_Parry",         new[] { new Link(L, false, "Salaadin_Light_SaifCut", true, Always) }),
        ("Salaadin_Special_VeiledRush",    new[] { new Link(M, false, "Salaadin_Medium_CrescentSlash", true, Always) }),
        ("Salaadin_Special_BreakingKick",  new[] { new Link(L, false, "Salaadin_Light_SaifCut", true, Always) }),
        ("Salaadin_Special_WhirlingSaif",  new[] { new Link(L, false, "Salaadin_Light_SaifCut", true, Always) }),
    };

    [MenuItem("Crucible/Combat/Wire combo strings")]
    public static void Wire()
    {
        var all = LoadAttacks(null);
        int written = 0;
        foreach (var table in new[] { Bjorn, Miyamoto, Richard, Salaadin })
            foreach (var (attackName, links) in table)
            {
                if (!all.TryGetValue(attackName, out var attack)) { Debug.LogError($"no AttackData named {attackName}"); continue; }
                var so = new SerializedObject(attack);
                var arr = so.FindProperty("comboLinks");
                arr.arraySize = links.Length;
                for (int i = 0; i < links.Length; i++)
                {
                    var l = links[i];
                    if (!all.TryGetValue(l.followUp, out var followUp)) { Debug.LogError($"{attackName}: no follow-up named {l.followUp}"); continue; }
                    var e = arr.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("input").enumValueIndex = l.input;
                    e.FindPropertyRelative("withSpecialModifier").boolValue = l.sp;
                    e.FindPropertyRelative("followUp").objectReferenceValue = followUp;
                    e.FindPropertyRelative("requires").enumValueIndex = l.requires;
                    e.FindPropertyRelative("openerOnly").boolValue = l.openerOnly;
                }
                so.ApplyModifiedProperties();
                written++;
                Debug.Log($"{attackName}: {links.Length} link(s) - " + string.Join(", ",
                    links.Select(l => Press(l.input, l.sp) + " -> " + l.followUp + (l.openerOnly ? " [opener]" : ""))));
            }

        AssetDatabase.SaveAssets();
        Debug.Log($"Combo strings wired onto {written} attacks. Run 'Report combo strings' to list what they reach.");
    }

    /// <summary>
    /// Walks the live assets and prints every string a player can actually dial, so the tables
    /// and the authored data can be checked against each other without launching the game.
    /// </summary>
    [MenuItem("Crucible/Combat/Report combo strings")]
    public static void Report()
    {
        foreach (var (dir, who) in new[] { ("Assets/_project/Characters/Bjorn/Attacks", "Bjorn"),
                                           ("Assets/_project/Characters/Miyamoto/Attacks", "Miyamoto"),
                                           ("Assets/_project/Characters/Richard/Attacks", "Richard") })
        {
            var lines = new List<string>();
            foreach (var opener in LoadAttacks(dir).Values.OrderBy(a => a.name))
            {
                // Only a move the player can press for is a real opener: a string ender such as
                // SpinOverhead lives outside the fighter's libraries and is reached by a cancel.
                if (!IsPressable(opener)) continue;
                Walk(opener, Label(opener), 0, lines, IsSpecial(opener));
            }
            Debug.Log($"{who}: {lines.Count} strings\n  " + string.Join("\n  ", lines));
        }
    }

    static Dictionary<string, ScriptableObject> LoadAttacks(string dir)
    {
        var guids = dir == null ? AssetDatabase.FindAssets("t:AttackData")
                                : AssetDatabase.FindAssets("t:AttackData", new[] { dir });
        var map = new Dictionary<string, ScriptableObject>();
        foreach (var guid in guids)
        {
            var a = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) map[a.name] = a;
        }
        return map;
    }

    static void Walk(ScriptableObject attack, string path, int depth, List<string> into, bool usedSpecial = false)
    {
        var arr = new SerializedObject(attack).FindProperty("comboLinks");
        if (arr == null) return;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            var followUp = e.FindPropertyRelative("followUp").objectReferenceValue as ScriptableObject;
            if (followUp == null) continue;
            if (e.FindPropertyRelative("openerOnly").boolValue && depth > 0) continue;
            // The engine allows one special per string (CombatSystem.stringUsedSpecial).
            if (usedSpecial && IsSpecial(followUp)) continue;
            string next = path + " -" + Press(e.FindPropertyRelative("input").enumValueIndex,
                                              e.FindPropertyRelative("withSpecialModifier").boolValue)
                               + "-> " + Label(followUp);
            into.Add(next);
            if (depth + 2 < 3) Walk(followUp, next, depth + 1, into, usedSpecial || IsSpecial(followUp));
        }
    }

    static bool IsPressable(ScriptableObject attack)
    {
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb.GetType().Name != "CombatSystem") continue;
            var so = new SerializedObject(mb);
            foreach (var field in new[] { "attacks", "specials" })
            {
                var arr = so.FindProperty(field);
                if (arr == null) continue;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == attack) return true;
            }
        }
        return false;
    }

    static bool IsSpecial(ScriptableObject attack) =>
        new SerializedObject(attack).FindProperty("attackType").enumValueIndex == 3;

    static string Label(ScriptableObject attack)
    {
        var so = new SerializedObject(attack);
        string name = so.FindProperty("attackName").stringValue;
        return so.FindProperty("attackType").enumValueIndex == 3 ? "[" + name + "]" : name;
    }

    static string Press(int input, bool sp) =>
        (sp ? "SP+" : "") + (input == 0 ? "Light" : input == 1 ? "Medium" : input == 2 ? "Heavy" : "Special");
}
