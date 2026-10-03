# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Seeds a sandbox home and project with hand-edited-looking configuration.
# Requires sandbox-env.sh to have been sourced.
H="$(cygpath -u "$USERPROFILE")"
P="$S/project"
mkdir -p "$H/AppData/Local/BrowserAI.app/current" "$P/.codex"
: > "$H/AppData/Local/BrowserAI.app/current/BrowserAI.Server.exe"
( cd "$P" && git init -q . 2>/dev/null )
cat > "$H/.claude.json" <<'J'
{
  "numStartups": 42,
  "installMethod": "native",
  "autoUpdates": false,
  "projects": {
    "C:/work/demo": {
      "allowedTools": [],
      "mcpServers": {}
    }
  },
  "mcpServers": {
    "other": {
      "type": "stdio",
      "command": "C:\\Tools\\other\\other-mcp.exe",
      "args": [
        "--flag"
      ],
      "env": {}
    }
  },
  "userID": "seed"
}
J
cat > "$CODEX_HOME_U/config.toml" <<'T'
# my codex config -- hand-edited, keep these comments
model = "gpt-5.5"   # the model I use
approval_policy = "on-request"

[projects.'C:\work\demo']
trust_level = "trusted"

# a server I added by hand
[mcp_servers.other]
command = 'C:\Tools\other\other-mcp.exe'   # literal string on purpose
args = ["--flag"]
startup_timeout_sec = 20

[mcp_servers.other.env]
FOO = "bar"
T
cat > "$P/.mcp.json" <<'J'
{
  "mcpServers": {
    "teammate-server": {
      "command": "npx",
      "args": ["-y", "@example/teammate-mcp"]
    }
  }
}
J
cat > "$P/.codex/config.toml" <<'T'
# project codex config, committed by the team
[mcp_servers.teammate]
command = "npx"
args = ["-y", "@example/teammate-mcp"]
T
SERVER_W="$(cygpath -w "$H/AppData/Local/BrowserAI.app/current/BrowserAI.Server.exe")"
export SERVER_W
node -e 'JSON.parse(require("fs").readFileSync(process.argv[1],"utf8")); console.log("seed .claude.json parses")' "$(cygpath -w "$H/.claude.json")"
echo "seeded; server=$SERVER_W"
