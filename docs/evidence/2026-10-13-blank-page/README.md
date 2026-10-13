<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-13 -- a Firefox session's restore with Playwright's blank page kept, and the reproduction for Playwright

Cited by
[A Firefox session's restore with Playwright's blank page kept starts in about a second, and closing the blank page once a reopened tab is up breaks nothing](../../../kb/playwright/provisioning-and-timings.md#a-firefox-sessions-restore-with-playwrights-blank-page-kept-starts-in-about-a-second-and-closing-the-blank-page-once-a-reopened-tab-is-up-breaks-nothing----measured-2026-10-13)
and
[Firefox reports no `load` for its network error page, with every launch option at its default](../../../kb/playwright/provisioning-and-timings.md#firefox-reports-no-load-for-its-network-error-page-with-every-launch-option-at-its-default----measured-2026-10-13),
by [the hazard index](../../../HAZARDS.md), whose row of the maintainer's 125 b closed with
his 161 of 2026-10-13, in his words verbatim *"161 your recommendation"*, and by
re-verification row 222. The rig is
[`docs/probes/2026-10-13-blank-page`](../../probes/2026-10-13-blank-page/README.md). The report
to Playwright these batches gave the reproduction for is
[microsoft/playwright#43341](https://github.com/microsoft/playwright/issues/43341).

Everything was taken on 2026-10-13 under the suite's lock with the payload of that day,
`@playwright/mcp` 0.0.83, `playwright-core` 1.64.0-alpha-1790635538000 and node v24.21.0, and
Firefox 156.0 (`firefox-1553`) and Chrome for Testing 155.0.8059.12 (`chromium-1247`), each
browser a copy of the provisioned one in a scratch browsers root. One folder per batch:

| Folder | What it holds |
|---|---|
| `blank/` | The `blank` stage, 15:31:48Z to 15:49:52Z: three rounds of the five arms `one-dead`, `one-alive`, `three-mixed`, `none` and `fresh`, interleaved, through the payload's own child with BrowserAI's Firefox config and nothing in `ignoreDefaultArgs`, two resumes in each repetition. `summary.txt` is the tables `summarize.mjs` printed |
| `repro/` | The `repro` stage, 15:49:52Z to 15:50:50Z: through `playwright-core` alone, the persistent launch onto a restored tab with the blank page left out and kept, with the server ended and answering, the page `no-store` and cacheable; and a `goto` to a refused address with a wait for `load`, in Firefox and Chromium |
| `navigate/` | The `navigate` stage, 15:52:02Z to 15:52:24Z: a page that navigates itself to a refused address and waits for `load`, in Firefox and Chromium, every launch option at its default |
| `issue/` | The `issue` stage, 16:02:21Z to 16:03:24Z: the two scripts the report carries, run as written, and the first again with Chromium. Each folder holds the copy that ran and `node.stdout.txt`, what it printed |

**In each batch folder:** `build.txt`, the payload, `playwright-core`'s version, the commit
and the stage; `launcher.log`, each step's start and end inside the lock the holder kept;
`browsers.txt`, the copies; `registry-<stage>.txt`, what Firefox added under
`HKCU\Software\Mozilla\Firefox` and what the launcher took out again; `summary.txt`; `cuts.txt`,
what was done to each file; and `originals.sha256`, the SHA-256 and size of every file a copy
here was made from.

**In each repetition of `blank/`:** `result.json`, every call of both resumes with its answer
and duration, every listing with its time, the session files decoded, and the reads of each
tab; `config.json`, the config the child launched from; `events.jsonl`, the rig's own record;
`census.jsonl`, the members of the step's job every five seconds; and the standard error of
the first run's child and of each resume's, with Playwright's protocol log on.

**Not here: the profiles**, which hold a browser's whole state; **the full registry before
each batch**, which lists every value Firefox keeps for every install on this machine; **the
`node_modules` of the `issue` folders**, a `package.json` and one line that re-exports the
payload's `playwright-core`; and **the `issue` batch's `tmp`**, where `restore.mjs` made its
profile. Each copy's digest is in `originals.sha256` where one was cut from a file.

**How these bytes depart from the ones taken**, file by file in `cuts.txt`: the user's profile
path is `%USERPROFILE%` and terminal colour escapes are removed, under a `.trimmed.` name; and
a child's standard error, which carries Playwright's whole protocol log, keeps the lines of
each page's attach, its main frame's commits, aborts, readiness, `DOMContentLoaded` and `load`,
the launch and the API calls, each cut at 1,500 characters. ⚠️ **The `summary.txt` of `repro/`
and `navigate/` ends in three empty tables**, the repetition tables, which the summarizer of
that hour printed for every batch with a row of `undefined`; it prints them for a `blank`
batch alone since, and the tables above them are the batch's own. Line endings are what
`.gitattributes` normalises.
