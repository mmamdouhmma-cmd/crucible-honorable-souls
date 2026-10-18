# Two-week plan — 2026-10-17 → 2026-10-31

Agreed with the user on 2026-10-17. Their order, with three deltas marked **[delta]**.

Target: **specials, combos, dash counter, parry, lows + trips, low block, 2 grabs each** —
systems complete and play-tested on Bjorn and Miyamoto, animations from the new video pipeline
where it works and Mixamo/hand-keyed where it does not.

---

## Lane A — video → animation (runs the whole fortnight, parallel to the systems)

The user films themselves: **start in a T-pose, hold ~2 s, then perform the move.** White wall
behind, full body in frame, phone horizontal and still, filmed from a 3/4 angle (not straight
on — depth is the weak axis of single-camera capture).

Pipeline to build (`ArtSource/Blender/video_mocap.py` + `pose_extract.py`):

1. **Step zero (install):** Python 3.12 via winget + `mediapipe` + `opencv-python`. Blender's
   own Python is 3.13 and mediapipe has no 3.13 wheel, so the pose estimator runs as a separate
   process and writes landmarks to JSON. No ffmpeg needed (OpenCV decodes the mp4).
2. `pose_extract.py <video> <out.json>` — MediaPipe Pose per frame: 33 `pose_world_landmarks`
   (metric, hip-centred) plus visibility and the detected fps.
3. **Calibration on the T-pose frames:** measure the user's limb lengths and shoulder/hip width
   from the held T-pose, and solve the camera yaw so +X is the direction they face. Scale every
   landmark by the Tripo rig's own limb lengths — that is what lets a 1.8 m human drive a
   0.98-unit rig without the feet skating.
4. `video_mocap.py <master.blend> <landmarks.json> <ClipName>` — feed the scaled landmarks into
   the IK controls the anim masters already have (`IK_L/R_Hand`, `IK_L/R_Foot`, `POLE_*`,
   `CTRL_root`, hip/spine/head FK), key every 2nd frame, then retime so the clip lands on the
   AttackData frame count. Export with `bake_and_export()` as usual (rest key on frame -10,
   armature named `Armature`).
5. **Clean-up is mandatory, not optional:** single-camera depth jitters, feet slide, and the
   weapon hand has to be re-solved so the blade path is a clean arc. Expect the capture to give
   the timing, the body attitude and the weight shift; contact frames, foot plant and the weapon
   get fixed by hand afterwards.
6. **Review loop:** after each clip goes in, capture it in Play mode (frame sequence → gif) and
   send it over, so a bad move is seen rather than described.

Fallback if MediaPipe's 3D is too noisy to be worth cleaning: Rokoko Video free tier
(mp4 → FBX, needs the user's account), retargeted onto the Tripo rig in Blender. Decide after
the **first test clip** — that is the gate, and it is the first thing to run this week.

What Lane A does **not** have to produce: the ~100 clips in
`Character_Movesets.md > Animation clip list`. This fortnight needs about 30:

| Clips | What |
|---|---|
| 8 | specials (4 per character) |
| 6 | low attacks (2 each) + tackle (1 each) |
| 2 | low block holds |
| 8 | grabs: connect + hold + 2 finishes, per character |
| 4 | victim side of the grabs (caught, held, struck, taken down) |
| 2 | `Knockdown`, `Getup` (shared, one per weapon set if the carry differs) |
| 2 | `Parry`, `Parry_Success` |

Mixamo stays the stop-gap for anything the capture cannot deliver in time — the Great Sword and
Axe packs have unused clips close to several of these.

---

## Lane B — systems

### 0. Dash-counter recovery — **[delta: day 1, not after combos]**
It is a number and a field, and the rest of the counter layer builds on it.
`BalanceConfig.DODGED_RECOVERY_FRAMES` 18 → **48 (0.8 s)**, exposed as a serialized field on
`CombatSystem` and overridable per `AttackData`, so it can be tuned from the Inspector without a
rebuild. Already wired into `CombatSystem.MarkEvaded`, so the dodger's existing
`DASH_COUNTER_WINDOW` (0.5 s) + `DASH_COUNTER_DAMAGE_MULTIPLIER` (1.35) finally land on a
genuinely open attacker.

### 1. Attack router + input buffer — **[delta: prerequisite, before the specials]**
`InputData.specialModifier` / `lowModifier` are read by `InputManager` and used by nothing. One
change unlocks specials, lows, trips and grabs at once:
- `FighterData` / `CombatSystem` get a move table keyed on (modifier, direction, button) instead
  of the three hard-coded L/M/H slots.
- A buffered input queue in `CombatSystem` (a press remembered for ~8 frames) — the same queue
  the combo cancels will read.
- Bind the modifiers in `ControlBindings` + the Controls menu (L1 special, L2 low, R1 grab) for
  keyboard and pad, and add them to the touch pad.

### 2. Specials — **[revised 2026-10-17: the L1+button trio is parked]**
The user's call: the three `L1 + button` defaults (Haft Strike, Hewing Charge, Blood Eagle and
the Miyamoto equivalents) were never part of the plan and are dropped from this fortnight. They
stay written up in `Character_Movesets.md`, marked parked, for a later pass.

What is in scope instead, per character: **one signature strike on the existing heavy button**,
captured on video — Bjorn gets `Mountain Breaker` (L / △), the biggest overhead. So the work is
an upgraded heavy rather than a new input layer:
- The move still needs its clip, contact calibration (`swing_contacts.json`), `WeaponAim` mode
  and lunge tuning, exactly like the six existing strikes.
- **Frame data is a decision, not a copy.** `Character_Movesets.md` authored Mountain Breaker as
  a special: 22/5/26, 22 damage, *unblockable*. On a plain heavy button that is a lot — the
  current heavy is 15/5/15 for 16. Recommendation: keep the capture's big silhouette, keep the
  existing heavy's frame data (or go slightly slower, 18/5/18), and drop unblockable; revisit if
  it feels weak.
- Because this is a normal, not a special, the attack router (item 1) is no longer a blocker for
  it. The router still gets built for lows, the tackle and grabs.

### 3. Combos — real cancel windows
Replaces "time it off the end of the animation". New on `AttackData`: `cancelWindowStart` /
`cancelWindowEnd` (frames, relative to contact) and the list of moves this one may cancel into.
`CombatSystem` reads the buffered press inside the window and starts the follow-up early;
the controllers get attack→attack transitions (today every attack is Any State → attack, exit
0.9) so the animations blend instead of snapping. The chain limiter
(`SAME_ATTACK_CHAIN_LIMIT` 2 / `ATTACK_CHAIN_LOCKOUT` 0.8 s) stays as the anti-spam floor: a
cancel into a *different* move is free, mashing the same one still locks out.

### 4. Parry — **[delta: same pass as 0, they share the plumbing]**
The constants are already in `BalanceConfig` and used by nothing: `PARRY_WINDOW` 0.15 s,
`PARRY_STUN_DURATION` 1.0 s, `PARRY_COUNTER_WINDOW` 0.5 s, `SUPER_GAIN_PARRY` 15. Wire them:
block pressed within `PARRY_WINDOW` *before* the hit lands → **no damage, no stamina loss**,
attacker takes `PARRY_STUN_DURATION` of stun (attack force-ended, facing locked — the same state
a dodged attacker enters), parrier gets a damage multiplier for `PARRY_COUNTER_WINDOW`. Needs
`Parry` + `Parry_Success` clips and a HUD flash (`CLASH` already does this). All five numbers
serialized.

### 5. Attack height — low attacks, trips, tackle, low block (one system, four features)

**Design call (user, 2026-10-17): the L2 family is strikes, not grapples.** The tackle lives
here, not with the grabs: it connects, deals damage, knocks the victim down, and nothing is ever
held — so it needs one clip and a `Knockdown` reaction, no paired animation. That also means the
knockdown state gets built and tuned by the trips/tackle *before* the grabs need it.

`AttackHeight` (High/Mid/Low/Overhead/Uppercut) exists on `AttackData` and is read by nothing.
- `Fighter` gets a block height (block + down = low block); a block only works against a
  matching height — a low attack beats a high block, an overhead beats a low block.
- `React_Low` get-hit animation; `HitSide` unchanged.
- **Trips** need the only genuinely new state here: `Knockdown` → `Getup`, with invulnerable
  frames on the way up and a tunable down time. Grabs and the signature moves reuse it.
- Low attacks: 2 per character from the `L2 + button` table.

### 6. Grabs — the hard one, 2 per character

**Design call (user, 2026-10-17): a grab is a real grab — it connects, the opponent is held, and
then it branches.** So the "2 grabs each" are the two finishes off **one** grab connect, not two
unrelated throws:

| | Bjorn | Miyamoto |
|---|---|---|
| Connect (R1) | "Beard Hook" | "Kumi-uchi" |
| Branch A — strikes (□/✕) | "Butt Ring Hammer" 6+6+8 | "Tsuka-ate" 5+5+8 |
| Branch B — takedown (△/↓) | "Hook and Haul" 14, knockdown | "Kote Gaeshi" 8+10, knockdown |

Cheaper than two separate grabs: per character that is one connect clip, one hold loop, two
finishes, and the victim side of each. What it needs:
- A grab attempt that is **not** a hitbox: a short range check in front (`grabRange` 1.5–1.8)
  during the active frames, and a whiff recovery long enough to be punishable.
- A **held state** on both fighters: root motion locked, the relative offset owned by the
  attacker, hits ignored by both, and a timeout if no branch is pressed.
- A **grab break** — the victim mashes inside `GRAB_BREAK_WINDOW`, both get pushed apart and the
  attacker eats the longer recovery, so a connect is never a guaranteed 20 damage.
- Branch input read from the same buffered queue as the combo cancels (item 1), with up to 3
  strikes in branch A, each extending the hold.
- **Paired animation.** Mixamo has no victim clips, so this is hand-keyed or captured twice (the
  user performs the attacker, then the victim) and the two clips played in lockstep.
- Netplay: the guest's `CombatSystem` is disabled, so the grab has to ride the existing state
  sync (`NetSession`) or it desyncs. Host-authoritative, like everything else.

---

## Schedule

**Week 1 (Oct 17 – Oct 24)**

| Day | Work |
|---|---|
| 1 | Dash-counter 0.8 s + serialize; mocap step zero (Python 3.12 + mediapipe); **first test video from the user** |
| 2 | `pose_extract.py` + T-pose calibration; verdict on MediaPipe vs Rokoko |
| 3 | `video_mocap.py` → first captured clip in Unity, gif back to the user |
| 4 | Attack router + input buffer + bindings |
| 5–6 | Specials: 4 each — data, clips, calibration |
| 7 | Combos: cancel windows, buffer, controller transitions; play-test |

**Week 2 (Oct 25 – Oct 31)**

| Day | Work |
|---|---|
| 8 | Parry: window, no damage, attacker stun, counter multiplier, HUD |
| 9 | Parry + dash-counter animations; tune both against the AI (Moderate/Insane) |
| 10 | Attack height axis: low block, low attacks, `React_Low` |
| 11 | `Knockdown` → `Getup` state; trips |
| 12–13 | Grab 1 per character end to end (paired anim, break, netplay) |
| 14 | Grab 2 per character; balance pass; web build + phone test |

---

## Tuning knobs this adds (Inspector-editable, defaults in `BalanceConfig`)

| Knob | Default | Note |
|---|---|---|
| `DODGED_RECOVERY_FRAMES` | 18 → **48** | 0.8 s, the user's number; per-attack override |
| `PARRY_WINDOW` | 0.15 s | exists, unused |
| `PARRY_STUN_DURATION` | 1.0 s | exists, unused — the "mini stun" |
| `PARRY_DAMAGE_MULTIPLIER` | 1.5 | new; the dash counter is 1.35 |
| `PARRY_COUNTER_WINDOW` | 0.5 s | exists, unused |
| `INPUT_BUFFER_FRAMES` | 8 | new |
| `cancelWindowStart` / `cancelWindowEnd` | per attack | new, on `AttackData` |
| `KNOCKDOWN_FRAMES` / `GETUP_INVULN_FRAMES` | 60 / 12 | new |
| `GRAB_RANGE` / `GRAB_BREAK_WINDOW` | 1.8 / 0.4 s | new |

## Risks, and what is deliberately out

- **The capture pipeline is the one real unknown.** If the first test clip is unusable the
  animation lane falls back to Mixamo + hand-keying and the clip count shrinks further. That is
  why it is day 1–3 and not day 10.
- 15 specials per character, the uppercuts, the signature moves and characters 3–7 are all out of
  scope — the slice proves the pipeline, the rest is production work after it.
- Still open from last session and not scheduled here: shrinking the 280 MB web build to the
  active arena only, gating `MobileQuality` by the touch check, blood/hit effects on the netplay
  guest.
