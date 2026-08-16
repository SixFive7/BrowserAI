<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Changelog

Everything notable that has happened to BrowserAI. The format is
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versions are
[semantic](https://semver.org/spec/v2.0.0.html) — three parts, because that is
[the shape `vpk` accepts](kb/packaging/velopack.md#nativeaot-hooks-and-vpk-output).

**A version here is a git tag, and nothing else.** The build derives its version
from the nearest `v*` tag ([`plan/stack.md`](plan/stack.md)); no number is typed
in a project file, in this file, or anywhere else. A section heading carries the
bare version and the tag carries the `v`.

**Entries are written as the work lands, never reconstructed at release time.**
[The pre-release checklist](plan/pre-release.md) refuses a release whose
`[Unreleased]` section is empty, and `build/Get-ReleaseNotes.ps1` is what
enforces it — a checklist satisfied by fifteen minutes of `git log` archaeology
has been satisfied in form only.

## [Unreleased]

### Added

- **The version comes from the git tag and is typed nowhere.** A build on
  `v0.1.0` is `0.1.0`; five commits later, with no new tag, it is
  `0.1.1-alpha.0.5`. Nothing to edit, forget, or get out of step with the tag,
  and an untagged build says so in its own version string — which is the whole
  of *never self-update from a build that is not a release*.
- **A build that cannot work out what version it is now fails.** A shallow clone
  or an unfetched tag makes the derivation fall back to `0.0.0`, and a binary
  that does not know what it is cannot be rolled back to or bisected against.
  The build refuses it and the message names the remedy, which is never
  guessable from the number itself.
- **This changelog**, and `build/Get-ReleaseNotes.ps1`, which extracts the
  unreleased section, refuses to produce release notes when it is empty, and
  stamps it under the version being cut.
- **The running build's version is the first line of every process log**, so
  *"which version was running when this happened"* is answerable for past runs
  as well as the current one. The process log survives an update, so a machine
  that updated itself records both versions and the moment it changed.

### Fixed

- **Every session's `lock.json` would have recorded the wrong version.** The
  build stamp was read from the assembly version, which the versioning mechanism
  fixes at `{Major}.0.0.0` by design — so the whole 0.x line would have written
  `0.0.0.0`, and the whole 1.x line `1.0.0.0`, into the one record a support
  question starts from. It now records the version the build was actually
  derived as. Measured on the artifact rather than reasoned about: at `v0.1.0`
  the assembly version really is `0.0.0.0`.

### Changed

- **One place answers "what version is this binary".** Two implementations
  disagreed — one read the informational version, the other the assembly
  version — and the version now has a single source that reads the informational
  version and never the assembly version.
- **The SDK is forbidden from decorating the version string**, repository-wide.
  Left on, it appends the 40-character commit sha to a version that has already
  been published, which is invisible until an update path that *matches*
  versions rather than comparing them starts downloading the binary it is
  already running, on a loop, forever.

## [0.1.0] - 2026-08-16

The first tagged version. *No artifact has been published from it* — packaging
and updates are still to come — so this section records what the product does at
the tag rather than what anyone has installed.

### Added

- **Browser automation for AI agents on Windows, as one MCP server that brings
  its own everything.** BrowserAI ships its own Node runtime and its own
  `@playwright/mcp`, downloads its own browser on first use, and proxies
  JSON-RPC to it. Nothing on the machine has to be installed, on `PATH`, or of
  any particular version.
- **Sessions that survive the agent that made them.** A session is a directory
  the caller names: the browser profile, its downloads, its artifacts, its log
  and the lock that owns it all live inside it. Close the agent, come back
  tomorrow, `browserai_resume` the same directory, and the cookies, the logins
  and the local storage are still there.
- **Three session modes, one table.** `headless`, `interactive` and `persistent`
  differ in what they may do, and the same table renders the server
  instructions, the tool descriptions, the refusal messages and the enforcement
  — so a mode cannot mean one thing to a model and another to the code.
- **Every tool call is judged against the mode of the session it names.**
  Deny-by-default in two dimensions: a tool nobody has classified is refused
  everywhere, and a mode with no policy row permits nothing.
- **Artifacts land inside the session, not wherever the browser felt like.**
  Screenshots, PDFs, downloads and traces are routed by type into folders under
  the session directory, never overwrite each other, and every answer says where
  the file went — as an absolute path and as a session-relative one.
- **A path that would escape the session is refused with a sentence saying
  why**, decided on the string and without touching the filesystem.
- **First-run browser provisioning that does not block the conversation.**
  `browserai_init` returns immediately and says a download is running; browser
  tools are refused with a recovery until it lands; the same child then
  navigates with no restart. A run that fails halfway removes its partial tree
  rather than leaving something that looks installed.
- **Firefox as well as Chromium**, including the profile-lock preflight that
  turns *"a modal dialog blocks startup for three minutes"* into a refusal in
  milliseconds.
- **Nothing is left running.** Every child and every browser it starts is
  created inside a job object, so killing BrowserAI — however abruptly — takes
  the whole tree with it. A stray browser from a previous crash is found on
  startup and ended only when the session directory that owns it is provably
  free.
- **Answers arrive exactly as the browser produced them.** The proxy splices the
  child's own bytes into the caller's frame rather than re-serialising, so
  escapes, numeric form, key order, unknown fields and unknown content types all
  survive unchanged.
- **Failures say what happened and what to do next.** One error catalogue,
  written for the model that has to act on it, with every row provoked by a real
  condition rather than asserted to exist.
- **The log is a log.** Everything outside a session goes to one rolling process
  log that survives updates; everything inside one goes to a log beside that
  session's lock. Nothing anywhere can write to the protocol channel by
  accident.
- **Upstream cannot move underneath it silently.** Four snapshots of
  `@playwright/mcp`'s surface are regenerated from the resolved package on every
  build and diffed against committed copies, and the build fails with the diff
  itself when anything moves.
- **Every dependency floats to latest and the build freezes what it resolved**,
  so the resolved set is recorded beside the artifact rather than remembered.
