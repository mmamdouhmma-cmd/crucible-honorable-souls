"""Bjorn Ironside clip definitions (twin bearded axes). Loaded by author_clips.py.

Style notes: grounded, heavy, hips-first. Left foot leads, body bladed ~25 degrees right so the lead
shoulder and left axe face the opponent; right axe cocked at the hip ready to swing. Knees stay bent,
weight sits over the rear foot; nothing bounces. Units are file units (body ~0.98 tall), 60 fps.
"""
import math

# ------------------------------------------------------------------ guard pose (offsets from rest)
def guard(rig, t=0.0, breath=0.0, sway=0.0):
    """Apply the full twin-axe guard. `breath` (0..1) and `sway` (-1..1) add life."""
    b, s = breath, sway
    rig.hip(dx=0.03 + 0.01 * s, dy=0.02 * s, dz=-0.065 + 0.008 * b, yaw=-24 + 2 * s, pitch=4 + 2.5 * b, roll=-3 * s)
    rig.spine(lean=7 + 3 * b, side=2 * s, twist=12 - 2 * s)
    rig.neck(lean=-2, twist=4)
    rig.head(lean=2, twist=8 + 1.0 * s, side=-1.5 * s)
    rig.clav("L", lift=4 + 2 * b, fwd=10)
    rig.clav("R", lift=3 + 2 * b, fwd=-4)
    # feet: left lead, right rear turned out, both flat
    rig.foot("L", x=0.17, y=0.03, z=0.0, yaw=6)
    rig.foot("R", x=-0.13, y=-0.04, z=0.0, yaw=-32)
    rig.pole("L", dx=0.05, dy=0.04); rig.pole("R", dx=-0.02, dy=-0.12)
    # hands (IK): left axe forward at chest height, right axe cocked by the right hip
    rig.hand("L", x=0.22 + 0.02 * b, y=-0.20 + 0.02 * s, z=0.0 + 0.02 * b, yaw=-15, pitch=40, roll=-8)
    rig.hand("R", x=-0.02 + 0.01 * b, y=0.05 + 0.015 * s, z=-0.16 + 0.015 * b, yaw=-35, pitch=70, roll=0)
    rig.elbow("L", dx=-0.05, dy=0.05, dz=-0.08); rig.elbow("R", dx=-0.10, dy=-0.06, dz=-0.10)

@clip("Bjorn_Idle", frames=120, loop=True)
def idle(rig, n):
    rig.ik_arms(1)
    for f in range(0, n, 10):
        t = f / n
        breath = 0.5 - 0.5 * math.cos(2 * math.pi * t)            # one full breath per loop
        sway = math.sin(2 * math.pi * t) * 0.6                      # slow weight shift
        guard(rig, t, breath, sway)
        rig.key(f, "bezier")

def walk_cycle(rig, n, direction=1, stride=0.30, lead_bias=0.09, lift=0.045):
    """In-place guard walk. direction +1 forward, -1 back. Keys every 2 frames.
    Each foot: half the cycle on the ground sliding against the travel direction, half swinging."""
    rig.ik_arms(1)
    for f in range(0, n + 1, 2):
        t = f / n
        guard(rig, t)
        for side, phase, bias in (("L", 0.0, lead_bias), ("R", 0.5, -lead_bias)):
            p = (t + phase) % 1.0
            if p < 0.5:                      # stance: slide from +stride/2 to -stride/2 (times direction)
                u = p / 0.5
                x = (0.5 - u) * stride * direction
                z = 0.0
                pitch = 0.0
            else:                             # swing: lift and travel back to the front
                u = (p - 0.5) / 0.5
                e = 0.5 - 0.5 * math.cos(math.pi * u)
                x = (-0.5 + e) * stride * direction
                z = lift * math.sin(math.pi * u)
                pitch = -12 * math.sin(math.pi * u) * direction   # toes trail the lift
            yaw = 6 if side == "L" else -32
            y = 0.03 if side == "L" else -0.04
            rig.foot(side, x=x + bias, y=y, z=z, yaw=yaw, pitch=pitch)
        # hips: counter-yaw to the swinging leg, sway over the stance foot, bob (highest mid-stance)
        hip_yaw = -24 + 4 * math.sin(2 * math.pi * t) * direction
        sway = -0.016 * math.sin(2 * math.pi * t)                   # toward the stance foot
        bob = -0.065 - 0.012 * abs(math.cos(2 * math.pi * t))
        rig.hip(dx=0.03, dy=sway, dz=bob, yaw=hip_yaw, pitch=5, roll=-3 * math.sin(2 * math.pi * t))
        rig.spine(lean=8, side=0, twist=12 - 3 * math.sin(2 * math.pi * t) * direction)
        rig.head(lean=2, twist=8 - 1.5 * math.sin(2 * math.pi * t) * direction)
        # hands ride the body with a touch of lag
        lag = math.sin(2 * math.pi * t - 0.6)
        rig.hand("L", x=0.22, y=-0.20 + 0.012 * lag, z=0.0 - 0.01 * abs(math.cos(2 * math.pi * t)), yaw=-15, pitch=40, roll=-8)
        rig.hand("R", x=-0.02, y=0.05 + 0.012 * lag, z=-0.16 - 0.01 * abs(math.cos(2 * math.pi * t)), yaw=-35, pitch=70, roll=0)
        rig.key(f, "linear")

@clip("Bjorn_Walk_Forward", frames=40, loop=True)
def walk_fwd(rig, n): walk_cycle(rig, n, +1)

@clip("Bjorn_Walk_Back", frames=40, loop=True)
def walk_back(rig, n): walk_cycle(rig, n, -1, stride=0.24)
