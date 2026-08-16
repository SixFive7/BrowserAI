// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Velopack.Locators;

namespace BrowserAI.Updates;

/// <summary>
/// Whether this process is an installed BrowserAI, and where its install root
/// is. The one place in the product that touches <see cref="VelopackLocator"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The seam is a boolean, not an exception handler, and that is measured
/// rather than designed.</b> [§G landmine 6](../../plan/G-updates.md) said
/// <c>NotInstalledException</c> is the normal outcome under <c>dotnet run</c>
/// and every test host. **That is wrong for 1.2.0**
/// ([kb](../../kb/packaging/velopack.md#6-notinstalledexception-under-dotnet-run-and-every-test-host)):
/// <c>VelopackLocator.Current</c> throws
/// <c>InvalidOperationException: No VelopackLocator has been set</c> until
/// <c>VelopackApp.Build().Run()</c> has run, and after it has run in an
/// uninstalled process the locator exists and simply reports
/// <c>CurrentlyInstalledVersion == null</c>. So the question is answerable
/// without catching anything, and both states are ordinary.
/// </para>
/// <para>
/// ⚠️ <b><see cref="VelopackLocator.Current"/> is not free.</b> It probes
/// writability, <b>creates <c>packages\</c> and <c>packages\VelopackTemp</c></b>
/// and opens a log file
/// ([kb](../../kb/packaging/velopack.md#5-reading-the-installed-version-must-not-touch-the-network)),
/// so it is read once here and cached. It does <b>not</b> touch the network —
/// that landmine never applied to 1.2.0 — but a startup path that created
/// directories on every call would still be wrong.
/// </para>
/// <para>
/// <b>Why this decides the app root at all.</b> The install root is
/// <c>%LocalAppData%\BrowserAI</c> by default, which is exactly what
/// <see cref="Hosting.LocalAppDataPaths"/> would compute — but only by default.
/// <c>Setup.exe --installto</c> moves it, and a computed root would then put the
/// process log, the session index and the provisioned browsers beside a
/// BrowserAI that is not running. Locating is the difference between a
/// coincidence and a guarantee.
/// </para>
/// </remarks>
internal static class InstallLocation
{
    private static readonly Lazy<Located> Resolved = new(Locate, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Whether this process was installed by Velopack, which is the same
    /// question as <i>may it update itself</i>.
    /// </summary>
    public static bool IsInstalled => Resolved.Value.IsInstalled;

    /// <summary>
    /// The install root — the directory <b>containing</b> <c>current\</c> — or
    /// <see langword="null"/> when this process is not an installed one.
    /// </summary>
    public static string? RootAppDir => Resolved.Value.RootAppDir;

    /// <summary>
    /// The channel this install came from, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Read for reporting only. It is <b>never</b> the channel an update check
    /// uses: a client installed from a beta <c>Setup.exe</c> inherits <c>beta</c>
    /// in its manifest and stays there silently, which is the real reason
    /// <c>ExplicitChannel</c> is set rather than inferred
    /// ([kb](../../kb/packaging/velopack.md#channel--the-charters-reason-was-wrong)).
    /// </remarks>
    public static string? InstalledChannel => Resolved.Value.Channel;

    /// <summary>
    /// The version the <b>locator</b> reports for this install, for comparison
    /// against <see cref="Hosting.BuildVersion.Current"/>.
    /// </summary>
    /// <remarks>
    /// Two numbers that must agree and are produced by different mechanisms:
    /// this one comes from the package manifest <c>vpk</c> stamped, the other
    /// from the assembly attribute MinVer stamped. A build packed at one version
    /// and compiled at another is exactly the state
    /// <c>SixFive7/FrameLink</c>'s hourly restart loop was, so the disagreement
    /// is worth being able to see.
    /// </remarks>
    public static string? InstalledVersion => Resolved.Value.Version;

    private static Located Locate()
    {
        // ⚠️ THE ONE EXCEPTION THAT IS CAUGHT HERE, AND IT IS NOT
        // NotInstalledException. `VelopackLocator.Current` throws
        // `InvalidOperationException: No VelopackLocator has been set` until
        // `VelopackApp.Build().Run()` has run -- read out of 1.2.0's own
        // property, which is a bare null check and a throw. Program calls that
        // first thing, so in the product this cannot happen; a test host, the
        // probe and anything else that loads this assembly without being
        // BrowserAI's entry point reach it immediately. Answering "not an
        // install" is exactly right for all of them, and the alternative is a
        // startup path that throws in every context except the one nobody can
        // run under a debugger.
        try
        {
            var locator = VelopackLocator.Current;
            var version = locator.CurrentlyInstalledVersion;

            return version is null
                ? new Located(false, null, null, null)
                : new Located(true, locator.RootAppDir, locator.Channel, version.ToFullString());
        }
        catch (InvalidOperationException)
        {
            return new Located(false, null, null, null);
        }
    }

    private readonly record struct Located(bool IsInstalled, string? RootAppDir, string? Channel, string? Version);
}
