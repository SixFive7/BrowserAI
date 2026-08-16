// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The sixth authored tool: it refuses rather than coordinates, and when it does
/// act it deletes before it downloads.
/// </summary>
/// <remarks>
/// <para>
/// <b>The refusal is the feature.</b> The browser install is shared by every
/// session on the machine, so "make this safe" would mean terminating browsers
/// other agents are driving. There is deliberately no force argument, and the
/// tests below assert both halves of that: it names what is live, and nothing it
/// found is still running afterwards because nothing was ever killed.
/// </para>
/// <para>
/// <b>The delete is real product code and only the download is doubled.</b>
/// §E's shared post-order routine removes the tree here exactly as it does in
/// production; what a double replaces is the 203.8 MB that would otherwise be
/// fetched on every suite run. The real fetch is
/// <see cref="FirstRunProvisioningTests"/>.
/// </para>
/// </remarks>
internal sealed class ReinstallBrowserTests
{
    [Test]
    public async Task ItDeletesTheTreeAndDownloadsItAgainWhenNothingIsRunning()
    {
        var installs = 0;

        await using var sessions = RigSessionEnvironment.Create(
            installer: (_, root) =>
            {
                Interlocked.Increment(ref installs);
                return FakeInstaller.Succeeding(Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName), TimeSpan.Zero);
            },
            timers: new ProvisioningTimers { Poll = TimeSpan.FromMilliseconds(20) });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        // A complete tree with a file in it that must not survive, so "the
        // directory exists afterwards" cannot pass for "it was replaced".
        var stale = Path.Combine(sessions.ChromiumDirectory, "stale-from-the-old-install.bin");
        _ = Directory.CreateDirectory(sessions.ChromiumDirectory);
        await File.WriteAllTextAsync(stale, new string('x', 4096));
        await File.WriteAllTextAsync(Path.Combine(sessions.ChromiumDirectory, BrowsersManifest.InstallationCompleteMarker), string.Empty);

        // Counted from here: the rig opens a default session, and that init
        // legitimately starts an install against a root this test left empty.
        var before = Volatile.Read(ref installs);
        var answer = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, []);
        var text = TextOf(answer);

        await Assert.That((bool?)answer["isError"]).IsNotEqualTo(true);
        await Assert.That(text).Contains("Re-provisioned");

        // Deleted, not merely written over: the old file is gone and the marker
        // the new install wrote is there.
        await Assert.That(File.Exists(stale)).IsFalse();
        await Assert.That(Volatile.Read(ref installs) - before).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(sessions.ChromiumDirectory, BrowsersManifest.InstallationCompleteMarker))).IsTrue();

        // And the size it removed is reported, because the whole point of the
        // tool is that it is destructive and the caller should see what it cost.
        await Assert.That(text).Contains("MiB");
    }

    [Test]
    public async Task ItRefusesWhileSomethingIsRunningFromTheTreeAndNamesTheSessionThatIsOpen()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var session = Path.Combine(sessions.Root, "holds-a-browser");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the session a reinstall must refuse to act around",
            ["mode"] = "headless",
        });

        // A real process whose image path is inside the browsers root. That is
        // the only property the product matches on: an image NAME would find the
        // user's own Chrome, and this test would then pass on a machine where
        // BrowserAI had never run. The helper does not return until the
        // product's own enumeration can see it.
        using var scope = new JobObjectScope();
        var (running, planted) = await PlantedProcess.StartInAsync(
            scope,
            Path.Combine(sessions.ChromiumDirectory, "chrome-win64"),
            sessions.ChromiumDirectory);

        var answer = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, []);
        var text = TextOf(answer);

        await Assert.That((bool?)answer["isError"]).IsTrue();

        // It names what is live rather than saying "in use". A model told only
        // that something is busy has nothing to act on.
        await Assert.That(text).Contains(session);
        await Assert.That(text).Contains("no force option");

        // Nothing was killed and nothing was deleted, which is the half the
        // message cannot claim for itself.
        await Assert.That(ProcessIdentity.IsAlive(running.Id, ProcessIdentity.CreationTimeOf(running.Id))).IsTrue();
        await Assert.That(File.Exists(planted)).IsTrue();
    }

    [Test]
    public async Task ItReportsWhatWouldNotDeleteRatherThanDownloadingOnTopOfIt()
    {
        var installs = 0;

        await using var sessions = RigSessionEnvironment.Create(
            installer: (_, root) =>
            {
                Interlocked.Increment(ref installs);
                return FakeInstaller.Succeeding(Path.Combine(root, RigSessionEnvironment.ChromiumDirectoryName), TimeSpan.Zero);
            },
            timers: new ProvisioningTimers { Poll = TimeSpan.FromMilliseconds(20) });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        _ = Directory.CreateDirectory(sessions.ChromiumDirectory);
        await File.WriteAllTextAsync(Path.Combine(sessions.ChromiumDirectory, BrowsersManifest.InstallationCompleteMarker), string.Empty);

        var held = Path.Combine(sessions.ChromiumDirectory, "held-open.bin");

        // Counted from HERE rather than from the start of the test: the rig
        // opens a default session, and that init legitimately starts an install
        // against a root this test deliberately left empty.
        var before = Volatile.Read(ref installs);

        // FileShare.None, so the delete meets a file it genuinely cannot remove
        // -- and no process is running from the tree, so the check above passes
        // and the delete is what fails.
        using (var _ = new FileStream(held, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var answer = await CallAsync(rig, SessionToolSurface.ReinstallBrowser, []);
            var text = TextOf(answer);

            await Assert.That((bool?)answer["isError"]).IsTrue();
            await Assert.That(text).Contains(held);

            // ⚠️ The load-bearing assertion. Downloading on top of a tree that
            // would not delete is how a directory ends up half old and half new
            // with an INSTALLATION_COMPLETE over it, which every later check
            // then short-circuits on without validating anything.
            await Assert.That(Volatile.Read(ref installs) - before).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ItTakesNoArgumentsAndSaysWhyInItsDescription()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var tools = await rig.Client.RoundTripAsync("tools/list");

        var tool = (tools["tools"]?.AsArray() ?? [])
            .Single(entry => (string?)entry!["name"] == SessionToolSurface.ReinstallBrowser)!;

        // Machine-scoped by design, which is exactly why it refuses while any
        // session has a browser open. Every other authored tool names its
        // session explicitly; this one has none, which is a different thing from
        // a default.
        await Assert.That(tool["inputSchema"]!["properties"]!.AsObject().Count).IsEqualTo(0);
        await Assert.That(tool["inputSchema"]!["required"]!.AsArray().Count).IsEqualTo(0);

        var description = (string)tool["description"]!;

        await Assert.That(description).Contains("REFUSES");
        await Assert.That(description).Contains(SessionToolSurface.Init);
    }

    [Test]
    public async Task EveryAuthoredToolIsAnsweredAndAnUnknownOneIsRefused()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var advertised = (await rig.Client.RoundTripAsync("tools/list"))["tools"]!.AsArray()
            .Select(tool => (string)tool!["name"]!)
            .Where(SessionToolSurface.IsAuthored)
            .ToList();

        // Both directions, which is what makes this a mechanism rather than a
        // count: a tool declared and never routed answers "not a BrowserAI
        // session tool", and a tool routed and never declared is invisible to
        // every caller.
        await Assert.That(string.Join(", ", advertised)).IsEqualTo(string.Join(", ", SessionToolSurface.Names));

        // ⚠️ And every declared name is actually ROUTED. The comparison above is
        // between two readings of one list, so a seventh tool added to the
        // surface and forgotten in the dispatch would satisfy it while answering
        // "not a BrowserAI session tool" to every caller. Called with no
        // arguments on purpose: each answers or refuses on its own merits, and
        // the only answer that fails here is the one that says the tool does not
        // exist.
        var unrouted = new List<string>();

        foreach (var tool in SessionToolSurface.Names)
        {
            var answer = await CallAsync(rig, tool, []);

            if (TextOf(answer).Contains("is not a BrowserAI session tool", StringComparison.Ordinal))
            {
                unrouted.Add(tool);
            }
        }

        await Assert.That(string.Join(", ", unrouted)).IsEmpty();

        // Deny-by-default in the authored half of the surface. The prefix match
        // is what routes a call here at all, so a name nobody implemented must
        // be refused rather than forwarded to the child as an upstream tool.
        var invented = await CallAsync(rig, "browserai_do_something_nobody_built", []);

        await Assert.That((bool?)invented["isError"]).IsTrue();
        await Assert.That(TextOf(invented)).Contains("is not a BrowserAI session tool");
    }

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
