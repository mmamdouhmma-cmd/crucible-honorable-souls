# Salaadin: the Mixamo re-rig

The files live in `ArtSource/Characters/Mixamo/` (gitignored, like all the source art):
`Salaadin_ToRig.fbx` goes UP to mixamo.com and `Salaadin_Rigged.fbx` comes back down.

`ArtSource/Blender/salaadin_body.py` then turns the result into
`Assets/_project/Characters/salaadin/Model/Salaadin_Body.fbx`.

## Why Salaadin is being re-rigged (2026-10-21)

The rig that shipped first came back from Mixamo with two marker-placement faults. Measured on
`Salaadin_Rigged.fbx` itself, before any of our scripts touch it:

| | Salaadin | should be | Richard (came out right) |
|---|---|---|---|
| upper arm, % of height | **8.0%** | ~15% | 14.6% |
| forearm, % of height | 11.6% | ~14% | 11.2% |
| elbow, along shoulder -> wrist | **41% / 41%** | ~52% | 57% / 56% |
| knee, along hip -> ankle | **54% / 48%** | ~50%, equal both sides | 46% / 47% |
| knee off the hip-ankle line | **5.8% / 9.1%** of height | ~2% | 2.1% / 2.4% |
| knee angle at bind | **149 / 135 deg** | ~170, equal | 167 / 165 |

In the game that read as an arm that bends up near the armpit and a lopsided crouch instead of a
T-pose. The MESH is not at fault - rendered on its own it is well proportioned and stands with
straight, symmetric legs - so this is a re-rig, not a re-model.

## Placing the markers

1. Upload `Salaadin_ToRig.fbx` (no skeleton, 5,283 verts, well inside Mixamo's limits).
2. **Elbows: about HALFWAY between shoulder and wrist**, not a third of the way along. Last time
   they landed at 41%, which is what shortened the upper arm to half its proper length.
3. **Knees: the same height on both legs**, on the kneecap, roughly halfway from hip to ankle.
   Last time the left and right were 6% apart and both sat off the leg axis.
4. **Check the preview before downloading.** If he crouches, if one leg bends more than the other,
   or if an arm folds near the armpit, go back and move the markers - that is the same fault again.
5. Download as FBX and save it here as `Salaadin_Rigged.fbx`, replacing the old one.

## Then, in the repo

Everything after the download is scripted:

```
blender -b --python ArtSource/Blender/salaadin_body.py -- \
    ArtSource/Characters/Mixamo/Salaadin_Rigged.fbx \
    Assets/_project/Characters/salaadin/Weapons/Salaadin_Falchion.fbx \
    Assets/_project/Characters/salaadin/Weapons/Salaadin_Shield.fbx \
    Assets/_project/Characters/salaadin/Model/Salaadin_Body.fbx <render_dir>
blender -b --python ArtSource/Blender/salaadin_clips.py -- <body.fbx> <SwordShieldPack> <out> <renders>
```
then in Unity: `Crucible > Characters > Salaadin > 1/2/3`, `Rebuild Player4 (Salaadin body)`,
`Wire special and riposte states`, `Use Mixamo packs`, `Wire combo strings`, `Salaadin > 4 Rewire`.

**The MOUNT numbers in `salaadin_body.py` are metres against a rig 1.04 units tall**, and the IK
targets in `salaadin_clips.py` are fractions of each limb's own reach. The fractions carry over to
new proportions by themselves; the mount offsets do not, so check the renders `salaadin_body.py`
writes and nudge `MOUNT` if the falchion or shield sits off the hand.
