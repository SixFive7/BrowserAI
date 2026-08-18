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
| Running all of that on a machine nobody owns | `.github/workflows/build.yml` — payload, both browsers, the AOT publish and the whole suite, on every push and pull request |
| Finding the payload at run time | `src/BrowserAI/Runtime/PayloadLayout.cs` |
| Composing the child's configuration and command line | `src/BrowserAI/Runtime/{BrowserConfiguration, ChildLaunch}.cs` |
| First-run browser provisioning, and the tool that repairs it | `src/BrowserAI/Runtime/{BrowserProvisioner, BrowsersManifest, ProvisioningRemediation, RevisionPrune, TreeDelete}.cs`, `src/BrowserAI/Interop/BrowserProcesses.cs` |

**The configuration is generated, never hand-held.** `BrowserConfiguration` writes
`browserName`, an explicit `chrome-for-testing` channel, `headless` from the mode,
`userDataDir`, `downloadsPath`, `outputDir`, `capabilities`, `saveSession` and
`console.level`. `--sandbox` goes on the **command line** and never in the config
file, because only the command line reaches the browser.
`ConfigRoundTripTests` reads every one of those leaves back out of the running
child, with a named list of required keys, so deleting one from the generator
turns the suite red.

**Provisioning happens on first use and returns immediately.** `init` starts the
download and answers `browserProvisioning: provisioning`; every upstream tool is
refused until the marker lands, and the *same* child then navigates with no
restart. The installer is upstream's own, out of the payload, inside a job,
watched against three caps — 45 minutes absolute, 10 minutes from the moment the
browser's directory appears, 60 minutes as a crash tripwire — with upstream's own
30-second socket stall left alone. One install per machine rather than per
process, through a `Global\` mutex keyed on the browsers root **and** the family.
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
| The authored tools, and routing a call to a session's child | `src/BrowserAI/Sessions/{SessionMode, SessionToolSurface, SessionToolPolicy, SessionManager, SessionEnvironment, LiveSession}.cs` |
| The machine-wide inventory | `src/BrowserAI/Sessions/SessionIndex.cs` |
| Lifetime | `src/BrowserAI/Sessions/BrowserIdleTimer.cs`, `src/BrowserAI/Interop/ClientLiveness.cs` |
| Reclaiming what a crash left behind | `src/BrowserAI/Sessions/StraySweep.cs`, `src/BrowserAI/Interop/{MessageWindows, BrowserProcesses}.cs`, `src/BrowserAI/Runtime/ProvisionedBrowsers.cs` |
| The model-facing error text | `src/BrowserAI/Sessions/SessionErrors.cs` |

**The session directory is the identity.** One directory holds `lock.json` at its
root and `profile/`, `output/` and `downloads/` beneath it. `lock.json` is both
the lock and the record — held `FileAccess.ReadWrite, FileShare.Read`, carrying
the schema version and then mode, browser, purpose, the resolved path, the
BrowserAI build and a `(pid, creationFileTime, clientProcessName)` holder that
deliberately outlives its holder. There is no central registry, no bearer token,
no label and no expiry timer; all four were designed and then dropped, because the
directory already is all of those things.

**Every field of `lock.json` is an ordered list of timestamped statements —
schema 2, since 2026-08-18.** The record is append-only rather than a snapshot, so
it says how a session got here and not only where it is; `created` and `lastUsed`
are no longer stored because they are exactly the earliest and latest statement,
and a stored copy could only disagree with what it summarises. **A statement is
appended only when the value changes**, so mode, browser, directory and build stay
one statement long for a session that is not moved, copied or run under a new
build, and each field is capped at 32 statements — trimmed out of the *middle*,
because `created` is read from the first one and a trim at the front would move a
session's creation date. **There is no schema-1 converter**: an old file is
refused with the fix in the message.

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

**Six authored tools, and `session` is mandatory.** `browserai_init`,
`browserai_resume`, `browserai_list`, `browserai_destroy`,
`browserai_set_purpose` and `browserai_reinstall_browser`. A `session` parameter
is injected into every upstream tool's raw `inputSchema`, appended so upstream's
own properties keep their order; a call naming no session is refused rather than
reaching the run's own child. `init` takes a required directory, purpose and mode
with no default and no fallback; `resume` reads mode and browser from `lock.json`
and **refuses them as arguments**, because a profile is browser-specific.

**One table drives six consumers.** `SessionMode` is the table; the server
`instructions`, `init`'s description, `resume`'s result, the refusal text, the
generated child config and the tests all render from it, and `ModelSurfaceTests`
asserts each consumer renders every row. A mode added to the table alone turns the
build red naming the consumers that do not render it. *Corrected 2026-08-18
(previously the fifth consumer was "the `(tool, mode)` decision").*

**A mode is two switches on a real browser, not a permission set.**
`BrowserConfiguration.ForSession` turns `Headed` into upstream's `headless` and
`Storage` into the capability set the session's own child is launched with — so a
session without storage has no cookie tools **in its child at all**, which is the
"the capability does not exist" form rather than "our code declines to use it".

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

**Two refusals survive, and neither is a permission.** `session` is **mandatory**:
a call naming none is refused rather than reaching the run's own child, because
that is *routing* — a proxy holding N children has to be told which one a call
belongs to, and a default would silently pick a session nobody chose. And
`browser_annotate` is refused on a mode that opens no window, as a **liveness**
guard with no security claim attached: it opens the Playwright Dashboard and
blocks until a human draws, the window appears on a headless session too, and an
unattended run that called it would hang until it was killed.

**Lifetime is one timer and no expiry.** A ten-minute browser-idle timer closes
the browser and keeps the node child; the relaunch on the next call is upstream's
own lazy creation, so nothing was built for it. Teardown is stdin EOF plus a
client-liveness watcher — an `OpenProcess` handle signalled on the client's exit,
never a poll and never a ping — and there is deliberately no close tool.

## Locking, ownership and the sweep

**One canonicalisation function feeds three consumers.** `GetFullPath` →
`TrimEnd('\')` → `ToUpperInvariant` → SHA-256 → hex produces the mutex name, the
lock file's identity and the index key alike. `LockScopes` names the three scopes;
`MachineMutex` creates `Global\` names **only** and throws on anything else, with
no `Local\` fallback anywhere and a test that fails the build if any other file in
`src/` constructs a named waitable object.

**Acquisition never waits.** It is zero-timeout and answers contention with the
holder's pid, start time, lock time and purpose. The one bounded wait is the
create-or-take gate, and an `AbandonedMutexException` on it is a distinct
`AcquiredAbandoned` outcome that is logged and proceeded through.

**A contender probes for the holder in front of that gate, and only in front of
it.** `SessionLock.ProbeForHolder` opens `lock.json` before the per-directory
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
directory holds a `lock.json` this sweeper can take itself, without writing to it.

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
