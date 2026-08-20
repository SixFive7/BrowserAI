// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
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
/// <para>
/// <b><c>allowUnrestrictedFileAccess</c> is written <c>true</c> unconditionally,
/// with no argument to turn it off.</b> The maintainer's answer of 2026-08-20,
/// asked whether it should be always on, per mode or per call: <i>"a
/// always"</i>. Left unset it is upstream's default of <see langword="false"/>,
/// and that default is a <b>live regression</b> against every pre-BrowserAI way
/// of running this child: <c>checkFile</c> then refuses any path outside
/// <c>&lt;session&gt;\output</c> and the child's working directory, and
/// <c>checkUrlAllowed</c> refuses the <c>file:</c> protocol outright — so
/// <c>browser_file_upload</c> cannot reach a file the caller already has and
/// <c>browser_navigate</c> cannot open a local page at all.
/// <b>Upstream calls it a convenience defence rather than a secure
/// boundary</b>, in <c>config.d.ts</c>'s own words: <i>"a guardrail to prevent
/// the LLM from accidentally wandering outside its intended workspace … not a
/// secure boundary; a deliberate attempt to reach other directories can be
/// easily worked around, so always rely on client-level permissions for true
/// security."</i> BrowserAI's caller already holds file tools of its own — the
/// same reasoning that removed the <c>(tool, mode)</c> permission matrix on
/// 2026-08-18 — so what the guardrail withholds is reachable one tool call
/// away, and all it can do here is refuse the caller a thing it is entitled to
/// while proving nothing.
/// </para>
/// </remarks>
internal static class BrowserConfiguration
{
    /// <summary>The default browser family. Never left to upstream's default, which is Chrome.</summary>
    public const string BrowserName = ProvisionedBrowsers.Chromium;

    /// <summary>The folder a captured HTTP Archive is written into.</summary>
    /// <remarks>
    /// <c>output\network\</c>, which is already where BrowserAI's filename
    /// routing files anything a <c>network-</c> prefixed tool produces — so the
    /// archive sits beside the request and response bodies it duplicates rather
    /// than in a folder of its own.
    /// </remarks>
    public const string HarFolder = "network";

    /// <summary>The extension of a captured HTTP Archive.</summary>
    public const string HarExtension = ".har";

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

    /// <summary>
    /// The console level every session's child is launched with, and there is no
    /// argument to change it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Changed 2026-08-20 (previously <c>DefaultConsoleLevel = "info"</c>,
    /// upstream's own default, with a <c>consoleLevel</c> argument on
    /// <c>browserai_init</c> and <c>browserai_resume</c> offering all four
    /// levels).</b> The argument is deleted and the level is <c>debug</c>
    /// always.
    /// </para>
    /// <para>
    /// <b>Because the knob cost almost nothing to turn off and almost nothing to
    /// leave on.</b> Measured: moving from <c>error</c> to <c>debug</c> costs
    /// <b>+1 character</b> on a navigation response and <b>+5</b> otherwise,
    /// because the events line in a tool response is a <i>pointer</i> —
    /// <c>path#L1-L20</c> — and never the message text. The whole cost of the
    /// most verbose setting is the width of a larger line number.
    /// </para>
    /// <para>
    /// <b>And the read-level knob already exists, one layer up.</b>
    /// <c>browser_console_messages</c> takes its own level, so a caller that
    /// wants only errors asks for only errors — at the moment it asks, rather
    /// than having had to decide at <c>init</c> and discovered the loss
    /// afterwards. A capture level chosen hours earlier cannot be raised
    /// retroactively; a read level can always be lowered.
    /// </para>
    /// </remarks>
    public const string ConsoleLevel = "debug";

    /// <summary>
    /// The code-generation language, hard-coded to none.
    /// </summary>
    /// <remarks>
    /// <b>It strips a <c>### Ran Playwright code</c> block from every response,
    /// for a feature this product does not have.</b> Upstream emits the
    /// equivalent Playwright source beside each result so a caller can build a
    /// test out of a session; BrowserAI ships no test recorder, no
    /// <c>browser_start_codegen</c> and nothing that reads the block, so it is
    /// tokens spent on every single call for something no reader exists for.
    /// There is deliberately no argument: an option nobody can act on is a
    /// second state to test.
    /// </remarks>
    public const string Codegen = "none";

    /// <summary>
    /// The permissions every context is granted, hard-coded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>clipboard-read</c>, and nothing else.</b> Measured: the provisioned
    /// Chromium already grants <c>clipboard-write</c> to a page without asking,
    /// so naming it would be an opinion with no effect; <c>clipboard-read</c> is
    /// the one that prompts, and a prompt in a headless browser is a call that
    /// silently does nothing.
    /// </para>
    /// <para>
    /// ⚠️ <b>CHROMIUM ONLY, and this is measured rather than assumed.</b>
    /// Firefox does not know the permission at all: a context created with it
    /// fails at <c>initializeServer</c> with <c>Unknown permission:
    /// clipboard-read</c>, and the browser exits — so writing it for both
    /// families would have made <b>every Firefox session unusable</b>, not
    /// degraded. Measured 2026-08-20 against the provisioned
    /// <c>firefox-1539</c>, by writing it for both families and watching
    /// <c>FirefoxSessionTests</c> go red on a real front-door navigation. It is
    /// therefore family-scoped exactly as <see cref="Channel"/> is, and
    /// <see cref="RequiredSessionOpinions"/> requires it for Chromium only.
    /// </para>
    /// <para>
    /// <b>Nothing else is granted, and that is the decision.</b> Geolocation,
    /// notifications, camera and microphone all change what a page can do about
    /// the machine rather than about the page, and none of them is needed to read
    /// or drive one. A caller that needs one should have to ask for it, and
    /// nothing here offers a way — which is a limitation stated rather than a
    /// gap discovered.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Permissions { get; } = ["clipboard-read"];

    /// <summary>
    /// The key that lifts upstream's workspace guardrail, spelled once so the
    /// generator and the round trip cannot disagree about it.
    /// </summary>
    /// <remarks>
    /// It is a top-level key rather than one under <c>browser</c>, which is how
    /// upstream's <c>config.d.ts</c> declares it and how <c>checkFile</c> and
    /// <c>checkUrlAllowed</c> read it. The reasoning for setting it at all is on
    /// <see cref="BrowserConfiguration"/> itself.
    /// </remarks>
    public const string AllowUnrestrictedFileAccessKey = "allowUnrestrictedFileAccess";

    /// <summary>
    /// The capabilities every session gets — <b>all of them</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The MCP spec forbids the tool set varying per connection, and SEP-2567
    /// removed protocol-level sessions outright, so there is one static tool
    /// list and every session's child has to be able to answer all of it.
    /// </para>
    /// <para>
    /// ⚠️ <b>Changed 2026-08-20 (previously two lists — <c>BaseCapabilities</c>
    /// of <c>config</c>, <c>vision</c> and <c>devtools</c>, and
    /// <c>UnionCapabilities</c> which added <c>storage</c>; which of the two a
    /// session got was decided by its mode's <c>Storage</c> flag).</b> Session
    /// modes are gone, so there is no longer anything to decide <i>between</i>,
    /// and the honest list is the whole of what upstream offers. Three
    /// capabilities are granted here that no BrowserAI session has ever carried:
    /// <c>network</c> (4 tools), <c>pdf</c> (1) and <c>testing</c> (5). See
    /// <see cref="Sessions.SessionToolSurface"/> for what those ten are and why
    /// granting them is a decision rather than a consequence.
    /// </para>
    /// <para>
    /// <c>config</c> is what makes <c>browser_get_config</c> callable, and that
    /// tool is the only thing that can prove the rest of this file reached the
    /// child. The base 24 tools are unconditional — upstream ors
    /// <c>capability.startsWith("core")</c> with whatever is configured — so
    /// naming a <c>core*</c> capability here would do nothing, and
    /// <c>core-install</c> carries no tool at all.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> GrantedCapabilities { get; } =
        ["config", "vision", "devtools", "storage", "network", "pdf", "testing"];

    /// <summary>
    /// The default viewport, in CSS pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1920×1080, and the number that decided it is the token cost of a
    /// screenshot.</b> Measured end to end through BrowserAI: a full-page
    /// screenshot at this size arrives as <b>2,691 visual tokens</b>; 1280×720
    /// is 1,196 and 2560×1440 is <b>4,784 — exactly the API's per-image cap,
    /// with zero headroom</b>. So the largest size that is not on the edge of a
    /// hard limit is this one, and it is also the one a page's own desktop
    /// layout is designed for.
    /// </para>
    /// <para>
    /// ⚠️ <b>What arrives is what is set, unscaled, and that is specific to this
    /// product.</b> Upstream's <c>scaleImageToFitMessage</c> never runs here —
    /// BrowserAI's image handling diverges before it — so a caller that asks for
    /// 2560×1440 gets 2560×1440 worth of tokens rather than something downscaled
    /// on the way out. The argument exists and the description says what it
    /// costs.
    /// </para>
    /// </remarks>
    public static ViewportSize DefaultViewport { get; } = new(1920, 1080);

    /// <summary>
    /// The host machine's locale, as a BCP-47 tag.
    /// </summary>
    /// <remarks>
    /// <b>Read rather than hard-coded, because a hard-coded one is a lie about
    /// the machine.</b> Upstream leaves <c>locale</c> unset, which gives the
    /// browser's own default — for the provisioned Chromium that is
    /// <c>en-US</c> whatever the machine is, so a site that localises by
    /// <c>Accept-Language</c> shows an agent something a person at the same
    /// desk would never see. The argument overrides it for a caller who is
    /// deliberately testing another market.
    /// </remarks>
    public static string HostLocale { get; } = CultureInfo.CurrentCulture.Name is { Length: > 0 } name
        ? name
        : "en-US";

    /// <summary>
    /// The host machine's time zone as an IANA identifier, or
    /// <see langword="null"/> when Windows's own identifier cannot be converted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>IANA, because that is what Playwright accepts.</b>
    /// <c>TimeZoneInfo.Local.Id</c> on Windows is a Windows identifier —
    /// <c>W. Europe Standard Time</c> — and passing one to
    /// <c>contextOptions.timezoneId</c> is rejected by the browser at context
    /// creation, so the conversion is the whole of the work.
    /// </para>
    /// <para>
    /// <b>Null rather than a guess when the conversion fails.</b> The mapping
    /// comes from ICU, which a globalization-invariant build does not carry, and
    /// a Windows identifier written into the config would fail the launch rather
    /// than degrade. An absent key is upstream's own default, which is the
    /// machine's UTC offset — imperfect, and not a failure.
    /// </para>
    /// </remarks>
    public static string? HostTimeZone { get; } =
        TimeZoneInfo.Local.HasIanaId
            ? TimeZoneInfo.Local.Id
            : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : null;

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
        "browser.contextOptions.viewport.width",
        "browser.contextOptions.viewport.height",
        "browser.contextOptions.locale",
        "browser.contextOptions.ignoreHTTPSErrors",

        // Chromium only: Firefox rejects `clipboard-read` at context creation
        // and exits. See `Permissions`.
        .. IsFirefox(browser) ? [] : new[] { "browser.contextOptions.permissions" },

        // ⚠️ CONDITIONAL, AND IT IS A PROPERTY OF THE MACHINE RATHER THAN OF THE
        // SESSION. The Windows-to-IANA mapping comes from ICU; a
        // globalization-invariant host has none, and writing a Windows
        // identifier would fail the launch rather than degrade. So the key is
        // absent there, and requiring it unconditionally would make the round
        // trip red on a machine where the product is behaving correctly.
        .. HostTimeZone is null ? [] : new[] { "browser.contextOptions.timezoneId" },

        "capabilities",
        "outputDir",
        "saveSession",
        AllowUnrestrictedFileAccessKey,
        "console.level",
        "snapshot.boxes",
        "codegen",
    ];

    /// <summary>The config one session's child is started with.</summary>
    /// <param name="session">The session directory, which is where every path below lives.</param>
    /// <param name="headed">
    /// Whether a browser window appears. ⚠️ <b>A per-run argument since
    /// 2026-08-20 (previously <c>SessionModeDefinition mode</c>, whose
    /// <c>Headed</c> flag was bound at <c>init</c> and permanent for the
    /// directory's life).</b> Headedness is a property of <i>this launch</i>: it
    /// changes nothing on disk, so nothing is served by recording it, and a
    /// caller that wants to watch a session it created headless should not have
    /// to destroy it first.
    /// </param>
    /// <param name="browser">
    /// The family this session was created for, read from its own
    /// <c>browserai.json</c> rather than assumed. A profile belongs to the browser
    /// that made it, so generating a Chromium config for a session recorded as
    /// Firefox would point one browser at the other's profile — which upstream
    /// would launch, and which nothing would report.
    /// </param>
    /// <param name="tracing">Whether upstream records this session to the output directory.</param>
    /// <param name="run">The per-run arguments a caller gave for this launch.</param>
    /// <returns>The bytes to write, and every opinion they carry.</returns>
    /// <remarks>
    /// ⚠️ <b><c>tracing</c> maps to upstream's <c>saveSession</c>, because there
    /// is nothing else left to map it to.</b> Measured 2026-08-16 against
    /// <c>@playwright/mcp</c> 0.0.79: neither the CLI surface nor
    /// <c>config.d.ts</c> carries a trace option at all — <c>tracesDir</c> is
    /// computed internally as <c>&lt;outputDir&gt;/traces</c> and is not
    /// configurable — so BrowserAI's own <c>tracing</c> modifier has no upstream
    /// trace key to reach
    /// ([kb](../../../kb/playwright/configuration.md#defaults-that-are-not-what-they-look-like)).
    /// <c>saveSession</c> is the surviving
    /// feature with the same purpose: it records what the session did into the
    /// output directory.
    /// </remarks>
    public static GeneratedConfig ForSession(
        SessionPath session,
        bool headed,
        string browser,
        bool tracing,
        RunOptions run)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(browser);

        var output = Path.Combine(session.FullPath, SessionLayout.OutputFolderName);

        return Generate(new BrowserConfigurationRequest
        {
            Browser = browser,
            Headless = !headed,
            UserDataDirectory = Path.Combine(session.FullPath, SessionLayout.ProfileFolderName),
            OutputDirectory = output,
            DownloadsDirectory = Path.Combine(session.FullPath, SessionLayout.DownloadsFolderName),
            Capabilities = GrantedCapabilities,
            SaveSession = tracing,
            Viewport = run.Viewport,
            Locale = run.Locale,
            TimeZone = run.TimeZone,
            IgnoreHttpsErrors = run.IgnoreHttpsErrors,

            // ⚠️ A NEW FILENAME PER LAUNCH, and it is the whole reason the path
            // is computed here rather than fixed. `recordHar` truncates and
            // rewrites whatever path it is given at every context creation, so a
            // fixed name would silently destroy the previous run's capture the
            // moment a session was resumed -- an overwrite that a caller would
            // find out about by looking for evidence that had gone. The config
            // is regenerated per launch, so a timestamp in the name makes the
            // problem avoidable rather than documentable.
            HarPath = run.CaptureNetwork
                ? Path.Combine(output, HarFolder, $"network-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)}{HarExtension}")
                : null,
        });
    }

    /// <summary>
    /// The config for the run's own child — the one that answers
    /// <c>tools/list</c> before any session exists.
    /// </summary>
    /// <remarks>
    /// <b>It carries the same capability set every session gets.</b> This child
    /// produces the one static tool list every caller sees, so it has to expose
    /// every tool a session could reach — and since 2026-08-20 every session
    /// reaches all of them, so the two lists are the same list rather than one
    /// being the union of several. It also carries a <c>userDataDir</c> for a reason
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
            Capabilities = GrantedCapabilities,
            SaveSession = false,
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
    /// <exception cref="ArgumentException">A path is relative.</exception>
    public static GeneratedConfig Generate(BrowserConfigurationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Absolute(request.UserDataDirectory, nameof(request.UserDataDirectory));
        Absolute(request.OutputDirectory, nameof(request.OutputDirectory));
        Absolute(request.DownloadsDirectory, nameof(request.DownloadsDirectory));

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

            // `contextOptions` is `playwright.BrowserContextOptions` verbatim --
            // upstream passes it straight to `launchPersistentContext` -- so
            // every key here is Playwright's rather than upstream's, and none of
            // them appears in `config.d.ts` by name.
            writer.WriteStartObject("contextOptions");

            writer.WriteStartObject("viewport");
            writer.WriteNumber("width", request.Viewport.Width);
            writer.WriteNumber("height", request.Viewport.Height);
            writer.WriteEndObject();

            writer.WriteString("locale", request.Locale);

            if (request.TimeZone is { } zone)
            {
                writer.WriteString("timezoneId", zone);
            }

            writer.WriteBoolean("ignoreHTTPSErrors", request.IgnoreHttpsErrors);

            // ⚠️ CHROMIUM ONLY. Firefox rejects `clipboard-read` outright --
            // `Unknown permission: clipboard-read`, thrown at context creation,
            // with the browser exiting -- so writing it for both families does
            // not degrade a Firefox session, it makes every one of them
            // unusable. The same shape as `channel` two blocks up, for the same
            // kind of reason.
            if (!IsFirefox(request.Browser))
            {
                writer.WriteStartArray("permissions");

                foreach (var permission in Permissions)
                {
                    writer.WriteStringValue(permission);
                }

                writer.WriteEndArray();
            }

            if (request.HarPath is { } har)
            {
                // ⚠️ `serviceWorkers: "block"` IS NOT OPTIONAL BESIDE THIS, and
                // it is the half that makes the capture honest. A request served
                // out of a service worker's cache never reaches the network
                // layer the HAR is written from, so a page with a worker
                // produces an archive that is silently INCOMPLETE -- and
                // incomplete in the direction that matters, because the
                // requests a worker serves are the repeat ones a reader is
                // looking for. Blocking workers changes what the site does; the
                // description says so.
                writer.WriteString("serviceWorkers", "block");

                writer.WriteStartObject("recordHar");
                writer.WriteString("path", har);

                // `full` rather than `minimal`: minimal omits response bodies,
                // which is most of the reason to capture at all.
                writer.WriteString("mode", "full");

                // `embed` rather than `attach`: `attach` writes bodies as
                // separate files beside the archive, which turns one file a
                // caller can reason about -- and delete -- into a directory.
                writer.WriteString("content", "embed");
                writer.WriteEndObject();
            }

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

            // ⚠️ ALWAYS, AND THERE IS NO REQUEST FIELD TO TURN IT OFF. See the
            // type's remarks: the maintainer's answer was "a always", and
            // upstream's default of false is a live regression against every
            // pre-BrowserAI way of running this child. A field here would be a
            // knob nothing sets and a second state nothing tests.
            writer.WriteBoolean(AllowUnrestrictedFileAccessKey, true);

            writer.WriteStartObject("console");
            writer.WriteString("level", ConsoleLevel);
            writer.WriteEndObject();

            // ⚠️ ALWAYS TRUE, AND THE COST IS DEFERRED RATHER THAN PAID. A
            // snapshot response carries a LINK to the file rather than the
            // snapshot text, so boxes cost nothing until something reads it --
            // and BrowserAI grants the `vision` capability to every session,
            // whose six `browser_mouse_*_xy` tools take viewport coordinates
            // that a snapshot without boxes gives a model no way to compute.
            // Granting the tools and withholding the numbers they need would be
            // a surface that looks complete and is not.
            writer.WriteStartObject("snapshot");
            writer.WriteBoolean("boxes", true);
            writer.WriteEndObject();

            // See the constant: it strips a `### Ran Playwright code` block from
            // every response, for a feature this product does not have.
            writer.WriteString("codegen", Codegen);

            writer.WriteEndObject();
        }

        var json = buffer.ToArray();

        return new GeneratedConfig
        {
            Browser = request.Browser,
            ProfileDirectory = request.UserDataDirectory,
            HarPath = request.HarPath,
            Json = json,
            Opinions = Flatten(json),
            Directories =
            [
                request.UserDataDirectory,
                request.OutputDirectory,
                request.DownloadsDirectory,

                // The archive's own folder, so that a capture whose directory
                // does not exist yet is not a launch failure. Playwright creates
                // it, and creating it here means the ONE place that creates the
                // config's directories creates all of them.
                .. request.HarPath is { } archive && Path.GetDirectoryName(archive) is { Length: > 0 } folder
                    ? new[] { folder }
                    : [],
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
    /// Defaulted rather than required, and the default is the same one
    /// <c>browserai_init</c> applies. ⚠️ <b>Corrected 2026-08-19 (previously
    /// "every caller in this build asks for Chromium").</b> That stopped being
    /// true when Firefox was offered — <see cref="BrowserConfiguration.ForSession"/> passes whatever
    /// the session's <c>browserai.json</c> records. The default survives for the
    /// reason it always had: it keeps the Firefox branch a property of the
    /// session's own record rather than a decision each call site takes, and
    /// <see cref="BrowserConfiguration.ForSurface"/> — the run's own browser-less child — genuinely
    /// has no family to state.
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

    /// <summary>The viewport every page in this context gets.</summary>
    public ViewportSize Viewport { get; init; } = BrowserConfiguration.DefaultViewport;

    /// <summary>The BCP-47 locale the browser reports and formats with.</summary>
    public string Locale { get; init; } = BrowserConfiguration.HostLocale;

    /// <summary>
    /// The IANA time zone the browser reports, or <see langword="null"/> to leave
    /// upstream's default in place.
    /// </summary>
    public string? TimeZone { get; init; } = BrowserConfiguration.HostTimeZone;

    /// <summary>Whether TLS errors are ignored for every navigation in this context.</summary>
    public bool IgnoreHttpsErrors { get; init; }

    /// <summary>
    /// Where the HTTP Archive goes, or <see langword="null"/> for no capture.
    /// </summary>
    /// <remarks>
    /// <b>A path rather than a boolean, because the path is per launch.</b>
    /// <c>recordHar</c> truncates whatever it is given at every context
    /// creation, so the name carries a timestamp and the decision about what it
    /// is called belongs to the caller that knows which launch this is.
    /// </remarks>
    public string? HarPath { get; init; }
}

/// <summary>
/// A viewport, in CSS pixels.
/// </summary>
/// <param name="Width">How wide.</param>
/// <param name="Height">How tall.</param>
internal sealed record ViewportSize(int Width, int Height)
{
    /// <summary>The smallest side either dimension may be.</summary>
    /// <remarks>
    /// <b>A floor rather than a validation of taste.</b> A viewport of a few
    /// pixels is a page that lays out as nothing, and the failure presents as a
    /// screenshot of an empty box rather than as a refusal.
    /// </remarks>
    public const int Smallest = 200;

    /// <summary>
    /// The largest side either dimension may be.
    /// </summary>
    /// <remarks>
    /// <b>4,096, and it is about tokens rather than about the browser.</b> A
    /// screenshot arrives unscaled — upstream's <c>scaleImageToFitMessage</c>
    /// never runs here — so a viewport past this is an image the API refuses
    /// rather than shrinks, and the failure lands on the call after the one that
    /// set it.
    /// </remarks>
    public const int Largest = 4096;

    /// <summary>How it is written and how it is read: <c>WIDTHxHEIGHT</c>.</summary>
    /// <returns>The size as a caller writes it.</returns>
    public override string ToString() =>
        $"{Width.ToString(CultureInfo.InvariantCulture)}x{Height.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Reads a <c>WIDTHxHEIGHT</c> string.</summary>
    /// <param name="text">The value as the caller wrote it.</param>
    /// <param name="size">The size, when it parsed.</param>
    /// <returns>Whether it parsed and is within the bounds above.</returns>
    public static bool TryParse(string? text, out ViewportSize size)
    {
        size = BrowserConfiguration.DefaultViewport;

        if (text is null)
        {
            return false;
        }

        var parts = text.Split('x', StringSplitOptions.TrimEntries);

        if (parts.Length is not 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            || width is < Smallest or > Largest
            || height is < Smallest or > Largest)
        {
            return false;
        }

        size = new ViewportSize(width, height);
        return true;
    }
}

/// <summary>
/// The per-run arguments a caller gives one launch of a session.
/// </summary>
/// <remarks>
/// <b>Every one of these is regenerated at every child launch and none is
/// written to the session record</b> — the same rule <c>headed</c>, <c>tracing</c>
/// and <c>debug</c> follow. A session created at one viewport is resumed at
/// another without being destroyed first, and nothing on disk differs between
/// the two.
/// </remarks>
internal sealed record RunOptions
{
    /// <summary>The defaults, for a call that named none of them.</summary>
    public static RunOptions Default { get; } = new();

    /// <summary>The viewport every page gets.</summary>
    public ViewportSize Viewport { get; init; } = BrowserConfiguration.DefaultViewport;

    /// <summary>The BCP-47 locale, defaulting to the host machine's.</summary>
    public string Locale { get; init; } = BrowserConfiguration.HostLocale;

    /// <summary>The IANA time zone, defaulting to the host machine's.</summary>
    public string? TimeZone { get; init; } = BrowserConfiguration.HostTimeZone;

    /// <summary>Whether TLS errors are ignored.</summary>
    public bool IgnoreHttpsErrors { get; init; }

    /// <summary>Whether this launch writes an HTTP Archive.</summary>
    public bool CaptureNetwork { get; init; }
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

    /// <summary>
    /// Where this launch's HTTP Archive goes, or <see langword="null"/> when
    /// nothing is being captured.
    /// </summary>
    /// <remarks>
    /// Carried on the config so the answer <c>browserai_init</c> returns can name
    /// the file. A caller that turned network capture on has created a plaintext
    /// credential dump, and being told its path in the same answer is the
    /// difference between a fact it can act on and one it has to go looking for.
    /// </remarks>
    public string? HarPath { get; init; }

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
