using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's one audio surface (Design/Plan_Audio.md). Lives on the GameMangers object and owns
/// every AudioSource: two music sources that crossfade, a looping wind bed for the fight, and a
/// small pool for one-shots. Plain Unity audio on purpose - no mixer asset, no middleware - so the
/// web build carries nothing extra and the Settings sliders are just floats applied here.
///
/// Everything is reached through the static methods, which no-op while no instance exists, so the
/// hooks in GameMenu and CombatSystem are safe before `Crucible > Audio > Set up audio` has run.
/// Channels: MUSIC (menu/fight tracks), EFFECTS (combat, UI, wind), VOICE (efforts and death
/// cries). Master stays on AudioListener.volume, where Settings already had it.
/// </summary>
public class CrucibleAudio : MonoBehaviour
{
    public static CrucibleAudio Instance { get; private set; }

    public enum UiSound { Navigate, Select, Back, Wipe, Boot, CountBeat, CountFight }
    public enum CombatSound { Swish, Block, Parry, Hit, Dodge }
    public enum MusicMode { Menu, Fight }

    [Serializable]
    public class VoiceBank
    {
        [Tooltip("Matched against Fighter.CharacterName (prefix, case-insensitive).")]
        public string character;
        [Tooltip("Short exhale, thrown with Light and Medium swings.")]
        public AudioClip[] effortLight;
        [Tooltip("Full effort, thrown with Heavy and Special swings.")]
        public AudioClip[] effortHeavy;
        [Tooltip("The K.O. cry. Long, human, no words - each fighter his own register.")]
        public AudioClip[] death;
    }

    [Header("UI (metal ticks and hits)")]
    public AudioClip[] uiNavigate;
    public AudioClip[] uiSelect;
    public AudioClip[] uiBack;
    public AudioClip[] uiWipe;
    public AudioClip[] uiBoot;
    public AudioClip[] uiCountBeat;
    public AudioClip[] uiCountFight;

    [Header("Combat")]
    [Tooltip("Air cut, played when the hitbox arms on EVERY swing; a landed hit simply layers over it. Pitched down and up in level with attack weight.")]
    public AudioClip[] swish;
    public AudioClip[] block;
    public AudioClip[] parry;
    public AudioClip[] hit;
    public AudioClip[] dodge;

    [Header("Voices")]
    public VoiceBank[] voices;

    [Header("Music and ambience")]
    public AudioClip menuMusic;
    public AudioClip fightMusic;
    public AudioClip windLoop;
    [Tooltip("Level of the wind bed under the fight, before the Effects slider.")]
    [Range(0f, 1f)] public float windLevel = 0.3f;

    // ---------------------------------------------------------------- channel volumes (Settings)

    float musicVolume = 0.8f;
    float effectsVolume = 1f;
    float voiceVolume = 1f;

    // ---------------------------------------------------------------- runtime state

    const int OneShotSources = 12;

    // The ceiling on EVERYTHING this component plays, under the Settings sliders - the game at
    // 100% on every slider still tops out here. Raised or lowered in one place, the mix keeps.
    const float MasterLevel = 0.5f;

    const float MusicFade = 1.2f;       // seconds for a music crossfade
    const float WindFade = 1.8f;        // seconds for the wind to breathe in or out
    const float DuckLevel = 0.35f;      // music and wind while the pause menu is up

    AudioSource[] music = new AudioSource[2];   // the crossfade pair
    int musicFront = -1;                        // which of the pair carries the current track
    AudioSource wind;
    AudioSource[] shots;
    int nextShot;

    MusicMode mode = MusicMode.Menu;
    bool windOn;
    bool ducked;
    float duck = 1f;                            // eased toward 1 or DuckLevel

    // Last variant played per clip set, so a set never repeats back to back.
    readonly Dictionary<AudioClip[], int> lastPick = new Dictionary<AudioClip[], int>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        for (int i = 0; i < 2; i++) music[i] = MakeSource("Music" + i, loop: true);
        wind = MakeSource("Wind", loop: true);
        shots = new AudioSource[OneShotSources];
        for (int i = 0; i < OneShotSources; i++) shots[i] = MakeSource("Shot" + i, loop: false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    AudioSource MakeSource(string name, bool loop)
    {
        var go = new GameObject("Audio_" + name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;        // the fight is one screen; everything is 2D
        s.volume = 0f;
        return s;
    }

    void Start()
    {
        ApplyMusic(mode);           // the menu track starts with the boot screen
    }

    void Update()
    {
        // Unscaled on purpose: music settles and the wind breathes while the game is paused
        // (timeScale 0) exactly as it does in play.
        float dt = Time.unscaledDeltaTime;

        duck = Mathf.MoveTowards(duck, ducked ? DuckLevel : 1f, dt / 0.4f);

        for (int i = 0; i < 2; i++)
        {
            if (music[i] == null) continue;
            float target = (i == musicFront && music[i].clip != null) ? musicVolume * duck * MasterLevel : 0f;
            music[i].volume = Mathf.MoveTowards(music[i].volume, target, dt / MusicFade);
            if (music[i].volume <= 0f && music[i].isPlaying && i != musicFront) music[i].Stop();
        }

        if (wind != null)
        {
            float target = windOn && windLoop != null ? windLevel * effectsVolume * duck * MasterLevel : 0f;
            wind.volume = Mathf.MoveTowards(wind.volume, target, dt / WindFade);
            if (wind.volume <= 0f && wind.isPlaying && !windOn) wind.Stop();
        }
    }

    // ---------------------------------------------------------------- static API (all null-safe)

    public static void SetVolumes(float musicVol, float effectsVol, float voiceVol)
    {
        if (Instance == null) return;
        Instance.musicVolume = Mathf.Clamp01(musicVol);
        Instance.effectsVolume = Mathf.Clamp01(effectsVol);
        Instance.voiceVolume = Mathf.Clamp01(voiceVol);
    }

    /// <summary>Menu or fight: picks the track and whether the wind bed runs.</summary>
    public static void Scene(MusicMode m)
    {
        if (Instance == null) return;
        if (Instance.mode != m) Instance.ApplyMusic(m);
        Instance.windOn = m == MusicMode.Fight;
        if (Instance.windOn && Instance.windLoop != null && !Instance.wind.isPlaying)
        {
            Instance.wind.clip = Instance.windLoop;
            Instance.wind.Play();
        }
    }

    /// <summary>Pause menu up: music and wind drop to a murmur, one-shots keep their level.</summary>
    public static void Ducked(bool on)
    {
        if (Instance != null) Instance.ducked = on;
    }

    public static void Ui(UiSound s)
    {
        if (Instance == null) return;
        switch (s)
        {
            case UiSound.Navigate: Instance.Shot(Instance.uiNavigate, Instance.effectsVolume * 0.45f, 1f, 0.06f); break;
            case UiSound.Select: Instance.Shot(Instance.uiSelect, Instance.effectsVolume * 0.8f, 1f, 0.03f); break;
            case UiSound.Back: Instance.Shot(Instance.uiBack, Instance.effectsVolume * 0.6f, 0.92f, 0.03f); break;
            case UiSound.Wipe: Instance.Shot(Instance.uiWipe, Instance.effectsVolume * 0.5f, 1f, 0.04f); break;
            case UiSound.Boot: Instance.Shot(Instance.uiBoot, Instance.effectsVolume, 1f, 0f); break;
            case UiSound.CountBeat: Instance.Shot(Instance.uiCountBeat, Instance.effectsVolume * 0.6f, 1f, 0.02f); break;
            case UiSound.CountFight: Instance.Shot(Instance.uiCountFight, Instance.effectsVolume, 1f, 0f); break;
        }
    }

    /// <summary>
    /// A fight event, scaled by the weight of the move that caused it: a heavy's swish is lower
    /// and louder than a light's, a heavy landing hits harder. Weight comes from the AttackType.
    /// </summary>
    public static void Combat(CombatSound s, AttackType type)
    {
        if (Instance == null) return;
        float w = Weight(type);
        float vol = Instance.effectsVolume;
        switch (s)
        {
            case CombatSound.Swish: Instance.Shot(Instance.swish, vol * (0.45f + 0.45f * w), 1.25f - 0.45f * w, 0.05f); break;
            case CombatSound.Block: Instance.Shot(Instance.block, vol * (0.7f + 0.3f * w), 1.1f - 0.2f * w, 0.05f); break;
            case CombatSound.Parry: Instance.Shot(Instance.parry, vol, 1f, 0.03f); break;
            case CombatSound.Hit: Instance.Shot(Instance.hit, vol * (0.7f + 0.3f * w), 1.15f - 0.3f * w, 0.06f); break;
            // Full level: the dodge chime has to carry over the attacker's own swish, which
            // plays in the same instant (it is the dodged swing's arming sound).
            case CombatSound.Dodge: Instance.Shot(Instance.dodge, vol, 1f, 0.06f); break;
        }
    }

    /// <summary>The breath thrown with a swing. Light/Medium take the short exhale, Heavy/Special the full one.</summary>
    public static void Effort(string character, AttackType type)
    {
        if (Instance == null) return;
        var bank = Instance.Bank(character);
        if (bank == null) return;
        bool heavy = type == AttackType.Heavy || type == AttackType.Special;
        var set = heavy ? bank.effortHeavy : bank.effortLight;
        if (set == null || set.Length == 0) set = heavy ? bank.effortLight : bank.effortHeavy;
        Instance.Shot(set, Instance.voiceVolume * (heavy ? 0.9f : 0.7f), 1f, 0.05f);
    }

    /// <summary>The K.O. cry.</summary>
    public static void Death(string character)
    {
        if (Instance == null) return;
        var bank = Instance.Bank(character);
        if (bank != null) Instance.Shot(bank.death, Instance.voiceVolume, 1f, 0.02f);
    }

    // ---------------------------------------------------------------- internals

    static float Weight(AttackType t) =>
        t == AttackType.Light ? 0.35f : t == AttackType.Medium ? 0.6f : t == AttackType.Heavy ? 0.85f : 1f;

    VoiceBank Bank(string character)
    {
        if (voices == null || string.IsNullOrEmpty(character)) return null;
        foreach (var b in voices)
        {
            if (b != null && !string.IsNullOrEmpty(b.character) &&
                character.StartsWith(b.character, StringComparison.OrdinalIgnoreCase)) return b;
        }
        return null;
    }

    void ApplyMusic(MusicMode m)
    {
        mode = m;
        AudioClip track = m == MusicMode.Fight ? fightMusic : menuMusic;
        int back = musicFront == 0 ? 1 : 0;                // the source NOT carrying the old track
        if (musicFront >= 0 && music[musicFront] != null && music[musicFront].clip == track) return;

        musicFront = back;
        if (music[back] == null) return;
        music[back].clip = track;
        if (track != null) music[back].Play();              // volume eases up in Update
    }

    /// <summary>Plays one variant of a set on a pooled source. Never repeats the previous variant.</summary>
    void Shot(AudioClip[] set, float volume, float pitch, float jitter)
    {
        if (set == null || set.Length == 0 || shots == null) return;

        int pick = 0;
        if (set.Length > 1)
        {
            lastPick.TryGetValue(set, out int last);
            pick = UnityEngine.Random.Range(0, set.Length - 1);
            if (pick >= last) pick++;                       // skip the one just played
        }
        lastPick[set] = pick;
        var clip = set[pick];
        if (clip == null) return;

        var s = shots[nextShot];
        nextShot = (nextShot + 1) % shots.Length;
        if (s == null) return;
        s.Stop();
        s.clip = clip;
        s.volume = Mathf.Clamp01(volume) * MasterLevel;
        s.pitch = pitch * (1f + UnityEngine.Random.Range(-jitter, jitter));
        s.Play();
    }
}
