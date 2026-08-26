// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The per-run arguments, and the four opinions that stopped being arguments.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asserted on the generated config rather than on the schema.</b> A
/// parameter a model can see and the product ignores is the failure this file
/// exists to catch, so every arm below reads the bytes the child is started
/// with — through <c>BrowserConfiguration.ForSession</c>, which is what
/// <c>OpenAsync</c> calls, or through the front door where the value has to
/// survive argument parsing as well.
/// </para>
/// <para>
/// <b>That the child HONOURS them is a different test and it already exists.</b>
/// <c>ConfigRoundTripTests.EveryGeneratedOpinionComesBackFromTheChild</c> reads
/// every generated key back out of a live browser, and
/// <c>BrowserConfiguration.RequiredSessionOpinions</c> names the ones whose
/// disappearance is a red build — so a key added here and dropped by upstream's
/// own merge is caught there rather than here.
/// </para>
/// </remarks>
internal sealed partial class RunOptionTests
{
    [Test]
    public async Task TheDefaultViewportIsTheOneMeasuredAgainstTheImageCap()
    {
        var opinions = OpinionsOf(RunOptions.Default);

        await Assert.That(opinions["browser.contextOptions.viewport.width"]).IsEqualTo("1920");
        await Assert.That(opinions["browser.contextOptions.viewport.height"]).IsEqualTo("1080");

        // And the product's own constant agrees with the number above, so the
        // description a model reads and the config a browser gets cannot drift.
        await Assert.That(BrowserConfiguration.DefaultViewport.ToString()).IsEqualTo("1920x1080");
    }

    /// <summary>A viewport a caller named reaches the config, and a bad one is refused.</summary>
    /// <param name="written">The value as a caller writes it.</param>
    /// <param name="width">The width it should become, or 0 when it must be refused.</param>
    /// <param name="height">The height it should become, or 0 when it must be refused.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments("1280x720", 1280, 720)]
    [Arguments("2560x1440", 2560, 1440)]
    [Arguments(" 800 x 600 ", 800, 600)]
    [Arguments("1920", 0, 0)]
    [Arguments("1920x", 0, 0)]
    [Arguments("0x0", 0, 0)]
    [Arguments("100x100", 0, 0)]
    [Arguments("8000x1080", 0, 0)]
    [Arguments("-1920x-1080", 0, 0)]
    [Arguments("1920x1080x720", 0, 0)]
    public async Task AViewportIsParsedOrRefusedAndNeverRounded(string written, int width, int height)
    {
        var parsed = ViewportSize.TryParse(written, out var size);

        if (width is 0)
        {
            // ⚠️ REFUSED RATHER THAN CLAMPED. A caller that wrote something
            // meant something, and a server that silently substituted the
            // default would answer every later question about the page at a size
            // nobody chose.
            await Assert.That(parsed).IsFalse();
            return;
        }

        await Assert.That(parsed).IsTrue();
        await Assert.That(size.Width).IsEqualTo(width);
        await Assert.That(size.Height).IsEqualTo(height);
    }

    [Test]
    public async Task LocaleAndTimeZoneComeFromTheHostAndAnArgumentOverridesThem()
    {
        var host = OpinionsOf(RunOptions.Default);

        await Assert.That(host["browser.contextOptions.locale"]).IsEqualTo($"\"{BrowserConfiguration.HostLocale}\"");

        // The host locale is the machine's rather than a literal, which is the
        // whole claim — asserted against the framework rather than against a
        // string this file also chose.
        await Assert.That(BrowserConfiguration.HostLocale)
            .IsEqualTo(System.Globalization.CultureInfo.CurrentCulture.Name);

        // ⚠️ IANA RATHER THAN WINDOWS, which is what Playwright accepts. On a
        // host whose Windows identifier cannot be converted the key is absent —
        // an absent key is upstream's default, where a Windows identifier would
        // fail the launch.
        if (BrowserConfiguration.HostTimeZone is { } zone)
        {
            await Assert.That(host["browser.contextOptions.timezoneId"]).IsEqualTo($"\"{zone}\"");
            await Assert.That(zone).Contains("/");
            await Assert.That(zone).DoesNotContain("Standard Time");
        }
        else
        {
            await Assert.That(host.ContainsKey("browser.contextOptions.timezoneId")).IsFalse();
        }

        var overridden = OpinionsOf(RunOptions.Default with { Locale = "de-DE", TimeZone = "America/New_York" });

        await Assert.That(overridden["browser.contextOptions.locale"]).IsEqualTo("\"de-DE\"");
        await Assert.That(overridden["browser.contextOptions.timezoneId"]).IsEqualTo("\"America/New_York\"");
    }

    [Test]
    public async Task TlsErrorsAreNotIgnoredUnlessAskedFor()
    {
        await Assert.That(OpinionsOf(RunOptions.Default)["browser.contextOptions.ignoreHTTPSErrors"]).IsEqualTo("false");

        await Assert.That(OpinionsOf(RunOptions.Default with { IgnoreHttpsErrors = true })["browser.contextOptions.ignoreHTTPSErrors"])
            .IsEqualTo("true");
    }

    /// <summary>
    /// Network capture writes a HAR, blocks service workers, and names the file
    /// after this launch.
    /// </summary>
    /// <remarks>
    /// <b><c>serviceWorkers: "block"</c> is not optional beside <c>recordHar</c>
    /// and this is the arm that says so.</b> A request served out of a worker's
    /// cache never reaches the network layer the archive is written from, so
    /// without the block the capture is silently incomplete — incomplete in the
    /// direction that matters, because the requests a worker serves are the
    /// repeat ones a reader is looking for. The two are asserted together
    /// because separating them is exactly the edit that would look harmless.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NetworkCaptureWritesAHarAndBlocksServiceWorkers()
    {
        var off = OpinionsOf(RunOptions.Default);

        await Assert.That(off.Keys.Any(key => key.Contains("recordHar", StringComparison.Ordinal))).IsFalse();
        await Assert.That(off.ContainsKey("browser.contextOptions.serviceWorkers")).IsFalse();

        var on = OpinionsOf(RunOptions.Default with { CaptureNetwork = true });

        await Assert.That(on["browser.contextOptions.serviceWorkers"]).IsEqualTo("\"block\"");
        await Assert.That(on["browser.contextOptions.recordHar.mode"]).IsEqualTo("\"full\"");
        await Assert.That(on["browser.contextOptions.recordHar.content"]).IsEqualTo("\"embed\"");
    }

    /// <summary>
    /// Every launch gets its own archive filename, at the output root, because
    /// <c>recordHar</c> truncates whatever path it is given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The timestamp IS the mechanism, so the format is what is asserted.</b>
    /// A fixed name would destroy the previous run's capture the moment a
    /// session was resumed — an overwrite a caller would discover by looking for
    /// evidence that had gone. Two configs generated in the same millisecond
    /// would collide and a test cannot rule that out; what it can rule out is the
    /// version with no timestamp at all, which is the one anybody would write.
    /// </para>
    /// <para>
    /// ⚠️ <b>At the output root since 2026-08-26 (previously
    /// <c>output\network\</c>, "which is already where BrowserAI's filename
    /// routing files anything a <c>network-</c> prefixed tool produces").</b>
    /// There is no filename routing and there are no typed folders, so the
    /// folder that sentence pointed at does not exist — and the HAR is the one
    /// artifact whose directory BrowserAI still chooses, because it is a
    /// launch-time config value rather than something a tool names. It goes
    /// where everything else the session writes goes: <c>output\</c>, flat, as
    /// the child leaves it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryLaunchGetsItsOwnArchiveFilenameAtTheOutputRoot()
    {
        var session = SessionPath.For(Path.Combine(ScratchRoot.Path, $"har-{Guid.NewGuid():N}"));

        var config = BrowserConfiguration.ForSession(
            session,
            headed: false,
            SessionManager.DefaultBrowser,
            tracing: false,
            RunOptions.Default with { CaptureNetwork = true });

        await Assert.That(config.HarPath).IsNotNull();

        // The output root itself, not a folder under it: one path segment before
        // the file name, and that segment is `output`.
        await Assert.That(Path.GetDirectoryName(config.HarPath!))
            .IsEqualTo(Path.Combine(session.FullPath, SessionLayout.OutputFolderName));

        await Assert.That(TimestampedArchive().IsMatch(Path.GetFileName(config.HarPath!))).IsTrue();

        // The folder is created with the rest of the config's directories, so a
        // capture whose folder did not exist yet is not a launch failure.
        await Assert.That(config.Directories).Contains(Path.GetDirectoryName(config.HarPath!)!);
    }

    /// <summary>
    /// The four opinions that stopped being arguments, and the one that is
    /// family-scoped.
    /// </summary>
    /// <remarks>
    /// <b>Hard-coded is a claim about every session, so it is asserted over
    /// both families and both headednesses.</b> A value written on one branch of
    /// the generator and not the other reads as hard-coded and is not.
    /// </remarks>
    /// <param name="browser">The family.</param>
    /// <param name="headed">Whether a window appears.</param>
    /// <returns>The assertion task.</returns>
    [Test]
    [Arguments(ProvisionedBrowsers.Chromium, false)]
    [Arguments(ProvisionedBrowsers.Chromium, true)]
    [Arguments(ProvisionedBrowsers.Firefox, false)]
    [Arguments(ProvisionedBrowsers.Firefox, true)]
    public async Task TheHardCodedOpinionsAreTheSameForEverySession(string browser, bool headed)
    {
        var opinions = BrowserConfiguration.ForSession(
                SessionPath.For(Path.Combine(ScratchRoot.Path, $"hard-coded-{browser}-{headed}")),
                headed,
                browser,
                tracing: false,
                RunOptions.Default)
            .Opinions.ToDictionary(opinion => opinion.Path, opinion => opinion.Value.ToJsonString(), StringComparer.Ordinal);

        // The console level: always `debug`, and there is no argument. Measured:
        // `error` to `debug` costs +1 character on a navigation response and +5
        // otherwise, because the events line is a POINTER rather than the text.
        await Assert.That(opinions["console.level"]).IsEqualTo("\"debug\"");

        // Code generation: off. It strips a `### Ran Playwright code` block from
        // every response for a feature this product does not have.
        await Assert.That(opinions["codegen"]).IsEqualTo("\"none\"");

        // Snapshot boxes: on. The cost is deferred — a response carries a link
        // rather than the snapshot — and every session is granted `vision`,
        // whose six coordinate tools are unusable without them.
        await Assert.That(opinions["snapshot.boxes"]).IsEqualTo("true");

        // ⚠️ AND THE ONE THAT IS NOT THE SAME FOR EVERY SESSION, measured
        // 2026-08-20: Firefox fails at `initializeServer` with `Unknown
        // permission: clipboard-read` and the browser exits, so writing it for
        // both families makes every Firefox session unusable rather than
        // degraded. Family-scoped exactly as `channel` is.
        if (BrowserConfiguration.IsFirefox(browser))
        {
            await Assert.That(opinions.ContainsKey("browser.contextOptions.permissions")).IsFalse();
        }
        else
        {
            await Assert.That(opinions["browser.contextOptions.permissions"]).IsEqualTo("""["clipboard-read"]""");
        }
    }

    /// <summary>
    /// The arguments survive the front door, and a bad viewport is refused
    /// there with a sentence naming the form.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheArgumentsSurviveTheFrontDoorAndABadViewportIsRefused()
    {
        await using var sessions = RigSessionEnvironment.Create(opensDefaultSession: false);
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "front-door-run-options");

        var opened = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session opened with every per-run argument set",
            ["viewport"] = "1280x720",
            ["locale"] = "de-DE",
            ["timezone"] = "America/New_York",
            ["ignoreHTTPSErrors"] = true,
            ["captureNetwork"] = true,
        });

        await Assert.That((bool?)opened["isError"]).IsNotEqualTo(true);

        var text = TextOf(opened);

        // The answer says what a screenshot will cost and that a plaintext
        // credential file is being written, because both are consequences the
        // caller has just chosen and neither is visible anywhere else.
        await Assert.That(text).Contains("viewport: 1280x720");
        await Assert.That(text).Contains("NETWORK CAPTURE IS ON");
        await Assert.That(text).Contains("Service workers are BLOCKED");

        var config = ConfigOf(sessions, directory);

        await Assert.That((int?)config["browser"]?["contextOptions"]?["viewport"]?["width"]).IsEqualTo(1280);
        await Assert.That((string?)config["browser"]?["contextOptions"]?["locale"]).IsEqualTo("de-DE");
        await Assert.That((string?)config["browser"]?["contextOptions"]?["timezoneId"]).IsEqualTo("America/New_York");
        await Assert.That((bool?)config["browser"]?["contextOptions"]?["ignoreHTTPSErrors"]).IsTrue();
        await Assert.That((string?)config["browser"]?["contextOptions"]?["serviceWorkers"]).IsEqualTo("block");

        // The refusal, from the front door, naming the form and the bounds.
        var refused = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = Path.Combine(sessions.Root, "never-created"),
            ["purpose"] = "should never be created",
            ["viewport"] = "enormous",
        });

        await Assert.That((bool?)refused["isError"]).IsTrue();
        await Assert.That(TextOf(refused)).Contains("WIDTHxHEIGHT");
        await Assert.That(TextOf(refused)).Contains("Nothing was created");
        await Assert.That(Directory.Exists(Path.Combine(sessions.Root, "never-created"))).IsFalse();
    }

    /// <summary>The generated config for one set of run options, flattened.</summary>
    /// <param name="run">What the launch was asked for.</param>
    /// <returns>Dotted key to serialised value.</returns>
    private static Dictionary<string, string> OpinionsOf(RunOptions run) =>
        BrowserConfiguration.ForSession(
                SessionPath.For(Path.Combine(ScratchRoot.Path, $"run-options-{Guid.NewGuid():N}")),
                headed: false,
                SessionManager.DefaultBrowser,
                tracing: false,
                run)
            .Opinions.ToDictionary(opinion => opinion.Path, opinion => opinion.Value.ToJsonString(), StringComparer.Ordinal);

    /// <summary>
    /// The config file the rig generated for one session, read off disk.
    /// </summary>
    /// <remarks>
    /// <b>Off disk rather than out of the generator</b>, because what is being
    /// asserted is that the argument survived parsing and reached the file the
    /// child is started with — a check against the generator would pass for a
    /// front door that dropped the argument on the way in.
    /// </remarks>
    /// <param name="sessions">The rig.</param>
    /// <param name="directory">The session directory.</param>
    /// <returns>The parsed config.</returns>
    private static JsonObject ConfigOf(RigSessionEnvironment sessions, string directory)
    {
        var hash = SessionPath.For(directory).Hash[..16];
        var file = Directory.EnumerateFiles(sessions.Environment.InstanceDirectory, $"playwright-mcp-{hash}.json").Single();

        return JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    }

    [GeneratedRegex(@"^network-\d{8}-\d{9}\.har$")]
    private static partial Regex TimestampedArchive();

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
