// Minimal WebSocket bridge for the WebGL build. C# polls WsPoll() for queued messages.
var WebSocketBridge = {
  $WsState: { socket: null, queue: [], status: 0 },   // status: 0 closed, 1 connecting, 2 open, 3 error

  WsConnect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    try {
      if (WsState.socket) { try { WsState.socket.close(); } catch (e) {} }
      WsState.queue = [];
      WsState.status = 1;
      var ws = new WebSocket(url);
      WsState.socket = ws;
      ws.onopen = function () { WsState.status = 2; };
      ws.onmessage = function (ev) { WsState.queue.push(typeof ev.data === "string" ? ev.data : ""); };
      ws.onerror = function () { WsState.status = 3; };
      ws.onclose = function () { if (WsState.status !== 3) WsState.status = 0; WsState.socket = null; };
    } catch (e) {
      WsState.status = 3;
    }
  },

  WsSend: function (msgPtr) {
    if (WsState.socket && WsState.socket.readyState === 1) WsState.socket.send(UTF8ToString(msgPtr));
  },

  WsStatus: function () { return WsState.status; },

  WsPoll: function () {
    if (WsState.queue.length === 0) return 0;
    var s = WsState.queue.shift();
    var size = lengthBytesUTF8(s) + 1;
    var ptr = _malloc(size);
    stringToUTF8(s, ptr, size);
    return ptr;
  },

  WsFree: function (ptr) { _free(ptr); },

  WsClose: function () {
    if (WsState.socket) { try { WsState.socket.close(); } catch (e) {} }
    WsState.socket = null; WsState.status = 0;
  },

  WsPageUrl: function () {
    var s = (typeof window !== "undefined" && window.location) ? window.location.href : "";
    var size = lengthBytesUTF8(s) + 1;
    var ptr = _malloc(size);
    stringToUTF8(s, ptr, size);
    return ptr;
  }
};
autoAddDeps(WebSocketBridge, "$WsState");
mergeInto(LibraryManager.library, WebSocketBridge);
