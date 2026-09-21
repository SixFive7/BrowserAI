<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-21 - first-run provisioning at chromium 1246 and firefox 1549

**The re-establishment of [re-verification row 21](../../../kb/re-verification.md)
on the `@playwright/mcp` 0.0.81 -> 0.0.82 roll**, taken in the same batch as the
roll rather than left owed, because the product quotes these figures:
`BrowserProvisioner.FirstRunDownloadBytes` is what every caller refused during
provisioning reads. The rig is
[`docs/probes/2026-09-16-provisioning`](../../probes/2026-09-16-provisioning/README.md);
the figures it produced are in
[kb: first-run provisioning](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)
and
[Firefox, measured the same way](../../../kb/playwright/provisioning-and-timings.md#firefox-measured-the-same-way--2026-08-19).

## What is here

| File | What it is |
|---|---|
| `chromium-1.json`, `chromium-2.json` | Two clean Chromium provisions into an empty root, each the probe's own output: exit code, stopwatch seconds, total bytes and files, and the per-component breakdown |
| `firefox-1.json`, `firefox-2.json` | The same two runs for Firefox |
| `download-urls.json` | What `Get-DownloadUrls.ps1` derived from the rebuilt payload's **own** `browsers.json`, so that no revision in the wire figures was typed |
| `wire-content-length.txt` | The `HEAD` on each of those four URLs, and the two family totals summed from them. This is where `207,274,189` and `130,934,199` come from |

**Nothing is cut and nothing is trimmed.** The six files are whole and are
exactly what the rig wrote, with this repository's LF normalisation and nothing
else, so there is no digest of an original to record.

⚠️ **What is deliberately NOT here: the installer's own output.** The rig
captures it and it is what confirms the derived URLs against the string upstream
actually prints, but upstream colours that line with ANSI SGR escapes - `0x1B`
bytes - and `HouseRuleTests.NoTextFileInTheTreeCarriesAControlByte` refuses a C0
control byte anywhere in this repository's text. **It is dropped rather than
stripped**, because a doctored capture is worth less than a quoted line: what it
said, with the escapes removed by hand for reading only, is *"Downloading Chrome
for Testing 154.0.8037.0 (playwright chromium v1246) from
https://cdn.playwright.dev/builds/cft/154.0.8037.0/win64/chrome-win64.zip"* and
*"Downloading Firefox 156.0 (playwright firefox v1549) from
https://cdn.playwright.dev/dbazure/download/playwright/builds/firefox/1549/firefox-win64.zip"*.
Re-running the rig produces it again; the file it writes is `<OutJson>.log`.

## What the run found

**Chromium 1245 to 1246 produced no difference at all, for the third roll
running.** `playwright-core` builds Chromium's URL with `cftUrl()`, keyed on
`browserVersion` rather than on the revision, and 1246 carries the same
`154.0.8037.0` as 1245 and 1244 - so the archive fetched is the same archive,
`chromium-1246` holds **454,699,952 B across 308 files**, and the wire total is
**207,274,189 B**, both exactly what 1245 and 1244 held. The installer confirmed
that URL as a string rather than as something derived, in the line quoted above.

**Firefox 1548 to 1549 is the first roll in this table that is a new browser
rather than a rebuild**: `browserVersion` 155.0 -> **156.0**, `+1,431,551` B on
the wire and `+3,458,122` B on disk, with the file count moving **61 -> 63**
inside the Firefox tree for the first time since 1539. The last two Firefox rolls
moved it by 327 and 902 bytes respectively, so the size of this one is the
finding rather than the direction. `ffmpeg` 1011 and `winldd` 1007 are
byte-identical, which is the control for the revision that did move.

**Both pairs came back identical to each other**, which is the property the
procedure asks for before a figure is recorded.

## What it touched

**The scratch root it was given and nothing else.** Each run emptied
`.work\row21-root-<family>-<n>`, provisioned into it, and the root was removed
afterwards. Nothing read or wrote `%LocalAppData%\BrowserAI` or
`%LocalAppData%\BrowserAI.app`, and `TEMP` was left alone.

## What is not here

**No per-phase breakdown.** Node buffers stdout through a pipe, so both lines of
a two-line install arrive together; the rig's README records that and the entry
does not claim phases.

**No third or fourth run.** Two per family is what the procedure asks for and
what was taken. The stopwatch seconds - Chromium 11.28 and 11.31, Firefox 7.25
and 6.80 - are `[MACHINE]` figures and the spread across sessions is larger than
the byte difference, which the kb entry says rather than leaves to be noticed.
