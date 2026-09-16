<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-08-26 - the post-course-correction review's evidence

Every transcript
[`docs/reviews/2026-08-26-post-course-correction.md`](../../reviews/2026-08-26-post-course-correction.md)
cites, measured through the published binary: `drive*-out.txt` are the driver
transcripts, `deadshare.txt` is the share-mode probe, `dupkey.cs` is the .NET 10
duplicate-key repro, and `lead7/` is the console-and-report set for lead 7.

WARNING - **`drive5-out.txt` is not byte-identical to what was captured.** It is
the run that plants a `U+0007` in a directory name to prove the refusal, so the
file as taken contained two raw `0x07` bytes - which
`HouseRuleTests.NoTextFileInTheTreeCarriesAControlByte` forbids anywhere in this
repository. Both were rewritten as the six characters `\u0007`, which is how the
same line already spells it in its quoted half. Nothing else in any file here
was changed.
