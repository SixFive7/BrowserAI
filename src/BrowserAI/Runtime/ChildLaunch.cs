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
/// <b>What is settled here is which browser runs and whether it is sandboxed</b>,
/// and both are settled the only way that works. <c>browserName</c> and an
/// explicit chromium-alias channel are always both set: omit them and upstream
/// fills in <c>channel: "chrome"</c>, the user's own installed Google Chrome, so
/// the entire batteries-included premise becomes silently dead code — measured
/// with an <b>empty</b> browsers directory, where <c>initialize</c>,
/// <c>tools/list</c> and <c>browser_navigate</c> all succeeded. And
/// <c>--sandbox</c> goes on the command line, never <c>chromiumSandbox</c> in the
/// config file, because the config key parses, validates and is discarded
/// ([kb](../../../kb/playwright/configuration.md#defaults-that-are-not-what-they-look-like)).
/// Provisioning, modes, sessions and artifact routing all live elsewhere.
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
    /// The child's working directory. For a session that is the session
    /// directory itself; for the run's own child it is the instance directory.
    /// It must already exist.
    /// </param>
    /// <param name="configFile">
    /// Where to write the generated config. <b>Never inside a session
    /// directory</b>: <c>browserai.lock</c> and <c>browserai.data</c> are the only
    /// files at a session's root, and a third one would make the two that matter
    /// missable. *(Corrected 2026-08-26, previously "<c>browserai.json</c> and the
    /// session log" — one file became two and the session log went to stderr; the
    /// rule is unchanged and now has one more file to protect.)*
    /// </param>
    /// <param name="config">The generated config, from <see cref="BrowserConfiguration"/>.</param>
    /// <param name="name">The transport's name in diagnostics.</param>
    /// <param name="standardErrorLines">Where the child's stderr is delivered.</param>
    /// <returns>Everything <see cref="DirectStdioClientTransport"/> needs.</returns>
    /// <exception cref="ArgumentException"><paramref name="browsersDirectory"/> is not absolute.</exception>
    /// <exception cref="FileNotFoundException">The payload is incomplete.</exception>
    public static ChildProcessOptions Create(
        PayloadLayout payload,
        string browsersDirectory,
        string workingDirectory,
        string configFile,
        GeneratedConfig config,
        string name = "playwright-mcp",
        Action<string>? standardErrorLines = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(browsersDirectory);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(configFile);
        ArgumentNullException.ThrowIfNull(config);

        if (!Path.IsPathFullyQualified(browsersDirectory))
        {
            throw new ArgumentException(
                $"The browsers root must be absolute, and '{browsersDirectory}' is not: a relative {BrowsersPathVariable} resolves against INIT_CWD before it resolves against the child's own directory.",
                nameof(browsersDirectory));
        }

        payload.Verify();

        // ⚠️ Before the config is written and long before anything is spawned.
        // A Firefox that meets a held profile does not fail -- it puts a modal
        // dialog on the Windows desktop and blocks against Playwright's
        // three-minute launch timeout, with nothing on stderr and nothing in the
        // protocol. Refusing here is the difference between an answer and an
        // invisible hang, and it is HERE rather than in the session layer because
        // this function is the one route to a child: a later step that adds a
        // second caller inherits the guard instead of having to remember it.
        if (FirefoxProfileLockedException.For(config) is { } collision)
        {
            throw collision;
        }

        BrowserConfiguration.WriteTo(configFile, config);

        return new ChildProcessOptions
        {
            Command = payload.NodeExecutable,
            Arguments =
            [
                payload.PlaywrightMcpCli,
                "--config",
                configFile,

                // On the command line, never in the file above.
                SandboxFlag,

                // --caps is deliberately absent and must stay absent: it
                // REPLACES the config file's capability list rather than
                // merging with it, so passing it here would silently wipe the
                // capabilities the generator just wrote.
            ],
            WorkingDirectory = workingDirectory,
            Environment = ChildEnvironment.Build(
                [new KeyValuePair<string, string>(BrowsersPathVariable, browsersDirectory)]),
            StandardErrorLines = standardErrorLines,
            Name = name,
        };
    }
}
