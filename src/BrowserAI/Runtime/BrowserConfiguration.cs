// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;

namespace BrowserAI.Runtime;

/// <summary>
/// The config file BrowserAI generates for one child. It never accepts one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generating a key is not the same as the child honouring it.</b>
/// <c>loadConfig</c> is a bare <c>JSON.parse</c> with no schema validation, so a
/// renamed or removed key is discarded in silence — <c>--output-mode</c> was a
/// no-op for its entire life and nobody noticed. Every opinion this type
/// generates is therefore listed in <see cref="GeneratedConfig.Opinions"/> and
/// asserted back out of the running child through <c>browser_get_config</c>. A
/// key we set that does not come back is a red build rather than a mystery in
/// production.
/// </para>
/// <para>
/// <b>Three places where generation must not take the obvious route.</b>
/// <c>chromiumSandbox</c> is absent from this file because the config key is
/// discarded and only <c>--sandbox</c> on the command line works — the CLI stage
/// merges last and commander defaults <c>sandbox</c> to <see langword="false"/>
/// rather than to undefined, so it always overwrites what a file says.
/// <c>browserName</c> <i>and</i> an explicit chromium-alias channel are always
/// both present: omit them and <c>validateBrowserConfig</c> fills in
/// <c>chromium</c> + <c>channel: "chrome"</c>, the user's own Google Chrome, and
/// dropping the channel alone selects <c>chromium-headless-shell</c>, which is
/// never provisioned. And <c>outputMaxSize</c> is never set, because
/// <c>_enforceOutputBudget()</c> then runs on every tool response and unlinks
/// oldest-first across the whole output tree.
/// </para>
/// <para>
/// <b><c>capabilities</c> is written here and must never be passed as
/// <c>--caps</c>.</b> <c>mergeConfig</c> spreads defined overrides, so the flag
/// <i>replaces</i> this list rather than merging with it — as does
/// <c>PLAYWRIGHT_MCP_CAPS</c>, which is why the environment is an allowlist
/// rather than a strip list.
/// </para>
/// </remarks>
internal static class BrowserConfiguration
{
    /// <summary>The default browser family. Never left to upstream's default, which is Chrome.</summary>
    public const string BrowserName = ProvisionedBrowsers.Chromium;

    /// <summary>
    /// The chromium-alias channel, spelled as upstream's <c>chromiumAliases</c>
    /// spells it. Never <c>chrome</c>, which is the user's Google Chrome, and
    /// never absent, which selects the headless shell.
    /// </summary>
    /// <remarks>
    /// <b>Chromium only, and writing it for another family would be worse than
    /// useless.</b> <c>channel</c> is a Chromium concept — <c>chromiumAliases</c>
    /// has no Firefox member — and upstream's own <c>validateBrowserConfig</c>
    /// drops the key for a non-chromium <c>browserName</c>, so a channel written
    /// beside <c>firefox</c> would be an opinion that never arrives and a round
    /// trip that can never pass.
    /// </remarks>
    public const string Channel = "chrome-for-testing";

    /// <summary>The console level upstream defaults to, which silently drops <c>debug</c>.</summary>
    public const string DefaultConsoleLevel = "info";

    /// <summary>The capability a <c>persistent</c> session adds.</summary>
    public const string StorageCapability = "storage";

    /// <summary>The four levels <c>console.level</c> accepts, most severe first.</summary>
    public static IReadOnlyList<string> ConsoleLevels { get; } = ["error", "warning", "info", "debug"];

    /// <summary>
    /// The capabilities every session gets, whatever its mode.
    /// </summary>
    /// <remarks>
    /// <c>config</c> is what makes <c>browser_get_config</c> callable, and that
    /// tool is the only thing that can prove the rest of this file reached the
    /// child. The base 24 tools are unconditional — upstream ors
    /// <c>capability.startsWith("core")</c> with whatever is configured — so
    /// naming a <c>core*</c> capability here would do nothing.
    /// </remarks>
    public static IReadOnlyList<string> BaseCapabilities { get; } = ["config", "vision", "devtools"];

    /// <summary>
    /// Every capability any session can have, which is what the caller-facing
    /// tool list must be built from.
    /// </summary>
    /// <remarks>
    /// The MCP spec forbids the tool set varying per connection, and SEP-2567
    /// removed protocol-level sessions outright, so <c>init</c> cannot shrink the
    /// list. There is one static list and it has to be the union; a call that its
    /// session's mode does not permit is refused at call time instead.
    /// </remarks>
    public static IReadOnlyList<string> UnionCapabilities { get; } = [.. BaseCapabilities, StorageCapability];

    /// <summary>
    /// The keys that must survive into the child for a session to be the session
    /// it was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named rather than derived, and that is the point: derived from the
    /// generator, this list would shrink in step with a deleted key and the round
    /// trip would stay green while the opinion vanished. Written down, deleting a
    /// key turns the suite red — planted and reverted 2026-08-16, and the failure
    /// names the key.
    /// </para>
    /// <para>
    /// <b>Per family, because the two families require different keys and a
    /// union would assert a key that cannot exist.</b> Chromium requires the
    /// channel; Firefox has none and requires the restart-registration
    /// preference instead, which is the only thing standing between a Windows
    /// update and a resurrected browser no session claims.
    /// </para>
    /// <para>
    /// ⚠️ <b>The Firefox row's dotted path is ambiguous on purpose, and a reader
    /// following it key by key will not find it.</b> The preference's <i>name</i>
    /// contains dots, so <c>browser.launchOptions.firefoxUserPrefs.toolkit.
    /// winRegisterApplicationRestart</c> is four keys and a two-part leaf rather
    /// than six keys. That is what the flattener produces and what a
    /// set-membership check compares against; anything that <i>walks</i> a
    /// generated config by splitting on dots has to special-case it.
    /// </para>
    /// </remarks>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>The keys that family's config must carry.</returns>
    public static IReadOnlyList<string> RequiredSessionOpinions(string browser) =>
    [
        "browser.browserName",
        "browser.userDataDir",
        .. IsFirefox(browser)
            ? new[] { $"browser.launchOptions.firefoxUserPrefs.{FirefoxProfile.RestartRegistrationPreference}" }
            : ["browser.launchOptions.channel"],
        "browser.launchOptions.headless",
        "browser.launchOptions.downloadsPath",
        "capabilities",
        "outputDir",
        "saveSession",
        "console.level",
    ];

    /// <summary>The config one session's child is started with.</summary>
    /// <param name="session">The session directory, which is where every path below lives.</param>
    /// <param name="mode">The mode bound at <c>init</c>.</param>
    /// <param name="browser">
    /// The family this session was created for, read from its own
    /// <c>lock.json</c> rather than assumed. A profile belongs to the browser
    /// that made it, so generating a Chromium config for a session recorded as
    /// Firefox would point one browser at the other's profile — which upstream
    /// would launch, and which nothing would report.
    /// </param>
    /// <param name="tracing">Whether upstream records this session to the output directory.</param>
    /// <param name="consoleLevel">Which console messages the child returns.</param>
    /// <returns>The bytes to write, and every opinion they carry.</returns>
    /// <remarks>
    /// ⚠️ <b><c>tracing</c> maps to upstream's <c>saveSession</c>, because there
    /// is nothing else left to map it to.</b> Measured 2026-08-16 against
    /// <c>@playwright/mcp</c> 0.0.79: neither the CLI surface nor
    /// <c>config.d.ts</c> carries a trace option at all — <c>tracesDir</c> is
    /// computed internally as <c>&lt;outputDir&gt;/traces</c> and is not
    /// configurable — so [§C](../../../plan/C-sessions.md)'s <c>tracing</c> modifier
    /// has no upstream trace key to reach. <c>saveSession</c> is the surviving
    /// feature with the same purpose: it records what the session did into the
    /// output directory.
    /// </remarks>
    public static GeneratedConfig ForSession(
        SessionPath session,
        SessionModeDefinition mode,
        string browser,
        bool tracing,
        string consoleLevel)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentException.ThrowIfNullOrWhiteSpace(browser);

        return Generate(new BrowserConfigurationRequest
        {
            Browser = browser,
            Headless = !mode.Headed,
            UserDataDirectory = Path.Combine(session.FullPath, SessionLayout.ProfileFolderName),
            OutputDirectory = Path.Combine(session.FullPath, SessionLayout.OutputFolderName),
            DownloadsDirectory = Path.Combine(session.FullPath, SessionLayout.DownloadsFolderName),
            Capabilities = mode.Storage ? UnionCapabilities : BaseCapabilities,
            SaveSession = tracing,
            ConsoleLevel = consoleLevel,
        });
    }

    /// <summary>
    /// The config for the run's own child — the one that answers
    /// <c>tools/list</c> before any session exists.
    /// </summary>
    /// <remarks>
    /// <b>It carries the union capability set on purpose.</b> This child produces
    /// the one static tool list every caller sees, so it has to expose every tool
    /// any mode could reach. It also carries a <c>userDataDir</c> for a reason
    /// that has nothing to do with sessions: with the key unset, upstream writes
    /// each run's profile into <c>%LOCALAPPDATA%\ms-playwright-mcp\</c>, keyed by
    /// a hash of the client's working directory — 159 directories and 877 MB had
    /// accumulated on this machine before this step set the key.
    /// </remarks>
    /// <param name="instanceDirectory">This run's own directory.</param>
    /// <returns>The bytes to write, and every opinion they carry.</returns>
    public static GeneratedConfig ForSurface(string instanceDirectory)
    {
        ArgumentNullException.ThrowIfNull(instanceDirectory);

        return Generate(new BrowserConfigurationRequest
        {
            Headless = true,
            UserDataDirectory = Path.Combine(instanceDirectory, SessionLayout.ProfileFolderName),
            OutputDirectory = Path.Combine(instanceDirectory, SessionLayout.OutputFolderName),
            DownloadsDirectory = Path.Combine(instanceDirectory, SessionLayout.DownloadsFolderName),
            Capabilities = UnionCapabilities,
            SaveSession = false,
            ConsoleLevel = DefaultConsoleLevel,
        });
    }

    /// <summary>Writes a generated config, creating the directories it names.</summary>
    /// <param name="path">Where the config file goes. Overwritten if present.</param>
    /// <param name="config">What to write.</param>
    public static void WriteTo(string path, GeneratedConfig config)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(config);

        foreach (var directory in config.Directories)
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, config.Json);
    }

    /// <summary>Builds the config bytes and the list of opinions they carry.</summary>
    /// <param name="request">What this child is for.</param>
    /// <returns>The generated config.</returns>
    /// <exception cref="ArgumentException">A path is relative, or the console level is not one upstream accepts.</exception>
    public static GeneratedConfig Generate(BrowserConfigurationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Absolute(request.UserDataDirectory, nameof(request.UserDataDirectory));
        Absolute(request.OutputDirectory, nameof(request.OutputDirectory));
        Absolute(request.DownloadsDirectory, nameof(request.DownloadsDirectory));

        if (!ConsoleLevels.Contains(request.ConsoleLevel, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{request.ConsoleLevel}' is not a console level upstream accepts. Use one of: {string.Join(", ", ConsoleLevels)}.",
                nameof(request));
        }

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,

            // A Windows path is full of backslashes and a config file is read by
            // people. The default encoder additionally escapes characters a path
            // may legitimately contain, which round-trips perfectly and is
            // unreadable.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("browser");
            writer.WriteString("browserName", request.Browser);
            writer.WriteString("userDataDir", request.UserDataDirectory);

            writer.WriteStartObject("launchOptions");

            if (IsFirefox(request.Browser))
            {
                // ⚠️ The one lever that prevents browser resurrection rather
                // than cleaning up after it, and it is written on EVERY Firefox
                // launch. Upstream writes these into the profile's `user.js`
                // before the browser starts, so the preference is in force at
                // the moment `nsAppRunner` decides whether to register -- which
                // is why a pref delivered this way works where a runtime one
                // would be too late.
                writer.WriteStartObject("firefoxUserPrefs");
                writer.WriteBoolean(FirefoxProfile.RestartRegistrationPreference, false);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteString("channel", Channel);
            }

            writer.WriteBoolean("headless", request.Headless);
            writer.WriteString("downloadsPath", request.DownloadsDirectory);
            writer.WriteEndObject();

            writer.WriteEndObject();

            if (request.Capabilities.Count is not 0)
            {
                writer.WriteStartArray("capabilities");

                foreach (var capability in request.Capabilities)
                {
                    writer.WriteStringValue(capability);
                }

                writer.WriteEndArray();
            }

            writer.WriteString("outputDir", request.OutputDirectory);
            writer.WriteBoolean("saveSession", request.SaveSession);

            writer.WriteStartObject("console");
            writer.WriteString("level", request.ConsoleLevel);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        var json = buffer.ToArray();

        return new GeneratedConfig
        {
            Browser = request.Browser,
            ProfileDirectory = request.UserDataDirectory,
            Json = json,
            Opinions = Flatten(json),
            Directories =
            [
                request.UserDataDirectory,
                request.OutputDirectory,
                request.DownloadsDirectory,
            ],
        };
    }

    /// <summary>Whether a family name is Firefox's.</summary>
    /// <param name="browser">The family, as upstream names it.</param>
    /// <returns>Whether this is the Firefox family.</returns>
    public static bool IsFirefox(string browser) =>
        string.Equals(browser, ProvisionedBrowsers.Firefox, StringComparison.OrdinalIgnoreCase);

    private static void Absolute(string path, string name)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                $"'{path}' is not absolute, and every path in a generated config must be: upstream resolves a relative one against the config file's directory, which is not where the caller meant.",
                name);
        }
    }

    /// <summary>
    /// Every leaf of the generated config as a dotted path and a value, which is
    /// exactly what the round trip looks up in the child's answer.
    /// </summary>
    /// <remarks>
    /// Read back out of the bytes rather than accumulated while writing them, so
    /// the list cannot claim an opinion the file does not carry.
    /// </remarks>
    private static List<ConfigOpinion> Flatten(byte[] json)
    {
        var opinions = new List<ConfigOpinion>();
        Walk(JsonNode.Parse(json)!.AsObject(), string.Empty, opinions);
        return opinions;
    }

    private static void Walk(JsonObject node, string prefix, List<ConfigOpinion> opinions)
    {
        foreach (var (name, value) in node)
        {
            var path = prefix.Length is 0 ? name : $"{prefix}.{name}";

            if (value is JsonObject nested)
            {
                Walk(nested, path, opinions);
            }
            else if (value is not null)
            {
                // An array is one opinion rather than one per element: the whole
                // point of `capabilities` is that upstream replaces it wholesale,
                // so a per-element check would pass a list that had been merged
                // with something else.
                opinions.Add(new ConfigOpinion(path, value));
            }
        }
    }
}

/// <summary>What one child's config is for.</summary>
internal sealed record BrowserConfigurationRequest
{
    /// <summary>
    /// The browser family, as upstream names it.
    /// </summary>
    /// <remarks>
    /// Defaulted rather than required: every caller in this build asks for
    /// Chromium, and a required property would make the Firefox branch look like
    /// a decision each call site takes rather than a property of the session's
    /// own record.
    /// </remarks>
    public string Browser { get; init; } = BrowserConfiguration.BrowserName;

    /// <summary>Whether the browser runs without a window.</summary>
    /// <remarks>
    /// Written explicitly rather than omitted. Upstream fills an absent
    /// <c>headless</c> with <c>platform === "linux" &amp;&amp; !DISPLAY</c>, so
    /// on Windows "no key" means "a window appears" rather than "upstream
    /// decides". The assignment is guarded, so a value set here survives.
    /// </remarks>
    public required bool Headless { get; init; }

    /// <summary>The browser profile directory. Absolute.</summary>
    public required string UserDataDirectory { get; init; }

    /// <summary>Where the child writes artifacts. Absolute.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Where the browser puts downloads. Absolute.</summary>
    public required string DownloadsDirectory { get; init; }

    /// <summary>The capabilities to enable, beyond the unconditional <c>core*</c> family.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Whether upstream records the session into the output directory.</summary>
    public bool SaveSession { get; init; }

    /// <summary>Which console messages the child returns.</summary>
    public string ConsoleLevel { get; init; } = BrowserConfiguration.DefaultConsoleLevel;
}

/// <summary>A generated config: the bytes, and every opinion in them.</summary>
internal sealed record GeneratedConfig
{
    /// <summary>The browser family this config selects.</summary>
    /// <remarks>
    /// Carried on the config rather than re-derived by parsing the bytes back,
    /// so the one function every child launch passes through can ask which
    /// family it is about to start without a JSON read.
    /// </remarks>
    public required string Browser { get; init; }

    /// <summary>The profile directory this config points the browser at.</summary>
    /// <remarks>
    /// The same string as the first entry of <see cref="Directories"/>, named
    /// rather than indexed: the preflight has to open a file inside it, and a
    /// guard that depends on the order of a list is one reordering away from
    /// examining the downloads folder instead.
    /// </remarks>
    public required string ProfileDirectory { get; init; }

    /// <summary>The file's bytes, UTF-8, no BOM.</summary>
    public required byte[] Json { get; init; }

    /// <summary>Every leaf key and its value, for the <c>browser_get_config</c> round trip.</summary>
    public required IReadOnlyList<ConfigOpinion> Opinions { get; init; }

    /// <summary>The directories this config names, which must exist before the child starts.</summary>
    public required IReadOnlyList<string> Directories { get; init; }
}

/// <summary>One generated key, and what it was set to.</summary>
/// <param name="Path">The dotted path to the key, as it appears in the config object.</param>
/// <param name="Value">The value BrowserAI wrote.</param>
internal sealed record ConfigOpinion(string Path, JsonNode Value);
