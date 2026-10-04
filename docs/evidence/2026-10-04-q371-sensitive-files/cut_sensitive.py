"""Cut lane q371's 4 a and 2 b measurements of 2026-10-04 into one evidence batch.

Copies the two runs' reports, the refusal-size report and the two drivers into the
batch directory named on the command line, removing the user-profile path, the
user name and the machine name where a file holds them -- a file so changed is
stored under a .trimmed. name -- and writes originals.sha256: the digest of every
original beside the name it is stored under. The drivers are stored with a .txt
suffix so they are kept byte for byte.
"""
import hashlib
import os
import re
import shutil
import sys

AFTER = r"C:\Source\SixFive7\BrowserAI\.work\q371\measure-4a-after"
SIZES = r"C:\Source\SixFive7\BrowserAI\.work\q371\measure-4a"
LANE = r"C:\Source\SixFive7\BrowserAI\.work\wt\q371\.work\q371"
OUT = sys.argv[1]

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))

KEPT = [
    (os.path.join(AFTER, "sens-chromium-report.json"), "sensitive/sens-chromium-report.json"),
    (os.path.join(AFTER, "sens-firefox-report.json"), "sensitive/sens-firefox-report.json"),
    (os.path.join(LANE, "sens.cjs"), "sensitive/sens.cjs.txt"),
    (os.path.join(SIZES, "sizes2b-report.json"), "refusal-sizes/sizes2b-report.json"),
    (os.path.join(LANE, "sizes2b.cjs"), "refusal-sizes/sizes2b.cjs.txt"),
]


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

shutil.copyfile(__file__, os.path.join(OUT, "cut_sensitive.py"))
print(len(KEPT), "files")
