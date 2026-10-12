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

    // Vertical offset of the model child under the Player root, in root-local units, so the animated feet rest
    // on the floor (y = 0). The roots sit where the physics capsule lands (P1 0.20, P2 0.75 world) and the
    // Humanoid idle puts the feet a little below the rest pose; measured in Play mode on 2026-10-12 as the
    // renderer-bounds minimum: Bjorn 0.136 world under root scale 4, Miyamoto 0.623 under root scale 3.
    static readonly System.Collections.Generic.Dictionary<string, float> GroundOffset = new System.Collections.Generic.Dictionary<string, float>
    {
        { "Bjorn", -0.136f / 4f },
        { "Miyamoto", -0.623f / 3f },
    };

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
        if (GroundOffset.TryGetValue(charName, out float gy)) inst.transform.localPosition = new Vector3(0f, gy, 0f);
        Debug.Log($"{charName}: model height {bounds.size.y:F3} world units at child scale 1 under root scale {player.transform.lossyScale.y}; child scale set to {childScale:F3} for {TargetHeight} units, ground offset {inst.transform.localPosition.y:F3}");

        // 4. animators
        foreach (var a in inst.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        var rootAnim = player.GetComponent<Animator>();
        if (rootAnim == null) rootAnim = Undo.AddComponent<Animator>(player);
        Undo.RecordObject(rootAnim, "avatar");
        rootAnim.avatar = avatar;
        rootAnim.Rebind();
        // IK pass on layer 0 so TwoHandGrip can place the off hand
        var ac = rootAnim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        if (ac != null && ac.layers.Length > 0 && !ac.layers[0].iKPass)
        {
            var layers = ac.layers; layers[0].iKPass = true; ac.layers = layers; EditorUtility.SetDirty(ac);
        }

        // 4b. weapon: blade hitbox + off-hand grip socket + two-hand IK
        SetupWeapon(player, inst, charName, rootAnim);

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

    // Blade region of each weapon mesh along its local +Y (grip at the origin, business end at +Y), as a
    // fraction of the mesh's Y extent where the hitting part starts; and where the off hand holds the haft
    // (weapon-local Y, file units: a 0.98-tall body).
    // Both values are fractions of the mesh's Y extent measured from its bottom (butt / kashira), because the
    // imported mesh is in its own units (the FBX node carries the unit scale), so absolute file units don't apply.
    /// <summary>Fraction of the blade length the hit box extends past the tip (user tuning 2026-10-12: strikes felt a little short).</summary>
    const float BladeTipExtend = 0.15f;

    static readonly System.Collections.Generic.Dictionary<string, (float bladeStartFrac, float leftGripFrac)> WeaponSpec =
        new System.Collections.Generic.Dictionary<string, (float, float)>
    {
        { "Bjorn_Axe",       (0.62f, 0.50f) },   // 0.70 haft, butt at -0.21: head is the top ~38%; left hand 0.22 above the right (0.43/0.70)
        { "Miyamoto_Katana", (0.24f, 0.04f) },   // blade starts past the tsuba; left hand near the kashira (0.023/0.57 up from the butt)
    };

    static void SetupWeapon(GameObject player, GameObject inst, string charName, Animator rootAnim)
    {
        var weapon = inst.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => WeaponSpec.Keys.Any(k => r.name.StartsWith(k)));
        if (weapon == null) { Debug.LogWarning($"{charName}: no weapon renderer found for the blade hitbox"); return; }
        var spec = WeaponSpec[WeaponSpec.Keys.First(k => weapon.name.StartsWith(k))];
        var mf = weapon.GetComponent<MeshFilter>();
        var b = mf.sharedMesh.bounds;   // weapon-local
        // The Blender FBX exporter keeps mesh data in Blender axes and puts the axis swap on the node, so the
        // handle (Blender +Z) is the MESH-LOCAL +Z here, not +Y.
        float y0 = b.min.z + spec.bladeStartFrac * (b.max.z - b.min.z);
        var box = weapon.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(weapon.gameObject);
        box.isTrigger = true;
        // The hit volume runs BladeTipExtend past the real tip: strikes read as landing a touch before the model
        // actually touches, and the swept detection has margin at the end of the reach.
        float tipExtend = BladeTipExtend * (b.max.z - y0);
        box.center = new Vector3(b.center.x, b.center.y, (y0 + b.max.z + tipExtend) * 0.5f);
        box.size = new Vector3(b.size.x, b.size.y, b.max.z - y0 + tipExtend);
        box.enabled = false;   // HitDetector arms it on the active frames
        var det = weapon.GetComponent(RuntimeType("HitDetector"));
        if (det == null) det = Undo.AddComponent(weapon.gameObject, RuntimeType("HitDetector"));
        var sd = new SerializedObject(det);
        sd.FindProperty("followWeapon").boolValue = true;
        sd.ApplyModifiedProperties();
        // point the CombatSystem at it and park the old front box
        var combat = player.GetComponent(RuntimeType("CombatSystem"));
        if (combat != null)
        {
            var sc = new SerializedObject(combat);
            sc.FindProperty("hitDetector").objectReferenceValue = det;
            sc.ApplyModifiedProperties();
        }
        var oldBox = player.transform.Find("Hitbox");
        if (oldBox != null && oldBox.gameObject.activeSelf) { Undo.RecordObject(oldBox.gameObject, "park hitbox"); oldBox.gameObject.SetActive(false); }

        // off-hand socket: on the haft axis, oriented like the right hand bone (same wrap around the haft)
        var socket = weapon.transform.Find("LeftGrip");
        if (socket == null)
        {
            var go = new GameObject("LeftGrip");
            Undo.RegisterCreatedObjectUndo(go, "grip socket");
            socket = go.transform;
            socket.SetParent(weapon.transform, false);
        }
        float gripY = b.min.z + spec.leftGripFrac * b.size.z;
        socket.localPosition = new Vector3(b.center.x, b.center.y, gripY);
        var rightHand = rootAnim.GetBoneTransform(HumanBodyBones.RightHand);
        socket.rotation = rightHand != null ? rightHand.rotation : weapon.transform.rotation;

        // elbow hint: left of and behind the chest (root-local; root scale ~3-4) so the left arm bends outward
        var hint = player.transform.Find("LeftElbowHint");
        if (hint == null)
        {
            var hgo = new GameObject("LeftElbowHint");
            Undo.RegisterCreatedObjectUndo(hgo, "elbow hint");
            hint = hgo.transform; hint.SetParent(player.transform, false);
        }
        hint.localPosition = new Vector3(-0.35f, 0.42f, -0.25f);   // root faces +z, so -x is the fighter's left; raised to chest, behind

        var grip = player.GetComponent(RuntimeType("TwoHandGrip"));
        if (grip == null) grip = Undo.AddComponent(player, RuntimeType("TwoHandGrip"));
        var sg = new SerializedObject(grip);
        sg.FindProperty("leftGrip").objectReferenceValue = socket;
        sg.FindProperty("leftElbowHint").objectReferenceValue = hint;
        sg.ApplyModifiedProperties();

        // weapon aim toward the opponent during strikes
        var aim = player.GetComponent(RuntimeType("WeaponAim"));
        if (aim == null) aim = Undo.AddComponent(player, RuntimeType("WeaponAim"));
        var sa = new SerializedObject(aim);
        sa.FindProperty("weapon").objectReferenceValue = weapon.transform;
        sa.ApplyModifiedProperties();
        Debug.Log($"{charName}: blade box on {weapon.name} z {y0:F4}..{b.max.z:F4} (mesh local), LeftGrip at y {gripY:F4} (mesh units), TwoHandGrip + IK pass set");
    }

    [MenuItem("Crucible/Debug/Slow motion 15% (toggle on GameMangers)")]
    public static void ToggleSlowMo()
    {
        var gm = GameObject.Find("GameMangers");
        if (gm == null) { Debug.LogError("GameMangers not found"); return; }
        var ts = gm.GetComponent(RuntimeType("DebugTimeScale"));
        if (ts == null) { ts = Undo.AddComponent(gm, RuntimeType("DebugTimeScale")); var so = new SerializedObject(ts); so.FindProperty("timeScale").floatValue = 0.85f; so.ApplyModifiedProperties(); Debug.Log("Slow motion ON (0.85)"); }
        else { Undo.DestroyObjectImmediate(ts); Debug.Log("Slow motion OFF"); }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gm.scene);
    }

    // The Editor asmdef cannot reference the default game assembly, so runtime components are looked up by name.
    static System.Type RuntimeType(string name)
    {
        var t = System.Type.GetType(name + ", Assembly-CSharp");
        if (t == null) throw new System.Exception("runtime type " + name + " not found in Assembly-CSharp");
        return t;
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
