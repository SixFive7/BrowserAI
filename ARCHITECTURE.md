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
the index, the mutex names, the guard file, the store, the sweep — is derived
from it. *(Corrected 2026-08-26, previously "the index, the mutex names, the
artifact routing, the sweep": artifact routing is deleted, and the two files the
directory carries are what the identity now derives.)*

**What BrowserAI is, in one sentence:** a session-lifecycle manager and reason
logger wrapped around a verbatim Playwright pipe. Nothing sits between the two
servers except the session system and the reason system, and
[the one exception](#byte-identical-and-the-one-exception) is named rather than
implied.

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
`capabilities`, `saveSession`, `console.level`, `codegen`, `snapshot.boxes` and a
`contextOptions` block — `viewport`, `locale`, `timezoneId`,
`ignoreHTTPSErrors`, `permissions`, and `recordHar` with `serviceWorkers` when a
run asked for network capture.

**Everything in that block is per-run and none of it is recorded.** `RunOptions`
is what a caller gave one launch; `browserai_init` and `browserai_resume` read it
the same way, so a session created at one viewport is resumed at another without
being destroyed first. **Four opinions are hard-coded and are deliberately not
arguments**: `console.level` is `debug` always — `error`→`debug` costs **+1
character** on a navigation response, because the events line is a *pointer*
rather than the text, and `browser_console_messages` already takes a read level
that can be lowered at the moment of asking; `codegen` is `none`, which strips a
`### Ran Playwright code` block from every response for a feature this product
does not have; `snapshot.boxes` is `true`, whose cost is deferred behind a link
and which the `vision` capability's six coordinate tools are unusable without;
and `permissions` is `["clipboard-read"]` — ⚠️ **for Chromium only**, because
Firefox fails at `initializeServer` with `Unknown permission: clipboard-read` and
the browser exits, [measured
2026-08-20](kb/playwright/configuration.md#silent-config-failures). That is the
second key after `channel` whose correct value is *absent for one family*, and
the first whose wrong value is fatal rather than an opinion that never arrives.

**`serviceWorkers: "block"` ships with `recordHar` or not at all.** A request
served out of a worker's cache never reaches the network layer the archive is
written from, so without it the capture is silently incomplete — in the direction
that matters, because a worker serves the repeat requests. **Each launch gets its
own timestamped archive name** under `output\network\`: `recordHar` truncates
whatever path it is given at every context creation, and the config is
regenerated per launch, so the overwrite-on-resume is avoidable rather than
documentable. `--sandbox` goes on the **command line** and never in the config
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

### Byte-identical, and the one exception

**A forwarded call goes out byte-identical and its answer comes back
byte-identical.** BrowserAI kind-checks `name`, `session` and `why` at the door —
a wrong JSON type is a named refusal and never a bare `-32603` — looks the tool up
in [the verdicts file](#the-verdicts-file-and-why-a-tool-nobody-judged-is-refused),
writes the log row `in-flight`, **strips `session` and `why` from a clone of the
request**, and forwards what is left unchanged. Nothing is added to the answer:
no note, no scan of the text for filenames, no path handling, no filename
rewrite. `LosslessPassthroughTests` asserts the property as *byte-identical*
rather than as *byte-identical plus exactly one appended block*, which is what it
asserted while artifact routing existed.

⚠️ **THE ONE EXCEPTION, and it is named here so that it stays one: the
provisioning remediation rewrite.** When a child answers a call by telling the
caller to run `npx @playwright/mcp install-browser`, that advice is wrong for a
BrowserAI install — browsers are ours to provision — and following it puts a
second browser tree on the machine that nothing here manages.
`ProvisioningRemediation.Rewrite` replaces the advice with
`browserai_reinstall_browser`, keeping upstream's own diagnosis intact, and
`BrowserProxy` logs that the answer was **not** byte-identical when it fires.

**Three things keep the exception from spreading.** It is **anchored on an
identifier**, upstream's `install-browser` CLI subcommand, never on prose. It is
**`isError`-gated**, so a page that merely quotes upstream's advice inside a
successful answer is forwarded untouched —
`ProvisioningRemediationTests.APageQuotingUpstreamsAdviceInASuccessfulAnswerIsForwardedUntouched`.
And it has a **real-child canary**: an empty browsers root, the genuine error
provoked against a real child, and both halves asserted —
`ProvisioningRemediationTests.ARealChildWithNoBrowsersStillSaysTheSentenceTheRewriteIsAnchoredOn`.
If upstream rewords its advice, that is a red build at the next upstream review
rather than a rewrite that quietly stopped firing.

**The model is told, on a surface the client does not truncate.** The server
`instructions` carry it: *"Browsers are managed by BrowserAI — never install any
yourself (no `npx playwright install`). If the browser installation is broken,
`browserai_reinstall_browser` is the repair."*

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
| The directory, the guard and the record | `src/BrowserAI/Sessions/{CanonicalPath, SessionPath, SessionLayout, SessionLock, SessionRecord}.cs` over `src/BrowserAI/Storage/{LockFile, SessionStore}.cs` *(`LockRecord.cs` and the whole `browserai.json` serialisation were deleted 2026-08-26; `SessionDirectoryGuard.cs` the same day, into `CanonicalPath`)* |
| The two files themselves, and the SQLite they rest on | `src/BrowserAI/Storage/` — [its own rules](src/BrowserAI/Storage/CLAUDE.md) |
| The authored tools, and routing a call to a session's child | `src/BrowserAI/Sessions/{SessionToolSurface, ToolVerdicts, SessionManager, SessionEnvironment, LiveSession}.cs` *(`SessionMode.cs` was deleted 2026-08-20; `SessionToolPolicy.cs` 2026-08-26, into `ToolVerdicts` and the file it reads)* |
| The machine-wide inventory | `src/BrowserAI/Sessions/SessionIndex.cs` |
| Lifetime | `src/BrowserAI/Sessions/BrowserIdleTimer.cs`, `src/BrowserAI/Interop/ClientLiveness.cs` |
| Reclaiming what a crash left behind | `src/BrowserAI/Sessions/StraySweep.cs`, `src/BrowserAI/Interop/{MessageWindows, BrowserProcesses}.cs`, `src/BrowserAI/Runtime/ProvisionedBrowsers.cs`, and — since 2026-08-20 — `src/BrowserAI/Updates/LiveInstances.cs`'s `ReclaimStaleMarkers`, which the sweep runs at the end of its own pass |
| The model-facing error text | `src/BrowserAI/Sessions/SessionErrors.cs` |

⚠️ **DELETED 2026-08-26, and the paragraph it replaces is summarised rather
than kept.** Two paragraphs here described `ArtifactRouter.NoteWhatTheAnswerPublished`
— the rule that a file the child had named in its own answer was recorded where
it lay instead of being swept into a typed folder, the set being monotone across
calls, the delimited match that replaced an undelimited `Contains`, the eviction
that stopped it being monotone across files, and the provisioning-rewrite path
that used to bypass the whole thing. **All of it is gone with artifact routing.**
Nothing reads the child's answer and nothing moves a file, so a pointer upstream
publishes resolves because the file is still where upstream put it —
`FileAccessRootTests.EveryPointerARealChildPublishesResolvesBecauseNothingMovesIt`
keeps that measured against a real browser rather than assumed. See
[the output directory](#the-sessions-output-directory).

**The session directory is the identity.** One directory holds `browserai.lock`
and `browserai.data` at its root and `profile/`, `output/` and `downloads/`
beneath it. There is no central registry, no bearer token, no label and no expiry
timer; all four were designed and then dropped, because the directory already is
all of those things.

⚠️ ***Corrected 2026-08-26 (previously "One directory holds `browserai.json` at
its root … `browserai.json` is both the lock and the record — held
`FileAccess.ReadWrite, FileShare.Read`, carrying the schema version and then
browser, purpose, the resolved path, the BrowserAI build and a
`(pid, creationFileTime, clientProcessName)` holder that deliberately outlives its
holder").*** **They are two files, and neither answers the other's question.**
`browserai.lock` says *who owns this directory*: it carries the holder alone —
pid, process-creation FILETIME, and a display-only client name — it is written
**once**, at acquisition, and its open handle is the whole of ownership.
`browserai.data` says *what happened here*: a SQLite store in WAL mode,
`PRAGMA user_version` 1, with a `statements` table for the browser / directory /
purpose / build histories and a `log` table for every call. The split is not
tidiness — a transaction cannot be both the lock and the write path, because a
reader only sees committed work and committing ends the transaction, so a store
that tried to be the guard would either hide the log from every reader or lock
them out of it.

**Every field of the record is an ordered list of timestamped statements**, and
that survived the move intact: the record is append-only rather than a snapshot,
so it says how a session got here and not only where it is. `created` and
`lastUsed` are not stored because they are exactly the earliest and latest
statement, and a stored copy could only disagree with what it summarises. **A
statement is appended only when the value changes** — except `holder`, which
dedup cannot bound because `(pid, creationFileTime)` never repeats. ⚠️ *Corrected
2026-08-26 (previously "schema 3, since 2026-08-20", and "each field is capped at
32 statements — trimmed out of the middle, because `created` is read from the
first one and a trim at the front would move a session's creation date").*
**There is no cap and there is no trim**, anywhere: `created` is the oldest
statement because it is still there rather than because a policy protected it.
**There is no converter**, and a directory holding the old `browserai.json` is
refused with the format as the reason and the recovery in the message —
`browserai_destroy` says *"I cannot clean this up — remove the entire directory
yourself"*, because a converter for a population of zero is a migration path
nobody tests.

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

**Our own files reject what they do not recognise.** ⚠️ *Corrected 2026-08-26
(previously "`LockRecord.Read` is a hand-written `Utf8JsonReader` parse that
refuses an unknown key at any of the three levels, a missing key, an **empty
statement list**, a schema version it does not know, and a timestamp that is not
round-trippable ISO 8601 … **The version is checked in a pass of its own, before
anything else is parsed**").* The rule is unchanged and it is enforced in two
places now. `LockFile.Parse` refuses a lock file carrying a property BrowserAI
does not write, one missing the process-creation FILETIME — **a pid alone is not
an identity, because Windows reuses pids within seconds** — and one missing the
client name; a lock file that is not ours has to stay a different answer from
*this directory is free*. `SessionStore` refuses a `user_version` it does not
know, **with the version as the reason**, and there is no converter. Each refusal
names a recovery and says that repeating the call will fail identically.

**Seven authored tools, and `session` and `why` are both mandatory.**
`browserai_init`, `browserai_resume`, `browserai_catch_up`, `browserai_list`,
`browserai_destroy`, `browserai_set_purpose` and `browserai_reinstall_browser`. **Two** parameters are
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
restatement. **And not `browserai_catch_up`, which names a session and is the one
exception**: a tool whose whole purpose is to tell you what happened must not
itself become the most recent thing that happened, and writing an entry means
writing to `browserai.data` — which only the directory's holder may do, and the
reader this tool exists for is by definition not the holder
(⚠️ ***corrected 2026-08-26, previously "writing an entry would mean replacing
`browserai.json` — which a session another live BrowserAI is driving refuses"***,
itself a correction of 2026-08-24's "taking the per-directory gate — which a
session another live BrowserAI is driving would refuse". Nothing is replaced now:
an entry is an `INSERT` on a connection the holder already has open, so the second
reason moved from *the write would be refused* to *the write is not this caller's
to make*). `ModelSurfaceTests` carries a row for it, so adding a `why` later is a
red build rather than a silent widening.

**`browserai_catch_up` reads the log back against the directory, and the two
routinely disagree.** `SessionInventory` walks the tree — total size, a breakdown
by folder, the last file written, any `.har`, and whether the profile holds a
cookie store — and the answer prints it beside the log under two headings so a
reader knows which source each fact came from. **The disagreement that matters is
credentials**: cookies arrive from *navigation* rather than from tools, so a
log-only answer would report *"no credential tools were used"* about a directory
holding a live signed-in profile. It is **read-only and takes no lock it can be
refused by**: the store is opened read-only, and the walk opens no file inside the
directory, so a session another BrowserAI is driving answers normally.
Nothing here reads a cookie database — the answer a caller acts on is *this may
hold credentials*, which the file's existence settles.

⚠️ ***Corrected 2026-08-26 (previously "the record is read the way
`browserai_list` reads one — which since 2026-08-24 means under that directory's
own gate at a zero timeout").*** There is no gate on this path: the listing's
liveness question is one `CreateFile` on `browserai.lock` and never a database
open at all. **And read-only is not the same as side-effect-free, which was
measured rather than assumed**: a read-only open against a crashed holder's
uncheckpointed write-ahead log *recovers* the log and answers with the newest
rows, because `SQLITE_OPEN_READONLY` constrains the database file and not the
directory — and building the wal-index leaves a `-shm` beside the store. It is
refused only where the caller may not create that file. **`browserai_catch_up`'s
own description says so**, because that tool is the reader a caller reaches for.

⚠️ **And the reader was not always a caller: starting the server opened the store
of every session on the machine — closed 2026-08-26.** *The three places this side
effect was recorded all described a caller-initiated read of the directory the
caller named, and none of them said this.* `Program.Main` starts the stray sweep
in the background; one pass calls `SessionIndex.Sweep`, which followed **every**
entry in the machine-wide index through `SessionLock.ReadRecord`, which is a
`SessionStore.OpenForReading`. **One process start was one store open per
registered session on the host**, each leaving a `browserai.data-shm` and a
`browserai.data-wal` in a directory nobody named. Measured through the published
binary: a cleanly-closed session held `[browserai.data, browserai.lock]`; a
second BrowserAI was started and sent nothing but `initialize`, and both files
were back.

**The sweep goes probe-first now, and nothing was given up for it.** The record
was never part of its decision: it read one only to fill an inventory a sweep does
not print, and every removable state it acts on — `DirectoryMissing`,
`VolumeMissing`, `NotASession` — is settled by the directory and the guard.
`SessionIndex` walks at a stated **depth**: `Record` for the reporting callers
(`browserai_list`, the roll-up, the live-set read), `Guard` for the sweep, where
liveness is one `CreateFile` on `browserai.lock` and the fallback is two file
checks. **A server start on a machine of live sessions opens no store at all**,
and the only entry whose record is read in full is the one a pass is about to
act on — where there is nothing to open, because a removable entry is one whose
`browserai.data` is absent. `SessionIndexTests.ASweepOpensNoSessionsStoreAndLeavesACleanlyClosedOneAtTwoFiles`
asserts the untouched directory, with `Follow` still carrying a record as its
positive control.

**Both answers report the session's output size, and nothing is ever
auto-deleted.** `browserai_list` and `browserai_catch_up` state what the
directory weighs; deletion is `browserai_destroy` and nothing else. *(Q100=e,
2026-08-25.)* **`catch_up` is paged**: an optional `page`, about a hundred
entries a page, numbered **oldest-first** so a page number keeps meaning the same
entries as the log grows, each page stating its number, the total and the call
that fetches the next. The volatile material — the inventory, the in-use line,
the ages — appears on page 1 only.

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
purpose change, and every browser call the session forwarded **and every call it
refused** are rows in one ordered table in `browserai.data`, written by the
directory's holder — so a reader sees *the human changed the purpose here*
between the calls it explains rather than merging two streams by timestamp. **A
call whose row cannot be written is refused rather than forwarded**, and
`SessionLock.Append` throws rather than swallowing.

⚠️ ***Corrected 2026-08-26, in four places (previously "entries in one ordered
list in `browserai.json`, under the same session-long lock"; "a call BrowserAI
**refused** never reaches the list at all: this records what the session *did*,
and the refusals are in `browserai.log` beside it"; "the cost is a whole-record
durable write per call"; and "What is stored for an argument — every name always,
`value` and `text` never, an object or array as a shape, everything else cut at
200 characters — is `LoggedArgument`'s to say").*** **`browserai.log` is gone**,
so the record is the only place *the agent reached for a tool this build will not
forward* survives at all — which makes the log replay rather than diagnostics, and
is why the refusal is a row. **No argument is recorded**, and `LoggedArgument` is
deleted: the caller's `why` is what the row says the call was for, which is a
better answer to the same question and needs no list of upstream parameter names
to stay honest. **The cost is an `INSERT`**, not a whole-record rewrite.
[QUESTIONS.md §14](QUESTIONS.md#14-the-one-time-ordered-log-lives-inside-browseraijson--decided-by-the-maintainer-over-my-recommendation)
carries the reversal and what survived of the reasoning.

**A row's outcome has three values and a settle time.** It is written `in-flight`
**before** the call is forwarded — the property the write-before ordering always
existed for, so a call that never returns still leaves a record — and updated to
`successful` or `failed` with `settled_at`, from which the duration is derivable.
`browserai_catch_up` renders a stale `in-flight` as *"no answer was recorded"*.
**Failure payloads only:** the child's error bytes, its JSON-RPC error or the
transport exception with its stack go into `failure`; a successful call stores no
payload. ⚠️ **The settle happens in a `finally`, after the answer has gone out**,
which is deliberate and has a visible transient — see
[the hazard index](HAZARDS.md#hazard-index).

`init` takes a required directory and purpose with
no default and no fallback, an optional `browser` defaulting to `chromium`, and
the three per-run booleans `headed`, `tracing` and `debug`; `resume` takes the
same three and reads `browser` from `browserai.data`, **refusing it as an
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
`browser_annotate` is refused on a mode that opens no window").*

⚠️ ***Corrected 2026-08-26 (previously "Implemented by
`SessionToolPolicy.IsWithheldFromTheSurface` and `SessionToolSurface.Rewrite`").***
That type is deleted. **The judgement is a file now** —
[`tool-verdicts.json`](tool-verdicts.json), one row per tool, shipped inside the
payload it describes and read at startup — and the sentence above is a fact about
what the file says rather than about what the code decides. `browser_annotate` is
still the only `deny` this build ships, and the reasoning that was a doc comment
beside a C# constant is now that row's own `why`, which **is** the refusal a
caller reads. Implemented by `ToolVerdicts`, `SessionToolSurface.Rewrite` and
`BrowserProxy.AnswerToolsCallAsync`; the section below is what the file buys that
a constant could not.

### The verdicts file, and why a tool nobody judged is refused

| Concern | Implemented by |
|---|---|
| What this build knows of every tool, and what it does with a call naming one | [`tool-verdicts.json`](tool-verdicts.json), tracked at the repository root |
| Reading it, and refusing to serve on one it cannot read | `src/BrowserAI/Sessions/ToolVerdicts.cs` |
| Where it lives at run time, and an incomplete payload naming itself | `PayloadLayout.ToolVerdicts`, `PayloadLayout.Verify` |
| Getting the tracked copy into the payload | `CopyToolVerdictsIntoThePayload`, in `src/BrowserAI/BrowserAI.csproj` |
| The door, and the advertised list | `BrowserProxy.AnswerToolsCallAsync`, `SessionToolSurface.Rewrite` |
| That it agrees with the golden snapshot, both directions | `ToolVerdictTests` |

**Three verdicts.** `allow` forwards the call to the child of the session it
names, byte-identical. `deny` refuses it at BrowserAI's door **and** drops the
tool from `tools/list` entirely — dropped, not disabled, because a tool that can
never succeed costs attention and description budget for as long as it is in the
list — carrying the row's own `why` as the refusal and a `since` as provenance.
`answer` is one of BrowserAI's own seven, which never had a child to reach.

**DENY BY DEFAULT: a name with no row is refused too, and that is the half worth
arguing.** It reverses a decision taken 2026-08-18 — and it reverses it for a
reason that decision's reasoning does not reach. What went that day was a
`(tool, mode)` **permission** matrix, removed because it was never a boundary
against a caller who owns the session directory; every word of that still holds
and nothing here is a permission. A verdict decides something else: whether a name
this build has never been told about is worth **starting a browser** for. Upstream
creates the browser context *before* it looks a tool name up — the CLI factory's
`create` at `coreBundle.js:73101`, the name lookup at `:65533` — so a forwarded
call naming nothing launches a browser to be told there is nothing to run, and
answers by echoing the caller's own string into model-facing text.

**What bounds the cost is a red build rather than a promise.** `ToolVerdictTests`
compares the file with `upstream-snapshots/tools-list.json` in **both**
directions on every run: a tool in the snapshot with no row fails, and a row
naming a tool the snapshot does not carry fails. A Playwright bump that adds a
tool therefore reddens the suite in the same pass that reddens the snapshot diff,
and the two are adjudicated together — the **comparison** is on every build, the
**adjudication** is [`RELEASING.md` item 4](RELEASING.md#4-the-four-snapshots-and-the-verdict-file-adjudicated).

**An unjudged tool is still advertised; a denied one is not.** The asymmetry is
deliberate. A denial is a decision, so there is nothing for a model to weigh; an
absence is a gap, already loud on the same build, so dropping it would add
nothing — and filtering on *absence* would turn a verdicts file that failed to
load into a silently empty surface. Which is why a missing or malformed file is a
**startup failure naming the file**, never an empty set: under deny-by-default,
empty means refuse everything, and a silent fallback would present as a server
that starts, advertises a full surface and then refuses every call.

**Lifetime is one timer and no expiry.** A ten-minute browser-idle timer closes
the browser and keeps the node child; the relaunch on the next call is upstream's
own lazy creation, so nothing was built for it. Teardown is stdin EOF plus a
client-liveness watcher — an `OpenProcess` handle signalled on the client's exit,
never a poll and never a ping — and there is deliberately no close tool.

## Locking, ownership and the sweep

### The guard: six properties, and where each one lives

**`browserai.lock` is the whole of who owns a session directory, and it is a
plain file held open.** It is opened `FileAccess.ReadWrite, FileShare.Read` for
the session's life and written **once**, at acquisition, by
`Storage/LockFile.TakeAndWrite`. The six properties below are the constraint any
storage proposal has to meet; they are listed because they are the reason the
guard did **not** move into SQLite when the record did, and because two of them
live in an argument list where nothing reads like a mechanism.

| # | Property | What produces it |
|---|---|---|
| 1 | **One writer per directory**, across processes | `LockFile.Hold`'s `FileShare.Read`. A second `ReadWrite` open is refused by the kernel — no registry, no token, no lease |
| 2 | **Readers proceed *and see live data*** | The same share mode admits every reader, and what they read is `browserai.data`, a different file — so the guard never has to admit anybody to itself |
| 3 | **Held for the session's whole life** | The `FileStream` is a field of `LockFileHold` and nothing closes it. Written once and never rewritten, which is what removes the per-call unheld window the old record opened |
| 4 | **Released by the OS on death, however it dies** | A handle. No expiry, no heartbeat, no reaper — *stale* and *alive* are distinguishable without guessing because the kernel already knows |
| 5 | **Cheap to observe from a third process** | `LockFile.Probe` — one `CreateFile` and one `CloseHandle`, measured at **0.035 ms** free and **0.049 ms** held, with no directory walk, no process open and no database. That is what makes `browserai_list` affordable over a whole tree |
| 6 | **It names the holder** | The file's content: pid, process-creation FILETIME, and a display-only client name. Roughly a hundred bytes of indented UTF-8, because a person opens this file |

**Why no database engine can be the guard.** SQLite's own `xOpen` asks Windows
for `FILE_SHARE_READ | FILE_SHARE_WRITE` or for nothing at all. The first makes
property 1 unobservable — a probe reads a driven session as free; the second
breaks property 2 — a reader is locked out of the record it came for.
`FileShare.Read` is the one predicate this design needs and the one no engine
offers.

⚠️ **The probe cannot be made harmless, and that is a property rather than an
oversight.** To be refused by a holder's `FileShare.Read` it must ask for access
outside `Read` — so for the instant its handle lives it would refuse a holder's
own re-open. *Detecting an owner and blocking one are the same capability.* Its
share mode is deliberately wider than a holder's, `Delete` included, because a
narrower one would refuse a concurrent `browserai_destroy` for as long as the
handle lived.

**Two literals carry properties 1 and 5, and a scan holds them there.**
`HouseRuleTests.TheSessionGuardsTwoLoadBearingLiteralsAreTheOnesItIsWrittenWith`
reads `LockFile.cs` as text and fails a hold that shares writes or a probe that
asks only to read. Neither perturbation breaks a behavioural test — both leave a
file that opens — which is exactly why the rule is written down rather than left
to a reviewer. *(Q119, 2026-08-26.)*

### The identity chain

**One canonicalisation function feeds one identity chain, and the chain feeds
three consumers.** `CanonicalPath.Of` answers *what does the filesystem call
this*; `SessionPath` then does `TrimEnd('\')` → `ToUpperInvariant` → SHA-256 →
hex, producing the mutex name, the record file's identity and the index key
alike. ⚠️ *Corrected 2026-08-26 (previously one function beginning with
`GetFullPath`)* — the split is what lets `browserai_list` ask the first question
about a volume root without asking the second, which it must not, and it is why
that listing stopped answering *no sessions here* about a junction over the tree
they are in. `LockScopes` names the three scopes;
`MachineMutex` creates `Global\` names **only** and throws on anything else, with
no `Local\` fallback anywhere and a test that fails the build if any other file in
`src/` constructs a named waitable object.

**Two spellings never reach that function, because every spelling is resolved
into one first.** ⚠️ *Corrected 2026-08-26 (previously "…because two spellings
are refused first. `SessionDirectoryGuard` runs at `browserai_init` and
`browserai_resume`…"). That sentence was false the day it was written*: `destroy`,
`set_purpose`, `catch_up` and every forwarded call reached the identity chain
without the guard, so two spellings did reach it. `Sessions/CanonicalPath` runs
at **every** door, before anything is created and before the gate is taken, and
asks its questions in a fixed order that is itself the design: characters, then
the object manager (`QueryDosDeviceW`, then `GetDriveTypeW` on a letter that is
not a substitution), then one `GetFinalPathNameByHandleW` per level climbed —
which is the only step that opens anything, and the first two exist to ensure
that open is local. A drive letter mapped to a dead hostname was measured at
**22,210 ms for one `File.Exists`**
([kb](kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds)).
The Win32 half is `Interop/VolumeIdentity`; the policy half, including what it
knowingly cannot see, is `Sessions/CanonicalPath` and
[the decision](DECISIONS.md#one-path-function-normalise-what-is-cheap-refuse-what-is-not).
**8.3 short names are not in that list** — `Path.GetFullPath` expands them, so
they arrive canonical.

**A path BrowserAI stored is checked and never re-resolved**, which is what keeps
the machine-wide read path free: `SessionIndex` follows one entry per session on
the host on every listing, every roll-up and every sweep, and resolving an alias
there would be a directory open per entry. `PathOrigin.Read` runs the subset of
the same questions that cost nothing, so a stored path that is not the spelling
this build writes is `Unusable`, is swept, and is recorded again canonically by
the next `init` or `resume`.

**Acquisition never waits.** It is zero-timeout and answers contention with the
holder's pid, start time, lock time and purpose. The one bounded wait is the
create-or-take gate, and an `AbandonedMutexException` on it is a distinct
`AcquiredAbandoned` outcome that is logged and proceeded through.

**One process's own two callers are serialised in process, and that is a
different lock from the gate.** `SessionManager` deliberately serialises nothing
— `_live` is a `ConcurrentDictionary` — so a `browserai_set_purpose` and a
`browserai_destroy` naming one session reach one `SessionLock` at once.
`Append`, `Settle`, `SettleOpening`, `AppendPurpose`, `ReleaseAndDelete` and
`Dispose` therefore each hold `SessionLock._inProcess` for their whole body,
caller delegates included, so a disposal **waits for** an in-flight mutation
instead of disposing the gate underneath it. *(Corrected 2026-08-26, previously
"`Rewrite`, `Append`, `ReleaseAndDelete` and `Dispose`" — `Rewrite` is gone and
three settle paths arrived.)* It carries a second job since the store arrived:
`Append` is an `INSERT` followed by `last_insert_rowid` on **one** connection,
and two threads interleaving there would hand one call the other's row id, after
which a settle lands on somebody else's entry. Without the exclusion the old
rewrite re-opened `browserai.json` into a disposed lock, `_gate.Release()` threw,
and every later `TryAcquire` on that directory answered `Held` naming a pid with
no session for the life of the process —
[the adversarial review](docs/reviews/2026-08-18-adversarial-locking.md)'s B4,
closed 2026-08-24. It is **not** a fourth scope: `LockScopes` still names three
machine-wide objects in one place, and this one has no name and no kernel object.

**A contender probes for the holder in front of that gate, and only in front of
it.** `SessionLock.ProbeForHolder` opens `browserai.lock` before the
per-directory mutex is created: a sharing violation is the kernel's answer to
*who owns this*,
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
is stated once. The listing prints **in use: YES / no / UNKNOWN** per entry and
**never names the holder**, because a sharing violation says the file is held and
not by whom — the record inside can describe a previous one. It costs one
`CreateFile`/`CloseHandle` per entry, measured at 0.035 ms free and 0.049 ms held
against 0.6–2.3 ms for the size walk the same loop already performs
([kb](kb/windows/detection.md#the-pre-gate-probe-as-a-liveness-report--measured-2026-08-20)),
and a session this process is already driving is answered from its own live-session
map without asking the kernel at all.

⚠️ ***Corrected 2026-08-26 (previously a 2026-08-24 correction reading "The rule
did come apart… The report now goes through
`SessionLock.ProbeLivenessUnderTheGate`, which asks the same question with that
directory's gate held at a **zero** timeout … a mutex create, a zero-timeout
acquire, a release and a close are now in the per-entry cost").*** **The defect
that correction closed no longer exists and neither does the code that closed
it.** It rested on the rewrite: every forwarded call replaced `browserai.json`
whole, dropping the ownership handle and taking it back, so a busy session's
record was periodically *present and unheld* and a bare probe answered `NotHeld`
about a session another agent was driving. `browserai.lock` is written once and
never rewritten, so **an absent guard now genuinely means free**,
`ProbeLivenessUnderTheGate` is deleted, and the per-entry cost is the one
`CreateFile`/`CloseHandle` the figures above measure. **One window is left and it
is pinned rather than argued away**: a peer between its own rename and its first
hold, inside the gate, once per session —
`SessionListTests.APeerInsideCreateOrTakeIsTheOneWindowTheListingCanStillMisreport`.

**The listing no longer parses every record on the machine to print a few.** The
subtree filter used to run *after* `SessionIndex.Follow` had opened and strictly
parsed each session's `browserai.json` — up to 250 log entries and all their
arguments — and each of those opens inherits `RenameWindow`'s budget, so one
denied or scanner-held record anywhere could add it to a call scoped somewhere
else entirely. The roll-up did the same on **every `init` and every `resume`**.
`SessionIndex.FollowUnder` applies the prefix above the open instead; the
predicate is unchanged and the reported set is identical.
`HouseRuleTests.NoIndexWalkFiltersBySubtreeAfterFollowingTheEntry` is what keeps
it there; `HouseRuleTests.TheThreeWholeMachineIndexReadersStillTakeTheWholeMachineRead`
names the three that must not move; and `Follow()` stays whole-machine for the
sweep, the reinstall census and the stray sweep.

⚠️ ***Corrected 2026-08-24, same day (previously the paragraph stopped at "the
reported set is identical", with the budget exposure attributed entirely to the
`browserai.json` open).*** **What moved is the strict parse, not the open count.**
`SessionIndex.FollowOne` opens each *index entry file* through
`RenameWindow.WaitOut` before it can read the pointer the prefix is tested
against, so a subtree-scoped call still performs **one such open per index entry
on the machine**, each carrying `RenameWindow`'s whole 30-second budget. One
denied or delete-pending entry file anywhere on the host still adds up to that
budget to a `browserai_list` scoped to an unrelated tree, and to the roll-up on
every `init` and every `resume`. What is gone is the *record* open and its parse,
which is the larger of the two and the only one this change touched.

**The guard's write is durable and atomic, and it happens once.**
`WriteThrough` + `Flush(flushToDisk: true)` + `File.Move(overwrite: true)`, with
the temp file in the target's own directory, because a rename is atomic only
within one volume and cheap only within one directory. A rename cannot replace a
file whose handle is open under any share mode, so acquisition is write → rename
→ hold *inside* the per-directory mutex. ⚠️ *Corrected 2026-08-26 (previously
"**Writes** are durable and atomic … create-or-take is close → rename →
re-open").* **The record's writes are `INSERT`s** and are durable by SQLite's WAL
rather than by a rename, so the rename window is paid once per acquisition
instead of once per forwarded call — and there is no close-and-re-open at all,
which is what dissolved the two hazards that lived in it.

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
directory holds a `browserai.lock` this sweeper can take itself, without writing
to it — `SessionLock.TryHoldUnowned`, never `TryAcquire`, because a janitor is
the last party that should be writing a holder row into a crashed session's own
history.

**Both families are offered, and everything below the front door reads the family
from the session's own record.** `browserai_init` accepts `chromium` or
`firefox`; provisioning, the config generator, the launch preflight and the stray
sweep all take it from `browserai.data` rather than assuming one, so a Firefox
record can never be run as Chromium against a Firefox profile. *Corrected 2026-08-19
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
| Logging — one machine-wide file under a cross-process write gate | `src/BrowserAI/Logging/`, `src/BrowserAI/Interop/NativeFile.cs` *(the per-session file went 2026-08-26)* |
| Where files live, installed or not | `src/BrowserAI/Hosting/{IAppPaths, LocalAppDataPaths, BuildVersion}.cs`, `src/BrowserAI/Updates/InstallLocation.cs` |
| Refusing to serve out of a root two users could share | `src/BrowserAI/Hosting/InstallRootScope.cs`, called from `Program.Main` before anything creates state |

⚠️ ***Corrected 2026-08-26 (previously "Anything attributable to a session is
written to that session's own `browserai.log` and to nothing else; the
machine-wide log keeps only what no session owns … *Changed 2026-08-24
(previously every session record went to both)*").*** **There is no per-session
log file.** Everything it carried is on stderr, which `ProcessLog.OpenSessionLog`
already wrote to at every level, and what a session itself did is rows in
`browserai.data` — including, since the same day, the refusals that used to reach
only that file. The console half of `OpenSessionLog` stays and gained
`IncludeScopes`, because it is the only sink left carrying `session=`. **The
machine-wide log is unchanged**: startup, updates, provisioning, the stray sweep,
the server transport, the MCP server, and the proxy refusals that have no session
directory to be written into. **It is written under a lock rather than instead of
one**, and that is unchanged too: `NativeFile.TakeGate` takes an exclusive
byte-range claim one byte past any possible end of file — so no concurrent reader
is ever refused — and the length read, the write stamp and the bytes all happen
inside it. Write order and timestamp order therefore coincide, the file is sorted
by construction, and rotation happens exactly at the cap rather than near it.

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

## The session's output directory

⚠️ **Rewritten 2026-08-26. This section described artifact routing, and artifact
routing is deleted.** What was here: *three levers, and they only work
together* — the child's working directory, a `filename` rewritten to an absolute
path in the folder its generator prefix implied, and an answer carrying the
resolved path, the session-relative path, any rename, the session's cumulative
size and an index path — plus an inline-image restoration, a prefix-set coverage
gate in both directions, a string validator refusing nine path shapes, and a
*never overwrite* rule built on suffixing and an in-flight reservation set.
Around 1,200 lines. **The doctrine that replaced it is one sentence: nothing
between the two servers except the session system and the reason system.**

**`<session>\output\` is flat and BrowserAI adds nothing to it.** The child's
working directory *is* that folder and so is its `outputDir`, so a caller's
plain `login.png` is resolved there by upstream and written there by upstream,
under the name the caller chose. Upstream's own subdirectories appear inside it
when upstream makes them — `traces\`, `session-<stamp>\` — because they are
upstream's. The one path BrowserAI still chooses is the HTTP Archive's, because
it is a launch-time config value rather than something a tool names, and the
choice it makes is the output root.

**Containment is upstream's file-access roots and nothing else.**
`allowUnrestrictedFileAccess` is written `false` — explicitly, so the decision is
recorded and `browser_get_config` can read it back — and upstream's `checkFile`
then refuses any resolved name outside `outputDir` or the working directory.
BrowserAI writes both as the same folder, so the two roots coincide instead of
overlapping, and the refusal is upstream's own sentence forwarded byte-identical:
*File access denied: `<path>` is outside allowed roots.* Measured through the
published binary against a real Chromium, both directions, by
`FileAccessRootTests`.

⚠️ **Two properties were lost with the gate and are open hazard rows rather than
oversights.** A second file with a name already taken **overwrites** the first,
because nothing suffixes any more; and a reserved device name or a trailing
space or dot is stored as Windows rewrites it rather than being refused. The
`init` answer tells the caller about the first. Both are in the
[hazard index](HAZARDS.md#hazard-index) with what would close them.

| Concern | Implemented by |
|---|---|
| Where the folders are | `src/BrowserAI/Sessions/SessionLayout.cs`, `src/BrowserAI/Hosting/IAppPaths.cs` |
| The child's working directory, `outputDir` and the file-access roots | `src/BrowserAI/Sessions/SessionManager.cs`, `src/BrowserAI/Runtime/{BrowserConfiguration, ChildLaunch}.cs` |
| The per-root roll-up beside the sessions | `src/BrowserAI/Sessions/SessionRollUp.cs` |

**`outputMaxSize` is never written, and it matters more now than it did.**
Upstream's `_enforceOutputBudget()` runs on every tool response and unlinks
oldest-first across the whole output tree, sparing only the current response's
own writes — and a download now lives in that tree permanently rather than being
sorted out of it, so it would be the first thing evicted.
`FlatOutputTests.NothingBrowserAiGeneratesCanTurnEvictionOn` holds the config
door and `ChildEnvironmentTests` holds the environment one.

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
| Whether this machine could have seen a browser take the foreground at all — read, reported in the coverage block, never repaired | `tests/BrowserAI.Tests/Harness/ForegroundLock.cs`, `tests/BrowserAI.Tests/ForegroundLockTests.cs` |
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
