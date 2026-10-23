# Audio sources and licences

Rebuilt by `scratchpad/build_audio.py` + `fix_audio.py` (2026-10-23); the editor menu item
`Crucible > Audio > Set up audio` wires whatever sits in these folders by name prefix.

- **Combat/swish_*, UI/wipe_*** — from "Swishes Sound Pack" by artisticdude,
  OpenGameArt, **CC0**. Wipe is a pitched-down edit.
- **Combat/block_*, UI/navigate_*, select_*, back_*, boot_*, count_*** — from
  "20 Sword Sound Effects" by StarNinjas, OpenGameArt, **CC0** (credit appreciated).
  The UI set is cut/pitched edits of the clashes.
- **Ambience/wind_01.wav** — "Wind, Realistic, A" by **InspectorJ** (Jonathan Shaw,
  freesound.org/people/InspectorJ/sounds/376415/), loop edit via AntumDeluge on OpenGameArt,
  **CC BY 3.0 — attribution required in shipped credits**. Tail-to-head crossfaded for the loop.
- **Combat/hit_*, Voice/** — generated 2026-10-23 with Seed Audio 1.0 via Krea
  (the user's account); split from longer takes by silence. No third-party licence.
- **Combat/parry_*, dodge_*** — synthesized in code 2026-10-23 (`ArtSource/Audio/make_parry_dodge.py`, additive
  struck-steel ring / band-swept whoosh with a rising chirp), replacing the Krea parries and the
  pitched-swish dodges so each event has its own unmistakable sound. No third-party licence.
- **Music/** — supplied by the user (menu* / fight*), not yet present.
