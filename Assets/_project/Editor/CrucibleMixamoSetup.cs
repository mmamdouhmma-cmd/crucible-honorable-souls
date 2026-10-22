// Stop-gap animation: wire the fighter controllers to Mixamo packs (Animation/Clips/Mixamo/<Pack>/*.fbx)
// until the hand-keyed clip set exists.
//
// Crucible > Animation > Use Mixamo packs:      imports every pack clip as Humanoid (own avatar, root motion
//     kept on the root so an Animator without "Apply Root Motion" plays walks in place, Y and orientation
//     baked, loops on idle/walk/block), then re-points the P1/P2 controller states at the mapped clips and
//     sets each attack state's speed so the clip spans the attack's startup+active+recovery frames.
// Crucible > Animation > Restore custom clips:  points the states back at Animation/Clips/Custom/*.fbx.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrucibleMixamoSetup
{
    const string MixamoDir = "Assets/_project/Animation/Clips/Mixamo";
    const string CustomDir = "Assets/_project/Animation/Clips/Custom";
    const string P1Ctrl = "Assets/_project/Animation/Controllers/P1_Bjorn.controller";
    const string P2Ctrl = "Assets/_project/Animation/Controllers/P2_Miyamoto.controller";
    const string P3Ctrl = "Assets/_project/Animation/Controllers/P3_Richard.controller";
    const string P4Ctrl = "Assets/_project/Animation/Controllers/P4_Salaadin.controller";

    /// <summary>Where a pack lives. "Custom" is the hand-keyed set in Animation/Clips/Custom.</summary>
    static string PackDir(string pack) => pack == "Custom" ? CustomDir : $"{MixamoDir}/{pack}";

    // state name -> (pack, clip file stem, loop)
    static readonly (string state, string pack, string clip, bool loop)[] P1Map =
    {
        // AxePack2H = the axe pack carry re-posed in Blender (mixamo_carry2h.py) so the weapon sits in front of
        // the belly where the left-hand IK can reach the haft
        ("Bjorn_Idle",                   "AxePack2H", "standing idle",                   true),
        ("Bjorn_Walk_Forward",           "AxePack2H", "standing walk forward",           true),
        ("Bjorn_Walk_Back",              "AxePack2H", "standing walk back",              true),
        ("Bjorn_Light_Slash",            "AxePack", "standing melee attack horizontal",  false),
        ("Bjorn_Medium_SpinningSlash",   "AxePack2H", "standing melee attack 360 high",  false),   // arms re-posed: axe extended at chest, body spins
        ("Bjorn_Heavy_OverheadDoubleAxe","GreatSwordPack", "great sword slash",       false),   // two-handed overhead that comes down in front; the axe pack's chop bottoms out by the left hip
        // The string ender (Light -> this on Heavy, see AttackData.comboLinks). Deliberately the SAME clip as
        // Miyamoto's heavy - the 360 spin - so the combo finishes on a spinning strike; its own trim window is
        // cut from the same file under this state's name.
        ("Bjorn_Heavy_SpinOverhead",     "GreatSwordPack", "great sword high spin attack", false),
        // SPECIALS (SP + button). Cast from the measured pool in Design/Anim_Inventory.md; each one's
        // window is re-cut around its swing_contacts.json entry, like every other attack state.
        // Ripostes: the move a counter window gives instead of a normal strike.
        ("Bjorn_Riposte_Dash",           "AxePack", "standing melee combo attack ver. 3", false),  // fastest tip + tightest core in either pack
        ("Bjorn_Riposte_Parry",          "AxePack", "standing melee attack kick ver. 2",  false),  // a KICK: hitsWithWeapon is off, the body box judges it
        ("Bjorn_Special_HaftStrike",     "AxePack", "standing melee attack backhand",    false),   // big reverse horizontal, winds 245 deg and returns square
        ("Bjorn_Special_HewingCharge",   "AxePack", "standing melee attack 360 low",     false),   // the low twin of his medium: same full turn, tip 0.98 lower
        ("Bjorn_Special_BloodEagle",     "AxePack", "standing melee run jump attack",    false),   // leaping chop; its 3.64 m of root motion is discarded, so the AttackData lunges instead
        ("React_Front",                  "AxePack", "standing react large gut",          false),
        ("React_Left",                   "AxePack", "standing react large from left",    false),
        ("React_Right",                  "AxePack", "standing react large from right",   false),
        ("Block_Hold",                   "AxePack", "standing block idle",               true),
        ("Block_Impact",                 "AxePack", "standing block react large",        false),
        // parry: the catch gets its own animation (the parry itself is a well-timed block, so the
        // guard raise IS the attempt). Speed comes from the ParrySpeed parameter, not the fit.
        ("Parry_Success",                "AxePack", "standing block react large",            false),
        ("Death",                        "GreatSwordPack", "two handed sword death",         false),
        // dashes = one big step: the first stride (half cycle) of the two-handed walks, fitted to the dash time
        ("Dash_Forward",                 "AxePack2H", "standing walk forward",           false),
        ("Dash_Back",                    "AxePack2H", "standing walk back",              false),
        ("Dash_Left",                    "AxePack2H", "standing walk left",              false),
        ("Dash_Right",                   "AxePack2H", "standing walk right",             false),
    };
    static readonly (string state, string pack, string clip, bool loop)[] P2Map =
    {
        // locomotion from the (male) axe pack: the Great Sword walk is Maria's, with a feminine sway.
        ("Miyamoto_Idle",                "AxePack2H", "standing idle",                   true),
        ("Miyamoto_Walk_Forward",        "AxePack2H", "standing walk forward",           true),
        ("Miyamoto_Walk_Back",           "AxePack2H", "standing walk back",              true),
        // strikes chosen by hand-trajectory analysis (swing_analysis / strike_analysis in the session notes):
        // light = horizontal middle slash, medium = overhead diagonal, heavy = spinning strike
        ("Miyamoto_Light_Thrust",        "GreatSwordPack", "great sword slash (3)",     false),
        ("Miyamoto_Medium_SideSlash",    "GreatSwordPack", "great sword slash",         false),
        // The user's call after play-testing: a standalone heavy must read as a SLASH. "great sword
        // high spin attack" measures as diagonal-up+spin+advancing - 416 deg of hip yaw and 2.34
        // units of forward charge - and its stored trim core was the WHOLE clip (1-57), so what
        // played at x2 was mostly the charge: it read as a lunging THRUST, which is exactly the
        // complaint. It moves to SP+Heavy (Kuruma) where that much body is right. "great sword
        // casting" is the only non-spinning overhead left in the pack: yaw 23 deg, hips 0.00,
        // spin=False, tip coming down 0.62 over a 10-frame core.
        ("Miyamoto_Heavy_Overhead",      "GreatSwordPack", "great sword casting",       false),
        // SPECIALS (SP + button)
        ("Miyamoto_Riposte_Dash",        "GreatSwordPack", "great sword kick",          false),   // spinning kick, ends square
        ("Miyamoto_Riposte_Parry",       "GreatSwordPack", "great sword kick (2)",      false),   // front push kick; contact is at the very end of the clip
        ("Miyamoto_Special_Nukitsuke",   "GreatSwordPack", "great sword slash (5)",     false),   // rising reverse cut: a silhouette none of his normals have
        ("Miyamoto_Special_NitoCross",   "GreatSwordPack", "great sword slash (4)",     false),   // full-turn rising cut - keeps Nito Cross a katana move (no wakizashi exists)
        ("Miyamoto_Special_Iaijutsu",    "GreatSwordPack", "great sword attack",        false),   // the DOUBLE THRUST: f12-17 pushes, f18-23 retracts, f24-36 drives deeper. COMBO-ONLY now (see CrucibleCombos): it sits AFTER Kuruma in the scene's specials array, and FindAttack returns the first match, so SP+Heavy pressed cold throws Kuruma and only a live string can reach the thrust
        ("Miyamoto_Special_Kuruma",      "GreatSwordPack", "great sword high spin attack", false), // the wheel: the spin charge that used to be the normal heavy
        ("React_Front",                  "GreatSwordPack", "great sword impact",        false),
        ("React_Left",                   "GreatSwordPack", "great sword impact (2)",    false),
        ("React_Right",                  "GreatSwordPack", "great sword impact (3)",    false),
        ("Block_Hold",                   "GreatSwordPack", "great sword blocking (2)",  true),    // the static guard; "great sword blocking" is the raise and looped visibly
        ("Block_Impact",                 "AxePack",        "standing block react large", false),  // jolt that settles back into the guard (the sword pack has none)
        ("Parry_Success",                "AxePack",        "standing block react large", false),
        ("Death",                        "GreatSwordPack", "two handed sword death (2)",  false),
        ("Dash_Forward",                 "AxePack2H", "standing walk forward",           false),
        ("Dash_Back",                    "AxePack2H", "standing walk back",              false),
        ("Dash_Left",                    "AxePack2H", "standing walk left",              false),
        ("Dash_Right",                   "AxePack2H", "standing walk right",             false),
    };

    // Richard: the Pro Sword and Shield pack, plus the four clips it does not contain
    // (ArtSource/Blender/richard_clips.py keys them on his own rig: the thrust, the shield charge,
    // the bash into a low cut, and the two high swings spliced into one move).
    static readonly (string state, string pack, string clip, bool loop)[] P3Map =
    {
        ("Richard_Idle",                   "SwordShieldPack", "sword and shield idle (4)",   true),   // a settled fighting stance: 76 f, 8 cm of hand drift and a 0.3 cm loop gap, where "idle" (109 f) and idles 2-3 (224/261 f) look around and shift their weight
        ("Richard_Walk_Forward",           "SwordShieldPack", "sword and shield walk",       true),
        ("Richard_Walk_Back",              "SwordShieldPack", "sword and shield walk (2)",   true),
        ("Richard_Light_Thrust",           "Custom",          "Richard_Thrust",              false),
        ("Richard_Medium_Slash",           "SwordShieldPack", "sword and shield slash (5)",  false),  // diagonal-down, 42 f, the cleanest quick cut in the pack
        ("Richard_Heavy_Overhead",         "SwordShieldPack", "sword and shield slash",      false),  // the pack's true overhead: tip drops 1.44, fastest tip in the set
        // All three of his specials were re-cast on the user's call: no running and no jumping.
        // That rules out most of the pack - measured travel over the clip, "attack (2)" advances
        // 2.87, "attack (3)" 2.41, "slash (2)" 2.45 with a leap, "slash (4)" leaps too, and the run
        // clips are what the charge was built from. Only four strike clips in the whole
        // SwordShieldPack are thrown from a standing start: "casting" (travel 0.00, a 2.22 overhead
        // descent), "power up" (0.00, 1.40 descent), "draw sword 1/2" (0.08, a rising draw cut) and
        // "kick" (0.00, already his parry riposte).
        // ...and that first screen was too strict, because ROOT MOTION IS DISCARDED on these clips
        // (the same reason Bjorn's Blood Eagle throws away 3.64 m): a clip's own travel never
        // reaches the game, the AttackData lunge does. So "advancing" is not running. What IS
        // reproduced is the hips RISING, because that is body animation - so the test for "no
        // jumping" is maxHipsAboveRoot in play, not the clip's travel. That reopened the two
        // standing spins below, and both measure flat.
        // The gesture-named clips really are unusable: "casting" trimmed to its descent photographs
        // as Richard going down on one knee with the shield planted - a buff, not a strike. The
        // measurement said "overhead, 2.22 of drop" because that is the sword lowering as he kneels.
        // Judge a clip from a PHOTOGRAPH of the trimmed window in play, never from its numbers.
        // Each special now plays the clip of the NORMAL on the same button - the user's call, after
        // the pack ran out of standing strikes that were not spins or gestures. What makes a special
        // read as one is the camera: FightCamera.OnImpact shakes harder and longer for a Special.
        // A trim is keyed by CLIP, so these share the normals' trims; the per-STATE contacts in
        // swing_contacts.json keep each special's own window and speed.
        ("Richard_Special_ShieldCharge",   "Custom",          "Richard_Thrust",              false),
        // The user's call after play-testing: both authored specials read as "melty", so they take
        // pack clips untouched. Two rounds of picking. Measure yaw with clip_curves, which UNWRAPS it -
        // clip_measure wraps, which is how "attack (3)" passed as a 36 deg cut when it is really a full
        // 396 deg spin - and the pack's strikes are mostly spins: attack (2) 429, slash (2) 507,
        // slash (4) 391. The only two clips NOBODY else used, "power up" and "casting", measure as
        // overheads and are not strikes at all: photographed in play they hold the sword up across the
        // chest, a spell-cast gesture. So these share with Salaadin, as the walks and slash (5) already
        // do - both are real swings, no spin, no travel.
        ("Richard_Special_ShieldBash",     "SwordShieldPack", "sword and shield slash (5)",  false),  // his Medium's diagonal cut
        // Back to a spliced clip, on the user's call: a single pack chop here reads as BJORN's
        // special - his Blood Eagle is "standing melee run jump attack" and this pack's "attack" is
        // an overhead+leap too, so the two look the same whatever the weapon. Twin Hew is two high
        // swings, which is what the move is named for. See clip_double_high: pack frames verbatim,
        // only the four-frame join is authored.
        ("Richard_Special_DoubleHigh",     "SwordShieldPack", "sword and shield slash",      false),  // his Heavy's overhead
        ("Richard_Riposte_Dash",           "SwordShieldPack", "sword and shield attack (4)", false),  // 31 f, the shortest cut in the pack
        ("Richard_Riposte_Parry",          "SwordShieldPack", "sword and shield kick",       false),  // a BOOT: hitsWithWeapon is off
        ("React_Front",                    "SwordShieldPack", "sword and shield impact",     false),
        ("React_Left",                     "SwordShieldPack", "sword and shield impact (2)", false),
        ("React_Right",                    "SwordShieldPack", "sword and shield impact (3)", false),
        ("Block_Hold",                     "SwordShieldPack", "sword and shield block idle", true),   // the shield guard - his block really is the shield
        ("Block_Impact",                   "SwordShieldPack", "sword and shield block",      false),
        ("Parry_Success",                  "SwordShieldPack", "sword and shield block (2)",  false),
        ("Death",                          "SwordShieldPack", "sword and shield death",      false),
        // dashes: the runs and the fast strafes, each cut to one stride and fitted to the dash time
        ("Dash_Forward",                   "SwordShieldPack", "sword and shield run",        false),
        ("Dash_Back",                      "SwordShieldPack", "sword and shield run (2)",    false),
        ("Dash_Left",                      "SwordShieldPack", "sword and shield strafe (3)", false),
        ("Dash_Right",                     "SwordShieldPack", "sword and shield strafe (4)", false),
    };

    // Salaadin shares the pack with Richard but not his clips: Richard holds the pack's only
    // settled idle, so Salaadin's guard stance and the cuts the pack cannot give him are
    // hand-keyed (ArtSource/Blender/salaadin_clips.py) and his cuts are the variants Richard
    // left. The walks ARE shared - the pack has two and Richard has both (the user's call).
    static readonly (string state, string pack, string clip, bool loop)[] P4Map =
    {
        ("Salaadin_Idle",                  "SwordShieldPack", "sword and shield idle (4)",     true),   // the pack's ONLY settled idle: hand travels 0.22 against "idle"'s 2.54, which reads as wandering
        ("Salaadin_Walk_Forward",          "SwordShieldPack", "sword and shield walk",         true),   // shared with Richard
        ("Salaadin_Walk_Back",             "SwordShieldPack", "sword and shield walk (2)",     true),   // shared with Richard
        // The user's call: his light was SPINNING. Measured, "attack (2)" is horizontal+spin+advancing
        // with 428.8 deg of hip yaw and 2.87 units of travel - a full turn, not a cut, and it had no
        // trim entry either so it played whole at x4.33. "attack (4)" is the pack's most compact
        // clean cut: diagonal-down, yaw 63.2 deg, hips 0.01, an 11-frame core in a 31-frame clip.
        ("Salaadin_Light_SaifCut",         "SwordShieldPack", "sword and shield attack (4)",   false),
        ("Salaadin_Medium_ShinCut",        "SwordShieldPack", "sword and shield attack (3)",   false),  // 53 f, shoulder 4.17 and 36 deg of hip turn: the body goes with the sword
        ("Salaadin_Heavy_WhirlingSaif",    "SwordShieldPack", "sword and shield slash (4)",    false),  // a PLAIN overhead: vert 3.07 (drop/side) with only 9 deg of hip turn, hand from 1.32 above the belt down to -0.54. "attack" also drops a long way but turns the hips 43 deg and travels the shoulder 6.53, which is what read as a spin
        ("Salaadin_Special_VeiledRush",    "SwordShieldPack", "sword and shield slash (2)",    false),  // 107 f and a 21.14 hand path: the longest flourish in the pack, so the special reads as one
        ("Salaadin_Special_CrescentSlash", "SwordShieldPack", "sword and shield slash (5)",    false),  // the pack's LOW slash: the hand never rises above 0.69 of a hips-to-head (every other strike reaches 1.3+) and sweeps 2.01 sideways against 1.04 of drop. See swing_trims.json - its core had to be widened or it played as a 6-frame smear
        ("Salaadin_Special_HattinCleave",  "SwordShieldPack", "sword and shield attack",       false),  // the big committed chop freed up by the heavy: 4.00 of drop, 43 deg of hip turn and the pack's longest shoulder travel at 6.53 - too much body for a normal, right for a special
        ("Salaadin_Riposte_Dash",          "SwordShieldPack", "sword and shield slash (3)",    false),  // 48 f
        ("Salaadin_Riposte_Parry",         "SwordShieldPack", "sword and shield attack (2)",   false),  // a saber counter, not a boot - he has no kicks. It takes the 428 deg spin the light gave up: a parry answer is the one place that much turn reads as flourish rather than as a mistake
        ("React_Front",                    "SwordShieldPack", "sword and shield impact",       false),  // only three impacts exist; shared
        ("React_Left",                     "SwordShieldPack", "sword and shield impact (2)",   false),
        ("React_Right",                    "SwordShieldPack", "sword and shield impact (3)",   false),
        ("Block_Hold",                     "SwordShieldPack", "sword and shield block idle",   true),
        ("Block_Impact",                   "SwordShieldPack", "sword and shield block",        false),
        ("Parry_Success",                  "SwordShieldPack", "sword and shield block (2)",    false),
        ("Death",                          "SwordShieldPack", "sword and shield death (2)",    false),  // the pack's other death, so he does not fall like Richard
        ("Dash_Forward",                   "SwordShieldPack", "sword and shield run",          false),
        ("Dash_Back",                      "SwordShieldPack", "sword and shield run (2)",      false),
    };

    // Attack clips are trimmed to their swing core (measured in Blender: Animation/Clips/Mixamo/swing_trims.json,
    // peak right-hand speed + core window per clip) so the peak lands on the attack's hit frame.
    static Dictionary<string, (float first, float last, int frames)> Trims = new Dictionary<string, (float, float, int)>();

    static void ComputeTrims((string state, string pack, string clip, bool loop)[] map, string attackDir)
    {
        string jsonPath = $"{MixamoDir}/swing_trims.json";
        if (!File.Exists(jsonPath)) { Debug.LogWarning("no swing_trims.json; attack clips will be fitted whole"); return; }
        string json = File.ReadAllText(jsonPath);
        var attacks = AssetDatabase.FindAssets("t:AttackData", new[] { attackDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g))).ToList();
        foreach (var m in map)
        {
            var atk = attacks.FirstOrDefault(a => a.name == m.state);
            if (atk == null) continue;
            var so = new SerializedObject(atk);
            int s = so.FindProperty("startupFrames").intValue, a = so.FindProperty("activeFrames").intValue, r = so.FindProperty("recoveryFrames").intValue;
            int total = s + a + r;
            var block = System.Text.RegularExpressions.Regex.Match(json, "\"" + System.Text.RegularExpressions.Regex.Escape(m.clip) + "\"\\s*:\\s*\\{([^}]*)\\}");
            if (!block.Success) { Debug.LogWarning($"{m.clip}: not in swing_trims.json"); continue; }
            int Get(string key) => int.Parse(System.Text.RegularExpressions.Regex.Match(block.Groups[1].Value, "\"" + key + "\"\\s*:\\s*(-?\\d+)").Groups[1].Value);
            int first = Get("first"), last = Get("last"), peak = Get("peak"), cs = Get("core_start"), ce = Get("core_end");
            float hitFrac = (s + a * 0.5f) / total;
            float len = Mathf.Max(ce - cs, 6);
            float start = peak - hitFrac * len;
            // Measured contact (Animation/Clips/Mixamo/swing_contacts.json: state -> source frame where the weapon TIP
            // crosses the opponent's line, from BladeTrace) beats the hand-speed peak: the window is re-cut around it
            // at a fixed playback speed so the contact lands on the hit frame.
            float contact = ContactFrame(m.state);
            if (!float.IsNaN(contact))
            {
                len = Mathf.Max(ContactSpeedFor(m.state) * total * 0.5f, 6f);   // source frames: speed x total/60 s x 30 fps
                start = contact - hitFrac * len;
            }
            if (start < first) start = first;
            float end = Mathf.Min(start + len, last);
            if (end - start < len - 0.5f && !float.IsNaN(contact)) start = Mathf.Max(first, end - len);
            Trims[m.state] = (start, end, total);
            Debug.Log($"{m.state}: clip {m.clip} frames {first}-{last}, peak {peak}, core {cs}-{ce}, contact {(float.IsNaN(contact) ? "-" : contact.ToString("F1"))} -> window {start:F1}-{end:F1} for {total} f (hit at {hitFrac:P0})");
        }
    }

    const float ContactSpeed = 1.8f;   // playback speed used for contact-calibrated windows
    const float ReactDuration = 0.45f; // seconds a get-hit flinch takes on screen
    const float ParryDuration = 0.40f; // base on-screen length of the parry catch; Fighter's
                                       // parryAnimSpeed (the ParrySpeed parameter) multiplies it

    // swing_contacts.json entries are either  "State": 28.7  or  "State": {"contact": 36.2, "speed": 2.0}
    static float ContactFrame(string state) => ContactField(state, "contact", float.NaN);
    static float ContactSpeedFor(string state) => ContactField(state, "speed", ContactSpeed);

    static float ContactField(string state, string field, float fallback)
    {
        string p = $"{MixamoDir}/swing_contacts.json";
        if (!File.Exists(p)) return fallback;
        string text = File.ReadAllText(p);
        string key = "\"" + System.Text.RegularExpressions.Regex.Escape(state) + "\"\\s*:\\s*";
        var num = System.Text.RegularExpressions.Regex.Match(text, key + "(-?[0-9.]+)");
        if (num.Success) return field == "contact" ? float.Parse(num.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
        var obj = System.Text.RegularExpressions.Regex.Match(text, key + "\\{([^}]*)\\}");
        if (!obj.Success) return fallback;
        var f = System.Text.RegularExpressions.Regex.Match(obj.Groups[1].Value, "\"" + field + "\"\\s*:\\s*(-?[0-9.]+)");
        return f.Success ? float.Parse(f.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
    }

    [MenuItem("Crucible/Animation/Use Mixamo packs")]
    public static void UseMixamo()
    {
        Trims.Clear();
        ComputeTrims(P1Map, "Assets/_project/Characters/Bjorn/Attacks");
        ComputeTrims(P2Map, "Assets/_project/Characters/Miyamoto/Attacks");
        ComputeTrims(P3Map, "Assets/_project/Characters/Richard/Attacks");
        ComputeTrims(P4Map, "Assets/_project/Characters/salaadin/Attacks");
        ImportAll(P1Map, P2Map, P3Map, P4Map);
        Wire(P1Ctrl, P1Map, "Assets/_project/Characters/Bjorn/Attacks", "Player1");
        Wire(P2Ctrl, P2Map, "Assets/_project/Characters/Miyamoto/Attacks", "Player2");
        if (File.Exists(P3Ctrl)) Wire(P3Ctrl, P3Map, "Assets/_project/Characters/Richard/Attacks", "Player3");
        if (File.Exists(P4Ctrl)) Wire(P4Ctrl, P4Map, "Assets/_project/Characters/salaadin/Attacks", "Player4");
        AssetDatabase.SaveAssets();
        Debug.Log("Mixamo packs wired into P1_Bjorn and P2_Miyamoto. Play to check; Dash_* states keep the custom clips.");
    }

    [MenuItem("Crucible/Animation/Restore custom clips")]
    public static void RestoreCustom()
    {
        foreach (var (ctrlPath, prefix) in new[] { (P1Ctrl, "Bjorn"), (P2Ctrl, "Miyamoto") })
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            foreach (var cs in ac.layers[0].stateMachine.states)
            {
                var st = cs.state;
                string[] candidates = { $"{CustomDir}/{st.name}.fbx", $"{CustomDir}/{prefix}_{st.name}.fbx" };
                var clip = candidates.Select(p => LoadClip(p)).FirstOrDefault(c => c != null);
                if (clip == null) { Debug.LogWarning($"{st.name}: no custom clip found"); continue; }
                st.motion = clip; st.speed = 1f;
            }
            EditorUtility.SetDirty(ac);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Custom clips restored on both controllers.");
    }


    /// <summary>
    /// The avatar an authored clip must be imported against: `Clips/Custom/&lt;Name&gt;_*.fbx` belongs to
    /// &lt;Name&gt;, whose rig it was keyed on. Null for the shared pack clips, which keep their own.
    /// Salaadin's character folder is lowercase on disk, so the lookup tries both spellings.
    /// </summary>
    static Avatar OwnerAvatar(string path)
    {
        if (Path.GetFileName(Path.GetDirectoryName(path)) != "Custom") return null;
        string stem = Path.GetFileNameWithoutExtension(path);
        int cut = stem.IndexOf('_');
        if (cut <= 0) return null;
        string name = stem.Substring(0, cut);
        foreach (var dir in new[] { name, name.ToLowerInvariant() })
        {
            string body = $"Assets/_project/Characters/{dir}/Model/{name}_Body.fbx";
            if (!File.Exists(body)) continue;
            var av = AssetDatabase.LoadAllAssetsAtPath(body).OfType<Avatar>().FirstOrDefault();
            if (av != null) return av;
            Debug.LogWarning($"{body} has no Avatar; {stem} keeps its own");
            return null;
        }
        Debug.LogWarning($"no body FBX found for {stem}; it keeps its own avatar");
        return null;
    }

    static void ImportPack(string pack, (string state, string pack, string clip, bool loop)[] map) { ImportAll(map); }

    // One importer can hold several clips cut from the same take (walk + dash from one walk file, and the same
    // file mapped under a P1 and a P2 state name), so entries are grouped by file and written together.
    static void ImportAll(params (string state, string pack, string clip, bool loop)[][] maps)
    {
        var byFile = new Dictionary<string, List<(string state, bool loop)>>();
        foreach (var map in maps)
            foreach (var m in map)
            {
                string path = $"{PackDir(m.pack)}/{m.clip}.fbx";
                if (!byFile.TryGetValue(path, out var list)) byFile[path] = list = new List<(string, bool)>();
                if (!list.Any(e => e.state == m.state)) list.Add((m.state, m.loop));
            }
        // Hand-keyed clips exported from Salaadin_Studio.blend are not in any map yet - the artist
        // makes the clip first and points a state at it after. Without an entry here they would be
        // imported Generic with no avatar, which is the one setup that cannot work, so sweep in any
        // <Character>_*.fbx sitting in Clips/Custom and give it the same treatment as a mapped one.
        // OwnerAvatar decides whose it is from the file name, and returns null for anything else.
        foreach (var path in Directory.GetFiles(CustomDir, "*.fbx").Select(p => p.Replace(Path.DirectorySeparatorChar, '/')))
            if (!byFile.ContainsKey(path) && OwnerAvatar(path) != null)
                // one clip named after the file, so it still gets Based Upon: Original and the
                // restlead frame dropped - an empty list would write no clipAnimations at all
                byFile[path] = new List<(string, bool)> { (Path.GetFileNameWithoutExtension(path), false) };

        foreach (var kv in byFile)
        {
            string path = kv.Key;
            if (!File.Exists(path)) { Debug.LogError($"missing {path}"); continue; }
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            bool dirty = false;
            if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; dirty = true; }
            // A clip authored in ArtSource/Blender/*_clips.py is built on ONE character's rig, so it
            // can share that character's avatar outright - and it must. Retargeting from a clip's own
            // auto-built avatar onto the body's is an identity only if the two agree exactly, and even
            // on the same skeleton they did not: the wrist twist did not survive the hop and Salaadin's
            // guard arrived with the shield rolled 67 deg off square. Copying removes the hop. Pack
            // clips keep their own avatar - they are shared by fighters with different bodies.
            var owner = OwnerAvatar(path);
            if (owner != null)
            {
                if (mi.avatarSetup != ModelImporterAvatarSetup.CopyFromOther) { mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; dirty = true; }
                if (mi.sourceAvatar != owner) { mi.sourceAvatar = owner; dirty = true; }
                // The clip importer CACHES the copied rig configuration, and Unity builds the clip's
                // avatar from that CACHE, not from the source avatar - the same trap humanDescription
                // sets on a body (see Rebuild Player<N>). Re-rig a body with a different bone count and
                // every clip goes on demanding the bones that are gone. Salaadin's hand-edited rig
                // dropped the four toe bones and all five authored clips died with "Transform
                // 'mixamorig:LeftToeBase' for human bone 'LeftToes' not found", importing with NO takes
                // and no AnimationClip at all - so the states silently fell back to pack clips and he
                // fought in a stranger's idle. Clearing the cache is NOT the fix: an empty one gives
                // "No human bone found. Ensure both rig type match". It has to be overwritten with the
                // body's own description.
                var bodyMi = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(owner)) as ModelImporter;
                if (bodyMi != null)
                {
                    var bhd = bodyMi.humanDescription;
                    var chd = mi.humanDescription;
                    bool match = chd.human.Length == bhd.human.Length && chd.skeleton.Length == bhd.skeleton.Length;
                    for (int i = 0; match && i < bhd.human.Length; i++)
                        match = chd.human[i].humanName == bhd.human[i].humanName
                             && chd.human[i].boneName == bhd.human[i].boneName;
                    // POSITIONS too, not just names. The cache stores where every joint sits, and a
                    // re-rig that keeps the bone list identical but moves a knee leaves the clip
                    // retargeting onto the OLD skeleton with no error of any kind - the same trap
                    // that made Salaadin's corrected elbow invisible, one level further out.
                    for (int i = 0; match && i < bhd.skeleton.Length; i++)
                        match = chd.skeleton[i].name == bhd.skeleton[i].name
                             && (chd.skeleton[i].position - bhd.skeleton[i].position).sqrMagnitude < 1e-10f;
                    if (!match) { mi.humanDescription = bhd; dirty = true; }
                }
            }
            else if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            // A copied avatar only validates if the clip's node chain matches the body's, which is
            // what the EMPTY exported beside the armature is for (see export() in *_clips.py). A clip
            // built before that lands here with no takes at all, so put it back on its own avatar
            // rather than leave a state pointing at nothing.
            if (dirty && owner != null)
            {
                mi.SaveAndReimport(); dirty = false;
                if (mi.defaultClipAnimations.Length == 0)
                {
                    Debug.LogWarning($"{Path.GetFileName(path)}: {owner.name} does not fit it - rebuild the clip " +
                                     "through ArtSource/Blender/*_clips.py. Falling back to its own avatar.");
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    mi.sourceAvatar = null;
                    mi.SaveAndReimport();
                }
            }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            var takes = mi.defaultClipAnimations;
            if (takes.Length == 0) { Debug.LogError($"{path}: no takes"); continue; }
            var take = takes[0];
            // Clips authored in ArtSource/Blender/*_clips.py open with one REST-POSE LEAD-IN frame.
            // It is there to give an armature-only FBX a correct bind pose - without it Unity builds
            // the clip's avatar from the clip's own first pose, and every frame retargets to the
            // body's T-pose plus a delta (straight elbows, arms out sideways, a flat shield). The
            // lead-in itself must never play, and the exporter names its take "restlead" to say so.
            bool restLead = take.takeName != null && take.takeName.Contains("restlead");
            var wanted = new List<ModelImporterClipAnimation>();
            foreach (var (state, loop) in kv.Value)
            {
                var c = new ModelImporterClipAnimation();
                c.takeName = take.takeName;
                c.name = state;
                c.firstFrame = take.firstFrame + (restLead ? 1 : 0); c.lastFrame = take.lastFrame;
                c.loopTime = loop;
                c.loopPose = false;
                c.lockRootRotation = true;         // bake orientation into the pose
                // Based Upon: ORIGINAL for a clip we authored, BODY ORIENTATION for a pack clip.
                // "Body orientation" makes Unity work out which way the character faces from the
                // posed body and bake the difference into every frame - right for mocap that was
                // captured facing anywhere, wrong for a clip keyed facing a known direction. On
                // Salaadin's bladed guard it read the turned shoulders as the facing and yawed the
                // WHOLE skeleton 66.6 deg: the shield came out edge-on to the opponent and the
                // sabre pointed across him, while the clip itself sampled perfectly. The tell is
                // that every bone is off by the same angle.
                c.keepOriginalOrientation = owner != null;
                c.lockRootHeightY = true;          // bake Y
                // Based Upon: ORIGINAL, for authored clips too. When Richard's four came in at
                // RootT.y -23 to -50 against a healthy ~1 and his skeleton played a metre under the
                // arena - the "melting into the floor" - FEET looked like the fix and is not: it
                // moved him a little and left him underground, because the fault was in the clip,
                // not the reference. See the unit conversion in richard_clips.py. Measure the clip's
                // RootT.y before touching this: a healthy clip does not need rescuing here.
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = false;      // XZ stays root motion: not applied by the Animator, so in place
                c.keepOriginalPositionXZ = false;
                if (Trims.TryGetValue(state, out var t)) { c.firstFrame = t.first + (restLead ? 1 : 0); c.lastFrame = t.last; }
                else if (state.StartsWith("Dash_"))
                {   // one stride = the first half of the walk cycle
                    c.lastFrame = take.firstFrame + (take.lastFrame - take.firstFrame) * 0.5f;
                }
                wanted.Add(c);
            }
            var current = mi.clipAnimations;
            bool same = current.Length == wanted.Count;
            for (int i = 0; same && i < wanted.Count; i++)
            {
                var a = current[i]; var b = wanted[i];
                // keepOriginalPositionY and lockRootHeightY belong in this test: without them a
                // changed root-height setting is judged "same", nothing is reimported, and the fix
                // silently does not happen - which is how the melting survived its first fix.
                same = a.name == b.name && a.loopTime == b.loopTime && a.lockRootPositionXZ == b.lockRootPositionXZ && a.lockRootRotation == b.lockRootRotation
                       && a.keepOriginalOrientation == b.keepOriginalOrientation
                       && a.keepOriginalPositionY == b.keepOriginalPositionY
                       && a.lockRootHeightY == b.lockRootHeightY
                    && Mathf.Abs(a.firstFrame - b.firstFrame) < 0.01f && Mathf.Abs(a.lastFrame - b.lastFrame) < 0.01f;
            }
            if (!same) { mi.clipAnimations = wanted.ToArray(); dirty = true; }
            if (dirty) mi.SaveAndReimport();
        }
    }

    static void Wire(string ctrlPath, (string state, string pack, string clip, bool loop)[] map, string attackDir, string playerName)
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        float dashDuration = 0.25f;
        var playerGo = GameObject.Find(playerName);
        var fighterComp = playerGo != null ? playerGo.GetComponent("Fighter") : null;
        if (fighterComp != null) { var sf = new SerializedObject(fighterComp); var pd = sf.FindProperty("dashDuration"); if (pd != null) dashDuration = pd.floatValue; }
        var attacks = AssetDatabase.FindAssets("t:AttackData", new[] { attackDir })
            .Select(g => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g))).ToList();
        foreach (var m in map)
        {
            var cs = ac.layers[0].stateMachine.states.FirstOrDefault(s => s.state.name == m.state);
            if (cs.state == null) { Debug.LogWarning($"{Path.GetFileName(ctrlPath)}: no state {m.state}"); continue; }
            var clip = LoadClip($"{PackDir(m.pack)}/{m.clip}.fbx", m.state);
            if (clip == null) { Debug.LogError($"{m.clip}: clip {m.state} failed to import"); continue; }
            cs.state.motion = clip;
            cs.state.speed = 1f;
            // The parry catch is a 1.3 s performance: fit it to ParryDuration, then let the
            // ParrySpeed parameter (Fighter.parryAnimSpeed, default 2) multiply that on top, so the
            // speed is editable from the component without touching the controller.
            if (m.state == "Parry_Success" && clip.length > 0.05f)
            {
                cs.state.speed = clip.length / ParryDuration;
                cs.state.speedParameterActive = true;
                cs.state.speedParameter = "ParrySpeed";
                Debug.Log($"{m.state}: {clip.length:F2}s fitted to {ParryDuration:F2}s (x{cs.state.speed:F2}), then x ParrySpeed");
            }
            // flinches: the Mixamo reacts are 1-2 s stagger performances; play each over ReactDuration so the
            // stun reads as a hit, not a collapse
            if (m.state.StartsWith("React_") && clip.length > 0.05f)
            {
                cs.state.speed = clip.length / ReactDuration;
                Debug.Log($"{m.state}: react {clip.length:F2}s fitted to {ReactDuration:F2}s, speed x{cs.state.speed:F2}");
            }
            if (m.state.StartsWith("Dash_") && clip.length > 0.05f)
            {
                cs.state.speed = clip.length / Mathf.Max(dashDuration, 0.05f);
                Debug.Log($"{m.state}: one stride {clip.length:F2}s fitted to the {dashDuration:F2}s dash, speed x{cs.state.speed:F2}");
            }
            // attack states: fit the clip to the AttackData frame count
            var atk = attacks.FirstOrDefault(a => a.name == m.state);
            if (atk != null)
            {
                var so = new SerializedObject(atk);
                int frames = so.FindProperty("startupFrames").intValue + so.FindProperty("activeFrames").intValue + so.FindProperty("recoveryFrames").intValue;
                float wanted = frames / 60f;
                if (wanted > 0.05f && clip.length > 0.05f)
                {
                    cs.state.speed = clip.length / wanted;
                    Debug.Log($"{m.state}: clip {clip.length:F2}s (trimmed={Trims.ContainsKey(m.state)}) fitted to {frames} f ({wanted:F2}s), speed x{cs.state.speed:F2}");
                }
            }
        }
        EditorUtility.SetDirty(ac);
    }

    static AnimationClip LoadClip(string path, string name = null)
    {
        if (!File.Exists(path)) return null;
        var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
        return (name != null ? clips.FirstOrDefault(c => c.name == name) : null) ?? clips.FirstOrDefault();
    }
}
