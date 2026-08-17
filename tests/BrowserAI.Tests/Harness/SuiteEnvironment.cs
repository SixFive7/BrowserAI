// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using BrowserAI.Registration;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The four things a run either has or does not have, named so that a run that
/// did not have one cannot report the same summary as a run that did.
/// </summary>
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
/// [pre-release item 8](../../../PRE-RELEASE.md) already requires that count
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
                .Append("      cut from it: pre-release item 8 requires the skipped count to be zero.\n");
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
        _ => "packed release",
    };

    private static string WitnessFor(SuiteCapability capability) => capability switch
    {
        SuiteCapability.PublishedSlice => PublishedSlice.Executable,
        SuiteCapability.RepositoryPayload => Path.Combine(RepositoryPayload.Layout.Root, "payload.json"),
        SuiteCapability.ProvisionedChromium => BrowserAiPaths.ExpectedChromiumExecutable,
        SuiteCapability.ProvisionedFirefox => BrowserAiPaths.FirefoxExecutable,
        SuiteCapability.ClientCommandLine => ClientExecutable() ?? $"{McpClientRegistration.ClientExecutable} (not on PATH, nor at {BrowserAI.Registration.ClientCommandLine.FallbackDirectory})",
        _ => PackagedRelease() ?? Path.Combine(RepositoryLayout.Root.FullName, "Releases", "BrowserAI-<version>-full.nupkg"),
    };

    private static string RemedyFor(SuiteCapability capability) => capability switch
    {
        SuiteCapability.PublishedSlice => $"Run: {PublishedSlice.PublishCommand}",
        SuiteCapability.RepositoryPayload => "Run: pwsh -File build/Build-Payload.ps1",
        SuiteCapability.ProvisionedChromium => "Provision it: BrowserAI downloads it on first use, or run the suite once with a payload present.",
        SuiteCapability.ProvisionedFirefox => "Provision it: BrowserAI downloads it on first use of a Firefox session.",
        SuiteCapability.ClientCommandLine => $"Install the MCP client, so that '{McpClientRegistration.ClientExecutable}' is on PATH. Nothing is written to it: the real-client arms point it at a scratch configuration directory.",
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
