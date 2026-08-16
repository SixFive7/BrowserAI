<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# C. The `init` tool and instance handles

> **Decided 2026-08-13: one MCP registration, handle-based instance routing.**

The client calls `init` first, passing the session type and all target-directory information. BrowserAI resolves the directories, takes the appropriate locks, generates the child config, spawns a Playwright runtime, and returns a **short unique handle**. Every subsequent tool call carries that handle, which routes it to the right instance.

The point of the handle is separation: **the MCP server's lifetime is decoupled from the Playwright instances it supervises.** One BrowserAI process may own several children, and instance creation, lifetime and cleanup belong entirely to BrowserAI — not to the client, and not to Playwright.

This also replaces four static configurations with one dynamic one, and eliminates the relative-path hazard by making the directory an explicit argument rather than an implicit consequence of cwd.

## The `init` contract

**Takes:** the mode; the browser; a required `purpose`; and **a required session directory. There is no default and there is no fallback.** An empty, relative, malformed or unusable path is rejected outright — never normalised into something that happens to work.

**Requiring the path is the design, not a validation detail.** A default is a decision made on the caller's behalf that the caller never notices making, and the whole failure class this project exists to eliminate is decisions nobody observed. Forcing every `init` and `resume` to name a location makes an agent state where this session's data lives — which is precisely the thought the founding stray-file problem shows nobody was having. It also removes the only path by which two agents could land in the same place without either choosing it.

**The directory name carries what a separate label used to.** `checkout-flow-bug\` is navigable and `2026-08-14T04-11-50-882Z\` is not: three months of the current setup produced 346 session directories and 1.5 GB, and the reason nobody pruned them is that nobody could tell what any of them had been ([kb: the legacy setup](../kb/history.md#the-legacy-setup-and-this-machine)). That argument stands; the label concept it once justified does not. [The directory is the identity](#the-session-directory-is-the-identity), so an agent naming a location has already named the session, and `purpose` carries the sentence a label was never long enough for.

**Returns:** the handle, **and the resolved absolute paths.** [§settled](../README.md#settled-2026-08-13) already requires logging those at instance creation; returning them costs nothing extra and puts them where the agent can act on them — it can tell the user where the screenshot is instead of guessing, and the paths become auditable from the transcript rather than only from a log file nobody opens.

**Per-instance paths are unconstrained; per-call filenames are not.** These look like the same decision and are not. `init`'s directory arguments are deliberately unrestricted — [§settled](../README.md#settled-2026-08-13) accepts any path, because the caller is declaring where its data lives. A per-call `filename` names a file *within* a workspace already declared, so normalising it into that workspace is not a restriction on the caller's choice; it is honouring the choice already made. Record this distinction, because the two rules read as contradictory to anyone meeting them cold.

**Reject traversal rather than normalising it.** A `filename` of `..\..\..\foo.png` must resolve, be recognised as an escape, and be refused with an LLM-readable error — never silently collapsed into a path that happens to land somewhere.

**Check free disk space at `init`, and only with an O(1) query.** First-run browser provisioning needs **203.8 MB down and 430.48 MiB extracted** (re-measured 2026-08-16), so peak usage is ~640 MiB while both the archive and the tree exist; a session then grows unbounded. A refusal at `init` that names the number is recoverable in one turn; a failure partway through the download is the `spawn EFTYPE` shape — success-shaped, stderr empty, discovered at first navigation. **This must be a volume free-space query, never a directory walk**: `init` sits on the hot path of every session, and a check that scans the output tree would make the fix slower than the failure it prevents.

## Three modes, and tracing as a modifier

The legacy setup had four modes — `headless`, `interactive`, `tracing`, `persistent`. With `--isolated` dropped they are exactly four of the eight combinations of three independent switches: **headed?**, **storage?**, **tracing?**

| # | headed | storage | tracing | Legacy name | |
|---|:---:|:---:|:---:|---|---|
| 1 | ✗ | ✗ | ✗ | `headless` | the workhorse |
| 2 | ✗ | ✗ | ✓ | — | gap |
| 3 | ✗ | ✓ | ✗ | — | credentialed and invisible |
| 4 | ✗ | ✓ | ✓ | — | as 3 |
| 5 | ✓ | ✗ | ✗ | `interactive` | a human types credentials the agent must never capture |
| 6 | ✓ | ✗ | ✓ | `tracing` | interactive, plus a trace |
| 7 | ✓ | ✓ | ✗ | `persistent` | logged-in agent work |
| 8 | ✓ | ✓ | ✓ | — | gap |

**Settled: three modes — `headless`, `interactive`, `persistent` — with `tracing` a boolean on any of them.** `tracing` was never a mode; row 6 is `interactive` with a flag, which is why it was the odd one out. Promoting it to a modifier *removes* a mode while *adding* capability: rows 2 and 8 arrive for free, the classification matrix shrinks from four rows to three, and rows 3–4 stay closed.

> ⚠️ **`tracing` has no upstream trace key to reach, measured 2026-08-16 @ `@playwright/mcp` 0.0.79.** Neither the CLI surface nor `config.d.ts` carries a trace option at all, and `tracesDir` is computed internally as `<outputDir>/traces` rather than configured — so the switch this table calls `tracing` maps to upstream's surviving feature with the same purpose, **`saveSession`**, which records what the session did into the output directory. The switch, the argument name and the three modes are unchanged; what changed is what the boolean *does*, and it is now something upstream actually reads. Re-establish by grepping the resolved bundle for `saveTrace` (nothing) and reading `--save-session` in [the committed CLI snapshot](../upstream-snapshots/cli-help.txt). If a bump restores a trace option, this is the entry to revisit.

Rows 3 and 4 are deliberately not offered. They would be genuinely useful — routine work in a logged-in profile has no need to put a window on screen — but they are the only combination that grants full credential access with no visible signal that anything is driving the session. A window is not a security control; it is the sole cue a human gets. Opening that should be a decision taken on its own merits, never a side effect of making the switches orthogonal.

> **The mode is bound at `init` and carried by the handle**, exactly like the browser choice. `resume` reads it from `lock.json` and never accepts one, so a session cannot change what it is. Note the older argument in this section — that a named mode is harder to forge than a flag — is **weaker than it reads**: a flag bound at `init` and carried by the handle is equally unforgeable. The real reasons to keep names are the size of the classification matrix and the fact that a name carries intent to whoever reads it later.

**Discoverability is a requirement, not a nicety.** A mode nobody knows about is a mode nobody picks correctly, and the failure is silent — an agent that does not know `persistent` exists just fails to log in and reports the site as broken. All four channels below carry part of it** — three capped, one not:

- **Server `instructions`** — one compact line naming the three modes and the one-sentence rule for choosing. This is the only channel that reaches the model *before* it calls anything, so it must contain the choice, not a pointer to it. Costs perhaps 150 of the 2 KB.
- **`init`'s description** — the full table: what each mode grants, what it refuses, that `tracing` is a boolean orthogonal to all three, and that the choice is permanent for the directory's life.
- **`resume`'s description and result** — the recorded mode, played back, alongside the recorded purpose. An agent meeting an existing directory learns what it is without guessing.
- **Refusal text is the fourth channel and the most effective one.** A storage tool called on a `headless` handle must not fail with "not permitted"; it must name the mode that would permit it and what to do — a mode error is a teaching moment arriving exactly when the model is ready to learn. This channel has no budget, so it carries the detail the capped ones cannot.

Pin all of it with tests, as `SixFive7/OutlookAI` does for its instructions string: the mode list in `instructions`, in `init`'s description, and in the refusal text must all be generated from **one** table, so a fourth mode cannot ever be added in one place and missed in the other three.

## Where guidance lives: three channels, two of them capped

An MCP server can address the model in three places, and they are seen at different times. Putting the wrong content in the wrong one is why agents forget handles.

| Channel | Seen | Budget | Carries |
|---|---|---|---|
| **Server `instructions`** (on `initialize`) | Always, at session start — Claude Code loads tool *names* and server instructions eagerly and defers schemas | **2 KB, truncated silently** | That `init` comes first, and why. The fact that is useless once it is needed |
| **`init`'s description** | When the model reaches for `init` | **2 KB, truncated silently** | Argument meanings, the real-Chrome-profile warning, and the retention policy — the spec requires retention to be stated *here*, in the creation tool's description |
| **`init`'s result** | Immediately after the call | none | Resolved absolute paths, the layout, the mode and browser bound at creation, and the provisioning state |

The server instructions exist to pre-empt the cold-start failure named above: **the first call after a restart will forget the handle.** Only an eagerly-loaded string can reach the model before it makes that mistake. Detail belongs in the result, where there is no budget and the paths are concrete — spending a third of a 2 KB allowance on a directory diagram every agent re-reads at the wrong moment is the wrong trade. The client behaviour these budgets rest on — eager names, deferred schemas, silent truncation at 2 KB — is in [kb: the client](../kb/mcp/protocol.md#the-client-claude-code).

`SixFive7/OutlookAI` is the in-house precedent for treating this as a contract rather than prose: its instructions string lives in `ServerMetadata.cs` and is pinned by tests.

**Why the mode is bound at creation, not passed per call.** A `mode` argument on every tool is a value the model composes fresh each time and can compose wrongly; a mode fixed at `init` and recorded in `lock.json` is composed once, by the caller that chose the directory. That is the whole of the guarantee, and it is worth being precise about its size.

> ⚠️ **An earlier draft claimed more than this, and the claim is now false.** It argued that a *server-minted* handle could not be forged for a session type the agent never created, converting a model-authored assertion into a server-issued capability reference. [The mint is gone](#the-session-directory-is-the-identity) — the session is a directory path, which a model can compose freely. **Nothing prevents an agent naming a `persistent` directory it did not create.** What prevents it *reading* those cookies is that the directory it names has a `lock.json` recording `persistent`, and the mode check is against that file, not against the caller's word. So the enforcement holds and the *forgery* argument does not.
>
> The residual gap is unchanged and still worth stating: a connection holding both an `interactive` and a `persistent` session can route a call to the persistent one. That grants nothing new — an agent with a persistent session was already entitled to those cookies — and **the `interactive` guarantee, which is the one a human relies on, holds.**

**Critical constraint — read before designing `init`:** the MCP spec (2026-07-28, *Tools § Capabilities*) states the tool set "**MAY** change over time … but **MUST NOT** vary per-connection or as a side effect of other requests on the connection." SEP-2567 removed protocol-level sessions outright. Separately, `notifications/tools/list_changed` is unreliable in practice — Claude Code registers no handler for it (issues [#13646](https://github.com/anthropics/claude-code/issues/13646), [#4118](https://github.com/anthropics/claude-code/issues/4118)). **That citation is stale** — it held at client 2.0.65 and is false at 2.1.231 ([kb: the client](../kb/mcp/protocol.md#the-client-claude-code)). The conclusion below is unchanged, because it rests on SEP-2567 rather than on the client.

**Therefore `init` cannot shrink the tool list.** There is one static list; session-inappropriate calls must be **rejected at runtime by BrowserAI**, keyed on the handle's type. Storage tools remain *visible* in every session and are refused at call time. See [Trade-offs](../README.md#known-trade-offs) for what this costs.

**Obligations the handle design creates:**

- **Every tool schema gains a required `session` parameter** (the session directory — see [§H](H-model-surface.md) for why it is not called `handle`), injected by BrowserAI into the raw `inputSchema` of **every tool in the surface — 42 tools on `headless` and `interactive`, 59 on `persistent`.** `Measured 2026-08-16 @ @playwright/mcp 0.0.79` over a real `tools/list`, and regenerated into [the golden snapshot](../upstream-snapshots/tools-list.json) on every build. The composition: 24 is the default with no capabilities set and is **unconditional** — the `core*` family is or-ed in whatever `capabilities` says, so nothing can go below it; we add `config` (1), `vision` (6) and `devtools` (11) everywhere, which is the 42, and `storage` (17) on `persistent` alone, which is the 59. 69 remains the maximum ever exposed over MCP, and is not ours. [The per-capability breakdown](../kb/playwright/tools-and-artifacts.md#the-per-capability-breakdown-counted) is the whole table. **These are upstream's numbers before our own filtering.** ⚠️ **Corrected 2026-08-16 (previously: "[§H](H-model-surface.md) removes `browser_run_code_unsafe` from `interactive`, which is 41 there, and that subtraction is step 13's to make and to test").** The subtraction has been made, and it was two tools rather than one: **41 on `headless`, 41 on `interactive`, 58 on `persistent`**, out of the 59 the caller is advertised. `browser_annotate` is refused everywhere except `interactive` — it blocks until a human draws, and `interactive` is the one mode whose defining promise is that a human is present — so `headless` loses it too and lands on 41 as well. **The two 41s are different sets**, which is the fact worth carrying: `headless` permits arbitrary code and refuses the annotation tool, `interactive` does the opposite. [The table is in kb](../kb/playwright/tools-and-artifacts.md#what-browserais-own-modes-permit-after-its-own-filtering). Do this on the `JsonNode`; never materialise a typed schema to do it, because the typed path silently discards unknown tool-level members. Keep the injection order-stable — the spec SHOULDs deterministic tool ordering for prompt-cache hit rates.
- **Missing and unknown sessions need two distinct, LLM-readable errors** — [rows 1 and 2](H-model-surface.md#h4-the-error-catalogue). There is deliberately no *expired* case: [reclaim is forever](#lifetime-one-timer-and-reclaim-is-forever), so a session that exists is always resumable, and an error for an unreachable state would be a test nothing can satisfy. That text is read by a model deciding what to do next, not by a human tailing a console — the same principle as the current launcher's browser-preflight message, which exists precisely because "the server is stuck" was the wrong conclusion to invite.
- **The first call after a cold start will forget the handle.** Design for it: the error must name `init`, state what it needs, and be recoverable in one turn.
- **Instance lifetime is BrowserAI's to define**, and it is defined by three mechanisms, none of which is a close tool: the browser-idle timer, the client-liveness watcher, and **stdin EOF as the backstop** that reaps everything — EOF fires instantly when the parent holding the pipe is `TerminateProcess`d ([measured](../kb/windows/processes.md#stdio-exit-codes-and-process-startup)), and the SDK already treats it as shutdown. `browserai_destroy` **deletes** the session directory; it is not a way to end a session and keep it. There is deliberately no such tool, because [reclaim is forever](#lifetime-one-timer-and-reclaim-is-forever) makes one unnecessary: a session that is not being driven costs a stopped browser, and resuming it costs 515 ms.
- **N children per process.** One BrowserAI now supervises several `node` children, each with its own config, stderr stream and directory locks. **One job object per child, never one shared job** — a shared job fuses every instance's tree together, so tearing down one handle would kill them all, and assigning BrowserAI itself would make it a casualty too. See [the job object contract](E-lifecycle.md#zero-process-leakage-the-job-object-contract). Stderr must be demultiplexed per handle or diagnostics become unreadable at exactly the moment they matter.
- **`browser_get_config` becomes per-handle** — and is the natural per-instance drift check.

## The session directory is the identity

**One directory is one session, and it is simultaneously the name, the handle and the lock.** Settled 2026-08-15, replacing an earlier design with a central registry, opaque handles and a separate label concept. All three collapsed into the directory.

```
<session-dir>/
  lock.json      <- ours. The lock and the record
  browserai.log  <- ours. This session's own log
  profile/       <- --user-data-dir
  output/        <- --output-dir, holding the typed folders of [§F](F-artifacts.md)
  downloads/     <- browser downloads
```

Everything except those two is a subfolder, so the files that matter are unmissable, and [§F](F-artifacts.md)'s routing gets a home instead of scattering artifacts among Chromium's internals.

> ⚠️ **Corrected 2026-08-16 (previously "`lock.json` — **the only file at the root**").** It is one of two. [§E](E-lifecycle.md#logging-where-it-goes-and-what-forces-it-to-stderr) puts the session's own log at `<session-dir>\browserai.log`, and [step 12](build-order.md#12-the-session-tools-and-config-generation) built it — the two sections said different things and nobody had noticed, because until a session had a lifetime nothing wrote to that file. The flat root's purpose survives: it exists so the file that matters cannot be missed, which two files at the root still deliver and a subfolder would not. **A third arrived at [step 14](build-order.md#14-artifact-routing) and was named rather than smuggled in**: [§F](F-artifacts.md#the-artifact-index) puts `session.json` in the session folder by name, so the root now holds three. What the rule actually protects survives all three — **no artifact is ever at the root**, so every file there describes the session rather than being something it produced. The generated Playwright config is still forbidden: it is a per-run artifact and lives in the run's instance directory, keyed by the session's hash, never in the session directory.

**`lock.json` is both the lock and the record.** Held open `FileAccess.ReadWrite, FileShare.Read`: a second BrowserAI requesting write access fails — that is the lock — while any reader can still display who holds it and why. Contents: schema version, mode, browser, purpose and its history, created/last-used timestamps, BrowserAI version, the resolved absolute path of the directory itself, and the holder record — PID, process creation time, client process name. The holder record persists after death on purpose, so a stale lock yields *"held by PID 1234 since 14:02, no longer running — reclaiming"* instead of a bare refusal. `(pid, creationFileTime)` together defeat PID reuse.

> **Superseded 2026-08-16.** This paragraph read *"It is rewritten in place on the handle we already own; a reader that catches a torn write retries once."* Writes are now `WriteThrough` + `Flush(true)` + atomic `File.Move`, and the torn-read retry is gone because an atomic rename cannot produce a torn read. The full reasoning is in [§D — durable `lock.json` writes](D-locking.md#durable-lockjson-writes).

> ⚠️ **And *"the handle we already own"* cannot survive the rename, which is the half neither section noticed.** Measured 2026-08-16: Windows refuses to rename over a file whose handle is open, under **every** share mode including `FILE_SHARE_DELETE`. The handle has to be closed for the replacement and re-taken afterwards, and the only thing that makes that gap unobservable is that every acquisition holds the per-directory mutex across it. [§D — the rename and the held handle collide](D-locking.md#the-rename-and-the-held-handle-collide-and-the-mutex-is-what-resolves-it) carries the measurement and the two consequences it forces on readers and on the retry budget.

**There is no bearer token, and that is deliberate.** An earlier draft minted a 128-bit handle so the `resume` redirect could not be bypassed by guessing a path. It bought less than it cost. Within one BrowserAI process the lock does not isolate callers at all — two subagents share one MCP connection, which is the exact fork case `resume` exists for — and a token would not have stopped the second agent either: it would have called `resume`, read the warning, and proceeded. **The token's entire value was guaranteeing the warning was displayed.** Against that: an opaque token is precisely the state that evaporates when a model is compacted, and an agent that loses its handle cannot drive its own session, whereas a directory path is always reconstructible.

So the guarantee is recovered differently, and better placed: **BrowserAI knows whether this connection created the session.** A caller driving a session it did not `init` gets a notice prepended to its *first* response — *"you are driving a session this connection did not create; opened 2026-08-12, purpose: …; another agent may be using it."* That fires at first use rather than at reclaim time, which is where it matters.

> ⚠️ This holds because BrowserAI is **stdio-only, local, single-user**: another process is blocked by the file lock, another user cannot reach the server. **If BrowserAI ever gains an HTTP transport the handle becomes network-reachable and the token question reopens.** Recorded so a future transport change does not cross that line silently.

**`init` refuses a directory that already has one, including a cleanly closed one.** It fails with an error naming the existing session, its purpose and its mode, and directs the caller to `resume`. Being made to say "resume" is the point: it converts an accidental collision into a stated intent. There is deliberately **no difference between a lost session and a neatly closed one** — both must be resumed, so both behave identically, and the reason a session ended stops being a thing anyone has to model.

**`init` takes** a **required** directory, a **required** `purpose`, a **required** mode, and optional browser, tracing, console-level and `debug`. See [§H.2](H-model-surface.md#h2-the-authored-tools) for the full signature. **`resume` takes the directory, an optional appended `purpose`, and `debug`**; mode and browser come from `lock.json` and are refused as arguments, because a profile is browser-specific and a session cannot change what it is. A missing or unparseable `lock.json` is an error, never a guess.

**Debug logging is an argument, not a build flavour.** `debug` is optional on `init` and on `resume`, defaults to false, and raises this session's log level for the life of the session — see [§E](E-lifecycle.md#logging-where-it-goes-and-what-forces-it-to-stderr) for where those lines land. Two properties make it worth the argument slot. It is **per-session**, so turning it on for the session that is misbehaving does not drown the ninety-five that are not; and it is **reachable by the agent that has the problem**, at the moment it has it, with no restart, no environment variable and no second registration. `resume` accepts it for exactly that reason: the interesting case is almost always a session that is already running badly, and requiring an `init` to get diagnostics would mean destroying the evidence to look at it.

> An earlier proposal reached the same end by a different route: ship a second executable name whose only difference was its log level, and have the user re-register that one. It is superseded — it needs a config edit and a client restart to answer a question the agent is asking right now, it is machine-wide where the problem is one session, and it puts a second binary in the artifact for the update path to carry.

> `purpose` is free text written by one agent and replayed into another's context, which makes it a channel between agents. Cap its length, strip control characters, and frame it explicitly as recorded data — *"purpose recorded by a previous session:"* — so it cannot read as an instruction. Store the facts we get for free alongside it — created, last-used, mode, browser, last origins visited — because *"last used 3 days ago, last on portal.customer.example"* usually answers "what was this" better than prose written three days ago.

## Moved or copied: the recorded path is the discriminator

**Settled 2026-08-16. The directory is the identity; the path recorded in `lock.json` is provenance.** They are different jobs, and conflating them is what makes this look harder than it is. Identity is *where the caller is pointing right now*. The recorded path answers a narrower question — *where was this session when its record was last written* — and it is that answer, compared against the actual path, which tells `resume` what happened to the directory.

On `resume`, if the recorded path differs from the directory actually being opened:

| Recorded path | What happened | What `resume` does |
|---|---|---|
| No longer exists | The directory was **moved** or renamed | Repair the record to the actual path, log it, carry on |
| Still exists | The directory was **copied** | Refuse, and require an explicit acknowledgement argument to proceed |

**No extra identifier is needed, and one was proposed.** An earlier design added a random fingerprint field written at `init` so a copy could be told from an original. It is dropped: the recorded path already discriminates, because a move leaves nothing behind and a copy leaves the original standing. A fingerprint would be a second thing that can disagree with the first, for a question the first already answers.

> ⚠️ **The discriminator's exact scope, measured 2026-08-16 while building [step 12](build-order.md#12-the-session-tools-and-config-generation).** The recorded path answers this question **only while it is accurate**. Copy a directory whose record has *not* yet been repaired — one that was moved and not yet resumed — and the copy inherits a record naming a path that no longer exists, which is the move signature exactly: BrowserAI repairs it silently and asks nothing. That is not a defect to close, because it is the same information a fingerprint would have had to arbitrate on and there is nothing to arbitrate with: two directories, both claiming a third path that is gone, are genuinely indistinguishable. It is recorded because the first version of the acceptance test copied before resuming and the copy was accepted, which reads as the feature not working. The test now copies **after** the move is repaired, which is also the case the refusal is for: a copy of a session that says where it is.

**What the refusal is actually protecting, stated accurately.** A copy does **not** corrupt anything. Each copy carries its own `profile/` folder, each browser writes only to the folder it was launched against, and neither can reach the other's databases — [the profile-collision hazard](D-locking.md) does not apply, because there is no shared directory. The real problem is narrower and is about legibility: the copy inherits an ownership record naming a process that **may still be alive against a different directory**. That produces one of two wrong outcomes. Either the copy is refused because its inherited holder record points at a live PID, which is a refusal the caller cannot act on and cannot understand; or the copy is taken over and silently inherits another session's recorded purpose and history, so the sentence a previous agent wrote about *that* directory is replayed as though it described *this* one.

That is the same failure `resume` exists to prevent — an agent acting on a session whose history it has misread — which is why the remedy is the same shape: refuse, say what was found, and make the caller state the intent.

> ⚠️ **An earlier draft justified this refusal as protecting against profile corruption. That justification was wrong** and is corrected rather than deleted, because the wrong reason predicts the wrong scope: corruption would argue for refusing the *launch*, where legibility argues only for refusing the silent *takeover of a record*. The refusal is unchanged; what changed is what it is for.

## Our own files reject what they do not recognise

**Settled 2026-08-16.** `lock.json` and `session.json` are parsed strictly: an unknown key is an error, not something to ignore. This is the rule already applied to [Playwright's config](#config-generation-and-validating-it-against-the-runtime-we-ship) — *"`loadConfig` is a bare `JSON.parse` with no schema validation, so a renamed or removed key is discarded in silence"* — turned on ourselves, where it is cheaper to hold and where we own both ends.

Two failures it catches, and both are otherwise silent. A **newer BrowserAI's file read by an older one**: a field the older build has never heard of is a field it will not honour, and ignoring it means the older build reports the session as understood while acting on a partial record. And a **hand-edited or corrupted file**: a typo in a key name is indistinguishable from an absent key under lenient parsing, so `"purpse"` reads as no purpose at all and the wrong answer is returned confidently. The schema-version field exists to make the first case a deliberate decision — *refuse and say which version wrote this* — rather than a guess.

Strictness costs one thing worth naming: a field can never be added to these files without every reader that will meet them being able to parse it. That is a real constraint on the update path and it is accepted, because the alternative is the lenient-parse failure mode above, and the schema version is what turns the constraint into a stated error instead of a mystery.

**Every timestamp we write is ISO 8601, in the invariant culture, with an explicit offset** — `DateTimeOffset` round-tripped through the `"O"` format specifier, never `DateTime.ToString()` against the current culture and never a bare local time. The failure this closes is not hypothetical: a file written on a machine with a non-invariant culture and read on another produces either a parse error or, worse, a date that parses to the wrong day. A test round-trips every timestamp field through write and read **under a deliberately non-invariant culture**, because a test that runs only under the developer's own locale asserts nothing about the case that breaks.

## Lifetime: one timer, and reclaim is forever

**Exactly one timer exists: browser-idle, ~10 minutes, reset by any tool call.** It closes the browser and keeps the node child — re-measured 2026-08-16, ~496 MB → ~118 MB, with the next call bringing the browser back in ~0.41 s ([kb: timings](../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)). The relaunch is implicit: a caller that navigates after an idle close must never see "browser is closed", and that invisibility is a test, because it is the whole reason the timer is safe.

> ⚠️ **Corrected 2026-08-16 at [step 17a](build-order.md#17a-the-browser-idle-timer-and-teardown) (previously "measured 329 → 110 MB, and 186 ms to relaunch").** Both figures were carried here undated from a source nobody could re-run, and both moved: the fall is ~496 MB → ~118 MB and the relaunch is **2.2×** the recorded time. Nothing about the decision changes — an idle session still falls back to roughly the node child's own footprint, and 0.41 s is still far below anything a caller would notice. **What the step did settle is that the relaunch is upstream's own behaviour**: Playwright creates the browser lazily on first use, so there is no relaunch code in this product and none was needed.

**No handle-expiry timer, no session TTL, no reclaim window.** A torn-down session stays resumable indefinitely against its recorded directory, because the durable thing is the profile, not the process — measured, a resume after killing the node child preserves cookies, localStorage, IndexedDB, service workers and CacheStorage, losing only `sessionStorage`, in ~515 ms ([kb: timings](../kb/playwright/provisioning-and-timings.md#timings-spawn-resume-idle-close-proxy-overhead)). Every expiry timer considered was a cliff that deleted work in exchange for nothing: an agent thinking for 61 minutes came back to a dead handle, and the recovery was a `resume` it could have done anyway.

The cost is honest — **directories accumulate forever** — and it is why explicit `list` and `destroy` tools matter more here, not less. Deliberate deletion beats a timer that deletes.

**Teardown** closes stdin, which trips the child's own `setupExitWatchdog` (`stdin` close, `SIGINT`, `SIGTERM` → `gracefullyCloseAll()`, hard exit after 15 s — [kb: upstream config](../kb/playwright/configuration.md)). No killing is involved in the normal path. Force is closing the job handle, and only that — see [the job object contract](E-lifecycle.md#zero-process-leakage-the-job-object-contract).

## Finding sessions without a registry

Three mechanisms, none of which stores state. **That is the property that made the registry a liability and these not:** the registry held handle mappings, config and liveness, so two BrowserAIs could disagree, a stale entry was a bug, and every write needed a machine-wide mutex.

Because [there is no default directory](#the-init-contract), there is no root to scan and **the pointer store is the only inventory.** That makes it load-bearing, so it is designed to fail safe rather than to be correct under every race.

- **One pointer per session directory**, keyed by the SHA-256 of the canonical path, holding just that path. One immutable fact — *a session directory once existed here*. No state, no mapping, no liveness: that is the entire difference from the central registry this replaces, which held all three and therefore needed a mutex on every write and had a bug for every stale entry.
- **Written on every `init` *and* every `resume`, idempotently.** Re-asserting rather than writing-once is what makes a lost pointer self-heal: the cost of losing one is a single sweep cycle of invisibility, not a permanently orphaned directory. This is deliberate — it lets the store skip locking entirely (see [race R7](#race-conditions-and-what-closes-each)).
- **Self-cleaning on sweep.** A pointer whose directory is gone, or whose directory has no readable `lock.json`, is removed. The store therefore shrinks as sessions are destroyed, without anyone maintaining it.
- **The directory proves its own ownership.** Anything the store points at is verified by opening `lock.json` inside it, so the inventory never has to be trusted — only followed. A personal Chrome profile contains no `lock.json` and cannot be mistaken for ours however it was reached.

> **Superseded by [§D](D-locking.md#the-session-index-on-disk), which is authoritative.** This section originally recommended `HKCU\Software\BrowserAI\Sessions`, on the grounds that atomic single-value registry writes beat enumerating a directory that ~100 processes are mutating. **That argument evaporated** when enumeration replaced inventory lookup in the sweep: the index is now read only by `browserai_list` and by cleanup, so contention is low and files win on being inspectable, deletable and free of profile roaming. The four properties above still hold; only the storage mechanism changed. The exact layout is specified once, in §D — do not re-specify it here.

## The stray sweep, and the concurrency it must survive

**Design for ~100 concurrent BrowserAI processes, not for one.** Eight editor windows with a dozen agent sessions each is a normal working day, and every session spawns its own MCP server. Any sweep design that is merely *correct* for a single process is wrong here: 96 processes all sweeping at startup is a thundering herd, and 96 processes racing to kill the same stray is a correctness problem, not a performance one.

**One trigger: BrowserAI's own startup.** It is free, has no install footprint, and it fires exactly when a stray matters most, because that is when something is about to contend for a lock. It cannot know when Windows has finished restoring apps and no documented event marks it, so **it does not try to win that race** — the process that would contend takes the lock, finds it free or finds it held, and either way the answer is correct at the moment it is asked.

> ⚠️ **Corrected 2026-08-16 (previously "Two triggers, each looking twice", the second being a logon scheduled task with a ~10-minute repetition, plus a paragraph requiring it to run in the user's interactive session).** **The logon task is dropped.** [Step 16](build-order.md#16-the-stray-sweep) measured that it cannot be registered non-elevated on this machine — `schtasks /Create /XML` and the `Schedule.Service` COM API both answer `Access is denied` / `0x80070005` from a UAC-filtered administrator token, for a **minimal** definition as much as for ours ([kb](../kb/windows/detection.md#the-logon-sweep-task)) — and the three remaining options were an elevated install (which a per-user product does not have), `HKCU\…\Run` (one pass, no re-check) or dropping it. **Dropped**, at [step 19](build-order.md#19-velopack-package-update-roll-back): the case the task covered is *nobody starts a client for a week while a resurrected browser eats memory*, and a browser nobody is contending with costs memory rather than correctness.
>
> **What the task carried that still matters is the session-0 property, and it now applies to BrowserAI itself.** `FindWindowExW(HWND_MESSAGE, …)` is scoped to a window station and desktop, so a sweeper running outside the interactive session **sees no message windows at all** — it would sweep, find nothing, and report success forever ([kb: object names and window scoping](../kb/windows/detection.md#windows-object-names-and-window-scoping)). BrowserAI is a stdio child of an interactive client, so it is in the right session by construction; **R5's test is what stops that being an assumption**, and it is unchanged.

**Concurrency: try-acquire-and-skip, never queue.**

- The sweep runs on a **background thread, fire-and-forget, never awaited**, and nothing on the MCP request path waits for it or observes it. It touches the stdout wrapper never and stderr only.
- One machine-wide mutex, `Global\BrowserAI-Sweep`. A process does `WaitOne(0)` — **zero timeout**. If it fails, a sweep is already running, so this one exits immediately rather than queueing. With 96 startups, one sweeps and 95 do nothing but pay a mutex acquire.
- **A skipped sweep is not a missed sweep.** Whoever holds the mutex is scanning the same store this process would have scanned. Retrying would be pure duplication.
- The sweep must never be a startup gate: if the mutex or the store is unavailable, log and continue. A BrowserAI that cannot sweep is degraded; a BrowserAI that will not start is broken.

**Why not the named pipe.** A pipe would let a running sweeper tell a newcomer "already running" — but that is exactly what a zero-timeout mutex already says, at a fraction of the machinery. A pipe adds a server whose death orphans clients, a protocol to version, and a second failure mode where the pipe exists but the sweeper is gone. The only thing a pipe buys is handing back *results*, and a newcomer does not need results — it needs to not duplicate work. Mutex.

**Three lock scopes exist and must not be conflated:**

| Scope | Name | Held for |
|---|---|---|
| Per-directory, guarding create-or-take | `Global\BrowserAI-{sha256(path)[..32]}` | milliseconds |
| Per-session, proving ownership | `lock.json`, `FileShare.Read` | the session's life |
| Machine-wide, guarding the sweep | `Global\BrowserAI-Sweep` | one sweep pass |

### Race conditions, and what closes each

Every row is a test, not a note. The first three are the ones that lose data or kill the wrong process.

| # | Race | What closes it |
|---|---|---|
| **R1** | **The sweep kills a browser a live session just launched.** Process X sweeps; process Y is mid-`init` on the same directory. | **The sweep may only kill a browser whose directory lock it can itself acquire.** If `lock.json` cannot be opened for write, someone owns the directory — skip, unconditionally. Y-takes-lock-then-launches and Y-launching-then-holding are both covered, and X-holds-while-killing makes Y wait and then launch cleanly. The directory lock is held for the whole kill. |
| **R2** | **PID reuse between detection and kill.** | Capture `(pid, creationFileTime)` at detection and **hold an `OpenProcess` handle from that moment**: Windows will not recycle a PID while a handle is open. Re-verify creation time immediately before `TerminateProcess` regardless. |
| **R3** | **`AbandonedMutexException`.** A sweeper dies holding the mutex; every later acquire throws. | The mutex **is** acquired when that exception is thrown — catch it, treat it as acquired, and proceed. Unhandled, this disables sweeping permanently after the first crash, and nothing reports it. Same handling on the per-directory mutex. |
| **R4** | Two sweepers use different mutexes. | One name, one place in code, `Global\` prefixed. A `Local\` prefix would silently give per-session mutexes and let two sweeps run. **Corrected 2026-08-16 (previously "The scheduled task and BrowserAI use different mutexes")** — the task is dropped, and the second entry point is now `BrowserAI.exe --sweep`, which is the same binary reaching the same name. |
| **R5** | Session 0 blindness (above). | **Corrected 2026-08-16 (previously "Task runs in the interactive session")** — the task is dropped, and the subject is now BrowserAI itself, which is a stdio child of an interactive client. The test is unchanged and is the whole guard: it asserts the sweeper finds a browser it launched itself. |
| **R6** | The store is enumerated while an `init` adds an entry. | Benign: a missed entry is a live session, which the sweep would skip anyway, and it is present next pass. |
| **R7** | **The sweep deletes a pointer for a directory an `init` is creating right now.** | Not prevented — **absorbed**. Pointers are re-asserted idempotently on every `init` and `resume`, so a wrongly-deleted pointer costs one cycle of invisibility and is restored on next use. Locking the store to close this would put a machine-wide lock on the hot path of every session start, which is a worse trade at 96 processes. Deletion additionally re-checks absence immediately before acting. |
| **R8** | Two sweeps in different terminal-server sessions. | Correct and intended: message windows are per-session, so each session must sweep its own. The `Global\` mutex serialises them, which costs a little parallelism and prevents nothing valid. |
| **R9** | A sweep runs longer than the next one that starts. | Try-acquire-and-skip means the later one simply does nothing. No pile-up is possible. **Corrected 2026-08-16 (previously "longer than the 10-minute re-check")** — the re-check was the dropped task's repetition; the property is unchanged and is now about two startups, which is the far more likely pair at ~100 concurrent processes. |
| **R10** | Killing a stray mid-write corrupts its profile. | Accepted. The profile has no owner by definition (R1), and Chromium is built to survive `taskkill`, which is what upstream itself does. |
| **R11** | An exception in the sweep kills the process. | Catch-all at the thread boundary. A sweep failure is a log line, never a crash and never a protocol error. |
| **R12** | The sweep writes to `stdout`. | Forbidden process-wide already; the sweep is inside that rule, not an exception to it. |

### Detection: enumerate, then prove ownership

**Settled 2026-08-15 by two independent agents, one briefed to refute it.** Enumeration works: `FindWindowExW(HWND_MESSAGE, prev, "Chrome_MessageWindow", NULL)` walks every such window in 0.43 ms, and plain documented `GetWindowTextW` reads each title in well under a microsecond — it reads the kernel-side window name rather than sending `WM_GETTEXT`, so a hung, suspended or hostile owner cannot defeat it. Full detail and the discriminator measurements are [kb: cross-process title reads](../kb/windows/detection.md#cross-process-title-reads--settled-by-two-independent-agents).

**This changes the sweep for the better and the safety story for the worse.**

Better: the sweep no longer needs the inventory at all. It finds strays in directories the pointer store has forgotten — observed live, when one agent's sweep surfaced the other agent's browser in a directory it was never told about. **The sweep and the inventory are now independent**: enumeration answers *what is running*, the pointer store answers *what directories exist*, and neither depends on the other.

Worse, and this is the part to get right:

> ⚠️ **Enumeration hands back strangers' paths.** The earlier claim that the API "cannot return a profile you did not name" is true of the exact-title probe and **false of the enumerating sweep**. Docker Desktop, Discord, Signal, 1Password, Steam, Teams, WhatsApp and ChatGPT all publish real user-data-dirs on that channel. **The ownership test is the entire safety boundary now** — not a refinement on top of a safe primitive.
>
> And the signal is **forgeable**: a plain console app registered the class `Chrome_MessageWindow` (classes are per-process) and published an arbitrary path, indistinguishable from a real Chromium singleton.

### Detection is documented; attribution may fail, and must fail safe

The window read is **undocumented behaviour of a documented function**: `GetWindowTextW`'s contract says a window with no caption returns a null string, and `Chrome_MessageWindow` has no caption. It works on every Windows since NT and across 1,271 measured windows — but nothing says it must keep working, and a project whose founding complaint is *"it reported healthy while broken"* cannot rest a safety mechanism on that.

So the sweep is split, and **the undocumented part is deliberately not load-bearing**:

**Detection — fully documented, and it decides.** `EnumProcesses` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `QueryFullProcessImageNameW`, keeping any process whose **full image path** equals the Chrome for Testing binary BrowserAI provisioned. **Measured 13.88 ms median** for 611 PIDs / 454 opened ([kb: image-path detection](../kb/windows/detection.md#process-image-path--the-fully-documented-detection-path)) — trivial on a background thread, and the sweep mutex means one process pays it, not ninety-six.

This is path matching against a binary we installed, not image-name matching, and it does not weaken [that rule](D-locking.md#never-by-image-name): matching one absolute path is the opposite of matching `chrome.exe` wherever it appears.

**It also sees the case the window walk structurally cannot: a browser that fell back to a different profile.** A Chrome that could not open our directory retitles its message window to the *fallback* path, so title-keyed detection loses it entirely — and the tempting fix, extending the matcher to cover fallback paths, is the one that would have it matching a personal Chrome. Image-path detection finds it immediately, because it is still our binary.

> ⚠️ **Finding it is not the same as being able to kill it.** A fallen-back browser cannot be attributed to a session, and it may well *belong* to a live one whose directory turned out to be unusable. So it takes the fail-safe path below — reported, not killed. That is the correct outcome and it is still strictly better than not knowing. **The actual defence remains [validating the directory before launch](../README.md#settled-2026-08-15) so the fallback never happens.**

**Attribution — the window title, with a fallback.** Needed only to tie a candidate to a directory, so the [R1](#race-conditions-and-what-closes-each) lock test can run and the report can name the session. `GetWindowTextW` first; on empty, retry `InternalGetWindowText`, which is documented on MS Learn, declared unguarded in the SDK, and does exactly what its documentation says — its caveat is availability, not semantics. Measured zero divergence between them across ~1,550 windows.

**If attribution fails, refuse to kill and report loudly.** This is the property that makes the whole thing acceptable: the undocumented path can only ever cause us to *decline to act and say so*. It can never cause a wrong kill, and it can never cause silence.

**A candidate becomes a stray only when both guards agree:** its image path is our binary, **and** its attributed directory contains our `lock.json` whose lock we can acquire ourselves.

> ⚠️ **Three things this section got wrong, all found by building it at [step 16](build-order.md#16-the-stray-sweep) and all corrected here.**
>
> **The attributed directory is the *profile*, and the `lock.json` is one level up.** A browser publishes its `userDataDir`, and BrowserAI passes `<session>\profile` — so the title names a subfolder of the session and the file that proves ownership is its parent. Attribution climbs exactly one level, and only when the leaf is the profile folder name. It cannot reach a personal Chrome profile that way either: that directory's parent holds no `lock.json`.
>
> **"Whose lock we can acquire ourselves" must not mean `TryAcquire`.** Taking a directory that way rewrites `lock.json` with the sweeper as holder — overwriting a crashed session's own record, its purpose and its history with a janitor's, which is the one piece of evidence about what the stray was. The sweep takes the per-directory gate, opens `lock.json` for write, releases the gate and holds the *handle* across the kill, writing nothing. The gate is not held across a `TerminateProcess`; the handle is what keeps the directory ours, because a concurrent `TryAcquire` opens the same file for write and is refused by the kernel.
>
> **Most unattributable candidates are not strays**, and the report has to say so or it reads as an alarm. A Chromium tree publishes its profile from **one** process — the one that owns the singleton window — so every renderer, GPU and utility process of a browser that is perfectly well accounted for lands in the same bucket. What is worth a human's attention is a pid that is still listed on the next pass with no session open, and that is what [the catalogue row](H-model-surface.md#h4-the-error-catalogue) says.

**Two enumeration-specific hazards, both measured:**

- **The title is an untrusted string that we are about to use as a filesystem path.** A `\\host\share` title makes `File.Exists` block for **21 seconds** (measured: 21,037 ms; a dead hostname 22,225 ms). **Reject anything that is not a rooted local drive-letter path before touching the filesystem** — this is the sweep's single largest availability risk and it is closed by a string check.
- **The walk truncates silently.** If the `prev` handle dies between iterations, `FindWindowExW` returns `NULL` with `GetLastError() == 1400`; normal exhaustion returns `NULL` with error 0. Check and restart, or the sweep under-reports **exactly when browsers are exiting**, which is when it is most likely to be running.

`SendMessageTimeoutW(WM_GETTEXT)` must never be used for this: it is the one API a hung or wedged stray can defeat, and it burns a full timeout doing so.

**`destroy` deletes a whole session directory, and refuses anything without a valid `lock.json`.** That refusal is what makes it safe to hand a model a tool that deletes trees: it cannot be aimed at `Documents\`. It tears the browser down first, then deletes under the lock; if the lock is held elsewhere it reports the holder instead. Pair it with a `list` that shows each session's purpose, mode, last-used and **size on disk** — an agent cannot make a good retention decision without knowing it is sitting on 4 GB.

## Config generation, and validating it against the runtime we ship

BrowserAI generates the child's config; it never accepts one. But **generating a key is not the same as the child honouring it** — `loadConfig` is a bare `JSON.parse` with no schema validation, so a renamed or removed key is discarded in silence, and `--output-mode` was a no-op for its entire life without anyone noticing.

So every generated opinion is **asserted through `browser_get_config`** at startup, against the runtime actually in the payload. A key we set that does not come back is a red build, not a mystery in production.

Three known cases where generation must not take the obvious route:

- **`--sandbox` on the command line, never `chromiumSandbox` in the config file.** The config key parses, validates, and is discarded; only the flag works ([kb: configuration](../kb/playwright/configuration.md)). Assert `--no-sandbox` is absent from the child's **resolved browser command line**, not from our config.
- **`browserName` and an explicit chromium-alias channel, always both.** Omit them and `validateBrowserConfig` fills in `chromium` + `channel: "chrome"` — the user's installed Google Chrome, not the build we provisioned. **Dropping the channel *entirely* is a different mistake with the same shape**, and an earlier draft of this document prescribed exactly that: with no channel, `getExecutableName` selects `headless ? "chromium-headless-shell" : "chromium"` ([kb: configuration](../kb/playwright/configuration.md#defaults-that-are-not-what-they-look-like)), so a headless launch asks for a binary [we never provision](../README.md#settled-2026-08-15). That fails loudly rather than silently — `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` means it cannot appear on disk later — but it fails in the mode that carries most of the traffic. A chromium-alias channel (`chrome-for-testing`) resolves to the full binary in **every** mode, headless included. So: `browserName` explicit, channel explicit, never `chrome`, never absent.
- **`--output-max-size` never set, and `PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE` stripped.** The flag is not the only door.

**The environment is an allowlist, built by `Clear()` first** — `psi.Environment` arrives pre-populated and assignment *merges*. **42** `PLAYWRIGHT_MCP_*` variables can override every opinion we generate, including a capability wipe, so the count is derived from the resolved bundle at test time and never carried as a literal.
