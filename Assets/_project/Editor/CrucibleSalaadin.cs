// Salaadin - the fourth fighter: falchion and round shield, the roster's balanced one.
//
// Crucible > Characters > Salaadin > 1 Create move set   writes his AttackData + FighterData assets
// Crucible > Characters > Salaadin > 2 Build controller  copies P3_Richard into P4_Salaadin, renaming
//                                                         every state to his move names
// Crucible > Characters > Salaadin > 3 Add to the scene  clones Player3 into Player4, points it at his
//                                                         assets, and rebuilds the body from his FBX
//
// Run them in order on a fresh clone, then the shared menu items finish the job:
//   Crucible > Animation > Wire special and riposte states
//   Crucible > Animation > Use Mixamo packs          (his clips: the Pro Sword and Shield pack)
//   Crucible > Combat    > Wire combo strings
//
// He strikes ONLY with the falchion - no thrusts, no shield bashes, no kicks; that is what separates
// him from Richard, who shares the sword-and-shield pack with him. Richard holds the pack's only
// settled idle, so Salaadin's guard stance and the three cuts the pack cannot give him are keyed by
// hand in ArtSource/Blender/salaadin_clips.py; the rest come from clips Richard does not use.
//
// His MEDIUM and HEAVY are the two showpieces (the shin cut and the full turn) and the two compact
// pack swings sit on SP + Medium and SP + Heavy - the reverse of how he was first built. The frame
// data and the costs did NOT move with them: they belong to the slot, so a normal still costs a
// normal's stamina.
//
// The table below is the SHAPE of each move - its name, trigger, height, which dash evades it,
// its hitbox and its aim. The numbers (damage, stamina, frames, hitstun) live in
// CrucibleBalance with the rest of the roster, so the archetypes can be read side by side.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CrucibleSalaadin
{
    const string Dir = "Assets/_project/Characters/salaadin";
    const string AttackDir = Dir + "/Attacks";
    const string DataPath = "Assets/_project/Data/Fighters/salaadin_data.asset";
    const string P3Ctrl = "Assets/_project/Animation/Controllers/P3_Richard.controller";
    const string P4Ctrl = "Assets/_project/Animation/Controllers/P4_Salaadin.controller";

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

    // The middle of the roster: slower than Miyamoto, quicker than Bjorn, hitting for about what
    // Miyamoto hits for. His guard is a shield, so he blocks well without Richard's armour.
    static readonly Move[] Moves =
    {
        // LIGHT: a flat horizontal sweep out of the BARBARIAN pack (AxePack "standing melee combo
        // attack ver. 2"), the user's pick on 2026-10-23. 146 deg of UNWRAPPED yaw, hands apart at
        // grip_spread 0.76 so the shield arm is free, and used by nobody else in the roster.
        // CAUTION, and it was flagged before the choice was made: its tip arc is 0.29, the
        // second-lowest of any strike in the four packs - the same low-tip shape that made his old
        // SP+Light (slash (2), tipHi 0.07) read as an unfinished animation. The box is widened and
        // flattened to match a sweep, and AimLevel holds the falchion on the horizon through it so
        // the edge leads instead of the tip dropping toward the chest.
        new Move { asset = "Salaadin_Light_SaifCut", name = "Saif Cut", trigger = "LightAttack",
                   height = Mid, type = Light, comesFrom = FromRight, hitSide = HitLeft, aim = AimLevel,
                   range = 2.9f, size = new Vector3(1.4f, 0.9f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // MEDIUM: the low cut across the shins (Custom/Salaadin_ShinCut). Low, so a dash over it
        // beats it, and the box sits down at knee height where the blade actually travels.
        new Move { asset = "Salaadin_Medium_ShinCut", name = "Shin Cut", trigger = "MediumAttack",
                   height = Low, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 2.8f, size = new Vector3(1.3f, 1.0f, 1f), offset = new Vector3(0f, 0.6f, 0f),
                   lunge = -1f, weapon = true },

        // HEAVY: a plain overhead - saber up and straight down the centre (SwordShieldPack
        // "slash (4)"). It used to be the whirling turn, which the user could not read as a cut;
        // the clip now has 9 deg of hip turn instead of 43, so the move data has to follow it:
        // Overhead/FromOverhead so the right dashes evade it, HitStraight so the defender takes
        // the straight reaction rather than a side one, and AimFull because there is no turn left
        // to protect - he should face the man he is chopping. The asset NAME is deliberately
        // unchanged: the scene holds these by GUID and a rename needs "Salaadin > 4 Rewire".
        new Move { asset = "Salaadin_Heavy_WhirlingSaif", name = "Falling Saif", trigger = "HeavyAttack",
                   height = Overhead, type = Heavy, comesFrom = FromOverhead, hitSide = HitStraight, aim = AimFull,
                   range = 3.1f, size = new Vector3(1.2f, 1.6f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // SP + Light: the barbarian REVERSE slash (AxePack "standing melee attack backhand") -
        // the user's pick, 2026-10-23, over the committed overhead that briefly held this slot and
        // over slash (2) before it. 245 deg of turn, tip arc 1.40 (the highest of any strike in the
        // four packs) and foot^ -0.72, so the feet stay PLANTED. Richard's SP + Light and Bjorn's
        // both play this same clip; a trim is keyed by CLIP and shared, so all three take their
        // window and speed from their own per-STATE entry in swing_contacts.json instead.
        // Geometry follows the swing, not the slot's history: Mid rather than Overhead, FromLeft /
        // HitRight because it comes back across him, a wide flat box, and AimLevel - a turning
        // swing aimed Full points the tip DOWN at the chest from a raised hand, which is what read
        // as a spinning back FIST on Kuruma and the Nito Cross.
        // The LUNGE drops 2.4 -> 1.2: this clip plants its feet and travels 0.02 of its own, so the
        // old rush distance would have slid him across the floor with his boots still. The ASSET
        // name is kept - the scene holds it by GUID - but the displayed name follows the move.
        new Move { asset = "Salaadin_Special_VeiledRush", name = "Veiled Reversal", trigger = "SpecialLight",
                   height = Mid, type = Special, specialButton = Light, comesFrom = FromLeft, hitSide = HitRight,
                   aim = AimLevel, range = 3.2f, size = new Vector3(1.4f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.2f, weapon = true },

        // SP + Medium: the flat, wide swing. Widest box on him, because the cut is horizontal.
        new Move { asset = "Salaadin_Special_CrescentSlash", name = "Crescent Slash", trigger = "SpecialMedium",
                   height = Mid, type = Special, specialButton = Medium, comesFrom = FromLeft, hitSide = HitRight,
                   aim = AimFull, range = 3.0f, size = new Vector3(1.5f, 0.9f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.2f, weapon = true },

        // SP + Heavy: the SPINNING JUMPING SLASH - the user asked for this one by name after
        // seeing it as his Heavy (SwordShieldPack "slash (4)": overhead + spin + leap, 391 deg of
        // unwrapped yaw, foot^ -0.35, tip arc 1.28). Today's earlier change took it off the Heavy,
        // where a full turn is a fault; on a special a full turn is the flourish. AimYaw is kept
        // deliberately: it faces him at the man while leaving the clip's own vertical wheel alone,
        // which is what an overhead that spins needs - AimFull would fight the descent and AimLevel
        // would flatten a cut that is supposed to come down.
        new Move { asset = "Salaadin_Special_HattinCleave", name = "Hattin Cleave", trigger = "SpecialHeavy",
                   height = Overhead, type = Special, specialButton = Heavy, comesFrom = FromOverhead, hitSide = HitStraight,
                   aim = AimYaw, range = 3.1f, size = new Vector3(1.0f, 1.7f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.2f, weapon = true },

        new Move { asset = "Salaadin_Riposte_Dash", name = "Falcon Riposte", trigger = "RiposteDash",
                   height = Mid, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 2.9f, size = new Vector3(1.2f, 1.1f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // A short saber counter, NOT a boot: the other three fighters answer a parry with a kick and
        // he does not kick at all, so this one is judged on the blade like the rest of his set.
        new Move { asset = "Salaadin_Riposte_Parry", name = "Saber Riposte", trigger = "RiposteParry",
                   height = Mid, type = Medium, comesFrom = FromThrust, hitSide = HitStraight, aim = AimFull,
                   range = 2.6f, size = new Vector3(1.1f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },
    };

    [MenuItem("Crucible/Characters/Salaadin/1 Create move set")]
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

        // Anything left in his Attacks folder that the table above no longer names is a move he
        // used to have. Delete it, or a renamed slot leaves its old asset behind for the combo
        // wiring and the Moves screen to find.
        foreach (var guid in AssetDatabase.FindAssets("t:AttackData", new[] { AttackDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!Moves.Any(m => m.asset == stem))
            {
                Debug.Log($"Salaadin: dropping {stem}, no longer in the move set");
                AssetDatabase.DeleteAsset(path);
            }
        }

        // Damage, stamina cost, frames, hitstun and his FighterData are NOT written here: they
        // are the roster-wide table in CrucibleBalance, so the four archetypes stay comparable.
        CrucibleBalance.Apply();

        AssetDatabase.SaveAssets();
        Debug.Log($"Salaadin: {Moves.Length} attacks written to {AttackDir}; numbers from CrucibleBalance.");
    }

    // Richard's controller is the template this time, not Miyamoto's: the same sword-and-shield
    // shape, and his states already carry the special and riposte wiring.
    static readonly (string from, string to)[] StateRenames =
    {
        ("Richard_Idle",                 "Salaadin_Idle"),
        ("Richard_Walk_Forward",         "Salaadin_Walk_Forward"),
        ("Richard_Walk_Back",            "Salaadin_Walk_Back"),
        ("Richard_Light_Thrust",         "Salaadin_Light_SaifCut"),
        ("Richard_Medium_Slash",         "Salaadin_Medium_ShinCut"),
        ("Richard_Heavy_Overhead",       "Salaadin_Heavy_WhirlingSaif"),
        ("Richard_Riposte_Dash",         "Salaadin_Riposte_Dash"),
        ("Richard_Riposte_Parry",        "Salaadin_Riposte_Parry"),
        ("Richard_Special_ShieldCharge", "Salaadin_Special_VeiledRush"),
        ("Richard_Special_ShieldBash",   "Salaadin_Special_CrescentSlash"),
        ("Richard_Special_DoubleHigh",   "Salaadin_Special_HattinCleave"),
    };

    [MenuItem("Crucible/Characters/Salaadin/2 Build controller")]
    public static void BuildController()
    {
        if (!File.Exists(P3Ctrl))
        {
            Debug.LogError("P3_Richard.controller is the template and it is missing; build Richard first.");
            return;
        }
        if (File.Exists(P4Ctrl))
        {
            if (!EditorUtility.DisplayDialog("P4_Salaadin exists",
                    "Overwrite Animation/Controllers/P4_Salaadin.controller with a fresh copy of P3_Richard?",
                    "Overwrite", "Cancel")) return;
            AssetDatabase.DeleteAsset(P4Ctrl);
        }
        if (!AssetDatabase.CopyAsset(P3Ctrl, P4Ctrl)) { Debug.LogError("copy failed"); return; }
        AssetDatabase.ImportAsset(P4Ctrl);
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(P4Ctrl);
        var map = StateRenames.ToDictionary(r => r.from, r => r.to);
        int renamed = 0;
        foreach (var layer in ac.layers)
            foreach (var cs in layer.stateMachine.states)
                if (map.TryGetValue(cs.state.name, out string to)) { cs.state.name = to; renamed++; }
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log($"P4_Salaadin.controller built from P3_Richard, {renamed} states renamed. " +
                  "Run 'Wire special and riposte states' then 'Use Mixamo packs'.");
    }

    [MenuItem("Crucible/Characters/Salaadin/3 Add to the scene")]
    public static void AddToScene()
    {
        var p3 = GameObject.Find("Player3");
        if (p3 == null) { Debug.LogError("Player3 not found in the open scene"); return; }
        var existing = GameObject.Find("Player4");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Player4 exists", "Replace the Player4 already in the scene?",
                                             "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(existing);
        }

        var go = Object.Instantiate(p3);
        go.name = "Player4";
        Undo.RegisterCreatedObjectUndo(go, "add Salaadin");
        // Another 3.5 behind Richard's bench spot: he only stands here between matches, because
        // the select screens stage him and PlaceFighters puts whoever is fighting on the marks.
        go.transform.position = p3.transform.position + new Vector3(0f, 0f, 3.5f);
        go.transform.rotation = p3.transform.rotation;

        var fighter = go.GetComponent(Type("Fighter"));
        var sf = new SerializedObject(fighter);
        sf.FindProperty("playerNumber").intValue = 4;       // sorts last; ApplySides hands out 1 and 2
        sf.FindProperty("characterName").stringValue = "Salaadin";
        sf.FindProperty("maxHealth").intValue = 95;
        sf.FindProperty("maxStamina").intValue = 115;
        sf.FindProperty("armor").intValue = 18;
        sf.FindProperty("idleStateName").stringValue = "Salaadin_Idle";
        var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DataPath);
        var dataProp = sf.FindProperty("fighterData");
        if (dataProp != null && data != null) dataProp.objectReferenceValue = data;
        sf.ApplyModifiedProperties();

        WireCombat(go);   // move set + the animator controller

        // His off hand carries the shield, so there is no second grip on the hilt to solve.
        var grip = go.GetComponent(Type("TwoHandGrip"));
        if (grip != null) Undo.DestroyObjectImmediate(grip);

        Selection.activeGameObject = go;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Player4 (Salaadin) cloned from Player3. Now run " +
                  "'Crucible > Characters > Rebuild Player4 (Salaadin body)' to put his model on it.");
    }

    /// <summary>
    /// Point Player4's CombatSystem and Animator at the move set above. Its own menu item too,
    /// because the scene holds both by GUID: renaming a slot's move leaves it on the old asset,
    /// and "2 Build controller" writes a FRESH P4_Salaadin.controller whose GUID is new, so the
    /// Animator's reference goes null and he stands in the bind T-pose. Re-running "3 Add to the
    /// scene" would fix either, but only by rebuilding the whole fighter.
    /// </summary>
    [MenuItem("Crucible/Characters/Salaadin/4 Rewire Player4 to the move set")]
    public static void RewirePlayer4()
    {
        var go = GameObject.Find("Player4");
        if (go == null) { Debug.LogError("Player4 not found in the open scene"); return; }
        WireCombat(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Player4: controller, attacks, specials and both ripostes re-pointed at Salaadin's move set.");
    }

    static void WireCombat(GameObject go)
    {
        var anim = go.GetComponent<Animator>();
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(P4Ctrl);
        if (ctrl == null) Debug.LogError($"{P4Ctrl} is missing; run '2 Build controller'");
        else anim.runtimeAnimatorController = ctrl;

        var sc = new SerializedObject(go.GetComponent(Type("CombatSystem")));
        SetList(sc.FindProperty("attacks"), "Salaadin_Light_SaifCut", "Salaadin_Medium_ShinCut",
                                            "Salaadin_Heavy_WhirlingSaif");
        SetList(sc.FindProperty("specials"), "Salaadin_Special_VeiledRush", "Salaadin_Special_CrescentSlash",
                                             "Salaadin_Special_HattinCleave");
        sc.FindProperty("dashRiposte").objectReferenceValue = Attack("Salaadin_Riposte_Dash");
        sc.FindProperty("parryRiposte").objectReferenceValue = Attack("Salaadin_Riposte_Parry");
        sc.ApplyModifiedProperties();
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
