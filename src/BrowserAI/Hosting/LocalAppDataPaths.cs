// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Hosting;

/// <summary>
/// The layout under an install root: which folder each kind of state lives in.
/// </summary>
/// <remarks>
/// <para>
/// Per-user by design — a machine-wide install would need elevation, and a UAC
/// prompt cannot be answered by a background MCP server.
/// </para>
/// <para>
/// <b>Corrected 2026-08-16 (previously "The pre-Velopack implementation of
/// <see cref="IAppPaths"/>: <c>%LocalAppData%\BrowserAI</c>, computed rather
/// than located … step 19's swap changes where the answer comes from and not
/// what it is").</b> The second half was right and the first was the wrong
/// shape: this class is not pre-Velopack and was not replaced. **It never
/// computed the root in the installed case** — it takes one. What step 19 added
/// is <see cref="Updates.InstallLocation"/>, which *locates* that root from
/// <c>VelopackLocator.Current.RootAppDir</c> when this process is an installed
/// one. Everything below is a folder name and stays true either way.
/// </para>
/// <para>
/// <b>Never <c>AppContext.BaseDirectory</c>.</b> An installed BrowserAI runs out
/// of <c>&lt;root&gt;\current\</c>, which an update replaces wholesale, so every
/// path below is a sibling of <c>current\</c> and none is a child of it.
/// </para>
/// </remarks>
/// <param name="rootAppDir">
/// The installation root. Tests pass a scratch directory; an installed process
/// passes what the locator reported; an uninstalled one passes nothing and gets
/// <c>%LocalAppData%\BrowserAI</c>, which is where Velopack would have put it.
/// </param>
internal sealed class LocalAppDataPaths(string? rootAppDir = null) : IAppPaths
{
    /// <inheritdoc />
    public string RootAppDir { get; } = rootAppDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "BrowserAI");

    /// <inheritdoc />
    public string LogDirectory => Path.Combine(RootAppDir, "logs");

    /// <inheritdoc />
    public string BrowsersDirectory => Path.Combine(RootAppDir, "browsers");

    /// <inheritdoc />
    public string IndexDirectory => Path.Combine(RootAppDir, "index");

    /// <inheritdoc />
    public string InstanceRoot => Path.Combine(RootAppDir, "instances");

    /// <inheritdoc />
    public string LiveInstanceDirectory => Path.Combine(RootAppDir, "live");
}
