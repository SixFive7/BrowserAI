# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

"""Summarises one or more stress runs' results.jsonl files: per mode, launches,
captures, launches that lost the browser, dumps by signature, and whether the
StackSamplingProfiler thread was seen.

Usage: python -I summarize.py <results.jsonl> [...]
"""
import collections
import json
import sys

rows = []
for path in sys.argv[1:]:
    with open(path, encoding="utf-8") as handle:
        rows.extend(json.loads(line) for line in handle if line.strip())

for mode in ("on", "off"):
    mine = [row for row in rows if row["mode"] == mode]
    if not mine:
        continue
    captures = sum(row["captures"] for row in mine)
    lost = [row for row in mine if row["error"]]
    signatures = collections.Counter(
        "%s %s at %s on %s (read 0x%s)" % (
            dump.get("ptype"), dump.get("code"), dump.get("at"), dump.get("thread"),
            dump.get("params", ["", "?"])[-1].replace("0x", ""))
        for row in mine for dump in row["dumps"])
    profiler = collections.Counter(str(row["profilerThread"]) for row in mine)
    print("mode %-3s launches %3d captures %5d launches that lost the browser %3d dumps %3d profiler thread seen %s"
          % (mode, len(mine), captures, len(lost), sum(len(row["dumps"]) for row in mine), dict(profiler)))
    for signature, count in signatures.items():
        print("    %3d x %s" % (count, signature))
    for row in lost:
        print("    launch %3d lost the browser after %d captures, %d ms in: %s" % (row["launch"], row["captures"], row["errorAtMs"] or -1, row["error"][:90]))
