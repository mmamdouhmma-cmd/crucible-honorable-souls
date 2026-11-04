// The story BOSS of Chapter One, ARIMA KIHEI - a boastful Shinto-ryu duellist on Kojiro's body for now
// (the user's call, 2026-11-03; the bodies swap later). Every strike is borrowed from the roster, the
// user's picks 2026-11-04 - none from Musashi (Miyamoto's clips) or the Sensei, so he reads as neither:
//   Light      Proud Edge         = Bjorn's Light          (AxePack "standing melee attack horizontal")
//   Medium     Severing Cut       = Richard's Medium       (SwordShieldPack "sword and shield slash (5)", the low cut)
//   Heavy      Arima's Verdict    = Richard's Light        (AxePack "standing melee attack downward", the barbarian overhead).
//              Until 2026-11-04 it was Bjorn's parry riposte (AxePack2H, same clip name, two-handed): measured against
//              Musashi its blade stayed 1.43-1.60 ABOVE his head through the whole hit window - the user saw it pass
//              over him every time. The two-handed overheads in the GreatSwordPack are Musashi's and the Sensei's.
//   SP+Light   Rising Crane       = Richard's SP+Light     (AxePack "standing melee attack backhand")
//   SP+Medium  Whirlwind Advance  = Bjorn's SP+Medium      (AxePack "standing melee attack 360 low")
//   SP+Heavy   Heaven's Fall      = Salaadin's SP+Heavy    (SwordShieldPack "sword and shield slash (4)", the spinning jumping slash)
//   Idle / walks / guard / dashes / reacts / death = Miyamoto's (Kojiro's own animation is postponed).
// Both hands on the hilt in his stance, Light, Heavy and SP+Medium (CrucibleCharacterSetup.GripOnlyInStates).
// Numbers: CrucibleBalance ("Ch1_Arima"), a step above Musashi's.
//
// He lives ONLY in Scenes/Story/Musashi_Chapter01.unity, on the object "Arima". Order on a fresh clone:
//   Crucible > Characters > Arima > 1 Create move set     (AttackData + arima_data)
//   Crucible > Characters > Arima > 2 Build controller    (P8_Arima from P2_Miyamoto)
//   Crucible > Animation  > Use Mixamo packs               (in TESTARENA: it fits every fighter's walk to the
//                                                           fighter in the open scene; Arima's come from the table)
//   Crucible > Animation  > Wire special and riposte states
//   Crucible > Combat     > Wire combo strings
//   open Musashi_Chapter01, then
//   Crucible > Characters > Arima > 3 Wire Arima (chapter scene)   (also run by Chapter 1 - 2 Build the cast)
//
// Each move copies the HIT GEOMETRY of the move whose animation it plays (height, comesFrom, hitSide,
// aim, box, range, lunge), and its state takes that move's per-STATE contact in swing_contacts.json.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleArima
{
    const string Dir = "Assets/_project/Characters/Ch1_Arima";
    const string AttackDir = Dir + "/Attacks";
    const string Chars = "Assets/_project/Characters";
    const string P2Ctrl = "Assets/_project/Animation/Controllers/P2_Miyamoto.controller";
    const string P8Ctrl = "Assets/_project/Animation/Controllers/P8_Arima.controller";

    const int Light = 0, Medium = 1, Heavy = 2, Special = 3;

    struct Move
    {
        public string asset, name, trigger, source;
        public int type, specialButton;
    }

    static readonly Move[] Moves =
    {
        new Move { asset = "Arima_Light_ProudEdge",          name = "Proud Edge",        trigger = "LightAttack",
                   type = Light,   source = "Bjorn/Attacks/Bjorn_Light_Slash" },
        new Move { asset = "Arima_Medium_SeveringCut",       name = "Severing Cut",      trigger = "MediumAttack",
                   type = Medium,  source = "Richard/Attacks/Richard_Medium_Slash" },
        new Move { asset = "Arima_Heavy_ArimasVerdict",      name = "Arima's Verdict",   trigger = "HeavyAttack",
                   type = Heavy,   source = "Richard/Attacks/Richard_Light_Thrust" },
        new Move { asset = "Arima_Special_RisingCrane",      name = "Rising Crane",      trigger = "SpecialLight",
                   type = Special, specialButton = Light,  source = "Richard/Attacks/Richard_Special_ShieldCharge" },
        new Move { asset = "Arima_Special_WhirlwindAdvance", name = "Whirlwind Advance", trigger = "SpecialMedium",
                   type = Special, specialButton = Medium, source = "Bjorn/Attacks/Bjorn_Special_HewingCharge" },
        new Move { asset = "Arima_Special_HeavensFall",      name = "Heaven's Fall",     trigger = "SpecialHeavy",
                   type = Special, specialButton = Heavy,  source = "salaadin/Attacks/Salaadin_Special_HattinCleave" },
    };

    static readonly string[] Geometry =
    {
        "attackHeight", "comesFrom", "hitSide", "aimMode", "aimRollDegrees",
        "hitboxRange", "hitboxSize", "hitboxOffset", "lungeMaxDistance", "lungeStopDistance", "hitsWithWeapon",
    };

    [MenuItem("Crucible/Characters/Arima/1 Create move set")]
    public static void CreateMoves()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(Chars, "Ch1_Arima");
        if (!AssetDatabase.IsValidFolder(AttackDir)) AssetDatabase.CreateFolder(Dir, "Attacks");
        foreach (var m in Moves)
        {
            var src = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{Chars}/{m.source}.asset");
            if (src == null) { Debug.LogError($"Arima: source move {m.source} missing"); continue; }
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
        CrucibleBalance.Apply();   // damage / stamina / frames / flow / hyper armour / lunge overrides + arima_data
        AssetDatabase.SaveAssets();
        Debug.Log($"Arima: {Moves.Length} attacks written to {AttackDir}; numbers from CrucibleBalance.");
    }

    // Miyamoto's controller is the template; his ripostes and fourth special have no Arima counterpart.
    static readonly (string from, string to)[] StateRenames =
    {
        ("Miyamoto_Idle",               "Arima_Idle"),
        ("Miyamoto_Walk_Forward",       "Arima_Walk_Forward"),
        ("Miyamoto_Walk_Back",          "Arima_Walk_Back"),
        ("Miyamoto_Light_Thrust",       "Arima_Light_ProudEdge"),
        ("Miyamoto_Medium_SideSlash",   "Arima_Medium_SeveringCut"),
        ("Miyamoto_Heavy_Overhead",     "Arima_Heavy_ArimasVerdict"),
        ("Miyamoto_Special_Nukitsuke",  "Arima_Special_RisingCrane"),
        ("Miyamoto_Special_NitoCross",  "Arima_Special_WhirlwindAdvance"),
        ("Miyamoto_Special_Kuruma",     "Arima_Special_HeavensFall"),
    };

    [MenuItem("Crucible/Characters/Arima/2 Build controller")]
    public static void BuildController()
    {
        if (File.Exists(P8Ctrl)) AssetDatabase.DeleteAsset(P8Ctrl);
        if (!AssetDatabase.CopyAsset(P2Ctrl, P8Ctrl)) { Debug.LogError("copy of P2_Miyamoto failed"); return; }
        AssetDatabase.ImportAsset(P8Ctrl);
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(P8Ctrl);
        var map = StateRenames.ToDictionary(r => r.from, r => r.to);
        int renamed = 0, removed = 0;
        foreach (var layer in ac.layers)
        {
            foreach (var cs in layer.stateMachine.states)
                if (map.TryGetValue(cs.state.name, out string to)) { cs.state.name = to; renamed++; }
            foreach (var cs in layer.stateMachine.states.Where(s => s.state.name.StartsWith("Miyamoto_")).ToArray())
            { layer.stateMachine.RemoveState(cs.state); removed++; }
            // Miyamoto's SP+Heavy fires his bespoke SpecialKuruma trigger; Arima's fires the shared SpecialHeavy.
            foreach (var tr in layer.stateMachine.anyStateTransitions)
            {
                if (tr.destinationState == null || tr.destinationState.name != "Arima_Special_HeavensFall") continue;
                foreach (var c in tr.conditions) tr.RemoveCondition(c);
                tr.AddCondition(AnimatorConditionMode.If, 0f, "SpecialHeavy");
            }
        }
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log($"P8_Arima.controller built from P2_Miyamoto: {renamed} states renamed, {removed} removed. " +
                  "Run 'Use Mixamo packs' next.");
    }

    [MenuItem("Crucible/Characters/Arima/3 Wire Arima (chapter scene)")]
    public static void WireInChapter()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (scene.path != CrucibleChapterOne.ChapterScene) { Debug.LogError($"Arima: open {CrucibleChapterOne.ChapterScene} first"); return; }
        var go = GameObject.Find("Arima");
        if (go == null) { Debug.LogError("Arima: no object named Arima - run 'Chapter 1 - 2 Build the cast'"); return; }
        if (!WireCombat(go)) return;
        CrucibleBalance.WriteVitals("Ch1_Arima", go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Arima wired: P8_Arima controller, his six moves, his vitals. Save the scene.");
    }

    public static bool WireCombat(GameObject go)
    {
        var anim = go.GetComponent<Animator>();
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(P8Ctrl);
        if (ctrl == null) { Debug.LogError($"{P8Ctrl} missing; run 'Arima > 2 Build controller'"); return false; }
        anim.runtimeAnimatorController = ctrl;

        var sf = new SerializedObject(go.GetComponent(Type("Fighter")));
        sf.FindProperty("characterName").stringValue = "Arima Kihei";
        sf.FindProperty("idleStateName").stringValue = "Arima_Idle";
        sf.ApplyModifiedProperties();

        var sc = new SerializedObject(go.GetComponent(Type("CombatSystem")));
        SetList(sc.FindProperty("attacks"), Moves.Where(m => m.type != Special).Select(m => m.asset).ToArray());
        SetList(sc.FindProperty("specials"), Moves.Where(m => m.type == Special).Select(m => m.asset).ToArray());
        sc.FindProperty("dashRiposte").objectReferenceValue = null;
        sc.FindProperty("parryRiposte").objectReferenceValue = null;
        sc.ApplyModifiedProperties();
        return true;
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
