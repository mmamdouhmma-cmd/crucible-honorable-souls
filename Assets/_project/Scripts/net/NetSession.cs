using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Online play through the relay in Server/relay.js, which the hosting laptop runs behind a Cloudflare
/// quick tunnel (Server/online.js). The tunnel's address changes on every start, so the launcher publishes
/// it to a public GitHub gist (crucible-server.json) and <see cref="ConnectToServer"/> finds that gist by
/// the owner's GitHub name - nobody types a link. Lobby: rooms have a name and an optional 6-digit code. The HOST runs the whole fight; the GUEST sends its
/// input every frame and shows the state the host sends (positions, facing, health, stamina, animator state).
/// The guest's human plays Player 2 with the Player 1 controls (keyboard P1 layout, pad, or the full-screen
/// touch pad). Messages are short text frames:
///   guest -> host   I move block L M H dashX dashY        (one per frame; pressed flags fire once)
///   host  -> guest  S t | x y z yaw hp st anim time move blocking | ... (one per fighter, ~20 Hz)
///                   R  (match (re)started)   P 1|0 (pause)   K (KO shown by the guest's own menu from hp)
///   guest -> host   P 1|0 (pause request)
///   both ways       X cursor locked (character select)   V vote (K.O. card: 1 rematch, 2 select)
///   both ways       W version (on joining; a different or missing one sends both back to the lobby)
///   host  -> guest  A arena (arena select)   F (on to the versus card)   G (back to character select)
///                   E ... (an effect, sound, camera shake or kill shot that just played on the host)
/// </summary>
public class NetSession : MonoBehaviour
{
    public static NetSession Instance { get; private set; }

    public enum Role { None, Host, Guest }
    public enum Phase { Idle, Connecting, Hosting, Listing, Joined, Playing, Failed, Lobby }

    [Tooltip("GitHub account whose public gist 'crucible-server.json' holds the live server address " +
             "(written by Server/online.js on the hosting laptop).")]
    public string serverGistOwner = "mmamdouhmma-cmd";
    [Tooltip("Tried when the published address cannot be reached - the laptop that runs the server itself.")]
    public string localRelayUrl = "ws://localhost:8080/ws";
    [Tooltip("Seconds to wait for the server before calling the connection failed.")]
    public float connectTimeout = 10f;
    [Tooltip("State snapshots per second sent by the host.")]
    public float sendRate = 20f;

    public enum JoinError { None, NoRoom, Full, BadCode }

    /// <summary>An open game in the lobby list.</summary>
    public struct RoomInfo { public string id, name; public bool locked; }

    public Role CurrentRole { get; private set; } = Role.None;
    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    public string RoomName { get; private set; } = "";
    public string RoomCode { get; private set; } = "";
    public string Status { get; private set; } = "";
    public List<RoomInfo> Rooms { get; } = new List<RoomInfo>();
    public bool PeerConnected { get; private set; }
    public JoinError LastJoinError { get; private set; }
    /// <summary>The fighter (roster index) the other player picked in the room, -1 until they say.</summary>
    public int PeerCharacter { get; private set; } = -1;
    /// <summary>Character select: whether the other player has locked the fighter under their cursor.</summary>
    public bool PeerLocked { get; private set; }
    /// <summary>K.O. card: the other player's vote - 0 none yet, 1 rematch, 2 character select.</summary>
    public int PeerVote { get; private set; }
    public event Action OnPeerJoined, OnStartReceived, OnPeerLeft, OnJoined;
    /// <summary>The other player runs another version of the game; the room has already been left. Arg = their version, "" if too old to say.</summary>
    public event Action<string> OnVersionMismatch;
    /// <summary>Seconds a newly met player has to say their version. Builds before 0.3.0 never do, and
    /// their arena list is shorter, so an arena index from one side would mean a different arena on the other.</summary>
    const float VersionWait = 4f;
    string peerVersion;
    float peerMetAt = -1f;
    /// <summary>Guest: the host moved the arena cursor / confirmed it / sent everyone back to the select.</summary>
    public event Action<int> OnArenaReceived;
    public event Action OnVersusReceived, OnSelectReceived;
    public event Action<bool> OnPauseReceived;
    /// <summary>Guest: the pause as the host says it stands - on, paused by (1 host / 2 guest), pauses spent by each.</summary>
    public event Action<bool, int, int, int> OnPauseState;
    /// <summary>Guest: the host's match rules for the coming fight.</summary>
    public float RulesSpeed { get; private set; } = 1f;
    public bool RulesRegen { get; private set; } = true;
    /// <summary>Seconds without a word from the other player before they count as disconnected.</summary>
    public const float PeerTimeout = 8f;
    float lastPeerHeard, nextHeartbeat;
    /// <summary>The host's start message: Player 1's and Player 2's roster index and the arena (-1 = not sent).</summary>
    public int StartP1 { get; private set; } = -1;
    public int StartP2 { get; private set; } = -1;
    public int StartArena { get; private set; } = -1;

    WebSocketClient ws;
    float nextSend, nextList, connectStarted;
    Fighter[] fighters = new Fighter[0];
    Animator[] animators = new Animator[0];
    readonly Vector3[] targetPos = new Vector3[2];
    readonly float[] targetYaw = new float[2];
    readonly int[] lastAnim = new int[2];
    InputData guestInput;                       // host: the guest's input snapshot for this frame
    bool guestInputFresh;
    readonly List<string> candidates = new List<string>();   // relay addresses still to try, in order
    Coroutine locating;
    bool helloSent;
    int myCharacter = -1;
    bool myLocked;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { ws?.Close(); }

    /// <summary>True from the relay's WELCOME until <see cref="Disconnect"/> (lobby, rooms, fight).</summary>
    public bool Connected => ws != null && ws.Current == WebSocketClient.State.Open &&
                             CurrentPhase != Phase.Connecting && CurrentPhase != Phase.Failed && CurrentPhase != Phase.Idle;

    /// <summary>
    /// Finds the server and opens the connection. In a browser the page's own host IS the relay; in the app
    /// the address comes from the gist, then localhost (the hosting laptop itself). Phase goes Connecting ->
    /// Lobby on the relay's WELCOME, or -> Failed once every address has failed or timed out.
    /// </summary>
    public void ConnectToServer()
    {
        Disconnect();
        CurrentPhase = Phase.Connecting;
        Status = "Looking for the server...";
        string page = WebSocketClient.PageUrl();
        if (!string.IsNullOrEmpty(page) && Uri.TryCreate(page, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http"))
        {
            string scheme = u.Scheme == "https" ? "wss" : "ws";
            candidates.Add($"{scheme}://{u.Host}:{(u.IsDefaultPort ? (u.Scheme == "https" ? 443 : 80) : u.Port)}/ws");
            TryNextCandidate();
            return;
        }
        locating = StartCoroutine(LocateAndConnect());
    }

    System.Collections.IEnumerator LocateAndConnect()
    {
        string published = null;
        // The gist list is public and needs no token. The raw_url it hands back is pinned to the latest
        // revision, so it can never be a stale cached copy of an old address.
        using (var list = UnityEngine.Networking.UnityWebRequest.Get($"https://api.github.com/users/{serverGistOwner}/gists?per_page=100"))
        {
            list.timeout = 8;
            list.SetRequestHeader("User-Agent", "CRUCIBLE");
            yield return list.SendWebRequest();
            string raw = list.result == UnityEngine.Networking.UnityWebRequest.Result.Success ? RawUrlOf(list.downloadHandler.text) : null;
            if (raw != null)
            {
                using (var file = UnityEngine.Networking.UnityWebRequest.Get(raw))
                {
                    file.timeout = 8;
                    yield return file.SendWebRequest();
                    if (file.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        var info = JsonUtility.FromJson<ServerInfo>(file.downloadHandler.text);
                        if (info != null && info.online && !string.IsNullOrEmpty(info.url)) published = info.url;
                    }
                }
            }
            else Debug.LogWarning("Online: could not read the server address (" + list.error + ")");
        }
        locating = null;
        if (CurrentPhase != Phase.Connecting) yield break;     // cancelled meanwhile
        if (published != null) candidates.Add(published);
        if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer)
            candidates.Add(localRelayUrl);
        TryNextCandidate();
    }

    [Serializable] class ServerInfo { public string url; public bool online; }

    /// <summary>The raw_url of crucible-server.json in the gist list (JsonUtility cannot read dynamic keys).</summary>
    static string RawUrlOf(string json)
    {
        int at = json.IndexOf("\"crucible-server.json\"", StringComparison.Ordinal);
        if (at < 0) return null;
        int key = json.IndexOf("\"raw_url\"", at, StringComparison.Ordinal);
        if (key < 0) return null;
        int open = json.IndexOf('"', json.IndexOf(':', key) + 1);
        int close = open >= 0 ? json.IndexOf('"', open + 1) : -1;
        return open >= 0 && close > open ? json.Substring(open + 1, close - open - 1) : null;
    }

    void TryNextCandidate()
    {
        if (candidates.Count == 0)
        {
            ws?.Close();
            CurrentPhase = Phase.Failed;
            Status = "Could not reach the server. It may be switched off.";
            return;
        }
        string url = candidates[0];
        candidates.RemoveAt(0);
        ws = ws ?? new WebSocketClient();
        ws.Connect(url);
        connectStarted = Time.unscaledTime;
        helloSent = false;
        Debug.Log("Online: trying " + url);
    }

    /// <summary>Open a room. <paramref name="code"/> is "" for an open game, or exactly 6 digits.</summary>
    public void Host(string name, string code)
    {
        if (!Connected) return;
        CurrentRole = Role.Host;
        RoomName = name;
        RoomCode = code ?? "";
        PeerConnected = false;
        PeerCharacter = -1;
        PeerLocked = false;
        PeerVote = 0;
        CurrentPhase = Phase.Hosting;
        Status = "Opening the game...";
        Send("HOST " + RoomCode + "\t" + name);
    }

    /// <summary>List the open games; the list refreshes every two seconds while this phase lasts.</summary>
    public void BrowseRooms()
    {
        if (!Connected) return;
        CurrentRole = Role.Guest;
        CurrentPhase = Phase.Listing;
        LastJoinError = JoinError.None;
        Rooms.Clear();                      // never show a game that closed while we were away
        nextList = 0f;
        Status = "Looking for games...";
    }

    public void Join(RoomInfo room, string code)
    {
        if (!Connected) { Status = "Not connected"; return; }
        CurrentRole = Role.Guest;
        RoomName = room.name;
        LastJoinError = JoinError.None;
        PeerCharacter = -1;
        Send("JOIN " + room.id + "\t" + (code ?? ""));
        Status = "Joining " + room.name + "...";
    }

    /// <summary>Leave the room but stay connected (back to the lobby).</summary>
    public void LeaveRoom()
    {
        Send("LEAVE");
        CurrentRole = Role.None;
        PeerConnected = false;
        PeerCharacter = -1;
        if (ws != null && ws.Current == WebSocketClient.State.Open && CurrentPhase != Phase.Failed) CurrentPhase = Phase.Lobby;
        Status = "";
        if (InputManager.Instance != null) InputManager.Instance.SetAi(2, false);
    }

    /// <summary>Close the connection entirely.</summary>
    public void Disconnect()
    {
        if (locating != null) { StopCoroutine(locating); locating = null; }
        candidates.Clear();
        ws?.Close();
        CurrentRole = Role.None;
        CurrentPhase = Phase.Idle;
        PeerConnected = false;
        PeerCharacter = -1;
        Rooms.Clear();
        Status = "";
        if (InputManager.Instance != null) InputManager.Instance.SetAi(2, false);
    }

    public void Leave() => Disconnect();

    /// <summary>Character select: my cursor and lock, sent on every change and repeated when the other player arrives.</summary>
    public void SendSelect(int index, bool locked)
    {
        myCharacter = index;
        myLocked = locked;
        Send("X " + index + " " + (locked ? 1 : 0));
    }
    void SendCharacterEcho() { if (myCharacter >= 0) Send("X " + myCharacter + " " + (myLocked ? 1 : 0)); }

    void MeetPeer() { peerVersion = null; peerMetAt = Time.unscaledTime; Send("W " + Application.version); }

    void VersionMismatch(string theirs)
    {
        Debug.Log($"Online: the other player runs {(theirs == "" ? "an older version" : "v" + theirs)}, this is v{Application.version} - leaving the room");
        peerMetAt = -1f;
        PeerConnected = false;
        PeerCharacter = -1;
        PeerLocked = false;
        PeerVote = 0;
        LeaveRoom();
        OnVersionMismatch?.Invoke(theirs);
    }

    /// <summary>Host: the arena cursor, so the guest watches the same ground come up.</summary>
    public void SendArena(int arena) => Send("A " + arena);
    /// <summary>Host: the arena is chosen, both go to the versus card.</summary>
    public void SendVersus() => Send("F");
    /// <summary>Host: both back to the character select (after the K.O. card's vote, or a back-out).</summary>
    public void SendSelectAgain() => Send("G");
    /// <summary>K.O. card: my vote, 0 none, 1 rematch, 2 character select.</summary>
    public void SendVote(int vote) => Send("V " + vote);

    /// <summary>Between fights: the room is open again and nothing streams. Votes and locks start over.</summary>
    public void EnterSelectPhase()
    {
        if (CurrentPhase == Phase.Playing) CurrentPhase = CurrentRole == Role.Guest ? Phase.Joined : Phase.Hosting;
        PeerVote = 0;
        PeerLocked = false;
        myLocked = false;
        if (CurrentRole == Role.Host && InputManager.Instance != null) InputManager.Instance.SetAi(2, false);
    }

    // ---------------------------------------------------------------- match hooks (called by GameMenu)

    /// <summary>Host: the fight begins. Guest input drives Player 2 through the InputManager's override slot.</summary>
    public void HostStartMatch(int p1, int p2, int arena, float speed, bool regen)
    {
        Bind();
        if (InputManager.Instance != null) InputManager.Instance.SetAi(2, true);
        Send(string.Format(CultureInfo.InvariantCulture, "R {0} {1} {2} {3:0.##} {4}", p1, p2, arena, speed, regen ? 1 : 0));
        CurrentPhase = Phase.Playing;
        PeerVote = 0;
        lastPeerHeard = Time.unscaledTime;
    }

    /// <summary>Host: the match rules shown in the guest's room (sent on every change and when they join).</summary>
    public void SendRules(float speed, bool regen) =>
        Send(string.Format(CultureInfo.InvariantCulture, "O {0:0.##} {1}", speed, regen ? 1 : 0));

    /// <summary>Host: the pause as it stands. <paramref name="by"/> 1 = host, 2 = guest; used = pauses spent.</summary>
    public void SendPauseState(bool on, int by, int used1, int used2) =>
        Send(on ? $"P 1 {by} {used1} {used2}" : $"P 0 0 {used1} {used2}");

    /// <summary>Guest: the host said the fight (re)started. Local simulation off; this side only displays.</summary>
    public void GuestEnterMatch()
    {
        Bind();
        foreach (var f in fighters)
        {
            f.enabled = false;
            var cs = f.GetComponent<CombatSystem>(); if (cs) cs.enabled = false;
            targetPos[f.PlayerNumber - 1] = f.transform.position;
            targetYaw[f.PlayerNumber - 1] = f.transform.eulerAngles.y;
        }
        CurrentPhase = Phase.Playing;
        PeerVote = 0;
    }

    public void GuestLeaveMatch()
    {
        foreach (var f in fighters)
        {
            var cs = f.GetComponent<CombatSystem>(); if (cs) cs.enabled = true;
        }
    }

    public void SendPause(bool on) => Send("P " + (on ? 1 : 0));

    // ---------------------------------------------------------------- the host's effects, relayed
    // The guest runs no combat (its CombatSystems are off and it only shows the host's state), so
    // nothing that fires on a hit - blood, sparks, the parry flash, the dodge burst, the sounds, the
    // camera shake, the kill shot - ever happened there. Each of those has one choke point (the same
    // ones the game record hooks), and on the host each choke point also sends an "E" line here.

    /// <summary>True on the host while a fight streams to a guest.</summary>
    public static bool MuteForward;   // set around effects the guest makes for itself (the kill shot's blood)

    public static bool Forwarding => !MuteForward && Instance != null && Instance.CurrentRole == Role.Host &&
                                     Instance.CurrentPhase == Phase.Playing && Instance.PeerConnected;

    static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => N(v.x) + " " + N(v.y) + " " + N(v.z);
    static string C(Color c) => N(c.r) + " " + N(c.g) + " " + N(c.b);

    public static void ForwardFx(ReplayRecorder.Fx kind, Vector3 point, Vector3 dir, float radius, float strength)
    {
        if (Forwarding) Instance.Send("E f " + (int)kind + " " + V(point) + " " + V(dir) + " " + N(radius) + " " + N(strength));
    }

    public static void ForwardBlood(Fighter target, Fighter attacker, Color color)
    {
        if (Forwarding && target != null && attacker != null)
            Instance.Send("E b " + target.PlayerNumber + " " + attacker.PlayerNumber + " " + C(color));
    }

    public static void ForwardSound(AudioClip clip, float volume, float pitch)
    {
        if (Forwarding && clip != null) Instance.Send("E s " + N(volume) + " " + N(pitch) + " " + clip.name);
    }

    public static void ForwardImpact(int damage, AttackType type, bool blocked)
    {
        if (Forwarding) Instance.Send("E i " + damage + " " + (int)type + " " + (blocked ? 1 : 0));
    }

    public static void ForwardClash()
    {
        if (Forwarding) Instance.Send("E c");
    }

    public static void ForwardKill(Fighter loser, Fighter attacker, Color color)
    {
        if (Forwarding && loser != null && attacker != null)
            Instance.Send("E k " + loser.PlayerNumber + " " + attacker.PlayerNumber + " " + C(color));
    }

    Fighter ByNumber(int number)
    {
        foreach (var f in fighters) if (f != null && f.PlayerNumber == number) return f;
        return null;
    }

    /// <summary>Guest: plays an effect line from the host.</summary>
    void PlayForwarded(string msg)
    {
        var p = msg.Split(' ');
        if (p.Length < 2) return;
        switch (p[1])
        {
            case "f" when p.Length >= 11:
            {
                var at = new Vector3(F(p[3]), F(p[4]), F(p[5]));
                var dir = new Vector3(F(p[6]), F(p[7]), F(p[8]));
                float radius = F(p[9]), strength = F(p[10]);
                switch ((ReplayRecorder.Fx)(int)F(p[2]))
                {
                    case ReplayRecorder.Fx.Sparks: HitEffects.SpawnSparks(at, dir, radius, strength); break;
                    case ReplayRecorder.Fx.Parry: HitEffects.SpawnParryFlash(at, dir, radius, strength); break;
                    case ReplayRecorder.Fx.Dust: HitEffects.SpawnDust(at, radius, strength); break;
                    case ReplayRecorder.Fx.Dodge: HitEffects.SpawnDodgeBurst(at, radius, strength); break;
                }
                break;
            }
            case "b" when p.Length >= 7:
                HitEffects.SpawnBlood(ByNumber((int)F(p[2])), ByNumber((int)F(p[3])), new Color(F(p[4]), F(p[5]), F(p[6])));
                break;
            case "s" when p.Length >= 5:
                // the clip name may hold spaces: everything after the volume and the pitch
                CrucibleAudio.PlayExact(CrucibleAudio.FindClip(string.Join(" ", p, 4, p.Length - 4)), F(p[2]), F(p[3]));
                break;
            case "i" when p.Length >= 5:
                if (FightCamera.Instance != null) FightCamera.Instance.OnImpact((int)F(p[2]), (AttackType)(int)F(p[3]), p[4] == "1");
                break;
            case "c":
                if (FightCamera.Instance != null) FightCamera.Instance.OnClash();
                break;
            case "k" when p.Length >= 7:
                KillShot.Begin(ByNumber((int)F(p[2])), ByNumber((int)F(p[3])), new Color(F(p[4]), F(p[5]), F(p[6])));
                break;
        }
    }

    void Bind()
    {
        fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
        Array.Sort(fighters, (a, b) => a.PlayerNumber.CompareTo(b.PlayerNumber));
        animators = new Animator[fighters.Length];
        for (int i = 0; i < fighters.Length; i++) { animators[i] = fighters[i].GetComponent<Animator>(); lastAnim[i] = 0; }
    }

    void Send(string msg) { if (ws != null && ws.Current == WebSocketClient.State.Open) ws.Send(msg); }

    // ---------------------------------------------------------------- per frame

    void Update()
    {
        if (ws == null) return;
        var st = ws.Current;
        if (CurrentPhase == Phase.Connecting)
        {
            if (locating != null) return;               // still reading the gist
            if (st == WebSocketClient.State.Open && !helloSent) { ws.Send("HELLO"); helloSent = true; }
            bool dead = st == WebSocketClient.State.Error || (st == WebSocketClient.State.Closed && helloSent);
            if (dead || Time.unscaledTime - connectStarted > connectTimeout)
            {
                Debug.Log("Online: no answer from that address");
                ws.Close();
                TryNextCandidate();
                return;
            }
        }
        else if (CurrentPhase != Phase.Idle && CurrentPhase != Phase.Failed &&
                 st != WebSocketClient.State.Open && st != WebSocketClient.State.Connecting)
        {
            // The server went away under us (laptop closed the window, tunnel dropped).
            bool hadPeer = CurrentPhase == Phase.Playing || PeerConnected;
            CurrentPhase = Phase.Failed;
            Status = "Lost the connection to the server.";
            PeerConnected = false;
            if (hadPeer) OnPeerLeft?.Invoke();
            return;
        }

        string msg;
        while (ws.Receive(out msg)) Handle(msg);

        if (CurrentPhase == Phase.Listing && Time.unscaledTime >= nextList)
        {
            nextList = Time.unscaledTime + 2f;
            Send("LIST");
        }

        // Heartbeat. A player whose network dies does not close the socket - the relay only notices
        // minutes later - so each side says "H" every second and gives up on a peer it has not heard
        // from for PeerTimeout seconds (covers the pause too, when no input or state is flowing).
        if (PeerConnected && peerVersion == null && peerMetAt >= 0f && Time.unscaledTime - peerMetAt > VersionWait)
        {
            VersionMismatch("");
            return;
        }
        if (PeerConnected)
        {
            if (Time.unscaledTime >= nextHeartbeat) { nextHeartbeat = Time.unscaledTime + 1f; Send("H"); }
            if (Time.unscaledTime - lastPeerHeard > PeerTimeout)
            {
                Debug.Log("Online: nothing from the other player for " + PeerTimeout + " s - treating them as gone");
                PeerConnected = false;
                PeerCharacter = -1;
                Status = "Lost contact with the other player";
                Send("LEAVE");
                CurrentRole = Role.None;
                CurrentPhase = Phase.Lobby;
                OnPeerLeft?.Invoke();
                return;
            }
        }

        if (CurrentPhase != Phase.Playing) return;
        if (CurrentRole == Role.Host)
        {
            // feed the guest's latest input to Player 2 (held fields persist, pressed flags fire once)
            if (InputManager.Instance != null)
            {
                InputManager.Instance.SetAiInput(2, guestInput);
                guestInput.lightAttack = guestInput.mediumAttack = guestInput.heavyAttack = false;
                guestInput.defenseButton = false;   // a press fires once; the modifier is a hold and persists
                guestInput.dashDirection = Vector2.zero;
            }
            if (Time.unscaledTime >= nextSend)
            {
                nextSend = Time.unscaledTime + 1f / Mathf.Max(sendRate, 1f);
                Send(BuildState());
            }
        }
        else
        {
            // send my input (I am Player 2, playing with the Player 1 controls)
            var im = InputManager.Instance;
            if (im != null && im.InputEnabled)
            {
                InputData d = im.GetPlayer1Input();
                Send(string.Format(CultureInfo.InvariantCulture, "I {0} {1} {2} {3} {4} {5:0.##} {6:0.##} {7} {8}",
                    d.moveDirection, d.blockHeld ? 1 : 0, d.lightAttack ? 1 : 0, d.mediumAttack ? 1 : 0, d.heavyAttack ? 1 : 0,
                    d.dashDirection.x, d.dashDirection.y,
                    d.specialModifier ? 1 : 0, d.defenseButton ? 1 : 0));
            }
            // smooth toward the host's positions
            for (int i = 0; i < fighters.Length; i++)
            {
                var t = fighters[i].transform;
                t.position = Vector3.Lerp(t.position, targetPos[i], 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                float yaw = Mathf.LerpAngle(t.eulerAngles.y, targetYaw[i], 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                t.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }
    }

    string BuildState()
    {
        var sb = new System.Text.StringBuilder("S ");
        sb.Append(Time.timeScale.ToString("0.##", CultureInfo.InvariantCulture));
        for (int i = 0; i < fighters.Length; i++)
        {
            var f = fighters[i]; var a = animators[i];
            var p = f.transform.position;
            var info = a != null ? a.GetCurrentAnimatorStateInfo(0) : default;
            sb.Append(" | ");
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} {3:0.#} {4} {5} {6} {7:0.###} {8:0.##} {9}",
                p.x, p.y, p.z, f.transform.eulerAngles.y,
                f.Stats != null ? f.Stats.currentHealth : 0, f.Stats != null ? f.Stats.currentStamina : 0,
                info.fullPathHash, info.normalizedTime % 1f, a != null ? a.GetFloat("MoveSpeed") : 0f, f.IsBlocking ? 1 : 0));
            // the HUD's flashes and status line (PARRY!, CLASH, the heal float, COUNTER READY)
            sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:0.##} {1:0.##} {2:0.##} {3} {4:0.##} {5:0.##}",
                f.ParryFlashTimeLeft, f.ClashFlashTimeLeft, f.HealFlashTimeLeft, f.HealFlashAmount,
                f.ParryCounterTimeLeft, f.DashCounterTimeLeft));
        }
        return sb.ToString();
    }

    void Handle(string msg)
    {
        if (msg.StartsWith("WELCOME")) { candidates.Clear(); CurrentPhase = Phase.Lobby; Status = "Connected"; return; }
        if (msg.StartsWith("HOSTED "))
        {
            var p = msg.Substring(7).Split('\t');
            if (p.Length > 1) RoomName = p[1];
            CurrentPhase = Phase.Hosting;
            Status = "Waiting for a player...";
            return;
        }
        if (msg == "PEER_JOINED") { PeerConnected = true; PeerLocked = false; PeerVote = 0; lastPeerHeard = Time.unscaledTime; Status = "Player joined"; SendCharacterEcho(); MeetPeer(); OnPeerJoined?.Invoke(); return; }
        if (msg == "PEER_LEFT")
        {
            PeerConnected = false;
            PeerCharacter = -1;
            PeerLocked = false;     // a new player must not inherit the last one's lock or vote
            PeerVote = 0;
            Status = "The other player left";
            if (CurrentRole == Role.Guest) { CurrentRole = Role.None; CurrentPhase = Phase.Lobby; }
            else if (CurrentRole == Role.Host) CurrentPhase = Phase.Hosting;   // the room stays open for the next player
            OnPeerLeft?.Invoke();
            return;
        }
        if (msg.StartsWith("ROOMS"))
        {
            Rooms.Clear();
            foreach (var line in msg.Split('\n'))
            {
                var p = line.Split('\t');
                if (p.Length < 3) continue;
                Rooms.Add(new RoomInfo { id = p[0], locked = p[1] == "1", name = p[2] });
            }
            return;
        }
        if (msg == "JOINED")
        {
            CurrentPhase = Phase.Joined;
            PeerConnected = true;
            PeerLocked = false;
            PeerVote = 0;
            lastPeerHeard = Time.unscaledTime;
            Status = "Joined. Waiting for the host to start...";
            SendCharacterEcho();
            MeetPeer();
            OnJoined?.Invoke();
            return;
        }
        if (msg == "NOROOM" || msg == "FULL" || msg == "BADCODE")
        {
            LastJoinError = msg == "FULL" ? JoinError.Full : msg == "BADCODE" ? JoinError.BadCode : JoinError.NoRoom;
            Status = msg == "FULL" ? "That game is full" : msg == "BADCODE" ? "Wrong code" : "That game is gone";
            CurrentRole = Role.Guest;
            CurrentPhase = Phase.Listing;
            return;
        }
        // Everything below comes from the OTHER PLAYER (the relay's own replies are handled above).
        lastPeerHeard = Time.unscaledTime;
        if (msg == "H") return;
        if (msg.StartsWith("W "))
        {
            peerVersion = msg.Substring(2).Trim();
            if (peerVersion != Application.version) VersionMismatch(peerVersion);
            return;
        }
        if (msg.StartsWith("C ")) { if (int.TryParse(msg.Substring(2).Trim(), out int c)) PeerCharacter = c; return; }
        if (msg.StartsWith("X "))
        {
            var x = msg.Split(' ');
            if (x.Length > 1 && int.TryParse(x[1], out int c)) PeerCharacter = c;
            PeerLocked = x.Length > 2 && x[2] == "1";
            return;
        }
        if (msg.StartsWith("V ")) { PeerVote = (int)F(msg.Substring(2).Trim()); return; }
        if (msg.StartsWith("O "))
        {
            var o = msg.Split(' ');
            if (o.Length > 2) { RulesSpeed = F(o[1]); RulesRegen = o[2] == "1"; }
            return;
        }
        if (msg.StartsWith("ERR")) { Status = msg; return; }

        if (CurrentRole == Role.Host)
        {
            if (msg.StartsWith("I "))
            {
                var p = msg.Split(' ');
                if (p.Length >= 8)
                {
                    guestInput.moveDirection = F(p[1]);
                    guestInput.blockHeld = p[2] == "1";
                    guestInput.lightAttack |= p[3] == "1";
                    guestInput.mediumAttack |= p[4] == "1";
                    guestInput.heavyAttack |= p[5] == "1";
                    var dash = new Vector2(F(p[6]), F(p[7]));
                    if (dash != Vector2.zero) guestInput.dashDirection = dash;
                    // Added after the first test build, so an older guest simply sends a shorter line:
                    // the modifier is a hold (assign), the parry is a press (latch until consumed).
                    if (p.Length >= 9) guestInput.specialModifier = p[8] == "1";
                    if (p.Length >= 10) guestInput.defenseButton |= p[9] == "1";
                    guestInputFresh = true;
                }
            }
            else if (msg.StartsWith("P ")) OnPauseReceived?.Invoke(msg.EndsWith("1"));
        }
        else
        {
            if (msg.StartsWith("S "))
            {
                if (CurrentPhase != Phase.Playing) return;
                var parts = msg.Substring(2).Split('|');
                // the host's clock: its game speed, or 0 while paused - the guest's animators follow it
                float ts = F(parts[0].Trim());
                if (ts >= 0f && ts <= 4f) Time.timeScale = ts;
                for (int i = 1; i < parts.Length && i - 1 < fighters.Length; i++)
                {
                    var p = parts[i].Trim().Split(' ');
                    if (p.Length < 10) continue;
                    int k = i - 1;
                    targetPos[k] = new Vector3(F(p[0]), F(p[1]), F(p[2]));
                    targetYaw[k] = F(p[3]);
                    var f = fighters[k];
                    if (f.Stats != null) { f.Stats.currentHealth = (int)F(p[4]); f.Stats.currentStamina = (int)F(p[5]); }
                    int anim = int.Parse(p[6]);
                    float time = F(p[7]);
                    var a = animators[k];
                    if (a != null && a.runtimeAnimatorController != null)
                    {
                        if (anim != lastAnim[k] && anim != 0)
                        {
                            if (a.HasState(0, anim)) a.Play(anim, 0, time);
                            lastAnim[k] = anim;
                        }
                        a.SetFloat("MoveSpeed", F(p[8]));
                    }
                    if (p.Length >= 16)
                        f.ApplyNetHud(F(p[10]), F(p[11]), F(p[12]), (int)F(p[13]), F(p[14]), F(p[15]), p[9] == "1");
                }
            }
            else if (msg == "R" || msg.StartsWith("R "))
            {
                var p = msg.Split(' ');
                StartP1 = p.Length > 1 && int.TryParse(p[1], out int a1) ? a1 : -1;
                StartP2 = p.Length > 2 && int.TryParse(p[2], out int a2) ? a2 : -1;
                StartArena = p.Length > 3 && int.TryParse(p[3], out int a3) ? a3 : -1;
                if (p.Length > 5) { RulesSpeed = F(p[4]); RulesRegen = p[5] == "1"; }
                OnStartReceived?.Invoke();
            }
            else if (msg.StartsWith("E ")) { if (CurrentPhase == Phase.Playing) PlayForwarded(msg); }
            else if (msg.StartsWith("A ")) OnArenaReceived?.Invoke((int)F(msg.Substring(2).Trim()));
            else if (msg == "F") OnVersusReceived?.Invoke();
            else if (msg == "G") { EnterSelectPhase(); OnSelectReceived?.Invoke(); }
            else if (msg.StartsWith("P "))
            {
                var p = msg.Split(' ');
                if (p.Length >= 5) OnPauseState?.Invoke(p[1] == "1", (int)F(p[2]), (int)F(p[3]), (int)F(p[4]));
                else OnPauseState?.Invoke(p.Length > 1 && p[1] == "1", 1, 0, 0);
            }
        }
    }

    static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
}
