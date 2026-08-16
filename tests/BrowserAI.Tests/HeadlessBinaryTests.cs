// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Which browser binary actually runs, and what happens when the one BrowserAI
/// ships is not there.
/// </summary>
/// <remarks>
/// <para>
/// <b>A headless launch gets full <c>chrome.exe</c> because we set a
/// chromium-alias channel, never because headless does that.</b> Upstream's
/// selector reads: a chromium-alias channel → <c>chromium</c>; any other channel
/// → that channel; <b>no</b> channel → <c>headless ? "chromium-headless-shell" :
/// "chromium"</c>. The shell is deliberately never provisioned, so dropping the
/// channel would not degrade — it would fail, and the failure would be baffling
/// without this note.
/// </para>
/// <para>
/// <b>The second test is the one that keeps the first honest.</b> Verified
/// 2026-08-13: with an empty browsers directory, <c>initialize</c>,
/// <c>tools/list</c> <i>and</i> <c>browser_navigate</c> all succeed, because
/// upstream falls back to the user's installed Google Chrome. Without a test
/// that an empty root <b>fails</b>, the entire batteries-included premise can be
/// dead code with the suite green.
/// </para>
/// </remarks>
internal sealed class HeadlessBinaryTests
{
    [Test]
    public async Task TheResolvedBrowserIsOurChromiumAndNotTheHeadlessShell()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();
        var browser = run.BrowserProcess(BrowserAiPaths.BrowsersDirectory);

        // The exact executable, from the revision the resolved payload's own
        // browsers.json names. Not "somewhere under our root", because a
        // stale revision left behind by an earlier bump would also be under it.
        await Assert.That(browser.ImagePath).IsEqualTo(BrowserAiPaths.ExpectedChromiumExecutable);

        // Headless, so the selector really was exercised on the branch that
        // would otherwise have asked for the shell.
        await Assert.That(browser.CommandLine).Contains("--headless");

        // ⚠️ The profile, read off the BROWSER'S OWN command line rather than out
        // of the config the child reports. The two can disagree in exactly the
        // case that matters: a `UserDataDir` policy set on the machine overrides
        // the switch, and Chromium then runs against a profile nobody chose while
        // every config round trip still reports ours. An [unusable path falls back
        // invisibly](../../kb/chromium/profiles.md) for the same reason, so this
        // is the only assertion that distinguishes "we asked" from "it obeyed".
        await Assert.That(browser.CommandLine)
            .Contains(Path.Combine(run.SessionDirectory, SessionLayout.ProfileFolderName));

        // And the shell is not merely unused: it was never provisioned, which is
        // what makes "the channel is mandatory" a hard failure rather than a
        // performance note.
        await Assert.That(Directory.Exists(BrowserAiPaths.HeadlessShellDirectory)).IsFalse();
    }

    [Test]
    public async Task AnEmptyBrowsersDirectoryFailsRatherThanFallingBackToSystemChrome()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var scratch = ScratchDirectory.Create("empty-browsers-root");

        var browsers = Path.Combine(scratch.Path, "browsers");
        var work = Path.Combine(scratch.Path, "work");
        _ = Directory.CreateDirectory(browsers);
        _ = Directory.CreateDirectory(work);

        // The product's own generator and the product's own launch options, with
        // nothing changed but where browsers live. That is what makes this a
        // test of the config BrowserAI writes rather than of a config invented
        // here: delete `browserName` or the channel from BrowserConfiguration
        // and this test goes green while the premise dies.
        var options = ChildLaunch.Create(
            RepositoryPayload.Layout,
            browsers,
            work,
            Path.Combine(work, "playwright-mcp.config.json"),
            BrowserConfiguration.ForSurface(work));

        await using var client = RawStdioClient.Start(
            options.Command,
            options.Arguments,
            options.WorkingDirectory,
            options.Environment);

        // The handshake and the tool list succeed with an empty root. They did
        // in 2026-08-13's measurement too, which is exactly why neither is
        // evidence of anything.
        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);
        var tools = await client.RoundTripAsync("tools/list", new JsonObject());
        await Assert.That(tools["tools"]!.AsArray().Count).IsGreaterThan(0);

        var navigate = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl },
        });

        var failed = navigate.ContainsKey("error")
            || (bool?)navigate["result"]?["isError"] is true;

        await Assert.That(failed).IsTrue();

        // And it failed for the right reason. A refusal that happened to come
        // from somewhere else would satisfy the assertion above while leaving
        // the fallback to system Chrome untested.
        var report = navigate.ToJsonString();
        await Assert.That(report).Contains("chrome-for-testing");
    }
}
