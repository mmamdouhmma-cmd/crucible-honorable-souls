using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Crucible > Build > WebGL (test build): one-click web build into Builds/WebGL, uncompressed so any static server can host it.</summary>
public static class CrucibleBuild
{
    const string Scene = "Assets/_project/Scenes/testarena.unity";
    const string BuildScene = "Assets/_project/Scenes/_build_temp.unity";
    const string OutDir = "Builds/WebGL";

    const string WinDir = "Builds/CrucibleLite-Win64";

    /// <summary>
    /// The Windows app that friends download (Tools/release.cmd zips and publishes this folder). Bump
    /// Player Settings > Version FIRST: the game updates itself when a release tag is newer than its own
    /// <c>Application.version</c>, so a release built at the old number never reaches anyone.
    /// </summary>
    [MenuItem("Crucible/Build/Windows app")]
    public static void BuildWindows()
    {
        Directory.CreateDirectory(WinDir);
        string scene = PrepareScene();
        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(WinDir, "Crucible.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
        }
        finally
        {
            RestoreScene();
        }
        var s = report.summary;
        Debug.Log($"Windows build v{PlayerSettings.bundleVersion} {s.result}: {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors -> {Path.GetFullPath(WinDir)}");
        File.WriteAllText(Path.Combine(WinDir, "build_result.txt"), $"{s.result} v{PlayerSettings.bundleVersion} {s.totalSize} {s.totalErrors} {s.totalTime}");
    }

    [MenuItem("Crucible/Build/WebGL (test build)")]
    public static void BuildWebGL()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.template = "APPLICATION:Default";
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.productName = "CRUCIBLE test";
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
        Directory.CreateDirectory(OutDir);

        string scene = PrepareScene();
        BuildReport report;
        try
        {
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = OutDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            report = BuildPipeline.BuildPlayer(opts);
        }
        finally
        {
            RestoreScene();
        }
        var s = report.summary;
        Debug.Log($"WebGL build {s.result}: {s.totalSize / (1024 * 1024)} MB in {s.totalTime.TotalMinutes:F1} min, {s.totalErrors} errors, {s.totalWarnings} warnings -> {Path.GetFullPath(OutDir)}");
        File.WriteAllText(Path.Combine(OutDir, "build_result.txt"), $"{s.result} {s.totalSize} {s.totalErrors} {s.totalTime}");

        if (s.result == BuildResult.Succeeded) CacheBust();
    }

    /// <summary>
    /// Builds from a throwaway COPY of the scene with the scenery nobody can reach deleted, and
    /// returns its path. An inactive GameObject is still serialized, so a build used to ship all
    /// four arenas - about 126 MB of meshes and 2K textures. The real scene is never modified: the
    /// copy is deleted and the original reopened in <see cref="RestoreScene"/>.
    ///
    /// <para>The arenas marked <c>shipped</c> in the scene's <c>ArenaDirectory</c> are KEPT even
    /// though they are switched off, because arena select turns them on at runtime. That one
    /// toggle therefore decides both what ships and what is selectable, which is the point.</para>
    /// </summary>
    static string PrepareScene()
    {
        // The copy is made from the file on disk, so unsaved edits would silently miss the build.
        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            Debug.Log("Saving open scenes first - the build is made from the files on disk.");
            EditorSceneManager.SaveOpenScenes();
        }

        AssetDatabase.DeleteAsset(BuildScene);
        if (!AssetDatabase.CopyAsset(Scene, BuildScene))
        {
            Debug.LogWarning($"Could not copy {Scene}; building it as it stands, every arena included.");
            return Scene;
        }

        var sc = EditorSceneManager.OpenScene(BuildScene, OpenSceneMode.Single);
        var keep = ShippedArenaRoots(sc);
        var stripped = new List<string>();
        var kept = new List<string>();
        foreach (var go in sc.GetRootGameObjects())
        {
            // An inactive ROOT carrying renderers is scenery that has been switched off. Logic
            // objects carry no renderers, so this cannot take anything the game still needs - with
            // one exception, the arenas the player is allowed to choose between.
            if (go.activeSelf || go.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
            if (keep.Contains(go.name)) { kept.Add(go.name); continue; }
            stripped.Add(go.name);
            UnityEngine.Object.DestroyImmediate(go);
        }
        EditorSceneManager.SaveScene(sc);

        Debug.Log($"Build scene: shipping {1 + kept.Count} arena(s)" +
                  (kept.Count > 0 ? $" (selectable: {string.Join(", ", kept)})" : "") +
                  (stripped.Count > 0 ? $", left out {stripped.Count} ({string.Join(", ", stripped)})"
                                      : ", nothing left out."));
        return BuildScene;
    }

    /// <summary>
    /// Root names of the arenas marked <c>shipped</c> on the scene's ArenaDirectory. Read through
    /// SerializedObject because this assembly is an asmdef and so cannot reference the runtime
    /// types in Assembly-CSharp directly. No directory in the scene = nothing to keep, which is
    /// the old one-arena behaviour.
    /// </summary>
    static HashSet<string> ShippedArenaRoots(UnityEngine.SceneManagement.Scene sc)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var type = Type.GetType("ArenaDirectory, Assembly-CSharp");
        if (type == null)
        {
            Debug.LogWarning("Build scene: no ArenaDirectory type found - shipping only the active arena.");
            return keep;
        }

        foreach (var go in sc.GetRootGameObjects())
        {
            var comp = go.GetComponentInChildren(type, true);
            if (comp == null) continue;

            var arenas = new SerializedObject(comp).FindProperty("arenas");
            if (arenas == null || !arenas.isArray) break;
            for (int i = 0; i < arenas.arraySize; i++)
            {
                var entry = arenas.GetArrayElementAtIndex(i);
                var rootName = entry.FindPropertyRelative("rootName");
                var shipped = entry.FindPropertyRelative("shipped");
                if (rootName != null && shipped != null && shipped.boolValue && !string.IsNullOrEmpty(rootName.stringValue))
                    keep.Add(rootName.stringValue);
            }
            break;
        }

        if (keep.Count == 0) Debug.LogWarning("Build scene: ArenaDirectory marks no arena as shipped - shipping only the active one.");
        return keep;
    }

    /// <summary>Drops the throwaway scene and puts the real one back in the Editor.</summary>
    static void RestoreScene()
    {
        AssetDatabase.DeleteAsset(BuildScene);
        if (File.Exists(Scene) && EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
    }

    /// <summary>
    /// Stamps a version on the payload URLs in index.html. Unity's dataCaching keys the 200 MB+ data
    /// file on its URL, so without this a tester who reloads keeps playing the build they first opened,
    /// however hard they refresh. A new stamp each build means one fresh download, then caching as usual.
    /// </summary>
    static void CacheBust()
    {
        string indexPath = Path.Combine(OutDir, "index.html");
        if (!File.Exists(indexPath)) return;

        string stamp = System.DateTime.Now.ToString("yyyyMMddHHmm");
        string html = File.ReadAllText(indexPath);
        foreach (string file in new[] { "WebGL.loader.js", "WebGL.data", "WebGL.framework.js", "WebGL.wasm" })
        {
            html = html.Replace($"buildUrl + \"/{file}\"", $"buildUrl + \"/{file}?v={stamp}\"");
        }
        File.WriteAllText(indexPath, html);
        Debug.Log($"WebGL index.html stamped v={stamp} (cache-bust)");
    }
}
