// Richard the Lionheart - the third fighter: sword and kite shield, the roster's tank.
//
// Crucible > Characters > Richard > 1 Create move set   writes his AttackData + FighterData assets
// Crucible > Characters > Richard > 2 Build controller  copies P2_Miyamoto into P3_Richard, renaming
//                                                        every state to his move names
// Crucible > Characters > Richard > 3 Add to the scene  clones Player2 into Player3, points it at his
//                                                        assets, and rebuilds the body from his FBX
//
// Run them in order on a fresh clone, then the shared menu items finish the job:
//   Crucible > Animation > Wire special and riposte states
//   Crucible > Animation > Use Mixamo packs          (his clips: the Pro Sword and Shield pack)
//   Crucible > Combat    > Wire combo strings
//
// The table below is the SHAPE of each move - its name, trigger, height, which dash evades it,
// its hitbox and its aim. The numbers (damage, stamina, frames, hitstun) live in
// CrucibleBalance with the other two fighters, so the archetypes can be read side by side.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleRichard
{
    const string Dir = "Assets/_project/Characters/Richard";
    const string AttackDir = Dir + "/Attacks";
    const string DataPath = "Assets/_project/Data/Fighters/richard_data.asset";
    const string P2Ctrl = "Assets/_project/Animation/Controllers/P2_Miyamoto.controller";
    const string P3Ctrl = "Assets/_project/Animation/Controllers/P3_Richard.controller";

    // AttackHeight / AttackType / AttackOrigin / HitSide / AimMode, as the enum order serializes them
    const int High = 0, Mid = 1, Low = 2, Overhead = 3;
    const int Light = 0, Medium = 1, Heavy = 2, Special = 3;
    const int FromOverhead = 0, FromLeft = 1, FromRight = 2, FromThrust = 3;
    const int HitStraight = 0, HitLeft = 1, HitRight = 2;
    const int AimFull = 0, AimYaw = 1, AimOff = 2, AimLevel = 3;

    struct Move
    {
        public string asset, name, trigger;
        public int height, type, specialButton, comesFrom, hitSide, aim;
        public float range, lunge;
        public Vector3 size, offset;
        public bool weapon;
    }

    // Slower than Miyamoto, heavier than Bjorn's lighter swings, and the only fighter whose guard is
    // a shield: his normals trade start-up for damage and hitstun, his specials for armour pierce.
    static readonly Move[] Moves =
    {
        // LIGHT: an overhead chop out of the BARBARIAN pack (AxePack "standing melee attack
        // downward"), the user's pick on 2026-10-23 in place of the authored Lion Thrust. 93 deg of
        // UNWRAPPED yaw so it is a plain cut and not a turn, tip arc 1.37, and grip_spread 0.97 -
        // the hands stay APART, which is the whole reason a one-handed axe clip is safe on a man
        // carrying a shield (every GreatSwordPack clip sits at 0.11-0.20, both hands on the hilt,
        // and would drag his shield arm onto the sword). The geometry follows the new swing:
        // Overhead/FromOverhead so a dash over the top evades it, HitStraight for the straight
        // reaction, and a taller, narrower box than the Heavy's. The ASSET name is kept - the
        // scene holds it by GUID.
        new Move { asset = "Richard_Light_Thrust", name = "Lion Chop", trigger = "LightAttack",
                   height = Overhead, type = Light, comesFrom = FromOverhead, hitSide = HitStraight, aim = AimFull,
                   range = 3.0f, size = new Vector3(0.9f, 1.4f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        new Move { asset = "Richard_Medium_Slash", name = "Cross Slash", trigger = "MediumAttack",
                   height = Mid, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 2.9f, size = new Vector3(1.3f, 1.1f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        new Move { asset = "Richard_Heavy_Overhead", name = "Crusader Hew", trigger = "HeavyAttack",
                   height = Overhead, type = Heavy, comesFrom = FromOverhead, hitSide = HitStraight, aim = AimYaw,
                   range = 3.1f, size = new Vector3(1.0f, 1.7f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // SP + Medium and SP + Heavy play their OWN NORMAL's animation, so each takes that
        // normal's hit geometry too - a box that does not match the swing on screen is the thing
        // that reads as unfair. SP + Light no longer does: as of 2026-10-23 it has a clip of its
        // own (the barbarian backhand), because sharing the Light's clip left nothing on screen to
        // tell the two apart but the camera shake. They stay specials in every other respect:
        // damage, stamina, and the heavier shake FightCamera gives an unblocked Special.
        // The ASSET names are kept so the scene's GUID references survive.
        // SP + Light: a REVERSE slash out of the BARBARIAN pack (AxePack "standing melee attack
        // backhand"). 245 deg of turn - a real pivot but not a full circle - the highest tip arc of
        // anything on offer at 1.40, and foot^ -0.72, so the feet stay PLANTED: it reads as a cut
        // coming back across him rather than a charge. AimLevel, not AimFull: a turning swing aimed
        // Full points the tip DOWN at the opponent's chest from a raised hand, which is exactly
        // what read as a spinning back FIST on Kuruma and the Nito Cross. Level flattens the
        // desired direction to the horizon first. FromLeft/HitRight because it returns from his
        // off side. The ASSET name is kept for the scene's GUID.
        new Move { asset = "Richard_Special_ShieldCharge", name = "Lionheart Reversal", trigger = "SpecialLight",
                   height = Mid, type = Special, specialButton = Light, comesFrom = FromLeft, hitSide = HitRight,
                   aim = AimLevel, range = 3.2f, size = new Vector3(1.4f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.0f, weapon = true },

        // SP + Medium: his Cross Slash (SwordShieldPack/slash (5)).
        new Move { asset = "Richard_Special_ShieldBash", name = "Crusader Cross", trigger = "SpecialMedium",
                   height = Mid, type = Special, specialButton = Medium, comesFrom = FromRight, hitSide = HitLeft,
                   aim = AimFull, range = 3.0f, size = new Vector3(1.3f, 1.1f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.2f, weapon = true },

        // SP + Heavy: his Crusader Hew (SwordShieldPack/slash), the pack's true overhead.
        new Move { asset = "Richard_Special_DoubleHigh", name = "Judgement", trigger = "SpecialHeavy",
                   height = Overhead, type = Special, specialButton = Heavy, comesFrom = FromOverhead, hitSide = HitStraight,
                   aim = AimYaw, range = 3.1f, size = new Vector3(1.0f, 1.7f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.2f, weapon = true },

        new Move { asset = "Richard_Riposte_Dash", name = "Lion Riposte", trigger = "RiposteDash",
                   height = Mid, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 2.9f, size = new Vector3(1.2f, 1.1f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // A sword counter (it was a boot until 2026-10-27 - the user's call: no kicks on anyone).
        new Move { asset = "Richard_Riposte_Parry", name = "Crusader's Answer", trigger = "RiposteParry",
                   height = Mid, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 2.9f, size = new Vector3(1.2f, 1.1f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },
    };

    [MenuItem("Crucible/Characters/Richard/1 Create move set")]
    public static void CreateMoves()
    {
        if (!AssetDatabase.IsValidFolder(AttackDir)) AssetDatabase.CreateFolder(Dir, "Attacks");
        foreach (var m in Moves)
        {
            string path = $"{AttackDir}/{m.asset}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance("AttackData");
                AssetDatabase.CreateAsset(asset, path);
            }
            var so = new SerializedObject(asset);
            so.FindProperty("attackName").stringValue = m.name;
            so.FindProperty("animatorTriggerName").stringValue = m.trigger;
            so.FindProperty("attackHeight").enumValueIndex = m.height;
            so.FindProperty("attackType").enumValueIndex = m.type;
            so.FindProperty("specialButton").enumValueIndex = m.specialButton;
            so.FindProperty("comesFrom").enumValueIndex = m.comesFrom;
            so.FindProperty("hitSide").enumValueIndex = m.hitSide;
            so.FindProperty("hitboxRange").floatValue = m.range;
            so.FindProperty("hitboxSize").vector3Value = m.size;
            so.FindProperty("hitboxOffset").vector3Value = m.offset;
            so.FindProperty("aimMode").enumValueIndex = m.aim;
            so.FindProperty("lungeMaxDistance").floatValue = m.lunge;
            so.FindProperty("hitsWithWeapon").boolValue = m.weapon;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
        }

        // Damage, stamina cost, frames, hitstun and his FighterData are NOT written here: they
        // are the roster-wide table in CrucibleBalance, so the three archetypes stay comparable.
        CrucibleBalance.Apply();

        AssetDatabase.SaveAssets();
        Debug.Log($"Richard: {Moves.Length} attacks written to {AttackDir}; numbers from CrucibleBalance.");
    }

    // Miyamoto's controller is the template: same states, same transitions, his names.
    static readonly (string from, string to)[] StateRenames =
    {
        ("Miyamoto_Idle",               "Richard_Idle"),
        ("Miyamoto_Walk_Forward",       "Richard_Walk_Forward"),
        ("Miyamoto_Walk_Back",          "Richard_Walk_Back"),
        ("Miyamoto_Light_Thrust",       "Richard_Light_Thrust"),
        ("Miyamoto_Medium_SideSlash",   "Richard_Medium_Slash"),
        ("Miyamoto_Heavy_Overhead",     "Richard_Heavy_Overhead"),
        ("Miyamoto_Riposte_Dash",       "Richard_Riposte_Dash"),
        ("Miyamoto_Riposte_Parry",      "Richard_Riposte_Parry"),
        ("Miyamoto_Special_Nukitsuke",  "Richard_Special_ShieldCharge"),
        ("Miyamoto_Special_NitoCross",  "Richard_Special_ShieldBash"),
        ("Miyamoto_Special_Iaijutsu",   "Richard_Special_DoubleHigh"),
    };

    [MenuItem("Crucible/Characters/Richard/2 Build controller")]
    public static void BuildController()
    {
        if (File.Exists(P3Ctrl))
        {
            if (!EditorUtility.DisplayDialog("P3_Richard exists",
                    "Overwrite Animation/Controllers/P3_Richard.controller with a fresh copy of P2_Miyamoto?",
                    "Overwrite", "Cancel")) return;
            AssetDatabase.DeleteAsset(P3Ctrl);
        }
        if (!AssetDatabase.CopyAsset(P2Ctrl, P3Ctrl)) { Debug.LogError("copy failed"); return; }
        AssetDatabase.ImportAsset(P3Ctrl);
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(P3Ctrl);
        var map = StateRenames.ToDictionary(r => r.from, r => r.to);
        int renamed = 0;
        foreach (var layer in ac.layers)
            foreach (var cs in layer.stateMachine.states)
                if (map.TryGetValue(cs.state.name, out string to)) { cs.state.name = to; renamed++; }
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log($"P3_Richard.controller built from P2_Miyamoto, {renamed} states renamed. " +
                  "Run 'Wire special and riposte states' then 'Use Mixamo packs'.");
    }

    [MenuItem("Crucible/Characters/Richard/3 Add to the scene")]
    public static void AddToScene()
    {
        var p2 = GameObject.Find("Player2");
        if (p2 == null) { Debug.LogError("Player2 not found in the open scene"); return; }
        var existing = GameObject.Find("Player3");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Player3 exists", "Replace the Player3 already in the scene?",
                                             "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(existing);
        }

        var go = Object.Instantiate(p2);
        go.name = "Player3";
        Undo.RegisterCreatedObjectUndo(go, "add Richard");
        // Off to Player 2's side, behind the mark: he only stands here between matches, because
        // the select screens stage him and PlaceFighters puts whoever is fighting on the marks.
        go.transform.position = p2.transform.position + new Vector3(0f, 0f, 3.5f);
        go.transform.rotation = p2.transform.rotation;

        var fighter = go.GetComponent(Type("Fighter"));
        var sf = new SerializedObject(fighter);
        sf.FindProperty("playerNumber").intValue = 3;       // sorts last; ApplySides hands out 1 and 2
        sf.FindProperty("characterName").stringValue = "Richard";
        sf.FindProperty("maxHealth").intValue = 105;
        sf.FindProperty("maxStamina").intValue = 110;
        sf.FindProperty("armor").intValue = 24;
        sf.FindProperty("idleStateName").stringValue = "Richard_Idle";
        var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DataPath);
        var dataProp = sf.FindProperty("fighterData");
        if (dataProp != null && data != null) dataProp.objectReferenceValue = data;
        sf.ApplyModifiedProperties();

        var combat = go.GetComponent(Type("CombatSystem"));
        var sc = new SerializedObject(combat);
        SetList(sc.FindProperty("attacks"), "Richard_Light_Thrust", "Richard_Medium_Slash", "Richard_Heavy_Overhead");
        SetList(sc.FindProperty("specials"), "Richard_Special_ShieldCharge", "Richard_Special_ShieldBash", "Richard_Special_DoubleHigh");
        sc.FindProperty("dashRiposte").objectReferenceValue = Attack("Richard_Riposte_Dash");
        sc.FindProperty("parryRiposte").objectReferenceValue = Attack("Richard_Riposte_Parry");
        sc.ApplyModifiedProperties();

        var anim = go.GetComponent<Animator>();
        anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(P3Ctrl);

        // His off hand carries the shield, so there is no second grip on the hilt to solve.
        var grip = go.GetComponent(Type("TwoHandGrip"));
        if (grip != null) Undo.DestroyObjectImmediate(grip);

        Selection.activeGameObject = go;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Player3 (Richard) cloned from Player2. Now run " +
                  "'Crucible > Characters > Rebuild Player3 (Richard body)' to put his model on it.");
    }

    static void SetList(SerializedProperty arr, params string[] names)
    {
        arr.arraySize = names.Length;
        for (int i = 0; i < names.Length; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue = Attack(names[i]);
    }

    static Object Attack(string name) =>
        AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{AttackDir}/{name}.asset");

    static System.Type Type(string name)
    {
        var t = System.Type.GetType(name + ", Assembly-CSharp");
        if (t == null) throw new System.Exception("runtime type " + name + " not found");
        return t;
    }
}
