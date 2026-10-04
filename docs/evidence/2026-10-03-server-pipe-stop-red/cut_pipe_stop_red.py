"""Cut the gate run in which ServerPipeTests' graceful-stop arm went red, and the runs beside it.

Copies lane c's gate logs of 2026-10-03 into the batch directory named on the
command line, read from that lane's scratch and never changed there, removing the
user-profile path, the user name and the machine name where a file holds them,
storing a file so changed under a .trimmed. name, and writes originals.sha256:
the digest of every original beside the name it is stored under.
"""
import hashlib
import os
import re
import shutil
import sys

SUITE = r"C:\Source\SixFive7\BrowserAI\.work\wt\c\.work\suite"
OUT = sys.argv[1]

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))

KEPT = [
    ("c2-gate.log", "a32bf62-gate/c2-gate.log"),
    ("c2-ps-driver.log", "a32bf62-ps/c2-ps-driver.log"),
    ("c2-ps.log", "a32bf62-ps/c2-ps.log"),
    ("c2-ps-red-server-26992.log", "a32bf62-ps/c2-ps-red-server-26992.log"),
    ("c2-bash-driver.log", "a32bf62-bash/c2-bash-driver.log"),
    ("c2-bash.log", "a32bf62-bash/c2-bash.log"),
    ("c3-gate.log", "a32bf62-gate-again/c3-gate.log"),
    ("c3-ps-driver.log", "a32bf62-gate-again/c3-ps-driver.log"),
    ("c3-ps.log", "a32bf62-gate-again/c3-ps.log"),
    ("c3-bash-driver.log", "a32bf62-gate-again/c3-bash-driver.log"),
    ("c3-bash.log", "a32bf62-gate-again/c3-bash.log"),
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

shutil.copyfile(__file__, os.path.join(OUT, "cut_pipe_stop_red.py"))
print(len(KEPT), "files")
