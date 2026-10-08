"""Cut lane S1's gate at 079e3d1c, the first after the tool list was compiled into the binary.

Copies the gate wrapper's, the gate drivers' and the test host's logs from the lane's
worktree into the batch directory named on the command line, removing the user-profile path, the user
name and the machine name where a file holds them, storing a file so changed under a
.trimmed. name, and writes originals.sha256: the digest of every original beside the
name it is stored under.
"""
import hashlib
import os
import re
import shutil
import sys

SUITE = r"C:\Source\SixFive7\BrowserAI\.work\wt\s1b\.work\suite"
OUT = sys.argv[1]

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))

KEPT = [
    ("s1-list-gate.log", "079e3d1-gate/s1-list-gate.log"),
    ("s1-list-ps-driver.log", "079e3d1-ps/s1-list-ps-driver.log"),
    ("s1-list-ps.log", "079e3d1-ps/s1-list-ps.log"),
    ("s1-list-bash-driver.log", "079e3d1-bash/s1-list-bash-driver.log"),
    ("s1-list-bash.log", "079e3d1-bash/s1-list-bash.log"),
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

shutil.copyfile(__file__, os.path.join(OUT, "cut_gate_079e3d1.py"))
print(len(KEPT), "files")
