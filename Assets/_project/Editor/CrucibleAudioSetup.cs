using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires the audio pass (Design/Plan_Audio.md). Clips live under Assets/_project/Audio/ and are
/// matched BY FILE NAME PREFIX, so dropping in more variants and re-running the menu item is the
/// whole workflow - no component fiddling:
///
///   Audio/UI/        navigate*, select*, back*, wipe*, boot*, count_beat*, count_fight*
///   Audio/Combat/    swish*, block*, parry*, hit*, dodge*
///   Audio/Voice/&lt;Character&gt;/   effort_light*, effort_heavy*, death*
///   Audio/Music/     menu*, fight*          (the user supplies these)
///   Audio/Ambience/  wind*
///
/// `Set up audio` creates the folders, fixes each clip's import settings for its category,
/// puts a CrucibleAudio component on the GameMangers object, assigns every bank and SAVES THE
/// SCENE (the component's slots are scene state, which the CLI does not save for you).
/// Written through SerializedObject because Crucible.Editor cannot reference Assembly-CSharp.
/// </summary>
public static class CrucibleAudioSetup
{
    const string AudioRoot = "Assets/_project/Audio";

    static Type AudioType => Type.GetType("CrucibleAudio, Assembly-CSharp");

    [MenuItem("Crucible/Audio/Set up audio")]
    public static void Setup()
    {
        foreach (var dir in new[] { "UI", "Combat", "Voice", "Music", "Ambience",
                                    "Voice/Bjorn", "Voice/Miyamoto", "Voice/Richard", "Voice/Salaadin" })
        {
            string path = AudioRoot + "/" + dir;
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder(Path.GetDirectoryName(parent).Replace('\\', '/'), Path.GetFileName(parent));
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }

        FixImportSettings();

        var audio = FindOrAddComponent();
        if (audio == null) { Debug.LogError("Audio setup: no GameMangers object (or no CrucibleAudio type) in the scene"); return; }

        var so = new SerializedObject(audio);

        SetClips(so.FindProperty("uiNavigate"), Clips("UI", "navigate"));
        SetClips(so.FindProperty("uiSelect"), Clips("UI", "select"));
        SetClips(so.FindProperty("uiBack"), Clips("UI", "back"));
        SetClips(so.FindProperty("uiWipe"), Clips("UI", "wipe"));
        SetClips(so.FindProperty("uiBoot"), Clips("UI", "boot"));
        SetClips(so.FindProperty("uiCountBeat"), Clips("UI", "count_beat"));
        SetClips(so.FindProperty("uiCountFight"), Clips("UI", "count_fight"));

        SetClips(so.FindProperty("swish"), Clips("Combat", "swish"));
        SetClips(so.FindProperty("block"), Clips("Combat", "block"));
        SetClips(so.FindProperty("parry"), Clips("Combat", "parry"));
        SetClips(so.FindProperty("hit"), Clips("Combat", "hit"));
        SetClips(so.FindProperty("dodge"), Clips("Combat", "dodge"));

        var charDirs = AssetDatabase.GetSubFolders(AudioRoot + "/Voice");
        var voices = so.FindProperty("voices");
        voices.arraySize = charDirs.Length;
        for (int i = 0; i < charDirs.Length; i++)
        {
            string name = Path.GetFileName(charDirs[i]);
            var bank = voices.GetArrayElementAtIndex(i);
            bank.FindPropertyRelative("character").stringValue = name;
            SetClips(bank.FindPropertyRelative("effortLight"), Clips("Voice/" + name, "effort_light"));
            SetClips(bank.FindPropertyRelative("effortHeavy"), Clips("Voice/" + name, "effort_heavy"));
            SetClips(bank.FindPropertyRelative("death"), Clips("Voice/" + name, "death"));
        }

        so.FindProperty("menuMusic").objectReferenceValue = Clips("Music", "menu").FirstOrDefault();
        so.FindProperty("fightMusic").objectReferenceValue = Clips("Music", "fight").FirstOrDefault();
        so.FindProperty("windLoop").objectReferenceValue = Clips("Ambience", "wind").FirstOrDefault();

        so.ApplyModifiedPropertiesWithoutUndo();

        var comp = (Component)audio;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(comp.gameObject.scene);

        Report();
    }

    [MenuItem("Crucible/Audio/Report audio wiring")]
    public static void Report()
    {
        var audio = Find();
        if (audio == null) { Debug.Log("Audio: NO CrucibleAudio component in the scene - run Set up audio"); return; }
        var so = new SerializedObject(audio);

        string Count(string prop, SerializedProperty parent = null)
        {
            var p = parent != null ? parent.FindPropertyRelative(prop) : so.FindProperty(prop);
            return p == null || p.arraySize == 0 ? "MISSING" : p.arraySize + " clip(s)";
        }
        string One(string prop)
        {
            var c = so.FindProperty(prop).objectReferenceValue as AudioClip;
            return c == null ? "MISSING" : $"{c.name} ({c.length:0.0}s)";
        }

        var lines = new List<string>
        {
            $"UI: navigate {Count("uiNavigate")}, select {Count("uiSelect")}, back {Count("uiBack")}, " +
            $"wipe {Count("uiWipe")}, boot {Count("uiBoot")}, count {Count("uiCountBeat")}/{Count("uiCountFight")}",
            $"Combat: swish {Count("swish")}, block {Count("block")}, parry {Count("parry")}, " +
            $"hit {Count("hit")}, dodge {Count("dodge")}",
            $"Music: menu {One("menuMusic")}, fight {One("fightMusic")}; wind {One("windLoop")}",
        };
        var voices = so.FindProperty("voices");
        for (int i = 0; i < voices.arraySize; i++)
        {
            var b = voices.GetArrayElementAtIndex(i);
            lines.Add($"Voice {b.FindPropertyRelative("character").stringValue}: " +
                      $"light {Count("effortLight", b)}, heavy {Count("effortHeavy", b)}, death {Count("death", b)}");
        }

        Debug.Log("Audio wiring:\n" + string.Join("\n", lines));
    }

    static void SetClips(SerializedProperty array, AudioClip[] clips)
    {
        if (array == null) { Debug.LogWarning("Audio setup: a clip slot is missing on CrucibleAudio"); return; }
        array.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }

    static UnityEngine.Object Find()
    {
        var t = AudioType;
        return t == null ? null : UnityEngine.Object.FindAnyObjectByType(t, FindObjectsInactive.Include);
    }

    static UnityEngine.Object FindOrAddComponent()
    {
        var existing = Find();
        if (existing != null) return existing;
        var t = AudioType;
        if (t == null) return null;

        // The managers object carries the InputManager singleton; the audio belongs beside it.
        var managers = GameObject.Find("GameMangers");
        if (managers == null)
        {
            var imType = Type.GetType("InputManager, Assembly-CSharp");
            var im = imType != null ? UnityEngine.Object.FindAnyObjectByType(imType, FindObjectsInactive.Include) as Component : null;
            if (im != null) managers = im.gameObject;
        }
        return managers != null ? managers.AddComponent(t) : null;
    }

    static AudioClip[] Clips(string folder, string prefix)
    {
        string dir = AudioRoot + "/" + folder;
        if (!AssetDatabase.IsValidFolder(dir)) return new AudioClip[0];
        return AssetDatabase.FindAssets("t:AudioClip", new[] { dir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == dir)   // no spill between voice folders
            .Where(p => Path.GetFileNameWithoutExtension(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
            .Where(c => c != null)
            .ToArray();
    }

    /// <summary>
    /// Import settings per category: music and the wind stream (long, one instance at a time);
    /// everything else is a short one-shot that decompresses on load and goes mono - it all plays
    /// centre-panned 2D, and mono halves what the web build carries.
    /// </summary>
    static void FixImportSettings()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) continue;

            bool stream = path.Contains("/Music/") || path.Contains("/Ambience/");
            var wanted = new AudioImporterSampleSettings
            {
                loadType = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = stream ? 0.7f : 0.8f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
            };

            var current = importer.defaultSampleSettings;
            bool mono = !stream;
            if (current.loadType != wanted.loadType || current.compressionFormat != wanted.compressionFormat ||
                Math.Abs(current.quality - wanted.quality) > 0.01f || importer.forceToMono != mono)
            {
                importer.defaultSampleSettings = wanted;
                importer.forceToMono = mono;
                importer.loadInBackground = stream;
                importer.SaveAndReimport();
            }
        }
    }
}
