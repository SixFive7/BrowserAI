<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-01 -- a field report about resume and the idle close, checked against the code and the records (sidequest E)

**What this is.** The check of a report another Claude Code session wrote after
using the installed BrowserAI 1.1.0 for a signed-in session in another
repository, between 24 and 29 September: that `browserai_resume` ignores its
per-run arguments on a session the process already holds, that the idle close is
silent, and that a headed window can close under a person. The researcher read
v1.1.0, master and next, and the session records the reporting agent exported
before the sessions were destroyed, and cross-checked them against Claude Code's
own MCP log and BrowserAI's global log. Nothing was run and no browser was
started. **5 files beside this README, 64,837 bytes as cut.**

`REPORT.trimmed.md` is the researcher's report: the claims one by one, the root
causes with file and line at all three refs, and the options per problem.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: provisioning and timings](../../../kb/playwright/provisioning-and-timings.md#what-a-session-keeps-across-a-browser-close-and-what-brings-the-rest-back----measured-2026-10-03) | The idle closes seen in the field and the call that then ran on `about:blank` |
| [kb: what is not established](../../../kb/not-established.md) | The row on why the field session was signed out |

The defects it confirms are decisions and hazards of record, Q324 to Q328, kept
by [`DECISIONS.md`](../../../DECISIONS.md) and
[`HAZARDS.md`](../../../HAZARDS.md); this batch is what they were read from.

## What is here

| File | What it is |
|---|---|
| `REPORT.trimmed.md` | The researcher's report |
| `code-references.tsv` | 49 rows: each code site the report rests on, by file and line at v1.1.0 and at master and next |
| `session-A-timeline.tsv`, `session-B-timeline.tsv` | The two sessions' records as the reporting agent exported them, one row per tool call: id, time, tool, outcome, when it settled, whether its `why` was the idle-close sentence, and the length of its `why` and failure text. **No `why` and no failure text is here, only their lengths** |
| `crosscheck-client-log-vs-exports.tsv` | Tool-call counts per day from Claude Code's own MCP log against the exported records |

## What was cut

- **Private names**, replaced in the report: the other repository's name, the
  site the session was signed in to, the path and id of that session's
  transcript, and the maintainer's first name. Seven replacements, with the
  profile path replaced by `%USERPROFILE%` twice and the SPDX lines added; the
  report is stored under a `.trimmed.` name and `originals.sha256` carries the
  digest of the original. The four tables needed nothing cut.
- **Nothing of the sessions' content.** The timelines carry tool names, times,
  outcomes and text lengths and nothing a page or a person wrote.

## Privacy

The privacy scan, with a positive control, found nothing after the cuts.
