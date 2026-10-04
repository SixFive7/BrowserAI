"""Cut lane q371's gate at 5bf02f48, the first gate after the push rule, and its two reds.

Copies the gate driver's and the test host's logs from the gate worktree into the
batch directory named on the command line, removing the user-profile path, the
user name and the machine name where a file holds them, storing a file so changed
under a .trimmed. name, and writes originals.sha256: the digest of every original
beside the name it is stored under.
"""
import hashlib
import os
import re
import shutil
import sys

SUITE = r"C:\Source\SixFive7\BrowserAI\.work\wt\q371-gate\.work\suite"
OUT = sys.argv[1]

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))

KEPT = [
    ("q371g3-gate.log", "5bf02f4-gate/q371g3-gate.log"),
    ("q371g3-ps-driver.log", "5bf02f4-ps/q371g3-ps-driver.log"),
    ("q371g3-ps.log", "5bf02f4-ps/q371g3-ps.log"),
    ("q371g3-bash-driver.log", "5bf02f4-bash/q371g3-bash-driver.log"),
    ("q371g3-bash.log", "5bf02f4-bash/q371g3-bash.log"),
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

shutil.copyfile(__file__, os.path.join(OUT, "cut_gate_5bf02f4.py"))
print(len(KEPT), "files")
