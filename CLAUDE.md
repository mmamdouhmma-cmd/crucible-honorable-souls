# CRUCIBLE: Honorable Souls

1v1 3D fighting game. Unity 6000.4.4f1 (URP), C#. Target input: PS4/PS5 controller (P2) and keyboard (P1).

## Layout

- `Assets/_project/Scripts/characters/` — `Fighter` (movement, dash, block, facing, hit reaction), `FighterStats`, `FighterData`.
- `Assets/_project/Scripts/combat/` — `CombatSystem` (attack phases, hitstun, hit resolution), `HitDetector` (trigger hitbox), `AttackData` (ScriptableObject per attack), `DamageCalculator`.
- `Assets/_project/Scripts/config/BalanceConfig.cs` — global tuning constants, `FRAME_TIME = 1/60`.
- `Assets/_project/Scripts/input/` — `InputManager` singleton on the `GameMangers` object.
- `Assets/_project/Scripts/UI/FightHud.cs`, `Assets/_project/Scripts/core/FightCamera.cs`.
- `Assets/_project/Characters/<Name>/Attacks/` — per-character `AttackData` assets, named `<Name>_<Weight>_<Move>`. `Characters/<Name>/Model/` — the Tripo rigged FBX + textures + material (git-ignored, see Git notes). `Characters/_Unrigged/` — Tripo meshes not rigged yet (git-ignored).
- `Assets/_project/Animation/Controllers/` — `P1_Bjorn.controller`, `P2_Miyamoto.controller`, `P3_Richard.controller`. `Animation/Clips/{Mixamo,Maria}/` — placeholder test clips; `Animation/Clips/Custom/` — for Blender exports.
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

The session log lives in `Sessions.md` (repo root) — this file was past its character limit.
**Append new session entries to the bottom of `Sessions.md`**, as `### YYYY-MM-DD — <title>`.
Durable facts belong up here instead; the log is for narrative and for the gotchas of a day.

## Where things stand (end of 2026-10-21)

- **The game has a front end.** Boot (logo, press any button) -> main menu -> Battle Now (1P or
  2P, character select, arena select, difficulty) / Online / Practice / Move List / Settings /
  Quit, with a restyled pause and KO screen, wipe transitions and an ember-lit backdrop. Built from code in `Scripts/UI/{GameMenu,MenuArt,MenuStage}.cs`; run
  `Crucible > Front End > Set up the front end` once on a fresh clone.
- **Matches open with READY - 3 - 2 - 1 - FIGHT** (`GameMenu.CountInRoutine`). Until the last
  beat `inMatch` is false, the fighters are frozen and input is off - and `SetAi` is deliberately
  deferred to FIGHT too, because the AI calls `CombatSystem.TryAttack` directly and neither
  `InputEnabled` nor a disabled `Fighter` holds it back.
- **A uGUI menu that handles its own keyboard must shut the EventSystem out of it**: give every
  row Button `Navigation.Mode.None` AND clear `EventSystem.currentSelectedGameObject` each frame,
  or `StandaloneInputModule` navigates and submits in parallel and presses land on the wrong row.
- **Character and arena select show the LIVE rigs and the real arena**, not art: the menus pose
  the two fighters for the camera (`GameMenu.StageFighters`) and arena select switches the arena
  behind the UI as you scroll. Two arenas ship (`ArenaDirectory`, whose `shipped` flag also
  drives what the build keeps).

- **The roster's numbers live in ONE table**: `Editor/CrucibleBalance.cs`
  (`Crucible > Combat > Apply the roster balance`), which writes each fighter's `FighterData`,
  every `AttackData`'s damage/stamina/pierce/frames/hitstun, and the `Fighter` components in the
  scene. Each fighter now has his own data asset (`bjorn_data`, `miyamoto_data`, `richard_data`);
  `testfighter_data` is no longer referenced. Archetypes as of 2026-10-20: Miyamoto 80 hp / 100
  stamina / 8 armour / x0.92 / walk 4.4, Bjorn 95 / 110 / 14 / x1.10 / 3.8 (costliest moves),
  Richard 115 / 85 / 22 / x1.00 / 3.2 (85% block at 2/s drain). `FighterData.AttackSpeed` and
  `DashDistance` are DEAD fields - attack speed is only the AttackData frame counts, and changing
  one re-times its clip, so re-run `Use Mixamo packs` after a frame change.
- **Three fighters now, not two.** Richard (sword + kite shield, the tank: 105 hp, 110 stamina,
  24 armour, movement 33) lives on `Player3` with `P3_Richard.controller` and his own
  `richard_data`. His whole move set is the table in `Editor/CrucibleRichard.cs`; build him with
  `Crucible > Characters > Richard > 1/2/3`, then `Rebuild Player3 (Richard body)`, then the shared
  menu items (special states, `Use Mixamo packs`, `Wire combo strings`). His animation is the
  **Pro Sword and Shield pack** plus four clips keyed by hand in
  `ArtSource/Blender/richard_clips.py` (thrust, shield charge, shield bash, double-high) — the pack
  has no thrust and no bash. He carries a shield, so he is NOT in `CrucibleCharacterSetup.TwoHanded`
  and has no `TwoHandGrip`.
- **Four fighters now.** Salaadin (falchion + round shield, the balanced one: 95 hp, 115 stamina,
  18 armour, x0.92 damage, walk 4.1) lives on `Player4` with `P4_Salaadin.controller` and
  `salaadin_data`. His move set is the table in `Editor/CrucibleSalaadin.cs`; build him with
  `Crucible > Characters > Salaadin > 1/2/3`, then `Rebuild Player4 (Salaadin body)`, then the
  shared menu items, then `Salaadin > 4 Rewire Player4 to the move set` (the scene holds his
  AttackData by GUID, so a renamed slot needs re-pointing without rebuilding the fighter).
  **He strikes with the falchion and nothing else** - no thrusts, no shield bashes, no kicks, and
  his parry riposte is a saber counter where the other three answer with a boot. His two
  showpieces are the NORMALS (Medium = Shin Cut, Heavy = Whirling Saif) and the two compact pack
  swings sit on the specials (SP+Medium = Crescent Slash, SP+Heavy = Hattin Cleave); the frame
  data and costs stayed with the SLOT, so the normals play those long clips at about x2.7.
  His guard is play-verified against the authored clip: shield boss . forward **1.00**, blade edge
  . forward **0.94**, elbows 113/128, every bone within the idle's own breath. **His folder is lowercase
  `Characters/salaadin`** - renaming it deadlocks the Editor if any shell is sitting in it, so
  `Folder()` in `CrucibleBalance.cs` and `CharDir()` in `CrucibleCharacterSetup.cs` handle it in
  code. He shares only Richard's two WALK clips; his idle, his shin cut, his spin, his rush and
  his saber riposte are keyed in `ArtSource/Blender/salaadin_clips.py`; the light, both specials
  and the dodge riposte are pack clips Richard does not use.
- **Porting a hand-keyed clip to another character's rig: convert, never scale by height.**
  Learned on Salaadin, whose rig is 1.00 tall against Richard's 1.85 but whose arms are half
  human proportion:
  - Limb targets go in **fractions of that limb's own reach from its own joint**, which is what
    `pose()` in `salaadin_clips.py` takes. Height-scaled metres miss.
  - Swing a limb with the **two-bone IK** (`reach()`), not by rotating a bone about its own axis -
    bone roll differs per rig, and a limb keyed that way comes out sideways.
  - **Hip travel from a pack clip is in the PACK rig's units** (those armatures are scaled 0.01).
    Carry it as a fraction of the source's rest hip height. The check is `RootT.y` on the imported
    clip: healthy clips sit near 0.8-1.0, and a broken one read -52.4.
  - A weapon's mount decides its `aim()` arguments: Salaadin's shield has its boss on `-palm`, so
    `(UP, -FWD)` where Richard's shield takes `(UP, FWD)`. His falchion is mounted with a -20 deg
    tweak, so `pose()` turns the wrist back by `BLADE_LEAN` before aiming - without it the guard
    carries the edge 20 deg behind straight down and every cut slices a little with the spine.
  - **The elbow pole must sit BELOW the shoulder.** Richard's is taken from the hips, which are
    most of an arm's length down; Salaadin's was written from the shoulder with no vertical term,
    and both arms came out poled level - wings.
- **An armature-only clip FBX has no bind pose, so Blender's exporter hands Unity the pose it is
  evaluated on as the file's rest.** Unity then builds the clip's avatar from that pose, and every
  retargeted frame comes out as the body's own T-pose plus a delta. A clip whose arms barely move
  (Salaadin's guard idle) therefore played back as the pure bind T-pose: elbows locked straight,
  arms out sideways, the shield lying flat. `rest_lead_in()` in both `*_clips.py` fixes it at the
  source - it keys the real rest pose at frame 0, shifts the clip to 1.., and leaves the rig
  EVALUATED on frame 0 so that is what the exporter writes. The scene is renamed `restlead`, which
  becomes the FBX take name, and `CrucibleMixamoSetup` drops frame 0 from any take by that name.
  Richard's four authored clips were rebuilt through the fixed `richard_clips.py` too.
- **An authored clip must share its character's avatar AND keep its own orientation.** Both are set
  by `CrucibleMixamoSetup.ImportAll` for anything under `Clips/Custom/` whose name starts with a
  character's, via `OwnerAvatar()`; pack clips, shared between fighters with different bodies, keep
  `CreateFromThisModel` and `Based Upon: Body Orientation`.
  - **Root Transform Rotation -> Based Upon: ORIGINAL** (`keepOriginalOrientation = true`). "Body
    orientation" makes Unity work out which way the character faces FROM THE POSED BODY and bake
    the difference into every frame. That is right for mocap captured facing anywhere and wrong for
    a clip keyed facing a known direction: on Salaadin's bladed guard it read the turned shoulders
    as his facing and yawed the whole skeleton **66.6 deg**, so the shield stood edge-on to the
    opponent and the sabre pointed across him. **The tell is that every bone is off by the same
    angle** - check that before blaming a wrist, a limit or a pole vector.
  - **Copy the body's avatar** (`CopyFromOther`), which only validates if the clip's node chain
    matches the body's - see the `*_ClipRoot` empty in `export()` below.
  - **`clip.SampleAnimation` does NOT go through the Animator's retarget**, so it shows the clip as
    authored and hides both faults. Measure a pose in PLAY mode, off the live rig.
  - `HumanPoseHandler(avatar, root)` on these fighters returns a FIXED pose whatever the rig is
    doing, so its muscle values prove nothing. Compare bone rotations instead.
- **An armature-only clip FBX needs a second, empty object exported beside the armature.** With the
  armature alone, Unity makes it the file root and the 100x scale Blender writes on it lands on the
  avatar's ROOT, where the body FBX (which carries a mesh too) has root(1) -> `<Name>_Rig`(100) ->
  Hips. Unity reads an avatar's human scale in root space, so that one missing level made the
  clip's avatar believe the rig was 100x smaller, and retargeting across two human scales rewrote
  every stretched joint - the guard arrived with the elbows 16 deg tighter. `export()` in both
  `*_clips.py` now exports an empty named `<Name>_ClipRoot` beside the armature; it must not share
  a name with anything `load()` brings in or Blender appends a `.001`.
- **The scene holds the whole roster; the menu benches whoever is not fighting.**
  `GameMenu.ApplySides(first, second)` switches the non-participants off and calls
  `Fighter.RefreshOpponent()` on the pair — `FindOpponent` used to take the first other Fighter in
  the scene, which with three rigs is the wrong one (it made Richard's heavy hit a benched Bjorn).
  Anything that walks the roster must skip inactive fighters: K.O., the winner, `PlaceFighters`.
  **`FightCamera` had the same bug**: it resolves Player 1 and Player 2 ONCE in `Start`, so after
  the numbers moved it kept framing a benched fighter standing on his mark - the view read as
  off-centre and swung round. `PlaceFighters` now calls `FightCamera.RetargetToFighters()`, which
  re-finds the pair and re-reads the framing from the camera's AUTHORED scene transform (kept in
  `homePosition`, because the select screens move the camera and never put it back).
  Character select takes two locks (Player 1, then Player 2 / the computer) whenever the roster is
  bigger than two, and `StageFighters` lines up N.
- **Tasks 1–6 of `Design/Plan_LiteMobile.md` are done and play-verified**: six specials on
  `SP + Light/Medium/Heavy`, ten combo strings per character, four counter ripostes, defensive
  health recovery, per-fighter health/stamina/armour, a percentage HUD, a Moves screen, and a
  web build under half its old size (155 MB, one arena per build).
- **Task 7 (teaching the AI the lite game) was dropped by the user**, who is writing a new task
  list — the next session starts from that list, not from the lite plan's running order.
  `Design/Plan_2Weeks.md` is SUPERSEDED by `Plan_LiteMobile.md`.
- Attacks, combos, specials and ripostes are all authored data on `AttackData` + the editor
  menus that write them (`Crucible > Combat > Wire combo strings`,
  `Crucible > Animation > Wire special and riposte states`, `Use Mixamo packs`). Changing a
  move means editing the table in the editor script and re-running the menu item, not editing
  `.asset` YAML.
- Animation is the **Mixamo** packs (`Animation/Clips/Mixamo/{AxePack,AxePack2H,GreatSwordPack}`)
  until every system is finished; the mocap lane (`ArtSource/Mocap/`) and its clips stay in the
  repo, unused — the user's verdict on them was "wasn't the best for now".
- **Open, and only the user's phone can answer it**: frame rate, whether the on-screen buttons
  fall under a real thumb, online latency. Two touch-layout questions are written up in the lite
  plan and deliberately not changed blind.
- **Salaadin's rig was fixed by hand, because Mixamo cannot rig him.** His mesh is mostly cloth:
  the leg geometry stops at z=0.33 (the top of the boots) and above it there is only coat skirt and
  cloak. Mixamo's auto-rigger fits its skeleton to the mesh VOLUME, so where the thigh and pelvis
  should be it finds only fabric. Two careful marker rounds proved it - the second was symmetric
  and WORSE. `ArtSource/Blender/salaadin_fixrig.py` places four joints by CONSTRUCTION instead, by
  moving bones in EDIT mode (which moves the rest pose while the mesh and vertex groups stay put,
  so Mixamo's weights are kept and nothing is re-weighted):
  - **elbow** onto the shoulder->wrist line at 52% (Mixamo had it at 35%, giving an upper arm
    SHORTER than the forearm - the "snake arm");
  - **knee** onto the straight hip->ankle line (Mixamo bent it 128 deg into the skirt);
  - **hip joints** in to 11% of height apart and down to 52% up (Mixamo had them 19.8% apart -
    twice his ankle separation - and 54% up, out in the coat);
  - **hip fore/aft over the ankle**, which is the one that caused the SPLITS. The clips copy the
    pack's leg bones rest-relative (`apply_frame`), which is only valid when both rigs' legs rest
    the same way: the pack thigh hangs at (0.00,-0.01,-1.00) and Richard's at (+0.08,+0.13,-0.99),
    while Mixamo left Salaadin's at (-0.01,+0.39,-0.92) - **tilted 23 deg backward**, because his
    hip socket sat forward in the coat. Every copied leg rotation landed 23 deg out and compounded
    down the knee. **His arms never showed this because the clips key them with IK, not copied
    rotations** - only the legs come through `apply_frame`.
  Bone ROLL is preserved across all of it (`align_roll` to each bone's saved z axis), since a roll
  angle describes different axes once the head or tail moves.
- **`LEG_DAMP` in `salaadin_clips.py` narrows his stance, and it is not a hack.** With the rig
  correct his guard reproduced the pack's block idle EXACTLY (L 61.5 / R 48.0 against the pack's
  61.5 / 49.2) - and that pack clip plants the feet 94 deg apart. It reads fine on the pack rig and
  on Richard, who is short-legged and already stands wide, and reads as the splits on Salaadin, who
  is long-legged with his feet together at rest. `LEG_DAMP = 0.55` eases each leg rotation back
  toward rest, keeping the shape of the stance and taking the span out: 94 -> 50 deg, against
  Richard's 56. Verified across his states: idle 50, walk 31, shin cut 50, whirling saif 50,
  veiled rush 35; guard still shield boss . forward 0.98, blade edge . forward 0.94, elbows 118/129.
- **After re-pointing a state, READ THE CONTROLLER BACK.** `Use Mixamo packs` is the only menu
  item that writes a state's motion, it times out over the CLI on every run, and a run that
  returns `HTTP 400` writes NOTHING while the console still shows clip-fitting lines from an
  earlier run. A whole evening's clip reassignment was reported as done and never reached
  `P4_Salaadin.controller`. Load the AnimatorController and print each state's `motion` and the
  FBX it came from; a console message is not evidence, and neither is a screenshot of a pose you
  expected to change.
- **The whole roster is rigged the same way now: a Rigify metarig, 79 bones.** Salaadin's was
  hand-built by the user; Richard, Bjorn and Miyamoto are fitted by construction with
  `ArtSource/Blender/fit_metarig.py` (metarig in, 92 face bones out, joints placed against human
  targets, body skinned) and converted by `metarig_to_mixamo.py` (Rigify names -> Mixamo, spare
  seventh spine bone dissolved, 13 leaf bones, hand roll, project export options). Rebuild chain:
  `fit_metarig.py` -> `metarig_to_mixamo.py` (overwrite the FBX **in place**, the GUID matters)
  -> `Rebuild Player<N>` -> `Use Mixamo packs` -> `Wire special and riposte states` ->
  `Wire combo strings` -> `Salaadin > 4 Rewire` -> `Apply the roster balance`. The metarig blends
  live in `ArtSource/Blender/metarigs/`; the pre-re-rig bodies are in
  `ArtSource/Characters/_backup_20261022/premetarig_bodies/`. **None of it is in git** - the
  Model folders are gitignored - so a fresh clone re-runs the chain.
  - The existing rig is the ANCHOR, not a thing to discard: it knows where a shoulder sits inside
    a fur mantle and where a knuckle is inside a closed fist. Only the measured faults are
    corrected on top of it, and the left side is mirrored onto the right so `asym` is 0.
  - **Do NOT skin with `parent_set(type='ARMATURE_AUTO')`.** Bone-heat returned "failed to find
    solution" on Miyamoto and left all 119,952 verts unweighted, and where it works it throws
    away Tripo's costume weights and the hand-built finger chains. TRANSFER the old vertex groups
    instead (twist bones fold into the limb they twist about) - 0 unweighted verts on all three.
  - **`object.parent = None` does not preserve a world transform.** Blender keeps `matrix_basis`
    and drops the parent's contribution, so a body parented to a scaled armature lands at a new
    size - Richard's came out 42.75x and the next script reported his ankle at 50% of height.
    Capture `matrix_world` and put it back.
- **Unity's humanoid auto-mapper is a name heuristic and is NOT exhaustive.** With the whole
  roster on Mixamo names and both hands fully boned it took 30/30 fingers on three fighters and
  26/30 on Miyamoto, dropping RightRing/RightLittle intermediate and distal whose bones exist and
  are mirror images of the left ones it did take. `CrucibleCharacterSetup.FillMissingHumanBones`
  now fills any slot left empty whose Mixamo-named bone is in the hierarchy. It only ever ADDS.
- **A hand-tuned grip OFFSET does not survive a re-rig, and a rotation usually does.**
  `Richard_Sword` carried `(0.0386, 0.1908, -0.0029)` against the old hand bone; the re-rig threw
  the sword 33.18 world units off his hand. Every other weapon mounts at its hand bone's ORIGIN,
  which is the tell that such an offset is compensating for the rig and not for the weapon.
  Check it by measuring the nearest weapon vertex to the hand bone: healthy is 0.02-0.24.
- **A rig change puts every authored clip back on its own avatar, and `ImportAll` says so.**
  `CopyFromOther` only validates while the clip's node chain matches the body's, so changing bone
  names or count invalidates it - `Transform 'mixamorig:Hips' for human bone 'Hips' not found`.
  The fallback keeps the clip playing rather than leaving a state empty, but the retarget quality
  goes, and the fix is to re-export through `*_clips.py` against the new body. Check which states
  actually care: after the 2026-10-22 re-rig only 5 of 90 states still used a custom clip.
- **Fighter SIZE is a per-character table, and three different "heights" are in play.**
  `CrucibleCharacterSetup.StandingHeight` sets how tall each one stands, in the order the user
  wants them read: Richard, Bjorn, Salaadin, Miyamoto. The scale is SET from the bind-pose skinned
  bounds box, so the table is CALIBRATED, not literal. Measure **stature, crown MINUS sole** - it
  is the only one of the three that depends on the child scale alone. The mesh's own vertical span
  is wrong (Richard's cape and Salaadin's coat hang below their heels and buy them height they do
  not appear to have); the crown above the floor is wrong too (GroundOffset lifts it). And scale
  off the SKINNED renderers only - including the weapons shrank Salaadin to 2.10 against the
  others' 2.7 because two thirds of a metre of his bounds was falchion and shield.
  **GroundOffset anchors on the SOLES** - the lowest baked vertex whose dominant bone is a foot,
  toe or ankle - not the lowest vertex, which rests the cloth on the floor and leaves the man
  hovering. A re-rig moves statures 5-10%, so re-calibrate both after one.
- **A weapon mounted on a hand bone needs a POSITION as well as a rotation.** `ShieldWeaponMount`
  held `(0,0,0)` for Salaadin's shield, which puts the hand bone in the disc's own mid-plane;
  nothing animates his fingers, so the open hand ran through the face and stood 0.106 world units
  proud of it. Measure it by taking the verts weighted to `LeftHand*` and expressing them in the
  WEAPON's own frame - that gives the miss in-plane and along the normal separately. His fist
  spans 0.215 along the normal where the disc is 0.129 thick, so it can never sit inside: the
  mount slides the boss onto the fist and pushes the disc 0.120 forward, into the boss hollow.
- **A state's SPEED is the diagnostic for a bad animation window.** `Use Mixamo packs` sets
  `state.speed = clip.length / (frames/60)`, so anything far from x1 means the clip and the
  AttackData budget disagree. Well ABOVE 1 = the clip is not in `swing_trims.json` and plays whole
  against a short budget. Well BELOW 1 = an over-tight trim core: `ComputeTrims` uses
  `len = max(core_end - core_start, 6)` SOURCE frames, so a 5-frame core is clamped to 6 and then
  stretched over the whole budget. Salaadin's SP+Medium sat at **x0.27** - six frames smeared over
  44 - and widening that clip's core to 22 frames put it at x1.00. Fix the core, not the clip.
- **Choose a pack clip by MEASUREMENT.** `ArtSource/Blender/clip_measure.py` reports, per clip,
  the weapon hand's path length, vertical drop, forward/lateral travel, total hip yaw, the
  RIGHT SHOULDER's travel, the hand's highest and lowest point measured FROM THE HIPS (`hiTop` /
  `hiLow`) and `vert` = drop/side, all normalised by hips-to-head. **`drop` alone cannot tell a
  chop from a spin** - a big turn also drops a long way; `vert` plus `hipYaw` can (a plain
  overhead is vert 3.07 with 9 deg of yaw, the spin was 3.63 with 43). `hiTop` is what identifies
  a LOW slash: the pack's one low cut never lifts the hand above 0.69 where every other strike
  reaches 1.3+. It settles arguments that otherwise go in
  circles: a wandering idle is hand travel 2.54 against a settled one's 0.22; "the shoulder does
  not lead" is 0.97 against 4.50; an overhead that reads is a 4.00 drop where everything else in
  the pack is under 1.8. Run it before re-pointing a state.
- **A flat slab's facing is its THINNEST local axis** - that is how to check a shield without
  knowing the mount convention. Dot it with the fighter's forward: upright and facing out reads
  near +-1 forward and ~0 up. Salaadin's block read 0.12 forward / 0.98 up (face to the sky)
  because his mount was squared against his old hand-keyed guard, and the pack's block idle rolls
  the left hand differently. Fixed in `ShieldWeaponMount`.
- **`SetupWeapon` only ever finds the BLADE** (it matches `WeaponSpec`), so an off-hand shield
  entry in `ShieldWeaponMount` was never applied until a second pass over the model's mesh
  renderers was added. Check that pass exists before trusting any off-hand mount value.
- **The same cache exists on every CLIP importer, and that is how a rig change silently kills an
  animation.** A clip imported `CopyFromOther` stores its own copy of the rig configuration, and
  Unity builds the clip's avatar from THAT, not from the source avatar. Change the body's bone
  count and each clip goes on demanding the bones that are gone: deleting Salaadin's four toe
  bones made all five authored clips import with **no takes and no AnimationClip at all**
  (`Rig Error: Copied Avatar Rig Configuration mis-match. Transform 'mixamorig:LeftToeBase' for
  human bone 'LeftToes' not found`), so the animator states fell back to PACK clips with no error
  at the state level - he just fought in a stranger's idle. **Clearing the cache is not the fix**
  (an empty one gives `No human bone found. Ensure both rig type match`); it must be OVERWRITTEN
  with the body importer's `humanDescription`. `CrucibleMixamoSetup.ImportAll` now does this
  whenever the two disagree. **Before trusting any pose measurement, check the authored clip is
  really the one playing** - the fallback produced a 21 deg stance and 97 deg knees that had
  nothing to do with the rig.
- **`GroundOffset` in `CrucibleCharacterSetup.cs` is per character and dies with a rig change.**
  Move a fighter's ankles and he sinks: Salaadin's dropped from 12% to 3.1% of height and his feet
  ended up 0.866 world units under the floor. Recompute it live as `floor + ankleHeight - footY`,
  where `ankleHeight = ankleFraction * childScale * rootScale`. Salaadin's is `-0.600f / 3f` as of
  the metarig (2026-10-22). **Measure it as the lowest BAKED skinned vertex in play**
  (`SkinnedMeshRenderer.BakeMesh`): a SkinnedMeshRenderer's `bounds` is a loose box that does NOT
  track the pose and read his 0.64 world units wrong. Verified at idle +0.0028.
- **Salaadin's rig is the user's own, re-rigged in Blender as a RIGIFY METARIG, and it lives in
  `ArtSource/Blender/salahtest_rigged.blend`.** `ArtSource/Blender/salaadin_metarig.py` is the one
  script that turns it into a Mixamo-named rigged FBX the rest of the pipeline can eat. It is a
  good rig: symmetric, hips 51.5% of height, ankles 5.5%, a 163.5 deg knee break and a 164.9 deg
  elbow break (the old one's were collinear, giving the retargeter no hint). The script renames to
  Mixamo, bakes the armature's unapplied 0.48 scale, fills 384 unweighted verts from their nearest
  weighted neighbour, adds the `*_End` / `*4` leaves, and sets both hand bones' roll so bone-Z
  points at the floor - `salaadin_body.py`'s `hand_frame()` convention. **All 65 pack-rig bones
  exist on it by name.** `salaadin_fixrig.py`, `salaadin_rig_v2.py`, `salaadin_clips.py` and
  `Salaadin_Studio.blend` all belong to the SUPERSEDED rig; do not run them against this one.
  Animate in `Salaadin_Studio_v3.blend`. Previous rigs/bodies/clips are under
  `ArtSource/Characters/_backup_20261021/` and `_backup_20261022/`.
- **Unity's humanoid auto-mapper can wire an avatar ONE BONE OFF and report valid=True human=True.**
  The metarig's spine is SEVEN bones against the humanoid's six slots; leaving the spare in with a
  neutral name did not save it - the mapper walks the chain greedily, took the spare for **Head**
  and put the real head bone in the **RIGHT EYE** slot, with no error anywhere. `salaadin_metarig.py`
  dissolves the spare (weights to the parent, parent's tail extended). **After any rig change,
  print the slot->bone table** (`ModelImporter.humanDescription.human`), do not just check the
  avatar is valid. His reads 52 mapped, 30/30 fingers, toes mapped, only the eyes and jaw absent.
- **`matrix_world` is DERIVED, so the order of a transform bake matters.** Assigning
  `object.matrix_world` solves for `matrix_basis` against the parent inverse as it stands; clearing
  the parent inverse afterwards put the 1/0.48 straight back and the body re-imported at half the
  skeleton's size. Clear the parent inverse FIRST. `salaadin_metarig.py` asserts the body's height.
- **`humanDescription.skeleton` caches every bone's position and Unity builds the avatar from the
  CACHE, not the file.** Re-export an FBX with a joint in a new place and nothing changes at
  runtime - the Animator keeps posing the old skeleton. The tell is maddening: the same hierarchy
  measures CORRECTLY in edit mode and WRONG in play mode, because edit mode reads the file and play
  mode reads the avatar. `avatarSetup = CreateFromThisModel` does NOT refresh it; the stored arrays
  must be cleared (`hd.skeleton = new SkeletonBone[0]; hd.human = new HumanBone[0]`) before the
  reimport. `Rebuild Player<N>` in `CrucibleCharacterSetup.cs` now does that every time.
- **Rebuilding Salaadin end to end** (2026-10-22): `salaadin_metarig.py` (salahtest_rigged.blend ->
  `ArtSource/Characters/Mixamo/Salaadin_Rigged_V3.fbx`) -> `salaadin_body.py` (mounts the falchion
  and shield, writes `Salaadin_Body.fbx`) -> in Unity `Rebuild Player4`, `Use Mixamo packs`,
  `Wire special and riposte states`, `Wire combo strings`, `Salaadin > 4 Rewire`,
  `Apply the roster balance`. `salaadin_clips.py` is NOT in the chain any more - every one of his
  states is a pack clip. **Run the Unity steps through `eval`, not the menu bridge**: the menu
  returned `HTTP 400` and wrote nothing, twice now, and the only way to know is to read back.
  **His rig is NOT in git**:
  `Characters/*/Model/` and `ArtSource/Characters/` are both gitignored, so the FBXs live only on
  this machine and a fresh clone must re-run that chain. `Design/Salaadin_Rerig.md` has the numbers.
- **There is a Blender file to ANIMATE each shielded fighter in**: `ArtSource/Blender/
  {Richard,Salaadin}_Studio.blend`, built by `ArtSource/Blender/character_studio.py`
  (`blender -b --python character_studio.py -- <Name> <Body.fbx> <out.blend> <Clips/Custom>`).
  It opens on the rig in pose mode, weapons mounted, meshes `hide_select`, scene at 30 fps, and
  carries two text blocks: **EXPORT_CLIP** (Alt+P sends the current action to `Clips/Custom/`) and
  **README**. This is the hand-animation route; the `*_clips.py` scripts are code generators and
  cannot be posed. Bjorn and Miyamoto have neither - they are Tripo rigs with different bone names
  and the studio script has not been tried on them.
  - EXPORT_CLIP works on a **throwaway copy** of the rig and its action, so the lead-in never
    touches what you are animating. Two things it must get right, both found by self-test:
    the copy is born `<Name>_Rig.001` and that name goes into the FBX, which breaks the copied
    avatar - so it lends the copy the real name for the export; and **the copy must be deleted
    BEFORE the name is handed back**, or the rig you animate on ends up `<Name>_Rig.001` itself and
    the NEXT export cannot find it. There is an `assert` on that now.
  - `CrucibleMixamoSetup.ImportAll` sweeps any `<Character>_*.fbx` in `Clips/Custom/` that is in no
    map table and gives it a one-clip entry, so a hand-made clip gets the same treatment as a
    mapped one. Without it a studio export imported Generic with no avatar at all.
  - Verified end to end on both: `CopyFromOther` -> `<Name>_BodyAvatar`, Based Upon **Original**,
    `lockRootRotation`, `firstFrame 1` so the rest lead-in never plays.
- **`Design/Animation_Handoff.pdf` is the brief for a 3D animator** - the four rigs, weapon mounts,
  the scene/export spec, the animator contract (parameters, shared states, which four clips must
  loop) and **the exact frame budget for all 33 attacks**, generated from the AttackData assets and
  the controllers. The number it surfaced: **37 of 90 animator states play at 1.8x or faster**,
  worst x3.78, and Bjorn and Miyamoto hold 28 of them. `Use Mixamo packs` sets
  `state.speed = clip.length / (frames/60)`, so a clip keyed to that length plays at x1.00 - and
  a hand-edited state speed is overwritten the next time that menu item runs.
- **Unity's humanoid IK does NOTHING on these fighters, and that is why the off hand held air.**
  Measured 2026-10-21 on all four: layer IK Pass on, layer weight 1, `OnAnimatorIK` running
  (thousands of calls), `GetIKPosition` reading the goal back correctly - and a 15 cm lift asked of
  the left hand moved it **0 cm**. Richard (humanScale 1.01) fails exactly like the three 100x rigs
  (humanScale 0.51-0.58), so it is not an avatar fault. `TwoHandGrip` therefore solves the off arm
  **itself**, analytically, in `LateUpdate` (`SolveTwoBone`): elbow onto the circle both bone
  lengths allow, swung toward `leftElbowHint`, then each bone aimed in turn. Off-hand miss went
  from 0.29-0.56 world units (12-25% of body height) to **0.0000**, elbow 78-102 deg. It carries
  `[DefaultExecutionOrder(100)]` so it runs AFTER `WeaponAim` places the weapon. `strikeReachWeight`
  is 0 on both fighters, so the right-arm reach path is still on the (inert) built-in IK - port it
  the same way if it is ever turned on.
- **Rig health, measured per bone against the geometry it actually drives** (perpendicular distance
  from the bone axis to the centroid of its own weighted vertices, as % of body height; script was
  a headless Blender pass over each `<Name>_Body.fbx`):
  - **Salaadin is the worst by far**: LeftUpLeg **14.6%**, RightUpLeg 11.8%, LeftLeg 11.3%,
    RightLeg 10.9%, Spine1 7.5%. His whole leg chain runs down inside the CLOAK, 0.09-0.10 behind
    the legs (body 1.0 tall), converging only at the ankle. His foot bone splays **30.2 deg** where
    the shoe's own heel-to-toe axis is **8.1 deg**; his ankle sits at 12% of height (human ~4%);
    thigh:shin is 1.42 (human ~1.05); the toe bone ends 0.075 PAST the toe tip and at y=-0.022,
    below the floor. His torso chain leans **19.5 deg back** (Unity's zero-muscle T-pose: pelvis to
    chest -0.333 in z against Richard's -0.044) and every retargeted clip inherits that lean. His
    knees AND elbows are perfectly collinear in the T-pose - no bend hint for the retargeter, where
    Richard has a 13 deg knee break and a 22 deg elbow break.
  - Miyamoto: **his whole left forearm chain was OUTSIDE the mesh**, L_ForearmTwist01 7.1%
    off-limb. Bjorn: L_Forearm marginally out. **Both are FIXED** - see the Tripo arm bullet.
  - The `Root` bone reading "outside" on the Tripo rigs is correct - it is the motion root under
    the feet. Ignore it. So are the leaf bones (`*_End`, `*Thumb4`/`Index4`/...): a fingertip or
    toe-tip marker legitimately sits at the surface. On Richard and Salaadin they read 0.00-0.19%
    out and mean nothing.
  - **Measure containment with SIX rays and a majority vote, not one.** `rig_audit.py`'s single-ray
    parity gave false positives; the 6-ray probe (`inside.py`/`inside_mix.py` in the scratchpad)
    returns a clean 0/6 or 6/6 on a real verdict and 2-4/6 where the answer is genuinely murky.
    **And join every SKINNED mesh before probing** - probing the largest single mesh reported
    Richard's entire skeleton as 10-84% outside, because it picked one piece of him.
  - **The off-limb metric lies wherever a character wears cloth.** It measures a bone's axis
    against the centroid of the vertices it drives, so a coat skirt, a fur mantle or shoulder
    plates drag the centroid out of the limb. It is what made Salaadin's thigh read 14.6% off and
    Bjorn's mid-upper-arm ask to move 6.9% of his height. Trust it on a bare sleeve; on anything
    costumed, check containment instead.
- **Tripo lays an arm out as a straight line, so its joints end up outside the sleeve.** The
  shoulder->elbow->wrist chain is interpolated, and on a bent or asymmetric arm the elbow leaves
  the mesh: Miyamoto's left elbow sat **2.67% of body height clear of the surface**, driving his
  whole forearm from a pivot in open air. All four arms' bones sat ~2-3% of height forward of their
  own flesh in x. `ArtSource/Blender/fix_tripo_arms.py` re-centres each forearm joint on the
  cross-section of the flesh it drives, in EDIT mode so Tripo's weights survive, restoring roll
  afterwards. Result: **every arm bone on both fighters is now inside the mesh**, 1.2-3.8% deep,
  and the upper:forearm ratio is 0.86-0.99 (no snake arm). Run it, then `Rebuild Player1/2`.
  - **It deliberately does NOT touch the upper arm** - those vertex groups carry Bjorn's fur and
    Miyamoto's shoulder plates, so the centroid there is in the costume, not the arm.
  - Re-exporting a body FBX must use the project's options (`object_types={'ARMATURE','MESH'}`,
    `armature_nodetype='NULL'`, `apply_unit_scale`, `FBX_SCALE_NONE`, `-Z`/`Y`) or Unity reimports
    the body lying down at a hundredth of its size. Overwrite the FBX **in place** so the .meta,
    the GUID and every scene reference survive; the originals are in
    `ArtSource/Characters/_backup_20261021/tripo_bodies/`.
- **Richard's hips are ~10 points of height too low and forward of his ankles - NOT yet fixed.**
  They sit at **38.8% of his rig's span where Salaadin's corrected hips sit at 48.8%** (human ~52%),
  and 1.54% (left) / 2.31% (right) of height forward of the ankle - the same geometry that gave
  Salaadin the splits. Left alone on purpose: the 6-ray probe is ambiguous there (2-3/6, not the
  clean 0/6 that justified Miyamoto's elbow), his clips are play-verified, and moving a hip 9% of
  body height re-times every retargeted clip. Doing it needs the same stance-verification pass
  Salaadin got.
- **Bjorn and Miyamoto came off Tripo with NO finger bones** - 41 bones stopping at
  `L_Hand`/`R_Hand`, one rigid mitt sculpted half open, which is why the off hand could sit exactly
  on the haft and still read as holding air. **Both LEFT hands now have a full five-finger chain**
  (56 bones each) built by `ArtSource/Blender/add_fingers.py`. Their right hands are still mitts;
  Bjorn's right-hand mesh never separates into fingers, so it cannot be traced the same way.
  - The fingers ARE modelled, they just **fuse into the palm below the last knuckle** - they only
    separate into four components at t>=0.74 (Bjorn) / 0.92 (Miyamoto) of the hand bone. So the
    tips are measurable and the knuckles are not; the script finds the tip clusters, gives each
    finger the lateral band it owns, and walks that band's centroid in from the tip.
  - **Track each finger from its TIP inward, never by slicing the hand.** These are curled claws:
    a slice at one station holds the finger AND the palm under it, and their joint centroid lands
    in the gap between them. That first attempt put whole chains outside the mesh.
  - Tracing gets the shape right but cannot guarantee containment, so every joint is then **pushed
    to a verified interior point** (`pull_inside`), and a degenerate trace falls back to a straight
    knuckle->tip run. All 30 joints across both hands test **6/6 inside**.
  - Weights are **redistributed from `*_Hand`, never rebuilt**: each vertex keeps the total weight
    it had, split between its two nearest bones, so the hand deforms as before until a finger bends.
  - Name them `L_Index1..3`, `L_Middle`, `L_Ring`, `L_Pinky`, `L_Thumb` and Unity's auto-mapper
    fills all **15/15** humanoid finger slots on its own - no explicit `hd.human` needed.
- **Nothing in the game animates fingers** (every clip predates those bones), so `TwoHandGrip`
  closes them itself each frame, and this is where the real bug was hiding:
  - **The off hand was being placed BESIDE the haft, not around it.** `offHandMiss` read 0.0000 the
    whole time because the wrist was exactly on its goal - but the knuckles sat 0.09-0.26 world
    units off the haft axis with fingers only 0.06-0.11 long, so no amount of closing could ever
    have reached it. `PalmOffset()` now measures where the knuckle line actually landed and
    re-solves the arm onto the haft; knuckles came in to 0.01-0.19.
  - **A finger curls about the plane that faces the haft, not about the haft's own direction.**
    Turning it about the haft axis only sweeps it round a cone, and these fingers lie partly ALONG
    the haft, so that barely closed them (gaps 0.20 -> 0.08). The bend axis is
    `cross(fingerDir, toHaftPerp)`.
  - The search runs on a **copy of the joint positions** (`Simulate`), so the sweep costs vector
    maths instead of a hierarchy update per step; the bones are written once with the answer.
  - Verified in play: `offHandMiss` 0.0000 and finger gap 0.039-0.071 at rest, **0.047 mid-swing**.
- **The physics capsule must have its bottom ON THE SOLES, or the fighter floats and climbs.**
  It was a fixed 2.1 high centred at 0.8-1.0, which hangs 0.20 (Bjorn, root scale 4) to 0.75
  (everyone else, scale 3) world units BELOW the feet. Gravity rests that overhang on the arena and
  leaves the man hovering by exactly that much, and an overhang that deep catches on every step and
  ledge and walks him up it. `Rebuild Player<N>` now fits it from `Stature` (sole-to-crown, the
  number `StandingHeight` is calibrated to produce): `height = stature / rootScale`,
  `center.y = height / 2`. **`GroundOffset` and the capsule are ONE system** - the old offsets were
  negative because they pushed the MODEL down from a root floating on that overhang; measure them
  against the root only once the capsule bottom IS the root origin. **Verify against the arena, not
  y = 0**: the floor under the marks runs -0.065 to -0.267.
- **A fighter's AUTHORED root height is not where he should stand, and two places used it.** The
  capsule fit put every capsule's bottom on the soles, which makes the root land on the floor - but
  `GameMenu.ResolveSpawns` (match marks) and `PlaceStaged` (character select) still took y from
  each root's authored scene position (0.24 Bjorn, 0.75 the rest), measured when the capsule hung
  up to 0.75 BELOW the feet and the root floated by that much. Both now call `GroundedY(at,
  fallback)`, which raycasts down at the mark, ignores triggers and fighters, and takes the highest
  hit; the authored y is only the fallback. **A fall can hide this**: `FreezeFighters` only disables
  the `Fighter` component, so gravity still runs and a fighter spawned 0.8 in the air lands during
  READY-3-2-1 - measure AT THE INSTANT OF PLACEMENT, not after settling. A raycast is also the only
  version that survives an arena swap, since the floor under the marks is neither flat nor y=0.
- **Measure a sole as the lowest FOOT-WEIGHTED baked vertex, never the lowest vertex.** Richard's
  cape hangs 0.13 below his heels and Salaadin's coat does the same, so the plain minimum reports a
  man sunk into the arena while his feet are on it.
- **`ShieldWeaponMount` is now the mount table for ALL FOUR fighters, not just the shielded pair.**
  Its application used to sit inside `if (!twoHanded)`, so a Bjorn or Miyamoto entry was silently
  never applied - Miyamoto's katana had an entry written and still pointed backward through his
  chest. The gate was there because those two were TRIPO rigs mounted via `WeaponMount` +
  `HandGripPoint` (Tripo hand-bone units, ~220x scale); the 2026-10-22 re-rig made all four Mixamo
  metarig exports, so `WeaponMount` is dead and one table serves everyone.
- **A re-rig invalidates every weapon mount, because the hand bone's orientation changes.** Two
  separate places get this wrong:
  - `fit_metarig.py` re-parents the weapons to the new hand bones. Computing a
    `matrix_parent_inverse` from the new bone's frame RE-SEATS the weapon on that bone's axes and
    throws away the placement the FBX authored. Capture `matrix_world`, assign the parent, restore
    `matrix_world`, and assert it did not move.
  - Even then the bind pose is only half of it: the retarget drives the hand to a different world
    orientation than the old rig did, so the mount has to be re-solved in the POSE the fighter
    stands in. Solve it in play against a stated target and read the local euler back; do not guess
    eulers.
- **`offHandMiss` near zero proves the wrist is on its goal, NOT that the grip is right.** It read
  ~1e-7 on Miyamoto the whole time his blade was behind him - the hand was faithfully following a
  socket that was in the wrong place. The grip's real tells are the blade's direction against the
  fighter's own forward, and where the off hand lands laterally relative to the chest.
- **A flat slab's facing is its thinnest local axis, and that is how to catch a shield worn
  backwards.** Richard's read -0.82 against his own forward (face toward himself) with his fist
  0.240 PROUD of it, and he had no mount entry at all. The fix is the one Salaadin got: aim the
  facing along the fighter's forward with the long axis up, slide the boss onto the fist IN-PLANE,
  then seat the disc so the fist sits ~0.02 inside the front face.
- **A move that carries its weapon AROUND the body needs a lunge override, not a smaller hitbox.**
  Bjorn's two spins scored at a hitbox range of 3.2-3.5 while the default lunge stops at
  `BalanceConfig.ATTACK_LUNGE_STOP` 1.7, so the axe was nowhere near the man when the damage landed.
  `LungeOverride` in `CrucibleBalance.cs` carries per-move `(stop, max)`; the spins take
  `(0.9, 2.6)`, which is inside the sum of the two bodies, so the spin travels THROUGH and the head
  sweeps the torso. Verified: Medium closes 3.00 -> 1.63 with the axe 0.344 from the spine.
- **A pose bone's TRANSLATION is in bone-local units that scale with the ARMATURE OBJECT's scale,
  and that is the deepest of the clip-porting traps.** The pack rigs are scaled 0.01 and the
  re-rigged bodies are 1.0, so hip travel copied by `apply_frame` arrives a HUNDRED times too big.
  The symptom is not subtle and it is not obviously about hips: the fighter retargets about two
  thirds his own size with his feet a metre under the arena - what the user calls "melting into the
  floor". **The tell is `RootT.y` on the imported clip**: healthy is 0.8-1.0, Richard's four read
  -23.4, -50.0, -22.8 and -8.5. Convert by BOTH factors - `src_armature_scale / tgt_armature_scale`
  first, then the rest hip-height ratio - which is what `richard_clips.py` does now. The hip-height
  ratio alone (the Salaadin lesson) only moved -25.4 to -23.4.
  - **Do NOT reach for the importer's root-height setting to rescue a sunk clip.** "Root Transform
    Position Y -> Based Upon: FEET" is the obvious move, it does not work, and it would hide the
    real fault on every clip after it. Authored clips stay on **Original**, like the pack clips.
  - **`CrucibleMixamoSetup` reimports only when its `wanted` clip settings differ from the ones on
    disk, so every setting it writes must be in that comparison.** `keepOriginalPositionY` and
    `lockRootHeightY` were not, so changing them wrote nothing and the console said nothing - the
    same shape of failure as the menu bridge's silent `HTTP 400`. Read the importer back.
- **A flat slab's facing must be measured SIGNED, and paint cannot be measured at all.** Richard's
  shield passed an `Mathf.Abs(dot)` check while worn backwards; the signed dot was -0.82 and the
  photograph showed the enemy his arm straps. When a face is a face only because of what is painted
  on it, take the picture.
- **`Rebuild Player<N>`, the mounts and `StandingHeight` all write to the SCENE, which the CLI does
  not save for you.** The scene showed clean in `git status` with the whole size change and both
  weapon mounts live only in the loaded editor scene. Save it (`EditorSceneManager.SaveScene`)
  before committing, and check `git status -- Assets/_project/Scenes/`.
- **`GroundOffset` is a fixed root-local lift, so it dies with a SIZE change as surely as a re-rig.**
  Shrinking Richard 11% left him hovering 0.027; his is `0.119f / 3f` as of 2026-10-22.
  Statures in play after the change: Richard 2.887, Salaadin 2.795, Bjorn 2.746, Miyamoto 2.570.

- **`swing_contacts.json` is keyed by STATE, not by clip, and a contact entry OVERRIDES the
  measured core.** Re-point a state at a different clip and the old clip's contact frame silently
  re-cuts the new one: Miyamoto's heavy kept `contact 36.2, speed 2.0` from the spin it used to
  play, which against `great sword casting` (swing peak at source frame 66) chose the window
  18-50 - it missed the cut entirely and pinned playback at 2.0x. Removing the stale entry and
  widening the core to budget/2 (16 source frames, 57-73) put it at **x1.00** with the peak on the
  hit frame. **Whenever a state's clip changes, delete or re-measure its contact entry.** Without a
  contact the window is `len = max(core_end - core_start, 6)` SOURCE frames, so
  `speed = 2 x core / total` - core = total/2 is the recipe for x1.00.
- **A clip authored from pack frames can put a foot through the floor, and the fix belongs in the
  authoring, not the importer.** `plant_feet()` in `richard_clips.py` runs inside `key()`: it
  measures the lower ankle and lifts the HIPS when it is below `REST_ANKLE_Z * 0.55`. The legs are
  plain FK children there (`reach()` solves the arms by setting bones, not constraints), so lifting
  the hips lifts both feet by exactly the lift and one pass is exact. It only ever LIFTS, so a
  clean frame is untouched. Measured with it off: thrust 0.055 and charge 0.018 (both above the
  floor, both play-verified good), bash **-0.187** and double-high **-0.183** (both underground).
  Set the threshold from that spread, not by feel, and keep the cap above the worst violation or it
  binds and silently under-corrects.
- **What is left after that is the TRANSITION, and it is not the clip.** Blending a standing idle
  into a crouched move interpolates the root height faster than the legs reposition, so a foot dips
  for a frame or two: Richard's light 1 frame at -0.016 and his shield bash 4 frames at -0.096,
  while the clips' own frames sit +0.18 to +0.31 clear. Judge a clip by its own frames before
  blaming the authoring.

- **`clip_measure.py` WRAPS hip yaw; `clip_curves.py` unwraps it. Only the second can tell a cut
  from a spin.** `attack (3)` reads as 36 deg in one and **396** in the other - it is a full turn
  plus 36. Every clip chosen off `clip_measure`'s yaw column is suspect; re-check with
  `clip_curves.py` (which also reports the swing core, the contact candidate and a silhouette
  guess) before wiring a state. The SwordShieldPack is mostly spins: attack (2) 429, slash (2) 507,
  slash (4) 391, attack (3) 396.
- **A clip can measure as a strike and not be one.** "power up" and "casting" are the only two
  SwordShieldPack clips no fighter uses, both classify as overheads with a real tip drop, and
  photographed in play they are spell-cast gestures - the sword held up across the chest, no swing.
  Measurement narrows the field; the photograph decides.
- **`AimMode.Level` (AttackData + WeaponAim) holds a blade horizontal AND pointed at the opponent.**
  For a spin, `Full` aims at the opponent's CHEST, which from a raised hand points the tip DOWN
  (Kuruma measured -41 to -7 deg, which is what reads as a back fist), and `YawOnly` leaves the
  clip's own wheel alone (-36 to +39). Level flattens the desired direction to the horizon first:
  0.2-3.2 deg through the hit window. **The enum is appended, never reordered** - its values
  serialize by index on every AttackData asset. The aim only holds while WeaponAim's weight is up
  (startup + active, then `fadeOut`), so the follow-through is still the clip's.
- **`Apply the roster balance` writes AttackData in memory and does NOT save it to disk**, so a
  frame-budget change survives only until the next domain reload - and `Use Mixamo packs`, run in
  the same breath, fits windows against the NEW numbers while the assets still hold the old ones.
  Richard's charge kept its 47-frame budget through a whole verification pass and the window cut
  the first nine frames off the run. Call `AssetDatabase.SaveAssets()` after Apply and read the
  `.asset` back before trusting a state speed.
- **A frozen borrowed stance IS the clip's whole footing, so check the frame you borrow.** The
  authored thrust re-applies one pose at every key; it took the pack's `block idle`, a FENCING
  stance whose ankles sit at -0.535 along the hip joints' own axis - one boot on the wrong side of
  the other, which is "his legs cross". `attack (4)` frame 15 is a standing cut with both boots
  planted: +0.531. Two near misses worth knowing: his own idle stands him upright, which moves the
  shoulder back and puts the thrust target 0.189 beyond his reach, and overlaying the guard's torso
  on the idle's hips doubles the lean and bends him double.
- **Measure left-versus-right against the HIP JOINTS' own axis** (`LeftUpperLeg - RightUpperLeg`),
  never `transform.right`. A fighter's root, his pelvis and the Blender import each have their own
  idea of "right", and three rounds of contradictory readings here all came from that.

- **A pack clip has UNCROSSED and CROSSED stretches, and a spliced move must take its frames from
  the uncrossed ones.** Measure per frame as the ankle separation along the hip joints' own axis
  and the windows fall straight out: `sword and shield slash` is crossed 0-16, clean **17-32**,
  crossed 33+; `sword and shield attack` is clean **0-58**, crossed 59+. Richard's Twin Hew opened
  on nine crossed frames purely because the splice began at the overhead's wind-up (frame 10), and
  no amount of blend tuning could have fixed that - the legs were the CLIP's. The same clip reads
  clean as his Heavy because that state's window sits inside 17-32.
- **Do NOT try to fix a crossing by easing into the clip.** A four-frame lead-in interpolating out
  of the standing stance made it WORSE (11 crossed frames against 8): interpolating two UNCROSSED
  leg poses can swing a thigh straight through the other leg, so the lead-in crossed on its own
  account while the engine's blend into it still crossed. Move the window instead.
- **`CrucibleSpecialStates.BlendIn` is 0.05 s, and the number only started mattering once it was
  WRITTEN every run.** It used to be set inside `if (!reachable)`, i.e. only when the Any State
  transition was first created, so editing the constant changed nothing on a controller that
  already had its transitions - and the console still logged the new figure. The shorter blend is
  what stopped a special's first frames dipping the boots 0.16 under the arena, and a long
  cross-fade between two distant poses is also exactly what reads as **melty**.
- **`Wire special and riposte states` did not include Salaadin at all** (the roster was P1/P2/P3),
  so every shared change made there stopped at three fighters. He is in it now; check the roster
  before assuming a shared editor pass covered everyone.
- **A drained fighter's `TryAttack` returns FALSE and a sampler then measures the idle.** Six
  landed specials empty the bar, after which every later move in the sweep reports the idle clip's
  numbers as if they were the move's - which is how Richard's Twin Hew came back as
  "Richard_Idle, 5 crossed frames" twice. Set `Stats.currentStamina = maxStamina` before each
  swing and BAIL on a false return rather than sampling. (`Stats.currentHealth` too - a K.O. ends
  the pass just as silently.)
- **`GetCurrentAnimatorClipInfo(0)[0].clip.name` is not a reliable read of what is playing**; it
  can keep naming the idle through a whole attack. Take the entry with the highest `weight`, or
  read the state name - and trust the measurements (a 2.97 hip rise is a leap, not an idle).
- **`BakeMesh` output carries no bone weights.** Take `boneWeights` from `sharedMesh` and the
  positions from the baked mesh; guarding on the baked mesh's own weights skips every renderer and
  the result is a silent "no feet found" rather than an error.

- **`WeaponAim` CACHES the weapon's rest local position at Start and writes it back EVERY frame**
  (`restLocalPosition`, restored in its update). So setting `weapon.transform.localPosition` in
  play does nothing visible - the component stamps the old value back before the frame is drawn,
  and five photographs in a row come out identical while the numbers say the transform moved. To
  try a mount offset live, set the private `restLocalPosition` as well. From the SCENE it needs no
  code: Start reads whatever the scene holds. Richard's shield has no `WeaponAim`, which is why it
  moved while the sword would not - if one weapon responds and the other does not, this is why.
- **A weapon mounted so its axis passes through the HAND BONE is held at the WRIST**, because that
  bone is the wrist joint. Richard's sword had been fixed to put the blade's axis through the hand
  bone, and the result was a crossguard lying across the back of his hand with the grip running up
  his forearm and his fingers hanging clear of the weapon. The fix is to slide the weapon along the
  **wrist -> fingertip** vector. Not the whole way: at 1.0 the grip ends up past the fingertips and
  the sword photographs as floating beside him; **0.55 of it** puts the haft through the hand with
  the fingers in front. Nearest weapon vertex to the fingertip centre is the number to watch -
  sword 0.089 -> 0.045, shield 0.083 -> 0.017.
  - Sliding along the weapon's OWN long axis barely helps (0.060 of 0.245 on the sword): the
    fingers wrap a haft, so they sit mostly PERPENDICULAR to it.
  - A pure translation cannot change a facing, so the hard-won shield rotations survive it intact.
- **`sharedMesh.isReadable` is FALSE on these weapon meshes, and `mesh.vertices` then returns an
  EMPTY array at runtime rather than throwing.** Any min-distance loop over it silently keeps its
  sentinel - a column of 9999 in a CSV is this, not a weapon that moved to infinity.

- **ROOT MOTION IS DISCARDED on these clips, so a clip's own travel never reaches the game.** The
  AttackData `lunge` moves the fighter; the clip's forward displacement does not (the same reason
  Bjorn's Blood Eagle throws away 3.64 m). So `clip_curves`'s `travel` column, and its "advancing"
  tag, do NOT mean a move will run - screening on them threw away every usable strike in the
  SwordShieldPack and left only buff gestures. What IS reproduced is the hips RISING, because that
  is body animation: **the test for "no jumping" is `maxHipsAboveRoot` in play**, not the clip's
  travel. Richard's spins measure 1.43-1.57 against an idle's ~1.6; the splice that leapt read 2.97.
- **"casting" and "power up" are BUFF gestures and cannot be used as strikes.** Trimmed to its
  measured descent, `sword and shield casting` photographs as Richard going down on one knee with
  the shield planted on the floor. The measurement said "overhead, 2.22 of drop" because that is
  the sword lowering as he kneels. A silhouette guess from curves cannot tell a chop from a kneel -
  photograph the TRIMMED window in play before casting any clip into a slot.
- **A trim is keyed by CLIP and shared; a contact is keyed by STATE and is not.** To re-time one
  fighter's state onto a clip another fighter also uses, add a contact entry - with one,
  `len = max(speed * total * 0.5, 6)` and the shared core is ignored entirely. Widening
  `sword and shield attack (3)`'s core for Richard's SP+Heavy would have re-timed Salaadin's
  MEDIUM, which is the same shared-trim trap that once dropped his Hattin Cleave to x0.43.
- **A full-turn special crosses its legs and that is the move, not a fault.** Measured on two spins
  that are no longer wired: 11 and 34 crossed frames at pelvis sweeps of 263 and 279 degrees. Leg
  crossing is only a defect in a move that holds ONE stance (the thrust) - judge it against the
  pelvis sweep.
- **Richard's three specials play the SAME CLIPS as his three normals, and the CAMERA is what tells
  them apart.** The user's call, after the SwordShieldPack ran out of standing strikes that were
  neither spins nor buff gestures. SP+Light plays `Richard_Thrust`, SP+Medium `slash (5)`,
  SP+Heavy `slash` - each its own normal's clip, at the same speed (x1.23/x1.60/x1.40 against the
  normals' x1.21/x1.60/x1.40), because each special carries a per-STATE contact copied from the
  normal that owns the clip. Three things this depends on:
  - **A trim is keyed by CLIP, so a special sharing its normal's clip shares its trim.** The window
    and speed have to come from `swing_contacts.json`, which is keyed by STATE. Same mechanism that
    keeps Richard's SP+Heavy off Salaadin's Medium.
  - **A special that plays a normal's animation must take that normal's HIT GEOMETRY too** -
    `height`, `comesFrom`, `hitSide`, `aim`, `size`, `offset`. A box that does not match the swing
    on screen is what reads as unfair. Only `range`, `lunge`, damage and stamina stay special.
  - **`FightCamera.OnImpact` already shook for Heavy and Special and treated them identically**, so
    there was nothing on screen to separate them. `specialShakeMultiplier` (2.4) and
    `specialShakeDuration` (0.32 s) now split them. Measured in play: Heavy 0.108 over 0.18 s,
    SP+Heavy **0.374 over 0.32 s** - 3.5x, because the multiplier compounds with the damage the
    amplitude already scales by (13 against 9). **Light and Medium shake not at all** (0.000, by
    design - only Heavy and Special move the camera), so their specials go from no shake to a full
    one, which is the clearest tell of the three.
  - `Shake()` normalises its decay by the serialized `shakeDuration`, not by the seconds passed in,
    so a longer duration HOLDS full amplitude and then falls off. That is the right shape for a
    special landing; it is not a bug.
- **A new animation pack does NOT need Mixamo bone names.** `PackDir` is just
  `Clips/Mixamo/<pack>`, and pack clips import `CreateFromThisModel`, which builds an avatar from
  whatever skeleton the FBX carries. Any FBX that maps as Humanoid retargets onto these fighters;
  the Mixamo naming is a convention inherited from where the current packs came from, not a
  requirement. The only real risk with a bought pack is the auto-mapper failing on its naming, and
  that is one look at the Rig tab.

## Measuring a move from the CLI (the harness that works)

- **`Time.captureDeltaTime = 1f/60f` makes one rendered frame equal one 60 fps game frame**, so a
  sample index IS an attack frame number whatever the Editor's real frame rate is. This is what
  makes frame budgets readable off a series, and it beats the old `timeScale 0.02` plus round-trip
  guessing outright.
- **Sample inside a coroutine and write a CSV**, rather than one eval per frame: a local iterator
  function in `eval` plus `menu.StartCoroutine(...)` runs across consecutive frames (verified:
  frames 66, 67, 68) and `System.IO.File.WriteAllText` at the end keeps it out of the console,
  which otherwise blows the token budget on one `console get`.
- **Disable `DebugTimeScale` BEFORE setting the scale.** It re-applies the Settings game speed the
  moment the scale returns to ~1, so `Time.timeScale = 1f` alone leaves it at 0.7 and every sample
  is 0.7 of an attack frame.
- **A lunge only closes to `ATTACK_LUNGE_STOP` (1.7), so re-space the pair before EVERY move.**
  Once one swing has closed the gap, every later move measures zero travel and looks like a move
  that cannot move - Richard's shield charge read `travel 0.00` until the pair were re-spaced to
  4.2, when it charged **1.77**.
- **Identical consecutive frames in a series are HIT-STOP, not a frozen animation.** Miyamoto's
  heavy holds 5 frames at the strike because it connected; that is the game working. The pause the
  user reported in the shield charge was 22 frames with the LEGS alone frozen - a different shape.
- **A KO silently ends the pass.** Six landed attacks is enough to kill: Bjorn's verification run
  left Miyamoto at 0/80, and every move in Miyamoto's own pass then reported "NOT FIRED". Re-run
  `StartMatch` and check `Stats.currentHealth` before trusting an empty result.
- Spin or slash is settled by the **pelvis sweep, unwrapped through its wraps**: Salaadin's light
  was 429 deg as a spin clip and is 16 deg now; Miyamoto's heavy is 16 deg, his Kuruma 100 deg.

- Known gaps carried forward: the AI cannot parry, throw a special or dial a string on purpose;
  the web build is uncompressed on purpose; hosting means the laptop is the server (relay +
  quick tunnel, new URL each restart); `parryCounterDamageMultiplier` is serialized 1.5 against `BalanceConfig`'s
  1.35 and `parryCounterWindow` 1.0 s against 0.5 s, never settled; `PlayProbe`, `BladeTrace`
  and `InputHold` are debug-only components still in the scene (`DebugTimeScale` is wanted —
  the Settings speed slider drives it).

## Testing the game from the CLI (hard-won, still true)

- **Set `Time.timeScale` directly, AFTER `GameMenu.StartMatch`.** `DebugTimeScale` only applies
  itself when the scale is ~1, and `GameMenu.ApplySettings` writes the Settings game speed
  (0.85) at match start, so the component is inert for the rest of the match and anything set
  on it beforehand is thrown away twice over.
- Play mode starts on the MAIN MENU, where every `Fighter` is disabled and
  `InputManager.InputEnabled` is false, so input-driven tests do nothing until
  `StartMatch` (public) runs. A direct `CombatSystem.TryAttack` still works there.
- The Editor runs ~4–17 fps under the CLI. A sub-second window (parry, lockout, a counter
  window) cannot be observed across a round trip: force the private timer by reflection and ask
  the gate in the SAME eval. `timeScale` 0.3 is right for dodge tests (0.08 lets one rendered
  frame advance ~6 game frames and whole startups pass unseen).
- Never recompile while the Editor is in play mode — the domain reload wipes
  `InputManager.Instance` while play continues, and every input override silently does nothing.
- `unity command recompile` and wait for `completed` before invoking an editor method you just
  edited, or the stale assembly runs. `Assets/_project/Editor/*.cs` are in the `Crucible.Editor`
  asmdef, which the Roslyn check's `.rsp` files do not cover.
- Place test fighters through `Rigidbody.position` + `transform.position` +
  `Physics.SyncTransforms()`, keep each fighter's OWN y (averaging them is what left both
  floating in a saved scene), and wait a couple of frames before `TryAttack`.
- `eval_file` reports a bad member reference as "Unreachable code detected"; it rejects a
  top-level `using` and trips on ambiguous `Object`. An eval that throws returns no result line
  at all — re-run it without the `grep` to see the exception.
