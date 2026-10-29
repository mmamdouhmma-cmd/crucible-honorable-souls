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
//   Salaadin  the combo fighter: LOWEST damage per blow (x0.85) paid back in string freedom
//             (see CrucibleCombos - most branches, improvises mid-string, instant hit-confirm
//             cancels), armour between Bjorn and Richard, the biggest stamina pool, and a round
//             shield that blocks 75%
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
        // Plan_Balance 2.5 / item 6: the two axes that make a COUNTER fighter possible. Dash
        // i-frames are COVERAGE of that fighter's own dash (iframes / dashDuration), kept clearly
        // under 100% so every dash has a punishable tail. Parry was identical on all four before
        // this table existed - and writing it here SETTLES the old 1.5-vs-1.35 counter damage and
        // 1.0-vs-0.5 counter window discrepancies that were never decided.
        public float dashIFrames;
        public float parryWindow, parryCooldown, parryStun, parryCounterWindow, parryCounterDamage;
        public int parryCost;
        // A story fighter lives on Player2 of ONE test scene; its vitals are written to the scene
        // only there, and the roster fighter on the same player name is skipped there.
        public string onlyScene;
    }

    public const string SenseiDataPath = "Assets/_project/Data/Fighters/sensei_data.asset";
    public static float SenseiDashDuration => 0.24f * DashTimeScale;

    static readonly Vitals[] Fighters =
    {
        new Vitals { character = "Miyamoto", player = "Player2", fighterName = "Miyamoto", archetype = "Duelist",
                     dataPath = "Assets/_project/Data/Fighters/miyamoto_data.asset",
                     // moveSpeed 44 -> 48 (Plan_Balance 2.2): his old 7% walk lead over Salaadin
                     // was under the threshold anyone feels. At 48 the lead is 17%, and a fast
                     // walk is a COUNTER fighter's defensive tool - walking in and out of range is
                     // how he baits the whiffs he punishes.
                     // armor 8 -> 13 (user's hand tune in the scene, 2026-10-24)
                     health = 80, stamina = 100, armor = 13, damageMultiplier = 92, moveSpeed = 48,
                     regen = 20f, dashDistance = 2.6f, dashDuration = 0.22f, dashStamina = 12,
                     blockReduction = 60, blockDrain = 0f,
                     // The COUNTER fighter: 82% dash coverage and the only x1.5 riposte - his
                     // damage lives here, not in neutral. Parry window 0.2 s / cooldown 0.8 s
                     // ROSTER-WIDE (the user's call, 2026-10-25, killing the block-spam parry:
                     // the guard must go up just before contact, and attempts are paced).
                     dashIFrames = 0.18f,
                     parryWindow = 0.2f, parryCost = 8, parryCooldown = 0.8f, parryStun = 0.5f,
                     parryCounterWindow = 1f, parryCounterDamage = 1.5f },

        new Vitals { character = "Bjorn", player = "Player1", fighterName = "Bjorn", archetype = "Berserker",
                     dataPath = "Assets/_project/Data/Fighters/bjorn_data.asset",
                     health = 95, stamina = 110, armor = 14, damageMultiplier = 110, moveSpeed = 38,
                     regen = 20f, dashDistance = 2.2f, dashDuration = 0.30f, dashStamina = 18,   // regen flattened to 20 across the roster (user, 2026-10-23)
                     // 80 -> 68 (Plan_Balance 2.4): aggression should not also carry the
                     // second-best guard in the game.
                     blockReduction = 68, blockDrain = 0f,
                     // 47% dash coverage; the dearest parry - defence is not his game on any
                     // axis. (Window 0.2 / cooldown 0.8 roster-wide, user 2026-10-25.)
                     dashIFrames = 0.14f,
                     parryWindow = 0.2f, parryCost = 10, parryCooldown = 0.8f, parryStun = 0.5f,
                     parryCounterWindow = 0.8f, parryCounterDamage = 1.35f },

        // The wall: the shield makes his guard the strongest per hit, the small tank makes it short.
        new Vitals { character = "Richard", player = "Player3", fighterName = "Richard", archetype = "Tank",
                     dataPath = "Assets/_project/Data/Fighters/richard_data.asset",
                     // armor 22 -> 20 (user's hand tune in the scene, 2026-10-24)
                     health = 115, stamina = 85, armor = 20, damageMultiplier = 100, moveSpeed = 32,
                     regen = 20f, dashDistance = 2.0f, dashDuration = 0.34f, dashStamina = 16,
                     blockReduction = 85, blockDrain = 0f,
                     // 35% dash coverage - the wall blocks, he does not slip. Base parry.
                     dashIFrames = 0.12f,
                     parryWindow = 0.2f, parryCost = 8, parryCooldown = 0.8f, parryStun = 0.5f,
                     parryCounterWindow = 0.8f, parryCounterDamage = 1.35f },

        // The middle of the roster on every axis: Miyamoto's damage scalar on a frame, armour
        // halfway between Bjorn and Richard, and the most stamina of the four.
        new Vitals { character = "Salaadin", player = "Player4", fighterName = "Salaadin", archetype = "Champion",
                     dataPath = "Assets/_project/Data/Fighters/salaadin_data.asset",
                     // damageMultiplier 92 -> 85 (Plan_Balance 2.2, the DEFERRED cut, landed
                     // 2026-10-23 in the same pass as his combo freedom - the dependency in
                     // section 5): the roster's damage order is now Bjorn 110 > Richard 100 >
                     // Miyamoto 92 > Salaadin 85, and he and Miyamoto stop sharing x0.92. His
                     // damage now lives in the STRINGS, not the single blow.
                     // armor 18 -> 17 (user's hand tune in the scene, 2026-10-24)
                     health = 95, stamina = 115, armor = 17, damageMultiplier = 85, moveSpeed = 41,
                     regen = 20f, dashDistance = 2.3f, dashDuration = 0.26f, dashStamina = 15,
                     // 82 -> 75 (Plan_Balance 2.4): between Richard's 85 and Bjorn's 68, so the
                     // round shield still reads but the wall is Richard's alone.
                     blockReduction = 75, blockDrain = 0f,
                     // 62% dash coverage (second to Miyamoto), base parry everywhere else.
                     dashIFrames = 0.16f,
                     parryWindow = 0.2f, parryCost = 8, parryCooldown = 0.8f, parryStun = 0.5f,
                     parryCounterWindow = 0.8f, parryCounterDamage = 1.35f },

        // The PARRY-COUNTER tank (the user's brief, 2026-10-25): a tank below Richard - less
        // health, far less block - but faster (walk 3.6 against his 3.2), hitting about as hard
        // (x1.00). His one crown is the parry punish: counter damage x1.7, the roster's highest
        // (Miyamoto's is 1.5), paid through a 12-damage riposte - the great-sword wheel.
        new Vitals { character = "Genghis", player = "Player5", fighterName = "Genghis", archetype = "Warlord",
                     dataPath = "Assets/_project/Data/Fighters/genghis_data.asset",
                     health = 105, stamina = 95, armor = 18, damageMultiplier = 100, moveSpeed = 36,
                     regen = 20f, dashDistance = 2.1f, dashDuration = 0.30f, dashStamina = 16,
                     // "lower block": a two-handed saber is no shield - between Miyamoto's 60 and
                     // Bjorn's 68, on a TANK, so his defence has to be the parry.
                     blockReduction = 62, blockDrain = 0f,
                     // 43% dash coverage. The counter window runs longer than base so the wheel
                     // (36 f + the stun maths) always fits inside it.
                     dashIFrames = 0.13f,
                     parryWindow = 0.2f, parryCost = 8, parryCooldown = 0.8f, parryStun = 0.5f,
                     parryCounterWindow = 1.2f, parryCounterDamage = 1.7f },

        // The RANGE fighter (the user's brief, 2026-10-25): the spear out-reaches every weapon in
        // the roster (hitbox ranges 3.3-4.0, LungeOverride keeps him at the spear's length), and
        // everything else is deliberately moderate - block Bjorn's 68, walk between Bjorn's 3.8
        // and Salaadin's 4.1, base parry.
        new Vitals { character = "Buwaka", player = "Player6", fighterName = "Buwaka", archetype = "Hunter",
                     dataPath = "Assets/_project/Data/Fighters/buwaka_data.asset",
                     health = 100, stamina = 100, armor = 15, damageMultiplier = 95, moveSpeed = 40,
                     regen = 20f, dashDistance = 2.3f, dashDuration = 0.28f, dashStamina = 14,
                     blockReduction = 68, blockDrain = 0f,
                     // 54% dash coverage - mid-pack, like the rest of him.
                     dashIFrames = 0.15f,
                     parryWindow = 0.2f, parryCost = 8, parryCooldown = 0.8f, parryStun = 0.5f,
                     parryCounterWindow = 0.8f, parryCounterDamage = 1.35f },

        // The story SENSEI (Chapter One; the user's brief, 2026-10-29): "somewhere between Miyamoto
        // and Salaadin" on speed, damage, armour and block, "but lower health than both" - 75,
        // under Miyamoto's 80. Every number below sits between those two rows.
        new Vitals { character = "Ch1_Sensei", player = "Player2", fighterName = "Sensei", archetype = "Master",
                     dataPath = SenseiDataPath, onlyScene = "Ch1_Test_Sensei",
                     health = 75, stamina = 108, armor = 15, damageMultiplier = 88, moveSpeed = 44,
                     regen = 20f, dashDistance = 2.45f, dashDuration = 0.24f, dashStamina = 13,
                     blockReduction = 68, blockDrain = 0f,
                     dashIFrames = 0.17f,
                     parryWindow = 0.2f, parryCost = 8, parryCooldown = 0.8f, parryStun = 0.5f,
                     parryCounterWindow = 0.9f, parryCounterDamage = 1.42f },
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
            // Startups below are the user's own hand tune in the editor (copied in 2026-10-27).
            N("Miyamoto_Light_Thrust",                 7,  14,  8,      7,  3,  5,   7),
            N("Miyamoto_Medium_SideSlash",             7,  16,  10,      8,  4,  6,   8),
            N("Miyamoto_Heavy_Overhead",               9,  18,  16,     10,  4,  9,  10),   // was 23 start: the slowest heavy in the game, on the fastest fighter
            // Special stamina across the ROSTER cut 15% on 2026-10-23 (the user's call): every
            // SP slot's cost is the old figure x0.85, rounded.
            N("Miyamoto_Special_Nukitsuke",           13,  33,  18,     10,  4,  7,  12),   // SP+Light: was 18/5/20 = 0.72s. The user's call - too slow, and it is the pack's slowest strike (peak tip 0.286) so the clip could not carry that budget either
            N("Miyamoto_Special_NitoCross",           14,  37,  20,     10,  5, 12,  12),
            // SP+Heavy. The 30-frame draw WAS deliberate and the user's verdict is that it does not
            // play: 0.50s of windup before the first active frame. Kuruma (the spin charge handed
            // down from the normal heavy) takes the slot at 0.53s total.
            N("Miyamoto_Special_Kuruma",              15,  41, 24,     10,  4,  8,  14),
            // The double thrust, now reachable ONLY as the last hit of a string, so it needs no
            // tell - a confirmed finisher is paid for by the hit that opened the cancel window.
            N("Miyamoto_Special_Iaijutsu",            15,  41, 24,     10,  5,  9,  14),   // all four specials cut about a quarter on the user's call: 30/47/32/36 frames -> 22/34/24/27
            N("Miyamoto_Riposte_Dash",                 9,  12,  8,     10,  4, 11,   9),
            N("Miyamoto_Riposte_Parry",                8,  12,  8,     16,  4,  5,   9),
        },

        ["Bjorn"] = new[]
        {
            N("Bjorn_Light_Slash",                     8,  20,  0,     10,  4,  8,   8),
            N("Bjorn_Medium_SpinningSlash",           10,  23,  2,     18,  5, 12,   9),   // was 23 start
            N("Bjorn_Heavy_OverheadDoubleAxe",        13,  26,  5,     18,  5, 15,  11),   // was 15 start: the fastest heavy, on the slow fighter
            N("Bjorn_Heavy_SpinOverhead",             13,  29,  5,     28,  4, 14,  11),
            N("Bjorn_Special_HaftStrike",             15,  41,  5,     18,  5, 20,  13),
            N("Bjorn_Special_HewingCharge",           16,  47,  5,     22,  5, 22,  13),
            N("Bjorn_Special_BloodEagle",             18,  53, 10,     24,  5, 24,  15),
            N("Bjorn_Riposte_Dash",                    9,  16,  0,     12,  4, 14,   9),
            N("Bjorn_Riposte_Parry",                   8,  16,  0,     12,  4, 14,   9),
        },

        ["Richard"] = new[]
        {
            N("Richard_Light_Thrust",                  8,  17,  5,     11,  3,  9,   7),
            N("Richard_Medium_Slash",                  9,  21,  2,     13,  4, 11,   9),
            N("Richard_Heavy_Overhead",               13,  24,  8,     22,  5, 17,  11),
            N("Richard_Special_ShieldCharge",         14,  37, 10,     11,  4, 15,  13),   // the three specials run a little longer than the normals whose clips they play
            N("Richard_Special_ShieldBash",           15,  42, 12,     15,  5, 14,  13),
            N("Richard_Special_DoubleHigh",           16,  47, 10,     22,  5, 17,  14),   // 44 f, the same total as the Heavy, so the window maths match it exactly
            N("Richard_Riposte_Dash",                  9,  14,  0,     12,  4, 14,   9),
            N("Richard_Riposte_Parry",                 8,  14,  0,     12,  4, 14,   9),
        },

        // Every start-up sits between Miyamoto's and Bjorn's; the damage is Miyamoto's, a point
        // dearer on the medium and heavy because they are slower than his.
        // His medium and heavy traded moves with SP + Medium and SP + Heavy; the numbers did NOT
        // go with them. A row here is a SLOT, so a normal still costs a normal's stamina however
        // showy the swing that now sits in it.
        ["Salaadin"] = new[]
        {                                          // dmg stam pierce  start act rec  hitstun
            N("Salaadin_Light_SaifCut",                7,  16,  0,      8,  3,  7,   7),
            N("Salaadin_Medium_ShinCut",               8,  18,  2,     12,  4,  9,   8),
            N("Salaadin_Heavy_WhirlingSaif",          10,  22,  5,     17,  4, 13,  10),
            N("Salaadin_Special_VeiledRush",          13,  35,  5,     20,  5, 18,  12),
            N("Salaadin_Special_CrescentSlash",       14,  39,  8,     20,  5, 19,  13),
            N("Salaadin_Special_HattinCleave",        16,  44, 10,     26,  5, 20,  14),
            N("Salaadin_Riposte_Dash",                 9,  14,  0,     12,  4, 14,   9),
            N("Salaadin_Riposte_Parry",                8,  14,  0,     12,  4, 14,   9),
        },

        // Genghis: Richard's damage on quicker frames (startups between Miyamoto's and Richard's),
        // paid for with a thinner guard. The RIPOSTE_PARRY row is the point of him: 12 damage at
        // his x1.7 counter multiplier is a 20-point punish, the roster's hardest single answer.
        ["Genghis"] = new[]
        {                                          // dmg stam pierce  start act rec  hitstun
            N("Genghis_Light_SteppeCut",               7,  17,  4,     10,  3,  8,   7),
            N("Genghis_Medium_HordeSlash",             9,  20,  5,     14,  4, 10,   9),
            N("Genghis_Heavy_KhanCleave",             11,  24,  8,     21,  5, 16,  11),
            N("Genghis_Special_SteppeStorm",          14,  36, 10,     14,  4, 15,  13),
            N("Genghis_Special_HordeBreaker",         15,  41, 12,     17,  5, 16,  13),
            N("Genghis_Special_WrathOfTengri",        17,  46, 14,     20,  5, 19,  14),
            N("Genghis_Riposte_Dash",                  9,  14,  4,     12,  4, 13,   9),
            N("Genghis_Riposte_Parry",                12,  16, 10,     14,  5, 17,   9),
        },

        // Buwaka: moderate numbers on the longest reach. The damage order stays
        // Bjorn 110 > Richard = Genghis 100 > Buwaka 95 > Miyamoto 92 > Salaadin 85.
        ["Buwaka"] = new[]
        {                                          // dmg stam pierce  start act rec  hitstun
            N("Buwaka_Light_HornCut",                  7,  16,  2,     10,  3,  8,   7),
            N("Buwaka_Medium_SavannaCut",              8,  19,  3,     13,  4, 10,   8),
            N("Buwaka_Heavy_IklwaThrust",             10,  23,  8,     18,  4, 14,  10),
            N("Buwaka_Special_SerpentSweep",          13,  35,  6,     18,  5, 17,  12),
            N("Buwaka_Special_FallingSpear",          14,  40,  8,     20,  5, 18,  13),
            N("Buwaka_Special_GreatImpale",           16,  45, 12,     25,  5, 20,  14),
            // The combo-only ender (string: thrust -> Falling Spear -> this). Medium-classed so
            // the one-special rule never refuses it; costs like a heavy, hits between his
            // Medium and his specials - the string multiplier (x1.2 on the 3rd hit) tops it up.
            N("Buwaka_Ender_SerpentCoil",             13,  26,  6,     15,  5, 16,  12),
            N("Buwaka_Riposte_Dash",                   9,  14,  2,     12,  4, 14,   9),
            N("Buwaka_Riposte_Parry",                  8,  14,  0,     12,  4, 14,   9),
        },

        // The Sensei: each slot between Miyamoto's and Salaadin's (their effective frames, after
        // the x2 and Salaadin's hand-set UserFrames). Asset prefix "Sensei_", folder Ch1_Sensei.
        ["Ch1_Sensei"] = new[]
        {                                          // dmg stam pierce  start act rec  hitstun
            N("Sensei_Light_FallingBranch",            7,  15,  4,      8,  3,  6,   7),
            N("Sensei_Medium_TempleBell",              8,  17,  6,      9,  4,  8,   8),
            N("Sensei_Heavy_MountainSplitter",        10,  20, 10,     11,  4, 11,  10),
            N("Sensei_Special_RisingHeaven",          13,  34, 10,     15,  5, 16,  12),
            N("Sensei_Special_CircleOfWillow",        14,  38, 12,     15,  5, 15,  13),
            N("Sensei_Special_FinalLesson",           15,  42, 14,     16,  5, 17,  14),
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
        so.FindProperty("movementSpeed").intValue = Mathf.RoundToInt(v.moveSpeed * WalkSpeedScale);
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
        bool userSet = UserFrames.TryGetValue(m.asset, out var uf);
        float charSpeed = CharacterSpeedUpFor(m.asset);
        so.FindProperty("startupFrames").intValue = userSet ? Mathf.RoundToInt(uf.startup / charSpeed) : Mathf.RoundToInt(m.startup * FrameScaleFor(m.asset));
        so.FindProperty("activeFrames").intValue = m.active;
        so.FindProperty("recoveryFrames").intValue = userSet ? Mathf.RoundToInt(uf.recovery / charSpeed) : Mathf.RoundToInt(m.recovery * FrameScaleFor(m.asset));
        // UNIFORM HITSTUN (the user's call, 2026-10-24): every stun is 0.5 s, overriding the
        // per-move hitstun columns, which stay in the rows for the day this is per-move again.
        so.FindProperty("hitstunFrames").intValue = UniformHitstunFrames;
        if (LungeOverride.TryGetValue(m.asset, out var lg))
        {
            so.FindProperty("lungeStopDistance").floatValue = lg.stop;
            so.FindProperty("lungeMaxDistance").floatValue = lg.max;
        }
        so.FindProperty("hyperArmor").boolValue = HyperArmor.Contains(m.asset);
        so.FindProperty("swingFlow").enumValueIndex = (int)FlowFor(m.asset, so.FindProperty("hitSide").enumValueIndex);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
    }

    /// <summary>
    /// The swing's geometry for the free-form flow rule (the user's call, 2026-10-24: in quick
    /// succession no geometry may repeat back to back - a right cut may be followed by a left cut,
    /// a spin, a thrust or an overhead, never by another right cut). Classified by what each move
    /// actually does on screen; anything not listed falls back to its hitSide (Straight ->
    /// Overhead, Left/Right -> that side's cut).
    /// This editor assembly cannot see the game's types, so SwingFlow is MIRRORED here by index -
    /// keep it in step with the enum in AttackData.cs (append-only there, append-only here).
    /// </summary>
    enum SwingFlow { Overhead, Thrust, CutLeft, CutRight, SpinLeft, SpinRight, OverheadLeft, OverheadRight }

    static readonly System.Collections.Generic.Dictionary<string, SwingFlow> Flow =
        new System.Collections.Generic.Dictionary<string, SwingFlow>
    {
        // Bjorn: one-handed cuts, two true spins, a chop riposte.
        { "Bjorn_Light_Slash",            SwingFlow.CutLeft },
        { "Bjorn_Medium_SpinningSlash",   SwingFlow.SpinLeft },
        { "Bjorn_Heavy_OverheadDoubleAxe",SwingFlow.Overhead },
        { "Bjorn_Heavy_SpinOverhead",     SwingFlow.SpinRight },
        { "Bjorn_Special_BloodEagle",     SwingFlow.Overhead },   // the leaping overhead
        { "Bjorn_Special_HaftStrike",     SwingFlow.CutLeft },    // the front cut (was the backhand until 2026-10-27)
        { "Bjorn_Special_HewingCharge",   SwingFlow.CutLeft },
        { "Bjorn_Riposte_Dash",           SwingFlow.CutLeft },
        { "Bjorn_Riposte_Parry",          SwingFlow.Overhead },   // the chop

        // Miyamoto: thrust, side cut, overhead; the specials are draw cuts and spins.
        { "Miyamoto_Light_Thrust",        SwingFlow.Thrust },
        { "Miyamoto_Medium_SideSlash",    SwingFlow.CutLeft },
        { "Miyamoto_Heavy_Overhead",      SwingFlow.Overhead },
        { "Miyamoto_Special_Iaijutsu",    SwingFlow.CutRight },   // the draw cut sweeps out right
        { "Miyamoto_Special_Kuruma",      SwingFlow.SpinRight },  // 100 deg pelvis wheel
        { "Miyamoto_Special_NitoCross",   SwingFlow.SpinLeft },   // 356 deg pelvis
        { "Miyamoto_Special_Nukitsuke",   SwingFlow.CutLeft },
        { "Miyamoto_Riposte_Dash",        SwingFlow.CutRight },
        { "Miyamoto_Riposte_Parry",       SwingFlow.SpinRight },  // the spinning cut

        // Richard: his Light plays the barbarian downward attack, so it IS an overhead.
        { "Richard_Light_Thrust",         SwingFlow.Overhead },
        { "Richard_Medium_Slash",         SwingFlow.CutLeft },
        { "Richard_Heavy_Overhead",       SwingFlow.Overhead },
        { "Richard_Special_DoubleHigh",   SwingFlow.CutRight },   // the paired high cuts
        { "Richard_Special_ShieldBash",   SwingFlow.Thrust },     // a forward shove
        { "Richard_Special_ShieldCharge", SwingFlow.Thrust },     // the running slam
        { "Richard_Riposte_Dash",         SwingFlow.CutLeft },
        { "Richard_Riposte_Parry",        SwingFlow.CutLeft },    // the sword counter

        // Salaadin: the combo fighter gets alternating sides on his normals, a whirling heavy.
        { "Salaadin_Light_SaifCut",       SwingFlow.CutLeft },    // the flat sweep
        { "Salaadin_Medium_ShinCut",      SwingFlow.CutRight },   // the low forehand
        { "Salaadin_Heavy_WhirlingSaif",  SwingFlow.SpinRight },
        { "Salaadin_Special_CrescentSlash", SwingFlow.CutRight },
        // Diagonal: `sword and shield attack` sweeps 86 deg of yaw coming down from his right,
        // so the cleave flows straight into his right-hand spin (the Whirling Saif) - the one
        // overhead->spin pairing the roster owns. The dead-vertical overheads stay plain
        // Overhead and flow into NO spin.
        { "Salaadin_Special_HattinCleave",  SwingFlow.OverheadRight },
        { "Salaadin_Special_VeiledRush",    SwingFlow.Thrust },   // the rush
        { "Salaadin_Riposte_Dash",        SwingFlow.CutLeft },
        { "Salaadin_Riposte_Parry",       SwingFlow.CutRight },   // the saber counter

        // Genghis: alternating sides on the normals, the wheel on the parry answer.
        { "Genghis_Light_SteppeCut",      SwingFlow.CutRight },   // from the left, travels right
        { "Genghis_Medium_HordeSlash",    SwingFlow.CutLeft },
        { "Genghis_Heavy_KhanCleave",     SwingFlow.Overhead },
        { "Genghis_Special_SteppeStorm",  SwingFlow.CutRight },
        { "Genghis_Special_HordeBreaker", SwingFlow.CutLeft },
        { "Genghis_Special_WrathOfTengri",SwingFlow.Overhead },
        { "Genghis_Riposte_Dash",         SwingFlow.CutRight },   // his Light's cut
        { "Genghis_Riposte_Parry",        SwingFlow.SpinRight },  // the wheel

        // Buwaka: a thrusting heavy, a true 360 on SP+Light.
        { "Buwaka_Light_HornCut",         SwingFlow.Thrust },     // the quick jab (2026-10-26 re-point)
        { "Buwaka_Medium_SavannaCut",     SwingFlow.CutLeft },
        { "Buwaka_Heavy_IklwaThrust",     SwingFlow.Thrust },
        { "Buwaka_Special_SerpentSweep",  SwingFlow.SpinRight },  // the full low turn
        { "Buwaka_Special_FallingSpear",  SwingFlow.OverheadRight },  // the TURNING overhead (86 deg) - pairs with SerpentSweep's SpinRight, like Salaadin's Hattin
        { "Buwaka_Special_GreatImpale",   SwingFlow.Thrust },
        { "Buwaka_Ender_SerpentCoil",     SwingFlow.SpinLeft },   // measured: the clip turns left through its main swing
        { "Buwaka_Riposte_Dash",          SwingFlow.CutLeft },
        { "Buwaka_Riposte_Parry",         SwingFlow.Thrust },     // the spear
        // The Sensei: each move takes the flow of the move whose animation it borrows.
        { "Sensei_Light_FallingBranch",     SwingFlow.Overhead },   // Richard's Heavy
        { "Sensei_Medium_TempleBell",       SwingFlow.SpinRight },  // Salaadin's Heavy (his row's label)
        { "Sensei_Heavy_MountainSplitter",  SwingFlow.Overhead },   // Bjorn's Heavy
        { "Sensei_Special_RisingHeaven",    SwingFlow.Overhead },   // Genghis's SP+Heavy
        { "Sensei_Special_CircleOfWillow",  SwingFlow.CutRight },   // Salaadin's SP+Medium
        { "Sensei_Special_FinalLesson",     SwingFlow.Overhead },
    };

    static SwingFlow FlowFor(string asset, int hitSideIndex)
    {
        if (Flow.TryGetValue(asset, out var flow)) return flow;
        switch (hitSideIndex)   // HitSide: 0 Straight, 1 Left, 2 Right
        {
            case 1: return SwingFlow.CutLeft;
            case 2: return SwingFlow.CutRight;
            default: return SwingFlow.Overhead;
        }
    }

    /// <summary>
    /// The SP+Heavy slot on every fighter powers through hits: the wind-up is long enough that any
    /// light could interrupt it, so the move takes the damage and keeps swinging (the user's call,
    /// 2026-10-23). A parry still beats it outright. Written to AttackData.hyperArmor, false on
    /// everything else, so the table stays the one source of truth.
    /// </summary>
    static readonly System.Collections.Generic.HashSet<string> HyperArmor =
        new System.Collections.Generic.HashSet<string>
    {
        "Miyamoto_Special_Kuruma",
        "Bjorn_Special_BloodEagle",
        "Richard_Special_DoubleHigh",
        "Salaadin_Special_HattinCleave",
        "Genghis_Special_WrathOfTengri",
        "Buwaka_Special_GreatImpale",
        "Sensei_Special_FinalLesson",
    };

    /// <summary>
    /// Moves that have to CLOSE rather than reach. The default lunge stops at
    /// BalanceConfig.ATTACK_LUNGE_STOP (1.35 centre to centre) and may travel
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
        // Buwaka is the opposite case: the spear must KEEP its distance rather than close. The
        // default stop (1.7) would walk him inside his own point; these hold him at the spear's
        // length, and the Great Impale alone drives all the way in.
        { "Buwaka_Light_HornCut",        (2.3f, 0.9f) },
        { "Buwaka_Medium_SavannaCut",    (2.3f, 0.9f) },
        { "Buwaka_Heavy_IklwaThrust",    (2.6f, 1.0f) },
        { "Buwaka_Special_SerpentSweep", (2.2f, 1.2f) },
        { "Buwaka_Special_FallingSpear", (2.4f, 1.2f) },
        { "Buwaka_Special_GreatImpale",  (2.2f, 2.0f) },
        { "Buwaka_Riposte_Dash",         (2.3f, 0.9f) },
        // Richard's Lion Riposte sat exactly on the trigger boundary after the 2026-10-26 reach
        // tightening (closest blade approach 0.20 from a dodge's 1.5 gap - it landed one run and
        // whiffed the next). A closer stop converts the marginal graze into a clean hit.
        { "Richard_Riposte_Dash",        (1.1f, 0.9f) },
        // SP+HEAVY steps further (the user's call, 2026-10-26: "heavy special attacks should land
        // from little further distance"). Default stop, lunge max 0.9 -> 1.5, which the distance
        // sweep measured as ~+0.6 of landing range. The lunge speed cap (ATTACK_LUNGE_MAX_SPEED 4
        // x (startup + active/2)/60) allows it on all four: 1.63-1.90. The two absentees are
        // CAPPED, not forgotten: Buwaka_Special_GreatImpale already carries (2.2, 2.0) above and
        // its 25 f startup caps the step at 1.83 (he is the roster's longest SP+Heavy at 4.6
        // anyway), and Miyamoto_Special_Kuruma's 8 f startup caps his at 0.67 - a longer Kuruma
        // step needs more startup frames, not a bigger number here.
        { "Bjorn_Special_BloodEagle",     (1.35f, 1.5f) },
        { "Richard_Special_DoubleHigh",   (1.35f, 1.5f) },
        { "Salaadin_Special_HattinCleave",(1.35f, 1.5f) },
        { "Genghis_Special_WrathOfTengri",(1.35f, 1.5f) },
        // Bjorn's SP+Light (the user's call, 2026-10-27: it barely landed). The step in his other
        // specials take; the speed cap (18 f startup) allows ~1.37 of it. With the front cut and
        // the 15% bigger axe it lands from 3.4 (the old backhand reached 2.4).
        { "Bjorn_Special_HaftStrike",     (1.35f, 1.5f) },
        // The Sensei's SP+Heavy steps like everyone's; his SP+Light keeps Genghis's step with the clip.
        { "Sensei_Special_FinalLesson",   (1.35f, 1.5f) },
        { "Sensei_Special_RisingHeaven",  (1.35f, 1.5f) },
    };

    // Every get-hit stun is 0.8 s (48 frames at 60fps) - the user's call, 2026-10-24, raising the
    // morning's 0.5. Parry stun stays at parryStun in the vitals; the wrong-dodge penalty stun is
    // BalanceConfig.COMBO_WRONG_DODGE_STUN_FRAMES (60).
    const int UniformHitstunFrames = 48;

    // SLOWER, WEIGHTIER FIGHTING (the user's call, 2026-10-27, "for testing a few things in the editor"):
    // every move's startup and recovery are written at x2 of the numbers in the table above, and every
    // dash takes x2 as long over the same ground (its i-frames x2 too, so the same SHARE of it evades).
    // Active frames, hitstun, parry timing and the run are untouched. The table keeps the old numbers,
    // so setting these back to 1 and re-running Apply + Use Mixamo packs restores the old game exactly.
    // Use Mixamo packs plays the SAME clip segment over the longer budget, so the swings slow down too -
    // most of them were running at 1.4-3.8x their capture pace and now sit near it.
    public const float AttackFrameScale = 2f;
    // Dashes 20% FASTER than that (the user's call, 2026-10-27, after testing the x2): same ground in
    // 2/1.2 the old doubled time, i-frames shrinking with it so the same share of the dash evades.
    public const float DashTimeScale = 2f / 1.2f;
    // Specials 25% FASTER than that (the user's call, same day): startup and recovery x0.8 on top of
    // AttackFrameScale, so the move takes 1/1.25 the time and its clip plays 1.25x faster (ComputeTrims
    // cuts the same segment). Miyamoto's SP+Light (Nukitsuke) is left out, also the user's call.
    public const float SpecialSpeedUp = 1.25f;
    static readonly HashSet<string> SpecialSpeedUpExempt = new HashSet<string> { "Miyamoto_Special_Nukitsuke" };

    /// <summary>EXACT startup / recovery the user set by hand in the editor (2026-10-27, after the x2 and
    /// the special speed-up), written verbatim - no scale applies. They are not multiples of the table's
    /// numbers, so they live here instead of being folded back into the rows. Delete an entry to hand
    /// the move back to the table x FrameScaleFor.</summary>
    public static readonly Dictionary<string, (int startup, int recovery)> UserFrames = new Dictionary<string, (int, int)>
    {
        { "Bjorn_Medium_SpinningSlash", (26, 24) },
        { "Bjorn_Heavy_OverheadDoubleAxe", (27, 30) },
        { "Bjorn_Heavy_SpinOverhead", (27, 28) },
        { "Bjorn_Special_HaftStrike", (25, 32) },
        { "Bjorn_Special_HewingCharge", (25, 35) },
        { "Bjorn_Special_BloodEagle", (28, 38) },
        { "Richard_Heavy_Overhead", (30, 34) },
        { "Richard_Special_DoubleHigh", (27, 27) },
        { "Salaadin_Medium_ShinCut", (22, 18) },
        { "Salaadin_Heavy_WhirlingSaif", (25, 26) },
        { "Salaadin_Special_VeiledRush", (25, 29) },
        { "Salaadin_Special_CrescentSlash", (26, 30) },
        { "Salaadin_Special_HattinCleave", (26, 32) },
        { "Genghis_Special_WrathOfTengri", (25, 30) },
        { "Buwaka_Special_SerpentSweep", (30, 32) },
        { "Buwaka_Special_FallingSpear", (30, 29) },
        { "Buwaka_Special_GreatImpale", (30, 28) },
    };

    /// <summary>A move's frame budget as the TABLE row has it (startup + active + recovery, no scale), or
    /// -1 when the move has no row. ComputeTrims cuts the animation window from this, so any stretch,
    /// speed-up or hand-set frame count plays the same clip segment faster or slower.</summary>
    public static int TableTotal(string asset)
    {
        foreach (var rows in Moves.Values)
            foreach (var m in rows)
                if (m.asset == asset) return m.startup + m.active + m.recovery;
        return -1;
    }

    /// <summary>The factor a move's TABLE startup and recovery are written at. Also read by
    /// CrucibleMixamoSetup.ComputeTrims, so the animation window is cut from the table's budget.</summary>
    public static float FrameScaleFor(string asset) =>
        AttackFrameScale * (asset.Contains("_Special_") && !SpecialSpeedUpExempt.Contains(asset) ? 1f / SpecialSpeedUp : 1f)
        / CharacterSpeedUpFor(asset);

    /// <summary>Per-fighter attack speed on top of everything above (the user's call, 2026-10-27):
    /// every move of that fighter - normals, specials, enders, and the hand-set UserFrames too - has
    /// its startup and recovery divided by the factor, and its clip plays that much faster (ComputeTrims
    /// cuts the same segment).</summary>
    public static readonly Dictionary<string, float> CharacterSpeedUp = new Dictionary<string, float>
    {
        { "Bjorn",   1.05f },
        { "Richard", 1.185f },
        { "Genghis", 1.185f },
    };
    static float CharacterSpeedUpFor(string asset)
    {
        int cut = asset.IndexOf('_');
        return cut > 0 && CharacterSpeedUp.TryGetValue(asset.Substring(0, cut), out float f) ? f : 1f;
    }
    // Walk speed x0.5, same order across the roster (same day). At x1 every walk clip covers only
    // 1.0-1.75 world units/s of ground on these bodies (measured: planted-foot speed, walk in place),
    // and the old 3.2-4.8 u/s skated them across the arena at 2-4x their legs. Use Mixamo packs fits
    // each walk's playback to the new ground speed (CrucibleMixamoSetup.WalkStride).
    // Raised 20% to x0.6 after testing (the user's call, 2026-10-27).
    public const float WalkSpeedScale = 0.6f;

    // His art folder was created lowercase; every other fighter's matches the character name.
    static string Folder(string character) => character == "Salaadin" ? "salaadin" : character;

    static void WriteScene(Vitals v)
    {
        string scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name;
        if (v.onlyScene != null && v.onlyScene != scene) return;
        if (v.onlyScene == null && System.Array.Exists(Fighters, o => o.onlyScene == scene && o.player == v.player)) return;
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
        so.FindProperty("dashDuration").floatValue = v.dashDuration * DashTimeScale;
        // the run kept its old pace: it used to BE the dash speed, so pin it to the table's dash
        so.FindProperty("runSpeed").floatValue = v.dashDistance / v.dashDuration;
        so.FindProperty("dashStaminaCost").intValue = v.dashStamina;
        so.FindProperty("blockDamageReduction").intValue = v.blockReduction;
        so.FindProperty("blockStaminaDrainPerSecond").floatValue = v.blockDrain;
        so.FindProperty("dashIFrameDuration").floatValue = v.dashIFrames * DashTimeScale;
        so.FindProperty("parryWindow").floatValue = v.parryWindow;
        so.FindProperty("parryStaminaCost").intValue = v.parryCost;
        so.FindProperty("parryCooldown").floatValue = v.parryCooldown;
        so.FindProperty("parryStunDuration").floatValue = v.parryStun;
        so.FindProperty("parryCounterWindow").floatValue = v.parryCounterWindow;
        so.FindProperty("parryCounterDamageMultiplier").floatValue = v.parryCounterDamage;
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
