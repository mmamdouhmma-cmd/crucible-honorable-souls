# Chapter 1 Implementation Tracker: Miyamoto Musashi, "The Way of the Sword"

Audit date: 2026-11-03 (branch `salaadin`, after v0.4.9). Phase A (combat hooks) is DONE and play-tested; nothing story-facing is built yet.
Everything below was checked in the code, scenes and assets. Line references are as of this audit.

Status key:
- **State:** Existing / Partial / Missing
- **Need:** Required (for a playable Chapter 1) / Optional / Future
- **Decision:** Implement Now / Keep for Later / Awaiting Decision
- **Done:** not started / in progress / done (verified) / done (unverified)

## 0. Decisions (the user, 2026-11-03)

1. Approved: the shared-code hooks and a story match start with **no clock and no K.O. screen**. A knockdown means **kneel, then get back up**.
2. Lesson 2: back dodge + Special Light within ~1 s on the guard counts, with **no damage multiplier**. **The Sensei has NO health or stamina bar.** Only Musashi has bars, plus the Partner in Round 4 only.
3. Guard: "**block breaks after the 3rd attack**". Built as `Fighter.GuardBreaksAtStrike = 3`: the 3rd strike of a string breaks the Sensei's guard (see the open question below).
4. Short-recovery attack copies for the Sensei: yes, **training only**.
5. Knockdown: **kneel, then `crouch to standing idle`**.
6. **Arima Kihei = Kojiro's body** as a stand-in, to be swapped later.
7. The chapter lives in the story scene and is reached from the **main menu**.
8. Japanese font: **Noto Sans JP**, subset. Approved.
9. Exploration: **walk freely**, with dashes available as in the fight (keyboard: dash modifier; pad: right stick). The scope is an open question, below.
10. Partner rounds: a counter counts **as long as it was thrown inside the counter window** (the combat system's own window).

### Settled 2026-11-03 (second round)
- **Guard break:** strikes 1-2 of a string are blocked; the **3rd breaks the guard and LANDS** with full damage (`GuardBreaksAtStrike = 3`). Training health refills.
- **Free walking: the village walk only.** Every duel keeps the face-the-opponent movement.
- **Training dummy: a simple placeholder post** (primitives), to be swapped later.
- **Arima: a normal kill** (the normal killing blow, slow-motion and darkening), then the chapter-ending lines.

### Still open
- Round 3: does a hit Musashi BLOCKS count as a "hit received"? Default used: **no**, only clean hits count.

---

## 1. What exists and can be reused

| Area | What is there | Where |
|---|---|---|
| Story cast | Musashi and Partner are rigged, have wooden swords, use Miyamoto's controller and moves, and are play-verified. The Sensei is a full fighter with his own moves and is Player7 in the roster. Kojiro is rigged but not in Unity, and his animation is postponed. | `Design/Story_Ch1_Characters.md`, `Scenes/Story/Ch1_Test_{Musashi,Partner,Sensei}.unity`, `Editor/CrucibleSensei.cs`, `P7_Sensei.controller` |
| Village | One continuous environment: terrain with a MeshCollider, the dojo, houses, pines and lanterns. It has two fight spaces: **Dojo Courtyard** at the origin (r 9.5) and **Village Plaza** at (57, -10.5) (r 16), about 58 units apart over walkable ground. | `Scenes/Story/Musashi_Chapter01_Arena.unity`, `Editor/CrucibleMusashiArena.cs` |
| Combat | Attack phases, clash, dodge judgment by `comesFrom`, timed-raise parry, block with chip damage, block-cost growth on strings, guard break, hitstun, free-form combos with the flow rule, dash and parry counter windows, hyper armour, stamina weakening, whiff penalty. | `CombatSystem.cs`, `Fighter.cs`, `BalanceConfig.cs` |
| Sensei strikes the lessons need | **Overhead**: Mountain Splitter (Heavy, `comesFrom` Overhead). **Spin**: Circle of Willow (SP+Medium, `sword and shield attack (3)`, a ~396 deg spin). **Horizontal cuts** for the wrong-dodge punish: Falling Branch (Light), Temple Bell (Medium). | `Characters/Ch1_Sensei/Attacks/` |
| Special Light | `AttackData.IsSpecial` plus the Light input slot. Musashi's SP+Light is Miyamoto's. | `AttackData.cs:293` |
| AI | The `AiController` singleton has three difficulty profiles and per-behaviour `Rules` (walk / attack / block / dodge / punish), so sparring rounds can switch behaviours on and off. | `AiController.cs:27-45` |
| Scripted input | `InputManager.SetAi` / `SetAiInput(player, InputData)` gives a one-frame input snapshot per player. A story driver can hold the Sensei's guard through it. | `InputManager.cs:117-128` |
| Health / stamina | `FighterStats.Heal`, `RegenerateHealth`, `AddStamina`, `TakeDamage`. Passive regen is a global toggle (`Fighter.PassiveRegenOn`). | `FighterStats.cs` |
| HUD | Health and stamina bars, counter flashes, clock. | `FightHud.cs` |
| Save pattern | PlayerPrefs + JsonUtility (settings, bindings, arena). | `GameMenu.cs:973`, `ControlBindings.cs` |
| Practice reset | `PracticeMode` with infinite health/stamina and `ResetPositions`. | `PracticeMode.cs` |
| Cutscene tech (unused) | **Timeline 1.8.12** and **Cinemachine 3.1.6** are installed. Nothing in the project uses them yet. | `Packages/manifest.json` |
| Animations | Death falls (`two handed sword death`, `(2)`), impacts, crouches, `crouch to standing idle`, `sheath sword 1/2`, idles, walks, runs, taunts, plus every strike in four packs. | `Animation/Clips/Mixamo/` |

## 2. Conflicts with the existing combat system (decisions needed)

| # | Conflict | What the code does today | Recommendation |
|---|---|---|---|
| C1 | **A back dodge of an overhead gives NO counter** (Lesson 2) | `Fighter.ResolveDashAgainst` (`Fighter.cs:1517`): Overhead + back dash = `EvadedNoCounter`. The overhead misses, but no counter window opens, so there is no counter bonus or counter heal. | The story layer judges the lesson: back dash **evaded** the overhead (verified by the combat result) **and** a Special Light **thrown within ~1.0 s** of the dodge **contacts the guard**. The combat rule stays as it is, so the counter gets no damage bonus. The same rule would apply to back-dodged overheads in Sparring Round 1. |
| C2 | **The Sensei will parry by accident** | Parry is the timed **guard raise** (`Fighter.cs:651-685`). Every time the Sensei raises his guard after his own swing, a 0.2 s parry window opens, and the player's counter arrives right in that window. | Set the Sensei's `parryWindow` to 0 during training only, restored afterwards. The field is private (`Fighter.cs:204`), so this needs a tiny public override setter (preferred) or reflection (no code change, brittle). |
| C3 | **Guard break vs "blocks every strike"** | Blocking a string escalates stamina cost (x1, x2, x3 x1.25). A 3-hit heavy string costs 18+36+68 = 122 > the Sensei's 108 stamina, so the guard BREAKS and the hit lands clean (`CombatSystem.cs:1278-1296`). Ordinary blocks drain stamina across attempts too. | (a) Keep the rules honest and refill the Sensei's stamina at every exchange reset. A heavy-heavy-heavy string could still break him. Or (b) a story-only "unbreakable guard" flag on the Sensei. **(a) is recommended** if the user accepts that a heavy string can break him (the story can treat that as a valid exchange). |
| C4 | **He can't block while he is swinging** | A fighter mid-attack cannot block. A whiffed attack also DOUBLES its own recovery (`WHIFF_RECOVERY_MULTIPLIER`), and the Lesson 2 overhead always whiffs. | Story-only copies of his lesson attacks with short recovery (`Sensei_Training_*` AttackData assets, swapped onto his CombatSystem only during training). This is data only and changes no code or other fighter. With ~4 f recovery plus the 4 f whiff tax he is back on guard long before a dash ends (dashes are x2 long under the current test settings). |
| C5 | **Zero health = death + KillShot + K.O. card** | `ResolveHit` calls `NotifyDeath` + `KillShot.Begin` the instant health hits 0 (`CombatSystem.cs:1331`), and `GameMenu.CheckKO` ends the match. The story layer cannot step in between. | A small opt-in hook: `FighterStats.minHealth` (default 0, so no behaviour change), set to 1 in nonlethal encounters. The story layer treats "a damaging blow left him at 1" as the knockdown. Death stays as it is everywhere else. |
| C6 | **No combat events to listen to** | Combat has no C# events. Hit, block, parry, evade and counter outcomes exist only inside `ResolveHit` and in logs. Counting by polling health or animator state would count things the prompt says must not count. | Add **additive, read-only** static events: `CombatEvents.AttackStarted`, `HitResolved` (attacker, target, attack, outcome = Hit/Blocked/GuardBroken/Parried/Evaded, damage, isDashCounter, isParryCounter, stringDepth) and `DashEvaded` (direction). They change no behaviour. **This is the one shared-code touch that matters, so it needs approval.** |
| C7 | **Three characters in one fight** | `Fighter.FindOpponent` takes the first other ACTIVE Fighter (`Fighter.cs:1187`), and FightCamera and the HUD assume a pair. The Partner practising in the background (Stage 1) and the Sensei observing (Stage 2) would be picked as opponents. | Background and observer characters are **puppets**: the same rig and controller with Fighter, CombatSystem and Rigidbody removed, driven by an animator script. Swap puppet and fighter between stages. |
| C8 | **The match flow belongs to GameMenu** | `StartMatch` runs the count-in, the duel clock (1-15 min, ends on time) and the K.O. card. Training must not time out or show the K.O. card. | A small entry point in GameMenu (`StartStoryEncounter` with no clock, no K.O. card and no count-in, or a short one) plus a "story owns the outcome" flag that `CheckKO` respects. This touches GameMenu, so it needs approval. |
| C9 | **Test settings are live** | `AttackFrameScale` 2, `DashTimeScale` 2/1.2 and `WalkSpeedScale` 0.6 slow the whole game right now. | Tune the story timing (counter window, reset delay) against the settings as they are, and re-check if they are reverted. No action now. |

## 3. Feature table

| # | Feature | State | Need | Dependencies | Recommended implementation | Effect on existing systems | Decision | Done |
|---|---|---|---|---|---|---|---|---|
| 1 | Chapter manager (chapter, stage, step, progress, transitions) | Missing | Required | 2, 3, 13 | `StoryChapter` MonoBehaviour runs an ordered list of `StoryStep` ScriptableObjects (Lesson, SparRound, Explore, Boss, Beat). Each step has an objective type, counters, a dialogue table key and beat ids. No single giant script. | None (new layer) | Awaiting Decision | not started |
| 2 | Combat event hooks | **Existing (Phase A)** | Required | — | `combat/CombatEvents.cs`: `AttackStarted`, `AttackEnded`, `HitResolved` (Hit / Blocked / GuardBroken / Parried / Evaded, damage, string depth, counter flags, dodge direction) | Additive only | Implement Now | done (verified) |
| 3 | Encounter start / reset / end without the K.O. flow | **Partial (Phase A hooks)** | Required | 2 | See C8. Reset = fade to black, put both on their marks (Rigidbody + transform + `SyncTransforms`), clear hitstun/attack/dash, restore stamina per rule. | GameMenu: small, opt-in | Awaiting Decision | not started |
| 4 | Sensei training driver (always blocks, never parries, attacks only on cue) | Missing | Required | 2, 3, C2, C3, C4 | `SenseiTrainer` feeds `SetAiInput(P2, blockHeld)` every frame, throws the lesson's strike through `TryAttack`, and swaps in the short-recovery training AttackData. AiController stays off for him. | None outside the story, if C2 is done by override | Awaiting Decision | not started |
| 5 | Lesson 1: three Light attacks on the guard | Missing | Required | 1, 2, 4, 9 | Counts a `HitResolved` with outcome Blocked and attack.Light (not special). Any other attack that lands on the guard gives a reprimand (no repeats, cooldown). Reset after each success. | None | Awaiting Decision | not started |
| 6 | Lesson 2: back dodge + Special Light, 3 in a row | Missing | Required | 1, 2, 4, C1 | Sensei throws Mountain Splitter. Back dodge AND Evaded, then SP+Light within the window AND Blocked = success. A wrong dodge direction makes him follow with a real horizontal cut aimed at that side, and the streak resets. Fail conditions: no dodge, hit, wrong move, timeout. | None | Awaiting Decision | not started |
| 7 | Lesson 3: evade the spin + 3-hit combo, 3 total | Partial (the spin exists) | Required | 1, 2, 4, C3 | Sensei throws Circle of Willow. Any Evaded dodge, then three strikes of one chain (a string or a pressure chain, all Blocked) = +1. Progress is kept after mistakes. | None | Awaiting Decision | not started |
| 8 | Training health regen (Lessons + Rounds 1-3) | Missing | Required | 1 | Per encounter: `Stats.RegenerateHealth(dt, fastRate)` after a short delay since the last hit, for both fighters. Stamina is untouched. | None | Implement Now (once approved) | not started |
| 9 | Nonlethal knockdown + Sensei stop + get-up | Partial (death falls exist, no get-up) | Required | 2, C5, 11 | At 1 hp: freeze both, play the Death trigger, show 「そこまで！」, fade, get up, 「立て、武蔵！」, retry. **No get-up clip**: temporary options are a fade-cut, playing the death clip in reverse, or a kneel then `crouch to standing idle`. | FighterStats: opt-in field | Awaiting Decision | not started |
| 10 | Partner sparring Rounds 1-4 | Missing | Required | 1, 2, 3, 8, 9 | AiController with per-round `Rules` and a difficulty (R1: attack-heavy so there is something to dodge; R2: same, for parries; R3: Moderate; R4: full). Counters as specified. **R3 fails on the 5th hit received.** | None (uses Activate's Rules) | Awaiting Decision | not started |
| 11 | Story cast in ONE scene | Partial | Required | — | Put Musashi (P1), the Sensei and Partner (P2, swapped per stage) and the puppets into the chapter scene. Today the scene holds Bjorn/Miyamoto/Salaadin and the cast lives in three separate test scenes. The Rebuild menus only write Player2 inside `Scenes/Story/`, so they need a Player1 variant. | Story scenes only | Implement Now | **done (Phase B, verified)**: `Musashi_Chapter01.unity` holds Musashi (Player2), Sensei (Player7), Partner, Arima |
| 12 | Background Partner practising on a dummy | Partial (dummy placed) | Required (look) | 11, 7 | Puppet loops Miyamoto's strike states with idle gaps. Placeholder post `Ch01_TrainingDummy` built (Phase B); the looping puppet is Phase D. | None | Implement Now | dummy done, puppet not started |
| 13 | Dialogue + subtitles (JA/EN, speaker, duration, queue, no overlap, interrupt) | Missing | Required | 14 | `StoryLines` table: ScriptableObject or JSON in `Resources/Story/`, with id, speaker, ja, en, duration and tags. `SubtitleBox` queue on its own canvas, with a cooldown per reprimand group. The Unity Localization package is NOT installed, so a lightweight table is recommended over adding it. | None | Awaiting Decision | not started |
| 14 | Japanese font | Missing | Required | — | UI uses legacy `Text` + `LegacyRuntime.ttf`, and no CJK font is bundled. Windows may fall back to OS fonts. Android, WebGL and Mac are not guaranteed. Recommended: bundle **Noto Sans JP** (SIL OFL), subset to the chapter's glyphs. | Adds a font asset | Awaiting Decision | not started |
| 15 | Subtitle language setting | Missing | Required | 13 | `SettingsData.subtitleLanguage` (JA / EN / both) plus a row in Settings. | GameMenu settings (append) | Awaiting Decision | not started |
| 16 | Objective display | Missing | Required | 1 | Top-centre text: `Dodge & Counter: 0/5`, etc. Fed by the step. | None | Implement Now (once approved) | not started |
| 17 | Village exploration movement | Missing | Required | 11 | Fighter movement is 1-D toward the opponent (`Fighter.cs:1245`) and ArenaBounds clamps to the fight circle, so a new `ExploreMover` is needed: it disables Fighter/Combat and does 2-D walk/run relative to the camera on the existing move bindings, plus ground raycast and obstacle checks. The duel controller is NOT rewritten. | None (new component) | Awaiting Decision | not started |
| 18 | Exploration camera | Missing | Required | 17 | Third-person follow: a small script, or Cinemachine 3 (installed, unused). | None | Awaiting Decision | not started |
| 19 | Objective marker + boss trigger | Missing | Required | 17 | Floating marker over the plaza plus a trigger volume. Entering it shows the intro lines and switches to duel mode on the Plaza fight space. | None | Implement Now (once approved) | not started |
| 20 | Villagers (non-combatants) | Missing | Optional | 11 | No villager models exist. Postpone, or temporary placeholders (puppets reusing the Partner/Musashi rig would read as clones). | None | Keep for Later (recommended) | not started |
| 21 | Boss: Arima Kihei | **Missing** | Required | 11 | No Arima model. Options: (a) Kojiro's rigged body as a stand-in (his sleeves fin out on raised arms, open issue 3), (b) the roster Miyamoto in a recolour, (c) a new Tripo model (the tripo MCP failed to connect this session). Moves: borrowed like the Sensei's, with a step-up AI (Moderate to Insane). **Body: Kojiro (user's call), built. Move set: the user's picks 2026-11-04, built and play-verified (section 5c).** AI rules still to do (Phase F). | — | Implement Now | body + moves done |
| 22 | Boss defeat / victory presentation | Partial | Required | 21, 3 | Lethal (normal KillShot) or a "yield" ending? Musashi losing → checkpoint retry. Winning → ending lines, chapter complete, save. | None | Awaiting Decision | not started |
| 23 | Checkpoints + save/load | Missing | Required | 1 | `StoryProgress` JSON in PlayerPrefs: chapter, last completed step, flags. Checkpoints at every lesson, round, the exploration start and the boss. Completed steps are never replayed on load. | None | Implement Now (once approved) | not started |
| 24 | Story entry from the main menu | Missing | Required | 1, 23 | Main menu → Story → Chapter 1 (Continue / Restart). Append `Screen.Story` (the enum serializes). Loads the chapter scene. | GameMenu | Implement Now | **entry + scene round trip done (Phase B)**; Continue/Restart comes with saves (item 23) |
| 25 | Chapter select / unlock next | Missing | Optional | 23, 24 | Mark Ch1 complete and keep a `chaptersUnlocked` field ready. A real select screen comes when Chapter 2 exists. | None | Keep for Later | not started |
| 26 | Cutscene hooks | Missing | Required (hooks only) | 1 | Each `StoryBeat` id (`ch1.open`, `ch1.sensei_intro`, `ch1.lesson1_to_2`, … `ch1.end`) goes through an `IBeatPlayer`. Today it plays dialogue plus a fade. Later a Timeline `PlayableDirector` binds per id. Beats only run between exchanges (fighters frozen, input off) and always hand control back. | None | Implement Now (once approved) | not started |
| 27 | Cinematic cutscenes, VO, facial, cinematic cameras | Missing | Future | 26 | — | — | Keep for Later | — |
| 28 | Sensei combo strings (named) | Partial (placeholder L→M→H) | Optional | — | Not needed for the lessons. | — | Keep for Later | — |

## 4. Testing plan (per phase)

1. Every counter goes up only on a verified `HitResolved` / `DashEvaded` event, never on a button press or a state name.
2. Scripted CLI runs (`Time.captureDeltaTime = 1/60`, coroutine samplers, CSV): correct and incorrect inputs per lesson and round, R3 failing on exactly the 5th hit, the Sensei's parry count = 0 over the whole run, guard contact vs clean hit told apart.
3. Regression: testarena 1v1 vs AI on two fighters, with health, stamina, parry, dodge and K.O. unchanged (the new hooks default to off).
4. By hand (the user): feel, timing, subtitle readability, the pad in exploration.

## 5. Phase A: combat and menu hooks (DONE 2026-11-03, play-tested in testarena)

What changed (normal game unchanged; every switch is OFF by default):
- `combat/CombatEvents.cs` (new): read-only events, raised in `CombatSystem.BeginAttack`, `EndAttack`, `ResolveHit` (evade / parry / block / hit / guard break) and `ResolveDodgeAtArm`. `CombatSystem.StringDepth` getter.
- `Fighter.ParryDisabled`: a guard raise opens no parry window. `Fighter.GuardBreaksAtStrike`: the guard breaks on that strike of a string whatever stamina is left. `Fighter.DashForwardAmount` / `DashRightAmount` getters.
- `FighterStats.minHealth` + `OnNonLethalFloor`: health never drops below the floor, so no death and no kill shot.
- `GameMenu`: `StoryApplyPair`, `StartStoryEncounter` (no count-in, no clock, no K.O. card, no AI, passive regen off), `StoryHold` / `StoryResume`, `EndStoryEncounter`, `StoryOwnsOutcome` (CheckKO stands down), `StoryLeftToMenu` event. Bars restored on exit.
- `FightHud.SetPlayerBarsVisible(player, bool)`: survives a Rebuild.

Test results (Bjorn vs Miyamoto, 60 fps capture):
1. Normal match: Bjorn's Spinning Slash on a 1-hp Miyamoto = Hit, dead, MatchOver screen. PASS
2. Normal guard raise: ParryReady True. With ParryDisabled: ParryReady False while blocking. PASS
3. Normal light on a raised guard: Blocked, 3 chip. With GuardBreaksAtStrike 1: GuardBroken, 10 damage. PASS
4. Story encounter, minHealth 1, Heavy on 1 hp: Hit 13, alive, hp 1, OnNonLethalFloor, screen None. PASS
5. P2 bars hidden, still hidden after Rebuild; restored by EndStoryEncounter. PASS

## 5b. Phase B: the chapter scene and its cast (DONE 2026-11-04, play-tested)

What changed:
- `Scenes/Story/Musashi_Chapter01.unity` (new): a copy of the arena scene (which stays as the Bjorn/Miyamoto/Salaadin test).
  Built by `Crucible > Story > Musashi Chapter One > Chapter 1 - 2 Build the cast` (`Editor/CrucibleChapterOne.cs`, re-runnable):
  - Player2 = **Musashi** (roster #1), Player7 = **Sensei** (his own move set + vitals), **Partner**, **Arima Kihei**
    (Kojiro's body). Partner and Arima run Miyamoto's controller, moves and numbers for now. Player1 and Player4 removed.
  - Names chosen so `Apply the roster balance` writes the right rows: Player2 gets Miyamoto's (Musashi is his copycat),
    Player7 the Sensei's, Partner/Arima no row.
  - `Ch01_TrainingDummy` (a placeholder wooden post with a straw wrap, own capsule collider) and `Chapter1_Spots`
    (dummy, partner practice, sensei watching, the two lesson marks), all grounded by raycast.
  - The scene's GameMenu has `storyScene` on.
- Kojiro / Arima: stature 3.06, `GroundOffset` 0.197/2.8, katana mount solved in play (it pointed straight DOWN under
  Miyamoto's clips; now matches Musashi's bokken direction). `CrucibleCharacterSetup.Rebuild` and
  `CrucibleSensei.WireCombat` are public; `CrucibleBalance.WriteVitals(character, go)` writes one fighter.
- `GameMenu`: main menu **Story** row (says "not in this build yet" when the scene is not in the build); a story scene
  opens on no screen with the bars hidden; its pause menu has no Restart; **Main Menu** loads testarena, which opens
  straight on the main menu (no boot).
  - **Bug found and fixed**: InputManager makes the whole GameMangers object DontDestroyOnLoad, so testarena's menu
    survived into the chapter and the chapter's own managers destroyed themselves as duplicates (the menu then drove a
    roster of destroyed fighters - `MissingReferenceException` in `ApplySides`). `GameMenu.LoadScene` now hands the
    managers back to the current scene before loading, so each scene runs its own. `CombatEvents.Clear()` too.
- The chapter scene is in the EDITOR scene list only. The player builds (`CrucibleBuild`) keep their own lists.

Test results (60 fps capture, Musashi vs each):
1. Stature / sole gap: Musashi 2.78 / -0.01, Sensei 2.87 / -0.01, Partner 2.89 / 0.00, Arima 3.08 / 0.00. PASS
2. Weapon direction (root space): all four the same; Arima's katana fixed (was (0.02, -0.97, 0.25)). PASS
3. Strikes both ways from 2.0, reported through CombatEvents: Musashi->Sensei/Partner/Arima Hit 6, each of them
   ->Musashi Hit 6. PASS
4. testarena main menu shows Story; Story -> chapter scene: 1 menu, roster [Musashi, Sensei, Partner, Arima Kihei],
   screen None, bars hidden. Pause rows: no Restart. Main Menu -> testarena, screen Main, the 7-fighter roster back. PASS
- Open: off-hand grip miss is 0.14-0.26 at idle on all four (Musashi included), the same as the test scenes; not new.
- Open: until Phase C places them, the cast all stand active on the opening mark.

## 5c. Arima Kihei's move set (DONE 2026-11-04, play-verified)

The user's picks (my proposal, with the Medium and SP+Heavy swapped by the user). Nothing borrowed from Musashi
(Miyamoto's clips) or the Sensei. `Editor/CrucibleArima.cs`, controller `P8_Arima` (from Miyamoto's: his
movement and two-handed guard), data `arima_data`, attacks in `Characters/Ch1_Arima/Attacks/`, balance row
`Ch1_Arima` (105 hp / 100 stamina / 14 armour / x1.00 / walk 4.0 / block 65). Each move copies its source's hit
geometry and per-state contact.

| Slot | Name | Animation from | Grip | Lands @2.6 | Notes |
|---|---|---|---|---|---|
| Light | Proud Edge | Bjorn's Light | two hands | Hit 6 | tip sweeps right->left |
| Medium | Severing Cut | Richard's Medium (low cut) | one hand | Hit 8 | sweeps right->left too |
| Heavy | Arima's Verdict | Bjorn's (unused) parry riposte chop | two hands | Hit 11 | |
| SP+Light | Rising Crane | Richard's SP+Light (backhand) | one hand | Hit 12 | |
| SP+Medium | Whirlwind Advance | Bjorn's SP+Medium (advancing low spin) | two hands | Hit 13 | |
| SP+Heavy | Heaven's Fall | Salaadin's SP+Heavy (spinning jumping slash) | one hand | Hit 15 | leaps: hips 2.20 vs 1.52 idle; hyper armour |

- State speeds x0.69-1.43; photographed mid-leap and at the Heavy's wind-up, katana in hand.
- **Light and Medium sweep the same way**, so under the flow rule a Light can never be followed by the Medium
  (or the reverse) inside one pressure run; the placeholder string L -> M -> H therefore cannot convert its first
  link. Everything else flows.
- Gotcha hit and fixed: `Use Mixamo packs` run from the chapter scene re-fitted the five roster controllers whose
  fighters are not in that scene (walk/dash speeds to defaults). Restored from git; run it from testarena.

## 5d. Phase C: the story layer (DONE 2026-11-04, play-verified)

| Piece | Where | What it does |
|---|---|---|
| Chapter runner | `Scripts/story/ChapterRunner.cs` | Runs the steps in order from the first one not done: stage the cast on its spots (held), intro beats, the step's logic, outro beats, checkpoint. API for steps: `Cast`, `Place`, `BeginEncounter(step, showOpponentBars)`, `EndEncounter`, `Hold/ResumeEncounter`, `SetObjective`, `Bark`, `Say`, `Wait`, `PlayBeat`. |
| Step table (data) | `Story/Chapter1/ch1_steps.json` | 10 steps: opening, lesson1-3, spar1-4, village, boss. Each has id, kind, title, cast, spots, intro/outro beats, objective, target. |
| Step logic | `StoryStepKinds` in ChapterRunner.cs | One class per kind. Only `beats` is built; every other kind runs as a **placeholder** (the pair fight freely, F9 in the Editor completes it). Phases D-F replace them. |
| Dialogue table (data) | `Story/Chapter1/ch1_lines.json`, `StoryLines.cs` | 33 lines, JA + EN, speakers, random groups (no immediate repeat). The spec's lines verbatim; lines for beats the spec names without text are marked `draft: true`. |
| Subtitles + objective | `Scripts/story/StorySubtitles.cs` | Bottom block (speaker, Japanese, English), objective + counter under the health bars. One line at a time; a bark is dropped while a line is up or within 2.5 s of the last bark. Story clock = real seconds, frozen by the pause menu. |
| Subtitle setting | `StorySettings` | Japanese + English / Japanese / English. Main menu > Story and the story pause menu. Own PlayerPrefs key. |
| Font | `Fonts/NotoSansJP-Story.otf` (226 KB, OFL text beside it) | Built by `ArtSource/Fonts/subset_jp.py` from the full font in `ArtSource/Fonts/NotoSansJP/`: all kana + the 62 kanji the dialogue uses. **Re-run after adding a kanji.** |
| Save / checkpoints | `Scripts/story/StoryProgress.cs` | PlayerPrefs JSON per chapter: completed step ids, next step's title, complete flag; `ChaptersUnlocked` for a future chapter select. A step is saved done only when it completes, so a reload never replays a finished one and restarts the one in progress. |
| Cutscene hooks | `Scripts/story/StoryBeats.cs` | 18 named beats. `StoryBeats.Register(beatId, player)` plays a cutscene INSTEAD of the beat's lines; events `Started`, `Finished`, `LineShown` (the voice-over hook). Beats only run with the fight held, and the runner restores control itself. |
| Main menu | `GameMenu` `Screen.Story` | Continue (shows the next step's title) / Start from the beginning / Play again, plus Subtitles. Completion returns to the main menu (`GameMenu.LeaveStory`). |
| Scene | `Crucible > Story > Musashi Chapter One > Chapter 1 - 4 Add the story layer` | The `Story` object (runner + subtitles) wired in the chapter scene. Saved. |

Verified in play: the opening beats play; Japanese renders (both modes and Japanese-only photographed); the objective shows under the bars; lesson 1 completes and saves; Main menu > Story > Continue (pressed on a virtual pad) resumes at Lesson 2; un-pausing mid-beat keeps input off until the beat ends; leaving through the pause menu does not mark the step done; a full run of all ten steps saves the chapter complete, unlocks Chapter 2 and returns to testarena's main menu with no errors. Test saves erased afterwards.

Not in Phase C (on purpose): the lessons, rounds, walk and boss logic (D-F), placing the background/observer cast (the runner currently benches everyone but the pair).

## 5e. Phase D: the Sensei lessons (DONE 2026-11-04, play-verified with scripted input)

| Piece | Where | What it does |
|---|---|---|
| Lessons | `Scripts/story/SenseiLessons.cs` | `LessonLight`, `LessonDodgeCounter`, `LessonSpin` on a shared `SenseiLesson` base. Every judgment is read from `CombatLog` (CombatEvents in order): hits, blocks, evades with the dodge direction, attack start/end. |
| Training rules | `Scripts/story/TrainingDriver.cs` | The Sensei holds his guard through the AI input slot every frame (a real block), `ParryDisabled`, guard breaks on the 3rd strike of a string; he swings only when a lesson calls `SenseiAttack`, using runtime copies of his moves with 4 f recovery and NO dodged-swing penalty (`AttackData.CloneWithRecovery`, assets untouched). Health floor 1 on both; health returns at 25 hp/s after 1 s without damage; stamina untouched (his is refilled each exchange, C3 a). All undone by `End()`. |
| Exchange reset | `ChapterRunner.PlacePair` / `Reposition` / `FadeThrough` | Short fade, both back on the line between their spots at the lesson's gap, attack/stun/dash cleared, health and stamina KEPT. |
| Knockdown | `ChapterRunner.Knockdown` + `Editor/CrucibleStoryAnims.cs` | Fight held, `Story_Kneel` (AxePack `crouch idle`), beat `ch1.knockdown` (そこまで！ / 立て、武蔵！), `Story_GetUp` (`crouch to standing idle`), `ch1.retry` (もう一度だ！), full health. The two states are on `P2_Miyamoto` (Musashi, Partner), made Humanoid by `Crucible > Story > Wire the knockdown states`. |
| Background Partner | `Scripts/story/StoryPuppet.cs`, step `puppets` | A copy of the fighter stripped to its Animator, drilling Light/Medium/Heavy at the post. It cannot be targeted. Kept across consecutive steps with the same list. |
| Shared code touched | `CombatSystem.SetLibraries`, `AttackData.CloneWithRecovery` | Additive; nothing in a duel calls them. |

**Lesson changes the user allowed ("more convenient lessons, same concept", 2026-11-04):**
- Lesson 2 is **dodge back, step in, counter**: a back dash always leaves Musashi ~4 away, beyond his Special Light's reach (measured whiff at 3.05), so the counter window is **1.5 s** from the end of the dash (was ~1 s). Lessons 2-3 start **1.8** apart.
- Lesson 3's first strike gets the same 1.5 s; strikes after it must start within 1.0 s of the previous one ending (or be a combo follow-up).
- The Sensei has **no cut that starts on his right**: a dodge to Musashi's RIGHT is answered by Temple Bell (arrives on that side), a dodge LEFT by his quick Falling Branch. Both are real strikes the player can still block or dodge.

Verified (Musashi driven through the input slot: real dashes, real attacks, 60 fps capture):
- Lesson 1: Medium blocked = no progress, reprimand; three Lights = 3/3, step saved.
- Lesson 2: no dodge = reset; back + Special Light on the guard = +1; 1 hp + no dodge = knockdown, health back to 80, streak 0; dodge left = Falling Branch lands, streak 0; then 1, 2, 3 = done.
- Lesson 3: hit by the spin = no progress, progress kept; back dodge + Light, Medium, Heavy all Blocked = +1, three times = done.
- Sensei parries over the whole run: **0**. Every counter met the guard (Blocked). Kneel and puppet photographed.
- Not yet felt by hand: the timing on a real pad.

## 5f. Phase E: Partner sparring (DONE 2026-11-04, play-verified with scripted input)

| Piece | Where | What it does |
|---|---|---|
| Rounds | `Scripts/story/SparringRounds.cs` | `SparDodgeCounter`, `SparParryCounter` (shared `SparDefendAndCounter`), `SparAccuracy`, `SparBout` on a `SparRound` base. The Partner is the EXISTING `AiController` with per-round rules; every count comes from `CombatLog`. |
| Health | `TrainingDriver.Begin(player, opponent, senseiRules: false, fastRegen)` | Floor 1 on both (nobody dies). Fast training regen in rounds 1-3, normal health in round 4. No Sensei-only rules on the Partner. |
| Observer | step `puppets` = `Sensei:Spot_SenseiObserve:idle` | The Sensei stands and watches all four rounds; the Partner's practice puppet is gone (he is the opponent now). |
| HUD | `StorySubtitles.SetObjective(..., extra)` | A second objective line (round 3's `Hits Received: n / 5`). Partner bars only in round 4. |
| Hold fix | `ChapterRunner.Held` | A beat played while a step holds the fight no longer hands control back when it ends. Before this the Sensei's "Get up" / "Again!" played with the fight LIVE, and in round 4 the Partner knocked Musashi down a second time at 1 hp. Applies to the lessons' knockdown too. |

Rules as built:
- **Round 1 (Partner Chill: walks, attacks):** a dodge the combat system judges Evaded, then Musashi's next attack started within **2.0 s** must land (Hit or guard break). Any direction that genuinely evades counts - a back dash against a thrust is a hit by the existing rules, so it does not. Progress kept.
- **Round 2 (Chill):** the combat system's Parried outcome, then a landed counter within 2.0 s. Progress kept.
- **Round 3 (Moderate: walks, attacks, blocks):** clean hits / guard breaks landed count; blocked ones do not. Hits received = Hit or guard break on Musashi (a blocked hit is NOT a hit received - the open default). The **5th** received fails on the spot: そこまで！ / もう一度だ！, both counters to 0, round 3 again.
- **Round 4 (Moderate, full rules):** normal combat, normal health. Musashi at 1 hp = knockdown, get up, round 4 again (both refilled). Partner at 1 hp = the fight is held at once (no further attack), he kneels, the outro plays (そこまで！, 今日の稽古はここまでだ。).
- **Training breather (my addition, rounds 1-3):** after Musashi's stun from a hit ends, the Partner holds his attacks for 0.8 s. Found by the test: his quick thrust otherwise lands again before the 0.8 s stun is over and the player is stun-locked with no way to dodge or parry. Round 4 has no breather.

Verified:
- R1: 5/5 with back dodges against cuts and side dodges against thrusts/overheads; a hit taken and a missed counter added nothing.
- R2: five parries, each Partner stunned 1.2 s, five counters landed, 5/5.
- R3: standing still failed at exactly the 5th hit (5/5 shown, 0 landed), counters reset to 0/10 and 0/5 before control returned; then 10 landed with 2 received.
- R4: Musashi to 1 hp = knockdown, back at 80/80 before control; Partner to 1 hp = win, no attack after it, step saved, next step `village`.
- Sensei observer photographed. Not yet felt by hand: the AI's pace on a real pad.

## 5g. Phase F: the village walk and Arima Kihei (DONE 2026-11-04, play-verified with scripted input)

| Piece | Where | What it does |
|---|---|---|
| Free walking | `Scripts/story/ExploreWalker.cs` | Switches the fighter's `Fighter` off (the duel controller is NOT rewritten) and moves him itself: 2-D, camera-relative, on the existing bindings (left stick / move keys, Run, the dash). Kinematic body, feet on the ground by raycast, walls refuse the step (the fighters' capsule test with a 0.95 step - the dojo gate's sill is 0.84). Follow camera with wall pull-in. Keeps the HUD hidden and its Fighter off even after the pause menu's resume. |
| Input | `InputData.moveVector` | New field: the whole left stick / move keys. Additive; the duel still reads `moveDirection`. |
| Marker | `StorySubtitles.SetMarker` | A small diamond over the destination with the distance in metres, pinned to the screen edge when off screen. |
| Village step | `StoryVillage.cs` `VillageWalk` (kind `explore`) | Every fighter but Musashi benched (no combat possible on the road); the Sensei and Partner as puppets in the dojo, Arima as a puppet waiting on his plaza mark. Within 5 of `Musashi_Plaza/Mark_P1` the walk ends: fade to black, plaza fight space selected, the pair on its marks, fade in. Pause works during the walk (`GameMenu.StartStoryExplore`). |
| Boss step | `StoryVillage.cs` `BossDuel` (kind `boss`) | A normal duel: normal health and stamina, no floors, both bars, the existing AI on Full rules at `step.ai` (**Insane**), the normal killing blow. Musashi killed: 2.4 s for the killing blow, Arima's 出直してこい、小僧！ (`ch1.boss.lost`), fade, both refilled on their marks, again. Arima killed: AI off, 2.4 s, fight held, outro `ch1.boss.won` + `ch1.end`, chapter complete, Chapter 2 unlocked, back to testarena. |
| Runner | `ChapterStep.arena` / `.ai`, `Spot("Root/Child")`, `ArenaDirectory.SelectRoot` | A step names its fight space (dojo steps `Musashi_Dojo`, the boss `Musashi_Plaza`) and its spots by path. |

Verified:
- Walk: from the dojo start to the plaza along the open road in ~7 s at a run; marker 58 m -> 0; only Musashi active, 3 puppets; fight camera off; arrival at 4.9 from the mark; boss set up 6.51 apart, plaza bounds active, fight camera back, AI on.
- A walkability map (the walker's own rules, BFS) found the road: 58 m, east out of the dojo along z ~ 0, then down into the plaza.
- Pause during the walk: paused at timeScale 0, resumed with the walker still in charge and the bars hidden.
- Boss loss x6 (nobody at the controls): every time back to 80/105, lights and speed restored, AI on.
- Boss win: killing blow at x0.2, no Arima attack after it, both lines shown with input off, save `chapterComplete: true`, Chapter 2 unlocked, testarena main menu.

Flags for the user:
- **Arima is on the Insane AI** (Partner round 4 is Moderate). My scripted Musashi at 6 hp lasted 102 frames. `"ai"` in `ch1_steps.json` changes it; whether Insane is fair is a hand-play call.
- **No villagers** (item 20, no models): the road is empty. Postponed.
- **No story-specific boss presentation** beyond the normal killing blow: the closing lines play over the darkened arena. A defeat/victory cinematic is a cutscene hook (`ch1.boss.won`, `ch1.end`, `ch1.boss.lost` beats).
- The marker points straight at the plaza; the road bends, so a player may need to look for the gate east of the dojo.

## 5h. The user's first play-through feedback (DONE 2026-11-04, play-verified with scripted input)

The user played the whole chapter and gave nine notes; their answers are in brackets.

1. **An Arima attack passed over Musashi** ["change it to Bjorn's light as it was"]. Measured all six moves against
   Musashi from 1.5 / 2.4 / 3.3: the culprit was the HEAVY, Arima's Verdict (AxePack2H `standing melee attack
   downward`, Bjorn's old parry riposte), whose tip stayed **1.43-1.60 above Musashi's head** through the whole hit
   window. His Light already WAS Bjorn's Light (tip 1.0-1.4 below the crown, fine). The Heavy now plays Richard's Light,
   the one-handed barbarian overhead (AxePack, same clip name), with its geometry: tip **0.9-1.3 below the crown**, lands
   at all three distances. The GreatSwordPack's two-handed overheads are Musashi's and the Sensei's, so not used.
2. **Musashi deals 30% less** ["because he is young"]: `Fighter.ExtraDamageScale` (runtime, default 1), applied in
   `CombatSystem` after the duel-rules scale; `ChapterRunner.scaledFighter/scaledDamage` = Musashi x0.7 for the whole
   chapter. Measured vs Arima: L/M/H 4/4/5 against 6/6/7.
3. **Resets walk, never cut** [3.b]: `ChapterRunner.WalkPairBack` walks both fighters back to their marks (backing
   up, or turning to walk when the way is mostly sideways), squares them up and snaps the last centimetre. Used by the
   lesson exchanges, sparring knockdowns and the round-3 fail. Verified: no fade, 61 frames of movement, gap 4.00.
4. **Demonstration before every lesson and the training rounds 1-2, then Ready** [keep the streak, start further
   away; lesson 3 says the dash IN is the fast counter]. `StoryDemo`: x0.35 slow motion, Musashi driven through the
   exercise done right, a caption, and a timing bar of the incoming swing with the DODGE/PARRY window lit and the
   moment the blade lands marked. Then both walk back and "Ready? Press Space / Enter (Cross) to begin" waits.
   Lesson 2's gap 2.4. Lesson 3's demo chains all three strikes (game-time waits; frame waits broke under slow-mo).
5. **Control guide** [current lesson or round, never a duel]: `StoryControls.GuideFor(kind)`, right edge, labels
   follow the last device used. Lessons 1-3 and rounds 1-3; none in round 4 or the boss.
6. **Dodge counter** [6.a]: back dash, then dash in + attack stays the intended shape.
7. **The Sensei stuck in his block**: he was in BLOCK-STUN from the player's strikes, so `TryAttack` refused and the
   lesson waited for an attack forever. `TrainingDriver.SenseiAttack` now waits out stun/recovery, retries up to 30
   frames (clearing rhythm locks), and reports `AttackGaveUp`, on which the exchange resets.
8. **The Partner too passive** [attack every 1.5-2 s, use his reach]: `AiController.attackInterval/attackReach/
   standRange` (runtime, story only). Measured: rounds 1-3 attack every 1.6-2.1 s from 1.4-2.9 apart; round 4 keeps
   the plain AI.
9. **Katana** [cut scene later; for now he fights Arima with it]: `WeaponSkin` on `Ch1_Musashi_Bokken_R` swaps the mesh
   and material to `Miyamoto_Katana` (same local frame), so mount, hitbox and aim are unchanged; `BossDuel` turns it on.
   Photographed; strikes land the same. The lessons keep the bokken.

Re-running `Arima > 1 Create move set` and `Use Mixamo packs` (from TESTARENA) rebuilds point 1 from the tables.

## 5i. Follow-ups (DONE 2026-11-04, play-verified with scripted input)

- **Replay the demonstration from the pause menu.** Shown in the story pause menu only while a lesson's or training
  round's EXERCISE runs (`ChapterRunner.CanReplayDemo`; not during the demo or at Ready). It drops the exercise
  (`ChapterRunner.RunUntil` steps the lesson coroutine by hand and disposes it, so its finally blocks run; a pending
  `TrainingDriver.SenseiAttack` is cancelled by serial), walks both back to their marks (`StoryDemo.BackToMarks`),
  replays the demo and Ready, and the exercise starts again from zero. Verified on lesson 2 through the real menu item.
- **"Watch his weapon" tip**: a caption held for 2.4 s while the incoming strike is about to be thrown, in the four demos
  that have one (lessons 2-3, rounds 1-2), plus a closing line on the controls guide wherever timing matters.
- **Musashi follows his Sensei and his Partner to the plaza.** `ChapterStep.guides` ("Name:Spot") turns the staged
  dojo puppets into `StoryGuide`s that walk a `StoryPath` route (grid A* with the free walk's own step-height and
  wall tests, then pulled straight; ~0.1 s to plan) at their fighters' walking pace. They wait, turned back, when
  Musashi is more than 9 away, walk on within 6, the Partner keeps behind the Sensei, and at the end they face the
  duel from `Spot_PlazaSensei` / `Spot_PlazaPartner` (new, just outside the plaza's fight circle). The boss step's
  `puppets` list the same two strings, so the watchers are already standing there and a Continue at the boss has them
  too. Objective: "Follow your Sensei to the village plaza". Verified: 47 m route, they wait at 9.0 / 9.3, the walk
  behind them takes ~15 s, arrival hands over to the boss with both watching.

## 6. Log

- 2026-11-03: audit, this tracker. Decisions recorded in section 0.
- 2026-11-03: Phase A built and tested, all 5 checks pass.
- 2026-11-04: Phase B built and tested, all 4 checks pass; the DontDestroyOnLoad scene-switch bug found and fixed.
- 2026-11-04: Arima's move set built and play-verified (6/6 fire, land, right states).
- 2026-11-04: Phase C done: chapter runner, step and dialogue tables, JA/EN subtitles (Noto Sans JP subset), objectives, checkpoints, the Story menu, cutscene hooks. Play-verified end to end.
- 2026-11-04: Phase D done: the three Sensei lessons, training rules, knockdown, background Partner. Play-verified with scripted input.
- 2026-11-04: Phase E done: four sparring rounds, observer Sensei, round-3 second counter, hold fix for beats inside a held fight.
- 2026-11-04: Phase F done: village walk (free walking, marker, boss trigger) and the Arima duel (loss = retry, win = chapter complete). Chapter 1 playable end to end.
- 2026-11-04: the user's play-through feedback, all nine points done and play-verified (section 5h).
- 2026-11-04: demo replay from the pause menu, the weapon tip, and the guided walk to the plaza (section 5i).
