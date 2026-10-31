# Zips the Mac build so it still runs after being built and zipped on Windows.
#   python Tools/make_mac_zip.py <version>     -> Builds/Crucible-v<version>-Mac.zip
# Windows has no executable bit, so a plain zip gives a Mac an app that will not start: every entry here
# is marked as written on Unix, with mode 755 on the app's binaries.
# The app is not notarized, and nothing in this zip can get round that: a script packed beside it is
# quarantined with it, so macOS blocks the script and offers Move to Trash. Players install with the
# Terminal line in the release notes (Tools/release.js) instead.
import os
import shutil
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "Builds", "Crucible-Mac", "Crucible.app")
TOP = "Crucible"

def is_binary(rel):
    return "/MacOS/" in "/" + rel or rel.endswith((".dylib", ".so")) or ".framework/" in rel or ".bundle/Contents/MacOS/" in rel


def entry(name, mode):
    info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
    info.create_system = 3
    info.external_attr = (0o100000 | mode) << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    return info


def main():
    version = sys.argv[1]
    if not os.path.isdir(APP):
        sys.exit("no build at " + APP + " - run Crucible > Build > Mac app first")
    out = os.path.join(ROOT, "Builds", "Crucible-v%s-Mac.zip" % version)
    with zipfile.ZipFile(out, "w", allowZip64=True) as z:
        for folder, _, files in os.walk(APP):
            for f in files:
                path = os.path.join(folder, f)
                rel = os.path.relpath(path, APP).replace("\\", "/")
                info = entry("%s/Crucible.app/%s" % (TOP, rel), 0o755 if is_binary(rel) else 0o644)
                with open(path, "rb") as src, z.open(info, "w", force_zip64=True) as dst:
                    shutil.copyfileobj(src, dst, 1024 * 1024)
    print("%.0f MB -> %s" % (os.path.getsize(out) / 1048576, out))


if __name__ == "__main__":
    main()
