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
/// the entire batteries-included premise becomes silently dead code -- measured
/// with an <b>empty</b> browsers directory, where <c>initialize</c>,
/// <c>tools/list</c> and <c>browser_navigate</c> all succeeded. And
/// <c>--sandbox</c> goes on the command line, never <c>chromiumSandbox</c> in the
/// config file -- originally because the config key parsed, validated and was
/// discarded, and since 2026-09-14 because an explicit argument is worth more
/// than a default that has been measured to move
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
    /// ⚠️ <b>Corrected 2026-09-14 @ <c>@playwright/mcp</c> 0.0.80 /
    /// <c>playwright-core</c> 1.63.0-alpha-2026-08-31: it is no longer the
    /// <i>only</i> thing that does, and this flag was deliberately kept
    /// anyway.</b> <i>Previously: "<b>It cannot be a config key, and that is
    /// measured rather than remembered.</b> Re-measured 2026-08-16 against
    /// <c>@playwright/mcp</c> 0.0.79 ... with <c>"chromiumSandbox": true</c> in
    /// the config file and no flag, it is <b>still</b> present", and "The
    /// mechanism, also measured: upstream declares both <c>--sandbox</c> and
    /// <c>--no-sandbox</c>, and commander gives <c>sandbox</c> a default of
    /// <see langword="false"/> ... which is why upstream's intent and upstream's
    /// behaviour disagree."</i> Upstream deleted the normaliser that produced
    /// that disagreement
    /// ([microsoft/playwright#42288](https://github.com/microsoft/playwright/pull/42288)),
    /// and the config key now works. Measured on that review: with the key
    /// alone and no flag, <b>0</b> of the live browser's processes carried
    /// <c>--no-sandbox</c>, against <b>6</b> on the previous pin.
    /// </para>
    /// <para>
    /// <b>What did not change is what this constant is for.</b> Re-measured
    /// 2026-08-16 and re-confirmed by the whole-slice arm on 2026-09-14: with
    /// this flag, <c>--no-sandbox</c> is absent from the browser and from every
    /// one of its children. <b>The flag stays on the command line and the
    /// generator still omits the key</b> -- the sandbox now rests on an explicit
    /// argument <i>and</i> on upstream's default agreeing with it, and not
    /// on the default alone. Moving it into the config file would make this
    /// product's security posture depend on a default that has just been
    /// measured to move, which is a decision and not a tidy-up and has not
    /// been taken.
    /// </para>
    /// <para>
    /// <b>Only the browser's own command line proves any of this.</b> A test
    /// that asserts on what we wrote asserts on nothing -- which is precisely
    /// how the upstream change was caught: by
    /// <c>SandboxFlagTests</c> reading a live Chromium, not by a changelog.
    /// </para>
    /// </remarks>
    public const string SandboxFlag = "--sandbox";

    /// <summary>
    /// The environment variable that points the child at the browsers BrowserAI
    /// provisioned and not at Playwright's own per-user cache.
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
    /// session log" -- one file became two and the session log went to stderr; the
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
        // invisible hang, and it is HERE and not in the session layer because
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
                // REPLACES the config file's capability list instead of
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
