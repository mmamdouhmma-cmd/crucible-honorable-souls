# CRUCIBLE: Honorable Souls — Audio pass

Task saved 2026-10-23, to start the next session. Nothing here is built yet.
The user is supplying the **music** (menu/background + in-fight); everything else is
a reference hunt plus wiring. Importing reference/licensed audio is approved.

## 1. Menu and UI — my call

The user's words: *"menu and buttons i will leave it to you as a modern game developer,
some metal sound on select and navigating, any zip sound would work i guess."*

| Event | Character of the sound |
|---|---|
| Navigate (row to row) | Short metallic *zip* / scabbard tick. Quiet, ~60-90 ms, no tail. |
| Select / confirm | Heavier metal — a struck anvil or a blade seated home. |
| Back / cancel | The select sound reversed or dulled; lower, shorter. |
| Screen transition (wipe) | Low metal scrape under the existing wipe. |
| Boot — button press | One deep hit, timed to `BootExitRoutine` (0.45 s). |

Keep navigation well under the select in level — it fires constantly. Watch for
retrigger pile-up when a stick is held: the menus repeat on a timer.

## 2. Fight events — the user's onomatopoeia, verbatim

| Event | User's sound | Notes |
|---|---|---|
| Missed attack (whiff) | **"FEEWWWW"** | Air swish. Should scale with weapon weight — Bjorn's axe heavier than Miyamoto's katana. |
| Blocked attack | **"TINNN"** — metal clash | Fires on the block path in `CombatSystem.OnHit`. |
| Parried attack | **"TRINNN" with ECHO** | Brighter than a block and tailed — the parry is the showpiece. |
| Landed attack | **"QRUTTCH"** — a cutting sound | Flesh/armour cut, not a thud. |
| Successful dodge | **"WOOFFF"** | The dash-evade path. |

Hook points, all already in code — no new systems needed:
- `CombatSystem.OnHit` — the one damage path; it already branches dash-evade / block / hit.
- A whiff is the active window closing with no hit registered (`CombatSystem.Update`).
- The parry has its own counter window (`parryCounterWindow`).

## 3. Per-character voice — no words

*"each character should have his own voice, no talking, just sound with the attacks like
'HUhh' or 'TSSS' (for Miyamoto), just breathing out."*

A breath/effort layer per fighter, triggered with the attack, pitched to the character:

| Fighter | Direction |
|---|---|
| Miyamoto | "TSSS" — sharp, thin, hissed through the teeth. Katana precision. |
| Bjorn | "HUH" — deep, chesty, barbarian. The heaviest of the four. |
| Richard | Armoured grunt, clipped, a little muffled — the knight. |
| Salaadin | Mid, dry, controlled — the balanced one. |

Needs variation sets (3-4 per fighter per weight) or the repetition will be obvious
inside one round. Tie to attack weight: light = short exhale, heavy = full effort.

## 4. Death cries — Elden Ring inspired

*"on Death try to get something inspired from elden ring, each character different tone."*

Long, human, no words. Each fighter a distinct register so a K.O. is identifiable with
eyes shut. Fires on the K.O. path.

## 5. Music — the user supplies

Menu/background and in-fight. I wire the playback, loops and the menu-to-fight
transition; I do not source these.

## Open questions for when this starts

- Which audio middleware, if any — plain `AudioSource`/`AudioMixer`, or FMOD/Wwise?
  Plain Unity is the lighter answer and fits the web build; the lite-mobile build size
  budget (155 MB) argues for it.
- Compression per category: music streams, one-shots decompress on load.
  See the `unity:optimize-audio` guidance before importing a lot of clips.
- Licence terms of whatever reference library we pull from, since this may ship.
