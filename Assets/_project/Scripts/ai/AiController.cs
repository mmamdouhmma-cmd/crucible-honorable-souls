using UnityEngine;

public enum AiDifficulty { Chill, Moderate, Insane }

/// <summary>
/// Computer opponent. Writes an <see cref="InputData"/> snapshot for its player into the
/// <see cref="InputManager"/> every frame, so it plays by exactly the rules a human does (lunge, stamina,
/// hitstun, clash, dash counter). Three profiles: Chill (slow, sloppy, light-happy), Moderate (reads
/// wind-ups, blocks and dodges half the time), Insane (reacts in a few frames, punishes every whiff,
/// dodges and counters). Sits on the GameMangers object; the menu turns it on for "Against AI".
/// </summary>
public class AiController : MonoBehaviour
{
    public static AiController Instance { get; private set; }

    [Tooltip("Which player the AI drives (the human is the other one).")]
    public int playerNumber = 2;
    public AiDifficulty difficulty = AiDifficulty.Moderate;
    [SerializeField] private bool debugLogs = false;

    /// <summary>
    /// What the opponent is allowed to do. Practice mode narrows this so a single mechanic can be
    /// drilled against a live opponent: a dummy that only blocks, one that only walks in, one that
    /// attacks but never defends. Everything off is a standing training dummy.
    /// </summary>
    [System.Serializable]
    public struct Rules
    {
        public bool walk;      // close the distance and keep spacing
        public bool attack;    // throw strikes of its own
        public bool block;     // guard against a wind-up it has seen
        public bool dodge;     // sidestep a wind-up it has seen
        public bool punish;    // attack into a recovery, and counter after a dodge

        /// <summary>An opponent that uses everything its difficulty allows - a normal fight.</summary>
        public static Rules Full => new Rules { walk = true, attack = true, block = true, dodge = true, punish = true };

        /// <summary>A dummy that stands there and takes it.</summary>
        public static Rules Dummy => new Rules();

        public bool IsDummy => !walk && !attack && !block && !dodge && !punish;
    }

    [Tooltip("What this opponent may do. Practice mode writes this; a normal match uses Full.")]
    public Rules rules = Rules.Full;

    /// <summary>True while the AI is in charge of its player.</summary>
    public bool Active { get; private set; }

    struct Profile
    {
        public float reaction;        // seconds before it responds to a wind-up it has seen
        public float think;           // seconds between offensive decisions
        public float aggression;      // chance per decision to attack when in range
        public float dodge;           // chance to dodge a seen strike
        public float block;           // chance to block a seen strike it did not dodge
        public float mistake;         // chance per decision to do something dumb (swing from range, drop the guard)
        public float range;           // distance it likes to stand at
        public bool punish;           // attacks into the opponent's recovery
        public float heavyBias;       // weight toward medium/heavy picks
        public float retreat;         // chance per decision to back off when too close / low stamina
    }

    static Profile For(AiDifficulty d)
    {
        switch (d)
        {
            case AiDifficulty.Chill: return new Profile { reaction = 0.45f, think = 0.55f, aggression = 0.35f, dodge = 0.12f, block = 0.30f, mistake = 0.30f, range = 2.3f, punish = false, heavyBias = 0.25f, retreat = 0.15f };
            case AiDifficulty.Insane: return new Profile { reaction = 0.08f, think = 0.12f, aggression = 0.85f, dodge = 0.75f, block = 0.85f, mistake = 0.02f, range = 1.9f, punish = true, heavyBias = 0.55f, retreat = 0.35f };
            default: return new Profile { reaction = 0.22f, think = 0.28f, aggression = 0.6f, dodge = 0.45f, block = 0.6f, mistake = 0.12f, range = 2.1f, punish = true, heavyBias = 0.4f, retreat = 0.25f };
        }
    }

    Fighter me, foe;
    CombatSystem myCombat, foeCombat;
    Transform cam;
    Profile p;

    // state
    float nextThink;
    float threatSeenAt = -1f;      // Time.time the current enemy wind-up was first noticed
    bool threatHandled;
    float blockUntil;
    float backOffUntil;
    int backOffDir;
    float lastDashAt = -10f;
    int lastFoeAttackId;           // changes when the foe starts a new attack

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    public void Activate(AiDifficulty d, int player = 2) => Activate(d, player, Rules.Full);

    /// <summary>Takes a player over, restricted to <paramref name="allowed"/> (practice mode).</summary>
    public void Activate(AiDifficulty d, int player, Rules allowed)
    {
        difficulty = d;
        playerNumber = player;
        rules = allowed;
        p = For(d);
        Bind();
        Active = true;
        nextThink = Time.time + 0.5f;
        threatSeenAt = -1f; threatHandled = false; blockUntil = 0f; backOffUntil = 0f;
        if (InputManager.Instance != null) InputManager.Instance.SetAi(playerNumber, true);
        if (debugLogs) Debug.Log($"AI: driving Player {playerNumber} at {d}");
    }

    public void Deactivate()
    {
        Active = false;
        if (InputManager.Instance != null) InputManager.Instance.SetAi(playerNumber, false);
    }

    void Bind()
    {
        me = null; foe = null;
        foreach (var f in FindObjectsByType<Fighter>(FindObjectsSortMode.None))
        {
            if (f.PlayerNumber == playerNumber) me = f; else foe = f;
        }
        myCombat = me != null ? me.GetComponent<CombatSystem>() : null;
        foeCombat = foe != null ? foe.GetComponent<CombatSystem>() : null;
        cam = Camera.main != null ? Camera.main.transform : null;
    }

    void Update()
    {
        if (!Active || InputManager.Instance == null) return;
        if (me == null || foe == null || myCombat == null || foeCombat == null) { Bind(); if (me == null || foe == null) return; }
        if (!InputManager.Instance.InputEnabled || Time.timeScale <= 0f) return;

        InputData input = Think();
        InputManager.Instance.SetAiInput(playerNumber, input);
    }

    // screen-relative direction toward the foe: +1 = right on screen
    int ScreenDirToFoe()
    {
        Vector3 d = foe.transform.position - me.transform.position;
        float side = cam != null ? Vector3.Dot(d, cam.right) : d.x;
        return side >= 0f ? 1 : -1;
    }

    float Gap()
    {
        Vector3 d = foe.transform.position - me.transform.position; d.y = 0f;
        return d.magnitude;
    }

    InputData Think()
    {
        var input = new InputData();
        if (rules.IsDummy) return input;      // practice dummy: stands there and takes it
        float now = Time.time;
        float gap = Gap();
        int toFoe = ScreenDirToFoe();
        bool busy = myCombat.IsAttacking || myCombat.IsInHitstun || me.IsDashing;
        int stamina = me.Stats != null ? me.Stats.currentStamina : 100;

        // ---- read the opponent's wind-up
        bool foeWindingUp = false, foeRecovering = false;
        if (foeCombat.IsAttacking && foeCombat.CurrentAttack != null)
        {
            var a = foeCombat.CurrentAttack;
            float t = foeCombat.AttackTime;
            float startup = a.StartupFrames * BalanceConfig.FRAME_TIME;
            float activeEnd = startup + a.ActiveFrames * BalanceConfig.FRAME_TIME;
            foeWindingUp = t < startup;
            foeRecovering = t > activeEnd + 0.05f;
            int id = a.GetInstanceID() ^ Mathf.RoundToInt((now - t) * 1000f);
            if (foeWindingUp && threatSeenAt < 0f) { threatSeenAt = now; threatHandled = false; lastFoeAttackId = id; }
        }
        else
        {
            threatSeenAt = -1f; threatHandled = false;
        }

        // ---- defence: react to a seen wind-up after the profile's reaction time
        if ((rules.dodge || rules.block) && foeWindingUp && !threatHandled && threatSeenAt >= 0f &&
            now - threatSeenAt >= p.reaction && gap < 3.2f)
        {
            threatHandled = true;
            float roll = Random.value;
            if (rules.dodge && !busy && roll < p.dodge && stamina >= 15 && now - lastDashAt > 0.6f)
            {
                // sidestep (orbits the attacker); into the screen or toward the camera at random
                input.dashDirection = new Vector2(0f, Random.value < 0.5f ? 1f : -1f);
                lastDashAt = now;
                if (debugLogs) Debug.Log("AI: DODGE");
                return input;
            }
            if (rules.block && roll < p.dodge + p.block && !myCombat.IsAttacking)
            {
                var a = foeCombat.CurrentAttack;
                float remaining = a.GetTotalDuration() - foeCombat.AttackTime;
                blockUntil = now + Mathf.Max(0.25f, remaining * 0.6f);
                if (debugLogs) Debug.Log("AI: BLOCK");
            }
        }

        if (now < blockUntil && !me.IsDashing)
        {
            input.blockHeld = true;
            // a mistake drops the guard early
            if (Random.value < p.mistake * Time.deltaTime) blockUntil = 0f;
            return input;
        }

        if (busy) return input;

        // ---- counter after a clean dodge, or punish a recovery
        bool counterOpen = me.DashCounterOpen;
        if (rules.punish && rules.attack && (counterOpen || (p.punish && foeRecovering)) && gap <= 2.7f && stamina >= 10)
        {
            Pick(ref input, preferHeavy: foeRecovering);
            if (debugLogs) Debug.Log(counterOpen ? "AI: COUNTER" : "AI: PUNISH");
            return input;
        }

        // ---- backing off (low stamina or too close)
        if (now < backOffUntil)
        {
            input.moveDirection = backOffDir;
            return input;
        }

        if (now < nextThink)
        {
            // between decisions: keep the spacing
            if (rules.walk && gap > p.range + 0.35f) input.moveDirection = toFoe;
            return input;
        }
        nextThink = now + p.think * Random.Range(0.7f, 1.3f);

        if (stamina < 18)
        {
            if (rules.walk && Random.value < 0.7f) { backOffUntil = now + 0.5f; backOffDir = -toFoe; }
            return input;
        }

        // ---- offence
        bool inRange = gap <= BalanceConfig.ATTACK_LUNGE_STOP + BalanceConfig.ATTACK_LUNGE_MAX - 0.1f;
        if (inRange)
        {
            if (rules.attack && Random.value < p.aggression && !foeWindingUp)
            {
                Pick(ref input, preferHeavy: false);
                return input;
            }
            if (rules.walk && Random.value < p.retreat && gap < p.range - 0.3f)
            {
                backOffUntil = now + Random.Range(0.25f, 0.5f);
                backOffDir = -toFoe;
                return input;
            }
        }
        else if (rules.walk)
        {
            if (rules.attack && Random.value < p.mistake) { Pick(ref input, preferHeavy: false); return input; }   // swings at air
            // close in: walk, or dash in from far away
            if (rules.dodge && gap > 4.5f && stamina >= 25 && now - lastDashAt > 1.2f && Random.value < 0.5f)
            {
                input.dashDirection = new Vector2(toFoe, 0f);
                lastDashAt = now;
            }
            else input.moveDirection = toFoe;
        }
        return input;
    }

    void Pick(ref InputData input, bool preferHeavy)
    {
        float r = Random.value;
        float heavy = preferHeavy ? p.heavyBias + 0.25f : p.heavyBias;
        if (r < heavy * 0.45f) input.heavyAttack = true;
        else if (r < heavy) input.mediumAttack = true;
        else input.lightAttack = true;
    }
}
