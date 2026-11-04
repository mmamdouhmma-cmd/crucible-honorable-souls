// Story Chapter One (Miyamoto Musashi, "The Way of the Sword"): the chapter scene and its cast.
//
// The chapter lives in its OWN scene, Scenes/Story/Musashi_Chapter01.unity, a copy of the arena scene
// (Musashi_Chapter01_Arena.unity, which stays as the Bjorn/Miyamoto/Salaadin test scene). Run, in order:
//   1. Create the chapter scene      - copies the arena scene and opens the copy
//   2. Build the cast                - Musashi, the Sensei, the Partner and Arima (Kojiro's body), plus the
//                                      training dummy and the chapter's spots. Re-runnable.
// then save the scene. Tracker: CHAPTER_1_IMPLEMENTATION_TRACKER.md.
//
// Object names are chosen so `Apply the roster balance` can never write the wrong numbers here:
//   Player2 = Musashi  (the balance table writes MIYAMOTO's row to Player2 - right, he is Miyamoto's copycat)
//   Player7 = Sensei   (the table writes the Sensei's row to Player7)
//   Partner, Arima     (no table row has these names; their numbers are Miyamoto's, copied at creation)
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CrucibleChapterOne
{
    public const string ArenaScene = "Assets/_project/Scenes/Story/Musashi_Chapter01_Arena.unity";
    public const string ChapterScene = "Assets/_project/Scenes/Story/Musashi_Chapter01.unity";
    const string DummyMat = "Assets/_project/Environment/Musashi_Ch01/Ch01_TrainingDummy.mat";
    const string StrawMat = "Assets/_project/Environment/Musashi_Ch01/Ch01_TrainingDummyStraw.mat";

    // object name, body (CrucibleCharacterSetup id), shown name, roster order (playerNumber)
    static readonly (string obj, string body, string shown, int number)[] Cast =
    {
        ("Player2", "Ch1_Musashi", "Musashi",     1),
        ("Player7", "Ch1_Sensei",  "Sensei",      7),
        ("Partner", "Ch1_Partner", "Partner",     8),
        ("Arima",   "Ch1_Kojiro",  "Arima Kihei", 9),
    };

    // Spots in the dojo courtyard (Musashi_Dojo sits at the origin; the fight marks are x +-3.25, the camera
    // looks north from z -6.7, so +z is the background). x, z, facing yaw in degrees.
    static readonly (string name, float x, float z, float yaw)[] Spots =
    {
        ("Spot_Dummy",            2.4f, 6.2f, 270f),   // the training post, facing the partner
        ("Spot_PartnerPractice",  0.6f, 6.2f,  90f),   // the partner, facing the post
        ("Spot_SenseiObserve",   -6.2f, 3.6f, 120f),   // the sensei watching the sparring
        ("Spot_MusashiStart",    -3.25f, 0f,  90f),    // lesson marks = the dojo's own fight marks
        ("Spot_OpponentStart",    3.25f, 0f, 270f),
    };

    [MenuItem("Crucible/Story/Musashi Chapter One/Chapter 1 - 1 Create the chapter scene")]
    public static void CreateScene()
    {
        if (System.IO.File.Exists(ChapterScene)) { Debug.LogError($"{ChapterScene} already exists - open it and run 'Build the cast'"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!AssetDatabase.CopyAsset(ArenaScene, ChapterScene)) { Debug.LogError($"could not copy {ArenaScene}"); return; }
        EditorSceneManager.OpenScene(ChapterScene);
        Debug.Log($"Chapter 1: created {ChapterScene} from the arena scene. Now run 'Chapter 1 - 2 Build the cast'.");
    }

    [MenuItem("Crucible/Story/Musashi Chapter One/Chapter 1 - 2 Build the cast")]
    public static void BuildCast()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ChapterScene) { Debug.LogError($"Chapter 1: open {ChapterScene} first (active is {scene.path})"); return; }

        // Miyamoto (Player2) is the template for everyone: the two-handed rig setup, TwoHandGrip, WeaponAim,
        // his controller, moves and numbers. Clone BEFORE Player2 is rebuilt as Musashi.
        var template = GameObject.Find("Player2");
        if (template == null) { Debug.LogError("Chapter 1: Player2 not found"); return; }
        if (template.name == "Player2" && FighterName(template) != "Miyamoto" && FighterName(template) != "Musashi")
        { Debug.LogError($"Chapter 1: Player2 is {FighterName(template)}, expected Miyamoto (or Musashi on a re-run)"); return; }

        // The arena scene's other roster fighters do not belong in the chapter.
        foreach (var n in new[] { "Player1", "Player3", "Player4", "Player5", "Player6" })
        {
            var extra = GameObject.Find(n);
            if (extra != null) { Undo.DestroyObjectImmediate(extra); Debug.Log($"Chapter 1: removed {n}"); }
        }

        for (int i = 1; i < Cast.Length; i++)
        {
            var old = GameObject.Find(Cast[i].obj);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var go = Object.Instantiate(template);
            go.name = Cast[i].obj;
            Undo.RegisterCreatedObjectUndo(go, "chapter cast");
            go.transform.position = template.transform.position + new Vector3(0f, 0f, 4f * i);   // benched; the chapter places them
            go.transform.rotation = template.transform.rotation;
        }

        foreach (var c in Cast)
        {
            var go = GameObject.Find(c.obj);
            CrucibleCharacterSetup.Rebuild(c.obj, c.body);
            var sf = new SerializedObject(go.GetComponent(RuntimeType("Fighter")));
            sf.FindProperty("characterName").stringValue = c.shown;
            sf.FindProperty("playerNumber").intValue = c.number;
            sf.ApplyModifiedProperties();
            if (c.body == "Ch1_Sensei")
            {
                if (!CrucibleSensei.WireCombat(go)) return;
                CrucibleBalance.WriteVitals("Ch1_Sensei", go);
            }
            else if (c.body == "Ch1_Kojiro")
            {   // Arima Kihei: his own borrowed move set (CrucibleArima) on Kojiro's body
                if (!CrucibleArima.WireCombat(go)) return;
                CrucibleBalance.WriteVitals("Ch1_Arima", go);
            }
            else CrucibleBalance.WriteVitals("Miyamoto", go);   // Musashi and the Partner run Miyamoto's numbers
        }

        BuildSpots();
        BuildDummy();
        MarkStoryScene();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Chapter 1: cast built - Musashi (Player2), Sensei (Player7), Partner, Arima (Kojiro's body, his own moves); dummy and spots placed. Save the scene.");
    }

    /// <summary>The chapter scene's menu runs no boot or main menu (GameMenu.storyScene).</summary>
    static void MarkStoryScene()
    {
        var menu = Object.FindAnyObjectByType(RuntimeType("GameMenu")) as Component;
        if (menu == null) { Debug.LogError("Chapter 1: no GameMenu in the scene"); return; }
        var so = new SerializedObject(menu);
        so.FindProperty("storyScene").boolValue = true;
        so.FindProperty("openOnBattleSetup").boolValue = false;
        so.ApplyModifiedProperties();
    }

    const string StepsJson = "Assets/_project/Story/Chapter1/ch1_steps.json";
    const string LinesJson = "Assets/_project/Story/Chapter1/ch1_lines.json";
    const string StoryFont = "Assets/_project/Fonts/NotoSansJP-Story.otf";

    /// <summary>The story layer: one "Story" object carrying the chapter runner (steps, dialogue, spots) and the
    /// subtitle/objective display. Re-runnable; save the scene afterwards.</summary>
    [MenuItem("Crucible/Story/Musashi Chapter One/Chapter 1 - 4 Add the story layer")]
    public static void AddStoryLayer()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ChapterScene) { Debug.LogError($"Chapter 1: open {ChapterScene} first"); return; }
        var steps = AssetDatabase.LoadAssetAtPath<TextAsset>(StepsJson);
        var lines = AssetDatabase.LoadAssetAtPath<TextAsset>(LinesJson);
        var font = AssetDatabase.LoadAssetAtPath<Font>(StoryFont);
        if (steps == null || lines == null || font == null) { Debug.LogError("Chapter 1: steps / lines / font asset missing"); return; }

        var go = GameObject.Find("Story");
        if (go == null) { go = new GameObject("Story"); Undo.RegisterCreatedObjectUndo(go, "story layer"); }
        var subType = RuntimeType("StorySubtitles");
        var runType = RuntimeType("ChapterRunner");
        var sub = go.GetComponent(subType) ?? go.AddComponent(subType);
        var run = go.GetComponent(runType) ?? go.AddComponent(runType);

        var so = new SerializedObject(sub);
        so.FindProperty("japaneseFont").objectReferenceValue = font;
        so.ApplyModifiedProperties();
        so = new SerializedObject(run);
        so.FindProperty("stepsJson").objectReferenceValue = steps;
        so.FindProperty("linesJson").objectReferenceValue = lines;
        so.FindProperty("spotRoot").objectReferenceValue = SpotsRoot();
        so.FindProperty("subtitles").objectReferenceValue = sub;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Chapter 1: story layer added (ChapterRunner + StorySubtitles on 'Story'). Save the scene.");
    }

    /// <summary>Puts the chapter scene in the Editor's scene list, after testarena, so Main menu > Story can load it
    /// in play mode. The player builds (CrucibleBuild) pass their OWN scene list and are not changed by this.</summary>
    [MenuItem("Crucible/Story/Musashi Chapter One/Chapter 1 - 3 Add the chapter to the Editor scene list")]
    public static void AddToSceneList()
    {
        var list = EditorBuildSettings.scenes.ToList();
        if (list.Any(s => s.path == ChapterScene)) { Debug.Log("Chapter 1: already in the scene list"); return; }
        list.Add(new EditorBuildSettingsScene(ChapterScene, true));
        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log($"Chapter 1: scene list is now {string.Join(", ", list.Select(s => s.path))}");
    }

    static Transform SpotsRoot()
    {
        var root = GameObject.Find("Chapter1_Spots");
        if (root == null) { root = new GameObject("Chapter1_Spots"); Undo.RegisterCreatedObjectUndo(root, "chapter spots"); }
        return root.transform;
    }

    static void BuildSpots()
    {
        var root = SpotsRoot();
        foreach (var s in Spots)
        {
            var t = root.Find(s.name);
            if (t == null) { t = new GameObject(s.name).transform; t.SetParent(root, false); }
            t.position = new Vector3(s.x, GroundY(s.x, s.z), s.z);
            t.rotation = Quaternion.Euler(0f, s.yaw, 0f);
        }
    }

    /// <summary>A plain wooden training post (the user's call: a placeholder, swapped for a model later).
    /// Sized against the fighters (~2.8 world units tall): a 2.4 post, arms at shoulder height, a straw wrap.</summary>
    static void BuildDummy()
    {
        var old = GameObject.Find("Ch01_TrainingDummy");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var spot = SpotsRoot().Find("Spot_Dummy");

        var root = new GameObject("Ch01_TrainingDummy");
        Undo.RegisterCreatedObjectUndo(root, "training dummy");
        root.transform.SetPositionAndRotation(spot.position, spot.rotation);
        var wood = Mat(DummyMat, new Color(0.42f, 0.29f, 0.17f), 0.25f);
        var straw = Mat(StrawMat, new Color(0.72f, 0.62f, 0.38f), 0.1f);

        Part(root, PrimitiveType.Cylinder, "Post",  new Vector3(0f, 1.2f, 0f),  Vector3.zero,              new Vector3(0.26f, 1.2f, 0.26f), wood);
        Part(root, PrimitiveType.Cylinder, "Wrap",  new Vector3(0f, 1.45f, 0f), Vector3.zero,              new Vector3(0.44f, 0.38f, 0.44f), straw);
        Part(root, PrimitiveType.Cylinder, "Arms",  new Vector3(0f, 1.95f, 0f), new Vector3(0f, 0f, 90f),  new Vector3(0.12f, 0.75f, 0.12f), wood);
        Part(root, PrimitiveType.Sphere,   "Head",  new Vector3(0f, 2.55f, 0f), Vector3.zero,              new Vector3(0.36f, 0.42f, 0.36f), straw);
        Part(root, PrimitiveType.Cylinder, "Base",  new Vector3(0f, 0.06f, 0f), Vector3.zero,              new Vector3(0.7f, 0.06f, 0.7f),  wood);

        var col = root.AddComponent<CapsuleCollider>();   // so a walking Musashi does not pass through it
        col.center = new Vector3(0f, 1.3f, 0f);
        col.height = 2.6f;
        col.radius = 0.3f;
    }

    static void Part(GameObject root, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
    {
        var p = GameObject.CreatePrimitive(type);
        p.name = name;
        Object.DestroyImmediate(p.GetComponent<Collider>());
        p.transform.SetParent(root.transform, false);
        p.transform.localPosition = pos;
        p.transform.localRotation = Quaternion.Euler(euler);
        p.transform.localScale = scale;
        p.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    static Material Mat(string path, Color c, float smooth)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>The highest non-trigger, non-fighter surface under (x, z).</summary>
    static float GroundY(float x, float z)
    {
        Physics.SyncTransforms();
        float best = float.NegativeInfinity;
        foreach (var h in Physics.RaycastAll(new Vector3(x, 200f, z), Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent(RuntimeType("Fighter")) != null) continue;
            if (h.collider.name.StartsWith("Ch01_TrainingDummy")) continue;
            best = Mathf.Max(best, h.point.y);
        }
        if (float.IsNegativeInfinity(best)) { Debug.LogWarning($"Chapter 1: no ground under ({x}, {z}); using 0"); return 0f; }
        return best;
    }

    static string FighterName(GameObject go)
    {
        var f = go.GetComponent(RuntimeType("Fighter"));
        return f == null ? "" : new SerializedObject(f).FindProperty("characterName").stringValue;
    }

    static System.Type RuntimeType(string name)
    {
        var t = System.Type.GetType(name + ", Assembly-CSharp");
        if (t == null) throw new System.Exception("runtime type " + name + " not found");
        return t;
    }
}
