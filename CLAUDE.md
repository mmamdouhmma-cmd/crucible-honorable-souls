# CRUCIBLE: Honorable Souls

1v1 3D fighting game. Unity 6000.4.4f1 (URP), C#. Target input: PS4/PS5 controller (P2) and keyboard (P1).

## Layout

- `Assets/_project/Scripts/characters/` — `Fighter` (movement, dash, block, facing, hit reaction), `FighterStats`, `FighterData`.
- `Assets/_project/Scripts/combat/` — `CombatSystem` (attack phases, hitstun, hit resolution), `HitDetector` (trigger hitbox), `AttackData` (ScriptableObject per attack), `DamageCalculator`.
- `Assets/_project/Scripts/config/BalanceConfig.cs` — global tuning constants, `FRAME_TIME = 1/60`.
- `Assets/_project/Scripts/input/` — `InputManager` singleton on the `GameMangers` object.
- `Assets/_project/Scripts/UI/FightHud.cs`, `Assets/_project/Scripts/core/FightCamera.cs`.
- `Assets/_project/Characters/<Name>/Attacks/` — per-character `AttackData` assets, named `<Name>_<Weight>_<Move>`. `Characters/<Name>/Model/` — the Tripo rigged FBX + textures + material (git-ignored, see Git notes). `Characters/_Unrigged/` — Tripo meshes not rigged yet (git-ignored).
- `Assets/_project/Animation/Controllers/` — `P1_Bjorn.controller`, `P2_Miyamoto.controller`. `Animation/Clips/{Mixamo,Maria}/` — placeholder test clips; `Animation/Clips/Custom/` — for Blender exports.
- `Assets/_project/Data/Fighters/` — `FighterData` assets. `Assets/_project/Input/` — the Input System actions asset. `Assets/_project/Art/ConceptArt/` — character portraits.
- `Assets/_project/_Legacy/` — unreferenced leftovers (old Bjorn test model, Maria mesh/material, `Player2Controller.cs`, URP template readme). Safe to delete once nobody misses them.
- `Assets/_project/Scenes/testarena.unity` — the test scene.

## How combat works

- Each fighter root has `Fighter`, `CombatSystem`, `Animator`, `Rigidbody`, `CapsuleCollider`, and a child named `Hitbox` with `HitDetector` + a trigger `BoxCollider`.
- Attacks are frame data at 60fps: startup, active, recovery. `CombatSystem.Update` arms the hitbox when startup ends and disarms it when the active window ends. One hit per swing.
- `HitDetector.OnTriggerEnter` is the only damage path. It ignores trigger colliders and the owner, then calls `CombatSystem.OnHit`, which handles dash evasion, blocking, damage, hitstun and the hit animation.
- **Hitboxes are authored in world units.** The fighter roots are scaled in the scene (P1 4x, P2 3x) to size their models, and `HitDetector.Activate` divides that scale back out. Same units as `fighterRadius`, `dashDistance` and walk speed. The gizmo preview (select a fighter) shows the real box.
- `AttackData` also carries: `comesFrom` (which dash directions evade it), `hitSide` (Straight/Left/Right, picks the get-hit animation, set by hand, not detected), `hitstunFrames`.
- Hitstun: `CombatSystem.ApplyHitstun` blocks attacks, dashes and walking for the given frames. By default a hit also cancels the defender's attack in progress (`hitInterruptsAttack` on CombatSystem).

## Animator parameters the code uses

- `MoveSpeed` (float): walk blend, set every frame from input.
- `LightAttack`, `MediumAttack`, `HeavyAttack` (triggers): from each attack's `animatorTriggerName`.
- `HitSide` (int: 0 Straight, 1 Left, 2 Right) and `GetHit` (trigger): fired on hit. Until a controller defines both, `Fighter` only logs the side.

## Conventions

- Keep gameplay distances in world units; never rely on the model scale.
- New per-attack tuning goes on `AttackData` with a `[Tooltip]`; global numbers go in `BalanceConfig`.
- Debug logs are prefixed `P1:` / `P2:` and are on by default (`debugLogs` on each component).

## Compile check without opening Unity

Unity's bundled Roslyn can compile `Assembly-CSharp.csproj` (regenerate it from Unity if files were added). From Git Bash:

```
U="C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor"
"$U/Data/NetCoreRuntime/dotnet.exe" "$U/Data/DotNetSdkRoslyn/csc.dll" -nologo -target:library \
  -out:/tmp/Assembly-CSharp.dll @defs.rsp @refs.rsp @files.rsp
```

where the `.rsp` files are built from the csproj's `DefineConstants`, `HintPath` references and `Compile Include` entries.

## Git notes

- `Assets/_project/Characters/*/Model/` and `Characters/_Unrigged/` (Tripo FBX meshes, ~700 MB, ~1M verts each) are not committed; they need Git LFS (after decimation) or stay local. The scene references the Bjorn and Miyamoto FBX directly, so a fresh clone has no character models until those folders are copied in.
- `Assets/_Recovery/` holds Unity crash-recovery scenes. Do not commit.
- `.plastic/` is Plastic SCM state. Leave it out of git commits.

## Session log

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
