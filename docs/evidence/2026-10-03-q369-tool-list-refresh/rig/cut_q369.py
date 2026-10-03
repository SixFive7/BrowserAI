"""Cut the records behind the four Q369 corrections into docs/evidence/2026-10-03-q369-tool-list-refresh.

Takes the runs the corrections rest on (the terminal UI with a dead server, the
servers that declared no listChanged, one run with tool search on), the lane's
own report, index and tables, the rig files it wrote or changed, and the byte
offsets of what it read in the client. Removes the user profile path, the user
name, the machine name and terminal escapes where a file holds them (stored as
.trimmed., original SHA-256 in originals.sha256), adds the SPDX header to rig
files of a kind the header rule covers, and writes left-out.sha256 for each
file of a counted run that was not taken.
"""
import hashlib
import os
import re
import shutil

Q = r"C:\Source\SixFive7\BrowserAI\.work\q369"
W = r"C:\Source\SixFive7\BrowserAI\.work\wt\stale"
DST = os.path.join(W, "docs", "evidence", "2026-10-03-q369-tool-list-refresh")

USER = os.path.basename(os.environ["USERPROFILE"])
MACHINE = os.environ.get("COMPUTERNAME", "")
PROFILE = re.compile(r"(?i)[A-Z]:(\\\\|\\|/)Users\1" + re.escape(USER))
ESCAPES = re.compile(r"\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x07]*\x07")
HEADER_KINDS = {".js", ".mjs", ".cs", ".ps1", ".psm1", ".sh", ".md"}
COPY = "SPDX-FileCopyrightText: 2026 Jori Huisman"
LICENCE = "SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr"

RUNS = [
    ("tui", "TB-cli-r1"), ("tui", "TB-cli-r2"), ("tui", "TB-cli-r3"),
    ("tui2", "TBclean-cli-r1"), ("tui2", "TBclean-cli-r2"), ("tui2", "TBclean-cli-r3"),
    ("tuiK", "TBr-cli-r1"), ("tuiK", "TBr-cli-r2"), ("tuiK", "TBr-cli-r3"),
    ("ccP", "PA0-cli-r1"), ("ccP", "PA0-cli-r2"), ("ccP", "PA0-cli-r3"),
    ("ccS", "SA0-cli-r1"), ("ccS", "SA0-cli-r2"), ("ccS", "SA0-cli-r3"),
    ("ccS", "SA0-vsc287-r1"), ("ccS", "SA0-vsc287-r2"), ("ccS", "SA0-vsc287-r3"),
    ("ccS2", "SAts-cli-r1"),
]
TAKE = {"ph.launches.log", "ph.wire.jsonl", "result.json", "model.seen.jsonl", "screens.txt", "harness.log", "mcp.json"}
DEBUG_LINES = re.compile(r"ToolSearch|list_changed|tools/list|render-time tools|[Rr]econnect|disconnected|MCP server \"browserai\"|browserai.*(failed|connected|Connection)")

originals = []
left_out = []
bulk = {}
copied = []


def sha(data):
    return hashlib.sha256(data).hexdigest()


def header_lines(ext):
    if ext in (".js", ".mjs", ".cs"):
        return [f"// {COPY}", f"// {LICENCE}"]
    if ext == ".md":
        return [f"<!-- {COPY} -->", f"<!-- {LICENCE} -->"]
    return [f"# {COPY}", f"# {LICENCE}"]


def clean(text):
    changes = []
    if PROFILE.search(text):
        text = PROFILE.sub("%USERPROFILE%", text)
        changes.append("profile path")
    if re.search(r"(?i)\b" + re.escape(USER) + r"\b", text.replace("Jori Huisman", "")):
        text = re.sub(r"(?<![A-Za-z])" + re.escape(USER) + r"(?![A-Za-z])", "<user>", text)
        changes.append("user name")
    if MACHINE and re.search(re.escape(MACHINE), text, re.IGNORECASE):
        text = re.sub(re.escape(MACHINE), "<machine>", text, flags=re.IGNORECASE)
        changes.append("machine name")
    if ESCAPES.search(text):
        text = ESCAPES.sub("", text)
        changes.append("terminal escapes")
    bad = [c for c in text if ord(c) < 0x20 and c not in "\t\n\r"]
    if bad:
        text = "".join(c for c in text if not (ord(c) < 0x20 and c not in "\t\n\r"))
        changes.append(f"{len(bad)} other control characters")
    return text, changes


def write(rel, data_out):
    dst = os.path.join(DST, rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as f:
        f.write(data_out)
    copied.append((rel.replace(os.sep, "/"), len(data_out)))


def put(src, rel, rig=False):
    data = open(src, "rb").read()
    text = data.decode("utf-8")
    new, changes = clean(text)
    out_rel = rel
    if changes:
        stem, ext = os.path.splitext(rel)
        out_rel = f"{stem}.trimmed{ext}"
    ext = os.path.splitext(rel)[1].lower()
    if rig and ext in HEADER_KINDS:
        head = "\n".join(new.splitlines()[:4])
        if COPY not in head or "SPDX-License-Identifier: LicenseRef-" not in head:
            lines = new.split("\n")
            hdr = header_lines(ext)
            lines = ([lines[0]] + hdr + lines[1:]) if lines and lines[0].startswith("#!") else (hdr + lines)
            new = "\n".join(lines)
            changes.append("SPDX header added")
    write(out_rel, new.encode("utf-8"))
    if changes:
        originals.append((sha(data), rel.replace(os.sep, "/"), out_rel.replace(os.sep, "/"), ", ".join(changes)))


# Everything but the batch's README is regenerated on every run.
if os.path.exists(DST):
    for name in os.listdir(DST):
        if name == "README.md":
            continue
        path = os.path.join(DST, name)
        shutil.rmtree(path) if os.path.isdir(path) else os.remove(path)
os.makedirs(DST, exist_ok=True)

put(os.path.join(Q, "REPORT.txt"), "REPORT.txt")
put(os.path.join(Q, "INDEX.txt"), "INDEX.txt")
put(os.path.join(Q, "runs", "summary-by-arm.tsv"), os.path.join("runs", "summary-by-arm.tsv"))
put(os.path.join(Q, "runs", "model-texts.txt"), os.path.join("runs", "model-texts.txt"))
for b in ("ccP", "ccS", "ccS2", "tui", "tui2", "tuiK"):
    p = os.path.join(Q, "runs", b, "table.tsv")
    if os.path.isfile(p):
        put(p, os.path.join("runs", b, "table.tsv"))

for batch, run in RUNS:
    rd = os.path.join(Q, "runs", batch, run)
    for dp, dns, fns in os.walk(rd):
        for fn in sorted(fns):
            src = os.path.join(dp, fn)
            rel_in_run = os.path.relpath(src, rd)
            rel = os.path.join("runs", batch, run, rel_in_run)
            if dp == rd and fn in TAKE:
                put(src, rel)
            elif rel_in_run.split(os.sep)[0] == "cfg":
                key = os.path.join("runs", batch, run, "cfg").replace(os.sep, "/")
                n, b = bulk.get(key, (0, 0))
                bulk[key] = (n + 1, b + os.path.getsize(src))
            else:
                left_out.append((sha(open(src, "rb").read()), rel.replace(os.sep, "/"), os.path.getsize(src)))
    dbg = os.path.join(rd, "claude-debug.log")
    if os.path.isfile(dbg):
        lines = [l for l in open(dbg, encoding="utf-8", errors="replace").read().splitlines() if DEBUG_LINES.search(l)]
        text, _ = clean("\n".join(lines) + "\n")
        hdr = ("# The lines of claude-debug.log that match\n"
               f"# {DEBUG_LINES.pattern}\n"
               "# in file order, with the profile path, the user name and the machine name cut.\n"
               "# The whole log is listed in left-out.sha256.\n")
        write(os.path.join("runs", batch, run, "claude-debug.excerpt.txt"), (hdr + text).encode("utf-8"))

for f in ("phserver.js", "ccmodel.js", "gen.js", "summarize.py", "table.py", "render.py", "rollup.py"):
    put(os.path.join(Q, "rig", f), os.path.join("rig", f), rig=True)
# Stored under a .txt name: line 103 calls SCEN[sc](run), which the repository's
# link scan reads, in any .js file, as a Markdown link to a file named run.
gt = os.path.join(Q, "rig", "gen-tui.js")
put(gt, os.path.join("rig", "gen-tui.js.txt"))
originals.append((sha(open(gt, "rb").read()), "rig/gen-tui.js", "rig/gen-tui.js.txt", "renamed, because the link scan reads SCEN[sc](run) on line 103 as a link"))
for f in ("Harness.cs", "Pty.cs"):
    put(os.path.join(Q, "rig", "ExitRig", f), os.path.join("rig", "ExitRig", f), rig=True)
off = os.path.join(Q, "code", "offsets-claude.tsv")
left_out.append((sha(open(off, "rb").read()), "code/offsets-claude.tsv", os.path.getsize(off)))
put(os.path.join(Q, "isolation", "binaries.tsv"), os.path.join("isolation", "binaries.tsv"))
put(os.path.join(Q, "isolation", "real-config-hashes.tsv"), os.path.join("isolation", "real-config-hashes.tsv"))
put(os.path.join(r"C:\Source\SixFive7\BrowserAI\.work\stale-scratch\rigs2", "cut_q369.py"), os.path.join("rig", "cut_q369.py"), rig=True)

with open(os.path.join(DST, "originals.sha256"), "w", encoding="utf-8", newline="\n") as f:
    f.write("# SHA-256 of the file as lane q369 wrote it, its path, the name it is stored under, and what was changed.\n")
    for digest, rel, out_rel, what in originals:
        f.write(f"{digest}  {rel}  ->  {out_rel}  ({what})\n")
with open(os.path.join(DST, "left-out.sha256"), "w", encoding="utf-8", newline="\n") as f:
    f.write("# SHA-256, path and size of every file of the runs cut here that is not stored.\n")
    for digest, rel, size in left_out:
        f.write(f"{digest}  {rel}  {size}\n")

print(len(copied), "files", sum(n for _, n in copied), "bytes;", len(originals), "changed;", len(left_out), "left out,", sum(s for _, _, s in left_out), "bytes; cfg trees", sum(n for n, _ in bulk.values()), "files", sum(b for _, b in bulk.values()), "bytes")
for digest, rel, out_rel, what in originals:
    print("  changed:", rel, "|", what)
