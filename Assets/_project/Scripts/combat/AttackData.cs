using UnityEngine;

/// <summary>
/// Vertical region a strike targets. Drives blocking, anti-air, and overhead/low mixups.
/// </summary>
public enum AttackHeight
{
    High,
    Mid,
    Low,
    Overhead,
    Uppercut
}

/// <summary>
/// Attack tier — used to map button input (L/M/H/Special) to an authored move.
/// </summary>
public enum AttackType
{
    Light,
    Medium,
    Heavy,
    Special
}

/// <summary>
/// Where a strike comes from, seen from the attacker. Decides which dash directions
/// evade it (see <see cref="Fighter.ResolveDashAgainst"/>).
/// </summary>
public enum AttackOrigin
{
    Overhead,       // comes down from above
    AttackerLeft,   // horizontal swing starting on the attacker's left (arrives at the defender's right)
    AttackerRight,  // horizontal swing starting on the attacker's right (arrives at the defender's left)
    Thrust          // straight in; only a sidestep gets out of the way
}

/// <summary>
/// Which side the defender is struck from, seen from the defender. Authored per attack
/// and used to pick the get-hit animation. Straight covers overheads and thrusts.
/// </summary>
/// <summary>How the weapon aim may correct a strike: see AttackData.aimMode.</summary>
public enum AimMode { Full, YawOnly, Off }

/// <summary>
/// What a string link needs from the previous hit before it may be cancelled into.
/// Fighting-game convention (MK-style): plain strings continue whether or not the hit
/// connects, while the bigger enders are earned by connecting first.
/// </summary>
public enum CancelRequirement
{
    Always,       // the string continues even on a whiff
    HitOrBlock,   // the blade must have touched the opponent (guard counts)
    HitOnly       // only an unblocked hit opens it
}

/// <summary>
/// One branch of a string: which button continues it, and the move that comes out.
/// The follow-up is a normal <see cref="AttackData"/>, so it carries its own frame data,
/// hitbox and animator trigger - and can define links of its own, which is what makes
/// a chain of any length.
/// </summary>
[System.Serializable]
public class ComboLink
{
    [Tooltip("Button that continues the string.")]
    public AttackType input = AttackType.Heavy;

    [Tooltip("Move that comes out instead of this attack's recovery.")]
    public AttackData followUp;

    [Tooltip("What the previous hit must have done before this branch opens.")]
    public CancelRequirement requires = CancelRequirement.Always;
}

public enum HitSide
{
    Straight,
    Left,
    Right
}

/// <summary>
/// Frame-data and hitbox configuration for a single attack.
/// Authored as a ScriptableObject so designers can tune without recompiling.
/// All frame counts are at 60fps.
/// </summary>
[CreateAssetMenu(fileName = "NewAttackData", menuName = "CRUCIBLE/Attack Data")]
public class AttackData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string attackName = "New Attack";

    [Header("Damage & Cost")]
    [Range(10, 50)]
    [SerializeField] private int damage = 15;

    [Range(5, 70)]
    [SerializeField] private int staminaCost = 10;

    [Range(0, 100)]
    [Tooltip("Percentage of defender armor bypassed by this attack (0-100).")]
    [SerializeField] private int armorPierce = 0;

    [Header("Frame Data (60fps)")]
    [Range(1, 30)]
    [Tooltip("Frames before the hitbox becomes active.")]
    [SerializeField] private int startupFrames = 8;

    [Range(1, 15)]
    [Tooltip("Frames the hitbox is active and can hit.")]
    [SerializeField] private int activeFrames = 3;

    [Range(5, 40)]
    [Tooltip("Frames of recovery after the active window.")]
    [SerializeField] private int recoveryFrames = 12;

    [Header("Animation")]
    [SerializeField] private AnimationClip animationClip;
    [SerializeField] private string animatorTriggerName = "LightAttack";

    [Header("Classification")]
    [SerializeField] private AttackHeight attackHeight = AttackHeight.Mid;
    [SerializeField] private AttackType attackType = AttackType.Light;

    [Header("Dodge Rules")]
    [Tooltip("Where the strike comes from, from the attacker's point of view. Decides which dash directions evade it, " +
             "which ones open a counter, and which ones dash straight into it for extra damage.")]
    [SerializeField] private AttackOrigin comesFrom = AttackOrigin.Overhead;

    [Header("Hit Reaction")]
    [Tooltip("Which side the defender is struck from, from the defender's point of view. Picks the get-hit animation " +
             "(animator int HitSide: 0 = Straight, 1 = Left, 2 = Right). Set it by hand; nothing is detected at runtime.")]
    [SerializeField] private HitSide hitSide = HitSide.Straight;

    [Range(1, 60)]
    [Tooltip("Frames (60fps) the defender is locked in the get-hit animation after this lands: no attacking, dashing or walking. " +
             "Match it to the flinch clip's length.")]
    [SerializeField] private int hitstunFrames = 12;

    [Header("Hitbox")]
    [Tooltip("How far the hitbox reaches in front of the attacker, in world units (the same units as dash distance " +
             "and fighter radius). The fighter's model scale does not change it. Select the fighter to preview it in the Scene view.")]
    [SerializeField] private float hitboxRange = 1.5f;

    [Tooltip("Shifts the hitbox relative to the attacker, in world units. Y = height off the ground; Z pushes its start away from the body.")]
    [SerializeField] private Vector3 hitboxOffset = Vector3.zero;

    [Tooltip("Width (X) and height (Y) of the hitbox in world units. Its depth comes from Hitbox Range, so Z is ignored.")]
    [SerializeField] private Vector3 hitboxSize = new Vector3(1f, 1f, 1f);

    [Header("Aim")]
    [Tooltip("How WeaponAim corrects the blade during this attack. Full: point the blade at the opponent's chest (thrusts, " +
             "level cuts). YawOnly: only turn it left/right onto the centre line, keep the clip's pitch (overheads, chops: " +
             "a vertical cut must stay vertical). Off: leave the clip alone (spins).")]
    [SerializeField] private AimMode aimMode = AimMode.Full;

    [Tooltip("Largest aim correction in degrees for this attack. -1 = the WeaponAim component's own cap.")]
    [SerializeField] private float aimMaxAngle = -1f;

    [Header("Lunge")]
    [Tooltip("During the startup the attacker steps toward the opponent until they are this far apart (centre to centre, " +
             "world units), so the strike reaches whatever the clip does. -1 = BalanceConfig.ATTACK_LUNGE_STOP.")]
    [SerializeField] private float lungeStopDistance = -1f;

    [Tooltip("Farthest the attacker may travel during the lunge, world units. 0 = no lunge, -1 = BalanceConfig.ATTACK_LUNGE_MAX.")]
    [SerializeField] private float lungeMaxDistance = -1f;

    [Header("Combo (strings)")]
    [Tooltip("Branches off this attack. Pressing a listed button inside the cancel window drops the rest " +
             "of this attack's recovery and starts the follow-up, so the two read as one action.")]
    [SerializeField] private ComboLink[] comboLinks;

    [Range(-1, 60)]
    [Tooltip("First frame (counted from this attack's start) a follow-up may come out. -1 = the first recovery frame, " +
             "i.e. as soon as this attack's own active window is over. Authoring an earlier frame is allowed and lets a " +
             "string continue the moment the blade connects, but the follow-up still waits for a swing that has not " +
             "touched anything, so the opener never loses its hit.")]
    [SerializeField] private int cancelStartFrame = -1;

    [Range(-1, 90)]
    [Tooltip("Last frame a follow-up may come out. -1 = the final recovery frame.")]
    [SerializeField] private int cancelEndFrame = -1;

    [Header("Punishment")]
    [Tooltip("Extra recovery frames (60fps) when this attack is dodged by a dash: the attacker is left open, " +
             "facing locked and unable to walk, so the dodger can counter. -1 = the CombatSystem's own setting.")]
    [SerializeField] private int dodgedRecoveryFrames = -1;

    /// <summary>Display name for logs/UI (e.g. "Axe Slash").</summary>
    public string AttackName => attackName;

    /// <summary>Base damage before multipliers and armor.</summary>
    public int Damage => damage;

    /// <summary>Stamina spent on attempt.</summary>
    public int StaminaCost => staminaCost;

    /// <summary>Frames before the hitbox activates.</summary>
    public int StartupFrames => startupFrames;

    /// <summary>Frames the hitbox is live.</summary>
    public int ActiveFrames => activeFrames;

    /// <summary>Frames of recovery after active window ends.</summary>
    public int RecoveryFrames => recoveryFrames;

    /// <summary>Percentage of defender armor that this attack ignores (0-100).</summary>
    public int ArmorPierce => armorPierce;

    /// <summary>Authored animation clip for this attack.</summary>
    public AnimationClip AnimationClip => animationClip;

    /// <summary>Animator trigger string fired to start playback.</summary>
    public string AnimatorTriggerName => animatorTriggerName;

    /// <summary>Vertical region — drives block/dodge logic.</summary>
    public AttackHeight AttackHeight => attackHeight;

    /// <summary>Tier — maps to button input.</summary>
    public AttackType AttackType => attackType;

    /// <summary>Where the strike comes from (attacker's view) — drives dash evasion.</summary>
    public AttackOrigin ComesFrom => comesFrom;

    /// <summary>Side the defender is struck from (defender's view) — picks the get-hit animation.</summary>
    public HitSide HitSide => hitSide;

    /// <summary>Frames the defender is locked in hitstun after this attack lands.</summary>
    public int HitstunFrames => hitstunFrames;

    /// <summary>Reach along the attacker's forward axis, in the fighter's local units.</summary>
    public float HitboxRange => hitboxRange;

    /// <summary>Local-space offset for the hitbox center.</summary>
    public Vector3 HitboxOffset => hitboxOffset;

    /// <summary>Local-space size of the hitbox.</summary>
    public Vector3 HitboxSize => hitboxSize;

    public AimMode AimMode => aimMode;
    /// <summary>Aim cap for this attack, or -1 to use the component's.</summary>
    public float AimMaxAngle => aimMaxAngle;

    /// <summary>Centre-to-centre distance the lunge closes to (world units).</summary>
    public float LungeStopDistance => lungeStopDistance < 0f ? BalanceConfig.ATTACK_LUNGE_STOP : lungeStopDistance;

    /// <summary>Longest lunge this attack may make (world units, 0 = none).</summary>
    public float LungeMaxDistance => lungeMaxDistance < 0f ? BalanceConfig.ATTACK_LUNGE_MAX : lungeMaxDistance;

    /// <summary>Extra recovery frames when this attack is dodged, or -1 to use the CombatSystem's setting.</summary>
    public int DodgedRecoveryFrames => dodgedRecoveryFrames;

    /// <summary>Branches off this attack (may be empty).</summary>
    public ComboLink[] ComboLinks => comboLinks;

    /// <summary>True if any button continues this attack into something else.</summary>
    public bool HasComboLinks => comboLinks != null && comboLinks.Length > 0;

    /// <summary>First frame a follow-up may come out (defaults to the first recovery frame).</summary>
    public int CancelStartFrame => cancelStartFrame < 0 ? startupFrames + activeFrames : cancelStartFrame;

    /// <summary>Last frame a follow-up may come out (defaults to the end of recovery).</summary>
    public int CancelEndFrame =>
        cancelEndFrame < 0 ? startupFrames + activeFrames + recoveryFrames : cancelEndFrame;

    /// <summary>The branch this button opens, or null if the button does not continue this string.</summary>
    public ComboLink FindLink(AttackType input)
    {
        if (comboLinks == null) return null;
        foreach (var link in comboLinks)
        {
            if (link != null && link.followUp != null && link.input == input) return link;
        }
        return null;
    }

    /// <summary>
    /// Total attack duration in seconds (startup + active + recovery, at 60fps).
    /// </summary>
    public float GetTotalDuration()
    {
        return (startupFrames + activeFrames + recoveryFrames) / 60f;
    }
}
