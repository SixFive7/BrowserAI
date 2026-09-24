<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24 - Q261's refusal at the other end

Establishes
[What Q261's refusal does at the other end](../../../kb/mcp/protocol.md#what-q261s-refusal-does-at-the-other-end----measured-2026-09-24)
and re-verification row 150. Evidence:
[`docs/evidence/2026-09-24-q261/`](../../evidence/2026-09-24-q261/README.md).

**Why it exists.** Q261 adds a refusal and a notification, and a stub client can
establish that BrowserAI sends both. What it cannot establish is anything about
the client: whether a real one re-dials a server that has gone, whether the
sentence reaches the model at all, whether the retry the sentence asks for then
works, and whether the notification changes anything. Those are facts about
somebody else's binary.

## The one thing that needed inventing

⚠️ **BrowserAI has no switch that makes it exit mid-session, and it must not grow
one.** The condition Q261 exists for is a server that went away and was replaced,
so the disappearance is arranged from **outside**: the client's registered
command is `shim.js`, that file starts the published server and forwards stdio
both ways, and after the Nth `tools/call` **answer** it ends the server and exits.
The client meets a closed transport on the next call, re-dials its registered
command, and gets a fresh server -- which is the shape an update leaves behind.

Two details in it are load-bearing. It counts **answers** and not requests, so
the server dies having done its job and not half way through one. And it dies
**once per run**, held by a marker file: every shim instance is started from the
same registered command with the same environment, so without the marker the
re-dialled server would die the moment it answered the refusal and the retry
would meet a third launch instead of a live one.

**The child is ended through the handle of the process the shim started.** No
name, no image path, no enumeration -- the same rule the product holds itself to.

## What is here

| File | What it is |
|---|---|
| `shim.js` | The stdio pass-through that makes the server go away. `Q261_SERVER`, `Q261_LOGDIR`, `Q261_TAG`, `Q261_DIE_AFTER_CALLS`, `Q261_DIED_MARKER` |
| `cc-q261.sh` | One headless Claude Code run: `cc-q261.sh <run-name> <port> [hold-seconds-before-the-retry]`. The third argument is what decides whether the notification did anything -- it leaves the MCP connection idle between the refusal and the retry, so a `tools/list` that is going to arrive has time to |
| `cx-q261.sh` | One `codex app-server` run: `cx-q261.sh <run-name>`. No model, no stub, no credential -- `mcpServer/tool/call` makes the client connect and call without a turn |
| `apistub.js` | The Anthropic Messages API stub, which scripts the model's moves |
| `appserver.js` | The `codex app-server` driver |

⚠️ **`apistub.js` and `appserver.js` are copies taken from
[`2026-09-23-client-reconnect`](../../evidence/2026-09-23-client-reconnect/README.md)'s
rig, and the copy is deliberate.** [`ClientReconnectTests`](../../../tests/BrowserAI.Tests/ClientReconnectTests.cs)
drives both on every run where the binaries are present, and a suite arm may not
depend on a file under `docs/evidence/`: that directory is a **record** of what a
measurement was taken from, and nothing in the suite reads a record.

## What it touches

⚠️ **Every BrowserAI server these start runs with `BROWSERAI_ROOT` at a scratch
app root under the user's profile**, and that is not tidiness. `Program.Main`
starts the stray sweep in the background at startup, and that sweep is
machine-wide by design -- it hunts browsers belonging to *any* session under the
app root it was given. Left at the default it would sweep the developer's own.
The root must be under the profile because
[`InstallRootScope`](../../../src/BrowserAI.Core/Hosting/InstallRootScope.cs)
refuses anything else, which is why it cannot be `.work\`; the suite arms use
`ScratchRoot.ProfileScratch`, which exists for exactly this.

⚠️ **Neither client's real configuration is read or written.**
`CLAUDE_CONFIG_DIR` is a scratch directory seeded with the client's own
throwaway-config recipe, and `CODEX_HOME` is a scratch directory with a
`config.toml` the rig writes. The Codex CLI is taken from
`AppData\Local\OpenAI\Codex\bin`, outside `~\.codex`.

⚠️ **Two corrections from the independent re-measurement of 2026-09-24, by
addition.** *(1)* **That last sentence is about THIS rig and not about the suite
arm** -- *previously it stood alone, and it reads as a property of every Codex
measurement this batch made.* `ClientReconnectTests` resolves the binary through
the product's own `CodexRegistration.Locate`, which finds
`~\.codex\plugins\.plugin-appserver\codex.exe` -- **inside** `~\.codex`, and
byte-identical to the one above on this machine today. Nothing writes there and
the guard on the real configuration is unchanged; what is corrected is the claim
that no Codex binary under `~\.codex` is involved anywhere. *(2)* ⚠️ **A headless
Claude Code started from a working directory inside this repository connects the
repository's own project-scope `.mcp.json` servers**, even with an empty
`.mcp.json` in that directory and `CLAUDE_CONFIG_DIR` pointed at scratch -- the
scratch config isolates the *user* scope and not the project scope. It changes no
finding here -- the refusal and the retry are about `browserai`'s own connection --
and it does change the tool-count total a run reports, which is what the
[kb entry](../../../kb/mcp/protocol.md#the-refusal-fires-on-exactly-the-connection-it-was-designed-for-33)
now records.

## Running them

Publish the server first -- these drive the published binary, not the tree:

```bash
dotnet publish src/BrowserAI/BrowserAI.csproj -c Release -r win-x64 --self-contained
bash docs/probes/2026-09-24-q261/cc-q261.sh CCQ1 8811 5
bash docs/probes/2026-09-24-q261/cx-q261.sh CXQ1
```

Each writes into `.work/q261-2026-09-24/`. What the runs printed is in the
evidence batch; the numbers are in the kb entry, which is what to read first.
