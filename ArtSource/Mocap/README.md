# Video → animation

Film the move, get a clip on the fighter. Built 2026-10-17, see `Design/Plan_2Weeks.md`.

## How to film

0. **Film the T-pose once, in its own video**, then one video per move. Repeat the move a few
   times from different angles in the same file - the tool finds the takes and one gets picked.
1. **Whole body in frame, feet included.** This is the one that bites: when the legs are out of
   frame MediaPipe does not fail, it *invents* them, and the fighter folds up. The extractor now
   prints a `LEGS BARELY TRACKED` warning and the Blender step leaves the legs in the rest stance
   rather than using junk — but then the clip has no footwork, so reshoot.
2. **The T-pose video**: stand straight, arms level and straight out, hold ~2 s. It measures
   your limb lengths, and every clip is scaled from it, so a sloppy T-pose skews everything.
3. **Move videos: start from the idle/guard**, do the move, pause at the end, then reset and
   repeat. Slow is good - it tracks better and the clip is retimed afterwards anyway. The opening
   idle frames are what set which way the fighter faces, so do not start mid-swing.
4. **Stand at about 45° to the camera**, not square on. Depth is the weak axis of single-camera
   capture; a 3/4 angle gives it something to work with.
5. Phone **horizontal and still** (prop it up), plain wall behind you, even light, no one else in
   frame. 1080p is plenty; 60 fps is better than 30 for fast swings.
6. One move per video. Name it after the move: `bjorn_light.mp4`, `miyamoto_grab_connect.mp4`.

Drop the files in `ArtSource/Mocap/raw/` (git-ignored, they are large).

## How to run it

The T-pose is filmed once, in its own video. Extract it first - every clip afterwards is
calibrated from it automatically:

```bash
ArtSource/Mocap/video_to_clip.sh --tpose raw/tpose.mp4
```

Then per move. The extractor prints the takes it found (one video usually holds the same move
three or four times, from different angles), so pick one and re-key with `--reuse`:

```bash
ArtSource/Mocap/video_to_clip.sh raw/bjorn_light.mp4 Bjorn_Light_Slash Bjorn --frames 17
#   ... 4 take(s) found - pick one with --trim START:END
#         take 1: --trim 88:143   (1.8s)
ArtSource/Mocap/video_to_clip.sh raw/bjorn_light.mp4 Bjorn_Light_Slash Bjorn --frames 17     --trim 88:143 --reuse
```

`--frames N` retimes the capture to the move's AttackData frame count, so filming slowly is fine
and in fact tracks better. `--reuse` skips the MediaPipe pass and re-keys from the landmarks
already extracted - use it when trying another take or another frame count. Other flags:
`--smooth 5`, `--key-every 2`, `--legs on|off`, `--face take|tpose`, `--no-export`.

**Angles.** Facing comes from the hips over the opening frames of the take being used, not from
the T-pose, so each repetition can be filmed from a different angle and still comes out facing
+X. That is also why a take should start from the idle: those frames are the zero of the body
yaw. `--face tpose` restores the old behaviour.

Then **look at the filmstrip** in `ArtSource/Characters/Renders/mocap/` before taking it into
Unity, and in Unity remember the clip rules in CLAUDE.md (Body Orientation, the explicit human
bone map, muscle range limits).

## What the two stages are

| | |
|---|---|
| `pose_extract.py` | video → `landmarks/<Clip>.json`. MediaPipe Tasks (`models/pose_landmarker_heavy.task`), per-frame world landmarks, visibility per body group, and the T-pose calibration (limb lengths, facing). Needs Python 3.12 — `~/AppData/Local/Programs/Python/Python312/python.exe` — because mediapipe has no 3.13 wheel and Blender's bundled Python is 3.13. Run it with `-I`. |
| `../Blender/video_mocap.py` | landmarks → keys on `<Char>_AnimMaster.blend` → FBX. Drives limbs by *direction*, with the length taken from the rig, so no IK target is unreachable. Keys hip height + body yaw, spine, neck, head, IK hands and IK feet. Borrows the `Rig` helper out of `author_clips.py` so the pose conventions have one source of truth, then exports with `bake_and_export()`. |

## What it does not do

- **No global translation.** MediaPipe world landmarks are hip-origin, and every clip in this
  project is in place anyway (the Animator has no root motion). Hip *height* does come through.
- **No wrist/ankle orientation.** Single-camera capture has no reliable hand orientation, so hand
  and foot rotation stay at rest. The weapon is aimed afterwards — by hand in Blender for the
  clip, and by `WeaponAim` at runtime.
- **No contact calibration.** The swing still needs its `swing_contacts.json` entry and the
  AttackData frame data, same as the Mixamo clips.

Expect to clean up: the capture gives timing, body attitude and weight shift; contact frames,
foot plant and the weapon arc are finished by hand.

## Shot list — Bjorn (two-handed Dane axe)

Prop: a broom or mop handle at roughly **0.71 × your height** (the axe is 0.700 against a 0.980
body in `Bjorn_AnimMaster.blend`), so ~1.25 m if you are 1.75 m. Slightly head-heavy is better
than a bare stick — tape something small to one end so the swing has weight. Matte, not white.

Every take: **T-pose ~2 s** (haft in the right hand, both arms straight and level, stick pointing
out along the arm), then come to guard, then the move, then hold still for a beat. Whole body in
frame including feet, with room overhead for the big swings. 2–3 takes in one file is fine — the
best one gets picked out with `--trim`.

Three videos (user's call, 2026-10-17 — the L1+button specials are parked):

| File | Move | Button | What to perform |
|---|---|---|---|
| `bjorn_light.mp4` | Light Slash | J | plain diagonal cut from the right, back to guard |
| `bjorn_heavy.mp4` | Mountain Breaker | L | axe goes right behind the back, then the slowest, biggest overhead chop |
| `bjorn_combo.mp4` | the two chained | J → L | the light cut, then **without resetting to guard** flow straight on into the overhead, one continuous action |

Perform them at a natural speed — the clip gets retimed to the move's frame data afterwards
(`--frames`), so the capture does not have to match any count.

Film `bjorn_light.mp4` first and look at it before doing the rest: there is a Mixamo version of it
in game, so it is the honest test of whether the capture is worth cleaning up. Walking forward
during a take is fine — global translation is not captured (clips are in place) — just stay in
frame.

The combo take is what tells the cancel system what the transition should look like: it becomes
either a dedicated follow-up clip (`Bjorn_Heavy_FromLight`) or the reference for how long the
blend out of the light's recovery should be.
