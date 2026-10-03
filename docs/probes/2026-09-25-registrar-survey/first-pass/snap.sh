# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# snap <label>: copies every config file the experiment may touch into $S/snap/<label>/
snap() {
  local d="$S/snap/$1"; mkdir -p "$d"
  local H="$(cygpath -u "$USERPROFILE")"
  for f in "$H/.claude.json" "$H/.claude-alt/.claude.json" "$CODEX_HOME_U/config.toml" "$S/project/.mcp.json" "$S/project/.codex/config.toml" "$H/.apm/apm.yml"; do
    [ -f "$f" ] && cp "$f" "$d/$(echo "$f" | sed "s#$S/##; s#[/ ]#_#g")"
  done
  ( cd "$S" && /usr/bin/find . -path ./snap -prune -o -type f -newer "$S/.t0" -print 2>/dev/null | sort ) > "$d/_files-changed-since-start.txt"
  ( cd "$S" && /usr/bin/find . -path ./snap -prune -o -print 2>/dev/null | sort ) > "$d/_tree.txt"
}
# run <label> <cmd...>: runs a command, records exit code, milliseconds and both streams
run() {
  local label=$1; shift
  local t0=$(date +%s%N)
  "$@" > "$S/out-$label.txt" 2>&1; local rc=$?
  local t1=$(date +%s%N)
  echo "[$label] rc=$rc ms=$(( (t1-t0)/1000000 )) :: $*" | tee -a "$S/runlog.txt"
  sed 's/^/    | /' "$S/out-$label.txt" | head -12
}
