// CRUCIBLE test relay + static host.
//   node Server/relay.js [--port 8080] [--dir Builds/WebGL]
// Serves the WebGL build over HTTP and relays WebSocket messages between the two players of a room
// (players cannot open sockets to each other, so both talk through this PC). Fields are TAB-separated.
// Protocol (text frames; <TAB> = a tab character, <NL> = a newline):
//   client -> server:  HELLO                    -> WELCOME 2 (proves this is the relay, not an error page)
//                      HOST <code><TAB><name>   create a room; code = "" or 6 digits -> HOSTED <id><TAB><name>
//                      HOST <name>              legacy (no tab): open room, no code
//                      LIST                     -> ROOMS, then per open room <NL><id><TAB><locked 0|1><TAB><name>
//                      JOIN <id><TAB><code>     -> JOINED | NOROOM | FULL | BADCODE
//                      LEAVE                    leave your room (back to the lobby, socket stays open)
//                      anything else            relayed verbatim to the other player of your room
//   server -> client:  PEER_JOINED, PEER_LEFT, ERR ...
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

const rooms = new Map(); // id -> { id, name, code, host, guest }
let nextId = 1;
const wss = new WebSocketServer({ server, path: "/ws" });

function send(ws, msg) { if (ws && ws.readyState === 1) ws.send(msg); }
const clean = (t, n) => String(t || "").replace(/[\t\r\n]/g, " ").trim().slice(0, n);
function roomList() {
  let out = "ROOMS";
  for (const r of rooms.values()) if (r.host && !r.guest) out += `\n${r.id}\t${r.code ? 1 : 0}\t${r.name}`;
  return out;
}

function leaveRoom(ws) {
  const r = ws.room ? rooms.get(ws.room) : null;
  if (r) {
    if (ws.role === "host") { send(r.guest, "PEER_LEFT"); if (r.guest) { r.guest.room = null; r.guest.role = null; } rooms.delete(ws.room); }
    else if (r.guest === ws) { r.guest = null; send(r.host, "PEER_LEFT"); }
    console.log("room", ws.room, ws.role, "left");
  }
  ws.room = null; ws.role = null;
}

wss.on("connection", (ws, req) => {
  ws.room = null; ws.role = null;
  console.log("connect", req.headers["cf-connecting-ip"] || req.socket.remoteAddress);
  ws.on("message", (data) => {
    const msg = data.toString();
    if (msg === "HELLO") { send(ws, "WELCOME 2"); return; }
    if (msg.startsWith("HOST ")) {
      const body = msg.slice(5);
      const tab = body.indexOf("\t");
      let code = tab >= 0 ? body.slice(0, tab).trim() : "";
      const name = clean(tab >= 0 ? body.slice(tab + 1) : body, 24) || "Crucible duel";
      if (code && !/^\d{6}$/.test(code)) return send(ws, "ERR the code must be 6 digits");
      const id = String(nextId++);
      rooms.set(id, { id, name, code, host: ws, guest: null });
      ws.room = id; ws.role = "host";
      send(ws, `HOSTED ${id}\t${name}`);
      console.log(`room ${id} "${name}" hosted${code ? " (code)" : ""}`);
      return;
    }
    if (msg === "LIST") { send(ws, roomList()); return; }
    if (msg === "LEAVE") { leaveRoom(ws); return; }
    if (msg.startsWith("JOIN ")) {
      const [id, code = ""] = msg.slice(5).split("\t");
      const r = rooms.get(id.trim());
      if (!r) return send(ws, "NOROOM");
      if (r.guest) return send(ws, "FULL");
      if (r.code && code.trim() !== r.code) { console.log(`room ${r.id} wrong code`); return send(ws, "BADCODE"); }
      r.guest = ws; ws.room = r.id; ws.role = "guest";
      send(ws, "JOINED");
      send(r.host, "PEER_JOINED");
      console.log(`room ${r.id} joined`);
      return;
    }
    const r = ws.room ? rooms.get(ws.room) : null;
    if (!r) return;
    send(ws.role === "host" ? r.guest : r.host, msg);
  });
  ws.on("close", () => leaveRoom(ws));
});

server.listen(PORT, "0.0.0.0", () => {
  const ips = Object.values(os.networkInterfaces()).flat().filter(i => i.family === "IPv4" && !i.internal).map(i => i.address);
  console.log(`CRUCIBLE relay: serving ${DIR}`);
  for (const ip of ips) console.log(`  open on a phone (same WiFi):  http://${ip}:${PORT}/`);
});
