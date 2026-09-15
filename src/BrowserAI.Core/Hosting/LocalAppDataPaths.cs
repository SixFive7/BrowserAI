// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Hosting;

/// <summary>
/// The data root and the layout under it: which folder each kind of state lives
/// in.
/// </summary>
/// <remarks>
/// <para>
/// Per-user by design — a machine-wide install would need elevation, and a UAC
/// prompt cannot be answered by a background MCP server.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-09-15 (previously "The layout under an install root …
/// It never computed the root in the installed case — it takes one. What step 19
/// added is <see cref="Updates.InstallLocation"/>, which <i>locates</i> that root
/// … when this process is an installed one").</b> It computes it again, and that
/// is the whole of the layout decision taken this date: <b>the data root is the
/// constant <c>%LocalAppData%\BrowserAI</c></b> and the install root is
/// <c>%LocalAppData%\BrowserAI.app</c>, a sibling rather than a parent. The
/// locator no longer feeds this class at all. The reason is in
/// <see cref="IAppPaths"/>: an install root is a directory the installer
/// destroys, twice over and by design.
/// </para>
/// <para>
/// <b>The constructor still takes a root, and the one thing that supplies one is
/// <see cref="Overridden"/>.</b> The suite needs an <i>empty</i> browsers root
/// to prove first-run provisioning against, and a stdio server has no channel
/// but the environment — so <c>BROWSERAI_ROOT</c> survives, scoped to the data
/// root and to nothing else. It never moves the install root, which is Velopack's
/// to choose.
/// </para>
/// <para>
/// <b>Never <c>AppContext.BaseDirectory</c>.</b> An installed BrowserAI runs out
/// of <c>&lt;install root&gt;\current\</c>, which an update replaces wholesale —
/// and the install root above it is renamed aside and deleted by a repair
/// install and emptied by an uninstall. Nothing below resolves into either.
/// </para>
/// </remarks>
/// <param name="rootAppDir">
/// The data root. Tests pass a scratch directory and so does the override;
/// everything else passes nothing and gets <c>%LocalAppData%\BrowserAI</c>.
/// </param>
internal sealed class LocalAppDataPaths(string? rootAppDir = null) : IAppPaths
{
    /// <summary>
    /// The folder <c>%LocalAppData%</c> holds BrowserAI's data in, spelled once.
    /// </summary>
    /// <remarks>
    /// <b>It is deliberately the plain name, and the installer took the suffixed
    /// one.</b> The data outlives every install: naming it for the product and
    /// the install root for the application is the way round that leaves a
    /// person's browsers, sessions and logs where they expect them across an
    /// uninstall, a reinstall and a version that renames itself.
    /// </remarks>
    public const string FolderName = "BrowserAI";

    /// <summary>
    /// The data root every process resolves when nothing overrides it.
    /// </summary>
    /// <remarks>
    /// <b>Reachable without a locator, which is what a Velopack hook needs.</b>
    /// A fast-exit callback has no <c>VelopackLocator</c> it may pay for and no
    /// business deriving a data root from its own image path — see
    /// <see cref="IAppPaths"/> for why that derivation is the wrong one.
    /// </remarks>
    public static string Default { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        FolderName);

    /// <summary>
    /// The one environment variable BrowserAI reads about <b>itself</b>, and it
    /// moves the <b>data</b> root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists for one thing the suite otherwise cannot do: an empty
    /// browsers root.</b> First-run provisioning can only be proven against a
    /// root where nothing has ever been installed, and the alternative — deleting
    /// the developer's own <c>%LocalAppData%\BrowserAI\browsers</c> mid-suite —
    /// would destroy 430 MiB and break every other browser test running beside
    /// it. <see cref="IAppPaths"/> deliberately does not resolve relative to the
    /// binary, so moving the executable does not move the root either.
    /// </para>
    /// <para>
    /// ⚠️ <b>Moved here 2026-09-15 from <c>Program.AppRootVariable</c>, which is
    /// now an alias for it.</b> The move is what makes the cut to
    /// <c>BrowserAI.Core</c> acyclic: this class reads the variable and lives in
    /// the library, and <c>Program</c> lives in an executable that the library
    /// may not see. There are now <b>two</b> executables that resolve a data
    /// root — the server and the configuration app — so a constant owned by
    /// either one of them would have been owned by the wrong one.
    /// </para>
    /// <para>
    /// ⚠️ <b>Narrowed 2026-09-15 (previously "it moves the whole app root").</b>
    /// It moves the data root and <b>never the install root</b>, which is
    /// Velopack's to choose and which this process only ever reads.
    /// </para>
    /// <para>
    /// <b>Never silent.</b> A BrowserAI running against a root nobody expects
    /// would look exactly like one that lost its sessions, so an override is
    /// logged at Warning on the way past.
    /// </para>
    /// </remarks>
    public const string RootVariable = "BROWSERAI_ROOT";

    /// <summary>
    /// The data root <see cref="RootVariable"/> names, or
    /// <see langword="null"/> when it names nothing usable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read here rather than at each entry point</b>, because there is more
    /// than one: <c>Program.Main</c> serves stdio, and
    /// <c>Registration.HookRegistration</c> runs inside an installer callback
    /// that never reaches <c>Main</c>'s body. Two readers would eventually
    /// answer differently, and the one that would matter is the uninstall hook —
    /// it offers to delete the data root, and a root it resolved differently
    /// from the running product is a root nobody was using.
    /// </para>
    /// <para>
    /// <b>A relative value is ignored rather than resolved</b>, for the reason a
    /// relative <c>PLAYWRIGHT_BROWSERS_PATH</c> is refused: it would land
    /// somewhere nobody chose and report nothing. Never cached — the suite sets
    /// this variable in-process around a scope and expects the next read to see
    /// it.
    /// </para>
    /// </remarks>
    /// <returns>The override, or <see langword="null"/>.</returns>
    public static string? Overridden() =>
        Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } value
        && Path.IsPathFullyQualified(value)
            ? value
            : null;

    /// <inheritdoc />
    public string RootAppDir { get; } = rootAppDir ?? Default;

    /// <inheritdoc />
    public string LogDirectory => Path.Combine(RootAppDir, "logs");

    /// <inheritdoc />
    public string BrowsersDirectory => Path.Combine(RootAppDir, "browsers");

    /// <inheritdoc />
    public string IndexDirectory => Path.Combine(RootAppDir, "index");

    /// <inheritdoc />
    public string InstanceRoot => Path.Combine(RootAppDir, "instances");
}
