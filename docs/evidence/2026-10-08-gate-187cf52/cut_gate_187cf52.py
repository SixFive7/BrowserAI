"""Cut lane S1's gate at 187cf52d, its two reds, and the two red arms run on their own at that commit.

Copies the gate wrapper's, the gate drivers' and the test host's logs, and the logs of
the three runs of the two red arms on their own, from the lane's worktree into the
batch directory named on the command line, removing the user-profile path, the user
name and the machine name where a file holds them, storing a file so changed under a
.trimmed. name, and writes originals.sha256: the digest of every original beside the
name it is stored under.
"""
import hashlib
import os
import re
import shutil
import sys

SUITE = r"C:\Source\SixFive7\BrowserAI\.work\wt\s1\.work\suite"
OUT = sys.argv[1]

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))

KEPT = [
    ("s1-pin-gate.log", "187cf52-gate/s1-pin-gate.log"),
    ("s1-pin-ps-driver.log", "187cf52-ps/s1-pin-ps-driver.log"),
    ("s1-pin-ps.log", "187cf52-ps/s1-pin-ps.log"),
    ("s1-pin-bash-driver.log", "187cf52-bash/s1-pin-bash-driver.log"),
    ("s1-pin-bash.log", "187cf52-bash/s1-pin-bash.log"),
    ("rerun-1.log", "187cf52-again/rerun-1.log"),
    ("rerun-2.log", "187cf52-again/rerun-2.log"),
    ("rerun-3.log", "187cf52-again/rerun-3.log"),
]


def sha(data):
    return hashlib.sha256(data).hexdigest()


lines = []

for source, stored in KEPT:
    data = open(os.path.join(SUITE, source), "rb").read()
    text = data.decode("utf-8", errors="strict")
    cut = PROFILE.sub("%USERPROFILE%", text)

    if MACHINE:
        cut = cut.replace(MACHINE, "%COMPUTERNAME%")

    cut = re.sub(r"(?i)(?<![A-Za-z])" + re.escape(USER) + r"(?![A-Za-z])", "%USERNAME%", cut)

    if cut != text:
        stem, extension = os.path.splitext(stored)
        stored = f"{stem}.trimmed{extension}"

    target = os.path.join(OUT, *stored.split("/"))
    os.makedirs(os.path.dirname(target), exist_ok=True)
    open(target, "wb").write(cut.encode("utf-8"))
    lines.append(f"{sha(data)}  {stored}")

with open(os.path.join(OUT, "originals.sha256"), "w", encoding="utf-8", newline="\n") as digests:
    digests.write("\n".join(lines) + "\n")

shutil.copyfile(__file__, os.path.join(OUT, "cut_gate_187cf52.py"))
print(len(KEPT), "files")
