# CRUCIBLE: Honorable Souls — session log

Running log of work sessions, oldest first. This used to live at the bottom of `CLAUDE.md`;
it was split out on 2026-10-20 because that file had grown past its character limit.

**Write new entries at the BOTTOM of this file**, as `### YYYY-MM-DD — <short title>`.
Keep `CLAUDE.md` for the durable facts (layout, how combat works, conventions, gotchas that
still apply) and a short "where things stand" summary; everything narrative goes here.

### 2026-10-07 — hitbox scale fix, hit sides, hitstun

- Found why attacks landed from far away: the fighters' scene scale (4x/3x) was inflating the Hitbox child and the body capsules, so a 1.5-unit attack reached 6 units. Hitboxes now ignore model scale and are in world units; gizmos match.
- Added `HitSide` enum + `hitSide` field on `AttackData`, authored per attack. Defaults set on the six existing attacks to mirror `comesFrom`. `Fighter.NotifyHit` sets `HitSide` int and `GetHit` trigger on the animator.
- Added `hitstunFrames` on `AttackData` (default 12) and a hitstun timer in `CombatSystem`. Hitstun blocks attacking, dashing and walking; a hit cancels an attack in progress unless `hitInterruptsAttack` is off.
- Cached the `Animator` on `Fighter` instead of looking it up every frame.
- Still to do in the editor: add `HitSide` int and `GetHit` trigger to both animator controllers with three get-hit states; tune `hitboxRange` per attack (likely 3–4 units for these models) and `hitstunFrames` to the flinch clip lengths; consider raising `startupFrames` so hits land when the swing visually connects.

### 2026-10-10 — blocking stamina cost

- Holding block now drains stamina (`blockStaminaDrainPerSecond` on `Fighter`, default `BalanceConfig.BLOCK_STAMINA_DRAIN_PER_SECOND` = 4/s) via `FighterStats.DrainStamina`, on top of the existing per-blocked-hit cost in `CombatSystem.OnHit` (5/10/18/25 by attack type).
- Stamina no longer regenerates while block is held unless `staminaRecoversWhileBlocking` is ticked on the Fighter.
- When the bar hits 0 the guard drops (`IsBlocking` false, HUD shows OUT OF STAMINA) until regen brings it above 0; logged as `P#: Block BROKEN - out of stamina`.
- Dash dodges are now judged when the attacker's hitbox **arms** (`CombatSystem.ResolveDodgeAtArm`), not only on trigger contact: if the opponent is mid-dash and their dash *start* position was inside the attack's reach (hitbox grown by their body radius, world units), `ResolveDashAgainst` runs right then. An evade marks the swing `swingEvaded` so a later trigger brush does no damage. Hits still require real contact. A play-mode gizmo draws the reach box (green = dodged, red = not, grey = opponent not dashing) and the dash-start sphere.
- Clash rule. Hits are no longer applied inside the trigger callback: `CombatSystem.OnHit` queues them and `ResolvePendingHits` (static, drained at the top of the first `CombatSystem.Update` each frame) settles them together. Two queued hits pointing at each other are a clash: equal `AttackType` weight (Light < Medium < Heavy < Special) → both attacks end, no damage, both fighters get `Fighter.NotifyClash` (straight flinch, `CLASH_STAGGER_FRAMES` hitstun, slide `CLASH_PUSHBACK_DISTANCE` apart) and the HUD shows CLASH; unequal weight → the heavier swing resolves as a normal hit and the lighter one is force-ended ("LOST THE CLASH"). Staggered timing is unchanged: the faster startup lands first and its hitstun cancels the slower swing. Constants in `BalanceConfig` under CLASH.
- Reach authored per weapon: Miyamoto (katana) 2.5 on Slash/OverHead/HeavySwing; Bjorn (axe) light 3.0, medium 3.5, heavy 4.0. `fighterRadius` raised 0.8 → 1.2 in the scene so fighters stand off at 2.4 units, where the axe reaches and the katana does not. Note the body capsules are 1.2 (P2) / 1.6 (P1) world units wide, so centre-to-centre reach = offset.z + hitboxRange + opponent radius.
- Body capsules shrunk to ~0.6 world units (P1 capsule radius 0.15 on 4x, P2 0.2 on 3x; were 1.6 / 1.2). `Fighter.BodyRadius` reads the capsule radius × root scale and is what the dodge reach check, whiff log and reach gizmo use; `Fighter.Radius` (`fighterRadius`) is only the walk/dash standoff.
- Whiff log: when an attack's active window closes without touching anyone (and it was not dodged), `CombatSystem.LogWhiff` prints `P#: <attack> WHIFFED - opponent X units away, reach Y (...) - out of reach by Z` or why else it missed.
- Both animator controllers (`Assets/_project/Animation/Controllers/*.controller`) now define `HitSide` (int) and `GetHit` (trigger), so `Fighter.PlayHitAnimation` sets them instead of logging. No get-hit states exist yet because the project has no hit-reaction clips; until three states (Straight/Left/Right, Any State → state on `GetHit` + `HitSide == n`, exit back to idle) are wired, the trigger is set but nothing plays.
- Reorganised `Assets/`: everything lives under `_project/` (see Layout). Files were moved with their `.meta` so GUIDs and scene references are unchanged. Controllers renamed `P1_Bjorn` / `P2_Miyamoto`; attack assets renamed `<Name>_<Weight>_<Move>`; URP template readme, old test models and `Player2Controller.cs` parked in `_project/_Legacy/`.

### 2026-10-10 — first custom animation clips (Blender → Unity)

- `ArtSource/Blender/` (outside Assets so Unity never imports the .blend): `Tripo_AnimBase.blend` (Bjorn Tripo rig + 29k-vert decimated proxy mesh, check camera), `Tripo_AnimMaster.blend` (base + every authored action), `build_clips.py` (authors the clips procedurally from pose keys, renders check frames, exports armature-only FBX), `wire_controllers.py` (adds the states/transitions to a controller YAML), `inspect_pose.py` (prints a rig's bone axes).
- Rebuild clips headless: `"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe" -b --python ArtSource/Blender/build_clips.py -- <Bjorn rig fbx> ArtSource/Blender/Tripo_AnimBase.blend ArtSource/Blender/Tripo_AnimMaster.blend Assets/_project/Animation/Clips/Custom <render dir>`. Delete the base .blend to re-decimate from the FBX.
- Both Tripo rigs (Bjorn, Miyamoto) share the identical 41-bone skeleton, so one clip set plays on every Tripo character. Everything imports as **Humanoid**; clip FBX take name is `Scene`, each meta exposes one clip (internalID -203655887218126122) so controllers reference `{fileID: -203655887218126122, guid: <fbx guid>}`.
- Bone-space conventions on the Tripo rig (faces +X, left = +Y): spine/neck/head +X leans back, +Z leans left, Y twists; upper arms +X swing forward, L +Z / R −Z raise outward; forearms +X bend forward. All clip keys are offsets from `STANCE` (arms down, soft knees), never from the T-pose rest.
- Clips in `Animation/Clips/Custom`: React_Front / React_Left / React_Right (20 f), Block_Hold (30 f, loops), Block_Impact (14 f), Stance_Idle (reference). Controllers: new `Blocking` bool (set by `Fighter.HandleBlock`); Any State → React_* on `GetHit` + `HitSide == n` + `Blocking` false; Any State → Block_Hold on `Blocking`; Block_Hold → Block_Impact on `GetHit`; back to idle on exit time / `Blocking` false.
- Known gaps: walking while blocking slides (Block_Hold has no walk blend); the attack/idle clips are still Mixamo/Maria placeholders; hit reactions are upper-body only.
- Full first-pass clip set now custom (Mixamo/Maria clips unused but still in `Animation/Clips`): `Idle` (90 f loop), `Walk_Forward` / `Walk_Back` (48 f loops, in place), and the six strikes authored to their AttackData frames so the blade is in front exactly over the active window: `Bjorn_Light_AxeSlash` 17 f, `Bjorn_Medium_Backhand` 29 f, `Bjorn_Heavy_SpinningBackSlash` 35 f (full Root spin, baked orientation), `Miyamoto_Light_Slash` / `Miyamoto_Medium_Overhead` / `Miyamoto_Heavy_Swing` 23 f. `ArtSource/Blender/retarget_controllers.py` repoints existing states at clips and sets attack exits to exit time 0.9 / 0.1 s and Any State → attack blends to 0.03 s. `build_clips.py` accepts clip names after the paths to rebuild a subset.
- If an attack's startup/active/recovery frames change in AttackData, re-author that clip's key frames to match and rebuild it.
- **Clip import orientation rule:** every custom clip meta must use Root Transform Rotation *Based Upon: Body Orientation* (`keepOriginalOrientation: 0`). The Tripo rig faces +X in Blender; "Original" turns the whole body 90° in Unity (fighters face sideways and walks slide). Hip translation (bob/sway) is baked into the pose: `loopBlendPositionY: 1`, `loopBlendPositionXZ: 1`, `keepOriginalPositionXZ: 1`. Only the spin clip bakes rotation (`loopBlendOrientation: 1`), which for a non-loop clip is "Based Upon (at Start)" so the stance frame sets forward.
- Second pass on locomotion/strikes: `build_clips.py` now supports a `HIP` world-space offset (converted into the Hip bone's local space), `pelvis()` yaw/roll/tilt and foot roll. Walk is 44 f with keys every 2 f: thigh ±30°, knee to 50°, foot roll, pelvis yaw ±8°, shoulders counter-rotate, head counter-twists, hip bob/sway ~7 mm. Idle 120 f with breathing + slow weight shift. Strikes wind the pelvis against the swing, snap it through on the active frames and shift the hips forward.

### 2026-10-11 — moveset reset, dashes, hit feel

- **Moveset** (assets renamed, GUIDs kept): Bjorn `Bjorn_Light_Slash` (6/3/8, AttackerRight, 3.0), `Bjorn_Medium_SpinningSlash` (10/5/14, AttackerRight, 3.5, wide box, full Root spin), `Bjorn_Heavy_OverheadDoubleAxe` (15/5/15, Overhead, 3.2, tall narrow box, 18 dmg). Miyamoto `Miyamoto_Light_Thrust` (7/3/10, Thrust, 3.1, narrow box), `Miyamoto_Medium_SideSlash` (10/4/12, AttackerRight, 2.6, wide box), `Miyamoto_Heavy_Overhead` (14/4/14, Overhead, 2.8, 18 dmg). Rule for `hitSide`: the side of the **defender** that is struck — AttackerRight → Left (1), AttackerLeft → Right (2), Overhead/Thrust → Straight (0). The unused `animationClip` field on AttackData still points at old placeholder clips; the controllers drive animation, not that field.
- **Animation easing** in `build_clips.py`: each key may carry `(interpolation, easing)` for the segment it starts: `SETTLE` (sine out) into a windup, `WIND` (expo in) windup→first active frame, `THRU` (linear) across the active frames, `OVER` (back out = overshoot) into follow-through, `EASE` (sine in/out) recovery. Also `hands()` wrist flex, `head()` lead, `mirror()` / `mirror_keys()` for L/R pairs. Strikes hold a beat at the top of the windup.
- **Dashes**: `Dash_Forward/Back/Left/Right` (20 f, in place, athletic hop: dip → drive → airborne tuck with lean → absorb → gather). Controllers: `Dash` trigger + `DashDir` int (0 fwd, 1 back, 2 left, 3 right), Any State → Dash_* at 0.02 s, exit at 0.85. `Fighter` sets them at dash start. `dashDistance` 2.5 → 2.0 in the scene (was carrying past the counter range); facing now keeps updating during a dash and turns at `dodgeTurnSpeed` (2000°/s) while dashing or inside the counter window.
- **Hit feel**: `HitStop` (static) freezes both fighters' animators, attack timers and movement for `HIT_STOP_FRAMES` (4) on a landed strike, `HIT_STOP_BLOCK_FRAMES` (2) on a block; toggles on `CombatSystem.hitStop`. `HitEffects.SpawnBlood` builds a one-shot ParticleSystem in code (cone burst from the chest toward the attacker, sized by `BodyRadius`, URP Particles/Unlit material); toggle `bloodOnHit`, colour `bloodColor`. In a build the particle shader must be in Always Included Shaders or referenced by a material in the project.

### 2026-10-11 — look pass: HDRP, camera feel, VFX, slim characters

- **HDRP** added to `Packages/manifest.json` (17.4.0, URP left installed for now). `Assets/_project/Editor/CrucibleLookSetup.cs` (asmdef `Crucible.Editor`, compiles only with `HDRP_PRESENT`) adds menu **Crucible > Look**: 1 creates `Settings/HDRP_PC.asset` (SSR, SSAO, volumetrics, motion vectors, SSS, decals on) and assigns it to Graphics + every quality level; 2 converts the character materials from URP/Lit to HDRP/Lit (base, normal, packed `_mask.png` → Mask Map); 3 sets up the open scene: TAA camera, warm low **Sun** (9000 lux, 16° elevation, behind the fighters relative to the camera, 4096 shadows, contact shadows), cool shadowless **Fill Light** (900 lux), and a global Volume `Settings/CrucibleLook.asset` (physically based sky, fixed exposure 12.3, warm volumetric dust fog, ACES, contrast/warm grade, lift-gamma-gain, bloom, vignette, grain, chromatic aberration, manual DoF with the far wall soft, motion blur, AO, SSR, micro shadows). Distances in the profile are ×4 because scene units are ~4 m. After running: Window > Rendering > HDRP Wizard > Fix All, then save the scene. Old URP volumes get disabled, not deleted.
- **Camera feel** (`FightCamera`, all cosmetic): `OnImpact(damage, type, blocked)` shakes by damage (0.12 units per 10 dmg), heavies/specials unblocked also punch the FOV in 4° and run a 0.45× slow-motion beat for 0.14 real seconds; `OnClash()` shakes harder + slow beat. Smoothing uses unscaled time so slow-mo never lags the camera. Slow-mo scales `Time.timeScale`, so frame data in game time is unchanged.
- **VFX from code** (`HitEffects`, `VfxMaterials`, `WeaponTrail`): blood (unblocked hit), sparks (block, clash; stretched additive billboards), dust (dash push-off, dash landing, heavy strike arm), blade trail while the hitbox is live (emitter past the right hand along the forearm, `TrailRenderer`, added to each fighter automatically by `CombatSystem` when `bladeTrail` is on). `VfxMaterials.Unlit` picks HDRP/Unlit (transparent set by property) → URP → built-in by name, so runtime code has no pipeline dependency. HDRP/Unlit ignores vertex colour, so particles rely on size-over-lifetime to vanish; a VFX Graph pass should replace these later.
- **Characters slimmed**: `ArtSource/Blender/prep_character.py` backed up the Tripo originals to `ArtSource/Characters/*_tripo_original.fbx` (git-ignored) and re-exported both Model FBX at ~120k verts (from ~980k, 350 MB → 10 MB each) with skin weights, and packed `<name>_mask.png` (R metallic, G AO=1, A smoothness) into each `.fbm` folder.
- **Exposure is Automatic** (centre-weighted, limits EV 10–14.5, −0.4 compensation) after a fixed value went black; sun direction maths fixed (tilt about `Cross(up, dir)` tilts DOWN; the old code pointed the sun below the horizon → night). `Crucible > Look > Diagnose` prints pipeline, camera, lights and every volume component to the console. Volume components must be added to the profile asset with `AssetDatabase.AddObjectToAsset` or they vanish on the next domain reload (that was the first black screen).
- **Al-Quds arena**: `ArtSource/Blender/build_arena_alquds.py` builds the courtyard procedurally (floor, arcade of pointed arches on the far wall −Z, crenellated walls, octagonal corner towers, steps, gilded dome on a drum, minaret, randomised low backdrop buildings), bakes four stone materials with Cycles to 2K BaseColor/Normal/Mask PNGs, renders two check views, and exports `Assets/_project/Environment/AlQuds/AlQuds_Arena.fbx` (one object per material, box-projected UVs in world units / tile). `Crucible > Arena > Place Al-Quds arena` (`Editor/CrucibleArenaSetup.cs`) imports the FBX with no materials, builds HDRP/Lit materials in `Environment/AlQuds/Materials` from the baked maps, instantiates it at the origin (static, floor mesh collider), hides the test `Ground`, and aims the Sun from behind the far wall (dir (0.42,−0.34,1)). Fighters are ~2.9 units tall (0.725 m Tripo file × 4 / 0.98 × 3), so 1 unit ≈ 0.6 m; the courtyard is 64×44 units.
- **Pipeline decision (2026-10-11):** the dev laptop has an AMD integrated GPU (Radeon 0x164c) and HDRP ran at 8–10 fps even with every feature on its low tier; URP with the slimmed characters was smooth. Day-to-day development is on **URP**. `Crucible > Look > Switch to URP (laptop-friendly look)` (`Editor/CrucibleUrpLook.cs`, `URP_PRESENT`) assigns `Settings/PC_RPAsset` everywhere, converts HDRP/Lit materials to URP/Lit (HDRP mask R/A == URP metallic-gloss map, so the baked textures carry over), resets light intensities to unitless (sun 2.1 noon from the back-left, fill 0.45), sets a procedural skybox (`Settings/CrucibleSky.mat`), linear dust fog 70–320 units, and a URP post volume `Settings/CrucibleLookURP.asset` (ACES, grade, bloom, vignette, grain, CA; no DoF/motion blur) with SMAA on the camera. HDRP stays installed; `Run all three steps` switches back for a desktop GPU. `Place Al-Quds arena` converts its materials to URP automatically when URP is active. Frame rate on the laptop must be read from the Game view Stats overlay at 1080p with the Scene tab hidden.
- Attack asset `.asset` files: never edit `attackName` with a single-line sed — the original Tripo-era files had a quoted multi-line name and the leftover `'` line truncated the YAML (Bjorn heavy read as Light until fixed).
- **Effects pared back (2026-10-11):** blood is the only hit effect on by default. `VfxMaterials` now picks the shader by the ACTIVE pipeline asset (an installed-but-inactive HDRP was being found first and rendered pink under URP) and generates a soft-dot sprite and an irregular splat texture in code. `HitEffects.SpawnBlood` = fine spray + heavy drops (velocity-stretched soft dots, colour-over-lifetime to a dark red) + 1–2 `SpawnSplat` ground quads that hold 6 s and fade 4 s (`FadeAndDestroy`). Sparks, dust and the blade trail still exist but are off: `sparksOnBlock` / `bladeTrail` false on both CombatSystems in the scene, `dustEffects` false on Fighter. Camera: shake only on an unblocked Heavy/Special; punch-in, slow-mo and clash shake are toggles, off. Hit-stop (4 / 2 frames) is still on.
- **Per-weapon stance sets (2026-10-11):** `GUARD_AXE` (Bjorn: axes crossed at chest, low and wide) and `GUARD_KATANA` (Miyamoto: two-handed chudan, hands at the waist, right foot leading) in `build_clips.py`, with `guard_idle_keys()` and `guard_walk_keys()` (short steps, knees stay bent, guard up; `slide=True` = suri-ashi with flat feet and no bob). Clips: `<Name>_Idle` (120 f), `<Name>_Walk_Forward/Back` (40 f axe, 36 f katana), `<Name>_Block_Hold` (30 f), `<Name>_Block_Impact` (14 f). Controllers' idle/walk/block states now point at these per character (states renamed `Bjorn_Idle` etc.). The generic `Idle`, `Walk_*`, `Block_*` clips remain for a future weaponless character.
- **Blender MCP** is set up (see the user-level memory note): add-on in Blender 5.1, server `blender` registered in Claude Code via `uvx blender-mcp`. Launch Blender with the socket up: `blender.exe ArtSource/Blender/Tripo_AnimMaster.blend --python-expr "import bpy; bpy.app.timers.register(lambda: (bpy.ops.blendermcp.start_server(), None)[1], first_interval=2.0)"`. The tools appear only in a Claude Code session started afterwards.

### 2026-10-11 — stance guards fixed live over Blender MCP

- First live session with the Blender bridge (`mcp__blender__*`, see the memory note). Reviewing the per-weapon walks in the viewport showed both guards were "zombie arms": upper arms raised to shoulder height, elbows locked, weapons pointing nowhere. Cause: `GUARD_AXE` / `GUARD_KATANA` were authored blind from euler offsets whose stacked X/Z rotations did not do what the comments claimed.
- `ArtSource/Blender/livepose.py` is the live tool for this: `exec(open(...).read())` inside the open master, then `apply(pose)` previews STANCE + offsets statically, `reach(rig, side, target, base)` grid-searches upper arm + forearm so the hand lands on a world point, `aim(rig, hand, BLADE_REST[...], yaw, pitch, forearm_bend)` grid-searches forearm twist + wrist so the weapon points where asked. Weapon rest directions measured from the meshes: Bjorn's axe handles continue the arm line; Miyamoto's katana points straight **up** from the right hand (along R_Hand local X), so the blade is aimed with forearm pronation + wrist, not by pointing the forearm.
- **The two Tripo FBX skeletons share bone names and a T-pose but not bone rolls** (R_Hand differs by 52°, R_Forearm/R_Upperarm ~15°). A pose solved on Miyamoto's armature means something else on `TripoRig` (Bjorn's, the export rig). All solving happens on `TripoRig`; Unity's Humanoid retarget absorbs the roll difference. To preview on Miyamoto's mesh in Blender, import `miyamoto rigged.fbx`, rename the armature `MiyaRig`, and `retarget_action("Miyamoto_Walk_Forward")` bakes a `_miya` copy through the rest frames (preview only, never saved into the master — revert the file afterwards).
- New guards (offsets from STANCE, via the new `limb(side, upper, fore, hand)` helper in `build_clips.py`): axes low in front, elbows ~90°, handles forward, heads up 20° and splayed 15°; katana chudan, both hands on the hilt at the navel on the centreline, blade forward and up 25°. Blocks are now absolute poses: axes crossed in front of the face with heads up and out; katana near vertical in front of the chest. The 10 `Bjorn_*`/`Miyamoto_*` idle/walk/block clips were rebuilt and re-exported; controllers unchanged.
- **T-pose bug and the export fix (2026-10-11):** both fighters stood in a T-pose for idle/walk after the stance rebuild. Cause, confirmed by re-importing the exported FBX files: Blender's FBX exporter writes each bone's node defaults from the **current pose at export time**, and Unity builds a Humanoid avatar for an armature-only clip file from those defaults. The old clips exported in the neutral stance, which Unity's T-pose enforcement could straighten (with some twist error on every retarget); the guard poses (folded, twisted forearms, hands across the centreline) cannot be straightened, the avatar is invalid, the clip gets no muscle curves, and the character shows the T-pose. `build_clips.py` `export()` now keys the rest pose on `REST_FRAME` (-10, outside the baked 0..length range), exports from that frame, then removes the key. Every clip was rebuilt from the base with this, so the attack clips now retarget from a true T-pose too. Check a clip with `fbxcheck.py`-style re-import: the REST bone directions must equal the armature's rest, not a pose.
- **Unity Humanoid retarget rules (2026-10-11, found with `Editor/CrucibleClipProbe.cs`):** `Crucible > Debug > Probe stance clips` (or drop a `ClipProbe.request` file in the project root and refocus the editor) samples the custom clips on the scene fighters and writes hand/elbow/knee/foot positions relative to the hips, muscle curve ranges and avatar bone maps to `ClipProbe.txt`; compare with the same numbers from Blender (`livepose.py`, hip-relative). Findings: (1) auto-mapped per-clip avatars sometimes mapped the hand to `*_ForearmTwist01`, so every clip meta now has `avatarSetup: 2` copying the Bjorn model avatar (the rig all clips are authored on). (2) `armTwist` / `foreArmTwist` on both character importers are 1 (all twist on the main bone, as the rig is animated); leg twist stays 0.5. (3) Muscle ranges clamp: keep solved poses within forearm twist ≤ 75°, wrist flex ≤ 60°, wrist deviation ≤ 30°, upper-arm twist ≤ 40°; the first guard solve used 90° twist / 80° wrist and came out as hanging arms in game. (4) Guards are now the heavy low versions: axes at belt height with level forearms, katana low chudan with the blade up 35°; blocks cross the axes at the face and raise the katana across the chest at ~60°.
- **Static fighters / sinking (2026-10-11, late):** `Scripts/debug/PlayProbe.cs` (runtime, writes `PlayProbe.txt` 0.5/2/4 s into play) showed both Animators in the correct idle state but with **no clip in the state** (`GetCurrentAnimatorClipInfo` empty, state length 1.0): the controllers' runtime data still referenced the clips that vanished during the failed avatar-copy imports, and a Humanoid state with no motion parks the hips at the root origin (the "sinking"). The editor sees the motions fine. Fix: restart Unity (rebuilds the runtime controllers); failing that, re-assign one motion in each controller. Clip importers are back on their own avatars (`avatarSetup: 1`) with an **explicit** 22-bone human map written by `Crucible > Debug > Fix clip human bone maps` (copies the map from the Bjorn model avatar; the twist-bone-as-hand auto-map is gone). Copying the model avatar itself fails ("Invalid copied Avatar Rig Configuration / No human bone found") even with the armature node renamed to `Armature`, so don't retry that. Clip FBX files now export the armature node as `Armature` (harmless). Delete `PlayProbe.cs` once the pipeline is stable.
- **Pipeline MCPs registered** (see memory note `asset-pipeline-mcp`): `krea-ai` (hosted, OAuth), `tripo-ai` (needs `TRIPO_API_SECRET`), Unity official plugin + Unity CLI (`unity pipeline install`, `unity mcp configure claude-code`). Plan: Krea T-pose image -> Tripo image_to_3d + rig -> FBX -> Blender clips -> Unity.
- **Katana spin (2026-10-11):** Miyamoto's sword whipped around in the idle because the guard had a near-straight elbow (15° bend) with large twist; Unity's Humanoid cannot split twist between upper arm and forearm at a straight elbow and flips between the two solutions each frame (probe: Right Arm Twist 0.02↔1.65, Forearm Twist −1.44↔0.13 inside a breathing idle). Rule: keep elbows bent ≥ 50° total whenever the forearm carries twist. Katana guard/block re-solved with that constraint (`GUARD_KATANA`, `BLOCK_KATANA`).
- **Next (user decision, 2026-10-11):** skip Krea for now; rig and animate the four existing Tripo models in `Assets/_project/Characters/_Unrigged/` (Barbarian, FemaleWarrior, Knight, Samurai; git-ignored) via `prep_character.py` → Tripo MCP rig (`upload_file` → `animate_prerigcheck` → `rig_model` → `convert_model` FBX) or Blender → `build_clips.py` → Unity, following every rule above. Verify first that the fighters animate after the Unity restart and the katana no longer spins.

### 2026-10-11 — connector check (Tripo key, Unity CLI)

- `claude mcp list` health: `blender` connected (Blender 5.1.1, add-on 1.8, master .blend open), `tripo-ai` connected, `krea-ai` needs OAuth (parked), Unity CLI **not installed** (`unity` not on PATH; user must run `irm https://unity.com/install.ps1 | iex` in PowerShell, then Claude runs `unity pipeline install` + `unity mcp configure claude-code`).
- **Tripo key:** the `tripo-ai` server entry had no `env` block and relied on inheriting `TRIPO_API_SECRET` from the process; `setx` only reaches new processes, so the MCP server ran without a key and every call returned 401. The first stored key was also invalid (14 chars; real keys are `tsk_…`, much longer). Fixed: new key in the user environment, verified with `GET https://api.tripo3d.ai/v2/openapi/user/balance` (code 0), and the server re-registered with the key baked in. Argument order matters, `-e` is variadic so it must follow the server name:
  `claude mcp add -s user tripo-ai -e TRIPO_API_SECRET=<key> -- npx -y tripo-ai-mcp-server`
  (`claude mcp remove -s user tripo-ai` first). Tools pick the key up only in a session started afterwards. Tripo balance was **0 credits** at the time; top up before rigging.
- `PlayProbe.txt` (after the Unity restart) shows both animators bound to `Bjorn_Idle` / `Miyamoto_Idle` on valid avatars, so the "no clip in state" problem is gone. Still to eyeball: the idles visibly playing, the katana sitting still, and Player1's model child carrying a second enabled Animator with no controller (harmless, but should be removed).

### 2026-10-11 — Unity CLI + editor MCP wired up

- Unity CLI 1.0.0-beta.13 installed at `%LOCALAPPDATA%\Unity\bin\unity.exe`. `unity pipeline install` added `com.unity.pipeline` 0.8.0-exp.1 to `Packages/manifest.json`; an Editor that was already open loads it only after a refocus (`unity status` reports `STATUS_PIPELINE_LOAD_PENDING` until then, then `ready`).
- MCP server `unity-editor-mcp` registered at user scope. `unity mcp configure claude-code` writes a bare `unity mcp` command, which fails with CONNECTION_CLOSED in any session whose PATH predates the install, so it was re-added with the full exe path: `claude mcp add -s user unity-editor-mcp -- C:/Users/mmamd/AppData/Local/Unity/bin/unity.exe mcp`. Tools show up as `mcp__unity-editor-mcp__*` in sessions started afterwards.
- With a live Editor, prefer `unity status` + `unity command <name> --caller plugin --skill unity-cli` (or `eval` for C#) over hand-editing `.unity`/`.asset` YAML. Health check: `claude mcp list` should show blender, tripo-ai and unity-editor-mcp connected; Tripo balance is still 0 credits.

### 2026-10-11 — roster rebuild plan and design sheets

- User decisions: rebuild every fighter as a NEW Tripo FBX (body generated weaponless in a T-pose, weapons and shields as separate meshes parented to hand sockets); all animation hand-keyed in Blender (no Mixamo); Bjorn + Miyamoto first, then review, then Saladin, Karen Moorehead, Aymar Buwaka, Genghis Khan, Richard the Lionheart. Krea and Tripo accounts are under ahdsram@gmail.com.
- `Design/` (outside Assets, not imported): `Character_Stats.md` (all 7 filled, passives designed), `Character_Movesets.md` (Bjorn + Miyamoto complete: 9 normals, 15 specials, 4 grapples, signature, with frame data, hit side, evade directions and animation notes; 3–7 pending review), the two blank `*.template.md` sheets they came from, and `ref_muaythai_pushkick.jpg` (Saladin's teep reference). Concept art for 1–6 is in `Assets/_project/Art/ConceptArt/`; Richard has none.
- Blocked on: Tripo balance 0 credits, Krea OAuth not completed, Editor not yet refocused to load the Pipeline package.

### 2026-10-11 — new Bjorn + Miyamoto bodies with separate weapons (Krea → Tripo → Blender → Unity)

- **Source images** (`ArtSource/Characters/Krea/`, git-ignored): Krea `google/nano-banana-pro` edits of the concept art: weaponless A/T-pose body on flat grey (`<Name>_body_tpose.png`) and the weapon alone side-on (`Bjorn_axe.png`, `Miyamoto_katana.png`). Upload with `get_upload_url` + `curl -F file=@…`, generate async, poll `get_job`.
- **Tripo** (API wallet on platform.tripo3d.ai, separate from web credits): `image_to_3d` v3.1 detailed = 60 credits per body, standard = 30 per weapon, `rig_model` biped fbx = 25, prerig check free. Task ids in `ArtSource/Characters/Tripo/task_ids.txt`; raw GLB/FBX downloads there (git-ignored). Both rigs are the same 41-bone Tripo skeleton (same names as `TripoRig`), rest pose is an A-pose, textures embedded (Color jpg, Roughness/Metallic jpg, NormalGL png).
- **Blender prep scripts** (`ArtSource/Blender/`): `prep_body.py <rigged.fbx> <out_dir> <Name> <verts>` decimates to ~120k keeping skin weights, names `<Name>_Rig/_Body`, writes `<Name>_Body.fbx` + `<Name>_Body.fbm/<name>_basecolor.jpg,_normal.png,_mask.png` (R metallic, G 1, A smoothness). `prep_weapon.py <glb> <out_dir> <Name> <verts> <length> <grip_frac> [mass|curve] [guard_scale]` orients the handle along +Z (head at the wide end, or tip at the thin end for `curve`), edge/blade on +Y, origin at the grip, scales to `<length>` (axe 0.41, katana 0.57 of a 0.98-tall body) and writes `<Name>.fbm` from the GLB's ORM. `build_katana.py` makes a clean procedural katana (Tripo's was chunky with a blob guard) with slots Steel/Iron/Wrap/Brass written to `<Name>.slots.txt`. `attach_weapons.py <Body.fbx> <render_dir> <weapon.fbx>:<R_Hand|L_Hand>:<flip>` bone-parents each weapon to the hand (grip 55% down the hand, offset into the palm, handle along the thumb axis, head toward the thumb, edge away from the fingers), re-exports the body FBX with the weapons as separate meshes, and renders `ArtSource/Characters/Renders/<Name>_<Hand>.png`. It is idempotent (purges non-body meshes first). Left axe = right axe mirrored (`flip=1`).
- **Unity**: `Editor/CrucibleCharacterSetup.cs` → `Crucible > Characters > Rebuild Player1 (Bjorn new body)` / `Rebuild Player2 (Miyamoto new body)` / `Report fighters`: sets the FBX importer to Humanoid (upperArm/lowerArm twist 1, legs 0.5, no materials), replaces every child of the Player root except `Hitbox` with the new FBX instance scaled to 2.9 world units tall (child scale 0.721 under P1's 4x, 0.934 under P2's 3x), points the root Animator at the new avatar, disables the FBX's own Animator, builds URP/Lit materials in `Characters/<Name>/Materials/` from the `.fbm` maps (or from `.slots.txt` for procedural weapons) and assigns them by renderer name. Weapon renderers in the hierarchy: `Bjorn_Axe_R`, `Bjorn_Axe_L`, `Miyamoto_Katana_R` under the hand bones. Scene saved with the new bodies; the old `tripo_convert_*` / `miyamoto rigged` FBX files are still in the (ignored) Model folders but unreferenced.
- **Unity CLI driving**: `unity command eval --caller plugin --skill unity-cli '<C#>'` from Git Bash (PowerShell mangles the quotes). The Editor's main-thread cap is 5 s, so anything that reimports goes through `--detach` + `unity job wait <id>`. `capture_game_view --save_path <p>` writes under `Assets/` whatever path you give (move the PNG out and delete `Assets/ArtSource` afterwards). `console --tail N`, `editor_play` / `editor_stop`.
- Verified in Play mode: both fighters animate the existing guard idles on the new rigs (no T-pose), weapons follow the hands. Captures in `ArtSource/Characters/Renders/unity_play_*.png`.
- Tripo balance after this: 270 credits. Next: the new hand-keyed clip set from `Design/Character_Movesets.md` (attach the weapons to `TripoRig` in the animation master for preview; author on `TripoRig` as before, Humanoid retarget carries it to the new rigs).

### 2026-10-11 — fists, IK animation rig, new clip pipeline (first Bjorn clips)

- **Grips**: the Tripo rig has no finger bones, so `attach_weapons.py` now bakes closed fists into the body mesh: finger verts beyond the knuckle line (55% of the wrist→fingertip extent, measured from the mesh because the two hand bones differ in length) wrap around the handle axis like a bend deformer. Hand frame: both thumbs point world-forward (A-pose, palms down), palm = thumb × along flipped downward. `none:L_Hand:0` bakes a fist with no weapon (Miyamoto's left). Because the bake is destructive, `prep_body.py` output is kept clean in `ArtSource/Characters/Prepped/<Name>_Body_clean.fbx` and attach reads that; `ArtSource/Blender/rebuild_fighters.sh [names]` runs the whole chain (`SKIP_PREP=1` to redo only weapons/fists). Project path has a space: the script uses `pwd -W` and bash arrays.
- **Katana**: `build_katana.py` (procedural: tsuka, tsuba 7.2 cm, habaki, 72 cm blade with sori and kissaki; slots Steel/Iron/Wrap/Brass via `<Name>.slots.txt`, read by `CrucibleCharacterSetup` into flat URP materials).
- **Animation masters** `ArtSource/Blender/<Name>_AnimMaster.blend` from `anim_rig.py <Body.fbx> <out.blend>`: the body FBX (with weapons) plus a control layer: `CTRL_root`, `IK_L/R_Foot`, `IK_L/R_Hand`, `POLE_*_Knee/Elbow`, and helper bones `HLP_*_Ankle` (calf tail → foot head) / `HLP_*_Wrist` (forearm tail → hand head) that bridge Tripo's disconnected chains; foot/hand are re-parented under the helpers and the IK sits on the helper (chain 3, use_tail) so the control positions are reached exactly. Knees pre-bent 1.2 cm, elbows 1 cm, otherwise the straight rest pose gives the solver no bend direction. Arm IK influence = custom prop `ik_arms` (0 FK / 1 IK). `bake_and_export()` (text block `export_tools.py` in the master) samples world matrices of the 41 deform bones, deletes the control layer, re-parents foot/hand to calf/forearm, re-keys, keys the rest on frame -10, names the root `Armature` (the clip metas' explicit human map expects it) and exports an armature-only FBX. It is destructive: one clip per Blender run.
- **Clip authoring** `author_clips.py <master> <ClipName> <out_dir> [strip_dir]` + per-character `clips_<name>.py` (`@clip(name, frames, loop)` functions keying through the `Rig` helper in world terms: `hip/spine/neck/head/clav/foot/hand/pole/elbow/arm`, angles in degrees, +X forward +Y left +Z up; `rig.key(frame, ease)`). Never call `scene.frame_set` before `keyframe_insert` on this API (evaluating the action overwrites the pose just set: that was a frozen-clip bug). Blender 5.1: F-curves live in `action.layers[].strips[].channelbag(slot)` (`action_fcurves()` helper), `Bone.select` is gone, bezier easing enum is `AUTO`. A filmstrip (8 frames, workbench, weapons red) renders when `strip_dir` is given.
- **First clips**: `Bjorn_Idle` (120 f), `Bjorn_Walk_Forward` / `Bjorn_Walk_Back` (40 f, IK feet, in place) exported over the old files in `Animation/Clips/Custom`, so the controllers pick them up by name. Guard: left lead, body bladed 24°, knees bent (hip −0.065), left axe forward-up, right axe cocked by the hip.
- **Unity gotchas**: the clip metas' explicit human description still holds the OLD Bjorn model's skeleton → "Avatar Rig Configuration mis-match … 1.7 mm / 4.5°" warnings (harmless; refresh via `Fix clip human bone maps` once it points at `Bjorn_Body.fbx`). After a failed clip import the runtime controller keeps no clips (`GetCurrentAnimatorClipInfo` empty, fighter sinks); fix without restarting: re-assign every `state.motion` on the controller via the API and `SaveAssets`. Forcing `MoveSpeed` from `eval` did not enter the walk state; test walking with the keyboard.

### 2026-10-12 — one big axe for Bjorn, Tripo katana, Mixamo stop-gap animation

- **Weapons** from the user's Tripo downloads (`ArtSource/Characters/Tripo/BigAxe`, `Katana2`, unzipped; FBX + `.fbm` basecolor/metallic/roughness/normal): `prep_weapon.py` now imports FBX too, copies the `.fbm` maps as files (re-saving node-linked images fails with "no image data") and builds the mask from metallic+roughness. Bjorn's `Bjorn_Axe` is a 0.70-unit two-handed bearded axe held in the right hand, grip 30% from the butt, left fist baked for the haft (`rebuild_fighters.sh`: `Bjorn_Axe.fbx:R_Hand:0 none:L_Hand:0`). Miyamoto's `Miyamoto_Katana` is the Tripo katana (the procedural `build_katana.py` one is retired; its slots file removed). Sword handle-end rule in prep: the thick end (tsuka+tsuba, larger thin-axis width) goes to the bottom; the earlier radial rule was fooled by the strong curve.
- **Mixamo packs** copied to `Animation/Clips/Mixamo/AxePack` (Pro Melee Axe Pack, 47 clips) and `GreatSwordPack` (Great Sword Pack / Maria, 51 clips), skeleton-only 30 fps files, walks carry root motion (1.4 m per cycle). `Editor/CrucibleMixamoSetup.cs` → `Crucible > Animation > Use Mixamo packs`: imports each mapped clip as Humanoid (own avatar, XZ root motion NOT baked so an Animator without Apply Root Motion plays walks in place, Y + orientation baked, loops on idle/walk/block), re-points the P1/P2 states (idle, walks, 3 attacks, 3 reacts, block hold/impact; dashes keep the custom clips) and fits each attack: the clip is trimmed to its swing core from `Animation/Clips/Mixamo/swing_trims.json` (Blender analysis of right-hand speed: peak + core window, `scratchpad swing_analysis.py` logic) with the peak placed at startup + active/2 of the AttackData, then the state speed makes the window span startup+active+recovery (≈ x2 instead of x8 whole-clip). `Restore custom clips` puts the `Clips/Custom` FBX clips back by state name.
- **Play-mode capture from the CLI**: the Editor only advances when focused; `powershell (New-Object -ComObject WScript.Shell).AppActivate(<unity pid>)` before `editor_play` fixes it; capture with `--source camera`. `PlayerSettings.runInBackground` was set true (doesn't affect the Editor).
- Attack timing is still the AttackData frame data (unchanged); Mixamo swings only approximate it. The hand-keyed set (`author_clips.py`) remains the real target once the user's reference videos exist. Anim masters rebuilt for the new weapons.
- (2026-10-12, later) User feedback on the Mixamo pass: "smooth animations perfect", weapons faced the wrong way, fighters floated. Fixes: weapon edge now points along the knuckles (`yax = along` in `attach_weapons.py`); `CrucibleCharacterSetup.GroundOffset` lowers each model child so the animated feet sit on the floor (Bjorn −0.136/4, Miyamoto −0.623/3 root-local; measured renderer-bounds minimum in Play mode; roots stay where the capsules land, 0.20 / 0.75). `Design/Character_Movesets.md` Bjorn section rewritten for the two-handed Dane axe (same frame data as the AttackData assets).
- (2026-10-12, later still) User pass 2: both hands on Bjorn's axe everywhere, male idle/walk for Miyamoto, three katana strikes (overhead diagonal, middle horizontal, spinning), hitboxes on the weapons only, attacks + animation 15% slower. Done as:
  - **TwoHandGrip** (`Scripts/characters/TwoHandGrip.cs`, on each Player root): Humanoid IK pulls the left hand to a `LeftGrip` socket child of the weapon every frame (position weight 1, rotation 0). Needs IK Pass on the controller layer (set by the rebuild menu) and a REBUILT runtime controller (re-assign motions trick, or restart Unity) or `OnAnimatorIK` never runs. Debug counters `IkCalls` / `LastPreIkDistance`.
  - **Weapon hitboxes**: `CrucibleCharacterSetup.SetupWeapon` adds a trigger BoxCollider over the blade region of the weapon mesh (mesh-local **Z** is the handle axis: Blender's FBX exporter keeps mesh data in Blender axes and puts the axis swap on the node; `WeaponSpec` holds the blade-start and left-grip positions as fractions of the Z extent) plus a `HitDetector` with `followWeapon = true` (new flag: Activate only enables the collider instead of re-shaping the front box), points `CombatSystem.hitDetector` at it and deactivates the old `Hitbox` child. `CombatSystem` keeps reach/dodge maths in fighter space when `FollowWeapon` is set.
  - **Two-handed carry clips**: `ArtSource/Blender/mixamo_carry2h.py <in.fbx> <out.fbx> [fwd height side]` adds a constant rotation offset to RightArm/RightForeArm on every key (grid-solved on frame 1 so the right hand lands in front of the belly; Mixamo clips face world −Y after import) and re-exports the Mixamo skeleton. Output in `Animation/Clips/Mixamo/AxePack2H/` (standing idle, walk forward, walk back); originals backed up in `ArtSource/Mixamo/`. Both P1 and P2 locomotion map there. Reason: in the one-handed carry the haft is ~1.45 units from the left shoulder (arm reach 0.91), so no IK could put the left hand on it.
  - **Miyamoto strikes** (hand-trajectory analysis, `scratchpad strike_analysis.py` logic): Light = `great sword slash (3)` (horizontal middle), Medium = `great sword slash` (overhead diagonal), Heavy = `great sword high spin attack` (spinning); the pack's `great sword attack` is a plain thrust if the user prefers it. Left/right direction unverified: flip with the clip's `mirror` import flag if wrong.
  - **Slow motion**: `Scripts/debug/DebugTimeScale.cs` on `GameMangers` (menu `Crucible > Debug > Slow motion 15% (toggle)`), Time.timeScale 0.85; CombatSystem timers use deltaTime so frame data and animation slow together. Remove before balancing.
  - Play-mode `eval` right after `editor_play` tends to hit the 5 s main-thread cap during the domain reload: wait ~8 s and retry.
- (2026-10-12, third pass) User: "weapon collider blocks all attacks", dash is slimy, try the downloaded arenas.
  - **Why blade boxes missed**: a real blade crosses the 1.2-unit capsule for a few frames only, and the Mixamo trim aligned the *hand-speed peak* with the active window, which for the 360 spin is when the axe is on the far side. Fixes in `HitDetector`: swept capsule between last/this frame's blade (`sweepRadius` 0.25) in `Update`, gated on blade tip speed ≥ `minBladeSpeed` 3 u/s (a resting blade touching the opponent in the carry no longer "hits" at arm time); `CombatSystem` arms weapon boxes `BalanceConfig.WEAPON_ARM_PAD_FRAMES` (5) before/after the authored active window (IsInActivePhase unchanged). Verified with `Scripts/debug/BladeTrace.cs` (per-frame blade/capsule log; add to GameMangers at runtime) at `DebugTimeScale` 0.08: the spin's pass in front happens ~frame 17–18 and now registers at the pass, not the wind-up. The Editor runs ~4 fps under the CLI, so any timing test needs the 8% debug scale (set it through the DebugTimeScale component; raw `Time.timeScale` gets reset).
  - **Dashes**: `Dash_*` states = the first half (one stride) of the two-handed walks (`AxePack2H`, now also `standing walk left/right`), fitted to `Fighter.dashDuration` (0.25 s → ≈ x2.5). `CrucibleMixamoSetup.ImportAll` groups map entries per file so one Mixamo file carries several clips (walk loop + dash + P1/P2 names); `LoadClip(path, name)` picks by clip name.
  - **Arenas**: `ArtSource/Blender/prep_env.py` slims the three Tripo sets (desert arena, medieval fortress, forest clearing; ~1M → 300k verts, maps copied) into `Environment/Tripo/<Name>/`. `Editor/CrucibleTripoArenas.cs` → `Crucible > Arena > Tripo > …` places one: keep the prefab root rotation (it carries the FBX axis swap; resetting it stood the set on its side), scale to 70 units wide, floor = the largest flat up-facing area by area histogram (0.25-unit bins) moved to y = 0, mesh colliders, URP material, Al-Quds (`Arena_AlQuds`) + Ground hidden; `Back to Al-Quds` restores. Desert arena placed and saved; its props sit close to the fight camera.
- (2026-10-12, fourth pass) User: can't get close enough (esp. Miyamoto), Bjorn's left hand off the haft and arms through the body, overhead drifting left and never landing, spin should extend the axe and spin with the body, katana points up during strikes, arenas need boundaries.
  - **Standoff**: `fighterRadius` 1.2 → 0.8 on both Fighters (walk standoff 1.6); the katana physically reaches ~2.1 and the axe ~2.5 from the hand, the old 2.4 standoff was tuned for the 3-unit front boxes.
  - **WeaponAim** (`Scripts/characters/WeaponAim.cs`, added by the rebuild menu): in LateUpdate during an attack, rotates the weapon about the grip toward the opponent's chest (55% body height), weight ramps through startup, 1 on active, fades 0.12 s after; max 55°. Fixes the katana pointing at the sky in the greatsword clips and pulls off-centre chops onto the target. Hit detection reads the final pose.
  - **Left hand**: socket 0.22 up the haft from the right hand (fraction 0.61 of the haft; was lowered to 0.50 for reach and is back at 0.61 now that the carry sits further out), `LeftElbowHint` child of the Player root at (−0.35, 0.42, −0.25) root-local, carry re-posed with `mixamo_carry2h.py` target fwd 0.40, height −0.06, side +0.10 (idle, walks, strafes).
  - **Spin**: `AxePack2H/standing melee attack 360 high` = the spin with the right arm re-posed to fwd 0.55 / height +0.22 (axe extended at chest height, body rotation does the sweep). **Overhead**: `AxePack2H/standing melee attack downward` = the chop yawed 25° right (`mixamo_yaw.py`; measured with `scratchpad contact_analysis.py`: lowest-hand frame was 0.23 to the left of centre, now 0.05). Both mapped in `CrucibleMixamoSetup`.
  - **Arena bounds**: `CrucibleTripoArenas.AddBoundsRing` (28 thin tall box colliders at the radius containing 85% of the flat floor area, child "Bounds") on every Tripo arena placement; `AddBoundsRect(29, 19)` on Al-Quds when switching back.
  - Verified in Play at 2.0 units: Bjorn heavy HIT (15), Miyamoto light HIT (9). Captures `ArtSource/Characters/Renders/anim/sw_*.png`.
- (2026-10-12, fifth pass) User: L lands to the left, K spins without extending and ends before hitting, Miyamoto's . and / point up and fall short.
  - **Found the real blocker**: both `WeaponAim` components were DISABLED in the saved scene (left off by an eval during an earlier measurement that got saved), so the user never had the aim. Now enabled and saved; `maxAngle` serialized 120 (changing a C# default does not touch already-serialized components). Aim target is now the opponent's SkinnedMeshRenderer bounds (55% height ≈ chest), not the physics capsule (2.1 × root scale = 6.3 tall, the old "chest" was above the head, which is why the katana aimed high).
  - **Contact calibration**: `Animation/Clips/Mixamo/swing_contacts.json` (state → source frame where the weapon TIP crosses the opponent's centre line, measured with `BladeTrace` at `DebugTimeScale` 0.08) re-cuts each attack window around the contact at `ContactSpeed` 1.8 (`CrucibleMixamoSetup.ContactFrame`). Traces showed the axe HEAD crosses the centre well before the hand's speed peak on horizontal cuts, and the chop's head comes down 2 units to the left even after the 25° yaw (the aim closes that).
  - **Spin**: `mixamo_carry2h.py ... lock` replaces the right-arm keys with the solved pose (constant, chest-relative) instead of offsetting: axe held extended at chest height for the whole spin. `Bjorn_Medium_SpinningSlash` AttackData changed 10/5/14 → 12/5/28 (45 f) so the full rotation plays; the hit is the first sweep across the front, the rest of the spin is follow-through.
  - **HitDetector**: tip position/speed tracked every frame (armed or not) so a crossing on the arming frame counts (the spin's hit was exactly there).
  - `BladeTrace.Retarget()` + `lat`/`fwd` columns (tip in the attacker's frame). Verified at 1.8 units with aim: all six attacks HIT.
- (2026-10-12, sixth pass) User: "weapons drifting away, strikes only land point-blank; the target of the attack should be central regardless of the move; fighters should be reaching forward to land."
  - **Attack lunge** (`Fighter.HandleAttackLunge`): on the first frame of an attack the fighter works out `clamp(gap - LungeStopDistance, 0, LungeMaxDistance)` and covers it evenly over the startup frames, straight at the opponent, never past the stop distance. Defaults `BalanceConfig.ATTACK_LUNGE_STOP` 1.7 / `ATTACK_LUNGE_MAX` 1.5 (world units, centre to centre); `AttackData.lungeStopDistance` / `lungeMaxDistance` override per move (-1 = default, 0 max = no lunge). Logged `P#: <attack> lunge X units (gap A -> B) over N f`. Facing is now free during the startup too (locked from the active frames on), so every strike is thrown at the opponent's centre.
  - **Strike reach IK** (`TwoHandGrip.StrikeReach`): during the startup/active frames the right hand is pulled along the shoulder→chest line so the blade's middle overlaps the target (`bladeOverlap` 0.5, `maxArmExtension` 0.95 of the arm, `strikeReachWeight` 1); `WeaponAim` then points the blade. Weight ramps/fades like the aim.
  - `HitDetector.sweepRadius` 0.25 → 0.35 (C# default and both scene weapon detectors): the spin's tip was passing 0.5 from the axis vs a 0.55 catch radius.
  - **Testing gotcha**: teleporting `transform.position` on the fighters during Play leaves the Rigidbody interpolating toward it, so the first attack frames saw a stale capsule and random whiffs. Place test fighters through `Rigidbody.position` + `transform.position` + `Physics.SyncTransforms()` and wait a couple of frames before `TryAttack`. The Editor runs ~4 fps under the CLI, so normal-speed tests skip whole active windows: always test at `DebugTimeScale` 0.08.
- (2026-10-12, seventh pass) User: both floating, lunge too fast/long, Bjorn's spin ends at 270° and resets, Bjorn's overhead looks like a punch, Miyamoto's heavy "just moves".
  - **Floating**: my Play-mode test placements averaged the two roots' heights (`mid ± dir*d/2`) and a later save kept it (roots at 0.93/1.42 instead of 0.24/0.75). Fixed in the scene. Rule for placement evals: keep each fighter's own y.
  - **Lunge**: `ATTACK_LUNGE_MAX` 0.9, `ATTACK_LUNGE_MAX_SPEED` 4 u/s (lights step ≤ 0.5, heavies 0.7 at 2.4 units), eased out (quadratic) over startup + half the active window.
  - **Strike reach IK off** (`TwoHandGrip.strikeReachWeight` 0 in code and scene): pulling the hand at the chest while the aim swung the axe 120° made the chop a punch. Reach is the lunge's job.
  - **Aim modes** per attack: `AttackData.aimMode` Full / YawOnly / Off + `aimMaxAngle` (-1 = component cap). Overheads use YawOnly (turn onto the centre line, keep the clip's pitch). Bjorn heavy is YawOnly; the rest Full.
  - **Bjorn heavy clip** → `GreatSwordPack/great sword slash` (two-handed overhead that comes down in front at centre; the axe pack's `standing melee attack downward` bottoms out at the left hip with the hand at fwd 0, so no aim could make it a chop at the opponent). Contact 21.5.
  - **Miyamoto heavy**: the spin's wind-up was being cut (window started mid-turn, facing away). `swing_contacts.json` now accepts `{"contact": f, "speed": s}` per state (`CrucibleMixamoSetup.ContactField`); heavy = contact 36.2 at speed 2.0 and the AttackData is 30/4/14 (48 f) so the window is 4.2–52.2: the whole 360° plays before the cut. `great sword attack` is the plain-thrust alternative if the spin is rejected.
  - **Bjorn spin** measured in game: hip yaw completes to within ~15° of forward by the exit (the clip itself settles at −10°); it was never a 270° reset in this build. `BladeTrace` now logs `stateT` and `hipYaw`.
  - `scratchpad clip_curves.py` (Blender headless): per-frame hip yaw + right-hand fwd/right/up/speed for any Mixamo FBX; used to pick clips.
  - Verified at 2.4 units, 8% speed: all six HIT, feet on the floor.
- (2026-10-12, eighth pass) User: the spin dealt damage on the press, before the axe came round. `Bjorn_Medium_SpinningSlash` contact moved from the first pass (23.4) to the sweep after the rotation (38.0) and the AttackData re-cut 28/5/12 (45 f, hit at 68%) so the window still starts at the wind-up (10.6–51.1). Traced: the hit lands ~0.45–0.55 s after the press with the hips ~275° round, at both 2.0 and 2.4 units.
- (2026-10-12, ninth pass) User: hitbox a little short (10–20%), stun too long and the stun animation too slow; no animation changes. Blade boxes extended 15% past the tip (`CrucibleCharacterSetup.BladeTipExtend`, applied to both scene boxes). `hitstunFrames` 12 → 7 light / 8 medium / 10 heavy on all six AttackData. React_* states now play over `CrucibleMixamoSetup.ReactDuration` 0.45 s (speed ×1.9–3.8; the Mixamo reacts are 0.9–1.7 s performances).

### 2026-10-13 — front end, rebindable controls, dash counter positioning

- **Menus** (`Scripts/UI/GameMenu.cs` on `GameMangers`, built from code with uGUI legacy Text, no prefabs): main menu (Local Play, Controls, Settings, Help, Quit), pause (Escape / Options: Resume, Help, Controls, Settings, Main Menu), KO screen (Rematch / Main Menu, shown 1.6 s after a health bar empties), Settings (master volume, game speed 50–100% which drives `DebugTimeScale`, blood on/off via reflection on `CombatSystem.bloodOnHit`, which player the first controller drives), Controls (every `GameAction` for both players, keyboard key + pad button; select → press a key or pad button; Esc cancels; "Reset all to defaults"), Help (mechanics + a controls summary generated from the live bindings). Navigation: arrows/WASD, d-pad or left stick; Enter/Space/Cross confirm; Esc/Backspace/Circle back; mouse clicks work (EventSystem + StandaloneInputModule created at runtime). Pause sets `Time.timeScale` 0 and `InputManager.InputEnabled` false; fighters are frozen on the menus by disabling their `Fighter` components; `Fighter.ResetForMatch` / `CombatSystem.ResetForMatch` put everything back on the spawn marks with fresh stats.
- **Input** rewritten: `Scripts/input/ControlBindings.cs` (per-player KeyCode + `GamepadButton` per action, JSON in PlayerPrefs `crucible.controls.v1`, PlayStation button names in the UI) and `InputManager` now reads keyboard through the bindings and pads through the Input System's `Gamepad` (first pad → Player 2 by default, second pad → Player 1; `activeInputHandler` is Both). Dash: dash key + direction (P1 also keeps the number-row layout), or a right-stick flick / pad dash button + left stick. Settings/controls persist across runs.
- **Dash counter**: side dashes now arc around the opponent (`Fighter.HandleDash` re-aims the velocity perpendicular to the opponent each frame) so a dodge ends at the same distance, squared up. An attack that gets dodged (`CombatSystem.MarkEvaded`, both evade paths) gains `BalanceConfig.DODGED_RECOVERY_FRAMES` (18) of extra recovery with facing locked, so the dodger's half-second counter window lands on an open attacker. Not yet play-tested against a real timed dash (the CLI cannot press inputs).
- **Close-range whiff report** ("Miyamoto too close + lunge"): not reproduced. All five tested strikes land from 1.0 and 1.3 units at 8% speed with the blade tip within 0.0–0.8 of the capsule axis. Needs the exact sequence from the user (which buttons, was the other fighter attacking/blocking, after a dash?).
- (2026-10-13) User: "Miyamoto block keeps looping". P2 `Block_Hold` was `great sword blocking` (16 f raise-the-guard motion) set to loop. Now `great sword blocking (2)` (static hold); `Block_Impact` is the AxePack `standing block react large` (jolt that settles back to the guard) because the sword pack has no block-react. Clip curves via `clip_curves.py` tell a hold (hand still) from a performance.
- **State at the end of 2026-10-13 (user: "so far so good"):** menus, rebindable controls, lunge/aim/contact-calibrated strikes, dash orbit + dodged-attack recovery, static block holds all in and pushed.
- **Next session (user request): special strikes and syncing attacks (combos).** Design reference: `Design/Character_Movesets.md` (specials = modifier + button per the InputData `specialModifier` / `lowModifier` fields, already read by `InputManager` but unused by `CombatSystem`; `AttackType.Special` exists in the clash weights). Combos = chaining strikes on hit/block with cancel windows (e.g. light → medium → heavy) so the follow-up starts before the recovery ends and the animations sync: needs a cancel window on `AttackData` (frames after contact where the next input is accepted), input buffering in `CombatSystem`, and transitions between attack states in the controllers (currently every attack is Any State → attack, exit 0.9). Mixamo "standing melee combo attack ver. 1/2/3" and "great sword slash (2)/(4)/(5)" are the unused clips to draw from (`clip_curves.py` for their hand paths).
- (2026-10-13, later) **AI opponent** (`Scripts/ai/AiController.cs` on `GameMangers`; main menu "Against AI" → Chill / Moderate / Insane). It writes an `InputData` snapshot for Player 2 each frame (`InputManager.SetAi/SetAiInput`; the human's controller is routed to Player 1 while the AI holds Player 2), so it obeys every rule a human does. Profiles differ in reaction time to a seen wind-up (0.45 / 0.22 / 0.08 s), decision interval, aggression, dodge and block chances, mistakes (swinging from range, dropping the guard), preferred range, whiff punishing and heavy bias. Behaviour: reads the opponent's startup → sidestep dash (orbits) or block; counters after a clean dodge (`Fighter.DashCounterOpen`); punishes recovery; keeps spacing; backs off low on stamina; dashes in from far. Verified: Insane KOs an idle Player 1 in < 30 s with 9 hits. The KO screen says YOU WIN / THE AI WINS in that mode.
- **Close-range whiff root cause**: slow rendered frames. Under the CLI the Editor runs ~4 fps and the AI's thrusts whiffed at exactly 1.70 units ("box missed the body"): a thrust goes out and back inside one frame, so the swept blade test has nothing to catch; chained thrusts at 8% speed all land. The laptop Editor can dip the same way. Fix: `CombatSystem.Update` slow-frame fallback — when `Time.unscaledDeltaTime > BalanceConfig.SLOW_FRAME_SECONDS` (1/30) during the authored active window, the authored front box (`IsWithinReach`, fighter space) judges the hit and `HitDetector.ForceHit` applies it (logged "slow frame ... front box judged"). Never used at 60 fps. Rerun: 0 whiffs, 2 of 9 hits via the fallback.
- (2026-10-13, night) **Web / phone test build + same-WiFi play.**
  - `Crucible > Build > WebGL (test build)` (`Editor/CrucibleBuild.cs`) → `Builds/WebGL` (git-ignored), uncompressed, IL2CPP, low stripping. ~280 MB (224 MB data: every arena in the scene ships; hide/remove the unused Tripo arenas to shrink). Build time 23 min normally, ~2 h when the machine is out of RAM (run nothing else).
  - **Touch controls** (`Scripts/input/TouchControls.cs`, faded buttons built from code; shown on mobile/WebGL or `forceShow`): move < >, dash << >> ^ v (screen directions), L M H, BLOCK (hold), pause ||. Against AI / online = one full-screen pad for Player 1; Local Play on one device = two mirrored half-screen pads. `InputManager` merges the touch snapshot with keyboard and pad. `MobileQuality` (render scale 0.7, 18-unit shadows, no post, no AA on mobile/WebGL).
  - **Same WiFi**: browsers cannot talk to each other, so the PC runs `node Server/relay.js` (static host of `Builds/WebGL` + WebSocket relay on :8080; `npm install` once in `Server/`; prints the LAN URL, e.g. http://192.168.0.155:8080/). Phones open that URL. Main menu → Same WiFi → Host a game (you are Bjorn) / Join a game (room list, you are Miyamoto, played with the Player 1 controls). Host-authoritative: `Scripts/net/NetSession.cs` (host feeds the guest's `I ...` input lines into the InputManager's Player 2 override slot, sends `S ...` state at 20 Hz: position, yaw, hp, stamina, animator state hash + time, MoveSpeed; guest shows it with its Fighter/CombatSystem disabled). `WebSocketClient.cs` = browser WebSocket via `Plugins/WebGL/WebSocketBridge.jslib` (polled) or System.Net.WebSockets in the Editor/standalone. Pause and rematch are host-authoritative (`P 1|0`, `R`). Verified: relay protocol (scripted host+guest), Editor host + scripted guest through the relay (inputs drive P2, 15 state frames/s). Not yet verified on a real phone.
  - Known gaps for the test: guest sees no blood/hit effects (CombatSystem off on the guest), hit reactions on the guest come only from the animator state sync, no reconnection, no lobby names beyond the PC's machine name.
- **Hosting the test build (2026-10-13, final):** the router isolates its WiFi clients (the PC on 5 GHz "3nza" never saw a packet from the phones; Windows Firewall already allows Node), so LAN play through the router does not work there. Working routes: (1) **Cloudflare quick tunnel** (`cloudflared` installed via winget at `C:\Program Files (x86)\cloudflared\`): `Server/start-online.cmd` opens the relay and the tunnel in two console windows and the tunnel window prints the public `https://<words>.trycloudflare.com` link, which works from any network (the "Same WiFi" menu is really "online through this PC"); `Server/status.cmd` reports both and the last link. The link changes on every tunnel restart; a fixed address needs a free Cloudflare account + named tunnel. (2) The PC's **mobile hotspot** (turned on this session: SSID "MMAMDOUH 9448", password in Windows Settings > Mobile hotspot) at `http://192.168.137.1:8080/`. localtunnel (`npx localtunnel`) was tried first and died within minutes: do not use it. `WebSocketClient.PageUrl` derives ws/wss from the page URL, so the same build works on any of these routes.
- **Next session:** special strikes + combos (see the 2026-10-13 plan above); shrink the web build (only the active arena in the scene); confirm the phone test results from the user (touch feel, frame rate, online latency); blood/hit effects on the guest.

### 2026-10-17 — spam limiter, touch pad gating, camera home side, headless builds

- **Frame data tuned by the user** before this session (in the working tree): Bjorn light recovery 8→7, medium startup 28→23, heavy damage 18→16; Miyamoto light 11→10 dmg / recovery 10→6, medium 14→10 dmg / recovery 12→8, heavy 18→12 dmg / recovery 14→12. Scene: `dashDuration` 0.25→0.35, stamina regen 10/13→15/18, `dashStaminaCost` 25→20, turn speeds re-tuned, `dustEffects` back on. Combos are still **timing off the end of the animation** — there is no cancel-window system yet.
- **Same-attack chain limit** (anti-spam, `CombatSystem` + `BalanceConfig`): the same attack can be thrown `SAME_ATTACK_CHAIN_LIMIT` (2) times in a row; when the second one finishes its recovery every attack is locked out for `ATTACK_CHAIN_LOCKOUT` (0.8 s, user asked for 0.66–1 s). A different attack resets the chain, as does a gap longer than `ATTACK_CHAIN_WINDOW` (0.6 s) after an attack ends. Getting hit clears the chain, so a flinch never stacks a lockout on top of hitstun. Fields `chainType` / `chainCount` / `chainIdleTimer` / `attackLockoutTimer`; logs `2x Light in a row - locked out for 0.80s` and `BLOCKED - chain lockout, 0.52s left`. Alternating buttons is still uncapped by design.
- **Touch pad only on touch devices**: `TouchControls.WantsTouch` was `isMobilePlatform || platform == WebGLPlayer`, so every PC browser got the pad. New `Plugins/WebGL/DeviceBridge.jslib` → `CrucibleIsTouchDevice()` asks the page: a phone/tablet user agent, or `maxTouchPoints > 1` with a coarse pointer and **no** fine pointer (catches iPadOS Safari, which claims to be a Mac; a Windows touchscreen laptop reports a fine pointer and counts as desktop). Result cached in `touchDevice`, logged as `TouchControls: touch device = …`. A real finger on a device called desktop flips it on for the rest of the run. **Settings > On-screen buttons: Auto (phones) / Always / Off** (`SettingsData.touchButtons`, `TouchControls.SetMode`, applied live so it can be changed from the pause menu).
- **Camera stays on its home side** (`FightCamera`): the old rule only blocked an instant 180° flip, so orbiting side-dashes rotated the fighters' line a few degrees at a time and walked the view round to the far side of the arena. `homeSide` is remembered at `Start`; each frame the perpendicular on that half is chosen and the swing is clamped to `maxSideSwing` (70°), with `sideStickiness` (0.2) holding the current choice when the pair lines up with the camera and both perpendiculars are equally far from home. Both serialized.
- **Builds without opening the Editor**: `"C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe" -quit -batchmode -nographics -projectPath "<project>" -executeMethod CrucibleBuild.BuildWebGL -logFile <log>` — 6–14 min with a warm `Library/Bee` cache (the 23 min figure was from the Editor). Needs no Editor instance, and fails if one holds `Temp/UnityLockfile`. Compile-check first with the Roslyn trick: the `.rsp` files build straight from `Assembly-CSharp.csproj` / `Assembly-CSharp-Editor.csproj` (`DefineConstants` → `-define:`, `HintPath` → `-r:`, `Compile Include` → file list; the editor assembly also needs `-r:` on the runtime DLL).
- **Cache-bust** (`CrucibleBuild.CacheBust`, runs after a successful build): stamps `?v=<yyyyMMddHHmm>` on the loader/data/framework/wasm URLs in `index.html`. Unity's `dataCaching` keys the 224 MB data file on its URL, so without this a tester who reloads keeps playing the build they first opened however hard they refresh — that is why a PC screenshot still showed the touch pad after the gating shipped.
- **Quick tunnels drop without warning**: mid-session cloudflared logged `Unauthorized: Tunnel not found` and retried forever while the relay stayed healthy on `localhost:8080`. Restarting gives a **new** URL every time (this session used two). Check with `Server/status.cmd`, or `netstat -an | grep :8080` + `tasklist | grep cloudflared`. A fixed address needs `cloudflared tunnel login` (browser, user must do it) + a named tunnel — offered, not yet done.
- Server was started and then **stopped** at the end of the session; nothing is listening now.
- **Next session:** special strikes + combos with real cancel windows and input buffering (the chain limiter is the anti-spam floor they sit on); decide whether alternating-button spam needs a global minimum gap; consider gating `MobileQuality` by the same touch check so PC browsers stop getting phone visuals; shrink the web build (only the active arena); phone feedback (touch feel, frame rate, online latency); blood/hit effects on the guest.

### 2026-10-17 — axe guard attempt, REVERTED (the idle is being filmed instead)

- **User's ask was** rear hand high on the haft, lead hand at the bottom, axe canted so the handle
  points at the left hip and the blade sits above the right shoulder. **Outcome: reverted** - the
  user will film an idle and the guard will be rebuilt from the capture. Everything below is why,
  so the next attempt does not repeat it.
- **A mount tweak can never do it.** In the `AxePack2H` carry both hands sit at belly height ~0.5
  world units apart; that diagonal needs ~1.2 units between the grips. The haft lies on the line
  between the hands, so grip spread and axe angle are ONE parameter, not two.
- **`ArtSource/Blender/mixamo_guard2h.py`** (kept, unused): generalises `mixamo_carry2h.py` to
  grid-solve a constant rotation offset for BOTH arms so each hand lands on its own target
  (hips + fwd/up/side, metres). Wide works: `0.14 0.32 0.38 0.10 -0.16 -0.14` put the right hand
  at x 0.51 (was 0.13), hands 0.76 units apart. **High does not**: asking for the right hand at
  shoulder height (up 0.46) ran the search to its grid limits and lifted BOTH arms overhead.
- **Two real bugs found and fixed on the way (kept):**
  - `WeaponAim` rotations pivot about the hand bone, not the weapon origin, so a weapon that is not
    mounted at its origin still rotates about the grip. The attack aim deliberately still pivots
    about `weapon.position` (the stock mount puts the origin at the hand) so strike behaviour is
    unchanged.
  - `TwoHandGrip` can aim the PALM at the haft instead of the wrist bone (`offHandPalmLocal`,
    default zero = old behaviour). With it on, the right palm sat 0.006 from the haft instead of
    0.15 - the IK goal is the hand bone, which is at the wrist, so the shaft ran through the side
    of the hand and the baked fist gripped air.
- **The carry pass** (`WeaponAim.carryPose` + `carryAlongHands` + `carryExtraTilt` /
  `carryForwardTilt` / `alignWrists`) is in the code but OFF in the scene. It holds the haft on a
  guard line while not attacking, because the weapon is parented to the wrist and the Mixamo
  carry's wrist ROTATES through the cycle (measured: head 0.19 behind the body, butt 0.78 in front
  of the hip). `carryAlongHands` must read the off hand as the CLIP poses it
  (`TwoHandGrip.PreIkOffHand`): reading the settled hand feeds the grip IK back into the thing that
  moves it, and at a 0.76-unit spread that loop walked both hands onto one side of the body.
  `GetIKPosition` inside `OnAnimatorIK` returns last frame's GOAL, not the animated pose - read the
  bone.
- **Reverted to:** Bjorn's idle/walks/dashes back on `AxePack2H`, `AxeGuard/` clips deleted, weapon
  mount back to the FBX values (localPos (0.0001, 0.0005, 0), euler (0, 342.9, 0)), `LeftGrip`
  socket back to 50%, `WeaponSpec` leftGripFrac 0.50, `WeaponMount` table emptied, carry and wrist
  alignment off. Verified after the revert: Bjorn_Idle plays, light slash lands 8 dmg at 2.45 units.

### 2026-10-17 — two-week plan agreed

- `Design/Plan_2Weeks.md` is the plan for 2026-10-17 → 2026-10-31, in the user's order:
  video-driven animation, specials, combos, dash counter (0.8 s open window), parry, low
  attacks/trips, low block, two grabs each. Deltas from the user's order: the dash-counter
  number moves to day 1, an attack router + input buffer goes in before the specials, and
  parry is built in the same pass as the dash counter (shared counter plumbing).
- Specials scope for the fortnight (user's choice): **4 per character** (three `L1 + button`
  defaults + one directional), not the 15 in `Character_Movesets.md`.
- Animation lane: the user films himself (T-pose hold, then the move, white wall, 3/4 angle)
  and the capture is converted to clips — MediaPipe Pose in a separate Python 3.12 process
  (Blender's bundled Python is 3.13, no mediapipe wheel) → landmarks JSON → the existing IK
  controls in the anim masters → `bake_and_export()`. Rokoko Video free tier is the fallback.
  The first test clip is the gate for the whole lane.
- Placeholders found that this plan wires up: `BalanceConfig.PARRY_WINDOW` /
  `PARRY_STUN_DURATION` / `PARRY_COUNTER_WINDOW` / `SUPER_GAIN_PARRY` and the `AttackHeight`
  enum on `AttackData` all exist and are referenced by nothing yet.
- **Design call from the user (2026-10-17):** the L2 "low/trip" family is **strikes**, so the
  tackle belongs there (one clip + a `Knockdown` reaction, nothing held) and moved out of
  GRAPPLING in `Design/Character_Movesets.md`. A **grab** is a real grab: connect → both
  fighters HELD (paired animation, mash to break) → branch into **strikes** (□/✕, up to 3) or a
  **takedown** (△/↓, knockdown). The "2 grabs per character" are those two branches off one
  connect, so per character it is one connect clip + a hold loop + two finishes + the victim
  side. Bjorn: "Beard Hook" → Butt Ring Hammer / Hook and Haul. Miyamoto: "Kumi-uchi" →
  Tsuka-ate / Kote Gaeshi. The defensive counter-throws are listed as a later pass.
- **Day 1 done:** `DODGED_RECOVERY_FRAMES` 18 → **48 (0.8 s)**, with
  `CombatSystem.dodgedRecoveryFrames` and `AttackData.dodgedRecoveryFrames` as overrides
  (-1 = fall through to the next level); the dodged log now prints the seconds. Compiles clean.
- **Mocap toolchain installed:** Python 3.12.10 at
  `%LOCALAPPDATA%\Programs\Python\Python312\python.exe` (winget, user scope) with mediapipe
  1.1.1, opencv-python 5.0.0, numpy 2.5.4. **mediapipe 1.x has no `mp.solutions`** — it is the
  Tasks API only, so `ArtSource/Mocap/models/pose_landmarker_heavy.task` (30 MB, from the
  mediapipe-models bucket) is required and git-ignored by folder.
  `ArtSource/Mocap/pose_extract.py <video> <out.json>` (run with `-I`) writes per-frame world
  landmarks plus a calibration block: it finds the held T-pose by scoring arms-level-and-spread,
  averages that hold, and reports the performer's limb lengths and shoulder-line yaw. Videos go
  in `ArtSource/Mocap/raw/`, JSON in `ArtSource/Mocap/landmarks/` (both git-ignored).
  Smoke-tested on a 30 fps video built from `unity_play_1.png`: pose found in 10/10 frames.
- **Mocap pipeline built and smoke-tested end to end (2026-10-17).** One command:
  `ArtSource/Mocap/video_to_clip.sh <video> <ClipName> <Character> [--frames N] [--trim A:B]`
  → `Assets/_project/Animation/Clips/Custom/<ClipName>.fbx` + a filmstrip in
  `ArtSource/Characters/Renders/mocap/`. Recording recipe and the flag list in
  `ArtSource/Mocap/README.md`.
  - `ArtSource/Blender/video_mocap.py` keys the landmarks onto `<Char>_AnimMaster.blend`. It
    drives limbs by **direction**, taking the length from the rig (`rig_limb * performer
    extension fraction`), so IK targets are always reachable and nothing skates. Keys hip height
    + body yaw, spine, neck, head and the IK hands/feet; hand and foot ROTATION stay at rest
    (single-camera capture has no reliable wrist orientation — the weapon is aimed afterwards).
    Clips are in place: MediaPipe world landmarks are hip-origin, which suits this project.
  - It lifts the `Rig` helper class out of `author_clips.py` at run time (slices the source
    between `def action_fcurves` and `# ---- clip registry`) instead of copying it, so the pose
    conventions have one source of truth.
  - `bake_and_export` begins with `bpy.ops.object.select_all`, whose poll needs OBJECT mode, so
    switch out of POSE mode before calling it.
  - **Legs gate:** MediaPipe invents joints that are out of frame rather than failing, and an
    invented leg folds the fighter up. `pose_extract.py` reports tracking per body group and
    warns under 0.5; `video_mocap.py --legs auto` (default) then leaves the feet in the rest
    stance. Confirmed on a deliberately bad input: body stays standing, legs intact.
  - Axis convention confirmed against the data, not assumed: MediaPipe world landmarks have
    **y DOWN**, same sign as the image landmarks (checked nose vs hip in both).
- **Scope change (user, 2026-10-17, same day):** the three `L1 + button` specials per character
  (Haft Strike / Hewing Charge / Blood Eagle and the Miyamoto equivalents) are **parked** — they
  were never part of the user's plan. Marked PARKED in `Design/Character_Movesets.md`, dropped
  from `Design/Plan_2Weeks.md`. In their place: one captured signature strike on the existing
  **heavy** button (J/K/L = Light/Medium/Heavy on the keyboard), Bjorn's being `Mountain Breaker`.
  Open decision noted in the plan: its authored frame data (22/5/26, 22 dmg, unblockable) is a
  special's, which is a lot for a plain heavy (current heavy 15/5/15 for 16).
- **First capture set being filmed:** `bjorn_light.mp4` (J), `bjorn_heavy.mp4` (Mountain Breaker,
  L) and `bjorn_combo.mp4` (the two chained in one continuous action, no reset to guard). The
  combo take is the reference for the cancel-window transition — it becomes either a dedicated
  `Bjorn_Heavy_FromLight` clip or the blend timing out of the light's recovery.
- **Capture workflow for repeats from several angles (2026-10-17):** the user films the T-pose
  once in its own video, then one video per move with the move repeated a few times from
  different angles, slowly. Pipeline changes for that:
  - `video_to_clip.sh --tpose <video>` extracts once to `landmarks/_tpose.json`, and every later
    clip passes it as `--calib` automatically. Only LIMB LENGTHS come from it (rotation
    invariant), so the angle the T-pose was filmed at does not matter.
  - `pose_extract.py` segments the video into **takes** by hand speed and prints
    `--trim START:END` for each; `video_to_clip.sh … --reuse` re-keys from the existing
    landmarks so trying another take costs no MediaPipe pass.
  - **Facing now comes from the take, not the calibration:** `video_mocap.py --face take`
    (default) averages the HIP line over the opening frames of the trimmed take (hips stay
    square; shoulders are bladed in a guard) and that is also the zero of the body yaw. So each
    repetition can face any direction and still exports facing +X — but a take must start from
    the idle, not mid-swing. `--face tpose` is the old behaviour.
- **First real capture (2026-10-17, user's four videos in `ArtSource/Mocap/raw/`):** `T_pose.mov`
  tracked 103/103 frames (arms 0.99, torso 1.00, legs 0.94, 3.4 s hold); `bjorn_light.mp4.mov`
  1193 frames, 100% detected, arms 0.91 / legs 0.87, **7 takes** auto-found. Take 3
  (`--trim 501:546`, a 3/4 angle) is the one used. Working command:
  `video_to_clip.sh raw/bjorn_light.mp4.mov Bjorn_Light_Slash_Mocap Bjorn --frames 17
  --trim 501:546 --grip rl --reuse`.
- **Weapon swing solved from the two-hand line (new in `video_mocap.py`).** One camera gives no
  wrist orientation, so the first clip had Bjorn *carrying* the axe level through the swing. On a
  two-handed grip the shaft IS the line between the wrists, and both are tracked: `R_Hand` has a
  COPY_ROTATION from `IK_R_Hand` and the weapon is bone-parented to `R_Hand`, so putting
  `shaft_rest.rotation_difference(L_wrist - R_wrist)` on that control swings it. `rotation_
  difference` is the minimal arc, so the authored edge alignment is left alone (twist about the
  shaft is not knowable from one camera).
  **`--grip rl` for this user:** he grips right-hand-nearest-the-head, the opposite of the rig's
  own layout (left grip 61% up the haft), so the default `lr` pointed the axe backwards.
- **Captures export as `<Clip>_Mocap.fbx`**, not over the hand-keyed clip of the same name - the
  first run overwrote `Bjorn_Light_Slash.fbx` (restored from git). The attack states point at the
  Mixamo clips anyway, so a capture only reaches the game when a controller state is re-pointed.

### 2026-10-18 — mocap clips in game: spine doubling, trim = swing only

- **The captures are wired into Bjorn's controller**: `Bjorn_Light_Slash` -> `Bjorn_Light_Slash_Mocap.fbx`,
  `Bjorn_Heavy_OverheadDoubleAxe` -> `Bjorn_Heavy_Mocap_t6.fbx`, both at **state speed 1.0** (the
  clips are keyed to the AttackData frame count, so they need no fitting; Mixamo clips run x1.8).
  This is a manual override - running `Crucible > Animation > Use Mixamo packs` puts the Mixamo
  swings back. Light lands 8 dmg, heavy 14, so the hit path is unaffected.
- **Mocap FBX files import as Generic with no avatar** unless told otherwise: set
  `animationType = Human`, `avatarSetup = CreateFromThisModel`, then run
  `Crucible > Debug > Fix clip human bone maps` for the explicit 22-bone map. Without this a
  capture never retargets.
- **Spine lean was applied TWICE** (the first-pass clips' "hunchback" look): `Rig.spine()` keys
  Spine01 AND Spine02 and `video_mocap.py` passed each the full measured torso angle, so the
  40 deg clamp meant up to 80 deg of fold; `Rig.neck()` has the same two-bone shape. Fixed with
  `SPINE_BONES` / `NECK_BONES` divisors. Lean/side are now also measured RELATIVE to the
  performer's own standing attitude over the take's opening frames (`POSTURE_KEEP` 0.35), since
  the capture is filmed in a living room, not in a guard.
- **A trim must be the SWING, nothing else.** The light take was trimmed 501:546 but the cut is
  only frames 502-513 - the other 33 frames are the performer settling, and retiming stretched
  that pause across the attack window: in game the right hand dropped to (0.09, 1.48) and sat
  there from t=0.25 to 0.63. Re-trimmed 501:515 the hand now travels
  (0.73, 2.11, 0.56) -> (0.04, 1.48, 0.28) monotonically across the clip. Find the window by
  wrist speed in the landmarks JSON (`frames[i].world[15/16]`, per-frame distance): the core is
  where speed >= ~30% of peak, and anything flat on either side is not the move. The heavy take's
  chop is frames 557-566 of the 534:594 trim (tight trim 552:572 NOT yet re-keyed).
- **Unity's Humanoid muscle clamps straighten extreme spine bends**, so a capture reads more
  folded in the Blender filmstrip than in game (measured: head 0.91 above the hips in the swing,
  same as the idle). Judge a clip in play mode, not from the filmstrip.
- **Stepping a clip for inspection**: in play, `dts.enabled = false`, `Time.timeScale = 0`,
  `animator.speed = 0`, then `Play(state, 0, t)` + two `Update(0f)` calls and capture. At the
  normal 0.85 time scale an attack finishes inside one CLI round-trip, so frame-by-frame checks
  are impossible otherwise.
- (2026-10-18, heavy pass) `Bjorn_Heavy_Mocap_t6` re-keyed from the tight window
  (`--trim 552:572`, 21 source frames -> 35): the axe now goes properly overhead (axe head reaches
  y 3.7 in fighter space, hand y 2.4 -> 2.6) and comes down in front to y 1.9 / z 1.1. Lands 14 dmg
  at 2.45 units. Two things it still shows: the last third of the clip is STATIC (the take's
  follow-through was outside the trim, so the recovery frames hold the end pose), and the blade
  bottoms out at **68%** of the clip while the authored active window is 43-57% (frames 15-20 of
  35) - it only lands because `WEAPON_ARM_PAD_FRAMES` (5) pads the arming window. To put the
  contact inside the window, shift the trim ~3 source frames later (`552:572` -> `554:575`) rather
  than re-cutting the user's frame data.
- **Attack blend-in was 0.030 s on every Any State -> attack transition**, which snaps from the
  guard (axe across the belly, left foot lead) to a captured first frame with the axe already up:
  that is the "awkward start" the user reported. Now fixed-duration per weight on both
  controllers: Light 0.06 s (only 6 startup frames to hide it in), Medium/Heavy 0.12 s. Nothing in
  `CrucibleMixamoSetup` touches transitions, so these survive a re-import; they live in the
  controller assets.
- **Filming note for the capture lane:** start each take from the GUARD, not a neutral stand - then
  the clip's first frame already matches the idle and there is no pose jump to hide.
- **Clips can and should start from the performer's GUARD** (user's question, 2026-10-18). Two
  pieces make it work, both already in the pipeline:
  - The trim starts inside the guard HOLD, not at the first motion. Find it in the landmarks by
    wrist height above the hips: Bjorn's heavy take holds dead still at R +0.34 / L +0.05 for
    frames 512-526, the raise starts at 527, the chop is 561-566 and it settles by 572.
  - `--contact-at <clip frame>` (already in `video_mocap.py`) then bends the retime around the
    weapon-TIP speed peak instead of resampling uniformly: pre-contact source is squeezed into
    [0, contact], post-contact spread over the rest. Without it a guard-start trim puts 70% of the
    clip in the wind-up and the blade lands late.
  - `Bjorn_Heavy_Mocap_t6` is now `--trim 523:585 --contact-at 17.5` (heavy is 15/5/15, so contact
    = startup + active/2): 63 source frames -> 35, contact at clip frame 18, inside the authored
    active window. Still HITs for 14 at 2.45 units.
  - **Cost to know when filming:** his raise-and-hold was 34 source frames against a 5-frame chop,
    so the wind-up is compressed ~2.5x on playback. Perform the wind-up in rough proportion to the
    strike and do not hold at the top, and the retime barely distorts.
  - The remaining start mismatch is `Bjorn_Idle` (Mixamo carry, hands at belly height, R(0.05, 1.48,
    0.42)) vs the captured guard (R(0.38, 1.93, 0.62)). It closes when the IDLE is captured too.

### 2026-10-18 — the combo capture: light -> 360 overhead

- **Design (user, 2026-10-18):** the combo is the light attack followed by a **360 overhead**
  INSTEAD of recovering, if Heavy (L) is pressed before the light's animation finishes. So the
  follow-up is a spinning overhead, not the standalone chop, and it needs its own clip.
- **The combo take holds it all** (`bjorn_combo.mp4.mov`, take 5). Measured from the landmarks
  (blade tip = R wrist + (R-L) normalised x 0.70, the same construction `video_mocap.py` uses):
  | source frames | what |
  |---|---|
  | 459-461 | strike 1, tip speed 1.21/1.23 per frame, tip +0.57 -> +0.04 (the light cut) |
  | 450-467 | the SPIN - hips sweep +252 deg, overlapping the light's follow-through |
  | 469-479 | hold at the top (11 frames) |
  | 483-484 | strike 2, tip 1.02/1.05, +1.18 -> -0.77 (the overhead) |
  The two blade contacts are 0.78 s apart, but 11 of those frames are the hold; strip it and the
  natural chain is ~0.42 s - which is what the engine already does with NO cancel system (the
  light's 8 remaining frames + the heavy's 18 frames to contact). **The cancel window is not
  there to make combos faster, it is there so the follow-up can start before the light's recovery
  ends so the two read as one action.**
- **Two bugs the spin exposed:**
  - `video_mocap.py` wrapped body yaw into (-180, 180] every frame, so a 252 deg sweep became
    -108 deg and would have spun him the wrong way. Now accumulated frame to frame (`prev_raw_yaw`).
  - The clip importer deletes rotation: `keepOriginalOrientation: false` (= Root Transform
    Rotation *Based Upon: Body Orientation*, the project default and correct for cuts and walks)
    re-zeroes the body facing every frame. The first spin export measured **-10 deg** of rotation.
    A spinning clip needs **Bake Into Pose + Based Upon Original**
    (`lockRootRotation = keepOriginalOrientation = true`), same as the Mixamo spin. After that the
    clip carries **234 deg** in game, concentrated in the first 40%.
- **Clips cut (both from the combo take, so the chain is one continuous performance):**
  `Bjorn_Light_FromCombo` (`--trim 437:463 --frames 16 --contact-at 7.5`, contact at clip frame 8)
  and `Bjorn_Heavy_FromLight` (`--trim 448:492 --frames 35 --contact-at 17.5`, contact at 18,
  carries the spin). Both are states on `P1_Bjorn` with **no transitions** - unreachable until the
  cancel router exists.
- **Splicing across two videos does not work:** `Bjorn_Heavy_FromLight` against the standalone
  light's end pose is a 0.61/0.79 hand gap, against the combo's own light 0.38/0.27. Measured per
  light frame, the follow-up's first frame fits the light at **frame 8-10 of 16** best
  (R 0.11 at frame 8, growing to 0.38 by frame 16) - i.e. the cancel wants to fire at or just
  after the light's CONTACT, which is also what the design says.
- **Cancel window this implies: light frames 8-12 of 16** (contact to mid-recovery), tightest at 8.

### 2026-10-18 — combo strings the standard way (MK-style), first one working

- User asked for it "the recommended way, like MK1": authored STRINGS with per-move cancel windows
  and an input buffer, not a hardcoded light->heavy transition.
- **`AttackData`**: `ComboLink[] comboLinks` (button -> follow-up `AttackData` -> requirement),
  `cancelStartFrame` / `cancelEndFrame` (-1 = first active frame / end of recovery), and
  `CancelRequirement` = `Always` (MK strings continue on a whiff) | `HitOrBlock` | `HitOnly`.
  `FindLink(AttackType)` / `HasComboLinks` / `CancelStartFrame` / `CancelEndFrame` are the API.
  The follow-up being a full AttackData means it carries its own frame data, hitbox, aim mode and
  animator trigger AND can define links of its own - that is what gives chains of any length.
- **`CombatSystem`**: `BeginAttack(attack, depth)` extracted from `TryAttack` so a cancel starts a
  move exactly like a fresh press (stamina, dash-counter, chain bookkeeping, trigger).
  `TryCancelIntoFollowUp()` runs at the top of the phase advance: inside the window, with the
  requirement met and `stringDepth + 1 < MAX_STRING_LENGTH`, it `EndAttack("CANCELLED into the
  string")` and begins the follow-up, dropping the rest of the recovery. `attackTouched` /
  `attackHitClean` are set where damage is applied, for the earned links.
- **Input buffer**: a mid-attack press is buffered (`INPUT_BUFFER_FRAMES` 8) instead of being
  dropped, and comes out on the first legal frame. For a press that IS a link the hold extends to
  the end of the cancel window (dial-a-combo) - otherwise tapping the follow-up straight after the
  opener expires a frame or two before the window opens, which is how everyone actually plays.
- **`BalanceConfig`**: `INPUT_BUFFER_FRAMES = 8`, `MAX_STRING_LENGTH = 3`.
- **Bjorn's first string**: `Bjorn_Light_Slash` (6/3/7) links Heavy -> **`Bjorn_Heavy_SpinOverhead`**
  (new asset: 15/5/15, 16 dmg, trigger `HeavyFromLight`, **aimMode Off** - a spin must not be
  aim-corrected), window **frames 8-14**, requires Always. The follow-up is deliberately NOT in the
  fighter's `attacks` array, so a fresh L press still gives the normal heavy.
  Controller: `HeavyFromLight` trigger + `Bjorn_Light_Slash -> Bjorn_Heavy_FromLight`, **no exit
  time**, fixed 0.08 s blend (the cancel decides the timing, not the clip), and the follow-up
  returns to `Bjorn_Idle` at exit 0.9.
- **Verified in play at 8% speed**: `Slash HIT 8` -> `Slash -> Spinning Overhead (cancelled on
  frame 8, window 8-14)` -> `Spinning Overhead HIT 14`, 22 damage from one dialled L->H. At the
  Editor's ~4 fps under the CLI the OPENER whiffs (its 3 active frames fall between rendered
  frames) - test strings at `DebugTimeScale.timeScale = 0.08`, set through the component's public
  `timeScale` field (writing every float field on it does not work; `Apply()` re-asserts it).
- Strings never trip the anti-spam lockout: each link is a different `AttackType`, which resets the
  chain by the existing rule.
- Not done yet: Miyamoto has no string data (no captures for him), the AI does not know about
  strings (it presses buttons, so it can fire them by accident but never on purpose), and no UI
  shows a string is available.

### 2026-10-18 — back to the Mixamo swings; the combo built from Mixamo clips

- User's call: keep the Mixamo attacks for now (the captures stay in the project, unused), and make
  the combo **Mixamo light slash -> the same spinning clip as Miyamoto's heavy**.
- `Crucible > Animation > Use Mixamo packs` re-pointed Bjorn's light/medium/heavy, so the mocap
  overrides of the 2026-10-18 entry above are no longer live. `Bjorn_Light_Slash_Mocap`,
  `Bjorn_Heavy_Mocap_t6`, `Bjorn_Light_FromCombo`, `Bjorn_Heavy_FromLight` and `Bjorn_Idle_Mocap`
  are all still in `Animation/Clips/Custom` and committed; re-point a state to bring one back.
- **The string's ender is now a map entry, not a manual override.** The follow-up STATE was renamed
  `Bjorn_Heavy_FromLight` -> **`Bjorn_Heavy_SpinOverhead`** to match its AttackData, because
  `CrucibleMixamoSetup` keys everything off *state name == AttackData asset name*
  (`ComputeTrims` / `Wire` both look the attack up by the state's name). So the combo now survives
  `Use Mixamo packs` instead of being wiped by it - that was one of the listed gaps.
  New P1Map entry + `swing_contacts.json` entry, both pointing at
  `GreatSwordPack/great sword high spin attack` with contact 36.2 at speed 2.0, the same numbers
  Miyamoto's heavy uses (`ImportAll` groups by file, so one FBX carries a trim per state name).
- **`Bjorn_Heavy_SpinOverhead` frame data 15/5/15 -> 30/4/14** (16 dmg, aim Off). It has to match
  Miyamoto's heavy for the shared clip to fit: the contact maths cuts the window as
  `start = contact - hitFrac * speed * total / 2`, so at 35 frames the window opens at source
  frame 18.7 and eats most of the spin's wind-up, while at 48 frames it opens at 4.2 and the whole
  360 plays before the cut. Cost: ~0.5 s from the cancel to the ender's contact (the filmed combo
  was 0.42 s stripped of its hold), and the ender is punishable on a whiff.
- **Verified in play at 8% speed, 2.45 units:** `Slash HIT 8` -> `cancelled on frame 8, window 8-14`
  -> `Spinning Overhead HIT 14`. Spin measured in game (hip yaw in fighter space, unwrapped):
  35 -> -1 -> -80 -> -221 -> -298 -> -344 -> -313, a full turn with ~12 deg residual that the
  exit blend absorbs. Fitting log: `window 4.2-52.2 for 48 f (hit at 67%)`, clip 1.60s at speed x2.00.
- Also added to `.gitignore`: `ArtSource/Mocap/models/` (the 30 MB MediaPipe .task, which the notes
  already claimed was ignored but was not) and `Server/cf.prev.log`.

### 2026-10-18 — Miyamoto's three combos (and a cancel-ordering bug)

- User's design (P2 keys: `,` Light, `.` Medium, `/` Heavy): combo 1 `,`->`.`, combo 2 `.`->`,`,
  combo 3 `.`->`/`. Combos 2 and 3 are **two branches off the same move**, which is what the
  `ComboLink[]` array is for - the medium reads both buttons and gives a different ender for each.
- **Pure data, no new content.** The follow-ups are Miyamoto's EXISTING moves, so they reuse the
  `LightAttack` / `MediumAttack` / `HeavyAttack` triggers and the Any State transitions already in
  the controller. Links live on `Miyamoto_Light_Thrust` (Medium -> Side Slash) and
  `Miyamoto_Medium_SideSlash` (Light -> Mid Thrust, Heavy -> Overhead Slash), all
  `CancelRequirement.Always`, windows left at -1 (the defaults).
- **Bug the second combo would have exposed: a cancel could delete the opener's own hit.**
  `TryCancelIntoFollowUp` runs at the top of the phase advance, BEFORE the arming and
  hit-resolution code further down the same `Update`. So a window that opens during the opener's
  active frames cancelled the swing before its hitbox resolved - press two buttons, only the
  second one damages. Bjorn's authored window (frames 8-14) started at his measured contact, so it
  happened to work. Fixed in two places:
  - `TryCancelIntoFollowUp` refuses while `currentAttackTime < startup + active` and the swing has
    not touched anything (`attackTouched` / `swingEvaded` both false). It fires IMMEDIATELY once
    the blade connects, so hit-confirm cancels still work, and a whiff just waits for the active
    frames to pass.
  - `AttackData.CancelStartFrame` default (-1) is now `startupFrames + activeFrames` (the first
    recovery frame) instead of `startupFrames`.
- Strings chain both ways (`,`->`.`->`,` and `.`->`,`->`.` are both legal), capped at 3 hits by
  `MAX_STRING_LENGTH`. Open question for the user: whether a string should be allowed to double
  back on itself.
- **Verified in play at 8% speed, 2.2 units:** all three combos land BOTH hits, opener first -
  `,`->`.` 8+8, `.`->`,` 8+8, `.`->`/` 8+10 (cancels on frames 10 / 14 / 14).
- **Mocap verdict from the user (2026-10-18): the video animations "wasn't the best for now"** - the
  Mixamo swings stay. The capture lane and its clips remain in the repo for later.
- **Next (user): the parrying system.**

### 2026-10-18 — parry (built, block path verified, the window itself NOT yet verified)

- Wires up the four constants that existed and were referenced by nothing. **The rule:** a strike is
  PARRIED when the guard went UP within `parryWindow` (0.15 s) before it lands - `Fighter` records the
  press EDGE (`blockPressedTime`, set in `HandleBlock` from a false->true `blockHeld`), so timing the
  press beats holding the guard. Checked in `CombatSystem.ResolveHit` BEFORE the block branch, because
  a parry is also a block as far as `Fighter.isBlocking` goes.
- **On a parry:** no damage, no stamina cost; the attacker's swing is `EndAttack("PARRIED")` and they
  eat `parryStunDuration` (1.0 s = 60 f) of hitstun via `Fighter.NotifyParried` (so no attacking,
  dashing or walking - the free punish); the parrier gets `parryCounterWindow` (0.5 s) in which their
  next attack deals **x1.5** (`PARRY_COUNTER_DAMAGE_MULTIPLIER`, new constant - the plan referenced a
  multiplier but never defined one; it beats the dash counter's x1.35 because a 0.15 s press is harder
  than a dodge) and **+15 super** via the existing `FighterStats.AddSuperBar`.
- **All five numbers are serialized on `Fighter`** under a Parry header, as the plan asked:
  `parryWindow`, `parryStunDuration`, `parryCounterWindow`, `parryCounterDamageMultiplier`,
  `parrySuperGain`. `CombatSystem` gained `currentAttackIsParryCounter` beside the dash-counter flag,
  consumed in `BeginAttack` through `Fighter.ConsumeParryCounter()`.
- **No new clips needed:** the controllers already route `GetHit` -> `Block_Impact` while `Blocking`
  is true, so `NotifyParry` playing the straight hit animation reads as a guard jolt; the attacker
  gets the straight react as a stagger. Dedicated `Parry` / `Parry_Success` clips are still wanted.
  HUD: `PARRY!` (pale blue) over the clash slot and `PARRY COUNTER` beside `COUNTER READY`.
- **`Scripts/debug/InputHold.cs` (new, test-only).** `InputManager` CONSUMES the AI snapshot every
  frame (`if (ai2) aiInput2 = new InputData();`) because the AI rewrites it each frame, so a single
  `SetAiInput` from the console holds a button for ONE frame - that is why the first two parry test
  runs showed `blocking=False` and full damage on a supposedly held guard. `InputHold.Hold(player,
  block, seconds, delay)` re-asserts the snapshot every frame, and `delay` schedules the press in
  GAME time, which a console round trip (~1 s real = 0.08 s game at 8% speed) is far too slow to do:
  Bjorn's light lands 0.100-0.150 s after the press, so the guard must go up around 0.08 s.
- **Testing traps found (both cost a full run):**
  - **Never recompile while the Editor is in play mode.** The domain reload wiped
    `InputManager.Instance` (a static) while play continued, so every input override silently did
    nothing and the fighters kept taking unblocked hits. Symptom to look for:
    `InputManager.Instance == null` in an eval. Stop play, recompile, then play.
  - An `eval` that throws returns NO result line; re-run it without the `grep` to see
    "Runtime Error ... Object reference not set".
- **Verified:** the block path through `InputHold` - a guard held well before the swing gives
  `4 dmg [BLOCKED -50%, cost 5 stamina]`, i.e. an ordinary block, which is the correct NEGATIVE case
  (the press was >0.15 s before the hit).
- **NOT yet verified: the parry itself.** The scheduled-press run
  (`scratchpad/parry_test3.sh`: swing + `Hold(2, true, 0.30f, 0.08f)` in one eval) was killed by the
  system for low memory before printing anything. Re-run that script to confirm PARRY, the attacker's
  60 f stun, and the x1.5 counter.

### 2026-10-18 — damage -30%, parry counter 1.35, and the arenas were NOT fenced

- **All attack damage cut 30%** (user's call): Bjorn 10/13/16 -> **7/9/11** (light/medium/heavy),
  `Bjorn_Heavy_SpinOverhead` 16 -> 11, Miyamoto 10/10/12 -> **7/7/8**. `AttackData.damage` had
  `[Range(10, 50)]`, which would have silently clamped the new light values back up to 10 on any
  Inspector edit - the range is now `[Range(1, 50)]`.
- `PARRY_COUNTER_DAMAGE_MULTIPLIER` **1.5 -> 1.35**, the same as the dash counter.
- **Combos after a parry or a dash counter already work** (user's question). A counter attack is an
  ordinary attack, so its `comboLinks` fire as usual, and the timing fits: a parry stuns the
  attacker 1.0 s and a dodged attacker eats `DODGED_RECOVERY_FRAMES` (0.8 s), while a two-hit
  string reaches its second contact in ~0.7 s. **Only the first hit carries the counter
  multiplier** - `BeginAttack` consumes the flag through `ConsumeDashCounter` /
  `ConsumeParryCounter`, so the string's follow-ups are normal damage (standard counter-hit
  behaviour, and carrying x1.35 across three hits would give back much of the 30% cut).
- **The arenas were not fenced at all.** Two independent faults, both fixed:
  1. **Bound walls were 7119x oversized.** `AddBoundsRing` / `AddBoundsRect` set the `Bounds`
     child's `localScale = Vector3.one` under an arena root scaled ~7119x (the Tripo FBX is tiny),
     so wall sizes authored in WORLD units were multiplied by that: each "thin wall" measured
     3578 x 57250 x 43500 and BOTH fighters stood inside all 28 of them. Fixed with
     `InverseScale(parent)` (the same divide-the-scale-back-out pattern `HitDetector` uses).
     NOTE: fixing the scale alone collapses the ring, because each wall's localPosition was
     authored under the old scale - the ring must be REBUILT (`AddBoundsRing`) after the fix.
  2. **Wall colliders can never contain these fighters.** Each fighter's Rigidbody constraints are
     `122` = freeze rotation XYZ **plus freeze position X and Z**; movement is written straight to
     `transform.position`. PhysX cannot depenetrate a frozen axis, so no wall, however well sized,
     would stop them. Only Y is physics-driven, which is why the floor works.
- **`Scripts/core/ArenaBounds.cs` (new)** is therefore the real fence: a Circle or Rect shape that
  `Fighter.ClampToArena()` applies at the END of `Update`, after every move (walk, dash, lunge,
  knockback), zeroing dash/knockback velocity at the edge. `ArenaBounds.Instance` is set in
  `OnEnable`, so whichever arena is active provides the shape. Both placement paths in
  `CrucibleTripoArenas` now call `SetArenaBounds` alongside the wall ring; because the editor
  asmdef cannot reference `Assembly-CSharp`, it reaches the component by
  `Type.GetType("ArenaBounds, Assembly-CSharp")` + `SerializedObject`, like `RuntimeType` elsewhere.
- Scene now holds: `MedievalFortress` (ACTIVE, circle r 23.55), `DesertArena` (circle r 24.10),
  `Arena_AlQuds` (rect 29 x 19). **`ForestClearing` still has none** - it gets them when placed
  through `Crucible > Arena > Tripo`, since its floor radius comes from that placement maths.
- **Verified in play:** fighters teleported to r=100 and r=60 were both pulled back to exactly
  r=22.95 (23.55 - 0.6 body radius), logging `held inside the arena edge`.

### 2026-10-18 - parry gets its own button, its own animation, and a real cost

- **User's complaint:** sharing the block button made parrying free - "if you pressed early you still
  block", so there was never a reason not to try it, and it was too easy.
- **The parry is now its own committed action on the `Defense` button**, which was already bound and
  rebindable (`;` P1, `]` P2, **Circle** on the pad) and already read as a one-frame PRESS by
  `InputManager`. A tap opens `parryWindow` (0.15 s) of active window; a strike landing in it is
  parried exactly as before (no damage, no stamina, attacker stunned 1.0 s, 0.5 s counter window at
  x1.35, +15 super). A tap that catches NOTHING leaves `parryRecoveryFrames` (**20 f = 0.33 s**, new
  serialized field on Fighter) in which the fighter cannot block, dash or attack. A successful parry
  skips the recovery entirely - being instantly actionable IS the reward.
- Blocking no longer has anything to do with it: `blockPressedTime` / `wasBlockHeld` are gone,
  `ParryReady` is just `parryActiveTimer > 0`, and `isBlocking` now also requires `!IsParrying`.
  `HandleParry()` runs in `Update` immediately BEFORE `HandleBlock()` so a press refuses the guard
  the same frame. Gates added: `CombatSystem.TryAttack` refuses while `fighter.IsParrying`
  ("BLOCKED - parry, X s of recovery left"), and `HandleDash` returns early.
- **Its own animation.** New `Parry` / `ParrySuccess` triggers + `Parry` / `Parry_Success` states on
  both controllers (Any State -> state, fixed 0.05 s blend, no exit time, back to `<Name>_Idle` at
  exit 0.85). `Fighter` discovers the parameters like the others (`animatorHasParry`) and falls back
  to the old guard-jolt `PlayHitAnimation` when a controller lacks them. Clips, via new map entries
  in `CrucibleMixamoSetup` so `Use Mixamo packs` REPRODUCES them instead of wiping them:
  `Parry` = `GreatSwordPack/great sword blocking` (the 0.50 s guard-RAISE, which reads as a deflect
  push - it was rejected as a block HOLD for exactly the reason it suits a parry) and
  `Parry_Success` = `AxePack/standing block react large`. New `ParryDuration` (0.40 s) fitting rule
  for any state whose name starts with `Parry` (x1.25 and x3.33 respectively).
- **Touch pad gained a PARRY button** next to BLOCK (`TouchControls`, `Consume(p.parry)` -> a tap,
  never a hold), so the phone build can parry. Help screen explains the mechanic and both key
  summaries now print the parry key.
- **`InputHold.Parry(player, delay)`** (test-only) taps the Defense button for 0.05 s of game time
  after a scheduled game-time delay.
- **VERIFIED in play at 8% speed - the parry branch has now actually executed** (it never had
  before): `P2: PARRY - counter window 0.50s, +15 super`, `P1: hitstun for 60 frames (1.00s)`, P2
  took zero damage. Also verified: a parry tapped too early no longer falls back to a block, the
  strike lands in FULL (6 dmg, no "-50% BLOCKED") - which is the whole point of the change. And the
  whiff punish, tested deterministically (`scratchpad/parry_gate_test.sh`: force
  `parryRecoveryTimer` by reflection, then ask the gate in the SAME eval): `IsParrying=True`,
  `attack allowed=False`, logged `P2: BLOCKED - parry, 0.50s of recovery left`.
- **Two test-timing traps for the next run of these scripts** (both cost a run, neither was a code
  fault):
  - A press scheduled 0.10 s into a light swing is LATE. `WEAPON_ARM_PAD_FRAMES` (5) arms the blade
    during the startup, so the swept-blade hit can land ~0.02 s after `TryAttack`. Schedule the tap
    on the SAME frame as the swing (`Parry(2, 0f)` in the same eval).
  - The 0.33 s recovery expires inside one CLI round trip. Timing-based checks of a sub-second gate
    are not measurable this way: force the private timer by reflection and ask the gate in the SAME
    eval instead (`parry_gate_test.sh`).
- **Known gap: the AI never parries.** `AiController` only ever writes `blockHeld`, so nothing in
  any profile touches `defenseButton` - it used to parry by accident whenever its block landed in the
  window, and now it cannot at all. Insane probably should; not done.

### 2026-10-19 - parry back on the guard, parry/dash effects, combo lockout, death animation

User's call after testing the dedicated parry button: put the parry back on the **block button** with
different timing; add an effect after a successful parry and dash; stop combo and single-attack
spam; double the parry animation speed and make it editable from the component; add a death
animation. **All five are built and compile clean. NONE of it is play-verified** - the verification
run (`scratchpad/round2_test.sh`, four cases) was killed by the system for low memory before it
printed anything, and was not restarted.

- **Parry is back on the block press edge** (`blockPressedTime`, the pre-2026-10-18 rule). The
  dedicated-button version is fully reverted: no `Defense`-button path, no `Parry` attempt state, no
  `IsParrying`, no whiff recovery, no attack/dash/block gates. What replaces the risk, since an
  early press on a shared button costs nothing:
  - `parryWindow` **0.15 -> 0.12 s** (`BalanceConfig.PARRY_WINDOW`, and both scene Fighters were
    re-serialized - changing the const does NOT touch an already-serialized component).
  - **`parryRearmTime` (0.6 s, new serialized field):** one press arms ONE parry window. A press
    within the rearm still raises the guard but cannot parry (`blockPressedTime = -99`), so
    mashing block does not fish for free parries. A successful parry also burns the arm.
- **Effects.** `sparksOnParry` (new on CombatSystem, default true) - the parry spark used to ride on
  `sparksOnBlock`, which is off by design, so it never showed. `dustEffects` now defaults true and
  is on for both fighters, which is the dash dust (push-off + landing, already in `Fighter`).
- **Anti-spam, the combo case.** `BalanceConfig.STRING_LOCKOUT` (**1.5 s**, new) applies when a
  STRING finishes its last move: a combo must not be a way to swing more often than a single attack,
  which is what made Miyamoto's two-way links spammable. `ATTACK_CHAIN_LOCKOUT` **0.8 -> 1.5 s** for
  the same-attack repeat limit (the spammable J). Both are overridable per fighter via serialized
  `stringLockout` / `sameAttackLockout` on CombatSystem (0 = fall through to BalanceConfig), and the
  two take `Mathf.Max` so the longer wins. Implementation note: `EndAttack` clears `stringDepth`, so
  the depth is captured into `endedDepth` before the call.
- **Parry animation speed is editable and doubled.** `Parry_Success` is fitted to `ParryDuration`
  (0.40 s, x3.33) AND its speed multiplier now reads a new **`ParrySpeed`** float parameter on both
  controllers (default 2), which `Fighter.parryAnimSpeed` (new serialized field, default 2) sets when
  firing the trigger. Net **0.20 s** on screen; set `parryAnimSpeed` to 1 for the old speed.
  `CrucibleMixamoSetup.Wire` re-applies both the fit and the parameter, so a re-import keeps it.
- **Death animation.** `Death` trigger + state on both controllers (Any State -> Death, 0.10 s fixed
  blend, no exit - a corpse stays down), clips `GreatSwordPack/two handed sword death` (Bjorn, 2.40 s)
  and `two handed sword death (2)` (Miyamoto, 2.50 s), both map entries so they survive a re-import.
  `CombatSystem` calls `target.NotifyDeath()` **instead of** `NotifyHit` when `TakeDamage` leaves the
  target at zero, so the killing blow plays the death rather than a flinch. `Fighter.NotifyDeath`
  drops the guard, clears dash/knockback velocity and sets `IsDead`; `ResetForMatch` clears it.
  This works with the existing KO path because `GameMenu.FreezeFighters` only disables the `Fighter`
  component - the Animator keeps running, so the clip plays under the KO screen (which appears 1.6 s
  later, while the death clip is ~2.4 s).
- **Editor-script gotcha that cost a job run:** patching `CrucibleMixamoSetup` and immediately
  running `UseMixamo()` through `eval --detach` runs the STALE assembly (symptoms: `no state Parry`
  warnings and the old fitting log). Always `unity command recompile` and wait for `completed`
  before invoking an editor method you just edited.
- **Still to verify in play when memory allows** (`scratchpad/round2_test.sh`): (1) a block press
  timed into a swing parries at 0.12 s and sparks; (2) a re-press inside 0.6 s blocks instead;
  (3) a finished 2-hit string refuses the next attack for 1.5 s; (4) lethal damage enters the Death
  state with the death clip rather than a React. The AI still never parries (`AiController` writes
  `blockHeld`, so it can now parry by accident again, as it did before the dedicated button).

### 2026-10-19 - parry on its own button (cooldown, not lockout), death recoverable, louder effects

User's four items after testing the previous build. All four are done and **play-verified this time**
(the previous session's verification run never printed).

- **CRITICAL, fixed: a dead fighter stayed dead forever.** The `Death` state is deliberately a dead
  end (`Any State -> Death`, `m_Transitions: []`, so a corpse stays down), and `ResetForMatch` never
  touched the animator - so a rematch or a brand new match reset health, position and every flag but
  left the state machine lying on the floor. `Fighter.ResetAnimator()` (called from `ResetForMatch`)
  now **resets every trigger parameter** (a queued `Death` or `GetHit` would re-fire and drop the
  fighter straight back down), then `animator.Play(idleStateHash, 0, 0f)` + `Update(0f)`.
  `ResolveIdleState()` finds the state once in `Awake` from, in order, the new serialized
  `idleStateName`, `<FighterData.FighterName>_Idle`, `<asset name>_Idle`, `Idle`; if none exists it
  falls back to `animator.Rebind()`. Also: a dead fighter no longer walks or swings - `Update`
  returns early while `isDead` (zeroing dash/knockback and `MoveSpeed`) and `TryAttack` refuses.
  Verified end to end: a real killing blow -> `clip=Death`, `isDead=True`, Fighter frozen by the KO
  screen -> `StartMatch` -> `P1 clip=Bjorn_Idle`, `P2 clip=Miyamoto_Idle`, hp 100, `isDead=False`,
  feet at y 0.24 / 0.75, and both stay standing on later frames.
- **Parry moved to its own button again (user's choice), but light.** On the block press edge it fired
  by luck: pressing guard as the strike arrives IS the natural reaction, and an early press cost
  nothing, so every block was a free lottery ticket. Now it is the **Defense** button
  (`;` P1, `]` P2, **Circle** on the pad, all rebindable) and **blocking never parries anything**.
  The cost is deliberately not the old recovery lockout the user rejected: a press spends
  `parryStaminaCost` (8) and starts `parryCooldown` (1.0 s) before another attempt, and that is all -
  a parry that catches nothing leaves the fighter free to block, dash or swing at once. Attempts are
  refused while attacking, dashing or in hitstun. `parryRearmTime` / `blockPressedTime` /
  `wasBlockHeld` are gone; `ParryReady` is just `parryActiveTimer > 0`, and `ParryCooldownTimeLeft`
  is public for the HUD. Reward is unchanged (no damage, attacker stunned 1.0 s, counter window at
  x1.35, +15 super). No attempt animation: there is no commitment to show, and the controllers only
  have `Parry_Success`.
  Verified at 8% speed, 2.2 units: tap on the swing frame -> `P2: PARRY attempt - window 0.12s, 8
  stamina, 1.00s cooldown` -> `P2: PARRY` -> `P1: hitstun for 60 frames`, 0 damage. Second tap inside
  the cooldown -> `P2: no parry - 1.98s of cooldown left` and the strike lands in FULL (6). Guard
  held through a swing -> `3 dmg [BLOCKED -50%]`, never a parry.
- **Parry flash is much bigger.** `HitEffects.SpawnSparks` takes a `strength` (count scales with it,
  size/speed/lifetime with its square root) and new `SpawnParryFlash` adds a ring of slow bright
  embers where the blades met. `CombatSystem.parrySparkStrength` (serialized, 3) drives it:
  72-120 sparks instead of 24-40, plus 24-42 embers.
- **Dash dust much louder on a successful dodge.** `Fighter.NotifyEvaded` (which fires the moment a
  dash is judged to have made a strike miss, on both evade paths, guarded against a double spawn)
  throws a puff at `dodgeDustStrength` (serialized, 3) instead of nothing: 30-54 grains against the
  ordinary dash's 10-18, 85 deg spread, bigger, longer-lived and more opaque. `SpawnDust` scales the
  same way sparks do, so the push-off (1) and landing (0.8) puffs look as they did.
- Scene re-serialized for the new fields (`parryCooldown` 1, `parryStaminaCost` 8, `parryWindow` 0.12,
  `dodgeDustStrength` 3, `parrySparkStrength` 3, `dustEffects` on both fighters); the stale
  `parryRearmTime` key is gone. Help screen text rewritten for the new parry.
- **Test recipe note:** play mode starts on the MAIN MENU, where `GameMenu.FreezeFighters` disables
  every `Fighter` component and `InputManager.InputEnabled` is false - so an input-driven test
  (parry, block, walking) does nothing until the match starts, while a direct `CombatSystem.TryAttack`
  still works and lands hits. That mismatch ate the first parry run. Start the match first:
  `typeof(GameMenu).GetMethod("StartMatch", NonPublic|Instance).Invoke(menu, null)`.
- Still unverified from the previous session: the 1.5 s `STRING_LOCKOUT` after a finished combo
  (the string takes ~10 s of real time to play at the 8% test speed, which outruns the CLI round
  trip; check `attackLockoutTimer` by reflection instead). **Known gap: the AI still never parries** -
  `AiController` only ever writes `blockHeld`, and now that the parry has its own button it cannot
  even do it by accident.

### 2026-10-19 (counters checked after a user report)

User report: "parry and dash counters are no longer multiplies". **Both multipliers are in fact
working** - measured, not assumed - but one window was too short to reach in play, and that is
fixed.

- **Parry counter: working.** The user's own console shows it repeatedly:
  `P2: PARRY COUNTER - Mid Thrust will deal x1.5 damage` -> `for 9 dmg` where a normal Mid Thrust
  is 6. Note the SCENE is serialized at `parryCounterDamageMultiplier` **1.5** and
  `parryCounterWindow` **1.0 s**, not the 1.35 / 0.5 s in `BalanceConfig` - the 2026-10-18 change
  to 1.35 edited the const only, and a serialized component keeps its own value. Left as-is
  (it is a balance number the user may have chosen); worth settling deliberately.
- **Dash counter: working, but the window was unreachable.** `DASH_COUNTER_WINDOW` **0.5 -> 0.8 s**
  to match `DODGED_RECOVERY_FRAMES` (48 f = 0.8 s). The dodged ATTACKER is held open for 0.8 s
  while the DODGER had only 0.5 s to start the punish, so the window shut before the opening it
  exists for. The 2-week plan's day-1 item ("dash counter 0.8 s open window") had only moved the
  attacker's number. It is a const with no serialized override, so the change takes effect with
  no scene edit.
- **A new dash wipes the window** (`Fighter.cs`, START THE DASH zeroes `dashCounterTimer`). Correct
  as design - dashing again is choosing not to counter - but it is what made the first test read
  `counterOpen=False` right after a successful dodge.
- **Verified in play** (timescale 0.3, 2.26 units): `P2 EVADED P1's Overhead Double Axe (dashed
  right vs Overhead, judged as the blade arrived)`, `brushed P2 but was already dodged` (no
  damage), `P2: DODGE - dash counter window open`, then
  `P2: DASH COUNTER - Mid Thrust will deal x1.35 damage` -> `for 8 dmg [DASH COUNTER x1.35]`.
- **The specials were never broken either:** the user's log shows
  `P1: TryAttack(Light + SPECIAL) called` -> `no special on Light - throwing the normal instead`.
  The modifier reaches the combat system; the `specials` arrays are empty until Task 3 authors the
  assets, and the fall-through to the normal is deliberate.

**Testing lessons (all cost a run):**
- `InputHold` durations must be long enough in RENDERED frames, not game seconds. The Editor runs
  ~4 fps under the CLI, so at `timeScale` 0.08 a 0.10 s hold is ~1 frame: `InputManager` consumes
  the AI snapshot every frame, so the hold must span several frames or it is released before the
  manager ever hands it to the Fighter. `InputHold.Dash(player, dir, seconds, delay)` is new
  (x = forward/back, y = sidestep, matching `InputData.dashDirection`).
- **0.08 is too slow for a dodge test.** `Time.maximumDeltaTime` lets one rendered frame advance
  ~6 game frames, so an attack's whole startup can pass between frames and the dash never overlaps
  the arming. **timescale 0.3** resolves both, and the reliable staging is: hold the dash for
  several seconds in one eval (it re-triggers, so the fighter is dashing almost continuously), then
  swing into it from the next eval.
- A sub-second window cannot be observed across a CLI round trip: open it by reflection and attack
  in the SAME eval.
- The `eval_file` harness reports a **compile error as "Unreachable code detected"**. Two hit this
  session: `InputManager.InputEnabled` is an INSTANCE property (not static), and `FighterStats` has
  no `staminaRegenRate`. If that message appears, suspect a bad member reference, not dead code.
- Repeated dashes drift the gap (the side dash arcs), so re-place the fighters in the same eval as
  the swing or the attack starts out of reach.

### 2026-10-19 (end of session) - scope change: CRUCIBLE Lite, the mobile-first slice

User's call after testing: the game plays well **only on a PS5 pad** - keyboard and touch are not
fluid - so the next stretch is a LITE slice that can be played with a thumb, and the backlog is
re-cut around it. **`Design/Plan_LiteMobile.md` is the active plan and replaces `Plan_2Weeks.md`.
The next session starts at its Task 1.**

- **Dropped for now:** grabs, low attacks, low block, trips, the tackle. Everything authored so far
  is a HIGH attack; the `AttackHeight` enum stays on `AttackData` (read by nothing) because the user
  wants the height concept kept for the long run, so the lite version simply never produces a
  non-high attack.
- **In scope:** the three strikes, block, dash + dash counter, parry + parry counter, **6-9 combos
  per character** (Bjorn has 1 today, Miyamoto 3), **3 specials per character**, **counter-riposte
  moves with their own animations** after a successful dash or parry, health recovery on defensive
  success (+2% dodge / +2% parry / +3% counter hit, the user's numbers), the mobile build, and an AI
  that uses any of it.
- **The user's stated blocker is animations** ("i cant think of animations now"), so Task 1 is an
  inventory: ~16 unused attack clips sit in `Animation/Clips/Mixamo` (AxePack + GreatSwordPack, 98
  clips, only 20 mapped) - `combo attack ver. 1/2/3`, `slash (2)/(4)/(5)`, `slide attack`,
  `run jump attack`, two kicks each, `backhand`, `downward`, `360 low`, the plain thrust. Enough for
  6 specials, 4 ripostes and the combo enders with nothing filmed. The plan carries a first-pass
  clip-per-slot proposal to react to.
- **Seven decisions are queued at the top of the plan** with a recommendation each (special input =
  one SPECIAL button + direction; specials cost the super bar; **dash by double-tapping a
  direction** on keyboard/touch; new keyboard defaults with guard and parry on the left hand;
  health-recovery cap plus switching the hidden passive `healthRegenPerSecond = 1` off; no combo
  doubling back; Mixamo rather than filming).
- Found while planning: the super bar is **fed but never spent and never shown** (`AddSuperBar` on
  hit/combo/parry/dash counter, no HUD bar) - specials are what finally give it a purpose. And both
  fighters have a hidden passive health regen of 1 hp/s, which is why health crept up between test
  evals.

- **Decisions confirmed the same evening (2026-10-19), and the plan rewritten around them:**
  - **Specials: modifier button + attack button, no direction** — `SP + Light/Medium/Heavy` gives the
    three. `InputData.specialModifier` (L1 / `U`) already exists and is read by `InputManager` but
    used by nothing, which is exactly this. The moves themselves get confirmed next session.
  - **A special costs STAMINA, not super: 2.5x the matching normal** (light 15 → 38, medium 17 → 43,
    heavy 17–19 → 43–48 of a 100 bar, about 2.5 s of regen), plus a new `SPECIAL_LOCKOUT` (start
    1.0 s) before the next attack, applied through the existing `attackLockoutTimer`.
  - **The super bar is for SIGNATURE moves**, which come after the specials, combos and the animation
    pass. Nothing in lite spends it. `FighterStats.CanUseSignature()` / `signatureUsed` already
    exist — and that method also demands FULL stamina, which now fights the specials' 2.5x cost, so
    that condition needs revisiting in the signature pass.
  - **Dash unchanged** (no double-tap, no touch relayout); its counter ANIMATION is the only dash work.
  - **Keyboard defaults unchanged** — the in-game Controls menu is considered enough.
  - **Combos mix normals and specials**, in the user's shapes: `normal` → `special`,
    `normal` → `normal` → `special`, and `special` → `normal`. `MAX_STRING_LENGTH = 3` already fits
    all three, and `TryCancelIntoFollowUp` already stamina-checks a follow-up, so an ender can fail
    on an empty bar. **Specials therefore have to be built BEFORE the combos**, which reordered the
    tasks.
  - **Mixamo until every system is finished**, then a pass of purpose-made animations.
  - **Health recovery** +2% dodge / +2% parry / +3% counter hit, with `HEAL_CAP_PER_ROUND` present but
    **0 (off)** until it is seen to be abusable, and the hidden passive `healthRegenPerSecond` turned
    off so the recovery is the only heal in the fight.
  - The only control change left in the plan is the touch `SP` button, which has to LATCH for ~1 s:
    a modifier is a hold, and a thumb cannot hold two buttons at once.
  - **The three specials per character already exist on paper and are now revived.**
    `Design/Character_Movesets.md` authored a `L1 + button` trio for each fighter on 2026-10-11 with
    full frame data, damage, hit side, evade directions and animation notes; they were parked on
    2026-10-17 as out of scope. The input the user confirmed on 2026-10-19 **is** L1 + button, so
    both sections are un-parked and they are the shortlist to confirm next session: Bjorn
    **Haft Strike / Hewing Charge / Blood Eagle**, Miyamoto **Nukitsuke / Nitō Cross / Iaijutsu**.
    Three overrides are recorded in both docs: stamina comes from the 2.5x rule (38 / 43 / 43-48,
    not the 12-28 written there); the exotic properties are deferred because each needs a system
    that does not exist (multi-hit, armour frames, guard-crush, counter-stance, the self-propelled
    advance); and **Nitō Cross draws a wakizashi, which is not modelled** - it needs re-casting as a
    katana move or swapping out. Their damage also predates the 30% cut of 2026-10-18 (Blood Eagle
    24 against the current heavy's 11), so it wants a look.
  - `Design/Plan_2Weeks.md` now carries a **SUPERSEDED** header pointing at the lite plan, so there
    is no ambiguity about which plan is live.
  - Still to confirm next session, neither blocking: whether a string may repeat the same normal
    (`normal -> normal` on one button), and what replaces Nitō Cross if the wakizashi is out.

### 2026-10-19 (later) - Lite Task 1: the Mixamo pool measured, clips cast

`Design/Anim_Inventory.md` is the deliverable: all 32 attack-shaped clips in the two packs measured
(26 unused candidates + the 6 already in the game as calibration references), with a recommended
clip for each of the 6 specials and 4 ripostes. **Awaiting the user's pick/swap per slot** - nothing
is wired into the game.

- **`ArtSource/Blender/clip_curves.py` rebuilt** (the version used on 2026-10-12 lived in a
  scratchpad and was gone). Headless, whole set in one Blender run; reports per frame, in the
  fighter's own frame: right-hand path + a weapon-tip proxy (hand + 0.70 m along the grip line),
  unwrapped hip yaw, the swing core (tip speed >= 30% of peak), the tip's centre-line crossing
  (contact candidate), foot speed/height/reach, and hip travel. Summary committed at
  `ArtSource/Blender/clip_curves.json` (per-frame detail stripped; re-run for it).
- **Three measurement traps, all fixed in the script** - worth knowing before trusting any re-run:
  facing must come from the **hip-joint line**, not a hips *bone* axis (a bone axis tilts toward
  vertical when the pelvis pitches and its ground projection swings wildly - that read as 728 deg of
  yaw on the jump attack); the tip proxy must know the **grip** (GreatSword clips have the hands
  0.11-0.20 apart, AxePack clips 0.69-1.08, where the handle continues the FOREARM instead); and a
  kick must be judged against **tip** speed, not hand speed (in a body-driven sweep the hand barely
  moves while the blade flies, which labelled three sword cuts as kicks). Silhouette is now one arc
  label plus independent modifiers, because `great sword kick` is a kick AND a 360.
- **Accuracy, against the five already calibrated with `BladeTrace`:** single-arc cuts land within
  **+-2 frames** of the in-game contact (horizontal 26.7 vs 28.7, great sword slash 19.9 vs 21.5,
  slash (3) 29.6 vs 28.1). **On a spin the script finds the FIRST pass of the blade**, which is
  usually not the one wanted (both in-game spins were deliberately re-cut to a later sweep), so a
  spinning clip's contact must still be calibrated in game.
- **Four corrections the numbers forced on the plan's proposal:** `great sword attack` is **not a
  thrust** - the blade winds back/left and returns, never reaching further forward than its own
  guard, and **neither pack contains a thrust** (Miyamoto's `..._Light_Thrust` is already played by
  a diagonal cut); `great sword impact (4)`/`(5)` are **flinches** (peak tip 0.05/0.08), useful
  later as extra reactions; the **advancing clips' root motion is discarded** (XZ off the Animator,
  and the fighters' Rigidbody freezes X/Z, so root motion is not an option) - `run jump attack`
  3.64 m, `jump attack` 3.19 m, `slide attack` 3.49 m, `slash (2)` 3.10 m all play in place, so an
  advancing special needs a large `AttackData.lungeMaxDistance` instead; and both `disarm` clips end
  **38 deg off-facing**.
- **Design consequences for the user to settle:** Bjorn's authored `Haft Strike` (haft-butt punch)
  and `Blood Eagle` (spike driven in and ripped back) **have no clip in either pack** and cannot be
  trimmed into one, so they need renaming or re-designing. **Nitō Cross is answered** -
  `great sword slash (4)`, a full-turn rising cut, keeps it a katana move and sidesteps the
  unmodelled wakizashi. `Iaijutsu` has a tempting literal fit in `draw a great sword 1` (15 f, fast
  upward draw) but its hands are 0.81 apart around a scabbard the model does not have.
- **User's decisions, same day (Task 1 CLOSED):** all ten recommended clips **accepted as cast**;
  **Blood Eagle and Haft Strike keep their names** and are played by the leaping chop and the
  reverse horizontal (the names stop being literal until the animation pass); and **Iaijutsu becomes
  a DOUBLE THRUST**. That last one sent me back to the data: `great sword attack` drives forward
  **twice** - f12-17 takes the tip 0.77 m forward, f18-23 retracts, f24-36 drives 1.03 m - so the
  earlier "no committed strike" verdict was the single-arc test cancelling the two pushes against
  each other. It needs `WeaponAim` **Full** (the blade sits 0.70-0.83 m to the fighter's LEFT the
  whole clip and never crosses the centre line, which is why no crossing was found) and a lunge for
  reach (furthest tip extension is 0.97 m, about where the guard already sits). **Two HITS needs a
  system lite does not have** - multi-hit is deferred and the engine is one hit per swing - so in
  Task 3 it either deals damage on the deeper second push with the first as a feint, or becomes an
  **auto-linked two-move string**, which needs `ComboLink` to fire without a button press.
  `great sword jump attack` (727 deg double-spin leap) goes into reserve.
- Recommended and accepted: Bjorn backhand / 360 low / run jump attack, dash riposte `combo attack ver. 3`
  (fastest tip + tightest core in either pack), parry riposte `kick ver. 2` (shortest clip, furthest
  foot). Miyamoto `slash (5)` / `slash (4)` / `jump attack` (727 deg double spin), dash riposte
  `great sword kick` (spinning, furthest foot, ends square), parry riposte `kick (2)` (front push
  kick, contact at 0.88 of the clip so it needs a tight trim). Miyamoto SP+Light `slash (5)`,
  SP+Medium `slash (4)`, SP+Heavy `great sword attack` (the double thrust). All four of Bjorn's
  picks are one-handed clips leaning on `TwoHandGrip`'s IK; run `mixamo_carry2h.py` into
  `AxePack2H/` if a left arm stretches.
### 2026-10-19 (later still) - Lite Task 2: the special-attack input layer

`SP + Light/Medium/Heavy` now throws specials. Built and **play-verified**; the clips and the six
real assets are Task 3, so nothing is authored yet.

- **Two keys, one enum.** `AttackType.Special` is a single value but there are three specials, so
  `AttackData.specialButton` (Light/Medium/Heavy) is the second key and `CombatSystem.specials` is a
  separate serialized library beside `attacks`. `FindAttack(button, special)` searches one or the
  other, so **a plain press can never produce a special** and a special never shadows a normal.
  `AttackType` stays `Special`, which keeps the clash weights and the anti-spam chain rules working.
- `TryAttack(AttackType, bool special = false)` - the default keeps every existing caller (the AI,
  the tests) compiling unchanged. `Fighter.HandleCombatInput` reads `currentInput.specialModifier`
  on the frame the attack button is pressed (the modifier is a HOLD).
- **Fall-through:** the modifier held on a button with no special throws the NORMAL instead of eating
  the press (`no special on <button> - throwing the normal instead`), so every character stays
  playable before all three are authored and holding L1 never bricks the buttons.
- **Cost and lockout:** stamina comes from the asset (2.5x the matching normal per the user's rule),
  plus `BalanceConfig.SPECIAL_LOCKOUT` = **1.0 s** applied through the existing `attackLockoutTimer`
  when a special runs its course, overridable per fighter via serialized `specialLockout`
  (0 = fall through). `Mathf.Max` with the string/chain lockouts, so the longest wins.
- **Combo links can require the modifier:** new `ComboLink.withSpecialModifier`, and the buffer
  remembers whether the press carried it (`bufferedSpecial`). `FindLink(button, special)` prefers a
  modifier link and **falls back to a plain one**, so a player who holds the modifier through a
  string still gets its ordinary follow-ups. Existing links are unaffected (the flag defaults false).
  This is what Task 4's `normal -> special` combos will hang off.
- **Touch:** an `SP` button inboard of the strikes that **latches** for `specialLatchSeconds` (1 s)
  on a tap and lights up while armed, because a modifier is a hold and a thumb cannot do both. The
  next strike spends the latch. **Not verified - the CLI cannot press it.**
- **Two bugs found and fixed on the way:** (1) the touch **PARRY** button has never worked -
  `TouchControls.Read` has set `defenseButton` since the parry session but `InputManager` never
  merged it into the snapshot; (2) the net protocol (`I ...`) carried neither `specialModifier` nor
  `defenseButton`, so an **online guest could not throw a special or parry at all**. Both now cross
  the wire as fields 8 and 9, guarded by `p.Length` so a shorter line from an older build still parses.
- Help screen gained a SPECIAL entry and all three control summaries print the modifier key. The
  Controls menu already iterates every `GameAction`, so Special was already rebindable.
- **Play-verified** (stand-in special injected by reflection, no asset committed): `SP+Light` →
  `TEST Special (Light) STARTED`, type Special, stamina 100→62; `P1 ... HIT P2 for 12 dmg`;
  `special finished - locked out for 10.00s` (the override raised to 10 s on purpose - see below);
  plain `Light` → `Slash STARTED` (the normal); `SP+Medium` → `no special on Medium - throwing the
  normal instead` → `Spinning Slash STARTED`.
- **Testing notes:** `GameMenu.StartMatch` is **public** now, so the session log's instruction to
  reach it with `GetMethod(..., NonPublic|Instance)` returns null - call it directly. The 1.0 s
  special lockout is shorter than a CLI round trip and cannot be caught by polling: raise the
  serialized `specialLockout` to ~10 s for the probe, or read the console log line. `eval_file`
  takes `--file`, rejects a top-level `using` (fully-qualify `System.Reflection.BindingFlags`), and
  trips on ambiguous `Object` (use `UnityEngine.Object`); `DebugTimeScale.Apply` is private, so set
  `timeScale` and let its `LateUpdate` re-assert it. `console --tail N` returns full stack traces -
  filter hard.
- **Next: Task 3**, the six special `AttackData` assets from the agreed clips (frame data from
  18/5/20 for 14-16 damage), each with a `swing_contacts.json` entry, a `WeaponAim` mode, lunge
  tuning, a `CrucibleMixamoSetup` map entry and an animator trigger + Any State -> state whose name
  matches the asset.

### 2026-10-19 - Lite Task 3: the six specials

`SP + Light/Medium/Heavy` now throws a real move for each fighter. All six **play-verified**:
fired, animated from their own state, cost the authored stamina and landed.

| slot | asset | clip | frames | dmg | stam | aim | lunge |
|---|---|---|---|---|---|---|---|
| Bjorn SP+L | `Bjorn_Special_HaftStrike` | AxePack `attack backhand` | 18/5/20 | 14 | 38 | Full | - |
| Bjorn SP+M | `Bjorn_Special_HewingCharge` | AxePack `attack 360 low` | 22/5/22 | 15 | 43 | YawOnly | - |
| Bjorn SP+H | `Bjorn_Special_BloodEagle` | AxePack `run jump attack` | 24/5/24 | 16 | 48 | YawOnly | max 2.2 |
| Miya SP+L | `Miyamoto_Special_Nukitsuke` | GSP `slash (5)` | 18/5/20 | 14 | 38 | Full | - |
| Miya SP+M | `Miyamoto_Special_NitoCross` | GSP `slash (4)` | 20/5/22 | 15 | 43 | Off | max 1.2 |
| Miya SP+H | `Miyamoto_Special_Iaijutsu` | GSP `great sword attack` | 34/4/8 | 16 | 48 | Full | max 1.4 |

- **Stamina is the 2.5x rule** (light 15 -> 38, medium 17 -> 43, heavy 19 -> 48) and each one ends in
  the 1.0 s `SPECIAL_LOCKOUT` from Task 2. Damage sits at the top of the planned 14-16 band, so a
  special is worth about two lights for ~2.5x the bar.
- **Hit sides and evade directions are deliberately NOT copies of the normals.** Haft Strike is
  `AttackerLeft` where both Bjorn normals are `AttackerRight`, so the special is dodged by the
  opposite sidestep - the reverse-horizontal silhouette and the dodge rule agree.
- **`Iaijutsu` damages ONCE, on the second push.** `great sword attack` drives forward twice
  (f12-17, retract, f24-36); the frame data is **34/4/8** so the first push is STARTUP - a feint with
  no hitbox - and the hit lands at 78% of the window, inside the deeper second drive. A true two-hit
  version needs `ComboLink` to fire without a press; deferred, and recorded in the plan.
- **How each window was fitted** (the arithmetic, since it is not obvious): `CrucibleMixamoSetup`
  cuts the source clip to `len = speed * total / 2` source frames and puts the contact at
  `hitFrac = (startup + active/2) / total` through it. So the frame data, the `swing_contacts.json`
  speed and the clip's own length are ONE calculation - change the frame data and the window moves.
  Worked examples: Nukitsuke's contact is at source frame 12.2 with the clip starting at 1, so
  **speed 1.0** keeps the window (2.0-23.5) inside the clip; at the usual 2.0 it would have clamped
  to the clip start and shown the blade crossing 20% before the damage frame. Nito Cross uses
  **speed 2.4** so the window is the whole 55-frame clip and the full turn plays, the same trick both
  heavies use.
- **`swing_trims.json`** gained the 4 clips that had never been analysed
  (`attack 360 low`, `run jump attack`, `slash (4)`, `slash (5)`), generated from
  `ArtSource/Blender/clip_curves.json` - so Task 1's measurements feed the importer directly.
- **`Editor/CrucibleSpecialStates.cs` (new)** - `Crucible > Animation > Wire special states`. Finds
  every `AttackData` of type Special in a character's folder and ensures its trigger parameter, a
  state named exactly after the asset, `Any State -> state` on that trigger (0.12 s fixed blend, no
  exit time) and a return to the guard at exit 0.9. Re-runnable, adds only what is missing. It must
  run BEFORE `Use Mixamo packs`, which only re-points states that already exist. Triggers are
  `SpecialLight` / `SpecialMedium` / `SpecialHeavy` on both controllers.
- Map entries added to `CrucibleMixamoSetup` P1Map/P2Map, so `Use Mixamo packs` reproduces all six
  instead of wiping them. Scene `CombatSystem.specials` arrays filled (ordered Light/Medium/Heavy)
  and saved.
- **Verified in play at 2.2 units:** each `SP + button` produced its own asset, type Special, with
  stamina 100 -> 62 / 57 / 52 and 12 / 13 / 14 damage landed (after the defender's armour), on both
  fighters; every special state carries its own trimmed clip at the fitted speed; and all six plain
  presses still give the normals.
- **Editor-script note:** `Assets/_project/Editor/*.cs` belong to the **`Crucible.Editor` asmdef**,
  not `Assembly-CSharp-Editor`, so the Roslyn check's editor `.rsp` does not cover them - compile
  those with `unity command recompile` instead. Also: a sub-second move finishes inside one CLI
  round trip even at 8% time scale, so "what is the animator playing" cannot be read in a following
  eval - read the controller asset (state -> motion -> speed) instead, which is timing-free.
- **Next: Task 4**, the combos - 6-9 per character in the user's three shapes (`normal -> special`,
  `normal -> normal -> special`, `special -> normal`). The plumbing is already there:
  `ComboLink.withSpecialModifier` from Task 2, and `TryCancelIntoFollowUp` already stamina-checks a
  follow-up, so a special ender fails on an empty bar.

### 2026-10-20 - Lite Task 4: the combo strings and the move list

**10 strings per character** (14 counting the two-hit prefixes of the three-hit ones), in the user's
three shapes, plus a Moves screen. All **play-verified**. Pure data on the cancel system built in
October: no new clips, no new assets, no new animator states.

- **`Editor/CrucibleCombos.cs` (new)** holds the tables. `Crucible > Combat > Wire combo strings`
  writes every `comboLinks` array from them; `Report combo strings` walks the live assets and prints
  every string they reach. Re-runnable and REPLACES the links it owns, so changing a string means
  editing a row there and re-running the menu item - not hand-editing GUIDs into `.asset` YAML.
- **Bjorn**: Light opens with three branches (`Heavy` keeps the filmed light -> 360 overhead ender,
  `Medium`, `SP+Medium`); Medium and Heavy open with one normal and one special each; each special
  links back to a normal. **Miyamoto**: the three plain links verified on 2026-10-18 are kept, and
  each normal gains a special branch (`SP+Heavy` Iaijutsu off the light, `SP+Light` Nukitsuke off the
  medium, `SP+Medium` Nito Cross off the heavy).
- **Special enders require `HitOrBlock`**: a whiffed light must not buy a free special. Plain
  normal-into-normal links stay `Always` (MK strings continue on a whiff), and the cancel already
  fires the instant a blade connects, so a hit-confirmed ender still feels immediate.

**Two rules the shapes needed, because a link lives on an attack and knows nothing about where in a
string that attack is:**
- **`ComboLink.openerOnly`** (new field, honoured by `AttackData.FindLink(input, special, depth)`):
  a plain normal-into-normal link is offered only when that attack OPENED the string. Without it a
  mid-string normal offers its own continuation - three normals in a row, and strings that double
  back on themselves. **This settles the open question in the plan: a string cannot repeat a normal.**
  The input buffer's dial-a-combo hold is depth-aware for the same reason.
- **One special per string** (`CombatSystem.stringUsedSpecial`, set in `BeginAttack`, where depth 0
  starts the count over). Without it `special -> normal` reached that normal's own special link, so a
  single chain could spend two specials (~86 stamina) for three big hits. If the refused press has a
  PLAIN branch on the same button it is used instead; otherwise the press is dropped from the buffer
  rather than re-tested (and re-logged) on every frame of the cancel window. The string-length cap
  now drops its press the same way.
- `Bjorn_Light_Slash`'s authored cancel window **8-14 -> the defaults (-1)**. It was authored when
  the light was 6/3/7; at today's 10/4/8 it ENDED on the first recovery frame, leaving a one-frame
  window on a whiff. The default (first recovery frame to the end) is the uniform rule, and the
  hit-confirm path still fires it at contact.

- **Moves screen** (`GameMenu`, pause menu and main menu): built from the live `AttackData` every
  time it opens, so it cannot go stale. Pages through *fighter x (moves | strings)* with left/right,
  because one fighter's moves AND strings together overflow the panel - verified by capture, not
  assumed. Each move shows damage / stamina / frame data; each string shows the buttons in that
  player's own bindings and the move names. Only the LONGEST form of a string is listed (a two-hit
  that extends is shown as the three-hit, since stopping early is always allowed), which is what
  makes 10 lines fit. Presses are bracketed - `[,] > [.] > [R Ctrl + ,]` - because Player 2's
  strikes are punctuation and the modifier is a two-word key.
- The move list names each fighter from its attack-asset prefix (`Bjorn_Light_Slash` -> Bjorn).
  **Both fighters still share one `FighterData` called "test Fighter"**, so the HUD shows that name
  for both players; `Design/Character_Stats.md` has real per-character stats, and giving each
  fighter its own asset is a design pass, not this one.

**Verified in play** (2.2 units): `Light -> SP+Medium` 19 dmg and `Medium -> SP+Heavy` 22 dmg as
two-hit strings; `Light -> Medium -> SP+Heavy` reached **depth 2 with Blood Eagle** out;
`Medium -> SP+Light` on Miyamoto 18 dmg with `lockout=0.85s` of the string lockout still running;
`Light -> Medium -> Light` **refused** (openerOnly); `SP+Light -> Light` cancelled correctly and the
second special was **refused** with `a string carries only one special`; and
`2-hit string finished - locked out for 1.50s` confirms `STRING_LOCKOUT`, which had never been
play-tested.

**Testing trap worth more than the task: every test billed as "8% speed" since the menus landed has
actually run at 0.85.** `DebugTimeScale.LateUpdate` only applies its value when `Time.timeScale` is
~1, and `GameMenu` puts the Settings game speed (0.85) there at match start, so the component is
inert for the rest of the match - and `GameMenu.ApplySettings` also overwrites
`DebugTimeScale.timeScale` itself, so setting that field before `StartMatch` is thrown away twice
over. **Set `Time.timeScale` directly, after `StartMatch`.** Nothing re-asserts it during a running
match. The earlier tests were still valid (they measured damage and state, not timing), but this is
why a move always finished inside one CLI round trip. At a real 0.05 the Editor's ~17 fps gives
about 0.05 game-seconds per real second, which is slow enough to dial a three-hit string across
three round trips.

### 2026-10-20 - Lite Task 5: counter ripostes, defensive health recovery, per-fighter vitals

All **play-verified**. Also carries the user's two additions: each fighter has its own health and
stamina, and the HUD reads in percent rather than points.

**Per-fighter vitals.** `Fighter` gained `maxHealth` / `maxStamina` / `armor` under a Vitals header,
each **-1 = take the FighterData value**, with `MaxHealth` / `MaxStamina` / `Armor` properties that
both `FighterStats` construction sites now go through. Scene: **Bjorn 90 hp / 100 stam / 15% armor,
Miyamoto 85 / 100 / 10%**, the numbers in `Design/Character_Stats.md`. This is deliberately NOT a
second `FighterData` asset: both fighters still share `testfighter_data.asset` (which is why the HUD
still names them both "test Fighter"), and splitting that is a design pass with real balance
consequences. Note the sheet's health values make fights ~12% shorter than the 100/100 they were
tuned at.

**The HUD health number is a percentage** (`82%`), ceil so any health left shows at least 1%. With
different maxima the raw points read as one fighter simply having more bar than the other.

**Defensive recovery.** New `BalanceConfig.HEAL_ON_DODGE` / `HEAL_ON_PARRY` (0.02) /
`HEAL_ON_COUNTER_HIT` (0.03) / `HEAL_CAP_PER_ROUND` (**0 = off**), all fractions of the recovering
fighter's OWN max health so the two fighters get the same share of bar. `FighterStats.HealFraction`
does the work and keeps `healedThisRound` for the cap; `Fighter.RecoverHealth` is the single entry
point, so the three cases cannot drift apart. Serialized per fighter (`healOnDodge`, `healOnParry`,
`healOnCounterHit`, `healCapPerRound`). The hidden passive `healthRegenPerSecond` is **0 on both
fighters** - recovery is now the only heal in the fight.
- The dodge heal sits in `NotifyEvaded` **inside the same guard as the dodge dust**: both evade
  paths (hitbox arming and trigger contact) report the same dodge, so without that one guard a dodge
  pays out twice. The guard used to cover only the dust and only when `dustEffects` was on.
- The counter-hit heal is on the HIT, not on starting the counter: the window is not the reward,
  cashing it is.
- HUD: a green `+N%` float over the inner end of the health bar that fades and drifts up
  (`healColor`, `healFloatFadeOver`, `healFloatRise` on FightHud; `Fighter.HealFlashTimeLeft` /
  `HealFlashAmount` drive it).

**The four ripostes.** `CombatSystem.dashRiposte` / `parryRiposte` (serialized, null = fall back to
the normal). A plain strike press inside a counter window throws the riposte instead of the normal,
so a counter LOOKS like one; **holding the special modifier is the deliberate opt-out**, for
spending the window on a special instead. Parry counter wins if both windows are somehow open.
They are in neither library, so nothing else can reach them.

| slot | asset | clip | frames | dmg | stam | weapon? |
|---|---|---|---|---|---|---|
| Bjorn dash | `Bjorn_Riposte_Dash` (Axe Riposte) | AxePack `combo attack ver. 3` | 12/4/14 | 9 | 12 | yes |
| Bjorn parry | `Bjorn_Riposte_Parry` (Boot Riposte) | AxePack `attack kick ver. 2` | 12/4/14 | 8 | 12 | **no** |
| Miya dash | `Miyamoto_Riposte_Dash` (Spinning Riposte) | GSP `great sword kick` | 12/4/14 | 9 | 12 | **no** |
| Miya parry | `Miyamoto_Riposte_Parry` (Push Kick Riposte) | GSP `great sword kick (2)` | 20/4/6 | 8 | 12 | **no** |

- **`AttackData.hitsWithWeapon` (new, default true).** Three of the four clips Task 1 cast are
  KICKS, and hits are judged by sweeping the BLADE - a carried blade never catches a kick, so all
  three would simply have whiffed. With the flag off the authored front box (`hitboxRange` /
  `hitboxOffset` / `hitboxSize`, fighter space) judges the authored active window instead. That is
  the same code the slow-frame fallback already used, now entered on `bodyAttack || slowFrame`;
  logged as `body attack - front box judged <name>`. Kicks are `aimMode Off` (do not swing the
  sword at them) with reach 1.8-2.0, against the weapon riposte's 3.0.
- **Frame data is a single calculation with the clip**, as always: `Miyamoto_Riposte_Parry` is
  **20/4/6**, not 12/4/14, because its contact sits at source frame 46 of 52 - only 6 frames of
  clip remain after it, so the window has to be wind-up heavy or it clamps. Fitted windows, none
  clamped: Bjorn dash 14.6-44.6 of 1-83 (hit at 47%), Bjorn parry 3.8-27.8 of 1-43 (47%), Miya dash
  1.6-19.6 of 1-46 (47%), Miya parry 29.5-52.0 of 1-52 (73%).
- Damage is modest **because the counter multiplier rides on top** - the riposte consumes the flag
  through the existing `ConsumeDashCounter` / `ConsumeParryCounter` in `BeginAttack`, so 9 becomes
  ~12 on a dash counter and 8 becomes 12 on the scene's x1.5 parry counter. Stamina 12 is cheaper
  than a light: the punish was earned.
- **Each riposte links into that fighter's light** (`CrucibleCombos`, openerOnly). Without it,
  replacing the normal strike would have silently removed the ability to open a string off a
  counter, which worked before this task.
- `Crucible > Animation > Wire special states` is now **Wire special and riposte states**: it picks
  up `*_Riposte_*` by name as well as every Special by weight, since a riposte deliberately carries
  an ordinary weight (so it clashes normally). Triggers `RiposteDash` / `RiposteParry` on both
  controllers. Map entries, `swing_trims.json` and `swing_contacts.json` all updated, so
  `Use Mixamo packs` reproduces the four instead of wiping them.
- Moves screen: the two ripostes are listed on each fighter's moves page as `[after a dodge]` /
  `[after a parry]` with their follow-up inline, rather than on the strings page, which is already
  as long as the panel can hold.

**Verified in play at 2.2-2.3 units:** a forced parry window turned a Light press into
`Miyamoto_Riposte_Parry` (`RIPOSTE - Push Kick Riposte instead of the Light`), which landed through
the new path (`body attack - front box judged`) for 10 after armour at x1.5; a forced dash window
turned a **Heavy** press into `Miyamoto_Riposte_Dash`; Bjorn's weapon riposte landed 11 at x1.35
through the ordinary blade sweep. Recovery: `+2 hp from the dodge (62/85)`, `+2 hp from the parry
(64/85)`, `+3 hp from the counter hit (67/85)` and `(73/90)`. HUD captured showing `82%` / `73%`
and the `+2%` float on both sides.

**Testing note:** `HealFraction` gives nothing at full health, which is correct and is why the first
riposte run showed no recovery at all - hurt the fighter before testing a heal. Also
`CombatSystem.isAttacking` is not a reflectable field (a reflection `SetValue` on the null it
returns is the "Object reference not set" an `eval_file` reports).

### 2026-10-20 - Lite Task 6: one arena per build, quality by device (phone pass still open)

Items 1 and 2 done and verified; **item 3 (the phone pass) needs the user's device and is open.**

- **The build ships ONE arena: 268 MB -> 155 MB**, the data file **214 -> 101 MB**, in 12.8 min.
  `CrucibleBuild` now builds from a throwaway COPY of the scene (`_build_temp.unity`, gitignored)
  with the switched-off scenery deleted, then deletes the copy and reopens the real scene in a
  `finally`, so a failed build cannot leave the Editor on the stripped copy. An inactive GameObject
  is still serialized, so all four arenas (~126 MB of source meshes and 2K textures) had shipped in
  every build since October.
  - The rule is **"an inactive ROOT that carries renderers"**, not a list of arena names, so it
    keeps working when a different arena is placed. Nothing at runtime activates a root (checked:
    no runtime script references an arena or calls SetActive on one) and logic objects carry no
    renderers, so it cannot take something the game needs.
  - It saves open scenes first when the scene is dirty, because the copy is made from the file on
    DISK - unsaved edits would silently miss the build.
  - Logged as `Build scene: shipping one arena, left out 3 switched-off (Arena_AlQuds, DesertArena,
    ForestClearing)`. Verified separately from a build by invoking `PrepareScene` / `RestoreScene`
    through reflection: 9 gameplay roots + `MedievalFortress` in the copy, 12 roots and a deleted
    temp scene afterwards.
- **`MobileQuality` follows the DEVICE, not the build target.** It gated on
  `isMobilePlatform || WebGLPlayer`, so every PC browser got render scale 0.7, 18-unit shadows and
  no post. It now shares `TouchControls.IsTouchDevice` (made public; it asks the PAGE through
  `DeviceBridge.jslib`), which is the same call the on-screen pad already makes. `Awake` is now
  `Apply()`, public, and `TouchControls` calls it again if a finger later lands on something first
  read as a desktop - the quality decision was made at startup on the same wrong answer.
- **The touch pad was captured for the first time** (`ArtSource/Characters/Renders/ui/touch_pad*.png`,
  `forceShow` + `Layout()` through reflection). The `SP` button from Task 2 and the `PARRY` button
  both exist and are placed as intended. Two things for the phone pass, deliberately NOT changed
  blind: in the two-player layout both pads' BLOCK/PARRY meet within ~110 canvas units of screen
  centre, over the fighters; and BLOCK/PARRY sit ~510 canvas units up the right edge (canvas is
  1920x1080 reference, match 0.5, so on a 20:9 phone the canvas is ~2147x966 and that is ~53% of
  the screen height).
- Still uncompressed on purpose (any static server can host it); Brotli + decompression fallback is
  the next size win if the build ever needs to leave the PC.
- **Hosting, for the record** (the user asked): the laptop IS the server today - `Server/relay.js`
  hosts the files and relays the netplay, `cloudflared` tunnels it, so sleeping or closing either
  window kills every tester's link, and the quick-tunnel URL changes on each restart. Single-player
  and local two-player are pure static files and could live on a static host (itch.io; the 101 MB
  data file is still over GitHub Pages' 100 MB per-file limit), but **online two-player needs the
  relay running somewhere always-on** regardless.

### 2026-10-20 (end of session) - where CRUCIBLE Lite stands

**Tasks 1-6 of `Design/Plan_LiteMobile.md` are done**, every one play-verified. **Task 7 (teaching
the AI the lite game) was dropped by the user**, who is writing a new task list for the next
session - so the next session starts from THAT list, not from the lite plan's running order. The
plan file carries a status header saying so.

What exists now that did not on 2026-10-19: six specials on `SP + button`, ten combo strings per
character in the three agreed shapes, four counter ripostes with their own clips and animator
states, defensive health recovery, per-fighter health/stamina/armour, a percentage HUD, a Moves
screen, and a web build less than half the size it was.

**Open, and only the user's phone can answer it** (Task 6 item 3): frame rate, whether the
on-screen buttons fall under a real thumb, and online latency. Two layout questions are written up
in the plan and deliberately NOT changed blind - the two-player pads meeting over the fighters at
screen centre, and BLOCK/PARRY sitting ~53% of the screen height up the right edge on a phone
aspect.

**Other things left deliberately, each recorded where it belongs:**
- The AI cannot parry, throw a special or dial a string on purpose (lite plan, Task 7).
- The web build is **uncompressed on purpose**; Brotli + the decompression fallback is the next
  size win, and would need the right `Content-Encoding` from whatever serves it.
- Hosting still means the laptop is the server (relay + quick tunnel), the URL changes on every
  tunnel restart, and online two-player needs a relay running somewhere always-on no matter where
  the static files live.
- Both fighters still share one `FighterData` called "test Fighter", so the HUD names them both
  that; the per-fighter numbers live on the `Fighter` components instead.
- `parryCounterDamageMultiplier` is serialized **1.5** in the scene against `BalanceConfig`'s 1.35,
  and `parryCounterWindow` 1.0 s against 0.5 - a deliberate-looking divergence that was never
  settled.
- Debug-only components still in the scene: `PlayProbe`, `BladeTrace`, `InputHold`, `DebugTimeScale`
  (the last one is wanted - the Settings speed slider drives it).


### 2026-10-20 (later) - the front end: boot, character select on the live rigs, arena select, pause

The game now opens as a game: Unity splash -> logo -> "press any button" -> main menu -> a setup
flow -> the fight. Built from code like the rest of the UI, dressed from the logo (near-black
field, bronze inscription, dried-blood selection). All of it play-verified by capture.

- **`Scripts/UI/MenuArt.cs` (new)** - the palette and every texture, generated in code: mottled
  stone panel and bronze frame as 9-slice sprites, vignette, soft dot, diamond, hairline rule,
  film scratches, a top-down fade. Plus the uGUI builders (`Rect`/`Img`/`Raw`/`Label`/`Shadowed`/
  `Place`/`Box`/`Divider`) and `Track()` for letter-spaced caps. `Logo` loads
  `Resources/ui/crucible_logo` (the user's art, copied into Resources).
- **`Scripts/UI/MenuStage.cs` (new)** - three layers on the menu canvas: a back layer (dark field,
  dim logo watermark, hearth glow, 26 drifting embers, vignette), the menu content, and a front
  layer (grain, the two-panel wipe with bronze leading edges, and the announcement text used for
  FIGHT / K.O. / PARRY). `Wipe(atBlack)` is the transition every screen change uses; the callback
  is wrapped in try/catch so an exception cannot strand the screen behind a black wipe.
  Everything animates on **unscaled** time, because the pause menu runs at `Time.timeScale` 0.
- **`Scripts/core/ArenaDirectory.cs` (new)**, on GameMangers - the four arenas with a `shipped`
  flag. One flag decides BOTH what the build ships and what the player can pick:
  `CrucibleBuild.ShippedArenaRoots` reads the component out of the scene with `SerializedObject`
  (the editor asmdef cannot reference Assembly-CSharp) and keeps those roots instead of deleting
  every switched-off root. Shipping now: **Medieval Fortress + Desert Arena**; Al-Quds and Forest
  Clearing stay out.
- **`Scripts/core/PracticeMode.cs` (new)** + **`AiController.Rules`** - practice tops the bars up
  rather than nulling damage, so every hit still resolves normally, and only while
  `currentHealth > 0` so a death animation is never interrupted. `Rules` gates walking, attacking,
  blocking, dodging and punishing independently (12 gates inside `Think`), with `Dummy` and `Full`
  presets, so the practice screen can hand the AI as much or as little as you want.

**The select screens show the REAL fighters, not art** (the user's call mid-session; the concept-art
portraits were built first and then deleted). `GameMenu.StageFighters` disables `FightCamera`, poses
the pair for the camera and parks the camera on them; `PlaceFighters` puts everything back when the
match starts, so nothing staged has to be preserved. Three things it has to get right:
- **Frame first, pose second.** A fighter standing 2.4 units off to the side does not face the
  camera by facing the stage centre - that error measured 31 degrees, a near profile. The camera
  position is computed first, then each fighter is turned toward *it*.
- **Each fighter keeps its own Y** (`ownY`, 0.24 / 0.75): the two roots have different ground
  offsets, and averaging them is the old floating bug.
- Arena select pulls the camera right back and **switches the real arena live** as you scroll
  (`ArenaDirectory.Select(i, remember: false)`), so choosing the ground means looking at it.

**The three bugs the user reported, all root-caused and fixed:**
1. **The Move List would not change page.** The adjust lambda changed `movesPage` and called
   `Refresh()`, which only re-renders the rows already built - but a page's lines are baked into
   `items` when the screen is BUILT. The label (a live lambda) updated while the moves never did.
   Now the adjust rebuilds the screen. Verified: page 1 -> 2 -> 3 with the body text changing.
2. **Pause resumed on almost any button.** `MenuConfirm` fires on Enter/Space/Cross and `MenuBack`
   on Esc/Backspace/Circle/Options - so on a pad only Square, Triangle, the shoulders and the
   d-pad did NOT resume, and Space is Player 1's dash key. Two fixes: a **0.22 s unscaled input
   guard** on every screen change (the button that opened the screen is still down on the frame it
   appears), and `Back()` on the pause ROOT now does nothing - Options/Escape still toggles the
   pause off, but Circle, which is also the parry button, no longer throws you back into the
   fight. Verified: `Back()` on Pause leaves the screen on Pause; `Resume()` returns to the match.
3. **Health regen after a quiet spell is back.** It was never missing from the code
   (`healthRegenDelay` 3 s has been there) - `healthRegenPerSecond` was serialized **0** on both
   fighters. Set to 1 hp/s in the scene. Verified in play: hurt to 60/90, 69/90 ten seconds later.

**Other bugs found and fixed on the way, none of them reported:**
- **A domain reload broke the backdrop.** `MenuStage.Ember` was a plain class, so recompiling
  while the game was playing left `embers` null while the plain `Image` references beside it
  survived, and `ApplyBackdrop` then threw once per frame. `[System.Serializable]` on `Ember` plus
  null guards.
- **The HUD drew over the menus.** New `FightHud.SetVisible`, driven by the screen. And
  `Rebuild()` (which `ApplySides` calls when the sides swap) now restores the visibility it had,
  or the bars popped back on top of the VS card.
- **`Fighter.ResolveIdleState` never found the idle state.** It tried the FighterData name, but
  both fighters share one asset called "test Fighter" while the controllers name their states
  after the CHARACTER. Every match reset fell through to `Animator.Rebind()` - the fallback the
  dead-fighter-stays-dead fix exists to avoid. New `Fighter.CharacterName` (derived from the
  attack-asset prefix) is now the first candidate, so `Bjorn_Idle` / `Miyamoto_Idle` resolve.
- Lists longer than the visible rows could not be scrolled when nearly every row was body copy
  (How to Fight, the move list): the view now scrolls when there are two or fewer selectable rows.
- The slab is sized to the rows it holds instead of a fixed 920 tall.

**Layout notes worth keeping:** the header and footer live OUTSIDE the slab, so the select screens -
which have no slab, because the arena is behind them - get a title and a hint line in the same
place. The logo art is a 16:9 plate with the wordmark floating in black, measured at x 138-1796,
y 390-750 of 1920x1080; the main-menu title crops to exactly that (`uvRect`, 4.42:1) and the boot
screen shows it **full bleed**, because at any smaller size its opaque black background draws a
visible rectangle against the field.

- `Editor/CrucibleFrontEnd.cs` (new): `Crucible > Front End > Set up the front end` imports the
  logo (max 2048, no mips), paints Unity's own splash the logo's near-black so the hand-off has no
  flash, and adds ArenaDirectory + PracticeMode to GameMangers. Re-runnable.
- **Flow**: Quick Battle jumps straight back into your last setup (character, 1P/2P, difficulty,
  arena - saved to PlayerPrefs on every match start); Local Play is the full 1P/2P -> character ->
  arena -> difficulty -> VS path; Practice goes character -> arena -> rules. Character select is
  "pick your side": the two scene fighters are not cloned, only the player numbers swap
  (`Fighter.SetPlayerNumber`), the spawn marks are stored per SIDE, and the HUD is rebuilt.
- Verified end to end in play: pick the other fighter -> Miyamoto becomes P1 and Bjorn P2, they
  swap marks, the match starts with `FightCamera` back on and both fighters on the floor; vs-AI
  activates the AI, practice activates a standing dummy with infinite health. Console: 0 errors.

### 2026-10-20 (later still) - the count-in, the wrong-page bug, and death by name

Three things the user asked for after playing the new front end.

**"I click Local Play and it jumps to Quick Battle, or it lands on Settings."** Two selection
systems were running over the same rows. This menu tracks its own `selected` index and draws its
own gem, but the rows are uGUI `Button`s under a `StandaloneInputModule`, so Unity's EventSystem
was ALSO navigating them: the arrow keys moved its focus independently of the gem, and
Enter/Space sent `Submit` to whatever IT had selected - on top of this menu's own `Confirm()`.
One press did two things, and clicking a row left that row selected, so the next Enter fired the
row in that same slot on whatever screen you had moved on to. Fixed at both ends: every row
Button gets `Navigation.Mode.None`, and `Update` clears `EventSystem.currentSelectedGameObject`
whenever anything holds it. Verified: 26/26 rows have navigation off, and a selection forced onto
a row is gone by the next frame. **Any uGUI menu that does its own keyboard handling needs both
halves of this** - the module is enabled for mouse clicks, and it answers the keyboard too.

**"Sometimes the character keeps dying even after restarting."** The rematch path itself is
clean, and I could not reproduce a fighter coming back dead: killed and restarted repeatedly,
health returns to full, `isDead` clears, the animator lands back on `Bjorn_Idle`. (I also forced
`idleStateHash` to 0 to re-create the pre-fix `Animator.Rebind()` fallback, and even that
recovers.) What was real and is now fixed is the corpse that stayed down *through the menus*:
`MatchOver -> Change fighters` goes to character select, which only *moved* the fighters to pose
them, so the loser lay dead on the select screen and through every screen after it.
`PlaceStaged` now calls `ResetForMatch`, which revives and re-idles as part of posing. Also
hardened `CombatSystem.ResetForMatch` to disarm the hitbox unconditionally rather than only when
`isAttacking` - the blow that ends a match is live when the match stops, and a hitbox left armed
through the teleport to the spawn marks would land a free hit on the first frame of the next one.

**The count-in.** `StartMatch` no longer drops you straight into the fight: it places everything,
shows the arena, and holds. `CountInRoutine` runs READY (0.8 s) - 3 - 2 - 1 (0.55 s each) - FIGHT,
on unscaled time, and only while `screen == Screen.None`, so pausing mid-count does not burn the
count down behind the pause menu. Until FIGHT: `inMatch` is false (no K.O. can be declared),
`FreezeFighters(true)`, and `InputManager.InputEnabled` is false. **The AI needed its own gate** -
it drives `CombatSystem.TryAttack` directly, so neither the disabled `Fighter` nor `InputEnabled`
holds it, and it would have opened every match with a free swing. `SetAi` is now called with the
real rules at FIGHT, and with `false` at match start. Verified in one frame at match start:
`inMatch=False input=False fighterEnabled=False AI=False`; four seconds later all true.

**The lettering.** `MenuArt.Chiselled` builds one line as a stack of `Text` copies - six extrusion
layers walking down-right from the face colour into near-black, a pale copy offset up-left for the
lit top edge, then the face with an outline - so flat uGUI text reads carved and lit the way the
logo's wordmark does. The announcement layer uses it, so FIGHT, the numbers and the K.O. line all
get it. Two things it has to do: `FitWidth` shrinks the line until it fits (150 -> 114 for
"MIYAMOTO DIED"; "K.O." and a long name cannot share a size), and `Shape` tracks each line
separately, because tracking a string with a newline in it spaces the newline out too and knocks
both lines off centre. **No font was added**: the project still ships only `LegacyRuntime.ttf`, and
an OS serif would fall back to Arial in a web build, so the dimensional look is built from the
stack rather than from a typeface. A free Roman serif (Cinzel is OFL) would be a closer match to
the logo if it is ever worth the download.

Death now reads as the user asked - the name, with DIED under it:
`stage.Announce(f.CharacterName + "\ndied", BloodLit, 2.0f, null, 140)`.

### 2026-10-20 — Richard the third fighter: sword, kite shield, and a roster that holds three

Richard ("Tanke") is in: his own rig, his own weapons, 8 moves, 12 combo strings, and a character
select that now carries three fighters instead of a hard-coded pair.

**The model.** The Tripo knight in `Characters/Richard/` arrived unrigged (5340 verts, T-pose,
empty hands). It went through the **Mixamo auto-rigger** in the user's own browser — chin, wrists,
elbows, knees and groin markers, Standard Skeleton (65 bones), symmetry on — and came back as
"Sword And Shield Idle.fbx", which is the rigged source. `ArtSource/Blender/prep_weapon.py` and a
new `prep_shield.py` squared the sword (1.10 long, grip at z=0) and the shield (0.78 tall, boss on
+Y, grip at the origin), and `ArtSource/Blender/richard_body.py` mounts both on the hand bones and
exports `Characters/Richard/Model/Richard_Body.fbx`.

Two mounting traps, both cost an hour: **the Mixamo FBX imports already posed**, so the weapons
must be placed with `armature.data.pose_position = 'REST'` (and exported that way); and after bone
parenting, assigning `matrix_world` uses a stale parent matrix — the basis has to be computed
against the bone's TAIL space explicitly:
`bone_space = arm.matrix_world @ pb.matrix @ Matrix.Translation((0, pb.bone.length, 0))`.
A third: `pose_bone.matrix` is armature space with **unscaled** rotation, so inverting
`arm.matrix_world` (scale 0.01) injects a ×100 error — the IK missed by 5.7 m until the targets
went through `to_armature_space()`.

**The animation.** The Pro Sword and Shield pack (51 clips) covers everything except the two moves
the brief actually names: there is **no thrust and no shield bash** in it. Those are keyed on
Richard's rig in `richard_clips.py`, along with the shield charge and the double-high swing. The
measurements and the casting table are in `Design/Anim_Inventory.md`.

**The authoring.** New `Editor/CrucibleRichard.cs` holds his whole move set in one table and three
ordered menu items (`Crucible > Characters > Richard > 1/2/3`): write the AttackData + FighterData
assets, copy `P2_Miyamoto.controller` into `P3_Richard.controller` renaming 11 states, and clone
Player2 into Player3. Then the shared menu items finish it, unchanged in shape:
special/riposte states, `Use Mixamo packs` (now with a P3 map, and `Custom` as a pack name), combo
strings. `CrucibleCharacterSetup` grew a `TwoHanded` list — Richard is not on it, so he gets no
`TwoHandGrip` IK dragging his shield arm onto the hilt.

**Three fighters broke two things that only ever saw two.**
1. `Fighter.FindOpponent` returned *the first other Fighter in the scene*. Richard's heavy hit the
   benched Bjorn, in a match against Miyamoto, and the log read "P1 HIT P1". It now picks the
   active fighter with the other player number, and `GameMenu.ApplySides` calls the new
   `RefreshOpponent()` on both sides once the numbers are handed out.
2. `GameMenu` assumed the roster WAS the match: `ApplySides(int)` gave everyone else Player 2,
   `VsRoutine` computed "the other one" as `p1Character == 0 ? 1 : 0`, the K.O. winner was "any
   fighter that is not the loser", and staging placed exactly two. Now `ApplySides(first, second)`
   switches the non-participants OFF, select takes two locks (Player 1, then Player 2 — the title
   says which), `MoveCharacter` walks past the pick the other side holds, and `StageFighters`
   lines up N fighters and frames them.

Play-verified at `timeScale` 0.7 against Miyamoto: Lion Thrust 7, Cross Slash 9, Crusader Hew 12
(all landing on the blade sweep), Shield Charge 14 with a 0.60 lunge, Shield Bash 15, Twin Hew 16,
no errors. Select shows all three with their cards (Richard: health 105, stamina 110, armour 24%,
strikes 7/9/12) and the live rigs posed behind them.

Open: his ground offset (−0.480/3) was measured off the bind pose, so if his feet float in the
Mixamo idle it is that constant in `CrucibleCharacterSetup.GroundOffset`. The AI drives his normals
but, like the other two, cannot dial a string or throw a special on purpose.

**Balance pass the same day (user's archetype brief).** "Miyamoto fastest, least damage, lowest
health, mid stamina; Bjorn a little more stamina but higher cost, slower, highest damage, mid
health; Richard most health, lowest stamina, damage between, slower than Bjorn." Two things were
in the way: Bjorn and Miyamoto **shared one `FighterData`** ("test Fighter"), so neither speed nor
damage could differ, and `FighterData.AttackSpeed`/`DashDistance` turned out to be **dead fields**
that nothing reads — attack speed is only the AttackData frame counts.

So the numbers moved into one place: `Editor/CrucibleBalance.cs` holds all three fighters and all
25 moves, and `Crucible > Combat > Apply the roster balance` writes the data assets, the
AttackData and the scene Fighters. `CrucibleRichard` no longer carries damage or frames — it
authors the SHAPE of his moves (trigger, height, hitbox, aim, which dash evades) and calls the
balance table.

Shipped: Miyamoto 80/100/8 x0.92 walk 4.4 regen 20 · Bjorn 95/110/14 x1.10 walk 3.8 regen 17,
heavy costs 23 and Blood Eagle 54 so his bigger tank empties faster · Richard 115/85/22 x1.00
walk 3.2 regen 14, and because his low pool fights his own shield the user approved paying him
back with the best guard (85% at 2/s drain, against 80/3 and 60/3). Startup ladders light
6/10/11, medium 9/18/15, heavy 16/18/22 — which fixed two inversions: Miyamoto's heavy had been
the SLOWEST in the game (23) and Bjorn's the fastest (15).

Verified in play, Bjorn vs Richard: the axe does 11 into Richard (13 x 1.10 against 22% armour
less 5 pierce) and the hew does 10 back (11 x 1.00 against 14% less 8 pierce), pools read 115/85
and 95/110. Frame changes re-time the clips, so `Use Mixamo packs` was re-run: Miyamoto's light is
now 0.25 s, Richard's 0.38 s; heavies 0.53 / 0.63 / 0.73 s.

### 2026-10-21 — Richard's shield turned round and his sword off his forearm

Two mount fixes in `ArtSource/Blender/richard_body.py`, both measured rather than eyeballed, then
re-exported to `Characters/Richard/Model/Richard_Body.fbx` and rebuilt with
`Crucible > Characters > Rebuild Player3`.

**The sword was crossing the forearm** because the blade sat on the hand bone's thumb axis and that
axis leans about 17 degrees back toward the elbow (measured in the idle pose: thumb dot
wrist->elbow = +0.29). A `tweak` of -20 degrees about the weapon's local X squares the blade with
the forearm - in the idle it now stands up and forward at roughly 50 degrees. Worth knowing: the
tweak is a rotation in the weapon's own frame applied AFTER the axis mapping, and because the
mapping is written in the hand bone's axes it means the same thing in every pose, so it can be
reasoned about at rest and trusted in animation.

**The shield was upside down AND back to front.** Point-up came from mapping its top (+Z) onto
`along`; it now goes on `thumb` with a -24 degree tweak, which in the idle puts the point straight
down and the face within a degree of the fighter's forward. Back-to-front was a bad assumption
inherited from `prep_shield.py`: its boss test picks the side whose centre band bulges furthest,
and on this asset the **grip bar stands prouder than the dome**, so +Y came out as the arm side.
The mount now takes the face from `-Y` and the docstring carries the caveat.

**The fist poked through the board** for two reasons, and the second only showed up on a close-up
render: the shield's origin is the TIP OF THE BOSS with the whole mesh behind it (not the grip, as
the comment claimed), and the hand sat 7 cm above the boss where the shell curves away. Centring
the boss on the fist (`thumb` 0.088) and pushing the board out along its own normal fixed it. The
push was picked by sweeping 0 to 15 cm inside one Blender run and counting hand vertices with no
shell in front of them: 17 of 266 at zero, 1 at 3 cm, 0 from 6 cm on. 6 cm it is -> `along` 0.235.

Two measurement traps for next time: sampling "hand" vertices within 12 cm of the wrist bone only
catches the palm (a hand is ~19 cm long, and it was the FINGERS poking through), and neither a
`closest_point_on_mesh` normal test nor a ray-parity test can say whether a weapon is inside this
body - the mesh is not watertight, cape and skirt are open shells. What does work: ray-cast from
the body vertex along the shield's own face normal and ask whether any shell is in front of it.
A side-view render is not evidence either - the hilt that looked buried in the forearm was just
depth overlap, which a render looking straight down the forearm settled in one frame.

**And his idle was the wrong one.** The pack ships four. Measured over each clip - head yaw,
hand height and forward reach, and the total distance the shield hand travels - they fall into
two groups: `idle` (109 f, 1.17 of hand travel), `idle (2)` (224 f, 2.93) and `idle (3)` (261 f,
4.58) are loose, weight-shifting, look-around idles, while **`idle (4)` (76 f, 0.08)** is a
settled fighting stance that only breathes, with the shield already up (hand 0.13 above the hips
and 0.35 in front). It also loops better: a 0.3 cm worst-bone gap and no hips drift, against
2.0 cm and 0.7 cm for the clip he had. `Richard_Idle` in `CrucibleMixamoSetup.P3Map` now points
at it. No barbarian idle was needed - the axe-pack idles put the hands where an axe is, not where
a shield is.

### 2026-10-21 — The camera was framing a benched fighter

"Why is the camera angle changed, it should be closer and centered between both." The scene file
turned out to be innocent - reading transforms while the Editor is PLAYING gives runtime values,
and the first look (Player1 inactive at a 199-degree yaw, 0.12 high) was Bjorn's corpse from a
K.O., not something saved. On disk the layout was exactly as committed.

The bug was `FightCamera.Start`: it resolves `player1`/`player2` once, by player number, and
`ApplySides` hands those numbers to a different pair every match. Playing Miyamoto vs Richard,
the camera was still following Miyamoto and **Bjorn**, who was switched off and standing on his
spawn mark - so it centred on the midpoint between a fighter and a bench, and swung off-axis
(10 degrees of yaw, 2.3 units left). Exactly the `FindOpponent` bug from the day before, in a
second place. `PlaceFighters` now calls the new `FightCamera.RetargetToFighters()`: re-find the
pair, snap back to the camera's authored transform, re-read the framing. The authored transform
has to be remembered in `Awake` (`homePosition`), because `StageFighters` moves the camera for the
select screens and `Unstage` never puts it back.

With the right pair in frame the view was still the old one, so the camera moved too, by the
user's two words: **centred** on the fighters' midpoint (x 0.93 instead of -0.62, which had put
them a ninth of the screen off to the right) and **closer** - 6.6 units back instead of 9.5, which
takes the pair from about a quarter of the frame height to about forty percent. Checked at a
6.5-unit gap, far wider than a dash: `zoomOutPerUnit` 0.5 keeps both in frame with room to spare.

**The post-move dead time came down to 0.85 s**, on the user's call. It was three separate
constants in `BalanceConfig`, not one: `STRING_LOCKOUT` 1.5 (after a combo), `ATTACK_CHAIN_LOCKOUT`
1.5 (after the second repeat of the same button) and `SPECIAL_LOCKOUT` 1.0 (after a special). All
three are 0.85 now, so whatever a fighter just finished the gap reads the same. No fighter
overrides them - the three `CombatSystem` fields (`stringLockout`, `sameAttackLockout`,
`specialLockout`) are all 0 in the scene, which means "fall back to BalanceConfig", so the
constants were the only place to change. Verified live: all three log lines read 0.85 s.
Worth remembering: the lockout is GAME time, and the Settings game speed (0.85) stretches it on
the clock - 0.85 s of lockout is about one real second at the default setting.

### 2026-10-21 — Salaadin, the fourth fighter: everything that does not need the rig

The post-move dead time came down to 0.85 s first (above), then the roster grew.

**Salaadin** - falchion and round shield, the balanced one. His source meshes were already in
`Assets/_project/Characters/salaadin/`: the Tripo body (`islamic knight 3d model`), the
`ornate curved sword` and an `ornate round shield`. The user settled the open questions:
slashes as his normals and kicks in his specials; the kicks hand-keyed in Blender rather than
downloaded; 95 hp, 115 stamina, 18 armour, damage x0.92, walk 4.1; and - asked later - he MAY
share Richard's two walk clips, but nothing else of Richard's.

Done this session, all of it ahead of the rig:

- **Weapons prepped and verified.** `Salaadin_Falchion.fbx` (4,036 verts, 0.90 long, grip at the
  origin) and `Salaadin_Shield.fbx` (4,085 verts, 0.80 across). The shield was RENDERED from both
  faces before trusting the script, after Richard's boss test lied: +Y really is the outward face
  (crescent-and-star boss, riveted rim), -Y the back. It is a **centre-grip round shield** - one
  vertical bar across the middle, held in the fist, not strapped like Richard's kite. Its origin
  sits 0.096 ABOVE the disc centre, because prep_shield puts the origin where a kite shield's grip
  belongs; `salaadin_body.py` pays that back with `thumb = 0.096`.
- **A real bug in `prep_weapon.py` / `prep_shield.py`**: the .fbm texture scan matched Unity's
  `<name>_basecolor.jpg.meta`, which sorts AFTER the jpg and overwrote it, so the load threw. It
  never bit Richard because his sources sat outside `Assets/`. Both scripts now filter by image
  extension (`IMAGE_EXT`).
- **`Editor/CrucibleSalaadin.cs`** - his whole move set, built on the Richard pattern but cloning
  **P3_Richard** as the controller template (same sword-and-shield shape, specials already wired):
  Saif Cut / Crescent Slash / Hattin Cleave, specials Veiled Rush / Breaking Kick / Whirling Saif,
  ripostes Falcon (dash) and Heel (parry, a kick, `hitsWithWeapon` off).
- **`CrucibleBalance`** gained his row and his eight moves. Start-ups sit between Miyamoto's and
  Bjorn's (8/12/17 against 6/9/16 and 10/18/18). NOTE: his assets live in a LOWERCASE `salaadin`
  folder, so `WriteMove` now goes through a `Folder()` mapping.
- **`CrucibleMixamoSetup` P4Map**: clips measured, not guessed. Light `slash (3)` (48 f, the most
  compact cut left), medium `attack (2)` (40 f, only 0.5 of vertical travel - genuinely flat),
  heavy `slash (4)` (70 f, 1.87 of drop), dash riposte `attack (3)`, death `death (2)`. Richard's
  `slash`, `slash (5)`, `attack (4)`, `idle (4)` and the pack's one `kick` are left alone.
- **`ArtSource/Blender/salaadin_body.py`** - his mount, copied from Richard's with a centre-grip
  shield table. Starting numbers only; they get the render check when the rig lands.

**The idle problem, written down so it is not rediscovered**: of the pack's four idles Richard
holds the only settled one. `idle`, `idle (2)` and `idle (3)` are the wandering ones the user
rejected for Richard (hand drift 0.32 / 0.73 / 0.68 against idle (4)'s 0.08) and `block idle` is
frozen stiff (drift 0.01). So Salaadin's idle is a FIFTH hand-keyed clip: block idle's guard
stance with a breath keyed on top.

**Still to do** (all of it needs the Mixamo auto-rigged body, which the user was producing):
`salaadin_clips.py` (guard idle, rising slash, front kick, spinning double, heel kick), run
`salaadin_body.py` and check the grips in renders, the three `Crucible > Characters > Salaadin`
menu items, a `Rebuild Player4` body step, then the shared wiring menus and a play test.
Nothing in `GameMenu` hardcodes a roster size - `roster` is discovered - so the select screens
should take a fourth fighter without a code change. That is a claim to verify, not a result.

### 2026-10-21 — Salaadin animates: his own five clips, and the hip-travel trap

Finished the fourth fighter. He now fights with nothing of Richard's but the two walks, which
is what was asked for.

**His own clips.** `ArtSource/Blender/salaadin_clips.py` keys five the pack cannot give him:
`Salaadin_GuardIdle` (the pack's block idle with a breath over it, one sine so frame 0 == frame
72 and it loops), `Salaadin_VeiledRush` (run-in behind the shield into a rising cut),
`Salaadin_BreakingKick` (front kick, then a low cut), `Salaadin_WhirlingSaif` (a full turn with
two cuts in it) and `Salaadin_HeelKick` (the parry riposte: the same boot, shorter). Richard's
four were the starting framework; all five builders are new, and the controller no longer holds
a single Richard clip.

Three things had to be fixed before any of it worked, and all three are the same lesson -
**a number lifted from another character's rig means nothing until it is converted**:

- **Hand targets are now fractions of that arm's own reach, measured from its own shoulder**,
  not metres from the hips. His rig is 1.00 tall against Richard's 1.85, but its arms are 0.197 -
  about half human proportion - so scaling Richard's targets by height put every one of them past
  his fingertips (the guard pose missed by 0.021, a thrust by 0.174). `pose()` also pulls in any
  target longer than 0.97 of reach instead of quietly missing it.
- **The kicks are foot IK, not bone bends.** Rotating a leg bone about its own X swings it
  sideways on this rig: the first build rendered as a crouch with no leg in front of him. The
  arms' analytic two-bone solver now drives `UpLeg/Leg/Foot` as well, poled forward so the knee
  cannot hyperextend.
- **Hip travel is carried as a fraction of the source rig's hip height.** The packs are authored
  on an armature scaled 0.01, so their hips translation is in hundredths against his metres.
  Applied raw it dropped him 52 hip-heights through the floor - Unity read `RootT.y` **-52.4**
  where the pack's own block idle reads **0.796**. `take()` now normalises it on the way in and
  `apply_frame` puts it back in the target rig's units; his five clips land at 0.82-0.94, inside
  the band the pack's own clips occupy. **`RootT.y` on the imported clip is the check** - read it
  before believing any hand-built clip.

Also: his shield mounts with its boss on **-palm**, so `aim()` takes `(UP, -FWD)` where Richard's
takes `(UP, FWD)`. Richard's value shows the opponent the back of the shield, which is exactly
what the first render did.

**Combat.** `CrucibleCombos.cs` gained his table - the same three shapes as the others, but every
special he can dial is a kick or a turn: light buys the Breaking Kick, medium the Whirling Saif,
heavy the Veiled Rush, and each special hands a normal back. Eight attacks, 2/2/2/1/1/1/1/1 links.

**Play-verified** Salaadin (P1) vs Bjorn: all three specials fire their own clips and land -
Bjorn 100 -> 89 -> 76 -> 66% - the Veiled Rush lunges in and the shield reads correctly facing
the opponent through the cut. No game-side console errors.

**Committed** as `3bb1150` on branch **`salaadin`** - 297 files, ~83 MB. It carries this session's
work plus everything that had never been committed before it: Richard's character assets, his
four keyed clips, the Sword and Shield pack, the per-fighter data assets and the 0.85s lockout.
Splitting those out would have left a commit that cannot open the scene. `main` is still behind;
land it with `git merge --ff-only salaadin`. Nearly all of the 83 MB is weapon basecolor JPGs at
4-6 MB each - the first thing to downsize if the repo starts to feel heavy.

Still open on Salaadin: his ground offset came from the rest pose and has only been eyeballed
standing; the cape has not been watched in motion at full speed; and the AI has never driven him.

**Next session starts from the user's new task list**, not from `Plan_LiteMobile.md`'s running
order - Task 7 (teaching the AI the lite game) was dropped and the list that replaces it has not
been written down here yet.

### 2026-10-21 — Battle Now, Salaadin's move set reshuffled, and the bind pose that ate every authored clip

Three asks, one of which turned out to be a pipeline bug rather than a pose problem.

**1. Main menu.** Quick Battle is gone — the row, `QuickSummary`, `QuickBattle`, the `LastSetup`
class, `LoadSetup`/`SaveSetup` and the `crucible.lastsetup.v1` pref, all of it. Nothing else read
any of it. "Local Play" is now **Battle Now** (the row and the setup screen's title).

**2. Salaadin's move set.** His Medium and Heavy traded places with SP + Medium and SP + Heavy,
and every kick came out of him (the user's call: "no kicks anywhere on him").

| slot | move | clip |
|---|---|---|
| Light | Saif Cut | pack `sword and shield slash (3)` |
| Medium | **Shin Cut** | `Custom/Salaadin_ShinCut` (the old Breaking Kick with the boot cut off) |
| Heavy | **Whirling Saif** | `Custom/Salaadin_WhirlingSaif` |
| SP + Light | Veiled Rush | `Custom/Salaadin_VeiledRush` |
| SP + Medium | **Crescent Slash** | pack `sword and shield attack (2)` |
| SP + Heavy | **Hattin Cleave** | pack `sword and shield slash (4)` |
| dodge riposte | Falcon Riposte | pack `sword and shield attack (3)` |
| parry riposte | **Saber Riposte** | `Custom/Salaadin_SaberRiposte` — a cut, not a boot, so `hitsWithWeapon` is ON where the other three fighters' parry ripostes have it off |

The AttackData assets were renamed to match their new slots, so `CreateMoves` now **sweeps the
Attacks folder of anything the table no longer names**, and `Salaadin > 4 Rewire Player4 to the
move set` re-points the scene's CombatSystem lists without rebuilding the whole fighter (it holds
the assets by GUID). `kick()` / `kick_keys()` are gone from `salaadin_clips.py`.

**The numbers did not move with the moves.** A row in `CrucibleBalance` is a SLOT, so a normal
still costs a normal's stamina. The visible consequence: the two showpieces now play at about
**x2.7** to fit a normal's window (Shin Cut 25 f, Whirling Saif 34 f). Widening those frames is a
balance decision and was left alone.

**3. "Both elbows broken, shield facing up, arms like he has wings."** Not a posing mistake — the
poses were right in Blender every time. **An armature-only FBX carries no skin, so it has no bind
pose, and Blender's exporter writes the pose it is evaluated on at export time as the file's node
transforms, baking the animation relative to them.** Unity then builds the clip's own avatar from
that pose, so a clip that opens in the guard makes THE GUARD the T-pose and every retargeted frame
lands as the body's own T-pose plus a delta. Salaadin's guard idle barely moves, so the delta was
nothing and the clip played as the pure bind T-pose: arms straight out, elbows locked, shield flat.

Measured, in Blender: author the guard (left elbow 113°, hand at `(0.132,-0.457,0.654)`), export,
re-import → elbow 158°, hand at `(0.347,-0.044,0.721)` — the rest pose, exactly. Six export-flag
variants changed nothing; `pose_position='REST'` fixed the written rest and killed the animation.

`rest_lead_in()` in both `salaadin_clips.py` and `richard_clips.py` fixes it at the source:
shift every key by one, key the real rest pose at frame 0, then `frame_set(0)` **and**
`view_layer.update()` so the exporter is evaluated on the rest pose — without that update the
depsgraph is stale and it writes the last pose built anyway (that cost two wrong attempts: first
the lead-in keyed the guard again, then the clip and the lead-in swapped places). The scene is
renamed `restlead`, which becomes the FBX take name, and `CrucibleMixamoSetup` drops frame 0 from
any take by that name (including through a `Trims` window).

Two real posing bugs were under it:
- **The elbow pole had no vertical term.** Richard's is taken from the hips, most of an arm's
  length below the shoulders; Salaadin's was written from the shoulder as
  `lat*0.6*sign - fwd*0.5`, poling both elbows out level — wings even once the pose survived.
  Now `lat*0.45*sign - fwd*0.40 - UP*0.85`, in units of arm span.
- **The falchion is mounted with a -20° tweak** (`salaadin_body.py`), so asking `aim()` for a
  blade direction put the spine 20° out of it and the edge 20° out of square: the guard carried
  the edge behind straight down and every cut sliced a little with the spine. `pose()` now turns
  the wrist back by `BLADE_LEAN` before aiming. Measured in play: blade·forward **0.94**,
  edge·down **0.94**, shield boss·forward **1.00** — tip at the opponent, sharp side leading, the
  decorated face of the shield square on.

The shield's `aim()` arguments were right all along; it only ever looked "up" because the clip was
playing the bind T-pose, where the palm faces down and the boss (on `-palm`) therefore faces up.

**Richard's four authored clips still carry the old offset.** They travel far enough to still read
as a thrust and a bash, so nobody noticed, but `Richard_Thrust` frame 1 is measurably his T-pose
rather than his guard. `richard_clips.py` is fixed; his FBXs were deliberately NOT rebuilt, since
that visibly changes a character nobody complained about.

**And one trap on top: "2 Build controller" writes a FRESH `P4_Salaadin.controller`**, so its GUID
changes and the scene's Animator reference goes null - Player4 then stands in the bind T-pose with
no animation at all, which looks exactly like the bug above. `RewirePlayer4` now reassigns the
controller as well as the move set, and says so.

**The shield, chased to the bottom.** With the bind pose fixed the guard still arrived with the
shield 76 deg off square, while the clip sampled onto Player4 in the Editor reproduced Blender to
two decimals. Three things were wrong, found in this order, and the first diagnosis was wrong:

1. **A dead end worth recording.** `HumanPoseHandler(avatar, root)` on these fighters returns the
   SAME muscle values whatever the rig is doing - identical left and right arms, unchanged between
   the idle and a mid-spin. The `Forearm Stretch = 0.98` it reported was not a clamped limit, it
   was noise. Muscle values from it prove nothing here; compare bone rotations instead.
2. **The clip's avatar thought the rig was 100x too small.** An armature-only FBX has one object,
   so Unity makes the armature the file ROOT and the 100x scale Blender writes on it lands on the
   avatar's root - where the body FBX, which carries a mesh too, has root(1) -> Salaadin_Rig(100)
   -> Hips. Human scale is read in root space, so the two avatars disagreed by 100x and every
   stretched joint was rewritten in the retarget: the elbows came out 16 deg tighter than authored.
   `export()` now writes an empty named `<Name>_ClipRoot` beside the armature, which keeps the
   armature a child of the file root. That fixed the elbows exactly (113.3 / 125.7 against the
   authored 113.4 / 124) - and, as a bonus, made `CopyFromOther` validate, which it never had.
   `ImportAll` now gives every authored clip its character's own avatar (`OwnerAvatar()`), so the
   retarget is a literal identity. Pack clips keep their own; they are shared between bodies.
3. **And the shield was still 66 deg off even then** - because a SHARED avatar was never the
   problem. Every bone was off by the same 66.6 deg, which is the signature of a ROOT ORIENTATION
   offset, not a joint fault. `Root Transform Rotation -> Based Upon: Body Orientation` makes Unity
   work out which way the fighter faces from the posed body and bake the difference into every
   frame. Salaadin's guard is bladed, so Unity read his turned shoulders as his facing and yawed
   the whole skeleton. Authored clips now import `Based Upon: Original`; pack clips, which really
   are captured facing anywhere, keep Body Orientation.

Measured live after all three, against the clip as authored: **shield boss . forward 1.00**, blade
tip . forward 0.94, edge . down 0.94, elbows 113/128, and every bone within the idle's own breath
(hands and spine 0.0 deg, left arm 2.8 deg). From dead in front he presents the full face of the
shield - crescent boss, star pattern, studded rim - square at the opponent.

**Two lessons worth the scar tissue**: `clip.SampleAnimation` does NOT go through the Animator's
retarget, so it shows a clip as authored and hides both faults above - measure in PLAY mode off the
live rig. And when every bone is off by the same angle, stop looking at joints.

**Richard's four clips were rebuilt too**, through the same fixed script, so his thrust, charge,
bash and double-high no longer carry the old bind-pose offset either; `ImportAll` falls back to a
clip's own avatar with a warning if a copied one does not fit, so an un-rebuilt clip can never
silently lose its animation.

Verified in play mode (Salaadin vs Richard and vs Miyamoto, Al-Quds): the guard matching the
authored clip bone for bone, both fighters' 22 animator states pointing at a clip, the Move List reading
the new set off the live AttackData, and the main menu showing Battle Now with no Quick Battle row.

### 2026-10-21 - Salaadin's rig: Mixamo cannot see him, and Unity cached the old skeleton

The user: "why in the game scene salaadine is not even in a Tpose like the others, he is broken he
got a streatch leg, and his arm is like a snake, i think it should be re rigged. and humanized."

He was right, and it was upstream of every fix from earlier in the day. Measured on the raw Mixamo
download, before any of our scripts touch it: upper arm 8.0% of height against Richard's 14.6% and
SHORTER than his own forearm, because the elbow landed 41% of the way from shoulder to wrist
instead of ~52%; knees bound at 149/135 deg against Richard's 167/165. The mesh rendered on its own
is well proportioned and stands with straight symmetric legs, so a re-rig, not a re-model.

The user redid the Mixamo pass with the markers placed carefully. **It came out worse**: symmetric
now (the placement was good) but the elbow moved the wrong way to 35% and the knees bent to 128 deg
with the joint 11.6% of height off the leg axis. The reason is structural, and worth remembering -
his mesh is 27 loose parts and the LEG GEOMETRY STOPS AT z=0.33, the top of the boots. Above that
there is only coat skirt and cloak (which reaches y=0.22 behind him). Mixamo fits its skeleton to
the mesh VOLUME, so where the thigh should be it finds fabric and puts the knee in the skirt. The
knee marker's HEIGHT was right at 51%; it is the DEPTH, which Mixamo infers, that no front-view
marker can reach. Same story at the elbow, where the shoulder cape swallows the upper arm.

So the skeleton was corrected by construction instead of by fitting: `salaadin_fixrig.py` moves the
knee onto the straight hip->ankle line and the elbow onto the shoulder->wrist line at 52%, in EDIT
mode, which moves the rest pose while the mesh and the vertex groups stay where they are. Mixamo's
weights are kept and nothing is re-weighted. Elbow 35 -> 52%, knees 128 -> 179 deg, 11.2% -> 0.2%
off the axis. The weapon mounts needed no change, because only the elbow moved and the hand bones
were never touched.

**Then the fix did not appear in the game, and the reason cost an hour.** The body FBX measured 52%
and the scene's Player4 measured 52% in edit mode - but 41% in PLAY mode. `humanDescription.skeleton`
caches every bone's position in the .meta, and Unity builds the avatar from that cache rather than
from the file. Edit mode reads the file; play mode reads the avatar. `avatarSetup =
CreateFromThisModel` does not refresh the cache - the stored `skeleton` and `human` arrays have to
be cleared before the reimport. `CrucibleCharacterSetup.Rebuild` now clears them every time, which
is what "rebuild the body from the FBX" should always have meant.

Verified in a live match against Richard: elbow 52%, bind knees 179 deg, shield boss . forward 0.89,
22 states and 0 null clips, console clean. He stands and fights like the other three.

Carried forward: his corrected rig is NOT in git - `Characters/*/Model/` and `ArtSource/Characters/`
are both gitignored, so a fresh clone must re-run `salaadin_fixrig.py` then `salaadin_body.py`.
Richard's own rig was never re-fitted and does not need it (Mixamo found his limbs because he wears
plate, not a cape).

### 2026-10-21 - Salaadin, continued: the splits, and the 23 degrees behind them

"salaadin is completely messed, like he is doing a split, however i tried few animations on mixamo
it was fine, but i think it needs to be rigged on blender."

Both halves of that were right, and the second half explains the first. On Mixamo's own site the
rig animates fine because Mixamo retargets to the rig it built. Our clips do something different:
`apply_frame` in `salaadin_clips.py` copies the PACK's leg bones rest-relative onto his rig, which
is only valid when both skeletons' legs rest the same way. The pack thigh hangs at
(0.00,-0.01,-1.00) and Richard's at (+0.08,+0.13,-0.99) - both vertical. Mixamo left Salaadin's at
(-0.01,+0.39,-0.92), **tilted 23 degrees backward**, because it had put his hip socket forward
inside the coat. Every copied leg rotation landed 23 degrees out and compounded down the knee.
His ARMS were never affected, which is why only the legs ever looked wrong - the clips key arms
with IK and only the legs come through apply_frame.

Getting there took four passes at the joints, each one uncovering the next, and all four are now in
salaadin_fixrig.py: elbow to 52% of shoulder->wrist, knee onto the straight hip->ankle line, hip
joints in to 11% of height apart and down to 52% up, and finally the hip's fore/aft over the ankle.
Bone roll is preserved throughout (align_roll to each bone's saved z axis) - a roll angle means
different axes once the head or tail moves, and I lost a pass to that.

With the rig correct, his guard then reproduced the pack's block idle EXACTLY - L 61.5 / R 48.0
against the pack's 61.5 / 49.2 - and that is when the real nature of the thing showed: the pack's
block idle plants the feet 94 degrees apart. It reads as a wide guard on the pack rig and on
Richard, who is short-legged and already stands wide at rest, and as the splits on Salaadin, who is
long-legged with his feet together. So the last step was not a rig fix at all: LEG_DAMP = 0.55 in
salaadin_clips.py eases each leg rotation back toward rest, keeping the shape and taking the span
out. 94 -> 50 degrees, against Richard's 56.

Verified live: idle 50, walk 31, shin cut 50, whirling saif 50, veiled rush 35; guard still shield
boss . forward 0.98, blade edge . forward 0.94, elbows 118/129, 22 states and 0 null clips.

Also fixed on the way, and worth more than the rig: **humanDescription.skeleton caches every bone's
position and Unity builds the avatar from the CACHE, not the file.** Re-export a joint in a new
place and nothing changes at runtime. The tell is that the same hierarchy measures correctly in
edit mode and wrong in play mode - edit mode reads the file, play mode reads the avatar.
CreateFromThisModel does not refresh it. Rebuild Player<N> now clears the stored arrays every time.


### 2026-10-21 — The bones were the problem, and so was the IK

The user's read on the last screenshot was right: Salaadin's bones are not where his body is.
Checked it twice, in Blender and in Unity, and then audited all four rigs with a headless Blender
pass that measures each bone against the geometry it actually drives.

**Salaadin.** His whole leg chain runs down inside the cloak, 0.09–0.10 behind the legs on a body
1.0 tall — LeftUpLeg 14.6% of body height off, RightUpLeg 11.8%, the shins ~11%. The foot bone
splays 30.2° where the shoe's own heel-to-toe axis is 8.1°; the ankle sits at 12% of height against
a human's ~4%; the toe bone ends past the toe tip and below the floor. The torso leans 19.5° back,
which Unity's zero-muscle T-pose confirms (pelvis→chest −0.333 in z, Richard −0.044), so every
retargeted clip inherits the lean. Knees and elbows are perfectly collinear in the T-pose — no bend
hint at all, where Richard has a 13° knee and 22° elbow break. `salaadin_fixrig.py` fixed the hips
"over the ankle", but the ankle was itself too far back, so the error propagated up the leg.

**The hands.** Bjorn and Miyamoto have no finger bones — 41-bone Tripo rigs that stop at
`L_Hand`/`R_Hand`. So the two-handed grip is entirely `TwoHandGrip` placing a rigid fist. And that
was not working either: the off hand settled 0.29–0.56 world units (12–25% of body height) from the
haft. It is not reach — their arms measure 37–40% of height and the socket is well inside it.

Unity's humanoid IK is simply inert here. With the layer's IK Pass on, layer weight 1,
`OnAnimatorIK` running thousands of times and `GetIKPosition` reading the goal back correctly, a
15 cm lift asked of the left hand moved it **0 cm** — on all four fighters, Richard included, so it
is not an avatar fault. Nothing in the project rewrites arm bones afterwards either. Rather than
keep excavating Unity's internals, `TwoHandGrip` now solves the off arm itself in `LateUpdate`:
elbow onto the circle both bone lengths allow, swung toward the existing `leftElbowHint`, then each
bone aimed in turn. Off-hand miss **0.29–0.56 → 0.0000**, elbow 78–102°, verified in idle and
through a heavy swing. It takes `[DefaultExecutionOrder(100)]` so it runs after `WeaponAim` has
placed the weapon, otherwise the hand chases where the weapon was.

Next: Salaadin's legs, feet and spine, by construction in `salaadin_fixrig.py`, then the rebuild
chain. Miyamoto's left forearm (outside the sleeve) and the two waists after that. Real fingers for
Bjorn and Miyamoto need a re-rig and are not patchable.

### 2026-10-21 (later) — Salaadin's feet, and the arm bones that were never in the arm

Finished Salaadin, then went after the user's real complaint: "bjorn and miyamoto has one hand
holding air", and bones sitting outside the models.

**Salaadin** — the foot pass in `salaadin_fixrig.py` landed: splay 30.2/32.1 deg -> 11.8/12.0,
ankle 11.6% -> 7.5% of height, toe tip from 0.022 BELOW the floor to +1.5% above it. Rebuilt the
five clips, ran the whole Unity chain. On the live rig the shin twist dropped 14.1 -> 2.6 deg and
the ankle roll +/-23 -> +/-11.5. His guard still reads as a wide crouch next to Richard, and that
is NOT the rig: stance width over leg length is **1.44 against Richard's 1.39**. It is his short
torso and the coat skirt.

**The grip was already fixed; the fingers are the wall.** Both fighters report
`offHandMiss=0.0000` and both hands land on the haft. Zooming the game camera onto Miyamoto's
tsuka shows why it still reads wrong: the hands are sculpted as loose half-open claws and
**there are no finger bones to close them** - 41-bone Tripo rigs stopping at `L_Hand`/`R_Hand`,
against Richard's 65 bones with all 40 finger bones. Nothing in code fixes that.
- A false alarm I talked myself into and then killed: `offHandPalmLocal` printing as
  `(0.00, 0.00, 0.00)`. It is ~0.0005 - Vector3.ToString is 2 decimals and the Tripo hand bones
  carry a ~220x scale. The palm offset was right all along.

**Miyamoto's left elbow was 2.67% of body height outside his own arm.** Tripo interpolates the
shoulder->elbow->wrist chain as a straight line; his left arm curves back in x and the bone did
not. Everything weighted to that forearm was being driven from a pivot in open air. All four arms
on both fighters had bones ~2-3% of height forward of their flesh.
`ArtSource/Blender/fix_tripo_arms.py` re-centres each forearm joint on the cross-section of the
flesh it drives. **Every arm bone on both is now inside the mesh**, and materials survived the
re-export. It leaves the upper arm alone on purpose - fur and shoulder plates poison the centroid
there, the same trap as Salaadin's coat.

**Two measurement lessons, both of which produced wrong answers first:**
- One ray gives false positives. Six rays and a majority vote return a clean 0/6 or 6/6 when the
  answer is real, and 2-4/6 when it genuinely is not.
- Probe the JOIN of every skinned mesh. Probing the biggest single mesh reported Richard's whole
  skeleton as 10-84% outside - it had picked one piece of him.

**Richard: one real finding, deliberately not acted on.** His hips sit at 38.8% of his rig's span
where Salaadin's corrected hips sit at 48.8%, and 1.5-2.3% of height forward of his ankles - the
same geometry that gave Salaadin the splits. Left for a deliberate pass: the probe is ambiguous
there, his clips are play-verified, and a 9%-of-height hip move re-times all of them.

### 2026-10-21 (later still) — Fingers for the off hand, and the grip bug they exposed

The user asked for fingers in Bjorn's and Miyamoto's LEFT hands specifically, inside the mesh,
because both carry a two-handed weapon. Built them, and found the grip was wrong in a way the
old metric could never have shown.

**The rig.** `ArtSource/Blender/add_fingers.py` traces five chains per left hand out of the hand
mesh that was already there. Both rigs went 41 -> 56 bones; Unity's auto-mapper filled **15/15**
humanoid finger slots from the names alone. Every one of the 30 joints tests **6/6 inside**.
- The fingers fuse into the palm below the last knuckle - four components only appear at
  t>=0.74 (Bjorn) / 0.92 (Miyamoto). Tips measurable, knuckles not.
- **Slicing the hand at a station does not work on a curled claw**: the slice holds the finger AND
  the palm beneath it and the centroid lands in the gap. First attempt put whole chains outside.
  Tracking inward from the tip, anchored on the previous step, fixed it.
- Tracing still cannot guarantee containment, so each joint is then pushed to a verified interior
  point. Weights are redistributed from `*_Hand`, not rebuilt, so the hand deforms as before.

**Then the real bug.** With fingers on, they would not close - and the reason was not the fingers.
`offHandMiss` had been reading **0.0000 all along**, because the WRIST was exactly on its goal.
But the knuckles were 0.09-0.26 world units off the haft axis with fingers only 0.06-0.11 long:
**the hand had been placed beside the haft, not around it**, and no amount of closing could ever
have reached. That is the "holding air" the user saw, and the wrist-distance metric was blind to
it. `PalmOffset()` now measures where the knuckle line landed and re-solves the arm onto the haft.

Second wrong turn: curling each phalanx about the haft's own direction. That sweeps a finger
round a cone, and these fingers lie partly along the haft, so it barely closed them (0.20 ->
0.08). The bend axis has to be the plane that faces the shaft, `cross(fingerDir, toHaftPerp)`.

Verified in play: `offHandMiss` 0.0000, finger gap 0.039-0.071 at rest and **0.047 mid-swing**,
both hands visibly wrapped. Right hands are still mitts - Bjorn's right-hand mesh never separates
into fingers, so it cannot be traced the same way.

### 2026-10-21 (later still) — Two weapon mounts, and why a runtime tweak lasts one frame

Richard's sword hung about 0.08 world units ABOVE his fist (his hand closed under the grip, on air),
and Salaadin's falchion was rolled edge-up so every cut would have led with the spine. Both fixed.

**The trap.** Setting the weapon's transform in play mode LOOKS like it works and then quietly
reverts. `WeaponAim.Awake` caches `restLocalRotation`/`restLocalPosition`, and its `LateUpdate`
restores them before aiming - so a runtime change survives exactly one frame, and the screenshot
after it is the animation having moved on, not the edit holding. The giveaway was a measurement
that came back at the ORIGINAL value after a change that had reported success. The mount is
whatever sits on the scene transform at play time, so that is where the change has to be made.

**Richard**: the grip is now seated in the fist by construction - the sword is slid so the point 10%
along it (the middle of the grip, since the blade starts at 20%) lands midway between the wrist bone
and the middle knuckle. Measured 0.000 off the weapon axis, at rest and mid-swing, where it was
0.201 (6.4% of body height) before.

**Salaadin**: rolled 180 deg about the blade's own axis. Note `prep_weapon.py`'s "+Y is the edge" is
INVERTED for this asset - with local +Y pointing down the sharp bevel rendered UP. Trust the render
over the convention here.

Both are baked on the scene AND in `CrucibleCharacterSetup.ShieldWeaponMount`, which re-applies them
after `Rebuild Player<N>` (the rebuild reimports the FBX and would otherwise restore the Blender
mount). That table is the single source of truth; `{richard,salaadin}_body.py` now say so.

Still open: Salaadin's own fist is 0.163 off his falchion's axis (5% of body height) - the same class
of fault as Richard's, not touched because it was not asked for.

### 2026-10-21 (later still) — Salaadin's leg column and head bone, off the body they drive

The user, looking at a render of the studio file, asked for the lower-body bones, "specially from
under his knee", and said his hips were not under his head - with the warning not to confuse the
cape for the body. Both were real and both are fixed in `salaadin_fixrig.py`.

**Measure against the leg's OWN loose shells.** The body mesh is 27 separate shells, and the boots,
shoes and thigh plates are per-side shells that never reach above the hip, while the cape and the
robe are centre shells running to the shoulders. Selecting on that gives leg geometry with the
cloak provably excluded - which is the whole reason the numbers can be trusted. The old `off-limb`
metric cannot do this on a costumed character.

**The first measurement was off a STALE FILE.** `Salaadin_Studio.blend` is 18:59; the rig was fixed
at 20:20 and the body rebuilt at 20:21. Measuring the open .blend gave the pre-foot-pass rig (ankle
at 12% of height) and a knee reading of 5.7%H out. Check the timestamps before trusting a .blend.

**The knee was NOT 5.7% out, it was 2.9%.** The first number came from a band that caught only the
boot's top rim, which flares forward. A 3 cm band (143 verts) is the honest figure.

**What was wrong**: the leg column ran 2.4-4.2% of height BEHIND the leg from the knee up, and the
head bone 5.5% behind the head (no head geometry under the helmet for Mixamo to fit to).

**What was done**: hip and knee move FORWARD TOGETHER by one measured offset (3.1/3.2% of height),
so the thigh's rest direction is untouched at -0.4 deg - the property the copied pack rotations
depend on, and the thing that cured the splits - and the whole correction is spent on the shin,
which goes 0.4 -> 8.8 deg, inside the 7.5 deg Richard already rests at. The head bone runs up the
middle of the helmet. Results: knee +2.9%H -> -0.3%H, worst leg error 4.2% -> 1.5%, head bone
+3.1% -> -0.8%, hips-to-head lean -6.7% -> -2.7%H. Hip and ankle separation unchanged.

**His wide stance is NOT this, and is still open.** Live leg span is 74.3 deg against Richard's
54.2 and the 50 deg this log recorded as verified. Rebuilding with `LEG_FWD = 0.0` gave 74.6 - so
the leg pass costs nothing and the regression predates it. The cause is the FOOT pass earlier the
same day: it moved the ankle bones from 10.1% to 25.0% of height apart to put them in the actual
shoes, which widens every hip->ankle vector. Left alone pending a decision, since the ankles are
now anatomically right and it is `LEG_DAMP` in `salaadin_clips.py` that would have to absorb it.

### 2026-10-21 (night) — the user re-rigged Salaadin by hand; porting the animation back onto it

The user adjusted Salaadin's rig themselves, inside `ArtSource/Blender/Salaadin_Studio.blend`, and
asked for his animation put back on it. Three things had to be true for that, and only the first
was obvious.

**Find what actually changed before rebuilding on it.** Only the .blend's timestamp had moved, so
the edit lived there and nowhere else - the studio file is normally DERIVED from `Salaadin_Body.fbx`,
but once the rig is hand-edited it is the only copy. A headless rest-pose diff against the body FBX
is what told us what we were dealing with, and it is worth running every time:
- hips forward 0.15 and down 0.026, knees and ankles out to the width of the actual boots, ankle
  down from 12% to **3.1% of height** (human ~4%) - a real, coherent lower-body fix;
- the four TOE bones deleted (65 bones -> 61);
- `HeadTop_End` dragged 21% of height sideways, which looks accidental and is harmless (Unity does
  not map that leaf), so it was left as the user saved it;
- no pose offsets at all, so it was EDIT-mode work, which is the kind that survives into the FBX.
Because the file is the pre-foot-pass studio build plus those edits, the head/leg/foot passes from
earlier in the day are not in it. The user's own geometry supersedes them.

**Deleting the toes did not break the clip generator but DID break the clip importers.**
`apply_frame` walks the TARGET rig's bones and skips what is absent, so `salaadin_clips.py` ran
clean. Unity was the problem, and it cost most of the session:
- all five authored clip FBXs imported with **no takes and no AnimationClip at all**, so the
  animator states silently fell back to PACK clips and he fought in a stranger's idle;
- the error is `Rig Error: Copied Avatar Rig Configuration mis-match. Transform
  'mixamorig:LeftToeBase' for human bone 'LeftToes' not found`;
- the body avatar was already clean (50 human bones, no toes). **The stale cache was on the CLIP
  importers** - each still held `skeleton=70 human=52` with 2 toe mappings, and Unity builds the
  clip's avatar from THAT cache, not from the source avatar. Same trap `humanDescription` sets on a
  body, one level further out.
- **Clearing the cache is NOT the fix.** An empty one gives `Invalid copied Avatar Rig
  Configuration: No human bone found. Ensure both rig type match`. It has to be OVERWRITTEN with
  the body importer's own `humanDescription`. `CrucibleMixamoSetup.ImportAll` now does that
  whenever the clip's mapping differs from the body's, so a future bone-count change cannot
  silently produce zero clips again.

**Measure the stance only once the authored clip is really playing.** Every alarming number this
session came from the pack fallback, not from the rig: leg span read 21 deg and the knees 97/92
with the clip broken. On the authored idle the same rig reads **span 52.6 deg against Richard's
54.3**, feet 44.1% of height apart against his 42.0%, and the head **+3.8% forward of the hips**
against his +4.3% - hips under the head, which is what the user asked for. `LEG_DAMP = 0.55` was
left alone and lands right on the 50 deg this log recorded as verified. The wide 74 deg stance
noted earlier today is GONE, fixed by the user's own hip and ankle work.

**A rig change moves the ground.** His feet ended up 0.866 world units under the floor, because
`GroundOffset` in `CrucibleCharacterSetup.cs` is measured per character and his ankle had dropped
9 points of height. Recomputed from the live rig - target is `floor + ankleHeight`, where
ankleHeight is 3.1% of his rig height times child scale times root scale - giving
**`{ "Salaadin", 0.394f / 3f }`**, positive where every other fighter's is negative. Verified after
a full rebuild: foot exactly +0.083 above the floor, which is his ankle height to three decimals.

Rebuild order that works, from a hand-edited studio file: export the body out of the .blend with
`salaadin_body.py`'s EXACT export options (wrong ones put the body in Unity lying down at a
hundredth of its size - the structure check is root `Salaadin_Rig` -> 3 meshes, dims 0.889/1.0/0.333),
then `salaadin_clips.py`, then in Unity `Rebuild Player4`, `Use Mixamo packs`, `Wire special and
riposte states`, `Wire combo strings`, `Salaadin > 4 Rewire`, `Apply the roster balance`.
The pre-change rig, body and all five clips are backed up in
`ArtSource/Characters/_backup_20261021/pre_userrig/`.

### 2026-10-22 — the reset: why every Salaadin rig fix kept failing

The user reported only two of his moves animating (both of them PACK clips) and called for a
restart: re-rig, verify the bones BEFORE animating, then a plain Mixamo idle and walk. The restart
was right, and mapping his mesh first found the thing that had been wrong all along.

**HE HAS NO THIGHS.** His real per-side leg geometry is two shells - a shoe (0-7.3% of height) and
a boot/lower leg (5.1-28%) - and above 28% there is nothing but coat: the two cloak shells run
11-85%. In the thigh band (40-55%) the vertices spread continuously from the centre line outward,
one skirt volume, not two legs. So **"is the bone inside the mesh" is a MEANINGLESS test on his
thighs** - it can only ever return the ambiguous 2/6 it has been returning, and it is what made
Mixamo's auto-rigger put his knee in the skirt and his hip out in the coat. Every rig pass, mine
included, had been trying to validate thigh placement against geometry that does not exist.
**The rule from now on: containment only for bones with real flesh (shins, feet, head, arms,
torso); hip, thigh and knee are placed by CONSTRUCTION and checked by proportion and symmetry.**

**The user's hand-edited rig was kept** (their call, and the right one - hips 51.2% up and 10.6%
apart, knee on the top of the real boot shell, ankle at 3.1% of height, all sane). Only the bones
that could be verified were corrected, in `ArtSource/Blender/salaadin_rig_v2.py`, which edits the
rig inside `Salaadin_Studio.blend` and writes the body FBX:
- both FEET re-aimed at their own shoe's toe, 85% of the way to the tip and inside the leather;
- HEAD up the middle of the helmet, stopping at 93% of the crown instead of 109.7% of body height,
  which is above the skull entirely;
- `HeadTop_End` put back over the head (the user had dragged it 21% of height sideways).

**A caution learned here: an FBX does not store bone TAILS.** With the toe bones deleted, `Foot`
has no child, so Blender's importer INVENTS a tail on re-import - it read (18.0,-25.9,-3.4) where
the rig actually says (14.7,-12.0,0.2). The containment audit therefore over-accused both feet and
the head. Measure bone directions in the .blend, not in a re-imported FBX.

**The broken animation was the hand-keyed clips, not the rig.** Only five of his states were
`Custom` (idle, Medium, Heavy, SP+Light, parry riposte) and those are exactly the ones the user
saw failing; everything that worked was a pack clip. `Salaadin_Idle` now points at the pack's
`sword and shield idle` and his walks already did. Verified in play: he stands upright and
planted, and the walk cycles with a real stride.

**`GroundOffset` depends on the IDLE CLIP as well as the rig.** It moved twice in one day: the
rig edit sank him 0.95 world units, then swapping the crouched hand-keyed guard for the pack's
upright idle left him hovering 1.10 the other way. Now `-0.709f / 3f`, verified after a rebuild at
foot +0.090 against a wanted 0.085.

**The humanDescription cache check now compares joint POSITIONS too**, not just bone names - a
re-rig that keeps the bone list identical but moves a knee would otherwise leave every clip
retargeting onto the old skeleton with no error at all.

Still to do: Medium, Heavy, SP+Light and the parry riposte are still pointed at the broken Custom
clips and need pack replacements chosen (free in the pack: `attack`, `slash (2)`, `casting`,
`casting (2)`, `power up`, the `crouching` set). The user's hand-keyed clips are NOT deleted.

### 2026-10-22 (cont) — choosing pack clips by MEASUREMENT, and a shield that faced the sky

The user watched the whole move set and reported six faults. Five were clip choices and one was a
real bug; all of them were decidable with numbers rather than opinion.

**Pick an attack clip by what it DOES, not by its file name.** `clipchar.py` (scratchpad) imports
each pack FBX and measures the sword hand's path length, vertical drop, forward and lateral
travel, the hips' total yaw, and - the useful one - how far the RIGHT SHOULDER travels, all
normalised by hips-to-head so the numbers compare across rigs. It turned every complaint into a
measurement:
- "the idle just wanders": `sword and shield idle` moves the hand **2.54** where `idle (4)` moves
  it **0.22**. `idle (4)` is the pack's only settled stance and is now his too (shared with
  Richard, like the walks).
- "the light needs the shoulder forward, he strikes sideways": `slash (3)` has shoulder travel
  **0.97** and 1 deg of hip turn - a pure arm swing. `attack (2)` has **4.50** and 59 deg. Moved.
- "medium: the sword is way behind and the body stays put": now `attack (3)`, shoulder 4.17 and
  36 deg of hip turn.
- "heavy: can't tell if he is spinning or flipping": now `sword and shield attack`, whose **4.00**
  vertical drop is nearly three times any other clip's - an unmistakable overhead.
- "SP+light doesn't sync": now `slash (2)`, 107 f and a 21.14 hand path, the longest flourish in
  the pack, so a special reads as one.
`Salaadin_Special_CrescentSlash` moved to `slash (5)` to free `attack (2)` for the light.

**The shield faced the sky because its mount was tuned to a clip that is gone.** It was mounted
`axis_y="-palm"`, squared up against his old hand-keyed guard; the pack's block idle rolls the
left hand differently and the disc went flat. Measured by taking the shield mesh's THINNEST local
axis as the disc normal (a shield is a flat slab, so this needs no guessing about mount
conventions) and dotting it with the fighter's forward: **face.forward 0.12, face.up 0.98**
against Richard's 0.70 / 0.03. Solved in play - swing the normal onto the fighter's forward, then
roll the boss upright - giving local euler (2.71, 170.39, 331.41) and **face.forward 1.00,
face.up 0.00**. Baked into `ShieldWeaponMount`.

**And the bug that would have made that silently do nothing:** `SetupWeapon` finds exactly ONE
weapon, the BLADE, by matching `WeaponSpec`. The shield is never that weapon, so a shield entry in
`ShieldWeaponMount` was simply never applied. There is now a second pass over the model's mesh
renderers for off-hand mounts. Verified across a `Rebuild Player4`: the shield holds 1.00 / 0.00.

All three normals fire and land in play (Richard 100 -> 96 -> 91 -> 85) with the shield upright.
NOT verified: how the swings read in MOTION - single frames cannot show that, and it is the
user's eye that found the last round of faults.

### 2026-10-22 (close) — the clip reassignment NEVER REACHED THE CONTROLLER

The user's verdict on the whole evening was "nothing was visibly different", and they were right.
A read-back of `P4_Salaadin.controller` - the check that should have been run first and was not -
shows every state still bound to its OLD clip:

    Salaadin_Idle               <- sword and shield idle       (the wandering one, hand travel 2.54)
    Salaadin_Light_SaifCut      <- sword and shield slash (3)  (shoulder 0.97, no body commit)
    Salaadin_Medium_ShinCut     <- Salaadin_ShinCut            (BROKEN hand-keyed)
    Salaadin_Heavy_WhirlingSaif <- Salaadin_WhirlingSaif       (BROKEN hand-keyed)
    Salaadin_Special_VeiledRush <- Salaadin_VeiledRush         (BROKEN hand-keyed)
    Salaadin_Riposte_Parry      <- Salaadin_SaberRiposte       (BROKEN hand-keyed)

which is EXACTLY the six faults the user reported, one for one.

**Cause.** `Crucible > Animation > Use Mixamo packs` is the ONLY menu item that writes a state's
motion. It times out over the CLI on every run; on the run after the move set was re-pointed it
returned `HTTP 400: Bad Request` and was never re-run. Completion was inferred from console lines
like "Salaadin_Medium_ShinCut: clip 1.13s ... speed x2.72" - but those came from an EARLIER run,
and x2.72 is the speed of the hand-keyed clip, not of a pack one. The lesson is blunt:
**"Use Mixamo packs timed out but probably worked" is not evidence. Read the controller back.**
The screenshots taken to "verify" the new light/medium/heavy were pictures of the old clips.

**Nothing was reverted** (the user asked for everything to be kept). The new map is sitting in
`CrucibleMixamoSetup.P4Map` and is one successful menu run away.

WHAT IS ACTUALLY APPLIED AND VERIFIED:
- rig v2 (`ArtSource/Blender/salaadin_rig_v2.py`): both feet re-aimed into their own shoe, head up
  the middle of the helmet, `HeadTop_End` put back overhead; body FBX rebuilt, Player4 rebuilt;
- `GroundOffset` `-0.709f / 3f` - foot measured at +0.090 against a wanted 0.085 after a rebuild;
- the shield mount `(2.71, 170.39, 331.41)` - face.forward 0.12 -> **1.00**, face.up 0.98 -> 0.00,
  and it survives `Rebuild Player4`;
- the off-hand mount pass in `CrucibleCharacterSetup.SetupWeapon` (without it a shield entry in
  `ShieldWeaponMount` is silently ignored, because that method only ever finds the BLADE);
- the clip-importer `humanDescription` fix, now comparing joint POSITIONS as well as bone names;
- `ArtSource/Blender/clip_measure.py`, which is what made the clip choices measurable.

TOMORROW, in order:
1. Run `Use Mixamo packs` and **read the controller back** before anything else. If it keeps
   failing over the CLI, run it from the Unity menu by hand.
2. Then re-watch all six moves IN MOTION - the thing single frames cannot show.
3. Open question: the shield fix IS applied and verified by measurement, yet the user still saw no
   difference. Either the block was not tested, or the front-end path (Battle Now -> character
   select) reaches a different instance than the `stage34` harness does. Worth checking before
   trusting any future "verified in play" that used the harness.


### 2026-10-22 — the user's Rigify metarig, and Salaadin finally takes the Mixamo packs

The user re-rigged Salaadin by hand in Blender and handed over
`ArtSource/Blender/salahtest_rigged.blend`. It is a good rig and it is now his rig: the whole
chain from that .blend to a fighter standing in the arena with every state on a pack clip is
`ArtSource/Blender/salaadin_metarig.py`.

**What the hand rig got right**, measured before touching it (`metarig`, 67 bones, the knight
skinned to it): perfectly symmetric left and right, hips at **51.5%** of height (human ~52%),
ankles at **5.5%** (human ~4%), knee **54%** along hip->ankle with a **163.5 deg** break and the
elbow a **164.9 deg** break - both real hints for the retargeter, where the old rig's knees and
elbows were collinear. Thigh:shin 1.15. Nothing like the splits geometry.

**Four things stopped Unity using it, all fixed in `salaadin_metarig.py`:**

1. **Rigify metarig names.** No C# in this project resolves a bone by name - it all goes through
   `Animator.GetBoneTransform(HumanBodyBones.*)` - but that only works once Unity's humanoid
   AUTO-MAPPER has filled the slots, and the mapper is a name heuristic. Everything Unity must map
   is renamed to Mixamo; every extra bone gets a short neutral name with no humanoid keyword in it
   (`L_Meta1`, `L_Heel`, `L_Skirt`, `L_Pec`), because a bone called "Pelvis" or "Palm" invites the
   mapper to fill the wrong slot. Result: **all 65 of the pack rig's bones exist on his rig by
   name**, pack-only = 0.
2. **An unapplied 0.48 object scale on the armature**, baked into the bone data.
3. **384 verts (7.3%) carrying no weight at all** - a band down the front of the coat - which
   would have stayed in the bind pose while the torso moved. Each takes its nearest weighted
   neighbour's weights.
4. **No leaf bones.** An FBX does not store bone TAILS, so a childless bone has its direction
   invented on re-import. Real `*_End` / `*4` leaves are added for the head, both toes and all ten
   finger tips, the way a Mixamo rig carries them.

**The trap that cost the most, and the reason to always read the avatar back: SEVEN spine bones
into SIX humanoid slots.** Leaving the spare in with a neutral name does not work. Unity's mapper
walks the chain greedily, so it took the spare for **Head** and put the real head bone in the
**RIGHT EYE** slot - 53 bones mapped and no error anywhere. Head rotation would have driven the
lower neck while the head counter-rotated as an eye. `spine.005` is now dissolved into `spine.004`
(657 verts of weight move to the parent, the parent's tail extends to the head's head), giving one
neck bone. Re-read: **52 mapped, `Head <- mixamorig:Head`, `Neck <- mixamorig:Neck`,
`UpperChest <- Spine2`, 30/30 fingers, toes mapped**, only LeftEye/RightEye/Jaw missing and those
are genuinely absent. **The lesson generalises: a humanoid avatar that reports valid=True
human=True can still be wired one bone off. Print the slot->bone table.**

**`matrix_world` is derived, so the order of the bake matters.** Assigning `body.matrix_world`
solves for `matrix_basis` against the parent inverse AS IT STANDS; clearing the parent inverse
afterwards put the 1/0.48 straight back and the body re-imported at **half the skeleton's size**.
Clear the parent inverse first. There is an assert on the body's height now.

**The hand bones' roll is set so bone-Z points at the floor**, which is the convention
`salaadin_body.py`'s `hand_frame()` reads ("palm = the hand bone's Z; in the T-pose rest it points
DOWN"). Without it the falchion and shield mount against an arbitrary roll.

**Then the Unity side, and the two numbers that had to be re-solved** because the rig and the hand
roll changed:
- `GroundOffset["Salaadin"]` **-0.709f/3f -> -0.600f/3f**. Measured as the lowest BAKED skinned
  vertex in play: **a SkinnedMeshRenderer's `bounds` is a loose box that does not track the pose
  and read this wrong by 0.64 world units** (-0.749 against the true -0.1085). After the fix:
  idle **+0.0028**, Block_Hold -0.0141, walk +0.0524.
- `ShieldWeaponMount["Salaadin_Shield_L"]` **(2.71, 170.39, 331.41) -> (0.75, 181.74, 355.40)**:
  face.forward 0.91 -> **1.00**, face.up 0.12 -> **0.00**, the disc's top straight up at 1.00.
  **Measure a shield in BLOCK_HOLD, never in the idle** - a shield at rest hangs off the arm and
  reads about 0.70 forward / 0.67 up however it is mounted, which is what sent me chasing a mount
  that was already nearly right.

**The falchion needed nothing**, but the first measurement said it did. Taking the hilt as the
mesh BOUNDING BOX's -Z extreme put the hand "0.190 off the blade axis" - an artifact of putting a
box round a CURVED blade. The honest metric is the nearest actual sword vertex to the hand bone:
**0.031 on Salaadin against Richard's known-good 0.173**. It is in his fist. (It also *looked*
detached in a swing render; it is rigidly parented to the hand bone and measures identically in
every pose - the cloak was in front of it.)

**`Use Mixamo packs` landed this time, and the controller was read back from disk before anything
was claimed.** All 22 states are on pack clips; not one hand-keyed Custom clip remains:

    Salaadin_Idle               <- sword and shield idle (4)      x1.00
    Salaadin_Light_SaifCut      <- sword and shield attack (2)    x4.33
    Salaadin_Medium_ShinCut     <- sword and shield attack (3)    x4.16
    Salaadin_Heavy_WhirlingSaif <- sword and shield attack        x4.12
    Salaadin_Special_VeiledRush <- sword and shield slash (2)     x4.93
    Salaadin_Riposte_Parry      <- sword and shield attack (4)    x0.67

**`Crucible > Characters > Rebuild Player4` over the MENU bridge returned `HTTP 400` and wrote
NOTHING** - the same failure that lost an evening on 2026-10-21. The read-back caught it (the old
-0.2363 and the old shield euler were still on the scene objects). `CrucibleCharacterSetup.RebuildP4()`
called directly through `eval` with a long timeout works. **Prefer eval to the menu bridge for
anything slow, and read the result back either way.**

`ArtSource/Blender/Salaadin_Studio_v3.blend` is the studio file for the new rig (79 bones, weapons
mounted). The old `Salaadin_Studio.blend` was deliberately NOT overwritten - it holds the previous
rig and whatever is being animated in it.

**Still open, and the biggest thing left to the eye: 37 of 90 animator states play at 1.8x or
faster** and Salaadin's normals are now x4.1-x4.9. That is the AttackData frame budgets against
the pack clips' lengths, not the rig - it is the number `Design/Animation_Handoff.pdf` was written
around. A swing at x4.3 reads as a blur whatever the skeleton does.

### 2026-10-22 (later) — a plain overhead, a low slash that is not a smear, and the fist out of the shield

Three changes on the user's eye, all decided by measurement and all read back after applying.

**The heavy is a plain overhead now.** `clip_measure.py` gained three columns - `hiTop` / `hiLow`
(the sword hand's highest and lowest point measured FROM THE HIPS) and `vert` (drop / side) -
because `drop` alone cannot tell a chop from a spin: a big turn also drops a long way. With those:

    clip                     drop  side  vert  hipYaw  shoulder  hiTop  hiLow
    sword and shield attack  4.00  1.10  3.63    -43     6.53     1.68  -0.16   <- the OLD heavy
    sword and shield slash(4)3.36  1.09  3.07     -9     1.82     1.32  -0.54   <- the NEW heavy

Both drop. Only `slash (4)` does it without turning the body - **9 deg of hip yaw against 43, and
a third of the shoulder travel** - and its hand finishes at -0.54, below the knee, where the old
one stopped at hip height. That 43 deg turn is what read as "is he spinning?". The freed
`sword and shield attack` went to SP+Heavy, where a big committed chop belongs.

**The move data had to follow the clip, not just the animation.** The heavy was authored for a
whirling cut (`Mid` / `FromLeft` / `HitRight` / `AimYaw`, box 1.5 x 1.2) and is now
`Overhead` / `FromOverhead` / `HitStraight` / `AimFull`, box 1.2 x 1.6 - so the right dashes evade
it, the defender plays the straight reaction, and he faces the man he is chopping instead of
keeping a turn that no longer exists. Renamed "Whirling Saif" -> **"Falling Saif"** in the move
list; the ASSET name is deliberately unchanged, because the scene holds these by GUID.
Play-verified: it still connects, Richard 115 -> 108.

**SP+Medium was not the wrong clip - it was a six-frame smear.** `slash (5)` IS the pack's low
slash and the measurement says so plainly: its hand never rises above **0.69** of a hips-to-head
where every other strike reaches 1.3+, and it sweeps 2.01 sideways against 1.04 of drop. The
fault was the TRIM. `ComputeTrims` takes `len = max(core_end - core_start, 6)` source frames, and
that clip's core was 17-22 - five frames, clamped to the six-frame floor - then stretched over a
44-frame budget, which is exactly the **x0.27** the controller reported. Widening the core to
8-30 gives a 22-frame window: 0.73 s of clip over a 0.73 s budget, **x1.00**.
**The speed column is the tell: anything far from x1 means the window and the budget disagree,
and a speed well BELOW 1 means an over-tight core, not a bad clip choice.**

**The fist was going through the shield because `ShieldWeaponMount` had no POSITION.** It was
`(0,0,0)`, which puts the hand bone in the disc's own mid-plane; nothing animates his fingers, so
the open hand ran straight through the face and stood **0.106 world units proud of it**. Measured
by taking the verts weighted to `LeftHand*` and expressing them in the SHIELD's own frame: the
fist sat **0.093 off the boss in-plane** (24% of the radius) and **0.090 ahead of the disc**, and
spans 0.215 along the normal where the whole disc is only 0.129 thick - it can never sit inside.
The fix slides the boss onto the fist and pushes the disc 0.120 forward along its normal, so the
fist sits in the boss hollow the way a centre grip really is held: in-plane **0.000** off the
boss, front-most vertex **+0.050** against a front face of +0.064. Facing unchanged at 1.00
forward / 1.00 top-up, and it survives `Rebuild Player4`.

Still at x4.1-4.9: the light, the medium, the heavy and SP+Light, because those clips are not in
`swing_trims.json` and play whole against short AttackData budgets. The user has not objected to
those, and changing them means changing frame data, which is balance.

### 2026-10-22 (later still) — the roster finally has sizes, and three rigs get measured

**Salaadin was not small because of his rig.** `CrucibleCharacterSetup` scaled every fighter so
the bounds of ALL his renderers came to 2.9 world units - and those bounds include the weapons.
A fighter whose sword points up gets shrunk until the SWORD fits; a fighter whose blade hangs
down barely shrinks at all. Salaadin's model bounds were a true 2.9, but two thirds of a metre of
it was falchion and shield, so his body stood **2.10 against the others' 2.69-2.78**. The fix is
one line of intent: measure the SKINNED renderers only, since the body is skinned and the weapons
are plain MeshRenderers bone-parented to a hand.

**Then a per-character height table**, because the user wants the roster to read biggest to
smallest as Richard, Bjorn, Salaadin, Miyamoto - which is also the order of their armour and the
reverse of their speed. It used to come out Miyamoto, Bjorn, Richard, Salaadin: the first three
within 9 cm of each other and in the wrong order.

**Three different "heights" are in play here and two of them are traps.** This cost three full
rebuild-and-measure rounds, each one confidently wrong:
1. **The bind-pose bounds box** is what the scale is SET from, but it is loose and every idle
   stands with bent knees, so the body always comes out shorter than the box - by 5% for Bjorn,
   28% for Salaadin, whose idle has the widest stance. So the table must be calibrated, not set.
2. **The mesh's own vertical span** (lowest vertex to highest) is the obvious measure and it is
   WRONG: Richard's cape and Salaadin's coat hang below their heels, so it credits them with
   height they do not appear to have. It ranked Richard the tallest while on screen Bjorn stood
   a head over him, and I reported that as done before looking at an orthographic render.
3. **The crown above the floor** is what the eye judges, but it moves when GroundOffset moves -
   lifting a fighter lifts his crown - so it cannot be solved on its own either.
   **STATURE, crown minus sole, is the only one that depends on the child scale alone.** Solve
   that first, then drop each sole onto the floor with GroundOffset. Converges in one pass.
Final, play-verified, each fighter in his own idle: **Richard 3.250, Bjorn 3.049, Salaadin 2.799,
Miyamoto 2.552**, every sole within 0.013 of the floor.

**And anchor GroundOffset on the SOLES** - the lowest baked vertex whose dominant bone is a foot,
toe or ankle - not on the lowest vertex of the body. Anchoring on the lowest vertex rests the
CLOTH on the floor and leaves the man hovering half a unit above it.

**The rig audit, because the user wants to re-rig the other three the way he rigged Salaadin.**
Head positions only (an FBX stores no bone tails), every skinned mesh joined for the height
normalisation - normalising against the single densest mesh picked a WEAPON on Richard and
reported his whole skeleton as nonsense (hip -134% of height).

    rig        bones  hip%  ankle%  hipFwd%  knee%  kneeAng  elbow%  elbAng  th:sh  up:fore  asym
    Bjorn         56  51.4     2.0     0.04   51.5    176.6    52.0   143.4   0.99     0.96   3.7
    Miyamoto      56  49.4     2.0     0.74   47.1    173.6    50.4   143.1   0.88     0.98   3.7
    Richard       65  43.8     7.8     2.15   46.9    165.9    57.6   157.8   0.87     1.30   2.0
    Salaadin      79  51.5     5.5     1.60   54.0    163.5    63.6   164.9   1.15     1.71   0.0
    human         --  ~52      ~4      <2     ~50   163-170     ~52 160-170   ~1.05    ~1.1    0

The verdict is not the expected one. **Bjorn's and Miyamoto's joints are placed WELL** - hips,
knees and elbows all within a couple of points of human, and better than Salaadin's old rig ever
was. Their real defects are elsewhere: **15/30 finger slots** (both right hands are still mitts)
and **knees that are nearly straight in the bind pose** - 176.6 and 173.6 degrees, against the
163-165 of the two good rigs. A straight-knee bind pose gives the retargeter no hint which way
the knee folds, which is exactly the fault CLAUDE.md already records against Salaadin's old rig.
**Richard is the one that measures badly**: hips **43.8%** of height where human is ~52 and the
user's own metarig is 51.5, ankles 7.8% where human is ~4, and hips **2.15%** of height forward
of the ankle - past the threshold that gave Salaadin the splits. This confirms the warning
carried in CLAUDE.md since 2026-10-21, now with the whole roster to compare against.

### 2026-10-22 (evening) — the whole roster re-rigged the way Salaadin was

The user's verdict was "re-rig all of them, exactly how i rigged salaadin". The audit disagreed
about two of the three, I said so, and the user repeated it - so all three were done.

**Salaadin's rig was hand-built as a Rigify metarig; the other three are now fitted by
construction.** `ArtSource/Blender/fit_metarig.py` adds a Rigify human metarig, deletes its 92
face bones, places every remaining joint, and skins the body to it. The existing rig is the
ANCHOR - it knows where a shoulder sits inside a fur mantle and where a knuckle is inside a
closed fist, which no mesh measurement recovers - and only the faults the audit found are
corrected on top of it. `metarig_to_mixamo.py` is the converter written for Salaadin, generalised:
Rigify names -> Mixamo names, the spare seventh spine bone dissolved, 13 leaf bones added, the
hand roll set, the FBX written with the project's exact export options.

    rig        bones  hip%  ankle%  hipFwd%  knee%  kneeAng  elbow%  elbAng  th:sh  up:fore  asym
    Richard   65->79  43.8    7.8     2.15   46.9    165.9    57.6   157.8   0.87     1.30   2.0
      after          52.0    4.0     0.00   50.5    164.0    51.0   163.7   1.00     1.02   0.0
    Bjorn     56->79  51.4    2.0     0.04   51.5    176.6    52.0   143.4   0.99     0.96   3.7
      after          52.0    4.0     0.00   50.5    164.0    51.6   163.5   1.00     1.00   0.0
    Miyamoto  56->79  49.4    2.0     0.74   47.1    173.6    50.4   143.1   0.88     0.98   3.7
      after          52.0    4.0     0.00   50.5    164.0    51.9   163.4   1.00     1.00   0.0

**Every fighter now maps 52 humanoid slots and 30/30 fingers.** Bjorn and Miyamoto were on 37
and 15/30: their right hands were Tripo mitts. The fit gives the mitt a real finger chain by
mirroring the left hand's, and the weight pass splits the mitt's own weights onto it - 1,775
verts on Bjorn, 4,913 on Miyamoto.

**Three traps, each of which reported a clean run while doing nothing:**

- **`bpy.ops.object.parent_set(type='ARMATURE_AUTO')` is the wrong tool here.** Bone-heat
  weighting returned "failed to find solution" on Miyamoto and left **all 119,952 verts
  unweighted**, and where it does succeed it throws away work that was done on purpose: Tripo's
  costume weights and the left-hand finger chains `add_fingers.py` built by hand. The weights are
  TRANSFERRED instead, every old vertex group poured into the metarig bone that replaces it
  (twist bones fold into the limb they twist about), which is also this project's standing rule.
  Result: 0 unweighted verts on all three.
- **Read the weights BEFORE deleting the vertex groups.** The capture sat below the teardown, so
  the transfer ran over an empty list and reported "0/119979 verts carry weights" on all three
  without erroring.
- **`object.parent = None` does not preserve a world transform.** Blender keeps `matrix_basis`
  and drops the parent's contribution, so a body parented to an armature the FBX importer scaled
  lands at a different size - Richard's came out **42.75x**, and the converter then cheerfully
  reported his ankle at 50% of body height. Capture `matrix_world` and put it back; there is an
  assert on the body's height now.

**`CrucibleCharacterSetup.FillMissingHumanBones` is new.** Unity fills humanoid slots from a NAME
HEURISTIC and the heuristic is not exhaustive: with the whole roster on Mixamo names and both
hands fully boned, it took 30/30 fingers on three fighters and **26/30 on Miyamoto**, dropping
RightRing and RightLittle intermediate/distal whose bones exist, are correctly named, and are
mirror images of the left ones it did take. Any slot still empty after the reimport whose
Mixamo-named bone is sitting in the hierarchy is now mapped explicitly. It only ever ADDS.

**A hand-tuned grip OFFSET does not survive a re-rig.** `Richard_Sword` carried
`(0.0386, 0.1908, -0.0029)`, tuned against the old hand bone, and the re-rig threw the sword
**33.18 world units** off his hand. Every other weapon on the roster mounts at its hand bone's
origin, so that offset was compensating for the old rig and not for the sword; cleared to zero,
the sword sits **0.023** from the hand. The other five weapons came through untouched
(0.044-0.235, against Salaadin's known-good shield at 0.195).

**Every authored clip fell back to its own avatar, and that is expected.** `ImportAll` tries
`CopyFromOther` against the character's body avatar; a rig change invalidates that, and the
console says so plainly - `Transform 'mixamorig:Hips' for human bone 'Hips' not found` for the
Tripo-era Bjorn and Miyamoto clips, `'mixamorig:LeftToeBase'` for Salaadin's toeless-era ones.
The safety net put each clip back on its own avatar rather than leave a state pointing at
nothing. It matters less than it sounds: of 90 states, **five** still use a custom clip - one on
Bjorn and Richard's four authored specials - and everything else is on pack clips. Richard's four
should be re-exported through `richard_clips.py` against the new body to get the quality back.

Statures were re-calibrated twice more (a re-rig moves them 5-10%) and the ground offsets with
them. Final, play-verified: **Richard 3.250, Bjorn 3.050, Salaadin 2.799, Miyamoto 2.550**, every
sole exactly on the floor, and all three re-rigged fighters land a heavy on Salaadin (9, 12, 7).

### 2026-10-22 — The weapons after the re-rig: a blade through the chest, a shield worn backwards, and four men standing on the floor at last

Four complaints, four different causes, and only one of them was where it looked.

**Miyamoto's katana went through him.** The nearest blade vertex sat **0.038** from his torso
axis and the blade reached z −1.11 — behind him — because its direction read **−0.94 against his
own forward**. The first suspect was `fit_metarig.py`, which re-parents each weapon to the new
hand bone and computed a `matrix_parent_inverse` from that bone's frame, throwing away the
orientation the FBX had authored. That WAS a bug and is fixed (the weapon's world transform is
captured, the parent assigned, then the world transform restored, with an assert that it did not
move) — but it was not THIS bug. The real one: the entire `ShieldWeaponMount` application sat
inside `if (!twoHanded)`, so Bjorn and Miyamoto could never receive a mount entry at all. The
gate existed because those two were TRIPO rigs mounted through `WeaponMount` in Tripo hand-bone
units; the re-rig made all four Mixamo metarig exports, so the gate was obsolete. Hoisted out,
and the katana's rotation **solved in play rather than guessed** — the one that aims the blade
forward, up and slightly out. Torso clearance 0.038 → **0.292**, and the grip tightened 0.088 → **0.020**.

**His "handcuffed" hands were the same bug.** `TwoHandGrip` pulls the off hand onto the hilt's
`LeftGrip` socket and reported `offHandMiss` ≈ 0 throughout — the wrist was exactly on its goal,
so the number looked perfect while the goal itself was behind him. With the blade aimed forward
the socket comes back in front and both hands sit on the hilt.

**Richard's shield was on backwards and his fist went through it.** Nobody had ever given him a
mount entry. A flat slab's facing is its thinnest local axis: his read **−0.82 against his own
forward**, i.e. he carried the face toward himself, and his fist stood **0.240 proud** of it —
more than twice the miss fixed on Salaadin. Solved the same way Salaadin's was: aim the facing
along his forward with the kite's long axis up, slide the boss onto the fist in-plane, then seat
the disc so the fist sits 0.020 inside the front face. Torso 0.385 → **0.773**.

**All four floated, and the capsule was why.** The `CapsuleCollider` was a fixed 2.1 high centred
at 0.8–1.0, which hangs **0.20 (Bjorn) to 0.75 (everyone else) world units BELOW the feet**.
Gravity rests that overhang on the arena and leaves the man hovering by exactly that much — roots
were sitting at y 0.48–0.69 — and an overhang that deep catches on every step and ledge and walks
him up it, which is the "climbing the arena" half of the complaint. The old `GroundOffset` was a
compensation for it (push the MODEL down from a floating root); the re-rig's offsets were measured
against the root instead, which doubled the float. The capsule is now fitted to the body, bottom on
the soles, which makes the root land on the floor and the measured offsets correct. **Every sole is
now within 0.005 of the arena surface beneath it** — and measured against the real arena, not y=0:
the floor under them runs −0.065 to −0.267.

**Bjorn's spin did damage without the axe touching.** The hitbox ranges 3.2–3.5 while the default
lunge stops at `ATTACK_LUNGE_STOP` 1.7 and may travel only `ATTACK_LUNGE_MAX` 0.9 — fine for a
move that strikes out in front, wrong for one that carries the axe round the body. The user's call
was that **the spin should go THROUGH the opponent**, not that the hitbox should shrink, so both
spins got a lunge override (stop 0.9, max 2.6) in a new `LungeOverride` table in `CrucibleBalance`.
Medium now closes 3.00 → 1.63 (bodies touching) with the axe **0.344 from the spine**, 9 damage;
Heavy closes to 2.10 with the axe 0.528 from the spine, 11 damage.

Still carried: Richard's four authored clips want re-exporting through `richard_clips.py` against
his new body.

### 2026-10-22 — the spawn heights the capsule fix left behind

Picked up after the laptop went down mid-session. The four fixes from the morning were all on
disk and all still measure right in play: Miyamoto's katana reads **+0.554 against his own
forward** with **0.694** of torso clearance (it was −0.94 and 0.038 — the blade inside his chest),
both his hands sit on the hilt (`offHandMiss` 0.0000, finger gap 0.021), Richard's shield faces
**1.000 out and 0.003 up** with his sword gripped at 0.022, Bjorn's axe is forward 0.833 at 0.555
clearance, and Bjorn's two spins still carry `lungeStopDistance` 0.9 / `lungeMaxDistance` 2.6 in
their assets.

Re-verifying the floating turned up the tail of it. The capsule fit was right — every fighter's
capsule bottom IS his root origin now, and each one SETTLES with his soles on the arena. But two
places still read the fighter's **authored scene height**, and those numbers (0.24 Bjorn, 0.75 the
other three) were measured back when the capsule hung up to 0.75 BELOW the feet and the root
floated by exactly that much. `ResolveSpawns` used them for the match marks and `PlaceStaged` for
the character-select line-up, so every fighter was still **put into the air** — 0.35 (Bjorn) to
0.82 (the rest) above the floor — and only *looked* fixed because `FreezeFighters` leaves gravity
running and he lands during READY-3-2-1. The earlier verification measured him after that fall,
which is why it read clean.

Both now go through one `GroundedY(at, fallback)` helper: raycast down at the mark, ignore triggers
(the hitboxes are triggers) and anything belonging to a fighter, take the highest hit. A raycast
also survives an arena swap, which one authored height cannot — the floor under the marks is
neither flat nor y=0. The authored height stays as the fallback when nothing is hit.

Measured at both pairings' marks, at the instant of placement and before gravity can hide
anything: **capsule bottom 0.0000 above the floor for all four**, and the real soles (foot-weighted
baked vertices, not the lowest vertex — Richard's cape hangs 0.13 below his heels and reads as a
sunken man) land within **0.013**: Bjorn −0.005, Miyamoto +0.010, Richard +0.004, Salaadin +0.013.
No drop at the count-in and nothing standing in the air on the select screens.

Still carried: Richard's four authored clips want re-exporting through `richard_clips.py` against
his new body.

### 2026-10-22 — Nine play-test notes, and the hip travel that hid under all of them

The user played the build and came back with a list: Salaadin's light attack was a spin, Miyamoto's
heavy had become a thrust, two of Miyamoto's specials were too slow, Richard's sword sat in his
forearm, his shield faced the wrong way, his running attack paused mid-run before the thrust, his
light and medium attacks melted into the arena floor, and Bjorn and Richard towered over the other
two. All nine are done.

The quick ones first. **Salaadin's light** was `sword and shield attack (2)`, which `clip_measure.py`
reads as yaw 428.8 deg, spin=True — a literal spin; it is now `attack (4)`, and while fixing it I
found that none of his five attack clips had a `swing_trims.json` entry at all, so every one of them
was playing whole at 4.06–4.93x. Measured cores for all five put his set at 0.96–1.11.
**Miyamoto's heavy** was `great sword high spin attack` with a core of 1–57, i.e. the whole clip
including its 2.34-unit forward charge, so what you saw was the charge and it read as a lunging
thrust. The heavy is now `great sword casting`, a slash; the thrust (Iaijutsu) survives as a
combo ender only, which `FindAttack` gives for free since it returns the FIRST array match.
**Nukitsuke** came down to 12/4/14 (0.50 s) and the old heavy clip was re-cut to 9–22 and promoted
to a new move, **Kuruma**, on SP+Heavy at 0.43 s.

**Richard's shield** was on backwards — and this is worth remembering because the first measurement
said it was fine. `Mathf.Abs(dot)` on the slab's thinnest axis hides a 180 deg flip; the signed dot
read -0.82, and the photograph settled it: the enemy saw the arm straps and the buckle, the cross
and the lion faced Richard himself. **Paint cannot be measured** — when a face has a front and a
back only to a human eye, take the picture.

**The melting, which was four bugs in a trench coat.** All four of Richard's hand-keyed clips
(light thrust, shield charge, shield bash, double-high) imported `CreateFromThisModel` with a null
source avatar: they had been authored against his PRE-metarig skeleton, so `CopyFromOther` no longer
validated and the fallback quietly retargeted them as garbage. Re-exporting through
`richard_clips.py` against the new body put them back on `Richard_BodyAvatar` — and he still played
a metre underground, two thirds his own size. The real fault was in the hip travel:

- `apply_frame` copies a pack frame's pose-bone `matrix_basis` across, and **a pose bone's
  translation is in bone-LOCAL units, whose magnitude follows the ARMATURE OBJECT's scale**. The
  pack rigs are scaled 0.01; the re-rigged Richard is 1.0. Every hip offset therefore arrived a
  hundred times too big. `RootT.y` on the imported clips read **-23.4 (thrust), -50.0 (bash),
  -22.8 (double-high)** against the healthy 0.8–1.0 this file already records.
- The hip-height ratio CLAUDE.md prescribes for Salaadin is the SECOND of two factors and on its own
  moved the number from -25.4 to -23.4. The first is `src_armature_scale / target_armature_scale`.
  With both, the four clips came in at **0.57–1.08** and he thrusts standing up.
- **"Based Upon: Feet" is not the rescue it looks like.** With the clip still broken, flipping the
  importer's root-height reference was the obvious move and it was wrong: it shifted him slightly
  and left him underground, and it would have masked the real fault on every clip afterwards. It is
  back to Original. Measure `RootT.y` on the clip before touching the importer.
- And the fix did not take the first time, for a reason worth its own line: `CrucibleMixamoSetup`
  decides whether to reimport by comparing the clip settings it is about to write against the ones
  on disk, and **that comparison did not include the fields it had just changed**. It judged the
  clip unchanged, wrote nothing, and the console said nothing. Both root-height fields are in the
  test now.

**The running attack** froze its legs on `run[n_run - 1]` for 22 frames while only the arms moved —
exactly the pause the user described. `clip_charge` now samples the 22-frame run cycle x1.5 for
three strides, the user's stated maximum, and smoothsteps the sword out so the point is extended by
frame 25 of 33, before the last stride lands. Verified in play: the feet alternate the whole way
(fore/aft gap -0.86 -> +0.51 -> +1.81 -> +1.16) and the sword hand travels forward monotonically.

**Sizes**: Richard and Bjorn came down 11% (`StandingHeight` 3.753 -> 3.340 and 3.498 -> 3.113,
`Stature` to 2.89 and 2.71). Richard's `GroundOffset` had to come down with it — it is a fixed
root-local lift and shorter legs left him hovering 0.027 — to `0.119f / 3f`. Measured in play
afterwards: **Richard 2.887, Salaadin 2.795, Bjorn 2.746, Miyamoto 2.570**. Worth the user's eye:
that drops Bjorn just under Salaadin, where he used to stand over him.

Richard's grip closed too: the sword's axis now runs exactly through the hand bone (off-axis
0.174 -> **0.000**) and the nearest blade vertex to his forearm went 0.005 -> **0.131**.

### 2026-10-22 — The verification pass: what careful testing found after "done"

The nine items were reported done and committed. The user asked for a slow, precise pass over
them — "precision is the key to what i want" — and it paid for itself twice.

**What the pass confirmed, measured frame-exact in play** (`Time.captureDeltaTime = 1/60`, one
rendered frame per game frame, sampled from a coroutine into a CSV):

| # | item | the measurement |
|---|------|-----------------|
| 1 | Salaadin's light is a slash | `Salaadin_Light_SaifCut`, pelvis sweep **16°** against the old clip's 429° |
| 2 | Miyamoto's heavy is a slash | `Miyamoto_Heavy_Overhead`, 16° of pelvis, hand descends 0.97 → 0.14; the thrust now only appears as a combo ender |
| 3 | SP+Light quicker | Nukitsuke, 30 frames, **x1.00** |
| 4 | SP+Heavy swapped and quicker | Kuruma on SP+Heavy, 36 frames, x0.81 |
| 5 | Richard's grip | off-axis **0.000**, along the blade 0.090, nearest vertex to the forearm 0.131 |
| 6 | the charge runs | 3 lead-foot swaps ~22 frames apart, blade forward 0.78–0.99 throughout, travels **1.77** |
| 7 | the shield faces out | thin-axis dot **1.000**, forearm 0.402 *behind* the face, cross photographed facing the enemy |
| 8 | no melting | light and medium both upright, root exactly on the floor |
| 9 | sizes | Richard **2.878**, Salaadin 2.790, Bjorn 2.746, Miyamoto 2.551 — both cuts land at −11% |

**And two things it caught that "done" had missed.**

*A stale contact entry was re-cutting Miyamoto's new heavy.* `swing_contacts.json` is keyed by
STATE, so moving that state onto `great sword casting` left behind the contact frame measured
against the spin it used to play. The window landed on source frames 18–50 of a clip whose swing
peaks at 66 — the cut was outside the window entirely — and the entry pinned playback at 2.0x,
which is exactly the "too slow/too fast" class of complaint the user had raised about the specials.
Removing it and widening the core to half the budget put the state at **x1.00** with the peak on
the hit frame.

*Richard buried a boot.* The shield bash drove its planted ankle 0.066 world units under the arena
for five frames while the other foot was 1.47 up — the clear outlier across all 24 moves measured.
Rather than tune that one clip, `plant_feet()` now runs inside `key()` in `richard_clips.py`: it
lifts the hips whenever the lower ankle falls below 0.55 of its rest height. The threshold came
from measuring with the clamp off — thrust 0.055, charge 0.018, bash **−0.187**, double-high
**−0.183** — so the two good clips are untouched and only the two that were underground move.
Afterwards: **zero frames below the floor on 22 of 24 moves**, and the two that remain (Richard's
light, 1 frame at −0.016; his bash, 4 frames at −0.096) are the idle→crouch BLEND, not the clips,
whose own frames sit +0.18 to +0.31 clear.

All four stand with their capsule bottoms exactly on the floor and their soles within ±5 mm of it.

**Three ways this kind of testing lies, all hit in one evening.** A lunge only closes to
`ATTACK_LUNGE_STOP`, so after the first swing every later move reads `travel 0.00` and the shield
charge looked like it could not move — re-space before each one. Five bit-identical frames in a
series is hit-stop, which is the game working, not a freeze. And a KO silently ends the run: six
landed attacks put Miyamoto at 0/80, after which every move in his own pass reported NOT FIRED.

### 2026-10-22 — Four more from play-testing: the run, two melty specials, crossed legs, a flat blade

**Richard's two "melty" specials are pack clips now, and picking them took two rounds.** The user
asked for "any 2 similar animations without changing them". The trap is that `clip_measure.py`
WRAPS hip yaw, so a full turn reads as a small one: `attack (3)` looked like a 36 deg cut and is a
396 deg spin. Run `clip_curves.py` instead, which unwraps — and the pack turns out to be mostly
spins (attack (2) 429, slash (2) 507, slash (4) 391). The only two clips no fighter used, `power up`
and `casting`, measure as overheads and are **not strikes at all**: photographed in play they hold
the sword up across the chest, a spell-cast gesture. So the specials share with Salaadin, as the
walks and slash (5) already do: **SP+Medium takes `slash (3)`** (diagonal-up, 48 f, yaw 109, no
travel — his dodge riposte, the lowest-visibility clip to share) and **SP+Heavy takes
`sword and shield attack`** (the pack's biggest committed chop: drop 4.00, yaw 86). Both at x1.00.

**The charge was Looney Tunes because two things stacked.** The clip held the upper body in a fixed
carry pose every frame — a man sprinting with his shoulders locked — and the state was pinned at
1.2x on top of a clip that already crammed 1.5 run cycles into 33 frames. Now `apply_frame` copies
the pack run's **whole** upper body for the approach and only the right arm is taken over at the
end, lerped out of wherever the run's swing left it (`hand_rel`) so there is no jump. The budget
went 24/5/18 = 47 frames to **40/5/21 = 66**, which is what lets the whole 34-frame clip play:
measured 1.07 s at **x0.97**, 71 game frames, travelling **2.42** against the old 1.79.

**His light attack crossed his legs because the stance it borrows is a fencing stance.** The thrust
freezes one pose for the whole clip, and it took it from the pack's `block idle`, whose feet are in
line — measured along the hip joints' own axis the ankles sat at **-0.535**, i.e. the left boot on
the wrong side of the right. It now borrows `attack (4)` frame 15, a standing cut with both boots
planted apart: **+0.531**, no crossed frames, and the arm reaches full extension without the IK
falling short. Two wrong turns on the way: his own idle stands upright, which moves the shoulder
back and puts the thrust target 0.189 beyond his reach, and overlaying the guard's torso on the
idle's hips doubles the lean — photographed, it bent him double.

**Miyamoto's spin needed a new aim mode, not a new clip.** The blade rode **-41 to -7 degrees**
below horizontal all through Kuruma, which is what reads as a back fist, and neither existing mode
fixes it: `Full` aims at the opponent's CHEST, which from a raised hand points the tip down, and
`YawOnly` leaves the clip's own wheel (-36 to +39) alone. **`AimMode.Level`** flattens the desired
direction to the horizon before aiming, so the blade stays level AND points at the man: measured
**0.2 to 3.2 degrees** through the hit window. The enum is appended, never reordered — those values
serialize by index on every AttackData asset.

Across both fighters' twelve moves: zero frames below the floor. The two remaining negatives are a
single blend frame on Richard's bash (-0.018) and Miyamoto's Nito Cross, where the legs cross
because the move is a full turn.

**And the two process traps that cost the most time here**, both already written in CLAUDE.md and
both worth re-reading before the next session: `Use Mixamo packs` ran against a **stale assembly**
and silently fitted the OLD clip map (`trimmed=False` in its own log is the tell), and
`Apply the roster balance` wrote the new frame budget in memory but **never to disk**, so the charge
kept its 47-frame budget and the window cut the first nine frames off the run. Read the asset back,
not the console.

### 2026-10-22 - Richard's three: a short run, a plain thrust, and two swings that are not Bjorn's

**The run is shorter.** 48 frames against 66, clip 24 frames against 34, measured 53 game frames
travelling 2.05 - where the first attempt at "slower" had made it a 71-frame journey.

**The light attack is a plain thrust again, and the stance had to be picked by measurement.** The
lunging footing that uncrossed his legs last time came from the pack's standing CUT, so the thrust
read as a lunging slash. His own idle is the obvious answer and it is wrong twice over: `take()[0]`
is the clip's first frame, and an authored clip is imported CopyFromOther / Based Upon ORIGINAL
while a pack clip is CreateFromThisModel / Body Orientation - so the same pose does not land the
same way. Copying the idle's frame measured **-0.066** in play where the idle itself measures
+1.741. What Blender does predict is the SIGN, and the scan across the pack is decisive: block idle
-0.531, idle (4) -0.321, every attack frame but one negative, and the WALK **+0.38 to +0.50**
throughout. Its frame 2 is the double-support beat, both boots down. Result: **+0.797, zero crossed
frames**, and the arm now reaches within 0.053 of the target instead of 0.189 - a full extension.

**Twin Hew is two swings again** - a single pack chop is Bjorn's special, since his Blood Eagle is
a leaping overhead and the sword pack's "attack" is one too. It splices the pack's overhead into
its big committed chop, frames verbatim, with a four-frame cross-fade at the join, and the hit
lands on the second swing (52/5/15 = 72 frames, contact 27.4, x0.94).

Getting the legs right took three attempts and the first two were wrong in instructive ways. A
four-frame lead-in easing out of the standing stance made it WORSE - 11 crossed frames against 8 -
because interpolating two uncrossed leg poses swings a thigh through the other leg. Shortening the
special blend-in from 0.12 s to 0.05 s cleared the floor dip (12 frames at -0.099 to **zero**) but
left the crossing, which proved the crossing was never the transition. The answer was in the source
clips: measured per frame, `slash` is crossed 0-16 and 33+ and clean only at **17-32**, `attack` is
clean 0-58. Moving the first swing into 17-32 gave **0 crossed frames, gap +0.117**.

All six of Richard's moves now measure zero frames below the floor and zero crossed frames, bar one
-0.018 blend frame on the shield bash.

**Two silent-failure traps, both the same shape as the ones already on file.** `BlendIn` was only
applied when the Any State transition was CREATED, so editing the constant did nothing to an
existing controller while the console reported the new number - it is written every run now. And
`Wire special and riposte states` never included Salaadin, so every shared change there had been
stopping at three fighters; he is in the roster now and all four controllers read 0.05 s.

**The verification harness was lying for two whole passes.** Six landed specials empty the stamina
bar, `TryAttack` then returns false, and the sampler happily measured the IDLE and reported it as
the move - which is where "SP+Heavy plays Richard_Idle with 5 crossed frames" came from. It tops
the bar up before every swing now and bails on a false return. Two smaller ones found alongside:
`GetCurrentAnimatorClipInfo(0)[0]` can name the idle right through an attack (take the highest
weight), and `BakeMesh` output has no bone weights, so the foot-vertex probe must read them from
`sharedMesh`.

### 2026-10-22 - Richard holds his sword and shield in his fingers, not his wrist

The last item on the list, and the whole thing hinged on something that cost five identical
photographs: **`WeaponAim` caches the weapon's rest local position at Start and writes it back
every frame**, so every live change to the sword's transform was stamped out before the frame was
drawn. The numbers said it had moved; the pictures said it had not. His shield has no `WeaponAim`,
which is why that one responded and the sword would not - the asymmetry was the clue.

The fault itself: his sword's mount had been tuned to put the blade's axis through the HAND BONE,
which is the wrist joint. Photographed close up, the crossguard lay across the back of his hand,
the grip ran up inside his forearm, and his fingers - open, since nothing closes Richard's, he has
no `TwoHandGrip` - hung clear of the weapon entirely.

Both weapons now slide along the **wrist -> fingertip** vector by **0.55** of its length. The whole
way is wrong: at 1.0 the grip sits past his fingertips with the crossguard clear of the hand, which
photographs as a sword floating beside him. Sliding along the weapon's own long axis is wrong too -
it only accounts for 0.060 of the 0.245, because fingers wrap a haft perpendicular to it. Nearest
weapon vertex to the fingertip centre: sword 0.089 -> 0.045, shield 0.083 -> 0.017, and verified
mid-swing as well as at rest - his fist closes on the red grip with the crossguard just beyond it.

The rotations were not touched, so the facing work on that shield (including the 180 deg roll that
stopped him showing the enemy the enarmes) stands: a translation cannot turn a shield round.

One more measuring trap for the file: these weapon meshes have `isReadable` FALSE, and
`mesh.vertices` on a non-readable mesh returns an EMPTY array at runtime instead of throwing, so a
min-distance loop keeps its sentinel. A column of 9999 is that, not a weapon that flew away.

### 2026-10-22 - Three new specials for Richard, and Miyamoto's four cut a quarter

**Miyamoto's specials are all faster.** Totals cut about 25% - Nukitsuke 30 -> 22 frames,
Nito Cross 47 -> 34, Kuruma 32 -> 24, Iaijutsu 36 -> 27 - and the two with pinned playback speeds
raised with them (Nukitsuke 1.00 -> 1.25, Iaijutsu 1.30 -> 1.50). Measured state speeds afterwards:
Kuruma x0.81 -> x1.08, Nukitsuke x1.00 -> x1.25, Iaijutsu x1.24 -> x1.43, Nito Cross x2.26 -> x2.40.
Kuruma had been playing SLOWER than authored, which is likely what read as sluggish.

**All three of Richard's specials are new clips: no running, no jumping.** The charge and the
leaping splice are both gone.

- **SP+Light "Rising Draw"** - `draw sword 2`, a rising draw cut from a stand. 34 frames,
  pelvis sweep 16 deg, hips 1.42-1.64.
- **SP+Medium "Wheel Cut"** - `sword and shield attack (2)`, a horizontal spinning cut.
  36 frames, sweep 263 deg, hips 1.43-1.57.
- **SP+Heavy "Judgement"** - `sword and shield attack (3)`, a diagonal-down spinning cut.
  44 frames, sweep 279 deg, hips 1.08-1.47.

All three play at **x1.00**, none puts a foot through the floor, and none leaves the ground - the
splice that replaced read 2.97 on that last column.

**The first attempt was wrong and the mistake is the useful part.** Screening the pack for "no
running" on the clips' own TRAVEL column eliminated every real strike and left only `casting` and
`power up`, whose numbers look like big overheads. Photographed through their trimmed windows, both
are buff gestures: Richard goes down on one knee with the shield planted. Two things came out of
that: **root motion is discarded on these clips, so a clip's travel never reaches the game** - the
AttackData lunge moves him, and "advancing" is not running - and **a jump is the HIPS RISING**,
which is body animation and does reach the game. Screening on `maxHipsAboveRoot` instead reopened
the two standing spins, which is what he now has.

**One trap avoided rather than hit, for once.** `attack (3)` is also Salaadin's MEDIUM, and a trim
is keyed by CLIP - widening its core for Richard would have re-timed Salaadin's move, exactly as
the shared `sword and shield attack` trim once dropped his Hattin Cleave to x0.43. A CONTACT is
keyed by STATE, so Richard's window is set there and Salaadin's medium still measures x0.96.

The asset names (`Richard_Special_ShieldCharge`, `_ShieldBash`, `_DoubleHigh`) are deliberately
unchanged so the scene's GUID references survive; only the clips, the displayed names and the
frame budgets moved.

### 2026-10-22 - Richard's specials are his normals, and the camera makes the difference

The user's call, and the right one: the SwordShieldPack has no more standing strikes that are not
spins or buff gestures, so rather than keep hunting, each special now plays the clip of the NORMAL
on the same button - SP+Light `Richard_Thrust`, SP+Medium `slash (5)`, SP+Heavy `slash` - and a
heavier camera shake tells them apart.

Renamed to read as the bigger version of the same swing: **Lionheart Thrust**, **Crusader Cross**,
**Judgement**. Asset names are unchanged so the scene's GUID references survive.

Three things the change turned on:

- **Per-STATE contacts.** A trim is keyed by CLIP, so a special sharing its normal's clip shares
  its trim; the window and speed come from `swing_contacts.json` instead, copied from the normal
  (15.0/x1.3, 17.4/x1.6, 17.7/x1.4). The specials measure x1.23 / x1.60 / x1.40 against the
  normals' x1.21 / x1.60 / x1.40 - the same swing at the same speed.
- **The hit geometry moved with the clip.** `height`, `comesFrom`, `hitSide`, `aim`, `size` and
  `offset` are now each normal's, because a box that does not match what is on screen reads as
  unfair. Only `range`, `lunge`, damage and stamina stay special.
- **The camera.** `OnImpact` already shook for Heavy and Special and treated them the same, so
  nothing on screen separated them. `specialShakeMultiplier` 2.4 and `specialShakeDuration` 0.32 s
  now do. Measured: Heavy 0.108 over 0.18 s, SP+Heavy **0.374 over 0.32 s**; Light and Medium shake
  **0.000** where their specials shake 0.346-0.374. The light and medium specials are the ones this
  sells hardest - no shake at all becomes a big one.

Verification pass: all six moves 0 frames below the floor, 0 crossed frames, hips 1.46-1.63 (no
leap), and each special's numbers now match its normal exactly (SP+Light ankle gap 0.797 = Light's
0.797; SP+Heavy 1.651 = Heavy's 1.651). Salaadin re-checked after the shared menu run and unchanged
at x0.96 / x1.02 / x1.00 / x1.02 / x0.67 / x1.07.

Also corrected a claim from earlier in the day: **a bought animation pack does not need Mixamo bone
names.** Pack clips import `CreateFromThisModel`, so any FBX that maps as Humanoid retargets fine -
which widens what is worth buying considerably.

### 2026-10-22 - Both hands on the hilt, a shield that hides the fist, and a katana that pierces

Four from a play test, all four measured and photographed.

**Richard's shield is 0.125 further out.** The metric that mattered was the WORST left-hand vertex,
not the centroid: the centroid read 0.065 behind the front face while his worst knuckle stood 0.090
PROUD of it, which is the fist showing through the paint. Pushed along the disc's own normal, the
worst vertex now sits 0.035 behind the face and the shield presents the lion and cross clean.

**Miyamoto now holds the katana with both hands, and the bug was a good one.** His left fist was
solved up at his chest, nowhere near the hilt - while `TwoHandGrip` reported `OffHandMiss` 1.6e-07,
because the wrist was faithfully on its socket and the SOCKET was wrong. `SetupWeapon` placed
`LeftGrip` at the weapon mesh's bounds CENTRE in x/y, and on a long curved blade that centre sits
**0.357 world units off the haft**. The socket belongs on the haft AXIS, local (0,0,z), because a
weapon mounts at its hand bone's origin so the weapon origin IS the right fist. Bjorn had the same
fault at 0.148 - half the size, same cause - and is fixed with him; it is probably what
`PalmOffset()` in `TwoHandGrip` had been compensating for. Both now photograph with the fists
stacked on the haft.

**Miyamoto's armour pierce was 0 on every normal** - the katana fighter pierced least in the whole
roster. Now 8/10/16 on the normals, 18/20/24/24 on the specials and 8 on the ripostes, against
Richard's best of 12. Pierce is a percentage off the defender's armour.

**SP+Medium (Nito Cross) was a spinning back fist because its `aimMode` was `Off`**, which leaves
the clip's own wheel alone. `Level` is the same cure Kuruma got: measured through the move the
blade drops from **84 deg of elevation to 0.0 deg** across the hit window (frames 20-32) and holds
flat, on a 356 deg pelvis sweep - about the 90 degrees of sword angle the user asked for. It
photographs as a horizontal slash with the edge leading.

Worth remembering: **Miyamoto and Bjorn have no move table**, so their aim and box geometry live
only in the `.asset` - nothing regenerates them. And a transform solved in PLAY mode is thrown away
when play stops; all three scene fixes had to be re-applied in edit mode and the scene saved.
