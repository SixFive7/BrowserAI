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
/// one is reported as a missing file and not as a launch failure inside
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
    /// <c>@playwright/mcp</c>'s entry point, addressed as a file and not
    /// through a <c>.cmd</c> shim -- a shim would need a shell, and a shell is
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
    /// <c>playwright-core</c>'s server registry, whose <c>list()</c> is the only
    /// code upstream has that unlinks a dead browser descriptor.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not one of the three <see cref="Verify"/> names.</b>
    /// A payload missing this module costs the prune that
    /// <see cref="ServerRegistryReap"/> starts at a session close, and a prune
    /// nobody asked for must never be a reason a session refuses to open -- so its
    /// absence is a record in the process log and nothing more. <b>It is also an
    /// internal module and not a documented entry point</b> (the package's
    /// <c>exports</c> map names four subpaths and this is not one of them, which
    /// only gates a require by package name and not the absolute one used here),
    /// so a rename upstream is exactly what the re-verification row keyed on the
    /// <c>playwright-core</c> version is for.
    /// </remarks>
    public string ServerRegistryModule =>
        Path.Combine(Root, "mcp", "node_modules", "playwright-core", "lib", "serverRegistry.js");

    // ⚠️ DELETED 2026-10-08: `ToolVerdicts`, the payload's copy of
    // tool-verdicts.json, which a build target copied in from the repository root
    // because "the verdicts describe the cli.js they shipped with". They still do,
    // and the binary is what carries them now: the file is compiled into it beside
    // the tool list (ToolVerdicts.Compiled), and the binary and the payload are
    // packed and replaced together, so the same property holds with no file to
    // copy and none that can be missing.

    /// <summary>
    /// Checks that the payload's two required files exist, so an incomplete
    /// payload names itself.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Two since 2026-10-08 (previously three, "Three since 2026-08-26
    /// (previously two). The verdicts file joins the executable and the CLI").</b>
    /// The verdicts are compiled into the binary since that day, so a payload
    /// without the file is not incomplete, and a binary built without them does
    /// not build.
    /// </remarks>
    /// <exception cref="FileNotFoundException">Any of them is missing.</exception>
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
