"""Video -> 3D pose landmarks JSON, for the CRUCIBLE animation pipeline.

Reads a phone video of the user performing a move (T-pose held at the start, white wall
behind, 3/4 angle) and writes one JSON file of MediaPipe world landmarks per frame, plus
the calibration measured from the held T-pose.

Run with the dedicated Python 3.12 (Blender's 3.13 has no mediapipe wheel), and with -I so
nothing is imported from the video folder:

    PY=~/AppData/Local/Programs/Python/Python312/python.exe
    "$PY" -I ArtSource/Mocap/pose_extract.py ArtSource/Mocap/raw/bjorn_light.mp4 \
        ArtSource/Mocap/landmarks/bjorn_light.json

Coordinate convention of what is written (MediaPipe world landmarks, kept raw):
  metres, origin at the midpoint of the hips, x = image right, y = image DOWN,
  z = toward the camera negative. video_mocap.py converts this to the rig's
  +X forward / +Y left / +Z up before it keys anything.
"""

import argparse
import json
import math
import os
import sys

import cv2
import numpy as np
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision
import mediapipe as mp

# MediaPipe Pose landmark indices used for calibration and reporting.
L_SHOULDER, R_SHOULDER = 11, 12
L_ELBOW, R_ELBOW = 13, 14
L_WRIST, R_WRIST = 15, 16
L_HIP, R_HIP = 23, 24
L_KNEE, R_KNEE = 25, 26
L_ANKLE, R_ANKLE = 27, 28

NAMES = {
    L_SHOULDER: "L_shoulder", R_SHOULDER: "R_shoulder",
    L_ELBOW: "L_elbow", R_ELBOW: "R_elbow",
    L_WRIST: "L_wrist", R_WRIST: "R_wrist",
    L_HIP: "L_hip", R_HIP: "R_hip",
    L_KNEE: "L_knee", R_KNEE: "R_knee",
    L_ANKLE: "L_ankle", R_ANKLE: "R_ankle",
}

DEFAULT_MODEL = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                             "models", "pose_landmarker_heavy.task")


def dist(a, b):
    return math.dist(a, b)


def tpose_score(world):
    """How much a frame looks like a T-pose: arms out sideways, level with the shoulders.

    Returns 0..1, where 1 is both arms fully horizontal and spread. y is image-down, so a
    level arm has a small |dy| compared with its length.
    """
    ls, rs = world[L_SHOULDER], world[R_SHOULDER]
    lw, rw = world[L_WRIST], world[R_WRIST]
    span = dist(ls, rs)
    if span < 1e-4:
        return 0.0
    score = 1.0
    for sh, wr in ((ls, lw), (rs, rw)):
        arm = dist(sh, wr)
        if arm < 1e-4:
            return 0.0
        sideways = abs(wr[0] - sh[0]) / arm      # how much of the arm is horizontal
        level = 1.0 - min(1.0, abs(wr[1] - sh[1]) / arm)
        reach = min(1.0, arm / (1.15 * span))    # a straight arm is ~1.2x shoulder width
        score = min(score, sideways * level * reach)
    return score


def find_takes(frames, fps, quiet_ratio=0.18, min_len=0.25, gap=0.35):
    """Split a video of several repetitions into takes, by when the hands are moving.

    One video usually holds the same move done three or four times from different angles, so
    the clip has to be cut to ONE of them (--trim). This reports the candidates: runs of hand
    movement separated by stillness.
    """
    speeds, idx = [], []
    prev = None
    for f in frames:
        if not f["world"]:
            prev = None
            continue
        w = f["world"]
        hands = (np.array(w[L_WRIST]), np.array(w[R_WRIST]))
        if prev is not None:
            speeds.append(max(float(np.linalg.norm(h - p)) for h, p in zip(hands, prev)))
            idx.append(f["i"])
        prev = hands
    if not speeds:
        return []

    s = np.array(speeds)
    if len(s) >= 5:                                  # smooth over ~5 frames
        s = np.convolve(s, np.ones(5) / 5.0, mode="same")
    threshold = quiet_ratio * float(s.max())
    runs, run = [], None
    for i, v in zip(idx, s):
        if v >= threshold:
            run = [i, i] if run is None else [run[0], i]
        elif run is not None:
            runs.append(run)
            run = None
    if run is not None:
        runs.append(run)

    merged = []
    for r in runs:                                   # join runs split by a short pause
        if merged and (r[0] - merged[-1][1]) / fps <= gap:
            merged[-1][1] = r[1]
        else:
            merged.append(list(r))
    takes = [{"start": a, "end": b, "seconds": (b - a + 1) / fps}
             for a, b in merged if (b - a + 1) / fps >= min_len]
    return takes


def calibrate(frames, threshold=0.55, min_run=5):
    """Find the held T-pose at the start and measure the performer from it."""
    scored = [(f["i"], tpose_score(f["world"])) for f in frames if f["world"]]
    if not scored:
        return None

    best_run, run = None, []
    for idx, score in scored:
        if score >= threshold:
            run.append(idx)
            continue
        if len(run) >= min_run and best_run is None:
            best_run = (run[0], run[-1])
        run = []
    if best_run is None and len(run) >= min_run:
        best_run = (run[0], run[-1])
    if best_run is None:                      # nothing clean: fall back to the best single frame
        idx = max(scored, key=lambda s: s[1])[0]
        best_run = (idx, idx)

    lo, hi = best_run
    held = [f["world"] for f in frames if lo <= f["i"] <= hi and f["world"]]
    if not held:
        return None

    mean = np.mean(np.array(held), axis=0)    # average the hold: far less noisy than one frame
    m = [tuple(p) for p in mean]

    limbs = {
        "shoulder_width": dist(m[L_SHOULDER], m[R_SHOULDER]),
        "hip_width": dist(m[L_HIP], m[R_HIP]),
        "upper_arm_L": dist(m[L_SHOULDER], m[L_ELBOW]),
        "upper_arm_R": dist(m[R_SHOULDER], m[R_ELBOW]),
        "forearm_L": dist(m[L_ELBOW], m[L_WRIST]),
        "forearm_R": dist(m[R_ELBOW], m[R_WRIST]),
        "thigh_L": dist(m[L_HIP], m[L_KNEE]),
        "thigh_R": dist(m[R_HIP], m[R_KNEE]),
        "shin_L": dist(m[L_KNEE], m[L_ANKLE]),
        "shin_R": dist(m[R_KNEE], m[R_ANKLE]),
    }
    shoulder_mid = np.mean([mean[L_SHOULDER], mean[R_SHOULDER]], axis=0)
    ankle_mid = np.mean([mean[L_ANKLE], mean[R_ANKLE]], axis=0)
    limbs["spine"] = float(np.linalg.norm(shoulder_mid))          # hips are the origin
    limbs["hip_to_ankle"] = float(abs(ankle_mid[1]))

    # Facing: in a T-pose the shoulder line is across the body, so its perpendicular in the
    # ground plane is the way the performer faces. Positive z is away from the camera here.
    across = mean[L_SHOULDER] - mean[R_SHOULDER]
    yaw = math.degrees(math.atan2(across[2], across[0]))

    return {
        "tpose_frames": [lo, hi],
        "tpose_score": float(np.mean([s for i, s in scored if lo <= i <= hi])),
        "limbs_metres": {k: float(v) for k, v in limbs.items()},
        "shoulder_line_yaw_deg": yaw,
        "landmarks_mean": [[float(c) for c in p] for p in mean],
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("video")
    ap.add_argument("out_json")
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--stride", type=int, default=1, help="process every Nth frame")
    ap.add_argument("--max-frames", type=int, default=0, help="stop after N processed frames")
    args = ap.parse_args()

    if not os.path.isfile(args.model):
        sys.exit(f"model not found: {args.model}\n"
                 "download pose_landmarker_heavy.task from the mediapipe-models bucket")
    cap = cv2.VideoCapture(args.video)
    if not cap.isOpened():
        sys.exit(f"cannot open video: {args.video}")

    fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH) or 0)
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT) or 0)
    print(f"{os.path.basename(args.video)}: {width}x{height}, {fps:.2f} fps, {total} frames")

    options = vision.PoseLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=args.model),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=1,
        min_pose_detection_confidence=0.5,
        min_tracking_confidence=0.5,
        output_segmentation_masks=False,
    )

    frames, missed = [], 0
    with vision.PoseLandmarker.create_from_options(options) as landmarker:
        i = -1
        while True:
            ok, bgr = cap.read()
            if not ok:
                break
            i += 1
            if args.stride > 1 and i % args.stride:
                continue
            if args.max_frames and len(frames) >= args.max_frames:
                break

            image = mp.Image(image_format=mp.ImageFormat.SRGB,
                             data=cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))
            result = landmarker.detect_for_video(image, int(i * 1000.0 / fps))

            entry = {"i": i, "t": i / fps, "world": [], "image": [], "vis": []}
            if result.pose_world_landmarks:
                for lm in result.pose_world_landmarks[0]:
                    entry["world"].append([lm.x, lm.y, lm.z])
                    entry["vis"].append(lm.visibility)
                for lm in result.pose_landmarks[0]:
                    entry["image"].append([lm.x, lm.y])
            else:
                missed += 1
            frames.append(entry)
            if len(frames) % 60 == 0:
                print(f"  {len(frames)} frames, {missed} without a pose", flush=True)
    cap.release()

    detected = len(frames) - missed
    if not detected:
        sys.exit("no pose detected in any frame - check the framing (full body, good light)")

    # Visibility report. A move filmed with the legs out of frame still "detects" a pose:
    # MediaPipe invents the missing joints, and the clip comes out crumpled. Say so loudly.
    groups = {"arms": [L_SHOULDER, R_SHOULDER, L_ELBOW, R_ELBOW, L_WRIST, R_WRIST],
              "torso": [L_SHOULDER, R_SHOULDER, L_HIP, R_HIP],
              "legs": [L_KNEE, R_KNEE, L_ANKLE, R_ANKLE]}
    seen = [f for f in frames if f["vis"]]
    visibility = {name: float(np.mean([[f["vis"][i] for i in idx] for f in seen]))
                  for name, idx in groups.items()} if seen else {}

    takes = find_takes(frames, fps)
    calib = calibrate(frames)
    data = {
        "takes": takes,
        "video": os.path.abspath(args.video),
        "model": os.path.basename(args.model),
        "fps": fps,
        "size": [width, height],
        "stride": args.stride,
        "frame_count": len(frames),
        "detected": detected,
        "convention": "mediapipe world landmarks: metres, origin = hip midpoint, "
                      "x = image right, y = image DOWN, z = toward camera negative",
        "visibility": visibility,
        "calibration": calib,
        "frames": frames,
    }
    os.makedirs(os.path.dirname(os.path.abspath(args.out_json)), exist_ok=True)
    with open(args.out_json, "w", encoding="utf-8") as fh:
        json.dump(data, fh)

    print(f"\npose found in {detected}/{len(frames)} frames "
          f"({100.0 * detected / len(frames):.0f}%)")
    if visibility:
        print("tracking: " + ", ".join(f"{k} {v:.2f}" for k, v in visibility.items()))
        for name, v in visibility.items():
            if v < 0.5:
                print(f"  ** {name.upper()} BARELY TRACKED ({v:.2f}) - they are probably out of "
                      f"frame or hidden. MediaPipe invents missing joints, so the clip will be "
                      f"wrong there. Reshoot with the whole body in frame if the move needs them.")
    if takes:
        print(f"\n{len(takes)} take(s) found - pick one with --trim START:END")
        for n, t in enumerate(takes, 1):
            print(f"  take {n}: --trim {t['start']}:{t['end']}   ({t['seconds']:.1f}s)")
    if calib:
        lo, hi = calib["tpose_frames"]
        print(f"T-pose hold: frames {lo}-{hi} ({(hi - lo + 1) / fps:.2f}s), "
              f"score {calib['tpose_score']:.2f}")
        limbs = calib["limbs_metres"]
        print("measured (metres): " + ", ".join(
            f"{k} {limbs[k]:.3f}" for k in ("shoulder_width", "upper_arm_R", "forearm_R",
                                            "thigh_R", "shin_R", "spine")))
        print(f"shoulder line yaw: {calib['shoulder_line_yaw_deg']:.1f} deg")
    print(f"written: {args.out_json} ({os.path.getsize(args.out_json) / 1e6:.1f} MB)")


if __name__ == "__main__":
    main()
