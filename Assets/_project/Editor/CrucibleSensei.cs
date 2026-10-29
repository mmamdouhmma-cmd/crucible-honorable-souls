// The story SENSEI (Chapter One) - a wooden-sword master who fights ONE-handed and borrows every
// strike from the roster (the user's picks, 2026-10-29):
//   Light      = Richard's Heavy        (SwordShieldPack "sword and shield slash")
//   Medium     = Salaadin's Heavy       (the SAME file - only the per-state contact separates them)
//   Heavy      = Bjorn's Heavy          (GreatSwordPack "great sword slash", the two-handed overhead)
//   SP+Light   = Genghis's SP+Heavy     (GreatSwordPack "great sword casting")
//   SP+Medium  = Salaadin's SP+Medium   (SwordShieldPack "sword and shield attack (3)")
//   SP+Heavy   = Bjorn's Heavy again    (the only other move he grips with both hands)
//   Block      = Bjorn's guard; idle / walk / dash / react / death = Miyamoto's.
// Numbers sit between Miyamoto's and Salaadin's, health under both (CrucibleBalance).
//
// He lives ONLY in Scenes/Story/Ch1_Test_Sensei.unity, on Player2. Order on a fresh clone:
//   Crucible > Characters > Sensei > 1 Create move set     (AttackData + sensei_data)
//   Crucible > Characters > Sensei > 2 Build controller    (P7_Sensei from P2_Miyamoto)
//   Crucible > Animation  > Use Mixamo packs               (in testarena - it wires every fighter)
//   Crucible > Combat     > Wire combo strings
//   open Ch1_Test_Sensei, then
//   Crucible > Story > Chapter One > Rebuild Player2 as the Sensei
//   Crucible > Characters > Sensei > 3 Wire Player2 (test scene)
//   Crucible > Combat > Apply the roster balance           (writes his vitals in his scene only)
//
// Each move copies the HIT GEOMETRY of the move whose animation it plays (height, comesFrom,
// hitSide, aim, box, range, lunge): a box that does not match the swing on screen reads unfair.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CrucibleSensei
{
    const string Dir = "Assets/_project/Characters/Ch1_Sensei";
    const string AttackDir = Dir + "/Attacks";
    const string Chars = "Assets/_project/Characters";
    const string P2Ctrl = "Assets/_project/Animation/Controllers/P2_Miyamoto.controller";
    const string P7Ctrl = "Assets/_project/Animation/Controllers/P7_Sensei.controller";
    const string TestScene = "Ch1_Test_Sensei";

    const int Light = 0, Medium = 1, Heavy = 2, Special = 3;

    struct Move
    {
        public string asset, name, trigger, source;
        public int type, specialButton;
    }

    static readonly Move[] Moves =
    {
        new Move { asset = "Sensei_Light_FallingBranch",    name = "Falling Branch",   trigger = "LightAttack",
                   type = Light,   source = "Richard/Attacks/Richard_Heavy_Overhead" },
        new Move { asset = "Sensei_Medium_TempleBell",      name = "Temple Bell",      trigger = "MediumAttack",
                   type = Medium,  source = "salaadin/Attacks/Salaadin_Heavy_WhirlingSaif" },
        new Move { asset = "Sensei_Heavy_MountainSplitter", name = "Mountain Splitter", trigger = "HeavyAttack",
                   type = Heavy,   source = "Bjorn/Attacks/Bjorn_Heavy_OverheadDoubleAxe" },
        new Move { asset = "Sensei_Special_RisingHeaven",   name = "Rising Heaven",    trigger = "SpecialLight",
                   type = Special, specialButton = Light,  source = "Genghis/Attacks/Genghis_Special_WrathOfTengri" },
        new Move { asset = "Sensei_Special_CircleOfWillow", name = "Circle of Willow", trigger = "SpecialMedium",
                   type = Special, specialButton = Medium, source = "salaadin/Attacks/Salaadin_Special_CrescentSlash" },
        new Move { asset = "Sensei_Special_FinalLesson",    name = "Final Lesson",     trigger = "SpecialHeavy",
                   type = Special, specialButton = Heavy,  source = "Bjorn/Attacks/Bjorn_Heavy_OverheadDoubleAxe" },
    };

    // geometry fields taken verbatim from the source move
    static readonly string[] Geometry =
    {
        "attackHeight", "comesFrom", "hitSide", "aimMode", "aimRollDegrees",
        "hitboxRange", "hitboxSize", "hitboxOffset", "lungeMaxDistance", "lungeStopDistance", "hitsWithWeapon",
    };

    [MenuItem("Crucible/Characters/Sensei/1 Create move set")]
    public static void CreateMoves()
    {
        if (!AssetDatabase.IsValidFolder(AttackDir)) AssetDatabase.CreateFolder(Dir, "Attacks");
        foreach (var m in Moves)
        {
            var src = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{Chars}/{m.source}.asset");
            if (src == null) { Debug.LogError($"Sensei: source move {m.source} missing"); continue; }
            string path = $"{AttackDir}/{m.asset}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance("AttackData");
                AssetDatabase.CreateAsset(asset, path);
            }
            var so = new SerializedObject(asset);
            var ss = new SerializedObject(src);
            foreach (var f in Geometry) so.CopyFromSerializedProperty(ss.FindProperty(f));
            so.FindProperty("attackName").stringValue = m.name;
            so.FindProperty("animatorTriggerName").stringValue = m.trigger;
            so.FindProperty("attackType").enumValueIndex = m.type;
            so.FindProperty("specialButton").enumValueIndex = m.specialButton;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
        }
        CrucibleBalance.Apply();   // damage / stamina / frames / flow / hyper armour / lunge overrides
        AssetDatabase.SaveAssets();
        Debug.Log($"Sensei: {Moves.Length} attacks written to {AttackDir}; numbers from CrucibleBalance.");
    }

    // Miyamoto's controller is the template (his idle, walks, dashes, reacts and death are the
    // Sensei's). Ripostes and the fourth special have no Sensei counterpart and are removed.
    static readonly (string from, string to)[] StateRenames =
    {
        ("Miyamoto_Idle",               "Sensei_Idle"),
        ("Miyamoto_Walk_Forward",       "Sensei_Walk_Forward"),
        ("Miyamoto_Walk_Back",          "Sensei_Walk_Back"),
        ("Miyamoto_Light_Thrust",       "Sensei_Light_FallingBranch"),
        ("Miyamoto_Medium_SideSlash",   "Sensei_Medium_TempleBell"),
        ("Miyamoto_Heavy_Overhead",     "Sensei_Heavy_MountainSplitter"),
        ("Miyamoto_Special_Nukitsuke",  "Sensei_Special_RisingHeaven"),
        ("Miyamoto_Special_NitoCross",  "Sensei_Special_CircleOfWillow"),
        ("Miyamoto_Special_Kuruma",     "Sensei_Special_FinalLesson"),
    };

    [MenuItem("Crucible/Characters/Sensei/2 Build controller")]
    public static void BuildController()
    {
        if (File.Exists(P7Ctrl)) AssetDatabase.DeleteAsset(P7Ctrl);
        if (!AssetDatabase.CopyAsset(P2Ctrl, P7Ctrl)) { Debug.LogError("copy of P2_Miyamoto failed"); return; }
        AssetDatabase.ImportAsset(P7Ctrl);
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(P7Ctrl);
        var map = StateRenames.ToDictionary(r => r.from, r => r.to);
        int renamed = 0, removed = 0;
        foreach (var layer in ac.layers)
        {
            foreach (var cs in layer.stateMachine.states)
                if (map.TryGetValue(cs.state.name, out string to)) { cs.state.name = to; renamed++; }
            foreach (var cs in layer.stateMachine.states.Where(s => s.state.name.StartsWith("Miyamoto_")).ToArray())
            { layer.stateMachine.RemoveState(cs.state); removed++; }
            // Miyamoto's SP+Heavy fires his bespoke SpecialKuruma trigger; the Sensei's fires the
            // shared SpecialHeavy (the same fix Genghis needed).
            foreach (var tr in layer.stateMachine.anyStateTransitions)
            {
                if (tr.destinationState == null || tr.destinationState.name != "Sensei_Special_FinalLesson") continue;
                foreach (var c in tr.conditions) tr.RemoveCondition(c);
                tr.AddCondition(AnimatorConditionMode.If, 0f, "SpecialHeavy");
            }
        }
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log($"P7_Sensei.controller built from P2_Miyamoto: {renamed} states renamed, {removed} removed. " +
                  "Run 'Use Mixamo packs' (in testarena) next.");
    }

    [MenuItem("Crucible/Characters/Sensei/3 Wire Player2 (test scene)")]
    public static void WirePlayer2()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (scene.name != TestScene) { Debug.LogError($"Sensei: open Scenes/Story/{TestScene}.unity first (active is {scene.name})"); return; }
        var go = GameObject.Find("Player2");
        if (go == null) { Debug.LogError("Player2 not found"); return; }

        var anim = go.GetComponent<Animator>();
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(P7Ctrl);
        if (ctrl == null) { Debug.LogError($"{P7Ctrl} missing; run '2 Build controller'"); return; }
        anim.runtimeAnimatorController = ctrl;

        var sf = new SerializedObject(go.GetComponent(Type("Fighter")));
        sf.FindProperty("characterName").stringValue = "Sensei";
        sf.FindProperty("idleStateName").stringValue = "Sensei_Idle";
        sf.ApplyModifiedProperties();

        var sc = new SerializedObject(go.GetComponent(Type("CombatSystem")));
        SetList(sc.FindProperty("attacks"), Moves.Where(m => m.type != Special).Select(m => m.asset).ToArray());
        SetList(sc.FindProperty("specials"), Moves.Where(m => m.type == Special).Select(m => m.asset).ToArray());
        sc.FindProperty("dashRiposte").objectReferenceValue = null;
        sc.FindProperty("parryRiposte").objectReferenceValue = null;
        sc.ApplyModifiedProperties();

        CrucibleBalance.Apply();   // his vitals onto Player2 (this scene only) + sensei_data
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Player2 is the Sensei: P7_Sensei controller, his six moves, his vitals. Save the scene.");
    }

    static void SetList(SerializedProperty arr, params string[] names)
    {
        arr.arraySize = names.Length;
        for (int i = 0; i < names.Length; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{AttackDir}/{names[i]}.asset");
    }

    static System.Type Type(string name)
    {
        var t = System.Type.GetType(name + ", Assembly-CSharp");
        if (t == null) throw new System.Exception("runtime type " + name + " not found");
        return t;
    }
}
