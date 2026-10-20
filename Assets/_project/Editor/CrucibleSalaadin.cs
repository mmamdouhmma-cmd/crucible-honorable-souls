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
// He strikes ONLY with slashes and kicks - no thrusts, no shield bashes; that is what separates him
// from Richard, who shares the sword-and-shield pack with him. Richard holds the pack's one kick and
// its only settled idle, so Salaadin's kicks and his guard stance are keyed by hand in
// ArtSource/Blender/salaadin_clips.py, and his three cuts come from clips Richard does not use.
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
    const int AimFull = 0, AimYaw = 1, AimOff = 2;

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
        new Move { asset = "Salaadin_Light_SaifCut", name = "Saif Cut", trigger = "LightAttack",
                   height = Mid, type = Light, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 2.9f, size = new Vector3(1.1f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // Wide and flat - the widest normal box on the roster, because the swing is horizontal.
        new Move { asset = "Salaadin_Medium_CrescentSlash", name = "Crescent Slash", trigger = "MediumAttack",
                   height = Mid, type = Medium, comesFrom = FromLeft, hitSide = HitRight, aim = AimFull,
                   range = 3.0f, size = new Vector3(1.5f, 0.9f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        new Move { asset = "Salaadin_Heavy_HattinCleave", name = "Hattin Cleave", trigger = "HeavyAttack",
                   height = Overhead, type = Heavy, comesFrom = FromOverhead, hitSide = HitStraight, aim = AimYaw,
                   range = 3.1f, size = new Vector3(1.0f, 1.7f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // SP + Light: the shield leads the dash, the saber comes up underneath it
        // (Custom/Salaadin_VeiledRush).
        new Move { asset = "Salaadin_Special_VeiledRush", name = "Veiled Rush", trigger = "SpecialLight",
                   height = Mid, type = Special, specialButton = Light, comesFrom = FromThrust, hitSide = HitStraight,
                   aim = AimFull, range = 3.2f, size = new Vector3(1.0f, 1.3f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 2.4f, weapon = true },

        // SP + Medium: a front kick that staggers, carried into a low cut (Custom/Salaadin_BreakingKick).
        // The kick sells it; the cut is the hit, so the active window sits on the blade.
        new Move { asset = "Salaadin_Special_BreakingKick", name = "Breaking Kick", trigger = "SpecialMedium",
                   height = Low, type = Special, specialButton = Medium, comesFrom = FromRight, hitSide = HitLeft,
                   aim = AimFull, range = 2.8f, size = new Vector3(1.3f, 1.0f, 1f), offset = new Vector3(0f, 0.6f, 0f),
                   lunge = 1.2f, weapon = true },

        // SP + Heavy: a full turn with two cuts in it (Custom/Salaadin_WhirlingSaif). One hit, on the
        // second cut - the engine gives a swing one hit, and the second is the one that reads.
        new Move { asset = "Salaadin_Special_WhirlingSaif", name = "Whirling Saif", trigger = "SpecialHeavy",
                   height = Mid, type = Special, specialButton = Heavy, comesFrom = FromLeft, hitSide = HitRight,
                   aim = AimYaw, range = 3.1f, size = new Vector3(1.5f, 1.2f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.2f, weapon = true },

        new Move { asset = "Salaadin_Riposte_Dash", name = "Falcon Riposte", trigger = "RiposteDash",
                   height = Mid, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 2.9f, size = new Vector3(1.2f, 1.1f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // A heel kick, like the other three parry ripostes: judged by the body box, not the blade.
        new Move { asset = "Salaadin_Riposte_Parry", name = "Heel Riposte", trigger = "RiposteParry",
                   height = Mid, type = Medium, comesFrom = FromThrust, hitSide = HitStraight, aim = AimOff,
                   range = 1.9f, size = new Vector3(1.0f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = false },
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
        ("Richard_Medium_Slash",         "Salaadin_Medium_CrescentSlash"),
        ("Richard_Heavy_Overhead",       "Salaadin_Heavy_HattinCleave"),
        ("Richard_Riposte_Dash",         "Salaadin_Riposte_Dash"),
        ("Richard_Riposte_Parry",        "Salaadin_Riposte_Parry"),
        ("Richard_Special_ShieldCharge", "Salaadin_Special_VeiledRush"),
        ("Richard_Special_ShieldBash",   "Salaadin_Special_BreakingKick"),
        ("Richard_Special_DoubleHigh",   "Salaadin_Special_WhirlingSaif"),
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

        var combat = go.GetComponent(Type("CombatSystem"));
        var sc = new SerializedObject(combat);
        SetList(sc.FindProperty("attacks"), "Salaadin_Light_SaifCut", "Salaadin_Medium_CrescentSlash",
                                            "Salaadin_Heavy_HattinCleave");
        SetList(sc.FindProperty("specials"), "Salaadin_Special_VeiledRush", "Salaadin_Special_BreakingKick",
                                             "Salaadin_Special_WhirlingSaif");
        sc.FindProperty("dashRiposte").objectReferenceValue = Attack("Salaadin_Riposte_Dash");
        sc.FindProperty("parryRiposte").objectReferenceValue = Attack("Salaadin_Riposte_Parry");
        sc.ApplyModifiedProperties();

        var anim = go.GetComponent<Animator>();
        anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(P4Ctrl);

        // His off hand carries the shield, so there is no second grip on the hilt to solve.
        var grip = go.GetComponent(Type("TwoHandGrip"));
        if (grip != null) Undo.DestroyObjectImmediate(grip);

        Selection.activeGameObject = go;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Player4 (Salaadin) cloned from Player3. Now run " +
                  "'Crucible > Characters > Rebuild Player4 (Salaadin body)' to put his model on it.");
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
