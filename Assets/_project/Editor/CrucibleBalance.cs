// The whole roster's numbers, in one table.
//
// Crucible > Combat > Apply the roster balance
//
// Writes three things from the tables below:
//   1. each fighter's FighterData asset (health, stamina, armour, damage scalar, walk speed),
//   2. every AttackData's damage, stamina cost, armour pierce, frames and hitstun,
//   3. the Fighter components in the open scene (vitals, stamina regen, dash, block).
//
// The archetypes it encodes (user's call, 2026-10-20):
//   Miyamoto  fastest, cheapest moves, least damage, least health, mid stamina
//   Bjorn     highest damage, mid health, biggest stamina pool but the costliest moves
//   Richard   most health, lowest stamina, damage between the two, slowest of the three;
//             his shield pays him back with the best block (85%) and the lowest hold drain
//   Salaadin  the balanced one: Miyamoto's damage at a slower swing, armour between Bjorn and
//             Richard, the biggest stamina pool, and a round shield that blocks 82%
//
// Frames are the ONLY attack-speed lever: FighterData.AttackSpeed and FighterData.DashDistance
// are dead fields that nothing reads. Changing a frame count re-times that clip, so run
// 'Crucible > Animation > Use Mixamo packs' afterwards to refit the animations to the new windows.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CrucibleBalance
{
    // ------------------------------------------------------------------ the fighters
    struct Vitals
    {
        public string character, dataPath, player, fighterName, archetype;
        public int health, stamina, armor, damageMultiplier, moveSpeed;
        public float regen, dashDistance, dashDuration;
        public int dashStamina, blockReduction;
        public float blockDrain;
    }

    static readonly Vitals[] Fighters =
    {
        new Vitals { character = "Miyamoto", player = "Player2", fighterName = "Miyamoto", archetype = "Duelist",
                     dataPath = "Assets/_project/Data/Fighters/miyamoto_data.asset",
                     health = 80, stamina = 100, armor = 8, damageMultiplier = 92, moveSpeed = 44,
                     regen = 20f, dashDistance = 2.6f, dashDuration = 0.22f, dashStamina = 12,
                     blockReduction = 60, blockDrain = 3f },

        new Vitals { character = "Bjorn", player = "Player1", fighterName = "Bjorn", archetype = "Berserker",
                     dataPath = "Assets/_project/Data/Fighters/bjorn_data.asset",
                     health = 95, stamina = 110, armor = 14, damageMultiplier = 110, moveSpeed = 38,
                     regen = 17f, dashDistance = 2.2f, dashDuration = 0.30f, dashStamina = 18,
                     blockReduction = 80, blockDrain = 3f },

        // The wall: the shield makes his guard the strongest per hit, the small tank makes it short.
        new Vitals { character = "Richard", player = "Player3", fighterName = "Richard", archetype = "Tank",
                     dataPath = "Assets/_project/Data/Fighters/richard_data.asset",
                     health = 115, stamina = 85, armor = 22, damageMultiplier = 100, moveSpeed = 32,
                     regen = 14f, dashDistance = 2.0f, dashDuration = 0.34f, dashStamina = 16,
                     blockReduction = 85, blockDrain = 2f },

        // The middle of the roster on every axis: Miyamoto's damage scalar on a frame, armour
        // halfway between Bjorn and Richard, and the most stamina of the four.
        new Vitals { character = "Salaadin", player = "Player4", fighterName = "Salaadin", archetype = "Champion",
                     dataPath = "Assets/_project/Data/Fighters/salaadin_data.asset",
                     health = 95, stamina = 115, armor = 18, damageMultiplier = 92, moveSpeed = 41,
                     regen = 16f, dashDistance = 2.3f, dashDuration = 0.26f, dashStamina = 15,
                     blockReduction = 82, blockDrain = 2.5f },
    };

    // ------------------------------------------------------------------ the moves
    struct Numbers
    {
        public string asset;
        public int damage, stamina, pierce, startup, active, recovery, hitstun;
    }

    static Numbers N(string asset, int dmg, int stam, int pierce, int s, int a, int r, int hitstun) =>
        new Numbers { asset = asset, damage = dmg, stamina = stam, pierce = pierce,
                      startup = s, active = a, recovery = r, hitstun = hitstun };

    // Startup ladders across the roster: light 6 / 10 / 11, medium 9 / 18 / 15, heavy 16 / 18 / 22.
    // (Bjorn's medium is a spin and his heavy a double-axe drop, so his two swap places.)
    static readonly Dictionary<string, Numbers[]> Moves = new Dictionary<string, Numbers[]>
    {
        ["Miyamoto"] = new[]
        {                                          // dmg stam pierce  start act rec  hitstun
            N("Miyamoto_Light_Thrust",                 7,  12,  0,      6,  3,  6,   7),
            N("Miyamoto_Medium_SideSlash",             7,  14,  0,      9,  4,  8,   8),
            N("Miyamoto_Heavy_Overhead",               9,  16,  0,     16,  4, 12,  10),   // was 23 start: the slowest heavy in the game, on the fastest fighter
            N("Miyamoto_Special_Nukitsuke",           13,  34,  5,     18,  5, 20,  12),
            N("Miyamoto_Special_NitoCross",           14,  38,  5,     20,  5, 22,  12),
            N("Miyamoto_Special_Iaijutsu",            15,  42, 10,     30,  4,  8,  14),   // the draw is slow on purpose
            N("Miyamoto_Riposte_Dash",                 9,  10,  0,     12,  4, 14,   9),
            N("Miyamoto_Riposte_Parry",                8,  10,  0,     20,  4,  6,   9),
        },

        ["Bjorn"] = new[]
        {
            N("Bjorn_Light_Slash",                     8,  17,  0,     10,  4,  8,   8),
            N("Bjorn_Medium_SpinningSlash",           10,  20,  2,     18,  5, 12,   9),   // was 23 start
            N("Bjorn_Heavy_OverheadDoubleAxe",        13,  23,  5,     18,  5, 15,  11),   // was 15 start: the fastest heavy, on the slow fighter
            N("Bjorn_Heavy_SpinOverhead",             13,  25,  5,     28,  4, 14,  11),
            N("Bjorn_Special_HaftStrike",             15,  42,  5,     18,  5, 20,  13),
            N("Bjorn_Special_HewingCharge",           16,  48,  5,     22,  5, 22,  13),
            N("Bjorn_Special_BloodEagle",             18,  54, 10,     24,  5, 24,  15),
            N("Bjorn_Riposte_Dash",                    9,  14,  0,     12,  4, 14,   9),
            N("Bjorn_Riposte_Parry",                   8,  14,  0,     12,  4, 14,   9),
        },

        ["Richard"] = new[]
        {
            N("Richard_Light_Thrust",                  7,  15,  5,     11,  3,  9,   7),
            N("Richard_Medium_Slash",                  9,  18,  2,     15,  4, 11,   9),
            N("Richard_Heavy_Overhead",               11,  21,  8,     22,  5, 17,  11),
            N("Richard_Special_ShieldCharge",         14,  38, 10,     24,  5, 18,  13),
            N("Richard_Special_ShieldBash",           15,  43, 12,     22,  5, 20,  13),
            N("Richard_Special_DoubleHigh",           16,  48, 10,     30,  5, 20,  14),
            N("Richard_Riposte_Dash",                  9,  12,  0,     12,  4, 14,   9),
            N("Richard_Riposte_Parry",                 8,  12,  0,     12,  4, 14,   9),
        },

        // Every start-up sits between Miyamoto's and Bjorn's; the damage is Miyamoto's, a point
        // dearer on the medium and heavy because they are slower than his.
        ["Salaadin"] = new[]
        {                                          // dmg stam pierce  start act rec  hitstun
            N("Salaadin_Light_SaifCut",                7,  14,  0,      8,  3,  7,   7),
            N("Salaadin_Medium_CrescentSlash",         8,  16,  2,     12,  4,  9,   8),
            N("Salaadin_Heavy_HattinCleave",          10,  19,  5,     17,  4, 13,  10),
            N("Salaadin_Special_VeiledRush",          13,  36,  5,     20,  5, 18,  12),
            N("Salaadin_Special_BreakingKick",        14,  40,  8,     20,  5, 19,  13),
            N("Salaadin_Special_WhirlingSaif",        16,  45, 10,     26,  5, 20,  14),
            N("Salaadin_Riposte_Dash",                 9,  12,  0,     12,  4, 14,   9),
            N("Salaadin_Riposte_Parry",                8,  12,  0,     12,  4, 14,   9),
        },
    };

    [MenuItem("Crucible/Combat/Apply the roster balance")]
    public static void Apply()
    {
        int moves = 0;
        foreach (var v in Fighters)
        {
            WriteData(v);
            foreach (var m in Moves[v.character]) { WriteMove(v.character, m); moves++; }
            WriteScene(v);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Roster balance applied: {Fighters.Length} fighters, {moves} moves. " +
                  "Frames changed, so run 'Crucible > Animation > Use Mixamo packs' to refit the clips, " +
                  "then save the scene.");
    }

    static void WriteData(Vitals v)
    {
        var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(v.dataPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance("FighterData");
            AssetDatabase.CreateAsset(data, v.dataPath);
        }
        var so = new SerializedObject(data);
        so.FindProperty("fighterName").stringValue = v.fighterName;
        so.FindProperty("archetype").stringValue = v.archetype;
        so.FindProperty("maxHealth").intValue = v.health;
        so.FindProperty("maxStamina").intValue = v.stamina;
        so.FindProperty("armor").intValue = v.armor;
        so.FindProperty("damageMultiplier").intValue = v.damageMultiplier;
        so.FindProperty("movementSpeed").intValue = v.moveSpeed;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(data);
    }

    static void WriteMove(string character, Numbers m)
    {
        string path = $"Assets/_project/Characters/{Folder(character)}/Attacks/{m.asset}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        if (asset == null) { Debug.LogWarning($"balance: {path} not found"); return; }
        var so = new SerializedObject(asset);
        so.FindProperty("damage").intValue = m.damage;
        so.FindProperty("staminaCost").intValue = m.stamina;
        so.FindProperty("armorPierce").intValue = m.pierce;
        so.FindProperty("startupFrames").intValue = m.startup;
        so.FindProperty("activeFrames").intValue = m.active;
        so.FindProperty("recoveryFrames").intValue = m.recovery;
        so.FindProperty("hitstunFrames").intValue = m.hitstun;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
    }

    // His art folder was created lowercase; every other fighter's matches the character name.
    static string Folder(string character) => character == "Salaadin" ? "salaadin" : character;

    static void WriteScene(Vitals v)
    {
        var go = GameObject.Find(v.player);
        if (go == null) { Debug.LogWarning($"balance: {v.player} not in the open scene"); return; }
        var fighter = go.GetComponent(System.Type.GetType("Fighter, Assembly-CSharp"));
        if (fighter == null) { Debug.LogWarning($"balance: {v.player} has no Fighter"); return; }
        var so = new SerializedObject(fighter);
        so.FindProperty("fighterData").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<ScriptableObject>(v.dataPath);
        so.FindProperty("maxHealth").intValue = v.health;
        so.FindProperty("maxStamina").intValue = v.stamina;
        so.FindProperty("armor").intValue = v.armor;
        so.FindProperty("staminaRecoveryPerSecond").floatValue = v.regen;
        so.FindProperty("dashDistance").floatValue = v.dashDistance;
        so.FindProperty("dashDuration").floatValue = v.dashDuration;
        so.FindProperty("dashStaminaCost").intValue = v.dashStamina;
        so.FindProperty("blockDamageReduction").intValue = v.blockReduction;
        so.FindProperty("blockStaminaDrainPerSecond").floatValue = v.blockDrain;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(fighter);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
    }

    /// <summary>Prints the table as it stands on the live assets, for a sanity read.</summary>
    [MenuItem("Crucible/Combat/Report the roster balance")]
    public static void Report()
    {
        foreach (var v in Fighters)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{v.character} ({v.archetype}): {v.health} hp, {v.stamina} stamina, {v.armor}% armour, " +
                          $"damage x{v.damageMultiplier / 100f:0.00}, walk {v.moveSpeed / 10f:0.0} u/s, " +
                          $"regen {v.regen:0}/s, dash {v.dashDistance:0.0}u in {v.dashDuration:0.00}s ({v.dashStamina} st), " +
                          $"block {v.blockReduction}% at {v.blockDrain:0}/s");
            foreach (var m in Moves[v.character])
            {
                float scaled = m.damage * v.damageMultiplier / 100f;
                sb.AppendLine($"  {m.asset,-34} {m.damage,3} dmg (x{v.damageMultiplier / 100f:0.00} = {scaled,5:0.0})  " +
                              $"{m.stamina,3} st  pierce {m.pierce,2}  {m.startup,2}/{m.active}/{m.recovery,2} f  " +
                              $"hitstun {m.hitstun}");
            }
            Debug.Log(sb.ToString());
        }
    }
}
