// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;
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
    /// <summary>The block's own copy on disk, for whoever assembles the evidence.</summary>
    public static string ReportPath { get; } =
        Path.Combine(RepositoryLayout.Root.FullName, ".work", "suite-coverage.txt");

    /// <summary>
    /// Takes the filter reading before anything else runs.
    /// </summary>
    /// <remarks>
    /// <b>Here rather than lazily on first use, and the timing is the honesty.</b>
    /// <c>TUnitTestFramework.ExecuteRequestAsync</c> assigns
    /// <c>GlobalContext.Current</c> and <c>TestSessionContext.Current</c> before
    /// it runs a single hook, so a reading taken here is taken after the only
    /// event that could populate them — and a null filter read before that point
    /// is indistinguishable from a run that really had none, which is the false
    /// green <see cref="SuiteFilter"/> exists to prevent.
    /// </remarks>
    [Before(TestSession)]
    public static void ReadWhetherThisRunWasFiltered() => SuiteFilter.Take();

    /// <summary>Writes the coverage block at the end of the session.</summary>
    [After(TestSession)]
    public static void ReportWhatThisRunExercised()
    {
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
        // over its own channel rather than forwarding its console. The gate runs
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
    }
}

/// <summary>
/// The gate that makes a degraded run distinguishable from a real one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every assertion here is about the mechanism, not about the machine.</b>
/// The suite must stay runnable on a clean clone, so nothing below requires a
/// browser — what is required is that a run without one cannot report the same
/// summary as a run with one.
/// </para>
/// <para>
/// <b>The release branch is exercised in every ordinary run.</b>
/// <see cref="SuiteEnvironment.Decide"/> is a pure function of the two inputs
/// precisely so that <c>BROWSERAI_RELEASE_RUN</c> is not a code path that only
/// runs on release day — a mechanism nobody exercises until it matters is the
/// same defect as the one this file closes.
/// </para>
/// </remarks>
internal sealed class SuiteCoverageTests
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
    /// contents are asserted rather than trusted.
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

        // Not a capability either, and the only row here that reports what the
        // SHELL handed this run rather than what the machine holds.
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
    /// declares nothing, so the live arm asserts nothing there — and a check that
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
    /// forcing that did not take fails here rather than passing quietly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gate's reason for running two shells is that they are two
    /// instruments, and until 2026-08-24 nothing measured that.</b> All six runs
    /// of that day's gate received <c>C:</c> — three of them silently duplicating
    /// the other three — and the run summary, the coverage block and the release
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
    /// A filtered run is told from a full one, from one that could not tell, and
    /// from a broken instrument — and only one of the four costs a release.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The positive control under the live arms below</b>, and it is mandatory
    /// rather than thorough. A gate run is never filtered, so
    /// <see cref="SuiteFilter.Verdict"/> reads <c>FULL RUN</c> on every run this
    /// repository takes — and a reading that can only ever come back empty is
    /// indistinguishable from one that cannot read. This drives all four states
    /// and both modes in-process, on every ordinary run.
    /// </para>
    /// <para>
    /// <b><see cref="SuiteFilterVerdict.Unread"/> is the state this exists
    /// for.</b> <see cref="GlobalContext"/> lazily creates an empty instance for
    /// whoever asks first, so a filter read before the framework populated
    /// anything is <see langword="null"/> — identical to a run that really had
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
    /// platform rather than from its own command line.
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
    /// inside a test rather than inside the session hook, so a latch that had
    /// gone stale or had been taken from somewhere else shows up as a
    /// disagreement rather than as a row nobody checked.
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
    /// A run that was filtered is not a release, and a full one is not refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Decision (d), and this method is also the probe a filtered child
    /// run executes</b> — see
    /// <see cref="AFilteredChildRunReadsAsFilteredAndIsRefusedAsARelease"/>. It
    /// is one method rather than two because the refusal has to fire in
    /// something the child actually runs, and the child runs exactly one thing.
    /// </para>
    /// <para>
    /// <b>On the gate this asserts the negative direction live</b>: an
    /// unfiltered ordinary run is not refused. The positive direction cannot be
    /// arranged in-process — a test cannot filter the run it is inside — so it
    /// is arranged out of process instead.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARunThatWasFilteredIsNeverARelease()
    {
        // The child writes what it read before it fails, because a process that
        // failed the way it was meant to still has to be readable.
        if (Environment.GetEnvironmentVariable(SuiteFilter.ProbeVariable) is { Length: > 0 } report)
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            await File.WriteAllTextAsync(report, SuiteFilter.Describe(SuiteFilter.Reading, SuiteEnvironment.IsReleaseRun));
        }

        await Assert.That(SuiteFilter.Decision)
            .IsNotEqualTo(SuiteFilterDecision.Refuse)
            .Because(SuiteFilter.Refusal(SuiteFilter.Reading, SuiteEnvironment.IsReleaseRun));
    }

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
    /// <b>It carries the release half too.</b> The child is handed
    /// <c>BROWSERAI_RELEASE_RUN=1</c> deliberately, so the one test it runs meets
    /// exactly the state decision (d) forbids — filtered <i>and</i> claiming to
    /// be a release — and must fail. The other direction is this very run:
    /// unfiltered, ordinary, and green.
    /// </para>
    /// <para>
    /// <b>Recursion is impossible by construction and guarded anyway.</b> The
    /// child's filter names <see cref="ARunThatWasFilteredIsNeverARelease"/> and
    /// nothing else, so it can never select this method; and
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
        // assertion below unreachable rather than false.
        await Assert.That(File.Exists(host)).IsTrue().Because(host);

        using var scratch = ScratchDirectory.Create("filter-probe");
        var report = Path.Combine(scratch.Path, "filter.txt");
        const string Filter = "/*/*/SuiteCoverageTests/" + nameof(ARunThatWasFilteredIsNeverARelease);

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

        // The diagnostic beside the verdict, asserted to be present rather than
        // to have a value: it records whether the child's OWN command line
        // carried the filter, which is the question the verdict deliberately
        // does not answer from. A field nobody reads is a field that can rot.
        await Assert.That(described).Contains("commandLineCarriesIt=");

        // ⚠️ AND THE REFUSAL FIRED. A child that read FILTERED and passed anyway
        // would leave decision (d) written and never taken.
        await Assert.That(exitCode).IsNotEqualTo(0).Because(console);
        await Assert.That(console).Contains(SuiteEnvironment.ReleaseRunVariable).Because(console);

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
    /// needed it — which is the same dead-mechanism defect as a release branch
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
        // against a run that lacks half of everything rather than against none.
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
        // declares — and is not the same thing as declaring nothing.
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

        // Set to something that names nothing. Loud rather than silently
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
    // so that deleting the declaration was a red build rather than a silent
    // switch-off. CI was removed at the maintainer's decision that day and the
    // file it read no longer exists.
    //
    // IT WAS DELETED RATHER THAN RE-POINTED, and the reason is this repository's
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
    // WHAT IS THEREFORE UNGUARDED, said plainly rather than left to be
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
    /// nothing</b> — which is correct rather than a gap. What is provisioned on
    /// somebody's laptop is a fact about their disk; a suite that pinned it would
    /// be red on every clean clone. <c>BROWSERAI_EXPECTED_ABSENT</c> is set by
    /// the environment that knows.
    /// </para>
    /// <para>
    /// ⚠️ <b>Since 2026-08-20 no environment sets it, so this arm asserts nothing
    /// on every run.</b> CI was removed that day at the maintainer's decision and
    /// it was the only thing that ever declared. The code is deliberately kept
    /// rather than deleted: unset means <i>declares nothing</i>, which is already
    /// what a developer machine does, so it is correct and inert and ready for
    /// whatever runs the suite next. Its behaviour is held by
    /// <see cref="TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent"/>,
    /// which is in-process and pure and therefore unaffected.
    /// </para>
    /// <para>
    /// <b>What it closes:</b> <see cref="AReleaseRunExercisedEveryLayer"/> makes
    /// an absence *loud*, and loud is not the same as *noticed*. CI ran with
    /// two capabilities ABSENT from the day it existed, so two more going the
    /// same way changes nothing a reader would spot — the run is green, the block
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
}
