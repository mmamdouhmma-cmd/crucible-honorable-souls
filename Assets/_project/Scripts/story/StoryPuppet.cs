using UnityEngine;

/// <summary>
/// A cast member standing in the background (the Partner at the training post, later the Sensei watching
/// the sparring). It is a COPY of the fighter with every gameplay component taken off before it ever wakes,
/// so it cannot be picked as an opponent, take a hit, or read input - only its Animator is left. "practice"
/// throws the fighter's own strikes at the spot it faces, with idle gaps; "idle" just stands.
/// </summary>
public class StoryPuppet : MonoBehaviour
{
    public enum Mode { Idle, Practice }

    static readonly string[] Strikes = { "LightAttack", "MediumAttack", "LightAttack", "HeavyAttack" };

    Animator anim;
    Mode mode;
    float next;
    int strike;

    public string Source { get; private set; }

    /// <summary>Makes a puppet of <paramref name="source"/> standing on <paramref name="spot"/>.</summary>
    public static StoryPuppet Create(Fighter source, Transform spot, Mode mode)
    {
        bool wasActive = source.gameObject.activeSelf;
        source.gameObject.SetActive(false);   // clone it asleep: nothing on the copy runs Awake until it is stripped
        var go = Instantiate(source.gameObject, spot.position, spot.rotation);
        source.gameObject.SetActive(wasActive);
        go.name = source.gameObject.name + " (puppet)";

        // Gameplay scripts first (CombatSystem requires Fighter, which requires the Rigidbody), then physics.
        // Reverse order: a component added later usually depends on an earlier one (CombatSystem on Fighter).
        var scripts = go.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = scripts.Length - 1; i >= 0; i--) DestroyImmediate(scripts[i]);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);

        var p = go.AddComponent<StoryPuppet>();
        p.Source = source.CharacterName;
        p.anim = go.GetComponent<Animator>();
        p.mode = mode;
        p.next = Time.time + Random.Range(0.6f, 1.4f);
        if (p.anim != null) p.anim.applyRootMotion = false;
        go.SetActive(true);
        return p;
    }

    /// <summary>Stops (or starts) the practice strikes.</summary>
    public void SetMode(Mode m) { mode = m; }

    void Update()
    {
        if (anim == null || mode != Mode.Practice || Time.time < next) return;
        anim.SetTrigger(Strikes[strike]);
        strike = (strike + 1) % Strikes.Length;
        next = Time.time + Random.Range(1.6f, 2.6f);   // a breath between cuts, like a man drilling alone
    }
}
