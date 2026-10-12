using UnityEngine;

/// <summary>
/// Debug: logs one line per frame for the traced fighter's weapon blade: tip/base distance from the
/// opponent's capsule axis (XZ), heights, and whether the attack is in its active phase. Reads as a
/// swing path from the console. Add to any object in the scene; remove when done.
/// </summary>
public class BladeTrace : MonoBehaviour
{
    [Tooltip("Fighter whose blade is traced (Player1 / Player2).")]
    public string fighterName = "Player1";
    [Tooltip("Log only while the fighter is attacking (any phase); off = every frame.")]
    public bool onlyWhileAttacking = true;

    private Fighter me, other;
    private CombatSystem combat;
    private BoxCollider box;
    private CapsuleCollider cap;

    private void Update()
    {
        if (me == null)
        {
            var all = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            if (all.Length < 2) return;
            me = all[0].name == fighterName ? all[0] : all[1];
            other = me == all[0] ? all[1] : all[0];
            combat = me.GetComponent<CombatSystem>();
            cap = other.GetComponent<CapsuleCollider>();
            foreach (var b in me.GetComponentsInChildren<BoxCollider>(true))
                if (b.isTrigger && b.GetComponent<HitDetector>() != null && b.transform.parent != me.transform) { box = b; break; }
        }
        if (box == null || cap == null || combat == null) return;
        bool attacking = combat.IsAttacking;
        if (onlyWhileAttacking && !attacking) return;
        Vector3 s = box.size;
        Vector3 axis = s.x >= s.y && s.x >= s.z ? Vector3.right : (s.y >= s.z ? Vector3.up : Vector3.forward);
        float half = Mathf.Max(s.x, s.y, s.z) * 0.5f;
        Vector3 tip = box.transform.TransformPoint(box.center + axis * half);
        Vector3 bse = box.transform.TransformPoint(box.center - axis * half);
        Vector3 c = cap.bounds.center;
        float tipXZ = Vector2.Distance(new Vector2(tip.x, tip.z), new Vector2(c.x, c.z));
        float baseXZ = Vector2.Distance(new Vector2(bse.x, bse.z), new Vector2(c.x, c.z));
        float capR = cap.radius * Mathf.Max(cap.transform.lossyScale.x, cap.transform.lossyScale.z);
        Debug.Log($"BLADE {me.name} f{Time.frameCount} t={combat.AttackTime:F3} active={combat.IsInActivePhase()} armed={box.enabled} tipXZ={tipXZ:F2} baseXZ={baseXZ:F2} capR={capR:F2} tipY={tip.y:F2} baseY={bse.y:F2} dt={Time.deltaTime:F3}");
    }
}
