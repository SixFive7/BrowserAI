// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A session's <c>output\</c> is what the child left in it, and BrowserAI adds
/// no structure to it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This file replaces <c>ArtifactRoutingTests</c> and
/// <c>ArtifactPointerTests</c>, both deleted on 2026-08-26 with the machinery
/// they covered.</b> What went: a table of every tool carrying a
/// <c>filename</c>, a folder per upstream generator prefix, a string validator
/// that refused nine path shapes, an inbound rewrite to an absolute path, a
/// reservation set that suffixed collisions, an after-the-fact sweep of the
/// output root, a pin set holding every name the child had published in an
/// answer, a <c>session.json</c> index and a note spliced into every rewritten
/// call's result. Roughly 1,200 lines of product code and 1,700 of tests.
/// </para>
/// <para>
/// <b>What is asserted instead is the absence, and the absence has to be
/// asserted rather than assumed</b> — a routing layer that came back would
/// otherwise be caught by nothing here, only by the byte-identity arm in
/// <c>LosslessPassthroughTests</c>, which says the answer is unchanged and not
/// that the tree is.
/// </para>
/// <para>
/// <b>Three things in this file are not about the absence and survive
/// unchanged:</b> the child's working directory, upstream's output-budget
/// eviction staying off, and the per-root roll-up. Each is a session-system fact
/// rather than a traffic one, which is why each outlived the deletion.
/// </para>
/// </remarks>
internal sealed class FlatOutputTests
{
    private static readonly string[] LeftRoot = ["alpha", "beta"];
    private static readonly string[] RightRoot = ["gamma"];
    private static readonly string[] RigRoot = ["rig-session"];

    /// <summary>
    /// The three directories a session is created with are the only ones
    /// BrowserAI ever makes, whatever a call writes.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Inverted 2026-08-26 (previously
    /// <c>ArtifactRoutingTests.ATypedFolderAppearsWhenItIsUsedAndNotBefore</c>,
    /// which asserted <c>output\page\</c> appeared after a screenshot and
    /// <c>output\video\</c> did not).</b> No typed folder appears at any point,
    /// because there are none: a <c>filename</c> reaches the child as the caller
    /// spelled it and upstream resolves it against its own working directory,
    /// which is <c>output\</c> itself.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoTypedFolderEverAppearsAndTheOutputRootStaysFlat()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 8 });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var expected = string.Join(
            ", ",
            new[] { SessionLayout.DownloadsFolderName, SessionLayout.OutputFolderName, SessionLayout.ProfileFolderName }
                .Order(StringComparer.Ordinal));

        await Assert.That(DirectoriesUnder(rig.Session!)).IsEqualTo(expected);

        _ = await ScreenshotAsync(rig, "login.png");

        // Still three. The file landed at the output root under the name the
        // caller chose, and nothing was created above it.
        await Assert.That(DirectoriesUnder(rig.Session!)).IsEqualTo(expected);

        await Assert.That(File.Exists(Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "login.png"))).IsTrue();

        // ⚠️ AND `session.json` NEVER APPEARS. It was the artifact index — one
        // entry per routed file, plus a resolved path for every typed folder —
        // and the session directory holds two files that describe it now, both
        // of them the session system's.
        await Assert.That(File.Exists(Path.Combine(rig.Session!, "session.json"))).IsFalse();

        // The guard, the store, and the store's two WAL companions, which exist
        // for as long as a connection is open and are SQLite's rather than
        // BrowserAI's. Named individually rather than filtered out, so a third
        // file arriving at the root is a red build.
        await Assert.That(FilesAtTheRootOf(rig.Session!))
            .IsEqualTo(string.Join(
                ", ",
                new[]
                {
                    SessionLayout.DataFileName,
                    SessionLayout.DataFileName + "-shm",
                    SessionLayout.DataFileName + "-wal",
                    SessionLayout.LockFileName,
                }.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// A second file with a name that is already taken overwrites the first,
    /// and nothing suffixes it or says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THIS IS A LOSS, ASSERTED SO THAT IT IS RECORDED RATHER THAN
    /// DISCOVERED.</b> Until 2026-08-26 <c>ArtifactRouter.Unique</c> suffixed a
    /// colliding name against both the filesystem and an in-flight reservation
    /// set, and the answer said what it had been renamed from — the hazard row
    /// <i>two artifacts with the same caller-supplied name in one session is
    /// data loss wearing a success</i>. That machinery is gone with the rest of
    /// the routing, and upstream's own <c>writeFile</c> truncates.
    /// </para>
    /// <para>
    /// <b>So the hazard is open again, deliberately</b>, and this test is what
    /// stops it being open <i>and</i> unstated: it fails the day anything starts
    /// suffixing again, which would mean a mechanism had come back without the
    /// row being re-adjudicated. The model is told in the <c>init</c> answer
    /// that a name it reuses is overwritten, which is the only thing a
    /// passthrough can honestly do about it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASecondFileWithTheSameNameOverwritesTheFirstAndNothingSuffixesIt()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour
            {
                WritesArtifactContent = Encoding.UTF8.GetBytes("the first screenshot"),
            });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        _ = await ScreenshotAsync(rig, "login.png");

        sessions.SessionChildren[0].Tools["browser_take_screenshot"] = new FakeToolBehaviour
        {
            WritesArtifactContent = Encoding.UTF8.GetBytes("the second"),
        };

        _ = await ScreenshotAsync(rig, "login.png");

        var output = Path.Combine(rig.Session!, SessionLayout.OutputFolderName);

        // One file, and it is the second one's bytes.
        await Assert.That(Directory.EnumerateFiles(output, "login*").Count()).IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "login.png"))).IsEqualTo("the second");

        // The named absence: no `login-2.png`, which is what the deleted
        // reservation set produced.
        await Assert.That(File.Exists(Path.Combine(output, "login-2.png"))).IsFalse();

        // And the caller was told at `init` rather than at the moment it lost
        // the first file, which is the only warning a passthrough can give.
        var opened = TextOf(await InitAsync(rig, Path.Combine(sessions.Root, "told-about-overwriting")));

        await Assert.That(opened).Contains("OVERWRITTEN");
    }

    [Test]
    public async Task NothingBrowserAiGeneratesCanTurnEvictionOn()
    {
        // "Nothing is ever auto-deleted" is a promise about a runtime rather
        // than about us: `_enforceOutputBudget()` runs on every tool response
        // and unlinks oldest-first across the whole output tree, sparing only
        // the current response's writes. It has no default at any merge stage,
        // so the promise holds exactly as long as neither door is opened. The
        // environment door is `ChildEnvironmentTests`; this is the config one.
        //
        // ⚠️ It matters MORE since the routing went, not less: a download now
        // lives in the output tree permanently rather than being sorted out of
        // it, and it is the first thing an evictor would unlink.
        var config = BrowserConfiguration.ForSession(
            SessionPath.Resolve(Path.Combine(ScratchRoot.Path, "eviction-check")),
            headed: false,
            SessionManager.DefaultBrowser,
            tracing: false,
            RunOptions.Default);

        await Assert.That(config.Opinions.Any(opinion => opinion.Path.Contains("outputMaxSize", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(Encoding.UTF8.GetString(config.Json)).DoesNotContain("outputMaxSize");
    }

    [Test]
    public async Task TheChildsWorkingDirectoryIsTheOutputRoot()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        // ⚠️ THE WHOLE CONTAINMENT NOW RESTS ON THIS LINE AND ON
        // `allowUnrestrictedFileAccess: false`. Upstream resolves a relative
        // `filename` against the child's cwd and refuses anything outside it or
        // `outputDir`; BrowserAI writes both as this directory, so the two
        // allowed roots coincide instead of overlapping and nothing of ours
        // stands between a caller's string and the check.
        await Assert.That(sessions.Launches).IsNotEmpty();

        foreach (var launch in sessions.Launches)
        {
            await Assert.That(launch.WorkingDirectory)
                .IsEqualTo(Path.Combine(Path.GetDirectoryName(launch.WorkingDirectory!)!, SessionLayout.OutputFolderName));
        }
    }

    [Test]
    public async Task TheRollUpCoversOnlyTheRootInPlay()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var left = Path.Combine(sessions.Root, "project-left");
        var right = Path.Combine(sessions.Root, "project-right");

        _ = await InitAsync(rig, Path.Combine(left, "alpha"));
        var second = await InitAsync(rig, Path.Combine(left, "beta"));
        _ = await InitAsync(rig, Path.Combine(right, "gamma"));

        await Assert.That(Beneath(left)).IsEquivalentTo(LeftRoot);
        await Assert.That(Beneath(right)).IsEquivalentTo(RightRoot);

        // BrowserAI is registered once and serves every repository on the host,
        // so an aggregate over everything would pull unrelated projects into
        // whatever context happens to be open. Neither root's roll-up knows the
        // other exists.
        await Assert.That(Beneath(left)).DoesNotContain("gamma");
        await Assert.That(Beneath(right)).DoesNotContain("alpha");

        // A roll-up sits at the session's OWN root and is not propagated
        // upwards. Refreshing every ancestor would scatter this file up a
        // caller's tree to the drive root, which is the opposite of scoping it.
        await Assert.That(Beneath(sessions.Root)).IsEquivalentTo(RigRoot);

        // And the init that created each one said how many neighbours it had,
        // which is the half a file on disk cannot deliver to a model.
        await Assert.That(TextOf(second)).Contains("other sessions under " + left + ": 1");
        await Assert.That(TextOf(second)).Contains("alpha");
    }

    /// <summary>
    /// A roll-up that could not be written is said out loud, in the answer that
    /// names it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>SessionRollUp.TryWrite</c>'s own doc comment is the requirement</b>
    /// — a read-only volume or a scanner holding the file open must not turn a
    /// session that opened into one that failed to open, and it must not be
    /// silent either. Until 2026-08-16 the call site discarded the answer, so
    /// <c>init</c> ended <c>(rolled up in &lt;path&gt;)</c> whether or not the
    /// write had happened.
    /// </para>
    /// <para>
    /// <b>A directory standing in for the file, rather than a second process
    /// holding it.</b> <c>File.WriteAllBytes</c> onto a directory is refused by
    /// Windows every time, with no timing in it, which makes this the arm that
    /// cannot flake — and what is under test is the <i>answer</i>, not which
    /// error produced it.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARollUpThatCouldNotBeWrittenIsNamedInTheAnswerRatherThanImplied()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var root = Path.Combine(sessions.Root, "project-blocked");
        _ = Directory.CreateDirectory(Path.Combine(root, SessionRollUp.FileName));

        var opened = TextOf(await InitAsync(rig, Path.Combine(root, "alpha")));

        // The session opened. Only the aggregate beside it is behind.
        await Assert.That(opened).Contains("Session ready.");
        await Assert.That(opened).Contains(Path.Combine(root, SessionRollUp.FileName));
        await Assert.That(opened).Contains("COULD NOT BE WRITTEN");
    }

    /// <summary>Every session directory the roll-up beneath one root lists.</summary>
    private static IReadOnlyList<string> Beneath(string root)
    {
        var file = Path.Combine(root, SessionRollUp.FileName);

        if (!File.Exists(file))
        {
            return [];
        }

        return [.. (JsonNode.Parse(File.ReadAllText(file))!["beneath"]?.AsArray() ?? [])
            .Select(entry => Path.GetFileName((string?)entry?["directory"] ?? string.Empty))];
    }

    private static string DirectoriesUnder(string session) =>
        string.Join(
            ", ",
            Directory.EnumerateDirectories(session, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(session, path))
                .Order(StringComparer.Ordinal));

    private static string FilesAtTheRootOf(string session) =>
        string.Join(
            ", ",
            Directory.EnumerateFiles(session).Select(Path.GetFileName).Order(StringComparer.Ordinal));

    private static async Task<JsonObject> ScreenshotAsync(McpTestHarness rig, string filename) =>
        await CallAsync(rig, "browser_take_screenshot", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
            [SessionToolSurface.WhyParameter] = "the suite exercising this call",
            ["filename"] = filename,
        });

    private static async Task<JsonObject> InitAsync(McpTestHarness rig, string directory) =>
        await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "one of several sessions under one root",
        });

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
