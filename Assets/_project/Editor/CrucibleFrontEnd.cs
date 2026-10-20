using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot setup for the front end: puts the runtime pieces the menus need on the GameMangers
/// object, gives the logo sensible import settings, and matches Unity's own
/// splash screen to the logo's background so the hand-off into our boot screen has no flash.
///
/// Re-runnable; it only adds what is missing. This assembly is an asmdef, so it cannot reference
/// the runtime types directly - everything goes through <see cref="Type.GetType"/> on
/// Assembly-CSharp, the same pattern the arena setup already uses.
/// </summary>
public static class CrucibleFrontEnd
{
    const string Scene = "Assets/_project/Scenes/testarena.unity";
    const string Managers = "GameMangers";   // the object's name in the scene, typo and all

    static readonly (string path, int maxSize)[] UiTextures =
    {
        ("Assets/_project/Resources/ui/crucible_logo.png", 2048),
    };

    [MenuItem("Crucible/Front End/Set up the front end")]
    public static void Setup()
    {
        ImportUiTextures();
        MatchSplashToLogo();
        AddRuntimePieces();
        Debug.Log("Front end: setup complete.");
    }

    /// <summary>Keeps the UI art off the "2048 RGBA uncompressed" default that bloats a web build.</summary>
    static void ImportUiTextures()
    {
        foreach (var (path, maxSize) in UiTextures)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogWarning($"Front end: no texture at {path}"); continue; }

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;          // screen-space UI never samples a mip
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
            Debug.Log($"Front end: imported {System.IO.Path.GetFileName(path)} at max {maxSize}");
        }
    }

    /// <summary>
    /// Unity's splash is the "made with Unity" screen the game opens on in a build; our own logo
    /// screen follows it. Painting its background the logo's near-black makes the two read as one
    /// sequence instead of a grey flash followed by a cut to black.
    /// </summary>
    static void MatchSplashToLogo()
    {
        PlayerSettings.SplashScreen.show = true;
        PlayerSettings.SplashScreen.showUnityLogo = true;
        PlayerSettings.SplashScreen.backgroundColor = new Color(0.039f, 0.031f, 0.027f, 1f);
        PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Dolly;
        PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
        Debug.Log("Front end: Unity splash set to the logo's background.");
    }

    /// <summary>Adds ArenaDirectory and PracticeMode to the managers object, if they are not there.</summary>
    static void AddRuntimePieces()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != Scene) scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        GameObject managers = null;
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == Managers) { managers = go; break; }

        if (managers == null)
        {
            Debug.LogError($"Front end: no '{Managers}' object in {Scene}; nothing added.");
            return;
        }

        bool changed = false;
        foreach (string typeName in new[] { "ArenaDirectory", "PracticeMode" })
        {
            var type = Type.GetType(typeName + ", Assembly-CSharp");
            if (type == null) { Debug.LogError($"Front end: type {typeName} not found - is the project compiled?"); continue; }
            if (managers.GetComponent(type) != null) { Debug.Log($"Front end: {typeName} already on {Managers}."); continue; }
            managers.AddComponent(type);
            changed = true;
            Debug.Log($"Front end: added {typeName} to {Managers}.");
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Front end: scene saved.");
        }
    }

    /// <summary>Prints what the front end can see, for checking a setup without entering play mode.</summary>
    [MenuItem("Crucible/Front End/Report")]
    public static void Report()
    {
        var lines = new List<string>();
        foreach (var (path, _) in UiTextures)
            lines.Add($"{System.IO.Path.GetFileName(path)}: {(AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null ? "present" : "MISSING")}");

        var scene = EditorSceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.name != Managers) continue;
            foreach (string typeName in new[] { "ArenaDirectory", "PracticeMode", "GameMenu", "AiController" })
            {
                var type = Type.GetType(typeName + ", Assembly-CSharp");
                lines.Add($"{typeName}: {(type != null && go.GetComponent(type) != null ? "on " + Managers : "missing")}");
            }
        }

        foreach (var go in scene.GetRootGameObjects())
            if (go.GetComponentsInChildren<Renderer>(true).Length > 0 && go.transform.childCount > 0)
                lines.Add($"root '{go.name}': {(go.activeSelf ? "ACTIVE" : "off")}");

        Debug.Log("Front end report\n" + string.Join("\n", lines));
    }
}
