<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

I did not write `.work/zoomout/a/REPORT.md`: the harness refused it, saying a sub-agent must return its findings as text. The full report is below, so the root can save it verbatim. Evidence is under `C:\Source\SixFive7\BrowserAI\.work\zoomout\a\`.

**Two side effects I caused, both undone:**

1. **Two "Connect to GitHub" windows appeared on the maintainer's screen**, one for about 8 minutes (from 06:07:15 local) and one for about 2 minutes (from 06:12:54). APM's `apm install -g` started Git Credential Manager through `git credential-manager get`. I stopped the two GCM processes (pids 96660 and 106036) and the hung APM process (pid 100216). Each was checked by pid, creation time and path first, and nothing else was stopped. Once, I also ran a read-only `tasklist` listing filtered by image name; nothing was acted on from it.
2. **HKCU\Environment\Path was changed, and is now back to its exact original.** `dotnet package search` added `C:/Source/SixFive7/BrowserAI/.work/zoomout/a/dotnet-home\.dotnet\tools` to it: its first-run step does that in a fresh `DOTNET_CLI_HOME`. My hash check caught it. I rebuilt the original value, checked it against the baseline hash before writing it back, then broadcast the settings change. The hash matched again afterwards.

Every other real file hashes the same at baseline and at the end. The one exception is `~/.codex/logs_2.sqlite-wal`, which the running Codex desktop app writes on its own: five sandboxed `codex mcp list` calls left it untouched. `git status` is clean. Details are in `notes\INCIDENTS.md`.

## 1. The answer

**No. There is no library or tool BrowserAI should adopt for this.** Several good ones exist; the problem is what they cover.

- **The best is add-mcp 2.4.0** (Apache-2.0, 24 clients, a TypeScript SDK with idempotent add, remove and list). It would replace only the code that drives the two client command lines, about **710 of the 3,435 lines** of registration code. After adding a small script and the Node dependency, the net cut is about 500 lines, roughly 15%.
- **The rest of the code is BrowserAI's own policy**, which no registrar has:
  - which executable may be registered (the stub refusal and the console-binary check);
  - never touching another install's `browserai` entry;
  - what install, update and uninstall may each do;
  - the record file.
- **Adopting a tool reverses a settled decision.** Today the client's own command line writes the client's file. With add-mcp, a third party's library would edit that file directly, which DECISIONS rejected on 2026-08-16.

**What add-mcp did in the sandbox that the client command lines do not:**
- It ignores `CLAUDE_CONFIG_DIR`, so it writes a file Claude Code never reads.
- It rewrites a Codex `config.toml` from scratch, dropping every comment, without an atomic write.
- It refuses a valid TOML 1.0 file.
- Its command line turns both of BrowserAI's project spellings into `npx -y ...`.
- Its list and remove skip Claude Code when `~/.claude` does not exist. The remove left the entry behind and still reported success.

**The quirks in the brief are client runtime behaviour, not registration**, so no registrar touches them. These are: Codex expands no variables; Codex looks up a bare name in the PATH it started with; Claude Code relaunches a server but never re-reads its tool list; Codex never relaunches. I checked the Codex source at rust-v0.157.0: the two files involved are byte-identical to 0.155.0-alpha.9.2, and openai/codex#2680 is still open.

**The least complex path:** keep the clients' own command lines as the only writers, and cut the surface where the quirks pile up: Codex project scope.

## 2. What BrowserAI does today (checked in the code)

| Client and scope | How it writes | How it reads, to judge ownership |
|---|---|---|
| Claude Code, user | `claude.exe mcp add browserai --scope user -- <path>`, started directly (no `cmd.exe`), 10 s budget. A reinstall removes first, because `add` fails on an existing name. Uninstall runs `mcp remove ... --scope user` | Parses `%CLAUDE_CONFIG_DIR%` or `%USERPROFILE%\.claude.json`, `mcpServers.browserai.command`. It avoids `claude mcp get`, which starts servers |
| Claude Code, project | The same commands with `--scope project`, run inside the repository. The command is the portable `${LOCALAPPDATA}/BrowserAI.app/current/BrowserAI.Server.exe` when that points at this install | Parses `<repo>\.mcp.json`, expanding `${VAR}` for the comparison only |
| Codex, user | `codex.exe mcp add browserai -- <path>` and `mcp remove browserai` | `codex mcp list --json`; it never parses the TOML |
| Codex, project | The same commands with `CODEX_HOME=<repo>\.codex` (folder created first). The command is the bare name `BrowserAI.Server.exe` (Q294 b). The `tmp\arg0` leftovers are removed while empty | The same list; a bare name is resolved through the registry PATH, the way Codex would |
| Finding Codex | PATH, then `~\.local\bin`, then the desktop app's `chrome-native-hosts-v2.json` (`codexCliPath`), then `%APPDATA%\npm` | |

**The 15 files in `src\BrowserAI.Core\Registration`** (lines / code lines, where code means neither blank nor comment; 5,063 / 2,030 in total):

| Category | Files | Lines | Code |
|---|---|---:|---:|
| MCP registration | `ClientCommandLine` 271/132, `CodexRegistration` 411/124, `CodexRegistryView` 345/155, `IRegistrationCommand` 118/18, `McpClientRegistration` 333/43, `McpRegistrar` 907/417, `McpRegistryView` 405/158, `RegistrationClient` 269/98, `RegistrationRecord` 181/78, `RegistrationTarget` 195/56 | 3,435 | 1,279 |
| Velopack hooks | `HookRegistration` 408/153 (runs every install-time step) | 408 | 153 |
| Logon task | `LogonTasks` 289/165, `SignInTask` 213/116 | 502 | 281 |
| PATH | `UserPath` 341/173 (exists only for the Codex bare-name project entry) | 341 | 173 |
| Data-root disposal | `DataRootDisposal` 377/144 | 377 | 144 |

Half of all lines are comments.

**The app side:**
- `ClientState.cs` (313 lines) keeps one reading per client plus the five per-client action checks.
- `AppState.cs` (139) reads every client.
- `ConfigurationDialog.cs` (448) turns that into five links per client: 12 command links in the "everything registered" rendering.
- `Program.cs` holds about 180 lines of registration click handlers.

**Tests:** `RegistrationTests` 2,104, `CodexRegistrationTests` 740, `FakeClientCommandLine` 318, `UserPathTests` 291, `ScratchUserPath` 53, plus parts of `ConfigurationAppTests`, `RealInstallerTests` and `InstallerHandoffTests`.

**The "index" the maintainer asked about in Q307 is not stored anywhere.** The "this folder ... registers" lines come from `ClientState.NearestProject`, which walks up from the window's own working directory every time it reads. The rendering was started inside `p\05\myrepo`; opened from the Start Menu, the walk finds nothing.

**What a library could replace** (lines per member, doc comments included):
- `CodexRegistration`: 222 (command-line verbs, checks, discovery).
- `CodexRegistryView`: about 170 of 212 (reading the list).
- `McpClientRegistration`: 113 (verbs, the "already exists" and "not found" text checks).
- `McpRegistrar`: 84 (leftover cleanup, remove-before-add).
- `RegistrationClient`: 40 (command-line members).
- `ClientCommandLine`: 79 (finding the client executables).

That is about 710 lines. Everything else stays under any registrar.

## 3. Survey (versions and dates looked up 2026-09-25)

**Tools that register a server with many clients**

| Tool | Clients and scopes | Remove / detect | How it writes | License | Maintenance | Runtime |
|---|---|---|---|---|---|---|
| **add-mcp** 2.4.0 | 24 agents; Claude Code user and project; Codex via `$CODEX_HOME` and `.codex` | SDK removes by exact name; the command line removes by name *substring*; list only covers "detected" agents | Edits files directly: jsonc for JSON; @iarna/toml 2.2.5 (from 2020, TOML 0.5) re-serializes TOML; plain `writeFileSync` | Apache-2.0 | v2.4.0 2026-09-08; 35 commits in 90 days; about 175 of 186 commits by one author; one npm maintainer; 8 open issues, 21 open PRs | Node 18 or later, 11 packages, 2.7 MB |
| **APM** (microsoft/apm) 0.31.0 | Claude Code (honours `CLAUDE_CONFIG_DIR`); Codex (honours `CODEX_HOME`); project writes only if `.claude/` or `.codex/` exists; plus 7 more | Removal only by editing `~/.apm/apm.yml` and reinstalling; its lockfile knows what it wrote | Rewrites the whole `~/.claude.json`; Codex via tomlkit (keeps comments); atomic writes | MIT | Released 2026-09-15, weekly; 169 open issues; still pre-1.0 | Python, a 32 MB PyInstaller folder |
| install-mcp 1.10.2 | Claude Code, Codex (user only), about 20 more | No remove | Edits files; writes a fresh file when parsing fails | MIT | No commits since 2026-04-16 | Node |
| mcp-add 0.2.4 | Codex user only (ignores `CODEX_HOME`) | No remove | Replaces the whole config when it can't read it | Says MIT, but no LICENSE file | Last release 2026-03-17 | Node |
| Smithery CLI (`smithery` 1.2.0) | Uses `claude mcp add` / `codex mcp add` for those two | Remove "not supported" for them; the install commands are deprecated | Client command lines | AGPL-3.0 | Project moved to a hosted product | Node |
| mcpm.sh 2.15.0 | User scope only; ignores `CLAUDE_CONFIG_DIR` and `CODEX_HOME` | Yes | Writes `mcpm run` proxy entries; renames a corrupt `~/.claude.json` and starts empty | MIT | No commits in 90 days | Python |

Not fits, briefly:
- **mcp-get**: archived. **MCP Router**: archived 2026-09-18.
- **rulesync / ruler**: they generate every agent's config from one source and own the whole server list.
- **Docker MCP Toolkit / ToolHive / 1MCP**: each registers itself as a gateway in front of other servers.
- **NuGet**: DevJoy.Mcp.Config and McpInstallGenerator do not cover Claude Code or Codex.

**Each client's own channel:**
- `claude mcp` has local, project and user scopes. `add` fails on an existing name.
- `codex mcp` still has no scope flag at 0.157.0. Its writer replaces every `[mcp_servers.*]` table.
- `gemini mcp add --scope user|project`.
- `copilot mcp add/remove`. It reads the same `.mcp.json` as Claude Code.
- `code --add-mcp` writes the VS Code user profile and has no remove.
- VS Code and Cursor install links need the person to confirm in the app.
- Claude Code plugins would rename every tool to `mcp__plugin_<plugin>_<server>__...`.

**Registry, bundles and standards:**
- **The official MCP Registry / `server.json`** only knows npm, pypi, nuget, oci, cargo and mcpb packages. I found no evidence that Claude Code or Codex installs from it.
- **`.mcpb` bundles** are installed by Claude Desktop only.
- **Windows' on-device agent registry (`odr.exe`)** needs Windows build 26220.7262 or later; this machine is 26200.9550 and has no `odr.exe`. It also runs servers in a separate, restricted session, which would break BrowserAI.
- **SEP-2633**, a shared config format, is a draft pull request, open since 2026-04-22.

## 4. Sandbox results

**Setup:**
- USERPROFILE, HOME, APPDATA, LOCALAPPDATA, XDG_CONFIG_HOME, TEMP, CLAUDE_CONFIG_DIR and CODEX_HOME were all redirected. The fake home was named `Zoë O'Brien`, to test a space, an apostrophe and a non-ASCII letter.
- The seed config had comments, trust tables and a teammate's project entries.
- `claude.exe` 2.1.282 and `codex.exe` 0.155.0-alpha.9.2 were copied into scratch and run from there.
- No `claude mcp get` or `list`, since those start servers.

**The clients' own command lines** (what BrowserAI uses now, `sandbox\t1-native`):
- Claude, user scope: add worked in 365 ms. Adding again failed with "already exists". Removing twice failed on the second with "No MCP server named". The project scope behaved the same way and kept `${LOCALAPPDATA}` exactly as written.
- Codex: add and remove succeed every time, in 79 to 128 ms. A path containing an apostrophe is stored as `'''...'''` and reads back correctly.
- **Codex's own add loses comments in the MCP section.** The comment above `[mcp_servers.other]`, a comment at the end of a line and the project file's header comment all went, and `startup_timeout_sec = 20` became `20.0`. Comments at the top of the file survived.
- **Claude Code resets a broken config file.** When `~/.claude.json` was not valid JSON (by accident), it moved it to `backups\.claude.json.corrupted.<ms>` and started a fresh file.

**add-mcp** (`t2`, `t3`, `t4`, `t8`, `t9`):
- **It worked:** it writes correct JSON and TOML escaping, and adding the same entry twice leaves the files byte-identical.
- **Codex config:** all comments lost and the file reformatted (issue #122 and PR #124, both opened 2026-09-23 and still open).
- **`CLAUDE_CONFIG_DIR`:** ignored. Claude Code then reported "No MCP server named browserai".
- **With no `~\.claude` folder:** its list returned nothing for Claude although the file held two entries, and the remove left the Claude entry in place.
- **Project spellings via its command line:** both became `npx -y <spelling>`.
- **Another install's `browserai` entry:** replaced silently, including that install's own arguments and environment.
- **A config with a mixed-type array** (valid TOML 1.0, which Codex accepts): refused with `success:false`, and the file was left alone.

**APM** (`t5`, `t6`):
- **A path with a space was refused on the command line.** It works only through a hand-written `~/.apm/apm.yml`.
- **It opened the GitHub sign-in windows** until I disabled git's credential helpers and passed `--no-policy`.
- **What it wrote:**
  - Claude: the whole file was rewritten; `ë` became `\u00eb`.
  - Codex: comments kept, but it added `id = ""` and an empty `env` table. Codex still reads the file.
- **On every install it looked the name up at `api.mcp.github.com`.**
- **Another install's `browserai` entry** was left alone but reported as "already configured".
- **Project scope needs `apm init`**, which puts `apm.yml` and a lockfile in the person's repository.

**install-mcp 1.10.2** (`t7`, `t8`), tested as a negative control:
- It split the path at the space in both clients and reported success.
- On the TOML 1.0 file it **wiped the whole Codex config**, leaving one broken `browserai` table.

## 5. Directions

| # | Direction | Removes | Costs | Verdict |
|---|---|---|---|---|
| 1 | Adopt add-mcp through the payload's `node.exe` | About 500 lines net | Node in the installer hooks; one maintainer; a pending breaking change to its API defaults (PR #43, open since 2026-05-23); direct, non-atomic edits of a live `~/.claude.json`; lost comments; no `CLAUDE_CONFIG_DIR`; its reader can't be trusted | No |
| 2 | Adopt APM | About the same | A Python dependency; a user manifest BrowserAI would have to edit; files in people's repos; the sign-in prompt; a network lookup per install; reports another install's entry as success | No |
| 3 | Keep the command lines and both scopes; shrink the window (Q311 a) | About 120 lines of source plus tests (`NearestProject`, the walk's link and handler); 12 links become 8 | Keeps the PATH writes and Q304 | Yes, if the Codex project request stands |
| 4 | Keep the command lines; drop Codex project scope, keep Claude's | About 1,300 lines with tests: `UserPath` 341, `EnvironmentBroadcast` 55, `ChangeThePath` 24, `CodexRegistration` project members 75, `CodexRegistryView.ReadProject` 37 plus the bare-name branch, `RemoveResidue` 61, `UserPathTests` 291, `ScratchUserPath` 53, about 6 test arms. Also every HKCU PATH write, the release gate's PATH check, and Q304 | Reverses the Codex half of Q258 and Q294 b | **Recommended** |
| 5 | User scope only, both clients | About 2,200 lines with tests (direction 4 plus `ApplyToProject` and helpers 201, `RegistrationClient` project members ~105, `McpClientRegistration` 79, `McpRegistryView` 65, `ClientState` 84, dialog 41, `Program` 124). Q311 goes away | Reverses Q258 and the README's per-project registration | The largest cut |
| 6 | Publish `server.json` / `.mcpb` | Nothing | Neither target client installs from them | At most a later item for discoverability |

**Why project entries add so little here.** Both project spellings only work on a machine where BrowserAI is installed, and every install already registers BrowserAI at user scope for both clients. So a committed entry matters only to someone who removed the user-scope entry, or whose install could not find the client. For Codex it also needs a trusted project and a Codex started after the install.

**Recommendation:** direction 4, using direction 3's single folder pair for the Claude project actions. If the maintainer wants to keep Codex project scope, take direction 3 and add the restart advice from Q304.

## 6. Risks

- **A third-party writer racing a running Claude Code** on `~/.claude.json`: a half-written file makes Claude Code reset to a backup. I measured the reset, not the race.
- **Supply chain.** add-mcp has one maintainer. install-mcp pulls in tar@6.2.1, which npm flags as vulnerable. Under this repository's everything-floats rule, a registrar arrives new with every build.
- **Tools that rewrite other servers' entries.** Every file-editing registrar rewrites at least the whole server list. add-mcp's command-line remove matches by substring.
- **Windows paths.**
  - APM refuses a space.
  - install-mcp splits at the space, and its unreleased code wraps every command in `cmd /c`.
  - add-mcp's command line mangles bare names and `${VAR}`.
  - The client command lines handled `Zoë O'Brien` correctly.
- **Slow to follow client changes.** add-mcp's Windows Store Claude Desktop path has been broken since 2026-05-28 (#50). Its space-in-path fix took six weeks (#29). Nothing is filed about `CLAUDE_CONFIG_DIR`.

## 7. Not verified

- Whether Claude Code launches an add-mcp entry without `type`: not launched, because `get` and `list` start servers.
- Whether Claude Code locks `~/.claude.json`: closed source.
- An actual write race with a running Claude Code.
- codex-cli 0.157.0: read in source only; everything run was 0.155.0-alpha.9.2, the build installed here.
- APM's project writes after `apm init`.
- `odr.exe`: not present on this build.
- Whether any client other than VS Code and Visual Studio installs from the MCP Registry.
- Whether Copilot CLI expands `${LOCALAPPDATA}`.
- One documentation fetch claimed Claude Code's Windows config folder is `%APPDATA%\Claude`. This machine uses `%USERPROFILE%\.claude`, so I did not rely on it.

## 8. Open questions for the maintainer

1. **Q-A1: keep Codex project scope?**
   - *Background:* Codex expands no variables, so its project entry must be the bare `BrowserAI.Server.exe`. That forces the installer to write the user PATH. A Codex started before the install never sees it (Q304), and the entry only works in trusted projects. Wherever it does work, the installer has already registered BrowserAI with Codex at user scope.
   - *Options:* (a) drop Codex project scope, keep Claude's; (b) keep it and have the window tell people to restart Codex; (c) write the absolute path (right on one machine, wrong for teammates); (d) drop project scope for both clients.
   - *Recommendation:* (a).
2. **Q-A2: Q304, if Codex project scope stays.**
   - *Background:* 3 of 3 runs showed no code can change a running Codex's PATH.
   - *Options:* (a) the window says to restart a Codex that was running before the install, with the install time; (b) say nothing; (c) use `cmd /c %LOCALAPPDATA%\...` as the command (unmeasured, and it brings back the `cmd.exe` quoting problems STACK.md's first SDK deviation avoids).
   - *Recommendation:* (a).
3. **Q-A3: Q311, the project actions in the window.**
   - *Background:* each client has three project links. One of them is the upward folder walk he read as an index.
   - *Options:* (a) one "Register in a folder..." and one "Remove from a folder..." covering every client, each saying what it did or that nothing was there; (b) per-client folder pickers without the walk; (c) keep all of them.
   - *Recommendation:* (a). The registrar already refuses other installs' entries, so a wrong folder costs one sentence.
4. **Q-A4: use a registrar later, to add more clients?**
   - *Background:* a registrar's value is breadth. BrowserAI's real cost per client is measuring runtime behaviour; writing the entry is the cheap part.
   - *Options:* (a) add clients through their own command lines (`gemini mcp` and `copilot mcp` exist); (b) use add-mcp's SDK only for clients without a command line, behind BrowserAI's ownership check; (c) adopt it for all clients.
   - *Recommendation:* (a) now, (b) only if a client without a command line is ever wanted.

## 9. Side findings

- **The window can overstate a Claude registration.** BrowserAI's reader ignores Claude Code's **local** scope (`projects[<path>].mcpServers`), which outranks user scope. A hand-added local `browserai` in one repository would win there while the window says "Registered for all your Claude Code projects".
- **Copilot CLI shares the `.mcp.json`** that BrowserAI writes for Claude Code.
- **The client command lines are fast.** `claude mcp add/remove` took 242 to 365 ms at 2.1.282 (the kb has 613 to 671 ms at 2.1.233), and Codex took 79 to 128 ms. The tightest installer hook allows 15 s.

## 10. Sources (all read 2026-09-25)

**Registrar candidates**
- add-mcp: github.com/neon-solutions/add-mcp, commit 37c8246; issues #122, #124, #50, #114, #43, #29.
- APM: github.com/microsoft/apm, commit c5fea9e, v0.31.0 zip sha256 a5b2b463...; microsoft.github.io/apm/consumer/install-mcp-servers/.
- install-mcp: commit c2a97fb, npm 1.10.2.
- mcp-add: commit 9df8dd3.
- Smithery CLI: github.com/arcadeai-labs/smithery-cli, commit 407ac3b.
- mcpm.sh: commit 6a92e54.
- rulesync bd5a659; ruler 0fa2cae.
- Docker: docker/mcp-gateway a34df45 (`pkg/client`) and docs.docker.com/reference/cli/docker/mcp/client/connect/.
- ToolHive: docs.stacklok.com/toolhive/reference/client-compatibility.

**Clients and their channels**
- Claude Code: code.claude.com/docs/en/mcp.
- Codex docs: learn.chatgpt.com/docs/extend/mcp?surface=cli.
- Codex source at rust-v0.157.0: `mcp_cmd.rs`, `config/edit.rs`, `program_resolver.rs`, `utils.rs`; issues #2680, #16899, #4955.
- Gemini CLI: geminicli.com/docs/tools/mcp-server/.
- Copilot CLI: docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers.
- VS Code: code.visualstudio.com/docs/copilot/customization/mcp-servers.
- Cursor: cursor.com/docs/context/mcp/install-links.

**Registry, bundles, standards**
- MCP Registry: `generic-server-json.md`.
- MCPB: github.com/modelcontextprotocol/mcpb.
- SEP-2633: modelcontextprotocol PR #2633.
- Windows ODR: learn.microsoft.com/windows/ai/mcp/ (overview, odr-tool, containment, mcpb).

**Package registries**
- npm: `@anthropic-ai/claude-code` 2.1.282, `@openai/codex` 0.157.0, `@google/gemini-cli` 0.61.0, `@github/copilot` 1.0.88, `@anthropic-ai/mcpb` 2.1.2, `@iarna/toml` 2.2.5 (published 2020-04-22).
- nuget.org: McpInstallGenerator 1.1.1, DevJoy.Mcp.Config 0.7.0.

## 11. Evidence in `C:\Source\SixFive7\BrowserAI\.work\zoomout\a\`

- `hashes\00-baseline.txt` through `09-final-after-cleanup.txt`
- `notes\INCIDENTS.md`, `notes\repo-meta-2026-09-25.tsv`, `notes\maintenance-2026-09-25.txt`, `notes\source-commits-read.txt`, `notes\codex-0.157.0-mcp_cmd.rs`, `notes\codex-0.157.0-config-edit.rs`
- `sandbox\t1-native` (the client command-line control)
- `sandbox\t2`/`t3`/`t4`/`t8`/`t9` (add-mcp), `t5`/`t6` (APM), `t7` (install-mcp). Each has `runlog.txt`, `out-*.txt` and `snap\<step>\` copies of every config file.
- Test scripts: `sandbox-env.sh`, `seed.sh`, `snap.sh`, `hash-real.sh`, `sdk-test.mjs`, `toml-compat.cjs`, `foreign.cjs`, `member-lines.cjs`

I deleted the copied client binaries (534 MB), the source clones, the npm cache and the unpacked packages; they can be rebuilt from the versions and commits above. No process started from the scratch folder is still running.