<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Architecture

How BrowserAI is put together, and which code satisfies which part of it.

[`DECISIONS.md`](DECISIONS.md) says what was decided and why. This file says where each
decision lives in the tree, so that a reader who wants to change something can
find the one place that owns it. [`kb/`](kb/README.md) holds the measurements
behind the choices; [`HAZARDS.md`](HAZARDS.md) holds the failure modes each area
is defending against.

**One rule runs through all of it: BrowserAI is a proxy.** It spawns
`@playwright/mcp` over stdio and forwards JSON-RPC. Every tool schema originates
from the child's own `tools/list` at runtime, no tool definition is written in
C#, and Playwright is never driven directly. Rewriting the advertised surface —
filtering it, appending to descriptions, injecting a `session` parameter — is in
scope; renaming, or composing new tools out of several upstream calls, is not.

## The shape, in one pass

A client starts one `BrowserAI.exe` and talks MCP to it over stdio. That process
serves **one** MCP server upward and holds **N** children downward, one per open
session. Each child is a `node.exe` running the vendored `@playwright/mcp`, in
its own job object, against its own session directory.

```
MCP client ──stdio──> BrowserAI.exe ──stdio──> node.exe + @playwright/mcp ──> browser
                        (one server)             (one child per session,
                                                  one job object each)
```

The session directory is the identity, the handle and the lock. Everything else —
the index, the mutex names, the artifact routing, the sweep — is derived from it.

## The runtime it ships

**BrowserAI owns its Node and its Playwright tree.** Nothing on the machine is
assumed and nothing on `PATH` is used.

| Concern | Implemented by |
|---|---|
| Building the payload: Node, the vendored `node_modules`, the provenance stamp | `build/Build-Payload.ps1`, `build/payload/{package.json, package-lock.json}`, the publish-only payload copy in `src/BrowserAI/BrowserAI.csproj` |
| Finding the payload at run time | `src/BrowserAI/Runtime/PayloadLayout.cs` |
| Composing the child's configuration and command line | `src/BrowserAI/Runtime/{BrowserConfiguration, ChildLaunch}.cs` |
| First-run browser provisioning, and the tool that repairs it | `src/BrowserAI/Runtime/{BrowserProvisioner, BrowsersManifest, MaintenanceLock, ProvisioningRemediation, RevisionPrune, TreeDelete}.cs`, `src/BrowserAI/Interop/BrowserProcesses.cs` |

**The configuration is generated, never hand-held.** `BrowserConfiguration` writes
`browserName`, an explicit `chrome-for-testing` channel, `headless` from the
call's own `headed` argument, `userDataDir`, `downloadsPath`, `outputDir`,
`capabilities`, `saveSession` and `console.level`. `--sandbox` goes on the **command line** and never in the config
file, because only the command line reaches the browser.
`ConfigRoundTripTests` reads every one of those leaves back out of the running
child, with a named list of required keys, so deleting one from the generator
turns the suite red.

**Provisioning happens on first use and returns immediately.** `init` starts the
download and answers `browserProvisioning: provisioning`; every upstream tool is
refused until the marker lands, and the *same* child then navigates with no
restart. The installer is upstream's own, out of the payload, inside a job,
watched against **two** caps — ten minutes with **no progress at all**, and ten
minutes from the moment the browser's directory appears — with upstream's own
30-second socket stall left alone. *Corrected 2026-08-19 (previously "three caps
— 45 minutes absolute, 10 minutes from the moment the browser's directory
appears, 60 minutes as a crash tripwire").* **A total-time ceiling can only fire
on a link that is working**: 203,824,344 B in 2,700 s is 0.60 Mbps, so the old
cap stopped a slow-but-moving download and never a dead one, which upstream's own
socket timeout catches twenty times sooner. Progress is now **bytes on disk under
the browsers root**, and it is one number only because BrowserAI points the
installer's `TEMP` at `<browsers root>\.downloads` — upstream downloads into
`os.tmpdir()`, so without that the root does not grow at all until the unzip
starts ([kb](kb/playwright/provisioning-and-timings.md#what-grows-on-disk-while-an-install-runs-and-when--2026-08-19)).
**Ten minutes is set by upstream's own `__dirlock`**, which legitimately writes
nothing for up to 470 s before giving up by itself. The crash tripwire went with
the absolute cap and its job is now done by the same stall detector, which the
*waiting* process runs against the same bytes. One install per machine rather
than per process, through a `Global\` mutex keyed on the browsers root **and**
the family.

**The refusal a browser call meets while that runs is a progress report.** Bytes
written by this attempt against the measured download total, elapsed, the rate
those two give, and the remaining time that is arithmetic on them — because
`@playwright/mcp` emits no progress notifications at all
([kb](kb/mcp/sdk.md#lossless-passthrough-cancellation-notifications-and-error-frames)),
so the refusal is the whole mechanism. *Changed 2026-08-19 (previously the size,
the destination and "wait about ten seconds", which said the same thing at 8 s in
and at 25 minutes in).*
`INSTALLATION_COMPLETE` is the completeness check upstream never makes at launch;
a run that exits 0 without it is a failure whose partial tree is removed.

## The MCP server

| Concern | Implemented by |
|---|---|
| The proxy itself: filters, forwarding, the two methods it serves | `src/BrowserAI/Proxy/{BrowserProxy, ChildConnection, ServerInstructions}.cs` |
| Entry point, wiring, `--sweep` | `src/BrowserAI/Program.cs` |
| Registering BrowserAI with the client | `src/BrowserAI/Registration/{McpClientRegistration, RegistrationTarget, IRegistrationCommand, ClientCommandLine, McpRegistrar, RegistrationRecord, HookRegistration}.cs` |

**The protocol version is split deliberately.** `McpServerOptions.ProtocolVersion`
is `null` upward — whatever the caller asks for — while `McpClientOptions.
ProtocolVersion` is pinned to the child's ceiling downward. The pair is logged as
`requested=… negotiated=…` and **throws** when they differ, because the child caps
or echoes silently and never rejects, so that assertion is the only place a
mis-negotiation is visible.

**`tools/list` and `tools/call` are answered from the child's own bytes.** The
server carries no typed tool handlers at all: an incoming **message filter**
short-circuits both methods, and the answer is the child's `result` sliced by
`Utf8JsonReader` token offset and written with `WriteRawValue`. That is what makes
passthrough lossless — an unknown content type, an unmodelled tool member and a
non-ASCII character all survive, none of which a typed round trip preserves.

**There is one outgoing filter, and it removes a capability the SDK adds behind
our back.** `McpServerImpl` builds its own `ServerCapabilities` and gates
`Tools`, `Prompts`, `Resources` and `Completion` on configuration —
`ConfigureLogging` has no such guard and sets `Logging = new()` unconditionally, so
`initialize` advertised MCP logging that this server has never implemented and
never declared. `BrowserProxy.UnadvertiseLogging` takes it back out of the
`initialize` result on the way to the caller; the property cannot be un-set from
the options object, because the constructor overwrites it and it is
`[Obsolete("MCP9005")]`. What BrowserAI advertises is now byte-identical to what
the child advertises — `{"tools":{}}` — and `VerticalSliceTests` asserts the whole
object off the wire against the child's own snapshot. *Added 2026-08-18.*

**Registration is one file's decision.** `McpClientRegistration` is the only place
that decides *how* BrowserAI registers, and it carries the three rejected
alternatives and what a replacement must keep. The mechanism is the client's own
`claude mcp add --scope user`; the scope is user, which is the charter's promise
of one registration available in every repository with no per-repo files. Four
properties hold it together: it never registers the execution stub (only
`current\`), it is idempotent because the client is not, it writes a log record
**and** a `mcp-registration.json` beside the install root on every failure path
and can never throw into the installer, and it survives an update and a rollback.

## Sessions

The largest area, and the one everything else keys on.

| Concern | Implemented by |
|---|---|
| The directory, the lock and the record | `src/BrowserAI/Sessions/{SessionPath, SessionLayout, LockRecord, SessionLock}.cs` |
| The authored tools, and routing a call to a session's child | `src/BrowserAI/Sessions/{SessionToolSurface, SessionToolPolicy, SessionManager, SessionEnvironment, LiveSession}.cs` *(`SessionMode.cs` was deleted 2026-08-20)* |
| The machine-wide inventory | `src/BrowserAI/Sessions/SessionIndex.cs` |
| Lifetime | `src/BrowserAI/Sessions/BrowserIdleTimer.cs`, `src/BrowserAI/Interop/ClientLiveness.cs` |
| Reclaiming what a crash left behind | `src/BrowserAI/Sessions/StraySweep.cs`, `src/BrowserAI/Interop/{MessageWindows, BrowserProcesses}.cs`, `src/BrowserAI/Runtime/ProvisionedBrowsers.cs`, and — since 2026-08-20 — `src/BrowserAI/Updates/LiveInstances.cs`'s `ReclaimStaleMarkers`, which the sweep runs at the end of its own pass |
| The model-facing error text | `src/BrowserAI/Sessions/SessionErrors.cs` |

**The session directory is the identity.** One directory holds `browserai.json` at its
root and `profile/`, `output/` and `downloads/` beneath it. `browserai.json` is both
the lock and the record — held `FileAccess.ReadWrite, FileShare.Read`, carrying
the schema version and then browser, purpose, the resolved path, the
BrowserAI build and a `(pid, creationFileTime, clientProcessName)` holder that
deliberately outlives its holder. There is no central registry, no bearer token,
no label and no expiry timer; all four were designed and then dropped, because the
directory already is all of those things.

**Every field of `browserai.json` is an ordered list of timestamped statements —
schema 3, since 2026-08-20** *(previously schema 2, since 2026-08-18)*. The record
is append-only rather than a snapshot, so it says how a session got here and not
only where it is; `created` and `lastUsed` are no longer stored because they are
exactly the earliest and latest statement, and a stored copy could only disagree
with what it summarises. **A statement is appended only when the value changes**,
so browser, directory and build stay one statement long for a session that is not
moved, copied or run under a new build, and each field is capped at 32 statements
— trimmed out of the *middle*, because `created` is read from the first one and a
trim at the front would move a session's creation date. **There is no converter
for either superseded version**: an old file is refused with the fix in the
message. **What moved at schema 3 is that `mode` is gone** — session modes were
deleted, so the field described nothing while the strict parser went on requiring
it.

**`browserai_resume` no longer refuses a copied directory, and BrowserAI has zero
confirmation flags.** `acknowledgeCopy` existed because the record was a snapshot
— taking a copy over overwrote the only evidence that it *was* a copy — and it
went the day the record stopped being one. A resumed copy appends its path to a
`directory` history that still carries the original, and the answer hands the model
that history: where the directory has been, when, and that the recorded purpose
describes the original. A confirmation whose whole content can be returned as fact
is a question that did not need asking.
[`ModelSurfaceTests.NoAuthoredToolAsksTheCallerToConfirmAnything`](tests/BrowserAI.Tests/ModelSurfaceTests.cs)
keeps the count at zero.

**Our own files reject what they do not recognise.** `LockRecord.Read` is a
hand-written `Utf8JsonReader` parse that refuses an unknown key at any of the
three levels, a missing key, an **empty statement list**, a schema version it does
not know, and a timestamp that is not round-trippable ISO 8601 — every refusal
naming a recovery and stating that repeating the call will fail identically. **The
version is checked in a pass of its own, before anything else is parsed**: a
schema-1 file is well-formed JSON whose keys this build still recognises by name,
so a version checked last would report it as damage and send a caller to repair a
file that is not broken.

**Six authored tools, and `session` and `why` are both mandatory.**
`browserai_init`, `browserai_resume`, `browserai_list`, `browserai_destroy`,
`browserai_set_purpose` and `browserai_reinstall_browser`. **Two** parameters are
injected into every upstream tool's raw `inputSchema`, appended in that order so
upstream's own properties keep their positions; a call naming no session is
refused rather than reaching the run's own child, and a call naming a session
with no `why` is refused before anything is forwarded. Both are stripped from a
**clone** of the request before it goes to the child, which has never heard of
either — from a clone because the request object is the SDK's and may still be
read afterwards. *(`why` added 2026-08-20.)*

**`why` is on calls that NAME a session and nowhere else.** Every upstream
browser tool, plus `browserai_resume`, `browserai_destroy` and
`browserai_set_purpose`. Not `browserai_list`, which is directory-scoped, nor
`browserai_reinstall_browser`, which is machine-scoped: neither has a session
record to write into. Not `browserai_init`, which asks for `purpose` instead —
two mandatory free-text fields on one call gets one thoughtful answer and one
restatement.

**`purpose` is durable and `why` is disposable, and the schemas have to make that
unmistakable.** A `purpose` is the session's standing description — what
`browserai_list` shows six weeks later, and what whoever resumes the directory
reads first. A `why` is why you are doing this *right now*, it is one entry in
the log, and nothing shows it in a listing. `browserai_set_purpose` takes both,
which is where they collide, so its two descriptions carry the same example on
both sides of the line: *"the original login bug turned out to be a redirect
loop"* is a `why`; *"tracking the checkout redirect loop on staging"* is a
`purpose`. **If what you are writing would still be true next week it belongs in
`purpose`** — that sentence is in the schema, not only here.

**One time-ordered log, inside the record.** `browserai_init`'s purpose, every
purpose change, and every browser call the session **forwarded** are entries in
one ordered list in `browserai.json`, under the same session-long lock — so a
reader sees *the human changed the purpose here* between the calls it explains
rather than merging two streams by timestamp. An entry is written **immediately
before** a call is forwarded, so a call that never returns still left one, and a
call BrowserAI **refused** never reaches the list at all: this records what the
session *did*, and the refusals are in `browserai.log` beside it. **A call whose
entry cannot be written is refused rather than forwarded** — see
`SessionErrors.SessionLogCouldNotBeWritten`. It is inside the record rather than
in a sibling append-only file at the maintainer's decision; the cost is a
whole-record durable write per call, and
[QUESTIONS.md §14](QUESTIONS.md#14-the-one-time-ordered-log-lives-inside-browseraijson--decided-by-the-maintainer-over-my-recommendation)
weighs both sides. What is stored for an argument — every name always, `value`
and `text` never, an object or array as a shape, everything else cut at 200
characters — is `LoggedArgument`'s to say. `init` takes a required directory and purpose with
no default and no fallback, an optional `browser` defaulting to `chromium`, and
the three per-run booleans `headed`, `tracing` and `debug`; `resume` takes the
same three and reads `browser` from `browserai.json`, **refusing it as an
argument**, because a profile is browser-specific. *(Corrected 2026-08-20,
previously "a required directory, purpose and mode … `resume` reads mode and
browser … and **refuses them as arguments**": session modes were deleted, and
`browser` is now the only thing `resume` refuses.)*
`browserai_reinstall_browser` takes a **required** `browser` and nothing else —
*changed 2026-08-19 (previously no arguments, "because there is nothing to
name")*, which stopped being true the day a second family could be on disk. Its
accepted values are `ProvisionedBrowsers.ReinstallTargets`, a **superset** of the
families `init` offers: it also takes `shared`, which is `ffmpeg` and `winldd` —
downloaded by both families into one root, each with its own marker, and touched
by neither family's reinstall, so a corrupted `ffmpeg` had no route to repair
through this server. *Added 2026-08-19.* The two lists are deliberately separate
and `FirefoxSessionTests.TheAdvertisedSurfaceOffersBothFamiliesAndMakesReinstallNameOne`
asserts they differ, because a session's browser is a thing that renders web
pages. **`shared`'s refusal is wider than a family's**: it refuses while
**any** session is open, of either family, where a family reinstall counts only
sessions of the family it is replacing — `ffmpeg-win64.exe` exists only while a
recording runs, so *nothing is using it* and *nothing is about to* are different
statements there and the same statement for a browser.

**The browsers root carries a machine-wide READER/WRITER claim: every session
holds it shared for its whole life, and a reinstall holds it exclusively for the
whole call.** *Added 2026-08-19; became reader/writer 2026-08-20, at the
maintainer's decision — "any init or resume should take a system level lock. No
matter the browser type. These locks are cumulative. And reinstalling the browser
should be an exclusive lock."* The running-process census could never close this:
a reinstall establishes that nothing is running out of the tree and then deletes
it, and a peer's `init` in that window launches a browser into a directory that is
disappearing — the census was right when it was asked. The claim is
`<browsers root>\reinstall.lock`; `Runtime/MaintenanceLock.cs` opens it
`FileAccess.Read`/`FileShare.Read` for a session and
`FileAccess.ReadWrite`/`FileShare.Read` for a reinstall, and **Windows' sharing
rules give the reader/writer semantics directly** — an open is refused when its
access is outside an existing handle's share mode *or* when its share mode is
narrower than an existing handle's granted access, which is the check running in
both directions. **A file rather than a named mutex** because the
claim spans a 203.8 MB download inside an `async` method and a named mutex is
owned by the thread that waited on it, and **not a named semaphore** because a
semaphore's count is not restored when its holder dies, so one crashed reinstall
would refuse every `init` on the machine until a reboot. Windows closes a file
handle however the process ends.

**It is mutual against itself, it knows nothing about browser families, and it
does not drain.** A second reinstall is refused for the same reason a session is,
and a live Firefox session refuses a Chromium reinstall — the claim is one file at
the root of the browsers directory, so *nothing runs during a reinstall, whatever
family it belongs to*. **The family filter that used to narrow the refusal is
gone**; `SessionManager.LiveSessions` survives only to name what the caller must
close, and the kernel decides. A reinstall whose exclusive open is refused says so
at once and names the sessions holding it; it publishes **no intent marker**, it
starts no drain, and **writer starvation is accepted** — a machine that always has
one session open never lets a reinstall through, which is the maintainer's
decision rather than a defect to be mitigated. **The lock order is fixed and has
no cycle**: the claim is outermost and the per-family provisioning mutexes are
taken under it, never the other way round, and every acquisition on both sides is
non-blocking, so even an inverted order would produce a refusal rather than a
hang.

**A caller refused by a reinstall is told how far in it is.** The refusal quotes
the record the writer wrote and adds what the download staging directory weighs
and how long the claim has been held — both read off the filesystem, because the
peer cannot see the other process's provisioner at all. Zero staged bytes is
reported as *the delete, or an extraction already under way* rather than as a
stall, which is the honest reading of an empty staging directory.

⚠️ **Session modes were deleted on 2026-08-20, and the one table with them.**
*Corrected 2026-08-20 (previously "**One table drives six consumers.**
`SessionMode` is the table; the server `instructions`, `init`'s description,
`resume`'s result, the refusal text, the generated child config and the tests all
render from it, and `ModelSurfaceTests` asserts each consumer renders every row. A
mode added to the table alone turns the build red naming the consumers that do not
render it"; and "**A mode is two switches on a real browser, not a permission
set.** `BrowserConfiguration.ForSession` turns `Headed` into upstream's `headless`
and `Storage` into the capability set the session's own child is launched with —
so a session without storage has no cookie tools **in its child at all**, which is
the 'the capability does not exist' form rather than 'our code declines to use
it'".)* Five of the six consumers no longer exist. **The two switches went
different ways:** the window became a per-run argument — `headed` on `init` and on
`resume`, regenerated at every child launch and never recorded — and the
capability set became **every capability upstream declares**, in
`BrowserConfiguration.GrantedCapabilities`. Why, and what it cost, is in
[DECISIONS](DECISIONS.md#processes-browsers-and-session-modes).

**Ten tools became reachable in that change**, listed in
`SessionToolSurface.NewlyGrantedTools` and asserted by name in `ModelSurfaceTests`
and `VerticalSliceTests`: upstream's `network`, `pdf` and `testing` capabilities
had never been named in a generated config. The advertised surface is **68 of the
69** a fully-capable child exposes. `browser_run_code_unsafe` is **not** one of
them — it is `core` and always was.

⚠️ **The tool-permission policy was removed on 2026-08-18.** *Corrected
2026-08-18 (previously "**Enforcement is deny-by-default in two dimensions.**
`SessionToolPolicy` is the single place a call is permitted or refused, with a
**written-down** policy row per mode rather than one derived from the table's
flags — a permission inferred for a mode nobody considered is a security posture
arrived at by accident. A tool it does not classify and a mode it has no row for
both refuse everything.")* Five tool classes, the `(tool, mode)` matrix, both
deny-by-default fallbacks and the `browser_get_config` secrets guard are gone.
**The reason is that it was never a boundary against the caller**, though it was
described as one: the calling agent chooses the session directory, the browser
profile and its cookie database are created inside it, and the agent runs as the
same Windows user — so DPAPI decrypts for it and any file tool the agent holds
reads what the matrix declined to return. **Measured 2026-08-18**, against a
session this product configured and from a second process as the same user:
`CryptUnprotectData` and AES-256-GCM recovered the cookie with no elevation, and
App-Bound Encryption — the one mechanism that would have made the argument false
— is not in force for the provisioned Chromium
([kb](kb/chromium/profiles.md#chromiums-cookie-store-and-what-it-takes-to-read-one--measured-2026-08-18)).
Prompt injection is real and is not
addressed at this layer; a model tricked into wanting the cookies is capable of
opening the file, and a defeated motivation is not answered by more execution
complexity. **Change control moved to [the release gate](TESTING.md#the-upstream-review-gate)**,
where four golden snapshots — `tools-list.json` with every tool's `inputSchema`,
`cli-help.txt`, `config-schema.d.ts`, `browsers.json` — are regenerated from the
resolved payload and diffed on every build, and `upstream-review.json` blocks a
release until a human adjudicates what moved. That catches a changed schema and a
new CLI flag, which deny-by-default on a tool *name* never did.

**One argument is mandatory and one tool is withheld; neither is a permission.**
`session` is **mandatory**: a call naming none is refused rather than reaching the
run's own child, because that is *routing* — a proxy holding N children has to be
told which one a call belongs to, and a default would silently pick a session
nobody chose. And `browser_annotate` is **filtered out of `tools/list`**, in every
session, as a **liveness** decision with no security claim attached: it opens the
Playwright Dashboard and blocks until a human draws, with no self-timeout, and the
window belongs to a second non-headless browser under a daemon that writes into
`%TEMP%` and outlives the session. Filtering the surface is in scope by the
charter where renaming is not, and a caller that names the tool anyway is refused
rather than forwarded — a model knows upstream's names from everywhere except this
server's list. *Corrected 2026-08-18 (previously "Two refusals survive …
`browser_annotate` is refused on a mode that opens no window").* Implemented by
`SessionToolPolicy.IsWithheldFromTheSurface` and `SessionToolSurface.Rewrite`.

**Lifetime is one timer and no expiry.** A ten-minute browser-idle timer closes
the browser and keeps the node child; the relaunch on the next call is upstream's
own lazy creation, so nothing was built for it. Teardown is stdin EOF plus a
client-liveness watcher — an `OpenProcess` handle signalled on the client's exit,
never a poll and never a ping — and there is deliberately no close tool.

## Locking, ownership and the sweep

**One canonicalisation function feeds three consumers.** `GetFullPath` →
`TrimEnd('\')` → `ToUpperInvariant` → SHA-256 → hex produces the mutex name, the
the record file's identity and the index key alike. `LockScopes` names the three scopes;
`MachineMutex` creates `Global\` names **only** and throws on anything else, with
no `Local\` fallback anywhere and a test that fails the build if any other file in
`src/` constructs a named waitable object.

**Two spellings never reach that function, because two spellings are refused
first.** `SessionDirectoryGuard` runs at `browserai_init` and `browserai_resume`,
before anything is created and before the gate is taken, and answers two
questions in a fixed order that is itself the design: *is this a network path*,
from characters and then `GetDriveTypeW`, with **no filesystem call in either**;
then *is this an alias*, from `QueryDosDeviceW` for a `subst` and one
`GetFinalPathNameByHandleW` on the deepest existing ancestor for a junction,
symlink or mount point. The order matters because the second question opens a
directory and the first exists to ensure that open is local — a drive letter
mapped to a dead hostname was measured at **22,210 ms for one `File.Exists`**
([kb](kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds)).
The Win32 half is `Interop/VolumeIdentity`; the policy half, including what it
knowingly cannot see, is `Sessions/SessionDirectoryGuard` and
[the decision](DECISIONS.md#refusing-network-paths-and-aliased-spellings-at-the-door).
**8.3 short names are not in that list** — `Path.GetFullPath` expands them, so
they arrive canonical and there is nothing to refuse.

**Acquisition never waits.** It is zero-timeout and answers contention with the
holder's pid, start time, lock time and purpose. The one bounded wait is the
create-or-take gate, and an `AbandonedMutexException` on it is a distinct
`AcquiredAbandoned` outcome that is logged and proceeded through.

**A contender probes for the holder in front of that gate, and only in front of
it.** `SessionLock.ProbeForHolder` opens `browserai.json` before the per-directory
mutex is created: a sharing violation is the kernel's answer to *who owns this*,
so a peer that only wants to name the holder is answered there and never queues
behind every other peer. Measured against a directory a live holder already had,
the slowest refusal falls from **2,084 ms to 203 ms at 100 contenders** — the
charter's design point — and from 4,267 ms to 449 ms at 200
([kb](kb/windows/detection.md#named-mutexes-and-lock-files)). **A probe that says
*looks free* proves nothing and falls through to the unchanged gated path**,
because with the gate skipped on the free path the rename retry loop becomes the
serialiser and two processes end up owning one directory — the failure
[the adversarial review](docs/reviews/2026-08-18-adversarial-locking.md) found in
the version that replaced the gate rather than fronting it.

**That same open has a second caller since 2026-08-20, and it reports rather than
decides.** `SessionLock.ProbeLiveness` is the one opener; `ProbeForHolder` is a
decision built on it and `browserai_list` is a **report** built on it, so the rule
*a sharing violation may be read as owned and nothing else may be read as free*
is stated once and cannot come apart. The listing prints **in use: YES / no /
UNKNOWN** per entry and **never names the holder**, because a sharing violation
says the file is held and not by whom — the record inside can describe a previous
one. It costs one `CreateFile`/`CloseHandle` per entry, measured at 0.035 ms free
and 0.049 ms held against 0.6–2.3 ms for the size walk the same loop already
performs
([kb](kb/windows/detection.md#the-pre-gate-probe-as-a-liveness-report--measured-2026-08-20)),
and a session this process is already driving is answered from its own live-session
map without asking the kernel at all.

**Writes are durable and atomic.** `WriteThrough` + `Flush(flushToDisk: true)` +
`File.Move(overwrite: true)`, with the temp file in the target's own directory.
A rename cannot replace a file whose handle is open under any share mode, so
create-or-take is close → rename → re-open *inside* the per-directory mutex.

**Never by image name — structurally, not procedurally.** BrowserAI can terminate
a process only if it belongs to a job BrowserAI created, or if its **full image
path** is a binary BrowserAI provisioned. `build/BannedSymbols.txt` is supplied to
every project from `Directory.Build.props`, so a project added later is covered by
construction; `NeverByImageNameTests` reads the whole tree for what an analyzer
cannot see — `taskkill`, a WMI query filtered on `Name`, a name-filtered
enumerator.

**The stray sweep needs two guards to agree.** Detection is fully documented and
decides: `EnumProcesses` → `OpenProcess` → `QueryFullProcessImageNameW`, keeping
only processes whose full image path is one of the two binaries BrowserAI
provisioned, composed from the payload's own revision and never spelled out.
Attribution may fail and must fail safe: a class-qualified
`FindWindowExW(HWND_MESSAGE, …)` walk with `ERROR_INVALID_WINDOW_HANDLE` checked
and the walk restarted, and a candidate no window claims is reported loudly and
never touched. A stray is a process running our binary **whose** attributed
directory holds a `browserai.json` this sweeper can take itself, without writing to it.

**Both families are offered, and everything below the front door reads the family
from the session's own record.** `browserai_init` accepts `chromium` or
`firefox`; provisioning, the config generator, the launch preflight and the stray
sweep all take it from `browserai.json` rather than assuming one, so a Firefox record
can never be run as Chromium against a Firefox profile. *Corrected 2026-08-19
(previously Firefox was built, measured and not offered.)* The per-family
first-run download sizes a refusal quotes are in
`BrowserProvisioner.FirstRunDownloadSizes`, measured and dated
([kb](kb/playwright/provisioning-and-timings.md#firefox-measured-the-same-way--2026-08-19)).

**Firefox needs a preflight and a second attribution path.** `FirefoxProfile`
opens `<profile>\parent.lock` for write before any Firefox starts and refuses on a
sharing violation — Playwright's own `isProfileLocked` checks only Chromium's
`lockfile`, so without this a collision puts a native modal on the desktop for up
to three minutes, which is an invisible hang in a background server. Attribution
is `RmStartSession` → `RmRegisterResources` → `RmGetList` via
`src/BrowserAI/Interop/RestartManager.cs`, intersected with the image-path
candidate set and guarded on `ProcessStartTime`, because the Restart Manager
answers about whatever file it is handed and is never actionable alone.

## Process containment and observability

| Concern | Implemented by |
|---|---|
| The job object, and starting a child inside it | `src/BrowserAI/Interop/{JobObject, JobLauncher, LaunchedProcess}.cs` |
| The two custom transports | `src/BrowserAI/Protocol/{DirectStdioClientTransport, ChildProcessSession, DirectStdioServerTransport, JsonLines, JsonLinesTransport, VerbatimPayload, ChildLink, ChildEnvironment}.cs` |
| stdout ownership | `src/BrowserAI/Protocol/StdioChannel.cs`, `src/BrowserAI/BannedSymbols.txt` |
| stderr classification | `src/BrowserAI/Protocol/StandardErrorClassifier.cs` and its pinned reference copy |
| Logging | `src/BrowserAI/Logging/` |
| Where files live, installed or not | `src/BrowserAI/Hosting/{IAppPaths, LocalAppDataPaths, BuildVersion}.cs`, `src/BrowserAI/Updates/InstallLocation.cs` |
| Refusing to serve out of a root two users could share | `src/BrowserAI/Hosting/InstallRootScope.cs`, called from `Program.Main` before anything creates state |

**One unnamed, non-inheritable job per child**, carrying `KILL_ON_JOB_CLOSE` and
nothing else, assigned at creation through `PROC_THREAD_ATTRIBUTE_JOB_LIST`, held
for the child's whole life. **Closing the handle is the only kill path** — there
is no `Process.Kill(entireProcessTree)` anywhere in the tree, and the analyzer
refuses one.

**stdout is the protocol channel and one type owns it.** Nothing anywhere in the
process may call `Console.WriteLine`, including inside a `catch`; the rule extends
to stdout's *handle*, because a dependency's type initializer counts as our code.
UTF-8, LF, no BOM.

**A child's exit code is cached as an `int` the moment it is available**, because
`Process.ExitCode` throws after `Dispose()` — and its stderr handler is wired
before `Start()`, not after.

**The stderr classifier is a pinned port.** The two regexes are carried verbatim
from the launcher this project replaces, with those two lines committed beside the
C# as `StandardErrorClassifier.reference.ps1` and compared character by character
on every build, so an edit to either side is red rather than a silent behaviour
change. **The classification is the log level:** Debug for the benign
`Session: <path>` line, Warning for an error-shaped one.

## Artifacts

**Three levers, and they only work together.**

1. The session child's `WorkingDirectory` is the session's `output\`, so a bare
   `foo.png` that nothing rewrote still lands inside the tree by construction.
2. A `filename` argument is rewritten to an absolute path in the folder its
   generator prefix implies, **before the child sees the call**.
3. The answer to any rewritten call carries the absolute path, the
   session-relative path, any rename, the session's cumulative size and the index
   path.

⚠️ **And a fourth thing, which lever 2 had been silently costing.** Upstream
answers a screenshot with an `image` content block *as well as* a file, under
`if (!params.filename)` — so supplying a name to make the artifact legible turned
that guard off and **no screenshot came back inline in any mode**, where bare
`@playwright/mcp` returns one. Added 2026-08-18: the block is appended to the same
answers this section already rewrites, read back off disk after the child wrote
it, under the caller-visible condition upstream tests — *the caller named no
file*. `browser_take_screenshot` only, because it is upstream's only
`registerImageResult` call site; a PDF is not an image. Sizes, and why there is no
threshold, are in
[kb](kb/playwright/tools-and-artifacts.md#the-inline-screenshot-and-what-it-costs--measured-2026-08-18).

| Concern | Implemented by |
|---|---|
| Routing, the prefix set, filename rules, the result note | `src/BrowserAI/Artifacts/{ArtifactRouting, ArtifactTools, ArtifactFilename, ArtifactRouter, ResultNote}.cs` |
| Where the folders are | `src/BrowserAI/Sessions/SessionLayout.cs`, `src/BrowserAI/Hosting/IAppPaths.cs` |
| The generated prefix set the routing is checked against | `build/upstream-snapshots.mjs` → `upstream-snapshots/tools-list.json` |

**The prefix set is a coverage gate derived from the resolved child, never
typed.** `build/upstream-snapshots.mjs` reads every artifact template out of
`coreBundle.js` — following a ternary, a template literal and one `this._member`
indirection rather than matching string literals — and writes `artifactPrefixes`
into the committed snapshot, which is regenerated and diffed on every build.
`ArtifactRoutingTests` compares it against the declared folders **in both
directions**. The same rule covers tool arguments: a tool carrying a `filename`
this build has not classified fails the build, deny-by-default.

**Refusals are decided on the string and never by touching the filesystem** —
`..\..\foo.png`, `C:\foo.png`, `C:foo.png`, a UNC path, `\foo.png`, `\\?\C:\…`,
`NUL.png`, a trailing space and a trailing separator each get a sentence naming
the shape and the fix. Touching the filesystem to decide is what makes a single
call hang for 21 measured seconds on an unresponsive UNC host.

**Never overwrite.** A taken name is suffixed, in-flight names are reserved so two
concurrent calls cannot collide, and the answer says what it was renamed from.

## Updates

| Concern | Implemented by |
|---|---|
| The update lane | `src/BrowserAI/Updates/{InstallLocation, UpdateFeed, UpdateConfiguration, IUpdateClient, VelopackUpdateClient, UpdateService, LiveInstances, VelopackStartup}.cs` |
| Packing, versioning and the resolved-set manifest | `build/{New-Release.ps1, Test-ReleaseVersion.ps1, Write-ReleaseManifest.ps1, Get-ReleaseNotes.ps1}` |

Per-user to `%LocalAppData%`, never `--msi`, `--shortcuts None`.
`SetAutoApplyOnStartup(false)` is the **first line** of `Main`, before logging,
because that call also serves the installer's own hooks. `--mainExe
BrowserAI.exe` so that registration names `current\BrowserAI.exe` and never the
execution stub beside it.

**The channel reaches Velopack through `UpdateOptions.ExplicitChannel` and nowhere
else.** `UpdateFeed.Create` *refuses* the three shapes that 404 silently: a base
URL that already ends in the channel, an empty channel, and a channel that is not
lower-case.

**An installed process takes its root from `VelopackLocator.Current.RootAppDir`**
through `InstallLocation` — never `AppContext.BaseDirectory`, and never arithmetic
on `%LocalAppData%`, which is a coincidence that stops holding the moment
`Setup.exe --installto` is used. The layout *below* the root is unchanged either
way, which is why only the root moved.

**The apply is gated on being the last instance.** Every run takes a held
`<root>\live\<pid>-<guid>.live` handle at startup, keyed on the same
canonicalisation sessions use; the installer kills every process under the install
root after each hook returns, so an apply that cannot prove solitude must not
happen. Then `Update.exe apply --silent --norestart --waitPid <ownPid>` and an
ordinary shutdown — never `Environment.Exit`.

**The census is three-valued and the updater collapses it, which is the point.**
`LiveInstances.Census` answers `Alone`, `NotAlone(n)` or `Undetermined(why)`, and
the third one carries the path, mutex name or exception that stopped it, so a
tool that *repairs* on the strength of it has something to act on. `AmIAlone` is
one expression over that with exactly one `true` arm, and `UpdateService` still
calls only `AmIAlone` — so an undetermined census is treated precisely as *not
alone* was and is: stage, do not apply.

**A marker is stale only when it is not held, and the reclaim runs where it will
actually happen.** Until 2026-08-20 the only code that removed a dead marker was
the census itself, reached only after an update had been found *and* downloaded;
755 unheld files had accumulated in two days. `ReclaimStaleMarkers` now takes the
same per-root gate at **zero timeout** — one process reclaims, the rest move on —
and runs from the stray sweep and from startup on a background thread. It is
deliberately *not* inside `Join`'s hold: that hold is on the startup path with
every process on the machine queueing behind it, and a join that times out makes
this process invisible to a peer's census.

**Three download timers, off the message loop:** 30 minutes absolute, a 60-second
stall reset on every progress callback, and a 45-minute tripwire deliberately
outside the other two combined.

## The build and the toolchain

Component choices, the SDK deviations, the build-configuration requirements and
where the version comes from are in [`STACK.md`](STACK.md). The short form:

- **NativeAOT, self-contained, win-x64.** Analyzers and `.editorconfig` at error
  severity; a severity is never weakened to make code pass.
- **Every dependency floats to latest at build time and is frozen into the
  artifact.** Version numbers in the documentation are provenance stamps, not
  targets — the build does not read them.
- **Versions come from git tags** via MinVer, product project only. A version
  derived from no tag fails the build with the remedy in the message, and nothing
  reads `AssemblyVersion`.
- **The MCP SDK is deviated from in nine specific places**, each recorded in
  `STACK.md` with what it costs and what would make it unnecessary.

## The suite

[`TESTING.md`](TESTING.md) is the argument for the suite's shape — why it is not
severable from the float, the five cadence layers, why the harness is ours, and
the two gates. What implements it:

| Layer | Implemented by |
|---|---|
| The raw protocol client — the oracle for everything that talks to a real child | `tests/BrowserAI.Tests/Harness/RawStdioClient.cs` |
| The published-slice rig | `tests/BrowserAI.Tests/Harness/{PublishedSlice, SliceRun, ProcessCommandLine, BrowserAiPaths}.cs` |
| The in-process harness and the scriptable fake child | `tests/BrowserAI.Tests/Harness/{McpTestHarness, FakePlaywrightChild, PipeDuplex, PipeClientTransport, FrameChannel, RawPipeClient, RigSessionEnvironment}.cs` |
| Session-level oracles | `tests/BrowserAI.Tests/Harness/{SessionRun, UpstreamSurface}.cs` |
| Out-of-process probes | `tests/BrowserAI.TestProbe/` |
| The clock the suite advances by hand, so the one timer in the product is asserted on rather than raced | `tests/BrowserAI.Tests/Harness/ManualClock.cs`, `src/BrowserAI/Sessions/SessionEnvironment.cs` (`Clock`) |
| How wide the suite runs, and the design point run for real: 100 concurrent BrowserAI processes | `tests/BrowserAI.Tests/{SuiteParallelism, SaturationTests}.cs` |
| The capability gate that makes a degraded run visible | `tests/BrowserAI.Tests/Harness/SuiteEnvironment.cs`, `tests/BrowserAI.Tests/SuiteCoverageTests.cs` |
| The upstream-review gate | `upstream-snapshots/`, `build/upstream-snapshots.mjs`, `build/Update-UpstreamSnapshots.ps1`, `build/UpstreamSnapshots.targets`, `tests/BrowserAI.Tests/{UpstreamSnapshotTests, UpstreamReviewTests, ReVerificationIndexTests, ResolvedVersions}.cs` |
| The documents themselves | `tests/BrowserAI.Tests/{DocumentationLinkTests, HazardIndexTests, ChangelogTests, BuildConfigurationTests}.cs` |

**The raw client is mandatory, not a nicety.** With both SDK transports replaced,
an `McpClient` would be testing the code under test using the code under test.
`RawStdioClient` shares no protocol type with the product.

**The harness has two pipe hops where the SDK's fixture has one**, because a proxy
is a server on one side and a client on the other, and the double never touches
the SDK — so anything the caller sees differently is the proxy's doing. Its
`DisposeAsync` **asserts** that it left no pipe and no task alive.

**Byte-identity is checked with a second reader.** `Harness/JsonSpan.cs` is a
token-offset reader written for the tests, because a byte comparison whose two
spans were both sliced by the product agrees with an off-by-one in the product.

**A run that proves less says so.** Every capability the suite can lack — the
published slice, the repository payload, a provisioned Chromium, a provisioned
Firefox, a packed `.nupkg`, an MCP client on the machine — routes through one
gate, and the run ends with a block naming each `PRESENT` / `ABSENT` / `PARTIAL`
and every test that took a degraded path. `BROWSERAI_RELEASE_RUN=1` turns every
such skip into a failure. This exists because the four numbers in a run summary
were once character-identical between a run that launched a real browser and one
that could not.

**And since 2026-08-19 a controlled environment declares *which* it expects to
lack**, because *loud* is not *noticed*. The gate above made an absence visible;
nothing pinned the set, so a fifth capability going absent read exactly like the
four that are absent by design — green run, one more `ABSENT` line, a handful of
tests skipping instead of running. `BROWSERAI_EXPECTED_ABSENT` names what an
environment expects to lack, and an absence it does not name fails the build — as
does a name in it that turns out to be `PRESENT`, since a declaration wider than
the truth is standing permission for that capability to disappear later. An unset
variable declares nothing, so a developer machine, whose provisioned set is a fact
about somebody's disk, behaves exactly as before.

⚠️ **Corrected 2026-08-20 (previously "`BROWSERAI_EXPECTED_ABSENT` on the
workflow's test step names what CI expects to lack"): nothing sets it now, so the
mechanism is intact and inert.** Hosted CI was its only consumer and was removed
that day at the maintainer's decision; the third arm that read the workflow file
went with it, because a re-pointed version could have no positive control. What
this leaves unverified, and what has to come back with CI, is
[`TODO.md`](TODO.md#continuous-integration).
