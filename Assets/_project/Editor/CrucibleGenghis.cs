// Genghis - the fifth fighter: a two-handed steppe saber, the PARRY-COUNTER tank.
//
// Crucible > Characters > Genghis > 1 Create move set   writes his AttackData + FighterData assets
// Crucible > Characters > Genghis > 2 Build controller  copies P2_Miyamoto into P5_Genghis, renaming
//                                                        every state to his move names
// Crucible > Characters > Genghis > 3 Add to the scene  clones Player2 into Player5 and points it at
//                                                        his assets
// Crucible > Characters > Genghis > 4 Rewire            re-points Player5 at the move set
//
// Run them in order on a fresh clone, then the shared menu items finish the job:
//   Crucible > Animation > Wire special and riposte states
//   Crucible > Animation > Use Mixamo packs          (his clips: the GreatSwordPack, two-handed)
//   Crucible > Animation > Wire the run state / Wire the block-walk legs
//   Crucible > Combat    > Wire combo strings
//   Crucible > Characters > Rebuild Player5 (Genghis body)
//
// The design (the user's brief, 2026-10-25): a tank BELOW Richard - less health, less block - but
// faster, hitting about as hard. NOT diverse: the shortest combo table in the roster. What he owns
// outright is the PARRY COUNTER: the roster's biggest riposte payoff (damage 12 at x1.7, where
// Miyamoto's famous counter is 8 at x1.5), paid out through the great-sword wheel.
//
// His moves follow Richard's pattern: each special is a HEAVIER version of the normal on the same
// button, playing the same clip at its own per-STATE contact speed - the camera shake and the
// numbers are what separate them. The clips are Miyamoto's three strikes, per the brief: slash
// from the left (Light), slash from the right (Medium), overhead (Heavy). He is two-handed, so
// the GreatSwordPack's narrow grip (grip_spread 0.11-0.20) is exactly right for him.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CrucibleGenghis
{
    const string Dir = "Assets/_project/Characters/Genghis";
    const string AttackDir = Dir + "/Attacks";
    const string DataPath = "Assets/_project/Data/Fighters/genghis_data.asset";
    const string P2Ctrl = "Assets/_project/Animation/Controllers/P2_Miyamoto.controller";
    const string P5Ctrl = "Assets/_project/Animation/Controllers/P5_Genghis.controller";

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
        // LIGHT: "great sword slash (3)" - the pack's horizontal middle slash (Miyamoto plays it as
        // his Light). Authored as coming FROM THE LEFT per the brief; verify the direction in play
        // and flip comesFrom/hitSide if the blade actually crosses the other way.
        new Move { asset = "Genghis_Light_SteppeCut", name = "Steppe Cut", trigger = "LightAttack",
                   height = Mid, type = Light, comesFrom = FromLeft, hitSide = HitRight, aim = AimFull,
                   range = 2.7f, size = new Vector3(1.4f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // MEDIUM: "great sword slash" - the diagonal from the right (Miyamoto's Medium).
        new Move { asset = "Genghis_Medium_HordeSlash", name = "Horde Slash", trigger = "MediumAttack",
                   height = Mid, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimFull,
                   range = 2.6f, size = new Vector3(1.5f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // HEAVY: "great sword casting" - the pack's only non-spinning overhead (Miyamoto's Heavy).
        new Move { asset = "Genghis_Heavy_KhanCleave", name = "Khan's Cleave", trigger = "HeavyAttack",
                   height = Overhead, type = Heavy, comesFrom = FromOverhead, hitSide = HitStraight, aim = AimFull,
                   range = 2.8f, size = new Vector3(0.9f, 1.6f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },

        // SPECIALS: each plays its own normal's clip, heavier - Richard's pattern. A trim is keyed
        // by CLIP so each special shares its normal's trim; the per-STATE contact in
        // swing_contacts.json keeps its own window and speed, and the special camera shake
        // (FightCamera.specialShakeMultiplier) is what the player sees.
        new Move { asset = "Genghis_Special_SteppeStorm", name = "Steppe Storm", trigger = "SpecialLight",
                   height = Mid, type = Special, specialButton = Light, comesFrom = FromLeft, hitSide = HitRight,
                   aim = AimFull, range = 2.9f, size = new Vector3(1.5f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.2f, weapon = true },

        new Move { asset = "Genghis_Special_HordeBreaker", name = "Horde Breaker", trigger = "SpecialMedium",
                   height = Mid, type = Special, specialButton = Medium, comesFrom = FromRight, hitSide = HitLeft,
                   aim = AimFull, range = 2.9f, size = new Vector3(1.6f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.2f, weapon = true },

        new Move { asset = "Genghis_Special_WrathOfTengri", name = "Wrath of Tengri", trigger = "SpecialHeavy",
                   height = Overhead, type = Special, specialButton = Heavy, comesFrom = FromOverhead, hitSide = HitStraight,
                   aim = AimYaw, range = 3.0f, size = new Vector3(1.0f, 1.7f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = 1.4f, weapon = true },

        // Dash counter: the great-sword KICK, like Miyamoto's - a boot, judged on the body box.
        new Move { asset = "Genghis_Riposte_Dash", name = "Khan's Answer", trigger = "RiposteDash",
                   height = Mid, type = Medium, comesFrom = FromRight, hitSide = HitLeft, aim = AimOff,
                   range = 2.2f, size = new Vector3(1.0f, 1.2f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = false },

        // PARRY counter: the great-sword WHEEL ("great sword high spin attack" - Kuruma's clip).
        // THE move of his kit: 12 damage at parryCounterDamage x1.7 is the roster's hardest
        // punish (20.4 against Miyamoto's 12). AimLevel keeps the edge on the horizon through
        // the turn, the same cure the Veiled Reversal and Nito Cross needed.
        new Move { asset = "Genghis_Riposte_Parry", name = "Khan's Verdict", trigger = "RiposteParry",
                   height = Mid, type = Heavy, comesFrom = FromRight, hitSide = HitLeft, aim = AimLevel,
                   range = 3.2f, size = new Vector3(1.4f, 1.0f, 1f), offset = new Vector3(0f, 1f, 0f),
                   lunge = -1f, weapon = true },
    };

    [MenuItem("Crucible/Characters/Genghis/1 Create move set")]
    public static void CreateMoves()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_project/Characters", "Genghis");
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
                Debug.Log($"Genghis: dropping {stem}, no longer in the move set");
                AssetDatabase.DeleteAsset(path);
            }
        }
        CrucibleBalance.Apply();
        AssetDatabase.SaveAssets();
        Debug.Log($"Genghis: {Moves.Length} attacks written to {AttackDir}; numbers from CrucibleBalance.");
    }

    // Miyamoto's controller is the template: the same two-handed shape, and his states already
    // carry the special and riposte wiring. He has FOUR specials (Iaijutsu is combo-only); the
    // fourth has no Genghis counterpart and is deleted after the rename.
    static readonly (string from, string to)[] StateRenames =
    {
        ("Miyamoto_Idle",               "Genghis_Idle"),
        ("Miyamoto_Walk_Forward",       "Genghis_Walk_Forward"),
        ("Miyamoto_Walk_Back",          "Genghis_Walk_Back"),
        ("Miyamoto_Light_Thrust",       "Genghis_Light_SteppeCut"),
        ("Miyamoto_Medium_SideSlash",   "Genghis_Medium_HordeSlash"),
        ("Miyamoto_Heavy_Overhead",     "Genghis_Heavy_KhanCleave"),
        ("Miyamoto_Riposte_Dash",       "Genghis_Riposte_Dash"),
        ("Miyamoto_Riposte_Parry",      "Genghis_Riposte_Parry"),
        ("Miyamoto_Special_Nukitsuke",  "Genghis_Special_SteppeStorm"),
        ("Miyamoto_Special_NitoCross",  "Genghis_Special_HordeBreaker"),
        ("Miyamoto_Special_Kuruma",     "Genghis_Special_WrathOfTengri"),
    };

    [MenuItem("Crucible/Characters/Genghis/2 Build controller")]
    public static void BuildController()
    {
        if (!File.Exists(P2Ctrl))
        {
            Debug.LogError("P2_Miyamoto.controller is the template and it is missing.");
            return;
        }
        if (File.Exists(P5Ctrl))
        {
            if (!EditorUtility.DisplayDialog("P5_Genghis exists",
                    "Overwrite Animation/Controllers/P5_Genghis.controller with a fresh copy of P2_Miyamoto?",
                    "Overwrite", "Cancel")) return;
            AssetDatabase.DeleteAsset(P5Ctrl);
        }
        if (!AssetDatabase.CopyAsset(P2Ctrl, P5Ctrl)) { Debug.LogError("copy failed"); return; }
        AssetDatabase.ImportAsset(P5Ctrl);
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(P5Ctrl);
        var map = StateRenames.ToDictionary(r => r.from, r => r.to);
        int renamed = 0, removed = 0;
        foreach (var layer in ac.layers)
        {
            foreach (var cs in layer.stateMachine.states)
                if (map.TryGetValue(cs.state.name, out string to)) { cs.state.name = to; renamed++; }
            // Anything still named Miyamoto_* has no Genghis counterpart (Iaijutsu, the combo-only
            // fourth special). Removing the state removes its transitions with it.
            foreach (var cs in layer.stateMachine.states.Where(s => s.state.name.StartsWith("Miyamoto_")).ToArray())
            { layer.stateMachine.RemoveState(cs.state); removed++; }
            // Miyamoto's SP+Heavy fires his bespoke SpecialKuruma trigger; Genghis's AttackData
            // fires the shared SpecialHeavy, so the inherited Any State transition never matched
            // and the move played with the animator stuck on idle.
            foreach (var tr in layer.stateMachine.anyStateTransitions)
            {
                if (tr.destinationState == null || tr.destinationState.name != "Genghis_Special_WrathOfTengri") continue;
                foreach (var c in tr.conditions) tr.RemoveCondition(c);
                tr.AddCondition(AnimatorConditionMode.If, 0f, "SpecialHeavy");
            }
        }
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log($"P5_Genghis.controller built from P2_Miyamoto, {renamed} states renamed, {removed} leftover removed. " +
                  "Run 'Wire special and riposte states' then 'Use Mixamo packs'.");
    }

    [MenuItem("Crucible/Characters/Genghis/3 Add to the scene")]
    public static void AddToScene()
    {
        var p2 = GameObject.Find("Player2");
        if (p2 == null) { Debug.LogError("Player2 not found in the open scene"); return; }
        var existing = GameObject.Find("Player5");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Player5 exists", "Replace the Player5 already in the scene?",
                                             "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(existing);
        }

        var go = Object.Instantiate(p2);
        go.name = "Player5";
        Undo.RegisterCreatedObjectUndo(go, "add Genghis");
        // A bench spot: he only stands here between matches (the select screens stage him and
        // PlaceFighters puts whoever is fighting on the marks).
        go.transform.position = p2.transform.position + new Vector3(0f, 0f, 7f);
        go.transform.rotation = p2.transform.rotation;

        var fighter = go.GetComponent(Type("Fighter"));
        var sf = new SerializedObject(fighter);
        sf.FindProperty("playerNumber").intValue = 5;
        sf.FindProperty("characterName").stringValue = "Genghis";
        sf.FindProperty("maxHealth").intValue = 105;
        sf.FindProperty("maxStamina").intValue = 95;
        sf.FindProperty("armor").intValue = 18;
        sf.FindProperty("idleStateName").stringValue = "Genghis_Idle";
        var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DataPath);
        var dataProp = sf.FindProperty("fighterData");
        if (dataProp != null && data != null) dataProp.objectReferenceValue = data;
        sf.ApplyModifiedProperties();

        WireCombat(go);   // move set + the animator controller
        // Two-handed: the TwoHandGrip cloned from Miyamoto stays; Rebuild Player5 re-solves its
        // LeftGrip socket against the saber.

        Selection.activeGameObject = go;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Player5 (Genghis) cloned from Player2. Now run " +
                  "'Crucible > Characters > Rebuild Player5 (Genghis body)' to put his model on it.");
    }

    [MenuItem("Crucible/Characters/Genghis/4 Rewire Player5 to the move set")]
    public static void RewirePlayer5()
    {
        var go = GameObject.Find("Player5");
        if (go == null) { Debug.LogError("Player5 not found in the open scene"); return; }
        WireCombat(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Player5: controller, attacks, specials and both ripostes re-pointed at Genghis's move set.");
    }

    static void WireCombat(GameObject go)
    {
        var anim = go.GetComponent<Animator>();
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(P5Ctrl);
        if (ctrl == null) Debug.LogError($"{P5Ctrl} is missing; run '2 Build controller'");
        else anim.runtimeAnimatorController = ctrl;

        var sc = new SerializedObject(go.GetComponent(Type("CombatSystem")));
        SetList(sc.FindProperty("attacks"), "Genghis_Light_SteppeCut", "Genghis_Medium_HordeSlash",
                                            "Genghis_Heavy_KhanCleave");
        SetList(sc.FindProperty("specials"), "Genghis_Special_SteppeStorm", "Genghis_Special_HordeBreaker",
                                             "Genghis_Special_WrathOfTengri");
        sc.FindProperty("dashRiposte").objectReferenceValue = Attack("Genghis_Riposte_Dash");
        sc.FindProperty("parryRiposte").objectReferenceValue = Attack("Genghis_Riposte_Parry");
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
