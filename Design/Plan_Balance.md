# CRUCIBLE: Honorable Souls — Balance and feel pass

Agreed 2026-10-23 in conversation.
**This pass runs BEFORE `Design/Plan_Audio.md`** - the user's call, 2026-10-23.

**Progress (2026-10-23, same day):** items 1-4 of the sequence in section 5 are SHIPPED and
play-verified - the whiff recovery extension (2.1, `WHIFF_RECOVERY_MULTIPLIER = 1.0`: a whiff
doubles the move's own recovery), the low-stamina reversal (2.3, below 40%: cost x0.75, damage
x0.75, pace x0.85 - equal cost/damage cuts, so efficiency is flat and only tempo is lost), the
block spread (2.4: 85 / 75 / 68 / 60) and Miyamoto's walk (2.2, 44 -> 48). No frame budgets
changed, so no animation refit was needed. Measured: Bjorn whiffed light 30 f vs landed 26 f,
whiffed heavy 54 f vs authored 38; weakened light cost 13 vs 17, damage 6 vs 8, 36 f vs 30.
Item 5 shipped later the same day: Salaadin's combo freedom (normal->normal links no longer
openerOnly so he alone dials three normals; every normal branches to both other normals; specials
hand back two normals MID-string, giving him N->SP->N; cancelStartFrame = his moves' own startup,
so a landed hit converts instantly) with the deferred damage cut riding along, x0.92 -> x0.85 -
the roster reads Bjorn 110 > Richard 100 > Miyamoto 92 > Salaadin 85 and the x0.92 twinning with
Miyamoto is over. Play-verified: L->M->L lands 5+6+5 damage, L->SP+M->M lands 22, the fourth
press is capped at MAX_STRING_LENGTH, and ShinCut's hit-confirm cancel fires on frame 12 where
the default window would open at 16.

Item 6 shipped the same evening, which completes the plan. Dash i-frames are a per-fighter field
(`Fighter.dashIFrameDuration`, a `CrucibleBalance` column) at the proposed 0.18 / 0.16 / 0.14 /
0.12 s = 82 / 62 / 47 / 35% coverage - and the finding on the way in: `DASH_IFRAME_DURATION` was
dead code, nothing read it, so the WHOLE dash evaded (coverage was 100% for everyone, not the
68-44% the table above assumed). Only the head of a dash evades now; the tail is punishable.
Parry became a six-column per-fighter table in the same pass (Miyamoto window 0.18 / cooldown 0.8
/ counter window 1.0 / riposte x1.5; Bjorn 0.10 / cost 10 / cooldown 1.2; Richard and Salaadin
base), which settles the 1.5-vs-1.35 and 1.0-vs-0.5 discrepancies from section "Not built at
all". And per the user's call the same day, a SUCCESSFUL dodge refunds the dash's stamina - the
plan's own warning stands: refund + 82% coverage is double-generous for Miyamoto, first thing to
watch in a playtest. Verified by dash age at the judgment frame: Miyamoto hit at 0.217 / evaded
at 0.100 against his 0.18; Richard hit at 0.200 / evaded at 0.117 against his 0.12.

The measured numbers below came off the live assets and the scene, not off the design intent —
`Editor/CrucibleBalance.cs` for the table, the four `Fighter` components for the defensive fields.

---

## 1. The diagnosis, measured

### Head to head today

Time to kill, best sustained normal, stamina-limited, no blocking or parrying. Alternating two
different normals, which is the real optimum because it dodges the same-attack lockout entirely.

| attacker | →Miyamoto | →Salaadin | →Bjorn | →Richard | kills in | survives | ratio |
|---|---|---|---|---|---|---|---|
| Miyamoto | — | 10.9 | 9.5 | 13.1 | 11.2 | 10.3 | 0.92 |
| Salaadin | 10.6 | — | 13.9 | 19.2 | 14.5 | 12.5 | 0.86 |
| Bjorn | 8.3 | 10.7 | — | 14.1 | 11.1 | 12.5 | **1.13** |
| Richard | 12.0 | 15.8 | 14.2 | — | 14.0 | 15.5 | 1.10 |

**Head-to-head wins: Bjorn 3, Richard 2, Salaadin 1, Miyamoto 0.**

The model deliberately ignores blocking, parrying and dodging, because those are currently
IDENTICAL for every fighter — so it measures what happens when defensive skill cannot
differentiate anyone. Miyamoto is the fighter left with nothing.

### Why Salaadin feels like a better Miyamoto

The user's words: *"salaadin feels like miyamoto but with better block and higher damage."*
Confirmed, and the mechanism is the useful part:

| Salaadin wins by | | Miyamoto wins by | |
|---|---|---|---|
| health | +19% | frames | −31% |
| armour | +125% | regen | +25% |
| block | +37% | dash speed | +34% |
| stamina pool | +15% | armour pierce | large |
| damage (M/H) | +11% | walk | +7% |

**Salaadin's advantages are UNCONDITIONAL, Miyamoto's are CONDITIONAL.** Health, armour, block and
damage apply on every exchange. Speed is capped by the stamina bar (below), pierce only matters
against armour, and 4.4 against 4.1 walk is under the threshold anyone feels. They also share a
damage multiplier — both x0.92 — which is the giveaway that they were specced as one fighter at
two speeds.

### Stamina, not time, is the binding constraint

Swings per second, today's costs, neutral against inside a combo:

| | Light | Medium | Heavy |
|---|---|---|---|
| Miyamoto | 1.67 / **1.67** (bar) | 1.42 / 1.43 | 1.01 / 1.25 |
| Salaadin | 1.14 / **1.14** (bar) | 1.00 / **1.00** (bar) | 0.80 / 0.84 |
| Bjorn | 1.00 / **1.00** (bar) | 0.80 / 0.85 | 0.66 / 0.74 |
| Richard | 0.93 / **0.93** (bar) | 0.78 / **0.78** (bar) | 0.60 / 0.67 |

Where it reads *bar*, the stamina pool already decides the rate and no timing change can touch it.
Miyamoto's light is identical neutral and in-combo. **Any pacing change not paired with a stamina
change will do nothing on those moves.**

### Block is a wasted axis

Richard 85, Salaadin 82, Bjorn 80, Miyamoto 60. Three of four within five points, so "can I afford
to turtle" is the same question for everyone but Miyamoto — and the berserker currently owns the
second-best guard in the game, which does not match the archetype.

---

## 2. What was agreed

### 2.1 Recovery frames carry the pacing — no new wait system

The per-weight "wait time" idea (L 0.4 / M 0.6 / H 0.8) is **dropped** in favour of tuning the
RECOVERY FRAMES that already exist on every attack. Same effect, one fewer system, and it re-times
the animation along with it.

- Pacing lives in each `AttackData`'s `recoveryFrames`, tuned per fighter and per weight.
- **A combo link is the only way to act before recovery ends** — which is already how it works.
- **A MISSED attack extends recovery by a percentage of the move's own recovery**, so a whiffed
  heavy is punished harder than a whiffed light without needing a second table.

### 2.2 Salaadin's damage stands for now; Miyamoto takes the movement speed

The decision that Salaadin should carry the LEAST damage in the roster
(**Bjorn > Richard > Miyamoto > Salaadin**, and the two of them to stop sharing x0.92) is
**DEFERRED, not dropped**. It waits for his combo freedom, because of the dependency in section 5.
For now his damage is untouched.

The separation comes from the other side instead: **Miyamoto's walk speed goes up, 4.4 -> 4.8**
(`moveSpeed` 44 -> 48 in `CrucibleBalance`). He currently leads Salaadin by 7% (4.4 against 4.1),
which is beneath the threshold anyone notices - it is one of the five "advantages" in the table
above that never converts into anything. At 4.8 the lead is 17%, and the roster reads
4.8 / 4.1 / 3.8 / 3.2.

**Why this side and not Salaadin's.** Not cutting Salaadin's damage is already a buff against the
plan; giving him the movement as well would buff him twice over while Miyamoto got nothing and
stayed the fighter who loses every matchup.

It also serves Miyamoto's axis rather than just his stat line. Counters need SPACING - walking in
and out of range to bait a whiff is how a counter fighter manufactures the opening he punishes -
so a fast walk is a defensive tool for him, not only an approach tool. It pairs directly with the
whiff-recovery rule in 2.1: the faster he can leave range, the more whiffs he draws.

### 2.3 Low stamina — cheaper, weaker, slower

The current rule (recovery at x0.75 below 20% of the bar) is **REVERSED**; `LOW_STAMINA_FRACTION`
and `LOW_STAMINA_RECOVERY_MULTIPLIER` come back out of `Fighter.HandleStaminaRecovery`.

In its place, below **40%** of the bar an attack:

- costs LESS stamina,
- deals LOWER damage,
- comes out SLOWER.

A drained fighter stays in the fight but stops threatening. The user's framing matters here and
should not be re-litigated: the stamina spiral is **not a fault to be fixed** — it is what teaches
people to pace a round, and a player who has learned the game should rarely reach it.

**Design constraint so this cannot be farmed:** the damage cut must be at least as large as the
cost cut. If cost falls further than damage, damage-per-stamina IMPROVES below 40% and the optimal
play becomes deliberately sitting at 39% of the bar. Equal percentages leave efficiency flat and
cost only tempo, which is the intent.

### 2.4 Everyone can block; not everyone can lean on it

Spread the reductions so guard is a real per-character decision. Direction, not final numbers:
Richard stays highest, Bjorn comes DOWN (aggression should not also carry the second-best guard),
Salaadin sits between, Miyamoto stays lowest and is paid back in parry and dodge.

### 2.5 Dash i-frames per fighter

Today `DASH_IFRAME_DURATION = 0.15 s` is a single global constant with **no per-fighter field at
all** — every fighter is invulnerable for exactly the same slice of their dash. It needs a
serialized field on `Fighter` and a column in `CrucibleBalance`, exactly like the parry fields.

**The number that matters is COVERAGE — i-frames as a fraction of that fighter's dash duration —
not the absolute i-frame time.** Dash durations already differ, so the fixed 0.15 s is already
producing a ranking by accident, and in the right direction:

| | dash duration | i-frames now | coverage now | proposed | coverage |
|---|---|---|---|---|---|
| Miyamoto | 0.22 s | 0.15 s | **68%** | 0.18 s | 82% |
| Salaadin | 0.26 s | 0.15 s | 58% | 0.16 s | 62% |
| Bjorn | 0.30 s | 0.15 s | 50% | 0.14 s | 47% |
| Richard | 0.34 s | 0.15 s | 44% | 0.12 s | 35% |

Dash SPEED already ranks correctly and should be left alone: 11.8 / 8.8 / 7.3 / 5.9 world units
per second.

Two things to watch:

- **Coverage must stay clearly under 100%.** A dash nobody can hit is a free escape that needs no
  read, which is the opposite of what the dodge game is for. Miyamoto at 0.20 s on a 0.22 s dash
  would be 91% — effectively invincible while dashing. 0.18 s keeps a punishable head and tail.
- **It interacts with a stamina refund on a successful dodge.** High coverage plus a refund makes
  dashing nearly free for Miyamoto. Pick one of the two to be generous, not both.

---

## 3. Signature axis per fighter

One unmistakable axis each, everything else near the middle — so the fighters are not competing on
the same ground and the numbers only have to be roughly even.

| fighter | axis | reads as |
|---|---|---|
| **Miyamoto** | COUNTERS | widest parry window (0.18 against a 0.12 base), longest dash i-frames, fastest dash AND fastest walk (4.8). Low damage in neutral; his damage comes from ripostes, and his walk is what baits the whiffs he punishes. |
| **Bjorn** | WEIGHT | highest damage, worst sustain, costliest swings. Candidate: his blows hurt through a guard, making him the answer to turtling. |
| **Richard** | ATTRITION | most health, most armour, best block, least stamina, slowest. Already correct — the one archetype fully expressed today. |
| **Salaadin** | COMBO FREEDOM | lowest damage per blow, most branches, longest combo input window, improvises mid-string. |

---

## 4. Already built — do NOT rebuild

Checked in code this session:

- **Mashing cannot outrun the animation.** `CombatSystem.TryAttack` returns false while
  `isAttacking` unless the press matches a combo link; the press is BUFFERED, not dropped, and
  fires on the first legal frame. And `state.speed = clip.length / (frames/60)` means the clip is
  time-scaled to the budget, so "animation finished" and "frames finished" are the same instant.
- **Extra recovery on a dodged swing.** `evadedExtraRecovery` + `DodgedRecoveryFramesFor()` already
  hold the attacker open — no turning, no walking — for 48 frames (0.8 s) by default, overridable
  per fighter (`CombatSystem.dodgedRecoveryFrames`) and per move
  (`AttackData.DodgedRecoveryFrames`). **It only fires on a dash-dodge.** Extending it to a plain
  whiff is the work in 2.1; `swingEvaded` and the one-hit-per-swing flag are the hooks.
- **A combo already ends in a 0.5 s lockout** (`STRING_LOCKOUT`).
- **There is NO flat per-attack wait today.** `ATTACK_CHAIN_LOCKOUT` (0.5 s) fires only after the
  SAME attack twice in a row (`SAME_ATTACK_CHAIN_LIMIT = 2`), after a string, or after a special.
- **A successful dodge already heals 2%** (`HEAL_ON_DODGE`), so a stamina refund on a successful
  dodge would extend an existing reward rather than introduce a new idea.

### Not built at all

- **Parry is identical on all four**: window 0.12 s, cost 8, cooldown 1.0 s, stun 1.0 s, counter
  window 1.0 s, counter damage x1.5. `CrucibleBalance.WriteScene` never writes a parry field.
- **Dash i-frames are a single global** (`DASH_IFRAME_DURATION = 0.15 s`) — there is no per-fighter
  field to set. One accident in the right direction: i-frames are fixed but dash DURATION is not,
  so Miyamoto is already invulnerable for 68% of his dash and Richard for 44%.
- Combo link counts: Miyamoto 23, Bjorn 22, Richard 19, **Salaadin 19** — he is tied last while
  being the fighter whose identity is variety.

---

## 5. Sequencing, and the one real dependency

**Salaadin's damage cut must not ship before his combo advantage exists.** He is already the
slowest killer in the roster (14.5 s average); taking damage off him with nothing in return makes
him unplayable. Build the combo freedom first, or both in the same pass. This is why the cut is
deferred in 2.2 and Miyamoto's walk speed carries the separation in the meantime.

Suggested order:

1. Recovery-frame pass + the whiff extension (2.1). Self-contained — but it re-times animations,
   so `Crucible > Animation > Use Mixamo packs` has to follow it, and the state speeds read back.
2. Low-stamina rule (2.3). Small, and reverses a one-line change made 2026-10-23.
3. Block spread (2.4). One column in `CrucibleBalance`.
4. Miyamoto's walk speed, 44 -> 48 (2.2). One integer in `CrucibleBalance`, no animation refit -
   `MoveSpeed` drives the walk blend, and the clip is a loop rather than a timed budget.
5. Salaadin's combo freedom, with his deferred damage cut riding along WHENEVER that lands (2.2).
6. Per-fighter parry and dash i-frames (2.5). New serialized fields on `Fighter`, plus a
   `CrucibleBalance` column so the whole roster stays in one table. These two are what give
   Miyamoto his axis, so they are not optional trimming - without them his damage cut in neutral
   buys him nothing and he stays the fighter who loses every matchup.

---

## 6. Open questions

- **Bjorn's guard-break**: chip damage through block, or heavy block-stamina drain into a posture
  break? The user has flagged posture / block-break as a future task in its own right.
- **Does a whiffed first hit close a combo string**, or may the string continue? Closing it is the
  stricter, more read-driven answer.
- **The AI cannot parry, dodge on reaction, or throw a special.** If counters become Miyamoto's
  whole identity, that axis is invisible in 1P — he will feel weak against the computer however the
  numbers land. Worth deciding whether the AI learns to parry in the same pass.
- **Hitstun has never been examined.** It runs 7–15 frames across the roster and nobody has asked
  what it should express; it is the obvious lever for whose hits keep a turn going.
- Richard's and Bjorn's exact landing spots after the above. Modelling says the changes
  over-correct Bjorn if he takes both a cost rise and a pacing penalty, so he should take one.

---

## 7. How to verify

The harness from the clip work applies: `Time.captureDeltaTime = 1f/60f`, sample in a coroutine,
write a CSV. For a matchup pass specifically, three traps that each produce confident wrong numbers:

- **`StartMatch` must have run and `inMatch` must be true**, or the fighters are frozen: every
  attack still fires and every lunge reports 0.00 travel.
- **`ApplySides` must have run**, or `FindOpponent` takes the first other Fighter in the scene —
  a sweep had Bjorn lunging at a benched Salaadin across the arena while the health check watched
  Miyamoto.
- **Top up `currentStamina` and `currentHealth` before every swing.** A drained `TryAttack` returns
  false and a K.O. ends the pass silently.
