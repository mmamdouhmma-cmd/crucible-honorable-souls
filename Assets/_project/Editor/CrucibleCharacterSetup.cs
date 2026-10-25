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

    /// <summary>
    /// How tall each fighter STANDS, feet to crown, in world units - the body only, no weapon.
    /// The roster is meant to read biggest to smallest as Richard, Bjorn, Salaadin, Miyamoto,
    /// which is also the order of their armour (22 / 14 / 18 / 8) and the opposite of their speed.
    /// They used to come out Miyamoto 2.78, Bjorn 2.74, Richard 2.69, Salaadin 2.10 - the first
    /// three near enough identical and in the wrong order, and Salaadin a head shorter than anyone
    /// because the old code scaled the model's bounds WITH HIS WEAPONS IN THEM.
    /// These are cosmetic: the hitboxes hang off the Player root, not the model, so changing a
    /// height moves the body relative to its own boxes and nothing else. GroundOffset DOES depend
    /// on this - re-measure it whenever a height changes.
    ///
    /// CAREFUL: the number here is the BIND-POSE skinned bounds target, which is NOT what the
    /// fighter measures on screen - the box is loose and every idle stands with bent knees. These
    /// values are CALIBRATED so that the fighters' STATURE, sole to crown, averaged over the idle
    /// cycle, comes out at:
    ///     Richard 3.25, Bjorn 3.05, Salaadin 2.80, Miyamoto 2.55.
    /// Two traps, both of which produced a confidently wrong ranking before this comment existed:
    ///  1. Do NOT measure the mesh's own vertical span (lowest vertex to highest). Richard's cape
    ///     and Salaadin's coat hang BELOW their heels, so that credits them with height they do
    ///     not appear to have - it called Richard the tallest while on screen Bjorn stood over him.
    ///  2. Do NOT measure the crown above the floor either, because GroundOffset lifts the crown
    ///     along with the body: change an offset and every crown moves. Stature (crown MINUS sole)
    ///     is the only one of the three that depends on the child scale alone, so it is the one
    ///     that can be solved independently. Set these first, then re-measure the soles and set
    ///     GroundOffset to drop each sole onto the floor.
    /// Measure in Play mode, per fighter in his OWN idle, from BAKED skinned vertices:
    ///     stature = max(vertex y) - min(vertex y over verts whose dominant bone is foot/toe/ankle).
    /// Re-measure whenever a fighter's idle clip or rig changes - the 2026-10-22 re-rig moved all
/// three of the re-rigged fighters by 5-10% even though the table had just been calibrated.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<string, float> StandingHeight = new System.Collections.Generic.Dictionary<string, float>
    {
        // 2026-10-22: the user's call after play-testing all four together - Richard and Bjorn read
        // as "way too big for Miyamoto and Salaadin", so both come down 11%. That DOES invert the
        // intended ranking (Bjorn now stands below Salaadin, 2.71 against 2.80); the brief was the
        // two big men against the two small ones, not the order among them.
        { "Richard",  3.340f },   // -> stature 2.89  (was 3.753 -> 3.25, -11%)
        { "Bjorn",    3.113f },   // -> stature 2.71  (was 3.498 -> 3.05, -11%)
        { "Salaadin", 3.857f },   // -> stature 2.80  (his idle crouches hardest, so the box has to be tallest)
        { "Miyamoto", 2.824f },   // -> stature 2.55
        // 2026-10-25 PLACEHOLDERS, to be calibrated in play like the four above (measure stature,
        // crown MINUS sole, in each man's own idle): Genghis reads as a big armoured tank
        // (between Bjorn and Richard), Buwaka a lean mid-sized hunter.
        { "Genghis",  3.46f },   // calibrated 2026-10-26: 3.20 measured 2.568 in play, x 2.78/2.568
        { "Buwaka",   3.10f },   // calibrated 2026-10-26: 3.05 measured 2.611 in play, x 2.65/2.611
    };

    static float HeightOf(string charName) =>
        StandingHeight.TryGetValue(charName, out float h) ? h : TargetHeight;

    /// <summary>
    /// What each fighter MEASURES, sole to crown, once StandingHeight has been calibrated - the
    /// number the physics capsule has to match. StandingHeight is the bind-pose box target that
    /// produces it and is always the larger of the two.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<string, float> Stature = new System.Collections.Generic.Dictionary<string, float>
    {
        { "Richard",  2.89f },
        { "Bjorn",    2.71f },
        { "Salaadin", 2.80f },
        { "Miyamoto", 2.55f },
        // 2026-10-25 placeholders; re-measure with StandingHeight.
        { "Genghis",  2.78f },
        { "Buwaka",   2.65f },
    };

    static float StandingStature(string charName) =>
        Stature.TryGetValue(charName, out float h) ? h : TargetHeight;

    /// <summary>
    /// Unity fills the humanoid slots from a NAME HEURISTIC, and the heuristic is not exhaustive:
    /// with the whole roster on Mixamo names and both hands fully boned, it took 30/30 fingers on
    /// Bjorn, Richard and Salaadin and only 26/30 on Miyamoto - dropping RightRing and RightLittle
    /// intermediate and distal, whose bones exist, are correctly named, and are mirror images of
    /// the left ones it DID take. So after the reimport, any slot still empty whose Mixamo-named
    /// bone is sitting right there in the hierarchy gets mapped explicitly.
    /// Only ever ADDS: a slot the mapper filled is left exactly as it found it.
    /// </summary>
    static void FillMissingHumanBones(string fbxPath, string charName)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (mi == null || mi.animationType != ModelImporterAnimationType.Human) return;
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx == null) return;

        var inHierarchy = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var t in fbx.GetComponentsInChildren<Transform>(true))
        {
            var leaf = t.name.Contains(":") ? t.name.Substring(t.name.LastIndexOf(':') + 1) : t.name;
            if (!inHierarchy.ContainsKey(leaf)) inHierarchy[leaf] = t.name;
        }

        var hd = mi.humanDescription;
        var taken = new System.Collections.Generic.HashSet<string>();
        var usedBones = new System.Collections.Generic.HashSet<string>();
        foreach (var hb in hd.human) { taken.Add(hb.humanName); usedBones.Add(hb.boneName); }

        var added = new System.Collections.Generic.List<HumanBone>(hd.human);
        var names = new System.Collections.Generic.List<string>();
        foreach (var human in HumanTrait.BoneName)
        {
            if (taken.Contains(human)) continue;
            // "Right Ring Intermediate" -> the Mixamo bone "RightHandRing2"
            var w = human.Split(' ');
            if (w.Length != 3) continue;
            string side = w[0], finger = w[1] == "Little" ? "Pinky" : w[1];
            int joint = w[2] == "Proximal" ? 1 : w[2] == "Intermediate" ? 2 : w[2] == "Distal" ? 3 : 0;
            if (joint == 0 || (side != "Left" && side != "Right")) continue;
            var leaf = $"{side}Hand{finger}{joint}";
            if (!inHierarchy.TryGetValue(leaf, out var bone) || usedBones.Contains(bone)) continue;
            var nb = new HumanBone { humanName = human, boneName = bone };
            nb.limit.useDefaultValues = true;
            added.Add(nb);
            usedBones.Add(bone);
            names.Add($"{human} <- {leaf}");
        }
        if (names.Count == 0) return;
        hd.human = added.ToArray();
        mi.humanDescription = hd;
        mi.SaveAndReimport();
        Debug.Log($"{charName}: filled {names.Count} humanoid slot(s) the auto-mapper missed: {string.Join(", ", names)}");
    }

    // Vertical offset of the model child under the Player root, in root-local units, so the animated feet rest
    // on the floor (y = 0). The roots sit where the physics capsule lands (P1 0.20, P2 0.75 world) and the
    // Humanoid idle puts the feet a little below the rest pose; measured in Play mode on 2026-10-12 as the
    // renderer-bounds minimum: Bjorn 0.136 world under root scale 4, Miyamoto 0.623 under root scale 3.
    static readonly System.Collections.Generic.Dictionary<string, float> GroundOffset = new System.Collections.Generic.Dictionary<string, float>
    {
        { "Bjorn", 0.119f / 4f },
        { "Miyamoto", 0.117f / 3f },
        // 2026-10-22c: re-measured after the -11% size change. GroundOffset is a FIXED root-local
        // lift, so shrinking the body left the same lift under shorter legs and he hovered 0.027.
        { "Richard", 0.119f / 3f },
        // Re-measure this whenever a fighter's RIG or his IDLE CLIP changes - it depends on both.
        // Salaadin's moved twice on 2026-10-21: once when the rig was hand-edited (ankles from 12%
        // to 3.1% of height) and again when his idle went from the crouched hand-keyed guard to the
        // pack's upright one, which stands 1.1 world units taller and left him hovering.
        // 2026-10-22b: ALL FOUR moved again when the per-character StandingHeight table came in,
        // because the ground offset is a root-local lift and every child scale changed with it.
        // Measure it on the SOLES - the lowest baked vertex whose dominant bone is a foot, toe
        // or ankle - and NOT on the lowest vertex of the whole body. Richard's cape and
        // Salaadin's coat hang below their heels, so anchoring on the lowest vertex rests the
        // CLOTH on the floor and leaves the man hovering half a unit over it.
        // 2026-10-22, the user's Rigify metarig: ankles went 3.1% -> 5.5% of height and he sank
        // again. Measured in play on Salaadin_Idle as the lowest BAKED skinned vertex (a
        // SkinnedMeshRenderer's bounds are a loose box that does not track the pose, so they read
        // this wrong by half a metre): -0.1085 world, which is 0.0362 of root-local at root scale 3.
        { "Salaadin", 0.109f / 3f },
        // 2026-10-25 placeholders for the metarig pair; measure in play as the lowest BAKED
        // skinned vertex whose dominant bone is a foot/toe/ankle, in each man's own idle.
        { "Genghis",  0.113f / 3f },   // re-measured 2026-10-26 at the calibrated stature: sole -0.013
        { "Buwaka",   0.110f / 3f },   // re-measured 2026-10-26: sole -0.010
    };

    /// <summary>
    /// Where a fighter's assets live. Every folder matches the character name except Salaadin's,
    /// which was created lowercase and cannot be renamed while Unity holds the files open
    /// ("Access is denied"); AssetDatabase paths are case sensitive even though Windows is not.
    /// </summary>
    static string CharDir(string charName) =>
        "Assets/_project/Characters/" + (charName == "Salaadin" ? "salaadin" : charName);

    /// <summary>Fighters whose off hand holds the haft. Richard's holds a shield, so he has no grip IK.</summary>
    static readonly string[] TwoHanded = { "Bjorn", "Miyamoto", "Genghis" };

    [MenuItem("Crucible/Characters/Rebuild Player1 (Bjorn new body)")]
    public static void RebuildP1() => Rebuild("Player1", "Bjorn");

    [MenuItem("Crucible/Characters/Rebuild Player2 (Miyamoto new body)")]
    public static void RebuildP2() => Rebuild("Player2", "Miyamoto");

    [MenuItem("Crucible/Characters/Rebuild Player3 (Richard body)")]
    public static void RebuildP3() => Rebuild("Player3", "Richard");

    [MenuItem("Crucible/Characters/Rebuild Player4 (Salaadin body)")]
    public static void RebuildP4() => Rebuild("Player4", "Salaadin");

    [MenuItem("Crucible/Characters/Rebuild Player5 (Genghis body)")]
    public static void RebuildP5() => Rebuild("Player5", "Genghis");

    [MenuItem("Crucible/Characters/Rebuild Player6 (Buwaka body)")]
    public static void RebuildP6() => Rebuild("Player6", "Buwaka");

    [MenuItem("Crucible/Characters/Report fighters")]
    public static void Report()
    {
        foreach (var n in new[] { "Player1", "Player2", "Player3", "Player4", "Player5", "Player6" })
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
        string charDir = CharDir(charName);
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
        // THROW THE STORED SKELETON AWAY. humanDescription.skeleton keeps a copy of every bone's
        // position from the LAST time the avatar was built, in the .meta, and Unity builds the
        // avatar from that copy rather than from the file. Re-exporting the FBX with joints in new
        // places therefore changes nothing at runtime: the Animator keeps posing the old skeleton
        // while the same hierarchy measures correctly in edit mode, because edit mode reads the
        // file and play mode reads the avatar. Salaadin's corrected elbow was invisible for exactly
        // this reason - the file said 52% of the way from shoulder to wrist and the stored skeleton
        // still said 41%. Clearing both arrays makes CreateFromThisModel regenerate them, which is
        // what "rebuild the body from the FBX" is supposed to mean.
        if (hd.skeleton.Length > 0 || hd.human.Length > 0)
        {
            hd.skeleton = new SkeletonBone[0];
            hd.human = new HumanBone[0];
            reimport = true;
        }
        if (hd.upperArmTwist != 1f || hd.lowerArmTwist != 1f || hd.upperLegTwist != 0.5f || hd.lowerLegTwist != 0.5f)
        {
            hd.upperArmTwist = 1f; hd.lowerArmTwist = 1f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
            reimport = true;
        }
        if (reimport) mi.humanDescription = hd;
        if (reimport) { mi.SaveAndReimport(); Debug.Log($"{charName}: importer set to Humanoid, reimported"); }

        FillMissingHumanBones(fbxPath, charName);

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
        // Scale off the BODY, never the whole model. Taking the bounds of every renderer measures
        // the weapons too, so a fighter holding a sword up gets shrunk until the sword fits the
        // target and a fighter whose blade hangs down barely shrinks at all. That is what left
        // Salaadin standing 2.10 world units against the others' 2.69-2.78: his model bounds were
        // a true 2.90, but two thirds of a metre of it was falchion and shield.
        var rends = inst.GetComponentsInChildren<Renderer>(true);
        // The body is the SKINNED renderer; the weapons are plain MeshRenderers bone-parented to a
        // hand. Measuring only the skinned ones is the whole fix - and do NOT try to derive the
        // height from sharedMesh.bounds instead, because a skinned mesh's vertices are not in its
        // renderer transform's space and that reads Salaadin as a third of his real size.
        var skinned = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).Cast<Renderer>().ToArray();
        var measure = skinned.Length > 0 ? skinned : rends;
        var bounds = measure[0].bounds;
        foreach (var r in measure) bounds.Encapsulate(r.bounds);
        float want = HeightOf(charName);
        float childScale = want / Mathf.Max(bounds.size.y, 1e-4f);
        inst.transform.localScale = Vector3.one * childScale;
        if (GroundOffset.TryGetValue(charName, out float gy)) inst.transform.localPosition = new Vector3(0f, gy, 0f);

        // Fit the physics capsule to the BODY, bottom at the root origin. It used to be a fixed
        // 2.1 high centred at 0.8-1.0, which hangs 0.20 (Bjorn) to 0.75 (everyone else) world
        // units BELOW the feet: gravity rests that overhang on the arena and leaves the fighter
        // hovering by exactly that much, and an overhang that deep catches on every step and
        // ledge and walks him up it. With the bottom on the soles the root lands on the floor,
        // which is also what makes GroundOffset's measured-against-the-root values correct.
        var cap = player.GetComponent<CapsuleCollider>();
        if (cap != null)
        {
            float rootScale = Mathf.Max(player.transform.lossyScale.y, 1e-4f);
            float h = StandingStature(charName) / rootScale;
            Undo.RecordObject(cap, "capsule");
            cap.height = h;
            cap.center = new Vector3(0f, h * 0.5f, 0f);
            Debug.Log($"{charName}: capsule fitted to height {h:F3} root-local ({h * rootScale:F3} world), bottom on the soles");
        }
        Debug.Log($"{charName}: BODY height {bounds.size.y:F3} world units at child scale 1 under root scale {player.transform.lossyScale.y}; child scale set to {childScale:F3} for {want} units, ground offset {inst.transform.localPosition.y:F3}");

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
        { "Richard_Sword",   (0.20f, 0.00f) },   // 1.10 long with the grip at 0: the blade starts just past the cross guard
        { "Salaadin_Falchion", (0.24f, 0.00f) }, // 0.50 long, grip at 0: the curve starts just past the hilt
        { "Genghis_Saber",   (0.24f, 0.04f) },   // 0.55 long, grip at 0.13 from the butt: blade past the guard; left hand near the pommel (two-handed)
        { "Buwaka_Spear",    (0.72f, 0.00f) },   // 1.10 long, mid-haft grip: only the leaf head (top ~28%) is the hitting part
    };

    /// <summary>
    /// How the weapon sits in the right hand, overriding the mount baked by attach_weapons.py: rightGripFrac
    /// is where the hand holds the haft, cantEuler the local rotation under the hand bone. EMPTY for now -
    /// the 2026-10-17 axe-guard mount was dropped when the guard went back to being filmed (see CLAUDE.md).
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<string, (float rightGripFrac, Vector3 cantEuler)> WeaponMount =
        new System.Collections.Generic.Dictionary<string, (float, Vector3)>
    {
    };

    /// <summary>Where the hand grips, in R_Hand local units (the palm point attach_weapons.py mounts to).</summary>
    static readonly Vector3 HandGripPoint = new Vector3(0.0001f, 0.0005f, 0f);

    /// <summary>
    /// The shield fighters' weapons, whose mount is baked in Blender by &lt;Name&gt;_body.py: the local transform
    /// under the hand bone, re-applied after a rebuild so a tweak is not lost to the next reimport. These are
    /// Mixamo rigs, so they do NOT share <see cref="HandGripPoint"/> - that is in the Tripo hand bones' units,
    /// which carry a ~220x scale, and is three orders of magnitude away from these.
    /// Tuned 2026-10-21 against the live rigs: Richard's hilt sat 0.08 world ABOVE his fist (the hand closed on
    /// air below the grip), and the falchion was rolled edge-up, so every cut led with the spine.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<string, (Vector3 localPos, Vector3 localEuler)> ShieldWeaponMount =
        new System.Collections.Generic.Dictionary<string, (Vector3, Vector3)>
    {
        // The 2026-10-22 re-rig moved every hand bone, and a hand-tuned grip OFFSET does not survive
        // that: this one was (0.0386, 0.1908, -0.0029) against the old rig and threw the sword 33
        // world units off his hand. Every other weapon on the roster mounts at the origin of its
        // hand bone, so the offset was compensating for the old rig, not for the sword.
        // The ROTATION survived the re-rig; the POSITION did not exist and needed to. Measured in
        // play: the hand bone sat 0.174 world units OFF the sword's own axis, and the nearest blade
        // vertex was 0.005 from the forearm segment - the sword ran through his forearm instead of
        // sitting in his palm. This offset puts the blade's axis through the hand bone (the hand
        // still grips at 0.09 of the blade's length from the hilt, which is the grip).
        // ...and then it was still held at the WRIST. That offset put the blade's axis through the
        // HAND BONE, which is the wrist joint, so the grip and crossguard ran across the back of his
        // hand and up his forearm while his fingers hung clear of the weapon entirely - the user's
        // "move the sword to the tip of his fingers". Nothing closes Richard's fingers (he has no
        // TwoHandGrip), so the only lever is where the haft sits. Slid along the wrist->fingertip
        // vector by 0.55 of its length: the whole length puts the grip PAST his fingertips and the
        // crossguard clear of the hand, which photographs as a sword floating beside him. At 0.55
        // the haft runs through the hand with the fingers in front of it. Nearest weapon vertex to
        // the fingertip centre 0.089 -> 0.045, and to the wrist 0.131 -> 0.183.
        { "Richard_Sword",     (new Vector3(0.000429f, 0.001780f, 0.000326f), new Vector3(340f, 270f, 0f)) },
        // Miyamoto's katana had NO mount entry, so it sat at whatever rest rotation the FBX gave it
        // under the hand bone - and the re-rig changed that bone's orientation, which left the blade
        // pointing BACKWARD (blade . forward -0.94) straight through his chest, nearest vertex 0.038
        // from his torso axis. His off hand then followed the hilt onto the wrong side of him, which
        // is what read as handcuffed. Solved in play rather than guessed: the rotation that aims the
        // blade forward, up and slightly out. Clears the torso by 0.396 and keeps the grip at 0.057.
        { "Miyamoto_Katana",  (Vector3.zero, new Vector3(357.54f, 351.35f, 357.28f)) },
        { "Salaadin_Falchion", (new Vector3(0.0000f, 0.0003f,  0.0001f), new Vector3(340f, 270f, 180f)) },
        // His round shield was mounted with axis_y="-palm", squared up against his OLD hand-keyed
        // guard. On the pack's block idle the left hand rolls differently and the shield lay FLAT,
        // face to the sky. Solved in play against Block_Hold by swinging the disc normal onto the
        // fighter's forward and then rolling the boss upright: face.forward 0.12 -> 1.00,
        // face.up 0.98 -> 0.00. Re-solve this if his block clip ever changes.
        // Re-solved 2026-10-22 for the Rigify metarig, whose LeftHand carries a different roll
        // (salaadin_metarig.py sets bone Z to the floor): it read 0.91 / 0.12 and is now 1.00 /
        // 0.00 with the disc's top straight up. Measure it in BLOCK_HOLD, never in the idle - a
        // shield at rest hangs off the arm and reads about 0.70 / 0.67 however it is mounted.
        // The POSITION was (0,0,0), which put the hand bone in the disc's own mid-plane: nothing
        // animates his fingers, so the open hand ran straight through the face and stood out
        // 0.106 world units in front of it. Solved in Block_Hold by measuring the fist's vertices
        // (those weighted to LeftHand*) in the SHIELD's own frame: the fist sat 0.093 off the boss
        // in-plane and 0.090 ahead of the disc. This offset slides the boss onto the fist and
        // pushes the disc 0.120 forward along its normal, so the fist sits in the boss hollow the
        // way a centre grip is actually held - in-plane 0.000 off the boss, front-most vertex
        // +0.050 against a front face of +0.064. The facing is unchanged at 1.00 / 1.00.
        // Richard's shield had NO entry at all, so it sat wherever the FBX left it. After the re-rig
        // that was BACKWARDS - its thinnest axis (a flat slab's facing) read -0.82 against his own
        // forward, so he carried the face toward himself - and his fist stood 0.240 PROUD of it,
        // more than twice the miss that was fixed on Salaadin. Solved in play: aim the facing along
        // his forward with the kite's long axis up, slide the boss onto the fist in-plane, then seat
        // the disc so the fist sits 0.020 inside the front face (Salaadin's snug fit is 0.025).
        // ...and then it was still worn INSIDE OUT. The geometry test cannot catch this one: both
        // faces of a kite shield are flat, so "the arm is behind the face that looks at the enemy"
        // read as correct while the enemy was being shown the ENARMES - the arm straps and the grip
        // buckle - and the red cross and gold lion faced Richard. Photographed from both sides to
        // be sure, since no measurement sees paint. Rolled 180 deg about the shield's own LONG
        // axis (z: 344.30 -> 164.30), which swaps the faces and leaves it upright, then re-seated
        // so the fist still sits 0.020 inside the front face.
        // Slid out to the fingers the same way as his sword, and for the same reason: seated on the
        // FIST it was held at the wrist. Same 0.55 of the wrist->fingertip vector, which puts the
        // grip boss in his hand - nearest shield vertex to the fingertip centre 0.083 -> 0.017. The
        // ROTATION is untouched, so the hard-won facing (and the 180 deg roll that stopped him
        // showing the enemy the enarmes) still stands: a translation cannot turn a shield round.
        { "Richard_Shield_L",  (new Vector3(0.000856f, 0.002563f, -0.000085f), new Vector3(0.53f, 122.11f, 164.30f)) },   // pushed 0.125 further out along the disc's own normal: the worst LEFT-HAND vertex went from 0.090 PROUD of the front face to 0.035 behind it, so no fist shows through the paint
        { "Salaadin_Shield_L", (new Vector3(0.000209f, 0.000563f, -0.000347f), new Vector3(0.75f, 181.74f, 355.40f)) },
        // Buwaka's shield is the SAME source model, prep and metarig hand convention as Salaadin's
        // (mount_body.py even reuses his Blender offsets), so his solved entry is the starting
        // point. Verify in Block_Hold - same clip - and re-solve only if the facing reads wrong.
        { "Buwaka_Shield_L",   (new Vector3(0.000209f, 0.000563f, -0.000347f), new Vector3(0.75f, 181.74f, 355.40f)) },
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

        // Put each weapon back where it was tuned, for EVERY fighter. WeaponAim caches this local
        // transform in Awake and restores it every LateUpdate, so the value sitting here at play time
        // IS the mount - which also means a runtime tweak lasts exactly one frame, and the change has
        // to be made on the scene.
        // This used to be inside the !twoHanded branch, which silently skipped Bjorn and Miyamoto, so
        // Miyamoto's katana entry never reached him and his blade stayed pointing through his chest.
        // The gate was there because the two-handed pair were TRIPO rigs, mounted through WeaponMount
        // in Tripo hand-bone units (HandGripPoint, ~220x scale); the 2026-10-22 re-rig made all four
        // Mixamo metarig exports, so one table in world-sane units now serves the whole roster.
        var mountKey2 = ShieldWeaponMount.Keys.FirstOrDefault(k => weapon.name.StartsWith(k));
        if (mountKey2 != null)
        {
            var sm = ShieldWeaponMount[mountKey2];
            Undo.RecordObject(weapon.transform, "weapon mount");
            weapon.transform.localPosition = sm.localPos;
            weapon.transform.localRotation = Quaternion.Euler(sm.localEuler);
            Debug.Log($"{charName}: {weapon.name} mounted at {sm.localPos} / {sm.localEuler}");
        }
        // The off hand's gear is not the weapon found above - SetupWeapon picks the BLADE, by
        // WeaponSpec - so without its own pass a shield entry in the table would never be applied.
        foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.transform == weapon.transform) continue;
            var offKey = ShieldWeaponMount.Keys.FirstOrDefault(k => r.name.StartsWith(k));
            if (offKey == null) continue;
            var om = ShieldWeaponMount[offKey];
            Undo.RecordObject(r.transform, "off-hand mount");
            r.transform.localPosition = om.localPos;
            r.transform.localRotation = Quaternion.Euler(om.localEuler);
            Debug.Log($"{charName}: {r.name} mounted at {om.localPos} / {om.localEuler}");
        }

        bool twoHanded = TwoHanded.Contains(charName);
        if (!twoHanded)
        {
            // A shield fighter: no off-hand grip on the hilt, so drop the IK that would drag his
            // shield arm onto the sword, and let the shield mesh ride the hand bone it is parented to.
            var oldGrip = player.GetComponent(RuntimeType("TwoHandGrip"));
            if (oldGrip != null) Undo.DestroyObjectImmediate(oldGrip);
            var aimOnly = player.GetComponent(RuntimeType("WeaponAim")) ?? Undo.AddComponent(player, RuntimeType("WeaponAim"));
            var sao = new SerializedObject(aimOnly);
            sao.FindProperty("weapon").objectReferenceValue = weapon.transform;
            sao.ApplyModifiedProperties();
            Debug.Log($"{charName}: blade box on {weapon.name} z {y0:F4}..{b.max.z:F4} (mesh local), no two-hand grip");
            return;
        }

        // off-hand socket: on the haft axis, oriented like the right hand bone (same wrap around the haft)
        var socket = weapon.transform.Find("LeftGrip");
        if (socket == null)
        {
            var go = new GameObject("LeftGrip");
            Undo.RegisterCreatedObjectUndo(go, "grip socket");
            socket = go.transform;
            socket.SetParent(weapon.transform, false);
        }
        // guard mount: slide the weapon along its own haft to the solved grip and cant it onto the guard diagonal
        var mountKey = WeaponMount.Keys.FirstOrDefault(k => weapon.name.StartsWith(k));
        if (mountKey != null)
        {
            var m = WeaponMount[mountKey];
            var wt = weapon.transform;
            Undo.RecordObject(wt, "guard mount");
            wt.localRotation = Quaternion.Euler(m.cantEuler);
            float zGrip = b.min.z + m.rightGripFrac * b.size.z;
            wt.localPosition = HandGripPoint - wt.localRotation * new Vector3(b.center.x, b.center.y, zGrip);
        }

        float gripY = b.min.z + spec.leftGripFrac * b.size.z;
        // ON THE HAFT AXIS, which is x = y = 0 - NOT the mesh bounds' centre. A weapon mounts at its
        // hand bone's ORIGIN, so the weapon's own origin IS the right fist and local (0,0,z) runs
        // straight down the haft through it. The bounds CENTRE is a different thing entirely: it is
        // the middle of a box drawn round the whole weapon, and on anything whose mass is not
        // centred on the haft it sits well off it - 0.357 world units on Miyamoto's katana (a long
        // curved blade) and 0.148 on Bjorn's axe (a head on one end). Taking it put Miyamoto's off
        // hand a third of a metre off the hilt, up at his chest, where it read as him not holding
        // the sword at all; Bjorn's was the same fault at half the size, and is most likely what
        // PalmOffset() in TwoHandGrip was quietly compensating for.
        socket.localPosition = new Vector3(0f, 0f, gripY);
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
