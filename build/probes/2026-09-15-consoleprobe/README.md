<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-15 - a parked read on stdin is woken by nothing

Re-establishes
[A read parked on standard input is woken by neither cancelling it nor disposing the stream](../../../kb/windows/processes.md#a-read-parked-on-standard-input-is-woken-by-neither-cancelling-it-nor-disposing-the-stream--measured-2026-09-15),
which `DirectStdioServerTransportTests` cites as the reason the server cannot
simply cancel its way off stdin. Evidence:
[`docs/evidence/2026-09-15-fix/`](../../../docs/evidence/2026-09-15-fix/README.md).

Build it **outside this repository** - or put three empty `Project` stubs named
`Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props`
beside it, which is what the original had, so that this repository's own central
package management does not reach the probe.
