# CRUCIBLE Lite — the mobile-first slice

Agreed with the user on 2026-10-19. **This plan replaces `Plan_2Weeks.md` as the active backlog.**
Work starts at the first prompt of the next session, at Task 1.

## Why this exists

After testing, the game plays well **only on a PS5 pad**. Keyboard and touch are not fluid, and
that is a control problem, not a content problem. So: cut the scope to a slice that can be played
with a thumb, and make that slice good.

## Scope

**In:** the three high strikes per character, block, dash + dash counter, parry + parry counter,
**6–9 combos each**, **3 specials each**, dedicated **counter-riposte moves with their own
animations**, health recovery on defensive success, AI, local 2P, the online relay, and a mobile
build that actually runs on a phone.

**Out for now, kept in the code for later:** low attacks, low block, trips, the tackle, grabs,
and characters 3–7. Everything authored so far is a **high** attack. The `AttackHeight` enum stays
on `AttackData` (read by nothing) and the long-run height concept is *not* deleted — the lite
version simply never produces a non-high attack.

## Decisions to confirm in the first exchange of the next session

Each has a recommendation; a one-word answer unblocks it.

| # | Decision | Recommendation |
|---|---|---|
| D1 | **How a special is thrown.** Options: (a) one SPECIAL button + direction — neutral / toward / away = the 3 specials; (b) three separate buttons; (c) hold an attack button. | **(a).** One button on every device, the direction is already under the other thumb, no timing window, and it reuses the input buffer the combos use. (b) needs three more touch buttons on a pad that is already crowded; (c) adds latency to every normal. |
| D2 | **Do specials cost the super bar?** | **Yes, ~34 of 100 each.** `currentSuperBar` already exists and is already fed (hit +5, combo +10, parry +15, dash counter +12) but is **spent by nothing and shown nowhere** — specials finally give it a purpose, and it stops them being spammable normals. Needs a super bar added to the HUD. |
| D3 | **Dash input.** Today it is dash-key **plus** a direction (keyboard) or one of four dash buttons (touch). | **Double-tap a direction** on keyboard and touch; the pad keeps its button and right-stick flick. One input instead of two, and it deletes the four touch dash buttons. The single biggest fluidity win available. |
| D4 | **New keyboard defaults** (P1): `A`/`D` move, double-tap to dash, `J`/`K`/`L` light/medium/heavy, `I` special, `Space` guard (hold), `Left Shift` parry. | **Yes.** Guard and parry move to the left hand's big keys; today they are `P` and `;`, which is why blocking and parrying feel bad. Requires bumping the saved-bindings key to `crucible.controls.v2`, or saved prefs keep overriding the new defaults. |
| D5 | **Health recovery** — the user's idea: +2% on a successful dodge, +2% on a parry, +3% on a counter hit. | **Take it, with a per-round cap (~15% of max), and turn the existing passive regen OFF in lite.** There is already a hidden `healthRegenPerSecond = 1` on both fighters (~60 hp over a long round); two heal systems fighting each other would make neither readable. |
| D6 | **May a combo double back on itself** (`,`→`.`→`,`)? | **No** — a move may not appear twice in one string. Keeps the 6–9 strings distinct and stops a two-button loop. |
| D7 | **Specials and ripostes: Mixamo packs, or does the user film them?** | **Mixamo packs** (see Task 1). The capture lane is parked on the user's own verdict, and there are ~16 unused attack clips in the two packs — enough for all 6 specials, 4 ripostes and the combo enders without filming anything. |

---

## Task 1 — the animation inventory (START HERE)

The user's words: *"all i think is mainly missing from my brain is i cant think of animations now."*
So the first job is to take that off their plate entirely: produce the menu, with a recommendation
per slot, and let them say yes or swap one out.

**Deliverable:** `Design/Anim_Inventory.md` — every unused attack-shaped clip in the two Mixamo
packs, **measured, not guessed**, with the `clip_curves.py` approach (Blender headless: per-frame
hip yaw and right-hand forward/right/up/speed), listed with duration, silhouette (overhead /
horizontal / thrust / spin / rising / kick), the frame the weapon crosses the centre line, and the
slot it suits.

**Currently unused and available** (`Animation/Clips/Mixamo/`, 98 clips, 20 of them mapped):

- *AxePack*: `standing melee attack 360 low`, `backhand`, `downward`, `kick ver. 1`, `kick ver. 2`,
  `combo attack ver. 1` / `2` / `3`, `run jump attack`, `taunt battlecry`, `taunt chest thump`
- *GreatSwordPack*: `great sword attack` (plain thrust), `slash (2)`, `slash (4)`, `slash (5)`,
  `jump attack`, `slide attack`, `kick`, `kick (2)`, `power up`, `impact (4)`, `impact (5)`

Both pools serve **both** characters — the clips are Humanoid and retarget to either rig — and the
importer's `mirror` flag plus per-state trim and speed effectively double the pool. Axe-pack clips
need `ArtSource/Blender/mixamo_carry2h.py` first, because Bjorn's axe is two-handed and the raw
clips carry one-handed; greatsword clips already read two-handed.

**First-pass proposal, to be confirmed against the curves:**

| Slot | Bjorn (axe) | Miyamoto (katana) |
|---|---|---|
| Special — neutral | `melee attack downward` (2H re-posed), the big overhead | `great sword slash (4)` |
| Special — toward | `melee run jump attack`, a leaping advance | `great sword attack`, a committed thrust |
| Special — away | `melee attack backhand`, short and retreating | `great sword slash (2)` |
| Dash riposte | `great sword slide attack` | `great sword slide attack` (retimed / mirrored) |
| Parry riposte | `melee attack kick ver. 1`, a fast pommel punish | `great sword kick (2)` |
| Combo enders | `combo attack ver. 1/2/3`, `360 low` as a sweep | `slash (5)`, `jump attack`, `slash (2)` |

Each chosen clip then gets the same treatment as the six existing strikes: a `swing_contacts.json`
entry (contact frame + speed), a `WeaponAim` mode, and lunge tuning. Mechanical, not creative.

**Done when:** the user has a clip picked for all 6 specials and both ripostes per character.

---

## Task 2 — controls, the actual reason for this slice

1. **Double-tap dash** (D3) in `InputManager`: a direction pressed twice inside ~0.25 s sets
   `dashDirection`. Keyboard and touch; the pad's dash button and right-stick flick stay.
2. **Touch relayout** (`TouchControls`): left thumb = one move strip (hold to walk, double-tap to
   dash, swipe up/down for the in/out dashes) — the four dash buttons go. Right thumb = `L` `M` `H`
   in an arc with `GUARD` (hold) and `PARRY` (tap) below and `SP` beside them. Thumb-sized, with a
   pressed state that is visible *under* a finger.
3. **Keyboard defaults** (D4) in `ControlBindings`, prefs key bumped to v2 so the new layout
   actually lands on an existing install.
4. **Special input** (D1): `GameAction.Special` plus the held direction, read through the existing
   buffered queue and routed in `CombatSystem`.
5. Controls menu, Help screen and the generated key summaries updated for all of it.

**Done when:** the user plays a full round on the phone and on the keyboard without reaching, and
says both feel fluid. **This is the gate** — no new content until it passes.

---

## Task 3 — combos to 6–9 per character

The system is built and verified already (`AttackData.comboLinks`, `cancelStartFrame` /
`cancelEndFrame`, `CancelRequirement`, the 8-frame input buffer, `MAX_STRING_LENGTH = 3`,
`STRING_LOCKOUT = 1.5 s`). Today Bjorn has **one** string and Miyamoto **three**. Getting to 6–9 is
data plus controller transitions:

- 3 openers × 2 links = 6 two-hit strings; extend 3 of them to a third hit = 9 countable combos per
  character, inside `MAX_STRING_LENGTH`.
- Enders that need their own clip take one from Task 1; the rest reuse the character's own moves,
  which costs no animation at all — that is how Miyamoto's three work today.
- Each new follow-up asset needs a trigger name, an attack→attack transition on the controller (no
  exit time, 0.08 s fixed blend), and a map entry in `CrucibleMixamoSetup` so `Use Mixamo packs`
  reproduces it instead of wiping it.
- **Verify `STRING_LOCKOUT` while in here** — it has never been play-tested. A string takes ~10 s of
  real time at the 8% test speed, which outruns a CLI round trip, so read `attackLockoutTimer` by
  reflection inside the same eval.
- **New UI: a move list** (pause menu → Moves), generated from the live `AttackData` so it cannot go
  stale. 6–9 combos per character are worthless if nobody can find out what they are.

---

## Task 4 — 3 specials per character

- `AttackType.Special` already exists and already carries the heaviest clash weight.
- Three `AttackData` assets per character; frame data in the heavy's neighbourhood but slower and
  stronger — **start from 18/5/20 for 14–16 damage**. The parked moveset sheet authored these as
  22/5/26 for 22 and *unblockable*, which is a lot in a three-button game; revisit if they feel weak.
- Super cost per D2, plus **a super bar in the HUD**, which does not exist yet.
- A special may **end** a combo (a legal final link) but never open one, so it is always earned.

---

## Task 5 — counter ripostes + health recovery

The plumbing is already there, which makes this the cheap, high-impact item:
`CombatSystem.BeginAttack` already consumes `ConsumeDashCounter()` / `ConsumeParryCounter()`.

1. While a counter window is live, route the attack button to a **riposte** `AttackData` (one per
   character per window, or one shared) with its own trigger and clip from Task 1 — so a counter
   *looks* like a counter instead of a normal strike with a damage multiplier on it.
2. Health recovery per D5. `FighterStats.Heal` exists. New constants `HEAL_ON_DODGE` /
   `HEAL_ON_PARRY` (2%) / `HEAL_ON_COUNTER_HIT` (3%), applied in `Fighter.NotifyEvaded`,
   `Fighter.NotifyParry` and the counter-hit damage path, with the per-round cap and the passive
   regen switched off.
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
deliberate strings, specials when the bar allows, and ripostes inside their own counter windows.

---

## Running order

Task 1 → Task 2 (**gate: the user plays and approves the feel**) → Task 3 → Task 4 → Task 5 →
Task 6 → Task 7, then a balance pass.

Tasks 1 and 2 are the ones that unblock everything else: the first takes the animation question out
of the user's head, the second fixes the reason this slice exists at all.

## Deliberately untouched

Heights (kept in data), grabs, lows, trips, characters 3–7, the mocap lane (parked, clips kept in
`Animation/Clips/Custom`), netplay guest effects, the fixed tunnel URL.
