"""Cut the fourth full run in which CoordinatorWakeTests' recheck arm went red, and its other half.

Lane tab's two-shell gate at d8db27a, a commit rebased onto lane c's merge before it reached
next: the PowerShell half read 916 of 917, this arm the one red, and the Git Bash half 917 of 917.
Copies the logs into docs/evidence/2026-10-03-coordinator-wake-red the way cut_wake_red.py cut the
first nine, removing the user-profile path, the user name and the machine name where a file
holds them (stored as .trimmed., original SHA-256 appended to originals.sha256). It adds files
and touches none of the nine.
"""
import hashlib
import os
import re
import shutil

W = r"C:\Source\SixFive7\BrowserAI\.work\wt\tab"
DST = os.path.join(W, "docs", "evidence", "2026-10-03-coordinator-wake-red")
SUITE = os.path.join(W, ".work", "suite")
LOGS = os.path.join(W, ".work", "logs")

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))

SOURCES = [
    (os.path.join(SUITE, "ord-ps-tab8b.log"), "d8db27a-ps/ord-ps-tab8b.log"),
    (os.path.join(LOGS, "gate-ps-tab8b-driver.log"), "d8db27a-ps/ord-ps-tab8b-driver.log"),
    (os.path.join(SUITE, "ord-bash-tab8b.log"), "d8db27a-bash/ord-bash-tab8b.log"),
]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def clean(text):
    changes = []
    if PROFILE.search(text):
        text = PROFILE.sub("%USERPROFILE%", text)
        changes.append("profile path")
    if re.search(r"(?<![A-Za-z])" + re.escape(USER) + r"(?![A-Za-z])", text):
        text = re.sub(r"(?<![A-Za-z])" + re.escape(USER) + r"(?![A-Za-z])", "<user>", text)
        changes.append("user name")
    if MACHINE and re.search(re.escape(MACHINE), text, re.IGNORECASE):
        text = re.sub(re.escape(MACHINE), "<machine>", text, flags=re.IGNORECASE)
        changes.append("machine name")
    bad = [c for c in text if ord(c) < 0x20 and c not in "\t\n\r"]
    if bad:
        raise SystemExit("a control character in a source log")
    return text, changes


added = []
for src, rel in SOURCES:
    data = open(src, "rb").read()
    text, changes = clean(data.decode("utf-8"))
    out_rel = rel
    if changes:
        stem, ext = os.path.splitext(rel)
        out_rel = f"{stem}.trimmed{ext}"
        added.append((sha(data), rel, out_rel, ", ".join(changes)))
    dst = os.path.join(DST, out_rel)
    assert not os.path.exists(dst), dst
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as f:
        f.write(text.encode("utf-8"))
    print(out_rel, len(text), changes)

shutil.copyfile(os.path.abspath(__file__), os.path.join(DST, "cut_wake_red_tab.py"))
with open(os.path.join(DST, "originals.sha256"), "a", encoding="utf-8", newline="\n") as f:
    for digest, rel, out_rel, what in added:
        f.write(f"{digest}  {rel}  ->  {out_rel}  ({what})\n")
