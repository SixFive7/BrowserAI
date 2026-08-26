// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// Upstream's file-access roots are BrowserAI's containment, and this is the
/// measurement that says so.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>BrowserAI deleted its own <c>filename</c> gate on 2026-08-26.</b>
/// <c>ArtifactFilename</c> refused nine path shapes on the string —
/// <c>..</c>, drive-relative, UNC, rooted, Win32 device, reserved device names,
/// trailing space or dot, a trailing separator, empty — and
/// <c>ArtifactRouter.Plan</c> combined what survived and refused anything that
/// still landed outside the session. It was a weaker duplicate of a check the
/// child already performs, applied one hop earlier, and it was the reason
/// <c>allowUnrestrictedFileAccess</c> could be written <c>true</c>. Both halves
/// went together: the gate is gone and the key is <c>false</c>.
/// </para>
/// <para>
/// <b>So the containment is now entirely upstream's, and a claim about somebody
/// else's code is exactly the kind that has to be measured rather than
/// read.</b> <c>checkFile</c> refuses a resolved name that is inside neither
/// <c>outputDir</c> nor the child's working directory; BrowserAI writes both as
/// <c>&lt;session&gt;\output</c>. That is four sentences of somebody else's
/// source and one config key, floating, with no golden snapshot over it — the
/// tool descriptions are snapshotted and this behaviour is not.
/// </para>
/// <para>
/// <b>Through the published binary and a real browser, both directions.</b> A
/// name that climbs out must be refused with nothing written; a plain one must
/// land. Either arm alone is satisfiable by a broken product — a child that
/// refused everything would pass the first, and one that refused nothing would
/// pass the second.
/// </para>
/// </remarks>
internal sealed partial class FileAccessRootTests
{
    /// <summary>
    /// A write aimed outside the session is refused by the child, and a write
    /// inside it lands.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWriteOutsideTheSessionIsRefusedAndOneInsideItLands()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("file-access-roots");

        // The job this client owns is the containment net for the PROCESS tree:
        // an assertion that throws below closes it, and KILL_ON_JOB_CLOSE takes
        // the browser with it. Nothing in this file terminates anything by name.
        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            scratch.Path,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var session = Path.Combine(scratch.Path, "contained");
        var output = Path.Combine(session, SessionLayout.OutputFolderName);

        var created = await CallAsync(client, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "establishing whether upstream's file-access roots actually contain a write",
        });

        await Assert.That((bool?)created["isError"]).IsNotEqualTo(true);

        var navigated = await CallAsync(client, "browser_navigate", new JsonObject
        {
            ["url"] = SliceRun.TargetUrl,
            [SessionToolSurface.SessionParameter] = session,
            [SessionToolSurface.WhyParameter] = "putting a page up so a screenshot has something to take",
        });

        await Assert.That((bool?)navigated["isError"])
            .IsNotEqualTo(true)
            .Because($"the page never came up, so nothing below measures containment. The answer was: {TextOf(navigated)}");

        // ── The arm that must LAND ──────────────────────────────────────────
        var inside = await CallAsync(client, "browser_take_screenshot", new JsonObject
        {
            ["filename"] = "inside.png",
            [SessionToolSurface.SessionParameter] = session,
            [SessionToolSurface.WhyParameter] = "a plain relative name, which is what a caller is told to send",
        });

        await Assert.That((bool?)inside["isError"])
            .IsNotEqualTo(true)
            .Because($"an ordinary relative filename was refused, which would make the roots a wall rather than a boundary. The answer was: {TextOf(inside)}");

        // Flat, at the output root, under the name the caller chose. Nothing
        // rewrote the argument and nothing moved the file afterwards.
        await Assert.That(File.Exists(Path.Combine(output, "inside.png"))).IsTrue();

        // ── The arms that must be REFUSED ───────────────────────────────────
        //
        // Three shapes, chosen because they are the three a model actually
        // writes: a climb, an absolute path on the same drive, and a climb with
        // a forward slash. `escaped-*.png` is aimed at the scratch directory
        // above the session, which this test owns and can therefore prove is
        // untouched.
        var targets = new (string Filename, string Lands)[]
        {
            (@"..\..\escaped-climb.png", Path.Combine(scratch.Path, "escaped-climb.png")),
            ("../../escaped-slash.png", Path.Combine(scratch.Path, "escaped-slash.png")),
            (Path.Combine(scratch.Path, "escaped-absolute.png"), Path.Combine(scratch.Path, "escaped-absolute.png")),
        };

        foreach (var (filename, lands) in targets)
        {
            var refused = await CallAsync(client, "browser_take_screenshot", new JsonObject
            {
                ["filename"] = filename,
                [SessionToolSurface.SessionParameter] = session,
                [SessionToolSurface.WhyParameter] = "aiming a write outside the session, which must not succeed",
            });

            var text = TextOf(refused);

            // ⚠️ HALT-B's SUBJECT. If this is not an error, upstream's roots do
            // not contain a write to the session and the whole containment
            // decision is void -- the failure message carries the answer so a
            // reader is looking at what actually happened rather than at
            // `Expected to be true but found False`.
            await Assert.That((bool?)refused["isError"])
                .IsTrue()
                .Because($"'{filename}' was NOT refused. Upstream's file-access roots do not contain a write, and BrowserAI has no gate of its own any more. The answer was: {text}");

            // The refusal has to be the ROOTS refusing, rather than the
            // screenshot failing for some unrelated reason that happens to
            // produce an error.
            await Assert.That(text)
                .Contains("outside allowed roots")
                .Because($"'{filename}' produced an error that is not upstream's file-access refusal, so this arm proves nothing about containment. The answer was: {text}");

            // And nothing is on disk where it was aimed. An error message is a
            // claim; this is the fact.
            await Assert.That(File.Exists(lands))
                .IsFalse()
                .Because($"'{filename}' was reported as refused and the file is at '{lands}' anyway");
        }

        // Nothing leaked into the session's parent under any name, which is the
        // check that does not depend on having predicted where each shape would
        // have landed.
        await Assert.That(Directory.EnumerateFiles(scratch.Path, "*.png").Any()).IsFalse();

        _ = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
        {
            ["directory"] = session,
            [SessionToolSurface.WhyParameter] = "the run is over",
        });
    }

    /// <summary>
    /// Every pointer a real child publishes resolves, because nothing moves the
    /// file it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Kept from <c>ArtifactPointerTests.EveryPointerARealChildPublishesResolves</c>,
    /// deleted 2026-08-26 with the rest of that file, and its subject has
    /// inverted.</b> It was the reproduction of two defects: the sweep moved the
    /// console log and the snapshot <c>.yml</c> into typed folders, so upstream's
    /// own pointers named files that were no longer there — and the console half
    /// compounded, because the child holds that file open, recreated it at the
    /// root and the next sweep landed the copy as <c>-2</c>, then <c>-3</c>, with
    /// the answer citing lines 25-28 of a 24-line file.
    /// </para>
    /// <para>
    /// <b>Nothing sweeps now, so the property is free — and that is exactly why
    /// it is still asserted.</b> "Correct by construction" is a claim about a
    /// construction, and the construction is one config key and one working
    /// directory. This is what would go red if either moved.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryPointerARealChildPublishesResolvesBecauseNothingMovesIt()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();

        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("artifact-pointers");

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

        // ⚠️ ONE LOG, ASSERTED FIRST AND BY ITSELF. Before the sweep was deleted
        // this directory held `console-….log`, `console-….log-2` and `-3`, and
        // the suffixes are the visible half of the defect: a file BrowserAI
        // renamed is a file no pointer anywhere names.
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
                var last = int.Parse(pointer.Groups["last"].Value, CultureInfo.InvariantCulture);
                var first = int.Parse(pointer.Groups["first"].Value, CultureInfo.InvariantCulture);

                // The lines have to BE there. Under the sweep the third answer
                // said #L29-L31 about a 24-line file.
                await Assert.That(lines.Length)
                    .IsGreaterThanOrEqualTo(last)
                    .Because($"the answer said '{pointer.Value}' and '{Path.GetFileName(named)}' has {lines.Length.ToString(CultureInfo.InvariantCulture)} lines");

                // And they have to be the RIGHT lines, which a count alone does
                // not say: a log that had been truncated and rewritten would
                // have enough lines and the wrong ones in them.
                await Assert.That(lines[first - 1]).Contains("line ");
                await Assert.That(lines[last - 1]).Contains("line ");
            }
        }

        // Not vacuous: three answers, three pointers, and the last of them is
        // the one that used to be wrong.
        await Assert.That(checked_).IsEqualTo(3);

        // ⚠️ AND THE SNAPSHOT LINK, which was the same defect wearing a Markdown
        // link. `./page-….yml` is relative to the child's working directory.
        var links = SnapshotLink().Matches(navigated);

        await Assert.That(links.Count).IsEqualTo(1);

        var snapshot = Path.Combine(output, links[0].Groups["file"].Value);

        await Assert.That(File.Exists(snapshot))
            .IsTrue()
            .Because($"the answer said '{links[0].Value}' and the child's working directory is the output root");

        await Assert.That((await File.ReadAllTextAsync(snapshot)).Length).IsGreaterThan(0);

        // ⚠️ AND NOTHING WAS APPENDED TO THE ANSWER TO EXPLAIN ANY OF IT. The
        // note used to say where each file had been left or moved to; there is
        // nothing to explain now, and this is the real-child half of
        // `LosslessPassthroughTests`' byte-identity claim.
        await Assert.That(navigated).DoesNotContain("BrowserAI");

        _ = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
        {
            ["directory"] = session,
            [SessionToolSurface.WhyParameter] = "the run is over",
        });
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

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
