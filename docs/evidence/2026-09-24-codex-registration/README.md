<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24 -- how Codex is told about an MCP server, and what each path writes

**What this is.** The Q258 research: every supported way to register a local
stdio MCP server with Codex, what each one writes and where, what the plugin and
marketplace system can and cannot carry, and what the app-server protocol exposes.
**87 files, 569,373 bytes.** Taken at codex-cli **0.155.0-alpha.9.2** (the
desktop-app install, which is not on `PATH`) on Windows 10.0.26200.

⚠️ **`CODEX_HOME` was forced at a scratch directory for every call.** The
maintainer's real `~/.codex` was never written, and the premise that started this
research -- that editing Codex's own files might be unsupported -- was corrected
here: `~/.codex/sessions/` holds transcripts, registration touches
`config.toml`, and OpenAI documents that file as one a person edits.

## Cited by

| Record | What it takes from here |
|---|---|
| [`DECISIONS.md`](../../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client) | Q258: user scope through `codex mcp add`, project scope through the `CODEX_HOME` lever, the app-server protocol as the migration target, the plugin route skipped, TOML writing still rejected |
| [kb: protocol](../../../kb/mcp/protocol.md#registering-with-codex-and-what-its-startup-timeout-costs----measured-2026-09-24) | The discovery order, the option set, the idempotence, the scope behaviour and the trust model |
| [re-verification index](../../../kb/re-verification.md) | Row 148, keyed on the codex-cli version |
| `CodexRegistrationTests` | The argument shapes and the refusal it holds against a double |

## What is here

| Path | What it holds |
|---|---|
| `written/` | **The load-bearing part: what each path actually wrote.** One file per scratch home and project, flattened so the origin is in the name -- `codex-home*__config.toml` for the nine user-scope arms, `proj__.codex__config.toml` and `e2e__.codex__config.toml` for the project-scope arms, and the plugin caches for the marketplace arms, including the portable `plugin.json` and `mcp.json` pair that does **not** work in this build beside the `.codex-plugin/` form that does |
| `mcp-cli.md.txt`, `mcp-app.md.txt`, `mcp-ide.md.txt` | OpenAI's own documentation of the three registration surfaces, fetched 2026-09-23 |
| `config-reference.md.txt`, `approvals.md.txt`, `connectors.md.txt`, `llms.txt` | The configuration reference, the approval model, the connector documentation, and the documentation index |
| `docs_plugins.md.txt`, `docs_build-plugins.md.txt`, `plugins_build_plugins.md.txt`, `plugins_deploy_submission.md.txt`, `plugin-mgmt.md.txt` | The plugin and marketplace documentation, including the submission requirements that close the public directory to a local stdio server |
| `q-docs_*.md.txt` | The quickstart, CLI and overview pages, fetched for the same question a second time |
| `mcp.schema.json` | The `.mcp.json` schema a plugin's server list is written against |
| `schema/` | The app-server protocol schema: `ClientRequest.json`, which is the union naming every method a host may call and where `config/mcpServer/reload` lives, plus the two `McpServer*` shapes |

## What was cut, and what it was cut from

⚠️ **The generated protocol schema is a manifest plus three files, not all 39.**
[`schema/schema-index.sha256`](schema/schema-index.sha256) carries the SHA-256 and
the byte count of every file the generator produced, so the set is enumerable and
each one is checkable; the two aggregate documents
(`codex_app_server_protocol.schemas.json` 703,090 bytes and its `.v2` companion
602,439 bytes) and `ServerNotification.json` are **not** copied, because the whole
set is reproduced by one documented command:
`codex app-server generate-json-schema --out <DIR>`.

⚠️ **`mcp-cli.html` is dropped and its Markdown rendering is kept.** 457,900 bytes
of the same page.

⚠️ **Every fetched document here is named `*.md.txt` and not `*.md`, and the
bytes are unchanged.** They are somebody else's Markdown, full of links and images
that resolve on `developers.openai.com` and nowhere in this repository, and
[the link scan](../../../TESTING.md) reads every `.md` in the tree. Renaming the
capture is the honest fix; excluding a directory from the scan would have hidden
this repository's own prose along with it.

⚠️ **The scratch homes' working state is absent.** Nine `CODEX_HOME` directories,
six marketplace clones and two scratch git repositories were created; what is here
is the file each one **wrote**, which is the thing being measured. The plugin store
clone alone was 1.4 GB, and it is `github.com/openai/plugins` at its default
branch, cited by URL.
