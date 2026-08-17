// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Reflection;

namespace BrowserAI.Hosting;

/// <summary>
/// What version this binary is. One place, read from one attribute.
/// </summary>
/// <remarks>
/// <para>
/// The string is derived from the nearest git tag at build time and is typed
/// nowhere ([plan/stack.md](../../../plan/stack.md), *Versions come from git
/// tags*). On the tag <c>v0.1.0</c> it is <c>0.1.0</c>; five commits later,
/// with no new tag, it is <c>0.1.1-alpha.0.5</c>. Both were measured on
/// 2026-08-16 against MinVer 7.0.0.
/// </para>
/// <para>
/// <b>It reads <see cref="AssemblyInformationalVersionAttribute"/> and never
/// <c>Assembly.GetName().Version</c>, and that is the whole reason this type
/// exists.</b> MinVer sets <c>AssemblyVersion</c> to
/// <c>{Major}.0.0.0</c> by design, so every 0.x build reports
/// <c>0.0.0.0</c> from the assembly version and every 1.x build reports
/// <c>1.0.0.0</c> — measured here, on the artifact, at the tag. That is the
/// *version shows 4 parts* defect another shipped Velopack product filed as an
/// observed symptom rather than a theory
/// ([kb](../../../kb/packaging/velopack.md)), with the number collapsed
/// as well as widened, and it was live in this repository before this type
/// landed: <c>SessionLock</c> stamped every <c>lock.json</c> from
/// <c>GetName().Version</c>, which would have recorded <c>0.0.0.0</c> for the
/// whole 0.x line.
/// </para>
/// <para>
/// <b>No build metadata, ever.</b> The SDK appends <c>+$(SourceRevisionId)</c>
/// — or <c>.$(SourceRevisionId)</c> when the string already carries a
/// <c>+</c> — to the informational version, and
/// <c>Directory.Build.props</c> turns that off repository-wide. The version is
/// a value §G's update path <i>matches</i> rather than compares, and a
/// decorated copy can never equal the one a feed serves.
/// </para>
/// </remarks>
internal static class BuildVersion
{
    /// <summary>
    /// What a binary carrying no informational version reports.
    /// </summary>
    /// <remarks>
    /// It cannot arise from a build of this repository — the build refuses a
    /// version derived from no tag, and the SDK always emits the attribute —
    /// so this covers the assembly being loaded some way nobody has thought of.
    /// It carries a pre-release suffix deliberately: whatever else is unknown
    /// about such a build, it is provably not a release, and
    /// <see cref="IsPreRelease"/> says so without anyone having to special-case
    /// the string.
    /// </remarks>
    public const string Unknown = "0.0.0-unknown";

    /// <summary>The version this binary was built as, as a caller sees it.</summary>
    public static string Current { get; } =
        typeof(BuildVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { Length: > 0 } informational
            ? informational
            : Unknown;

    /// <summary>
    /// Whether this build is <b>not</b> a release.
    /// </summary>
    /// <remarks>
    /// <b>This is the whole of the "never self-update from a build that is not
    /// a release" rule</b>, and it is why no magic development-build number is
    /// needed. An untagged build carries its own pre-release suffix, generated
    /// by the same mechanism that produced the version, so the check cannot be
    /// forgotten on the build where it matters. Nothing consumes it yet;
    /// [§G](../../../plan/G-updates.md) does, at build-order step 19.
    /// </remarks>
    public static bool IsPreRelease { get; } = HasPreReleaseSuffix(Current);

    /// <summary>Whether a semantic version string carries a pre-release suffix.</summary>
    /// <remarks>
    /// Split on the first <c>-</c> after the version core rather than parsed:
    /// build metadata is forbidden here, so the only thing a <c>-</c> can
    /// introduce is the pre-release part.
    /// </remarks>
    /// <param name="version">The version string.</param>
    /// <returns><see langword="true"/> when it is a pre-release.</returns>
    public static bool HasPreReleaseSuffix(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return version.Contains('-', StringComparison.Ordinal);
    }
}
