<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- the MCP registrar survey: what registers a server with Claude Code and Codex (zoom-out track A)

**What this is.** The record of the zoom-out's track A, which asked whether a
maintained library or tool should take over BrowserAI's registration with
Claude Code and Codex. Three passes, one folder each:

- `first-pass/`, **2026-09-25**: the clients' own `mcp add` and `mcp remove`, and
  three third-party registrars, run in nine experiments against a sandbox home
  named `Zoë O'Brien`, which puts a space, an apostrophe and a non-ASCII letter in
  every path, seeded with hand-edited-looking configuration. Claude Code
  **2.1.282** and codex-cli **0.155.0-alpha.9.2**, copied into scratch and run
  from there; add-mcp **2.4.0**, APM **0.31.0** and install-mcp **1.10.2**.
- `dotnet/`, **2026-09-27**: a search of NuGet for a .NET registrar.
- `second-look/`, **2026-10-01**: a search for the wheel the first two missed,
  by reading source, documentation and registries. **Nothing was run** in it.

The answer was that no tool should be adopted, and on 2026-10-01 the maintainer
chose a command-line program of his own (Q330 to Q333 in
[`DECISIONS.md`](../../../DECISIONS.md)). **331 files beside this README, 513,711
bytes as cut.** The scripts are a probe record at
[`docs/probes/2026-09-25-registrar-survey`](../../probes/2026-09-25-registrar-survey/README.md).

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: MCP protocol](../../../kb/mcp/protocol.md#the-clients-own-registration-commands-in-a-sandbox-and-the-tools-that-would-register-for-them----measured-2026-09-25) | What the clients' own commands and the three registrars did, and what the second look read |
| [kb: toolchain](../../../kb/toolchain.md#a-fresh-dotnet_cli_home-writes-the-real-user-path----measured-2026-09-25) | The user `PATH` a first `dotnet` run wrote, from `first-pass/notes/INCIDENTS.md` |
| [kb: re-verification](../../../kb/re-verification.md) | Rows 176 and 177 |
| [kb: not established](../../../kb/not-established.md#the-clients-at-the-other-end) | What the survey did not run |

## What is here

| Path | What it is |
|---|---|
| `first-pass/REPORT.md` | The first pass's report: the answer, what BrowserAI's registration does and how many lines it takes, what each tool did in the sandbox, and the directions |
| `first-pass/notes/INCIDENTS.md` | The two side effects the first pass caused on the real machine, and how each was undone |
| `first-pass/notes/repo-meta-2026-09-25.tsv`, `maintenance-2026-09-25.txt`, `source-commits-read.txt` | Each candidate project's licence, stars, last push, open issues and recent commits, and the commit of every source tree read |
| `first-pass/sandbox/t1-native/` | The clients' own commands: each step's output (`out-*.txt`), `runlog.txt` with the exit code and milliseconds of each, the configuration files copied after each step (`snap/`), and the sandbox home and project as they were left |
| `first-pass/sandbox/t2-addmcp/` to `t9-addmcp-foreign/` | The same for add-mcp's command line and its programmatic API, add-mcp with `CLAUDE_CONFIG_DIR` pointed elsewhere, APM, APM and add-mcp against another install's entry, install-mcp, and a Codex file that uses TOML 1.0 |
| `second-look/REPORT.trimmed.md` | The second look: the verdict, what the first passes missed, how other products register, BrowserAI's and OutlookAI's registration code compared, and the directions and open questions |
| `second-look/candidates.tsv`, `registry-candidates.json`, `search-registries.err`, `source-commits-read.txt` | The candidate matrix, the npm, NuGet and crates.io search results, and the commit of every source tree read |
| `dotnet/nuget-candidates.json`, `search.err` | The NuGet search |
| `originals.sha256`, `left-out.sha256` | In each folder: what was changed and what was left out whole, with digests |

## What was cut, and what was left out

- **Ten listings of the real client configuration files** in `first-pass/` and
  four in `second-look/`, SHA-256 by path: they name the user profile and what
  it holds. Two of the four in `second-look/` are named after a private
  repository, and `left-out.sha256` lists them as `10-later-plan-baseline.txt`
  and `11-later-plan-final.txt`; the digests are of the files as they were.
- **This machine's user `PATH`**, `first-pass/notes/hkcu-path-now.txt`, which
  lists the software installed on it.
- **Third-party source and documentation**: two files of openai/codex at
  `rust-v0.157.0` in `first-pass/`, 22 source files and 35 pages, changelogs,
  feeds and READMEs in `second-look/`, and six READMEs and three source files in
  `dotnet/`. Each is cited by repository and commit or by address.
- **Three files a tool wrote into a sandbox home**: two NVIDIA shader-cache files
  and a Git Credential Manager sentinel, from the APM experiment.
- **Two empty READMEs** in `dotnet/`, which the fetch wrote when it found none;
  they are not listed.
- **Changed, under the names in `originals.sha256`**: the terminal escapes in
  nine add-mcp outputs, removed under `.trimmed.` names; the two SPDX lines added
  to the two reports, `INCIDENTS.md` and three `one.mjs` scripts; and in
  `second-look/REPORT.trimmed.md`, one private project's name replaced by
  *Another private project*.

## Privacy

No file here names the user or the machine. The sandbox home's name is made up
for the test. The privacy scan, with a positive control, found two things and
neither is personal: a version number shaped like a private address in
`second-look/registry-candidates.json`, and a package author's address in
`dotnet/nuget-candidates.json`, which is nuget.org's own public record of that
package and is kept as it came.
