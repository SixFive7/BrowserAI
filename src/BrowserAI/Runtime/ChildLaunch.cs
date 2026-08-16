// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Protocol;

namespace BrowserAI.Runtime;

/// <summary>
/// How one <c>@playwright/mcp</c> child is started: the command line, the
/// working directory, the generated config and the one environment variable
/// that says where browsers live.
/// </summary>
/// <remarks>
/// This is the whole of the launch half of
/// [§A](../../plan/A-runtime.md) that build-order step 7 owns. Provisioning,
/// modes, sessions and artifact routing are later steps; what is settled here is
/// which browser runs and whether it is sandboxed.
/// </remarks>
internal static class ChildLaunch
{
    /// <summary>
    /// The flag that turns the Chromium sandbox on, and the only thing that
    /// does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It cannot be a config key, and that is measured rather than
    /// remembered.</b> Re-measured 2026-08-16 against <c>@playwright/mcp</c>
    /// 0.0.79 by reading the resolved browser command line of a live Chromium,
    /// three ways: with nothing set, <c>--no-sandbox</c> is present; with
    /// <c>"chromiumSandbox": true</c> in the config file and no flag, it is
    /// <b>still</b> present; with this flag, it is absent from the browser and
    /// from every one of its children.
    /// </para>
    /// <para>
    /// The mechanism, also measured: upstream declares both <c>--sandbox</c> and
    /// <c>--no-sandbox</c>, and commander gives <c>sandbox</c> a default of
    /// <see langword="false"/> rather than leaving it undefined — so the CLI
    /// stage, which merges <i>last</i>, always defines
    /// <c>launchOptions.chromiumSandbox</c> and always overwrites the config
    /// file's value. <c>validateBrowserConfig</c>'s non-Linux
    /// <c>chromiumSandbox = true</c> branch is therefore unreachable on this
    /// path, which is why upstream's intent and upstream's behaviour disagree.
    /// </para>
    /// <para>
    /// <b>Only the browser's own command line proves this.</b> The config key
    /// parses, validates, and is discarded — a test that asserts on what we
    /// wrote asserts on nothing.
    /// </para>
    /// </remarks>
    public const string SandboxFlag = "--sandbox";

    /// <summary>
    /// The environment variable that points the child at the browsers BrowserAI
    /// provisioned rather than at Playwright's own per-user cache.
    /// </summary>
    public const string BrowsersPathVariable = "PLAYWRIGHT_BROWSERS_PATH";

    /// <summary>Builds the options one child is started with.</summary>
    /// <param name="payload">Where <c>node.exe</c> and <c>cli.js</c> live.</param>
    /// <param name="browsersDirectory">
    /// The browsers root, which <b>must be absolute</b>: a relative value
    /// resolves against <c>INIT_CWD</c> first.
    /// </param>
    /// <param name="workingDirectory">
    /// The child's working directory, which also receives the generated config.
    /// It must already exist.
    /// </param>
    /// <param name="standardErrorLines">Where the child's stderr is delivered.</param>
    /// <returns>Everything <see cref="DirectStdioClientTransport"/> needs.</returns>
    /// <exception cref="ArgumentException"><paramref name="browsersDirectory"/> is not absolute.</exception>
    /// <exception cref="FileNotFoundException">The payload is incomplete.</exception>
    public static ChildProcessOptions Create(
        PayloadLayout payload,
        string browsersDirectory,
        string workingDirectory,
        Action<string>? standardErrorLines = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(browsersDirectory);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        if (!Path.IsPathFullyQualified(browsersDirectory))
        {
            throw new ArgumentException(
                $"The browsers root must be absolute, and '{browsersDirectory}' is not: a relative {BrowsersPathVariable} resolves against INIT_CWD before it resolves against the child's own directory.",
                nameof(browsersDirectory));
        }

        payload.Verify();

        var configPath = Path.Combine(workingDirectory, "playwright-mcp.config.json");
        BrowserConfiguration.WriteTo(configPath);

        return new ChildProcessOptions
        {
            Command = payload.NodeExecutable,
            Arguments =
            [
                payload.PlaywrightMcpCli,
                "--config",
                configPath,

                // On the command line, never in the file above.
                SandboxFlag,
            ],
            WorkingDirectory = workingDirectory,
            Environment = ChildEnvironment.Build(
                [new KeyValuePair<string, string>(BrowsersPathVariable, browsersDirectory)]),
            StandardErrorLines = standardErrorLines,
            Name = "playwright-mcp",
        };
    }
}
