// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Runtime;

/// <summary>
/// Where the vendored runtime lives: <c>node.exe</c> and the
/// <c>@playwright/mcp</c> tree that BrowserAI spawns.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="AppContext.BaseDirectory"/> is correct here and forbidden
/// everywhere else.</b> <see cref="Hosting.IAppPaths"/> bans it because a log or
/// a browser tree placed beside the binary resolves <i>inside</i>
/// <c>current\</c>, which an update replaces wholesale. The payload is the one
/// thing that <b>should</b> be replaced wholesale by an update: it is the
/// vendored copy of upstream that the running build was tested against, and a
/// payload surviving an update would mean the new binary driving the old
/// upstream.
/// </para>
/// <para>
/// Nothing here is searched for and nothing is resolved through <c>PATH</c>.
/// Both files are named absolutely, and <see cref="Verify"/> exists so a missing
/// one is reported as a missing file rather than as a launch failure inside
/// <c>CreateProcessW</c>.
/// </para>
/// </remarks>
/// <param name="root">
/// The payload directory. Production passes nothing and gets
/// <c>&lt;binary&gt;\payload</c>; the suite passes the repository's own
/// <c>payload\</c>, which is where <c>build/Build-Payload.ps1</c> assembles it.
/// </param>
internal sealed class PayloadLayout(string? root = null)
{
    /// <summary>The payload directory.</summary>
    public string Root { get; } = root ?? Path.Combine(AppContext.BaseDirectory, "payload");

    /// <summary>The bundled Node runtime. The only executable BrowserAI starts.</summary>
    public string NodeExecutable => Path.Combine(Root, "node", "node.exe");

    /// <summary>
    /// <c>@playwright/mcp</c>'s entry point, addressed as a file rather than
    /// through a <c>.cmd</c> shim — a shim would need a shell, and a shell is
    /// the process this project spent a whole deviation removing.
    /// </summary>
    public string PlaywrightMcpCli =>
        Path.Combine(Root, "mcp", "node_modules", "@playwright", "mcp", "cli.js");

    /// <summary>
    /// <c>playwright-core</c>'s own <c>browsers.json</c>, which is where the
    /// revision BrowserAI provisions comes from.
    /// </summary>
    /// <remarks>
    /// Inside the artifact and never looked up online: upstream's registry code
    /// contains no "latest" lookup at all, so a release knows forever which
    /// browser it wants and a bump moves the number without anybody editing
    /// anything.
    /// </remarks>
    public string BrowsersManifest =>
        Path.Combine(Root, "mcp", "node_modules", "playwright-core", "browsers.json");

    /// <summary>
    /// Checks that both files exist, so an incomplete payload names itself.
    /// </summary>
    /// <exception cref="FileNotFoundException">Either file is missing.</exception>
    public void Verify()
    {
        foreach (var file in new[] { NodeExecutable, PlaywrightMcpCli })
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException(
                    $"The payload is incomplete: '{file}' does not exist. Run build/Build-Payload.ps1, or reinstall.",
                    file);
            }
        }
    }
}
