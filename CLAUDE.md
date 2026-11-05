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

## Session log and project notes

- **`Sessions.md`** (repo root) is the session log. **Append new entries to the bottom**, as
  `### YYYY-MM-DD — <title>`. It is for narrative and for the gotchas of a day.
- **`ProjectNotes.md`** (repo root) holds the durable facts: every per-fighter, rig, clip, combat,
  arena, online and build lesson that used to sit in this file (moved 2026-11-05, this file was at
  166k characters). **Search it before touching any of those systems**, and add new durable facts
  THERE, not here. Keep this file to the essentials.

## Where things stand (summary; details in ProjectNotes.md)

- **Seven fighters** in testarena, one rig each, `Player1..7`: Bjorn, Miyamoto, Richard, Salaadin,
  Genghis, Buwaka, and the story Sensei (Player7, a planned DLC, appended last - online sends roster
  indices). Each has a move table in `Editor/Crucible<Name>.cs` (Bjorn/Miyamoto: the `.asset`s) and
  the roster's numbers live in ONE table, `Editor/CrucibleBalance.cs`
  (`Crucible > Combat > Apply the roster balance`). Change a move by editing the table and
  re-running the menu item, never the `.asset` YAML - and call `AssetDatabase.SaveAssets()` after.
- **The menu benches whoever is not fighting** (`GameMenu.ApplySides`); anything that walks the
  roster must skip inactive fighters and read `Fighter.Opponent`, never "the first other Fighter".
- **Animation is the Mixamo packs** plus a few authored clips; `Use Mixamo packs` writes every
  state's motion and speed (`clip.length / (frames/60)`), with trims in `swing_trims.json` (by CLIP)
  and contacts in `swing_contacts.json` (by STATE). **Read the controller back after re-pointing a
  state**, and run the Unity steps through `eval`, not the menu bridge.
- **Every rig is a Rigify metarig with Mixamo names (79 bones)**; bodies and rigs are gitignored, so
  a fresh clone re-runs the rebuild chain in ProjectNotes.md.
- **Combat**: no fixed strings (free-form cancels gated by `CombatSystem.Flows`), combo pressure,
  the timed block raise is the parry, dash i-frames per fighter, hyper armour on every SP+Heavy,
  honest reach. Slow-game TEST settings are on (`AttackFrameScale` 2 etc.).
- **Front end, online lobby (laptop relay + Cloudflare tunnel), records/replay export, self-updating
  Windows/Android builds, touch controls** all ship. Arenas: Medieval Fortress, Desert Arena, Forest
  Clearing, Dojo Courtyard, Village Plaza (append-only, online sends indices).
- **The story (Musashi Chapter One) is Editor-only** until the whole story ships.
- **For any works-in-Editor-not-in-app report, read the player log first**:
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CRUCIBLE test\Player.log`.

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
