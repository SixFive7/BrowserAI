<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24-codex-expansion

**Q288: does Codex expand a variable in an MCP server's `command`?** It does not.
Measured 2026-09-24 between 13:41Z and 13:57Z on this machine, against codex-cli
**0.155.0-alpha.9.2** (the CLI the Codex desktop app installs, at
`~\.codex\plugins\.plugin-appserver\codex.exe`), Windows 10.0.26200, by a researcher
the session dispatched; cut into this directory by the phase 1b writer the same day.

Cited by [kb](../../../kb/mcp/protocol.md#codex-expands-nothing-in-a-servers-command-and-finds-a-bare-name-on-the-servers-path----measured-2026-09-24)
and [re-verification row 161](../../../kb/re-verification.md), and by
`CodexRegistryView.Classify`, `McpRegistryView.ClassifyPath` and
`CodexRegistrationTests.ACodexEntrySpelledWithAVariableIsNeverOursBecauseCodexExpandsNothing`.
The maintainer's decision it led to is Q294 b: a Codex project entry names
`BrowserAI.Server.exe` and the install puts its `current\` folder on the user's PATH.

## How it was measured

`rig/` drives a real `codex app-server` over stdio with a scratch `CODEX_HOME` per run
(`runs/<run>/home`, removed afterwards; `logs/cleanup.log`) and a stub model provider
that points at nothing, so no model was ever asked anything. Every configured server
is `stub/ProbeStub.cs`, a stdio MCP server that records how it was started -- its image,
its argv, its working directory and its environment -- into `runs/<run>/stublogs/`,
and answers one tool, `probe_whoami`, with the same record. `rig/matrix.sh` runs the
whole matrix three times; `summary.txt` and `summary.json` are `rig/summarize.js` over
every `runs/*/result.json`.

| Case | What the entry says | Started |
|---|---|---|
| `${LOCALAPPDATA}`, `$LOCALAPPDATA`, `%LOCALAPPDATA%` and `~` in `command`, with `/` and with `\` | four spellings, two separators, user and project scope, three rounds | **0 of 48**, each *"MCP startup failed: The system cannot find the path specified. (os error 3)"* |
| The same four spellings in `args` | an absolute `command` | 24 of 24, and the server received each argument as written |
| Absolute controls, `ctl` and `uctl` | an absolute `command` | 45 of the 45 Codex loaded; the other 3 are an untrusted project's own entries, which Codex does not read |
| `barepath` | `command = "probe-stub.exe"` and the entry's own `env.PATH` naming the stub's directory | 3 of 3, project scope |
| `tildepath` | the same with `env.PATH` spelled `~\AppData\Local\...` | 3 of 3, project scope |
| `cmdwrap`, `cmdwrap_v` | `cmd.exe /d /c %LOCALAPPDATA%\...` and its `/v:on` form | 3 of 3 each |
| `cmdwrap_space*` | the same with a space in the expanded path | 0 of 9 plain or quoted; 3 of 3 with `/v:on` and `!LOCALAPPDATA!` |

**`codex mcp add` stores what it is given, literally**: 78 of 78 adds exited 0 and read
back byte for byte through `codex mcp get --json` (`runs/*/setup.json`).

⚠️ **The bare-name row is the entry's own PATH, not the inherited one.** Codex builds a
server's environment from an allowlist of its own variables, `PATH` among them, and the
entry's `env` table replaces any of them; it then resolves `command` with the `which`
crate over that `PATH`. So a bare name also resolves through the PATH Codex itself
inherited, with no `env` in the entry -- which is what Q294 b relies on -- but that half
is read from the source below and was **not** a case here.

## The source it was read against

Codex at tag `rust-v0.155.0-alpha.9.2`, commit
`4607249e430dac1c961df4dc615beae88e33cec8` (`clone.sh`, `logs/clone.log`):

- `codex-rs/rmcp-client/src/stdio_server_launcher.rs:263-285`: `launch_server` builds
  the environment, resolves `program` with `program_resolver::resolve(program, &envs,
  &cwd)` and starts `Command::new(&resolved_program)` with `env_clear().envs(&envs)`.
  Nothing between the configured string and the process expands anything.
- `codex-rs/rmcp-client/src/program_resolver.rs:41-65`: on Windows, `which::which_in`
  over the `PATH` in that environment; on a failure the original text is started as it
  is, and *os error 3* is Windows' own *path not found* for that text.
- `codex-rs/rmcp-client/src/utils.rs:16-57`: `create_env_for_mcp_server` reads each
  allowlisted variable from Codex's own environment and lays the entry's `env` over it.

Re-read on `main` at `282cd7b019378746cb87bd91a95d8b4bcae12aa3` (2026-09-24T13:09:49Z)
by the writer: `program_resolver.rs` and `utils.rs` are byte-identical to the tag once
line endings are set aside, and `launch_server` still resolves the configured program
with the same call and starts what it returns. Variable expansion is
[openai/codex#2680](https://github.com/openai/codex/issues/2680), *"Support environment
variable expansion"*, open since 2025-08-25 (read 2026-09-24).

## Two more things the same runs show

- **Codex creates `tmp\arg0\` under its home and puts it FIRST on every server's PATH.**
  All 18 project setups listed `config.toml`, `tmp` and `tmp\arg0` in the project's
  `.codex` after `codex mcp add` and `codex mcp get`
  (`files.projectDotCodexListing` in each `runs/*/setup.json`), and in all 31 app-server
  runs every server whose entry did not set a PATH of its own carried
  `<CODEX_HOME>\tmp\arg0\codex-arg0<random suffix>` as the first entry of its PATH.
- **Every `mcpServerStatus/list` starts one more copy of each server.** The driver asked
  for the status list twice per run, once with the thread's id and once without. In all
  31 runs every server that started was launched exactly three times, and in 84 of 84
  rows the list without a thread reported a pid different from the list with one, and
  neither was the pid that answered the thread's tool call. The copies had exited by the
  census the driver took after its tool calls, in 31 of 31 runs.

## What was cut, and what was left out

⚠️ **This machine's PATH is cut wherever it was recorded.** Every stub launch record
carries the server's environment, and the tool answers, the driver logs and the result
files repeat it; the PATH lists the software installed on the maintainer's machine. In
**323** files, **712** PATH values keep only their `codex-arg0` alias entries -- the
evidence for the first bullet above -- and end in `[cut: N entries of this machine's
PATH]`. `logs/real-codex-before.txt` keeps its first three lines, the SHA-256, size and
time of the real `~\.codex\config.toml` taken before the matrix ran, and cuts the
directory listing of the maintainer's own `~\.codex` that followed them. Each such file
is here under a `.trimmed.` name, and [`originals.sha256`](originals.sha256) carries
the SHA-256 and byte count of every original with its path and the number of values
cut. Nothing else in those files moved. Profile paths under `C:\Users\jori` stay, as
they do in the other batches here.

**Left out whole:**

| What | Why | SHA-256 |
|---|---|---|
| `stub/probe-stub.exe`, 7,680 bytes | A build of `stub/ProbeStub.cs` | `97c060102d093cdf37648a6dd550d3087bd9ee770d313057affc7dc829479217` |
| `crates/which-8.0.0.crate`, 26,209 bytes, and its unpacked source | The `which` crate as published on crates.io, read for `which_in`; the registry is the record | `d3fabb953106c3c8eea8306e4393700d7657561cb43122571b172bbfb7c7ba1d` |
| `crates/env_home-0.1.0.crate`, 9,006 bytes, and its unpacked source | An optional dependency of `which` that supplies the home directory (`sys.rs:146-149`), read for how a PATH entry starting with `~` resolves (`finder.rs:242`), which is the `tildepath` row | `c7f84e12ccf0a7ddc17a6c41c93326024c42920d7ee630d04950e6926645c0fe` |

The two Codex clones `clone.sh` made were removed by the researcher's own cleanup; the
commits above are the record.

⚠️ **Ten files gained the repository's two-line SPDX header** -- `clone.sh`, every
`.js` and `.sh` under `rig/`, and `stub/ProbeStub.cs` -- because this tree requires one
on each of those kinds. Nothing else in them moved. **Line endings are this
repository's**, per [`.gitattributes`](../../../.gitattributes).
