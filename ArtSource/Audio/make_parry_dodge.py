# Synthesizes distinct parry and dodge clips for CRUCIBLE and overwrites the four WAVs in
# Assets/_project/Audio/Combat/ IN PLACE (same names -> same GUIDs -> no rewiring in Unity).
#
#   parry_01/02.wav  - struck-steel ring: a 6 ms clank then inharmonic bell partials with a
#                      beating shimmer, ~0.7 s. Nothing else in the game rings.
#   dodge_01/02.wav  - rising air whoosh (band-swept noise) topped with a quick two-note
#                      upward chirp, ~0.45 s. The chirp is what separates it from the
#                      attack swishes it plays on top of.
#
# Pure stdlib (no numpy): ~60k samples per clip, fine in a loop.

import math
import os
import random
import struct
import wave

SR = 44100
OUT = r"C:\Users\mmamd\Desktop\Projects\CRUCIBLE Honorable Souls\Assets\_project\Audio\Combat"


def write_wav(path, samples, peak=0.9):
    m = max(1e-9, max(abs(s) for s in samples))
    k = peak / m
    data = b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s * k)) * 32767)) for s in samples)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data)
    print("wrote", path, len(samples) / SR, "s")


def parry(f0, seed):
    rng = random.Random(seed)
    dur = 0.72
    n = int(SR * dur)
    # Struck metal: inharmonic partial ratios (bell-like), each with its own decay.
    ratios = [1.0, 2.32, 3.01, 4.17, 5.43, 6.79, 8.21]
    amps = [1.0, 0.62, 0.55, 0.38, 0.26, 0.17, 0.10]
    taus = [0.26, 0.20, 0.17, 0.13, 0.10, 0.08, 0.06]
    detune = [rng.uniform(2.5, 7.0) for _ in ratios]      # beating shimmer, Hz
    phases = [rng.uniform(0, 2 * math.pi) for _ in ratios]
    out = []
    for i in range(n):
        t = i / SR
        s = 0.0
        for r, a, tau, d, ph in zip(ratios, amps, taus, detune, phases):
            f = f0 * r
            if f > SR * 0.45:
                continue
            env = math.exp(-t / tau)
            s += a * env * (math.sin(2 * math.pi * f * t + ph)
                            + 0.6 * math.sin(2 * math.pi * (f + d) * t))
        # the clank: 6 ms of bright noise at the onset
        if t < 0.006:
            s += rng.uniform(-1, 1) * 2.2 * (1 - t / 0.006)
        # fade the tail to zero so the clip never clicks
        if t > dur - 0.05:
            s *= (dur - t) / 0.05
        out.append(s)
    return out


def dodge(sweep_lo, sweep_hi, dur, seed):
    rng = random.Random(seed)
    n = int(SR * dur)
    lp = bp = 0.0
    out = []
    chirp_t0 = dur * 0.45
    for i in range(n):
        t = i / SR
        x = t / dur
        # band-swept noise: state-variable filter whose centre rises through the clip
        f = sweep_lo + (sweep_hi - sweep_lo) * (x ** 1.6)
        fcoef = 2 * math.sin(math.pi * min(f, SR * 0.22) / SR)
        q = 0.45
        noise = rng.uniform(-1, 1)
        hp = noise - lp - q * bp
        bp += fcoef * hp
        lp += fcoef * bp
        # envelope: fast rise, held, snapped off
        env = min(x / 0.12, 1.0) * (1.0 if x < 0.75 else max(0.0, (1.0 - x) / 0.25))
        s = bp * 2.2 * env
        # the chime on top: two quick rising notes, quiet but unmistakably tonal
        for k, (t0, fa, fb) in enumerate([(chirp_t0, 900, 1500), (chirp_t0 + 0.07, 1350, 2250)]):
            if t0 <= t < t0 + 0.09:
                u = (t - t0) / 0.09
                fc = fa + (fb - fa) * u
                s += 0.55 * math.sin(2 * math.pi * fc * (t - t0)) * math.sin(math.pi * u)
        out.append(s)
    return out


os.makedirs(OUT, exist_ok=True)
write_wav(os.path.join(OUT, "parry_01.wav"), parry(1180.0, 11))
write_wav(os.path.join(OUT, "parry_02.wav"), parry(980.0, 23))
write_wav(os.path.join(OUT, "dodge_01.wav"), dodge(400, 2800, 0.46, 37))
write_wav(os.path.join(OUT, "dodge_02.wav"), dodge(320, 2400, 0.42, 51))
