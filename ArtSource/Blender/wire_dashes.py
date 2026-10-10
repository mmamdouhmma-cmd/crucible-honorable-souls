"""Add the four dash states to a Unity AnimatorController YAML (run with Blender's python).

Usage: blender -b --python wire_dashes.py -- <controller.yaml> <idleStateFileID>

Adds parameters Dash (trigger) and DashDir (int), states Dash_Forward/Back/Left/Right,
Any State -> Dash_* on Dash + DashDir == n, and exits back to idle on exit time.
"""
import sys, re, random

args = sys.argv[sys.argv.index("--") + 1:]
PATH, IDLE = args[0], int(args[1])
CLIP_ID = -203655887218126122
GUIDS = {
    "Dash_Forward": "327e1571333e363ae31ae7a0fb9e87d7",
    "Dash_Back":    "f5ce87fc683923cdedc604476d307272",
    "Dash_Left":    "9e3733d19ef3337967ce700b094c4b41",
    "Dash_Right":   "d6ec6f9fbd52df2e5e353538862d9514",
}
ORDER = ["Dash_Forward", "Dash_Back", "Dash_Left", "Dash_Right"]  # DashDir 0..3

text = open(PATH, encoding="utf-8").read()
if "m_Name: DashDir" in text:
    print("already wired:", PATH); sys.exit(0)

used = set(int(x) for x in re.findall(r"^--- !u!\d+ &(-?\d+)", text, re.M))
def fid():
    while True:
        v = random.randint(-(2**62), 2**62)
        if v not in used:
            used.add(v); return v

blocks = []
def state_block(i, name, guid, transitions):
    tr = (chr(10) + "".join(f"  - {{fileID: {t}}}" + chr(10) for t in transitions)) if transitions else " []" + chr(10)
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
  m_Transitions:{tr}  m_StateMachineBehaviours: []
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

def transition(dst, conditions, duration, exit_time=None, self_ok=0):
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

ids = {n: fid() for n in ORDER}
any_t = []
for n, name in enumerate(ORDER):
    exit_t = transition(IDLE, [], 0.12, exit_time=0.85)
    state_block(ids[name], name, GUIDS[name], [exit_t])
    any_t.append(transition(ids[name], [(1, "Dash", 0), (6, "DashDir", n)], 0.02, self_ok=1))

params = """  - m_Name: Dash
    m_Type: 9
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  - m_Name: DashDir
    m_Type: 3
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
"""
text = text.replace("  m_AnimatorLayers:\n", params + "  m_AnimatorLayers:\n", 1)
pos = {"Dash_Forward": (-150, 460), "Dash_Back": (0, 460), "Dash_Left": (150, 460), "Dash_Right": (300, 460)}
child = "".join(f"  - serializedVersion: 1\n    m_State: {{fileID: {ids[n]}}}\n    m_Position: {{x: {x}, y: {y}, z: 0}}\n"
                for n, (x, y) in pos.items())
text = text.replace("  m_ChildStateMachines: []\n", child + "  m_ChildStateMachines: []\n", 1)
anys = "".join(f"  - {{fileID: {t}}}\n" for t in any_t)
text = text.replace("  m_EntryTransitions: []\n", anys + "  m_EntryTransitions: []\n", 1)
if not text.endswith("\n"):
    text += "\n"
text += "".join(blocks)
open(PATH, "w", encoding="utf-8", newline="\n").write(text)
print("wired dashes:", PATH, ids)
