// Swaps a fighter's model in the open scene for a new prepared body FBX
// (ArtSource/Blender/prep_body.py + attach_weapons.py output: body mesh + weapons bone-parented
// to the hands, textures in <Name>_Body.fbm and Weapons/<W>.fbm).
//
// Crucible > Characters > Rebuild Player1 (Bjorn) / Rebuild Player2 (Miyamoto):
//   1. forces the FBX importer to Humanoid (arm/forearm twist 1, leg twist 0.5, no materials imported)
//   2. removes the old model child under the Player root (everything except "Hitbox")
//   3. instantiates the new FBX under the root, scaled so the fighter stands TargetHeight world units tall
//   4. points the root Animator at the new avatar, disables the Animator the FBX brings along
//   5. builds URP/Lit materials from the .fbm textures and assigns them by renderer name
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CrucibleCharacterSetup
{
    const float TargetHeight = 2.9f;   // world units, matches the previous fighters (~1.75 m at 0.6 m/unit)

    [MenuItem("Crucible/Characters/Rebuild Player1 (Bjorn new body)")]
    public static void RebuildP1() => Rebuild("Player1", "Bjorn");

    [MenuItem("Crucible/Characters/Rebuild Player2 (Miyamoto new body)")]
    public static void RebuildP2() => Rebuild("Player2", "Miyamoto");

    [MenuItem("Crucible/Characters/Report fighters")]
    public static void Report()
    {
        foreach (var n in new[] { "Player1", "Player2" })
        {
            var go = GameObject.Find(n);
            if (go == null) { Debug.Log($"{n}: not in scene"); continue; }
            var anim = go.GetComponent<Animator>();
            var rends = go.GetComponentsInChildren<Renderer>(true);
            var b = rends.Length > 0 ? rends[0].bounds : new Bounds();
            foreach (var r in rends) b.Encapsulate(r.bounds);
            Debug.Log($"{n}: scale {go.transform.localScale} avatar {(anim && anim.avatar ? anim.avatar.name : "none")} " +
                      $"controller {(anim && anim.runtimeAnimatorController ? anim.runtimeAnimatorController.name : "none")} " +
                      $"children [{string.Join(", ", Enumerable.Range(0, go.transform.childCount).Select(i => go.transform.GetChild(i).name))}] " +
                      $"renderers [{string.Join(", ", rends.Select(r => r.name + ":" + (r.sharedMaterial ? r.sharedMaterial.name : "null")))}] " +
                      $"height {b.size.y:F2} units");
        }
    }

    static void Rebuild(string playerName, string charName)
    {
        string charDir = $"Assets/_project/Characters/{charName}";
        string fbxPath = $"{charDir}/Model/{charName}_Body.fbx";
        var player = GameObject.Find(playerName);
        if (player == null) { Debug.LogError($"{playerName} not found in the open scene"); return; }
        if (!File.Exists(fbxPath)) { Debug.LogError($"missing {fbxPath}"); return; }

        // 1. importer
        var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        bool reimport = false;
        if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; reimport = true; }
        if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; reimport = true; }
        if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; reimport = true; }
        var hd = mi.humanDescription;
        if (hd.upperArmTwist != 1f || hd.lowerArmTwist != 1f || hd.upperLegTwist != 0.5f || hd.lowerLegTwist != 0.5f)
        {
            hd.upperArmTwist = 1f; hd.lowerArmTwist = 1f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
            mi.humanDescription = hd; reimport = true;
        }
        if (reimport) { mi.SaveAndReimport(); Debug.Log($"{charName}: importer set to Humanoid, reimported"); }

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
        if (fbx == null || avatar == null) { Debug.LogError($"{charName}: FBX or avatar failed to load ({fbx != null}, {avatar != null}); is the rig valid?"); return; }
        if (!avatar.isValid || !avatar.isHuman) { Debug.LogError($"{charName}: avatar invalid or not human"); return; }

        // 2. remove the old model
        foreach (Transform c in player.transform.Cast<Transform>().ToArray())
            if (c.name != "Hitbox") Undo.DestroyObjectImmediate(c.gameObject);

        // 3. instantiate + scale
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        Undo.RegisterCreatedObjectUndo(inst, "new body");
        inst.name = charName + "_Body";
        inst.transform.SetParent(player.transform, false);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;
        var rends = inst.GetComponentsInChildren<Renderer>(true);
        var bounds = rends[0].bounds;
        foreach (var r in rends) bounds.Encapsulate(r.bounds);
        float h = bounds.size.y / Mathf.Max(player.transform.lossyScale.y, 1e-4f) * 1f; // height in root-local units at child scale 1
        float childScale = TargetHeight / Mathf.Max(bounds.size.y, 1e-4f);
        inst.transform.localScale = Vector3.one * childScale;
        Debug.Log($"{charName}: model height {bounds.size.y:F3} world units at child scale 1 under root scale {player.transform.lossyScale.y}; child scale set to {childScale:F3} for {TargetHeight} units");

        // 4. animators
        foreach (var a in inst.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        var rootAnim = player.GetComponent<Animator>();
        if (rootAnim == null) rootAnim = Undo.AddComponent<Animator>(player);
        Undo.RecordObject(rootAnim, "avatar");
        rootAnim.avatar = avatar;
        rootAnim.Rebind();

        // 5. materials
        string matDir = $"{charDir}/Materials";
        if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(charDir, "Materials");
        var bodyMat = BuildMaterial($"{charDir}/Model/{charName}_Body.fbm", charName.ToLower(), $"{matDir}/{charName}_Body.mat");
        var weaponMats = Directory.Exists($"{charDir}/Weapons")
            ? Directory.GetDirectories($"{charDir}/Weapons", "*.fbm").ToDictionary(
                d => Path.GetFileNameWithoutExtension(d),
                d => BuildMaterial(d.Replace('\\', '/'), Path.GetFileNameWithoutExtension(d), $"{matDir}/{Path.GetFileNameWithoutExtension(d)}.mat"))
            : new System.Collections.Generic.Dictionary<string, Material>();
        foreach (var r in rends)
        {
            Material m = null;
            if (r.name.Contains("Body")) m = bodyMat;
            else foreach (var kv in weaponMats) if (r.name.StartsWith(kv.Key)) m = kv.Value;
            if (m != null) { Undo.RecordObject(r, "material"); r.sharedMaterial = m; continue; }
            // procedural weapons carry a <Weapon>.slots.txt (name:r,g,b:metallic:smoothness per material slot)
            string slotsFile = Directory.Exists($"{charDir}/Weapons")
                ? Directory.GetFiles($"{charDir}/Weapons", "*.slots.txt").FirstOrDefault(f => r.name.StartsWith(Path.GetFileName(f).Replace(".slots.txt", "")))
                : null;
            if (slotsFile != null)
            {
                var lines = File.ReadAllLines(slotsFile).Where(l => l.Contains(':')).ToArray();
                var mats = new Material[lines.Length];
                for (int i = 0; i < lines.Length; i++)
                {
                    var p = lines[i].Split(':');
                    var c = p[1].Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    mats[i] = BuildFlatMaterial($"{matDir}/{Path.GetFileName(slotsFile).Replace(".slots.txt", "")}_{p[0]}.mat",
                        new Color(c[0], c[1], c[2]), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
                }
                Undo.RecordObject(r, "material");
                r.sharedMaterials = mats;
                continue;
            }
            Debug.LogWarning($"{charName}: no material for renderer {r.name}");
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.scene);
        Debug.Log($"{charName}: rebuilt under {playerName}. Renderers: {string.Join(", ", rends.Select(r => r.name))}. Save the scene.");
    }

    static Material BuildFlatMaterial(string matPath, Color color, float metallic, float smoothness)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, matPath); }
        else if (mat.shader != lit) mat.shader = lit;
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        mat.DisableKeyword("_METALLICSPECGLOSSMAP");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material BuildMaterial(string fbmDir, string baseName, string matPath)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) { Debug.LogError("URP/Lit shader not found; is URP active?"); return null; }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, matPath); }
        else if (mat.shader != lit) mat.shader = lit;

        Texture2D Load(string suffix, System.Action<TextureImporter> setup)
        {
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string p = $"{fbmDir}/{baseName}{suffix}{ext}";
                if (!File.Exists(p)) continue;
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti != null)
                {
                    bool changed = false;
                    var before = (ti.textureType, ti.sRGBTexture, ti.maxTextureSize);
                    setup(ti);
                    if (ti.maxTextureSize > 4096) ti.maxTextureSize = 4096;
                    if (before != (ti.textureType, ti.sRGBTexture, ti.maxTextureSize)) changed = true;
                    if (changed) ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }
        var baseMap = Load("_basecolor", ti => { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; });
        var normal = Load("_normal", ti => { ti.textureType = TextureImporterType.NormalMap; });
        var mask = Load("_mask", ti => { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; });

        if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);
        mat.SetColor("_BaseColor", Color.white);
        if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
        if (mask != null)
        {
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);  // metallic map alpha
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else { mat.SetFloat("_Metallic", 0f); mat.SetFloat("_Smoothness", 0.4f); }
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log($"material {matPath}: base {(baseMap ? baseMap.name : "-")} normal {(normal ? normal.name : "-")} mask {(mask ? mask.name : "-")}");
        return mat;
    }
}
