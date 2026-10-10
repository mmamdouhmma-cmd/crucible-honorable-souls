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
