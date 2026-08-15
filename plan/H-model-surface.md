<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# H. The model-facing surface

Everything a model ever reads — tool names, argument descriptions, the server `instructions` string, and every refusal — is generated from **one table in code**. Not four hand-written copies that happen to agree on the day they are written. The failure this prevents is specific and has already happened once in this project's history: a fourth thing gets added, three copies are updated, and the fourth silently describes a system that no longer exists.

> **Naming consequence, not a new decision.** The charter originally injected a parameter called `handle`, back when it was an opaque server-minted token. [That token is gone](C-sessions.md#the-session-directory-is-the-identity) — the directory *is* the identity. Calling a filesystem path a "handle" tells a model to treat it as opaque, which is the opposite of the truth and the opposite of what makes it recoverable after a compaction. **The injected parameter is `session`**, described as the session directory. Renamed here rather than carried forward as a misnomer.

## H.1 The one table

```
mode        headed  storage  description-fragment
headless    no      no       "no window. The default for automation."
interactive yes     no       "a visible window; storage tools refused, so a human can
                              type credentials the agent never sees."
persistent  yes     yes      "a visible window, plus your saved logins."
```

`tracing` is a boolean orthogonal to all three. [Why three and not four, and why headless-with-storage stays closed](C-sessions.md#three-modes-and-tracing-as-a-modifier).

From this table, generated at build time:

| Consumer | Uses |
|---|---|
| Server `instructions` | name + description-fragment, all three |
| `browserai_init` description | name + fragment + what each refuses |
| `browserai_resume` result | the recorded mode's name and fragment |
| Refusal text | the mode that *would* permit the refused call |
| Session-type enforcement | `(tool, mode) → allow/deny`, deny-by-default |
| Tests | every tool classified; an unclassified tool fails the build |

**Six consumers, one source.** A test asserts each consumer's rendering matches the table — a mode added in one place and missed in another is a red build, not a documentation bug discovered in production.

## H.2 The authored tools

Six, all `browserai_` prefixed. [Why not `browser_`](../README.md#settled-2026-08-15) — SEP-2567 names `destroy_*` and `list_*` as the documented companions to a creation tool, so upstream shipping `browser_list` is the expected pattern rather than a hypothetical, and upstream names are never renamed.

| Tool | Arguments | Returns |
|---|---|---|
| `browserai_init` | `directory` **req**, `purpose` **req**, `mode` **req**, `browser` (default `chromium`), `tracing` (default `false`), `consoleLevel` (default `info`), `debug` (default `false`) | resolved absolute paths, mode, browser, provisioning state |
| `browserai_resume` | `directory` **req**, `purpose` (appended, not replaced), `debug` (default `false`), `acknowledgeCopy` (default `false`) | the same, **plus the recorded purpose and its history** |
| `browserai_list` | `directory` **req** | every session **under** that directory: path, mode, browser, purpose, created, last-used, **size on disk** |
| `browserai_destroy` | `directory` **req** | what was deleted, and its size |
| `browserai_set_purpose` | `session` **req**, `purpose` **req** | the new purpose and the previous one |
| `browserai_reinstall_browser` | none | what was removed and re-provisioned |

**`mode` is required with no default.** A default mode is a security posture chosen by omission, and the whole point of `interactive` is that a human relies on it.

**`browser` is accepted only by `init`.** `resume` reads it from `lock.json` and *refuses it as an argument* — a profile is browser-specific, so a caller asking to resume a Firefox directory as Chromium is stating something impossible, and answering "sure" would be the wrong kind of helpful.

**`debug` is accepted by both, and it is the only argument that changes nothing about the session.** It raises this session's log level for its life; the reasoning, and why a second executable was rejected in its favour, is in [§C](C-sessions.md#the-session-directory-is-the-identity). `acknowledgeCopy` is likewise `resume`-only and exists solely to answer [the copied-directory refusal](C-sessions.md#moved-or-copied-the-recorded-path-is-the-discriminator) — a moved directory needs no argument, because a move is repaired without asking.

**`browserai_list` requires a directory and lists everything beneath it.** There is no unscoped form: breadth is stated, never assumed. Pass a drive root to see everything, and the size of what comes back is then the caller's own doing.

The reason is not access control — anything a model can list, it could have found by other means. It is **noise**. Each row carries a `purpose`, free text a previous agent wrote, so an unscoped list would pull every project's session notes into whichever agent happened to ask. Scoping by subtree keeps a session's context inside the tree it belongs to.

This completes an invariant worth naming: **every session-scoped tool names its session explicitly, and none has an implicit scope.** `init` creates a directory, `resume` reclaims one, `destroy` removes one, `list` enumerates beneath one, `set_purpose` names one as `session`. A caller never has to know what BrowserAI would have assumed, because it assumes nothing.

The one exception is `browserai_reinstall_browser`, which takes nothing because it is **machine-scoped by design** — the browser install is shared by every session on the host, which is exactly why it refuses to act while any of them has a live browser.

Filtering is a path-prefix match against the session index, so the directory need not exist — sessions may have been deleted beneath it. A malformed or relative path is refused exactly as everywhere else; a valid path with nothing under it returns an empty list, which is an answer rather than an error.

**Size on disk is reported** because retention is [the calling agent's decision](../README.md#settled-2026-08-15), and an agent cannot make that decision well without knowing it is sitting on four gigabytes.

**`browserai_destroy` refuses any directory without a valid `lock.json`.** That single check is what makes it safe to hand a model a tool that deletes trees: it cannot be aimed at `Documents\`. It closes the browser first, then deletes under the lock; if the lock is held elsewhere it reports the holder instead of waiting.

Every upstream tool additionally gains a required **`session`** parameter, injected into the raw `inputSchema` as a `JsonNode` — [never typed](../kb/mcp/sdk.md), because the typed path silently discards unknown tool-level members.

## H.3 The server `instructions` string

The only channel that reaches a model **before** it calls anything, so it carries the choice itself rather than a pointer to it. Hard cap 2 KB, [silently truncated by the client](../kb/mcp/protocol.md) — the tail of anything longer does not exist, and nothing reports that.

> BrowserAI drives a real browser. Call `browserai_init` first; it returns a session directory that every other tool requires as `session`.
>
> **Modes** — chosen at `init`, fixed for the session's life:
> · `headless` — no window. The default choice for automation.
> · `interactive` — a visible window; cookie and storage tools are refused, so a human can type credentials the agent never sees.
> · `persistent` — a visible window, plus your saved logins. Storage tools allowed.
> `tracing: true` adds a Playwright trace to any of them.
>
> **You must supply a directory.** There is no default. Say where this session's data lives. You must also supply a one-sentence `purpose` — another agent meeting this directory later will read it.
>
> `init` refuses a directory that already holds a session and directs you to `browserai_resume`. That is deliberate, not an obstacle: it turns an accidental collision into a stated intent.

**~1,050 bytes of a 2,048-byte budget.** Measured at build time and failed over budget, as `SixFive7/OutlookAI` already does for its own instructions string. The remaining headroom is deliberate: it absorbs a fourth mode or a fifth authored tool without a rewrite.

## H.4 The error catalogue

Every string here is read by a **model deciding what to do next**, not by a human tailing a console. Three rules, and they are testable:

1. **Name the fix, not just the fault.** "Not permitted" tells a model nothing it can act on.
2. **Recoverable in one turn.** The next call should be able to succeed.
3. **Never blame the caller for a decision we made.** A refused `init` is our design working, and should read that way.

| # | Condition | Text |
|---|---|---|
| 1 | `session` missing | *"This tool needs a `session`. Call `browserai_init` to create one, or `browserai_list` with a directory to see sessions beneath it."* |
| 2 | `session` names no session | *"No BrowserAI session at `<path>` — there is no `lock.json` there. Call `browserai_init` to create one."* |
| 3 | Directory empty, relative or malformed | *"`directory` must be an absolute local path. There is no default: name where this session's data should live."* |
| 4 | `init` on an existing directory | *"A session already exists at `<path>`, mode `<mode>`, created `<date>`, purpose: `<purpose>`. Use `browserai_resume` to take it over — do that only if you expected it to be there. Another agent may be using it."* |
| 5 | Mode refusal | *"`<tool>` needs a session in `persistent` mode; this one is `<mode>`. `interactive` refuses storage tools so a human can type credentials the agent never sees. Create a `persistent` session if the stored logins are yours to read."* |
| 6 | Provisioning in progress | *"First use of this browser version on this machine. The download has started (203.8 MB). Wait about 10 seconds and retry."* |
| 7 | Browser launch failed | *"The browser at `<path>` did not start. If this persists, delete that directory and call `browserai_init` again to re-provision, or call `browserai_reinstall_browser`."* |
| 8 | Lock held | *"`<path>` is in use by PID `<pid>`, running since `<time>`, purpose: `<purpose>`. Wait, or choose another directory."* |
| 9 | Lock held by a dead process | *"`<path>` was locked by PID `<pid>` since `<time>`, which is no longer running. Reclaiming it."* — **not an error**; proceed and say so |
| 10 | `browser` passed to `resume` | *"`browser` cannot be set on resume — this session's profile is `<browser>`, and a profile is browser-specific. Omit the argument, or `browserai_init` a new directory."* |
| 11 | Firefox profile locked | *"That Firefox profile is held by another process. Not launching, because Firefox would raise a desktop dialog and block for up to three minutes."* |
| 12 | Insufficient disk | *"`<path>` has `<n>` MB free; first-run provisioning peaks near 640 MiB. Free space or choose another volume."* |
| 13 | Stray found, unattributable | *"A browser is running from our binary that no session claims (PID `<pid>`). Not terminating it — reporting only."* — diagnostic channel, never `stdout` |
| 14 | Machine-wide lock cannot be created | *"BrowserAI could not create the machine-wide lock that makes a session exclusive (`<reason>`). No session was created. This needs `SeCreateGlobalPrivilege`, which a low-integrity or AppContainer process does not have — there is no reduced-protection mode to fall back to."* — [§D](D-locking.md#global-only-and-there-is-no-fallback); a hard blocker, and the reason is the payload |
| 15 | Session directory was copied | *"`<path>` records that it lives at `<recorded-path>`, and that directory still exists — so this is a copy, not a move. Its ownership record and its purpose describe the original. Pass `acknowledgeCopy: true` to take it over and rewrite the record."* — [§C](C-sessions.md#moved-or-copied-the-recorded-path-is-the-discriminator). A **moved** directory produces no error at all: the record is repaired and the resume proceeds |

Rows **4**, **5** and **9** carry the most weight. 4 is the collision the whole `init`/`resume` split exists to make visible. 5 is the one a model will meet most often and the one that teaches the mode system at the moment it is ready to learn. 9 is not an error at all: the [holder record](C-sessions.md#the-session-directory-is-the-identity) exists so a stale lock reads as a fact rather than a refusal.

⚠️ **`purpose` is replayed into another model's context** in rows 4 and 8, which makes it a channel between agents. Cap it, strip control characters, and frame it as recorded data — *"purpose recorded by a previous session:"* — so it cannot be read as an instruction.

## H.5 Descriptions drift in both directions, and both are silent

**Settled 2026-08-16.** BrowserAI rewrites upstream's tool descriptions — [names pass through byte-for-byte, descriptions do not](../README.md#settled-2026-08-14) — and a rewrite has two edges, not one. The plan previously guarded neither.

**Direction 1 — ours breaks theirs.** Upstream descriptions carry text a model acts on: what a tool refuses, what it costs, which argument is destructive. Our rewrite trims, re-frames and appends, and a phrase dropped in that pass is a warning the model no longer receives. Nothing fails; the tool still works, and the model simply stops being told the thing that stopped it doing something stupid.

**The check:** a set of **required phrases per tool**, asserted to survive the rewrite. Not a whole-string comparison — the whole point of the rewrite is that the string changes — but a declared list of substrings whose disappearance is a red build. A phrase is added to that list the moment anyone decides upstream's wording is load-bearing, which makes the list the record of *why* a sentence is there. This direction had no coverage of any kind before 2026-08-16, and it is the one entirely within our control.

**Direction 2 — theirs breaks ours.** Upstream rewords a description underneath our appended text. Ours still appends; theirs now says something else; the result contradicts itself, or says the same thing twice in two voices, and both halves are individually correct. This is the harder direction because the defect is in the *seam*, and nothing that checks either side alone can see it.

**Detection is already half-solved and should not be re-solved.** [The `tools-list.json` golden snapshot](testing.md#what-the-gate-actually-checks) carries the description strings, so a reworded description already produces a diff on every build. What it does not do is ask the right question about that diff: it reports that a string changed, and the question that matters is whether *our sentence still belongs beside their new one*.

**So the addition is adjudication, not detection.** Snapshot upstream's description text **per tool** at review time, alongside the appended text we pair it with. On every build, a changed description surfaces as a three-way view — upstream before, upstream after, our addition — and is **flagged for agent review, blocking the build or the release until it is adjudicated**, exactly as [an unadjudicated snapshot change already does](testing.md#the-upstream-review-gate). The adjudication is recorded in the marker entry with the rest, and *"reviewed, no change needed to our text"* is a complete answer. An unanswered one is not.

This is deliberately the same machinery as the upstream-review gate rather than a second mechanism beside it. The gate's whole argument is that enforcement belongs in the suite as evidence rather than as assent; a description that quietly stops making sense is precisely a change the build observed and nobody adjudicated.

## H.6 What the tests assert

- Every consumer's rendering matches the one table; a mode present in one and absent from another fails the build.
- The `instructions` string and every tool description are **measured in bytes** and fail over 2 KB.
- Every error above is produced by an actual triggering condition, not asserted as a literal — a string that no code path emits is documentation, not behaviour.
- Rows 1–3 each name a recovery tool, and the named tool exists.
- Row 5 names the mode that would permit the call, derived from the table rather than written by hand.
- `purpose` round-trips through `lock.json` with control characters stripped and length capped, and every timestamp round-trips as [ISO 8601 invariant under a non-invariant culture](C-sessions.md#our-own-files-reject-what-they-do-not-recognise).
- `browserai_destroy` refuses a directory with no `lock.json`, one with a `lock.json` of the wrong schema version, and one carrying a key it does not recognise.
- **Every required phrase survives the rewrite**, per tool, from the declared list in [§H.5](#h5-descriptions-drift-in-both-directions-and-both-are-silent).
- **An upstream description that changed and has not been adjudicated fails the build**, carrying upstream's old text, its new text and our appended text in the failure message.
- **No conditional compilation reaches the enforcement path** — zero occurrences of `#if`, `[Conditional]` or a configuration-dependent branch in the `(tool, mode)` decision, asserted at analyzer-error severity. [Why it is stated as a property of the artifact](A-runtime.md).
- **The handle→type lookup holds under concurrency**, driven across handles of different modes at once ([§testing](testing.md#the-tests-enumerated)).
