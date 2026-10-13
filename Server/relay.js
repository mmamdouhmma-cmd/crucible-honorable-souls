// CRUCIBLE test relay + static host.
//   node Server/relay.js [--port 8080] [--dir Builds/WebGL]
// Serves the WebGL build over HTTP and relays WebSocket messages between the two players of a room
// (browsers cannot open LAN sockets to each other, so both phones talk through this PC).
// Protocol (text frames):
//   client -> server:  "HOST <name>"   create a room, you are the host
//                      "LIST"          list open rooms  -> "ROOMS name1,name2"
//                      "JOIN <name>"   join a room as the guest -> "JOINED" (or "FULL"/"NOROOM")
//                      anything else   relayed verbatim to the other player of your room
//   server -> client:  "HOSTED <name>", "PEER_JOINED", "PEER_LEFT", "ROOMS ...", "JOINED", "ERR ..."
const http = require("http");
const fs = require("fs");
const path = require("path");
const os = require("os");
const { WebSocketServer } = require("ws");

const args = process.argv.slice(2);
const arg = (k, d) => { const i = args.indexOf(k); return i >= 0 && args[i + 1] ? args[i + 1] : d; };
const PORT = parseInt(arg("--port", "8080"), 10);
const DIR = path.resolve(arg("--dir", path.join(__dirname, "..", "Builds", "WebGL")));

const MIME = {
  ".html": "text/html; charset=utf-8", ".js": "application/javascript", ".css": "text/css", ".json": "application/json",
  ".wasm": "application/wasm", ".data": "application/octet-stream", ".png": "image/png", ".jpg": "image/jpeg",
  ".ico": "image/x-icon", ".svg": "image/svg+xml", ".txt": "text/plain", ".symbols.json": "application/json",
};

const server = http.createServer((req, res) => {
  let url = decodeURIComponent(req.url.split("?")[0]);
  if (url === "/") url = "/index.html";
  const file = path.join(DIR, url);
  if (!file.startsWith(DIR)) { res.writeHead(403); return res.end(); }
  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) { res.writeHead(404); return res.end("not found: " + url); }
    const ext = path.extname(file).toLowerCase();
    const headers = { "Content-Type": MIME[ext] || "application/octet-stream", "Content-Length": st.size, "Cache-Control": "no-cache" };
    if (file.endsWith(".gz")) headers["Content-Encoding"] = "gzip";
    if (file.endsWith(".br")) headers["Content-Encoding"] = "br";
    res.writeHead(200, headers);
    fs.createReadStream(file).pipe(res);
  });
});

const rooms = new Map(); // name -> { host, guest }
const wss = new WebSocketServer({ server, path: "/ws" });

function send(ws, msg) { if (ws && ws.readyState === 1) ws.send(msg); }
function roomList() { return "ROOMS " + [...rooms.entries()].filter(([, r]) => r.host && !r.guest).map(([n]) => n).join(","); }

wss.on("connection", (ws, req) => {
  ws.room = null; ws.role = null;
  console.log("connect", req.socket.remoteAddress);
  ws.on("message", (data) => {
    const msg = data.toString();
    if (msg.startsWith("HOST ")) {
      const name = msg.slice(5).trim().slice(0, 24) || "room";
      let n = name, k = 2;
      while (rooms.has(n)) n = name + " " + k++;
      rooms.set(n, { host: ws, guest: null });
      ws.room = n; ws.role = "host";
      send(ws, "HOSTED " + n);
      console.log("room", n, "hosted");
      return;
    }
    if (msg === "LIST") { send(ws, roomList()); return; }
    if (msg.startsWith("JOIN ")) {
      const n = msg.slice(5).trim();
      const r = rooms.get(n);
      if (!r) return send(ws, "NOROOM");
      if (r.guest) return send(ws, "FULL");
      r.guest = ws; ws.room = n; ws.role = "guest";
      send(ws, "JOINED");
      send(r.host, "PEER_JOINED");
      console.log("room", n, "joined");
      return;
    }
    const r = ws.room ? rooms.get(ws.room) : null;
    if (!r) return;
    send(ws.role === "host" ? r.guest : r.host, msg);
  });
  ws.on("close", () => {
    const r = ws.room ? rooms.get(ws.room) : null;
    if (!r) return;
    if (ws.role === "host") { send(r.guest, "PEER_LEFT"); rooms.delete(ws.room); }
    else { r.guest = null; send(r.host, "PEER_LEFT"); }
    console.log("room", ws.room, ws.role, "left");
  });
});

server.listen(PORT, "0.0.0.0", () => {
  const ips = Object.values(os.networkInterfaces()).flat().filter(i => i.family === "IPv4" && !i.internal).map(i => i.address);
  console.log(`CRUCIBLE relay: serving ${DIR}`);
  for (const ip of ips) console.log(`  open on a phone (same WiFi):  http://${ip}:${PORT}/`);
});
