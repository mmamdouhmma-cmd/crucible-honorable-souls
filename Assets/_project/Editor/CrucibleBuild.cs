using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Crucible > Build > WebGL (test build): one-click web build into Builds/WebGL, uncompressed so any static server can host it.</summary>
public static class CrucibleBuild
{
    const string Scene = "Assets/_project/Scenes/testarena.unity";
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
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = OutDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log($"WebGL build {s.result}: {s.totalSize / (1024 * 1024)} MB in {s.totalTime.TotalMinutes:F1} min, {s.totalErrors} errors, {s.totalWarnings} warnings -> {Path.GetFullPath(OutDir)}");
        File.WriteAllText(Path.Combine(OutDir, "build_result.txt"), $"{s.result} {s.totalSize} {s.totalErrors} {s.totalTime}");

        if (s.result == BuildResult.Succeeded) CacheBust();
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
