// Publishes the Windows build, and the Android APK when it is the same version, as a GitHub release
// that friends download and the game updates from.
//   node Tools/release.js ["what changed"]        (or double-click Tools/release.cmd)
// 1. reads the version from ProjectSettings (Player Settings > Version) - the release tag is v<version>
// 2. zips Builds/CrucibleLite-Win64 (without the Burst debug folder) to Builds/Crucible-v<version>-Win64.zip
// 3. creates the release on the public repo and uploads the zip and APK (gh CLI, your login)
// The game compares that tag with its own version on start, so: bump the version, build
// (Crucible > Build > Windows app), THEN run this. Re-running for a version already released is refused.
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const buildDir = path.join(root, "Builds", "CrucibleLite-Win64");
const settings = fs.readFileSync(path.join(root, "ProjectSettings", "ProjectSettings.asset"), "utf8");
const version = (settings.match(/^\s*bundleVersion:\s*(\S+)/m) || [])[1];
if (!version) throw new Error("no bundleVersion in ProjectSettings.asset");
const tag = "v" + version;
const notes = process.argv[2] || `CRUCIBLE: Honorable Souls ${tag}`;
const run = (cmd, args, opts = {}) => execFileSync(cmd, args, { cwd: root, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"], ...opts }).trim();

if (!fs.existsSync(path.join(buildDir, "Crucible.exe"))) throw new Error("no build at " + buildDir + " - run Crucible > Build > Windows app first");
const result = fs.existsSync(path.join(buildDir, "build_result.txt")) ? fs.readFileSync(path.join(buildDir, "build_result.txt"), "utf8") : "";
if (result && !result.includes(tag)) console.warn(`WARNING: build_result.txt says "${result.trim()}" - is the build at ${tag}?`);

try { run("gh", ["release", "view", tag]); console.error(`${tag} is already released. Bump Player Settings > Version, rebuild, and run this again.`); process.exit(1); } catch {}

const zip = path.join(root, "Builds", `Crucible-${tag}-Win64.zip`);
if (fs.existsSync(zip)) fs.unlinkSync(zip);
console.log(`zipping ${buildDir} ...`);
// .NET's zip writer via PowerShell (make_zip.ps1): Explorer's "Extract all" rejects the "./"-prefixed
// entries Windows' bsdtar writes, so that zip downloaded fine and would not open.
run("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path.join(__dirname, "make_zip.ps1"), buildDir, zip, "Crucible"]);
console.log(`${(fs.statSync(zip).size / 1048576).toFixed(0)} MB -> ${zip}`);

console.log(`creating release ${tag} and uploading (this takes a while) ...`);
// Phones update from the -Android.apk asset, so a release without it just leaves them where they are.
const assets = [zip];
const androidDir = path.join(root, "Builds", "Crucible-Android");
const androidResult = path.join(androidDir, "build_result.txt");
const apkBuilt = fs.existsSync(androidResult) && fs.readFileSync(androidResult, "utf8").startsWith(`Succeeded ${tag} `);
if (apkBuilt) {
  const apk = path.join(root, "Builds", `Crucible-${tag}-Android.apk`);
  fs.copyFileSync(path.join(androidDir, "Crucible.apk"), apk);
  assets.push(apk);
} else console.warn(`WARNING: no Android build at ${tag} - this release has no APK and phones will not update.`);

const url = run("gh", ["release", "create", tag, ...assets, "--title", `CRUCIBLE ${tag}`, "--notes",
  notes + "\n\nDownload the zip below, unzip it anywhere you can write to (Desktop, Documents - not Program Files), run Crucible.exe. " +
  "Windows may warn that the app is unrecognised: More info > Run anyway. Later versions install themselves from the main menu."]);
console.log("released: " + url);
console.log("download page for friends: https://github.com/" + run("gh", ["repo", "view", "--json", "nameWithOwner", "-q", ".nameWithOwner"]) + "/releases/latest");
