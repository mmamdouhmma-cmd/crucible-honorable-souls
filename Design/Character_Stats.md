# CHARACTER STATS — CRUCIBLE: Honorable Souls

Filled 2026-10-11 from `Character_Stats_Master_Sheet.template.md`. Every number is the
template's own suggested value unless marked `(changed)`. Units follow the template: speed
35 = 3.5 units/s, multipliers 100 = 1.0x, dash distance 30 = 3.0 units. Frame data is at 60 fps.
Playtest will move all of this; the point of this file is that there are no blanks.

In-engine today (`Fighter`, `FighterStats`, `BalanceConfig`): health, stamina, block drain,
walk speed, dash distance, rotation speed. Armor, damage multiplier, crit, combo multipliers,
guard meter, parry and signature are not implemented yet and are listed here as targets.

---

## Roster summary

| # | Fighter | Archetype | Weapons (separate meshes) | Health | Armor | Dmg | Move spd | Atk spd | Weight |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Bjorn Ironside | Aggressive rushdown | Twin bearded axes (R + L) | 90 | 15 | 115 | 45 | 100 | 1.10 |
| 2 | Miyamoto Musashi | Fast duelist / counter | Katana (R), wakizashi (sheathed L hip, signature only) | 85 | 10 | 100 | 50 | 115 | 0.85 |
| 3 | Saladin | Balanced combo master | Scimitar (R), round shield (L), push kicks | 105 | 20 | 100 | 40 | 105 | 1.00 |
| 4 | Karen Moorehead | Skirmisher / range hybrid | Dagger (R), longbow (L, swapped in), quiver | 95 | 12 | 90 | 46 | 110 | 0.90 |
| 5 | Aymar Buwaka | Range control | Spear-scythe (R, two-handed for heavies), oval hide shield (L) | 100 | 17 | 105 | 38 | 95 | 1.05 |
| 6 | Genghis Khan | Skill-based tank | Curved sabre (R) | 115 | 22 | 110 | 35 | 95 | 1.15 |
| 7 | Richard the Lionheart | Heavy knight / stance switcher | Claymore (R or two-handed), kite shield (L, stance 1) | 125 | 28 | 110 | 30 | 85 | 1.20 |

Concept art exists for 1–6 in `Assets/_project/Art/ConceptArt/`. Richard has none yet and
needs a Krea T-pose sheet before his model can be generated.

---

## 1. BJORN IRONSIDE

```yaml
CHARACTER: Bjorn Ironside
ARCHETYPE: Aggressive Rushdown
WEAPON: Twin bearded axes (one per hand; the template said two-handed battle axe, the
        concept art and the current in-game model both show two one-handed axes, so twin
        axes it is)
ORIGIN: Scandinavia, 9th Century

VITALITY:   Max Health 90 | Armor 15% | Weight 1.1
OFFENSE:    Damage Multiplier 115 | Attack Speed 100 | Crit 10% | Crit Multiplier 150
MOVEMENT:   Movement Speed 45 | Dash Speed 100 | Dash Distance 30 | Dash I-Frames 9 | Rotation 450
COMBAT:     Combo 2-hit 110 | 3-hit 120 | 4-hit 130 | 5+ 140 | Parry Multiplier 130 | Dash Multiplier 110
STAMINA:    Max 100 | Recovery 14/s | Block Drain 5/s | Tackle Cost 55
DEFENSE:    Guard Max 90 | Guard Recovery 15/s | Block Damage Reduction 50%
SIGNATURE:  30% of opponent HP

PASSIVE: "Berserkergang"
  Below 40% health Bjorn's damage multiplier rises from 115 to 125 and his stamina
  recovery from 14 to 17/s. Landing any hit also refunds 4 stamina. He never gets
  slower, he gets angrier.
```

## 2. MIYAMOTO MUSASHI

```yaml
CHARACTER: Miyamoto Musashi
ARCHETYPE: Fast Duelist / Counter Master
WEAPON: Katana (right hand, two-handed grip on heavies). A wakizashi hangs sheathed on
        the left hip and is only drawn for the signature (Niten Ichi).
ORIGIN: Japan, 17th Century

VITALITY:   Max Health 85 | Armor 10% | Weight 0.85
OFFENSE:    Damage Multiplier 100 | Attack Speed 115 | Crit 12% | Crit Multiplier 160
MOVEMENT:   Movement Speed 50 | Dash Speed 115 | Dash Distance 35 | Dash I-Frames 12 | Rotation 540
COMBAT:     Combo 2-hit 110 | 3-hit 125 | 4-hit 140 | 5+ 150 | Parry Multiplier 130 | Dash Multiplier 150
STAMINA:    Max 100 | Recovery 16/s | Block Drain 5/s | Tackle Cost 50
DEFENSE:    Guard Max 85 | Guard Recovery 15/s | Block Damage Reduction 45%
SIGNATURE:  28% of opponent HP

PASSIVE: "Heihō" (the Way of Strategy)
  His dash-counter and parry windows are 3 frames wider than everyone else's, and the
  first attack after a successful counter deals +15% damage. Rewards reading the
  opponent, punishes mashing.
```

## 3. SALADIN

```yaml
CHARACTER: Salah ad-Din (Saladin)
ARCHETYPE: Balanced / Combo Master
WEAPON: Scimitar (right), round shield (left), push kicks (reference: Design/ref_muaythai_pushkick.jpg)
ORIGIN: Middle East, 12th Century

VITALITY:   Max Health 105 | Armor 20% | Weight 1.0
OFFENSE:    Damage Multiplier 100 | Attack Speed 105 | Crit 10% | Crit Multiplier 150
MOVEMENT:   Movement Speed 40 | Dash Speed 100 | Dash Distance 30 | Dash I-Frames 10 | Rotation 360
COMBAT:     Combo 2-hit 115 | 3-hit 130 | 4-hit 145 | 5+ 155 | Parry Multiplier 150 | Dash Multiplier 110
STAMINA:    Max 110 | Recovery 15/s | Block Drain 3/s | Tackle Cost 50
DEFENSE:    Guard Max 110 | Guard Recovery 18/s | Block Damage Reduction 55%
SIGNATURE:  30% of opponent HP

PASSIVE: "Sultan's Patience"
  A successful shield block refunds 3 stamina and shortens the block-impact recovery by
  2 frames, so Saladin is the only fighter who can block into a combo starter.
```

## 4. KAREN MOOREHEAD

```yaml
CHARACTER: Karen Moorehead
ARCHETYPE: Skirmisher / Range Hybrid
WEAPON: Dagger (right), longbow (left hand, drawn from the back for bow moves), quiver
ORIGIN: Ireland, Medieval

VITALITY:   Max Health 95 | Armor 12% | Weight 0.9
OFFENSE:    Damage Multiplier 90 | Attack Speed 110 | Crit 15% | Crit Multiplier 165
MOVEMENT:   Movement Speed 46 | Dash Speed 110 | Dash Distance 35 | Dash I-Frames 11 | Rotation 480
COMBAT:     Combo 2-hit 108 | 3-hit 118 | 4-hit 128 | 5+ 138 | Parry Multiplier 125 | Dash Multiplier 130
STAMINA:    Max 100 | Recovery 15/s | Block Drain 6/s | Tackle Cost 45
DEFENSE:    Guard Max 80 | Guard Recovery 14/s | Block Damage Reduction 40%
SIGNATURE:  29% of opponent HP
UNIQUE:     Bow Range 8 units | Bow Aim Time 30 frames (kneel + draw) | Bow vs Running +20% | 2 specials only

PASSIVE: "Hunter's Eye"
  Crit chance doubles (15% → 30%) on any hit that lands on an opponent's back or during
  their recovery frames. She is built to punish, not to trade.
```

## 5. AYMAR BUWAKA

```yaml
CHARACTER: Aymar Buwaka
ARCHETYPE: Range Control
WEAPON: Spear-scythe (iklwa-length shaft with a hooked blade; right hand, both hands on heavies), oval hide shield (left)
ORIGIN: Zulu-inspired

VITALITY:   Max Health 100 | Armor 17% | Weight 1.05
OFFENSE:    Damage Multiplier 105 | Attack Speed 95 | Crit 8% | Crit Multiplier 150
MOVEMENT:   Movement Speed 38 | Dash Speed 95 | Dash Distance 28 | Dash I-Frames 8 | Rotation 300
COMBAT:     Combo 2-hit 108 | 3-hit 115 | 4-hit 122 | 5+ 130 | Parry Multiplier 125 | Dash Multiplier 115
STAMINA:    Max 105 | Recovery 14/s | Block Drain 4/s | Tackle Cost 55
DEFENSE:    Guard Max 95 | Guard Recovery 14/s | Block Damage Reduction 45%
SIGNATURE:  30% of opponent HP
UNIQUE:     Spear Reach 4.0 units (longest melee) | Slash into movement +10% | Thrust into movement +15%

PASSIVE: "Keeper of Ground"
  Attacks that hit at the outer third of the spear's reach (tip hits) deal +12% and
  push the opponent back 0.5 units further. Standing in his range is the mistake.
```

## 6. GENGHIS KHAN

```yaml
CHARACTER: Genghis Khan
ARCHETYPE: Skill-Based Tank
WEAPON: Curved sabre (right), heavy lamellar coat
ORIGIN: Mongolia, 12th–13th Century

VITALITY:   Max Health 115 | Armor 22% | Weight 1.15
OFFENSE:    Damage Multiplier 110 | Attack Speed 95 | Crit 12% | Crit Multiplier 165
MOVEMENT:   Movement Speed 35 | Dash Speed 90 | Dash Distance 25 | Dash I-Frames 7 | Rotation 270
COMBAT:     Combo 2-hit 120 | 3-hit 135 | 4-hit 150 | 5+ not available (chains cap at 4) | Parry Multiplier 130 | Dash Multiplier 115
STAMINA:    Max 115 | Recovery 13/s | Block Drain 4/s | Tackle Cost 60
DEFENSE:    Guard Max 105 | Guard Recovery 16/s | Block Damage Reduction 55%
SIGNATURE:  33% of opponent HP
UNIQUE:     Heavy Push Effect: yes (heavies knock back 1.5, no counter possible) | Limited Combo System: yes | Big Hit Multiplier 1.5x on a completed chain

PASSIVE: "Iron Horde"
  Super armor (no flinch) during the startup of every heavy attack. He still takes the
  damage; he just does not stop.
```

## 7. RICHARD THE LIONHEART

```yaml
CHARACTER: Richard I of England
ARCHETYPE: Heavy Knight Tank / Stance Switcher
WEAPON: Claymore + kite shield (stance 1), two-handed claymore (stance 2, shield slung on the back)
ORIGIN: England, late 12th Century

VITALITY:   Max Health 125 | Armor 28% | Weight 1.2
OFFENSE:    Damage Multiplier 110 | Attack Speed 85 | Crit 8% | Crit Multiplier 165
MOVEMENT:   Movement Speed 30 | Dash Speed 85 | Dash Distance 22 | Dash I-Frames 5 | Rotation 240
COMBAT:     Combo 2-hit 115 | 3-hit 130 | 4-hit 145 | 5+ 150 | Parry Multiplier 130 | Dash Multiplier 105
STAMINA:    Max 120 | Recovery 12/s | Block Drain (shield) 3/s | Block Drain (2H) 5/s | Tackle Cost 65 | Stance Switch Cost 10
DEFENSE:    Guard Max 120 | Guard Recovery 18/s | Block Reduction (shield) 60% | Block Reduction (2H) 40%
SIGNATURE:  32% of opponent HP
UNIQUE:     Stance Switch L1 + ↑ | Switch Animation 0.4 s (24 f) | Switch Cooldown 1.0 s | Range 4.5 units (2nd longest)

STANCE 1 — SHIELD + SWORD:  Damage 100 | Attack Speed 95 | Block Reduction 60%
STANCE 2 — TWO-HANDED:      Damage 115 | Attack Speed 80 | Block Reduction 40%

PASSIVE: "Crusader's Resolve" (from the template)
  Damage taken −5% at 75–50% HP, −10% at 50–25%, −15% below 25%.
```

---

## Comparative table

| | Health | Armor | Dmg | Speed | Atk spd | Combo 5+ | Parry | Dash | Sig % |
|---|---|---|---|---|---|---|---|---|---|
| Bjorn | 90 | 15 | 115 | 45 | 100 | 140 | 130 | 110 | 30 |
| Miyamoto | 85 | 10 | 100 | 50 | 115 | 150 | 130 | 150 | 28 |
| Saladin | 105 | 20 | 100 | 40 | 105 | 155 | 150 | 110 | 30 |
| Karen | 95 | 12 | 90 | 46 | 110 | 138 | 125 | 130 | 29 |
| Aymar | 100 | 17 | 105 | 38 | 95 | 130 | 125 | 115 | 30 |
| Genghis | 115 | 22 | 110 | 35 | 95 | 150 (4-hit) | 130 | 115 | 33 |
| Richard | 125 | 28 | 110 | 30 | 85 | 150 | 130 | 105 | 32 |
| **Avg** | 102 | 17.7 | 104 | 40.6 | 100.7 | 145 | 132 | 119 | 30.3 |

Range ranking is unchanged from the template: Aymar > Richard > Miyamoto > Genghis > Saladin > Bjorn > Karen (dagger; bow is longest).
