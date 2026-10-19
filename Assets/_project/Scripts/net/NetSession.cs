using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Same-WiFi play through the relay in Server/relay.js. The HOST runs the whole fight; the GUEST sends its
/// input every frame and shows the state the host sends (positions, facing, health, stamina, animator state).
/// The guest's human plays Player 2 with the Player 1 controls (keyboard P1 layout, pad, or the full-screen
/// touch pad). Messages are short text frames:
///   guest -> host   I move block L M H dashX dashY        (one per frame; pressed flags fire once)
///   host  -> guest  S t | x y z yaw hp st anim time move blocking | ... (one per fighter, ~20 Hz)
///                   R  (match (re)started)   P 1|0 (pause)   K (KO shown by the guest's own menu from hp)
///   guest -> host   P 1|0 (pause request)    R (rematch request)
/// </summary>
public class NetSession : MonoBehaviour
{
    public static NetSession Instance { get; private set; }

    public enum Role { None, Host, Guest }
    public enum Phase { Idle, Connecting, Hosting, Listing, Joined, Playing, Failed }

    [Tooltip("Relay address used outside the browser (the Editor hosting a test). In WebGL the page's own host is used.")]
    public string editorRelayUrl = "ws://localhost:8080/ws";
    [Tooltip("State snapshots per second sent by the host.")]
    public float sendRate = 20f;

    public Role CurrentRole { get; private set; } = Role.None;
    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    public string RoomName { get; private set; } = "";
    public string Status { get; private set; } = "";
    public List<string> Rooms { get; } = new List<string>();
    public bool PeerConnected { get; private set; }
    public event Action OnPeerJoined, OnStartReceived, OnPeerLeft;
    public event Action<bool> OnPauseReceived;
    public event Action OnRematchRequested;

    WebSocketClient ws;
    float nextSend, nextList;
    Fighter[] fighters = new Fighter[0];
    Animator[] animators = new Animator[0];
    readonly Vector3[] targetPos = new Vector3[2];
    readonly float[] targetYaw = new float[2];
    readonly int[] lastAnim = new int[2];
    InputData guestInput;                       // host: the guest's input snapshot for this frame
    bool guestInputFresh;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { ws?.Close(); }

    string RelayUrl()
    {
        string page = WebSocketClient.PageUrl();
        if (!string.IsNullOrEmpty(page) && Uri.TryCreate(page, UriKind.Absolute, out var u))
        {
            string scheme = u.Scheme == "https" ? "wss" : "ws";
            return $"{scheme}://{u.Host}:{(u.IsDefaultPort ? (u.Scheme == "https" ? 443 : 80) : u.Port)}/ws";
        }
        return editorRelayUrl;
    }

    void Connect()
    {
        ws = ws ?? new WebSocketClient();
        ws.Connect(RelayUrl());
        CurrentPhase = Phase.Connecting;
        Status = "Connecting to " + RelayUrl();
    }

    public void Host(string name)
    {
        Leave();
        Connect();
        CurrentRole = Role.Host;
        RoomName = name;
        pendingAfterOpen = "HOST " + name;
    }

    public void BrowseRooms()
    {
        Leave();
        Connect();
        CurrentRole = Role.Guest;
        pendingAfterOpen = "LIST";
        CurrentPhase = Phase.Listing;
    }

    public void Join(string room)
    {
        if (ws == null || ws.Current != WebSocketClient.State.Open) { Status = "Not connected"; return; }
        CurrentRole = Role.Guest;
        RoomName = room;
        ws.Send("JOIN " + room);
        Status = "Joining " + room;
    }

    public void Leave()
    {
        ws?.Close();
        CurrentRole = Role.None;
        CurrentPhase = Phase.Idle;
        PeerConnected = false;
        Rooms.Clear();
        Status = "";
        pendingAfterOpen = null;
        if (InputManager.Instance != null) InputManager.Instance.SetAi(2, false);
    }

    string pendingAfterOpen;

    // ---------------------------------------------------------------- match hooks (called by GameMenu)

    /// <summary>Host: the fight begins. Guest input drives Player 2 through the InputManager's override slot.</summary>
    public void HostStartMatch()
    {
        Bind();
        if (InputManager.Instance != null) InputManager.Instance.SetAi(2, true);
        Send("R");
        CurrentPhase = Phase.Playing;
    }

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
    }

    public void GuestLeaveMatch()
    {
        foreach (var f in fighters)
        {
            var cs = f.GetComponent<CombatSystem>(); if (cs) cs.enabled = true;
        }
    }

    public void SendPause(bool on) => Send("P " + (on ? 1 : 0));
    public void SendRematchRequest() => Send("R");

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
        if (st == WebSocketClient.State.Open && pendingAfterOpen != null)
        {
            ws.Send(pendingAfterOpen);
            pendingAfterOpen = null;
            Status = CurrentRole == Role.Host ? "Waiting for a player..." : "Looking for games...";
            if (CurrentRole == Role.Host) CurrentPhase = Phase.Hosting;
        }
        if (st == WebSocketClient.State.Error) { CurrentPhase = Phase.Failed; Status = "Could not reach the relay. Is Server/relay.js running on the PC?"; }

        string msg;
        while (ws.Receive(out msg)) Handle(msg);

        if (CurrentRole == Role.Guest && CurrentPhase == Phase.Listing && Time.unscaledTime >= nextList)
        {
            nextList = Time.unscaledTime + 2f;
            Send("LIST");
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
        }
        return sb.ToString();
    }

    void Handle(string msg)
    {
        if (msg.StartsWith("HOSTED ")) { RoomName = msg.Substring(7); CurrentPhase = Phase.Hosting; Status = $"Room \"{RoomName}\" is open. Waiting for a player..."; return; }
        if (msg == "PEER_JOINED") { PeerConnected = true; Status = "Player joined"; OnPeerJoined?.Invoke(); return; }
        if (msg == "PEER_LEFT") { PeerConnected = false; Status = "The other player left"; OnPeerLeft?.Invoke(); return; }
        if (msg.StartsWith("ROOMS")) { Rooms.Clear(); foreach (var r in msg.Substring(5).Trim().Split(',')) if (r.Length > 0) Rooms.Add(r); return; }
        if (msg == "JOINED") { CurrentPhase = Phase.Joined; PeerConnected = true; Status = "Joined. Waiting for the host to start..."; return; }
        if (msg == "NOROOM" || msg == "FULL") { Status = msg == "FULL" ? "That game is full" : "That game is gone"; CurrentPhase = Phase.Listing; return; }
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
            else if (msg == "R") OnRematchRequested?.Invoke();
        }
        else
        {
            if (msg.StartsWith("S "))
            {
                if (CurrentPhase != Phase.Playing) return;
                var parts = msg.Substring(2).Split('|');
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
                }
            }
            else if (msg == "R") OnStartReceived?.Invoke();
            else if (msg.StartsWith("P ")) OnPauseReceived?.Invoke(msg.EndsWith("1"));
        }
    }

    static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

    /// <summary>The PC address players type into their phone browser, for the host screen.</summary>
    public static string LanHint()
    {
        string page = WebSocketClient.PageUrl();
        if (!string.IsNullOrEmpty(page)) return page;
        try
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !ip.ToString().StartsWith("127.")) return "http://" + ip + ":8080/";
        }
        catch { }
        return "http://<pc-ip>:8080/";
    }
}
