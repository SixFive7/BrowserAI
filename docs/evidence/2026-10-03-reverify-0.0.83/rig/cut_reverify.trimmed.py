"""Cut lane stale's re-verification outputs into docs/evidence/2026-10-03-reverify-0.0.83.

Copies the small text records and the rigs that produced them, removes the user
profile path and terminal escape bytes where a file holds them (the copy is then
named with .trimmed. and the original's SHA-256 goes to originals.sha256), and
adds the two-line SPDX header to every rig file of a kind the header rule covers
(same name, original digest recorded too). Prints what it did.
"""
import hashlib
import os
import re
import shutil
import sys

S = r"C:\Source\SixFive7\BrowserAI\.work\stale-scratch"
W = r"C:\Source\SixFive7\BrowserAI\.work\wt\stale"
DST = os.path.join(W, "docs", "evidence", "2026-10-03-reverify-0.0.83")

USER = os.path.basename(os.environ["USERPROFILE"])
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))
OWNER = re.compile(r"(?m)(\s)" + re.escape(USER) + r"(\s+\d+\s)")
ESCAPES = re.compile(r"\x1b\[[0-9;]*[A-Za-z]")
HEADER_KINDS = {".js", ".mjs", ".cs", ".ps1", ".psm1", ".sh", ".md"}
COPY = "SPDX-FileCopyrightText: 2026 Jori Huisman"
LICENCE = "SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr"

originals = []
copied = []


def sha(data):
    return hashlib.sha256(data).hexdigest()


def header_lines(ext):
    if ext in (".js", ".mjs", ".cs"):
        return [f"// {COPY}", f"// {LICENCE}"]
    if ext == ".md":
        return [f"<!-- {COPY} -->", f"<!-- {LICENCE} -->"]
    return [f"# {COPY}", f"# {LICENCE}"]


def put(src, rel, rig=False):
    data = open(src, "rb").read()
    text = None
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        pass
    changes = []
    out_rel = rel
    if text is not None:
        new = text
        if PROFILE.search(new):
            new = PROFILE.sub("%USERPROFILE%", new)
            changes.append("profile path")
        if OWNER.search(new):
            new = OWNER.sub(r"\1<user>\2", new)
            changes.append("user name")
        if ESCAPES.search(new):
            new = ESCAPES.sub("", new)
            changes.append("terminal escapes")
        if "fgInTree=False fg=[" in new:
            # The foreground window while no window of the launched tree held it
            # belonged to some other program on the desktop; its title is cut,
            # its class and pid are kept.
            new = re.sub(r"(fgInTree=False fg=\[hwnd=0x[0-9A-Fa-f]+ pid=\d+ class=\S+ title=)[^\]]*\]", r"\1<cut>]", new)
            changes.append("titles of windows outside the launched tree")
        if "\x1b" in new:
            raise SystemExit(f"an escape byte survived in {src}")
        if changes:
            stem, ext = os.path.splitext(rel)
            out_rel = f"{stem}.trimmed{ext}"
        ext = os.path.splitext(rel)[1].lower()
        if rig and ext in HEADER_KINDS:
            head = "\n".join(new.splitlines()[:4])
            if COPY not in head or "SPDX-License-Identifier: LicenseRef-" not in head:
                lines = new.split("\n")
                hdr = header_lines(ext)
                if lines and lines[0].startswith("#!"):
                    lines = [lines[0]] + hdr + lines[1:]
                else:
                    lines = hdr + lines
                new = "\n".join(lines)
                changes.append("SPDX header added")
        data_out = new.encode("utf-8")
    else:
        data_out = data
    dst = os.path.join(DST, out_rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as f:
        f.write(data_out)
    if changes:
        originals.append((sha(data), rel.replace(os.sep, "/"), out_rel.replace(os.sep, "/"), ", ".join(changes)))
    copied.append((out_rel.replace(os.sep, "/"), len(data_out)))


def tree(src_dir, rel_dir, rig=False, skip=()):
    for dp, dns, fns in os.walk(src_dir):
        dns[:] = [d for d in dns if not d.startswith(("profile", "session-", "conc-", "eftype")) and d not in ("output", "downloads", "fp")]
        for fn in sorted(fns):
            if fn in skip:
                continue
            src = os.path.join(dp, fn)
            rel = os.path.join(rel_dir, os.path.relpath(src, src_dir))
            put(src, rel, rig=rig)


if os.path.exists(DST):
    shutil.rmtree(DST)
os.makedirs(DST)

out = os.path.join(S, "out")
# The batch logs, one directory per batch.
for b in ("batch1", "batch2", "batch3", "batch4"):
    tree(os.path.join(out, b), os.path.join("runs", b))
# Per-row records that the rigs wrote outside the batch logs.
put(os.path.join(out, "row109", "results.json"), os.path.join("row109", "results.json"))
put(os.path.join(out, "row109", "ua-probe.log"), os.path.join("row109", "ua-probe.log"))
for arm in sorted(os.listdir(os.path.join(out, "row109"))):
    p = os.path.join(out, "row109", arm, "config.json")
    if os.path.isfile(p):
        put(p, os.path.join("row109", arm, "config.json"))
for f in sorted(os.listdir(os.path.join(out, "row115"))):
    if f.endswith(".log"):
        put(os.path.join(out, "row115", f), os.path.join("row115", f))
put(os.path.join(out, "row121", "sandbox-arms.log"), os.path.join("row121", "sandbox-arms.log"))
put(os.path.join(out, "row121", "summary.json"), os.path.join("row121", "summary.json"))
for arm in ("plain-1", "plain-2", "disable-field-trial-config-1", "disable-field-trial-config-2"):
    put(os.path.join(out, "row121", arm, "stderr.log"), os.path.join("row121", arm, "stderr.log"))
put(os.path.join(out, "row122", "raw-chromium", "raw.log"), os.path.join("row122", "raw.log"))
put(os.path.join(out, "row122", "raw-chromium", "raw-rows.json"), os.path.join("row122", "raw-rows.json"))
put(os.path.join(out, "row127", "window-time.log"), os.path.join("row127", "window-time.log"))
put(os.path.join(out, "row32", "direct-arms.log"), os.path.join("row32", "direct-arms.log"))
put(os.path.join(out, "row32", "a-mcp", "mcp-arm.log"), os.path.join("row32", "a-mcp", "mcp-arm.log"))
put(os.path.join(out, "row32", "a-mcp", "config.json"), os.path.join("row32", "a-mcp", "config.json"))
for d in ("row34", "row38", "row95"):
    tree(os.path.join(out, d), d)
for f in sorted(os.listdir(os.path.join(out, "rows5-6"))):
    if f == "row6-context-diff.txt":
        continue
    put(os.path.join(out, "rows5-6", f), os.path.join("rows5-6", f))
tree(os.path.join(out, "registry"), "registry")
fp = os.path.join(S, "rigs", "row152", "fp")
for arm in sorted(os.listdir(fp)):
    for f in ("config.json", "fingerprint.json", "fp.log"):
        p = os.path.join(fp, arm, f)
        if os.path.isfile(p):
            put(p, os.path.join("row152", arm, f))
pw = os.path.join(out, "row152-pw")
if os.path.isdir(pw):
    for f in sorted(os.listdir(pw)):
        p = os.path.join(pw, f)
        if os.path.isfile(p):
            put(p, os.path.join("row152", "pw", f))
dash = os.path.join(W, ".work", "zoomout", "c", "evidence")
tree(dash, "rows163-166")
# Lane rv's own records of its two gate rounds, which rows 5 and 66 are stamped from.
RV = r"C:\Source\SixFive7\BrowserAI\.work\rv-scratch"
for f in ("gate-1.log", "gate-2.log", "gate-2-ps-driver.log", "gate-2-bash-driver.log"):
    put(os.path.join(RV, "logs", f), os.path.join("rv-gate", f))
for f in ("browsers-10-gate1-before.txt", "browsers-11-gate1-after.txt", "browsers-10-gate2-before.txt", "browsers-11-gate2-after.txt"):
    put(os.path.join(RV, f), os.path.join("rv-gate", f))
for f in ("containment-chromium.json", "containment-firefox.json"):
    put(os.path.join(r"C:\Source\SixFive7\BrowserAI\.work\wt\rv\.work", f), os.path.join("rv-gate", f))
put(os.path.join(RV, "gate.ps1"), os.path.join("rv-gate", "gate.ps1"), rig=True)
# The rigs as they ran, and the driver of the row 152 raw arms.
tree(os.path.join(S, "rigs"), "rig", rig=True)
for f in ("row152-pw.sh", "cut_reverify.py", "inventory.py"):
    put(os.path.join(S, "rigs2", f), os.path.join("rig", f), rig=True)

with open(os.path.join(DST, "originals.sha256"), "w", encoding="utf-8", newline="\n") as f:
    f.write("# SHA-256 of the file as the rig or the driver wrote it, its path in the scratch tree's\n")
    f.write("# layout as cut here, the name it is stored under, and what was changed.\n")
    for digest, rel, out_rel, what in originals:
        f.write(f"{digest}  {rel}  ->  {out_rel}  ({what})\n")

total = sum(n for _, n in copied)
print(len(copied), "files", total, "bytes;", len(originals), "changed")
for digest, rel, out_rel, what in originals:
    print("  changed:", rel, "->", out_rel, "|", what)
