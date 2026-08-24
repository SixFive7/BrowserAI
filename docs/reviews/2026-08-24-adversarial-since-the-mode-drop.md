<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Adversarial review 2 — 2026-08-24

Scope: the seams where the 2026-08-20/24 changes (mode removal, the `why`
injection, the durable action log, `browserai_catch_up`, the reader/writer
maintenance lock, the instance-directory marker) meet the code that predates
them. Read-only pass over `src/`, `upstream-snapshots/tools-list.json`, and the
2.2.0 SDK source. Measurements below were taken on this machine on 2026-08-24
**while three other agents were running test suites**, so timings are an upper
bound on a loaded machine; the shapes, not the digits, are the claim.

Findings are ordered by whether the defect is reachable in ordinary use, not by
how clever it is. Nothing was changed.

---

## F1 — `browserai_list` reports **`in use: no`** for a session that is being driven right now

**What breaks.** `SessionLock.Rewrite`
(`src/BrowserAI/Sessions/SessionLock.cs:385-435`) closes the session's own
`browserai.json` handle, writes a temp file, renames it over the target and
re-opens. That is what `Append` is
(`src/BrowserAI/Sessions/SessionLock.cs:373-378`), and since 2026-08-20 `Append`
runs on **every forwarded browser tool call**
(`src/BrowserAI/Proxy/BrowserProxy.cs:521`). So `browserai.json` is
**periodically unheld while a session is being actively driven**, once per call.

`browserai_list`'s per-entry liveness report calls `SessionLock.ProbeLiveness`
**directly, with no gate** (`src/BrowserAI/Sessions/SessionManager.cs:909`), and
renders a `NotHeld` answer as:

> `in use: no — nothing held '<…>\browserai.json' at the instant this listing
> looked. It is a snapshot rather than a reservation: another agent can open the
> session immediately afterwards.`

The hedge is about the *future*. The answer is wrong about the *present*: the
session is open, a browser is running, another agent is driving it.

This is also the one place the rule the probe was extracted to protect is
broken. `ProbeForHolder` states it — *a sharing violation may be read as owned
and nothing else may be read as free*
(`src/BrowserAI/Sessions/SessionLock.cs:831-845`, which answers `null` and falls
through to the gate). `InUse` reads not-held as free.

**Measured** (2026-08-24, this machine, loaded; .NET via `Add-Type`, replaying
`Rewrite`'s exact sequence — `Dispose` → `CreateNew`+`WriteThrough`+
`Flush(flushToDisk: true)` → `File.Move(overwrite: true)` → re-open — 200
iterations after a 50-iteration warm-up):

| Record size | Unheld window per rewrite | Uncontended `Global\` mutex acquire |
|---|---:|---:|
| 1 KB | **3.94 ms** | 0.007 ms |
| 40 KB | **10.72 ms** | 0.009 ms |
| 400 KB (the documented cap) | **13.62 ms** | 0.009 ms |

The floor is `FlushFileBuffers`, not the byte count. Against a browser call that
round-trips in 50–500 ms, `browserai.json` is unheld for roughly **1–25 % of the
time a busy session is being driven**, and the fraction rises as the log fills
toward the 250-entry cap. *(The call cadence is an assumption, not a
measurement — I did not launch a browser.)*

**How it is reached.** `browserai_list` against any root holding a session
another BrowserAI is driving. No race construction, no hostile input, no second
spelling. It is the ordinary case for the tool.

**What it does not break.** Every *decision* path is still safe, because it takes
the per-directory gate first and `Rewrite` holds that same gate across the whole
window — see the "could not break" list below. The damage is confined to the
report, and the report is what an agent reads before deciding whether to touch
somebody else's session.

**Cost to fix.** Small in code, a decision in shape. The `NotHeld` branch has no
way to distinguish *nobody has this* from *the holder is mid-rewrite*; the
information that would settle it is the gate, which the report deliberately does
not take. Options span "re-word `no` to `not at this instant — a driving session
releases the file for milliseconds on every call`", "probe twice a few ms apart",
and "take the gate at zero timeout and report `UNKNOWN` when it is held".

---

## F2 — Page content can trigger BrowserAI's one answer-rewrite, which silently drops the artifact record and the result note

**What breaks.** `BrowserProxy.Remediate`
(`src/BrowserAI/Proxy/BrowserProxy.cs:638-663`) scans **every text block of every
child answer** — there is no `isError` gate — for the literal `install-browser`
(`src/BrowserAI/Runtime/ProvisioningRemediation.cs:60`) and then applies

```
Run `[^`]*install-browser[^`]*` to install\.?
```

(`src/BrowserAI/Runtime/ProvisioningRemediation.cs:108`), replacing the match
with BrowserAI's own three-sentence instruction.

When it fires, the call takes a different exit
(`src/BrowserAI/Proxy/BrowserProxy.cs:591-600`):

```csharp
if (Remediate(response) is { } corrected)
{
    live.Artifacts.Release(plan);
    ProxyLog.RemediationRewritten(_logger, tool, live.Location.FullPath);
    await caller.SendMessageAsync(new JsonRpcResponse { Id = request.Id, Result = corrected }, …);
    return;
}
```

`live.Artifacts.Complete(plan, …)` at line 616 is **never reached**. So on that
path, for a call that did write a file:

- the artifact is **not recorded** in the session's artifact index;
- the answer carries **no `ResultNote`** — no absolute path, no session-relative
  path, no rename, no cumulative size, no index path;
- `NoteWhatTheAnswerPublished` never runs, so a console log or snapshot `.yml`
  this answer pointed at is movable by the next call's sweep — the exact defect
  that method exists to close;
- the in-flight name reservation is released while the file is on disk.

And the model reads BrowserAI's own instruction text ("Call `browserai_init`
again to re-provision it…", "Do NOT run npx or npm…") spliced into the middle of
what is otherwise page content, indistinguishable from it.

The reasoning that made this safe is stated in the code and is the thing that
stopped being true: *"on the paths where it appears at all the answer is already
a failure with no bytes worth preserving"*
(`src/BrowserAI/Proxy/BrowserProxy.cs:630-637`). Upstream tool answers carry
**page** text — `browser_snapshot`, `browser_console_messages`,
`browser_evaluate`, `browser_find`, `browser_network_request` — so the marker is
reachable from the page, not only from upstream's error.

**How it is reached.** Two ways, and the accidental one matters more:

1. **By accident.** An agent browses a page that documents `@playwright/mcp`.
   Upstream's own README, a GitHub issue, a blog post — anything rendering the
   sentence ``Run `npx @playwright/mcp install-browser chromium` to install.``
   A `browser_snapshot` of that page trips it.
2. **Deliberately**, and this is the brief's item 3: `browser_route` became
   reachable on 2026-08-20. A mocked response body containing that sentence,
   then a navigation, then a snapshot. The tool that "renders as if it came from
   the server" now also decides whether BrowserAI rewrites its own answer.

**Cost to fix.** Small. The marker check has no context: candidates are gating it
on the answer's `isError`, on the child having reported the browser missing (a
state BrowserAI already tracks in `ProvisioningRefusal`), or on requiring the
whole of upstream's sentence rather than a clause. Separately: the early return
at line 593-599 should not be the only path that skips `Complete`.

---

## F3 — A log entry's **argument name** is uncapped, un-flattened, caller-controlled, and replayed verbatim into another agent's context

**What breaks.** `LoggedArgument.Of`
(`src/BrowserAI/Sessions/LockRecord.cs:1221-1234`) applies its whole policy to
the **value** and none of it to the **name**:

```csharp
public static LoggedArgument Of(string name, JsonNode? value)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    …
    return new LoggedArgument(name, Summarise(value));   // name passes through untouched
}
```

`SessionManager.Entry` (`src/BrowserAI/Sessions/SessionManager.cs:1624-1645`)
records **every** property of the caller's `arguments` object except `session`,
`why` and (on `init`) `purpose`. Nothing checks the name against the tool's
schema, nothing caps its length, and nothing counts them. `browserai_catch_up`
then prints them straight into the answer
(`src/BrowserAI/Sessions/SessionManager.cs:732`):

```csharp
.Append(string.Join(", ", entry.Arguments.Select(argument => $"{argument.Name}={argument.Value}")))
```

Measured round trip (2026-08-24): `Utf8JsonWriter` under
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` escapes a newline inside a name as
`\n`, so the record stays valid JSON — and `Utf8JsonReader` hands it back as a
**real newline**, which `catch_up` appends as a real newline. So a caller-chosen
argument name reaches the next agent's context with its line structure intact and
no length bound.

This is precisely the property `purpose` and `why` have mechanisms for.
`LockRecord.cs:104-109` says so in as many words about `purpose`: *"Free text
written by one agent and replayed into another's context is a channel between
agents; the cap and the control-character strip are what keep it data."*
`PurposeMaximumLength = 2000`, `WhyMaximumLength = 400`,
`ArgumentValueMaximumLength = 200`, and every one of them runs through `Flatten`.
The argument **name** goes through none of them.

The size consequence follows from the same gap. `MaximumLogEntries = 250` bounds
the record by *count*; the per-entry bytes are bounded only if every field is
bounded, and one is not. 250 entries × one megabyte-long argument name is a
250 MB `browserai.json` — re-read, re-serialised and durably re-written on every
subsequent call, and fully parsed by every `browserai_list`, every `catch_up` and
every `resume` on the machine. The documented worst case of "roughly 400 KB"
(`src/BrowserAI/Sessions/LockRecord.cs:153-158`) holds only for well-formed
callers.

**How it is reached.** The benign half is ordinary: a model hallucinates a
parameter name and it is written down verbatim — harmless, and arguably correct
(QUESTIONS §14 wants every name recorded so a filled password field is visible).
The weaponised half needs a caller that wants it: an agent under prompt injection
writing one crafted `tools/call`, or any client that constructs arguments from
untrusted text. The entry is written **before** the call is forwarded, so the
child's own `additionalProperties: false` rejection does not prevent the record.

**Cost to fix.** Very small and local: `Of` already has the string in hand.
Whether it should be `Flatten(name, N)`, or a refusal, or a name checked against
the tool's advertised `properties`, is the decision — the third also changes what
"every argument name, always" means, and QUESTIONS §14 chose that wording
deliberately.

---

## F4 — `browserai_destroy` and `browserai_set_purpose` take the per-directory gate without running `SessionDirectoryGuard`

**What breaks.** `ARCHITECTURE.md` states: *"**Two spellings never reach that
function, because two spellings are refused first.** `SessionDirectoryGuard` runs
at `browserai_init` and `browserai_resume`, before anything is created and before
the gate is taken."* The second clause is false for two tools that do take the
gate:

| Tool | Resolver | Guard runs | Takes the per-directory gate | Mutates |
|---|---|---|---|---|
| `browserai_init` | `ResolveToOpen` (`SessionManager.cs:445`) | yes | yes | yes |
| `browserai_resume` | `ResolveToOpen` (`SessionManager.cs:553`) | yes | yes | yes |
| `browserai_destroy` | `Resolve` (`SessionManager.cs:926`) | **no** | **yes** | **deletes the tree** |
| `browserai_set_purpose` | `Resolve` (`SessionManager.cs:1046`) | **no** | **yes** | **rewrites the record** |
| `browserai_catch_up` | `Resolve` (`SessionManager.cs:683`) | no | no | no |
| `browserai_list` | `Subtree` (`SessionManager.cs:805`) | no | no | no |

Two consequences.

**The network-path half.** `destroy` accepts a UNC or mapped-drive spelling and
reaches `SessionLock.ReadRecord`, `ProbeForHolder`, `TryAcquire`,
`SessionLayout.SizeOnDisk` and two `TreeDelete` passes against it — the last of
which runs inside a hold of the per-directory gate. A mapped drive letter whose
host has stopped answering was measured by this repository at **22,210 ms for one
`File.Exists`**. That is the cost the guard exists to keep out of the critical
section. The blast radius is narrower than DECISIONS' framing suggests — the gate
is keyed on that directory, so only peers naming the same dead path wait — but
the calling conversation stalls for minutes with nothing to say why.

**The alias half.** `Resolve` is `GetFullPath` → `TrimEnd('\')` →
`ToUpperInvariant` → SHA-256, which resolves neither `subst`, a junction, a mount
point nor a mapped drive. So `J:\sess` and `C:\work\sess` produce **two mutex
names for one directory**, and `destroy` and `set_purpose` are exactly the two
mutating callers that can be given either. That is review A4's premise, still
live on two tools; the stated fix (refuse at the door) simply is not applied
there.

Two things narrow it, and both are worth stating because they are why this is
ranked here and not higher. `_index.Record` is called only from `OpenAsync`
(`SessionManager.cs:1753`), so an aliased `set_purpose` cannot publish a
duplicate index entry. And a live holder's `browserai.json` handle refuses an
aliased taker regardless of which mutex it queued on — the file is one file
whatever the spelling. What remains exposed is the rename window inside
create-or-take, which is the very thing the gate was fronted to serialise.

One non-racy consequence, single actor: `destroy` via an aliased spelling deletes
the tree and then calls `_index.Forget(location)` with the **aliased** key
(`SessionManager.cs:995`), so the real index entry survives pointing at a
directory that no longer exists until a later sweep removes it.

**How it is reached.** A caller naming a session by a `subst` drive or through a
junction, or naming a UNC path to `destroy`. `init`/`resume` refuse both, so a
BrowserAI-created session is never *opened* that way — but `destroy` and
`set_purpose` both work on a session this process never opened, which is what
makes the spelling the caller's free choice.

**Cost to fix.** One line each (`Resolve` → `ResolveToOpen`) if refusal is the
answer, plus whatever it costs that `destroy` can then no longer clean up a
session that legitimately lives on a share. `catch_up` and `list` are a separate
question — they take no gate, so refusing them buys only the caller's own
latency.

---

## F5 — Any I/O failure on `reinstall.lock` is reported as "BrowserAI is replacing the browsers … right now"

**What breaks.** `MaintenanceLock.TakeShared`
(`src/BrowserAI/Runtime/MaintenanceLock.cs:172-192`) returns `null` for
`IOException or UnauthorizedAccessException` — which covers a sharing violation
(correct), an ACL denial, a read-only directory, a full or failing volume, a path
that is too long, and an anti-virus filter holding the file. `InitAsync` and
`ResumeAsync` translate every one of them into
`SessionErrors.BrowsersAreBeingReinstalled`
(`src/BrowserAI/Sessions/SessionErrors.cs:533-542`), whose first sentence is
unhedged:

> `'<tool>' was not run and nothing was changed: BrowserAI is replacing the
> browsers under '<dir>' on this machine right now, and no session can start and
> no second reinstall can begin until that finishes.`

The catch's own comment anticipates this — *"Held by a reinstall, denied, or
unreachable … and `Describe` says which for the sentence"* — but `Describe`
returns the **last writer's** line, which is never truncated when a reinstall
ends, so on a machine that reinstalled an hour ago the message quotes that as
though it were current, and `ProgressOf` reports the (empty) staging directory as
*"the delete, or an extraction already under way"*. A caller is told to wait
minutes for a download that is not running, for a condition that is not the
cause.

The same shape appears one level up in `TheRootIsBusy`
(`src/BrowserAI/Sessions/SessionManager.cs:1318-1331`): when `LiveSessions()`
returns zero it concludes "another reinstall has it", on the strength of *"the
two causes are mutually exclusive by construction"*. That holds for the kernel's
refusal, not for the census — `LiveSessions` reads `_live` plus `_index.Follow()`
(`SessionManager.cs:1551-1578`), so a peer process's session whose index entry was
swept, or whose record will not parse, is invisible, and the whole diagnosis flips
rather than losing a line. The code says a session it cannot see "costs the
refusal a line"; at count zero it costs the refusal its subject.

**How it is reached.** An anti-virus scanner momentarily holding
`%LocalAppData%\BrowserAI\browsers\reinstall.lock`; a locked-down or roaming
profile; a full disk. Occasional rather than rare, and it lands on `init` — the
first call of every conversation.

**Cost to fix.** Small: `TakeShared` already knows which exception it caught, and
the two sentences differ only in their opening clause.

---

## F6 — A malformed `tools/call` argument answers `{"code":-32603,"message":"An error occurred."}` with no catalogue entry

**What breaks.** Two argument shapes escape `AnswerToolsCallAsync` as exceptions
rather than refusals.

1. **A non-string `session` or `why`.**
   `src/BrowserAI/Proxy/BrowserProxy.cs:425-426` reads both as
   `(arguments?[…] as JsonValue)?.GetValue<string>()`. Measured 2026-08-24:

   | Argument | Result |
   |---|---|
   | `"session": "C:\\x"` | returns the string |
   | `"session": 5` | **throws** `InvalidOperationException: An element of type 'Number' cannot be converted to a 'System.String'.` |
   | `"why": true` | **throws** `InvalidOperationException` |
   | `"why": null` / `{}` / `[]` | `as JsonValue` yields `null` — clean refusal |

2. **An empty-string argument name.** `LoggedArgument.Of` opens with
   `ArgumentException.ThrowIfNullOrWhiteSpace(name)`
   (`src/BrowserAI/Sessions/LockRecord.cs:1223`) — a guard clause for programmer
   error placed on caller-controlled input. `{"": 1}` parses into a `JsonObject`
   and enumerates (verified 2026-08-24), so `SessionManager.Entry` reaches it.
   The surrounding `catch` at `BrowserProxy.cs:522` filters
   `IOException or UnauthorizedAccessException`; `ArgumentException` is neither.
   `SessionManager.InvokeAsync`'s catch (`SessionManager.cs:391-399`) filters
   `SessionToolException or LockFileException` — also neither — so the authored
   tools have the same hole.

Read out of `ModelContextProtocol` 2.2.0's own source
(`src/ModelContextProtocol.Core/McpSessionHandler.cs`, tag `v2.2.0`,
`ProcessMessageAsync`'s catch at lines 266-312): an exception escaping an incoming
message filter is caught per-message, so **the session survives** — this is not a
denial of service. It is turned into
`JsonRpcErrorDetail { Code = InternalError, Message = "An error occurred." }`.

The asymmetry is the finding: `SessionManager.Optional` and `SessionManager.Flag`
(`SessionManager.cs:2293-2325`) check `GetValueKind()` first and produce
`'why' must be a string, and it arrived as Number.` The upstream tool path,
which is 58 of the 59 reachable entry points, does not.

**How it is reached.** A model emitting a number or boolean where the schema says
string. Occasional rather than routine, and it produces the one answer this
repository's founding complaint is about: a failure that names nothing and
suggests nothing, indistinguishable from any other internal error.

**Cost to fix.** Small: route both reads through the same kind-checked helper the
authored tools already use, and turn the `ThrowIfNullOrWhiteSpace` into a refusal.

---

## F7 — A site-named download plus the monotone `_published` set defeats artifact sorting

**What breaks.** `ArtifactRouter.NoteWhatTheAnswerPublished`
(`src/BrowserAI/Artifacts/ArtifactRouter.cs:759-789`) marks every loose file in
the output root whose **name is a substring of the child's serialised answer**,
and `_published` is a monotone `HashSet` that nothing ever removes:

```csharp
if (answer.Contains(name, StringComparison.OrdinalIgnoreCase))
{
    _ = _published.Add(name);
}
```

The defence is stated at lines 750-756: *"A generated name carries a millisecond
timestamp, so a false positive would need the answer to contain that exact string
for some other reason."* That is true of every name **upstream** generates. It is
not true of the one artifact class this same file says upstream does not name: a
browser-initiated download, *"named by the site rather than by an argument"*
(lines 93-98), which lands loose in the output root.

So a page chooses the filename, and a page controls answer text (a snapshot's
accessibility tree, a console message, an `evaluate` return). A one-character
download name matches essentially every answer; a chosen name matches an answer
that renders it. Once matched, the file is pinned in the output root for the life
of the session and reported as `LeftWhereTheChildPutIt` instead of being sorted
into `downloads\`.

**Blast radius is genuinely small** and worth saying so: lever 1 still holds by
construction — the child's working directory is the session's `output\`, so the
file is inside the session tree either way. What is defeated is classification,
plus an unsorted set that grows.

**How it is reached.** A page that triggers a download and then renders its own
filename. Needs intent, or a very short name.

**Cost to fix.** Small if the rule stays mechanical: the pin could require the
name to look like a generator-produced name (the prefix set is already derived
from the resolved bundle), or the match could require the answer to name it in a
pointer-shaped context. Both weaken the "the child named this file" property the
method was written to have, which is the trade.

---

## F8 — A cancelled `tools/call` leaks the in-flight filename reservation for the life of the session

**What breaks.** `_reserved` loses an entry only through
`ArtifactRouter.Release` (`src/BrowserAI/Artifacts/ArtifactRouter.cs:501-512`),
and `Taken(candidate)` is `_reserved.Contains(candidate) || File.Exists(candidate)`
(line 698). In `AnswerToolsCallAsync` the reservation is made at
`live.Artifacts.Plan(tool, arguments)` (`BrowserProxy.cs:544`) and released on
exactly three paths: the remediation exit (593), the child-failure exit (622),
and inside `Complete` when the file turns out not to exist (`ArtifactRouter.cs:410-414`).
There is no `try`/`finally` around `await live.Child.AskAsync(…)`
(`BrowserProxy.cs:580`), so an `OperationCanceledException` on the caller's token
leaves the name reserved permanently.

**Consequence.** The caller retries `browser_take_screenshot` with the same
`filename` and is silently given a suffixed name, with the answer reporting a
rename that no file on disk justifies. Not data loss — the opposite of the
never-overwrite rule's failure direction — but a wrong answer about where a file
went, which is the class this product exists to remove.

**How it is reached.** Any client-side cancellation or timeout of a call that
names a file: a screenshot, a PDF, a trace, a video. Ordinary.

**Cost to fix.** Small: a `try`/`finally` or a `using`-shaped scope around the
child call, with the three existing release sites folded into it.

---

## F9 — Every `browserai_list` fully parses the log of every session on the machine, then filters

**What breaks.** `List` (`src/BrowserAI/Sessions/SessionManager.cs:805-830`)
calls `_index.Follow()`, which calls `SessionLock.ReadRecord` for **every** index
entry on the host (`src/BrowserAI/Sessions/SessionIndex.cs:187-207, 435`), and
only then applies `IsUnder(session, prefix)`. `ReadRecord` parses the whole
record, materialising up to 250 `LogEntry` objects and all their
`LoggedArgument`s per session. `List` uses four fields, and the only one that
needs the log is `LastUsed`, which needs one timestamp
(`LockRecord.cs:318-322`).

The same walk backs `LiveSessions()` on every reinstall refusal
(`SessionManager.cs:1551`) and the index sweep.

This is a cost note rather than a break, and it is bounded — 250 entries is the
cap. It is here because the multiplier is the machine's session count, which this
repository has already watched reach 346 in three months on the setup it
replaces, and because the filter runs on the wrong side of the parse. Combined
with F3 (an entry whose size is not bounded) the two compound.

**Cost to fix.** Moderate. Filtering by prefix before following, or a
record-header read that stops at the log, both change `SessionIndex`'s contract
that *following is verification*.

---

# What I attacked and could not break

Stated because this repository says plainly that this is the half that makes the
rest trustworthy.

1. **Breaking `browserai.json`'s own parse with log content.** Measured
   2026-08-24: `Utf8JsonWriter` under `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`
   still escapes control characters, `"` and `\` — a newline, a quote and
   `<b>&'"</b>` all round-trip through `LockRecord.ToUtf8` → `LockRecord.Read`
   intact and the file stays valid JSON. `Read` refuses an unknown key at all
   three levels (`LockRecord.cs:983-1100`). I found no caller-supplied string
   that damages the record. (The *content* still escapes into a model's context —
   that is F3, and it is a different property.)

2. **Unbounded log growth by entry count.** `MaximumLogEntries = 250`, trimmed
   from the middle so entry zero — `browserai_init`'s purpose — is never dropped
   (`LockRecord.cs:170, 409-420`), and `LogIsAtTheCap` is surfaced in
   `catch_up`'s answer rather than presented as continuity. The bound holds; only
   the per-entry byte bound does not.

3. **A `why` collision in an upstream schema.** Scanned all 69 tools in
   `upstream-snapshots/tools-list.json`: **zero** declare a `session` or `why`
   property. Positive control on the same scan: it found `additionalProperties:
   false` on 69 of 69, and a `filename` property on exactly the 11 tools
   `ArtifactTools` classifies. If a collision did arrive, `InjectSession`
   (`SessionToolSurface.cs:382-392`) would overwrite upstream's declaration and
   `BrowserProxy.cs:563-564` would strip the caller's value before the child saw
   it — but `UpstreamSnapshotTests` makes that schema change a red build before it
   can reach a caller.

4. **A schema that is not an object, or has no `properties`.** `InjectSession`
   returns without touching a non-object `inputSchema`
   (`SessionToolSurface.cs:365-371`) — correct, but it then advertises a tool
   without `session`/`why` while `AnswerToolsCallAsync` still requires both, so
   such a tool would be advertised and permanently unreachable. A non-object
   `properties` or a non-array `required` would be **replaced**, discarding
   upstream's own declarations (lines 373-377, 394-398). Neither is reachable
   today: 69 of 69 carry an object `properties` and an array `required`, and a
   change to either is a snapshot diff a human adjudicates.

5. **The stray sweep terminating a live session inside F1's unheld window.**
   It cannot. `SessionLock.TryHoldUnowned` acquires
   `LockScopes.PerDirectoryGate` **before** it opens `browserai.json`
   (`SessionLock.cs:627-638`), and `Rewrite` holds that same gate across the whole
   close → write → rename → re-open (`SessionLock.cs:394-433`). The same holds for
   `TryAcquire`'s create-or-take, and `ProbeForHolder`'s "looks free" answer
   returns `null` and falls through to the gate (`SessionLock.cs:839-844`).
   `browserai_list` is the **only** ungated reader of the probe, which is exactly
   why F1 is confined to the report.

6. **`browserai_catch_up` blocking a session another BrowserAI is driving.**
   `ReadRecord` opens `FileAccess.Read, FileShare.ReadWrite | FileShare.Delete`
   (`SessionLock.cs:713-716`). The `Delete` share is load-bearing: without it the
   driving process's `File.Move(overwrite: true)` inside `Rewrite` would be
   refused while any `catch_up` held the file, and by
   `BrowserProxy.cs:521-533` a call whose entry cannot be written is **refused**.
   As written, a `catch_up` cannot make a peer's browser call fail. It holds, and
   the share flags are the whole reason.

7. **`catch_up`'s directory walk as a cost.** `SessionInventory.Of` recursively
   enumerates the entire session tree including the browser profile, with no cap
   and no cancellation token. Measured 2026-08-24 in .NET against real
   profiles on this machine: **3,825 files / 494 MB in 22–24 ms warm** (Chrome
   user data), **6,426 files / 1,022 MB in 35–42 ms** (Edge). Not pathological.
   *My first attempt at this measured 650 ms and was wrong — it was PowerShell's
   per-object loop overhead, not the enumeration API. Recorded because a number
   that size would have gone into this report unchallenged.*

8. **The reader/writer maintenance lock's exclusion arithmetic.** Reader
   `Read`/`Read`, writer `ReadWrite`/`Read`, describer `Read`/`ReadWrite|Delete`
   (`MaintenanceLock.cs:172-249`). Both directions of the Windows sharing check
   do what the remarks claim. A reader whose process is **suspended** rather than
   dead still holds its handle, so a reinstall is *refused* rather than
   proceeding into a tree somebody is using — the failure mode is writer
   starvation, which is the maintainer's stated and accepted decision. The
   misdiagnosis in the refusal *text* is F5; the exclusion itself is sound.

9. **A reinstall beginning between a session's claim and its index entry.**
   `MaintenanceLock.TakeShared` is the **first statement** of `InitAsync`
   (`SessionManager.cs:420-421`) and of `ResumeAsync` — before argument parsing,
   before the directory guard, before the gate, before `_index.Record` at line
   1753. There is no instant in which a session exists and the root is unclaimed.
   The reverse order (a reinstall already holding it) is refused at `TakeShared`.

10. **The instance-directory window between marker release and tree delete.**
    `run.Dispose()` and `InstanceDirectory.Delete(instance)` are both in `Main`'s
    `finally` (`Program.cs:373-377`), which runs **after** the `await using`
    disposals of the proxy and every session — so every child, browser and job is
    already gone before the marker drops. A peer sweeping in that window must
    still pass the 5-minute `GetLastWriteTimeUtc` guard and then win an atomic
    `Directory.Move`; the worst outcome is that this process's own `TreeDelete`
    walks a path that is no longer there. Separately, a run whose marker could not
    be taken at all (`InstanceDirectory.cs:176-181`) falls back to the
    working-directory lock — which the 2026-08-24 correction records as the
    mechanism that gutted live sessions — and says so only in a log line. That is
    an accepted degradation, documented where it happens.

11. **A failed `Rewrite` leaving the session unowned.** The inner catch reclaims
    the handle before rethrowing (`SessionLock.cs:418-428`), and `Reclaim` refuses
    to hand back an object that reports ownership it does not have
    (`SessionLock.cs:437-451`). Holds for `IOException` and
    `UnauthorizedAccessException`, which is what `WriteDurably` and `ReopenHeld`
    raise.

12. **An aliased spelling publishing a duplicate index entry.** `_index.Record`
    is reached only from `OpenAsync`, i.e. only from the two guarded tools, so
    `browserai_set_purpose` under an alias cannot add a second entry for one
    directory. `browserai_list` cannot be made to show a session twice this way.
    (What an alias *can* still do is F4.)

13. **`browser_route` against artifact routing and the snapshot/console
    pointers.** A mocked response cannot reach the inbound `filename` rewrite —
    that happens before the child sees the call — and cannot move where a file is
    born, because the child's working directory is the session's `output\`. Its
    reach into this area is through answer *text*, which is F2 and F7.

14. **Forging a stray-sweep attribution from page content.** Attribution reads a
    class-qualified message-only window's text, which is the browser's
    `userDataDir` as BrowserAI spelled it on the command line
    (`StraySweep.cs:639-662`). A page cannot set it, and a candidate no window
    claims is reported and never touched.
