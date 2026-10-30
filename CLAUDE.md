# CRUCIBLE: Honorable Souls

1v1 3D fighting game. Unity 6000.4.4f1 (URP), C#. Target input: PS4/PS5 controller (P2) and keyboard (P1).

## Layout

- `Assets/_project/Scripts/characters/` — `Fighter` (movement, dash, block, facing, hit reaction), `FighterStats`, `FighterData`.
- `Assets/_project/Scripts/combat/` — `CombatSystem` (attack phases, hitstun, hit resolution), `HitDetector` (trigger hitbox), `AttackData` (ScriptableObject per attack), `DamageCalculator`.
- `Assets/_project/Scripts/config/BalanceConfig.cs` — global tuning constants, `FRAME_TIME = 1/60`.
- `Assets/_project/Scripts/input/` — `InputManager` singleton on the `GameMangers` object.
- `Assets/_project/Scripts/UI/FightHud.cs`, `Assets/_project/Scripts/core/FightCamera.cs`.
- `Assets/_project/Characters/<Name>/Attacks/` — per-character `AttackData` assets, named `<Name>_<Weight>_<Move>`. `Characters/<Name>/Model/` — the Tripo rigged FBX + textures + material (git-ignored, see Git notes). `Characters/_Unrigged/` — Tripo meshes not rigged yet (git-ignored).
- `Assets/_project/Animation/Controllers/` — `P1_Bjorn` … `P6_Buwaka` controllers, one per fighter. `Animation/Clips/{Mixamo,Maria}/` — placeholder test clips; `Animation/Clips/Custom/` — for Blender exports.
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
- **Six fighters now (2026-10-26).** Genghis (Player5, `P5_Genghis.controller`, `genghis_data`) —
  two-handed ornate saber, the second tank: 105 hp / 95 stamina / 18 armour / x1.00 / walk 3.6,
  block 62, dash i-frames 0.13, and the roster's biggest parry payoff (counter window 1.2,
  riposte x1.7, Khan's Verdict = GreatSwordPack `high spin attack`). Move table in
  `Editor/CrucibleGenghis.cs`; controller copied from P2_Miyamoto; animation is the
  GreatSwordPack (the 2H pack the shield fighters cannot use), specials play the normals' clips
  harder, like Richard's. He IS in `TwoHanded` (TwoHandGrip holds the saber hilt). Combos are
  deliberately sparse: L→M, M→H, H→SP+H, no special hands back.
  Buwaka (Player6, `P6_Buwaka.controller`, `buwaka_data`) — spear + round hide shield (the shield
  is Salaadin's source model re-prepped): 100 / 100 / 15 / x0.95 / walk 4.0, block 68, the
  roster's longest reach (ranges 3.3–4.0, GreatImpale lunge 2.0) with `LungeOverride` keeping the
  spear at distance (stops 2.2–2.6). Move table in `Editor/CrucibleBuwaka.cs`; controller copied
  from P3_Richard; strikes are SwordShieldPack + AxePack + GreatSwordPack + **`Richard_Thrust`
  retargeted across fighters** (his Light and ripostes — an authored CopyFromOther clip retargets
  onto any humanoid; each state takes a per-STATE contact or the thrust's 9-frame core smears to
  x0.36). Since 2026-10-30 (the user's picks) his Heavy "Iklwa Cleave" plays Richard's Heavy clip,
  SP+Medium "Leopard Wheel" Salaadin's Crescent Slash clip and SP+Heavy "Great Whirlwind" Bjorn's
  SpinOverhead clip (a 2H clip on a shield fighter), each with its source's contact AND hit
  geometry. That SP+Heavy was REPLACED the same day (the 2H clip dragged the shield arm onto the
  spear): it is now "Great Impale", the LEAPING overhead plunge SwordShieldPack
  `sword and shield attack` (contact {36.4, 2.0}, Overhead flow, AimFull) - lands 1.5-5.0, hips
  peak 3.27 against 1.58 standing, and at the peak BOTH arms go up, shield over his head; asset names unchanged (`Buwaka_Heavy_IklwaThrust`, `_Special_FallingSpear`,
  `_Special_GreatImpale`). Both built by the metarig chain (`fit_metarig.py` Salaadin-shape targets →
  `metarig_to_mixamo.py` → `mount_body.py`, weapons bone-parented); bodies/rigs are gitignored
  like the rest. Statures calibrated 2026-10-26: Genghis 2.78, Buwaka 2.65 (StandingHeight
  3.46 / 3.10, GroundOffset 0.113/3 and 0.110/3), soles verified within 0.013 of the arena.
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
  **Character select is a RING (2026-10-26, the user's design)**: `StageCircle` stands the
  whole roster on a circle (`CircleRadius` 5.2) round the marks' midpoint, facing in; the camera
  sits just off centre (`CircleCamIn`) looking OUT at one fighter, and scrolling sweeps it round
  (target angle ACCUMULATES, so it loops the way you scrolled). **2P = split screen**: Camera.main
  takes the left half, a runtime `SelectCam2` the right; each player's own pad drives his half at
  once (`InputManager.MenuNavigateFor/MenuConfirmFor/MenuBackFor(player)`, honouring the Choose
  Controls pins; keyboard P1 = WASD+Space+Backspace, P2 = arrows+Enter+Delete). Cross locks,
  Circle unlocks, Circle on an unlocked half with nobody locked leaves; Escape unwinds the latest
  lock. Both locked -> `MergeSelectRoutine` slides the halves into one and goes to arena select,
  which shows ONLY the picked pair. **1P / Practice** keep one full-screen camera and the
  sequential two locks (you, then the computer/dummy). `RestoreSelectCameras` runs on ANY exit
  from CharSelect and in `Unstage`, and leaving a stage screen for a menu sets the backdrop
  INSTANTLY - the ramp from the stage's 0.42 showed the posed fighters through the main menu.
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
- **NO FIXED STRINGS (2026-10-29, the user's call).** `CombatSystem.TryCancelIntoFollowUp` cancels a
  swing's recovery into ANY press (special openers included) that passes `CombatSystem.Flows(last,
  next)` - the one flow rule, shared with raw pressure swings in `TryAttack`: never the same geometry
  twice EXCEPT two straights (Overhead->Overhead, Thrust->Thrust), and an overhead flows into a spin only
  on its own diagonal. Still in force: 3-hit `MAX_STRING_LENGTH`, stamina, the 1.5 repeat bar (now also
  checked on a cancel), the cancel window and the no-cancel-before-the-hit-resolves guard. GONE: the
  one-special-per-string rule and every authored link EXCEPT the dedicated ones - a link whose follow-up
  the button cannot throw raw (`IsDedicatedLink`: Bjorn Light->Heavy = SpinOverhead, Miyamoto ->SP+Heavy
  = Iaijutsu, Buwaka FallingSpear->Light = SerpentCoil), which keep their own `requires` AND must flow.
  The Move List's strings page is removed. `SwingFlow` is now gameplay-critical: changing a move's flow
  changes what can follow it.
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
- **Slope is handled by ATTACK HEIGHT (2026-10-30, the user's call; REPLACED the 2026-10-23
  "uneven ground" fallback and `UNEVEN_GROUND_HEIGHT_GAP`).** `AttackData.attackHeight` was unused
  until now and is gameplay data: `CombatSystem.IsRangeJudged` makes **Overhead, Low and Uppercut**
  strikes land on HORIZONTAL reach (authored front box + the tip's forward reach this swing, judged over
  the whole padded armed window, whatever the slope); an Overhead must also have brought the tip down
  to the target's crown (`HitDetector.TipPosition.y <= capsule top`). **Mid and High stay physical** -
  a slash that passes over a lower opponent's head is a valid miss. The case that started it: Bjorn
  0.3 uphill at 0.9-1.35, Blood Eagle (then labelled High) came down BEHIND Salaadin - only the blade
  head is swept, so a target inside the arc was never touched. Heights relabelled the same day: Bjorn
  OverheadDoubleAxe / BloodEagle / SpinOverhead / HewingCharge, Miyamoto Heavy, Sensei MountainSplitter / FinalLesson
  -> Overhead; Richard Medium, Salaadin CrescentSlash (user's calls) and Salaadin Heavy (measured tip
  0.75-1.34), Sensei TempleBell / CircleOfWillow -> Low; Richard SP+Light -> Uppercut. A future
  high/low guard split (L2 modifier) is planned on top of this field.
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
- **A dash DODGE-CANCELS an attack's recovery** (the user's call, 2026-10-28 - "I can't dodge after
  any attack"; with the x2 test frames plus the whiff penalty a missed heavy locked the dash for
  ~1.5 s and the press was dropped). Once the swing's hit window has closed
  (`CombatSystem.CanDodgeCancel`: armed and disarmed), `HandleDash` calls `DodgeCancel`, which ends
  the attack through the normal `FinishAttack` path (special/string/repeat lockouts still apply) and
  serves the skipped recovery - whiff and dodged-swing extensions included - as `attackLockoutTimer`:
  you can get away, you cannot swing sooner. The attack states have no Dash transition, so the
  cancel `CrossFadeInFixedTime`s straight to `Dash_Forward/Back/Left/Right` (all six controllers).
  Play-verified on Bjorn's whiffed heavy: cancel open f36, dash f41, Dash_Back by f44, a light at
  f70 refused.
- **Hitstun stops an in-progress dash** (2026-10-24, the user's call after a parried special
  whiffed): `HandleDash` ends the dash the frame `IsInHitstun` goes up - before that the dash
  carried a STUNNED fighter 1.96 world units (play-measured), which is what let a parried player
  drift out of the AI's counter. Walking and dash STARTS were already gated; the running dash was
  the hole. And **the AI counter fires from blade range**: a parry happens at weapon contact (up
  to ~3.4 away), so AiController's counter branch allows `gap <= 3.4` when a counter window is
  open (recovery punishes keep 2.7) - Salaadin's counter specials land from 3.3-3.4 on a stunned
  victim (contact f21 < the 30 f stun; range 3.0+ plus 1.2 lunge). Walking cannot escape the
  stun (play-measured 0.000 movement, counters land through a held move-away).
- **The held double-flick RUN** (2026-10-25): one right-stick flick = a dash; a second flick
  toward the opponent inside 0.5 s, HELD, keeps the fighter running at HIS OWN DASH SPEED
  (dashDistance/dashDuration - Miyamoto 11.8 u/s, Salaadin 8.8, Bjorn 7.3, Richard 5.9; no
  multiplier constant exists, tune the dash to tune the run) until the stick re-centres,
  anything interrupts him, the bar empties or he reaches the opponent. No second dash and no
  second dash cost; the run drains `RUN_STAMINA_PER_UNIT` 3.2/unit of ground (the cost rode the
  speed up from 2/unit, the user's calls), regen paused while running. The Run_Forward clip
  loops at x1.10 (`RunAnimSpeed`, set by `Use Mixamo packs`). Keyboard: Run key + walk toward.
  The run clip is the BARBARIAN run for Bjorn AND Miyamoto ("great sword run" is Maria's and
  carried her sway); Bjorn's Walk_Back + Dash_Back are the PLAIN AxePack walk back now, not the
  re-posed 2H carry - TwoHandGrip holds the haft whatever the clip does. Latch in
  `InputManager.PadFlickState`; movement in `Fighter.HandleRun`; animator = `Running` bool +
  looping `Run_Forward` state on all four controllers (`Crucible > Animation > Wire the run
  state`, `Editor/CrucibleRun.cs`; the clip entries live in the `CrucibleMixamoSetup` maps).
  Bjorn/Miyamoto's `Dash_Forward` is a RUN stride now (AxePack `standing run forward`), not the
  walk. Touch controls have no run gesture yet.
- **Two gamepads already drive the two players** - `ControlBindings.PadFor`: first pad -> the
  Settings "controller player" (default P2), second pad -> the other; play-verified with two
  virtual pads, no cross-talk. `InputManager.LogPads` logs every pad change and the assignment
  ("Pads (...): 2 connected - P1 <- ..., P2 <- ...") so a dead second controller is diagnosable
  from Player.log - a pad on a charge-only USB cable never appears there at all.
- **A 2-player match now opens on a CHOOSE CONTROLS screen** (the user's call, 2026-10-25, after
  "both controllers work in menus but only one in game"): menus poll EVERY pad (`Gamepad.all`
  loops in MenuNavigate/MenuConfirm) while the fight used `PadFor`'s index guess - and a DualSense
  paired over Bluetooth AND plugged in with a data cable is listed TWICE, so a player could be
  handed the dead ghost entry. `Screen.PickDevices` (appended to the enum) sits between Two
  Players and character select: each player cycles Keyboard / every connected pad by name; the two
  may not share a pad (two keyboards are fine); landing on a pad PULSES ITS RUMBLE
  (`InputManager.IdentifyPulse`) so identical pads - and ghosts, which cannot rumble - tell
  themselves apart. Defaults: 2 pads = one each, 1 pad = keyboard P1 / pad P2, none = two
  keyboards. `InputManager.SetPlayerDevice(player, pad-or-null)` pins the routing: a chosen pad is
  read EXCLUSIVELY (that player's keyboard goes dead), a chosen keyboard ignores all pads, a
  chosen pad that disconnects falls back to the auto routing rather than leaving the player dead.
  One Player / Practice / Online entries call `ClearPlayerDevices()` so a stale pin never leaks
  into other modes; rematches keep the choice. Play-verified with virtual pads: pinned pads land
  on their own players only, keyboard-only ignores pad presses, disconnect falls back.
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
  Motion-JPEG .avi writer, 30 fps, to `%USERPROFILE%\Videos\CRUCIBLE\`, menu hidden
  during capture (frames come off the screen). Playback disables Animator/TwoHandGrip/WeaponAim/
  FightCamera + kinematic rigidbodies; EndPlayback restores the exact tableau. No export on web.
- **The record carries the fight's EVENTS, and the export carries HUD + effects + SOUND**
  (2026-10-25): choke-point hooks record every hit effect (`ReplayRecorder.NoteFx`/`NoteBlood`
  inside HitEffects' spawns), every one-shot exactly as played - final volume AND jittered pitch
  (`NoteSound` inside `CrucibleAudio.Shot`) - and each fighter's hp/stamina per 30 Hz sample.
  Playback re-fires effects (and sounds, outside export) on small forward steps (0 < dt <= 0.5 s)
  and drives the real FightHud by writing recorded bars into the frozen fighters' Stats
  (`FightHud` polls stats, so it just works; the HUD stays visible on `Screen.Replay`). The
  export's soundtrack is MIXED, not captured - one-shots at exact sample positions over the
  fight-music + wind beds (`CrucibleAudio.ExportBeds`), muxed as a second AVI stream ('01wb'
  PCM 16-bit stereo); `AudioClip.GetData` only works on DecompressOnLoad clips, so a streaming
  clip ships the video silent rather than not at all. The kill shot's light dimming is the one
  presentation not recorded. **Export is PIPELINED** (same day, the user's call - it ran slower
  than real time): the main thread only captures raw pixels; JPEG encoding runs on 2-6 worker
  threads (`ImageConversion.EncodeArrayToJPG` takes plain arrays and is thread-safe - a
  `Texture2D.EncodeToJPG` is not an option off the main thread) and one writer thread streams
  the .avi in frame order with the audio interleaved. Inline, the ~40 ms 1080p encode alone
  exceeded the 33 ms frame budget.
- **Every finished fight auto-saves to a RECORDS LIBRARY, listed on the main menu** (2026-10-25):
  `ShowMatchOver` calls `ReplayRecorder.SaveRecord` (gzipped binary `.crec`, magic CREC v1, to
  `persistentDataPath/Records/`, newest 20 kept, duration >= 3 s only - an abandoned match via
  EndMatchToMenu is NOT saved). Transforms are stored by PATH under each fighter's root and
  sounds by CLIP NAME, so `LoadRecord` re-binds to the LIVE roster + `CrucibleAudio.FindClip`;
  a record of a fighter not in the build refuses to load rather than half-playing, and a renamed
  bone just stops animating (null track, skipped). Main menu `Records` -> `Screen.Records`
  (APPENDED to the enum) lists them (`ListRecords` reads only gzip headers - meta sits before the
  bulk on purpose); `OpenRecord` switches to the record's arena (`Select(i, remember:false)`),
  `ApplySides` the recorded pair by name, loads, and enters the same `Screen.Replay`. The Show()
  teardown re-applies the SELECTED sides when leaving Records/Replay outside a match, so a watched
  record cannot leave the wrong pair active behind the menus.
- **Exports take SEGMENTS now, and "Export the highlights" uses them** (2026-10-25):
  `BeginExport(List<Vector2>)` stitches cuts into one .avi (the single-cut call is a one-segment
  wrapper); audio is mixed per segment at its own sample offset, so one silent cut cannot cost
  the reel its soundtrack, and `FireEvents`' 0.5 s forward-step guard already swallows the seam
  between cuts. `HighlightSegments(pre 2.0, post 1.6)` builds windows around every marker +
  every Blood/Parry fx event, merging windows that come within 0.75 s of each other.
- **The K.O. card asks "are you sure" before Rematch / Change fighters / Main Menu**
  (`confirmAsk`/`confirmYes` on GameMenu; Esc answers No).
- **Parry window 0.2 s / cooldown 0.8 s roster-wide** (the user's call, 2026-10-25, killing the
  block-spam parry: 0.9 window + ~1.0 cooldown kept a window open half the time under rhythmic
  tapping). The guard must now go up in the LAST 0.2 s before contact - the end of the incoming
  swing, not its wind-up - and a re-raise inside 0.8 s of the previous raise opens NO window (it
  is an ordinary block; blocking itself is never locked out). Numbers in the `CrucibleBalance`
  parry table (all four rows) + `BalanceConfig.PARRY_WINDOW`; the mechanics were already there.
  Play-verified: window gates exact (open f2, expired f15, refused f23, re-open f55), raise 9 f
  before a heavy = parry, holding block from the swing's first frame = plain chip block.
- **A parried attacker can do ONE thing: parry the counter** (the user's call, 2026-10-27 - a
  parried Miyamoto recovered after the flat 0.5 s stun and swung again inside the parrier's 0.8-1.2 s
  counter window). `Fighter.ParryStunFrames` = max(`parryStunDuration`, the PARRIER's
  `parryCounterWindow` + `BalanceConfig.PARRY_STUN_PAST_COUNTER` 0.2), so the stun always outlasts
  the counter window. The stun is flagged `CombatSystem.ParriedStun`: attack, dash, walk and plain
  block all refused; a guard raise opens the ordinary 0.2 s parry window (the guard shows only while
  it is open), so a mistimed raise eats the counter. Play-verified Genghis parrying Miyamoto: stun
  1.40 s vs counter 1.20 s, dash/walk/attack refused, raise = 0.2 s parry window then guard down.
- **Nobody walks up an obstacle any more** (the user's call, 2026-10-27: ledges, rocks and walls
  were climbable, worst in the corners). `Fighter.BlockObstacles` runs after every move, before
  `ClampToArena`: a capsule from `BalanceConfig.OBSTACLE_STEP_HEIGHT` (0.6, knee height - the
  desert's 0.4 floor border still walks) to `OBSTACLE_PROBE_TOP` (2.0) is swept along the frame's
  move AND tested for overlap at the end of it; either one refuses the whole move (a dash or
  knockback into it ends there). Fighters and triggers are ignored. Three things that did NOT work:
  `Physics.ComputePenetration` returns nothing against the arenas' concave MeshColliders (enabled
  probe or not); a cast that STARTS inside a surface reports no hit; and SLIDING along the face -
  on the desert's curved wall the slide carried 0.007/frame outward until the probe was inside,
  after which he walked through. Hence: no slide, plus the end-overlap test. Verified: desert
  ledges stop at r 16.8 / 14.8 / 15.2 against walls at 17.4 / 15.3 / 15.6, forest edge 12.7 vs 13.4.
- **[REMOVED 2026-10-29 - nobody can fall out of the desert now; `ringOutGaps` is empty and a `RingOutSeal` BoxCollider under DesertArena closes the opening, written by `Crucible > Arena > Desert > Seal the ring-out` and by the surroundings build. The ring-out code in ArenaBounds/Fighter stays, unused.]** The Desert Arena HAD a RING-OUT (the user's call, 2026-10-27): the one opening between the
  wall end and the statue, `ArenaBounds.ringOutGaps` = (-17.0, 9.4) -> (-12.1, 11.8), depth 1.8
  past the line (the floor lip; the ground drops behind it). Crossing it by ANY means - walking
  included, the user's choice - is a loss: `Fighter.CheckRingOut` zeroes health and calls
  `NotifyDeath`, the menu's ordinary `CheckKO` ends the match ("Ring out / <name> wins"), and
  `FallOut` drops the body through the gap (colliders off, kinematic; `ResetForMatch` restores
  both). Practice with infinite health puts both back on the marks instead. Play-verified: walked
  out at frame 81, health 0, match over, body at y -4.6 a second later.
- **Forest Clearing ships now, with a GREEN floor** (2026-10-27). Its floor was a set of magenta UV
  charts in the 4096 atlas; `ArtSource/Textures/green_forest_floor.py` recolours exactly those
  (mask = up-facing arena triangles inside the clearing, rasterized in UV space in Unity, saved as
  `ArtSource/Textures/forestclearing_floor_mask.png`; grown 4 px into pink padding only, so the
  purple orbs keep their colour). Original texture in `ArtSource/Textures/_backup_20261027/`.
  Selectable order is now Medieval Fortress, Desert Arena, Forest Clearing.
- **String follow-ups play FASTER when dialed fast** (the user's call, 2026-10-25): a string
  link's attack clock AND animation run at `currentAttackPace`, lerped from
  `COMBO_FOLLOWUP_MAX_PACE` 1.3 (press already buffered when the cancel opened - the dialed
  combo) down to `COMBO_FOLLOWUP_MIN_PACE` 1.1 across `COMBO_FOLLOWUP_RESPONSE_WINDOW` 0.25 s of
  lateness. MAX is the speed limit; fresh swings and raw pressure swings stay x1. `cancelOpen` /
  `cancelOpenedAt` are stamped once per swing in `CombatSystem.Update` (window entered AND hit
  resolved / active passed); the pace rides `BeginAttack(attack, depth, pace)` like the weakened
  x0.85 does. Play-verified: instant dial = animator 1.30, +4 f late = 1.25 (the lerp exactly),
  and the victim's stun held with NO gap from the opener's contact through the follow-up.
- **Every get-hit stun is 0.8 s** (`UniformHitstunFrames` 48),
  2026-10-24. **Stun locks the guard** (HandleBlock checks hitstun) - EXCEPT the stun from a hit
  the fighter BLOCKED (`GuardStun`, 2026-10-25): that one holds the guard UP, so a block is
  continuous under pressure. **Combo escape**: a clean hit by a linked move applies a DODGEABLE
  stun (`ComboStun` - dash allowed, block/walk not); dodging the wrong way into a follow-up =
  hit + 1.0 s non-dodgeable stun (`COMBO_WRONG_DODGE_STUN_FRAMES`). A dash remembers its
  `StunSerial`, so a combo-stun dodge survives the stun while any NEW hit still ends a running
  dash.
- **A combo cannot break a guard that can still pay** (the user's call, 2026-10-25, REPLACING
  the 2026-10-24 guard bypass `blocked = IsBlocking && !StringArmored`): a string follow-up
  against a raised block is blocked like any hit, but each strike of the string costs the
  defender MORE - base block cost x (1 + `COMBO_BLOCK_COST_GROWTH` (1.0) x stringDepth), and the
  3rd strike pays a further x1.25 (`COMBO_BLOCK_THIRD_HIT_MULTIPLIER`, the user's call later the
  same day) - so a medium string runs 10/20/38 and a heavy one 18/36/68. The moment the bar
  cannot cover the price the guard BREAKS: full damage, the rest of the bar emptied, ordinary
  non-dodgeable stun.
  **Then 1.3 s of RECOVERY after that stun** (`GUARD_BREAK_RECOVERY`, 2026-10-26): walk or dodge
  only - `GuardBreakRecovering` refuses attacks, the guard and the parry.
  A blocked hit's stun holds the guard (`Fighter.NotifyBlockedHit` -> `ApplyHitstun(...,
  guardHolds: true)`), and when the blocked move strings on the dash OUT of that stun is open -
  but only the direction the move's `comesFrom` evades (or straight back against a sideways
  strike), and only inside the dash's i-frame HEAD, so the dodge-out is TIMED against the
  follow-up, not mashed at the opener. Play-verified (Salaadin L->M vs Richard): full bar =
  guard holds the whole string, exactly 5+20 stamina, 2 chip; 12 stamina = the 20-cost
  follow-up BREAKS the guard (full 6 dmg, bar emptied, stunned guard-down); a back-dash 8 f
  into the block-stun evades the follow-up outright, a sideways dash toward the blade eats it.
  Testing note: `InputManager.SetAiInput` is a ONE-FRAME snapshot - feed `blockHeld` EVERY
  frame including settle waits, or each re-raise opens a fresh parry window and the opener gets
  PARRIED (the tell: the defender pays exactly his parry cost, 8, and the string never links).
- **`EndMatchToMenu` stops the count-in and `ClearBeat()`s** (else "FIGHT" stayed over the main
  menu), and it + `Unstage()` re-run `ApplySides` - the select screens wake the WHOLE roster, and
  leaving them any other way stacked the extras on the two marks, capsules climbing (the floating
  bystanders).
- **Reach is honest as of 2026-10-26** (the user's call: "the hit collider barely on the tip of
  each weapon, player collider tight and precise"). Four generosities died together, and they only
  work as a SET - loosen one and the contact timings stop matching:
  `BladeTipExtend` 0.15 -> **0.02**; `sweepRadius` 0.35 -> **0.12** (a per-INSTANCE serialized
  field, written by `SetupWeapon` from `BladeSweepRadius`); capsules 0.600 world -> **0.13 x
  stature** (`CapsuleRadiusFrac`, 0.33-0.38 world - honest torso half-widths are 0.31-0.52, and
  vertex maxima lie under cloth: Genghis's cape reads 1.20); `ATTACK_LUNGE_STOP` 1.7 -> **1.35**
  (1.7 was calibrated against the old collider fat; real blades pass 0.2-0.5 clear from there).
  The front-box fallback (kicks / slow frames / range-judged heights) is CLAMPED by
  `HitDetector.MaxTipRadius` - the tip's FORWARD-PROJECTED reach this swing actually achieved
  (radial is wrong: a backswing carries Richard's tip 2.7 sideways and authorizes far hits).
  Under the CLI editor every frame is "slow", so every pre-clamp CLI hit test was box theatre.
  All 18 normals play-verified landing at 2.6 (blade dive inside the armed window); far heavies
  whiff. **A whiffing move after a re-point is contact timing**: the fat colliders absorbed late
  contacts for years - but check the dive frame against the ARMED window first; a dive after the
  window is the return-to-guard follow-through, not the blow (re-timing Richard L/M on it was an
  overcorrection, reverted). All six parry ripostes and all six back-dash dodge ripostes land
  from ~1.5: Buwaka_Riposte_Dash plays his own Richard_Thrust clip now ({17.5, 1.2} - the old
  sword-cut clip swung the spear so its leaf head, the only hitting part, never came within
  0.99), and Richard_Riposte_Dash carries LungeOverride (1.1, 0.9) (it sat exactly on the 0.20
  trigger boundary and flip-flopped).
- **All six body materials are two-sided** (`_Cull 0`, written by `BuildMaterial(doubleSided:
  true)`; weapons stay back-culled). Richard's "transparent cap/cape" was single-sided cloth
  showing its backfaces - the mesh was fine. Photographed solid front and back.
- **AI vs AI needs a second AiController past the singleton**: null the static `Instance` backing
  (reflection), `AddComponent<AiController>`, `Activate(d, 1)`, and restore `Instance` +
  `Destroy` the extra afterwards - its Awake only kills the duplicate while `Instance` is set.
  Works: Genghis vs Buwaka fought to a KO and the record auto-saved. `GameMenu.Show` is
  `(Screen s, bool push)` - a one-arg reflective Invoke throws and silently kills the coroutine.
- **`WeaponAim` and `TwoHandGrip` aimed every blade at a BENCHED BYSTANDER** (found 2026-10-26,
  and it was most of "none of Buwaka's strikes land" + "Miyamoto barely lands point-blank"): both
  cached `opponent` as the FIRST other Fighter in the scene, once, at a time when all six are
  active - so after `ApplySides` every swing was aimed at Bjorn standing on his spawn mark across
  the arena. Same family as the `FindOpponent`/`FightCamera` bugs. Both now read `me.Opponent`
  (which `ApplySides` refreshes) every frame. The tell: a move whose mount, clip, contact and box
  are all verified still whiffs by ~1 unit, and the whiff DIRECTION depends on where a benched
  fighter happens to stand.
- **Blade sizes eased on the user's call (2026-10-26)**: `sweepRadius` 0.12 -> 0.16 and
  `BladeTipExtend` 0.02 -> 0.05 (point-blank strikes read too strict after the honest-reach cut);
  capsules unchanged. Still under half the old fat 0.35/0.15. **Eased again the same day** -
  "even miyamoto attack barely lands even when they are standing as close as they can" - to
  sweep 0.20 / tip extend 0.08, stamped straight onto the six scene detectors and blade boxes
  (the constants only reach the scene on a Rebuild) and saved.
- **The whole roster has a MEASURED distance band per move** (2026-10-26, the sweep lives in the
  session scratchpad pattern - see Sessions.md): every normal, special and authored string of all
  six fighters thrown at 0.8 -> 6.5 in a live match against a standard defender (Richard; Bjorn
  for Richard's own moves), landing judged off the defender's health. After the second easing
  EVERY move in the game lands from 0.8 (point-blank is fixed); per-fighter max landing
  distances: Miyamoto 2.8-3.6, Bjorn 2.6-3.6, Genghis 3.0-4.0, Salaadin 3.4-4.0, Buwaka 3.6-4.6,
  Richard 3.8-4.8. Strings all convert in full at close range; at an opener's far edge the
  knockback can carry the defender out of the follow-up's re-lunge, which the user ruled FINE
  ("its fine if the first or second attack of the string miss, if he started 2 far").
- **SP+Heavy steps further** (the user's call, 2026-10-26): `LungeOverride` entries (1.35, 1.5)
  for BloodEagle / DoubleHigh / HattinCleave / WrathOfTengri, play-verified max landing 3.6 /
  4.8 / 3.8 / 4.0. Two cannot gain from the table: Buwaka's GreatImpale (already (2.2, 2.0),
  speed-capped at 1.83, roster-longest at 4.6) and **Miyamoto's Kuruma - his 8-frame startup
  caps the lunge at 4 u/s x 0.17 s = 0.67**, so his SP+Heavy (3.0) lands SHORTER than his Heavy
  (3.6) and only more startup frames can change that (open decision, the user tuned those frames
  by hand). A lunge bump is also NOT 1:1 at the far edge: Bjorn's covered 1.50 of travel is
  play-verified, but his axe tip only projects ~1.75 past his root (`HitDetector.MaxTipRadius`),
  so reach = lunge + tip + sweep + half-capsule and the last 0.2 is contact-timing noise.
- **Miyamoto's specials array holds TWO SP+Heavy entries** - Kuruma (3rd, hyper armour) and
  Iaijutsu (4th). `FindAttack` returns the first match, so a raw SP+Heavy always throws Kuruma
  and Iaijutsu is reachable ONLY as the authored string ender, which is the intent - but know
  that reordering the scene array would silently swap them.
- **An authored SPECIAL -> SPECIAL string link can never fire**: `TryCancelIntoFollowUp` allows
  one special per string, counting the opener. `Buwaka_Special_FallingSpear ->
  Buwaka_Special_SerpentSweep` (CrucibleCombos) was dead in every context - the string stopped
  at 2 hits. **Fixed with a COMBO-ONLY ENDER** (the user's call, 2026-10-26, preferring "a
  special animation for his last attack which is only in that combo"):
  `Buwaka_Ender_SerpentCoil` ("Serpent Coil", trigger `ComboEnder`) is Medium-classed so the
  one-special rule never sees it, lives in NO library so it cannot be thrown raw, and is dialed
  by a plain Light press off Leopard Wheel (asset still `Buwaka_Special_FallingSpear`). Its clip is the last unused barbarian strike,
  AxePack `standing melee combo attack ver. 1` (233 deg advancing left sweep; measured
  in-editor: 140 f @30, main swing 47-66, peak 59 - the trims entry was authored from that and
  the contact is `{59.0, 1.2}`). The `_Ender_` name is now a recognized class in
  `CrucibleSpecialStates` (state + trigger + Any State transition get wired like a riposte's).
  Play-verified: both opener variants land 3/3 with Serpent Coil as the third hit. It does NOT
  appear on the Moves screen (that lists the libraries) - a hidden finisher, by design.
- **Bjorn and Miyamoto have NEW BODIES on Salaadin's exact rig** (2026-10-26, the user's two
  downloaded models: `fantasy+barbarian+3d+model` and `samurai warrior 3d model` in `~/Downloads`,
  both UNRIGGED Tripo meshes). Built by `ArtSource/Blender/rebody.py` - the OPPOSITE of
  `fit_metarig.py`: the rig (imported untouched from `Salaadin_Body.fbx`, all 79 bones) stays
  identical across fighters and the MESH is fitted to it - grounded, scaled to the donor body's
  crown, arm-warped onto the rig's rest when the pose differs (the barbarian was A-posed 41 deg
  down; the samurai matched to 6 deg), weights transferred from Salaadin's skinned body, and the
  old body's weapon re-seated on the new hand with its bone-local placement (a bone-parent frame
  measures from the TAIL). **The transferred weights MELTED THE ARMS in play** (the user's
  report) - nearest-face interpolation across a 5k-vert donor in a different costume smears the
  elbow gradients - so both were RE-SKINNED with bone heat (`reskin_heat.py`, same rig, mesh's
  own topology; Bjorn solved 123k/123k). Heat fails on detached accessory shells, and filling
  those from the transfer snapshot bound Miyamoto's tsuba to a THUMB - it floated beside him in
  idle. `repair_weights.py` is the cure: any vert whose dominant bone's segment is further than
  0.22 from it is mis-bound; re-bind from the nearest well-bound neighbour (266 on Miyamoto,
  107 of them on thumbs). Heat is also NOT idempotent on its own output - a second pass on
  Miyamoto's re-exported FBX failed 5318/5318 where the first solved 89% - so repair, don't
  re-heat. Textures swapped in
  place in each `.fbm` (Miyamoto's old normal/mask DELETED - old UVs; Bjorn's mask packed from
  the download's metallic+roughness by `pack_mask.py` pattern: RGB=metallic, A=1-roughness).
  Avatars verified 52 mapped / 30-30 fingers / Head on Head. Sizes are the user's call: Miyamoto
  = Salaadin's stature (2.80, measured 2.86), Bjorn +10cm (2.95 exactly); StandingHeight
  3.682 / 3.815, GroundOffset 0.153/4 and 0.104/3, soles verified +0.05 / +0.007. Old bodies in
  `ArtSource/Characters/_backup_20261026/`. A desert SPAWN can sit on a terrain ridge, so a
  sole-gap read at the marks can show +0.27 of honest ground - re-measure on flat before
  touching GroundOffset.
- **Buwaka holds his OWN tribal shield now** (2026-10-26): the "shield" in his Downloads folder
  was a re-download of SALAADIN's (byte-identical basecolor, same 5,266-vert mesh - that is why
  he appeared to carry Salaadin's shield), and the real one existed only as the render
  `Ornate Tribal Shield with Fur and Bronze Boss.png`. Converted via the **Tripo REST API
  directly** (the Studio site AND the tripo-ai MCP were down; the API key lives in the MCP env
  in `~/.claude.json`, and `/v2/openapi/upload` -> `task` image_to_model -> poll works fine).
  `ArtSource/Blender/swap_shield.py` seats a new disc INTO the old shield object's local frame
  (thin axis to thin axis, boss side to boss side, diameter matched) and swaps the MESH DATA,
  so object, bone parent, mount and material slot survive; the basecolor overwrites
  `Weapons/Buwaka_Shield.fbm/Buwaka_Shield_basecolor.jpg` in place (the GUID
  `Buwaka_Shield.mat` points at). **A Tripo glb carries Color/NormalGL/ORM images and the
  biggest-packed-image fallback grabs the NORMAL MAP** - the shield rendered uniform blue; pick
  the image by mean pixel colour (normal maps sit at ~(0.5,0.5,1.0)). Old body FBX + texture in
  `_backup_20261026/`. Play-verified front and back.
- **Mirror matches are allowed (2026-10-26)**: both players may pick the same fighter. The scene
  still holds one rig per character, so `GameMenu.EnsureTwin` CLONES the rig for Player 2 (born
  inactive - cloned from the source while it is switched off - so its Awake/Start run only once
  it is Player 2) and `MirrorSkin.Apply` swaps the clone's body `_BaseMap` for
  `Resources/MirrorSkins/<Name>_P2` (the user's palette: Richard royal blue, Miyamoto crimson
  lacquer, Salaadin black, Genghis sky-blue deel, Bjorn charcoal leather + white fur + blue,
  Buwaka black pelt + blue beads), generated by `ArtSource/Textures/make_mirror_skins.py`
  (soft HSV masks gated off skin; gitignored output, like the bodies). The twin is NOT in
  `roster` (the ring counts characters off it): everything that walks the fighters IN PLAY
  uses `Everyone()`, and `P1Fighter`/`P2Fighter` resolve who is fighting. Replacing a twin
  deactivates it BEFORE `Destroy` - Destroy lands at end of frame and `RefreshOpponent` would
  otherwise pick the dying Player 2. Salaadin's and Buwaka's mirrors are the subtlest reads.
  **A body's new basecolor means re-running `make_mirror_skins.py`**: the P2 texture is a
  recolour of the body's OWN atlas, so after the 2026-10-27 HD bodies (new UVs) five twins wore
  an old-layout texture - the "weird texture and colours". Regenerate after any body swap, and
  re-check the masks against the new texture (Buwaka's bead mask had to narrow to true red:
  his HD skin is a dark saturated red-brown the lit-skin gate misses).
- **Every body wears the old Tripo GLAZE (2026-10-26, the user's call)**: the look the old
  Bjorn/Miyamoto FBX had was a CONTRAST, measured off their masks - skin smoothness 0.62-0.65,
  steel metal 0.86 / smooth 0.72, cloth and leather ~0.15 - with Smoothness 0.75 and a normal
  map. The new bodies were a flat 0.4 with no normal map. The old maps cannot be reused (old
  UVs) and only Bjorn's download had PBR maps, so `ArtSource/Textures/make_body_maps.py`
  DERIVES `<name>_mask.png` (classify basecolor texels, write the old values, x0.75 baked in)
  and `<name>_normal.png` (high-passed luminance; Bjorn keeps his real one) into each
  `<Name>_Body.fbm`, and `CrucibleCharacterSetup.BuildMaterial` picks them up. Bjorn's
  download mask is backed up in `ArtSource/Characters/_backup_20261026/body_masks/`.
  The WEAPONS got the same (`make_body_maps.py --weapons`, skin class off - warm wood and bronze
  sit in the skin band): every weapon `.fbm` without a mask gets derived maps; Bjorn's axe and
  Miyamoto's katana have REAL ones and are skipped. Weapon maps are committed (the Weapons
  folders are tracked). The user's re-exports from Tripo had no PBR maps either: these are
  SMART MESHES, and only HD models carry PBR. Real maps would mean baking an HD version of the
  SAME generation onto the smart mesh's UVs - do not regenerate, that is a new mesh and UVs.
- **Nobody kicks (2026-10-27, the user's call).** The six boot ripostes are weapon strikes now,
  each a pack clip on its own per-STATE contact: Bjorn parry = AxePack2H `standing melee attack
  downward` (chop), Miyamoto dash = `great sword slash (3)`, Miyamoto parry = `great sword
  slash (2)` (the pack's unused 498 deg spin), Richard parry = `sword and shield slash (3)`,
  Genghis dash = `great sword slash (3)`, Buwaka parry = `Richard_Thrust`. All twelve ripostes
  are `hitsWithWeapon` and play-verified landing from 1.5 to 3.3. The body-box path for
  `hitsWithWeapon = false` is still in `CombatSystem`, unused. Written straight into the
  `.asset`s, NOT through `<Name> > 1 Create move set`: that menu calls
  `CrucibleBalance.Apply()`, which would overwrite the user's hand-edited frames and damage
  (Miyamoto/Genghis frames; Richard's L/H damage 8/13 and Medium startup 13, 2026-10-27) with the
  table's numbers. **Since resolved**: those numbers were copied into the table the same day, and
  a table-vs-live diff over every field `Apply` writes reads "no differences". If the user tunes
  numbers in the editor again, copy them in before running `Apply` or any `Create move set`.
- **Bjorn's SP+Light (Haft Strike) is a FRONT CUT** - AxePack `standing melee combo attack
  ver. 3` (his dash riposte's clip, own contact `{28.6, 1.6}`), AimMode Level, LungeOverride
  (1.35, 1.5). The backhand it replaced met the target mid-turn with the axe wrapped round his
  body (photographed: back to camera), so its tip only reached 1.3 forward and it landed to 2.4;
  the front cut reaches 2.0 and lands from 3.4. It is CutLeft like his Light now, so a RAW
  L -> SP+L inside combo pressure is refused by the flow rule (the authored string still links).
- **`HitVolumeScale` in `CrucibleCharacterSetup`** scales a fighter's blade box (thickness,
  width, and length toward the tip) and sweep radius together: Bjorn 1.15 (sweep 0.23).
- **Slow-game TEST settings (2026-10-27, the user's call, "we will be testing few things in editor")** -
  four constants, each revertible by setting it to 1 and re-running `Apply the roster balance` +
  `Use Mixamo packs` + `Wire combo strings`: `CrucibleBalance.AttackFrameScale` 2 (every move's
  startup and recovery x2; active, hitstun and parry untouched), `DashTimeScale` 2 (dash duration and
  i-frames x2, same distance; the run keeps its old pace via the new `Fighter.runSpeed`, written as
  the table's dash speed), `WalkSpeedScale` 0.5 (walk 1.6-2.4 u/s) plus
  `BalanceConfig.BACK_WALK_SPEED_MULTIPLIER` 0.85. The table keeps the ORIGINAL numbers.
  `ComputeTrims` measures a contact window against the table's budget, so a stretched move plays
  the same clip segment SLOWER (attack states now x0.5-1.3, most were 1.4-3.8). Walk states play
  at ground speed / `CrucibleMixamoSetup.WalkStride` (each walk's measured ground speed at x1:
  planted-foot speed with the walk playing in place, 1.0-1.75 u/s forward), clamped 0.8-2.0 - the
  AxePack2H walk (Bjorn/Miyamoto/Genghis) has a short stride and hits the cap, so those three still
  slide a little. **Ripostes are gone**: `dashRiposte`/`parryRiposte` are null on all six (and the
  move-set scripts write null); a counter is any normal/special/combo thrown in the window, with the
  counter bonus. The `*_Riposte_*` assets and animator states stay, unused.
- **The test settings were tuned up after testing (2026-10-27, the user's calls)**: walk and dash
  +20% (`WalkSpeedScale` 0.6, `DashTimeScale` 2/1.2 - i-frames shrink with it; the run is
  unchanged), per-fighter attack speed `CrucibleBalance.CharacterSpeedUp` (Bjorn 1.05, Richard
  and Genghis 1.185 - divides startup and recovery, the `UserFrames` too, so clips play that much
  faster), the guard +20% (`Crucible > Animation > Set the block speed`, `CrucibleBlockSpeed`:
  raise/lower/impact blends and Block_Impact x1.2, written from base numbers so it never
  compounds), the FIRST swing in a parry or dodge counter window at x1.25 pace
  (`BalanceConfig.COUNTER_ATTACK_PACE_MULTIPLIER`, clock and animation together), and the
  Settings game speed now runs 50-200% (`GameMenu.MaxGameSpeed`). Walk states for
  Bjorn/Miyamoto/Genghis now sit on the 2.0 anim cap, so their feet slide a little more.
- **Size order (the user's, 2026-10-27)**: smallest to largest Miyamoto 2.82, Salaadin 2.90, Buwaka
  2.98, Bjorn 3.06, Genghis 3.14, Richard 3.22 (even 0.08 steps; `Stature` table). Measured live
  statures wander +-0.05 with the idle, so judge the order across runs, not one reading.
- **Online is a LOBBY on a relay this laptop runs (2026-10-28)**, not a link. `Server/start-online.cmd`
  starts `relay.js` + a Cloudflare quick tunnel and `online.js` writes the tunnel's
  `wss://.../ws` into the public gist `crucible-server.json` (created by `gh` on the FIRST run - the
  user's own run, gist creation from here is refused). The game finds it via
  `api.github.com/users/mmamdouhmma-cmd/gists` -> the file's revision-pinned `raw_url`, then falls
  back to `ws://localhost:8080/ws`. Server down -> "Can't Reach the Server" / Try again / Main menu.
  Host = name + optional 6-digit code; Join = live room list ("needs a code" / "open"). Relay protocol
  is tab-separated (`HOST code\tname`, `LIST` -> `ROOMS`, `JOIN id\tcode` -> `JOINED/NOROOM/FULL/
  BADCODE`, `PEER_JOINED/PEER_LEFT`); peer messages `C` pick, `R p1 p2 arena speed regen` start,
  `O speed regen` rules, `P` pause, `H` heartbeat, `I` guest input, `S` host state (first field =
  host `Time.timeScale`, which the guest copies).
  - **Both players wait on the SPLIT CHARACTER RING, like local 2P** (2026-10-28, the user's call):
    "Open the game" (host setup now also holds the speed and regen rows) and a successful join go
    straight to `CharSelect` with `netRole` set - host = left half, guest = right. Each side drives
    its own half with `MenuNavigate/Confirm/Back` and sends `X cursor locked`; the other half follows
    `PeerCharacter`/`PeerLocked` (`GameMenu.UpdateOnlineSelect`). Both locked -> merge -> arena select,
    which ONLY the host steers (`A arena`; the guest's input is ignored), then `F` -> both to Vs, and
    the host's `StartMatch` sends `R`. The old `OnlineHost`/`OnlineGuest` room screens are unused.
    Back on the select leaves the room; a peer leaving the select keeps the host's room open.
  - **The K.O. card is a VOTE** (`V 1` rematch / `V 2` change fighters), each player's choice shown
    on both cards; the host settles it once both are in: both rematch -> rematch, anything else ->
    `G` and both back to the ring. "Leave the session" closes it for both. `matchSerial` drops a
    K.O. card still on its 1.9 s delay when the host has already moved everyone on.
  - **The guest runs no combat, so the host FORWARDS the presentation** (`E` lines from the same
    choke points `ReplayRecorder` hooks: HitEffects spawns, `CrucibleAudio.Shot` minus UI sets,
    `FightCamera.OnImpact/OnClash`, `KillShot.Begin`), and the `S` frame carries six HUD fields per
    fighter (parry/clash/heal flash, heal amount, both counter windows) applied by
    `Fighter.ApplyNetHud`. `NetSession.MuteForward` wraps effects the guest makes for itself.
  - **Host rules**: game speed 50-200% and "Health regen per second" on/off, set in the room, shown
    read-only to the guest. `Fighter.PassiveRegenOn` (static) gates ONLY the passive hp/s - the
    dodge/parry/counter heals always stay on (the user's call, 2026-10-28). Offline the same toggle
    is in Settings (`SettingsData.healthRegen`, saved, applied by `ApplySettings`, hidden online).
  - **Pauses**: 2 per player per fight, 30 s each, host-authoritative (`HostPause/HostResume`, the
    guest only requests); only the pauser resumes early, the host's clock auto-resumes at 30 s.
  - **Disconnects**: `H` every 1 s; nothing heard for `NetSession.PeerTimeout` (8 s, a CONST - it
    was a serialized field and the scene silently kept 6) = gone. Peer left/lost mid-fight -> back to
    the Online lobby, "the fight is yours". Relay dies -> the failure page, Try again reconnects.
  - **`GameMenu.SetAi(false)` re-asserts `SetAi(2,true)` for an online host** - the count-in's
    deactivate at FIGHT was wiping the guest's input slot, so the guest could never move.
  - All verified 2026-10-28 with `peer2.js` (scratchpad, a scripted opponent) on both roles, plus one
    run through a real trycloudflare tunnel.
- **Updates**: `GameUpdater` (Windows player only) compares `Application.version` with the latest
  GitHub release tag `v<ver>` on `mmamdouhmma-cmd/crucible-honorable-souls`, downloads `*-Win64.zip`
  and hands off to a batch file that waits for the game to exit, copies over the install and
  relaunches. Release = bump Player Settings > Version, `Crucible > Build > Windows app`,
  `Tools/release.cmd` (Windows tar.exe for the zip - Git Bash's tar writes a TAR). The server window
  says SERVER IS ONLINE when published; stop it by typing `stop` + Enter, Ctrl+C, or closing it. The first public
  build has to be downloaded by hand; 0.1.0 builds have no updater.
- **Every fight starts from GAME SETTINGS (`Screen.MatchSetup`, 2026-10-30)**: speed, regen, Duel time
  (1-15 min) and Damage / Health / Stamina 50-200% in 5s, held in `Scripts/core/DuelRules.cs` (`DuelRules.Active`,
  saved in `SettingsData.duel`, carried in the online `R` and `O` lines). Practice skips it and always
  runs on default rules. The clock runs on unscaled time on the host or local game; the guest follows
  the `S` frame. At time-up the bigger share of health left wins, equal shares draw (`EndOnTime`, online
  line `T`). `ShowMatchOver(null, ...)` is a draw. `StartMatch` must set `DuelRules.Active` BEFORE
  `PlaceFighters`, which builds the bars.
- **A dead fighter cannot land a blow**: `Fighter.NotifyDeath` calls `CombatSystem.StopForMatchEnd`,
  `ResolveHit` returns if either side is dead, and the time-up does the same to both fighters.
- **Phone menus are their own layout (`GameMenu.BigRows`)**: rows 88 tall (62 on dense lists) in a
  `RectMask2D`, finger-scrolled through `SetScrollPosition` (`scroll` stays the whole-row index, `scrollFrac`
  the slide), with a scroll bar and a fixed top-left `< BACK` (`LayoutTouchBack`). Rows use `MenuTap`, not
  Button. An adjustable row draws fixed `-` / `+` caps (`<` / `>` for choices); a press past `ValueColumn`
  steps it, and `Item.repeats` rows repeat while held. **Set `repeats` on numbers only**, a held toggle
  flip-flops. Character and arena select have `SelectTouch`: arrows, swipe, SELECT, BACK, feeding
  `InputManager.PushMenuTouch`, so a tap is the same press as Cross. Fields open `TouchScreenKeyboard`.
- **The touch fight pad** (`TouchControls`): walk + Special on one side; the four PlayStation face buttons
  (Triangle Heavy, Cross Medium, Square Light, Circle Parry/Block) with the 8 dashes above on the other.
  The guard button is a hold: raising it opens the parry window. Sizes are `SetSizes` from Settings, and a
  column that would hit the health bars shrinks to fit. Button art is generated from distance functions.
- **Test a touch control at the place it is DRAWN.** `EventSystem.RaycastAll` at the glyph's world centre
  plus `ExecuteEvents` down/up/click. A test that tapped by fraction of row width passed while the drawn
  `<` actually raised the value.
- Known gaps carried forward: the AI cannot parry, throw a special or dial a string on purpose;
  the web build is uncompressed on purpose; hosting means the laptop is the server (relay +
  quick tunnel, new URL each restart); `PlayProbe`, `BladeTrace`
  and `InputHold` are debug-only components still in the scene (`DebugTimeScale` is wanted —
  the Settings speed slider drives it).

- **The Desert Arena has a world round it (2026-10-29)**: `Crucible > Arena > Desert > Build the
  surroundings` (`Editor/CrucibleDesertUpgrade.cs`, idempotent, same seeds every run) generates
  everything into `Environment/DesertSurroundings/` and the root `DesertArena_Surroundings`, which is
  the Desert entry's `sharedRoot` and carries an `ArenaAtmosphere` (low SW sun, Desert_Sky, haze
  80-900, `Desert_Look` = CrucibleLook cloned a touch cooler). Three landscape rings (one sand sheet
  r12-330 conformed under the arena by raycast, terraced sandstone ridges r225-530, mountains
  r450-860), each sinking under the next, sand/sandstone split by slope into two submeshes; rocks and
  shrubs merged into one mesh each. The arena mesh, bounds, marks and ring-out are untouched.
  **Two palms stand INSIDE** (`DesertArena/Palms_Inside`, at (18.2,-4.9) and (-14,-8.9)): trunk
  MeshColliders (so WallCutout sees through them) PLUS `keepOut` circles r 0.55 on ArenaBounds,
  wider than the trunk so the keep-out slides a fighter round it before `BlockObstacles` (which
  refuses moves outright) ever touches the collider - that is what stops the AI walking into one and
  sticking. Moving a palm means editing `InsidePalms` and re-running; the build replaces its own
  keep-outs and leaves the others. Pre-upgrade scene: `ArtSource/SceneBackups/`.
- **The Medieval Fortress has its own sky and light (2026-10-29)**: `Crucible > Arena > Fortress > Build the sky and
  lighting` (`Editor/CrucibleFortressLook.cs`) - lighting/atmosphere ONLY, the arena is untouched. Root
  `MedievalFortress_Atmosphere` (the Fortress entry's sharedRoot, ArenaAtmosphere): GOLDEN-HOUR sun from the WSW (30 deg - lower and the walls shade the floor),
  sky fill, Trilight, a generated cloudy panorama + reflection cubemap, light haze, `Fortress_Look` grade. No lightmaps
  - six arenas share one scene and one bake. Assets in `Environment/FortressLook/`.
- **The Forest Clearing sits inside a forest now (2026-10-29)**: `Crucible > Arena > Forest > Build the surroundings`
  (`Editor/CrucibleForestUpgrade.cs`, idempotent, same seeds) -> root `ForestClearing_Surroundings` (sharedRoot +
  ArenaAtmosphere, golden late afternoon). Floor + a rising canopy sheet close the horizon; trees, boulders, ferns,
  bushes, logs, mushrooms are merged per group, no colliders, all outside r 32 (the fight circle is 16). The desert
  script's mesh/texture helpers (`MeshBuilder`, `Rock`, `SaveTex/SaveMesh/Mat(dir)`, `TFbm`) are `internal` and shared.
- **Dojo Courtyard and Village Plaza SHIP in the game (v0.3.0)**: in testarena as `Musashi_Dojo` /
  `Musashi_Plaza` (arena indices 3 and 4) sharing the root `Musashi_Village`
  (`ArenaDirectory.Entry.sharedRoot`), whose `ArenaAtmosphere` brings the village's own sky, light,
  fog and grade and gives testarena's back on the way out. They are put there by
  `Crucible > Story > Musashi Chapter One > Add the two arenas to the game (testarena)` - re-run it
  after rebuilding the story scene. New arenas are APPENDED: the online protocol sends arena indices.
  Online peers exchange `W <version>` and refuse a different (or silent, pre-0.3.0) version.
- **The Musashi Chapter One arena is a PLAYABLE TEST SCENE** (`Scenes/Story/Musashi_Chapter01_Arena.unity`):
  Bjorn, Miyamoto and Salaadin, opening on One Player / Two Players (`GameMenu.openOnBattleSetup`), with
  two arenas in its ArenaDirectory, `Musashi_Dojo` and `Musashi_Plaza`. These are fight-space ROOTS:
  ArenaBounds plus marks plus camera home, all sharing one `Environment`. Regenerate with
  `Crucible > Story > Musashi Chapter One > Build the arena scene` (it rebuilds only Environment /
  Lighting / Cameras / the two fight roots and keeps the game), then `Bake the sky lighting`.
  `Add the game` moves the runtime in from testarena without saving testarena. The game objects in
  this scene are COPIES: a change made to them in testarena does not reach it.
  - ArenaBounds can carry `scenery` (the meshes WallCutout sees through and occludes with: the
    non-trigger MeshColliders under it), `markP1/P2` and `cameraHome`. All are optional; empty = the
    old one-mesh behaviour.
  - Environment meshes go through `ArtSource/Blender/prep_env_lod.py` (`_LODn` / `_COL` / `_SHD`).
    A hero mesh over ~500k triangles needs a `_SHD` shadow proxy, and distant repeated scenery should
    render one cheap LOD rather than sit in a LODGroup (a big mesh stays on a high LOD for hundreds of
    units).
  - Do not level a fight zone with `plateau` on these Tripo terrains: it stretched a bank into a
    smear. Raycast grounding copes with the plaza's +-0.7.

- **The Windows look has its own render settings; the WEB build does not share them (2026-10-29).**
  `Crucible > Look > Visual quality pass` (`Editor/CrucibleVisualQuality.cs`, idempotent) split the web
  build onto `Web_RPAsset` / `Web_Renderer` (copies of PC_* as they stood: 120 m / 2048 shadows, no MSAA,
  SSAO 0.4), assigned to the "Mobile" quality level, which is WebGL's default. PC_RPAsset (Standalone's
  "PC" level) is now shadows **45 m @ 4096**, 4 cascades, **MSAA 4x** (SMAA kept on the camera), SSAO
  0.6. Every arena Look profile keeps ACES with grain 0.05, chromatic aberration OFF, saturation 0
  (Forest -3) and contrast eased (base 5, Desert 4, Fortress 8, Forest 5, Musashi 6); the arena build
  scripts write the same numbers, so a rebuild keeps them. Anything that reassigns pipeline assets
  (`CrucibleUrpLook`) must be followed by the pass again.
- **Fighter and weapon textures carry a STANDALONE override**: 4096 BC7 for colour, 4096 BC5 for normals,
  2048 BC7 for the masks; the default (web) settings are unchanged (2048, normal quality).
  `CrucibleCharacterSetup.DesktopTextureQuality` applies it inside `BuildMaterial`, and
  `Crucible > Characters > Desktop texture quality` sweeps every `*.fbm` under Characters (skipping the
  unused `tripo_convert_*` folders). The Model folders are gitignored, so a fresh clone runs that menu.
  No AO maps exist anywhere - the Tripo smart meshes ship none.
- **Every arena root carries a `ReflectionProbe` + `ArenaReflectionProbe`**: one 256 HDR cube captured
  ON ENABLE (after ArenaAtmosphere sets the sky), as a synchronous `RenderToCubemap` with all fighter
  renderers `forceRenderingOff` for that one call, set as the probe's custom texture. Baking is wrong here:
  six arenas share one scene and their skies only exist while active. The capture point raycasts to the
  floor under it (+2.5) - arena roots do not sit on their floors.
- **Measuring frame cost from the CLI**: render `Camera.main` into a 1920x1080 RT N times, each followed by
  a 1-pixel `AsyncGPUReadback...WaitForCompletion()`, and time it with a Stopwatch - the Editor's own
  frame rate is meaningless unfocused. On this AMD iGPU a 1080p match frame costs 30-60 ms either way;
  take the MINIMUM over runs, medians swing +-10 ms. **An unfocused Editor freezes play at frame 1 until
  `set_autotick` is on**, and a domain reload turns a non-persisted autotick off again.

- **The story SENSEI is a seventh fighter (2026-10-29)**: `Editor/CrucibleSensei.cs`, controller `P7_Sensei`,
  `sensei_data`, attacks under `Characters/Ch1_Sensei/Attacks/`. He is IN THE GAME as **Player7** in testarena
  (v0.4.0; the user: a planned DLC, in the roster for testing) - appended LAST, since the roster sorts by
  `playerNumber` and online sends roster indices - and on Player2 of `Scenes/Story/Ch1_Test_Sensei.unity`.
  `Crucible > Characters > Sensei > 4 Add to the game` rebuilds Player7; `Rebuild Player7 (Sensei body)` exists.
  Musashi and the Partner are NOT in the roster (the user's call). Every strike is borrowed from the roster (the user's picks; table in
  `Design/Story_Ch1_Characters.md`) and copies the source move's contact AND hit geometry. Two mechanisms came with him:
  `TwoHandGrip.onlyInStates` (off hand on the hilt only in listed states; empty = always) and
  `CrucibleBalance.Vitals.storyScene` (in that scene the fighter is written to Player2, and the roster's Player2 is skipped).
  `CrucibleMixamoSetup.Wire` resolves each controller's fighter by the controller itself, not by `Player<N>`.
  **Keep every clip map in the one `ImportAll` call** - leaving a map out rewrites the shared pack importers
  without that fighter's takes.

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
