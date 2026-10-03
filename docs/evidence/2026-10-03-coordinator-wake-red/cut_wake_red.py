"""Cut the three full runs in which CoordinatorWakeTests' recheck arm went red, and the runs beside them.

Copies the logs into docs/evidence/2026-10-03-coordinator-wake-red, removing the
user-profile path, the user name and the machine name where a file holds them
(stored as .trimmed., original SHA-256 in originals.sha256). Keeps the README.
"""
import hashlib
import os
import re
import shutil

W = r"C:\Source\SixFive7\BrowserAI\.work\wt\stale"
DST = os.path.join(W, "docs", "evidence", "2026-10-03-coordinator-wake-red")
REG = r"C:\Source\SixFive7\BrowserAI\.work\wt\reg\.work\suite"
MAIN = r"C:\Source\SixFive7\BrowserAI\.work\suite"
STALE = os.path.join(W, ".work", "suite")

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))

SOURCES = [
    (os.path.join(REG, "reg6d-bash.log"), "dffe9d4-bash/reg6d-bash.log"),
    (os.path.join(REG, "reg6d-bash-driver.log"), "dffe9d4-bash/reg6d-bash-driver.log"),
    (os.path.join(REG, "reg6d-ps.log"), "dffe9d4-ps/reg6d-ps.log"),
    (os.path.join(REG, "reg6e-bash.log"), "dffe9d4-bash-again/reg6e-bash.log"),
    (os.path.join(MAIN, "agentsmd-ps-1.log"), "2026-09-29-ps/agentsmd-ps-1.log"),
    (os.path.join(STALE, "stale-bash-3.log"), "6e6388d-bash/stale-bash-3.log"),
    (os.path.join(STALE, "stale-bash-3-driver.log"), "6e6388d-bash/stale-bash-3-driver.log"),
    (os.path.join(STALE, "stale-ps-3.log"), "6e6388d-ps/stale-ps-3.log"),
    (os.path.join(STALE, "stale-bash-4.log"), "6e6388d-bash-again/stale-bash-4.log"),
]

originals = []


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


if os.path.exists(DST):
    for name in os.listdir(DST):
        if name == "README.md":
            continue
        path = os.path.join(DST, name)
        shutil.rmtree(path) if os.path.isdir(path) else os.remove(path)
os.makedirs(DST, exist_ok=True)

for src, rel in SOURCES:
    data = open(src, "rb").read()
    text, changes = clean(data.decode("utf-8"))
    out_rel = rel
    if changes:
        stem, ext = os.path.splitext(rel)
        out_rel = f"{stem}.trimmed{ext}"
        originals.append((sha(data), rel, out_rel, ", ".join(changes)))
    dst = os.path.join(DST, out_rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as f:
        f.write(text.encode("utf-8"))
    print(out_rel, len(text), changes)

shutil.copyfile(os.path.abspath(__file__), os.path.join(DST, "cut_wake_red.py"))
with open(os.path.join(DST, "originals.sha256"), "w", encoding="utf-8", newline="\n") as f:
    f.write("# SHA-256 of the log as the run wrote it, its name here, the name it is stored under, and what was changed.\n")
    for digest, rel, out_rel, what in originals:
        f.write(f"{digest}  {rel}  ->  {out_rel}  ({what})\n")
