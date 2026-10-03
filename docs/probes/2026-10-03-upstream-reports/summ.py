# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import json, os, re, sys

for d in sys.argv[1:]:
    print("===========", d)
    for line in open(os.path.join(d, "results.jsonl"), encoding="utf-8"):
        r = json.loads(line)
        m = re.search(r"firefox-\d+", r["firefox"])
        ff = m.group(0) if m else "?"
        print(r["arm"].ljust(26), "pw", r["playwrightCore"], ff,
              "keyVar=", r["keyOff"], "restart=", r.get("safeModeRestart"),
              "| launch", r.get("launch"), "| newPage", r.get("newPage"),
              "| goto", r.get("goto"), "| tree@20s", r.get("treeAt20s"),
              "| tree", r.get("treeHealthy"))
