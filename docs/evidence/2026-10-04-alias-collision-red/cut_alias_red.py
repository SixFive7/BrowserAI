"""Cut the gate run in which CanonicalPathTests' subst-over-a-mapped-drive arm went red, and the runs beside it.

Copies lane q371's gate logs of 2026-10-04 into the batch directory named on the
command line, removing the user-profile path, the user name and the machine name
where a file holds them, storing a file so changed under a .trimmed. name, and
writes originals.sha256: the digest of every original beside the name it is
stored under.
"""
import hashlib
import os
import re
import shutil
import sys

SUITE = r"C:\Source\SixFive7\BrowserAI\.work\wt\q371\.work\suite"
OUT = sys.argv[1]

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))

KEPT = [
    ("q371g2-gate.log", "a34ce65-gate/q371g2-gate.log"),
    ("q371g2-ps-driver.log", "a34ce65-ps/q371g2-ps-driver.log"),
    ("q371g2-ps.log", "a34ce65-ps/q371g2-ps.log"),
    ("q371g2-bash-driver.log", "a34ce65-bash/q371g2-bash-driver.log"),
    ("q371g2-bash.log", "a34ce65-bash/q371g2-bash.log"),
    ("q371g2r-gate.log", "a34ce65-ps-again/q371g2r-gate.log"),
    ("q371g2r-ps-driver.log", "a34ce65-ps-again/q371g2r-ps-driver.log"),
    ("q371g2r-ps.log", "a34ce65-ps-again/q371g2r-ps.log"),
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

shutil.copyfile(__file__, os.path.join(OUT, "cut_alias_red.py"))
print(len(KEPT), "files")
