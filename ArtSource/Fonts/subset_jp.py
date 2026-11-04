"""Builds the game's Japanese subtitle font: Noto Sans JP (SIL OFL 1.1, LICENSE.txt beside the source)
cut down to the glyphs the story actually uses.

    python -I ArtSource/Fonts/subset_jp.py

Keeps: ASCII + Latin-1, Japanese punctuation, every hiragana and katakana, full-width forms, and every
character that appears in Assets/_project/Story/**/*.json. Re-run after editing any dialogue line,
or a new kanji renders as a blank box. Output: Assets/_project/Fonts/NotoSansJP-Story.otf
"""
import glob
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")

from fontTools import subset

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "ArtSource", "Fonts", "NotoSansJP", "NotoSansJP-Regular.otf")
OUT = os.path.join(ROOT, "Assets", "_project", "Fonts", "NotoSansJP-Story.otf")
LINES = os.path.join(ROOT, "Assets", "_project", "Story", "**", "*.json")

RANGES = [(0x20, 0x7E), (0xA0, 0xFF), (0x2010, 0x2027), (0x3000, 0x303F),
          (0x3040, 0x309F), (0x30A0, 0x30FF), (0xFF00, 0xFFEF)]


def main():
    codepoints = set()
    for a, b in RANGES:
        codepoints.update(range(a, b + 1))
    files = glob.glob(LINES, recursive=True)
    used = set()
    for path in files:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        for item in data.get("speakers", []) + data.get("lines", []):
            for key in ("ja", "en"):
                used.update(ord(c) for c in item.get(key, ""))
    codepoints |= used
    kanji = sorted(c for c in used if 0x4E00 <= c <= 0x9FFF)

    opts = subset.Options()
    opts.layout_features = ["*"]
    opts.name_IDs = ["*"]
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    font = subset.load_font(SRC, opts)
    sub = subset.Subsetter(opts)
    sub.populate(unicodes=sorted(codepoints))
    sub.subset(font)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    subset.save_font(font, OUT, opts)
    print(f"{len(files)} dialogue file(s), {len(kanji)} kanji: {''.join(chr(c) for c in kanji)}")
    print(f"wrote {OUT} ({os.path.getsize(OUT) // 1024} KB, {len(font.getGlyphOrder())} glyphs)")


if __name__ == "__main__":
    sys.exit(main())
