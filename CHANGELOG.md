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
  that updated itself records both versions and the moment it changed. *Now
  demonstrated rather than asserted: after a real update and a real rollback, one
  log file carries `BrowserAI 0.9.0 started` and `BrowserAI 0.9.1 started`.*
- **Silent background self-update, per-user, with a rollback that works.**
  BrowserAI installs to `%LocalAppData%` with no elevation, checks its feed off
  the message loop so a tool call stays answerable while a package is in flight,
  and swaps itself between sessions — in normal use there is no *restart to
  apply* prompt at all. A BrowserAI-only release is a **97,216-byte** delta
  against a 46.8 MiB full package.
- **An update is never applied while another BrowserAI is running.** Applying
  terminates every process under the install root, which at the concurrency this
  is designed for is every other agent's browser. The last instance to exit
  applies what the others staged.
- **Rollback is publishable as well as acceptable.** The client allows a version
  downgrade and the release script permits *monotonic **or** an explicit rollback
  republish* — both halves, because either alone is a rollback that can be
  accepted but never emitted, or emitted and never accepted.
- **`build/New-Release.ps1`**, which publishes, packs and refuses: on a `vpk`
  that does not match the Velopack library, on a version that is `0.0.0` or
  carries build metadata, on a non-monotonic release nobody stated, on anything
  in ILC's raw output, and on this build's own version string appearing in
  decorated form anywhere in the linked binary.
- **Velopack's MIT licence and a trademark disclaimer now ship inside the
  package**, in `THIRD-PARTY-NOTICES.txt` beside the binary. Both were absent
  from an otherwise releasable package: Velopack is compiled *into*
  `BrowserAI.exe`, so its licence never leaves the NuGet cache, and no upstream
  file carries a trademark disclaimer at all. The licence is copied from the
  commit the resolved package records as its source, never transcribed, and a
  Velopack bump is a red build until it has been re-fetched.
- **A release now records the resolved set beside its artifact**, emitted rather
  than assembled by hand: the three `packages.lock.json`, the payload's
  `package-lock.json`, `payload.json`, `browsers.json`, and a `manifest.json`
  stating the version, the tag, the package's SHA-256 and the resolved version
  read back out of each copy.

### Fixed

- **Two licences that had to travel with the binary were not travelling.**
  `ModelContextProtocol` and `ModelContextProtocol.Core` are Apache-2.0 and
  seventeen `Microsoft.Extensions.*` assemblies are MIT; all nineteen are
  compiled into `BrowserAI.exe` exactly as Velopack is, and a NuGet package's
  licence stays in the machine's package cache — it is never copied to a publish
  output, so *linked in* and *its notice ships* are independent facts and the
  second was false for both. Apache-2.0 §4(a) requires a copy of the licence to
  reach every recipient, which is stricter than MIT's notice clause rather than
  looser. All of it now ships in `THIRD-PARTY-NOTICES.txt` beside the binary.
  Upstream's MCP `LICENSE` turned out to grant **three** licences rather than
  one — Apache-2.0, MIT for contributions whose authors never consented to
  relicensing, and CC-BY-4.0 for documentation — so it is reproduced whole, and
  its Apache half **ends at *END OF TERMS AND CONDITIONS* and omits the appendix
  its own §4 points at**, which is upstream's file as published and is left
  unaltered. **The `Microsoft.Extensions.*` list is derived from
  `packages.lock.json` rather than typed**, so a package entering the closure on
  a later bump is a red build here rather than a licence nobody noticed had
  arrived.
- **A suite run that exercised nothing reported exactly what a real one
  reports.** With the whole publish directory moved aside — no binary, no
  browser ever started — the suite returned `329 total · 328 succeeded ·
  1 skipped · exit 0`, character for character a healthy run's summary, because
  thirty-five guards returned early after asserting something weaker and every
  one reported as a pass. They now report as **skipped**, so the run's own
  counts differ; every run ends with a block naming what it did and did not
  exercise; and with `BROWSERAI_RELEASE_RUN=1` a missing capability is a
  **failure** naming the command that produces it. This was the project's
  founding failure class living inside its own release gate.

- **37 MB of every release was a zip nobody reads.** The published artifact
  carried `payload\.cache\node-<ver>-win-x64.zip`, the download cache the payload
  build keeps so a re-run does not re-fetch Node. Excluding it took the full
  package from **85,348,009** to **49,043,498 bytes** — 42.5% of every download,
  for a file that is never opened at runtime and compresses to nothing because it
  is already compressed.

- **`UseSystemResourceKeys` is asserted rather than merely set.** It strips the
  framework's exception message strings, and this product's error text is read
  by a model deciding what to do next. The property was correct and guarded by
  nothing, which is the state a size optimisation walks into.

- **Every session's `lock.json` would have recorded the wrong version.** The
  build stamp was read from the assembly version, which the versioning mechanism
  fixes at `{Major}.0.0.0` by design — so the whole 0.x line would have written
  `0.0.0.0`, and the whole 1.x line `1.0.0.0`, into the one record a support
  question starts from. It now records the version the build was actually
  derived as. Measured on the artifact rather than reasoned about: at `v0.1.0`
  the assembly version really is `0.0.0.0`.

### Changed

- **The stray-browser sweep now has one trigger instead of two.** The logon
  scheduled task is dropped: it cannot be registered without elevation on a
  UAC-filtered administrator token, and a per-user install has no elevation to
  offer. BrowserAI's own startup sweep already covers the case that matters — a
  stray browser matters when something is about to contend for its profile lock,
  and that is exactly when a client starts.
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
