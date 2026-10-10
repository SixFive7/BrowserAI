// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The start Velopack makes after an apply: <c>--after-update &lt;version&gt;</c>
/// tells an install from a failure by comparing that version with its own, raises
/// the matching toast, and asks for the background either way.
/// </summary>
/// <remarks>
/// <b>Why a comparison and nothing else</b>: Velopack 1.2.161 starts the OLD
/// version with the SAME arguments after a failed apply, measured 2026-10-08, 3 runs
/// of 3 (step 0 of the one-binary build, <c>.work/step0-velopack/FINDINGS.md</c>),
/// so the version the background put into the arguments is the only thing that
/// differs between the two outcomes.
/// </remarks>
internal sealed class AfterUpdateTests
{
    /// <summary>
    /// The same version is an install and another is a failure, each raises its own
    /// toast, and the background is asked for in both outcomes and when the argument
    /// is not a version.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSameVersionIsAnInstallAnotherIsAFailureAndTheBackgroundIsAskedForEitherWay()
    {
        var events = new ConcurrentQueue<string>();
        var toasts = new RecordedToasts(events);
        using var log = new CapturingLoggerProvider();
        var logger = log.CreateLogger("BrowserAI.Updates");
        var asked = 0;
        const string VelopackLog = @"C:\Users\someone\AppData\Local\velopack\velopack_BrowserAI.app.log";

        var installed = AfterUpdate.Report("1.2.0", "1.2.0", VelopackLog, toasts, () => asked++, logger);

        await Assert.That(installed).IsEqualTo(AfterUpdateOutcome.Installed);
        await Assert.That(string.Join('|', events)).IsEqualTo("toast installed 1.2.0");
        await Assert.That(asked).IsEqualTo(1);

        var failed = AfterUpdate.Report("1.2.0", "1.1.0", VelopackLog, toasts, () => asked++, logger);

        await Assert.That(failed).IsEqualTo(AfterUpdateOutcome.Failed);
        await Assert.That(string.Join('|', events)).IsEqualTo("toast installed 1.2.0|toast failed 1.2.0");
        await Assert.That(asked).IsEqualTo(2).Because("every BrowserAI process is gone after a failed apply too");
        await Assert.That(log.Records.Any(record => record.EventId.Id is 69 && record.Message.Contains(VelopackLog, StringComparison.Ordinal)))
            .IsTrue()
            .Because("the failure's log line names Velopack's own log");

        var garbled = AfterUpdate.Report("not a version", "1.2.0", VelopackLog, toasts, () => asked++, logger);

        await Assert.That(garbled).IsEqualTo(AfterUpdateOutcome.NotAVersion);
        await Assert.That(events.Count).IsEqualTo(2).Because("nothing true can be said in a toast");
        await Assert.That(asked).IsEqualTo(3);
    }

    /// <summary>
    /// A restart Velopack made with no version of ours in its arguments is this build's
    /// install, so it raises the installed toast and asks for the background, and is
    /// never a person's start, which opens a tab.
    /// </summary>
    /// <remarks>
    /// <b>The maintainer's 20, 2026-10-10, in his words verbatim: <i>"20 nothing except for
    /// the toast"</i></b>: after an update, the installed toast and no dashboard tab. An
    /// apply another build made, such as 1.1.0 applying this one, restarts the new version
    /// with no <c>--after-update</c>, and until that day the start went on as a person's
    /// start, which opens a tab. <b>Planted red 2026-10-10</b> against the argument alone,
    /// which read that restart as no after-update start at all.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARestartWithNoVersionOfOursIsThisBuildsInstallAndNoPersonsStart()
    {
        await Assert.That(AfterUpdate.TargetOf([], restarted: true, "1.2.0")).IsEqualTo("1.2.0");
        await Assert.That(AfterUpdate.Judge(AfterUpdate.TargetOf([], restarted: true, "1.2.0")!, "1.2.0")).IsEqualTo(AfterUpdateOutcome.Installed);

        // The background's own restart names the version it installed, which decides.
        await Assert.That(AfterUpdate.TargetOf(["--after-update", "1.3.0"], restarted: true, "1.2.0")).IsEqualTo("1.3.0");

        // And a start that Velopack's restart did not make is no after-update start.
        await Assert.That(AfterUpdate.TargetOf([], restarted: false, "1.2.0")).IsNull();
        await Assert.That(AfterUpdate.TargetOf(["--sessions"], restarted: false, "1.2.0")).IsNull();
    }

    /// <summary>
    /// Versions are compared the way Velopack compares them: pre-release labels
    /// without regard to case, and a pre-release is not its release.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task VersionsAreComparedTheWayVelopackComparesThem()
    {
        await Assert.That(AfterUpdate.Judge("1.2.0-Alpha.0.5", "1.2.0-alpha.0.5")).IsEqualTo(AfterUpdateOutcome.Installed);
        await Assert.That(AfterUpdate.Judge("1.2.0", "1.2.0.0")).IsEqualTo(AfterUpdateOutcome.Installed);
        await Assert.That(AfterUpdate.Judge("1.2.0-alpha.0.5", "1.2.0")).IsEqualTo(AfterUpdateOutcome.Failed);
        await Assert.That(AfterUpdate.Judge("1.2.0", "1.2.1")).IsEqualTo(AfterUpdateOutcome.Failed);
        await Assert.That(AfterUpdate.Judge("1.2.0", "")).IsEqualTo(AfterUpdateOutcome.NotAVersion);
    }

    /// <summary>
    /// The restart arguments carry the version, the start reads it back, and the
    /// failed toast's log is Velopack's per-pack log.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRestartArgumentsCarryTheVersionAndTheStartReadsItBack()
    {
        var arguments = AfterUpdate.RestartArguments("1.2.0");

        await Assert.That(string.Join(' ', arguments)).IsEqualTo("--after-update 1.2.0");
        await Assert.That(AfterUpdate.TargetIn(arguments)).IsEqualTo("1.2.0");
        await Assert.That(AfterUpdate.TargetIn(["--background"])).IsNull();
        await Assert.That(AfterUpdate.TargetIn([AfterUpdate.Argument])).IsNull();

        await Assert.That(AfterUpdate.VelopackLogPath(@"C:\Users\someone\AppData\Local", "BrowserAI.app"))
            .IsEqualTo(@"C:\Users\someone\AppData\Local\velopack\velopack_BrowserAI.app.log");
    }
}
