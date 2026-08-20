// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BrowserAI.Artifacts;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The pointers upstream puts in its own answers, and whether they resolve.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two defects, one shape, both reproduced against a real browser before
/// either was fixed.</b> Upstream writes two files BrowserAI's inbound routing
/// cannot reach, because neither comes from a <c>filename</c> argument — the
/// <b>console log</b> and the <b>snapshot <c>.yml</c></b> — and publishes a
/// pointer to each <i>inside the answer</i>, relative to the child's working
/// directory, which is the output root. BrowserAI's after-the-fact sweep moved
/// both into typed folders, so <b>every one of those pointers named a file that
/// was no longer there</b>.
/// </para>
/// <para>
/// <b>The console half was worse, because the file is still open.</b> Measured
/// 2026-08-20 through the published binary: after the first sweep the child
/// appended again, recreated the log at the output root, and the next sweep
/// collided with the moved copy and landed it as <c>-2</c>. The answer then said
/// <c>console-….log#L25-L28</c> about a file with 24 lines in it, while those
/// four entries sat in <c>console-….log-2</c> at <i>its</i> lines 1 to 4. A
/// third call produced <c>-3</c>. <b>Bare upstream does not have this</b>:
/// nothing there moves the file.
/// </para>
/// <para>
/// <b>Driven end to end against a real Chromium, and it has to be.</b> The
/// pointer's text, the working directory it is relative to, and the fact that
/// the log is appended to rather than rewritten are all upstream's — a double
/// would be asserting this file's own idea of them. The in-process arm below
/// covers the mechanism deterministically; this one covers that the mechanism is
/// aimed at the right thing.
/// </para>
/// </remarks>
internal sealed partial class ArtifactPointerTests
{
    /// <summary>
    /// Every pointer a real child hands the model resolves to the file it names
    /// and to the lines it names.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryPointerARealChildPublishesResolves()
    {
        SuiteEnvironment.RequirePublishedSlice();

        using var scratch = ScratchDirectory.Create("artifact-pointers");

        // The job this client owns is the containment net: an assertion that
        // throws below closes it, and KILL_ON_JOB_CLOSE takes the browser with
        // it. Nothing in this file terminates anything by name.
        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var session = Path.Combine(scratch.Path, "pointers");

        var created = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "reading back every pointer the child publishes",
        });

        await Assert.That((bool?)created["isError"]).IsNotEqualTo(true);

        // Twenty-four entries in one go, so the first pointer is a range rather
        // than a single line.
        var navigated = TextOf(await CallAsync(client, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,<h1>one</h1><script>for (let i = 0; i < 24; i++) console.log('line ' + i);</script>",
            [SessionToolSurface.SessionParameter] = session,
            [SessionToolSurface.WhyParameter] = "producing a console log and a snapshot in one call",
        }));

        // ⚠️ TWO MORE BATCHES WITHOUT A NAVIGATION, which is what makes the
        // child APPEND to the log it already made rather than start a new one.
        // A second navigation would produce a second file and never reach the
        // collision this exists for.
        var appended = new List<string>();

        foreach (var start in new[] { 100, 200 })
        {
            appended.Add(TextOf(await CallAsync(client, "browser_evaluate", new JsonObject
            {
                ["function"] = $"() => {{ for (let i = {start}; i < {start + 4}; i++) console.log('line ' + i); return 'ok'; }}",
                [SessionToolSurface.SessionParameter] = session,
                [SessionToolSurface.WhyParameter] = "appending to a log BrowserAI has already seen",
            })));
        }

        var output = Path.Combine(session, SessionLayout.OutputFolderName);

        // ⚠️ THE COLLISION, ASSERTED FIRST AND BY ITSELF. Before the fix this
        // directory held `console-….log`, `console-….log-2` and `-3`, and the
        // suffixes are the visible half of the defect: a file BrowserAI renamed
        // is a file no pointer anywhere names.
        var logs = Directory.EnumerateFiles(output, "console-*.log", SearchOption.AllDirectories).ToList();

        await Assert.That(logs.Count)
            .IsEqualTo(1)
            .Because($"the child appends to one console log; these are the files that exist: {string.Join(", ", logs.Select(Path.GetFileName))}");

        // And every pointer, in every answer, against the file it names and the
        // lines it names.
        var checked_ = 0;

        foreach (var answer in new[] { navigated }.Concat(appended))
        {
            foreach (Match pointer in ConsolePointer().Matches(answer))
            {
                checked_++;

                var named = Path.Combine(output, pointer.Groups["file"].Value);

                await Assert.That(File.Exists(named))
                    .IsTrue()
                    .Because($"the answer said '{pointer.Value}', and the child's working directory is the output root");

                var lines = await File.ReadAllLinesAsync(named);
                var last = int.Parse(pointer.Groups["last"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var first = int.Parse(pointer.Groups["first"].Value, System.Globalization.CultureInfo.InvariantCulture);

                // The lines have to BE there. Before the fix the third answer
                // said #L29-L31 about a 24-line file.
                await Assert.That(lines.Length)
                    .IsGreaterThanOrEqualTo(last)
                    .Because($"the answer said '{pointer.Value}' and '{Path.GetFileName(named)}' has {lines.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} lines");

                // And they have to be the RIGHT lines, which a count alone does
                // not say: a log that had been truncated and rewritten would
                // have enough lines and the wrong ones in them.
                await Assert.That(lines[first - 1]).Contains("line ");
                await Assert.That(lines[last - 1]).Contains("line ");
            }
        }

        // Not vacuous: three answers, three pointers, and the last of them is
        // the one that was wrong.
        await Assert.That(checked_).IsEqualTo(3);

        // ⚠️ AND THE SNAPSHOT LINK, which is the same defect wearing a Markdown
        // link. `./page-….yml` is relative to the child's working directory.
        var links = SnapshotLink().Matches(navigated);

        await Assert.That(links.Count).IsEqualTo(1);

        var snapshot = Path.Combine(output, links[0].Groups["file"].Value);

        await Assert.That(File.Exists(snapshot))
            .IsTrue()
            .Because($"the answer said '{links[0].Value}' and the child's working directory is the output root");

        await Assert.That((await File.ReadAllTextAsync(snapshot)).Length).IsGreaterThan(0);

        // And BrowserAI said what it did with them, rather than leaving a caller
        // to notice that a typed folder it expected is empty.
        await Assert.That(navigated).Contains("LEFT this artifact where the browser wrote it");
        await Assert.That(navigated).Contains(Path.GetFileName(snapshot));

        _ = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
        {
            ["directory"] = session,
            [SessionToolSurface.WhyParameter] = "the run is over",
        });
    }

    /// <summary>
    /// A file the child named is left alone however many times the sweep runs,
    /// and one it did not name is still sorted.
    /// </summary>
    /// <remarks>
    /// <b>The in-process half, and it holds the property the real arm cannot
    /// isolate.</b> The console log is named in the answer that <i>creates</i>
    /// entries and in none of the answers that follow — so a set scoped to one
    /// call would leave it movable on the very next call, which is precisely the
    /// version of the fix somebody would write. The second arm is the control:
    /// the sweep still does the job it was built for.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ANamedFileSurvivesEveryLaterSweepAndAnUnnamedOneIsStillSorted()
    {
        const string Log = "console-2026-08-20T09-00-00-000Z.log";

        await using var sessions = RigSessionEnvironment.Create(child =>
            child.Tools["browser_navigate"] = new FakeToolBehaviour());

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var output = Path.Combine(rig.Session!, SessionLayout.OutputFolderName);

        // The child's own file, at the output root, and an answer that names it
        // exactly as upstream's events line does.
        await File.WriteAllTextAsync(Path.Combine(output, Log), "one\ntwo\n");

        sessions.SessionChildren[0].Tools["browser_navigate"] = new FakeToolBehaviour
        {
            RawResult = $$"""{"content":[{"type":"text","text":"### Events\n- New console entries: {{Log}}#L1-L2"}]}""",
        };

        var named = TextOf(await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,x",
            [SessionToolSurface.SessionParameter] = rig.Session!,
            [SessionToolSurface.WhyParameter] = "the call whose answer names the log",
        }));

        await Assert.That(File.Exists(Path.Combine(output, Log))).IsTrue();
        await Assert.That(named).Contains("LEFT this artifact where the browser wrote it");

        // ⚠️ THE ARM THAT MATTERS. Three more calls whose answers say nothing
        // about the log, with the child appending to it in between exactly as a
        // real one does. A per-call rule passes the assertion above and fails
        // here.
        foreach (var round in Enumerable.Range(0, 3))
        {
            await File.AppendAllTextAsync(Path.Combine(output, Log), $"more {round.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");

            sessions.SessionChildren[0].Tools["browser_navigate"] = new FakeToolBehaviour
            {
                RawResult = """{"content":[{"type":"text","text":"### Page\n- Page URL: about:blank"}]}""",
            };

            _ = await CallAsync(rig, "browser_navigate", new JsonObject
            {
                ["url"] = "data:text/html,x",
                [SessionToolSurface.SessionParameter] = rig.Session!,
                [SessionToolSurface.WhyParameter] = "a call that says nothing about the log",
            });
        }

        await Assert.That(File.Exists(Path.Combine(output, Log))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(output, "console"))).IsFalse();
        await Assert.That((await File.ReadAllLinesAsync(Path.Combine(output, Log))).Length).IsEqualTo(5);

        // And it is recorded ONCE rather than on every call, because a note that
        // repeated the same file five times is a note nobody reads.
        var index = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(rig.Session!, ArtifactRouter.IndexFileName)))!;

        await Assert.That((index["artifacts"]?.AsArray() ?? []).Count(entry => (string?)entry?["path"] == Path.Combine(output, Log)))
            .IsEqualTo(1);

        // ⚠️ THE CONTROL. A file nothing named is still sorted, so the fix is a
        // rule about pointers rather than the sweep being switched off.
        await File.WriteAllBytesAsync(Path.Combine(output, "quarterly-report.pdf"), new byte[16]);

        _ = await CallAsync(rig, "browser_navigate", new JsonObject
        {
            ["url"] = "data:text/html,x",
            [SessionToolSurface.SessionParameter] = rig.Session!,
            [SessionToolSurface.WhyParameter] = "the call that sweeps the download",
        });

        await Assert.That(File.Exists(Path.Combine(rig.Session!, SessionLayout.DownloadsFolderName, "quarterly-report.pdf"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, "quarterly-report.pdf"))).IsFalse();
    }

    /// <summary>Upstream's events line: a file name and an inclusive line range.</summary>
    [GeneratedRegex(@"New console entries: (?<file>\S+?\.log)#L(?<first>\d+)-L(?<last>\d+)")]
    private static partial Regex ConsolePointer();

    /// <summary>Upstream's snapshot link, relative to the child's working directory.</summary>
    [GeneratedRegex(@"\[Snapshot\]\(\./(?<file>[^)]+\.yml)\)")]
    private static partial Regex SnapshotLink();

    private static async Task<JsonObject> CallAsync(RawStdioClient client, string tool, JsonObject arguments) =>
        await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
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
