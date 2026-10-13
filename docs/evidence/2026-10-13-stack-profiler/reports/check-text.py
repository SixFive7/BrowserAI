"""Checks report drafts for characters a person does not type, the phrases the
house wording rule refuses, and personal or product names.

Usage: python -I check-text.py <file> [...]
"""
import re
import sys

CHARS = {
    "\u2014": "em dash", "\u2013": "en dash", "\u2012": "figure dash", "\u2015": "horizontal bar",
    "\u2010": "hyphen", "\u2011": "non-breaking hyphen", "\u2212": "minus sign",
    "\u2026": "ellipsis", "\u2018": "left single quote", "\u2019": "right single quote",
    "\u201c": "left double quote", "\u201d": "right double quote", "\u00a0": "no-break space",
    "\u202f": "narrow no-break space", "\u2009": "thin space", "\u200b": "zero-width space",
    "\u2060": "word joiner", "\ufeff": "byte order mark",
}

PHRASES = [
    "rather than", "it's worth noting", "it is worth noting", "in other words", "simply put",
    "in conclusion", "in summary", "to summarise", "to summarize", "furthermore", "moreover",
    "ultimately", "importantly", "crucially", "at its core", "as mentioned", "robust", "seamless",
    "leverage", "delve", "landscape", "a testament to", "streamlined", "game-changer",
    "cutting-edge", "state-of-the-art", "empower", "let's", "a wide range of", "a variety of",
    "numerous", "essentially", "that said,", "additionally,",
]

NAMES = [
    "browserai", "registerai", "sixfive7", "jori", "huisman", "voipfabric", "exofabric",
    "ede@", "c:\\users", "c:/users", "/c/users", "c:\\source", "c:/source", "/c/source",
    "appdata", ".work", "windhawkmain",
]

bad = 0
for path in sys.argv[1:]:
    text = open(path, encoding="utf-8").read()
    for number, line in enumerate(text.splitlines(), 1):
        for char, name in CHARS.items():
            if char in line:
                print(f"{path}:{number}: {name}")
                bad += 1
        if any(ord(c) > 126 for c in line):
            print(f"{path}:{number}: non-ASCII: {[c for c in line if ord(c) > 126]}")
            bad += 1
        lower = line.lower()
        for phrase in PHRASES:
            if phrase in lower:
                print(f"{path}:{number}: phrase '{phrase}'")
                bad += 1
        for name in NAMES:
            if name in lower:
                print(f"{path}:{number}: name '{name}'")
                bad += 1
        for match in re.finditer(r"(^|[.!?]\s+)(Note (that|also|how|the))\b", line):
            print(f"{path}:{number}: sentence opens with '{match.group(2)}'")
            bad += 1
print(f"{bad} finding(s)")
