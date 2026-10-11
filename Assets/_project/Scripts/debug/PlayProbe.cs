using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Play-mode diagnostic: a few seconds after play starts, writes PlayProbe.txt (project root) with the state of
/// every Fighter (position, rigidbody, capsule, ground raycast, animator state and clip, hips height) plus the
/// global time scale and hit-stop. Reads the file afterwards instead of guessing what the game did.
/// Remove once the animation pipeline is stable.
/// </summary>
public class PlayProbe : MonoBehaviour
{
    static readonly float[] SampleTimes = { 0.5f, 2.0f, 4.0f };
    int next;
    readonly StringBuilder sb = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("PlayProbe");
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<PlayProbe>();
    }

    void Update()
    {
        if (next >= SampleTimes.Length) return;
        if (Time.unscaledTime < SampleTimes[next]) return;
        Sample(SampleTimes[next]);
        next++;
        Flush();
    }

    void Sample(float t)
    {
        sb.AppendLine($"=== t={t:F1}s realtime={Time.realtimeSinceStartup:F2} timeScale={Time.timeScale} deltaTime={Time.deltaTime:F4} gravity={Physics.gravity} hitStop={HitStop.IsActive}");
        foreach (var f in FindObjectsByType<Fighter>(FindObjectsSortMode.None))
        {
            var tr = f.transform;
            sb.AppendLine($"FIGHTER {f.name} pos={tr.position} scale={tr.lossyScale} active={f.gameObject.activeInHierarchy} enabled={f.enabled}");
            var rb = f.GetComponent<Rigidbody>();
            if (rb) sb.AppendLine($"  rb vel={rb.linearVelocity} kinematic={rb.isKinematic} gravity={rb.useGravity} constraints={rb.constraints} sleeping={rb.IsSleeping()} detectCollisions={rb.detectCollisions}");
            else sb.AppendLine("  rb: none");
            foreach (var c in f.GetComponents<Collider>())
                sb.AppendLine($"  collider {c.GetType().Name} enabled={c.enabled} trigger={c.isTrigger} bounds={c.bounds.min}..{c.bounds.max}");
            var origin = tr.position + Vector3.up * 1.0f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 50f, ~0, QueryTriggerInteraction.Ignore))
                sb.AppendLine($"  ground raycast from y={origin.y:F2}: hit {hit.collider.name} at y={hit.point.y:F3} dist={hit.distance:F2} layer={hit.collider.gameObject.layer}");
            else sb.AppendLine($"  ground raycast from y={origin.y:F2}: NO HIT");
            var anim = f.GetComponent<Animator>() ?? f.GetComponentInChildren<Animator>();
            if (anim == null) { sb.AppendLine("  animator: none"); continue; }
            sb.AppendLine($"  animator on {anim.gameObject.name}: enabled={anim.enabled} speed={anim.speed} isHuman={anim.isHuman} avatar={(anim.avatar ? anim.avatar.name : "none")} valid={(anim.avatar && anim.avatar.isValid)} controller={(anim.runtimeAnimatorController ? anim.runtimeAnimatorController.name : "none")} bound={anim.hasBoundPlayables} initialized={anim.isInitialized} culling={anim.cullingMode} rootMotion={anim.applyRootMotion} layers={anim.layerCount}");
            if (anim.runtimeAnimatorController != null && anim.layerCount > 0)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                var clips = anim.GetCurrentAnimatorClipInfo(0);
                var names = new StringBuilder();
                foreach (var ci in clips) names.Append(ci.clip ? ci.clip.name : "null").Append('(').Append(ci.weight.ToString("F2")).Append(") ");
                sb.AppendLine($"  state hash={st.shortNameHash} normTime={st.normalizedTime:F2} length={st.length:F2} loop={st.loop} clips=[{names}] isBjornIdle={st.IsName("Bjorn_Idle")} isMiyaIdle={st.IsName("Miyamoto_Idle")}");
                sb.AppendLine($"  params MoveSpeed={anim.GetFloat("MoveSpeed"):F2} Blocking={anim.GetBool("Blocking")}");
            }
            var hips = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            var head = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            var lfoot = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
            if (hips) sb.AppendLine($"  hips world y={hips.position.y:F3} (root y={tr.position.y:F3}) head y={(head ? head.position.y : float.NaN):F3} leftFoot y={(lfoot ? lfoot.position.y : float.NaN):F3} bodyPos={anim.bodyPosition}");
            foreach (var child in tr.GetComponentsInChildren<Animator>())
                if (child != anim) sb.AppendLine($"  extra animator on child {child.gameObject.name}: enabled={child.enabled} controller={(child.runtimeAnimatorController ? child.runtimeAnimatorController.name : "none")}");
        }
    }

    void Flush()
    {
        try
        {
            var path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PlayProbe.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("PlayProbe written: " + path);
        }
        catch (System.Exception e) { Debug.LogWarning("PlayProbe write failed: " + e.Message); }
    }
}
