"""Cut lane q371's measurements of 2026-10-03 and 2026-10-04 into one evidence batch.

Copies each kept file into the batch directory named on the command line,
removing the user-profile path, the user name and the machine name where a file
holds them -- a file so changed is stored under a .trimmed. name -- and writes
originals.sha256: the digest of every original beside the name it is stored
under. The rig's scripts and the two transcripts are stored with a .txt suffix
so they are kept byte for byte.
"""
import hashlib
import os
import re
import shutil
import sys

SAMPLE = r"C:\Source\SixFive7\BrowserAI\.work\q371\sample"
LANE = r"C:\Source\SixFive7\BrowserAI\.work\wt\q371\.work\q371"
SIZES = r"C:\Source\SixFive7\BrowserAI\.work\q371-scratch\sizes"
OUT = sys.argv[1]

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))

KEPT = [
    (os.path.join(SAMPLE, "session.md"), "transcript/session-after.md.txt"),
    (os.path.join(SAMPLE, "session-before-tracing-d8a0101a.md"), "transcript/session-before.md.txt"),
    (os.path.join(SAMPLE, "before-report.json"), "transcript/before-report.json"),
    (os.path.join(SAMPLE, "after-report.json"), "transcript/after-report.json"),
    (os.path.join(LANE, "drive.cjs"), "transcript/drive.cjs.txt"),
    (os.path.join(SIZES, "bigserver.js"), "sizes/rig/bigserver.js.txt"),
    (os.path.join(SIZES, "sizes.js"), "sizes/rig/sizes.js.txt"),
    (os.path.join(SIZES, "ccmodel.js"), "sizes/rig/ccmodel.js.txt"),
    (os.path.join(SIZES, "cxmodel.js"), "sizes/rig/cxmodel.js.txt"),
    (os.path.join(SIZES, "template.json"), "sizes/rig/template.json"),
]

for batch in ["smoke", "limits1", "limits2", "limits3", "limits4"]:
    for name in ["results.json", "progress.log", "policy.json"]:
        KEPT.append((os.path.join(SIZES, "runs", batch, name), f"sizes/runs/{batch}/{name}"))

for run in ["limits1/ccP-12000-r1", "limits2/ccS-12000-r1", "limits1/cx160-20000-r1", "limits1/ccP-ok-60000-r1"]:
    for name in ["model.seen.jsonl", "big.log"]:
        KEPT.append((os.path.join(SIZES, "runs", *run.split("/"), name), f"sizes/runs/{run}/{name}"))


def sha(data):
    return hashlib.sha256(data).hexdigest()


lines = []

for source, stored in KEPT:
    data = open(source, "rb").read()
    text = data.decode("utf-8")
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

shutil.copyfile(__file__, os.path.join(OUT, "cut_q371.py"))
print(len(KEPT), "files")
