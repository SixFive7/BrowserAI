<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 -- the payload's own child, asked for its tools under five session configurations

Re-establishes [the kb entry](../../../kb/playwright/tools-and-artifacts.md#the-list-compiled-into-the-binary-is-the-childs-own-bytes----measured-2026-10-08)
and re-verification row 207.

| File | What it does |
|---|---|
| `probe.py` | Starts the assembled payload's `node.exe` and `cli.js` five times, each with a configuration shaped as BrowserAI writes one for a session, asks `initialize` and `tools/list`, and compares the bytes of the `result` member as the child wrote them with `{"tools":`, the snapshot's `tools` value with every whitespace byte outside a string taken out, and `}` |

The five configurations: the snapshot generator's own, every declared capability
and nothing else; a hidden Chromium session; a hidden Firefox session; a headed
Chromium session; and a Chromium session writing a transcript and a network
capture, with a time zone set and HTTPS errors ignored. Each points its profile,
output, downloads, temporary folder and browsers root at a folder of its own under
the repository's `.work`, and no browser starts, because `tools/list` is answered
before any page exists.

**What it printed on 2026-10-08**, against `@playwright/mcp` 0.0.83 and the
snapshot committed with it:

```
expected bytes: 45612
snapshot-config: 45612 bytes, byte-identical=True
chromium-headless: 45612 bytes, byte-identical=True
firefox-headless: 45612 bytes, byte-identical=True
chromium-headed: 45612 bytes, byte-identical=True
chromium-transcript-har: 45612 bytes, byte-identical=True
```

**The positive control**, with `--snapshot` pointed at the 0.0.82 snapshot taken
out of the history (`git show 702c360e:upstream-snapshots/tools-list.json`): all
five came back `byte-identical=False`, the first difference at byte 12,300 of
45,612 against 45,428 expected, inside `browser_find`'s schema. So the comparison
can tell two lists apart, and a difference lands on the tool that carries it.
