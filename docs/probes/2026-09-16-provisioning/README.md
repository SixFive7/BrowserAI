<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-16 - first-run provisioning, wire and disk and wall clock

The re-establishment named by
[First-run provisioning](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)
and
[Firefox, measured the same way](../../../kb/playwright/provisioning-and-timings.md#firefox-measured-the-same-way----2026-08-19),
which is [re-verification row 21](../../../kb/re-verification.md).

`Measure-Provisioning.ps1` empties a scratch browsers root, runs upstream's own
`install-browser <family> --no-shell --no-progress` out of the payload with
`PLAYWRIGHT_BROWSERS_PATH` pointed at that root, times it end to end on a
`Stopwatch`, and then sums every file under the root broken out by component.
Two runs per family; the 2026-09-16 pair came back byte-identical for both.

`Get-DownloadUrls.ps1` derives the four CDN URLs from the payload's **own**
`browsers.json` so that no revision is ever typed. ⚠️ **Chromium does not
resolve the way the other three do** -- `playwright-core` builds its URL with
`cftUrl()`, which is `builds/cft/<browserVersion>/win64/chrome-win64.zip` keyed
on the browser version, off the bare `https://cdn.playwright.dev` mirror rather
than the `/dbazure/download/playwright` one. The wire figures are the
`content-length` of a `HEAD` on those URLs, and the URLs were confirmed against
the string upstream's own installer prints rather than trusted as derived.

## What it touches

**Nothing outside the scratch root it is given.** It never reads or writes
`%LocalAppData%\BrowserAI` or `%LocalAppData%\BrowserAI.app`, and it leaves
`TEMP` alone -- which is the predicate both earlier measurements were taken
under. The product itself additionally redirects `TEMP`/`TMP` into the browsers
root; the 2026-08-19 run established that this produces a byte-identical tree,
so the two predicates agree.

Each run downloads a full browser family, so the pair costs about 670 MB of
transfer and needs roughly 1.6 GB of scratch across four runs.

## What it cannot produce

**Per-phase boundaries.** The earlier entries quoted the installer's own output
timestamped per line. Node buffers stdout when it is not a console, so through a
pipe both lines of a two-line install arrive together at process exit --
measured here at 5 ms apart for a download that took ten seconds. The phases
were dropped from the entry rather than carried forward; getting them back needs
a console or an unbuffered channel.

| Trips `NeverByImageNameTests` | No |
|---|---|
