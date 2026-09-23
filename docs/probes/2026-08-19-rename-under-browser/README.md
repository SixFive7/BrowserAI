<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-08-19 - what a running browser refuses to let you rename

Re-establishes the two entries under
[The Win32 interop surface](../../../kb/windows/processes.md#the-win32-interop-surface)
and
[The same measurement for Firefox](../../../kb/windows/processes.md#the-same-measurement-for-firefox-and-for-what-both-families-share----2026-08-19),
which are what `browserai_reinstall_browser`'s refusal rests on.

WARNING - **these rename the shared provisioned browsers root that every
browser-touching test on this machine reads.** Each restores what it renamed in
a `finally` and re-asserts the executables are present at the end; one that dies
half-way breaks the suite instead of failing its own assertion. That is why
none of it is automated - see re-verification row 103.
