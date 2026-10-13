using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Tiny text WebSocket client: the browser's WebSocket through a .jslib in WebGL, System.Net.WebSockets
/// everywhere else (Editor hosting, standalone). Poll <see cref="Receive"/> every frame.
/// </summary>
public class WebSocketClient
{
    public enum State { Closed, Connecting, Open, Error }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void WsConnect(string url);
    [DllImport("__Internal")] static extern void WsSend(string msg);
    [DllImport("__Internal")] static extern int WsStatus();
    [DllImport("__Internal")] static extern IntPtr WsPoll();
    [DllImport("__Internal")] static extern void WsFree(IntPtr p);
    [DllImport("__Internal")] static extern void WsClose();
    [DllImport("__Internal")] static extern IntPtr WsPageUrl();

    public State Current => (State)WsStatus();
    public void Connect(string url) { WsConnect(url); }
    public void Send(string msg) { WsSend(msg); }
    public void Close() { WsClose(); }
    public bool Receive(out string msg)
    {
        IntPtr p = WsPoll();
        if (p == IntPtr.Zero) { msg = null; return false; }
        msg = Marshal.PtrToStringUTF8(p);
        WsFree(p);
        return true;
    }
    public static string PageUrl()
    {
        IntPtr p = WsPageUrl();
        string s = Marshal.PtrToStringUTF8(p);
        WsFree(p);
        return s;
    }
#else
    System.Net.WebSockets.ClientWebSocket socket;
    readonly Queue<string> queue = new Queue<string>();
    readonly object gate = new object();
    volatile State state = State.Closed;
    CancellationTokenSource cts;

    public State Current => state;

    public void Connect(string url)
    {
        Close();
        state = State.Connecting;
        cts = new CancellationTokenSource();
        socket = new System.Net.WebSockets.ClientWebSocket();
        var tok = cts.Token;
        var sock = socket;
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await sock.ConnectAsync(new Uri(url), tok);
                state = State.Open;
                var buf = new byte[64 * 1024];
                while (!tok.IsCancellationRequested && sock.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var sb = new StringBuilder();
                    System.Net.WebSockets.WebSocketReceiveResult r;
                    do
                    {
                        r = await sock.ReceiveAsync(new ArraySegment<byte>(buf), tok);
                        if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) { state = State.Closed; return; }
                        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    } while (!r.EndOfMessage);
                    lock (gate) queue.Enqueue(sb.ToString());
                }
                state = State.Closed;
            }
            catch (Exception e)
            {
                if (!tok.IsCancellationRequested) { Debug.LogWarning("WebSocket: " + e.Message); state = State.Error; }
            }
        });
    }

    public void Send(string msg)
    {
        if (socket == null || socket.State != System.Net.WebSockets.WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(msg);
        try { socket.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (Exception e) { Debug.LogWarning("WebSocket send: " + e.Message); }
    }

    public bool Receive(out string msg)
    {
        lock (gate)
        {
            if (queue.Count == 0) { msg = null; return false; }
            msg = queue.Dequeue();
            return true;
        }
    }

    public void Close()
    {
        try { cts?.Cancel(); socket?.Dispose(); } catch { }
        socket = null; state = State.Closed;
    }

    public static string PageUrl() => "";
#endif
}
