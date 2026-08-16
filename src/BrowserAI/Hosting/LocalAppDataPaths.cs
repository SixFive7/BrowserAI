// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Hosting;

/// <summary>
/// The pre-Velopack implementation of <see cref="IAppPaths"/>:
/// <c>%LocalAppData%\BrowserAI</c>, computed rather than located.
/// </summary>
/// <remarks>
/// This is the same directory Velopack installs into, so step 19's swap changes
/// where the answer comes from and not what it is. Per-user by design — a
/// machine-wide install would need elevation, and a UAC prompt cannot be
/// answered by a background MCP server.
/// </remarks>
/// <param name="rootAppDir">
/// The installation root. Tests pass a scratch directory; production passes
/// nothing and gets <c>%LocalAppData%\BrowserAI</c>.
/// </param>
internal sealed class LocalAppDataPaths(string? rootAppDir = null) : IAppPaths
{
    /// <inheritdoc />
    public string RootAppDir { get; } = rootAppDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "BrowserAI");

    /// <inheritdoc />
    public string LogDirectory => Path.Combine(RootAppDir, "logs");
}
