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
    }
}
