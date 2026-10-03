<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- the clients' own registration commands and three registrars, in a sandbox home

Establishes
[The clients' own registration commands in a sandbox](../../../kb/mcp/protocol.md#the-clients-own-registration-commands-in-a-sandbox-and-the-tools-that-would-register-for-them----measured-2026-09-25)
and re-verification row 176. Evidence:
[`docs/evidence/2026-09-25-registrar-survey/`](../../evidence/2026-09-25-registrar-survey/README.md),
where each experiment's `runlog.txt` holds every command as it ran.

**Why it exists.** The zoom-out's track A asked whether a maintained registrar
should replace BrowserAI's own registration code. The rig runs the clients'
commands and the registrars against one seeded sandbox and keeps a copy of every
configuration file after each step, so a re-run compares file against file.

## What is here

| File | What it does |
|---|---|
| `first-pass/sandbox-env.sh` | Sourced with an experiment's name: builds the sandbox home `Zoë O'Brien` and points `USERPROFILE`, `HOME`, `APPDATA`, `LOCALAPPDATA`, `TEMP`, `CODEX_HOME` and `CLAUDE_CONFIG_DIR` into it, npm at a scratch cache and an empty user config, and `PATH` at the scratch copies of the clients. Mode `separate` points `CLAUDE_CONFIG_DIR` at a second folder, which is how add-mcp was caught ignoring it |
| `first-pass/seed.sh` | Writes the seeded configuration: a `~/.claude.json` with another server and project entries, a Codex `config.toml` with comments and trust tables, a project folder with a git repository, and an empty stand-in for the installed server |
| `first-pass/snap.sh` | `snap <label>` copies every configuration file a step may touch, and lists the files changed since the sandbox was made |
| `first-pass/hash-real.sh`, `second-look/hash-real.sh` | Hash the real client configuration files, read-only, before and after each experiment, to show a run touched none of them; the second adds OutlookAI's registry record and both repositories' git state |
| `first-pass/sdk-test.mjs` | Drives add-mcp's programmatic API the way a registrar would |
| `first-pass/toml-compat.cjs` | Which TOML 1.0 shapes Codex reads can the registrars' TOML library read and write back |
| `first-pass/foreign.cjs` | Seeds another install's `browserai` entry |
| `first-pass/member-lines.cjs` | Counts the lines a C# member takes, for the report's line counts |
| `second-look/fetch-src.sh` | Downloads a public repository's source as a tarball, with no git and no credential helper, so nothing can ask for a sign-in |
| `second-look/search-registries.mjs`, `dotnet/search.mjs` | Search npm, NuGet and crates.io through their public endpoints |

## What keeps it off the rest of the machine

- Every client and registrar runs under the sandbox home, from scratch copies,
  with updates and telemetry switched off. `hash-real.sh` is the check that this
  held; on 2026-09-25 it caught the one write that got through.
- ⚠️ **That write was the user `PATH`**: a `dotnet package search` run by hand
  with `DOTNET_CLI_HOME` in scratch appended a folder to it, and it was restored
  to the byte. [kb](../../../kb/toolchain.md#a-fresh-dotnet_cli_home-writes-the-real-user-path----measured-2026-09-25)
  says why; no script here runs `dotnet`.
- ⚠️ **APM starts git for a GitHub sign-in.** On 2026-09-25 it put Git Credential
  Manager's window on the desktop twice, until git's credential helpers were
  disabled. How they were disabled was not recorded beyond the empty
  `empty.gitconfig` each APM experiment holds, so a re-run disables them before
  the first `apm` command.
- **It selects no process by image name.** None of the eight spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on appears in any file here, checked by a search that found them in
  [the Stagehand rig](../2026-09-25-stagehand/README.md) in the same command, so
  the scan has nothing to read.
- `fetch-src.sh` and the two searches reach public registries and codeload
  only.

## Running it

Nobody ran it when this record was written; what follows is read from the files
and the run logs. Each experiment is `. sandbox-env.sh <name>`, then `seed.sh`,
then the commands its `runlog.txt` lists with `snap` between them. The scratch
root is compiled in as `C:\Source\SixFive7\BrowserAI\.work\zoomout\a`, and the
clients and registrars have to be copied or installed under it first; npm reads
an empty file named `npmrc-empty` there, which is not stored because it is empty.
A run is a new measurement with a date of its own.

## How the stored copies differ from the ones that ran

The two SPDX lines were added to every script. In both `hash-real.sh` the line
`H=` named the user profile and reads `%USERPROFILE%` here, which bash does not
expand: a run puts the profile's POSIX path back.
