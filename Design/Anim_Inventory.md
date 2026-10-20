# Animation inventory — what the Mixamo pool actually contains

Task 1 of `Plan_LiteMobile.md`. Every attack-shaped clip in the two Mixamo packs, **measured, not
guessed**, with a recommended clip for each of the 6 specials and 4 ripostes. The user confirms or
swaps; nothing here is wired into the game yet.

Measured 2026-10-19 on 32 clips (the 26 unused candidates plus the 6 already in the game as
calibration references).

## Confirmed by the user, 2026-10-19

- **All ten recommended clips accepted as cast** (the table under *Recommended casting*), each still
  to have its contact calibrated in game before it is called done.
- **Blood Eagle and Haft Strike keep their names**, played by the leaping chop and the reverse
  horizontal. The names stop being literal descriptions until the animation pass.
- **Iaijutsu becomes a double thrust**, from `great sword attack` — the one clip that can show one.
  This replaces the `jump attack` recommendation for Miyamoto's SP + Heavy, which goes into reserve.

Task 1 is therefore closed; the casting is agreed for all 6 specials and both ripostes per
character. Open question carried into Task 3: whether the double thrust deals damage once (on the
second push) or becomes an auto-linked two-move string.

## How it was measured

`ArtSource/Blender/clip_curves.py` (rebuilt this session — the old one lived in a scratchpad and was
lost). Headless, one Blender run for the whole set:

```
"C:/Program Files/Blender Foundation/Blender 5.1/blender.exe" -b --factory-startup \
  --python ArtSource/Blender/clip_curves.py -- <out.json> <clip.fbx> [clip.fbx ...]
```

It reports, per frame, in the **fighter's own frame** (origin = hips, facing from the hip-joint line
averaged over the opening frames, +fwd forward, +lat to the fighter's right, +up up, metres):

- the right hand's path and speed, and a **weapon-tip proxy** — hand + 0.70 m along the grip line,
  the same construction `video_mocap.py` uses
- **hip yaw, unwrapped**, so a 360 reads as a real 360 instead of wrapping
- the **swing core** (contiguous frames where tip speed ≥ 30 % of peak)
- the frame the tip **crosses the centre line** out in front — the contact candidate
- foot speed, height and reach, so a kick is told from a sword swing
- how far the hips travel, so an advancing move is told from one thrown from a stand

The summary (no per-frame curves) is committed at `ArtSource/Blender/clip_curves.json`; re-run the
command for the full per-frame detail.

Two things it got wrong on the way, both fixed, both worth knowing if the numbers are ever re-read:

- **Facing must come from the hip-joint line, not a hips *bone* axis.** A bone axis tilts toward
  vertical when the pelvis pitches, and its ground projection then swings wildly — that is what
  first read as 728° of yaw on the jump attack.
- **The tip proxy has to know the grip.** Both hands on the haft ⇒ the shaft is the line between
  them. In the axe pack's **one-handed** carry the left hand is nowhere near it, and the handle
  continues the forearm instead. Grip spread is in the table: GreatSword clips 0.11–0.20 (two
  hands together), AxePack clips 0.69–1.08 (one-handed).
- A kick must be judged against **tip** speed, not hand speed: in a body-driven sweep the hand
  barely moves while the blade flies, which labelled three sword cuts as kicks.

### How accurate the contact numbers are

Against the five clips whose contact has already been calibrated in game with `BladeTrace`:

| clip | measured | in game | error |
|---|---|---|---|
| `standing melee attack horizontal` (Bjorn light) | 26.7 | 28.7 | +2.0 |
| `great sword slash` (Bjorn heavy, Miyamoto medium) | 19.9 | 21.5 / 21.3 | +1.6 / +1.4 |
| `great sword slash (3)` (Miyamoto light) | 29.6 | 28.1 | −1.5 |
| `standing melee attack 360 high` (Bjorn medium) | 33.4 | 38.0 | first pass, not the chosen one |
| `great sword high spin attack` (both heavies) | 12.2 | 36.2 | first pass, not the chosen one |

**Single-arc cuts land within ±2 frames** — good enough to cast a clip and to seed
`swing_contacts.json`. **On a spin the script reports the FIRST pass of the blade**, which is often
not the one you want: both spins in the game were deliberately re-cut to a later sweep (Bjorn's
medium moved 23.4 → 38.0 on 2026-10-18 for exactly this reason). So for any spinning clip, treat the
contact figure as "there is a pass here" and calibrate the real one in game.

## The measurements

`shape` is one arc label plus modifiers (they are independent — `great sword kick` is a kick **and**
a full turn). `core` = the swing's own frames. `contact` = frame / fraction of the clip.
`yawE` = facing left over at the end, modulo 360 (how square the fighter finishes).
`travel` = hip travel in metres. `tipspd` = peak tip speed, m per frame at 30 fps.

### AxePack — Bjorn's pool (one-handed carry; grip 0.69–1.08)

| clip | f | shape | tipspd | core | contact | yawE | travel | foot reach/speed | what it is |
|---|---|---|---|---|---|---|---|---|---|
| `standing melee attack horizontal` | 73 | horizontal | **0.825** | 24–32 | 26.7 (.36) | −1 | 0.02 | 0.16 / 0.06 | **IN USE** — Bjorn light |
| `standing melee attack 360 high` | 85 | horizontal+spin 375° | 0.804 | 25–39 | 33.4 (.39) | −2 | 0.04 | 0.46 / 0.20 | **IN USE** — Bjorn medium (2H re-posed) |
| `standing melee attack backhand` | 96 | horizontal+spin 245° | 0.812 | 26–42 | 30.0 (.30) | 0 | 0.02 | 0.43 / 0.20 | big reverse horizontal, winds 245° and returns square; tip reaches 1.40 high |
| `standing melee attack 360 low` | 70 | diagonal-down+spin 363° | 0.766 | 23–34 | 31.7 (.44) | 0 | 0.03 | 0.20 / 0.15 | full-turn **low** sweep, tip drops 0.98 — the low twin of his medium |
| `standing melee attack downward` | 69 | overhead | 0.623 | 21–30 | 22.5 (.32) | 0 | 0.00 | 0.11 / 0.03 | **steepest chop in either pack** (tip drops 1.69). Rejected for the heavy on 2026-10-12 because it bottoms out at the left hip; a yawed 2H copy exists in `AxePack2H` |
| `standing melee combo attack ver. 1` | 141 | horizontal+spin 233°+advancing | 0.645 | 82–101 | 89.6 (.63) | 0 | **1.53** | 0.44 / 0.12 | 4.7 s multi-swing performance, walks forward — a source of several cuts, not one move |
| `standing melee combo attack ver. 2` | 127 | horizontal | 0.678 | 24–36 | 28.6 (.22) | 0 | 0.00 | 0.33 / 0.13 | 4.2 s; the cut is early and then a very long tail |
| `standing melee combo attack ver. 3` | 83 | horizontal | **0.842** | 26–36 | 28.6 (.34) | 0 | 0.00 | 0.18 / 0.06 | **fastest tip in either pack**, tight 10-frame core — a clean quick horizontal |
| `standing melee run jump attack` | 111 | overhead+advancing | 0.858 | 47–54 | 52.0 (.46) | −15 | **3.64** | 0.50 / 0.20 | leaping chop, tip drops 1.83. Travel is a problem — see Gaps |
| `standing melee attack kick ver. 1` | 57 | kick | 0.206 | 16–24 | 16.0 (.27) | 0 | 0.00 | 0.66 / 0.28 | mid front kick, foot at hip height |
| `standing melee attack kick ver. 2` | 43 | kick | 0.257 | 15–23 | 15.0 (.33) | 0 | 0.00 | **0.74 / 0.35** | **fastest, furthest kick**, and the shortest clip in the pool |
| `standing taunt battlecry` | 86 | horizontal | 0.469 | 14–20 | 14.6 (.16) | 0 | 0.00 | 0.23 / 0.05 | taunt; no strike |
| `standing taunt chest thump` | 77 | diagonal-up | 0.436 | 18–22 | 19.4 (.24) | +2 | 0.03 | 0.13 / 0.01 | taunt; no strike |
| `standing disarm over shoulder` | 50 | rising | 0.263 | 5–20 | 11.8 (.22) | **+38** | 0.21 | 0.11 / 0.04 | grapple-flavoured disarm, ends 38° off-facing |
| `standing disarm underarm` | 50 | small | 0.259 | 13–17 | 13.0 (.24) | **+38** | 0.01 | 0.12 / 0.10 | ditto; almost no travel on the weapon |

### GreatSwordPack — Miyamoto's pool (already two-handed; grip 0.11–0.20)

| clip | f | shape | tipspd | core | contact | yawE | travel | foot reach/speed | what it is |
|---|---|---|---|---|---|---|---|---|---|
| `great sword slash (3)` | 56 | diagonal-down | 0.585 | 23–33 | 29.6 (.52) | +2 | 0.01 | 0.45 / 0.12 | **IN USE** — Miyamoto light |
| `great sword slash` | 39 | diagonal-down | 0.578 | 14–24 | 19.9 (.50) | 0 | 0.00 | 0.13 / 0.07 | **IN USE** — Miyamoto medium, Bjorn heavy |
| `great sword high spin attack` | 57 | diagonal-up+spin 417°+adv | 0.755 | 9–22 | 12.2 (.20) | +5 | 2.34 | 0.46 / 0.16 | **IN USE** — both heavies, Bjorn's combo ender |
| `great sword slash (5)` | 44 | horizontal | 0.286 | 3–25 | 12.2 (.26) | +2 | 0.02 | 0.60 / 0.14 | **rising reverse cut**: starts high right, sweeps down through the knees and up across to high left. Slowest tip of the slashes, but short and a silhouette nothing else has |
| `great sword slash (4)` | 55 | diagonal-up+spin 386°+adv | 0.490 | 4–14 | 4.0 (.06)? | 0 | 1.13 | 0.37 / 0.16 | full-turn rising cut; contact fell back to "most forward frame" — needs in-game calibration |
| `great sword jump attack` | 66 | horizontal+spin **727°**+adv | 0.655 | 29–42 | 34.3 (.51) | +7 | **3.19** | 0.41 / 0.20 | **double-spin leaping cut** — two full turns, ends square. The most spectacular clip in either pack |
| `great sword slash (2)` | 107 | horizontal+spin 498°+adv | 0.725 | 51–59 | 54.7 (.51) | 0 | **3.10** | 0.54 / 0.15 | 3.6 s spinning advance; long |
| `great sword slide attack` | 65 | diagonal-up+spin 370°+leap | 0.693 | 37–50 | 39.0 (.59) | +8 | **3.49** | 0.62 / 0.15 | sliding/airborne spin, feet leave the ground |
| `great sword kick` | 46 | kick+spin 362° | 0.354 | 8–16 | 10.0 (.20) | 0 | 0.00 | **0.89 / 0.29** | **spinning kick**, the furthest-reaching foot in either pack, comes full circle and ends square |
| `great sword kick (2)` | 52 | diagonal-up | 0.262 | 27–46 | 46.0 (.88) | −1 | 0.00 | 0.77 / 0.20 | front push kick; the contact is very late in the clip, so it needs a tight trim |
| `great sword attack` | 37 | small | 0.445 | 4–9 | 4.0 (.08)? | 0 | 0.00 | 0.11 / 0.04 | **NOT a thrust** — see Gaps. The blade winds back and left and returns; it never reaches further forward than its own start pose |
| `great sword casting` | 145 | overhead | 0.480 | 64–73 | 72.0 (.49) | 0 | 0.00 | 0.37 / 0.05 | 4.8 s spell performance; the one downward sweep is buried in it |
| `great sword power up` | 94 | small | 0.072 | 67–78 | 78.0 (.83) | +4 | 0.08 | 0.20 / 0.06 | no strike at all (peak tip 0.07) — a stance/charge |
| `great sword impact (4)` | 21 | small | 0.048 | 2–11 | 7.0 (.30) | 0 | 0.01 | 0.13 / 0.01 | **a flinch, not an attack** (peak tip 0.05) |
| `great sword impact (5)` | 30 | small | 0.084 | 5–14 | 5.0 (.14) | 0 | 0.00 | 0.34 / 0.01 | ditto |
| `draw a great sword 1` | 15 | rising | 0.676 | 4–10 | 5.0 (.29) | −26 | 0.11 | −0.02 / 0.03 | 0.5 s fast upward draw; hands apart (grip 0.81) |
| `draw a great sword 2` | 24 | overhead | 0.357 | 11–17 | 14.0 (.56) | +13 | 0.12 | 0.11 / 0.06 | 0.8 s sharp downward; hands apart (grip 0.35) |

## Recommended casting

One per slot, chosen on the numbers. Each pick avoids repeating a silhouette the character's three
normals already use — Bjorn has horizontal / 360 spin / overhead chop, Miyamoto has two
diagonal-down cuts and a 360 spin.

### Bjorn

| slot | clip | why |
|---|---|---|
| **SP + Light** | `AxePack/standing melee attack backhand` | 0.812 tip, 17-frame core, returns square (yawE 0). Reverse horizontal reads as the mirror of his light rather than a repeat of it. Trim around contact 30 |
| **SP + Medium** | `AxePack/standing melee attack 360 low` | the low twin of his medium: same full turn, tip 0.98 lower. Distinct at a glance, and it is the one clip that keeps a "low" idea alive for when lows return |
| **SP + Heavy** | `AxePack/standing melee run jump attack` | the biggest thing he has: leaping chop, tip drops 1.83, fastest tip in the pack. Needs the lunge treatment, not root motion (Gaps) |
| **Dash riposte** | `AxePack/standing melee combo attack ver. 3` | fastest tip in either pack with the tightest core (26–36). A counter wants to be over quickly |
| **Parry riposte** | `AxePack/standing melee attack kick ver. 2` | 43 frames, the shortest clip in the pack, furthest/fastest foot. A boot punish reads instantly as a different kind of action from a swing |
| combo enders spare | `combo attack ver. 1` / `ver. 2`, `AxePack2H/standing melee attack downward` | ver. 1 is a 4.7 s multi-swing to cut several enders from |

### Miyamoto

| slot | clip | why |
|---|---|---|
| **SP + Light** | `GreatSwordPack/great sword slash (5)` | 44 frames, and a rising reverse cut is a silhouette he has none of. Fits **Nukitsuke** (quick rising draw-cut) better than anything else in the pack |
| **SP + Medium** | `GreatSwordPack/great sword slash (4)` | full-turn rising cut, 55 f. **This is the answer to the Nitō Cross problem** — it keeps the move a katana move instead of needing the unmodelled wakizashi |
| **SP + Heavy** | `GreatSwordPack/great sword attack` — **Iaijutsu as a DOUBLE THRUST** (user's call, 2026-10-19) | the one clip in either pack with two separate forward pushes: f12→17 drives the tip 0.77 m forward, f18→23 retracts, f24→36 drives 1.03 m. See the caveats below |
| SP + Heavy runner-up | `GreatSwordPack/great sword jump attack` | 727° double-spin leaping cut, ends square, 66 f. Was the recommendation until the double thrust was chosen; keep it in reserve |
| **Dash riposte** | `GreatSwordPack/great sword kick` | spinning kick, 46 f, furthest foot in either pack, comes full circle and finishes square — a dodge-and-whirl counter |
| **Parry riposte** | `GreatSwordPack/great sword kick (2)` | front push kick, 52 f. Distinct from the spinning one. Contact at 0.88 of the clip, so it must be trimmed tight |
| combo enders spare | `great sword slash (2)`, `great sword slide attack` | both spin and advance; good enders, too long for openers |

### Iaijutsu is a double thrust (decided 2026-10-19)

The user's call: try it as a double thrust rather than a draw-cut. `great sword attack` is the only
clip in either pack that can show one, and the full frame dump confirms the shape:

| | frames | tip forward |
|---|---|---|
| windup | f1 → f11 | 0.95 back to −0.47 (blade drawn back past the hip) |
| **push 1** | f12 → f17 | −0.42 → **+0.35** (0.77 m of drive) |
| retract | f18 → f23 | back to −0.09 |
| **push 2** | f24 → f36 | −0.06 → **+0.97** (1.03 m, the committed one) |

This also corrects the earlier verdict in this document that the clip has "no committed strike": it
does, twice — the single-arc test missed it because the two pushes cancel out over the core window.

Three things it needs, all of them Task 3 work:

- **`WeaponAim` must be `Full`.** The blade sits 0.70–0.83 m to the fighter's **left** for the whole
  clip and never crosses the centre line, which is why the contact finder found no crossing. Aim
  yaws it onto the target — exactly the job it was added for.
- **It needs a lunge for reach.** The tip's furthest extension is 0.97 m from the hips, which is
  about where the guard already sits; the thrust's forward reach has to come from
  `AttackData.lungeMaxDistance`, not from the arms.
- **Two *hits* needs a system lite does not have.** Multi-hit is deferred, and the engine is one hit
  per swing by design. So either the move deals its damage on one push (recommend the second, deeper
  one, with the first reading as a feint) or it becomes an **auto-linked two-move string** — which
  the combo system is one small change away from, since `ComboLink` currently requires a button
  press to fire. Decide this in game, in Task 3.

The literal draw-cut reading is parked, not lost: `draw a great sword 1` is 15 frames with a 0.676
tip, a genuinely fast upward draw. It was rejected because its hands are 0.81 apart around a
scabbard the model does not have (the katana is mounted in the right hand only).

## Gaps and corrections to the plan

1. **`great sword attack` is a DOUBLE push, not a single thrust.** The plan and the 2026-10-12 notes
   list it as "the plain thrust alternative". It drives forward twice (f12–17 and f24–36, 0.77 m then
   1.03 m) with a retraction between, and the whole motion stays 0.7–0.8 m off to the fighter's left.
   It is now cast as Miyamoto's Iaijutsu double thrust — see above. **No clip in either pack is a
   plain single thrust**, so Miyamoto's `Miyamoto_Light_Thrust` stays played by a diagonal cut, as
   it is today.
2. **`great sword impact (4)` and `(5)` are flinches, not attacks** (peak tip speed 0.05 and 0.08 —
   essentially static). The plan listed them among the unused attack-shaped clips. They are useful
   later as extra hit reactions or the knockdown that the parked low/trip family will want.
3. **The advancing clips carry root motion the game throws away.** `run jump attack` (3.64 m),
   `jump attack` (3.19 m), `slash (2)` (3.10 m) and `slide attack` (3.49 m) all travel. The importer
   keeps XZ root motion off the Animator, so they **play in place** — a leap that goes nowhere.
   Fighters are moved by writing `transform.position` and their Rigidbody freezes X and Z, so
   turning root motion on is not an option. The project already has the right tool:
   `Fighter.HandleAttackLunge` with a per-move `AttackData.lungeMaxDistance`. Any advancing special
   should be given a large lunge (the defaults are `ATTACK_LUNGE_MAX` 0.9 at 4 u/s) instead.
4. **Two of Bjorn's authored specials have no clip at all.** `Haft Strike` is a haft-butt punch and
   `Blood Eagle` is a spike driven in and ripped back; the axe pack has neither, and nothing can be
   trimmed into them. The recommendations above substitute a reverse horizontal and a leaping chop.
   **Decided 2026-10-19: both keep their names** — Blood Eagle stays Blood Eagle and is played by
   the leaping chop (`run jump attack`), Haft Strike stays Haft Strike and is played by the reverse
   horizontal (`attack backhand`). The names are no longer literal descriptions of the animation;
   a purpose-made clip can make them literal again in the animation pass.
5. **Bjorn's picks are one-handed clips.** All four AxePack recommendations have the hands 0.69–1.08
   apart, where his axe is two-handed. His light already runs a raw one-handed clip and
   `TwoHandGrip`'s IK pulls the left hand onto the haft each frame, so this is expected to work —
   but if a chosen clip stretches the left arm, run it through `ArtSource/Blender/mixamo_carry2h.py`
   into `AxePack2H/` as was done for the locomotion and the 360.
6. **Both disarm clips end 38° off-facing**, so they are poor gameplay clips despite being
   grapple-flavoured — worth remembering when grabs come back.

## What each chosen clip still needs (Tasks 2–3)

Mechanical, per the existing pipeline — none of it is creative work:

1. A map entry in `CrucibleMixamoSetup` keyed by **state name == AttackData asset name** (that is
   what makes a move survive `Use Mixamo packs`).
2. A `swing_contacts.json` entry: the measured contact above as the starting value, then confirmed
   in game with `BladeTrace` at `DebugTimeScale` 0.08. **Required for the spins**, whose measured
   contact is the first pass.
3. A `WeaponAim` mode on the AttackData — `Off` for anything spinning (a spin must not be
   aim-corrected), `YawOnly` for overheads, `Full` otherwise.
4. Lunge tuning, and a large `lungeMaxDistance` for the advancing picks (point 3 above).

---

# Sword and shield (Richard), 2026-10-20

51 clips from the **Pro Sword and Shield Pack** (`Animation/Clips/Mixamo/SwordShieldPack/`, 20 MB),
measured with `ArtSource/Blender/clip_curves.py` the same way as the axe and greatsword packs.
Frames are 30 fps source frames; contact is where the tip crosses the opponent's centre line.

| clip | frames | shape | contact | cast as |
|---|---|---|---|---|
| `slash` | 46 | true overhead, tip drops 1.44, fastest tip in the pack (1.188) | 17.7 | **Heavy — Crusader Hew** |
| `slash (5)` | 42 | diagonal down from the right shoulder | 17.4 | **Medium — Cross Slash** |
| `slash (3)` | 48 | diagonal up | 27.4 | second half of `Richard_DoubleHigh` |
| `attack (4)` | 31 | quick diagonal down, shortest cut in the pack | 13.2 | **Riposte (dash)** |
| `attack` | 42 | leaping chop, travels 3.63 | — | unused: travel would need root motion |
| `attack (2)`, `attack (3)` | 48, 52 | spinning advances | — | unused |
| `kick` | 37 | front boot | 4.2 | **Riposte (parry)** — `hitsWithWeapon` off |
| `block idle` | 41 | shield up, weight back | — | **Block_Hold** — his guard really is the shield |
| `block`, `block (2)` | 17, 15 | shield impact, shield shove | — | Block_Impact, Parry_Success |
| `impact`, `impact (2)`, `impact (3)` | 21, 29, 21 | front, left, right flinch | — | React_Front/Left/Right |
| `walk`, `walk (2)` | 33, 38 | forward, back | — | Walk_Forward/Back |
| `run`, `run (2)` | 22, 17 | forward, back | — | Dash_Forward/Back (half-cut to one stride) |
| `strafe (3)`, `strafe (4)` | 25, 27 | fast left, fast right | — | Dash_Left/Right |
| `sword and shield idle` | 108 | guard-down idle | — | Richard_Idle |
| `death` | 69 | fall backward | — | Death |

**The pack has no thrust and no shield bash**, and those are two of the three moves the brief asks
for. They were keyed on Richard's own rig in `ArtSource/Blender/richard_clips.py` (analytic
two-bone IK onto targets measured from the hips, exported armature-only at 30 fps to
`Animation/Clips/Custom/`):

| authored clip | frames | how it is built | contact |
|---|---|---|---|
| `Richard_Thrust` | 34 | guard, cock at f7, extend to `right=(0.46, 0.08, 0.33)` at f15, hold, return | 15 |
| `Richard_ShieldCharge` | 41 | run frames 0–17 with the shield up, then the thrust | 25 |
| `Richard_ShieldBash` | 58 | keyed left-arm bash f0–9, then mocap `slash (5)` from f16 as the low cut | 33 |
| `Richard_DoubleHigh` | 68 | `slash` f0–29 spliced to `slash (3)` from f10 — one move, two swings | 47 |

Reach note: the extension target had to come in from 0.60 forward to 0.46 — 0.60 is outside the
arm's reach on this rig and the IK missed by 0.32.
