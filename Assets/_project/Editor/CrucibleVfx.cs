using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Weapon VFX setup. `Crucible > Look > Create weapon VFX profiles` writes one WeaponVfxProfile asset
/// per fighter in the open scene (from the built-in character defaults,
/// WeaponVfxProfile.ForCharacter) to Assets/_project/Data/Vfx/, and assigns it on the fighter's
/// CombatSystem (Vfx Profile). Idempotent: an existing asset is KEPT (hand tuning survives) and only
/// re-assigned. Game types are reached by reflection: this asmdef cannot reference Assembly-CSharp.
/// </summary>
public static class CrucibleVfx
{
    const string Folder = "Assets/_project/Data/Vfx";

    [MenuItem("Crucible/Look/Create weapon VFX profiles")]
    public static void CreateProfiles()
    {
        Type combatT = Type.GetType("CombatSystem, Assembly-CSharp");
        Type fighterT = Type.GetType("Fighter, Assembly-CSharp");
        Type profileT = Type.GetType("WeaponVfxProfile, Assembly-CSharp");
        if (combatT == null || fighterT == null || profileT == null)
        {
            Debug.LogError("CrucibleVfx: game types not found (compile errors?)");
            return;
        }
        MethodInfo forCharacter = profileT.GetMethod("ForCharacter", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo charName = fighterT.GetProperty("CharacterName");

        if (!AssetDatabase.IsValidFolder(Folder))
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
        }

        int made = 0, assigned = 0;
        foreach (var obj in UnityEngine.Object.FindObjectsByType(combatT, FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var cs = (Component)obj;
            var f = cs.GetComponent(fighterT);
            string who = f != null ? charName.GetValue(f) as string : null;
            if (string.IsNullOrEmpty(who)) who = cs.name;
            string path = $"{Folder}/{who}_Vfx.asset";

            var asset = AssetDatabase.LoadAssetAtPath(path, profileT) as ScriptableObject;
            if (asset == null)
            {
                asset = (ScriptableObject)forCharacter.Invoke(null, new object[] { who });
                asset.name = who + "_Vfx";
                AssetDatabase.CreateAsset(asset, path);
                made++;
            }

            var so = new SerializedObject(cs);
            var prop = so.FindProperty("vfxProfile");
            if (prop != null && prop.objectReferenceValue != asset)
            {
                prop.objectReferenceValue = asset;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(cs.gameObject.scene);
                assigned++;
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"CrucibleVfx: {made} profile(s) created, {assigned} fighter(s) assigned ({Folder}).");
    }
}
