// CRUCIBLE online server launcher - run this on the laptop that hosts online play.
//   node Server/online.js            (or double-click Server/start-online.cmd)
// 1. starts the relay (relay.js) on :8080
// 2. starts a Cloudflare quick tunnel to it and reads the public https://<words>.trycloudflare.com address
// 3. publishes that address to a PUBLIC GitHub gist named crucible-server.json (created on the first run,
//    through the gh CLI you are logged into). The game finds the gist by the GitHub user name and reads it,
//    so nobody ever types a link. The address changes on every start; the gist always holds the live one.
// Close this window (or Ctrl+C) to stop: the gist is marked offline on the way out.
const { spawn, execFileSync } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const PORT = 8080;
const GIST_FILE = "crucible-server.json";
const CLOUDFLARED = ["C:/Program Files (x86)/cloudflared/cloudflared.exe", "C:/Program Files/cloudflared/cloudflared.exe", "cloudflared"]
  .find(p => p === "cloudflared" || fs.existsSync(p));

const log = (...a) => console.log(new Date().toLocaleTimeString(), ...a);

function gh(args, input) {
  return execFileSync("gh", args, { input, encoding: "utf8", stdio: ["pipe", "pipe", "pipe"] }).trim();
}

let gistId = null;
function findOrCreateGist() {
  const list = JSON.parse(gh(["api", "/gists?per_page=100"]));
  const hit = list.find(g => g.files && g.files[GIST_FILE]);
  if (hit) return hit.id;
  log("first run: creating the public gist that tells the game where this server is");
  const body = JSON.stringify({ public: true, description: "CRUCIBLE: Honorable Souls - online server address (Server/online.js)",
    files: { [GIST_FILE]: { content: JSON.stringify({ url: "", online: false }) } } });
  return JSON.parse(gh(["api", "--method", "POST", "/gists", "--input", "-"], body)).id;
}

function publish(url, online) {
  try {
    if (!gistId) gistId = findOrCreateGist();
    const content = JSON.stringify({ url, online, updated: new Date().toISOString() });
    gh(["api", "--method", "PATCH", `/gists/${gistId}`, "--input", "-"], JSON.stringify({ files: { [GIST_FILE]: { content } } }));
    if (online) {
      log(`published ${url}`);
      console.log("");
      console.log("  ==========================================================");
      console.log("   SERVER IS ONLINE - players can press Online in the game.");
      console.log("   To STOP it: type  stop  and press Enter (or Ctrl+C),");
      console.log("   or just close this window.");
      console.log("  ==========================================================");
      console.log("");
    } else log("gist marked offline - server stopped");
  } catch (e) {
    log("could not update the gist (is `gh auth status` logged in?):", (e.stderr || e.message || "").toString().trim());
  }
}

// ---- relay
const relay = spawn(process.execPath, [path.join(__dirname, "relay.js"), "--port", String(PORT)], { stdio: ["ignore", "pipe", "pipe"] });
relay.stdout.on("data", d => process.stdout.write("[relay] " + d));
relay.stderr.on("data", d => process.stdout.write("[relay] " + d));
relay.on("exit", c => { log("relay stopped", c); shutdown(); });

// ---- tunnel
let published = null;
const tunnel = spawn(CLOUDFLARED, ["tunnel", "--url", `http://localhost:${PORT}`, "--no-autoupdate"], { stdio: ["ignore", "pipe", "pipe"] });
const cfLog = fs.createWriteStream(path.join(__dirname, "cf.log"));
function onTunnel(d) {
  const s = d.toString();
  cfLog.write(s);
  const m = s.match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/);
  if (m && m[0] !== published) {
    published = m[0];
    log("tunnel up:", published);
    // the address resolves a few seconds after cloudflared prints it
    setTimeout(() => publish(published.replace("https://", "wss://") + "/ws", true), 4000);
  }
  if (/Unauthorized: Tunnel not found|failed to .*tunnel/i.test(s)) log("tunnel trouble - if players cannot connect, close this window and start it again");
}
tunnel.stdout.on("data", onTunnel);
tunnel.stderr.on("data", onTunnel);
tunnel.on("exit", c => { log("tunnel stopped", c); shutdown(); });

let down = false;
function shutdown() {
  if (down) return;
  down = true;
  if (published) publish("", false);
  try { relay.kill(); } catch {}
  try { tunnel.kill(); } catch {}
  setTimeout(() => process.exit(0), 300);
}
process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
process.on("SIGHUP", shutdown);   // closing the console window
// Typing "stop" (or q) and Enter in the window shuts the server down cleanly, like Ctrl+C.
process.stdin.setEncoding("utf8");
process.stdin.on("data", d => { if (/^\s*(stop|q|quit|exit)\s*$/i.test(d)) shutdown(); });

log(`CRUCIBLE online server on ${os.hostname()} - starting relay and tunnel...`);
