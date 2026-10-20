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
    /// Builds from a throwaway COPY of the scene with the arenas that are switched OFF deleted, and
    /// returns its path. An inactive GameObject is still serialized, so every build so far has
    /// shipped all four arenas - about 126 MB of meshes and 2K textures nobody can reach. The real
    /// scene is never modified: the copy is deleted and the original reopened in <see cref="RestoreScene"/>.
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
        var stripped = new List<string>();
        foreach (var go in sc.GetRootGameObjects())
        {
            // An inactive ROOT carrying renderers is scenery that has been switched off - the other
            // arenas. Logic objects carry no renderers, and nothing at runtime turns a root back on,
            // so this cannot take anything the game still needs.
            if (go.activeSelf || go.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
            stripped.Add(go.name);
            Object.DestroyImmediate(go);
        }
        EditorSceneManager.SaveScene(sc);

        Debug.Log(stripped.Count > 0
            ? $"Build scene: shipping one arena, left out {stripped.Count} switched-off ({string.Join(", ", stripped)})"
            : "Build scene: no switched-off scenery to leave out.");
        return BuildScene;
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
