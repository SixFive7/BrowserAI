<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-17 - which artifact pointers `filePaths: "absolute"` reaches

Establishes
[Every artifact pointer a tool result carries is absolute, and the option that did it](../../../kb/playwright/tools-and-artifacts.md#every-artifact-pointer-a-tool-result-carries-is-absolute--measured-2026-09-17),
which is the measurement that closed
[upstream ask #1](../../../TODO.md#upstream-asks) - `--file-paths=absolute`,
[microsoft/playwright#42673](https://github.com/microsoft/playwright/pull/42673),
merged 2026-09-16 against this project's own
[#42497](https://github.com/microsoft/playwright/issues/42497). Evidence:
[`docs/evidence/2026-09-17-file-paths/`](../../evidence/2026-09-17-file-paths/README.md).

## Two rigs, and both are needed

| File | What it establishes |
|---|---|
| `probe.mjs` | What UPSTREAM does. Drives the payload's own `cli.js` over stdio with a generated config, once per value of `filePaths`, and prints every tool result verbatim. **Run it twice and diff**: a shape that reads the same in both is a shape the option does not reach, and a shape already absolute under `relative` was never one of the six |
| `through-browserai.mjs` | What a CALLER sees. The same drive through the published `BrowserAI.Server.exe`, so the pointers are the ones BrowserAI's generated config actually produces. A pointer absolute in the first and relative here would mean the generator wrote a key the product then lost |

Neither decides anything. The classification is in the kb entry, taken from what
these printed.

## Running them

```
node docs/probes/2026-09-17-file-paths/probe.mjs payload .work/pointer-probe absolute
node docs/probes/2026-09-17-file-paths/probe.mjs payload .work/pointer-probe relative
node docs/probes/2026-09-17-file-paths/through-browserai.mjs \
  src/BrowserAI/bin/Release/net10.0-windows/win-x64/publish/BrowserAI.Server.exe \
  .work/e2e-session
```

Run them under the payload's own `node.exe`, which is what the product runs.

⚠️ **`through-browserai.mjs` creates a real session in the app root's index and
destroys it at the end.** Point its directory inside `.work/` and nowhere else.
It does not set `BROWSERAI_ROOT`, because
[setting it does not isolate a run](../../../CLAUDE.md) and would provoke a
provisioning download; it shares the one app root, so do not run it beside a
suite run.

## Three things the rigs have to do, and why

**A loopback HTTP server rather than a file on disk or a `data:` URL.** The child
blocks the `file:` protocol unless `allowUnrestrictedFileAccess` is on, which
BrowserAI writes `false`; and a `data:` URL produces no network request at all,
so the **binary response body** shape would be unmeasurable against one. The
server answers a 1x1 PNG so that `browser_network_request --part response-body`
has to write a file rather than inline the bytes.

**The index is read off upstream's own line, not counted.** The
`browser_network_requests` block opens with a `### Result` heading, so counting
lines makes every index one too high and the follow-up call comes back
*Request #N not found* - which reads like an absent request rather than like a
bug in the rig. It cost one run here.

**`capabilities` is BrowserAI's own granted set**, read off
`BrowserConfiguration.GrantedCapabilities` rather than invented. `core*` is
unconditional and naming one does nothing; naming a capability that does not
exist silently yields a smaller surface, and the tools you wanted come back
*not found*.

## What is not here

**The paused-debugger location.** It is the fourth `_printablePath` call site and
so is covered by the same helper, and the pull request's own body named it - but
no run here drove a paused session, so the kb entry records it as a reading of
the bundle rather than as a measurement. Driving it needs a `page.pause()` and a
resume, which is a different rig.
