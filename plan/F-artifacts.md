<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# F. Artifact management

> ✅ **Built 2026-08-16** by [build-order step 14](build-order.md#14-artifact-routing). `src/BrowserAI/Artifacts/{ArtifactRouting, ArtifactTools, ArtifactFilename, ArtifactRouter, ResultNote}.cs` · `src/BrowserAI/Sessions/{SessionLayout, SessionManager, LiveSession, SessionErrors}.cs` · `src/BrowserAI/Proxy/BrowserProxy.cs` · the `artifactPrefixes` section of `build/upstream-snapshots.mjs` · `tests/BrowserAI.Tests/{ArtifactRoutingTests, LosslessPassthroughTests, ErrorCatalogueTests}.cs`.
>
> ⚠️ **The nine prefixes below are eleven, and that was found by building the gate this section asks for.** Deriving the set from the resolved bundle rather than counting it by hand found `element` (an element screenshot, behind a ternary) and `annotations` (from `browser_annotate`, behind a template literal), neither visible to a scan for `prefix: "<literal>"`. Corrected in [kb](../kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour) and in the layout below on the same day. **The coverage gate found its first drift on the day it was written**, which is the argument for it.
>
> ⚠️ **`traces\` is upstream's folder, not ours.** This section says *"the folder is ours by choice"*; measured, upstream computes `<outputDir>/traces` and it is not configurable. What the section gets right is that it is **not a generator prefix** — the template supplies its own `suggestedFilename` — and the sort still must not pretend to derive it.
>
> ⚠️ **A `filename` is supplied only where upstream would have generated one.** Five of the nine writing tools document it as *"if not provided, the result is returned as text"*, so supplying a name there would silently change what the call does. The four that document a `page-{timestamp}` default always write, and those are the four that get a legible name. This section's *"Names must be legible"* obligation reads as though it applies to all of them.
>
> ⚠️ **Session-relative, not repository-relative.** The fourth obligation asks for `docs/screenshots/login.png`; BrowserAI does not know where a repository root is and cannot invent one. What it returns is the path relative to the session directory the caller itself named, which is what [filename normalisation](#filename-normalisation) item 5 asks for. The two halves of this document disagreed and the normalisation list is the one that was implementable.

Playwright writes every artifact flat into one directory with a generated name, mixing machine churn with hand-named work. Eleven fixed generator prefixes make classification exact rather than heuristic: `annotations`, `console`, `download`, `element`, `network`, `page`, `request`, `response`, `result`, `storage-state`, `video` — plus one empty prefix, which is the traces template ([kb: artifacts](../kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)).

Port the prefix-based sort. **Classification must be by generator prefix, never by date** — that is precisely what keeps a hand-named file out of the machine-generated folders, and no date rule can make that distinction.

## Route on the way in, do not sort on the way out

A sort is cleanup; a proxy can do better. BrowserAI sees every `tools/call` before the child does and every result before the caller does, so **files can be born in the right place instead of being swept there.** Three levers, in increasing order of effort:

1. **Set the child's `WorkingDirectory` to the instance's output root.** Already required by [§Windows process spawning](../README.md#windows-process-spawning) for a different reason. It makes the stray-file failure *impossible* rather than *caught*: a bare `foo.png` resolves inside the instance tree by construction. Ten repositories run a `deny` hook — counted 2026-08-13 by a filesystem sweep of `C:\Source` (recorded in the charter's opening table) — because upstream resolves a relative `filename` against the child's cwd ([kb: artifacts](../kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)) — this closes that without a hook.
2. **Normalise `filename` on the way in.** Route it to the typed subfolder its generator prefix implies, so the agent never has to construct a path.
3. **Return the resolved absolute path on the way out.** Non-negotiable if lever 2 is used. Silently relocating a file while telling the model it went somewhere else produces an agent that confidently reports the wrong location — a new silent failure introduced by the fix for an old one.

> **Lever 3 collides with [step 9's byte-identical passthrough](build-order.md#9-lossless-passthrough), and the seam goes at *forwarding*.** Both requirements are right and neither may be narrowed, so the resolution is mechanical rather than a compromise: the note is **spliced into the child's own bytes** — the `content` array is found by token offset and one element is inserted before its closing bracket — so nothing the child wrote is re-serialised, re-escaped or reordered. The guarantee is therefore now stated as two halves. *A call BrowserAI forwarded unchanged comes back unchanged, byte for byte* — that is every call naming no file. *A call whose request BrowserAI rewrote comes back with every byte the child wrote, in order, plus one appended element.* There is nothing in between: BrowserAI never edits a byte the child wrote, on either path.

**There is no default root, because there is no default.** `init` requires an explicit session directory and rejects an empty or invalid one — see [the `init` contract](C-sessions.md#the-init-contract). An earlier draft defaulted to `%LocalAppData%\BrowserAI\sessions\<label>\` when given no path; that is now an error instead. The founding stray-file problem was files landing in repo roots *because nobody chose where they should go*, and a safe default answers the symptom while preserving the cause. Making the caller name the location is what actually removes it.

Layout beneath the session directory. The typed folders are **named for the generator prefixes, spelled exactly as upstream spells them**, because a folder whose name differs from the prefix that fills it is a mapping table nobody maintains:

```
<session-dir>\
  lock.json
  browserai.log
  session.json             <- the artifact index
  profile\
  downloads\               <- the `download` prefix, at the session root
  output\
    annotations\  console\   element\   network\
    page\         request\   response\  result\
    storage-state\          video\
    traces\                <- not a generator prefix; see below
```

Ten prefixes get a folder under `output\`. The eleventh is `download`, and it sits at the session root as `downloads\` because **it is the one exception to routing** — a browser-initiated download lands where the browser puts it, not where a `filename` argument says, so it is classified after the fact like the old sort. That difference should be visible in the code rather than discovered.

**`annotations` is the second exception, for a different reason.** `browser_annotate` carries no `filename` argument at all, so there is nothing to rewrite on the way in and its files land in the output root with a generated name. It is sorted after the fact by the same routine as a download — which is why the sweep exists at all rather than being a download special case.

**The typed folders are created on first use, not at `init`.** Measured 2026-08-16: creating all fourteen costs **10.4 ms** per session against **2.5 ms** for the three roots, about a second each way per suite run — and it leaves ten empty directories in every session a caller ever makes, for generators they never used, which is navigational noise in the tree this section exists to make navigable. Nothing is lost by being lazy: the folder set is declared in code and asserted against the resolved child's prefixes on every build, and `session.json` names every one with its resolved path whether it exists yet or not. What changes is that a folder on disk now *means* an artifact of that kind was produced.

**`traces\` is the one folder that is not a prefix.** There is no `trace` generator prefix: the template carries an empty prefix and its own `suggestedFilename`, so the sort must not pretend to derive the folder — but the path is upstream's `<outputDir>/traces` rather than a name we chose, and it is not configurable. An earlier version of this layout listed eight folders, called them "the nine generator prefixes", renamed four of them (`screenshots` for `page`, `results` for `result`, `storage` for `storage-state`, and `traces` for nothing at all) and omitted `request` and `response` entirely — which is exactly the drift the exact-spelling rule above exists to stop.

### The prefix set is a coverage gate

**Settled 2026-08-16. A twelfth upstream generator prefix must fail the build**, exactly as [an unclassified tool does](H-model-surface.md#h1-the-one-table). The test derives the prefix set from **the resolved child at test time** — never from a list typed into a `.cs` file — and asserts it equals the set of folders this layout declares, with `download` and the empty traces prefix named as the two deliberate exceptions rather than silently tolerated.

**A tool that arrives carrying a `filename` nobody has classified is the same failure and is gated the same way.** Eleven do today, nine writing and two reading; the classification is deny-by-default and a twelfth turns the build red.

Until 2026-08-16 the plan had no such gate, and the absence was asymmetric in a way worth naming: **the same rule already existed for tools and had no counterpart for artifacts.** An upstream tool that arrives unclassified is a red build; an upstream *artifact prefix* that arrives unclassified is a file that silently lands in whichever folder the sort's fallback happens to name — or in the output root beside the typed folders, which is the flat directory this whole section exists to replace. The prefix count is a measured count ([kb: artifacts](../kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)), not a fixed property of Playwright, and under [a floating payload](../README.md#versioning-policy-everything-floats-the-build-freezes-it) a measured count is a thing that moves — as this one did, by two, on the day the gate was first written.

Two failure directions, and both must be red:

- **A prefix with no folder** — upstream added a generator. The artifacts exist and are misfiled.
- **A folder with no prefix** — upstream removed or renamed a generator. The folder is now dead, and worse, the *rename* case presents as one of each: an unexplained new prefix beside an unexplained empty folder, which is exactly the diff that says what happened.

The failure message carries both sets and their difference, on the same principle as [the upstream-review snapshots](testing.md#what-the-gate-actually-checks): not *"the prefix list changed"*, but *"here is what moved — adjudicate it."*

### Filename normalisation

[§F](#f-artifact-management) routes on the way in. The mechanics:

1. **Reject traversal; never normalise it.** `..\..\foo.png` must resolve, be recognised as an escape, and be refused with a readable error — never silently collapsed into a path that happens to land somewhere.
2. **Reject absolute paths and drive-relative forms** (`C:foo.png`, `\\server\share`, `\foo.png`). The caller declared the workspace at `init`; a per-call filename names a file *within* it.
3. **Route by generator prefix** into the typed subfolder, so a screenshot and a trace do not land beside each other. A hand-named file is never swept into a machine-named folder.
4. **Suffix duplicates rather than overwriting.** An artifact silently replacing an earlier one is data loss wearing a success.
5. **Return both forms** — the resolved absolute path and the path relative to the session directory. The agent needs the first to tell a human where something is, and the second to put in a commit message.

> **The two path rules read as contradictory and are not.** `init`'s directory arguments are deliberately unconstrained — the caller is declaring where its data lives. A per-call `filename` names a file inside a workspace already declared, so normalising it into that workspace *honours* the choice already made rather than overriding it. Record the distinction, because anyone meeting the two rules cold will think one of them is wrong.

## Four obligations that follow

- **Names must be legible.** Upstream generates `page-2026-08-14T04-11-50-882Z.png` ([kb: artifacts](../kb/playwright/tools-and-artifacts.md#artifacts-and-output-directory-behaviour)). Prefer the caller's own name where one was given, and a page-derived slug plus a counter where none was — `checkout-step-3.png` survives a month, a timestamp does not. This is what made 346 session directories unnavigable. **Only where upstream would itself have generated a name**: for the five tools whose `filename` is documented as *"if not provided, the result is returned as text"*, supplying one takes the answer out of the response and puts it in a file the caller never asked for, which is a louder failure than an unreadable name.
- **Never overwrite silently.** Two screenshots named `login.png` in one session is data loss. Suffix, and say so in the result.
- **Report cumulative session size in the result.** The current setup reached 1.5 GB in three months with nothing saying so. BrowserAI routes every file and therefore knows; not reporting it is a choice to stay blind.
- **Return a session-relative path alongside the absolute one.** When an agent writes a commit message, a PR body or a report, `output\page\login.png` is what it needs; an absolute path is machine-specific and useless there. BrowserAI resolves both anyway — emitting only one of them discards work already done. *(This bullet said **repository**-relative until 2026-08-16. BrowserAI has no idea where a repository root is and would have to invent one; the session directory is the root the caller itself named, and it is what [normalisation item 5](#filename-normalisation) already asked for.)*

## The artifact index

> Not to be confused with [the session index on disk](D-locking.md#the-session-index-on-disk), which is a different file solving a different problem: that one lists *which session directories exist*, machine-wide, one file per session. This one lists *what is inside one session*, and lives in that session's own folder.

Routing means BrowserAI knows, at write time, every fact worth recording: which tool produced a file, when, at which URL, in which session and under which session type. Not writing that down throws away information that cannot be reconstructed afterwards — which is exactly how 346 session directories became untriageable. **The directory name and its `purpose` tell you what a session was; the index tells you what is in it.**

Write `session.json` into each session folder as artifacts are routed: the session's own resolved path, created and last-touched timestamps, the resolved absolute path of each subfolder, and one entry per artifact with its tool, timestamp, page URL, size and both path forms. Mode, browser and `purpose` stay [`lock.json`'s](C-sessions.md#the-session-directory-is-the-identity) to own — `session.json` is the artifact record, and a second copy of the session's identity is a second thing to disagree with the first.

**Scope the roll-up by root, never by machine.** BrowserAI is registered once and serves every repository on the host, so an index that aggregates everything would pull sessions from unrelated projects into whatever context happens to be open. That is a **noise problem rather than a security boundary** — the paths were the caller's own choice and [§settled](../README.md#settled-2026-08-13) accepts any of them — but noise in an agent's context is a real cost, and the cheap fix is to default the aggregate to the root in play:

- A roll-up index sits at each **output root**, covering sessions beneath that root only.
- `init`'s result names prior sessions under the same root — count and labels, nothing wider.
- A machine-wide view stays available and is **opt-in**: an explicit request for everything, or for a root that happens to contain everything, returns everything. Scoping is a default, not a restriction.

**No new tool is needed for any of this, and none should be added.** The index is a file; the calling agent reads it with its own filesystem tools. A `browser_list_artifacts` tool would be BrowserAI composing a capability out of its own state — the boundary this document holds everywhere else, and there is no reason to cross it for something `Read` already does.

## Retention is no longer ours alone to promise

An earlier draft of this section said *"Nothing is ever auto-deleted."* That is **no longer true of the runtime**: `@playwright/mcp` now carries `--output-max-size <bytes>`, *"Threshold for evicting old output files, in bytes."* Unless BrowserAI asserts it stays unset — and strips `PLAYWRIGHT_MCP_OUTPUT_MAX_SIZE`, which is the other door to it — the promise in this document is not the promise the child keeps, and a silently evicted artifact is precisely the failure class in [the README's opening table](../README.md#read-this-before-designing-anything). **Settled 2026-08-15: it has no default at any merge stage** (`defaultConfig` carries only `browser` and `timeouts`; `mergeConfig` filters through `pickDefined`, which drops `undefined`), so eviction is off unless someone turns it on. [kb: upstream config](../kb/playwright/configuration.md).
