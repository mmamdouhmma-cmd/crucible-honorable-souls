using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A puppet that LEADS the way (the user's call, 2026-11-04: on the walk to Arima, Musashi follows his Sensei and his
/// Partner). It walks a <see cref="StoryPath"/> route at its fighter's own walking pace, waits - turned back toward
/// the follower - whenever the follower drops too far behind, keeps its distance from the guide in front of it, and at
/// the end of the route turns to face <see cref="faceAtEnd"/> and stands. The Animator gets MoveSpeed like a fighter,
/// so the feet play the fighter's own walk.
/// </summary>
public class StoryGuide : MonoBehaviour
{
    [Tooltip("Ground speed, units per second (the source fighter's walk).")] public float speed = 2.4f;
    [Tooltip("Stop and wait when the follower is further than this.")] public float waitBeyond = 9f;
    [Tooltip("Walk on again once the follower is within this.")] public float resumeWithin = 6f;
    [Tooltip("Keep at least this far behind the guide in front.")] public float spacing = 1.8f;
    public float turnRate = 360f;

    public Transform follower;
    public StoryGuide ahead;
    public Vector3 faceAtEnd;

    List<Vector3> path;
    int next = 1;
    bool waiting;
    Animator anim;

    public bool Arrived { get; private set; }
    public bool Waiting => waiting;

    public void Lead(List<Vector3> route)
    {
        path = route;
        next = 1;
        Arrived = path == null || path.Count < 2;
        anim = GetComponent<Animator>();
        if (!Arrived) transform.position = path[0];
    }

    void Update()
    {
        if (path == null) return;
        float dt = Time.deltaTime;
        if (Arrived) { Turn(faceAtEnd - transform.position, dt); Move(0f); return; }

        float behind = follower != null ? Flat(transform.position, follower.position) : 0f;
        if (waiting) { if (behind < resumeWithin) waiting = false; }
        else if (behind > waitBeyond) waiting = true;
        bool blocked = ahead != null && !ahead.Arrived && Flat(transform.position, ahead.transform.position) < spacing;
        if (ahead != null && ahead.Waiting && !ahead.Arrived) blocked |= Flat(transform.position, ahead.transform.position) < spacing + 1.2f;

        if (waiting || blocked)
        {
            if (waiting && follower != null) Turn(follower.position - transform.position, dt);
            Move(0f);
            return;
        }

        Vector3 target = path[next];
        Vector3 to = target - transform.position; to.y = 0f;
        float step = speed * dt;
        if (to.magnitude <= step)
        {
            transform.position = new Vector3(target.x, transform.position.y, target.z);
            if (++next >= path.Count) { Arrived = true; Move(0f); return; }
        }
        else transform.position += to.normalized * step;
        var gy = StoryPath.GroundFrom(transform.position, transform.position.y);
        if (gy != null) transform.position = new Vector3(transform.position.x, gy.Value, transform.position.z);
        Turn(to, dt);
        Move(1f);
    }

    void Turn(Vector3 dir, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnRate * dt);
    }

    void Move(float amount)
    {
        if (anim == null) return;
        anim.SetFloat("MoveSpeed", Mathf.MoveTowards(anim.GetFloat("MoveSpeed"), amount, Time.deltaTime * 6f));
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
}
