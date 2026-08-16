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
/// <b>The config key reads fine and does nothing.</b> Upstream's
/// <c>validateBrowserConfig</c> intends <c>chromiumSandbox = true</c> on
/// non-Linux, and the browser still runs <c>--no-sandbox</c> — upstream
/// behaviour contradicting upstream intent, which means the default posture is
/// unsandboxed and a config key is not a fix. Only the CLI flag works.
/// </para>
/// <para>
/// <b>Both arms are needed, and the second is the one that ages.</b> The first
/// asserts what BrowserAI ships. The second asserts that the upstream defect is
/// still there, so the day upstream fixes it this test goes red and the flag
/// stops being load-bearing on purpose rather than by accident. Without it,
/// "we pass <c>--sandbox</c>" would keep passing forever whether or not it was
/// still the only thing that worked.
/// </para>
/// </remarks>
internal sealed class SandboxFlagTests
{
    private const string NoSandbox = "--no-sandbox";

    [Test]
    public async Task NoProcessOfOurBrowserRunsWithTheSandboxDisabled()
    {
        if (!PublishedSlice.IsPresent)
        {
            await Assert.That(PublishedSlice.IsAbsentAsAWhole).IsTrue();
            return;
        }

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
        // the running node process rather than from the argument list we built.
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
    public async Task TheConfigKeyIsStillDiscardedByUpstream()
    {
        if (!RepositoryPayload.IsPresent)
        {
            await Assert.That(RepositoryPayload.IsAbsentAsAWhole).IsTrue();
            return;
        }

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

        var unsandboxed = client.JobProcessIds()
            .Where(processId => ProcessCommandLine.ImagePathOf(processId) is { } path
                && path.StartsWith(BrowserAiPaths.BrowsersDirectory, StringComparison.OrdinalIgnoreCase))
            .Count(processId => ProcessCommandLine.Of(processId)?.Contains(NoSandbox, StringComparison.Ordinal) is true);

        // Still discarded. If this ever returns zero, upstream has fixed it and
        // the note in kb/playwright/configuration.md is the thing to correct.
        await Assert.That(unsandboxed).IsGreaterThan(0);
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
