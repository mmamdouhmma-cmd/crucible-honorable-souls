using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rules a dojo exercise lays over an ordinary story encounter, all switched off again by <see cref="End"/>:
/// - the Sensei holds his guard through the AI input slot every frame (a real block: chip, stamina, guard break
///   all apply), never parries (<see cref="Fighter.ParryDisabled"/>), and his guard breaks on the 3rd strike of a
///   string (<see cref="Fighter.GuardBreaksAtStrike"/>);
/// - he attacks only when a lesson calls <see cref="SenseiAttack"/>, with SHORT-RECOVERY runtime copies of his
///   moves so he is back on guard at once (the assets are untouched);
/// - nobody dies (health floor 1), and health comes back fast after a short pause since the last hurt.
///   Stamina is left to the combat rules.
/// </summary>
public class TrainingDriver : MonoBehaviour
{
    [Tooltip("Seconds after the last damage before training health starts coming back.")]
    public float regenDelay = 1.0f;
    [Tooltip("Training health regeneration, hp per second (the normal game's passive regen is a few hp/s).")]
    public float regenPerSecond = 25f;
    [Tooltip("Recovery frames of the Sensei's training copies.")]
    public int senseiRecoveryFrames = 4;

    public Fighter Player { get; private set; }
    public Fighter Sensei { get; private set; }
    public bool Active { get; private set; }
    /// <summary>The player has been brought to the health floor: the lesson must stop the exchange.</summary>
    public bool PlayerKnockedDown => Active && Player != null && Player.Stats.OnNonLethalFloor;

    bool guardUp;
    AttackData[] origAttacks, origSpecials;
    readonly List<AttackData> clones = new List<AttackData>();
    readonly Dictionary<Fighter, (int hp, float hurtAt)> watch = new Dictionary<Fighter, (int, float)>();

    /// <summary>True: health comes back fast (the lessons, sparring rounds 1-3). False: normal health (round 4).</summary>
    public bool FastRegen { get; set; } = true;

    /// <summary>Starts the rules. <paramref name="senseiRules"/> = the opponent is the Sensei (guard, no parry, training
    /// moves); false = an ordinary opponent (the Partner) who only gets the health floor and the regen.</summary>
    public void Begin(Fighter player, Fighter sensei, bool senseiRules = true, bool fastRegen = true)
    {
        Player = player;
        Sensei = sensei;
        Active = true;
        FastRegen = fastRegen;
        senseiMode = senseiRules && sensei != null;
        guardUp = senseiMode;
        ApplyFloors();
        if (senseiMode)
        {
            sensei.ParryDisabled = true;
            sensei.GuardBreaksAtStrike = 3;
            if (InputManager.Instance != null) InputManager.Instance.SetAi(sensei.PlayerNumber, true);
            var cs = sensei.Combat;
            if (origAttacks == null)
            {
                origAttacks = cs.Attacks;
                origSpecials = cs.Specials;
                cs.SetLibraries(Copies(origAttacks), Copies(origSpecials));
            }
        }
    }

    bool senseiMode;

    /// <summary>The opponent has been brought to the health floor.</summary>
    public bool OpponentKnockedDown => Active && Sensei != null && Sensei.Stats.OnNonLethalFloor;

    public void End()
    {
        if (Sensei != null) Sensei.Stats.minHealth = 0;
        if (senseiMode && Sensei != null)
        {
            Sensei.ParryDisabled = false;
            Sensei.GuardBreaksAtStrike = 0;
            if (origAttacks != null) Sensei.Combat.SetLibraries(origAttacks, origSpecials);
            if (InputManager.Instance != null) InputManager.Instance.SetAi(Sensei.PlayerNumber, false);
            Sensei.Stats.minHealth = 0;
        }
        if (Player != null) Player.Stats.minHealth = 0;
        origAttacks = origSpecials = null;
        foreach (var c in clones) if (c != null) Destroy(c);
        clones.Clear();
        watch.Clear();
        Active = false;
        guardUp = false;
    }

    void OnDestroy() { if (Active) End(); }

    /// <summary>Health floors go on the fighters' CURRENT stats object, which ResetForMatch replaces - call after every reset.</summary>
    public void ApplyFloors()
    {
        if (Player != null) Player.Stats.minHealth = 1;
        if (Sensei != null) Sensei.Stats.minHealth = 1;
        watch.Clear();
    }

    AttackData[] Copies(AttackData[] src)
    {
        if (src == null) return null;
        var dst = new AttackData[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            if (src[i] == null) continue;
            dst[i] = src[i].CloneWithRecovery(Mathf.Min(src[i].RecoveryFrames, senseiRecoveryFrames), dodgedFrames: 0);
            clones.Add(dst[i]);
        }
        return dst;
    }

    /// <summary>False if the last <see cref="SenseiAttack"/> could not come out (the lesson then skips the exchange).</summary>
    public bool LastAttackFired { get; private set; }
    /// <summary>The last <see cref="SenseiAttack"/> is over and never came out: a lesson waiting on it must move on.</summary>
    public bool AttackGaveUp { get; private set; }

    /// <summary>Drops the guard, throws one of the Sensei's moves, and raises the guard again when it is over.
    /// The strike always comes out: it waits for any stun to end (Musashi's strikes on his guard put him in
    /// block-stun - that refusal is what left him standing in his guard forever) and clears the gates that judge
    /// his own rhythm (the repeat bar: a lesson throws the same move every exchange).</summary>
    public IEnumerator SenseiAttack(AttackType type, bool special)
    {
        int my = ++attackSerial;
        LastAttackFired = false; AttackGaveUp = false;
        if (Sensei == null) { AttackGaveUp = true; yield break; }
        float giveUp = Time.time + 3f;
        while (Time.time < giveUp && (Sensei.Combat.IsInHitstun || Sensei.Combat.GuardBreakRecovering || Sensei.Combat.IsAttacking || Sensei.IsDead))
            yield return null;
        if (my != attackSerial) yield break;
        guardUp = false;
        yield return null; yield return null;   // the release reaches the fighter before the swing starts
        if (my != attackSerial) { guardUp = true; yield break; }
        bool fired = false;
        for (int tries = 0; tries < 30 && !fired && my == attackSerial; tries++)
        {
            Sensei.Stats.currentStamina = Sensei.Stats.maxStamina;   // a lesson strike is never refused for stamina
            Sensei.Combat.ClearRhythmLocks();
            fired = Sensei.Combat.TryAttack(type, special);
            if (!fired) yield return null;
        }
        LastAttackFired = fired;
        AttackGaveUp = !fired;
        if (!fired) Debug.LogWarning($"Story: the Sensei's {(special ? "SP+" : "")}{type} did not fire");
        while (fired && Sensei.Combat.IsAttacking && my == attackSerial) yield return null;
        if (my == attackSerial) guardUp = true;
    }

    int attackSerial;
    /// <summary>Drops any <see cref="SenseiAttack"/> still waiting to throw (the demonstration replayed) and puts the
    /// guard back up.</summary>
    public void CancelAttack() { attackSerial++; guardUp = true; }

    public bool SenseiBusy => Sensei != null && Sensei.Combat.IsAttacking;

    void Update()
    {
        if (!Active) return;
        var im = InputManager.Instance;
        if (Sensei != null && im != null && guardUp && !Sensei.Combat.IsAttacking)
            im.SetAiInput(Sensei.PlayerNumber, new InputData { blockHeld = true });
        if (Time.timeScale <= 0f || !FastRegen) return;
        Regen(Player);
        Regen(Sensei);
    }

    void Regen(Fighter f)
    {
        if (f == null || !f.gameObject.activeInHierarchy) return;
        var s = f.Stats;
        if (!watch.TryGetValue(f, out var w)) w = (s.currentHealth, -99f);
        if (s.currentHealth < w.hp) w.hurtAt = Time.time;
        if (Time.time - w.hurtAt >= regenDelay && !s.OnNonLethalFloor) s.RegenerateHealth(Time.deltaTime, regenPerSecond);
        w.hp = s.currentHealth;
        watch[f] = w;
    }
}
