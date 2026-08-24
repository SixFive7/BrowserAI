// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Whether this run got the drive-letter spelling its shell was supposed to
/// force on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is about the gate's claim, not about the tests' own paths, and the
/// distinction is the whole reason it exists beside
/// <see cref="DriveLetterCase"/>.</b> That type spells every guard path both ways
/// inside a run, including a spelling no Windows API ever returns, so the
/// <i>class</i> of defect is covered whatever started the suite. What it cannot
/// speak to is the release gate's own reason for running two shells:
/// <b>that the two halves are two instruments.</b> That claim is about the path
/// the test host <i>received</i>, and nothing measured it.
/// </para>
/// <para>
/// ⚠️ <b>It held by luck, run to run, and nothing noticed when it did not.</b>
/// On the 2026-08-24 gate <b>all six runs received <c>C:</c></b> — three of them
/// silently duplicating the other three — and the gate reported exactly what a
/// genuine two-instrument gate reports. On the next gate the two shells did
/// differ. The spelling is inherited from whatever started the run, so a
/// harness-started Git Bash and a human-started one are not the same instrument:
/// measured 2026-08-24, a Git Bash that <i>inherits</i> its working directory
/// hands a child <c>c:\…</c>, and the same shell after any <c>cd</c> — POSIX
/// form, Windows form, either case — hands it <c>C:\…</c>, because MSYS resolves
/// the real path and Windows always answers upper.
/// </para>
/// <para>
/// <b>So the gate forces it and this checks that the forcing took.</b>
/// [Testing](../../../TESTING.md#how-the-suite-is-run-detached-teed-and-the-log-polled)
/// owns the two invocations: each passes <c>dotnet test</c> an
/// <b>absolute, explicitly-spelled</b> path to the solution, which is what puts
/// the spelling into <c>MSBuildProjectDirectory</c>, into <c>TargetPath</c> and
/// therefore into the test host's own <see cref="AppContext.BaseDirectory"/>
/// whatever the shell's working directory says — measured 2026-08-24 through
/// <c>dotnet msbuild -getProperty:TargetPath</c> from both shells, each given
/// the other's spelling. <b>A forcing that silently fails to take is the same
/// trap in a new coat</b>, so each half also declares what it forced in
/// <see cref="Variable"/>, and a run that did not get what it declared is a
/// failing test rather than a line nobody reads.
/// </para>
/// <para>
/// <b>Unset declares nothing, which is what an ordinary developer run does.</b>
/// The shape is <see cref="SuiteEnvironment.ExpectedAbsentVariable"/>'s, for the
/// same reason: a suite that asserted a spelling would be asserting a fact about
/// whoever's shell happened to start it.
/// </para>
/// </remarks>
internal static class GateDriveCase
{
    /// <summary>
    /// The variable each half of the gate sets to declare which spelling it
    /// forced.
    /// </summary>
    /// <remarks>
    /// Set in the detached shell's own environment, on the same invocation as
    /// <see cref="SuiteEnvironment.ReleaseRunVariable"/> and for the same reason:
    /// set in the caller's environment it is not where the test host reads it.
    /// </remarks>
    public const string Variable = "BROWSERAI_DRIVE_CASE";

    /// <summary>The value the Git Bash half declares.</summary>
    public const string Lower = "lower";

    /// <summary>The value the PowerShell half declares.</summary>
    public const string Upper = "upper";

    /// <summary>
    /// The spelling this test host actually received, or <see langword="null"/>
    /// when its base directory is not on a drive letter at all.
    /// </summary>
    /// <remarks>
    /// <b>Read off <see cref="AppContext.BaseDirectory"/> rather than off the
    /// working directory</b>, because that is the path every composed assertion
    /// in this suite is anchored on — <c>RepositoryLayout</c> walks up from it.
    /// The working directory is reported beside it in <see cref="CoverageRow"/>
    /// and is not what the verdict is taken on.
    /// </remarks>
    public static DriveLetterCase? Received { get; } = SpellingOf(AppContext.BaseDirectory);

    /// <summary>
    /// The spelling this run's environment said it forced, or
    /// <see langword="null"/> when it declared nothing.
    /// </summary>
    public static DriveLetterCase? Declared { get; } =
        Environment.GetEnvironmentVariable(Variable) switch
        {
            { } value when value.Equals(Lower, StringComparison.OrdinalIgnoreCase) => DriveLetterCase.Lower,
            { } value when value.Equals(Upper, StringComparison.OrdinalIgnoreCase) => DriveLetterCase.Upper,
            _ => null,
        };

    /// <summary>What this run's declaration and its received spelling amount to.</summary>
    public static GateDriveVerdict Verdict => Judge(Declared, Received);

    /// <summary>
    /// The drive letter a path is spelled with, or <see langword="null"/> when it
    /// does not begin with one.
    /// </summary>
    /// <param name="path">Any path.</param>
    /// <returns>Which spelling it carries.</returns>
    public static DriveLetterCase? SpellingOf(string? path) =>
        path is { Length: >= 2 } && char.IsAsciiLetter(path[0]) && path[1] is ':'
            ? char.IsAsciiLetterUpper(path[0]) ? DriveLetterCase.Upper : DriveLetterCase.Lower
            : null;

    /// <summary>
    /// The verdict, as a pure function of its two inputs.
    /// </summary>
    /// <remarks>
    /// <b>Pure, and shaped after <see cref="SuiteEnvironment.Decide"/> for the
    /// reason that one is:</b> a branch that only runs when somebody is cutting a
    /// release is a mechanism nobody exercises until it matters. Both directions
    /// are driven on every ordinary run by
    /// <c>SuiteCoverageTests.ADeclaredDriveLetterSpellingThatDidNotTakeIsARedRunAndAnUndeclaredOneIsNot</c>,
    /// which is the positive control under the live arm: with nothing declared
    /// the live arm asserts nothing, and a check that can only pass is
    /// indistinguishable from one that works.
    /// </remarks>
    /// <param name="declared">What the shell said it forced, or <see langword="null"/>.</param>
    /// <param name="received">What the test host got, or <see langword="null"/>.</param>
    /// <returns>Whether the forcing took.</returns>
    public static GateDriveVerdict Judge(DriveLetterCase? declared, DriveLetterCase? received) =>
        declared is not { } wanted
            ? GateDriveVerdict.NotDeclared
            : received == wanted
                ? GateDriveVerdict.AsDeclared
                : GateDriveVerdict.NotAsDeclared;

    /// <summary>
    /// The sentence a run whose forcing did not take fails with.
    /// </summary>
    /// <param name="declared">What the shell said it forced.</param>
    /// <param name="received">What the test host got, or <see langword="null"/>.</param>
    /// <returns>The refusal.</returns>
    public static string Refusal(DriveLetterCase? declared, DriveLetterCase? received) =>
        $"This run declared {Variable}={Spelling(declared)}, so the shell that started it meant to force a "
        + $"{Spelling(declared)}-case drive letter onto the test host — and the test host received {Spelling(received)}: "
        + $"AppContext.BaseDirectory is '{AppContext.BaseDirectory}'. "
        + "The forcing did not take, so this half of the release gate is not the instrument it claims to be, and running "
        + "the suite from two shells proves less than it looks like it proves. "
        + "TESTING.md, 'How the suite is run', owns both invocations: each passes dotnet test an absolute, "
        + "explicitly-spelled path to the solution. A relative path, or a bare 'dotnet test', takes the spelling from "
        + "whatever started the shell instead.";

    /// <summary>The coverage block's row for this run.</summary>
    /// <remarks>
    /// <b>Printed on every run, declared or not.</b> The whole defect this closes
    /// is a gate that exercised one instrument twice and reported what two
    /// instruments report, so the number the run publishes about itself has to be
    /// the spelling it actually got rather than the one it was configured to
    /// want.
    /// </remarks>
    public static string CoverageRow =>
        "  " + "drive letter".PadRight(20) + State().PadRight(9) + "  " + Witness();

    private static string State() => Received switch
    {
        DriveLetterCase.Upper => @"UPPER C:\",
        DriveLetterCase.Lower => @"LOWER c:\",
        _ => "UNROOTED ",
    };

    private static string Witness() =>
        $"{AppContext.BaseDirectory} · cwd {Directory.GetCurrentDirectory()} · "
        + Verdict switch
        {
            GateDriveVerdict.NotDeclared =>
                $"{Variable} is unset, so nothing here pins which spelling this half of the gate was meant to receive",

            GateDriveVerdict.AsDeclared =>
                $"{Variable}={Spelling(Declared)} and it took, so this half is a different instrument from the other",

            _ => $"{Variable}={Spelling(Declared)} and IT DID NOT TAKE",
        };

    private static string Spelling(DriveLetterCase? casing) => casing switch
    {
        DriveLetterCase.Upper => Upper,
        DriveLetterCase.Lower => Lower,
        _ => "neither (the base directory is not on a drive letter)",
    };
}

/// <summary>What a run's drive-letter declaration and its received spelling amount to.</summary>
internal enum GateDriveVerdict
{
    /// <summary>Nothing was declared, so nothing is pinned. An ordinary developer run.</summary>
    NotDeclared,

    /// <summary>A spelling was declared and the test host received it.</summary>
    AsDeclared,

    /// <summary>A spelling was declared and the test host received something else. A red run.</summary>
    NotAsDeclared,
}
