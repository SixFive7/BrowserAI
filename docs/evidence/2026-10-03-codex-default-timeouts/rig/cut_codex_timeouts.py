"""Cut the Codex default-timeout runs into docs/evidence/2026-10-03-codex-default-timeouts.

Each run's own files are kept and its scratch CODEX_HOME is left out by
directory, apart from the config.toml the run was given. The user-profile path,
the user name, the machine name and terminal escapes are removed where a file
holds them (stored as .trimmed., original SHA-256 in originals.sha256), and the
rig's own scripts already carry the SPDX header.
"""
import hashlib
import os
import re
import shutil

C = r"C:\Source\SixFive7\BrowserAI\.work\stale-scratch\codex-timeouts"
W = r"C:\Source\SixFive7\BrowserAI\.work\wt\stale"
DST = os.path.join(W, "docs", "evidence", "2026-10-03-codex-default-timeouts")

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))
ESCAPES = re.compile(r"\x1b\[[0-9;?]*[A-Za-z]")

originals = []
copied = []
bulk = {}
left_out = []


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
    if ESCAPES.search(text):
        text = ESCAPES.sub("", text)
        changes.append("terminal escapes")
    return text, changes


def put(src, rel):
    data = open(src, "rb").read()
    text, changes = clean(data.decode("utf-8"))
    out_rel = rel
    if changes:
        stem, ext = os.path.splitext(rel)
        out_rel = f"{stem}.trimmed{ext}"
        originals.append((sha(data), rel.replace(os.sep, "/"), out_rel.replace(os.sep, "/"), ", ".join(changes)))
    dst = os.path.join(DST, out_rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as f:
        f.write(text.encode("utf-8"))
    copied.append(out_rel)


# Everything but the batch's README is regenerated on every run.
if os.path.exists(DST):
    for name in os.listdir(DST):
        if name == "README.md":
            continue
        path = os.path.join(DST, name)
        shutil.rmtree(path) if os.path.isdir(path) else os.remove(path)
os.makedirs(DST, exist_ok=True)

for f in ("standin.js", "run.js", "run-app.js"):
    put(os.path.join(C, f), os.path.join("rig", f))
put(r"C:\Source\SixFive7\BrowserAI\.work\stale-scratch\rigs2\cut_codex_timeouts.py", os.path.join("rig", "cut_codex_timeouts.py"))

# The two files of the Q369 rig the drivers run, which that lane's batch does not
# carry: its app-server driver and its scripted OpenAI model, with the header added.
Q369_RIG = os.path.join(r"C:\Source\SixFive7\BrowserAI\.work\q369", "rig")
for f in ("appdrv.js", "cxmodel.js"):
    src = os.path.join(Q369_RIG, f)
    data = open(src, "rb").read()
    text, changes = clean(data.decode("utf-8"))
    header = "// SPDX-FileCopyrightText: 2026 Jori Huisman\n// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr\n\n"
    dst = os.path.join(DST, "rig", "q369", f)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as out:
        out.write((header + text).encode("utf-8"))
    copied.append(f"rig/q369/{f}")
    originals.append((sha(data), f"q369 rig/{f}", f"rig/q369/{f}", ", ".join(changes + ["SPDX header added"])))

for batch in ("runs", "runs-app"):
    root = os.path.join(C, batch)
    for name in sorted(os.listdir(root)):
        src = os.path.join(root, name)
        if os.path.isfile(src):
            if name == "policy.json":
                continue
            put(src, os.path.join(batch, name))
            continue
        for dp, dns, fns in os.walk(src):
            rel_dir = os.path.relpath(dp, src)
            top = rel_dir.split(os.sep)[0]
            for fn in fns:
                p = os.path.join(dp, fn)
                if top == "home" and not (rel_dir == "home" and fn == "config.toml"):
                    key = f"{batch}/{name}/home"
                    n, b = bulk.get(key, (0, 0))
                    bulk[key] = (n + 1, b + os.path.getsize(p))
                    continue
                if fn in ("appenv.json", "model.requests.jsonl", "driver.log.stderr.txt"):
                    left_out.append((sha(open(p, "rb").read()), f"{batch}/{name}/{fn}", os.path.getsize(p)))
                    continue
                put(p, os.path.join(batch, name, os.path.relpath(p, src)))

with open(os.path.join(DST, "originals.sha256"), "w", encoding="utf-8", newline="\n") as f:
    f.write("# SHA-256 of the file as the run wrote it, its path, the name it is stored under, and what was changed.\n")
    for digest, rel, out_rel, what in originals:
        f.write(f"{digest}  {rel}  ->  {out_rel}  ({what})\n")
with open(os.path.join(DST, "left-out.sha256"), "w", encoding="utf-8", newline="\n") as f:
    f.write("# Left out whole, with SHA-256 and size: the environment each app-server was handed, each app-server's\n")
    f.write("# own trace output (driver.log.stderr.txt, up to 1.8 MB a run), and every request body the scripted\n")
    f.write("# model received, which carries Codex's own prompt and this repository's AGENTS.md.\n")
    for digest, rel, size in left_out:
        f.write(f"{digest}  {rel}  {size}\n")
    f.write("# Left out by directory, with no digest per file: each run's scratch CODEX_HOME, apart from its config.toml.\n")
    for key, (n, b) in sorted(bulk.items()):
        f.write(f"#   {key}/  {n} files  {b} bytes\n")

print(len(copied), "files;", len(originals), "changed;", sum(n for n, _ in bulk.values()), "files left out by directory")
for digest, rel, out_rel, what in originals:
    print("  changed:", rel, "|", what)
