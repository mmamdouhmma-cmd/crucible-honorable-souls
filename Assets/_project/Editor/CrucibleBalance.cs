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
//             Richard, the biggest stamina pool, and a round shield that blocks 75%
//
// Block spread (Plan_Balance 2.4, 2026-10-23): 85 / 75 / 68 / 60 so "can I afford to turtle" is
// a different question per fighter - it was 85 / 82 / 80 / 60, three of four within five points.
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
                     // moveSpeed 44 -> 48 (Plan_Balance 2.2): his old 7% walk lead over Salaadin
                     // was under the threshold anyone feels. At 48 the lead is 17%, and a fast
                     // walk is a COUNTER fighter's defensive tool - walking in and out of range is
                     // how he baits the whiffs he punishes.
                     health = 80, stamina = 100, armor = 8, damageMultiplier = 92, moveSpeed = 48,
                     regen = 20f, dashDistance = 2.6f, dashDuration = 0.22f, dashStamina = 12,
                     blockReduction = 60, blockDrain = 3f },

        new Vitals { character = "Bjorn", player = "Player1", fighterName = "Bjorn", archetype = "Berserker",
                     dataPath = "Assets/_project/Data/Fighters/bjorn_data.asset",
                     health = 95, stamina = 110, armor = 14, damageMultiplier = 110, moveSpeed = 38,
                     regen = 17f, dashDistance = 2.2f, dashDuration = 0.30f, dashStamina = 18,
                     // 80 -> 68 (Plan_Balance 2.4): aggression should not also carry the
                     // second-best guard in the game.
                     blockReduction = 68, blockDrain = 3f },

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
                     // 82 -> 75 (Plan_Balance 2.4): between Richard's 85 and Bjorn's 68, so the
                     // round shield still reads but the wall is Richard's alone.
                     blockReduction = 75, blockDrain = 2.5f },
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
            // Miyamoto's ARMOUR PIERCE, raised across the board on the user's call. Pierce is a
            // PERCENTAGE off the defender's armour (DamageCalculator step 2), and his normals were
            // the only ones in the roster sitting at 0 - the katana fighter pierced LEAST, which is
            // backwards. He is the pierce leader on every slot now; Richard's best is 12.
            // EVERY Miyamoto move is quicker, on the user's call - normals and ripostes this time,
            // not just the specials. About 17-19% off each total, taken out of startup and recovery
            // with every ACTIVE window left exactly as it was, so nothing starts whiffing. A budget
            // change also re-times the clip (Use Mixamo packs sets state.speed = clip.length /
            // (frames/60)), so each of these animations now plays faster on screen too.
            N("Miyamoto_Light_Thrust",                 7,  12,  8,      5,  3,  5,   7),
            N("Miyamoto_Medium_SideSlash",             7,  14,  10,      7,  4,  6,   8),
            N("Miyamoto_Heavy_Overhead",               9,  16,  16,     13,  4,  9,  10),   // was 23 start: the slowest heavy in the game, on the fastest fighter
            N("Miyamoto_Special_Nukitsuke",           13,  34,  18,      7,  4,  7,  12),   // SP+Light: was 18/5/20 = 0.72s. The user's call - too slow, and it is the pack's slowest strike (peak tip 0.286) so the clip could not carry that budget either
            N("Miyamoto_Special_NitoCross",           14,  38,  20,     11,  5, 12,  12),
            // SP+Heavy. The 30-frame draw WAS deliberate and the user's verdict is that it does not
            // play: 0.50s of windup before the first active frame. Kuruma (the spin charge handed
            // down from the normal heavy) takes the slot at 0.53s total.
            N("Miyamoto_Special_Kuruma",              15,  42, 24,      8,  4,  8,  14),
            // The double thrust, now reachable ONLY as the last hit of a string, so it needs no
            // tell - a confirmed finisher is paid for by the hit that opened the cancel window.
            N("Miyamoto_Special_Iaijutsu",            15,  42, 24,      8,  5,  9,  14),   // all four specials cut about a quarter on the user's call: 30/47/32/36 frames -> 22/34/24/27
            N("Miyamoto_Riposte_Dash",                 9,  10,  8,     10,  4, 11,   9),
            N("Miyamoto_Riposte_Parry",                8,  10,  8,     16,  4,  5,   9),
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
            N("Richard_Special_ShieldCharge",         14,  38, 10,     11,  4, 15,  13),   // the three specials run a little longer than the normals whose clips they play
            N("Richard_Special_ShieldBash",           15,  43, 12,     15,  5, 14,  13),
            N("Richard_Special_DoubleHigh",           16,  48, 10,     22,  5, 17,  14),   // 44 f, the same total as the Heavy, so the window maths match it exactly
            N("Richard_Riposte_Dash",                  9,  12,  0,     12,  4, 14,   9),
            N("Richard_Riposte_Parry",                 8,  12,  0,     12,  4, 14,   9),
        },

        // Every start-up sits between Miyamoto's and Bjorn's; the damage is Miyamoto's, a point
        // dearer on the medium and heavy because they are slower than his.
        // His medium and heavy traded moves with SP + Medium and SP + Heavy; the numbers did NOT
        // go with them. A row here is a SLOT, so a normal still costs a normal's stamina however
        // showy the swing that now sits in it.
        ["Salaadin"] = new[]
        {                                          // dmg stam pierce  start act rec  hitstun
            N("Salaadin_Light_SaifCut",                7,  14,  0,      8,  3,  7,   7),
            N("Salaadin_Medium_ShinCut",               8,  16,  2,     12,  4,  9,   8),
            N("Salaadin_Heavy_WhirlingSaif",          10,  19,  5,     17,  4, 13,  10),
            N("Salaadin_Special_VeiledRush",          13,  36,  5,     20,  5, 18,  12),
            N("Salaadin_Special_CrescentSlash",       14,  40,  8,     20,  5, 19,  13),
            N("Salaadin_Special_HattinCleave",        16,  45, 10,     26,  5, 20,  14),
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
        if (LungeOverride.TryGetValue(m.asset, out var lg))
        {
            so.FindProperty("lungeStopDistance").floatValue = lg.stop;
            so.FindProperty("lungeMaxDistance").floatValue = lg.max;
        }
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
    }

    /// <summary>
    /// Moves that have to CLOSE rather than reach. The default lunge stops at
    /// BalanceConfig.ATTACK_LUNGE_STOP (1.7 centre to centre) and may travel
    /// BalanceConfig.ATTACK_LUNGE_MAX (0.9) to get there, which is fine for a move that strikes
    /// out in front - but Bjorn's two SPINS carry the axe round his body, so at 1.7 apart the head
    /// is nowhere near the opponent while the hitbox (range 3.2-3.5) still scores. The axe missing
    /// visibly while damage lands is that gap, and the fix the game wants is for the spin to travel
    /// THROUGH the man rather than for the hitbox to shrink. stop 0.9 is inside the sum of the two
    /// bodies, so he ends the turn overlapping and the head sweeps through.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<string, (float stop, float max)> LungeOverride =
        new System.Collections.Generic.Dictionary<string, (float, float)>
    {
        { "Bjorn_Medium_SpinningSlash", (0.9f, 2.6f) },
        { "Bjorn_Heavy_SpinOverhead",   (0.9f, 2.6f) },
    };

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
