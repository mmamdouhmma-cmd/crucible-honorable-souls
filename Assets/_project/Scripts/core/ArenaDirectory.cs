using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The arenas a player can choose between, and the switch that puts one on screen.
///
/// Every arena is already a (mostly inactive) root object in the scene, placed by
/// <c>Crucible &gt; Arena &gt; Tripo</c>, so switching is just activating one root and turning the
/// others off. Each arena root carries its own <see cref="ArenaBounds"/>, whose OnEnable/OnDisable
/// hand the active one over, so the fighters are fenced by whichever ground they are standing on.
///
/// <para><b>This list is also what the build ships.</b> An arena with <c>shipped</c> off is left out
/// of the player build by <c>CrucibleBuild</c> (an inactive root still serializes into the scene,
/// meshes and 2K textures and all), which is what keeps the web build small. An arena that is not
/// shipped is therefore not selectable either - the two facts are the same toggle on purpose.</para>
/// </summary>
[DisallowMultipleComponent]
public class ArenaDirectory : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        [Tooltip("Shown on the arena select screen.")]
        public string displayName = "Arena";

        [Tooltip("Name of the arena's root GameObject in the scene.")]
        public string rootName = "";

        [Tooltip("One line of flavour under the name.")]
        public string blurb = "";

        [Tooltip("Ship this arena in player builds. Off = cut from the build AND not selectable.")]
        public bool shipped = true;

        [NonSerialized] public GameObject root;
    }

    public static ArenaDirectory Instance { get; private set; }

    const string PrefKey = "crucible.arena.v1";

    [Tooltip("Every arena root in the scene, in the order the select screen offers them.")]
    [SerializeField]
    private List<Entry> arenas = new List<Entry>
    {
        new Entry { displayName = "Medieval Fortress", rootName = "MedievalFortress", blurb = "Grey stone, high walls, no way out.",  shipped = true },
        new Entry { displayName = "Desert Arena",      rootName = "DesertArena",      blurb = "Sun-bleached sand and old blood.",    shipped = true },
        new Entry { displayName = "Al-Quds Courtyard", rootName = "Arena_AlQuds",     blurb = "Arches, gilded dome, holy ground.",   shipped = false },
        new Entry { displayName = "Forest Clearing",   rootName = "ForestClearing",   blurb = "Wet earth under a closed canopy.",    shipped = false },
    };

    [Tooltip("Test ground plane, hidden whenever a real arena is active.")]
    [SerializeField] private string testGroundName = "Ground";

    private int current = -1;

    /// <summary>Index into <see cref="Selectable"/> of the arena on screen, or 0 if unknown.</summary>
    public int CurrentIndex => Mathf.Max(0, current);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        Resolve();
    }

    /// <summary>Looks every entry's root up in the scene, including the switched-off ones.</summary>
    private void Resolve()
    {
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var entry in arenas)
        {
            entry.root = null;
            foreach (var go in roots)
            {
                if (string.Equals(go.name, entry.rootName, StringComparison.OrdinalIgnoreCase)) { entry.root = go; break; }
            }
        }
    }

    /// <summary>
    /// The arenas a player can actually pick: shipped, and present in this build of the scene.
    /// Rebuilt on each call, so it stays honest after the build strips the ones left out.
    /// </summary>
    public List<Entry> Selectable
    {
        get
        {
            var list = new List<Entry>();
            foreach (var e in arenas) if (e.shipped && e.root != null) list.Add(e);
            if (list.Count == 0)
            {
                // Nothing matched (renamed roots, or an arena-less test scene): offer whatever is
                // active so the select screen is never an empty box.
                foreach (var e in arenas) if (e.root != null) list.Add(e);
            }
            return list;
        }
    }

    /// <summary>Index of the arena currently active, as an index into <see cref="Selectable"/>.</summary>
    public int IndexOfActive()
    {
        var list = Selectable;
        for (int i = 0; i < list.Count; i++) if (list[i].root != null && list[i].root.activeSelf) return i;
        return 0;
    }

    /// <summary>The player's last choice, clamped to what this build actually has.</summary>
    public int LoadSaved()
    {
        var list = Selectable;
        if (list.Count == 0) return 0;
        string saved = PlayerPrefs.GetString(PrefKey, "");
        for (int i = 0; i < list.Count; i++) if (list[i].rootName == saved) return i;
        return IndexOfActive();
    }

    /// <summary>
    /// Puts arena <paramref name="index"/> on screen and turns every other one off. Safe to call
    /// with the arena that is already active (it still hides the test ground).
    /// </summary>
    public void Select(int index, bool remember = true)
    {
        Resolve();
        var list = Selectable;
        if (list.Count == 0) return;
        index = Mathf.Clamp(index, 0, list.Count - 1);
        var chosen = list[index];

        foreach (var e in arenas)
        {
            if (e.root == null) continue;
            bool on = ReferenceEquals(e, chosen);
            if (e.root.activeSelf != on) e.root.SetActive(on);
        }

        // The old flat test plane sits at the origin under every arena; it would z-fight the floor.
        if (!string.IsNullOrEmpty(testGroundName))
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (string.Equals(go.name, testGroundName, StringComparison.OrdinalIgnoreCase) && go.activeSelf)
                    go.SetActive(false);
            }
        }

        current = index;
        if (remember)
        {
            PlayerPrefs.SetString(PrefKey, chosen.rootName);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Display name of a selectable arena, for the menus.</summary>
    public string NameOf(int index)
    {
        var list = Selectable;
        if (list.Count == 0) return "Arena";
        return list[Mathf.Clamp(index, 0, list.Count - 1)].displayName;
    }

    /// <summary>Flavour line of a selectable arena, for the menus.</summary>
    public string BlurbOf(int index)
    {
        var list = Selectable;
        if (list.Count == 0) return "";
        return list[Mathf.Clamp(index, 0, list.Count - 1)].blurb;
    }
}
