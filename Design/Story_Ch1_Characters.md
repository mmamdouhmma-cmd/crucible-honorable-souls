# Story Mode — Chapter One characters

Started 2026-10-29. Four story characters for Musashi Chapter One. They are NOT roster fighters
(yet): they live in the story scene `Scenes/Story/Musashi_Chapter01_Arena.unity` and are rigged
on the same 79-bone Mixamo-named skeleton as the roster, so every pack clip and every editor
menu that wires a fighter works on them unchanged.

## The cast

| Working id    | Who                          | Look                                                                                     | Holds                         | Source model (`~/Downloads/Miyamoto chapter one/`)              | Concept art                                  |
|---------------|------------------------------|------------------------------------------------------------------------------------------|-------------------------------|-----------------------------------------------------------------|----------------------------------------------|
| `Ch1_Musashi` | Young Miyamoto (the player)  | WHITE / pale torn kimono, dark hakama, wrist wraps, straw sandals, katana in the sash    | wooden sword (with tsuba)     | `young+samurai+warrior+3d+model.zip`                            | `Battle-Worn Young Ronin in T-Pose.png`      |
| `Ch1_Partner` | His partner, a bit older     | BLACK and BROWN: dark kimono, brown vest, wide hakama, katana in the sash                | wooden sword (with tsuba)     | `samuraipartner+3d+model.zip`                                   | `Battle-Worn Samurai in T-Pose.png`          |
| `Ch1_Sensei`  | The sensei, an old man       | bald, grey goatee, layered torn grey/black robes and vest, prayer beads                  | plain wooden stick / bokken   | `samurai+sensei+warrior+3d+model.zip`                           | `Weathered Ronin in T-Pose.png`              |
| `Ch1_Kojiro`  | The BOSS                     | long silver hair in a high tail, red scarf/cape, dark green haori, mustard kimono, torn crimson hakama | katana with a RED haft | `kojiro warrior 3d model.zip`                                   | `Rugged Silver-Haired Ronin Turnaround.png`  |

Weapons:
- **Wooden sword** (Musashi, Partner) = `wooden+sword+3d+model.zip` — the bokken WITH a round wooden tsuba
  (`Worn Wooden Bokken Sword Render.png`).
- **Wooden stick / bokken** (Sensei) = `wooden+bokken+3d+model.zip` — the plain bokken, no guard, a stepped grip
  (`Weathered Wooden Bokken on White.png`).
- **Red-hafted katana** (Kojiro) = the roster Miyamoto's katana with the grip recoloured red (the user's call,
  2026-10-29). DONE: `ArtSource/Blender/recolour_grip.py` finds the grip on the mesh (the far side of the
  widest slice, which is the tsuba), rasterizes those faces into UV space and re-hues the wrap red, keeping
  the brass collar, end cap and ornaments by colour. Miyamoto's mask does not flag them as metal. Run with
  `--lift 0.5 --sat 0.82`. Output in `ArtSource/Characters/HD/Ch1_Kojiro/weapon/` (basecolor + copied normal
  and mask); on Kojiro's rig the weapon is now `Ch1_Kojiro_Katana_R` with material `Ch1_Kojiro_Katana`.

Materials: the **same metallic texture treatment as the roster** (the user, 2026-10-29). These are Tripo HD
exports and carry real `metallic` / `roughness` / `normal` maps, so each body's mask is PACKED from them the
way Bjorn's download was (`RGB = metallic`, `A = 1 - roughness`) and `BuildMaterial` picks up
`<name>_mask.png` + `<name>_normal.png` from the `.fbm`. The weapons get the same.

## Rigging route

`ArtSource/Blender/hd_rig.py` (the 2026-10-27 HD route), template `Salaadin_Body.fbx`:
fit joints to the mesh -> bone-heat bind + repair -> pose onto the template's rest -> grip hands -> checks
(zero-weight verts, 6-ray containment, renders). Output goes to `ArtSource/Characters/HD/<id>/`.

Things to watch, from the roster's history (CLAUDE.md):
- All four wear **wide hakama** — cloth below the hips. Judge the legs by containment and the hip/knee/ankle
  fractions of height, never the off-limb centroid metric (cloth drags it).
- The **sheathed katana is fused into the mesh** at the left hip on Musashi / Partner / Sensei. It will be
  weighted to the pelvis/thigh by heat; check it does not follow a thigh.
- Kojiro's **long hair tail and cape** hang behind — the fit reads the spine off the torso's front for exactly
  this reason. Check his head and neck bones.
- After import: print the avatar's slot -> bone table, fit `StandingHeight` / `GroundOffset` / the capsule,
  and measure sole contact on flat ground.

## Sizes (proposal — the user to confirm)

Young Musashi smallest, Partner a little taller, Sensei slightly stooped, Kojiro the tallest.

## Moves and animations

**Decided 2026-10-29 (the user):**
- **Musashi and the Partner are COPYCATS of the roster Miyamoto**: the same animations - his controller
  (`P2_Miyamoto.controller`), his clips, his move set (normals, specials, combo strings, contacts). Both hold the
  wooden sword two-handed exactly like his katana, so they go in `TwoHanded` with a `TwoHandGrip`.
- **The Sensei and Kojiro get their OWN animations**, different from Miyamoto's.
- **Order: Musashi + Partner first, then the Sensei. Kojiro's animation is POSTPONED** for a while.

Per character, fill in:

| Character | Idle / walk / run | Light | Medium | Heavy | Specials | Story-only (cutscene) clips |
|-----------|-------------------|-------|--------|-------|----------|-----------------------------|
| Musashi   | = Miyamoto        | = Miyamoto | = Miyamoto | = Miyamoto | = Miyamoto | later |
| Partner   | = Miyamoto        | = Miyamoto | = Miyamoto | = Miyamoto | = Miyamoto | later |
| Sensei    | own: borrowed strikes (the user's picks, 2026-10-29) - see "Unity phase, Sensei" | | | | | |
| Kojiro    | own - POSTPONED   |       |        |       |          |                             |

## Status

| Character | Extracted | Rigged | Weapon mounted | In Unity | Notes |
|-----------|-----------|--------|----------------|----------|-------|
| Musashi   | yes       | yes (2026-10-29) | wooden sword (`Ch1_Musashi_Bokken`, swap_blade.py) | yes - `Scenes/Story/Ch1_Test_Musashi.unity` (play-verified 2026-10-29) | clean first pass: 0 zero-weight verts, raise + squat render fine |
| Partner   | yes       | yes (2026-10-29) | wooden sword (`Ch1_Partner_Bokken`, swap_blade.py) | yes - `Scenes/Story/Ch1_Test_Partner.unity` (play-verified 2026-10-29) | clean first pass |
| Sensei    | yes       | yes (2026-10-29, re-fit `--arm-axis section --arm-mirror 1`) | plain bokken (`Ch1_Sensei_Bokken`, swap_blade.py + strip_guard.py, 2026-10-29) | yes - `Scenes/Story/Ch1_Test_Sensei.unity` (play-verified 2026-10-29) | arms symmetric, 0 bones outside; scabbard on Hips |
| Kojiro    | yes       | yes (2026-10-29) | red-grip katana (seated by the grip pass) | no | needed `--arm-top 1` + `fix_cape_weights.py` (below) |

Models are extracted to `ArtSource/Characters/Tripo/Story_Ch1/<id>/` (gitignored); rigged output is
`ArtSource/Characters/HD/<id>/<id>_HD_rigged.{fbx,blend}` with check renders in `renders/<id>/`.
Musashi's and the Partner's katana were swapped for the wooden sword by `ArtSource/Blender/swap_blade.py`
(pre-swap blends kept as `<id>_HD_rigged_katana.blend`). The Sensei still holds the placeholder katana until
his bokken goes on.

Command (run from the repo root with ABSOLUTE paths - a relative OUT_DIR sends the renders to `C:\ArtSource\...`):

```
blender -b --python ArtSource/Blender/hd_rig.py -- <id> <tripo.fbx> <repo>/Assets/_project/Characters/salaadin/Model/Salaadin_Body.fbx \
    <repo>/Assets/_project/Characters/Miyamoto/Model/Miyamoto_Body.fbx <repo>/ArtSource/Characters/HD/<id> [--arm-top 1]
```

### Kojiro's two fixes

- **`--arm-top 1`**: the arm axis is a median over two bands along the arm, and Kojiro's kimono sleeves hang
  from the arm to the waist in exactly those bands, so the median landed in the cloth. The shoulders ended up
  in the drape and the rest re-pose bent the arms 50-70 deg. With the flag, each band keeps only its top
  0.06 H (the cloth drapes OVER the arm), and the arms fit inside the sleeves (re-pose 30 deg, 0 verts on
  finger bones).
- **`ArtSource/Blender/fix_cape_weights.py`**: bone heat bound the red cape's side panels to the arms, so
  raising them pulled the cape out into flat fins. Arm-bound verts more than 0.10 H below AND 0.05 H behind
  their bone are cape, not sleeve, and go to the spine (398 verts). The pre-fix blend is kept as
  `Ch1_Kojiro_HD_rigged_precape.blend`.

## Rig audit (2026-10-29)

Tool: `ArtSource/Blender/audit_rig.py` (read-only; `blender -b <rig>.blend --python audit_rig.py -- <id> <Salaadin_Body.fbx> <out_dir>`).
Results per character in `ArtSource/Characters/HD/<id>/audit/` (`audit.json`, `poses/`, `sheet_front/back.png`).
Baselines audited the same way: Miyamoto, Genghis, Bjorn, Richard (`HD/<Name>/audit/`).

**Healthy on all four:** 79/79 bones, hierarchy identical to the template, no negative scale, body bound
cleanly; 0 unweighted verts, max 4 influences, all normalised; leg/spine/head joint heights equal to the
play-verified roster; knee 163.5 / elbow 164.9 breaks bending the right way. Hand-bone roll ~176 deg vs the
template is BY DESIGN (palm-frame roll, `hd_rig.py:597`; Genghis ships with the same). Finger bones carry no
weight by design (grip mode: the hands are sculpted fists), so finger-curl tests move 0 verts on every rig
including the roster. Weapons: parented to RightHand, fist wraps 315-360 deg round the grip.

**FIXED (2026-10-29, after the pause):**
1. **Sheathed katana skinned to the LEFT THIGH** (Musashi 527 far verts, Partner 725, Sensei ~1400): a 35 deg
   step tipped the hilt up toward the chest (5-13% H of scabbard travel; 12-15% H at a high knee).
   `ArtSource/Blender/fix_scabbard_weights.py` finds the tube by shape (the scabbard is fused into the body
   island - one island per mesh - so it cannot be picked by connectivity): line-fit the thigh-bound verts
   >0.15 H from the thigh, refit on the inliers (drops the Sensei's robe flaps), re-bind every vert within
   0.018 H of that axis over its length wholly to Hips, smoothstep blend out to 3x the radius. Scabbard
   travel now 0.00. The rigid tube's seam with the hakama stretches ~100-400 edges at 35 deg, all BURIED in
   the obi (photographed); widening the band does not remove them - they are the fused intersection.
   Backups: `<id>/_pre_scabbard_20261029/`. The audit now lists those verts as "far from Hips" - by design.
2. **Sensei's arm height gap was the RIGHT arm, not the left.** On the true arm cross-sections (x 0.28-0.40 H)
   both arms of the sculpt match to 0.05% H; the inner band median on his RIGHT caught the robe ridge over the
   shoulder (+1.2% H), and extrapolating the line from that band to the shoulder tripled it to 3.3% H - which
   is also why `RightShoulder`'s tail was OUTSIDE the mesh. `hd_rig.py` gained `--arm-axis section` (the arm
   line through slices that are ARM ONLY, vertical extent < 0.10 H; height from the slice box, depth from its
   median - the box depth took in the sleeve and put the shoulder 2% H out the back) and `--arm-mirror 1`
   (average each side with the other's mirror: the sleeves still pulled one fit 2% H back). Re-fit with
   `--arm-axis section --arm-mirror 1`: arm heads 0.8136 / 0.8136 H, **0 bone points outside the mesh**
   (was 2), arm chains no longer in the mirror-error list. Previous rig in `Ch1_Sensei/_pre_armfix_20261029/`.
   The re-fit replaces the earlier `--arm-top 1` one; the scabbard fix was re-applied after it.

**Still open:**
3. **Kojiro's haori sleeves / cape side panels fin out** when the arms rise (overhead pose: max edge stretch
   47x, 4-7x the extreme-edge count of the others; ~3300 verts bound to arm/thigh bones far from them).
   His animation is postponed, so deferred until his moves are known.
4. Kojiro's arms measure ~14% shorter than the roster's (upper arm 14.7% H vs 17.2%) - check against his
   sculpt before trusting it.

5. Musashi's Hips/Spine and both characters' shoulder heads read 0.2-1.4% H outside the mesh (slim waist) -
   unchanged by the fixes, pre-existing, minor. Leg poses stretch more than the roster's (deep knee bend
   1235-1747 extreme edges vs Miyamoto 393) - photographed: it is the wide hakama spreading between the legs.

**Unity phase, Musashi + Partner (2026-10-29, DONE):**
- Each lives in its own test scene, a copy of testarena with Player2 rebuilt as the story character by
  `Crucible > Story > Chapter One > Rebuild Player2 as ...` (the menu refuses any scene outside `Scenes/Story/`).
  testarena and the combat code are untouched; the per-character numbers are table entries in
  `CrucibleCharacterSetup.cs` (StandingHeight, Stature, GroundOffset, TwoHanded, WeaponSpec, OffHandGripOverride,
  ShieldWeaponMount).
- **Avatar:** 52 slots mapped, 83 skeleton entries, and the slot -> bone table is IDENTICAL to Miyamoto's (Head on
  Head, toes mapped). 30/30 fingers via `FillMissingHumanBones`.
- **Blade box:** WeaponSpec 0.24 checked against the mesh - the bokken's tsuba (its widest slice about its own
  centreline) sits at 0.225-0.250 of the length, so the box starts at the tsuba. (The katana's is at 0.30.)
- **The real fault was the weapon MOUNT.** The bokken came off hd_rig.py in the Tripo sculpt's own hold, which
  under Miyamoto's clips hung it straight DOWN beside the leg in every state (tip-from-hand y -0.99 at idle, against
  the katana's +0.04; screenshot showed empty fists at the chest). Fixed with a `ShieldWeaponMount` entry per
  character, solved in play: Miyamoto_Idle frame 0, the bokken given the katana's exact world pose relative to the
  root (both blades run along local +z and lean the same way off it - checked on the meshes), the hand offset scaled
  by the stature ratio. Miyamoto's LeftGrip socket (`OffHandGripOverride`) carried over unchanged.
- **Verified in play over all 24 layer-0 states** (harness: per-frame baked mesh, sole/crown/hips, TwoHandGrip miss,
  blade direction): blade direction matches Miyamoto's to within 0.05 in every state, both hands on the hilt
  (photographed idle / heavy / block). Statures at idle **Musashi 2.820, Partner 2.901** (targets 2.82 / 2.90);
  soles at idle **+0.003 / +0.008** (Miyamoto +0.003). Off-hand miss runs 10-50% above Miyamoto's in the big swings
  (Heavy 0.36/0.44 vs 0.30, Kuruma 0.53/0.70 vs 0.34) - the same clips on slightly different arm proportions; the
  hands still read on the hilt. Watch it in play; a per-character LeftGrip tweak is the lever.
- Not done (out of scope of the rig audit): Musashi/Partner are not in testarena's roster or the story scene's
  ArenaDirectory game - the test scenes are the integration.

**Sensei's bokken (2026-10-29, DONE):** `swap_blade.py ... Miyamoto_Katana_R Ch1_Sensei_Bokken <Bokken_Stick fbx>`,
then `strip_guard.py -- Ch1_Sensei_Bokken_R`: the Tripo "plain" bokken had a metal tsuba fused in at 18-22% of its
length; the guard verts are collapsed onto a stepped shaft (grip r 0.017 -> blade r 0.024 in the fist frame), wood UVs.
Backup `Ch1_Sensei_HD_rigged_katana.{blend,fbx}`.

**Unity phase, Sensei (2026-10-29, DONE):** his own fighter, built by `Editor/CrucibleSensei.cs`
(`Crucible > Characters > Sensei > 1/2/3`, order in the file header), controller `P7_Sensei` (copied from Miyamoto's),
data `sensei_data`, attacks in `Characters/Ch1_Sensei/Attacks/`. He lives only in `Ch1_Test_Sensei`, on Player2.
- **Move set, the user's picks** - every strike borrowed, each with the source move's per-STATE contact and hit geometry:

  | Slot | Name | Animation from |
  |---|---|---|
  | Light | Falling Branch | Richard's Heavy (`sword and shield slash`) |
  | Medium | Temple Bell | Salaadin's Heavy (the SAME file - the user asked for both by name; contacts 17.7 @x1.4 vs 31 @x1.0) |
  | Heavy | Mountain Splitter | Bjorn's Heavy (`great sword slash`, two-handed) |
  | SP+Light | Rising Heaven | Genghis's SP+Heavy (`great sword casting`) |
  | SP+Medium | Circle of Willow | Salaadin's SP+Medium (`sword and shield attack (3)`) |
  | SP+Heavy | Final Lesson | Bjorn's Heavy again (hyper armour, like every SP+Heavy) |

  Block = Bjorn's guard (AxePack block idle / react). Idle, walks, dashes, run, reacts, death = Miyamoto's.
  Strings are a placeholder in Genghis's shape: L -> M -> H, H -> SP+H.
- **One-handed, except the overhead.** `TwoHandGrip.onlyInStates` (new; empty = always, so the roster is unchanged)
  holds the off hand on the hilt only in his idle, walks, Heavy and SP+Heavy, blending 0.12 s in and out. Measured:
  grip weight 0 through Light / Medium / SP+Light / SP+Medium and the block, 1 through Heavy and SP+Heavy.
- **Numbers between Miyamoto and Salaadin, health under both**: 75 hp, 108 stamina, 15 armour, x0.88 damage,
  walk 4.4, block 68, dash 2.45 / i-frames 0.17, counter window 0.9 at x1.42; each move's frames, damage and stamina
  between the two fighters' same slot. His `CrucibleBalance` row carries `onlyScene = "Ch1_Test_Sensei"`: his
  vitals are written to Player2 in that scene only, and Miyamoto's are skipped there.
- **Mount**: the Partner's numbers hung his bokken straight down - his hand bone rolls differently. Solved in play
  by giving the bokken the Partner's exact pose relative to the root at idle frame 0 (all three bokkens share the
  katana's mesh frame): `(0.000699, 0.000420, 0.000135)` / `(339.92, 265.18, 17.10)`.
- **Verified in play vs Bjorn from 2.4**: all six moves fire their own state and land (5 / 6 / 8 / 10 / 11 / 12
  damage); stature 2.88 at idle (target 2.86), soles -0.010; photographed idle (two-handed guard), Heavy (both
  hands on the hilt), block (Bjorn's diagonal guard).
- Open: walk stride is Miyamoto's (`WalkStride["Sensei"]`), not re-measured on his body; his strings await the user.

**Next:** Kojiro (deferred, items 3-4), or the Sensei's strings when the user names them.
