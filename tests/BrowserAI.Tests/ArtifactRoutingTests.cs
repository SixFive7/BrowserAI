// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Artifacts;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Where a file goes, decided before the child sees the call — and what the
/// caller is told about it afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Route on the way in; do not sort on the way out.</b> The child's working
/// directory is the instance output root, so a name nothing rewrote still lands
/// inside the session tree by construction; a <c>filename</c> that <i>is</i>
/// rewritten goes into the folder its generator prefix implies. Classification
/// is by prefix and never by date, which is the only rule that can tell a
/// generated name from one a caller chose.
/// </para>
/// <para>
/// <b>The prefix set is a coverage gate, and it is derived rather than
/// declared.</b> <see cref="TheDeclaredFoldersAreExactlyThePrefixesTheResolvedChildCarries"/>
/// reads the set out of <c>upstream-snapshots/tools-list.json</c>, which
/// <c>build/upstream-snapshots.mjs</c> regenerates from the resolved bundle on
/// every build. A prefix with no folder and a folder with no prefix are each a
/// red build; a rename presents as one of each, which is exactly the diff that
/// says what happened.
/// </para>
/// </remarks>
internal sealed class ArtifactRoutingTests
{
    private static readonly string[] LeftRoot = ["alpha", "beta"];
    private static readonly string[] RightRoot = ["gamma"];
    private static readonly string[] RigRoot = ["rig-session"];

    /// <summary>
    /// The two prefixes whose folder is not <c>output\&lt;prefix&gt;</c>, named
    /// rather than silently tolerated.
    /// </summary>
    private static readonly (string Prefix, string Folder, string Why)[] Exceptions =
    [
        (ArtifactRouting.DownloadPrefix, SessionLayout.DownloadsFolderName,
            "a browser-initiated download lands where the browser puts it, so it sits at the session root"),
        (ArtifactRouting.TracesPrefix, Path.Combine(SessionLayout.OutputFolderName, "traces"),
            "the traces template carries an empty prefix and its own suggestedFilename, so it is not a generator prefix at all"),
    ];

    [Test]
    public async Task TheDeclaredFoldersAreExactlyThePrefixesTheResolvedChildCarries()
    {
        var resolved = ResolvedPrefixes();
        var declared = ArtifactRouting.Destinations.Keys.ToHashSet(StringComparer.Ordinal);

        await Assert.That(Adjudicate(resolved, declared)).IsEmpty();

        // Said separately, because "the two sets agree" would also be satisfied
        // by both being empty or by the gate having been quietly narrowed.
        await Assert.That(resolved.Count).IsGreaterThan(10);

        foreach (var (prefix, folder, _) in Exceptions)
        {
            await Assert.That(resolved).Contains(prefix);
            await Assert.That(ArtifactRouting.FolderFor(prefix)).IsEqualTo(folder);
        }
    }

    [Test]
    public async Task ATenthPrefixArrivingUpstreamFailsTheSort()
    {
        // The negative arm, and the reason the comparison above is a helper: a
        // gate nobody has watched fail is a gate nobody knows the shape of.
        // Re-verification row 19's requirement, in code.
        var resolved = ResolvedPrefixes();
        var declared = ArtifactRouting.Destinations.Keys.ToHashSet(StringComparer.Ordinal);

        var arrived = Adjudicate([.. resolved, "sarcophagus"], declared);

        await Assert.That(arrived).Contains("sarcophagus");
        await Assert.That(arrived).Contains("no folder");

        var vanished = Adjudicate([.. resolved.Where(prefix => prefix is not "video")], declared);

        await Assert.That(vanished).Contains("video");
        await Assert.That(vanished).Contains("no prefix");

        // And an unknown prefix cannot be filed by accident: there is no
        // fallback folder for one, which is what would otherwise put an
        // unclassified artifact beside the typed folders in the output root.
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ArtifactRouting.FolderFor("sarcophagus"));
    }

    [Test]
    public async Task EveryPrefixRoutesToTheFolderNamedAfterIt()
    {
        foreach (var prefix in ResolvedPrefixes())
        {
            var expected = Array.Find(Exceptions, exception => exception.Prefix == prefix) is { Folder: { } folder }
                ? folder
                : Path.Combine(SessionLayout.OutputFolderName, prefix);

            await Assert.That(ArtifactRouting.FolderFor(prefix)).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task EveryToolCarryingAFilenameHasBeenJudged()
    {
        // The same rule as the session-type policy, applied to files: a tool
        // whose `filename` nobody has classified writes wherever upstream's
        // default puts it, which is the flat output directory §F exists to
        // replace.
        using var snapshot = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots", "tools-list.json")));

        var carrying = snapshot.RootElement.GetProperty("tools").EnumerateArray()
            .Where(tool => tool.GetProperty("inputSchema").TryGetProperty("properties", out var properties)
                && properties.TryGetProperty(ArtifactTools.FilenameArgument, out _))
            .Select(tool => tool.GetProperty("name").GetString()!)
            .ToList();

        var unjudged = carrying.Where(tool => ArtifactTools.For(tool) is null).Order(StringComparer.Ordinal);
        var stale = ArtifactTools.Rules.Keys.Where(tool => !carrying.Contains(tool)).Order(StringComparer.Ordinal);

        await Assert.That(string.Join(Environment.NewLine, unjudged)).IsEmpty();
        await Assert.That(string.Join(Environment.NewLine, stale)).IsEmpty();
        await Assert.That(carrying.Count).IsEqualTo(ArtifactTools.Rules.Count);
    }

    [Test]
    public async Task NothingBrowserAiGeneratesCanTurnEvictionOn()
    {
        // §F promised "nothing is ever auto-deleted", and `--output-max-size`
        // made that a promise about a runtime rather than about us:
        // `_enforceOutputBudget()` runs on every tool response and unlinks
        // oldest-first across the whole output tree, sparing only the current
        // response's writes. It has no default at any merge stage, so the
        // promise holds exactly as long as neither door is opened. The
        // environment door is `ChildEnvironmentTests`; this is the config one.
        var config = BrowserAI.Runtime.BrowserConfiguration.ForSession(
            SessionPath.Resolve(Path.Combine(ScratchRoot.Path, "eviction-check")),
            SessionModes.Recorded("headless"),
            SessionManager.SupportedBrowser,
            tracing: false,
            BrowserAI.Runtime.BrowserConfiguration.DefaultConsoleLevel);

        await Assert.That(config.Opinions.Any(opinion => opinion.Path.Contains("outputMaxSize", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(System.Text.Encoding.UTF8.GetString(config.Json)).DoesNotContain("outputMaxSize");
    }

    [Test]
    public async Task ATypedFolderAppearsWhenItIsUsedAndNotBefore()
    {
        // ⚠️ Creating all fourteen up front was measured at 10.4 ms per session
        // against 2.5 ms for these three — about a second per suite run, and ten
        // empty directories in every session a caller ever makes, for generators
        // they never used. That is navigational noise in the tree §F exists to
        // make navigable, so a folder on disk now means an artifact of that kind
        // was produced.
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 8 });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var atInit = Directory.EnumerateDirectories(rig.Session!, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(rig.Session!, path))
            .Order(StringComparer.Ordinal);

        await Assert.That(string.Join(", ", atInit)).IsEqualTo(string.Join(
            ", ",
            new[] { SessionLayout.DownloadsFolderName, SessionLayout.OutputFolderName, SessionLayout.ProfileFolderName }.Order(StringComparer.Ordinal)));

        _ = await ScreenshotAsync(rig, "login.png");

        await Assert.That(Directory.Exists(Path.Combine(rig.Session!, ArtifactRouting.FolderFor("page")))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(rig.Session!, ArtifactRouting.FolderFor("video")))).IsFalse();

        // The folder set is not lost by being lazy: `session.json` names every
        // one of them with its resolved path whether it exists yet or not, so a
        // reader never has to reconstruct the layout.
        using var index = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(rig.Session!, ArtifactRouter.IndexFileName)));

        await Assert.That(index.RootElement.GetProperty("folders").EnumerateObject().Count())
            .IsEqualTo(ArtifactRouting.Folders.Count);

        // §C names session.json beside lock.json under "our own files reject
        // what they do not recognise", and it carried no version to reject
        // anything against until 2026-08-17. The field is the half that cannot
        // be added afterwards: every file written before it existed would be
        // indistinguishable from version 1, forever. A strict *reader* is
        // deliberately not built, because nothing in this build reads either
        // file and a parser with no caller is the shape this project has
        // already deleted once -- the reasoning is on ArtifactRouter.
        await Assert.That(index.RootElement.GetProperty("schemaVersion").GetInt32())
            .IsEqualTo(ArtifactRouter.CurrentSchemaVersion);

        var rollUp = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(Directory.GetParent(rig.Session!)!.FullName, ArtifactRouter.RollUpFileName)));

        using (rollUp)
        {
            await Assert.That(rollUp.RootElement.GetProperty("schemaVersion").GetInt32())
                .IsEqualTo(ArtifactRouter.CurrentSchemaVersion);
        }
    }

    [Test]
    public async Task TheChildsWorkingDirectoryIsTheOutputRoot()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        // §F's first lever, and the one that makes the stray-file failure
        // impossible rather than caught: upstream resolves a relative filename
        // against the child's cwd, so a bare foo.png nothing rewrote is inside
        // the instance tree by construction.
        await Assert.That(sessions.Launches).IsNotEmpty();

        foreach (var launch in sessions.Launches)
        {
            await Assert.That(launch.WorkingDirectory)
                .IsEqualTo(Path.Combine(Path.GetDirectoryName(launch.WorkingDirectory!)!, SessionLayout.OutputFolderName));
        }
    }

    [Test]
    [Arguments(@"..\..\foo.png", "climbs out of the session directory")]
    [Arguments(@"C:\foo.png", "it is an absolute path naming a drive")]
    [Arguments("C:foo.png", "it is a drive-relative path")]
    [Arguments(@"\\server\share\foo.png", "it is a UNC path naming another machine")]
    [Arguments(@"\foo.png", "it is rooted")]
    [Arguments(@"\\?\C:\foo.png", "it is a Win32 device path")]
    [Arguments("NUL.png", "reserved device name")]
    [Arguments("trailing .png ", "silently strips")]
    [Arguments(@"a\b\", "names a directory rather than a file")]
    public async Task APathThatLeavesTheSessionIsRefusedRatherThanNormalised(string filename, string because)
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 64 });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var answer = await CallAsync(rig, "browser_take_screenshot", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
            ["filename"] = filename,
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();
        await Assert.That(TextOf(answer)).Contains(because);

        // Refused, not normalised: the child never heard about it, so there is
        // no path anywhere that a normalisation could have landed on.
        await Assert.That(sessions.SessionChildren.SelectMany(child => child.MethodsReceived).Contains("tools/call")).IsFalse();

        // And nothing was written anywhere in the session.
        await Assert.That(Directory.EnumerateFiles(rig.Session!, "*.png", SearchOption.AllDirectories)).IsEmpty();
    }

    [Test]
    public async Task AScreenshotIsRoutedByPrefixAndTheAnswerCarriesBothPathForms()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 4096 });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var answer = await CallAsync(rig, "browser_take_screenshot", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
            ["filename"] = "login.png",
        });

        var expected = Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "page", "login.png");
        var text = TextOf(answer);

        await Assert.That((bool?)answer["isError"]).IsNotEqualTo(true);

        // Both forms, and the file at the first of them. "The result carries an
        // absolute path" is satisfied by a proxy that invented one.
        await Assert.That(text).Contains(expected);
        await Assert.That(text).Contains(Path.Combine(SessionLayout.OutputFolderName, "page", "login.png"));
        await Assert.That(File.Exists(expected)).IsTrue();
        await Assert.That(new FileInfo(expected).Length).IsEqualTo(4096);

        // The caller's own name, kept. A hand-named file is filed by the tool's
        // generator prefix and never renamed into a machine name.
        await Assert.That(Path.GetFileName(expected)).IsEqualTo("login.png");

        // Cumulative session size, which the current setup reached 1.5 GB
        // without ever saying so.
        await Assert.That(text).Contains("session total:");
        await Assert.That(text).Contains("MiB");
    }

    [Test]
    public async Task AnElementScreenshotAndAResponseBodyRouteToDifferentFoldersThanTheirSiblings()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
        {
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 8 };
            child.Tools["browser_network_request"] = new FakeToolBehaviour { WritesArtifactBytes = 8 };
        });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        // `prefix: target ? "element" : "page"` and the response-part switch,
        // both read out of the resolved bundle. A table keyed on the tool name
        // alone would file both of these under the wrong folder.
        var element = await CallAsync(rig, "browser_take_screenshot", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
            ["filename"] = "button.png",
            ["target"] = "e17",
        });

        var body = await CallAsync(rig, "browser_network_request", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
            ["filename"] = "body.txt",
            ["part"] = "response-body",
        });

        await Assert.That(File.Exists(Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "element", "button.png"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "response", "body.txt"))).IsTrue();
        await Assert.That(TextOf(element)).Contains(Path.Combine("element", "button.png"));
        await Assert.That(TextOf(body)).Contains(Path.Combine("response", "body.txt"));
    }

    [Test]
    public async Task ASecondArtifactWithTheSameNameIsSuffixedAndTheResultSaysSo()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 16 });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var first = await ScreenshotAsync(rig, "login.png");
        var second = await ScreenshotAsync(rig, "login.png");

        var folder = Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "page");

        // Two files, not one overwritten. An artifact silently replacing an
        // earlier one is data loss wearing a success.
        await Assert.That(File.Exists(Path.Combine(folder, "login.png"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(folder, "login-2.png"))).IsTrue();

        await Assert.That(TextOf(first)).DoesNotContain("renamed:");
        await Assert.That(TextOf(second)).Contains("renamed:");
        await Assert.That(TextOf(second)).Contains("login-2.png");
        await Assert.That(TextOf(second)).Contains("was NOT overwritten");
    }

    [Test]
    public async Task ACallThatNamesNoFileIsGivenOneOnlyWhereUpstreamWouldHaveGeneratedIt()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
        {
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 8 };
            child.Tools["browser_console_messages"] = new FakeToolBehaviour();
            child.Tools["browser_navigate"] = new FakeToolBehaviour();
        });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        _ = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
            ["url"] = "https://shop.example.com/checkout/step-3",
        });

        _ = await ScreenshotAsync(rig, filename: null);

        // A page-derived slug and a counter, because checkout-step-3.png
        // survives a month and page-2026-08-14T04-11-50-882Z.png does not.
        var written = Directory.EnumerateFiles(Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "page")).ToList();

        await Assert.That(written.Count).IsEqualTo(1);
        await Assert.That(Path.GetFileName(written[0])).IsEqualTo("shop-example-com-checkout-step-3-1.png");

        // And the tools whose `filename` decides whether the answer is a file or
        // the response body are never given one: supplying it would silently
        // change what the call does.
        var console = await CallAsync(rig, "browser_console_messages", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
        });

        await Assert.That(TextOf(console)).DoesNotContain("BrowserAI routed");
        await Assert.That(ForwardedArgument(sessions, "browser_console_messages", "filename")).IsNull();
    }

    [Test]
    public async Task ADownloadIsSortedAfterTheFactAndAHandNamedFileIsNeverFiledByDate()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour());

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var output = Path.Combine(rig.Session!, SessionLayout.OutputFolderName);

        // Exactly what upstream leaves in the output root: a download named by
        // the site, and an annotation it named itself. Neither can be routed on
        // the way in -- one has no `filename` argument and the other's name
        // comes from the server.
        await File.WriteAllBytesAsync(Path.Combine(output, "quarterly-report.pdf"), new byte[32]);
        await File.WriteAllBytesAsync(Path.Combine(output, "annotations-2026-08-16T04-11-50-882Z.png"), new byte[16]);

        var answer = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
            ["url"] = "data:text/html,<h1>ok</h1>",
        });

        // Classified by generator prefix, never by date. The annotation carries
        // one; the download does not, and a download is the one artifact whose
        // name upstream did not choose.
        await Assert.That(File.Exists(Path.Combine(rig.Session!, SessionLayout.DownloadsFolderName, "quarterly-report.pdf"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, "annotations", "annotations-2026-08-16T04-11-50-882Z.png"))).IsTrue();

        // Not into `page\`: the file's date says nothing about which generator
        // wrote it, which is the whole reason the sort is by prefix. The folder
        // does not even exist, because a typed folder appears when it is used.
        await Assert.That(Directory.Exists(Path.Combine(output, "page"))).IsFalse();

        // And the caller is told, rather than finding a file somewhere it did
        // not put it.
        await Assert.That(TextOf(answer)).Contains("sorted an artifact it could not route");
        await Assert.That(TextOf(answer)).Contains("quarterly-report.pdf");

        // The output root itself is flat no longer.
        await Assert.That(Directory.EnumerateFiles(output)).IsEmpty();
    }

    [Test]
    public async Task TheSessionIndexHasOneEntryPerRoutedArtifact()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 128 });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        _ = await ScreenshotAsync(rig, "one.png");
        _ = await ScreenshotAsync(rig, "two.png");
        _ = await ScreenshotAsync(rig, "one.png");

        using var index = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(rig.Session!, ArtifactRouter.IndexFileName)));

        var artifacts = index.RootElement.GetProperty("artifacts").EnumerateArray().ToList();

        await Assert.That(artifacts.Count).IsEqualTo(3);
        await Assert.That(index.RootElement.GetProperty("session").GetString()).IsEqualTo(rig.Session);

        foreach (var artifact in artifacts)
        {
            await Assert.That(artifact.GetProperty("tool").GetString()).IsEqualTo("browser_take_screenshot");
            await Assert.That(artifact.GetProperty("bytes").GetInt64()).IsEqualTo(128);
            await Assert.That(File.Exists(artifact.GetProperty("path").GetString()!)).IsTrue();
            await Assert.That(artifact.GetProperty("sessionRelative").GetString()!).StartsWith(SessionLayout.OutputFolderName);
        }

        // Mode, browser and purpose stay lock.json's to own: a second copy of
        // the session's identity is a second thing to disagree with the first.
        await Assert.That(index.RootElement.TryGetProperty("mode", out _)).IsFalse();
        await Assert.That(index.RootElement.TryGetProperty("purpose", out _)).IsFalse();

        // Every folder resolved, so a reader of this file never has to
        // reconstruct the layout.
        await Assert.That(index.RootElement.GetProperty("folders").EnumerateObject().Count())
            .IsEqualTo(ArtifactRouting.Folders.Count);
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
        // The one beside this rig is the rig session's, written before any of
        // the three above existed, and it still says so.
        await Assert.That(Beneath(sessions.Root)).IsEquivalentTo(RigRoot);

        // And the init that created each one said how many neighbours it had,
        // which is the half a file on disk cannot deliver to a model.
        await Assert.That(TextOf(second)).Contains("other sessions under " + left + ": 1");
        await Assert.That(TextOf(second)).Contains("alpha");
    }

    /// <summary>
    /// An index that could not be written is said out loud, in the same answer
    /// that names its path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>ArtifactRouter.TryWrite</c>'s own doc comment is the requirement</b>
    /// — <i>"a read-only volume or a virus scanner holding the file open must not
    /// turn a screenshot that was taken into a screenshot that failed — but it
    /// must not be silent either, so the answer carries what could not be
    /// written"</i>. Until 2026-08-16 both call sites discarded the answer, so
    /// the note still ended with <c>index: &lt;path&gt;</c> for a file that was
    /// stale or absent. Found by the plan's final audit.
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
    public async Task AnIndexThatCouldNotBeWrittenIsNamedInTheAnswerRatherThanImplied()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 8 });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var index = Path.Combine(rig.Session!, ArtifactRouter.IndexFileName);
        _ = Directory.CreateDirectory(index);

        var routed = TextOf(await ScreenshotAsync(rig, "login.png"));

        // The screenshot itself still happened, and is still reported at its
        // real path -- an index that cannot be written must never turn a file
        // that was taken into one that failed.
        var artifact = Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "page", "login.png");

        await Assert.That(File.Exists(artifact)).IsTrue();
        await Assert.That(routed).Contains("BrowserAI routed");
        await Assert.That(routed).Contains(artifact);

        // And the line that names the index says it is behind, rather than
        // pointing at a directory and leaving the reader to find out.
        await Assert.That(routed).Contains(index);
        await Assert.That(routed).Contains("COULD NOT BE WRITTEN");
    }

    /// <summary>
    /// A roll-up that could not be written is said out loud too, in the answer
    /// that names it.
    /// </summary>
    /// <remarks>
    /// The second of the two discarded call sites. <c>init</c>'s answer ends
    /// <c>(rolled up in &lt;path&gt;)</c>, which was printed whether or not the
    /// write happened.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ARollUpThatCouldNotBeWrittenIsNamedInTheAnswerRatherThanImplied()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var root = Path.Combine(sessions.Root, "project-blocked");
        _ = Directory.CreateDirectory(Path.Combine(root, ArtifactRouter.RollUpFileName));

        var opened = TextOf(await InitAsync(rig, Path.Combine(root, "alpha")));

        // The session opened. Only the aggregate beside it is behind.
        await Assert.That(opened).Contains("Session ready.");
        await Assert.That(opened).Contains(Path.Combine(root, ArtifactRouter.RollUpFileName));
        await Assert.That(opened).Contains("COULD NOT BE WRITTEN");
    }

    [Test]
    public async Task AStorageStateWrittenByOneToolIsFoundByTheToolThatReadsIt()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
        {
            child.Tools["browser_storage_state"] = new FakeToolBehaviour { WritesArtifactBytes = 64 };
            child.Tools["browser_set_storage_state"] = new FakeToolBehaviour();
        });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        _ = await CallAsync(rig, "browser_storage_state", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
            ["filename"] = "auth.json",
        });

        _ = await CallAsync(rig, "browser_set_storage_state", new JsonObject
        {
            [SessionToolSurface.SessionParameter] = rig.Session!,
            ["filename"] = "auth.json",
        });

        // The read is routed by the same prefix as the write, which is the only
        // thing that makes the round trip work once the write has been moved.
        var expected = Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "storage-state", "auth.json");

        await Assert.That(File.Exists(expected)).IsTrue();
        await Assert.That(ForwardedArgument(sessions, "browser_set_storage_state", "filename")).IsEqualTo(expected);

        // And a read is never suffixed: suffixing it would point the reader at a
        // file that does not exist.
        await Assert.That(Directory.EnumerateFiles(Path.GetDirectoryName(expected)!).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task AToolThatWroteNothingReportsNothingAndGivesTheNameBack()
    {
        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_take_screenshot"] = new FakeToolBehaviour
            {
                // Upstream's own shape for a tool that failed: a success
                // envelope carrying isError, and no file anywhere.
                RawResult = """{"content":[{"type":"text","text":"Error: element is not visible"}],"isError":true}""",
            });

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var failed = await ScreenshotAsync(rig, "login.png");

        await Assert.That(TextOf(failed)).DoesNotContain("BrowserAI routed");
        await Assert.That(File.Exists(Path.Combine(rig.Session!, ArtifactRouter.IndexFileName))).IsFalse();

        // And the retry gets the name it asked for rather than a suffix, which
        // is what the reservation would otherwise have cost it.
        sessions.SessionChildren[0].Tools["browser_take_screenshot"] = new FakeToolBehaviour { WritesArtifactBytes = 8 };

        var retried = await ScreenshotAsync(rig, "login.png");

        await Assert.That(TextOf(retried)).DoesNotContain("renamed:");
        await Assert.That(File.Exists(Path.Combine(rig.Session!, SessionLayout.OutputFolderName, "page", "login.png"))).IsTrue();
    }

    /// <summary>What the two sets disagree about, in the shape the gate reports.</summary>
    /// <remarks>
    /// Both directions, because a rename presents as one of each: an unexplained
    /// new prefix beside an unexplained empty folder is the diff that says what
    /// happened, and reporting only one half would leave a reviewer guessing.
    /// </remarks>
    private static string Adjudicate(IEnumerable<string> resolved, IReadOnlySet<string> declared)
    {
        var upstream = resolved.ToHashSet(StringComparer.Ordinal);

        var missing = upstream.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(prefix => $"prefix '{prefix}' has no folder — upstream added a generator and its artifacts are misfiled");

        var dead = declared.Except(upstream, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(prefix => $"folder for '{prefix}' has no prefix — upstream removed or renamed a generator");

        var difference = string.Join(Environment.NewLine, missing.Concat(dead));

        return difference.Length is 0
            ? string.Empty
            : difference
                + Environment.NewLine + "resolved: " + string.Join(", ", upstream.Order(StringComparer.Ordinal).Select(Quote))
                + Environment.NewLine + "declared: " + string.Join(", ", declared.Order(StringComparer.Ordinal).Select(Quote));
    }

    private static string Quote(string value) => value.Length is 0 ? "<empty>" : value;

    /// <summary>
    /// The generator prefixes the resolved child carries, from the snapshot the
    /// build regenerates and diffs.
    /// </summary>
    private static IReadOnlyList<string> ResolvedPrefixes()
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(RepositoryLayout.Root.FullName, "upstream-snapshots", "tools-list.json")));

        return
        [
            .. snapshot.RootElement.GetProperty("artifactPrefixes").GetProperty("prefixes")
                .EnumerateArray()
                .Select(prefix => prefix.GetString()!),
        ];
    }

    private static IReadOnlyList<string> Beneath(string root)
    {
        using var rollUp = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, ArtifactRouter.RollUpFileName)));

        return
        [
            .. rollUp.RootElement.GetProperty("beneath").EnumerateArray()
                .Select(entry => Path.GetFileName(entry.GetProperty("directory").GetString()!)),
        ];
    }

    /// <summary>The value a child actually received for one argument of one tool.</summary>
    private static string? ForwardedArgument(RigSessionEnvironment sessions, string tool, string argument) =>
        sessions.SessionChildren
            .SelectMany(child => child.FramesReceived)
            .Select(frame => JsonNode.Parse(FrameChannel.TextOf(frame)))
            .Where(node => node?["params"]?["name"]?.GetValue<string>() == tool)
            .Select(node => (node!["params"]!["arguments"] as JsonObject)?[argument]?.GetValue<string>())
            .LastOrDefault();

    private static async Task<JsonObject> ScreenshotAsync(McpTestHarness rig, string? filename)
    {
        var arguments = new JsonObject { [SessionToolSurface.SessionParameter] = rig.Session! };

        if (filename is not null)
        {
            arguments["filename"] = filename;
        }

        return await CallAsync(rig, "browser_take_screenshot", arguments);
    }

    private static async Task<JsonObject> InitAsync(McpTestHarness rig, string directory) =>
        await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["mode"] = "headless",
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
