# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
F=C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
NODE="C:/Program Files/nodejs/node.exe"
CODEX="/c/Users/jori/AppData/Local/OpenAI/Codex/bin/247581e40ee272fb/codex.exe"
export STUB_PORT=8909 STUB_LOGDIR="$F/logs" STUB_TAG="C4r-stub" STUB_SCRIPT='tool:mcp__probe/ping:{"note":"p2-after-resume"}||text:phase2 done'
export STUB_WAITFILE="" STUB_WAITBEFORE=-1
"$NODE" "$F/rig/openaistub.js" > "$WU/logs/C4r.stub.out" 2>&1 &
SP=$!
sleep 1
sed -i 's|base_url = "http://127.0.0.1:[0-9]*"|base_url = "http://127.0.0.1:8909"|' "$WU/codexhome/config.toml"
export CODEX_HOME="$F/codexhome" STUB_KEY=not-a-real-key
timeout 180 "$CODEX" exec --json --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox -C "$F/proj" resume --last "Call ping again." \
  > "$WU/out/C4r.jsonl" 2> "$WU/out/C4r.err.txt" < /dev/null
echo "codex_exit=$?"
kill $SP 2>/dev/null
