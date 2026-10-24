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
  stamina / 8 armour / x0.92 / walk 4.8 (raised from 4.4 on 2026-10-23, Plan_Balance 2.2),
  Bjorn 95 / 110 / 14 / x1.10 / 3.8 (costliest moves),
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
- **Four fighters now.** Salaadin (falchion + round shield, the combo fighter: 95 hp, 115 stamina,
  18 armour, x0.85 damage - the roster's lowest, cut from x0.92 on 2026-10-23 - walk 4.1) lives on `Player4` with `P4_Salaadin.controller` and
  `salaadin_data`. His move set is the table in `Editor/CrucibleSalaadin.cs`; build him with
  `Crucible > Characters > Salaadin > 1/2/3`, then `Rebuild Player4 (Salaadin body)`, then the
  shared menu items, then `Salaadin > 4 Rewire Player4 to the move set` (the scene holds his
  AttackData by GUID, so a renamed slot needs re-pointing without rebuilding the fighter).
  **He strikes with the falchion and nothing else** - no thrusts, no shield bashes, no kicks, and
  his parry riposte is a saber counter where the other three answer with a boot. His two
  showpieces are the NORMALS (Medium = Shin Cut, Heavy = Falling Saif) and the two compact pack
  swings sit on the specials (SP+Medium = Crescent Slash, SP+Heavy = Hattin Cleave); the frame
  data and costs stayed with the SLOT.
  **His Medium and Heavy were FULL-TURN SPINS until 2026-10-23** - `attack (3)` and `slash (4)`,
  which `clip_measure` reports as 36 and 9 degrees of hip yaw and `clip_curves` UNWRAPS to 395.7
  and 391.4. They are plain cuts now (`slash (5)`, the pack's low slash, and `slash`, its plain
  overhead), measured in play at **12 and 132 degrees of pelvis sweep against the old ~390**.
  His whole LIGHT and SPECIAL set was re-pointed later the same day, on the user's picks:
  Light = AxePack `combo attack ver. 2` (a flat sweep; its tip arc is only 0.29 and that was
  flagged before the choice), SP+Light = `sword and shield attack` (the committed overhead, tip
  arc 1.45) and then, on a second pass the same day, the BARBARIAN REVERSE SLASH (AxePack
  `standing melee attack backhand`) - both replacing the tip-never-rises `slash (2)`;
  SP+Heavy = `slash (4)`, **the spinning jumping slash the user asked for by name** - the clip his
  Heavy gave up that morning. Verified in play: Light 118 deg pelvis / hips 1.39,
  SP+Light **351 / 1.52** (hand 1.42 above the hips down to -0.17: a full sweep, feet planted),
  SP+Heavy **443 / 2.09**. **His SP+Heavy is now his only leaping move** - the overhead that
  briefly held SP+Light hopped to 2.80, and the reverse slash does not leave the ground.
  **Three states share `standing melee attack backhand`** - Bjorn's SP+Light (2.0), Richard's
  SP+Light (1.3) and Salaadin's (2.0) - each on its own per-STATE contact. It is the clearest
  proof that clip sharing costs nothing but the look.
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

- **The off-hand `LeftGrip` socket belongs on the haft AXIS - local (0, 0, z) - not on the mesh
  bounds' CENTRE.** A weapon mounts at its hand bone's origin, so the weapon's own origin IS the
  right fist and local (0,0,z) runs straight down the haft through it. The bounds centre is the
  middle of a box drawn round the whole weapon, and on anything whose mass is not centred on the
  haft it sits well off it: **0.357 world units on Miyamoto's katana** (long curved blade) and
  **0.148 on Bjorn's axe** (head on one end). `SetupWeapon` took the centre, so Miyamoto's off hand
  was solved a third of a metre off the hilt, up at his chest - it read as him not holding the
  sword at all, and `TwoHandGrip` reported `OffHandMiss` 1.6e-07 the whole time because the wrist
  was faithfully on a socket that was in the wrong place. Bjorn had the same fault at half the
  size, and it is most likely what `PalmOffset()` was quietly compensating for.
  **The tell is a photograph, not the miss.** Both fighters' fists sit stacked on the haft now.
- **Judge a fist against a shield by the WORST vertex, not the centroid.** Richard's left-hand
  centroid measured 0.065 BEHIND the shield's front face while his worst knuckle stood **0.090
  PROUD of it** - a fist through the paint. Pushing the disc 0.125 further along its own normal
  (`ShieldWeaponMount`) put the worst vertex 0.035 behind the face. Measure as
  `dot(vertex - discCentre, normal) - halfThickness` over every vertex weighted to `LeftHand*`.
- **Miyamoto and Bjorn have NO move table**, so unlike Richard (`CrucibleRichard`) and Salaadin
  (`CrucibleSalaadin`) their `aimMode`, `comesFrom`, `hitSide` and box geometry live ONLY in the
  `.asset`. `Apply the roster balance` writes just damage/stamina/pierce/frames/hitstun, so an aim
  change on those two is an asset edit and nothing regenerates it. `Miyamoto_Special_NitoCross`
  (SP+Medium) was `AimMode.Off`, which leaves the clip's own wheel alone and reads as a spinning
  back FIST; `Level` gives the same cure it gave Kuruma - measured through the move, the blade
  swings from **84 deg elevation down to 0.0 deg** across the hit window (frames 20-32) and holds
  flat there, on a 356 deg pelvis sweep.
- **Miyamoto's ARMOUR PIERCE was 0 on every normal** - the katana fighter pierced LEAST in the
  roster, which is backwards. Pierce is a PERCENTAGE off the defender's armour (DamageCalculator
  step 2). He is the leader on every slot now (8/10/16 normals, 18-24 specials, 8 ripostes) against
  Richard's best of 12.
- **A transform edited in PLAY mode is discarded when play stops.** Mounts and sockets solved in
  play have to be re-applied in EDIT mode and the scene saved
  (`EditorSceneManager.SaveScene`) - solving them live and walking away loses the lot.

- **`AimMode` only decides where the TIP POINTS. Nothing in the game turned a blade about its own
  spine until now**, which is why a spin could be aimed perfectly and still slap the target with the
  FLAT of the sword - the thing that reads as a back fist rather than a slash. `AttackData.
  aimRollDegrees` + `WeaponAim.ApplyRoll` roll the weapon about its own long axis while the aim
  weight is up. It is applied for EVERY mode including `Off` (edge orientation is independent of
  pointing), and rolling about the blade axis cannot move the tip, so it never fights the aim.
  Miyamoto's Nito Cross is **+100**.
- **Find a blade's cutting edge from the MESH: the edge tapers thin, the spine stays thick.**
  Sample the verts in the middle of the blade's length, split them by the width axis, and compare
  mean half-thickness along the flat normal. Miyamoto's katana: local z is the blade, y the width,
  x the flat normal, and the **edge is +y** (0.000031 against the spine's 0.000046). Do NOT try to
  read it off the bounds box alone - a katana's local bounds are 0.0009 x 0.0038 x 0.0057, so the
  blade axis is only 1.5x the width and the box looks nothing like a sword.
- **Measure the roll a cut needs with the opponent OUT OF REACH.** It is the signed angle about the
  blade axis from the edge direction to the tip's VELOCITY, both projected perpendicular to the
  blade. Measured in range it is worthless - hit-stop freezes the velocity and the lunge adds the
  body's travel, and the answer swings between -88 and +88 from frame to frame. Spaced to 9 units
  the same move reads monotonic: +84.5 at the first active frame through +101 at contact to +125.9
  at the last. **A single roll cannot hold the edge leading through a full-turn spin** (the swing
  direction rotates under it); take the value at the CONTACT frame and accept the ends.
- **A contact's pinned `speed` in `swing_contacts.json` IS the final state speed, so cutting a frame
  budget shortens the MOVE without touching the animation's playback rate.** Five of Miyamoto's nine
  states are pinned (1.2-2.4x); cutting every budget ~18% left those rates alone and simply showed a
  narrower window around the contact - which is what "faster" should mean. The two states with a
  bare contact number and the one with none take `2 x core / total` instead, so they DID speed up
  (Light and Medium both to 1.80). Decide which of the two you want before changing a budget.
- **A paused Game view does not re-render, so a camera moved after the pause photographs the OLD
  frame.** Place the camera inside the coroutine, BEFORE `isPaused = true`. And a probe camera needs
  `depth` ABOVE the fight camera's or the capture just shows the match view - `-10` renders behind
  it. Two different poses coming out byte-identical is this, not a change that failed.
- **The `console` tool's `grep` did not filter** in this session - it returned the whole tail both
  times, at ~20k tokens a call. Write diagnostics to a file in the scratchpad and `cat` it.

- **A spin is a fault in a NORMAL and a flourish in a SPECIAL**, and that is how to spend the
  SwordShieldPack: it holds only five non-spin strikes (`slash (5)` 40.6, `attack (4)` 63.2,
  `slash` 84.9, `attack` 86.1, `slash (3)` 108.7 degrees of UNWRAPPED yaw) against four spins
  (`slash (4)` 391, `attack (3)` 396, `attack (2)` 429, `slash (2)` 507). Put the plain cuts on the
  normals and let the specials and the parry riposte carry the turns. Salaadin had it backwards -
  two of his three normals were full turns, which is what the user saw as "his normal attacks
  have changed".
- **Two fighters can share a clip and keep different speeds, and this is the pattern.** Salaadin's
  Medium and Heavy now play Richard's two clips (`slash (5)`, `slash`). A trim is keyed by CLIP so
  it is shared; the SPEED comes from a per-STATE contact in `swing_contacts.json`. Richard stayed
  at 1.60/1.40 while Salaadin's land at 1.00/1.00 off the same two files. **Always add the contact
  in the same pass as the re-point** - without one the shared trim governs and a 5-frame core gets
  clamped to 6 and smeared (that is the x0.27 failure).
- **"Same speed as X" means the pinned contact speed, not the frame budget.** Salaadin's specials
  were already SHORTER than Bjorn's in frames (43/44/51 against 43/49/53) and still played at half
  the pace, because he had no contact entries at all and Bjorn's are pinned. Matching them slot for
  slot (2.0 / 2.0 / 2.2) was five lines of JSON and no budget change.
- **Salaadin's SP+Heavy LEAPS**: `maxHipsAboveRoot` 2.80 against an idle's ~1.6 and his other
  moves' 1.35. It is the `sword and shield attack` clip ("overhead+leap") and it predates the
  2026-10-23 speed change - but it now plays at x2.2, so the hop is faster. Not touched; the user
  has not called it out, and the pack has nothing else that size.
- **Free-form combos live under COMBO PRESSURE now** (the user's calls, 2026-10-24). A swing that
  starts within `COMBO_PRESSURE_GAP` (1.0 s) of the previous swing ending — raw spam and string
  links alike — is "in pressure", and three rules ride on it, all in `CombatSystem`
  (`comboGapTimer` / `comboPressureCount` / `lastSwingFlow`, reset on flinch and at match start so
  the comeback swing and the opening swing are always free):
  - **Surcharge**: every pressure swing costs its authored stamina PLUS 5/7/10% of MAX stamina
    (Light/Medium/Heavy, `COMBO_SURCHARGE_*`) — this is what finally taxes the L,L,M,M,H,H
    rotation that ducked the 1.5 rule. Play-verified on Bjorn (110 bar): 20 fresh, then 26/31/37.
  - **Flow rule**: each AttackData carries `SwingFlow` (Overhead / Thrust / CutLeft / CutRight /
    SpinLeft / SpinRight / OverheadLeft / OverheadRight — APPEND-only, serialized by index, and
    MIRRORED as a private enum in `CrucibleBalance` because the editor asmdef cannot see game
    types). A pressure swing may be anything EXCEPT the same geometry twice in a row — right cut
    into left cut, spin, thrust or overhead all flow; right cut into right cut is refused — AND
    **an overhead flows into a spin only on its own diagonal**: OverheadRight -> SpinRight (and
    left/left), while a dead-vertical `Overhead` flows into NO spin. The diagonal lives on
    SwingFlow on purpose, NOT on `comesFrom` — that is the DEFENDER's dodge-side data and all
    overheads are authored `comesFrom = Overhead` anyway. Only `Salaadin_Special_HattinCleave`
    is diagonal (OverheadRight, 86 deg of yaw) and it pairs with his WhirlingSaif (SpinRight) —
    the roster's one overhead->spin flow. The table is `Flow` in `CrucibleBalance.cs` (fallback:
    derive from hitSide); gated in `TryAttack` ONLY, so the authored strings are exempt
    (play-verified: Bjorn L->L blocked, L->M->H->L flows, H->M spin blocked (vertical), L->M spin
    flows, Hattin->WhirlingSaif flows, string depth still reached under the gates).
  - **Cap**: a free-form run stops at `FREEFORM_COMBO_CAP` (4) swings — the 5th quick press is
    refused until the 1.0 s pause. String links count toward the run, so a 3-hit string buys one
    raw extension, not a fresh four.
- **String and special lockouts are 0.5 s; the same-attack rule is the 1.5 RULE again** (the
  user's call, 2026-10-23 evening, reversing that morning's 0.5): the same MOVE thrown twice in a
  row bars THAT MOVE ALONE for `SAME_ATTACK_LOCKOUT` 1.5 s - every other attack stays free - and
  `ATTACK_CHAIN_WINDOW` is 2.0 s so "medium, step back, medium" still counts as one chain (0.6 s
  reset it, which is what made Miyamoto's medium a spam button). Tracked by AttackData IDENTITY
  (`CombatSystem.chainAttack` / `bannedAttack`), not by button, so a special and its normal never
  pollute each other's count; gated in `TryAttack`, so string links are untouched and the AI obeys
  it too. `ATTACK_CHAIN_LOCKOUT` is gone. Play-verified: M, M, third M blocked, Light free, M back
  after 1.5 s - and the same with a 1.2 s step-back gap between the two Ms.
- **Uneven arena ground cannot eat a strike any more** (2026-10-23): when the two fighters' roots
  differ in height by more than `UNEVEN_GROUND_HEIGHT_GAP` (0.2 world units), the authored front
  box judges the active window - the third case beside kicks and slow frames in
  `CombatSystem.Update` - because the physical blade sweep passes clean over an opponent standing
  downhill on a sloped floor. On level ground nothing changes. Play-verified at dy 0.55 and 1.2
  with a kinematic raised defender: the hit lands (13 dmg + hitstun) where the user saw whiffs.
- **Parry and dodge each have an unmissable effect and their own sound** (the user's call,
  2026-10-23: both were invisible and the dodge clip was literally a pitched-down swish).
  `SpawnParryFlash` = the gold sparks + a blue-white core pop + a flat cyan shockwave ring;
  `SpawnDodgeBurst` (new in HitEffects, called from `Fighter.NotifyEvaded`; ordinary dash dust
  unchanged) = a pale blue-white cloud + a cyan streak ring at knee height. Cold colours on
  purpose: the whole game is lit warm, so these are the only cold flashes on screen. The clips
  are synthesized by `ArtSource/Audio/make_parry_dodge.py` (struck-steel ring / rising whoosh
  with a two-note chirp), OVERWRITING `parry_01/02.wav` + `dodge_01/02.wav` in place so the
  GUIDs and CrucibleAudio's references survived - no re-run of `Set up audio` needed. Dodge now
  plays at full level (was 0.8).
- **Armour is hand-tuned as of 2026-10-24**: Miyamoto 13, Bjorn 14, Salaadin 17, Richard 20 (the
  user tightened the spread in the scene; the `CrucibleBalance` table was updated to match and
  re-applied, so scene, table and the FighterData assets agree). Block spread unchanged.
- **Blocking fighters WALK now instead of floating** (2026-10-24): a "BlockLegs" animator layer on
  all four controllers - lower-body avatar mask (`BlockLegsLower.mask`), 1D MoveSpeed blend of the
  fighter's own walk clips at x0.8 pace - whose weight `Fighter.UpdateBlockLegs` raises only while
  the guard is up AND the fighter is moving, so a standing block keeps the authored pose head to
  toe. Wired by `Crucible > Animation > Wire the block-walk legs` (idempotent). Guarded walking is
  12% slower (`BLOCK_WALK_SPEED_MULTIPLIER` 0.88, play-measured 0.883). The packs already hold
  walk clips in all four directions (`standing walk left/right`, the strafes) - nothing imported;
  movement is only ever toward/away, so forward/back is the whole vocabulary.
- **ArenaBounds has KEEP-OUT circles** (2026-10-24): serialized `keepOut` list (x, z = centre,
  y = radius) pushed out in the same script clamp as the outer bound - a physical collider can
  never do this job because the fighters' Rigidbodies freeze X/Z (the reason ArenaBounds exists at
  all; the desert hill's MeshCollider is exactly what they were climbing and floating on). The
  desert carries two: the plateau at (-0.5, 12.3) r 5 and the small rock at (0.5, -6) r 1.8.
  Play-verified: a fighter dropped on the hill centre is expelled to the rim exactly.
- **Walls go see-through when they swallow a fighter** (2026-10-24): each arena is ONE mesh, so no
  material fade can work - `WallCutout` (on GameMangers) linecasts camera->chest against the active
  arena's collider and, while blocked, swaps the arena onto `Crucible/ArenaCutout`
  (`Assets/_project/Shaders/`), which dissolves a soft dithered hole along each sight line
  (per-fragment distance to the camera->fighter segment). Original material restored the moment
  the view clears. The shader approximates URP Lit (SH + shadowed main light + normal map) and
  carries ShadowCaster/DepthOnly passes so nothing pops during the swap; the Shader reference is
  serialized on WallCutout so builds cannot strip it.
- **`Shader.Find` only sees shaders the BUILD includes, and that is how every hit effect died in
  the Win64 app** (2026-10-24): `VfxMaterials.Unlit` found the URP unlit shaders in the Editor and
  got null in the player, and the `ArgumentNullException` aborted `CombatSystem.ResolveHit` MID-HIT
  on every landed blow - no blood, no parry flash, no dodge burst, no kill shot, while the Editor
  was perfect. "Universal Render Pipeline/Particles/Unlit" and ".../Unlit" are in
  Graphics > Always Included Shaders now (they land in the build's `globalgamemanagers.assets` -
  grep it to verify a build), and `VfxMaterials` falls back to `Sprites/Default` with a LogError
  instead of throwing. **For any works-in-Editor-not-in-app report, read the player log first**:
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CRUCIBLE test\Player.log` (+ `Player-prev.log`).
- **Hitstun stops an in-progress dash** (2026-10-24, the user's call after a parried special
  whiffed): `HandleDash` ends the dash the frame `IsInHitstun` goes up - before that the dash
  carried a STUNNED fighter 1.96 world units (play-measured), which is what let a parried player
  drift out of the AI's counter. Walking and dash STARTS were already gated; the running dash was
  the hole. And **the AI counter fires from blade range**: a parry happens at weapon contact (up
  to ~3.4 away), so AiController's counter branch allows `gap <= 3.4` when a counter window is
  open (recovery punishes keep 2.7) - Salaadin's counter specials land from 3.3-3.4 on a stunned
  victim (contact f21 < the 30 f stun; range 3.0+ plus 1.2 lunge). Walking cannot escape the
  stun (play-measured 0.000 movement, counters land through a held move-away).
- **Two gamepads already drive the two players** - `ControlBindings.PadFor`: first pad -> the
  Settings "controller player" (default P2), second pad -> the other; play-verified with two
  virtual pads, no cross-talk. `InputManager.LogPads` logs every pad change and the assignment
  ("Pads (...): 2 connected - P1 <- ..., P2 <- ...") so a dead second controller is diagnosable
  from Player.log - a pad on a charge-only USB cable never appears there at all.
- **The killing blow has a presentation** (`KillShot`, 2026-10-24): on the fatal hit time drops to
  x0.2 of match speed (0.9 s real hold, 0.8 s ramp back), every light + ambient + a skybox CLONE
  dim to ~10-15% so the arena goes near-black while the UNLIT blood keeps its colour, and an extra
  double gout of blood marks the blow. The K.O. screen stays dark on purpose; lights restore on
  any menu screen or `StartMatch` (both hooked in GameMenu). The K.O. announce now names the
  WINNER ("<name> wins"), not the fallen; the MatchOver title already did. Play-verified: 0.200
  at the kill, sun 2.10 -> 0.21, full restore on rematch.
- **Every SP+Heavy has HYPER ARMOUR** (`AttackData.hyperArmor`, the user's call 2026-10-23): an
  incoming hit deals its damage but neither flinches nor interrupts the swing; a PARRY still
  beats it outright. The branch is in `CombatSystem.ResolveHit` before `NotifyHit`; the flag is
  written by `CrucibleBalance`'s `HyperArmor` set (false on everything else), so the table stays
  the source of truth. Special stamina is the pre-cut figure x0.85 roster-wide, same session.
- **A combo whose OPENER lands CLEAN cannot be interrupted** (the user's call, 2026-10-23): every
  follow-up link powers through hits like hyper armour - damage still lands, no flinch, no
  interrupt; a parry still beats it and a same-frame trade is still a clash. A whiffed or BLOCKED
  opener earns nothing - and `stringDepth > 0` alone proves nothing, because an `Always` link
  fires off a whiff once the active window passes. The flag is `CombatSystem.stringArmored`: set
  at the FIRST cancel from `attackHitClean`, carried down the links by `BeginAttack`, read via
  `StringArmored` in `ResolveHit`'s armour branch. Play-verified both ways on Salaadin's L->M
  with an injected hit through the real pending-hits path.
- **The AI throws specials and parries now** (AiController, 2026-10-23). Specials: a `Pick()`
  roll (`p.special`: Chill .06 / Moderate .15 / Insane .28) behind stamina >= 55. Parry: a press
  SCHEDULED off the foe's startup, led by 0.12 s and HELD for 0.06 s - measured: contact lands
  ~3 frames before the authored startup ends, the press costs ~2 frames through
  AI -> InputManager -> Fighter, and the one-frame AI snapshot is consumed per frame, so a
  single-frame press can be eaten by the very hit it meant to parry. Verified 24/24 vs heavies;
  an 8-frame light is NOT parryable on reaction and that is fair. `ParryCounterTimeLeft` opens
  the AI punish branch like a dodge counter. Remaining AI gap: it still cannot dial a string.
- **`AiController` is a SINGLETON on GameMangers; `AddComponent<AiController>` on a fighter
  self-destructs in Awake** - and worse, `Activate` on the dying copy leaves `InputManager.IsAi(n)`
  true with nobody writing input, which freezes that player solid. Drive `AiController.Instance`.
- **`TryAttack` returns FALSE when a mid-attack press merely buffers** (the buffer fires later in
  Update), so a harness that counts string links off the return value under-counts. Count
  conversions off `CombatSystem.stringDepth` instead.
- **Low stamina weakens the ATTACKS, not the regen** (Plan_Balance 2.3, shipped 2026-10-23;
  it REPLACED the x0.75-regen-under-20% rule, whose constants are gone). Below
  `WEAKENED_STAMINA_FRACTION` (40%) a swing costs x0.75 stamina, deals x0.75 damage and runs at
  x0.85 pace - attack clock AND `animator.speed` together, so the picture matches the frames.
  Judged ONCE in `CombatSystem.BeginAttack` and held for the whole swing. The damage cut must
  never be smaller than the cost cut or camping at 39% of the bar becomes optimal.
- **A whiffed attack doubles its own recovery** (`WHIFF_RECOVERY_MULTIPLIER = 1.0`, Plan_Balance
  2.1, shipped 2026-10-23): active window closes untouched -> `whiffExtraRecovery` adds
  `recoveryFrames x 1.0` to the total, so a missed heavy is held open longer than a missed light.
  Mutually exclusive with the dodge tax (48 f), which still wins on a dash-dodge. The cancel
  window is NOT stretched - a combo link escapes the authored recovery only. Play-verified:
  Bjorn light 22 f -> 30 whiffed, heavy 38 -> 54; a LANDED light is 26 f (no tax).
- **Block spread is 85 / 75 / 68 / 60** (Richard / Salaadin / Bjorn / Miyamoto, Plan_Balance 2.4)
  and **Miyamoto walks 4.8** (moveSpeed 48, Plan_Balance 2.2) - both in `CrucibleBalance`,
  applied and play-verified 2026-10-23. All six items of `Design/Plan_Balance.md` are SHIPPED
  as of 2026-10-23 (5 and 6 landed later the same day - the bullets below).
- **Dash i-frames are PER FIGHTER and only the HEAD of a dash evades** (Plan_Balance 2.5 / item
  6, shipped 2026-10-23). `Fighter.dashIFrameDuration`, written by `CrucibleBalance`: Miyamoto
  0.18 / Salaadin 0.16 / Bjorn 0.14 / Richard 0.12 s = 82 / 62 / 47 / 35% COVERAGE of each dash.
  Before this, `DASH_IFRAME_DURATION` was DEAD CODE and the whole dash evaded; now
  `ResolveDashAgainst` returns Hit once `dashTimer` passes the window, so every dash has a
  punishable tail (forward-into-the-blade amplification stays live for the whole dash). Verified
  with the dash AGE read at the judgment frame: Miyamoto hit at 0.217 / evaded at 0.100 vs 0.18;
  Richard hit at 0.200 / evaded at 0.117 vs 0.12. The judgment moment is CONTACT (or hitbox-arm),
  so a feed-delay is not a controlled test variable - different dash speeds reach the blade at
  different ages; measure the age, don't infer it.
- **A SUCCESSFUL dodge refunds the dash's stamina** (the user's call, 2026-10-23):
  `Fighter.NotifyEvaded` -> `Stats.AddStamina(dashStaminaCost)`, behind the same firstReport
  guard as the dodge heal so the two evade paths cannot pay one dodge twice. Verified: Richard
  69 -> 85/85 where regen alone reaches ~79. Watch in playtests: the plan warned refund + high
  i-frame coverage is double-generous for Miyamoto - his coverage (82%) and this shipped together.
- **Parry is a per-fighter table now** (`CrucibleBalance` writes all six fields; they were
  identical on the whole roster). Miyamoto the counter fighter: window 0.18, cooldown 0.8,
  counter window 1.0, riposte x1.5; Bjorn the worst parry (0.10, cost 10, cooldown 1.2); Richard
  and Salaadin base (0.12 / 8 / 1.0). This SETTLED the old 1.5-vs-1.35 counter damage and
  1.0-vs-0.5 counter window discrepancies: the table is the answer.
- **The parry is the timed BLOCK RAISE now** (the user's call, 2026-10-24, reversing the
  dedicated-button design): raising guard opens `parryWindow` seconds of deflection, then the
  raise settles into an ordinary block. The Defense button still opens the same window - the AI
  parries through it. Opening the window is FREE; `parryStaminaCost` is charged only when a parry
  CONNECTS (in `NotifyParry`), and `parryCooldown` paces re-raises. Hand in hand with it:
  **block stamina drain is 0 roster-wide** (the user set it in the scene; table + BalanceConfig
  default updated - holding guard costs nothing, the 85/75/68/60 reduction spread is the block's
  whole identity) and **every stun in the game is 0.5 s**: `UniformHitstunFrames = 30` in
  `CrucibleBalance.WriteMove` overrides every AttackData's hitstun column, and `parryStun` /
  `PARRY_STUN_DURATION` are 0.5. Play-verified: raise 4 f before a heavy = PARRY (no damage,
  -10 on connect, attacker stunned ~30 f); raise 60 f early = ordinary block (chip, no parry);
  2 s hold drains nothing; a clean hit stuns ~30 f.
- **Salaadin's axis is COMBO FREEDOM** (Plan_Balance item 5, shipped 2026-10-23, with the
  deferred damage cut x0.92 -> x0.85 riding along - the roster's damage order is now Bjorn 110 >
  Richard 100 > Miyamoto 92 > Salaadin 85). Three breaks with the shared string shapes, all
  authored in `CrucibleCombos`: his normal->normal links are NOT `openerOnly` (he alone dials
  three normals, L->M->L; others are blocked by the opener flag, and `MAX_STRING_LENGTH` still
  caps everyone at 3); every normal branches to BOTH other normals and every special hands back
  TWO normals mid-string (the N->SP->N shape nobody else has); and `Wire()` writes his
  `cancelStartFrame` = the move's own startup (others stay -1 = startup+active), which with the
  pre-contact guard in `TryCancelIntoFollowUp` means exactly one thing: a LANDED hit converts
  into its follow-up instantly. Play-verified: L->M->L lands 5+6+5, L->SP+M->M lands 22, the 4th
  press is capped, ShinCut converts on frame 12 not 16. `Report combo strings` now includes his
  (lowercase) folder - it used to silently skip him.

- **A clip that is NOT in `swing_trims.json` is skipped entirely and fitted WHOLE**, and a contact
  entry does NOT rescue it - `ComputeTrims` bails at `if (!block.Success) continue;` before the
  contact is ever read. The symptom is a state speed in the double digits:
  `Salaadin_Light_SaifCut` came back at **x14.00** (the whole 4.20 s clip against an 18-frame
  budget) with a perfectly good contact of `{28.6, 1.1}` sitting in the JSON. Adding the trims
  entry put it at x1.10. **When wiring a clip no fighter has used before, add it to BOTH files.**
  The warning is logged (`<clip>: not in swing_trims.json`) and is easy to miss in a run that
  prints a hundred lines.
- **The GreatSwordPack cannot be used by a shield fighter, and `grip_spread` is how you know.**
  Every clip in it measures **0.11-0.20** (both hands on the hilt); the SwordShieldPack and the
  barbarian AxePack run **0.45-1.08** (hands apart). A two-handed clip retargeted onto Richard or
  Salaadin drags the shield arm onto the sword. The AxePack is a one-handed RIGHT-hand pack and
  retargets onto either of them cleanly - it is an axe only in the weapon the original held, which
  is not part of the clip.
- **`tipHi` is the measurement behind "that animation looks incomplete".** It is the weapon tip's
  highest point measured FROM THE HIPS. Salaadin's old SP+Light (`slash (2)`) is the only strike in
  all four packs whose tip NEVER rises above the hips - **0.07**, against 0.29-1.45 for everything
  else - so across 107 frames there is no arc to see and no edge presented. That is what the user
  reported as an unfinished animation, and it is checkable before wiring rather than after playing.
- **The barbarian pack holds only four strikes nobody uses**, and after 2026-10-23 three are spent:
  `attack downward` (Richard's Light), `attack backhand` (Richard's SP+Light, shared with Bjorn),
  `combo attack ver. 2` (Salaadin's Light). Only `combo attack ver. 1` (233 deg, advancing) is
  still free. The SwordShieldPack has nothing unused at all.
- **`Richard_Thrust` is now an ORPHAN**: both states that played it (his Light and his SP+Light)
  moved to barbarian clips on 2026-10-23, so **no animator state plays it** and Richard no longer
  has a thrust of any kind. The authored FBX and `richard_clips.py` still build it; wiring it back
  is a one-line clip-map change.
- **A capture needs the MAIN camera, and the coroutine needs time to reach the pose.** A second
  camera at `depth = 10` still lost to the fight camera and photographed the match view; taking
  `Camera.main` over (with `FightCamera.enabled = false` first, or it re-frames every frame) works.
  And `eval_file` returns the moment `StartCoroutine` is called - the first three captures were
  the idle, taken while the coroutine was still in its 70-frame settle. Poll
  `EditorApplication.isPaused` before capturing.

- **The VS screen posed the pair BACK TO BACK, and the cause was one sign.** `StageFighters`
  builds each fighter's rotation as `LookRotation(toCam) * Euler(0, yaw, 0)`, so the yaw turns him
  off camera-facing. A positive Y rotation carries +Z toward +X, while `screenRight` works out to
  **-X** when the camera sits on +Z - so `+turn` on the screen-LEFT fighter and `-turn` on the
  right swung BOTH of them outward. Negated, P1 turns right and P2 turns left. A fighter facing
  the camera at this framing is already ~25 deg off a pure profile, so the 60 deg yaw lands them
  **5.2 deg** short of square on, which keeps a little of each face to camera instead of two flat
  profiles. Measured on all three pairings as `dot(forward, toOpponent)` = 0.996.
- **`MATCH_START_GAP` (BalanceConfig, 6.5) is the opening distance, and the authored marks were
  never it.** The scene's two marks sit **2.30** apart while every fighter's longest reach is
  **3.10-3.50**, so a swing thrown on the first frame of FIGHT connected before anyone had moved.
  `ResolveSpawns` now spreads the two FIGHTING sides to that gap about the midpoint of the marks,
  along the axis the marks establish - taking both from the marks keeps it right in an arena whose
  marks are elsewhere or at an angle, and the height is still a raycast at the NEW spot. 6.5 clears
  the worst case outright: longest reach 3.50 plus longest lunge 2.60 is 6.10, so NOTHING connects
  from the marks. Verified live on both long-lunge fighters - Bjorn's six moves close at most to
  **4.73** (Heavy+SP, travelling 1.77) and Miyamoto's to 5.60; all twelve whiff.
- **A lunge needs a LIVE MATCH to move anyone, and a frozen test reports it as zero travel.**
  Calling `PlaceFighters` and `TryAttack` without `StartMatch` fires the attack (neither
  `InputEnabled` nor a disabled `Fighter` stops `TryAttack`) but the fighter never advances, so
  every closing distance reads exactly the starting gap. **And `ApplySides` must be called too**:
  without it the non-participants stay active and `FindOpponent` takes the first other Fighter in
  the scene - a sweep had Bjorn lunging at a benched Salaadin parked across the arena while the
  health check watched Miyamoto. Both faults produce confident, wrong numbers that look fine.
- **The landing page is `MenuStage.Backdrop.Black`, applied INSTANTLY.** Boot used `Backdrop.Menu`,
  which is warm ink (0.039, 0.031, 0.027) plus a bronze watermark glow at 0.22, the logo
  watermark, grain and 26 drifting embers - so the logo never faded up out of darkness. The new
  mode holds all of that at zero and uses true black. The `instant` flag matters as much as the
  colour: the field RAMPS toward its target, so without it the boot screen opened on the lit arena
  and sank to black over about a second (measured mid-ramp at alpha 0.670, arena plainly visible).
  Every other screen keeps the ramp - that settle is what makes the wipes read. `BootBlackHold`
  (0.5 s) then holds on black before the 1.1 s logo fade, so the two read as a sequence. Measured
  from Main: field (0,0,0,1.000) on the FIRST frame, logo 0.000 through 0.42 s, then 1.000 at 1.65.
  `Backdrop.Black` is APPENDED to the enum because `backdrop` is a serialized field.
- **The boot logo fades OUT on the press now** (`BootExitRoutine`, 0.45 s, the mirror of
  `BootRoutine`'s fade in) instead of being cut away mid-glow by the wipe. `bootExiting` makes
  `UpdateBoot` stand off the colours while it runs, and `BuildBoot` clears the flag so a second
  visit to Boot does not start muted.
- **`Mathf.Abs(Mathf.Sin(t * w))` runs at TWICE the period you wrote, and it blinks.** Abs folds
  the sine's negative half upward, so the cycle is pi/w rather than 2*pi/w, and it leaves a hard
  cusp at the bottom that reads as a blink rather than a fade. The boot prompt's breathe was
  `Abs(Sin(t * 1.6))` - a ~1.96 s cusped pulse - and is now a plain sine mapped into 0..1 over
  `BootPromptPeriod` (4 s). Measured in play: **3.99 s** peak-to-peak, alpha 0.30-1.00.
- **`simulate_key` does NOT reach `GameMenu.AnyButton()`**, which reads legacy `Input.anyKeyDown`,
  touches and `Gamepad.all`. A sampler sat on the boot screen for 260 frames with `bootReady` true
  and the press never registered. What works is a virtual device:
  `InputSystem.AddDevice<Gamepad>()`, then `InputSystem.QueueStateEvent(pad, new GamepadState()
  .WithButton(GamepadButton.South))`, and `RemoveDevice` afterwards - that drives the real
  `UpdateBoot -> AnyButton -> exit` path.
- **Driving a menu coroutine by hand skips the teardown the real path does.** Invoking
  `BootExitRoutine` through reflection without `StopCoroutine(bootRoutine)` first left the old
  fade-in alive, and the logo alpha snapped back to 1.0 the frame after the fade finished. It is
  an artifact of the test, not the code - the genuine key-press path holds 0.000 all the way into
  the wipe. **Reproduce a bug through the real entry point before believing it.**

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

- **The fight records itself and the K.O. card can replay and export it** (2026-10-24).
  `ReplayRecorder` (new, GameMangers, exec order 300 = after TwoHandGrip): all transforms under
  both fighting roots + the camera at 30 Hz on an UNSCALED clock, FIGHT -> the K.O. card, 600 s
  ring; markers tag parries/counters/specials/combo links/the kill (hooks in `ResolveHit`).
  `Screen.Replay` ("Save the game record") plays it in the arena: scrub, Cut from/to, Jump to
  marker, Camera row (As played + five locked angles computed from the pair), Export = own
  Motion-JPEG .avi writer, 30 fps, silent, to `%USERPROFILE%\Videos\CRUCIBLE\`, menu hidden
  during capture (frames come off the screen). Playback disables Animator/TwoHandGrip/WeaponAim/
  FightCamera + kinematic rigidbodies; EndPlayback restores the exact tableau. No export on web.
- **The K.O. card asks "are you sure" before Rematch / Change fighters / Main Menu**
  (`confirmAsk`/`confirmYes` on GameMenu; Esc answers No).
- **Parry window 0.9 s roster-wide and every get-hit stun 0.8 s** (`UniformHitstunFrames` 48),
  2026-10-24. **Stun locks the guard** (HandleBlock checks hitstun). **Combo escape**: a clean
  hit by a linked move applies a DODGEABLE stun (`ComboStun` - dash allowed, block/walk not); a
  follow-up with a clean opener BYPASSES a raised guard (`blocked = IsBlocking &&
  !StringArmored`); dodging the wrong way into a follow-up = hit + 1.0 s non-dodgeable stun
  (`COMBO_WRONG_DODGE_STUN_FRAMES`). A dash remembers its `StunSerial`, so a combo-stun dodge
  survives the stun while any NEW hit still ends a running dash.
- **`EndMatchToMenu` stops the count-in and `ClearBeat()`s** (else "FIGHT" stayed over the main
  menu), and it + `Unstage()` re-run `ApplySides` - the select screens wake the WHOLE roster, and
  leaving them any other way stacked the extras on the two marks, capsules climbing (the floating
  bystanders).
- Known gaps carried forward: the AI cannot parry, throw a special or dial a string on purpose;
  the web build is uncompressed on purpose; hosting means the laptop is the server (relay +
  quick tunnel, new URL each restart); `PlayProbe`, `BladeTrace`
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
