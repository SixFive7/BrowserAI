<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Second look: is there a wheel for MCP registration, and the shared-library design (2026-10-01)

## 0. Machine state

- **Nothing of the maintainer's was changed by this work.** I started no `claude`, no `codex` and no third-party tool. Everything comes from source, docs, registries and read-only queries (curl, tar, node for JSON, `reg query`, `Get-AppxPackage`, `Get-CimInstance`, `fsutil reparsepoint query`). No GitHub API call was made, nothing was installed or posted, and no tracked file was edited in either repository.
- **Hashes, before (11:35:27Z) and after (12:06:00Z):**

| Item | Before | After |
|---|---|---|
| `~/.claude.json`, whole file | `47fb7ee6e69401419866094c07270d8997559291dceabdfab5b72141faca0fec` (125,540 bytes) | `efcc587de25e5cc8e04b909257624ad867d5f4f25ebe97d1ddc5bf092e5929f7` (122,920 bytes) |
| `~/.claude.json`, `mcpServers` subtree (outlookai, browserai) | `f7c681829bfec02cafc5b5e99bdb4d1ae42bb89b39f64b40a532d7c97a9a0b39` | same |
| `~/.claude.json`, `projects[*].mcpServers` | `44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a` | same |
| `~/.codex/config.toml` | `592eea525b0b67f5e5dc08cbdf2211e2eeaeebc34f64d7c1ad1e2e870630b35e` (3,788 bytes) | `2dcac463c1eab5b0f455aa0965501cd3e69298f5c8549306317eea3328b92944` (3,896 bytes) |
| `~/.claude/settings.json` | `807b78724ddbc1952c2baa1f77c93e5f8b30c593f8ebbc7835dddefbf4c31cda` | same |
| VS Code `settings.json` | `2e2589f31d1f26f4e344146862c3e6d41b41177cccb96703da561fd2e8c9a415` | same |
| VS Code `mcp.json` | `ee8944bca3202aa91084fed1ce3dc8517b374045ac682393b8b0351b780b852a` | same |
| `HKCU\Environment\Path` | `17cb2c8703d03f599a30053c8747d7cd789e6a497681af82bc346c9bf0f477c0` | same |
| `HKCU\Environment`, all values | `a161a2c0c02a9ab3ffc79b870296ee2616378fc4a8483c652595339329fdb151` | same |
| `HKCU\Software\OutlookAI\Mcp` (minus the timestamp) | `f20b1cbd3ab99f01a2a3a512df22459ade088680f7ee20a413636a66bbf20cf3` | same |
| BrowserAI `mcp-registration.json` | `7368382ae814fb7d54e1c18387702f95002c8d4fe8c363b18809685f70cc4d1a` | same |
| `%APPDATA%\Claude` config | absent | absent |

- **`~/.claude.json` changed as a whole file** because the running Claude Code rewrites it for its own bookkeeping. The part a registrar would touch is identical.
- **`~/.codex/config.toml` changed, and not through me.** The Codex desktop app was started from Explorer at 13:58:16 local (pid 76112, parent 14580 = `Explorer.EXE`) and replaced the file at 13:58:28. Its creation time equals its write time. `[mcp_servers.browserai]` is still there. I cannot say what the 108 added bytes are: I only had the earlier hash, not the earlier content.
- **Repositories:** BrowserAI HEAD moved `ecfbab3` to `45e4a85`, the other agent's drift-check commit; the tree is clean. OutlookAI is at `7cce988`, clean.
- **Scratch** is `C:\Source\SixFive7\BrowserAI\.work\zoomout\a-second-look\` (3.4 MB). The downloaded source trees are deleted; their commits are in `source-commits-read.txt`.

## 1. Verdict

**There is no wheel to adopt, but there are many wheels, and BrowserAI already owns most of the one it needs.** Confidence: about 90% that nothing existing meets all nine requirements today, and higher for the in-process .NET case.

The problem is solved over and over, privately. I found nine generic registrars and read the source of nine product- or vendor-specific implementations today (three more were read on 2026-09-27). None is usable from a Windows NativeAOT installer that must add, remove and detect for Claude Code and Codex, leave other installs' entries alone, and let the client write its own file.

| Candidate | Claude Code + Codex | Add / remove / detect | Leaves other installs alone | Write method | From NativeAOT .NET | Maturity |
|---|---|---|---|---|---|---|
| **kurir 0.3.0** (Rust, MIT) | yes; Codex user scope only | add only | refuses a name clash unless `--force` | client CLI, or whole-file JSON rewrite | as a 4 to 6 MB exe | 18 days old, one author, 36 commits, 2 stars |
| **add-mcp 2.4.1** (Node, Apache-2.0) | yes, but ignores `CLAUDE_CONFIG_DIR` | yes / yes / partial | overwrites silently | direct file edit, plain write, TOML comments lost | needs Node | one maintainer |
| **agent-install 0.0.8** (Node, MIT) | same gap | yes / yes / list | not tested | same libraries as add-mcp | needs Node | no release since 2026-06-24 |
| **APM 0.32.0** (Python, MIT) | yes | add; remove by editing a manifest | leaves it, reports "already configured" | whole-file rewrite, atomic | 32 MB runtime | weekly releases, pre-1.0 |
| **BrowserAI's own code** | yes | yes / yes / yes | yes, in every intent | the client's own command | is NativeAOT .NET | in-house |

The full matrix (26 rows) is in `candidates.tsv`.

**Evidence that carries the verdict:**

- **The newest and best-built self-installers chose BrowserAI's mechanism.**
  - Blender's `mcp-for-blender setup` (834 lines, changed 2026-09-30) runs `claude mcp add --scope user` and `codex mcp add`, and reads the config files only to classify state as NEW, CONFIGURED, OUTDATED or UNREADABLE.
  - Serena (148 lines) drives `claude`, `codex`, `codebuddy` and `grok` the same way.
  - Both are hand-written and tied to one product.
- **Vendors that need this for several products built their own shared kit.** Sylphx `mcp-kit` (Rust, `setup.rs`, 372 lines) and Glean `@gleanwork/mcp-config-schema` (TypeScript) are exactly the maintainer's proposal, each for its own servers.
- **The SDKs ship no registrar.**
  - FastMCP's `fastmcp install claude-code` runs `claude mcp add` with no scope flag, so it lands in local scope. It has no Codex target, no remove, and only registers Python servers.
  - The official Python SDK's `mcp install` writes Claude Desktop only.
  - The TypeScript, C#, Go and Rust SDKs have no installer. Their docs tell the author to run `claude mcp add` or hand-write a file.
- **kurir is the first product-agnostic native tool with the maintainer's stated goal.** Its README says: "Products provide a generic server specification; Kurir handles harness-specific config paths, entry shapes, scopes, backups, delegated CLIs". Read at 0.3.0, not run:
  - It has no unregister and no "is my server registered" read.
  - Its `--force` removes without a scope flag.
  - Its file mode strips comments, re-sorts keys and writes without an atomic replace.
  - It starts `claude` by PATH only, with no timeout and no window suppression.

**"Reinventing" overstates what is left to do.** About 70 to 75% of the library the maintainer describes exists in BrowserAI today (section 6). The open decision is whether to extract and share it.

**"System wide" means per user in practice.** Neither client offers an all-users registration of a local executable. Claude Code's `C:\Program Files\ClaudeCode\managed-mcp.json` takes exclusive control of MCP, and `managedMcpServers` accepts only HTTP and SSE entries.

**Checked:** FastMCP, the six SDKs, NuGet MCP packages and `dnx`; Docker, JetBrains, GitHub, Figma, Notion, 1Password, WinGet, Desktop Commander, Blender, Serena, Context7, Sentry, Playwright MCP, Chrome DevTools MCP; add-mcp, APM, kurir, agent-install, mcptools; npm, crates.io and NuGet search with new queries (931 unique packages); every vendor surface in the brief.
**Not checked:** PyPI and Go modules through a registry API (web search only), GitHub code search, and anything private.

## 2. What the first passes missed or got wrong

1. **kurir was missed.** It was published 2026-09-13, before the first survey. The .NET follow-up searched NuGet and C# repositories only.
2. **agent-install was missed.** npm reports 9,683,485 monthly downloads. It is a Node API with install, remove and list for 14 hosts, and has the same gaps as add-mcp.
3. **The whole category "each product's own setup command" was missed.** Blender, Serena, Context7 (`ctx7 setup` and `ctx7 remove`, 2,321 lines with a hand-written TOML editor), Desktop Commander (942 + 745 lines), plus the Sylphx and Glean kits. This category is the strongest evidence for the verdict.
4. **Server-author tooling was not covered.** FastMCP's install code moved to `fastmcp_slim/fastmcp/cli/install/` in PrefectHQ/fastmcp, which is why the old path returned 404.
5. **".mcpb bundles are installed by Claude Desktop only" is incomplete.** Claude Code plugins accept `.mcpb` and `.dxt` bundles in the manifest's `mcpServers`; the changelog mentions it from 2.1.169 (2026-06-08). Windows' registry also registers from an mcpb manifest.
6. **The Windows build was recorded as 26200.9550.** `ver` reads 10.0.26300.9550 (26H2) today. That meets the documented floor of 26220.7262, and `odr.exe` is still absent from PATH, System32 and the CBS system app. I cannot tell whether the build changed or was misread.
7. **How installed products register on Windows was not covered** (section 3).

**Confirmed unchanged:**
- add-mcp 2.4.1 (2026-09-29) still ignores `CLAUDE_CONFIG_DIR`, still re-serialises TOML with `@iarna/toml`, still writes with plain `writeFileSync`. Issue #122 and PR #124 are still open; no issue mentions `CLAUDE_CONFIG_DIR`. The 2.4.1 release fixed the same class of bug for Copilot CLI only.
- APM 0.32.0 (2026-09-25): release notes show no MCP change. Not re-run.
- Codex 0.159.3: `codex mcp add` still has no scope flag. Issue #2680 is still open.
- SEP-2633 is still a draft; last activity 2026-07-28.

## 3. How others register, and where the clients are heading

**Installed products on Windows:**

| Product | Mechanism |
|---|---|
| Docker MCP Toolkit | Edits client files itself (yq for JSON, go-toml for Codex); registers only its own gateway |
| JetBrains IDEs 2026.2 | "Auto-Configure" for Junie, VS Code, Claude Code, Codex, Air, Copilot CLI; docs say it "updates the client's configuration file automatically" |
| 1Password 8.12.36.40 (installed here) | Does not register itself. Ships `1password-mcp.exe` as an MSIX alias; Claude Code users install a plugin, Codex users type the command by hand |
| Figma desktop | Manual: `claude mcp add --transport http figma-desktop http://127.0.0.1:3845/mcp` |
| Notion | Manual `claude mcp add`, or a Claude Code plugin; Codex by hand-editing `config.toml` |
| GitHub MCP server | VS Code install link; `claude mcp add` documented |
| WinGet's own MCP server | Microsoft's docs tell the user to hand-write `.vscode/mcp.json` with the exe path |
| Unity bridges (read 2026-09-27) | CoplayDev: `claude mcp add` plus a TOML edit. IvanMurzak: project-scope file edits |

**What each client vendor offers:**

- **Every command-line client now has the same verb shape.** `claude mcp add|remove|list|get`, `codex mcp add|remove|list|get`, `gemini mcp add|remove|list` (with `-s user|project`), `copilot mcp add|remove|list|get`, and per Serena's and kurir's code also `grok`, `codebuddy` and `hermes`. This is the one thing that is converging, and it favours a command-line-driven library: a new client is a small descriptor.
- **Claude Code** (2.1.286, npm 2026-09-30):
  - `claude mcp list` still health-checks every server, so there is no cheap machine-readable read.
  - 2.1.283 fixed `add`, `add-json` and `remove` reporting success when the file could not be written.
  - 2.1.284 makes `add` refuse when managed settings allow only plugin servers.
  - Plugins can be installed without a prompt (`claude plugin marketplace add`, `claude plugin install`), but a plugin server's tools are named `mcp__plugin_<plugin>_<server>__<tool>`.
- **Codex** (0.159.3, npm 2026-09-30): no scope flag. Plugins can bundle servers.
- **VS Code:** `code --add-mcp` writes the user profile and has no remove. It can discover servers from Claude Desktop, Copilot CLI, Cursor and Windsurf. It accepts `.mcp.json` at the project root. (From a summarised docs fetch.)
- **Cursor:** a deep link the user must confirm. Its command line has `list`, `login`, `enable` and `disable`, with no add or remove.
- **Windows' on-device registry:** needs MSIX identity, runs servers in a separate agent session, and bundles without identity are not reachable by default. I found no statement that Claude Code or Codex read it.
- **MCP Registry `server.json`** (v1.8.1): package types are npm, pypi, nuget, oci, cargo and mcpb. It cannot describe an executable an installer already put on the machine. Claude Code's docs name no registry install.

**Will "the client installs the server" replace "the server registers itself" soon enough to matter? No, not for these two clients within a year.** The install surfaces are per-vendor plugin formats that the user triggers. The cross-vendor pieces do not cover a pre-installed local exe, and the shared config format has stalled.

## 4. The two products' registration code compared

| | BrowserAI | OutlookAI |
|---|---|---|
| Clients | Claude Code, Codex | Claude Code only |
| Scopes | user and project, both clients | user (opt-in tick box, reconciled at every Outlook start) and project (button) |
| Trigger | Velopack install, update and uninstall hooks; window links | the add-in at Outlook start and its settings dialog; the installer and uninstaller do nothing |
| Writes | the client's own command | direct edit: validate, splice, verify, temp file plus `File.Replace` |
| Reads | Claude: parses the file, honours `CLAUDE_CONFIG_DIR`. Codex: `codex mcp list --json` | parses `%USERPROFILE%\.claude.json` only; three separate readers |
| Ours means | command under this install root; "present" only if the file is a console-subsystem exe | command equals the installed server's path after `${VAR}` expansion |
| Foreign entry | never touched, in any intent | never touched by itself; replaced after an explicit "on" |
| Unreadable | refuse | leave alone, including "exists but reads empty" |
| Consent | registers on install | tri-state; asks in Outlook; never acts on inferred intent |
| Pre-checks | must be `current\BrowserAI.Server.exe`, exist, console subsystem | server exists; .NET 10 runtime present; Claude Code present |
| Record | `mcp-registration.json`, schema 2 | `HKCU\Software\OutlookAI\Mcp`, read by `outlook_health` |
| Framework | net10.0-windows, NativeAOT, C# latest | add-in: .NET Framework 4.8, old-style VSTO project, no NuGet, C# 7.3 subset. Server and tests: net10.0-windows |
| Size | 3,435 lines / 1,279 code, 10 files | 2,850 lines / 1,786 code, 3 files |
| Tests | 49 (TUnit), some against the real clients | 94 (xunit), all pure |

**Three things stand out:**

- **OutlookAI spends more code on one client than BrowserAI spends on two.** 766 of its code lines are a JSON validator and splicer that BrowserAI does not need.
- **The two products already disagree about the same client.**
  - BrowserAI's `Expand` handles `${VAR}` and not `${VAR:-default}`, which Claude Code documents.
  - OutlookAI handles both, but expands an unset variable to empty where Claude Code keeps the literal text.
  - Both errors land on the safe side.
- **What a shared library must target.** net10.0 with `IsAotCompatible` covers BrowserAI and OutlookAI's server and tests. The net48 add-in can either link a dependency-free netstandard2.0 build or call a small exe.
- **What NativeAOT asks of it.** `JsonDocument` or a hand scanner (no reflection serialisation), no `Assembly.Location`, and BrowserAI's process rules: `ArgumentList`, `CreateNoWindow`, both streams read, a timed wait followed by a bare one, and `.exe` only.

**Who else would use it:** nobody today. Eight other repositories under `C:\Source\SixFive7` carry a `.mcp.json`, and they consume the old Playwright launcher. Another private project plans a remote `/mcp` endpoint with bearer tokens. StatusAI registers a status line, which is a different file.

## 5. Decision for the maintainer: directions for the shared library

**What stays product-specific under every direction:**
- which executable may be registered;
- the ownership rule;
- consent and timing;
- the command spelling per scope;
- the record;
- the wording;
- the hook budget.

**What the library would own:** the client table (how to find each client, verbs, flags, scope levers, exit codes and English needles, readers, restart hints), the process runner, the order of steps, and a structured result.

| | Direction | Moves / adds | Trade-off and risk |
|---|---|---|---|
| A | **No library; fix each product in place** | OutlookAI: about 60 to 120 code lines (`CLAUDE_CONFIG_DIR`, deregister on uninstall). BrowserAI: nothing | Cheapest. Client knowledge stays duplicated and has already drifted |
| B | **Extract inside BrowserAI first**: a product-neutral net10 project in the same repository, nothing published | Moves about 900 to 950 of 1,279 code lines; adds about 350 to 400; about 32 of 49 tests move, about 10 new | Proves the seam under BrowserAI's own gate. Rows in DECISIONS, ARCHITECTURE and HAZARDS that name moved symbols need correcting. OutlookAI gains nothing yet |
| C | **Own repository and NuGet package, command-line-driven only, net10 AOT-compatible, plus a small NativeAOT exe built from the same code** for hosts that cannot link it | B, plus about 250 code lines for the exe, repo and release scaffolding, an OutlookAI adapter of about 150 to 200 lines; OutlookAI deletes about 1,100 code lines and about 45 editor tests | One place for client drift. Costs a second release flow and a public API. Changes OutlookAI's behaviour (file edit to client command) |
| D | **C plus netstandard2.0 in-process and a file-edit engine** (OutlookAI's splicer generalised) for clients with no command line | C, plus about 770 code lines moved from `McpConfigEditor`, about 200 new, polyfills | Reaches Cursor, Claude Desktop and Windsurf. Reverses the DECISIONS rows for those clients; constrains the library to zero dependencies |
| E | **Adopt or contribute to an existing project** | kurir needs remove, status, ownership, Windows paths and process hygiene: roughly 600 to 900 lines of Rust upstream. add-mcp: a three-line fix | No code to own, but a dependency on an 18-day-old single-author project, or on Node and file edits. IvanMurzak's AgentConfig (36 files, 7,259 lines) would be a rewrite |

All line counts are estimates from reading, not from doing the split.

**Recommendation: B now, then C when OutlookAI is ready to migrate.** Keep it command-line-driven, Claude Code and Codex, user and project scope. Decide Q314 first: if Codex project scope is dropped, about 1,300 lines disappear before they would be moved. D only if a client without a command line is actually wanted. For E, at most file the `CLAUDE_CONFIG_DIR` issue on add-mcp and watch kurir.

**Usage sketch** (names are illustrative):

```csharp
var registrar = new McpRegistrar(new OwnedByInstallRoot(installRoot));   // the product's own rule
var server    = new McpServerSpec("browserai", serverExePath);            // Args and Env optional

// detect: reads only, starts no server
McpReading now = registrar.Detect(McpClient.ClaudeCode, McpScope.User, server.Name);
// now.ClientPath, now.Ownership (Absent, Ours, OursButStale, Foreign), now.Command, now.Unreadable

// register: safe to repeat, refuses a foreign or unreadable entry, reads back after writing
McpResult added  = registrar.Register(McpClient.Codex, McpScope.User, server, WhenOurs.Replace);
McpResult inRepo = registrar.Register(McpClient.ClaudeCode, McpScope.Project(repoRoot),
    server with { Command = "${LOCALAPPDATA}/BrowserAI.app/current/BrowserAI.Server.exe" }, WhenOurs.RepairIfStale);

// unregister: removes only what the rule calls ours
McpResult removed = registrar.Unregister(McpClient.ClaudeCode, McpScope.User, server.Name);

if (!added.Succeeded) log(added.Status, added.ClientSaid, added.ManualCommand);
```

For the add-in and the Inno uninstaller, the same three verbs as an exe with JSON on stdout:

```
mcpreg detect     --client claude-code --scope user --name outlookai --json
mcpreg register   --client claude-code --scope user --name outlookai --owned-path "<server exe>" -- "<command>"
mcpreg unregister --client claude-code --scope user --name outlookai --owned-path "<server exe>"
```

**New work versus moved code, plainly:**
- The library at step B is about 1,300 code lines, of which about 70% is existing BrowserAI code.
- The new part is the public surface, a structured result with a default renderer, the ownership seam, args and env support, and a read-back after every write.
- The real cost is an interface that fits two different policies, a green BrowserAI gate through the move, and a second release flow.

**Risks:**
- **An abstraction drawn from one implementation tends to fit only that one.** Write OutlookAI's adapter on paper before freezing the interface.
- **Client drift.** Claude Code shipped four releases in six days (2.1.283 to 2.1.286). The library reads its English and Codex's JSON. One shared place catches drift once, and one break reaches every product; BrowserAI's floating versions pull a library release into its next build.
- **House-rule friction.** BrowserAI's SPDX header, tree scans and append-only records apply to in-repo code. A NuGet package avoids the scans and adds an upstream to review.
- **OutlookAI's migration** changes behaviour for existing users: people with no `claude.exe` on PATH, and entries with hand-added keys.
- **Scope creep** toward hooks, skills and status lines, which is where kurir went in its first ten days.
- **Bounded downside.** If the clients converge on a registry within a year, the loss is a small library.

## 6. Not verified

- kurir, agent-install, the Sylphx kit, and the Blender, Serena and Context7 installers: read, not run. add-mcp 2.4.1 and APM 0.32.0: not re-run.
- Anything that needs a client invocation:
  - `claude mcp add-json` with a JSON argument on Windows;
  - whether `--scope user` stores `${VAR}` verbatim;
  - whether the VS Code extension's bundled `claude.exe` behaves like the standalone one;
  - exit codes on a failed write before 2.1.283.
- Whether Claude Code or Codex read Windows' on-device registry. I found no evidence; that is not proof of absence.
- Which files JetBrains edits. The VS Code, Copilot, Gemini and Cursor facts came through a summarising fetch.
- The effect of OutlookAI's user-scope re-rendering (below): read, not run.
- GitHub star and commit counts for kurir came from a summarised page.

## 7. Open questions for the maintainer

1. **Build the shared library, and when?**
   - *Primer:* nothing existing can be adopted, and BrowserAI holds most of the code. Sharing pays only once a second product uses it.
   - *Directions:* (a) stay separate and fix OutlookAI in place; (b) extract inside BrowserAI now, package later; (c) own repository and package straight away; (d) adopt or contribute to kurir.
   - *Recommendation:* (b), after Q314 is answered, then (c) when OutlookAI migrates.

2. **Client commands only, or also file editing?**
   - *Primer:* DECISIONS rejects writing a client's file. OutlookAI does it today. Cursor, Claude Desktop and Windsurf have no command to add a server.
   - *Directions:* (a) commands only, both products; (b) commands where they exist, file edits elsewhere; (c) leave OutlookAI on file edits and share only the reading and client table.
   - *Recommendation:* (a). Revisit when a client without a command line is wanted.

3. **How does OutlookAI's net48 add-in consume it?**
   - *Primer:* the add-in is .NET Framework 4.8 with no NuGet. Nothing runs at uninstall, so its entry stays behind today.
   - *Directions:* (a) a NativeAOT helper exe called by the add-in and by Inno's uninstall step; (b) verbs on its net10 server exe; (c) a dependency-free netstandard2.0 build in-process; (d) linked source in the C# 7.3 subset.
   - *Recommendation:* (a). It keeps the library on one target and fixes the uninstall gap. (b) fails when the .NET 10 runtime is missing.

4. **Packaging, visibility and licence.**
   - *Primer:* BrowserAI is under the bespoke FSL variant and OutlookAI is MIT. Both repositories are public. A private feed needs a token and breaks a public rebuild.
   - *Directions:* (a) public repository and nuget.org package under MIT; (b) private package; (c) git submodule or shared source; (d) publish from inside BrowserAI's repository.
   - *Recommendation:* (a), once step B has held under BrowserAI's gate. The licence of the extracted code is the maintainer's call as copyright holder.

5. **Which clients and scopes first?**
   - *Primer:* BrowserAI has all four combinations today. The first survey recommended dropping Codex project scope (Q314), which also removes the PATH writes.
   - *Directions:* (a) Claude Code user and project, Codex user only; (b) all four as now; (c) user scope only.
   - *Recommendation:* follow Q314's answer. The PATH code stays in BrowserAI either way.

6. **OutlookAI's "on plus foreign entry means repoint".**
   - *Primer:* BrowserAI never touches a foreign entry. OutlookAI replaces one after the user has explicitly turned the setting on.
   - *Directions:* (a) the library allows a take-over only when the caller states the user asked; (b) OutlookAI drops the repoint; (c) the library never takes over and OutlookAI keeps its own writer for that case.
   - *Recommendation:* (a).

7. **Anything upstream?**
   - *Primer:* add-mcp ignores `CLAUDE_CONFIG_DIR` and nobody has reported it. kurir shares the goal and lacks remove and detect. I posted nothing.
   - *Directions:* (a) file the add-mcp issue; (b) do nothing; (c) contribute remove and detect to kurir; (d) only watch kurir.
   - *Recommendation:* (a) and (d).

## 8. Side findings

**BrowserAI:**
- **No read-back after a write.** `McpRegistrar.Add` trusts exit 0. Per the changelog, Claude Code before 2.1.283 could report success when the file was not written.
- **Claude Code discovery misses the VS Code extension's bundled binary.** On this machine it is `~\.vscode\extensions\anthropic.claude-code-2.1.286-win32-x64\resources\native-binary\claude.exe`. A machine with only the extension reads as "client not found".
- **`McpRegistryView.Expand` lacks `${VAR:-default}`** (`McpRegistryView.cs:365-404`).
- **DECISIONS says the three Codex app-server config methods "are marked experimental".** The method table at rust-v0.150.0, 0.155.0, 0.157.0 and 0.159.3 puts no `#[experimental]` marker on `config/value/write`, `config/batchWrite` or `config/mcpServer/reload`, while 99 other entries carry one. I did not find what the row was based on.
- **Claude Code's precedence is local, project, user, plugin, connector.** The first survey's note about the unread local scope stands.

**OutlookAI:**
- **`CLAUDE_CONFIG_DIR` is not honoured**, in the add-in (`ClaudeConfigPath`) or the server (`HealthReporting.TryReadRegisteredCommand`).
- **Uninstall leaves the `outlookai` entry behind.** `[UninstallRun]` in `Installer.iss` only removes certificates.
- **The user-scope repair re-renders the whole `mcpServers` map** through `JavaScriptSerializer` (`McpRegistrationService.cs`, `TryBuildUpdatedConfig`). Other servers' entries, BrowserAI's included, are rewritten, and only their names are verified afterwards. The class comment says only its own value is re-rendered.

**This machine:**
- `~/.claude/.claude.json` exists beside `~/.claude.json`, which is what a run with `CLAUDE_CONFIG_DIR=~/.claude` leaves. I did not open it.
- The Codex app replacing `config.toml` at its own start is one observed case of a client rewriting the file a third-party editor would race.

## Files are in `C:\Source\SixFive7\BrowserAI\.work\zoomout\a-second-look\`

- `candidates.tsv` - every candidate against the nine requirements
- `registry-candidates.json` - the npm, crates.io and NuGet search results
- `source-commits-read.txt` - the 15 repositories and commits read
- `hashes\00-baseline.txt`, `hashes\99-final.txt`
- `evidence\` - 22 files quoted above (kurir, Blender, Serena, FastMCP, Python SDK, Context7, Docker, add-mcp, Sylphx, IvanMurzak)
- `web\` - downloaded docs, changelogs and feeds
- `hash-real.sh`, `fetch-src.sh`, `search-registries.mjs`

Code read:
- `C:\Source\SixFive7\BrowserAI\src\BrowserAI.Core\Registration\`
- `C:\Source\SixFive7\OutlookAI\Services\McpConfigEditor.cs`, `McpRegistrationDecision.cs`, `McpRegistrationService.cs`
- `C:\Source\SixFive7\OutlookAI\TaskPane\SettingsDialog.cs`
- `C:\Source\SixFive7\OutlookAI\McpServer\OutlookAI.Core\Services\HealthReporting.cs`
- `C:\Source\SixFive7\OutlookAI\Installer.iss`

Sources:
- [kurir](https://github.com/suiflex/kurir)
- [add-mcp](https://github.com/neon-solutions/add-mcp), [issue #122](https://github.com/neon-solutions/add-mcp/issues/122), [PR #124](https://github.com/neon-solutions/add-mcp/pull/124)
- [APM: install MCP servers](https://microsoft.github.io/apm/consumer/install-mcp-servers/)
- [FastMCP](https://github.com/PrefectHQ/fastmcp)
- [MCP Python SDK](https://github.com/modelcontextprotocol/python-sdk)
- [Blender MCP](https://github.com/ahujasid/blender-mcp)
- [Serena](https://github.com/oraios/serena)
- [Context7](https://github.com/upstash/context7)
- [Desktop Commander](https://github.com/wonderwhy-er/DesktopCommanderMCP)
- [Docker MCP gateway](https://github.com/docker/mcp-gateway)
- [IvanMurzak MCP-Plugin-dotnet](https://github.com/IvanMurzak/MCP-Plugin-dotnet)
- [Claude Code MCP docs](https://code.claude.com/docs/en/mcp), [managed MCP](https://code.claude.com/docs/en/managed-mcp), [changelog](https://raw.githubusercontent.com/anthropics/claude-code/main/CHANGELOG.md)
- [Codex MCP docs](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [Codex issue #2680](https://github.com/openai/codex/issues/2680)
- [SEP-2633](https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2633)
- [VS Code MCP servers](https://code.visualstudio.com/docs/copilot/customization/mcp-servers)
- [Copilot CLI MCP](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers)
- [Gemini CLI MCP](https://geminicli.com/docs/tools/mcp-server/)
- [Cursor install links](https://cursor.com/docs/context/mcp/install-links), [Cursor CLI MCP](https://cursor.com/docs/cli/mcp)
- [JetBrains MCP server](https://www.jetbrains.com/help/idea/mcp-server.html)
- [Figma local server](https://developers.figma.com/docs/figma-mcp-server/local-server-installation/)
- [Notion MCP](https://developers.notion.com/docs/get-started-with-mcp)
- [1Password MCP server](https://www.1password.dev/environments/mcp-server)
- [MCP servers on Windows](https://learn.microsoft.com/windows/ai/mcp/servers/mcp-server-overview), [odr.exe](https://learn.microsoft.com/windows/ai/mcp/odr-tool)
- [ODR internals](https://www.originhq.com/research/msft-odr-mcp)
- [NuGet MCP packages](https://learn.microsoft.com/nuget/concepts/nuget-mcp)
- [WinGet MCP server setup](https://learn.microsoft.com/windows/package-manager/winget/mcp-server-setup)