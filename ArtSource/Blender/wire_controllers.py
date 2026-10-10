"""Add hit-reaction and block states to a Unity AnimatorController YAML (run with Blender's python).

Usage: blender -b --python wire_controllers.py -- <controller.yaml> <idleStateFileID>

Adds:
  parameter Blocking (bool)
  states React_Front / React_Left / React_Right / Block_Hold / Block_Impact
  Any State -> React_*     on GetHit + HitSide == n + Blocking false
  Any State -> Block_Hold  on Blocking true (no self transition)
  Block_Hold -> Block_Impact on GetHit;  Block_Impact -> Block_Hold on exit time
  Block_Hold -> idle on Blocking false;  React_* -> idle on exit time
Clip motions are the single clip (internalID -203655887218126122) inside each clip FBX.
"""
import sys, re, random

args = sys.argv[sys.argv.index("--") + 1:]
PATH, IDLE = args[0], int(args[1])
CLIP_ID = -203655887218126122
GUIDS = {
    "React_Front":  "c3b528721605336922221f2cbe89f0f2",
    "React_Left":   "d52fcb9c7846bfc2ae568fc2f888dbc6",
    "React_Right":  "062ee142279b19acf105e997560e8eec",
    "Block_Hold":   "dbb4229fbfb217126fd155cf0eaaa370",
    "Block_Impact": "749c90921afcb2dd24f09fc52144b3c7",
}

text = open(PATH, encoding="utf-8").read()
if "m_Name: Blocking" in text or "m_Name: React_Front" in text:
    print("already wired:", PATH); sys.exit(0)

used = set(int(x) for x in re.findall(r"^--- !u!\d+ &(-?\d+)", text, re.M))
def fid():
    while True:
        v = random.randint(-(2**62), 2**62)
        if v not in used:
            used.add(v); return v

blocks = []
def state(name, guid, pos, transitions):
    i = fid()
    blocks.append(f"""--- !u!1102 &{i}
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions:{(chr(10) + "".join(f"  - {{fileID: {t}}}" + chr(10) for t in transitions)) if transitions else " []" + chr(10)}  m_StateMachineBehaviours: []
  m_Position: {{x: 50, y: 50, z: 0}}
  m_IKOnFeet: 0
  m_WriteDefaultValues: 1
  m_Mirror: 0
  m_SpeedParameterActive: 0
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
  m_Motion: {{fileID: {CLIP_ID}, guid: {guid}, type: 3}}
  m_Tag:
  m_SpeedParameter:
  m_MirrorParameter:
  m_CycleOffsetParameter:
  m_TimeParameter:
""")
    return i, pos

def transition(dst, conditions, duration, exit_time=None, self_ok=0):
    """conditions: list of (mode, param, threshold). mode 1=If 2=IfNot 3=Greater 4=Less 6=Equals 7=NotEqual"""
    i = fid()
    conds = "".join(f"  - m_ConditionMode: {m}\n    m_ConditionEvent: {p}\n    m_EventTreshold: {t}\n" for m, p, t in conditions)
    blocks.append(f"""--- !u!1101 &{i}
AnimatorStateTransition:
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name:
  m_Conditions:{(chr(10) + conds) if conds else " []" + chr(10)}  m_DstStateMachine: {{fileID: 0}}
  m_DstState: {{fileID: {dst}}}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: {duration}
  m_TransitionOffset: 0
  m_ExitTime: {exit_time if exit_time is not None else 0.75}
  m_HasExitTime: {1 if exit_time is not None else 0}
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: {self_ok}
""")
    return i

# States need their outgoing transition ids first, and transitions need destination ids:
# reserve state ids up front.
ids = {n: fid() for n in GUIDS}
for n in ids: used.add(ids[n])

# Outgoing transitions
t_react_exit = {n: transition(IDLE, [], 0.1, exit_time=0.9) for n in ("React_Front", "React_Left", "React_Right")}
t_hold_release = transition(IDLE, [(2, "Blocking", 0)], 0.15)
t_hold_hit = transition(ids["Block_Impact"], [(1, "GetHit", 0)], 0.02)
t_impact_back = transition(ids["Block_Hold"], [], 0.08, exit_time=0.95)

# States (ids reserved above; write blocks with those ids)
def state_with_id(i, name, guid, transitions):
    j, _ = state(name, guid, None, transitions)
    blocks[-1] = blocks[-1].replace(f"&{j}\n", f"&{i}\n", 1)
    used.discard(j)

state_with_id(ids["React_Front"], "React_Front", GUIDS["React_Front"], [t_react_exit["React_Front"]])
state_with_id(ids["React_Left"], "React_Left", GUIDS["React_Left"], [t_react_exit["React_Left"]])
state_with_id(ids["React_Right"], "React_Right", GUIDS["React_Right"], [t_react_exit["React_Right"]])
state_with_id(ids["Block_Hold"], "Block_Hold", GUIDS["Block_Hold"], [t_hold_hit, t_hold_release])
state_with_id(ids["Block_Impact"], "Block_Impact", GUIDS["Block_Impact"], [t_impact_back])

# Any State transitions
any_t = [
    transition(ids["React_Front"], [(1, "GetHit", 0), (6, "HitSide", 0), (2, "Blocking", 0)], 0.05, self_ok=1),
    transition(ids["React_Left"],  [(1, "GetHit", 0), (6, "HitSide", 1), (2, "Blocking", 0)], 0.05, self_ok=1),
    transition(ids["React_Right"], [(1, "GetHit", 0), (6, "HitSide", 2), (2, "Blocking", 0)], 0.05, self_ok=1),
    transition(ids["Block_Hold"],  [(1, "Blocking", 0)], 0.12, self_ok=0),
]

# --- splice into the file ---
# 1. parameter
param = """  - m_Name: Blocking
    m_Type: 4
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
"""
text = text.replace("  m_AnimatorLayers:\n", param + "  m_AnimatorLayers:\n", 1)

# 2. child states + any-state transitions in the state machine
positions = {"React_Front": (520, 40), "React_Left": (520, 90), "React_Right": (520, 140),
             "Block_Hold": (520, 220), "Block_Impact": (720, 220)}
child = "".join(f"  - serializedVersion: 1\n    m_State: {{fileID: {ids[n]}}}\n    m_Position: {{x: {x}, y: {y}, z: 0}}\n"
                for n, (x, y) in positions.items())
text = text.replace("  m_ChildStateMachines: []\n", child + "  m_ChildStateMachines: []\n", 1)
anys = "".join(f"  - {{fileID: {t}}}\n" for t in any_t)
text = text.replace("  m_EntryTransitions: []\n", anys + "  m_EntryTransitions: []\n", 1)

# 3. append new objects
if not text.endswith("\n"):
    text += "\n"
text += "".join(blocks)
open(PATH, "w", encoding="utf-8", newline="\n").write(text)
print("wired:", PATH, "states:", ids)
