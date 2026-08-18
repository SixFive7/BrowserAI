<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# What "2KB each" actually means

**Measured 2026-08-18 against Claude Code `2.1.234`** (`claude.exe`, native
install at `%USERPROFILE%\.local\bin`), Windows 11 Pro 26200, Node v26.7.0.

The question: Claude Code's [MCP documentation](https://code.claude.com/docs/en/mcp)
says *"Claude Code truncates tool descriptions and server instructions at 2KB
each"*, and **"each" does not say each what.** Per string, or per whole serialized
tool? BrowserAI's gate assumed per string and said so; another MCP server's
maintainer reported that trimming a description fixed nothing, which is what the
per-tool reading predicts.

**Answer: per string, 2,048 UTF-16 characters, cut at `> 2048`.** Every question
below is answered, none is left open.

---

## Method

The only ground truth is what the model receives, and asking a model to recall a
marker is a weak instrument: a refusal, a paraphrase or a safety deflection is
indistinguishable from a truncation. So the model was cut out of the loop
entirely.

**Claude Code honours `ANTHROPIC_BASE_URL`.** Point it at a local HTTP server and
the full `POST /v1/messages` body — including the `tools` array, byte for byte —
lands on disk. With `ANTHROPIC_AUTH_TOKEN` set to a throwaway string the local
server answers with a minimal SSE stream and the client is satisfied. **No real
credential is used, no request reaches Anthropic, and the experiment costs
nothing.**

Auth notes, both learned the hard way:

- `ANTHROPIC_API_KEY` with a made-up key **does not work** — the client prints
  `Not logged in · Please run /login` and never sends a `/v1/messages` request.
  It still hits the base URL for `HEAD /api/hello`, which is how the interception
  was confirmed to work before the auth shape was solved.
- `ANTHROPIC_AUTH_TOKEN` (bearer) **does** work, immediately.

The probe is a raw JSON-RPC stdio MCP server (no SDK) publishing descriptions of
exact byte and character length, each ending in a unique marker
(`MK-<WHAT>-<WHERE>-9F21`), registered with
`claude mcp add <name> --scope user` against a **scratch `CLAUDE_CONFIG_DIR`**.

**Controls.** Every probe server publishes a tiny control tool
(`probe_control`, `probe_control2`, `probe_control3`) whose marker must be present
for the run to count; all were present in all runs. The truncation predicate was
also confirmed non-vacuous in both directions in the same request — strings under
the cap arrived byte-identical, strings over it did not.

**Reproducibility.** The decisive run was repeated against two different models
(`sonnet` and `haiku`) with byte-identical results, which is expected: the cut is
client-side and the model never sees the uncut text.

Scripts (untracked scratch, `.work/truncation/`):

| File | What it is |
|---|---|
| `capture.js` | HTTP recorder + minimal SSE responder. Redacts `authorization` / `x-api-key` / `cookie` before writing |
| `probe-server.js` | Probe MCP server, modes `main` (6 tools) and `bulk` (66 tools) |
| `probe-server2.js` | Boundary triple, astral-plane probes, 20 KB parameter, 17 KB entry |
| `probe-server3.js` | Surrogate-split probe, construction asserted before publication |
| `analyse.js` | Diffs the captured `tools` array against what the probe published |
| `cap-main/`, `cap-wide/`, `cap-bulk/`, `cap-real/`, `cap-cur/` | Captured request bodies |

---

## The five questions

### 1. Per string, or per serialized tool? — **PER STRING**

`probe_entry_over` published a **1,500-character description** and **four
700-character parameter descriptions**. Every individual string is comfortably
under 2,048; the whole serialized entry is **4,565 B** as the MCP server wrote it
and **4,578 B** as Claude Code sent it to the API.

It arrived **completely intact**. All five markers present, every string
byte-identical to what was published.

Pushed harder in round two:

| Probe | Whole entry | Result |
|---|---|---|
| `probe_entry_over` | 4,578 B | intact |
| `probe_entry_huge` (8 params × 2,000 chars) | **17,411 B** | intact, all 8 markers |
| `probe_param_huge` (one 20,000-char param) | **20,172 B** | intact |

**There is no per-tool bucket.** The competing reading is dead.

**Therefore `browserai_init` is not truncated and never was.** Its whole
`tools/list` entry is 3,428 B on the MCP wire and 3,360 B as the client sends it
to the API — over 2,048 and irrelevant.

### 2. Are `inputSchema.properties[*].description` strings truncated? — **NO**

`probe_param_over.big` published **2,600 characters** on a tool whose own
description was 60 characters. Arrived whole, all five markers.

`probe_param_huge.huge` published **20,000 characters**. Arrived whole, all four
markers including `MK-PARAMHUGE-END-9F21` at character 20,000.

**Parameter descriptions are not capped at any value this experiment could
reach.** The documentation's silence about them turns out to be accurate rather
than an omission.

### 3. Bytes or characters? — **UTF-16 CHARACTERS. Bytes are never counted**

| Probe | Published | Sent | Cut at |
|---|---|---|---|
| `probe_desc_over` (ASCII) | 2,600 c / 2,600 B | 2,061 c / 2,063 B | **2,048 characters** |
| `probe_multibyte` (em dashes) | 2,669 c / **7,801 B** | 2,061 c / 6,019 B | **2,048 characters** = 6,004 bytes |

Two strings with wildly different byte lengths were cut at the *same character
offset*. A byte cap would have cut the em-dash string at character ~683.

**A 2,048-character string weighing 6,004 bytes arrives whole.**

**The boundary predicate is `> 2048`**, established as a triple in a single run:

| Probe | Published | Sent | Verdict |
|---|---|---|---|
| `probe_desc_2047` | 2,047 c | 2,047 c | intact |
| `probe_desc_2048` | 2,048 c | 2,048 c | **intact** |
| `probe_desc_2049` | 2,049 c | 2,061 c (2,048 + suffix) | **cut** |

**UTF-16 code units, not Unicode code points.** `probe_astral_aligned` published
1,539 code points spread over 3,000 UTF-16 units (5,922 bytes). Under a
code-point cap, 1,539 < 2,048 and it would have arrived whole. It was cut — at
**2,048 units / 1,044 code points**.

**The cut is surrogate-aware.** `probe_split_true` was constructed so that UTF-16
index 2,047 is a *high* surrogate and 2,048 its *low* partner — the construction
is asserted in the probe before it publishes, so this is not a lucky alignment.
A naive `slice(0, 2048)` would have left a lone high surrogate. The delivered
string was cut to **2,047** units instead, ending on a complete pair, and the
whole payload is well-formed (checked with a lone-surrogate regex).

So the exact rule is: **cut to the largest offset ≤ 2,048 UTF-16 units that does
not split a surrogate pair.**

### 4. Is there a total budget across `tools/list`? — **NO**

Two copies of the 66-tool bulk probe plus a third server were registered at once:

- **202 tools** in the request (including Claude Code's own built-ins)
- **348,314 bytes** of serialized tool entries
- request body **392,983 bytes**

**Nothing dropped and nothing cut.** All 120 bulk tools present, indices 0–59 in
both servers, every `MK-BULK-nn-END` marker intact, including
`mcp__bulkB__probe_bulk_59`.

For scale: BrowserAI's whole surface is 65 tools / 51,149 B of entries — under
15% of what went through untouched.

*Caveat stated rather than hidden:* this establishes no cap **at 348 KB**. A cap
above that was not probed, and a request that large would be a token problem long
before it was a truncation problem.

### 5. What does truncation look like? — **A hard positional cut plus a visible marker**

The cut string is the published string's **exact prefix** (verified by
`orig.startsWith(cut)` on every truncated probe), followed by the literal:

```
… [truncated]
```

That is U+2026 HORIZONTAL ELLIPSIS, a space, and `[truncated]` — **13
characters, 15 bytes**. A truncated string therefore arrives at **2,061
characters** (or 2,060 after a surrogate backoff).

Nothing is dropped wholesale: not the field, not the tool. The tool is still
present, still callable, with a description whose tail is gone.

⚠️ **It is visible to the model and invisible to the server.** The suffix is
added after the JSON-RPC response has left the MCP server, so **a server cannot
detect its own truncation** — no error, no notification, no second request. That
is precisely why a build-time gate is the only mechanism available. Conversely, a
*model* can see the marker, so *"did this arrive whole?"* is answerable by asking
and unanswerable by logging.

---

## Two things nobody asked, found on the way

**Server `instructions` are capped the same way, and delivered somewhere other
than the obvious place.** A 2,600-character probe `instructions` string was cut
at 2,048 with the same suffix. It arrives inside a `<system-reminder>` block in
the **`messages`** array, under a `## <server-name>` heading alongside every
other connected server's instructions — **not** in the `system` prompt. BrowserAI's
own is 1,261 characters and arrives whole.

**The API tool object is minimal.** Claude Code sends `{name, description,
input_schema}` and nothing else — no `title`, no `annotations`, no
`outputSchema`. The name is namespaced to `mcp__<server>__<tool>`. So an entry
total measured on the MCP wire and one measured on the API wire differ slightly
(`browserai_init`: 3,428 B vs 3,360 B); either is fine for judging a per-tool
budget that does not exist.

---

## Cross-check against the real surface

The shipped BrowserAI binary was registered against a scratch config and captured
the same way:

| | |
|---|---|
| tools | 65 |
| sum of tool entries as sent | **51,149 B** |
| strings truncated anywhere | **none** |
| largest description | `browserai_init`, **1,623 characters** / 1,639 B |
| `browserai_init` whole entry as sent | 3,360 B — intact |
| `instructions` | 1,261 characters / 1,276 B — intact |

These match `.work/description-budget.txt` exactly, which is the artifact
`ModelSurfaceTests.EveryModelFacingStringFitsTheClientsSilentTruncationBudget`
writes off the published binary's own wire. **Two independent instruments, same
numbers.**

*(An earlier cross-check used `artifacts/publish-release/BrowserAI.exe`, which is
a stale build from 2026-08-17 whose `browserai_init` description was 1,975
characters — still intact, but it does not match the current figures. Noted
because the stale artifact is a trap for the next person.)*

---

## What was changed as a result

| File | Change |
|---|---|
| `src/BrowserAI/Proxy/ClientTruncationBudget.cs` | The ⚠️ ASSUMPTION block replaced by the measurement. `Bytes` → `Characters`; `ParameterDescriptionBytes` → `ParameterDescriptionCharacters`, relabelled a **house limit** rather than a client limit |
| `src/BrowserAI/Proxy/ServerInstructions.cs` | `MaximumBytes` → `MaximumCharacters`; `CharacterCount` added beside `ByteCount` |
| `src/BrowserAI/Sessions/SessionToolSurface.cs` | `DescriptionMaximumBytes` → `DescriptionMaximumCharacters`; parameter constant likewise, with the honest label |
| `tests/BrowserAI.Tests/ModelSurfaceTests.cs` | Gate is now the measured predicate — `Length > 2048`, characters — instead of `max(chars, bytes)`. Bytes still reported. Entry totals still reported and still unasserted, now as the figure a future per-tool bucket would be judged against |
| `kb/mcp/protocol.md` | New section with the full measurement, the method, and a re-run recipe |
| `kb/re-verification.md` | Row 92; counts restamped 192→193 markers, 91→92 rows |
| `CLAUDE.md`, `TESTING.md`, `HAZARDS.md`, `QUESTIONS.md`, `CHANGELOG.md` | The assumption retired everywhere it was stated; questions 1 and 10 answered in place with their `previously` clauses |

**Nothing needed shrinking.** Under the per-tool reading `browserai_init` would
have needed splitting; under the measured reading it has 425 characters of
headroom.

**One gate got weaker on purpose, and it is the right direction.** The old gate
failed on the byte count, which for UTF-8 is never below the character count. It
could therefore only produce *false* failures — a 2,000-character description
carrying em dashes would have failed a build over text the client delivers whole.
Gating on the measured unit is not a relaxation of rigour; it is the end of a
guess.

---

## What is still open

**Nothing about the current behaviour.** All five questions are answered with
direct evidence.

**Everything about its stability.** Every figure here is a Claude Code
`2.1.234` fact, and nothing watches it. There is no notification, no version
header, and no server-side signal when it changes. The dangerous direction is a
release that *introduces* a per-tool bucket: BrowserAI's `browserai_init` would
fail it on day one, silently. That is what re-verification row 92 exists for, and
the only thing that will bring it back up is somebody working through that table
during an upstream review.

**Not probed, and honestly so:**

- Whether the same cap applies to Claude Code's **built-in** tool descriptions
  (only MCP-sourced tools were controlled).
- Whether a total budget exists **above** 348 KB.
- Whether other MCP clients (Cursor, Windsurf, Zed, the Claude desktop app)
  behave the same way. **They almost certainly do not** — this is one client's
  implementation detail, not a protocol rule. The method transfers; the numbers
  do not.
