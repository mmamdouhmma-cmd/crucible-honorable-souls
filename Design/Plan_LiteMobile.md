# CRUCIBLE Lite — the mobile-first slice

Agreed with the user on 2026-10-19, decisions confirmed the same evening.
**This plan replaces `Plan_2Weeks.md` as the active backlog.** Work starts at the first prompt of
the next session, at Task 1.

## Why this exists

After testing, the game plays well **only on a PS5 pad**. The user's call: rather than add more
systems, cut the scope to a slice that can be played with a thumb, and finish that slice properly.

## Scope

**In:** the three high strikes per character, block, dash + dash counter, parry + parry counter,
**3 specials each** (modifier + attack button), **6–9 combos each, mixing normals and specials**,
dedicated **counter-riposte animations** after a successful dash or parry, health recovery on
defensive success, the mobile build, and an AI that uses any of it.

**Out for now, kept in the code for later:** low attacks, low block, trips, the tackle, grabs,
characters 3–7, and **signature moves** (see below). Everything authored so far is a **high**
attack. The `AttackHeight` enum stays on `AttackData` (read by nothing) because the height concept
is wanted long term — the lite version simply never produces a non-high attack.

## Confirmed decisions (2026-10-19)

| # | Decision | Confirmed |
|---|---|---|
| D1 | **How a special is thrown** | **Modifier button + attack button, no direction.** `SP + Light` / `SP + Medium` / `SP + Heavy` = the three specials. `InputData.specialModifier` (L1 / `U` on the keyboard) already exists and is read by `InputManager`, used by nothing — this is what it was for. The actual moves get confirmed at the start of the next session. |
| D2 | **What a special costs** | **Stamina, not super: 2.5× the matching normal**, plus a lockout before the next attack. From the current assets that is light 15 → **38**, medium 17 → **43**, heavy 17–19 → **43–48** out of a 100 bar, so a special is roughly 2.5 s of stamina regen. New `SPECIAL_LOCKOUT` (**start at 1.0 s**, serialized) reuses the existing `attackLockoutTimer`. |
| D3 | **Dash** | **Unchanged.** No double-tap, no touch relayout of the dash buttons. The only dash work in this plan is its counter animation (Task 5). |
| D4 | **Keyboard defaults** | **Unchanged** — the in-game Controls menu already rebinds everything, which the user considers enough. |
| D5 | **Health recovery** | Take the user's numbers: **+2% dodge, +2% parry, +3% counter hit** of max health. The "cap" question is answered below — **start with no cap** (knob present, set to 0) and the hidden passive regen switched **off**, so this is the only heal in the fight. |
| D6 | **Animation source** | **Mixamo until every system is finished**, then a pass of purpose-made animations. So specials and ripostes are cast from the unused clips now (Task 1) and re-authored later. |
| D7 | **The super bar** | **Reserved for signature moves**, which come *after* the specials, combos and the animation pass. Nothing in lite spends it. `FighterStats.CanUseSignature()` and `signatureUsed` already exist, and `Character_Movesets.md` has a signature written for both characters. **Note for that pass:** `CanUseSignature()` currently also demands *full stamina*, which now fights the specials' 2.5× stamina cost — that condition will need revisiting. |

### On the recovery cap (the "double cap" question)

A cap means: a ceiling on how much health one fighter may recover **per round**, after which
dodges and parries still work but stop healing.

Why it came up: +2% a dodge sounds small, but a patient player against a swing-happy opponent (or
the Insane AI, which attacks constantly) gets a lot of attempts. Twenty clean defensive reads in a
round is +40–50 health, which can out-heal the damage coming in and stall a round out.

**Decision: start without one.** It is one number (`HEAL_CAP_PER_ROUND`, 0 = unlimited), it is
there in `BalanceConfig` from day one, and it stays at 0 until the user plays a round and says
whether defensive healing feels abusable. Easier to judge from feel than from arithmetic. Turning
the hidden passive regen off at the same time matters — today both fighters quietly regain
1 hp/s (~60 health over a long round), and two heal systems at once would make neither readable.

---

## Task 1 — cast the animations from the Mixamo pool — DONE 2026-10-19

> `Design/Anim_Inventory.md` holds all 32 clips measured and the agreed clip for each of the 6
> specials and 4 ripostes — **the user accepted all ten on 2026-10-19**, so the casting is settled.
> What the measurements forced on the proposal below: there is **no plain thrust** in either pack;
> the two `impact` clips are flinches, not attacks; the advancing clips' root motion is thrown away,
> so an advancing special needs a big `lungeMaxDistance` rather than root motion; and Bjorn's
> `Haft Strike` and `Blood Eagle` have no matching clip — **they keep their names** and are played
> by a reverse horizontal and a leaping chop. **`Nitō Cross` → `great sword slash (4)`** (full-turn
> rising cut, no wakizashi needed). **`Iaijutsu` → `great sword attack` as a DOUBLE THRUST**, the
> one clip in either pack with two separate forward pushes.
>
> Carried into Task 3: whether the double thrust deals damage once (on the deeper second push) or
> becomes an auto-linked two-move string — `ComboLink` would need to fire without a button press.

The user's blocker is animations, and the answer is that the project already owns far more than it
uses: **98 clips in the two Mixamo packs, only 20 of them mapped.**

**Deliverable:** `Design/Anim_Inventory.md` — every unused attack-shaped clip, **measured, not
guessed**, with the `clip_curves.py` approach (Blender headless: per-frame hip yaw and right-hand
forward/right/up/speed): duration, silhouette (overhead / horizontal / thrust / spin / rising /
kick), the frame the weapon crosses the centre line, and the slot it suits. Then a recommended clip
per slot, for the user to confirm or swap.

**Unused and available** (`Animation/Clips/Mixamo/`):

- *AxePack*: `standing melee attack 360 low`, `backhand`, `downward`, `kick ver. 1`, `kick ver. 2`,
  `combo attack ver. 1` / `2` / `3`, `run jump attack`, `taunt battlecry`, `taunt chest thump`
- *GreatSwordPack*: `great sword attack` (plain thrust), `slash (2)`, `slash (4)`, `slash (5)`,
  `jump attack`, `slide attack`, `kick`, `kick (2)`, `power up`, `impact (4)`, `impact (5)`

Both pools serve **both** characters — the clips are Humanoid and retarget to either rig — and the
importer's `mirror` flag plus per-state trim and speed effectively double the pool. Axe-pack clips
need `ArtSource/Blender/mixamo_carry2h.py` first, because Bjorn's axe is two-handed and the raw
clips carry one-handed; greatsword clips already read two-handed.

**First-pass proposal — SUPERSEDED by the measured recommendations in `Design/Anim_Inventory.md`.**
Kept for the record; four of its ten picks did not survive the curves (`great sword attack` is not a
thrust, `slide attack` was proposed for two different slots and is a 2.17 s leaping spin,
`slash (4)` moved from Miyamoto's heavy to his medium, `melee attack downward` was already rejected
for bottoming out at the left hip):

| Slot | Bjorn (axe) | Miyamoto (katana) |
|---|---|---|
| Special on Light — fast, short | `melee attack backhand` | `great sword attack`, a committed thrust |
| Special on Medium — advancing | `melee run jump attack` | `great sword slide attack` |
| Special on Heavy — the big one | `melee attack downward` (2H re-posed) | `great sword slash (4)` |
| Dash riposte | `great sword slide attack` | `great sword slash (5)` |
| Parry riposte | `melee attack kick ver. 1`, a fast pommel punish | `great sword kick (2)` |
| Combo enders (if a link needs its own clip) | `combo attack ver. 1/2/3`, `360 low` | `slash (2)`, `jump attack` |

Each chosen clip then gets the same treatment as the six existing strikes: a `swing_contacts.json`
entry (contact frame + speed), a `WeaponAim` mode, and lunge tuning. Mechanical, not creative.

**Done when:** a clip is agreed for all 6 specials and both ripostes per character.

### The six specials already exist on paper

`Design/Character_Movesets.md` has three **L1 + button** specials authored per character with
full frame data, damage, hit side, evade directions and animation notes. They were parked on
2026-10-17 as out of scope - but the input the user confirmed on 2026-10-19 is exactly L1 +
button, so they are revived and they are the shortlist to confirm:

| | Bjorn | Miyamoto |
|---|---|---|
| SP + Light | **Haft Strike** - haft butt punch into a short cut, 14 dmg, 8/6/14, reach 3.0 | **Nukitsuke** - quick-draw horizontal cut with a sliding step, 10 dmg, 9/3/14 |
| SP + Medium | **Hewing Charge** - three driving steps into a horizontal cut, 16 dmg, 14/6/18 | **Nitō Cross** - scissor cut, 15 dmg, 12/6/16 → **re-cast as a full-turn rising cut** |
| SP + Heavy | **Blood Eagle** - spike driven in and ripped back, 24 dmg, 20/6/22 → **played by a leaping chop, name kept** | **Iaijutsu** - stillness, then one draw-cut, 20 dmg, 18/4/18 → **re-cast as a DOUBLE THRUST** |

Three things to settle when confirming them:

- **Stamina comes from the user's 2.5x rule** (38 / 43 / 43-48), not the 12-28 authored there.
- **The exotic properties are deferred** - multi-hit, armour frames, guard-crush, counter-stance
  and the self-propelled advance each need a system that does not exist. Lite keeps damage,
  frames, reach, hit side and evade directions.
- **Nitō Cross draws a wakizashi, which is not modelled.** Re-cast it as a katana move or swap
  it for another special. **Answered 2026-10-19:** `great sword slash (4)`, a full-turn rising
  cut, keeps it a katana move — see `Design/Anim_Inventory.md`.
- **Haft Strike and Blood Eagle have no clip in either pack** (2026-10-19). The inventory
  substitutes a reverse horizontal and a leaping chop, so both moves need renaming or
  re-designing to match what the animation can actually show.

Damage also wants a look: 24 for Blood Eagle against the current heavy's 11 is more than double,
and all damage was cut 30% on 2026-10-18 after these were written.

---

## Task 2 — the special-attack input layer — DONE 2026-10-19

> Built and play-verified. `SP + button` routes to a separate `specials` library keyed by a new
> `AttackData.specialButton`, costs the authored stamina, and locks attacking out for
> `BalanceConfig.SPECIAL_LOCKOUT` (1.0 s, serialized per fighter) once the special runs its course.
> Verified in play with a stand-in special: `SP+Light` came out as the Special and spent 38 stamina,
> a plain `Light` still gave the normal, `SP+Medium` with no medium special fell through to the
> normal, and the console logged `special finished - locked out for ...`.
>
> Two bugs fixed in passing: the touch **PARRY** button has never worked (`TouchControls` set
> `defenseButton` but `InputManager` never merged it), and the net protocol carried neither the
> modifier nor the parry, so an online guest could do neither. Both now cross the wire.
>
> Still unverified: the touch **SP** latch, which needs a real finger (the CLI cannot press it).
> The AI still never holds the modifier, so it cannot throw a special — that is Task 7.

Small and self-contained, because the modifier already reaches the fighter.

1. **Lookup by (modifier, button).** `CombatSystem.FindAttack` currently keys on `AttackType`
   alone, and `AttackType.Special` is a single enum value, so three specials need a second key: a
   new `specialButton` field on `AttackData` (Light / Medium / Heavy) and a `specials` list beside
   `attacks`. `TryAttack(AttackType button, bool special)`; the data keeps `AttackType.Special` so
   the clash weights and the chain rules already work.
2. **Routing.** `Fighter.HandleCombatInput` passes `currentInput.specialModifier` through. Held, not
   tapped — on the pad L1 + △ is natural, and the existing input buffer covers a slightly late
   attack press.
3. **Cost and lockout** per D2: `staminaCost` authored at 2.5× the matching normal, plus
   `BalanceConfig.SPECIAL_LOCKOUT` (1.0 s) applied through `attackLockoutTimer` when a special
   ends, serialized per fighter like the other two lockouts.
4. **Touch.** The one control change this plan makes: an `SP` button on the right-hand cluster that
   **latches for ~1 s** when tapped (a modifier is a hold, and a thumb cannot hold two buttons
   comfortably), with a visible lit state so you can see the next strike will be a special.
5. Controls menu, Help screen and the key summaries mention the modifier.

**Done when:** `SP + J/K/L` throws three distinct moves on pad, keyboard and touch, each costing
~40 stamina and leaving a beat before the next attack.

---

## Task 3 — 3 specials per character — DONE 2026-10-19

> All six authored and play-verified. `SP + Light/Medium/Heavy` throws Bjorn's **Haft Strike /
> Hewing Charge / Blood Eagle** and Miyamoto's **Nukitsuke / Nito Cross / Iaijutsu**, each on the
> clip cast in Task 1, at 14/15/16 damage for 38/43/48 stamina, with its own animator state,
> trigger and contact-calibrated window. A plain press still gives the normal.
>
> **Iaijutsu's double thrust deals damage ONCE**, on the deeper second push: the frame data is
> 34/4/8, so the first push (source f12-17) is startup and reads as a feint. A genuine two-hit
> version needs `ComboLink` to fire with no button press, which is deferred.
>
> Still to eyeball in the game: whether each silhouette reads, and the spinning `Nito Cross`
> contact, whose measured frame was unreliable (seeded at 27 with the whole turn in the window).


- Three `AttackData` assets each, frame data slower and stronger than the heavy — **start from
  18/5/20 for 14–16 damage**. (The parked moveset sheet authored these as 22/5/26 for 22 and
  *unblockable*; that is a lot in a three-button game, so revisit only if they feel weak.)
- Clip, `swing_contacts.json` entry, `WeaponAim` mode and lunge per move, plus a
  `CrucibleMixamoSetup` map entry so `Use Mixamo packs` reproduces them instead of wiping them.
- Animator: a trigger and an `Any State →` state per special, matching the asset name (the whole
  Mixamo wiring keys off *state name == AttackData asset name*).

**Specials come before the combos** because the combos are built out of them.

---

## Task 4 — combos, 6–9 per character, normals into specials

The user's shape for a string:

- `normal → special`
- `normal → normal → special`
- `special → normal`

`MAX_STRING_LENGTH = 3` already fits all three. The system itself is built and verified
(`AttackData.comboLinks`, `cancelStartFrame` / `cancelEndFrame`, `CancelRequirement`, the 8-frame
input buffer, `STRING_LOCKOUT = 1.5 s`). Today Bjorn has **one** string, Miyamoto **three**.

- 3 openers × 2 links = 6 strings, 3 of them extended to a third hit = 9 countable combos per
  character, all inside the existing cap.
- A link into a special must check the special's **stamina** (`TryCancelIntoFollowUp` already
  tests `link.followUp.StaminaCost`), so an ender can legitimately fail on an empty bar — worth a
  log line and, later, a HUD hint.
- A special that opens a string needs its own `comboLinks` and cancel window; the system is
  generic, so this is data.
- Each new follow-up asset needs a trigger, an attack→attack transition on the controller (no exit
  time, 0.08 s fixed blend), and a map entry.
- **Verify `STRING_LOCKOUT` while in here** — never play-tested. A string takes ~10 s of real time
  at the 8% test speed, which outruns a CLI round trip, so read `attackLockoutTimer` by reflection
  inside the same eval.
- **New UI: a move list** (pause menu → Moves), generated from the live `AttackData` so it cannot go
  stale. Nine combos per character are worthless if nobody can find out what they are.
- Still open: whether a string may double back on itself. The user's shapes largely settle it — a
  special always terminates the string except in `special → normal` — but confirm that
  `normal → normal` of the *same* button stays illegal.

---

## Task 5 — counter ripostes + health recovery

The plumbing is already there, which makes this the cheap, high-impact item:
`CombatSystem.BeginAttack` already consumes `ConsumeDashCounter()` / `ConsumeParryCounter()`.

1. While a counter window is live, route the attack button to a **riposte** `AttackData` with its
   own trigger and clip from Task 1 — so a counter *looks* like a counter instead of a normal
   strike with a multiplier on it. This is also the dash's "counter animation" from D3.
2. Health recovery per D5. `FighterStats.Heal` exists. New constants `HEAL_ON_DODGE` /
   `HEAL_ON_PARRY` (2%) / `HEAL_ON_COUNTER_HIT` (3%) and `HEAL_CAP_PER_ROUND` (0 = off), applied in
   `Fighter.NotifyEvaded`, `Fighter.NotifyParry` and the counter-hit damage path, with
   `healthRegenPerSecond` set to 0 on both fighters.
3. HUD: a recovering segment on the health bar and a small `+2%` / `+3%` float, beside the existing
   `COUNTER READY` / `PARRY COUNTER` flags.

---

## Task 6 — the mobile build

1. **Ship one arena.** The 280 MB build (224 MB of it data) is mostly three Tripo arenas plus
   Al-Quds all sitting in the scene. Strip to the active one at build time.
2. **Gate `MobileQuality` by the real touch check**, not by platform: it still tests
   `isMobilePlatform || WebGLPlayer`, so a PC browser gets phone visuals. `TouchControls` already
   has the right check (`CrucibleIsTouchDevice()`); share it.
3. Phone pass: frame rate, button size under a real thumb, relay latency — none of which has ever
   come back from a real device.

---

## Task 7 — make the AI play the lite game

The AI only walks, swings, blocks and dashes. It **cannot parry at all** (it never writes
`defenseButton`), does not know strings exist, and will not know about specials. Lite is
single-player-first on a phone, so Moderate/Insane need: parry attempts against a read wind-up,
deliberate strings, specials when stamina allows, and ripostes inside their own counter windows.

---

## Running order

Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6 → Task 7, then a balance pass.

Tasks 1–3 are the spine: the inventory removes the animation question, the input layer and the
special assets are what the combos are then built out of.

## After lite is finished (not scheduled here)

1. **Signature moves** on the super bar — the bar's actual purpose (D7), after the animation pass.
2. **Purpose-made animations** replacing the Mixamo casting, per D6.
3. Heights: low attacks, low block, trips, the tackle. Then grabs. Then characters 3–7.
4. Netplay guest effects, the fixed tunnel URL, and deleting the debug-only components
   (`PlayProbe`, `BladeTrace`, `InputHold`; `DebugTimeScale` stays, the Settings speed slider
   drives it).
