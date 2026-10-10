<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-10 -- the update toasts and the broken install's toast on the maintainer's screen

What the kb entry
[A toast breaks a line after a slash or a hyphen too](../../../kb/windows/notifications.md#a-toast-breaks-a-line-after-a-slash-or-a-hyphen-too----read-2026-10-10)
was read from, and what `Harness/BannerText`'s lines of 2026-10-10 are: its
`ShownWhole`, `DidNotFit` and `WrappedExactly`, which
`UpdateToastContentTests.NoCommandVersionOrQuotedNameIsSplitAcrossTwoLines` and
`.EveryToastsTextShowsWholeInItsBanner` hold the measurement to before they measure
anything else. The three defects the crops show, a line end inside *"/mcp"*, inside an
older version's title at the version's hyphen and inside a quoted title, are recorded
in [DECISIONS](../../../DECISIONS.md#the-answers-of-the-afternoon-of-2026-10-10).

Taken on the maintainer's screen while he watched, at his answer of that day, verbatim:
*"28 b - do this now now."*, from 12:37:44Z to 12:38:47Z: seven toasts, each shown about 8 s under Windows PowerShell's application
id with the one before removed first, and a full-size crop of each banner saved 1.5 s
after its show. Windows 11 Pro 10.0.26300.9550 at 3840x2160, Windows PowerShell 5.1,
the same machine and banner as
[`2026-10-08-toast-popup`](../2026-10-08-toast-popup/README.md), whose monitor
(`Rig.cs`) and event hand (`evhand`) the rig here uses unchanged. The toasts were
composed by the product's own code at `db50964e`, through `rig/composer`, which types
no string of a toast, only the inputs.

| Path | What it is |
|---|---|
| `toasts.json` | The seven toasts as the composer wrote them: each one's file name, tag, group, XML, the bound fields' first values where it has them, and a note naming the case |
| `status.txt` | The rig's own log of the run: each show, the banner's rows and its place on screen, each crop saved, each removal, and the clean-up at the end with nothing of the rig's left in the history |
| `orchestrator.txt` | The rig's pid and start |
| `rig/now.ps1.txt` | The rig, run with Windows PowerShell 5.1: `powershell -NoProfile -ExecutionPolicy Bypass -File now.ps1` beside `Rig.cs` and `evhand` of the 2026-10-08 batch |
| `rig/composer/*.txt` | The composer: a .NET 10 program that references `src/BrowserAI.Core` at `db50964e` and writes `toasts.json` |
| `left-out.sha256` | The seven crops, left out, with their SHA-256 |
| `originals.sha256` | The SHA-256 of each rig file as it was taken, under its own name, before `.txt` was added |

## What the crops show, line by line

| Toast | Line on screen |
|---|---|
| ready, counted, third line | *After the update: 99 Claude Code terminals need /* |
| | *mcp, BrowserAI, Reconnect; 99 Codex conversations* |
| | *need a new one; 99 clients may need a reconnect.* |
| ready, the longest names, third line | *After the update: 1 Codex conversation needs a new* |
| | *one; new conversation in RegisterAI, "Summarise the* |
| | *open issues" and 97 more may need a reconnect.* |
| ready, an older version, title | *BrowserAI 1.1.1-alpha.0.323, older than 1.1.1-* |
| | *alpha.0.325, is ready to install* |
| failed, third line | *Velopack's log is in %LocalAppData%\velopack, and* |
| | *BrowserAI's in %LocalAppData%\BrowserAI\logs.* |
| broken install, second line | *Part of this install does not match the rest, so no* |
| | *browser session can open. Download BrowserAI.exe* |
| | *from the latest release and run it; your sessions and* |
| | *their files are kept.* |

Every other line the seven toasts carried showed on one line of its own, as
`toasts.json` gives it, and nothing was cut: the holders line *99 agents, 99 hidden
browsers, 99 windows*, each title but the older one, and the second lines.

## How these bytes depart from the ones taken

- **No picture of the screen is here**, as in the 2026-10-08 batch: the crops are of
  the corner of his screen where banners appear, and each is named in `left-out.sha256`
  with its SHA-256. The lines above are read off them.
- **The rig's files are stored as text** under a `.txt` name, so that nothing in the
  suite that reads the tree's code reads them as this repository's own. Their text is
  unchanged, line endings aside; `originals.sha256` gives the SHA-256 of each as it was
  taken, under its own name.
- Everything else is as written, line endings aside, which this repository stores as
  LF, as [the index](../README.md) says of every batch.
