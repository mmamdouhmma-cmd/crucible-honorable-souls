// Buwaka - the sixth fighter: spear and round hide shield, the RANGE fighter.
//
// Crucible > Characters > Buwaka > 1 Create move set   writes his AttackData + FighterData assets
// Crucible > Characters > Buwaka > 2 Build controller  copies P3_Richard into P6_Buwaka, renaming
//                                                       every state to his move names
// Crucible > Characters > Buwaka > 3 Add to the scene  clones Player3 into Player6 and points it at
//                                                       his assets
// Crucible > Characters > Buwaka > 4 Rewire            re-points Player6 at the move set
//
// Run them in order on a fresh clone, then the shared menu items finish the job:
//   Crucible > Animation > Wire special and riposte states
//   Crucible > Animation > Use Mixamo packs
//   Crucible > Animation > Wire the run state / Wire the block-walk legs
//   Crucible > Combat    > Wire combo strings
//   Crucible > Characters > Rebuild Player6 (Buwaka body)
//
// The design (the user's brief, 2026-10-25): the SPEAR gives him the longest reach in the roster
// (hitbox ranges 3.3-4.0 and LungeOverride stops that keep him at the spear's length instead of
// closing to 1.7), moderate everything else - health 100, damage x0.95, walk 4.0 (under Salaadin's
// 4.1, over Bjorn's 3.8), block 68 like Bjorn. Moves are Richard's swings per the brief: swing from
// the left (Light), swing from the right (Medium), thrust (Heavy) - and the Heavy IS Richard's
// orphaned authored thrust clip (Clips/Custom/Richard_Thrust.fbx, which no state has played since
// 2026-10-23). The specials trade one slot for an overhead and one for a low sweep, per the brief.
// He is a shield fighter: no TwoHandGrip, the AxePack's one-handed right-hand clips retarget onto
// him cleanly, and the GreatSwordPack cannot be used (it would drag the shield arm onto the haft).
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CrucibleBuwaka
{
    const string Dir = "Assets/_project/Characters/Buwaka";
    const string AttackDir = Dir + "/Attacks";
    const string DataPath = "Assets/_project/Data/Fighters/buwaka_data.asset";
    const string P3Ctrl = "Assets/_project/Animation/Controllers/P3_Richard.controller";
    const string P6Ctrl = "Assets/_project/Animation/Controllers/P6_Buwaka.controller";

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

    static readonly Move[] Moves =
    {
        // LIGHT: a quick THRUST (the user's call, 2026-10-26: "use Richard's attacks for him -
        // thrusts, slash and overhead only"; the old backhand sweep carried the spear sideways so
        // the leaf head never arrived and the light landed nothing). Richard's authored thrust
        // clip - the same one his Heavy and dash riposte play, both verified landing on his rig.
        new Move { asset = "Buwaka_Light_HornCut", name = "Horn Thrust", trigger = "LightAttack",
                   height = Mid, type = Light, comesFrom = FromThrust, hitSide = HitStraight, aim = AimFull,
                   range = 3.6f, size = new Vector3(0.8f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // MEDIUM: "sword and shield slash (5)" - the pack's LOW cut from the right (the hand never
        // rises above 0.69 of hips-to-head), so the move is authored Low like Salaadin's Shin Cut
        // on the same clip, with the box down at knee height where the blade travels.
        new Move { asset = "Buwaka_Medium_SavannaCut", name = "Savanna Cut", trigger = "MediumAttack",
                   height = Low, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 3.4f, size = new Vector3(1.4f, 1.0f, 1f), offset = new Vector3(0f, 0.6f, 0f),
                   lunge = -1f, weapon = true },

        // HEAVY: the THRUST - Richard's authored clip (Clips/Custom/Richard_Thrust.fbx), an orphan
        // since 2026-10-23. An authored clip imports on Richard's avatar (CopyFromOther) and
        // retargets onto any humanoid at runtime. The roster's longest normal: a narrow box way
        // out in front.
        new Move { asset = "Buwaka_Heavy_IklwaThrust", name = "Iklwa Thrust", trigger = "HeavyAttack",
                   height = Mid, type = Heavy, comesFrom = FromThrust, hitSide = HitStraight, aim = AimFull,
                   range = 3.8f, size = new Vector3(0.8f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // SP + Light: the LOW SWEEP the brief asks for - AxePack "standing melee attack 360 low"
        // (Bjorn's Hewing Charge clip, with its own contact). A full turn at shin height.
        new Move { asset = "Buwaka_Special_SerpentSweep", name = "Serpent Sweep", trigger = "SpecialLight",
                   height = Low, type = Special, specialButton = Light, comesFrom = FromRight, hitSide = HitLeft,
                   aim = AimLevel, range = 3.3f, size = new Vector3(1.6f, 0.8f, 1f), offset = new Vector3(0f, 0.6f, 0f),
                   lunge = 1.2f, weapon = true },

        // SP + Medium: the TURNING OVERHEAD (the user's combo 2 ender, 2026-10-26) - "sword and
        // shield attack", the committed overhead with 86 deg of turn (Salaadin's Hattin Cleave
        // clip). AimYaw: face the man, leave the descent alone.
        new Move { asset = "Buwaka_Special_FallingSpear", name = "Falling Spear", trigger = "SpecialMedium",
                   height = Overhead, type = Special, specialButton = Medium, comesFrom = FromOverhead, hitSide = HitStraight,
                   aim = AimYaw, range = 3.5f, size = new Vector3(1.0f, 1.7f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.2f, weapon = true },

        // SP + Heavy: the GREAT IMPALE - the thrust again, heavier and driven in with the roster's
        // longest lunge. Hyper armour (CrucibleBalance), like every SP+Heavy.
        new Move { asset = "Buwaka_Special_GreatImpale", name = "Great Impale", trigger = "SpecialHeavy",
                   height = Mid, type = Special, specialButton = Heavy, comesFrom = FromThrust, hitSide = HitStraight,
                   aim = AimFull, range = 4.0f, size = new Vector3(0.8f, 1.1f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 2.0f, weapon = true },

        // COMBO ENDER (the user's call, 2026-10-26): reachable ONLY as the third hit of
        // Horn Thrust / Savanna Cut -> Falling Spear -> this. attackType Medium ON PURPOSE: a
        // string carries at most ONE special (TryCancelIntoFollowUp) and Falling Spear is it, so
        // the authored special->special ender could never fire - this normal-classed showpiece
        // takes its place. It is in NO library (WireCombat leaves it out), so it cannot be thrown
        // raw; its clip is the one barbarian strike nobody used, "combo attack ver. 1" - a wide
        // advancing left sweep (clip peak f59 of 140 @30fps, measured in-editor 2026-10-26).
        new Move { asset = "Buwaka_Ender_SerpentCoil", name = "Serpent Coil", trigger = "ComboEnder",
                   height = Mid, type = Medium, comesFrom = FromLeft, hitSide = HitRight, aim = AimLevel,
                   range = 3.4f, size = new Vector3(1.6f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        new Move { asset = "Buwaka_Riposte_Dash", name = "Lion's Answer", trigger = "RiposteDash",
                   height = Mid, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 3.2f, size = new Vector3(1.2f, 1.1f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // The parry answer is the spear thrust, like his dash answer (it was a boot until
        // 2026-10-27 - the user's call: no kicks on anyone).
        new Move { asset = "Buwaka_Riposte_Parry", name = "War Dance", trigger = "RiposteParry",
                   height = Mid, type = Medium, comesFrom = FromThrust, hitSide = HitStraight, aim = AimFull,
                   range = 3.2f, size = new Vector3(1.2f, 1.1f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },
    };

    [MenuItem("Crucible/Characters/Buwaka/1 Create move set")]
    public static void CreateMoves()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_project/Characters", "Buwaka");
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
        foreach (var guid in AssetDatabase.FindAssets("t:AttackData", new[] { AttackDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!Moves.Any(m => m.asset == stem))
            {
                Debug.Log($"Buwaka: dropping {stem}, no longer in the move set");
                AssetDatabase.DeleteAsset(path);
            }
        }
        CrucibleBalance.Apply();
        AssetDatabase.SaveAssets();
        Debug.Log($"Buwaka: {Moves.Length} attacks written to {AttackDir}; numbers from CrucibleBalance.");
    }

    // Richard's controller is the template: the same shield-fighter shape.
    static readonly (string from, string to)[] StateRenames =
    {
        ("Richard_Idle",                 "Buwaka_Idle"),
        ("Richard_Walk_Forward",         "Buwaka_Walk_Forward"),
        ("Richard_Walk_Back",            "Buwaka_Walk_Back"),
        ("Richard_Light_Thrust",         "Buwaka_Light_HornCut"),
        ("Richard_Medium_Slash",         "Buwaka_Medium_SavannaCut"),
        ("Richard_Heavy_Overhead",       "Buwaka_Heavy_IklwaThrust"),
        ("Richard_Riposte_Dash",         "Buwaka_Riposte_Dash"),
        ("Richard_Riposte_Parry",        "Buwaka_Riposte_Parry"),
        ("Richard_Special_ShieldCharge", "Buwaka_Special_SerpentSweep"),
        ("Richard_Special_ShieldBash",   "Buwaka_Special_FallingSpear"),
        ("Richard_Special_DoubleHigh",   "Buwaka_Special_GreatImpale"),
    };

    [MenuItem("Crucible/Characters/Buwaka/2 Build controller")]
    public static void BuildController()
    {
        if (!File.Exists(P3Ctrl))
        {
            Debug.LogError("P3_Richard.controller is the template and it is missing.");
            return;
        }
        if (File.Exists(P6Ctrl))
        {
            if (!EditorUtility.DisplayDialog("P6_Buwaka exists",
                    "Overwrite Animation/Controllers/P6_Buwaka.controller with a fresh copy of P3_Richard?",
                    "Overwrite", "Cancel")) return;
            AssetDatabase.DeleteAsset(P6Ctrl);
        }
        if (!AssetDatabase.CopyAsset(P3Ctrl, P6Ctrl)) { Debug.LogError("copy failed"); return; }
        AssetDatabase.ImportAsset(P6Ctrl);
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(P6Ctrl);
        var map = StateRenames.ToDictionary(r => r.from, r => r.to);
        int renamed = 0;
        foreach (var layer in ac.layers)
            foreach (var cs in layer.stateMachine.states)
                if (map.TryGetValue(cs.state.name, out string to)) { cs.state.name = to; renamed++; }
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log($"P6_Buwaka.controller built from P3_Richard, {renamed} states renamed. " +
                  "Run 'Wire special and riposte states' then 'Use Mixamo packs'.");
    }

    [MenuItem("Crucible/Characters/Buwaka/3 Add to the scene")]
    public static void AddToScene()
    {
        var p3 = GameObject.Find("Player3");
        if (p3 == null) { Debug.LogError("Player3 not found in the open scene"); return; }
        var existing = GameObject.Find("Player6");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Player6 exists", "Replace the Player6 already in the scene?",
                                             "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(existing);
        }

        var go = Object.Instantiate(p3);
        go.name = "Player6";
        Undo.RegisterCreatedObjectUndo(go, "add Buwaka");
        // A bench spot beyond Salaadin's (Player4 sits at p3 + 3.5).
        go.transform.position = p3.transform.position + new Vector3(0f, 0f, 7f);
        go.transform.rotation = p3.transform.rotation;

        var fighter = go.GetComponent(Type("Fighter"));
        var sf = new SerializedObject(fighter);
        sf.FindProperty("playerNumber").intValue = 6;
        sf.FindProperty("characterName").stringValue = "Buwaka";
        sf.FindProperty("maxHealth").intValue = 100;
        sf.FindProperty("maxStamina").intValue = 100;
        sf.FindProperty("armor").intValue = 15;
        sf.FindProperty("idleStateName").stringValue = "Buwaka_Idle";
        var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DataPath);
        var dataProp = sf.FindProperty("fighterData");
        if (dataProp != null && data != null) dataProp.objectReferenceValue = data;
        sf.ApplyModifiedProperties();

        WireCombat(go);   // move set + the animator controller

        // A shield fighter: no second grip on the haft (Richard carries no TwoHandGrip, but guard
        // against a template change).
        var grip = go.GetComponent(Type("TwoHandGrip"));
        if (grip != null) Undo.DestroyObjectImmediate(grip);

        Selection.activeGameObject = go;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Player6 (Buwaka) cloned from Player3. Now run " +
                  "'Crucible > Characters > Rebuild Player6 (Buwaka body)' to put his model on it.");
    }

    [MenuItem("Crucible/Characters/Buwaka/4 Rewire Player6 to the move set")]
    public static void RewirePlayer6()
    {
        var go = GameObject.Find("Player6");
        if (go == null) { Debug.LogError("Player6 not found in the open scene"); return; }
        WireCombat(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Player6: controller, attacks, specials and both ripostes re-pointed at Buwaka's move set.");
    }

    static void WireCombat(GameObject go)
    {
        var anim = go.GetComponent<Animator>();
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(P6Ctrl);
        if (ctrl == null) Debug.LogError($"{P6Ctrl} is missing; run '2 Build controller'");
        else anim.runtimeAnimatorController = ctrl;

        var sc = new SerializedObject(go.GetComponent(Type("CombatSystem")));
        SetList(sc.FindProperty("attacks"), "Buwaka_Light_HornCut", "Buwaka_Medium_SavannaCut",
                                            "Buwaka_Heavy_IklwaThrust");
        SetList(sc.FindProperty("specials"), "Buwaka_Special_SerpentSweep", "Buwaka_Special_FallingSpear",
                                             "Buwaka_Special_GreatImpale");
        // No riposte moves (the user's call, 2026-10-27): a counter after a dodge or a parry is an
        // ordinary normal, special or combo with the counter bonus. The *_Riposte_* assets stay on disk.
        sc.FindProperty("dashRiposte").objectReferenceValue = null;
        sc.FindProperty("parryRiposte").objectReferenceValue = null;
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
