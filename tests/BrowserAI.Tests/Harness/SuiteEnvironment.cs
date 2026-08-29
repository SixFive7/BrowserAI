// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using BrowserAI.Registration;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The things a run either has or does not have, named so that a run that did
/// not have one cannot report the same summary as a run that did.
/// </summary>
/// <remarks>
/// ⚠️ <b>Deliberately not counted here.</b> This summary said "the four things"
/// while the enum held six, and then seven; a count in a sentence beside the
/// list it counts is a stale number waiting to happen, and nothing reads it.
/// </remarks>
internal enum SuiteCapability
{
    /// <summary>The NativeAOT publish, with a payload beside it.</summary>
    PublishedSlice,

    /// <summary>The assembled <c>payload/</c> tree at the repository root.</summary>
    RepositoryPayload,

    /// <summary>A provisioned Chromium under the browsers root.</summary>
    ProvisionedChromium,

    /// <summary>A provisioned Firefox under the browsers root.</summary>
    ProvisionedFirefox,

    /// <summary>A packed <c>.nupkg</c> under <c>Releases/</c>.</summary>
    PackagedRelease,

    /// <summary>
    /// The MCP client's command line on this machine, which is the only thing
    /// that can answer whether BrowserAI's registration works.
    /// </summary>
    /// <remarks>
    /// <b>It is a capability rather than an assumption because the product's
    /// registration reads upstream's English.</b> Every failure the client has
    /// exits 1, benign or not, so <i>"already exists"</i> and <i>"No MCP server
    /// named"</i> are the only discriminators there are — and a run that cannot
    /// start the client cannot notice either of them moving. Absent, the
    /// real-client arms skip; under <c>BROWSERAI_RELEASE_RUN=1</c> they fail,
    /// which is correct: a release that cannot demonstrate its own registration
    /// is one whose founding promise is untested.
    /// </remarks>
    ClientCommandLine,

    /// <summary>
    /// A git that can answer questions about the tree this run is reading.
    /// </summary>
    /// <remarks>
    /// <b>A capability rather than an assumption because the suite must run on
    /// an export that has none.</b> Every tree-as-text rule in this repository —
    /// the SPDX header, the link scan, the fragment count, never-by-image-name —
    /// reads the corpus <see cref="RepositoryLayout"/>'s walk produces, and the
    /// only second opinion about that corpus is <c>git ls-files</c>. Absent, the
    /// arm that compares them skips loudly and the block below says so; under
    /// <c>BROWSERAI_RELEASE_RUN=1</c> it fails, which is correct — a release cut
    /// from a run that could not check its own corpus is a release whose every
    /// tree scan is unverified.
    /// </remarks>
    Git,
}

/// <summary>
/// What this run of the suite can actually exercise, and what it must do when it
/// cannot exercise something.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because a degraded run used to be indistinguishable from a real
/// one, which is this project's founding failure class inside its own release
/// gate.</b> Measured 2026-08-16, on the tree at <c>c21fea7</c>, by moving the
/// whole publish directory aside and running the suite: <b>329 total, 328
/// succeeded, 1 skipped, exit 0</b> — the same four numbers as a run that
/// launched a real browser. Thirty-five guards across thirteen files returned
/// early after asserting something weaker, and every one of them reported as a
/// pass.
/// </para>
/// <para>
/// <b>The subject of that defect is the published slice, not the payload.</b>
/// Measured the same way in the same session: with <c>payload/</c> moved aside
/// the suite reports <b>80 failures</b>, because the fake-child and tool-surface
/// layers need <c>node.exe</c> and are not guarded at all. The payload's absence
/// was already loud; the publish's absence was silent. Both are gated here
/// anyway, because the guards exist for both and a guard nobody accounts for is
/// how the first one got missed.
/// </para>
/// <para>
/// <b>Three behaviours, and which one fires is the whole design.</b>
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>An ordinary run skips, loudly.</b> <see cref="Skip.Test(string)"/> makes
/// the test report as <i>skipped</i> rather than as <i>passed</i>, so the run's
/// own summary carries a skipped count that a healthy run does not — and
/// [release checklist item 8](../../../RELEASING.md) already requires that count
/// to be zero. A clean clone can still run the suite, which is the property the
/// early returns existed to preserve.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>A release run fails.</b> With <c>BROWSERAI_RELEASE_RUN=1</c> set there is
/// no skip: the missing capability is a red test naming the command that
/// produces it. That is the mechanical form of the paragraph of instructions
/// item 8 used to carry.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>A partial installation always fails, in either mode.</b> A publish
/// directory that exists without a binary in it, or a <c>payload/</c> holding no
/// <c>payload.json</c>, is a defect rather than a clean clone. This is the
/// distinction the old per-site <c>IsAbsentAsAWhole</c> assertion drew, kept
/// exactly.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>Nothing here reads a test's duration.</b> The rig shares one
/// <see cref="SliceRun"/>, so a slice test that really did assert against a live
/// browser can take 2.6 ms — measured 2026-08-16. A duration threshold would be
/// a second false green wearing the clothes of a fix.
/// </para>
/// </remarks>
internal static class SuiteEnvironment
{
    /// <summary>The variable a release run sets, turning every skip into a failure.</summary>
    public const string ReleaseRunVariable = "BROWSERAI_RELEASE_RUN";

    /// <summary>
    /// The variable a controlled environment sets to declare which capabilities
    /// it expects to be absent. An absence it did not declare is a red build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this closes, and why the policy above could not.</b>
    /// <see cref="Decide"/> pins what a guard must <i>do</i> about an absent
    /// capability, and <see cref="SuiteCoverageTests.NothingThisRunLacksIsHalfInstalled"/>
    /// pins that nothing is half-installed — but <b>nothing pinned WHICH
    /// capabilities are expected to be absent</b>. So a fifth quietly going
    /// absent in CI reads exactly like the four that are absent by design: the
    /// run stays green, the block says ABSENT, and the tests that needed it skip
    /// loudly into a log nobody reads line by line. That is this repository's
    /// founding failure shape — a degraded run indistinguishable from a real one
    /// — surviving one layer above the gate written to remove it.
    /// </para>
    /// <para>
    /// <b>The set is only knowable in a controlled environment, so the
    /// environment declares it.</b> A developer machine declares nothing and
    /// behaves exactly as it did before this existed: what is provisioned there
    /// is whatever that developer happens to have, and a suite that asserted a
    /// set would be asserting a fact about somebody's disk. A controlled
    /// environment knows, because it builds the machine it runs on.
    /// </para>
    /// <para>
    /// ⚠️ <b>Nothing sets it as of 2026-08-20, and that is a removal rather than
    /// a defect.</b> Hosted CI was this variable's only consumer — it named
    /// <c>PackagedRelease,ClientCommandLine</c> on the step that ran the suite —
    /// and CI was removed that day at the maintainer's decision. Unset means
    /// <i>declares nothing</i>, which is already the developer-machine
    /// behaviour, so the mechanism below is correct, inert, and ready for
    /// whatever environment runs the suite next. It is deliberately kept rather
    /// than deleted; re-declaring is one environment variable.
    /// </para>
    /// <para>
    /// <b><c>none</c> is a value rather than an omission, and that is
    /// load-bearing twice.</b> Windows cannot carry an empty environment variable
    /// — <c>$env:X = ''</c> removes it — so <i>"declared, and nothing is expected
    /// absent"</i> is inexpressible as an empty string and would collapse into
    /// <i>"not declared"</i>, which is the one value that switches the pin off.
    /// It is also what let the fault be planted end to end rather than only in
    /// the pure function: with everything present locally, <c>none</c> is the
    /// declaration a real run can be made red against by making one capability
    /// absent.
    /// </para>
    /// </remarks>
    public const string ExpectedAbsentVariable = "BROWSERAI_EXPECTED_ABSENT";

    /// <summary>The declaration that says nothing is expected to be absent.</summary>
    public const string NothingExpectedAbsent = "none";

    /// <summary>The variable that names the packed <c>.nupkg</c> to read notices out of.</summary>
    public const string ReleasePackageVariable = "BROWSERAI_RELEASE_PACKAGE";

    private static readonly ConcurrentDictionary<SuiteCapability, ConcurrentDictionary<string, byte>> Degradations = new();

    /// <summary>
    /// Whether this run is a release run, in which a missing capability is a
    /// failure rather than a skip.
    /// </summary>
    public static bool IsReleaseRun { get; } =
        Environment.GetEnvironmentVariable(ReleaseRunVariable) is { } value
        && (value.Equals("1", StringComparison.Ordinal) || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>The published NativeAOT binary and the payload beside it, or a skip.</summary>
    /// <param name="test">The calling test, filled in by the compiler.</param>
    public static void RequirePublishedSlice([CallerMemberName] string test = "") =>
        Require(SuiteCapability.PublishedSlice, test);

    /// <summary>The assembled <c>payload/</c> tree at the repository root, or a skip.</summary>
    /// <param name="test">The calling test, filled in by the compiler.</param>
    public static void RequireRepositoryPayload([CallerMemberName] string test = "") =>
        Require(SuiteCapability.RepositoryPayload, test);

    /// <summary>A provisioned Chromium, and the payload that drives it, or a skip.</summary>
    /// <param name="test">The calling test, filled in by the compiler.</param>
    public static void RequireProvisionedChromium([CallerMemberName] string test = "")
    {
        Require(SuiteCapability.RepositoryPayload, test);
        Require(SuiteCapability.ProvisionedChromium, test);
    }

    /// <summary>A provisioned Firefox, and the payload that drives it, or a skip.</summary>
    /// <param name="test">The calling test, filled in by the compiler.</param>
    public static void RequireProvisionedFirefox([CallerMemberName] string test = "")
    {
        Require(SuiteCapability.RepositoryPayload, test);
        Require(SuiteCapability.ProvisionedFirefox, test);
    }

    /// <summary>The packed <c>.nupkg</c> this run can read notices out of, or a skip.</summary>
    /// <param name="test">The calling test, filled in by the compiler.</param>
    /// <returns>The package's path.</returns>
    public static string RequirePackagedRelease([CallerMemberName] string test = "")
    {
        Require(SuiteCapability.PackagedRelease, test);
        return PackagedRelease()!;
    }

    /// <summary>A git that can answer about this tree, or a skip.</summary>
    /// <param name="test">The calling test, filled in by the compiler.</param>
    public static void RequireGit([CallerMemberName] string test = "") =>
        Require(SuiteCapability.Git, test);

    /// <summary>The MCP client's own command line, or a skip.</summary>
    /// <param name="test">The calling test, filled in by the compiler.</param>
    /// <returns>The client executable's absolute path.</returns>
    public static string RequireClientCommandLine([CallerMemberName] string test = "")
    {
        Require(SuiteCapability.ClientCommandLine, test);
        return ClientExecutable()!;
    }

    /// <summary>
    /// The MCP client's command line on this machine, found exactly the way the
    /// product finds it.
    /// </summary>
    /// <remarks>
    /// <b>Through the product's own <see cref="ClientCommandLine.Locate"/>
    /// rather than a second search.</b> A harness that looked somewhere else
    /// could report the capability present on a machine where the product would
    /// not find it, which is a false green about the one thing these tests
    /// exist to establish.
    /// </remarks>
    /// <returns>The path, or <see langword="null"/>.</returns>
    public static string? ClientExecutable() =>
        new ClientCommandLine().Locate(McpClientRegistration.ClientExecutable);

    /// <summary>
    /// Whether the repository payload is available, recording its absence
    /// without skipping the calling test.
    /// </summary>
    /// <remarks>
    /// For the two arms that assert something real either way and gain an extra
    /// assertion when a payload exists. Skipping those would throw away the half
    /// that does not need one; letting them run silently would leave a
    /// degradation off the summary, which is the thing this type exists to stop.
    /// A release run refuses, exactly as a hard requirement does.
    /// </remarks>
    /// <param name="test">The calling test, filled in by the compiler.</param>
    /// <returns>Whether the payload is present.</returns>
    public static bool HasRepositoryPayload([CallerMemberName] string test = "")
    {
        var state = StateOf(SuiteCapability.RepositoryPayload);

        if (state is CapabilityState.Present)
        {
            return true;
        }

        RefuseAPartialInstallation(SuiteCapability.RepositoryPayload, state);
        Record(SuiteCapability.RepositoryPayload, test);

        if (IsReleaseRun)
        {
            throw new InvalidOperationException(RefusalFor(SuiteCapability.RepositoryPayload));
        }

        return false;
    }

    /// <summary>Whether a capability is available to this run.</summary>
    /// <param name="capability">The capability.</param>
    /// <returns>Whether it is present.</returns>
    public static bool IsPresent(SuiteCapability capability) => StateOf(capability) is CapabilityState.Present;

    /// <summary>The tests that took a degraded path for want of a capability.</summary>
    /// <param name="capability">The capability.</param>
    /// <returns>The test names, ordered.</returns>
    public static IReadOnlyList<string> DegradedTests(SuiteCapability capability) =>
        Degradations.TryGetValue(capability, out var tests)
            ? [.. tests.Keys.Order(StringComparer.Ordinal)]
            : [];

    /// <summary>Every capability, in the order the summary reports them.</summary>
    public static IReadOnlyList<SuiteCapability> All { get; } = [.. Enum.GetValues<SuiteCapability>()];

    /// <summary>What this run exercised, and what it did not.</summary>
    /// <remarks>
    /// <b>Printed unconditionally at the end of every run</b> by
    /// <see cref="SuiteCoverage"/>, because the whole defect was that the four
    /// numbers in the run summary are the same either way. A reader who sees
    /// only this block can say whether a browser was started.
    /// </remarks>
    /// <returns>The block, without a trailing newline.</returns>
    public static string Summary()
    {
        var report = new StringBuilder();
        var rule = new string('=', 78);

        _ = report.Append(rule).Append('\n');
        _ = report.Append("BrowserAI suite coverage — what this run actually exercised\n");
        _ = report.Append(rule).Append('\n');

        var degraded = 0;

        foreach (var capability in All)
        {
            var state = StateOf(capability);
            var tests = DegradedTests(capability);
            degraded += tests.Count;

            _ = report.Append("  ")
                .Append(Title(capability).PadRight(20))
                .Append(state switch
                {
                    CapabilityState.Present => "PRESENT",
                    CapabilityState.AbsentAsAWhole => "ABSENT ",
                    _ => "PARTIAL",
                })
                .Append("  ")
                .Append(WitnessFor(capability))
                .Append('\n');

            if (tests.Count is not 0)
            {
                // Named for what actually happened to them, which differs by
                // mode: a release run fails these rather than skipping them, and
                // a block that said "skipped" either way would be asserting
                // something the run's own counts contradict.
                _ = report.Append("      ")
                    .Append(tests.Count)
                    .Append(tests.Count is 1 ? " test " : " tests ")
                    .Append(IsReleaseRun ? "FAILED for want of it: " : "skipped: ")
                    .Append(string.Join(", ", tests))
                    .Append('\n');
            }
        }

        _ = report.Append("  ")
            .Append("release run".PadRight(20))
            .Append(IsReleaseRun ? "YES" : "no ")
            .Append("      ")
            .Append(ReleaseRunVariable)
            .Append(IsReleaseRun ? " is set, so a missing capability is a failure" : "=1 turns every skip below into a failure")
            .Append('\n');

        // WHAT THIS RUN'S ENVIRONMENT SAID IT WOULD LACK, printed beside what it
        // actually lacked so the two can be read together. A block that reported
        // ABSENT and said nothing about whether that absence was expected is the
        // state this row was added to end: four ABSENT rows look identical
        // whether four were meant to be absent or five went and one was noticed
        // by nobody. Since 2026-08-20 this row reads "not declared" on every run,
        // because CI was the only environment that ever declared.
        _ = report.Append("  ")
            .Append("expected absent".PadRight(20))
            .Append(ExpectedAbsentDeclaration is null ? "not declared" : ExpectedAbsentDeclaration)
            .Append("   ")
            .Append(ExpectedAbsentVariable)
            .Append(ExpectedAbsentDeclaration is null
                ? " is unset, so nothing here pins WHICH capabilities may be absent"
                : " is set, so an absence it does not name is a failing test")
            .Append('\n');

        // ⚠️ Where the first-run test's 203.8 MB came from, and it is a row here
        // rather than a capability because it is not one: Chromium is provisioned
        // either way and every capability above reads PRESENT either way. What
        // this line says is who PAID for it -- Playwright's CDN, or a tree the
        // last cold run left in .work\. Without it, a suite that had quietly
        // stopped downloading for a week would report exactly what one that
        // downloaded every time reports, which is this block's founding defect
        // met one layer down. See FirstRunCache.
        _ = report.Append(FirstRunCache.CoverageRow).Append('\n');

        // ⚠️ WHICH DRIVE-LETTER SPELLING THIS RUN ACTUALLY RECEIVED, which is the
        // release gate's claim about itself rather than a capability. The gate
        // runs two shells because they hand the test host two different
        // spellings; on 2026-08-24 all six runs received `C:` and the gate
        // reported exactly what a genuine two-instrument gate reports. Both
        // halves now force a spelling and declare it, and this row is what says
        // whether the forcing took -- see GateDriveCase.
        _ = report.Append(GateDriveCase.CoverageRow).Append('\n');

        // ⚠️ WHETHER THIS RUN WAS FILTERED, which is the premise every other
        // number in this block and in the run summary rests on. A filtered run is
        // a CORRECT run -- every figure it prints is true of what it ran -- and
        // that is exactly why it needs a row: its total/failed/succeeded block is
        // character-for-character the shape a full run's is, so the false sentence
        // is the one a human writes underneath it, which no test can read. What a
        // test can do is make the run state the premise. Read from the platform's
        // own ITestExecutionFilter and never from this process's command line;
        // see SuiteFilter for why ICommandLineOptions could not be used.
        _ = report.Append(SuiteFilter.CoverageRow).Append('\n');

        // ⚠️ WHAT THIS RUN COULD NOT HAVE SEEN, which is a different statement
        // from every row above it. Those say whether an artefact was there; this
        // one says whether Windows would have let a defect show itself at all.
        // A row rather than a capability, for the reason ForegroundLock states:
        // every capability above names a command that produces it, and the only
        // thing that would turn this one green is changing a machine-wide user
        // preference, which is out of bounds. Without it a run on a machine whose
        // foreground lock is effectively infinite reports exactly what a run on a
        // machine that could see a focus steal reports — this block's founding
        // defect, one layer out from the product.
        _ = report.Append(ForegroundLock.CoverageRow).Append('\n');

        // ⚠️ WHAT THE MACHINE WAS CARRYING WHILE THIS RUN RAN, which is the one
        // reading a closed hazard row names as the thing that separates its own
        // cause from a live one -- and which no run had ever recorded, so the
        // question could not be asked of any gate this project has taken. A row
        // rather than a capability for ForegroundLock's reason: nothing anybody
        // types makes a machine's commit charge healthy. Two readings rather
        // than one, because the difference between them is what separates "the
        // machine was already loaded" from "this suite loaded it". See
        // CommitCharge, and HAZARDS.md for what asked for it.
        _ = report.Append(CommitCharge.CoverageRow).Append('\n');

        _ = report.Append(rule).Append('\n');

        var absent = All.Count(capability => StateOf(capability) is not CapabilityState.Present);

        if (degraded is not 0 && IsReleaseRun)
        {
            _ = report.Append("  ⚠️  DEGRADED RELEASE RUN: ").Append(degraded)
                .Append(" test executions could not exercise what their\n")
                .Append("      names claim, and each is a FAILURE because this run asked to be a\n")
                .Append("      release run. No release may be cut from this machine as it stands.\n");
        }
        else if (degraded is not 0)
        {
            _ = report.Append("  ⚠️  DEGRADED RUN: ").Append(degraded)
                .Append(" test executions took a path that proves less than the\n")
                .Append("      test's name claims. They are reported as SKIPPED, not as passed, so this\n")
                .Append("      run's summary is not the summary of a healthy one. A release may not be\n")
                .Append("      cut from it: release checklist item 8 requires the skipped count to be zero.\n");
        }
        else if (absent is not 0)
        {
            _ = report.Append("  ").Append(absent)
                .Append(" capability above is ABSENT and no test in this run asked for it.\n")
                .Append("      Nothing was degraded, and nothing here proves it could have been exercised.\n");
        }
        else
        {
            _ = report.Append("  This run exercised every layer. No test took a degraded path.\n");
        }

        return report.Append(rule).ToString();
    }

    /// <summary>
    /// The decision, as a pure function of the two inputs, so that the release
    /// branch is exercised rather than only written.
    /// </summary>
    /// <remarks>
    /// Without this the <c>BROWSERAI_RELEASE_RUN</c> arm would be code no run of
    /// the suite ever takes, which is the same class of dead mechanism as the
    /// degraded run it exists to catch.
    /// </remarks>
    /// <param name="state">What the capability's artefacts look like on disk.</param>
    /// <param name="isReleaseRun">Whether this is a release run.</param>
    /// <returns>What a guard must do.</returns>
    public static CapabilityVerdict Decide(CapabilityState state, bool isReleaseRun) => state switch
    {
        CapabilityState.Present => CapabilityVerdict.Proceed,
        CapabilityState.Partial => CapabilityVerdict.Fail,
        _ => isReleaseRun ? CapabilityVerdict.Fail : CapabilityVerdict.Skip,
    };

    /// <summary>
    /// What this run's environment declared it expects to be absent, or
    /// <see langword="null"/> when it declared nothing.
    /// </summary>
    /// <remarks>
    /// Read once, like <see cref="IsReleaseRun"/>, so that the whole run
    /// reconciles against one declaration rather than against whatever the
    /// environment block said at the moment each test asked.
    /// </remarks>
    public static string? ExpectedAbsentDeclaration { get; } =
        Environment.GetEnvironmentVariable(ExpectedAbsentVariable);

    /// <summary>
    /// Reconciles a declaration of what should be absent against what actually
    /// is, as a pure function of the two, and names every disagreement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pure, for <see cref="Decide"/>'s reason exactly.</b> The declaration
    /// only ever exists in a controlled environment, so an assertion written
    /// only against the live environment would be a mechanism a developer
    /// machine can never exercise and the controlled one would meet for the
    /// first time at the moment it mattered — and with no controlled
    /// environment left, it would be a mechanism nothing exercises at all. Every
    /// branch below is reachable in-process from
    /// <see cref="SuiteCoverageTests.TheExpectedAbsentDeclarationIsReconciledAgainstWhatIsAbsent"/>,
    /// and the live arm is the same function applied to the real environment.
    /// </para>
    /// <para>
    /// <b>Exact in both directions, not just <i>undeclared absence is red</i>.</b>
    /// A declaration naming a capability that is in fact present is a lie that
    /// weakens the pin silently: it is standing permission for that capability to
    /// go absent later and nothing would say so — which is the whole defect this
    /// closes, re-introduced through the file that closes it. So an over-broad
    /// declaration is a failure too, and the cost is that installing something on
    /// the runner means editing the declaration in the same commit. That is the
    /// intended cost.
    /// </para>
    /// <para>
    /// <b>A name that is not a capability is a failure rather than an ignored
    /// token.</b> A typo would otherwise silently shrink the declared set, which
    /// fails in the safe direction today and in the unsafe direction the moment
    /// somebody widens it — and a check that quietly discards its own input is
    /// how a positive control gets lost. Matched against
    /// <see cref="Enum.GetNames{TEnum}()"/> rather than through
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>, because that
    /// parses <c>"1"</c> into a capability and a declaration of <c>1</c> means
    /// nothing to anybody.
    /// </para>
    /// </remarks>
    /// <param name="declaration">The environment's declaration, or <see langword="null"/> when it made none.</param>
    /// <param name="absent">The capabilities this run does not have.</param>
    /// <returns>One line per disagreement, empty when the two agree or nothing was declared.</returns>
    public static IReadOnlyList<string> ReconcileDeclaredAbsence(string? declaration, IEnumerable<SuiteCapability> absent)
    {
        ArgumentNullException.ThrowIfNull(absent);

        // Nothing declared: a developer machine, whose provisioned set is a fact
        // about somebody's disk rather than about this repository. Exactly the
        // behaviour that existed before this function did.
        if (declaration is null)
        {
            return [];
        }

        var (declared, problems) = ReadDeclaration(declaration);
        var missing = new List<string>(problems);
        var actually = absent.ToList();

        missing.AddRange(actually
            .Where(capability => !declared.Contains(capability))
            .Select(capability =>
                $"'{Title(capability)}' ({capability}) is ABSENT and this run's environment did not declare that it would be. "
                + $"{WitnessFor(capability)}. Either the machine lost a capability it used to have — which is what this check exists to catch — or {ExpectedAbsentVariable} needs it added. {RemedyFor(capability)}"));

        missing.AddRange(declared
            .Where(capability => !actually.Contains(capability))
            .Select(capability =>
                $"{ExpectedAbsentVariable} declares '{Title(capability)}' ({capability}) absent and it is PRESENT. "
                + "A declaration wider than the truth is standing permission for that capability to disappear unnoticed, so it is a failure rather than a courtesy: remove it from the declaration."));

        return missing;
    }

    /// <summary>
    /// The capabilities a declaration names, and everything wrong with the way
    /// it named them.
    /// </summary>
    /// <remarks>
    /// <b>Split out of <see cref="ReconcileDeclaredAbsence"/> so that a
    /// declaration committed to a pipeline definition and this run's live one
    /// are read by one implementation.</b> A second copy of <i>what counts as a
    /// name</i> would eventually answer the two differently.
    /// <para>
    /// ⚠️ <b>Only one caller is left as of 2026-08-20, and the split is kept
    /// anyway.</b> The other was
    /// <c>SuiteCoverageTests.TheWorkflowStillDeclaresWhatItExpectsToBeAbsent</c>,
    /// which read <c>.github/workflows/build.yml</c> and was deleted with CI —
    /// it could not be re-pointed at a file that does not exist without losing
    /// the positive control that made it worth having. The reason to keep this
    /// entry point is the one it was created for: the day something declares
    /// again, the string it commits and the string this run reads must be parsed
    /// by the same code.
    /// </para>
    /// </remarks>
    /// <param name="declaration">The declaration, which must not be <see langword="null"/>.</param>
    /// <returns>What it names, and one line per thing wrong with it.</returns>
    public static (IReadOnlyList<SuiteCapability> Declared, IReadOnlyList<string> Problems) ReadDeclaration(string declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var problems = new List<string>();
        var declared = new List<SuiteCapability>();
        var names = Enum.GetNames<SuiteCapability>();
        var tokens = declaration.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length is 0)
        {
            problems.Add(
                $"{ExpectedAbsentVariable} is set to '{declaration}', which names nothing and is not '{NothingExpectedAbsent}'. "
                + $"An empty declaration is an accident rather than a statement: write '{NothingExpectedAbsent}' to declare that every capability is expected to be present, or unset the variable to declare nothing at all.");

            return (declared, problems);
        }

        if (tokens.Length is 1 && tokens[0].Equals(NothingExpectedAbsent, StringComparison.OrdinalIgnoreCase))
        {
            return (declared, problems);
        }

        foreach (var token in tokens)
        {
            // Against the NAMES rather than through Enum.TryParse, which parses
            // "1" into a capability -- and a declaration of `1` means nothing to
            // anybody reading the pipeline definition it would be written in.
            var match = Array.Find(names, name => name.Equals(token, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                problems.Add(
                    $"{ExpectedAbsentVariable} names '{token}', which is not a capability. The capabilities are: {string.Join(", ", names)}.");
                continue;
            }

            declared.Add(Enum.Parse<SuiteCapability>(match));
        }

        return (declared, problems);
    }

    /// <summary>What a capability's artefacts look like on disk.</summary>
    /// <param name="capability">The capability.</param>
    /// <returns>Present, absent as a whole, or a partial installation.</returns>
    public static CapabilityState StateOf(SuiteCapability capability) => capability switch
    {
        SuiteCapability.PublishedSlice => PublishedSlice.IsPresent
            ? CapabilityState.Present
            : PublishedSlice.IsAbsentAsAWhole ? CapabilityState.AbsentAsAWhole : CapabilityState.Partial,

        SuiteCapability.RepositoryPayload => RepositoryPayload.IsPresent
            ? CapabilityState.Present
            : RepositoryPayload.IsAbsentAsAWhole ? CapabilityState.AbsentAsAWhole : CapabilityState.Partial,

        // A revision directory with no executable in it is a half-finished
        // download or a half-deleted tree, never a clean machine — the same
        // distinction the publish and the payload already draw, added 2026-08-16
        // when the two ungated tests moved in here and brought it with them.
        SuiteCapability.ProvisionedChromium => File.Exists(BrowserAiPaths.ExpectedChromiumExecutable)
            ? CapabilityState.Present
            : Directory.Exists(BrowserAiPaths.ChromiumDirectory) ? CapabilityState.Partial : CapabilityState.AbsentAsAWhole,

        SuiteCapability.ProvisionedFirefox => File.Exists(BrowserAiPaths.FirefoxExecutable)
            ? CapabilityState.Present
            : Directory.Exists(BrowserAiPaths.FirefoxDirectory) ? CapabilityState.Partial : CapabilityState.AbsentAsAWhole,

        // No Partial state exists for this one: an executable is either on the
        // search path or it is not, and there is no half-installed shape a
        // directory could be in that would mean anything.
        SuiteCapability.ClientCommandLine => ClientExecutable() is not null
            ? CapabilityState.Present
            : CapabilityState.AbsentAsAWhole,

        // No Partial state here either, for the same reason and one more: git
        // either answers "this is a work tree" or it does not, and the two ways
        // of not answering — no git on PATH, and a directory that is not a
        // repository — are the same absence to every caller. A `.git` present
        // and unreadable would be a third thing, and it is not distinguished
        // here because nothing could act on the distinction.
        SuiteCapability.Git => GitOracle.IsAvailable
            ? CapabilityState.Present
            : CapabilityState.AbsentAsAWhole,

        _ => PackagedRelease() is not null ? CapabilityState.Present : CapabilityState.AbsentAsAWhole,
    };

    /// <summary>
    /// The packed release this run can read, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not a search of <c>.work/</c>.</b> Only the release
    /// script's own output directory counts, because a package left behind by an
    /// older run predates whatever is being asserted about it — and a stale
    /// artefact that satisfies a notice check is the same shape of false green
    /// as the degraded run.
    /// </remarks>
    /// <returns>The newest full package, or <see langword="null"/>.</returns>
    public static string? PackagedRelease()
    {
        if (Environment.GetEnvironmentVariable(ReleasePackageVariable) is { Length: > 0 } named)
        {
            return File.Exists(named) ? named : null;
        }

        var releases = new DirectoryInfo(Path.Combine(RepositoryLayout.Root.FullName, "Releases"));

        return releases.Exists
            ? releases.EnumerateFiles("BrowserAI-*-full.nupkg", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName
            : null;
    }

    private static void Require(SuiteCapability capability, string test)
    {
        var state = StateOf(capability);

        switch (Decide(state, IsReleaseRun))
        {
            case CapabilityVerdict.Proceed:
                return;

            case CapabilityVerdict.Fail:
                RefuseAPartialInstallation(capability, state);
                Record(capability, test);
                throw new InvalidOperationException(RefusalFor(capability));

            default:
                Record(capability, test);
                Skip.Test(RefusalFor(capability));
                return;
        }
    }

    private static void RefuseAPartialInstallation(SuiteCapability capability, CapabilityState state)
    {
        if (state is CapabilityState.Partial)
        {
            throw new InvalidOperationException(
                $"{Title(capability)}: {WitnessFor(capability)}. The directory exists and what must be inside it does not, which is a broken build rather than a clean clone — so this is a failure in every run, release or not. {RemedyFor(capability)}");
        }
    }

    private static void Record(SuiteCapability capability, string test) =>
        Degradations.GetOrAdd(capability, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))[NameOf(test)] = 0;

    /// <summary>
    /// The running test's own name, falling back to the calling member.
    /// </summary>
    /// <remarks>
    /// <b>[CallerMemberName] alone names the wrong thing when a guard sits in a
    /// helper.</b> Measured 2026-08-16 on the first degraded run after this gate
    /// landed: the block reported a skipped test called <c>RunAsync</c>, which is
    /// a private method of <c>StraySweepTests</c> and not a test at all — and a
    /// coverage block that names something a reader cannot find is the same
    /// defect it exists to fix, one level down.
    /// </remarks>
    /// <param name="fallback">The caller the compiler filled in.</param>
    /// <returns>The test's name.</returns>
    private static string NameOf(string fallback) =>
        TestContext.Current?.Metadata.TestName is { Length: > 0 } name ? name : fallback;

    private static string RefusalFor(SuiteCapability capability) =>
        $"{Title(capability)} is not available to this run, so this test would prove nothing that its name claims. {WitnessFor(capability)}. {RemedyFor(capability)}"
        + (IsReleaseRun
            ? $" This is a release run ({ReleaseRunVariable} is set), so it is a failure rather than a skip."
            : $" Set {ReleaseRunVariable}=1 to make this a failure instead of a skip.");

    private static string Title(SuiteCapability capability) => capability switch
    {
        SuiteCapability.PublishedSlice => "published slice",
        SuiteCapability.RepositoryPayload => "repository payload",
        SuiteCapability.ProvisionedChromium => "Chromium",
        SuiteCapability.ProvisionedFirefox => "Firefox",
        SuiteCapability.ClientCommandLine => "client CLI",
        SuiteCapability.Git => "git",
        _ => "packed release",
    };

    private static string WitnessFor(SuiteCapability capability) => capability switch
    {
        SuiteCapability.PublishedSlice => PublishedSlice.Executable,
        SuiteCapability.RepositoryPayload => Path.Combine(RepositoryPayload.Layout.Root, "payload.json"),
        SuiteCapability.ProvisionedChromium => BrowserAiPaths.ExpectedChromiumExecutable,
        SuiteCapability.ProvisionedFirefox => BrowserAiPaths.FirefoxExecutable,
        SuiteCapability.ClientCommandLine => ClientExecutable() ?? $"{McpClientRegistration.ClientExecutable} (not on PATH, nor at {BrowserAI.Registration.ClientCommandLine.FallbackDirectory})",
        SuiteCapability.Git => GitOracle.IsAvailable
            ? $"git -C {RepositoryLayout.Root.FullName} rev-parse --is-inside-work-tree said true"
            : $"git could not answer for {RepositoryLayout.Root.FullName} (not on PATH, or this is an export rather than a checkout)",
        _ => PackagedRelease() ?? Path.Combine(RepositoryLayout.Root.FullName, "Releases", "BrowserAI-<version>-full.nupkg"),
    };

    private static string RemedyFor(SuiteCapability capability) => capability switch
    {
        SuiteCapability.PublishedSlice => $"Run: {PublishedSlice.PublishCommand}",
        SuiteCapability.RepositoryPayload => "Run: pwsh -File build/Build-Payload.ps1",
        SuiteCapability.ProvisionedChromium => "Provision it: BrowserAI downloads it on first use, or run the suite once with a payload present.",
        SuiteCapability.ProvisionedFirefox => "Provision it: BrowserAI downloads it on first use of a Firefox session.",
        SuiteCapability.ClientCommandLine => $"Install the MCP client, so that '{McpClientRegistration.ClientExecutable}' is on PATH. Nothing is written to it: the real-client arms point it at a scratch configuration directory.",
        SuiteCapability.Git => "Install git and run the suite from a checkout rather than from an export. Nothing is written: the only command asked for is 'git ls-files'.",
        _ => $"Run: pwsh -File build/New-Release.ps1, or set {ReleasePackageVariable} to a packed .nupkg.",
    };
}

/// <summary>What a capability's artefacts look like on disk.</summary>
internal enum CapabilityState
{
    /// <summary>Everything the capability needs is there.</summary>
    Present,

    /// <summary>Nothing is there, which is what a clean clone looks like.</summary>
    AbsentAsAWhole,

    /// <summary>The directory is there and what must be inside it is not.</summary>
    Partial,
}

/// <summary>What a guard must do about a capability.</summary>
internal enum CapabilityVerdict
{
    /// <summary>Run the test.</summary>
    Proceed,

    /// <summary>Report the test as skipped, naming what is missing.</summary>
    Skip,

    /// <summary>Fail the test.</summary>
    Fail,
}
