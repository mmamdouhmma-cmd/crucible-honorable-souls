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
- **Salaadin's rig is being re-rigged and the current one is known bad** - see
  `Design/Salaadin_Rerig.md`. Mixamo's auto-rigger put his elbow markers 41% of the way from
  shoulder to wrist instead of ~52%, so his upper arm is 8.0% of his height against Richard's
  14.6% and shorter than his own forearm, and his knee markers left him bound at 149/135 deg
  instead of a straight symmetric T-pose. The MESH is fine; this is a re-rig, not a re-model, and
  nothing downstream caused it - the raw Mixamo download measures the same as the game. The user
  is redoing the Mixamo pass; when `ArtSource/Characters/Mixamo/Salaadin_Rigged.fbx` is replaced,
  rebuild with `salaadin_body.py` -> `salaadin_clips.py` -> the Salaadin menu chain, and check the
  `MOUNT` offsets in `salaadin_body.py` (metres against a 1.04-tall rig, so new proportions can
  move the falchion and shield off the hand; the IK targets in `salaadin_clips.py` are fractions
  of limb reach and carry over by themselves).
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
