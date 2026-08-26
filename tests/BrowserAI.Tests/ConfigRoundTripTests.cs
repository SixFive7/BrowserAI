// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Every opinion BrowserAI generates is read back out of the running child.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generating a key is not the same as the child honouring it, and the
/// failure is silent.</b> <c>loadConfig</c> is a bare <c>JSON.parse</c> with no
/// schema validation, so a renamed or removed key is discarded without a word —
/// <c>--output-mode</c> was a no-op for its entire life and nobody noticed. This
/// is the test that makes a key we set and the child ignores a red build rather
/// than a mystery in production.
/// </para>
/// <para>
/// <b>Two halves, and both are needed.</b> The first walks every leaf of the
/// generated file and requires it back, which catches a key the child drops. The
/// second requires a <i>named</i> list of keys to be in the generated file at
/// all, which catches a key the generator stops writing — deleting one from
/// <see cref="BrowserConfiguration"/> would otherwise remove it from both sides
/// of the first comparison and leave this green.
/// </para>
/// </remarks>
internal sealed class ConfigRoundTripTests
{
    [Test]
    public async Task EveryGeneratedOpinionComesBackFromTheChild()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SessionRun.SharedAsync();
        var resolved = ResolvedConfig(run);
        var missing = new List<string>();

        foreach (var opinion in Expected(run).Opinions)
        {
            var actual = Follow(resolved, opinion.Path);

            if (actual is null)
            {
                missing.Add($"{opinion.Path}: absent from the child's resolved config");
                continue;
            }

            if (!string.Equals(actual.ToJsonString(), opinion.Value.ToJsonString(), StringComparison.Ordinal))
            {
                missing.Add($"{opinion.Path}: generated {opinion.Value.ToJsonString()}, child resolved {actual.ToJsonString()}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, missing)).IsEmpty();
    }

    /// <summary>
    /// The generator still writes every key a session depends on.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately independent of the published binary and of any process.</b>
    /// This is a statement about the generator rather than about a run, and
    /// keeping it so is what makes "delete one key and watch it go red" a check
    /// anyone can perform in one build rather than one build plus a minute of
    /// ILC.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheGeneratorStillWritesEveryKeyASessionDependsOn()
    {
        // Both families, because they require different keys and each list is
        // only checked against a config generated for that family. Chromium
        // needs the channel; Firefox has none and needs the
        // restart-registration preference, which is the only thing standing
        // between a Windows update and a resurrected browser.
        foreach (var browser in ProvisionedBrowsers.Families)
        {
            var config = BrowserConfiguration.ForSession(
                SessionPath.For(Path.Combine(ScratchRoot.Path, "generator-shape")),
                headed: false,
                browser,
                tracing: true,
                RunOptions.Default);

            var written = config.Opinions
                .Select(opinion => opinion.Path)
                .ToHashSet(StringComparer.Ordinal);

            // This is the half that turns "delete one key from the generator"
            // red. The list is written down rather than derived, because a
            // derived list shrinks in step with the deletion.
            var dropped = BrowserConfiguration.RequiredSessionOpinions(browser)
                .Where(path => !written.Contains(path))
                .ToList();

            await Assert.That(string.Join(", ", dropped)).IsEmpty();
        }
    }

    [Test]
    public async Task TheChildResolvesOurChannelAndOurProfileRatherThanADefault()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SessionRun.SharedAsync();
        var resolved = ResolvedConfig(run);
        var session = Path.Combine(run.Root, "alpha");

        // The channel as the CHILD reports it, which is the half a process-table
        // reading cannot give: `validateBrowserConfig` fills in chrome + the
        // user's own Google Chrome when browserName is absent, and drops the
        // channel entirely for a non-chromium browserName.
        await Assert.That((string?)Follow(resolved, "browser.launchOptions.channel")).IsEqualTo(BrowserConfiguration.Channel);
        await Assert.That((string?)Follow(resolved, "browser.browserName")).IsEqualTo(BrowserConfiguration.BrowserName);

        // The profile is inside the session directory rather than in
        // %LOCALAPPDATA%\ms-playwright-mcp, which is what the key exists for.
        await Assert.That((string?)Follow(resolved, "browser.userDataDir"))
            .IsEqualTo(Path.Combine(session, SessionLayout.ProfileFolderName));

        await Assert.That((string?)Follow(resolved, "outputDir"))
            .IsEqualTo(Path.Combine(session, SessionLayout.OutputFolderName));

        // `capabilities` replaces rather than merges, so it arriving intact is
        // also the evidence that nothing on the way in wiped it.
        await Assert.That(Follow(resolved, "capabilities")?.ToJsonString())
            .IsEqualTo(new JsonArray([.. BrowserConfiguration.GrantedCapabilities.Select(capability => (JsonNode)capability)]).ToJsonString());

        // And the browser really used the directory, rather than the key merely
        // surviving the merge.
        await Assert.That(run.ProfileWasUsed).IsTrue();
    }

    /// <summary>
    /// Upstream's workspace guardrail is switched on <b>explicitly</b> in every
    /// config this product generates, with no argument that can turn it off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Inverted 2026-08-26 (previously
    /// <c>EveryGeneratedConfigLiftsUpstreamsWorkspaceGuardrail</c>, requiring
    /// <see langword="true"/> everywhere on the maintainer's 2026-08-20 answer
    /// of "a always").</b> The guardrail is BrowserAI's containment now.
    /// BrowserAI's own <c>filename</c> gate is deleted — nothing validates,
    /// rewrites or bounds a caller's path any more — so upstream's file-access
    /// roots are the only thing left between a caller's string and the
    /// filesystem, and a config that lifted them would leave nothing at all.
    /// </para>
    /// <para>
    /// <b>Written rather than merely omitted, and that is the assertion.</b>
    /// <see langword="false"/> is upstream's default, so leaving the key out
    /// produces the same behaviour and says nothing about whether anybody chose
    /// it — and <see cref="EveryGeneratedOpinionComesBackFromTheChild"/> can
    /// only prove the child honoured an opinion the file actually carries. An
    /// absent key is therefore a failure here exactly as a <see langword="true"/>
    /// one is, which is the both-directions half.
    /// </para>
    /// <para>
    /// <b>What it costs is stated where a reader meets it:</b> <c>file:</c>
    /// navigation is refused outright and <c>browser_file_upload</c> can no
    /// longer reach a file outside the session's output directory. That trade is
    /// the maintainer's, taken knowingly — see <c>BrowserConfiguration</c>.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryGeneratedConfigKeepsUpstreamsFileAccessRootsAndSaysSoExplicitly()
    {
        var refused = new List<string>();

        foreach (var browser in ProvisionedBrowsers.Families)
        {
            // ⚠️ Both headednesses rather than both modes, 2026-08-20. Session
            // modes are gone; what a session's config still varies on is the
            // window, and it varies per RUN rather than per directory.
            foreach (var headed in new[] { false, true })
            {
                var config = BrowserConfiguration.ForSession(
                    SessionPath.For(Path.Combine(ScratchRoot.Path, "unrestricted-file-access")),
                    headed,
                    browser,
                    tracing: false,
                    RunOptions.Default);

                check($"{browser}/headed={headed}", config);
            }
        }

        check("surface", BrowserConfiguration.ForSurface(Path.Combine(ScratchRoot.Path, "unrestricted-file-access-surface")));

        await Assert.That(string.Join(Environment.NewLine, refused)).IsEmpty();

        void check(string what, GeneratedConfig config)
        {
            var opinion = config.Opinions
                .FirstOrDefault(entry => string.Equals(entry.Path, BrowserConfiguration.AllowUnrestrictedFileAccessKey, StringComparison.Ordinal));

            if (opinion is null)
            {
                refused.Add($"{what}: '{BrowserConfiguration.AllowUnrestrictedFileAccessKey}' is not written at all. Upstream's default is false and the behaviour would be the same, but nothing then says BrowserAI chose it and the round trip has no opinion to read back — an omission is not a decision");
                return;
            }

            if (opinion.Value.ToJsonString() is not "false")
            {
                refused.Add($"{what}: '{BrowserConfiguration.AllowUnrestrictedFileAccessKey}' was written {opinion.Value.ToJsonString()} rather than false, which lifts the only containment left now that BrowserAI's own filename gate is gone");
            }
        }
    }

    [Test]
    public async Task NothingCanReplaceTheCapabilityListOnTheWayIn()
    {
        // `--caps` and PLAYWRIGHT_MCP_CAPS both REPLACE the config file's
        // capability list rather than merging with it, so either one silently
        // shrinks the tool surface with no error anywhere. The flag is never
        // built and the variable is refused by name -- absent because it is
        // refused, rather than absent because nobody added it.
        await Assert.That(ChildEnvironment.Refused.Contains("PLAYWRIGHT_MCP_CAPS")).IsTrue();
        await Assert.That(ChildEnvironment.Build().ContainsKey("PLAYWRIGHT_MCP_CAPS")).IsFalse();

        var offenders = new List<string>();

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var code = await RepositoryLayout.ReadCodeAsync(file);

            if (code.Contains("\"--caps\"", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName));
            }
        }

        await Assert.That(string.Join(", ", offenders)).IsEmpty();
    }

    /// <summary>
    /// The config the product generated for the run's first session, rebuilt from
    /// the same generator with the same arguments.
    /// </summary>
    private static GeneratedConfig Expected(SessionRun run) =>
        BrowserConfiguration.ForSession(
            SessionPath.For(Path.Combine(run.Root, "alpha")),
            headed: false,
            SessionManager.DefaultBrowser,
            tracing: false,
            RunOptions.Default);

    /// <summary>The child's own merged config, as <c>browser_get_config</c> reported it.</summary>
    /// <remarks>
    /// The tool's own body is <c>JSON.stringify(context.config, null, 2)</c>, but
    /// upstream's response builder wraps every text section in a
    /// <c>### &lt;title&gt;</c> heading before it reaches the wire, so the answer
    /// is Markdown with JSON inside it. Sliced from the first brace to the last
    /// rather than parsed as a whole — a heading cannot contain one.
    /// </remarks>
    private static JsonObject ResolvedConfig(SessionRun run)
    {
        var text = run.Text("getConfig");
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');

        return (start >= 0 && end > start
            ? JsonNode.Parse(text[start..(end + 1)])?.AsObject()
            : null)
            ?? throw new InvalidOperationException($"browser_get_config returned no JSON object: {text}");
    }

    private static JsonNode? Follow(JsonObject root, string path)
    {
        JsonNode? node = root;

        foreach (var segment in path.Split('.'))
        {
            node = (node as JsonObject)?[segment];

            if (node is null)
            {
                return null;
            }
        }

        return node;
    }
}
