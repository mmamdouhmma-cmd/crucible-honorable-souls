# CRUCIBLE: Honorable Souls

1v1 3D fighting game. Unity 6000.4.4f1 (URP), C#. Target input: PS4/PS5 controller (P2) and keyboard (P1).

## Layout

- `Assets/_project/Scripts/characters/` — `Fighter` (movement, dash, block, facing, hit reaction), `FighterStats`, `FighterData`.
- `Assets/_project/Scripts/combat/` — `CombatSystem` (attack phases, hitstun, hit resolution), `HitDetector` (trigger hitbox), `AttackData` (ScriptableObject per attack), `DamageCalculator`.
- `Assets/_project/Scripts/config/BalanceConfig.cs` — global tuning constants, `FRAME_TIME = 1/60`.
- `Assets/_project/Scripts/input/` — `InputManager` singleton on the `GameMangers` object.
- `Assets/_project/Scripts/UI/FightHud.cs`, `Assets/_project/Scripts/core/FightCamera.cs`.
- `Assets/_project/Data/bjorn Attacks/` — P1 (Bjorn) attack assets. `Assets/_project/models/characters/p2/*.asset` — P2 (Miyamoto) attack assets.
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

- `Assets/Character model/` (rigged FBX characters, ~716 MB) is not committed; it needs Git LFS or stays local. The scene references its prefabs.
- `Assets/_Recovery/` holds Unity crash-recovery scenes. Do not commit.
- `.plastic/` is Plastic SCM state. Leave it out of git commits.

## Session log

### 2026-10-07 — hitbox scale fix, hit sides, hitstun

- Found why attacks landed from far away: the fighters' scene scale (4x/3x) was inflating the Hitbox child and the body capsules, so a 1.5-unit attack reached 6 units. Hitboxes now ignore model scale and are in world units; gizmos match.
- Added `HitSide` enum + `hitSide` field on `AttackData`, authored per attack. Defaults set on the six existing attacks to mirror `comesFrom`. `Fighter.NotifyHit` sets `HitSide` int and `GetHit` trigger on the animator.
- Added `hitstunFrames` on `AttackData` (default 12) and a hitstun timer in `CombatSystem`. Hitstun blocks attacking, dashing and walking; a hit cancels an attack in progress unless `hitInterruptsAttack` is off.
- Cached the `Animator` on `Fighter` instead of looking it up every frame.
- Still to do in the editor: add `HitSide` int and `GetHit` trigger to both animator controllers with three get-hit states; tune `hitboxRange` per attack (likely 3–4 units for these models) and `hitstunFrames` to the flinch clip lengths; consider raising `startupFrames` so hits land when the swing visually connects.
