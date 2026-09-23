// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The sandbox, asserted where it is actually decided: the browser's own command
/// line.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-09-14 @ <c>playwright-core</c>
/// 1.63.0-alpha-2026-08-31. Upstream has fixed it, and this test is how that
/// was found.</b> <i>Previously: "<b>The config key reads fine and does
/// nothing.</b> Upstream's <c>validateBrowserConfig</c> intends
/// <c>chromiumSandbox = true</c> on non-Linux, and the browser still runs
/// <c>--no-sandbox</c> -- upstream behaviour contradicting upstream intent,
/// which means the default posture is unsandboxed and a config key is not a
/// fix. Only the CLI flag works."</i> The MCP CLI's action stage used to
/// normalise its own option with
/// <c>options.sandbox = options.sandbox === true ? void 0 : false</c> before
/// anything else ran, so an absent <c>--sandbox</c> became an explicit
/// <c>false</c> that outranked the config file; that line is <b>gone</b> from
/// the rolled bundle, and <c>chromiumSandbox: true</c> in a config file is now
/// honoured with no flag at all.
/// </para>
/// <para>
/// <b>Both arms are needed, and the second is the one that ages -- it aged.</b>
/// The first asserts what BrowserAI ships. The second used to assert that the
/// upstream defect was still there, so that the day upstream fixed it this test
/// would go red and the flag would stop being load-bearing on purpose and
/// not by accident. <b>That is exactly what happened</b>, on the
/// 0.0.79 → 0.0.80 review, and the arm now asserts the fixed behaviour so that
/// a regression the other way is equally loud.
/// </para>
/// <para>
/// ⚠️ <b>BrowserAI's own behaviour did not change and was deliberately left
/// alone.</b> <see cref="ChildLaunch.SandboxFlag"/> still goes on the command
/// line and the generator still omits the key, which the first arm proves
/// unchanged -- and that is now belt and braces, not the only thing that
/// works. Dropping the flag on the strength of upstream's new default is a
/// decision nobody has taken: it would make the sandbox depend on a default
/// that has just been shown to move.
/// </para>
/// </remarks>
internal sealed class SandboxFlagTests
{
    private const string NoSandbox = "--no-sandbox";

    [Test]
    public async Task NoProcessOfOurBrowserRunsWithTheSandboxDisabled()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();
        var chromium = run.ChromiumProcesses(BrowserAiPaths.BrowsersDirectory);

        // A browser that never started would satisfy "no process runs
        // --no-sandbox" vacuously, which is the shape of the failure this whole
        // suite exists to catch.
        await Assert.That(chromium.Count).IsGreaterThanOrEqualTo(3);

        // Every process, not only the browser: upstream pushes --no-sandbox onto
        // the browser command line and each child inherits its own copy, so a
        // check on the browser alone would miss a renderer running unsandboxed.
        var offenders = chromium
            .Where(process => process.CommandLine?.Contains(NoSandbox, StringComparison.Ordinal) is true)
            .Select(process => $"{process.ProcessId} {process.ImagePath}")
            .ToList();

        await Assert.That(string.Join(", ", offenders)).IsEmpty();

        // The assertion above is a negative and would also pass if upstream
        // simply stopped adding --no-sandbox, so the positive half is asserted
        // too: our flag really is on the child's command line, read back from
        // the running node process and not from the argument list we built.
        // EVERY node child, and there are now two of them -- the run's own,
        // which answers tools/list before any session exists, and the session's.
        // Step 13 made `session` mandatory, so the browser above belongs to a
        // session; the previous `SingleOrDefault` here threw the moment a second
        // child appeared, which is the right failure and the wrong assertion.
        var children = run.Processes
            .Where(process => process.ImagePath?.EndsWith(@"payload\node\node.exe", StringComparison.OrdinalIgnoreCase) is true)
            .ToList();

        await Assert.That(children.Count).IsGreaterThanOrEqualTo(2);

        var unflagged = children
            .Where(process => process.CommandLine?.Contains(ChildLaunch.SandboxFlag, StringComparison.Ordinal) is not true)
            .Select(process => $"{process.ProcessId} {process.CommandLine}")
            .ToList();

        await Assert.That(string.Join(", ", unflagged)).IsEmpty();

        // And it is there instead of in the config, not as well as: the key
        // reads fine, is discarded, and would make this look configured.
        await Assert.That(BrowserAiConfigOmitsTheSandboxKey()).IsTrue();
    }

    [Test]
    public async Task TheConfigKeyIsHonouredByUpstreamNow()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var scratch = ScratchDirectory.Create("sandbox-config-key");

        // Hand-written on purpose. BrowserAI's generator does not emit this key
        // and must not start to; what is under test here is upstream's handling
        // of it, so the file has to come from the test.
        //
        // ⚠️ `userDataDir` is here for a reason that has nothing to do with the
        // sandbox, and leaving it out was measured: with the key unset upstream
        // writes the profile into %LOCALAPPDATA%\ms-playwright-mcp\, keyed by a
        // hash of the client's cwd, and this test was the one thing still
        // recreating that directory after the product stopped. A hand-written
        // config in this suite carries the key for the same reason the product's
        // does.
        var config = new JsonObject
        {
            ["browser"] = new JsonObject
            {
                ["browserName"] = BrowserConfiguration.BrowserName,
                ["userDataDir"] = Path.Combine(scratch.Path, SessionLayout.ProfileFolderName),
                ["launchOptions"] = new JsonObject
                {
                    ["channel"] = BrowserConfiguration.Channel,
                    ["headless"] = true,
                    ["chromiumSandbox"] = true,
                },
            },
        };

        var configPath = Path.Combine(scratch.Path, "config.json");
        await File.WriteAllTextAsync(configPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        await using var client = RawStdioClient.Start(
            RepositoryPayload.Layout.NodeExecutable,

            // No --sandbox. The config key is the only thing asking for one.
            [RepositoryPayload.Layout.PlaywrightMcpCli, "--config", configPath],
            scratch.Path,
            ChildEnvironment.Build(
                [new KeyValuePair<string, string>(ChildLaunch.BrowsersPathVariable, BrowserAiPaths.BrowsersDirectory)]));

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var navigate = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl },
        });

        await Assert.That((bool?)navigate["result"]?["isError"] is true).IsFalse();

        var browserProcesses = client.JobProcessIds()
            .Where(processId => ProcessCommandLine.ImagePathOf(processId) is { } path
                && path.StartsWith(BrowserAiPaths.BrowsersDirectory, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // ⚠️ THE NON-VACUOUSNESS GUARD IS NEW AND IS NOT TIDINESS. While this
        // arm asserted `unsandboxed > 0`, a browser that never started failed it
        // for free. Asserting zero inverts that: with no browser running,
        // "nothing carries --no-sandbox" is true of nothing at all, and the arm
        // would report the fix it is looking for on a machine where the
        // navigate silently did nothing.
        await Assert.That(browserProcesses.Count).IsGreaterThan(0);

        var unsandboxed = browserProcesses
            .Count(processId => ProcessCommandLine.Of(processId)?.Contains(NoSandbox, StringComparison.Ordinal) is true);

        // ⚠️ Corrected 2026-09-14 @ playwright-core 1.63.0-alpha-2026-08-31
        // (previously `await Assert.That(unsandboxed).IsGreaterThan(0)`, with
        // "Still discarded. If this ever returns zero, upstream has fixed it and
        // the note in kb/playwright/configuration.md is the thing to correct").
        // It returned zero on the 0.0.79 -> 0.0.80 review -- 0 of the browser's
        // processes carried --no-sandbox with the config key alone and no flag --
        // so upstream HAS fixed it, the kb note has been corrected, and the
        // assertion now reads the other way. A return to the old behaviour is
        // red here and not quiet, which is the same property the arm always
        // had, pointed at the fact that is now true.
        await Assert.That(unsandboxed).IsEqualTo(0);
    }

    /// <summary>
    /// Whether the generated config leaves the sandbox key out entirely, read
    /// back from a file the product wrote.
    /// </summary>
    private static bool BrowserAiConfigOmitsTheSandboxKey()
    {
        using var scratch = ScratchDirectory.Create("sandbox-generated-config");

        var path = Path.Combine(scratch.Path, "config.json");
        BrowserConfiguration.WriteTo(path, BrowserConfiguration.ForSurface(scratch.Path));

        using var generated = JsonDocument.Parse(File.ReadAllText(path));

        return !generated.RootElement
            .GetProperty("browser")
            .GetProperty("launchOptions")
            .TryGetProperty("chromiumSandbox", out _);
    }
}
