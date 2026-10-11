# 📊 CHARACTER STATS MASTER SHEET
## CRUCIBLE: Honorable Souls

Fill in the numbers for each character. All values are tweakable - playtest will determine final balance.

---

## 📋 STAT REFERENCE GUIDE

### Stat Definitions
```
HEALTH:           Max HP (85-125 range, integer)
ARMOR:            Damage reduction percentage (10-28%)
DAMAGE MULTIPLIER: All attack damage modifier (90-115, where 100 = 1.0x)
MOVEMENT SPEED:   Walking speed (30-50, where 35 = 3.5 units/sec)
ATTACK SPEED:     Animation speed multiplier (85-115, where 100 = baseline)
DASH SPEED:       Dash distance multiplier (85-115)
DASH DISTANCE:    How far dash travels (22-40, where 30 = 3.0 units)
DASH I-FRAMES:    Invincibility frames during dash (5-15 frames)
ROTATION SPEED:   How fast character auto-rotates (240-540 degrees/sec)
PARRY MULTIPLIER: Counter damage after parry (110-150, where 120 = 1.2x)
DASH MULTIPLIER:  Counter damage after dash (105-150)
COMBO MULTIPLIER: Bonus damage in combos (110-155 per combo step)
STAMINA RECOVERY: Stamina regenerated per second (12-20)
GUARD MAX:        Max guard meter (80-120)
SIGNATURE DMG:    Percentage of opponent HP (28-33%)
```

---

## 1. BJORN IRONSIDE (Aggressive Berserker)

```yaml
─────────────────────────────────────────
CHARACTER: Bjorn Ironside
ARCHETYPE: Aggressive Rushdown
WEAPON: Battle Axe (Two-Handed)
ORIGIN: Scandinavia, 9th Century
─────────────────────────────────────────

VITALITY:
  Max Health:          [____] (suggested: 90)
  Armor:               [____]% (suggested: 15)
  Weight:              [____] (1.1 = heavy)

OFFENSE:
  Damage Multiplier:   [____] (suggested: 115)
  Attack Speed:        [____] (suggested: 100)
  Critical Chance:     [____]% (suggested: 10)
  Critical Multiplier: [____] (suggested: 150)

MOVEMENT:
  Movement Speed:      [____] (suggested: 45)
  Dash Speed:          [____] (suggested: 100)
  Dash Distance:       [____] (suggested: 30)
  Dash I-Frames:       [____] frames (suggested: 9)
  Rotation Speed:      [____] deg/sec (suggested: 450)

COMBAT:
  Combo Multiplier 2-hit: [____] (suggested: 110)
  Combo Multiplier 3-hit: [____] (suggested: 120)
  Combo Multiplier 4-hit: [____] (suggested: 130)
  Combo Multiplier 5+:    [____] (suggested: 140)
  Parry Multiplier:    [____] (suggested: 130)
  Dash Multiplier:     [____] (suggested: 110)

STAMINA:
  Max Stamina:         [____] (suggested: 100)
  Stamina Recovery:    [____] /sec (suggested: 14)
  Block Drain:         [____] /sec (suggested: 5)
  Tackle Cost:         [____] (suggested: 55)

DEFENSE:
  Guard Max:           [____] (suggested: 90)
  Guard Recovery:      [____] /sec (suggested: 15)
  Block Damage Reduction: [____]% (suggested: 50)

SIGNATURE:
  Signature Damage:    [____]% of opponent HP (suggested: 30)

PASSIVE ABILITY:
  Name: ____________________
  Description: ____________________
  Effect: ____________________

NOTES:
  Strong Points: Highest damage multiplier, momentum-based
  Weak Points: Low health, weak armor, predictable
```

---

## 2. MIYAMOTO MUSASHI (Speed/Counter Specialist)

```yaml
─────────────────────────────────────────
CHARACTER: Miyamoto Musashi
ARCHETYPE: Fast Duelist / Counter Master
WEAPON: Katana
ORIGIN: Japan, 17th Century
─────────────────────────────────────────

VITALITY:
  Max Health:          [____] (suggested: 85)
  Armor:               [____]% (suggested: 10)
  Weight:              [____] (0.85 = light)

OFFENSE:
  Damage Multiplier:   [____] (suggested: 100)
  Attack Speed:        [____] (suggested: 115)
  Critical Chance:     [____]% (suggested: 12)
  Critical Multiplier: [____] (suggested: 160)

MOVEMENT:
  Movement Speed:      [____] (suggested: 50)
  Dash Speed:          [____] (suggested: 115)
  Dash Distance:       [____] (suggested: 35)
  Dash I-Frames:       [____] frames (suggested: 12)
  Rotation Speed:      [____] deg/sec (suggested: 540 - HIGHEST)

COMBAT:
  Combo Multiplier 2-hit: [____] (suggested: 110)
  Combo Multiplier 3-hit: [____] (suggested: 125)
  Combo Multiplier 4-hit: [____] (suggested: 140)
  Combo Multiplier 5+:    [____] (suggested: 150)
  Parry Multiplier:    [____] (suggested: 130)
  Dash Multiplier:     [____] (suggested: 150 - HIGHEST IN GAME)

STAMINA:
  Max Stamina:         [____] (suggested: 100)
  Stamina Recovery:    [____] /sec (suggested: 16)
  Block Drain:         [____] /sec (suggested: 5)
  Tackle Cost:         [____] (suggested: 50)

DEFENSE:
  Guard Max:           [____] (suggested: 85)
  Guard Recovery:      [____] /sec (suggested: 15)
  Block Damage Reduction: [____]% (suggested: 45)

SIGNATURE:
  Signature Damage:    [____]% of opponent HP (suggested: 28)

PASSIVE ABILITY:
  Name: ____________________
  Description: ____________________
  Effect: ____________________

NOTES:
  Strong Points: Fastest speed, highest counter damage, longest katana reach
  Weak Points: Lowest health, weakest armor, fragile
```

---

## 3. SALADIN (Defensive Combo Specialist)

```yaml
─────────────────────────────────────────
CHARACTER: Salah ad-Din (Saladin)
ARCHETYPE: Balanced / Combo Master
WEAPON: Scimitar + Shield + Push Kicks
ORIGIN: Middle East, 12th Century
─────────────────────────────────────────

VITALITY:
  Max Health:          [____] (suggested: 105)
  Armor:               [____]% (suggested: 20)
  Weight:              [____] (1.0 = balanced)

OFFENSE:
  Damage Multiplier:   [____] (suggested: 100)
  Attack Speed:        [____] (suggested: 105)
  Critical Chance:     [____]% (suggested: 10)
  Critical Multiplier: [____] (suggested: 150)

MOVEMENT:
  Movement Speed:      [____] (suggested: 40)
  Dash Speed:          [____] (suggested: 100)
  Dash Distance:       [____] (suggested: 30)
  Dash I-Frames:       [____] frames (suggested: 10)
  Rotation Speed:      [____] deg/sec (suggested: 360)

COMBAT:
  Combo Multiplier 2-hit: [____] (suggested: 115)
  Combo Multiplier 3-hit: [____] (suggested: 130)
  Combo Multiplier 4-hit: [____] (suggested: 145)
  Combo Multiplier 5+:    [____] (suggested: 155 - BEST COMBO MASTER)
  Parry Multiplier:    [____] (suggested: 150 - HIGHEST IN GAME)
  Dash Multiplier:     [____] (suggested: 110)

STAMINA:
  Max Stamina:         [____] (suggested: 110)
  Stamina Recovery:    [____] /sec (suggested: 15)
  Block Drain:         [____] /sec (suggested: 3 - LOWEST, shield specialist)
  Tackle Cost:         [____] (suggested: 50)

DEFENSE:
  Guard Max:           [____] (suggested: 110)
  Guard Recovery:      [____] /sec (suggested: 18)
  Block Damage Reduction: [____]% (suggested: 55)

SIGNATURE:
  Signature Damage:    [____]% of opponent HP (suggested: 30)

PASSIVE ABILITY:
  Name: ____________________
  Description: ____________________
  Effect: ____________________

NOTES:
  Strong Points: Best parry counter, longest combos, defensive specialist
  Weak Points: Slower than fast characters, methodical playstyle required
```

---

## 4. KAREN MOOREHEAD (Hybrid Range / Skill-Based)

```yaml
─────────────────────────────────────────
CHARACTER: Karen Moorehead
ARCHETYPE: Skirmisher / Range Hybrid
WEAPON: Dagger + Bow (Kneeling Shot)
ORIGIN: Ireland, Medieval Period
─────────────────────────────────────────

VITALITY:
  Max Health:          [____] (suggested: 95)
  Armor:               [____]% (suggested: 12)
  Weight:              [____] (0.9 = light-medium)

OFFENSE:
  Damage Multiplier:   [____] (suggested: 90 - LOWEST)
  Attack Speed:        [____] (suggested: 110)
  Critical Chance:     [____]% (suggested: 15 - HIGHEST)
  Critical Multiplier: [____] (suggested: 165)

MOVEMENT:
  Movement Speed:      [____] (suggested: 46)
  Dash Speed:          [____] (suggested: 110)
  Dash Distance:       [____] (suggested: 35)
  Dash I-Frames:       [____] frames (suggested: 11)
  Rotation Speed:      [____] deg/sec (suggested: 480)

COMBAT:
  Combo Multiplier 2-hit: [____] (suggested: 108)
  Combo Multiplier 3-hit: [____] (suggested: 118)
  Combo Multiplier 4-hit: [____] (suggested: 128)
  Combo Multiplier 5+:    [____] (suggested: 138)
  Parry Multiplier:    [____] (suggested: 125)
  Dash Multiplier:     [____] (suggested: 130 - dagger + bow combo)

STAMINA:
  Max Stamina:         [____] (suggested: 100)
  Stamina Recovery:    [____] /sec (suggested: 15)
  Block Drain:         [____] /sec (suggested: 6 - no shield)
  Tackle Cost:         [____] (suggested: 45)

DEFENSE:
  Guard Max:           [____] (suggested: 80)
  Guard Recovery:      [____] /sec (suggested: 14)
  Block Damage Reduction: [____]% (suggested: 40)

SIGNATURE:
  Signature Damage:    [____]% of opponent HP (suggested: 29)

UNIQUE MECHANICS:
  Bow Range:           [____] units (suggested: 8 - long range)
  Bow Aim Time:        [____] frames (suggested: 30 - kneel + aim)
  Bow vs Running Bonus: [____]% (suggested: 20)
  Number of Specials:  2 only (lowest count)

PASSIVE ABILITY:
  Name: ____________________
  Description: ____________________
  Effect: ____________________

NOTES:
  Strong Points: Range mixing, unpredictable, high skill ceiling
  Weak Points: Lowest damage, requires skilled player
```

---

## 5. AYMAR BUWAKA (Zulu Spear Range)

```yaml
─────────────────────────────────────────
CHARACTER: Aymar Buwaka
ARCHETYPE: Range Control
WEAPON: Spear-Scythe Hybrid + Shield
ORIGIN: African Tradition (Zulu-inspired)
─────────────────────────────────────────

VITALITY:
  Max Health:          [____] (suggested: 100)
  Armor:               [____]% (suggested: 17)
  Weight:              [____] (1.05 = medium-heavy)

OFFENSE:
  Damage Multiplier:   [____] (suggested: 105)
  Attack Speed:        [____] (suggested: 95)
  Critical Chance:     [____]% (suggested: 8)
  Critical Multiplier: [____] (suggested: 150)

MOVEMENT:
  Movement Speed:      [____] (suggested: 38)
  Dash Speed:          [____] (suggested: 95)
  Dash Distance:       [____] (suggested: 28)
  Dash I-Frames:       [____] frames (suggested: 8)
  Rotation Speed:      [____] deg/sec (suggested: 300 - slower with polearm)

COMBAT:
  Combo Multiplier 2-hit: [____] (suggested: 108)
  Combo Multiplier 3-hit: [____] (suggested: 115)
  Combo Multiplier 4-hit: [____] (suggested: 122)
  Combo Multiplier 5+:    [____] (suggested: 130)
  Parry Multiplier:    [____] (suggested: 125)
  Dash Multiplier:     [____] (suggested: 115)

STAMINA:
  Max Stamina:         [____] (suggested: 105)
  Stamina Recovery:    [____] /sec (suggested: 14)
  Block Drain:         [____] /sec (suggested: 4)
  Tackle Cost:         [____] (suggested: 55)

DEFENSE:
  Guard Max:           [____] (suggested: 95)
  Guard Recovery:      [____] /sec (suggested: 14)
  Block Damage Reduction: [____]% (suggested: 45)

SIGNATURE:
  Signature Damage:    [____]% of opponent HP (suggested: 30)

UNIQUE MECHANICS:
  Spear Reach:         [____] units (suggested: 4 - LONGEST melee)
  Slash Damage Bonus into Movement: [____]% (suggested: 10)
  Thrust Damage Bonus into Movement: [____]% (suggested: 15)

PASSIVE ABILITY:
  Name: ____________________
  Description: ____________________
  Effect: ____________________

NOTES:
  Strong Points: Longest reach, heaviest individual hits, space control
  Weak Points: Slower combos, slower rotation
```

---

## 6. GENGHIS KHAN (Tank Heavy Hitter)

```yaml
─────────────────────────────────────────
CHARACTER: Genghis Khan
ARCHETYPE: Skill-Based Tank
WEAPON: Curved Sabre
ORIGIN: Mongolia, 12th-13th Century
─────────────────────────────────────────

VITALITY:
  Max Health:          [____] (suggested: 115)
  Armor:               [____]% (suggested: 22)
  Weight:              [____] (1.15)

OFFENSE:
  Damage Multiplier:   [____] (suggested: 110)
  Attack Speed:        [____] (suggested: 95 - slower)
  Critical Chance:     [____]% (suggested: 12)
  Critical Multiplier: [____] (suggested: 165)

MOVEMENT:
  Movement Speed:      [____] (suggested: 35)
  Dash Speed:          [____] (suggested: 90)
  Dash Distance:       [____] (suggested: 25)
  Dash I-Frames:       [____] frames (suggested: 7)
  Rotation Speed:      [____] deg/sec (suggested: 270)

COMBAT:
  Combo Multiplier 2-hit: [____] (suggested: 120 - higher per step)
  Combo Multiplier 3-hit: [____] (suggested: 135)
  Combo Multiplier 4-hit: [____] (suggested: 150 - VERY HIGH)
  Combo Multiplier 5+:    [____] (limited - hard to achieve)
  Parry Multiplier:    [____] (suggested: 130)
  Dash Multiplier:     [____] (suggested: 115)

STAMINA:
  Max Stamina:         [____] (suggested: 115)
  Stamina Recovery:    [____] /sec (suggested: 13 - slower)
  Block Drain:         [____] /sec (suggested: 4)
  Tackle Cost:         [____] (suggested: 60)

DEFENSE:
  Guard Max:           [____] (suggested: 105)
  Guard Recovery:      [____] /sec (suggested: 16)
  Block Damage Reduction: [____]% (suggested: 55)

SIGNATURE:
  Signature Damage:    [____]% of opponent HP (suggested: 33)

UNIQUE MECHANICS:
  Heavy Push Effect: [Yes/No] (heavy attacks push opponent, prevents counter)
  Limited Combo System: [Yes/No] (specific chains only)
  Big Hit Multiplier: [____] (suggested: 1.5x on chains achieved)

PASSIVE ABILITY:
  Name: ____________________
  Description: ____________________
  Effect: ____________________

NOTES:
  Strong Points: Highest health (was), best armor, heaviest hits
  Weak Points: Slow, limited combos, vulnerable to speed
```

---

## 7. RICHARD THE LIONHEART (Heavy Knight Tank / Stance Switcher)

```yaml
─────────────────────────────────────────
CHARACTER: Richard the Lionheart (Richard I of England)
ARCHETYPE: Heavy Knight Tank / Stance Switcher
WEAPON: Greatsword (Claymore) + Shield (toggleable)
ORIGIN: England, Late 12th Century (Crusader King)
─────────────────────────────────────────

VITALITY:
  Max Health:          [____] (suggested: 125 - HIGHEST IN ROSTER)
  Armor:               [____]% (suggested: 28 - HIGHEST IN ROSTER)
  Weight:              [____] (1.2 - heaviest)

OFFENSE:
  Damage Multiplier:   [____] (suggested: 110 - same as Genghis)
  Attack Speed:        [____] (suggested: 85 - SLOWEST)
  Critical Chance:     [____]% (suggested: 8)
  Critical Multiplier: [____] (suggested: 165)

MOVEMENT:
  Movement Speed:      [____] (suggested: 30 - SLOWEST)
  Dash Speed:          [____] (suggested: 85 - slowest dash)
  Dash Distance:       [____] (suggested: 22 - shortest dashes)
  Dash I-Frames:       [____] frames (suggested: 5 - fewest)
  Rotation Speed:      [____] deg/sec (suggested: 240 - SLOWEST)

COMBAT:
  Combo Multiplier 2-hit: [____] (suggested: 115)
  Combo Multiplier 3-hit: [____] (suggested: 130)
  Combo Multiplier 4-hit: [____] (suggested: 145)
  Combo Multiplier 5+:    [____] (suggested: 150)
  Parry Multiplier:    [____] (suggested: 130)
  Dash Multiplier:     [____] (suggested: 105)

STAMINA:
  Max Stamina:         [____] (suggested: 120 - HIGHEST)
  Stamina Recovery:    [____] /sec (suggested: 12 - slow recovery)
  Block Drain (Shield): [____] /sec (suggested: 3 - best shield blocker)
  Block Drain (2H):    [____] /sec (suggested: 5)
  Tackle Cost:         [____] (suggested: 65)
  Stance Switch Cost:  [____] (suggested: 10)

DEFENSE:
  Guard Max:           [____] (suggested: 120 - HIGHEST)
  Guard Recovery:      [____] /sec (suggested: 18)
  Block Damage Reduction (Shield Stance): [____]% (suggested: 60 - HIGHEST)
  Block Damage Reduction (2H Stance):     [____]% (suggested: 40)

SIGNATURE:
  Signature Damage:    [____]% of opponent HP (suggested: 32)

UNIQUE MECHANICS:
  Stance Switch Input:    L1 + ↑
  Stance Switch Animation: [____] seconds (suggested: 0.4)
  Stance Switch Cooldown: [____] seconds (suggested: 1.0)
  Range Tier:             2nd longest (after Aymar)
  Range:                  [____] units (suggested: 4.5)

STANCE 1: SHIELD + SWORD (Defensive)
  Damage Multiplier:    [____] (suggested: 100 - reduced)
  Attack Speed:         [____] (suggested: 95 - slightly faster)
  Block Reduction:      [____]% (suggested: 60)
  Best for: Counter-play, defensive turtles

STANCE 2: TWO-HANDED CLAYMORE (Offensive)
  Damage Multiplier:    [____] (suggested: 115 - bonus)
  Attack Speed:         [____] (suggested: 80 - even slower)
  Block Reduction:      [____]% (suggested: 40 - no shield)
  Best for: Aggressive damage, range

PASSIVE: "Crusader's Resolve"
  Effect: Damage reduction increases as HP decreases
  - 100-75% HP: Normal damage taken
  - 75-50% HP: -5% damage taken
  - 50-25% HP: -10% damage taken
  - 25-0% HP: -15% damage taken

NOTES:
  Strong Points: Highest HP and armor, longest sword range, stance versatility
  Weak Points: Slowest in everything (movement, attacks, dashes, rotation)
  
  RIVALRY: Real-life enemy of Saladin during Third Crusade.
  - Both honorable warriors
  - Both fight with shields (different styles)
  - Different cultures (English vs Middle Eastern)
  - Built-in narrative for tournaments
```

---

## 📊 COMPARATIVE BALANCE TABLE (7 CHARACTERS)

Use this to ensure no character is overpowered:

```
                Health  Armor  DMG   Speed  AttackSpd  Combos  Parry   Dash   Sig%
───────────────────────────────────────────────────────────────────────────────────
Bjorn          [___]   [__]   [__]  [__]   [___]      [___]   [___]   [___]  [_]
Miyamoto       [___]   [__]   [__]  [__]   [___]      [___]   [___]   [___]  [_]
Saladin        [___]   [__]   [__]  [__]   [___]      [___]   [___]   [___]  [_]
Karen          [___]   [__]   [__]  [__]   [___]      [___]   [___]   [___]  [_]
Aymar          [___]   [__]   [__]  [__]   [___]      [___]   [___]   [___]  [_]
Genghis        [___]   [__]   [__]  [__]   [___]      [___]   [___]   [___]  [_]
Richard        [___]   [__]   [__]  [__]   [___]      [___]   [___]   [___]  [_]
───────────────────────────────────────────────────────────────────────────────────
SUM             [___]   [__]   [__]  [__]   [___]      [___]   [___]   [___]  [_]
AVERAGE         [___]   [__]   [__]  [__]   [___]      [___]   [___]   [___]  [_]
```

**Balance Check:**
- Each character should have at least 3 STRONG areas
- Each character should have at least 2 WEAK areas
- No character should be top in ALL areas
- Sum of all stats should be similar across characters

---

## 🎯 RANGE RANKING

```
WEAPON RANGE (longest to shortest):
1. Aymar Buwaka     - Spear-Scythe (LONGEST)
2. Richard          - Greatsword (especially in 2-handed stance)
3. Miyamoto Musashi - Katana
4. Genghis Khan     - Sabre
5. Saladin          - Scimitar
6. Bjorn            - Battle Axe
7. Karen            - Dagger (BUT longest with bow)
```

---

## 🎯 BALANCE GUIDELINES

### Trade-off Rules
```
✓ HIGH HEALTH = LOWER damage or speed
✓ HIGH SPEED = LOWER health
✓ HIGH DAMAGE = LOWER health or speed
✓ HIGH ARMOR = SLOWER speed
✓ LONG RANGE = SLOWER combos
✓ COMBO MASTER = LOWER per-hit damage
✓ STANCE SWITCHER (Richard) = trade-off between offense/defense
```

### Skill Floor vs Ceiling
```
EASY TO PLAY (lower skill floor):
- Bjorn (forward, attack, simple)
- Aymar (range advantage protects)
- Richard (tank protects mistakes - in shield stance)

MEDIUM:
- Saladin (combo learning)
- Genghis (timing-based)

HARD TO MASTER (higher skill ceiling):
- Miyamoto (counter timing)
- Karen (range mixing, lowest damage)
- Richard (mastering stance switching)
```

---

## 📝 VALIDATION CHECKLIST

Before locking in stats, verify:

- [ ] Each character has clear STRENGTHS (3+)
- [ ] Each character has clear WEAKNESSES (2+)
- [ ] No character beats everyone in everything
- [ ] Each character has at least 1 favorable matchup
- [ ] Each character has at least 1 unfavorable matchup
- [ ] Health range: 85-125 (with Richard at top)
- [ ] Armor range: 10-28 (with Richard at top)
- [ ] Damage multiplier range: 90-115
- [ ] Speed range: 30-50 (with Richard at bottom)
- [ ] Skill floors vary (some easier, some harder)
- [ ] Richard's stance system feels balanced

---

## 🎮 FINAL NOTES

**Remember:** These are STARTING values. Playtesting will reveal:
- Which character feels overpowered
- Which feels weak
- Which numbers need adjusting

**Adjust during Phase 2-3 playtesting.** Iterate 100-150 times.

**This document is your DESIGN BIBLE** - reference it whenever balancing.

🎮⚔️🗡️
