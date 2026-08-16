// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<BrowserAI.Tests.SuiteParallelism>]

namespace BrowserAI.Tests;

/// <summary>
/// Caps how many tests this suite runs at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a timing budget being loosened. It stops the rig creating a
/// load it then fails to survive.</b> Twenty of this suite's test files start
/// real processes — published binaries, <c>node.exe</c>, Chromium, Firefox —
/// and TUnit's default parallelism is derived from the processor count. On the
/// maintainer's machine that is <b>32</b>, so the suite was launching browsers
/// dozens-wide and then asserting that things happened promptly.
/// </para>
/// <para>
/// <b>Measured 2026-08-16, on a freshly rebooted machine.</b> Unconstrained:
/// six runs at 18.6–21.6 s with <b>one run of 56.5 s carrying 20 failures</b>,
/// every one of them a timing budget on an unrelated test. That shape had been
/// seen by four separate build steps and blamed on Defender, which was never
/// verified and turns out not to be needed as an explanation.
/// </para>
/// <para>
/// With the limit applied: <b>twenty-four consecutive clean runs</b> and the
/// variance collapses. Against a 1-in-7 failure rate that is a 2.5% chance of
/// happening by luck.
/// </para>
/// <para>
/// <b>The cost, measured both ways, because the two disagree and the larger
/// number is the true one.</b> Passing <c>--maximum-parallel-tests 4</c> on the
/// command line: 24.5–29.8 s over fourteen runs. This attribute, with no flag:
/// <b>32.7–36.2 s</b> over ten. So the honest figure is roughly <b>+14 s</b> on
/// a ~19 s baseline, not the ~6 s the flag suggested. Why the two paths differ
/// is <b>not established</b> — plausibly a per-test lock against a scheduler
/// cap — and it is recorded rather than guessed at. The attribute is kept
/// regardless, because a limit that only exists when someone remembers a flag
/// is not a limit.
/// </para>
/// <para>
/// The alternative — a suite that is red one run in seven — cannot pass a
/// release gate whose rule is <i>no release with a red test</i>, and would have
/// been "fixed" by raising every budget it broke, which is the thing this
/// repository forbids.
/// </para>
/// <para>
/// <b>Why a whole-assembly cap rather than a constraint on the heavy tests.</b>
/// A per-class limiter would let the hundreds of in-process tests keep the
/// wider parallelism, and it is the better shape. It is not built because it
/// needs an annotation on twenty files and the failure is not confined to the
/// tests that cause it — a browser launch starves an assertion that only
/// formats strings, which is exactly how this presented. If the fourteen
/// seconds ever matter, the surgical version is the fix, and the numbers above
/// are the baseline to beat.
/// </para>
/// </remarks>
internal sealed class SuiteParallelism : IParallelLimit
{
    /// <summary>
    /// Four. Chosen because it was measured, not because it is a round number:
    /// it is what fourteen clean runs were established at. It is deliberately
    /// unrelated to the processor count — the resource being contended is not
    /// CPU, it is the machine's capacity to start browsers.
    /// </summary>
    public int Limit => 4;
}
