"""Point existing controller states at the custom clips and tighten attack transitions.

Usage: blender -b --python retarget_controllers.py -- <controller.yaml> state=clipGuid [state=clipGuid ...]

For each listed state (by m_Name): sets m_Motion to the custom clip, resets m_Speed to 1.
For every state whose name contains 'attack' marker list ATTACK_STATES: its exit transitions get
exit time 0.9 / duration 0.1, and Any State transitions into it get duration 0.03.
"""
import sys, re

args = sys.argv[sys.argv.index("--") + 1:]
PATH = args[0]
MAP = dict(a.split("=", 1) for a in args[1:])
CLIP_ID = -203655887218126122

text = open(PATH, encoding="utf-8").read()
parts = re.split(r"(?m)^(?=--- !u!)", text)
head, blocks = parts[0], parts[1:]

def block_id(b):
    m = re.match(r"--- !u!(\d+) &(-?\d+)", b)
    return int(m.group(1)), int(m.group(2))

states = {}       # id -> name
for b in blocks:
    t, i = block_id(b)
    if t == 1102:
        states[i] = re.search(r"^  m_Name: (.*)$", b, re.M).group(1).strip()

# Attack states: any mapped state whose name says so (slash / backhand / heavy / light / medium).
attack_ids = {i for i, n in states.items() if n in MAP and re.search(r"slash|backhand|heavy|light|medium", n, re.I)}
exit_ids = set()

out = []
for b in blocks:
    t, i = block_id(b)
    if t == 1102 and states[i] in MAP:
        guid = MAP[states[i]]
        b = re.sub(r"^  m_Motion: \{.*\}$", f"  m_Motion: {{fileID: {CLIP_ID}, guid: {guid}, type: 3}}", b, flags=re.M)
        b = re.sub(r"^  m_Speed: .*$", "  m_Speed: 1", b, flags=re.M)
        if i in attack_ids:
            for m in re.finditer(r"^  - \{fileID: (-?\d+)\}$", b.split("m_StateMachineBehaviours")[0], re.M):
                exit_ids.add(int(m.group(1)))
    out.append(b)
blocks = out

out = []
for b in blocks:
    t, i = block_id(b)
    if t == 1101:
        dst = int(re.search(r"^  m_DstState: \{fileID: (-?\d+)\}", b, re.M).group(1))
        if i in exit_ids:
            b = re.sub(r"^  m_ExitTime: .*$", "  m_ExitTime: 0.9", b, flags=re.M)
            b = re.sub(r"^  m_HasExitTime: .*$", "  m_HasExitTime: 1", b, flags=re.M)
            b = re.sub(r"^  m_TransitionDuration: .*$", "  m_TransitionDuration: 0.1", b, flags=re.M)
        elif dst in attack_ids:
            b = re.sub(r"^  m_TransitionDuration: .*$", "  m_TransitionDuration: 0.03", b, flags=re.M)
    out.append(b)

open(PATH, "w", encoding="utf-8", newline="\n").write(head + "".join(out))
print("retargeted", PATH, "states:", [states[i] for i in states if states[i] in MAP],
      "attack exits:", len(exit_ids))
