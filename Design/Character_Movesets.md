# CHARACTER MOVESETS — CRUCIBLE: Honorable Souls

Filled 2026-10-11 from `Character_Movesets_Master_Sheet.template.md`. Bjorn and Miyamoto are
complete because they are first through the new model + animation pipeline. The other five
follow once these two are approved.

Every move is written so it can be animated and authored directly:

```
NAME · input
DMG base damage | STA stamina cost | frames STARTUP/ACTIVE/RECOVERY (60 fps, total in brackets)
PIERCE armor bypass % | KB knockback units | HITSTUN frames
HIT  : Straight / Left / Right (side of the DEFENDER struck; drives the get-hit clip)
EVADE: which dash directions evade it (comesFrom on AttackData)
ANIM : what the body actually does, in plain words, for the clip
```

Properties vocabulary: `low` (must be blocked low / hits crouchers), `overhead` (anti-duck),
`anti-overhead` (beats overheads, rising), `launcher`, `knockdown`, `guard-crush` (big guard
meter damage), `armor` (no flinch during startup), `unblockable`, `counter-stance`, `multi-hit`,
`moves N` (travels N units forward), `charged` (hold to add damage and pierce).

Timing rules used throughout: lights 6–8 f startup, mediums 10–12, heavies 14–18, specials
up to 22, signature 26. Recovery is always longer than startup on anything that knocks down.
Reach numbers are world units (see CLAUDE.md: hitbox range + offset, 1 unit ≈ 0.6 m).

---

# 1. BJORN IRONSIDE — two-handed Dane axe

Weapon: one long-hafted bearded axe (haft ~1.25 m, bearded blade with a back spike, iron butt ring),
`Bjorn_Axe` in the right hand with the left hand on the haft. Fighting style: Viking long-axe.
Both hands on the haft (right hand low near the butt, left hand high under the head); the cuts come
from the hips and the rear foot, the beard hooks shields, limbs and necks, the spike thrusts, the
butt punches. Guard: left foot leading, body bladed, haft diagonal across the body with the head high
on the right shoulder side (the Mixamo "Pro Melee Axe" idle approximates it until the hand-keyed set
exists). Heavy, committed, never bouncy: when he moves it is because the whole body moved. Reach is
the axe's edge: hitboxRange 3.6 on the long cuts, 3.0 on the short haft work.

## NORMAL ATTACKS (high)

```
LIGHT · □ — "Raven Cut"
DMG 9 | STA 6 | 6/3/8 [17] | PIERCE 0 | KB 0.4 | HITSTUN 12
HIT Left | EVADE Right, Back
ANIM: short two-handed horizontal cut right-to-left at neck height from the guard, the left hand slides
      down the haft a hand's width to lengthen it, hips open a quarter turn, head stays on the target.
      Reach 3.6, wide flat box.

MEDIUM · ✕ — "Fjord Splitter"
DMG 13 | STA 11 | 10/5/14 [29] | PIERCE 5 | KB 0.7 | HITSTUN 16
HIT Right | EVADE Left, Back
ANIM: backhand return cut left-to-right, diagonal from the left hip up through the shoulder line, the
      right foot steps 0.4 in behind it. The beard leads. Reach 3.6, wide box.

HEAVY · △ — "Skull Cleaver"
DMG 20 | STA 18 | 15/5/15 [35] | PIERCE 20 | KB 1.0 | HITSTUN 22
HIT Straight | EVADE Left, Right
PROPERTIES: overhead, knockdown on counter-hit
ANIM: the axe goes up over the right shoulder with both hands (a readable wind), the rear foot drives,
      the whole body drops into a vertical chop that finishes at knee height with the hips squared.
      Reach 3.4, tall narrow box.
```

## LOW / TRIP ATTACKS (L2 + button)

These are **strikes**, not grabs: one hitbox, no hold, no paired animation. The ones marked
`knockdown` put the opponent on the ground and send them through `Knockdown` → `Getup`.

```
LOW LIGHT · L2+□ — "Shin Hook"
DMG 7 | STA 7 | 8/3/10 [21] | PIERCE 0 | KB 0.3 | HITSTUN 12
HIT Left | EVADE Right, Back | PROPERTIES: low
ANIM: the head drops to the ground on the right side and the beard hooks inward at shin height; short crouch.

LOW MEDIUM · L2+✕ — "Hamstring"
DMG 12 | STA 11 | 12/4/14 [30] | PIERCE 5 | KB 0.5 | HITSTUN 16
HIT Right | EVADE Left, Back | PROPERTIES: low
ANIM: deep lunge on the left leg, long horizontal cut at knee height left-to-right with the haft fully extended.

LOW HEAVY · L2+△ — "Ankle Breaker"
DMG 17 | STA 18 | 18/5/20 [43] | PIERCE 10 | KB 0.9 | HITSTUN 24
HIT Straight | EVADE Back | PROPERTIES: low, knockdown
ANIM: drops to the right knee, the axe sweeps a full low circle right-to-left at ankle height, rises on
      the recovery. Reach 3.8, wide flat box.

TACKLE · L2+↓+△ — "Shield Wall Breaker"
DMG 12 | STA 55 | 12/6/24 | KB 2.0 | moves 2.5, interrupts attacks, knockdown, target invulnerable on standup
HIT Straight | EVADE Left, Right | PROPERTIES: mid, trip, no grab state
ANIM: axe held across the chest like a bar, full sprint tackle, both go down, Bjorn rolls up first.
      A STRIKE that knocks down — the victim just plays Knockdown, nothing is held.
```

## UPPERCUTS (↓ + button)

```
LIGHT UPPERCUT · ↓+□ — "Beard Lift"
DMG 8 | STA 7 | 7/3/12 [22] | PIERCE 0 | KB 0.4 | HITSTUN 12
HIT Straight | EVADE Left, Right | PROPERTIES: anti-overhead
ANIM: the head is flicked upward from the hip with the hands close together, the beard catching an
      incoming weapon or chin; the body rises onto the toes.

MEDIUM UPPERCUT · ↓+✕ — "Rising Edge"
DMG 13 | STA 11 | 11/4/16 [31] | PIERCE 5 | KB 0.7 | HITSTUN 16
HIT Straight | EVADE Left, Right | PROPERTIES: anti-overhead, launcher (short)
ANIM: reverse of the Skull Cleaver: a two-handed rising vertical cut from the knees to above the head,
      back arching, the butt ring finishing at the chest.

HEAVY UPPERCUT · ↓+△ — "Valhalla's Gate"
DMG 18 | STA 20 | 16/5/22 [43] | PIERCE 15 | KB 1.2 | HITSTUN 26
HIT Straight | EVADE Left, Right, Back | PROPERTIES: launcher, knockdown
ANIM: squat, then a jumping rising cut; feet leave the ground for 6 frames, the axe ends vertical over
      the head, heavy landing with a stagger step.
```

## SPECIAL ATTACKS — default (L1 + button)  ·  PARKED (2026-10-17)

Designed but not planned for the current pass - the user dropped these three from the
fortnight on 2026-10-17. Kept here for a later one.

```
LIGHT SPECIAL · L1+□ — "Haft Strike"
DMG 6+8 (14) | STA 14 | 8/(3+3)/14 [31] | PIERCE 0 | KB 0.4 | HITSTUN 10, 14
HIT Straight, Left | EVADE Back | PROPERTIES: multi-hit (2)
ANIM: the butt of the haft punches straight forward from the guard (hands slide to the middle), then
      the head whips round into a short cut. Close-range tool: reach 3.0.

MEDIUM SPECIAL · L1+✕ — "Hewing Charge"
DMG 16 | STA 20 | 14/6/18 [38] | PIERCE 10 | KB 1.0 | HITSTUN 18
HIT Left | EVADE Left, Right | PROPERTIES: moves 1.5, armor (frames 4–14)
ANIM: the axe is carried across the chest, three driving steps, a horizontal cut released at the end
      of the charge with the full haft.

HEAVY SPECIAL · L1+△ — "Blood Eagle"
DMG 24 | STA 28 | 20/6/22 [48] | PIERCE 30 | KB 1.4 | HITSTUN 28
HIT Straight | EVADE Left, Right, Back | PROPERTIES: guard-crush
ANIM: the spike is driven into the guard with both hands and the axe is ripped back and down with the
      beard, opening it. Heavy exhale, head follows the pull.
```

## DIRECTIONAL SPECIALS

```
BACKWARD · L1+←
  □ "Measured Step"     DMG 9  | 6/3/12  | defensive: hop back 1.0 with a short horizontal cut on the retreat
  ✕ "Retreating Hook"   DMG 13 | 10/4/14 | hop back 1.0, the beard hooks the weapon arm and pulls the opponent 0.5 forward
  △ "Whirl Away"        DMG 16 | 14/5/18 | full turning cut away, ends 1.5 further back, knockback 1.0

DOWN · L1+↓
  □ "Earth Splitter"    DMG 10 | 9/3/14  | low, the head slams into the ground, dust burst, hits the feet
  ✕ "Trip Hook"         DMG 13 | 12/4/16 | low, the beard hooks the lead ankle and yanks: knockdown on counter-hit
  △ "Stomp of the North" DMG 17 | 16/4/22 | low, knockdown, a stomp then the axe drops on the grounded target

OVERHEAD · L1+↑
  □ "Hammer Fall"       DMG 11 | 10/3/14 | overhead (anti-duck), short vertical chop from the shoulder
  ✕ "Twin Fall"         DMG 15 | 13/4/18 | overhead, pierce 10, chop with a full step in
  △ "Mountain Breaker"  DMG 22 | 22/5/26 | overhead, unblockable, the slowest move: the axe goes behind the back first

FORWARD / CHARGED · L1+→
  □ "Charging Cleave"   DMG 13→19 | 10/4/16 | charged (hold up to 40 f for +6 dmg and pierce 20), step-in cut
  ✕ "Bull Rush"         DMG 14 | 12/6/18 | moves 2.0, armor, shoulder-and-haft check that staggers (hitstun 24)
  △ "Ragnarök Spin"     DMG 9×3 | 16/(4+4+4)/24 | charged, multi-hit, moves 1.0, three turning cuts on the long haft
```

## GRABS (R1) — grab first, then branch

A grab is a real grab: it connects, the opponent is **held** (both fighters locked, paired
animation, victim mashing to break), and only then does the attacker choose how it ends. The
two grab moves per character are the two **branches** off one grab connect, not two separate
grabs — one connect clip, one hold loop, two finishes.

```
GRAB CONNECT · R1 — "Beard Hook"
STA 10 | 8/2/20 | grab range 1.8 | PROPERTIES: not a strike (no hitbox), whiffs are punishable
ANIM: the beard goes over the opponent's shoulder and hooks the neck, Bjorn hauls them in close
      and plants his feet. On connect both fighters enter the HELD state: root motion locked,
      relative offset held by the attacker, hits ignored by both.
HELD: victim mashes to break (GRAB_BREAK_WINDOW); on a break both are pushed apart and the
      attacker takes the longer recovery.

  BRANCH A · GRAB STRIKES · □ / ✕ while held — "Butt Ring Hammer"
  DMG 6+6+8 (20) | STA 6 per strike | each strike extends the hold ~16 f, 3 max
  ANIM: knee into the body, then the butt ring of the haft down on the head twice, the last one
        shoves them off. Victim stays standing and staggers away (no knockdown).

  BRANCH B · TAKEDOWN · △ / ↓ while held — "Hook and Haul"
  DMG 14 | STA 14 | KB 2.0 | knockdown
  ANIM: Bjorn pivots and hauls them past him onto the ground with both hands; victim lands on
        the back into Knockdown → Getup.
```

Later (not in the first pass): `DEFENSIVE THROW · R1+←` — "Viking Toss", DMG 10, counter-throw
only out of a block or a parry; the haft catches the incoming arm, step through, hip throw over
the haft.

## SIGNATURE · L1+L2+R1+R2 — "Ironside's Wrath"

```
DAMAGE 30% of opponent max HP | STA 60 | SEQUENCE HIGH → LOW
FIRST HIT (HIGH)   26/4/—: a full two-handed horizontal cut at head height with the whole body behind
                   it. If it lands → full combo. If blocked/dashed → second hit.
SECOND HIT (LOW)   8/4/24 after the first: the cut is reversed into a low scything cut at the knees
                   without stopping. If it lands → full combo.
FULL COMBO         The beard hooks the lead leg and rips it out (knockdown), Bjorn steps onto the chest,
                   raises the axe over his head and drives it down. Camera: low angle, hit-stop 8 f on
                   the final blow, then the opponent is pushed 2 units away.
MISS               Both defended: Bjorn is left in a 30 f recovery, fully punishable.
```

PASSIVE: "Berserkergang", see `Character_Stats.md`.

---

# 2. MIYAMOTO MUSASHI — katana (+ sheathed wakizashi)

Fighting style: Niten Ichi-ryū as a solo katana until the signature. Suri-ashi footwork
(feet never cross, no bob), cuts come from the hips and the wrists, the blade is always
between him and the opponent. Thrusts are his longest reach; diagonals (kesa) are his bread
and butter; the overhead is the finisher. Stance: low chūdan (`GUARD_KATANA`).

## NORMAL ATTACKS (high)

```
LIGHT · □ — "Tsuki"
DMG 7 | STA 5 | 7/3/10 [20] | PIERCE 5 | KB 0.3 | HITSTUN 12
HIT Straight | EVADE Left, Right
ANIM: straight two-handed thrust at the throat, lead foot slides 0.3, blade returns on a
      line. Reach 3.1, narrow box.

MEDIUM · ✕ — "Kesa Giri"
DMG 11 | STA 9 | 10/4/12 [26] | PIERCE 5 | KB 0.5 | HITSTUN 16
HIT Left | EVADE Right, Back
ANIM: diagonal cut from the opponent's right shoulder to left hip, blade lifts to jōdan
      during startup, hips drive the cut, ends in a low hassō. Reach 2.8, wide box.

HEAVY · △ — "Shinchokugiri"
DMG 16 | STA 16 | 14/4/14 [32] | PIERCE 15 | KB 0.8 | HITSTUN 20
HIT Straight | EVADE Left, Right
PROPERTIES: overhead
ANIM: blade raised straight above the head, a single vertical cut that stops at the
      navel; rear foot comes up to meet the front. Reach 2.8, tall narrow box.
```

## LOW / TRIP ATTACKS (L2 + button)

Strikes, not grabs — one hitbox, no hold. `knockdown` sends the victim through
`Knockdown` → `Getup`.

```
LOW LIGHT · L2+□ — "Ashi Giri"
DMG 6 | STA 6 | 8/3/11 [22] | PIERCE 0 | KB 0.2 | HITSTUN 12
HIT Right | EVADE Left, Back | PROPERTIES: low
ANIM: short drop, horizontal cut at shin height from his right to left, blade edge out.

LOW MEDIUM · L2+✕ — "Suri-age"
DMG 10 | STA 10 | 12/4/14 [30] | PIERCE 5 | KB 0.5 | HITSTUN 16
HIT Left | EVADE Right, Back | PROPERTIES: low
ANIM: sliding step in with a rising low diagonal from the opponent's left knee upward
      to the hip.

LOW HEAVY · L2+△ — "Kiri-otoshi Low"
DMG 15 | STA 16 | 17/4/20 [41] | PIERCE 10 | KB 0.9 | HITSTUN 24
HIT Straight | EVADE Back | PROPERTIES: low, knockdown
ANIM: drops to a deep crouch and cuts down-and-across at the knees in one motion, the
      blade finishing behind him. Reach 3.0, flat wide box.

TACKLE · L2+↓+△ — "Irimi"
DMG 10 | STA 50 | 10/5/22 | KB 1.8 | moves 2.5, interrupts attacks, knockdown, target invulnerable on standup
HIT Straight | EVADE Left, Right | PROPERTIES: mid, trip, no grab state
ANIM: blade held vertical in the right hand, left shoulder entry into the chest. A STRIKE that
      knocks down — the victim plays Knockdown, nothing is held.
```

## UPPERCUTS (↓ + button)

```
LIGHT UPPERCUT · ↓+□ — "Gyaku Kesa"
DMG 7 | STA 6 | 7/3/12 [22] | PIERCE 0 | KB 0.4 | HITSTUN 12
HIT Right | EVADE Left, Right | PROPERTIES: anti-overhead
ANIM: reverse diagonal rising from his left hip to the opponent's right shoulder.

MEDIUM UPPERCUT · ↓+✕ — "Kiriage"
DMG 11 | STA 10 | 10/4/15 [29] | PIERCE 5 | KB 0.7 | HITSTUN 16
HIT Straight | EVADE Left, Right | PROPERTIES: anti-overhead, launcher (short)
ANIM: blade starts low behind the right hip and rises vertically up the centreline,
      body rises from the knees.

HEAVY UPPERCUT · ↓+△ — "Tenchi" (heaven and earth)
DMG 15 | STA 18 | 15/5/20 [40] | PIERCE 15 | KB 1.1 | HITSTUN 26
HIT Straight | EVADE Left, Right, Back | PROPERTIES: launcher, knockdown
ANIM: crouching rising cut that continues over the head into a one-footed stance, the
      body fully extended. The finish pose holds 4 frames before recovery.
```

## SPECIAL ATTACKS — default (L1 + button)  ·  PARKED (2026-10-17)

Designed but not planned for the current pass - the user dropped these three from the
fortnight on 2026-10-17. Kept here for a later one.

```
LIGHT SPECIAL · L1+□ — "Nukitsuke"
DMG 10 | STA 12 | 9/3/14 [26] | PIERCE 5 | KB 0.5 | HITSTUN 14
HIT Left | EVADE Right, Back | PROPERTIES: moves 1.0
ANIM: quick-draw cut from a half-sheathed blade (the blade dips to the hip in the first
      4 frames, then snaps out horizontally with a sliding step).

MEDIUM SPECIAL · L1+✕ — "Nitō Cross"
DMG 7+8 (15) | STA 16 | 12/(3+3)/16 [34] | PIERCE 10 | KB 0.6 | HITSTUN 14, 18
HIT Right, Left | EVADE Back | PROPERTIES: multi-hit (2)
ANIM: the only non-signature move that draws the wakizashi: left hand draws it on the
      startup, scissor cut (katana right-to-left, wakizashi left-to-right), the
      wakizashi is re-sheathed in the recovery.

HEAVY SPECIAL · L1+△ — "Iaijutsu"
DMG 20 | STA 24 | 18/4/18 [40] | PIERCE 20 | KB 1.0 | HITSTUN 24
HIT Left | EVADE Right | PROPERTIES: counter-stance (frames 4–14: if struck, the hit is
            absorbed and the cut comes out immediately with +50% damage)
ANIM: sheathes fully, settles into a low draw stance with the hand on the hilt, then a
      single draw-cut across the chest. The whole move reads as stillness, then a flash.
```

## DIRECTIONAL SPECIALS

```
BACKWARD · L1+←
  □ "Hiki Giri"         DMG 8  | 6/3/12  | defensive: slide back 1.0 with a horizontal cut on the way out
  ✕ "Kaeshi"            DMG 12 | 8/3/14  | parry-riposte: 8 f parry window on startup, on parry → kesa cut with +30% damage
  △ "Mikazuki"          DMG 14 | 13/4/18 | crescent back-swing, hop back 1.5, blade comes around from behind the head

DOWN · L1+↓
  □ "Ashi Barai"        DMG 8  | 9/3/14  | low, foot sweep (no blade), trips on counter-hit
  ✕ "Hiza Giri"         DMG 11 | 11/4/16 | low, cut at the knee with a step through
  △ "Dokko"             DMG 15 | 15/4/22 | low, knockdown, drops to one knee and cuts the ankles

OVERHEAD · L1+↑
  □ "Kabuto Wari"       DMG 10 | 10/3/14 | overhead, helmet-splitter, short vertical from jōdan
  ✕ "Jōdan Otoshi"      DMG 13 | 13/4/18 | overhead, pierce 10, with a full step in
  △ "Fuji Otoshi"       DMG 19 | 22/5/26 | overhead, unblockable, blade raised for the whole startup, one committed step

FORWARD / CHARGED · L1+→
  □ "Ganryū Tsuki"      DMG 10→16 | 10/4/16 | charged lunge thrust, moves 1.5 (2.5 fully charged)
  ✕ "Suri-ashi Rush"    DMG 6+8  | 12/(3+3)/16 | moves 2.0, sliding double cut (kesa then gyaku-kesa)
  △ "Ichi no Tachi"     DMG 18→24 | 16/4/22 | charged, armor on release, one decisive horizontal cut, pierce 25 fully charged
```

## GRABS (R1) — grab first, then branch

Same structure as Bjorn: one connect, a held state, two finishes.

```
GRAB CONNECT · R1 — "Kumi-uchi"
STA 10 | 8/2/18 | grab range 1.5 | PROPERTIES: not a strike (no hitbox), whiffs are punishable
ANIM: left hand seizes the opponent's weapon arm at the wrist, blade kept vertical in the right
      hand, he steps off-line and holds them bent forward. On connect both enter the HELD state:
      root motion locked, offset held by the attacker, hits ignored by both.
HELD: victim mashes to break (GRAB_BREAK_WINDOW); on a break both are pushed apart and the
      attacker takes the longer recovery.

  BRANCH A · GRAB STRIKES · □ / ✕ while held — "Tsuka-ate"
  DMG 5+5+8 (18) | STA 6 per strike | each strike extends the hold ~16 f, 3 max
  ANIM: pommel into the face, knee to the ribs, then a pommel shove that breaks the grip.
        Victim staggers away standing (no knockdown).

  BRANCH B · TAKEDOWN · △ / ↓ while held — "Kote Gaeshi"
  DMG 8+10 (18) | STA 28 | knockdown
  ANIM: wrist lock on the weapon hand, turn-out that drops them face down, knee to the back,
        blade point to the neck (held 6 frames), release into Knockdown → Getup.
```

Later (not in the first pass): `DEFENSIVE THROW · R1+←` — "Irimi Nage", DMG 9, counter-throw out
of a block or a parry; enters past the attacking arm, hand to the chin, drops them backward over
his leg.

## SIGNATURE · L1+L2+R1+R2 — "Niten Ichi" (Two Heavens as One)

```
DAMAGE 28% of opponent max HP | STA 55 | SEQUENCE HIGH → LOW
FIRST HIT (HIGH)   26/4/—: draws the wakizashi; both blades cross horizontally at neck
                   height. If it lands → full combo. If defended → second hit.
SECOND HIT (LOW)   8/4/24 after the first: drops into a spinning low double cut, both
                   blades at knee height. If it lands → full combo.
FULL COMBO         Five-cut sequence: kesa (katana), gyaku-kesa (wakizashi), vertical
                   (katana), horizontal (wakizashi), finishing two-handed thrust with the
                   katana that pushes the opponent 2.5 units. Wakizashi re-sheathed in the
                   recovery with chiburi + nōtō (the blood flick and the return to the
                   scabbard). Hit-stop 8 f on the thrust.
MISS               Both defended: 30 f recovery, punishable.
```

PASSIVE: "Heihō", see `Character_Stats.md`.

---

# 3–7. SALADIN · KAREN MOOREHEAD · AYMAR BUWAKA · GENGHIS KHAN · RICHARD

Not yet designed. They get the same treatment once Bjorn and Miyamoto are approved.
Already decided in `Character_Stats.md`: weapons, archetypes, passives and stats. Anchors
for the move design:

- Saladin: scimitar + round shield; push kicks from `Design/ref_muaythai_pushkick.jpg`
  (teep: rear-leg, hip through, foot to the sternum) as his medium special and tackle.
  Shield slam throw. Best parry in the game.
- Karen: dagger normals, bow specials only (kneel + draw, 30 f aim, 8 unit range), 2
  specials total, highest crit.
- Aymar: spear thrusts are the longest reach (4.0), shield bashes in close, slow turns.
- Genghis: sabre heavies push (no counter), 4-hit chain cap, super armor on heavies.
- Richard: two full stance move sets (shield + sword, two-handed), stance switch on L1+↑
  (24 f), needs concept art first.

---

## Animation clip list per character (what the pipeline has to produce)

Locomotion and reactions (shared structure, authored per weapon):
`Idle`, `Walk_Forward`, `Walk_Back`, `Walk_Left`, `Walk_Right`, `Dash_Forward/Back/Left/Right`,
`Block_Hold`, `Block_Impact`, `React_Front/Left/Right`, `React_Low`, `Knockdown`, `Getup`,
`Launch` + `Fall`, `Throw_Victim` (generic), `Taunt`, `Victory`, `Defeat`.

Attacks: the 9 normals, 15 specials, 4 grapples and the signature (3 clips: high, low,
full) above = 31 attack clips per character. Multi-hit moves are one clip with several
active windows.

Total per character: about 50 clips. Bjorn + Miyamoto: about 100 clips for the first review.
