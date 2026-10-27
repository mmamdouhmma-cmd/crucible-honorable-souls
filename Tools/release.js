// Publishes the Windows build as a GitHub release that friends download and the game updates from.
//   node Tools/release.js ["what changed"]        (or double-click Tools/release.cmd)
// 1. reads the version from ProjectSettings (Player Settings > Version) - the release tag is v<version>
// 2. zips Builds/CrucibleLite-Win64 (without the Burst debug folder) to Builds/Crucible-v<version>-Win64.zip
// 3. creates the release on the public repo and uploads the zip (gh CLI, your login)
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
// Windows' bundled bsdtar writes zips and is far faster than Compress-Archive on ~600 MB. Named by full
// path: Git Bash's GNU tar comes first on some PATHs and silently writes a TAR under a .zip name.
run(path.join(process.env.SystemRoot || "C:\\Windows", "System32", "tar.exe"), ["-a", "-c", "-f", zip, "--exclude", "*_BurstDebugInformation_DoNotShip*", "--exclude", "build_result.txt", "-C", buildDir, "."]);
console.log(`${(fs.statSync(zip).size / 1048576).toFixed(0)} MB -> ${zip}`);

console.log(`creating release ${tag} and uploading (this takes a while) ...`);
const url = run("gh", ["release", "create", tag, zip, "--title", `CRUCIBLE ${tag}`, "--notes",
  notes + "\n\nDownload the zip below, unzip it anywhere you can write to (Desktop, Documents - not Program Files), run Crucible.exe. " +
  "Windows may warn that the app is unrecognised: More info > Run anyway. Later versions install themselves from the main menu."]);
console.log("released: " + url);
console.log("download page for friends: https://github.com/" + run("gh", ["repo", "view", "--json", "nameWithOwner", "-q", ".nameWithOwner"]) + "/releases/latest");
