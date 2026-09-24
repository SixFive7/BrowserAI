// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Prints what the run exercised, once, after everything has run.
/// </summary>
/// <remarks>
/// <b>The run summary's four numbers cannot answer this and never could.</b>
/// Measured 2026-08-16 at <c>c21fea7</c>: with the whole publish directory moved
/// aside the suite reported <c>total: 329 · failed: 0 · succeeded: 328 ·
/// skipped: 1 · exit 0</c>, character for character what a run that launched a
/// real Chromium reported. The only difference was the duration, and
/// [release checklist item 8](../../RELEASING.md) already records that a slice
/// test's duration proves nothing. So the run says it in words.
/// </remarks>
internal static class SuiteCoverage
{
    /// <summary>
    /// Where this process writes what it read, when it is the filtered child.
    /// </summary>
    /// <remarks>
    /// Empty in every ordinary run: <see cref="SuiteFilter.ProbeVariable"/> is
    /// set by one launcher and nothing else, which is what makes it both the
    /// child's mailbox and the recursion guard. <b>Declared above
    /// <see cref="ReportPath"/> because a static initialiser runs in textual
    /// order</b>, and the one below reads this one.
    /// </remarks>
    public static string? ProbeReportFile { get; } = Environment.GetEnvironmentVariable(SuiteFilter.ProbeVariable);

    /// <summary>The block's own copy on disk, for whoever assembles the evidence.</summary>
    /// <remarks>
    /// ⚠️ <b>Repository-rooted for an ordinary run and beside the probe's own
    /// report for the filtered child, and the split is not tidiness.</b> Until
    /// 2026-08-24 this was repository-rooted unconditionally, so the child
    /// <see cref="SuiteCoverageTests.AFilteredChildRunReadsAsFilteredAndIsRefusedAsARelease"/>
    /// starts wrote its own one-test <c>FILTERED</c> block <i>over the parent's
    /// file while the parent was still running</i>. The parent rewrote it at its
    /// own session end, so the copy a gate log appends was never wrong -- but
    /// anyone reading the file during that window got the child's, and a block
    /// whose whole job is to say what a run covered must not be readable as a
    /// statement about a different run.
    /// </remarks>
    public static string ReportPath { get; } = ProbeReportFile is { Length: > 0 } probe
        ? Path.Combine(Path.GetDirectoryName(probe)!, "suite-coverage.txt")
        : Path.Combine(RepositoryLayout.Root.FullName, ".work", "suite-coverage.txt");

    /// <summary>
    /// Takes the filter reading before anything else runs.
    /// </summary>
    /// <remarks>
    /// <b>Here and not lazily on first use, and the timing is the honesty.</b>
    /// <c>TUnitTestFramework.ExecuteRequestAsync</c> assigns
    /// <c>GlobalContext.Current</c> and <c>TestSessionContext.Current</c> before
    /// it runs a single hook, so a reading taken here is taken after the only
    /// event that could populate them -- and a null filter read before that point
    /// is indistinguishable from a run that really had none, which is the false
    /// green <see cref="SuiteFilter"/> exists to prevent.
    /// </remarks>
    [Before(TestSession)]
    public static void ReadWhetherThisRunWasFiltered() => SuiteFilter.Take();

    /// <summary>
    /// Takes the machine's commit charge before the first test runs.
    /// </summary>
    /// <remarks>
    /// <b>Its own hook and not a second statement in the one above</b>, so
    /// that each says what it does: the filter reading has a timing argument
    /// behind its placement and this one has only <i>before anything has
    /// allocated</i>. A reading taken lazily on first use would be a reading of
    /// the suite partway through itself, which is the one thing the pair of
    /// readings exists to be able to distinguish. See <see cref="CommitCharge"/>.
    /// </remarks>
    [Before(TestSession)]
    public static void TakeTheCommitChargeReading() => CommitCharge.TakeTheStartReading();

    /// <summary>
    /// Takes the window baseline and starts the watch before the first test runs.
    /// </summary>
    /// <remarks>
    /// <b>A session hook, so no filter can deselect it</b>, for the reason
    /// <see cref="RefuseWhatThisRunsOwnReadingsForbid"/> gives about the refusal
    /// at the other end. The maintainer's rule, 2026-09-24: <i>"make sure this
    /// focus stealing is not something that ends up in the testbed."</i> See
    /// <see cref="WindowWatch"/>.
    /// </remarks>
    [Before(TestSession)]
    public static void WatchForWindows() => WindowWatch.Start();

    /// <summary>Writes the coverage block at the end of the session.</summary>
    [After(TestSession)]
    public static void ReportWhatThisRunExercised()
    {
        // FIRST, so the windows row below is the whole run's: the hook thread is
        // stopped after the events queued ahead of the stop are delivered, and the
        // closing sweep counts anything of the suite's still open.
        WindowWatch.Stop();

        var summary = SuiteEnvironment.Summary();

        // ⚠️ Console.WriteLine DOES NOT REACH THE RUN'S OUTPUT FROM A SESSION
        // HOOK, and a block nobody sees is not a mechanism. Measured 2026-08-16
        // on TUnit 1.65.0 / MTP: the hook runs, the file below is written, and
        // the Console.WriteLine copy appears nowhere in a fully redirected log
        // -- the platform replaces Console.Out for the session and attributes
        // captured text to tests, of which a session hook is none. So this
        // writes through the REAL standard output handle, which nothing has
        // replaced, and leaves it open for the run summary that follows.
        //
        // ⚠️ AND THAT REACHES A DIRECT RUN OF THE TEST EXECUTABLE AND NOT A
        // `dotnet test` ONE, measured 2026-08-24 on this tree: neither the real
        // stdout handle nor the real stderr handle appears in a fully redirected
        // `dotnet test` log, because the MTP integration talks to the test app
        // over its own channel instead of forwarding its console. The gate runs
        // `dotnet test`, so ReportPath below is the copy a gate run actually
        // has, and TESTING.md's two invocations append that file to each run's
        // own log for exactly this reason.
        using (var standardOutput = Console.OpenStandardOutput())
        using (var writer = new StreamWriter(standardOutput, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true })
        {
            writer.WriteLine(summary);
        }

        TestSessionContext.Current?.OutputWriter.WriteLine(summary);

        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
            File.WriteAllText(ReportPath, summary + Environment.NewLine);
        }
        catch (IOException)
        {
            // The console copies are the contract; the file is a convenience and
            // must never turn a green run red.
        }
        catch (UnauthorizedAccessException)
        {
        }

        // The child writes what it read BEFORE the refusal below throws, because
        // a process that failed the way it was meant to still has to be
        // readable. It lives here and not in a test for the reason the
        // refusal does: a filter that did not select that test took the report
        // with it.
        if (ProbeReportFile is { Length: > 0 } probe)
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
            File.WriteAllText(probe, SuiteFilter.Describe(SuiteFilter.Reading, SuiteEnvironment.IsReleaseRun));
        }

        RefuseWhatThisRunsOwnReadingsForbid();
    }

    /// <summary>
    /// Fails the whole run when its own filter reading says it may not be a
    /// release, or its own window watch saw it show a window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Two refusals since 2026-09-24, and one exception carrying both.</b>
    /// <i>Previously <c>RefuseARunThatMayNotBeARelease</c>, which carried the filter
    /// refusal alone.</i> The window refusal is Q278: a run whose own processes put
    /// a window on the screen or took the foreground fails in every mode, and since
    /// Q290 a so does a run the watch could not cover (<i>previously "fails only a
    /// release"</i>). Both are raised here for the
    /// same reason -- a refusal a filter can deselect is not a refusal -- and both
    /// sentences travel in one exception, so a run that earned both is told both.
    /// </para>
    /// <para>
    /// ⚠️ <b>A refusal that a filter can remove is not a refusal, and this used
    /// to be one.</b> Until 2026-08-24 the refusal was an ordinary <c>[Test]</c>,
    /// so <c>BROWSERAI_RELEASE_RUN=1</c> plus a <c>--treenode-filter</c> that did
    /// not happen to select that one method was a filtered run, a claimed
    /// release, and <b>green</b> -- the guard failing in exactly the class of run
    /// it exists to guard. It is a session hook now, and
    /// <see cref="SuiteCoverageTests.AFilteredChildRunReadsAsFilteredAndIsRefusedAsARelease"/>
    /// proves the difference by filtering the child down to a method that is
    /// <i>not</i> this one.
    /// </para>
    /// <para>
    /// <b>What TUnit actually guarantees, measured 2026-08-24 at TUnit 1.65.0 /
    /// Microsoft.Testing.Platform 2.3.3 on this tree and not assumed.</b> A
    /// <c>[Before(TestSession)]</c>/<c>[After(TestSession)]</c> hook is
    /// registered against the session and not against a test node, so no
    /// <c>--treenode-filter</c> and no uid-list selection can deselect it; an
    /// exception thrown from one is reported as a session-level failure and the
    /// host exits non-zero. The re-establishment is the child control itself:
    /// filter the host to one unrelated method with the variable set and read
    /// the exit code.
    /// </para>
    /// <para>
    /// <b>After the block is written, never before.</b> The coverage block and
    /// the probe report are the evidence a refused run is read from, and a
    /// refusal that threw first would take them with it.
    /// </para>
    /// <para>
    /// <b>The one thing it still cannot cover is a run that never starts a
    /// session</b> -- a filter naming no assembly at all, or a host that fails
    /// before the framework's hooks are registered. That run reports nothing and
    /// is not a release either, but nothing here fails it, and
    /// [`TESTING.md`](../../TESTING.md) states the guarantee with that limit
    /// and not unqualified.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The run's own filter reading or its own window watch forbids it.
    /// </exception>
    private static void RefuseWhatThisRunsOwnReadingsForbid()
    {
        var refusals = new List<string>();

        if (SuiteFilter.Decision is SuiteFilterDecision.Refuse)
        {
            refusals.Add(SuiteFilter.Refusal(SuiteFilter.Reading, SuiteEnvironment.IsReleaseRun));
        }

        if (WindowWatch.Refusal(WindowWatch.Reading, SuiteEnvironment.IsReleaseRun) is { } windows)
        {
            refusals.Add(windows);
        }

        if (refusals.Count is not 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine + Environment.NewLine, refusals));
        }
    }
}

/// <summary>
/// The gate that makes a degraded run distinguishable from a real one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every assertion here is about the mechanism, not about the machine.</b>
/// The suite must stay runnable on a clean clone, so nothing below requires a
/// browser -- what is required is that a run without one cannot report the same
/// summary as a run with one.
/// </para>
/// <para>
/// <b>The release branch is exercised in every ordinary run.</b>
/// <see cref="SuiteEnvironment.Decide"/> is a pure function of the two inputs
/// precisely so that <c>BROWSERAI_RELEASE_RUN</c> is not a code path that only
/// runs on release day -- a mechanism nobody exercises until it matters is the
/// same defect as the one this file closes.
/// </para>
/// </remarks>
internal sealed partial class SuiteCoverageTests
{
    /// <summary>
    /// A release run refuses what an ordinary run skips, and a partial
    /// installation fails either way.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReleaseRunFailsWhereAnOrdinaryRunSkips()
    {
        await Assert.That(SuiteEnvironment.Decide(CapabilityState.Present, isReleaseRun: false)).IsEqualTo(CapabilityVerdict.Proceed);
        await Assert.That(SuiteEnvironment.Decide(CapabilityState.Present, isReleaseRun: true)).IsEqualTo(CapabilityVerdict.Proceed);

        await Assert.That(SuiteEnvironment.Decide(CapabilityState.AbsentAsAWhole, isReleaseRun: false)).IsEqualTo(CapabilityVerdict.Skip);
        await Assert.That(SuiteEnvironment.Decide(CapabilityState.AbsentAsAWhole, isReleaseRun: true)).IsEqualTo(CapabilityVerdict.Fail);

        // A publish directory with no binary in it, or a payload directory with
        // no payload.json, is a broken build. It was never a clean clone and it
        // must not be treated as one, in either mode.
        await Assert.That(SuiteEnvironment.Decide(CapabilityState.Partial, isReleaseRun: false)).IsEqualTo(CapabilityVerdict.Fail);
        await Assert.That(SuiteEnvironment.Decide(CapabilityState.Partial, isReleaseRun: true)).IsEqualTo(CapabilityVerdict.Fail);
    }

    /// <summary>
    /// The summary names every capability, its state, and whether this is a
    /// release run.
    /// </summary>
    /// <remarks>
    /// The block is the only thing in a run's output that distinguishes a
    /// degraded run from a real one before the skipped count moves, so its
    /// contents are asserted, not trusted.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCoverageBlockStatesWhatWasExercised()
    {
        var summary = SuiteEnvironment.Summary();

        await Assert.That(summary).Contains("published slice");
        await Assert.That(summary).Contains("repository payload");
        await Assert.That(summary).Contains("Chromium");
        await Assert.That(summary).Contains("Firefox");
        await Assert.That(summary).Contains("packed release");
        await Assert.That(summary).Contains("client CLI");
        await Assert.That(summary).Contains(SuiteEnvironment.ReleaseRunVariable);

        // And what this run's environment said it would lack, which is the only
        // thing in the block that tells four expected absences from four
        // absences of which one was not expected.
        await Assert.That(summary).Contains(SuiteEnvironment.ExpectedAbsentVariable);

        // Not a capability -- Chromium reads PRESENT whether this run downloaded
        // it or read it out of .work\ -- and that is exactly why the block has to
        // say which. FirstRunCacheTests asserts what the row may contain.
        await Assert.That(summary).Contains("first-run bytes");

        // Not a capability either, and it is the SECOND question about the
        // artefact the first row reports. `published slice PRESENT` is a claim
        // about existence; until 2026-08-30 nothing in a green log said whether
        // that binary belonged to the tree the run was reading, and the nearest
        // sentence to hand turned out to be a commit date.
        await Assert.That(summary).Contains(PublishedSlice.FreshnessTitle);
        await Assert.That(summary).Contains(PublishedSlice.StateWord(PublishedSlice.Verdict).Trim());

        // Not a capability either, and the only row here that reports what the
        // SHELL handed this run and not what the machine holds.
        await Assert.That(summary).Contains("drive letter");
        await Assert.That(summary).Contains(GateDriveCase.Variable);

        // Not a capability either, and it is the premise the four numbers in the
        // run summary rest on: a filtered run prints the same shape of block a
        // full one does. Read from the platform's own filter -- see SuiteFilter,
        // which also records why ICommandLineOptions could not be used.
        await Assert.That(summary).Contains(SuiteFilter.Title);
        await Assert.That(summary).Contains(SuiteFilter.StateWord(SuiteFilter.Verdict));

        // Not a capability either, and for a stronger reason: nothing this run
        // may do would turn it green. It says whether Windows would have let a
        // browser take the foreground at all, which decides whether a green run
        // is evidence about focus or is evidence about the lock.
        // ForegroundLockTests asserts what the row may contain.
        await Assert.That(summary).Contains(ForegroundLock.Title);

        // Not a capability either, for the same reason, and it is the reading a
        // closed hazard row names as the thing that separates its own cause
        // from a live one. Until 2026-08-30 no run took it at all, so that row's
        // question could not be asked of any gate this project has ever run.
        await Assert.That(summary).Contains(CommitCharge.Title);
        await Assert.That(summary)
            .Contains(CommitCharge.StateWord(CommitCharge.Classify(CommitCharge.AtStart, CommitChargeReading.Take())).Trim())
            .Because("the row states the band this machine is actually in, and a block that printed a number without classifying it would be an assurance the run has not earned");

        // Not a capability either, and the only row that fails the run by itself:
        // whether this run's own processes put a window on the screen or took the
        // foreground. WindowWatchTests asserts what the row may contain.
        await Assert.That(summary).Contains("  " + WindowWatch.Title.PadRight(20));
        await Assert.That(summary).Contains(WindowWatch.StateWord(WindowWatch.Judge(WindowWatch.Reading)).Trim());

        // The start reading has to have been TAKEN, which is a statement about
        // the session hook and not about the machine: a run whose hook never
        // fired would print "<not read>" for it and still look like a row.
        await Assert.That(CommitCharge.AtStart.Answered)
            .IsTrue()
            .Because("SuiteCoverage.TakeTheCommitChargeReading is a [Before(TestSession)] hook, so by the time any test runs the start reading exists -- and a start reading that was never taken makes the start-versus-end difference, which is the whole point of taking two, unavailable");

        foreach (var capability in SuiteEnvironment.All)
        {
            var state = SuiteEnvironment.StateOf(capability);

            await Assert.That(summary).Contains(state switch
            {
                CapabilityState.Present => "PRESENT",
                CapabilityState.AbsentAsAWhole => "ABSENT",
                _ => "PARTIAL",
            });
        }
    }

    /// <summary>
    /// A declared drive-letter spelling that did not take is a red run, and an
    /// undeclared one pins nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The positive control under the live arm below.</b> A developer machine
    /// declares nothing, so the live arm asserts nothing there -- and a check that
    /// can only pass is indistinguishable from one that works. Every combination
    /// is driven here instead, in-process and pure, on every ordinary run.
    /// </para>
    /// <para>
    /// <b><see cref="GateDriveVerdict.NotAsDeclared"/> is the whole point.</b>
    /// The gate's two shells exist to be two instruments; forcing that by
    /// construction is worth nothing if a forcing that silently failed reads the
    /// same as one that worked.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADeclaredDriveLetterSpellingThatDidNotTakeIsARedRunAndAnUndeclaredOneIsNot()
    {
        // Nothing declared: whatever the shell handed this run is what it gets,
        // which is what a developer machine has always done.
        await Assert.That(GateDriveCase.Judge(null, DriveLetterCase.Upper)).IsEqualTo(GateDriveVerdict.NotDeclared);
        await Assert.That(GateDriveCase.Judge(null, DriveLetterCase.Lower)).IsEqualTo(GateDriveVerdict.NotDeclared);
        await Assert.That(GateDriveCase.Judge(null, null)).IsEqualTo(GateDriveVerdict.NotDeclared);

        // Declared and received: this half of the gate is the instrument it says
        // it is.
        await Assert.That(GateDriveCase.Judge(DriveLetterCase.Upper, DriveLetterCase.Upper)).IsEqualTo(GateDriveVerdict.AsDeclared);
        await Assert.That(GateDriveCase.Judge(DriveLetterCase.Lower, DriveLetterCase.Lower)).IsEqualTo(GateDriveVerdict.AsDeclared);

        // ⚠️ THE FAULT, in both directions, which is the state the 2026-08-24
        // gate was in three times over without anything saying so.
        await Assert.That(GateDriveCase.Judge(DriveLetterCase.Lower, DriveLetterCase.Upper)).IsEqualTo(GateDriveVerdict.NotAsDeclared);
        await Assert.That(GateDriveCase.Judge(DriveLetterCase.Upper, DriveLetterCase.Lower)).IsEqualTo(GateDriveVerdict.NotAsDeclared);

        // A base directory that is not on a drive letter at all cannot satisfy a
        // declaration either, and must not read as though it had.
        await Assert.That(GateDriveCase.Judge(DriveLetterCase.Upper, null)).IsEqualTo(GateDriveVerdict.NotAsDeclared);
        await Assert.That(GateDriveCase.Judge(DriveLetterCase.Lower, null)).IsEqualTo(GateDriveVerdict.NotAsDeclared);

        // And the reading itself, since the verdict is only as good as it: the
        // lower-case spelling is the one no Windows API ever returns, so telling
        // the two apart cannot be left to a comparison that ignores case.
        await Assert.That(GateDriveCase.SpellingOf(@"C:\Source\BrowserAI")).IsEqualTo(DriveLetterCase.Upper);
        await Assert.That(GateDriveCase.SpellingOf(@"c:\Source\BrowserAI")).IsEqualTo(DriveLetterCase.Lower);
        await Assert.That(GateDriveCase.SpellingOf(@"\\server\share\BrowserAI")).IsNull();
        await Assert.That(GateDriveCase.SpellingOf(null)).IsNull();
    }

    /// <summary>
    /// This run says which drive-letter spelling it actually received, and a
    /// forcing that did not take fails here instead of passing quietly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gate's reason for running two shells is that they are two
    /// instruments, and until 2026-08-24 nothing measured that.</b> All six runs
    /// of that day's gate received <c>C:</c> -- three of them silently duplicating
    /// the other three -- and the run summary, the coverage block and the release
    /// checklist all read exactly as they read on a gate that really did exercise
    /// both spellings.
    /// </para>
    /// <para>
    /// <b>This is not <see cref="DriveLetterCase"/> restated.</b> That type
    /// spells every guard path both ways <i>inside</i> a run, so the class of
    /// defect is covered whatever started the suite; this is the gate's claim
    /// about <i>itself</i>, and the two are different guarantees. See
    /// <see cref="GateDriveCase"/>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRunReportsTheDriveLetterSpellingItActuallyReceived()
    {
        // Unconditional: the row is what a reader of one log can act on, and it
        // is printed whether or not this run declared anything.
        await Assert.That(GateDriveCase.CoverageRow).Contains("drive letter");
        await Assert.That(GateDriveCase.CoverageRow).Contains(AppContext.BaseDirectory);
        await Assert.That(GateDriveCase.CoverageRow).Contains(GateDriveCase.Variable);

        await Assert.That(GateDriveCase.SpellingOf(AppContext.BaseDirectory)).IsEqualTo(GateDriveCase.Received);

        // ⚠️ THE LIVE ARM. It asserts nothing on a machine that declared nothing,
        // exactly as BROWSERAI_EXPECTED_ABSENT does, and the combinations it
        // cannot reach are driven by the pure test above.
        await Assert.That(GateDriveCase.Verdict)
            .IsNotEqualTo(GateDriveVerdict.NotAsDeclared)
            .Because(GateDriveCase.Refusal(GateDriveCase.Declared, GateDriveCase.Received));
    }

    /// <summary>
    /// The run records the machine's commit charge, and every band it could
    /// report is exercised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The reading exists because a closed hazard row asked for it by name
    /// and nothing took it.</b> The six-run-gate row in
    /// [HAZARDS.md](../../HAZARDS.md#hazard-index) closed on 2026-08-24 against a
    /// kernel-level leak outside this repository -- 137.4 GB committed of a
    /// 157.7 GB limit -- and closed by naming the one reading that would tell
    /// that cause from a live one: <i>the commit charge beside the run</i>. It
    /// then sat in a document, taken by nobody, so when the same shape recurred
    /// on 2026-08-29 the reading did not exist for that run either and the row's
    /// own question could not be asked.
    /// </para>
    /// <para>
    /// <b>The pure arm is mandatory and not thorough</b>, for
    /// <see cref="AFilteredRunIsToldFromAFullOneFromOneThatCouldNotTellAndFromABrokenInstrument"/>'s
    /// reason exactly: a healthy machine sits in <c>HEALTHY</c> for ever, so the
    /// two bands that matter would otherwise be code nobody has ever run --
    /// first exercised on the day something is already wrong, which is the worst
    /// possible day to find out that a band prints the wrong word.
    /// </para>
    /// <para>
    /// <b>No arm asserts a number the machine produced</b>, which is
    /// <see cref="MachineLoad"/>'s standing rule: an assertion on a live commit
    /// figure would pass or fail depending on the developer's other windows.
    /// What is asserted is that the row is produced, that the classification is
    /// right, and that an unreadable reading says so instead of reading as
    /// zero bytes committed.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRunReportsTheMachinesCommitChargeAndEveryBandIsExercised()
    {
        const ulong Limit = 100UL * 1024 * 1024 * 1024;

        static CommitChargeReading at(double fraction) => new((ulong)(Limit * fraction), Limit);

        // Every band, driven in-process on every ordinary run.
        await Assert.That(CommitCharge.Classify(at(0.10), at(0.20))).IsEqualTo(CommitChargeVerdict.Healthy);
        await Assert.That(CommitCharge.Classify(at(0.10), at(0.80))).IsEqualTo(CommitChargeVerdict.Tight);
        await Assert.That(CommitCharge.Classify(at(0.95), at(0.10))).IsEqualTo(CommitChargeVerdict.Critical);
        await Assert.That(CommitCharge.Classify(CommitChargeReading.NotTaken, CommitChargeReading.NotTaken))
            .IsEqualTo(CommitChargeVerdict.Unreadable);

        // ⚠️ The verdict is taken on the WORSE of the two readings and never on
        // the last one. A run that started at 95% and ended at 10% because the
        // thing eating the machine was reaped mid-run is a run whose timings are
        // suspect, and an end-only verdict would call it healthy -- which is
        // precisely the misreading the 2026-08-24 closure was arguing against.
        await Assert.That(CommitCharge.Classify(at(0.95), at(0.10)))
            .IsNotEqualTo(CommitCharge.Classify(at(0.10), at(0.10)))
            .Because("a run that was critical at either end is not a healthy run, however it finished");

        // A boundary is a boundary: at exactly the fraction, the harder word.
        await Assert.That(CommitCharge.Classify(at(CommitCharge.TightFraction), at(0))).IsEqualTo(CommitChargeVerdict.Tight);
        await Assert.That(CommitCharge.Classify(at(CommitCharge.CriticalFraction), at(0))).IsEqualTo(CommitChargeVerdict.Critical);

        // An unreadable reading must never read as zero committed, which is the
        // one wrong answer that would look healthy.
        var unreadable = CommitCharge.RowFor(CommitChargeReading.NotTaken, CommitChargeReading.NotTaken);

        await Assert.That(unreadable).Contains("<not read>");
        await Assert.That(unreadable).Contains("DID NOT ANSWER");
        await Assert.That(CommitChargeReading.NotTaken.Answered).IsFalse();

        // A limit of zero is unreadable and not infinitely full: the
        // division that would produce the percentage is the one thing here that
        // could throw or produce an infinity, and it is the shape a partially
        // populated struct takes.
        await Assert.That(new CommitChargeReading(1, 0).Answered).IsFalse();

        // And the two loud bands say why they are loud, in the run's own output.
        await Assert.That(CommitCharge.RowFor(at(0.95), at(0.95))).Contains("HAZARDS.md");
        await Assert.That(CommitCharge.RowFor(at(0.80), at(0.80))).Contains("quieter machine");

        // The live arm, which asserts the row is produced and never what it says.
        await Assert.That(CommitCharge.CoverageRow).Contains(CommitCharge.Title);
        await Assert.That(CommitCharge.CoverageRow).Contains("Committed Bytes");
        await Assert.That(CommitCharge.CoverageRow)
            .DoesNotContain("<unreadable>")
            .Because("GetPerformanceInfo answering is the premise of the whole row, and a machine where it does not is one this run cannot say anything about");
    }

    /// <summary>
    /// The run states the freshness its own publish check established, and a
    /// stale reading renders as staleness and not as a pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The row exists because a check that is silent on success gives a
    /// suspicion nothing to be checked against.</b>
    /// <see cref="PublishedSlice.EnsureFresh"/> has compared the published binary
    /// against every input that goes into it since the beginning, and it threw or
    /// it said nothing -- so twelve green gate logs carried no sentence about
    /// freshness at all. On 2026-08-30 a gate runner with a staleness suspicion
    /// reached for the nearest figure to hand, a <b>commit date</b>, put
    /// <c>56383c9</c>'s 01:20:40 beside the binary's 01:14:16 and reported four
    /// gate sets -- twelve full runs -- as having driven a stale binary. Every
    /// reading in that account was true and the conclusion was false: the file the
    /// commit touched was stamped 01:12:22.665, before the publish, and
    /// <c>git commit</c> records when it ran instead of touching a working-tree
    /// file. Dissolving it took an investigation that one printed line would have
    /// ended.
    /// </para>
    /// <para>
    /// <b>The synthetic arm is mandatory and not thorough</b>, for
    /// <see cref="TheRunReportsTheMachinesCommitChargeAndEveryBandIsExercised"/>'s
    /// reason exactly: a healthy tree publishes and then runs, so <c>STALE</c> is
    /// a state this machine reaches perhaps once a fortnight and the rendering
    /// that matters would otherwise first run on a day somebody is already
    /// confused. Both directions are driven here -- a stale reading must carry the
    /// word, the sign and the warning, and a fresh one must carry none of them.
    /// </para>
    /// <para>
    /// <b>And the live arm ties the row to the guard and not to a second
    /// enumeration.</b> The row and the refusal are two renderings of one
    /// <see cref="PublishedSlice.Measure"/>, so a run whose block says
    /// <c>FRESH</c> while its slice arms refuse is impossible by construction --
    /// which is a claim worth an assertion precisely because the construction is
    /// the whole of the guarantee. A second walk asking a subtly different
    /// question is how the corpus scan came to disagree with <c>git ls-files</c>
    /// by 520 files under a remark saying the two matched.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRunStatesThePublishFreshnessItEstablished()
    {
        var published = new DateTime(2026, 8, 30, 2, 3, 58, 876, DateTimeKind.Utc);
        const string Newest = @"src\BrowserAI\Sessions\SessionLock.cs";

        var fresh = new PublishFreshnessReading(
            published,
            published - new TimeSpan(2, 51, 36),
            Newest,
            Inputs: 95,
            Newer: [],
            Absence: null);

        var stale = new PublishFreshnessReading(
            published,
            published + new TimeSpan(1, 2, 0),
            Newest,
            Inputs: 95,
            Newer: [Newest, @"third-party\sqlite\sqlite3.c"],
            Absence: null);

        var nothing = PublishFreshnessReading.Establishing(
            "nothing has been published: there is no directory at 'X'");

        await Assert.That(PublishedSlice.Judge(fresh)).IsEqualTo(PublishFreshnessVerdict.Fresh);
        await Assert.That(PublishedSlice.Judge(stale)).IsEqualTo(PublishFreshnessVerdict.Stale);
        await Assert.That(PublishedSlice.Judge(nothing)).IsEqualTo(PublishFreshnessVerdict.NotEstablished);

        // ⚠️ THE BOUNDARY THE GUARD ACTUALLY USES. EnsureFresh refuses on `>`
        // and never on `>=`, so an input stamped to the millisecond OF the
        // publish is not newer than it. The verdict is taken on the same list
        // the guard refuses on and not on the sign of the row's own margin,
        // which is what keeps the two from parting company at exactly this tick.
        var tie = fresh with { Newest = published };

        await Assert.That(tie.Margin).IsEqualTo(TimeSpan.Zero);
        await Assert.That(PublishedSlice.Judge(tie)).IsEqualTo(PublishFreshnessVerdict.Fresh);

        // The three words are three words, and none is another's prefix, so a
        // reader of one log can tell them apart.
        var words = new[] { PublishFreshnessVerdict.Fresh, PublishFreshnessVerdict.Stale, PublishFreshnessVerdict.NotEstablished }
            .Select(verdict => PublishedSlice.StateWord(verdict).Trim())
            .ToList();

        await Assert.That(words.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(3);
        await Assert.That(words.Count(word => words.Exists(other => other != word && other.StartsWith(word, StringComparison.Ordinal))))
            .IsEqualTo(0);

        // The passing row, which is the one that did not exist and is the whole
        // point: the two timestamps, the margin, its direction, the input it was
        // taken against and how many there were.
        var freshRow = PublishedSlice.RowFor(fresh);

        await Assert.That(freshRow).Contains(PublishedSlice.FreshnessTitle);
        await Assert.That(freshRow).Contains(PublishedSlice.FreshState);
        await Assert.That(freshRow).Contains("2026-08-30T02:03:58.876Z");
        await Assert.That(freshRow).Contains("2026-08-29T23:12:22.876Z");
        await Assert.That(freshRow).Contains("2h51m36s newer than the newest of 95 inputs");
        await Assert.That(freshRow).Contains(Newest);

        await Assert.That(freshRow)
            .DoesNotContain("⚠️")
            .Because("a passing row states a margin and claims nothing more; the warning belongs to the band where the run's own results are worthless");

        // ⚠️ THE OTHER DIRECTION, which is the arm a rendering bug would show up
        // in. A stale reading must read as staleness -- the word, the sign, the
        // count and the command -- and must not read as a pass.
        var staleRow = PublishedSlice.RowFor(stale);

        await Assert.That(staleRow).Contains(PublishedSlice.StaleState);
        await Assert.That(staleRow).Contains("1h02m00s OLDER than the newest of 95 inputs");
        await Assert.That(staleRow).Contains("2 of 95 are newer");
        await Assert.That(staleRow).Contains("THE PUBLISHED BINARY IS OLDER THAN THE SOURCE");
        await Assert.That(staleRow).Contains(PublishedSlice.PublishCommand);

        await Assert.That(staleRow)
            .DoesNotContain(PublishedSlice.FreshState)
            .Because("the two states are told apart by a reader scanning one log for a word, so a stale row carrying the passing word would be worse than no row at all");

        // A run with no binary says so instead of reporting a comparison it
        // never made -- the shape of honesty every other row in this block owes
        // its existence to.
        var absentRow = PublishedSlice.RowFor(nothing);

        await Assert.That(absentRow).Contains(PublishedSlice.NotEstablishedState);
        await Assert.That(absentRow).Contains("nothing has been published");
        await Assert.That(absentRow).Contains("nothing was compared");
        await Assert.That(absentRow).DoesNotContain(PublishedSlice.FreshState);

        // ⚠️ THE VERSION HALF -- N16, 2026-09-24. Every file time agrees and the
        // binary was still built at another commit: the version is derived from
        // the git height, so a commit touching no input moves it and leaves every
        // timestamp alone. Measured the same day, 1.1.1-alpha.0.72 against a tree
        // at 1.1.1-alpha.0.73, while this row said FRESH. Planted red 2026-09-24
        // by making the comparison say the versions agree whatever they are.
        var behind = fresh with { PublishedVersion = "1.1.1-alpha.0.72", TreeVersion = "1.1.1-alpha.0.73" };

        await Assert.That(PublishedSlice.Judge(behind)).IsEqualTo(PublishFreshnessVerdict.Stale);
        await Assert.That(PublishedSlice.RefusalFor(behind)).Contains("built as 1.1.1-alpha.0.72 and this tree derives 1.1.1-alpha.0.73");
        await Assert.That(PublishedSlice.RefusalFor(behind)).Contains(PublishedSlice.PublishCommand);

        var behindRow = PublishedSlice.RowFor(behind);

        await Assert.That(behindRow).Contains(PublishedSlice.StaleState);
        await Assert.That(behindRow).Contains("BUILT AS 1.1.1-alpha.0.72 AND THIS TREE DERIVES");
        await Assert.That(behindRow).DoesNotContain(PublishedSlice.FreshState);

        // ⚠️ AND THE FILE TIMES ARE STILL REPORTED AS WHAT THEY ARE. A version
        // staleness is not a file-time one: this binary is newer than every input,
        // and the first rendering of this row said "OLDER than" because it chose
        // the word from the verdict -- seen live on the first run after the
        // version check landed, beside a refusal that was right.
        await Assert.That(behindRow).Contains("2h51m36s newer than the newest of 95 inputs");
        await Assert.That(behindRow).DoesNotContain("OLDER than");

        // And versions that agree change nothing, in the verdict or the row
        // beyond saying which version it was.
        var level = fresh with { PublishedVersion = "1.1.1-alpha.0.73", TreeVersion = "1.1.1-alpha.0.73" };

        await Assert.That(PublishedSlice.Judge(level)).IsEqualTo(PublishFreshnessVerdict.Fresh);
        await Assert.That(PublishedSlice.RowFor(level)).Contains("exe and tree both 1.1.1-alpha.0.73");

        // The live reading looks at both, which is what makes the two halves one
        // comparison and not two.
        var live = PublishedSlice.Measure();

        if (live.Absence is null)
        {
            await Assert.That(live.TreeVersion).IsEqualTo(BrowserAI.Hosting.BuildVersion.Current);
            await Assert.That(live.PublishedVersion).IsNotNull();
        }

        // And the refusals, driven from readings and not by arranging a
        // stale publish -- which would leave this tree needing a re-publish to
        // go green again, on the one message a developer reads at the worst
        // possible moment.
        await Assert.That(PublishedSlice.RefusalFor(stale)).Contains("older than 2 source file(s)");
        await Assert.That(PublishedSlice.RefusalFor(stale)).Contains(@"third-party\sqlite\sqlite3.c");
        await Assert.That(PublishedSlice.RefusalFor(stale)).Contains(PublishedSlice.PublishCommand);
        await Assert.That(PublishedSlice.RefusalFor(nothing)).Contains("There is no published binary");
        await Assert.That(PublishedSlice.RefusalFor(nothing)).Contains(PublishedSlice.PublishCommand);

        // ⚠️ THE LIVE ARM: the row reaches the block. This is the assertion that
        // goes red when the row is taken out of Summary(), which is the state
        // every gate log this project has ever produced was in.
        var summary = SuiteEnvironment.Summary();

        await Assert.That(summary).Contains(PublishedSlice.FreshnessTitle);
        await Assert.That(summary).Contains(PublishedSlice.StateWord(PublishedSlice.Verdict).Trim());

        // ⚠️ AND THE ROW IS THE GUARD'S OWN COMPARISON. EnsureFresh refuses
        // exactly the readings this row declines to spell FRESH; asserted and
        // not left to the construction, because the construction is the entire
        // guarantee and nothing else would notice it being replaced.
        var refused = false;

        try
        {
            PublishedSlice.EnsureFresh();
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        await Assert.That(refused)
            .IsEqualTo(PublishedSlice.Verdict is not PublishFreshnessVerdict.Fresh)
            .Because("the row and the refusal are two renderings of one reading, so a run whose block says FRESH while its slice arms refuse -- or the reverse -- would mean the row had grown an enumeration of its own");
    }

    /// <summary>
    /// A filtered run is told from a full one, from one that could not tell, and
    /// from a broken instrument -- and only one of the four costs a release.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The positive control under the live arms below</b>, and it is mandatory
    /// and not thorough. A gate run is never filtered, so
    /// <see cref="SuiteFilter.Verdict"/> reads <c>FULL RUN</c> on every run this
    /// repository takes -- and a reading that can only ever come back empty is
    /// indistinguishable from one that cannot read. This drives all four states
    /// and both modes in-process, on every ordinary run.
    /// </para>
    /// <para>
    /// <b><see cref="SuiteFilterVerdict.Unread"/> is the state this exists
    /// for.</b> <see cref="GlobalContext"/> lazily creates an empty instance for
    /// whoever asks first, so a filter read before the framework populated
    /// anything is <see langword="null"/> -- identical to a run that really had
    /// none. The pair of readings below is what separates them, and the row must
    /// never spell that state <c>FULL RUN</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFilteredRunIsToldFromAFullOneFromOneThatCouldNotTellAndFromABrokenInstrument()
    {
        const string Filter = "/*/*/SessionPathTests/*";

        var unread = SuiteFilterReading.NotTaken;
        var unpopulated = new SuiteFilterReading(Taken: true, SessionContextPopulated: false, Global: null, Session: null);
        var full = new SuiteFilterReading(Taken: true, SessionContextPopulated: true, Global: null, Session: null);
        var filtered = new SuiteFilterReading(Taken: true, SessionContextPopulated: true, Global: Filter, Session: Filter);
        var torn = new SuiteFilterReading(Taken: true, SessionContextPopulated: true, Global: Filter, Session: "/*/*/LockRecordTests/*");
        var halfTorn = new SuiteFilterReading(Taken: true, SessionContextPopulated: true, Global: Filter, Session: null);

        // ⚠️ NOBODY READ IT, AND IT IS NOT 'FULL RUN'. Both shapes: nothing took
        // the reading at all, and a reading taken before the framework populated
        // the contexts it is taken from.
        await Assert.That(SuiteFilter.Judge(unread)).IsEqualTo(SuiteFilterVerdict.Unread);
        await Assert.That(SuiteFilter.Judge(unpopulated)).IsEqualTo(SuiteFilterVerdict.Unread);

        await Assert.That(SuiteFilter.Judge(full)).IsEqualTo(SuiteFilterVerdict.NotFiltered);
        await Assert.That(SuiteFilter.Judge(filtered)).IsEqualTo(SuiteFilterVerdict.Filtered);

        // Two seams the framework fills from ONE value carrying two values, in
        // both shapes it can take: two different filters, and one seam empty.
        await Assert.That(SuiteFilter.Judge(torn)).IsEqualTo(SuiteFilterVerdict.Disagreed);
        await Assert.That(SuiteFilter.Judge(halfTorn)).IsEqualTo(SuiteFilterVerdict.Disagreed);

        // The four states are four words, and none of them is another's prefix,
        // so a reader of one log can tell them apart.
        var words = new[] { SuiteFilterVerdict.Unread, SuiteFilterVerdict.NotFiltered, SuiteFilterVerdict.Filtered, SuiteFilterVerdict.Disagreed }
            .Select(SuiteFilter.StateWord)
            .ToList();

        await Assert.That(words.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(4);
        await Assert.That(SuiteFilter.StateWord(SuiteFilterVerdict.Unread)).IsEqualTo(SuiteFilter.UnreadState);

        // ⚠️ THE RELEASE ASYMMETRY, which is the whole of decision (d): a
        // filtered run is a CORRECT run and a filtered release is not a release.
        await Assert.That(SuiteFilter.Decide(SuiteFilterVerdict.NotFiltered, isReleaseRun: false)).IsEqualTo(SuiteFilterDecision.Proceed);
        await Assert.That(SuiteFilter.Decide(SuiteFilterVerdict.NotFiltered, isReleaseRun: true)).IsEqualTo(SuiteFilterDecision.Proceed);

        await Assert.That(SuiteFilter.Decide(SuiteFilterVerdict.Filtered, isReleaseRun: false)).IsEqualTo(SuiteFilterDecision.Proceed);
        await Assert.That(SuiteFilter.Decide(SuiteFilterVerdict.Filtered, isReleaseRun: true)).IsEqualTo(SuiteFilterDecision.Refuse);

        // 'I cannot say' is not 'no', so it costs a release too.
        await Assert.That(SuiteFilter.Decide(SuiteFilterVerdict.Unread, isReleaseRun: false)).IsEqualTo(SuiteFilterDecision.Proceed);
        await Assert.That(SuiteFilter.Decide(SuiteFilterVerdict.Unread, isReleaseRun: true)).IsEqualTo(SuiteFilterDecision.Refuse);

        // A broken instrument fails in BOTH modes, exactly as CapabilityState
        // .Partial does and for the same reason: it was never a clean state.
        await Assert.That(SuiteFilter.Decide(SuiteFilterVerdict.Disagreed, isReleaseRun: false)).IsEqualTo(SuiteFilterDecision.Refuse);
        await Assert.That(SuiteFilter.Decide(SuiteFilterVerdict.Disagreed, isReleaseRun: true)).IsEqualTo(SuiteFilterDecision.Refuse);

        // And the rows those states produce, since a verdict nobody can read in
        // the block is a verdict that changes nothing.
        await Assert.That(SuiteFilter.RowFor(filtered, isReleaseRun: false)).Contains(Filter);
        await Assert.That(SuiteFilter.RowFor(filtered, isReleaseRun: false)).Contains(SuiteFilter.FilteredState);
        await Assert.That(SuiteFilter.RowFor(filtered, isReleaseRun: false)).Contains("NOT A VERIFICATION");
        await Assert.That(SuiteFilter.RowFor(full, isReleaseRun: false)).Contains(SuiteFilter.FullRunState);
        await Assert.That(SuiteFilter.RowFor(full, isReleaseRun: false)).DoesNotContain("⚠️");
        await Assert.That(SuiteFilter.RowFor(unread, isReleaseRun: false)).Contains("DID NOT ANSWER");
        await Assert.That(SuiteFilter.RowFor(torn, isReleaseRun: false)).Contains("INSTRUMENT IS BROKEN");

        // The refusals name the variable that produced them and the filter that
        // did, because whoever reads one is looking at a log and not at a screen.
        await Assert.That(SuiteFilter.Refusal(filtered, isReleaseRun: true)).Contains(SuiteEnvironment.ReleaseRunVariable);
        await Assert.That(SuiteFilter.Refusal(filtered, isReleaseRun: true)).Contains(Filter);
        await Assert.That(SuiteFilter.Refusal(unread, isReleaseRun: true)).Contains("CANNOT SAY");
        await Assert.That(SuiteFilter.Refusal(torn, isReleaseRun: false)).Contains("ONE value");
    }

    /// <summary>
    /// This run says whether it was filtered, and it reads that from the
    /// platform and not from its own command line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The live arm, shaped after
    /// <see cref="TheRunReportsTheDriveLetterSpellingItActuallyReceived"/>.</b>
    /// The row is unconditional because it is what a reader of one log can act
    /// on; the verdict is not asserted to be any particular value, because a
    /// developer running one class deliberately is not a defect.
    /// </para>
    /// <para>
    /// <b>What is asserted is that the latched reading is the seam it claims to
    /// be.</b> <see cref="SuiteFilter.Observe"/> re-reads both contexts here,
    /// inside a test and not inside the session hook, so a latch that had
    /// gone stale or had been taken from somewhere else shows up as a
    /// disagreement and not as a row nobody checked.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRunReportsWhetherItWasFiltered()
    {
        await Assert.That(SuiteFilter.CoverageRow).Contains(SuiteFilter.Title);
        await Assert.That(SuiteFilter.CoverageRow).Contains(SuiteFilter.StateWord(SuiteFilter.Verdict));
        await Assert.That(SuiteFilter.CoverageRow).Contains("GlobalContext.TestFilter");

        // The hook ran, so the reading is a reading. Without this the row could
        // sit at UNREAD forever and every assertion above would still pass.
        await Assert.That(SuiteFilter.Reading.Taken).IsTrue();
        await Assert.That(SuiteFilter.Verdict).IsNotEqualTo(SuiteFilterVerdict.Unread);

        // ⚠️ THE SEAM, RE-READ. The latch was taken in [Before(TestSession)];
        // this reads the same two contexts from inside a test. They must agree,
        // and both must still be populated.
        var now = SuiteFilter.Observe();

        await Assert.That(now.Global).IsEqualTo(SuiteFilter.Reading.Global);
        await Assert.That(SuiteFilter.Judge(now)).IsEqualTo(SuiteFilter.Verdict);

        // The filter is only ever handed out from the one state where it means
        // something, so nothing downstream can quote a string out of UNREAD.
        await Assert.That(SuiteFilter.Filter is null).IsEqualTo(SuiteFilter.Verdict is not SuiteFilterVerdict.Filtered);
    }

    /// <summary>
    /// This run is not one the gate would refuse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the ECHO of decision (d) and no longer the mechanism, and
    /// the difference is the defect it was.</b> Until 2026-08-24 the refusal
    /// lived here, in an ordinary <c>[Test]</c> -- so <c>BROWSERAI_RELEASE_RUN=1</c>
    /// with a filter that did not select this one method was a filtered run, a
    /// claimed release, and green. It is
    /// <see cref="SuiteCoverage.ReportWhatThisRunExercised"/>'s session hook that
    /// refuses now, from a place no filter reaches.
    /// </para>
    /// <para>
    /// <b>What it still buys, kept deliberately and not deleted.</b> The
    /// session hook fails a run <i>after</i> everything has run; this fails it in
    /// the list of tests, with the refusal quoted, which is where a human looks
    /// first. It also asserts the negative direction live -- an unfiltered
    /// ordinary run is not refused -- on every run of this suite, which is the
    /// direction the out-of-process control cannot cover.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARunThatWasFilteredIsNeverARelease() =>
        await Assert.That(SuiteFilter.Decision)
            .IsNotEqualTo(SuiteFilterDecision.Refuse)
            .Because(SuiteFilter.Refusal(SuiteFilter.Reading, SuiteEnvironment.IsReleaseRun));

    /// <summary>
    /// A child run that really was filtered reads as filtered, and refuses to be
    /// a release.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the positive control the pure test above cannot be, and
    /// this repository has already been burned by the gap between them.</b> A
    /// search that returns zero needs proof it could have returned something:
    /// every run of this suite is unfiltered, so <c>GlobalContext.TestFilter</c>
    /// is <see langword="null"/> on every one of them, and an instrument stuck at
    /// <see langword="null"/> would publish <c>FULL RUN</c> forever with nothing
    /// to say otherwise. One short child process, genuinely filtered, is what
    /// makes the row's <c>FULL RUN</c> mean something.
    /// </para>
    /// <para>
    /// ⚠️ <b>And since 2026-08-24 it carries the harder half: the filter names a
    /// method that is NOT the refusal.</b> The child selects
    /// <see cref="AReleaseRunFailsWhereAnOrdinaryRunSkips"/>, a pure test that
    /// passes, and is handed <c>BROWSERAI_RELEASE_RUN=1</c> -- so the only thing
    /// that can fail it is the session hook. That is the whole point: while the
    /// refusal was an ordinary <c>[Test]</c> this child <b>exited 0</b>, which is
    /// a filtered release reporting success, and it is the red this arm was
    /// re-pointed to catch.
    /// </para>
    /// <para>
    /// <b>The other direction is this very run</b>: unfiltered, ordinary, and
    /// green.
    /// </para>
    /// <para>
    /// <b>Recursion is impossible by construction and guarded anyway.</b> The
    /// child's filter names <see cref="AReleaseRunFailsWhereAnOrdinaryRunSkips"/>
    /// and nothing else, so it can never select this method; and
    /// <see cref="SuiteFilter.ProbeVariable"/> is set only by this launcher, so a
    /// process that sees it set knows it is the child. The second bolt exists for
    /// the day a filter fails open, which is a thing
    /// <c>--treenode-filter</c> is already documented to do.
    /// </para>
    /// <para>
    /// <b>The bound is <see cref="TestDefaults.ProcessHang"/> and it is a hang
    /// detector</b>: the child discovers one assembly and runs one test, so
    /// nothing here is a promptness claim.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFilteredChildRunReadsAsFilteredAndIsRefusedAsARelease()
    {
        // The recursion guard. Unreachable while the child's filter selects one
        // other method, and a real assertion on the day it is not.
        if (Environment.GetEnvironmentVariable(SuiteFilter.ProbeVariable) is { Length: > 0 })
        {
            await Assert.That(SuiteFilter.Verdict).IsEqualTo(SuiteFilterVerdict.Filtered);
            return;
        }

        var host = Path.Combine(AppContext.BaseDirectory, "BrowserAI.Tests.exe");

        // The instrument has something to run. A missing host would make every
        // assertion below unreachable and not false.
        await Assert.That(File.Exists(host)).IsTrue().Because(host);

        using var scratch = ScratchDirectory.Create("filter-probe");
        var report = Path.Combine(scratch.Path, "filter.txt");
        const string Filter = "/*/*/SuiteCoverageTests/" + nameof(AReleaseRunFailsWhereAnOrdinaryRunSkips);

        var startInfo = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        startInfo.ArgumentList.Add("--disable-logo");
        startInfo.ArgumentList.Add("--treenode-filter");
        startInfo.ArgumentList.Add(Filter);

        startInfo.Environment[SuiteFilter.ProbeVariable] = report;
        startInfo.Environment[SuiteEnvironment.ReleaseRunVariable] = "1";

        using var child = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{host}'.");

        // Started outside a job object, so the spawn record is the only thing
        // that can name it if this run is killed while it is running.
        SpawnRecord.Add(child.Id);

        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();

        using var patience = new CancellationTokenSource(TestDefaults.ProcessHang);

        await child.WaitForExitAsync(patience.Token);

        var exitCode = child.ExitCode;
        var console = await stdout + await stderr;

        // ⚠️ THE READING, ON A RUN THAT REALLY WAS FILTERED. Not the state word
        // alone: the filter string itself, byte for byte, because a verdict that
        // said FILTERED while carrying somebody else's filter would be the same
        // defect one layer in.
        await Assert.That(File.Exists(report)).IsTrue().Because(console);

        var described = await File.ReadAllTextAsync(report);

        await Assert.That(described).Contains($"verdict={SuiteFilter.FilteredState}").Because(console);
        await Assert.That(described).Contains($"global={Filter}");
        await Assert.That(described).Contains($"session={Filter}");
        await Assert.That(described).Contains($"decision={SuiteFilterDecision.Refuse}");

        // The diagnostic beside the verdict, asserted to be present and not
        // to have a value: it records whether the child's OWN command line
        // carried the filter, which is the question the verdict deliberately
        // does not answer from. A field nobody reads is a field that can rot.
        await Assert.That(described).Contains("commandLineCarriesIt=");

        // ⚠️ AND THE REFUSAL FIRED, IN A RUN THAT SELECTED SOMETHING ELSE. The
        // one test this child ran passes; the only thing in the process that can
        // fail it is the session hook, so a non-zero exit is the refusal and
        // nothing else. Measured 2026-08-24 at TUnit 1.65.0: an exception out of
        // an [After(TestSession)] hook exits 10 and prints
        // "Test adapter test session failure".
        await Assert.That(exitCode).IsNotEqualTo(0).Because(console);

        // ⚠️ AND IT IS THE REFUSAL , NOT SOMETHING ELSE THAT FAILED IT.
        // ***Corrected 2026-08-24 (previously
        // `Assert.That(console).Contains(SuiteEnvironment.ReleaseRunVariable)`).***
        // That was a tautology under a comment claiming it was the decisive
        // half: SuiteFilter.RowFor's FILTERED arm ends with
        // "<variable>=1 makes this state a failure.", and the coverage block goes
        // out through the real standard output handle on every run -- so the
        // child printed that variable's name whether or not anything refused.
        // The sentence below is written by SuiteFilter.Refusal and by nothing
        // else in this process, and it is spelled ASCII-only on purpose: the
        // child's console transliterates an em dash to a hyphen, so a needle
        // carrying one would be a needle that can never match.
        await Assert.That(console)
            .Contains("Re-run without a filter, or unset the variable and stop calling it a release.")
            .Because(console);

        // The other direction, live: this process took the same reading through
        // the same code and did not refuse.
        await Assert.That(SuiteFilter.Decide(SuiteFilter.Verdict, isReleaseRun: false)).IsEqualTo(SuiteFilterDecision.Proceed);
    }

    /// <summary>
    /// Nothing this run could not exercise is merely half-installed.
    /// </summary>
    /// <remarks>
    /// This is the old per-site <c>IsAbsentAsAWhole</c> assertion, kept and
    /// centralised: <i>nobody published</i> is an ordinary state, <i>the publish
    /// ran and the binary is missing from it</i> is a defect, and the two used to
    /// be told apart thirty-five times over.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NothingThisRunLacksIsHalfInstalled()
    {
        var partial = SuiteEnvironment.All
            .Where(capability => SuiteEnvironment.StateOf(capability) is CapabilityState.Partial)
            .Select(capability => capability.ToString())
            .ToList();

        await Assert.That(string.Join(", ", partial)).IsEmpty();
    }

    /// <summary>
    /// The reconciliation itself, over every shape a declaration can take.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>In-process and pure, for the reason <c>Decide</c> is.</b> The
    /// declaration only exists in a controlled environment, so a check written
    /// only against the live one would be a mechanism a developer machine could
    /// never exercise and CI would meet for the first time on the run that
    /// needed it -- which is the same dead-mechanism defect as a release branch
    /// that first runs on release day.
    /// </para>
    /// <para>
    /// <b>The fault is planted here in both directions</b>, because only one of
    /// them can be planted against this machine: everything is present locally,
    /// so a declaration naming anything is red, and a run in which something is
    /// genuinely absent is not something a test may arrange for itself.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent()
    {
        // Nothing declared: a developer machine, and no opinion about anything.
        // This is the arm that keeps a clean clone runnable, so it is asserted
        // against a run that lacks half of everything and not against none.
        await Assert.That(SuiteEnvironment.ReconcileDeclaredAbsence(null, [])).IsEmpty();
        await Assert.That(SuiteEnvironment.ReconcileDeclaredAbsence(
            null,
            [SuiteCapability.PackagedRelease, SuiteCapability.ClientCommandLine, SuiteCapability.ProvisionedFirefox]))
            .IsEmpty();

        // Declared exactly, in CI's own spelling.
        await Assert.That(SuiteEnvironment.ReconcileDeclaredAbsence(
            "PackagedRelease,ClientCommandLine",
            [SuiteCapability.PackagedRelease, SuiteCapability.ClientCommandLine]))
            .IsEmpty();

        // Order, spacing and casing are not the subject.
        await Assert.That(SuiteEnvironment.ReconcileDeclaredAbsence(
            " clientcommandline , packagedrelease ",
            [SuiteCapability.PackagedRelease, SuiteCapability.ClientCommandLine]))
            .IsEmpty();

        // `none`, which is what a fully provisioned controlled environment
        // declares -- and is not the same thing as declaring nothing.
        await Assert.That(SuiteEnvironment.ReconcileDeclaredAbsence(SuiteEnvironment.NothingExpectedAbsent, [])).IsEmpty();

        // ⚠️ THE FAULT: a fifth capability goes absent in an environment that
        // declared four. This is the whole reason the mechanism exists, and it
        // is the state that used to read as normal.
        var undeclared = SuiteEnvironment.ReconcileDeclaredAbsence(
            "PackagedRelease,ClientCommandLine",
            [SuiteCapability.PackagedRelease, SuiteCapability.ClientCommandLine, SuiteCapability.ProvisionedFirefox]);

        await Assert.That(undeclared.Count).IsEqualTo(1);
        await Assert.That(undeclared[0]).Contains(nameof(SuiteCapability.ProvisionedFirefox));
        await Assert.That(undeclared[0]).Contains(SuiteEnvironment.ExpectedAbsentVariable);

        // The same fault against `none`, which is the arm the end-to-end fault
        // injection uses: nothing was expected absent and something is.
        await Assert.That(SuiteEnvironment.ReconcileDeclaredAbsence(
            SuiteEnvironment.NothingExpectedAbsent,
            [SuiteCapability.PublishedSlice]).Count)
            .IsEqualTo(1);

        // ⚠️ AND THE OTHER DIRECTION, which is the one that keeps the pin from
        // rotting: a declaration wider than the truth is standing permission for
        // that capability to disappear later with nothing to say so.
        var overBroad = SuiteEnvironment.ReconcileDeclaredAbsence(
            "PackagedRelease,ClientCommandLine",
            [SuiteCapability.PackagedRelease]);

        await Assert.That(overBroad.Count).IsEqualTo(1);
        await Assert.That(overBroad[0]).Contains(nameof(SuiteCapability.ClientCommandLine));
        await Assert.That(overBroad[0]).Contains("PRESENT");

        // A name that is not a capability, which would otherwise shrink the
        // declared set in silence. The message names the legal values, because
        // whoever hits this is editing a YAML file with no completion.
        var typo = SuiteEnvironment.ReconcileDeclaredAbsence("PackedRelease", []);

        await Assert.That(typo.Count).IsEqualTo(1);
        await Assert.That(typo[0]).Contains("PackedRelease");
        await Assert.That(typo[0]).Contains(nameof(SuiteCapability.PackagedRelease));

        // Set to something that names nothing. Loud and not silently
        // equivalent to `none`: a variable that evaluated to empty is an
        // accident, and an accident that lands on the strictest reading would
        // read as a real failure of something else.
        var empty = SuiteEnvironment.ReconcileDeclaredAbsence("  ", []);

        await Assert.That(empty.Count).IsEqualTo(1);
        await Assert.That(empty[0]).Contains(SuiteEnvironment.NothingExpectedAbsent);
    }

    // ⚠️ THE THIRD ARM WAS DELETED ON 2026-08-20 AND WAS NOT RE-POINTED. It was
    // `TheWorkflowStillDeclaresWhatItExpectsToBeAbsent`, and it read
    // `.github/workflows/build.yml` -- scoped to the step that ran the suite --
    // so that deleting the declaration was a red build and not a silent
    // switch-off. CI was removed at the maintainer's decision that day and the
    // file it read no longer exists.
    //
    // IT WAS DELETED , NOT RE-POINTED, and the reason is this repository's
    // own rule that a search returning zero needs a positive control. The old
    // test had one: `steps.Count is 1` proved it had really found the step that
    // runs the suite before it concluded anything from a match or from a missing
    // one. A re-pointed version -- "no pipeline definition anywhere runs the
    // suite without declaring the pin" -- can have no positive control at all,
    // because there is nothing for it to find. It would pass over an empty
    // directory, over a typo in its own path, and over a pipeline written in a
    // shape it does not recognise, for as long as CI stays gone; and it would go
    // on passing on the day CI came back in a form it could not parse. That is a
    // green-when-blind test, which is the exact failure class the mechanism below
    // exists to remove.
    //
    // WHAT IS THEREFORE UNGUARDED, said plainly and not left to be
    // discovered: nothing sets BROWSERAI_EXPECTED_ABSENT anywhere in this
    // repository, so `EveryAbsentCapabilityIsOneThisRunsEnvironmentDeclared`
    // below asserts nothing on every run of the suite. The mechanism is intact,
    // exercised in-process by
    // `TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent`, and inert
    // until an environment declares something again. Restoring this arm against
    // whatever runs the suite next is part of the CI item in TODO.md.

    /// <summary>
    /// Every capability this run lacks is one its environment said it would
    /// lack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the live arm, and on a developer machine it asserts
    /// nothing</b> -- which is correct and not a gap. What is provisioned on
    /// somebody's laptop is a fact about their disk; a suite that pinned it would
    /// be red on every clean clone. <c>BROWSERAI_EXPECTED_ABSENT</c> is set by
    /// the environment that knows.
    /// </para>
    /// <para>
    /// ⚠️ <b>Since 2026-08-20 no environment sets it, so this arm asserts nothing
    /// on every run.</b> CI was removed that day at the maintainer's decision and
    /// it was the only thing that ever declared. The code is deliberately kept
    /// and not deleted: unset means <i>declares nothing</i>, which is already
    /// what a developer machine does, so it is correct and inert and ready for
    /// whatever runs the suite next. Its behaviour is held by
    /// <see cref="TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent"/>,
    /// which is in-process and pure and therefore unaffected.
    /// </para>
    /// <para>
    /// <b>What it closes:</b> <see cref="AReleaseRunExercisedEveryLayer"/> makes
    /// an absence *loud*, and loud is not the same as *noticed*. CI ran with
    /// two capabilities ABSENT from the day it existed, so two more going the
    /// same way changes nothing a reader would spot -- the run is green, the block
    /// says ABSENT four times instead of twice, and the tests that needed them
    /// skip. This is the assertion that tells those two states apart.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryAbsentCapabilityIsOneThisRunsEnvironmentDeclared()
    {
        var disagreements = SuiteEnvironment.ReconcileDeclaredAbsence(
            SuiteEnvironment.ExpectedAbsentDeclaration,
            SuiteEnvironment.All.Where(capability => !SuiteEnvironment.IsPresent(capability)));

        await Assert.That(string.Join(Environment.NewLine, disagreements))
            .IsEmpty()
            .Because(SuiteEnvironment.Summary());
    }

    /// <summary>
    /// A release run exercised every layer, and an ordinary run says which it
    /// did not.
    /// </summary>
    /// <remarks>
    /// <b>This is the item-8 check, moved from a paragraph telling a person to
    /// list two files by hand into the run itself.</b> Under
    /// <c>BROWSERAI_RELEASE_RUN=1</c> it fails naming everything absent; without
    /// it, it asserts the run is honest about what it skipped, which is what the
    /// other thirty-five guards now report.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AReleaseRunExercisedEveryLayer()
    {
        var absent = SuiteEnvironment.All
            .Where(capability => !SuiteEnvironment.IsPresent(capability))
            .Select(capability => capability.ToString())
            .ToList();

        if (SuiteEnvironment.IsReleaseRun)
        {
            await Assert.That(string.Join(", ", absent)).IsEmpty();
            return;
        }

        // Not a release run, so absence is permitted -- but never silent. The
        // block this run prints must say ABSENT for each one and must name the
        // variable that would have made it a failure, or a reader assembling
        // release evidence has nothing to read.
        var summary = SuiteEnvironment.Summary();

        if (absent.Count is not 0)
        {
            await Assert.That(summary).Contains("ABSENT");
        }

        await Assert.That(summary).Contains(SuiteEnvironment.ReleaseRunVariable);
    }

    /// <summary>
    /// Every gate driver declares the drive-letter spelling it forces, and the
    /// two shells force different ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The four drivers were recreated from prose every session until
    /// 2026-09-23, and once they were not there at all.</b> On 2026-09-22 at
    /// 19:26 a gate driver whose script had been wiped with the scratch folder
    /// died on its first line, in milliseconds, and read exactly like one that
    /// was working; fourteen minutes were lost waiting on it. They live in
    /// <c>build/</c> now, and this arm is what keeps a retyped one honest.
    /// </para>
    /// <para>
    /// <b>What is asserted is the pairing, not the spelling.</b>
    /// <see cref="TheRunReportsTheDriveLetterSpellingItActuallyReceived"/>
    /// already fails a run that did not receive what it declared -- but only for
    /// the run that is happening. This reads the drivers as text and holds that
    /// each one <i>forces</i> a case and <i>declares</i> the same case, and that
    /// the PowerShell pair and the Git Bash pair force different ones. A driver
    /// that declared <c>lower</c> while forcing upper would make every run it
    /// started red for a reason nobody could see from the run.
    /// </para>
    /// <para>
    /// <b>And the release halves are told from the ordinary ones by the variable
    /// and not by the file name</b>, because the difference that matters is
    /// what the run claims about itself: an ordinary run that set
    /// <c>BROWSERAI_RELEASE_RUN</c> would print <c>release run YES</c> in its own
    /// coverage block and be a release nobody cut.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-09-23</b> against a doctored driver that declared the
    /// spelling the other shell forces.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryGateDriverDeclaresTheDriveLetterSpellingItForces()
    {
        var offences = new List<string>();

        foreach (var driver in GateDrivers)
        {
            var text = await File.ReadAllTextAsync(Path.Combine(RepositoryLayout.Root.FullName, "build", driver.File));
            var declared = Declared(text);
            var forced = Forced(text, driver.File);

            if (declared is null)
            {
                offences.Add($"{driver.File}: declares no BROWSERAI_DRIVE_CASE, so a run it starts asserts nothing about the spelling it received");
                continue;
            }

            if (forced is null)
            {
                offences.Add($"{driver.File}: declares '{declared}' and forces nothing, so the spelling is whatever started the shell");
                continue;
            }

            if (!string.Equals(declared, forced, StringComparison.Ordinal))
            {
                offences.Add($"{driver.File}: forces '{forced}' and declares '{declared}'");
            }

            if (!string.Equals(forced, driver.Expected, StringComparison.Ordinal))
            {
                offences.Add($"{driver.File}: forces '{forced}' where this shell's half of the gate must force '{driver.Expected}', so the two halves would be one instrument run twice");
            }

            // ⚠️ AN ASSIGNMENT, NOT A MENTION. Every one of these files
            // EXPLAINS in prose whether it is a release half, so a Contains over
            // the text reports the ordinary halves as release ones -- which it
            // did, on the first run of this arm.
            var isRelease = ReleaseRunAssignment().IsMatch(text);

            if (isRelease != driver.Release)
            {
                offences.Add(driver.Release
                    ? $"{driver.File}: is the release half and never sets BROWSERAI_RELEASE_RUN, so a missing capability would skip where it must fail"
                    : $"{driver.File}: is the ordinary half and sets BROWSERAI_RELEASE_RUN, so its own coverage block would call an ordinary run a release");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offences)).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL, over the same two readers. A pattern that
        // stopped matching would report every driver silent and pass nothing,
        // which reads identically to four correct drivers.
        await Assert.That(Declared("$env:BROWSERAI_DRIVE_CASE = 'upper'")).IsEqualTo("upper");
        await Assert.That(Declared("BROWSERAI_DRIVE_CASE=lower dotnet test")).IsEqualTo("lower");
        await Assert.That(Declared("nothing here")).IsNull();

        await Assert.That(Forced("$x.Substring(0, 1).ToUpperInvariant()", "a.ps1")).IsEqualTo("upper");
        await Assert.That(Forced("$x.Substring(0, 1).ToLowerInvariant()", "a.ps1")).IsEqualTo("lower");
        await Assert.That(Forced("tr 'A-Z' 'a-z'", "a.sh")).IsEqualTo("lower");
        await Assert.That(Forced("tr 'a-z' 'A-Z'", "a.sh")).IsEqualTo("upper");
        await Assert.That(Forced("nothing here", "a.sh")).IsNull();

        await Assert.That(ReleaseRunAssignment().IsMatch("$env:BROWSERAI_RELEASE_RUN = '1'")).IsTrue();
        await Assert.That(ReleaseRunAssignment().IsMatch("BROWSERAI_RELEASE_RUN=1 dotnet test")).IsTrue();
        await Assert.That(ReleaseRunAssignment().IsMatch("BROWSERAI_RELEASE_RUN is deliberately NOT set here")).IsFalse();

        // And the corpus is not empty, which is the other way this could pass
        // while checking nothing.
        await Assert.That(GateDrivers.Length).IsEqualTo(4);

        foreach (var driver in GateDrivers)
        {
            await Assert.That(File.Exists(Path.Combine(RepositoryLayout.Root.FullName, "build", driver.File)))
                .IsTrue()
                .Because($"{driver.File} is one half of a gate and a gate recreated from prose is what this arm exists to end");
        }

        // The clearance snapshot is the fifth file and is shared by all four, so
        // it carries no spelling of its own and is asserted present, not
        // read for one.
        await Assert.That(File.Exists(Path.Combine(RepositoryLayout.Root.FullName, "build", "Get-ClearanceSnapshot.ps1"))).IsTrue();
    }

    /// <summary>
    /// The clearance snapshot reads the client's registration out of the client's
    /// own file, and never starts the client to ask.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Q281, decided 2026-09-24 by the maintainer, verbatim: "Q281 a".</b> The
    /// question put to him: the snapshot's third reading ran
    /// <c>claude mcp get browserai</c>, which starts the client -- and the client
    /// health-checks the server it names -- and may write <c>~/.claude.json</c>,
    /// the file the gate exists to prove untouched; every gate had run it. Direction
    /// (a) was a read-only parse of the <c>browserai</c> entry, which answers the
    /// same question without starting anything, and it is the reading
    /// permanently.
    /// </para>
    /// <para>
    /// <b>Read as code, comments blanked</b>, so the script may say why it no longer
    /// asks the client. <b>Planted red 2026-09-24</b> against the script as it
    /// stood, which this arm named for starting the client and for reading no
    /// file.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClearanceSnapshotReadsTheRegistrationWithoutStartingTheClient()
    {
        var script = Path.Combine(RepositoryLayout.Root.FullName, "build", "Get-ClearanceSnapshot.ps1");
        var code = CodeOf(await File.ReadAllTextAsync(script), ".ps1");

        await Assert.That(string.Join(Environment.NewLine, ClearanceOffences(code))).IsEmpty();

        // ⚠️ THE POSITIVE CONTROL, both directions, through the same reader: the
        // old reading is caught twice, the new one not at all, and a comment
        // about the client is not a call to it.
        await Assert.That(ClearanceOffences(CodeOf("$out += ((claude mcp get browserai 2>&1 | Out-String))", ".ps1")).Count).IsEqualTo(2);
        await Assert.That(ClearanceOffences(CodeOf("$j = $text | ConvertFrom-Json -AsHashtable # the entry in ~/.claude.json\n$f = '.claude.json'", ".ps1"))).IsEmpty();
        await Assert.That(ClearanceOffences(CodeOf("# never claude mcp get\n$f = '.claude.json'; $j = $t | ConvertFrom-Json -AsHashtable", ".ps1"))).IsEmpty();
    }

    /// <summary>
    /// The clearance compares each client's BrowserAI entry and nothing else in
    /// that client's file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Q292, decided 2026-09-24 by the maintainer, verbatim: <i>"Q292 a - Same
    /// for claude code"</i>.</b> The Codex reading hashed the whole of
    /// <c>~\.codex\config.toml</c>, and the Codex desktop app rewrites that file when
    /// it starts: a gate spanning a Codex start stopped on a difference that had
    /// nothing to do with BrowserAI. The rule for both clients is now the entry:
    /// <c>mcpServers.browserai</c> in <c>~/.claude.json</c>, and the
    /// <c>[mcp_servers.browserai]</c> table in <c>config.toml</c>.
    /// </para>
    /// <para>
    /// <b>Driven, not read.</b> The script runs for real under a scratch profile --
    /// <c>USERPROFILE</c> and <c>APPDATA</c> moved for that child alone -- whose two
    /// files are rewritten around the entry between two snapshots, and then in the
    /// entry itself. Only the two registration readings are compared, so a real
    /// Add/Remove key changing under the run cannot redden it. <b>Planted red
    /// 2026-09-24</b> against the script as it stood, which reported the rewritten
    /// <c>config.toml</c> as a difference.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheClearanceComparesOnlyEachClientsBrowserAiEntry()
    {
        using var profile = ScratchDirectory.Create("clearance-q292");

        var claude = Path.Combine(profile.Path, ".claude.json");
        var codex = Path.Combine(Directory.CreateDirectory(Path.Combine(profile.Path, ".codex")).FullName, "config.toml");

        // Forward slashes, so the reading can be searched for the text as written:
        // ConvertTo-Json would double a backslash in the Claude Code half.
        const string Ours = "C:/x/current/BrowserAI.Server.exe";
        const string Moved = "C:/y/current/BrowserAI.Server.exe";

        await File.WriteAllTextAsync(claude, ClaudeConfiguration(Ours, other: "one", startups: 1));
        await File.WriteAllTextAsync(codex, CodexConfiguration(Ours, other: "one", model: "a", appended: false));

        var before = await RegistrationReadingsAsync(profile.Path);

        // The entry is what is read, verbatim, from each file.
        await Assert.That(before.Count(line => line.Contains(Ours, StringComparison.Ordinal))).IsEqualTo(2);

        // ---- Everything AROUND the entry moves, in both files.
        await File.WriteAllTextAsync(claude, ClaudeConfiguration(Ours, other: "two", startups: 2));
        await File.WriteAllTextAsync(codex, CodexConfiguration(Ours, other: "two", model: "b", appended: true));

        var around = await RegistrationReadingsAsync(profile.Path);

        await Assert.That(string.Join("\n", around))
            .IsEqualTo(string.Join("\n", before))
            .Because("a client rewriting its own file around the BrowserAI entry is not a change a run made -- Q292 a");

        // ---- THE CONTROL, both clients: the entry itself moving IS a difference.
        await File.WriteAllTextAsync(codex, CodexConfiguration(Moved, other: "two", model: "b", appended: true));

        var codexMoved = await RegistrationReadingsAsync(profile.Path);

        await Assert.That(string.Join("\n", codexMoved)).IsNotEqualTo(string.Join("\n", around));
        await Assert.That(codexMoved.Any(line => line.Contains(Moved, StringComparison.Ordinal))).IsTrue();

        await File.WriteAllTextAsync(claude, ClaudeConfiguration(Moved, other: "two", startups: 2));

        var claudeMoved = await RegistrationReadingsAsync(profile.Path);

        await Assert.That(string.Join("\n", claudeMoved)).IsNotEqualTo(string.Join("\n", codexMoved));
        await Assert.That(claudeMoved.Count(line => line.Contains(Moved, StringComparison.Ordinal))).IsEqualTo(2);

        // ---- And an entry that is not there says so, in each client.
        await File.WriteAllTextAsync(claude, """{ "mcpServers": { "other": { "command": "c:/other.exe" } } }""");
        await File.WriteAllTextAsync(codex, "[mcp_servers.other]\ncommand = 'c:/other.exe'\n");

        var absent = await RegistrationReadingsAsync(profile.Path);

        await Assert.That(absent.Count(line => line.Contains("ABSENT", StringComparison.Ordinal))).IsEqualTo(2);
    }

    /// <summary>A <c>.claude.json</c> carrying a BrowserAI entry among other things.</summary>
    private static string ClaudeConfiguration(string command, string other, int startups) =>
        $$"""
        {
          "numStartups": {{startups}},
          "mcpServers": {
            "other": { "command": "c:/{{other}}.exe", "args": [] },
            "browserai": { "type": "stdio", "command": {{System.Text.Json.JsonSerializer.Serialize(command)}}, "args": [], "env": {} }
          },
          "projects": { "c:/somewhere": { "allowedTools": [ "{{other}}" ] } }
        }
        """;

    /// <summary>A <c>config.toml</c> carrying a BrowserAI table among other tables.</summary>
    private static string CodexConfiguration(string command, string other, string model, bool appended) =>
        $"model = \"{model}\"\n\n"
        + $"[mcp_servers.other]\ncommand = 'c:/{other}.exe'\n\n"
        + $"[mcp_servers.browserai]\ncommand = '{command}'\nargs = []\n\n"
        + "[mcp_servers.browserai.env]\nK = 'V'\n\n"
        + "[projects.'c:/somewhere']\ntrust_level = \"trusted\"\n"
        + (appended ? $"\n[notice]\nhide_{other} = true\n" : string.Empty);

    /// <summary>
    /// Runs the clearance script against a scratch profile and returns its two
    /// registration readings and nothing else.
    /// </summary>
    /// <param name="profile">The directory the child sees as its profile.</param>
    /// <returns>The lines of the two readings, in order.</returns>
    private static async Task<List<string>> RegistrationReadingsAsync(string profile)
    {
        var tag = $"suite-q292-{Guid.NewGuid():N}";
        var snapshot = Path.Combine(RepositoryLayout.Root.FullName, ".work", "clearance", $"{tag}.txt");

        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // The house rule for every launch in this tree: redirecting the
            // streams does not suppress the console.
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(RepositoryLayout.Root.FullName, "build", "Get-ClearanceSnapshot.ps1"));
        start.ArgumentList.Add("-Tag");
        start.ArgumentList.Add(tag);

        // The child's own block, so this process's environment is untouched.
        start.Environment["USERPROFILE"] = profile;
        start.Environment["APPDATA"] = Path.Combine(profile, "AppData", "Roaming");

        using (var process = Process.Start(start) ?? throw new InvalidOperationException("'pwsh' did not start for the clearance script."))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync().WaitAsync(TestDefaults.ProcessHang);
            _ = await output;
            _ = await error;
        }

        try
        {
            return RegistrationReadingsIn(await File.ReadAllLinesAsync(snapshot));
        }
        finally
        {
            File.Delete(snapshot);
        }
    }

    /// <summary>The two registration readings out of a whole snapshot.</summary>
    /// <remarks>
    /// <b>Each starts at a line that is not indented and names the client's file,
    /// and runs through the indented lines under it.</b> The shape the script had
    /// before Q292 -- one unindented line for Codex carrying the whole file's hash --
    /// is read by the same rule, which is what let this arm be planted red against it.
    /// </remarks>
    /// <param name="lines">The snapshot.</param>
    /// <returns>The readings' lines.</returns>
    private static List<string> RegistrationReadingsIn(IEnumerable<string> lines)
    {
        var kept = new List<string>();
        var inside = false;

        foreach (var line in lines)
        {
            if (!line.StartsWith(' '))
            {
                inside = line.Contains(".claude.json", StringComparison.Ordinal)
                    || line.Contains("config.toml", StringComparison.Ordinal);
            }

            if (inside)
            {
                kept.Add(line);
            }
        }

        return kept;
    }

    /// <summary>What is wrong with a clearance script's registration reading.</summary>
    /// <param name="code">The script, comments blanked.</param>
    /// <returns>One complaint per fault.</returns>
    private static List<string> ClearanceOffences(string code)
    {
        var offences = new List<string>();

        if (ClientInvocation().IsMatch(code))
        {
            offences.Add("it starts the client to read the registration, and the client can write ~/.claude.json while it answers -- Q281 a");
        }

        if (!code.Contains(".claude.json", StringComparison.Ordinal) || !code.Contains("ConvertFrom-Json", StringComparison.OrdinalIgnoreCase))
        {
            offences.Add("it does not read the browserai entry out of ~/.claude.json with a parse, which is the reading Q281 a made permanent");
        }

        return offences;
    }

    /// <summary>A script's text with its comments blanked, line structure kept.</summary>
    /// <param name="text">The script.</param>
    /// <param name="suffix">Its extension, with the dot.</param>
    /// <returns>The code.</returns>
    private static string CodeOf(string text, string suffix)
    {
        var characters = text.ToCharArray();

        foreach (var (start, end) in Commentary.SpansOf(text, suffix))
        {
            for (var at = start; at < end; at++)
            {
                if (characters[at] is not '\n')
                {
                    characters[at] = ' ';
                }
            }
        }

        return new string(characters);
    }

    /// <summary>A call to the Claude Code client's MCP verbs.</summary>
    [GeneratedRegex(@"\bclaude(?:\.exe)?\s+mcp\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClientInvocation();

    /// <summary>The four gate drivers, what each must force, and whether it is a release half.</summary>
    private static (string File, string Expected, bool Release)[] GateDrivers { get; } =
    [
        ("Invoke-OrdinaryGate.ps1", "upper", false),
        ("Invoke-ReleaseGate.ps1", "upper", true),
        ("invoke-ordinary-gate.sh", "lower", false),
        ("invoke-release-gate.sh", "lower", true),
    ];

    /// <summary>What a driver declares in <c>BROWSERAI_DRIVE_CASE</c>, or <see langword="null"/>.</summary>
    /// <param name="text">The driver's text.</param>
    /// <returns><c>upper</c>, <c>lower</c>, or <see langword="null"/> when it declares nothing.</returns>
    private static string? Declared(string text)
    {
        var match = DriveCaseDeclaration().Match(text);

        return match.Success ? match.Groups["case"].Value : null;
    }

    /// <summary>
    /// Which case a driver forces onto the drive letter, read from the one
    /// expression that does it in each language.
    /// </summary>
    /// <remarks>
    /// <b>Two spellings because two languages, and neither is a guess.</b> A
    /// PowerShell half re-cases the first character with
    /// <c>ToUpperInvariant</c>; a Git Bash half pipes it through <c>tr</c>. The
    /// direction is read off the expression and not off the file name, so a
    /// driver that was copied from the other shell and half-edited is caught.
    /// </remarks>
    /// <param name="text">The driver's text.</param>
    /// <param name="file">Its name, which chooses the language.</param>
    /// <returns><c>upper</c>, <c>lower</c>, or <see langword="null"/> when it forces nothing.</returns>
    private static string? Forced(string text, string file)
    {
        if (file.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            var match = ShellCaseForcing().Match(text);

            return match.Success
                ? match.Groups["from"].Value.StartsWith('A') ? "lower" : "upper"
                : null;
        }

        var powershell = PowerShellCaseForcing().Match(text);

        // Mapped and not lower-cased: CA1308 bans ToLowerInvariant, and a
        // two-value map is clearer than a normalisation anyway.
        return powershell.Success
            ? powershell.Groups["case"].Value is "Upper" ? "upper" : "lower"
            : null;
    }

    /// <summary>The declaration, in either shell's assignment syntax.</summary>
    [GeneratedRegex(@"BROWSERAI_DRIVE_CASE\s*=\s*'?(?<case>upper|lower)'?")]
    private static partial Regex DriveCaseDeclaration();

    /// <summary>PowerShell's re-casing of the drive letter.</summary>
    [GeneratedRegex(@"To(?<case>Upper|Lower)Invariant\(\)")]
    private static partial Regex PowerShellCaseForcing();

    /// <summary>An assignment of the release variable, in either shell's syntax.</summary>
    [GeneratedRegex(@"(?:\$env:)?BROWSERAI_RELEASE_RUN\s*=\s*'?1'?")]
    private static partial Regex ReleaseRunAssignment();

    /// <summary>A <c>tr</c> that re-cases the drive letter, with the direction it maps from.</summary>
    [GeneratedRegex(@"tr\s+'(?<from>A-Z|a-z)'\s+'(?:a-z|A-Z)'")]
    private static partial Regex ShellCaseForcing();
}
