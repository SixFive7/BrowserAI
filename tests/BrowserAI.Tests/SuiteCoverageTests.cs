// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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
    /// <summary>The block's own copy on disk, for whoever assembles the evidence.</summary>
    public static string ReportPath { get; } =
        Path.Combine(RepositoryLayout.Root.FullName, ".work", "suite-coverage.txt");

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

    /// <summary>
    /// The workflow still declares what it expects to be absent, on the step
    /// that runs the suite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Without this, the pin is one deleted line from being inert and nothing
    /// would say so.</b> The live arm below asserts nothing at all when
    /// <c>BROWSERAI_EXPECTED_ABSENT</c> is unset — that is deliberate, because a
    /// developer machine must keep behaving as it did — which means removing the
    /// variable from [`build.yml`](../../.github/workflows/build.yml) turns the
    /// mechanism off and leaves every test green. That is the same shape as the
    /// thing being closed: a capability disappearing without a reader.
    /// </para>
    /// <para>
    /// <b>Scoped to the step that runs the suite</b>, not to the file, because a
    /// declaration on the publish step is a declaration the test process never
    /// sees — inert in exactly the way this test exists to catch, while reading
    /// as present to a file-wide search. The value is parsed through
    /// <see cref="SuiteEnvironment.ReadDeclaration"/>, the same routine the live
    /// arm uses, so a typo committed to the workflow is red here rather than on
    /// the next CI run.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheWorkflowStillDeclaresWhatItExpectsToBeAbsent()
    {
        var workflow = Path.Combine(RepositoryLayout.Root.FullName, ".github", "workflows", "build.yml");

        await Assert.That(File.Exists(workflow)).IsTrue().Because(workflow);

        var yaml = await File.ReadAllTextAsync(workflow);

        // The positive control, before anything is concluded from a match or
        // from its absence: this really is the file that runs the suite. A scan
        // of the wrong path would otherwise report a missing declaration and a
        // scan of a renamed step would report a healthy one.
        var steps = StepBoundary().Split(yaml).Where(step => step.Contains("run: dotnet test", StringComparison.Ordinal)).ToList();

        await Assert.That(steps.Count).IsEqualTo(1).Because(workflow);

        var declaration = ExpectedAbsentDeclaration().Match(steps[0]);

        await Assert.That(declaration.Success)
            .IsTrue()
            .Because(
                $"'{workflow}' runs the suite without setting {SuiteEnvironment.ExpectedAbsentVariable}, so nothing pins which capabilities CI expects to be absent and a fifth going missing would read as normal.");

        var value = declaration.Groups["value"].Value;
        var (_, problems) = SuiteEnvironment.ReadDeclaration(value);

        await Assert.That(string.Join(Environment.NewLine, problems)).IsEmpty().Because(value);
    }

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
    /// the environment that knows, and [`build.yml`](../../.github/workflows/build.yml)
    /// is the one that does.
    /// </para>
    /// <para>
    /// <b>What it closes:</b> <see cref="AReleaseRunExercisedEveryLayer"/> makes
    /// an absence *loud*, and loud is not the same as *noticed*. CI has run with
    /// two capabilities ABSENT since the day it existed, so two more going the
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

    /// <summary>The start of a step in the workflow's step list.</summary>
    /// <remarks>
    /// Six spaces and a dash is where a step begins under <c>jobs.windows.steps</c>,
    /// and splitting on it is what scopes the search to one step rather than to
    /// the whole file.
    /// </remarks>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^      - name: ", RegexOptions.Multiline)]
    private static partial Regex StepBoundary();

    /// <summary>The declaration, as it is written in the workflow.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^[ \t]*BROWSERAI_EXPECTED_ABSENT:[ \t]*(?<value>[^ \t\r\n][^\r\n]*?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex ExpectedAbsentDeclaration();
}
