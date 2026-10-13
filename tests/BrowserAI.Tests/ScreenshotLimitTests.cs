// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers.Binary;
using System.Text.Json.Nodes;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Storage;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A Chromium screenshot larger than Chromium captures faithfully is refused, and
/// every other screenshot goes back as the browser server wrote it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q380, decided 2026-10-04 by the maintainer, in his words verbatim:</b>
/// <i>"9 d - and add a todo to the repo to track the progress of the bug for when
/// to remove our checks. Also, the refusal should mention the chromium bug
/// link."</i> Measured the same day at <c>chromium-1247</c>: a page 16,384 px
/// tall comes back whole and one 16,385 px tall comes back with its last row equal
/// to its first, reported as a success.
/// </para>
/// <para>
/// <b>Three layers.</b> The header reader on bytes built here; the proxy's
/// decision against the in-process double, for the inline path, the file path,
/// both sides of the line and a Firefox session; and a real Chromium against pages
/// of exactly 16,384 and 16,385 px, which is the arm planted red against the tree
/// before the check existed.
/// </para>
/// </remarks>
internal sealed class ScreenshotLimitTests
{
    /// <summary>
    /// The head of a PNG whose header says it is <paramref name="width"/> by
    /// <paramref name="height"/>: the signature and the IHDR chunk, which is all
    /// the size is read from.
    /// </summary>
    /// <param name="width">How wide it says it is.</param>
    /// <param name="height">How tall it says it is.</param>
    /// <returns>The bytes.</returns>
    public static byte[] PngHead(int width, int height)
    {
        var head = new byte[33];

        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(head, 0);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(head.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(16, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(20, 4), (uint)height);

        // Bit depth 8, colour type 2, and the three methods; the CRC is left zero,
        // because nothing reads it.
        head[24] = 8;
        head[25] = 2;

        return head;
    }

    /// <summary>
    /// The head of a baseline JPEG whose start-of-frame says it is
    /// <paramref name="width"/> by <paramref name="height"/>, behind a JFIF segment
    /// and a quantisation table, the order Chromium's encoder writes them in.
    /// </summary>
    /// <param name="width">How wide it says it is.</param>
    /// <param name="height">How tall it says it is.</param>
    /// <returns>The bytes.</returns>
    public static byte[] JpegHead(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        // APP0, JFIF, 16 bytes of segment.
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);

        // DQT, one table of 64 entries.
        bytes.AddRange([0xFF, 0xDB, 0x00, 0x43, 0x00]);
        bytes.AddRange(Enumerable.Repeat((byte)1, 64));

        // SOF0: precision, height, width, three components.
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);

        return [.. bytes];
    }

    /// <summary>
    /// The size is read off a PNG's IHDR and a JPEG's start-of-frame, and nothing
    /// else is taken for a size.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheSizeIsReadOffAPngOrAJpegHeadAndNothingElse()
    {
        await Assert.That(ScreenshotLimit.SizeOf(PngHead(1920, 16385))).IsEqualTo(new ImageSize(1920, 16385));
        await Assert.That(ScreenshotLimit.SizeOf(JpegHead(20000, 2000))).IsEqualTo(new ImageSize(20000, 2000));

        // Too short to carry a size, and bytes that are neither format.
        await Assert.That(ScreenshotLimit.SizeOf(PngHead(1920, 16385).AsSpan(0, 20))).IsNull();
        await Assert.That(ScreenshotLimit.SizeOf(JpegHead(1920, 16385).AsSpan(0, 30))).IsNull();
        await Assert.That(ScreenshotLimit.SizeOf("RIFF\0\0\0\0WEBPVP8 "u8)).IsNull();
        await Assert.That(ScreenshotLimit.SizeOf([])).IsNull();
    }

    /// <summary>
    /// An inline image past the line, tall or wide, is refused with the bug's link
    /// and what to do instead, and recorded as a refused call; one at the line goes
    /// back as the browser server wrote it.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnInlineChromiumScreenshotPastTheLineIsRefusedAndOneAtTheLineIsNot()
    {
        foreach (var (width, height) in new[] { (1920, 16385), (16385, 2000) })
        {
            var (answer, directory) = await ScreenshotAsync(ProvisionedBrowsers.Chromium, InlineAnswer(width, height));

            await Assert.That((bool?)answer["isError"]).IsTrue();
            await Assert.That(ImagesIn(answer)).IsEqualTo(0);
            await Assert.That(TextOf(answer)).IsEqualTo(SessionErrors.ScreenshotPastChromiumsLimit(width, height, file: null));
            await Assert.That(TextOf(answer)).Contains(ScreenshotLimit.ChromiumIssue);

            var row = (await RecordedSession.LogOfAsync(directory)).Single(entry => entry.Tool == ScreenshotLimit.ScreenshotTool);

            await Assert.That(row.Outcome).IsEqualTo(SessionStore.Failed);
            await Assert.That(row.Failure).IsEqualTo(TextOf(answer));
        }

        var (whole, _) = await ScreenshotAsync(ProvisionedBrowsers.Chromium, InlineAnswer(1920, ScreenshotLimit.LargestFaithfulSide));

        await Assert.That((bool?)whole["isError"]).IsNotEqualTo(true);
        await Assert.That(ImagesIn(whole)).IsEqualTo(1);
    }

    /// <summary>
    /// A screenshot written to a <c>filename</c> is judged by the file its answer
    /// names, and the refusal names that file, which stays where it is.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AScreenshotWrittenToAFileIsJudgedByTheFileItsAnswerNames()
    {
        // The double answers with a link to the file it writes, so the path has to
        // be known before the rig exists: a directory under the scratch root.
        var directory = Path.Combine(await ScratchRoot.PathAsync(), $"tall-file-{Guid.NewGuid():N}");
        var file = Path.Combine(SessionPath.For(directory).FullPath, SessionLayout.OutputFolderName, "tall.png");

        await using var sessions = await RigSessionEnvironment.CreateAsync(
            child => child.Tools[ScreenshotLimit.ScreenshotTool] = new FakeToolBehaviour
            {
                WritesArtifactContent = PngHead(1920, 20000),
                // In two pieces, so the documentation scan, which reads this file as
                // text, does not take the answer's link for one of the tree's own.
                RawResult = TextAnswer("### Result\n- [Screenshot of full page]" + $"({file})"),
            },
            opensDefaultSession: false);

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        _ = await InitAsync(rig, directory, ProvisionedBrowsers.Chromium);

        var answer = await CallAsync(rig, ScreenshotLimit.ScreenshotTool, new JsonObject
        {
            [SessionToolSurface.SessionParameter] = directory,
            [SessionToolSurface.WhyParameter] = "the suite taking a screenshot into a file",
            ["fullPage"] = true,
            ["filename"] = "tall.png",
        });

        await Assert.That((bool?)answer["isError"]).IsTrue();
        await Assert.That(TextOf(answer)).IsEqualTo(SessionErrors.ScreenshotPastChromiumsLimit(1920, 20000, file));

        // Nothing in BrowserAI deletes an artifact.
        await Assert.That(File.Exists(file)).IsTrue();
    }

    /// <summary>
    /// A Firefox session's screenshot is never read: Firefox refuses past its own
    /// limit with an error, and below it the image is whole.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFirefoxSessionsScreenshotIsNeverRead()
    {
        var (answer, _) = await ScreenshotAsync(ProvisionedBrowsers.Firefox, InlineAnswer(1920, 16385));

        await Assert.That((bool?)answer["isError"]).IsNotEqualTo(true);
        await Assert.That(ImagesIn(answer)).IsEqualTo(1);
    }

    /// <summary>
    /// Against a <b>real</b> Chromium: a page 16,385 px tall is refused, inline and
    /// into a file, and a page 16,384 px tall comes back whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Planted red against the tree before the check existed</b>: the 16,385 px
    /// page came back as a success, its image 1920x16385.
    /// </para>
    /// <para>
    /// ⚠️ <b>The maintainer's 94, 2026-10-12, in his words verbatim:</b> <i>"94 your
    /// recommendation"</i>, which is a, b and c below, after this arm lost its browser
    /// during a full-page capture in 7 of 77 full runs and never in a run alone, 5 of the
    /// 7 with the machine's commit charge healthy. Why the browser ends is not known, and
    /// the three are there to find out and to keep the arm out of what it met.
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>(a) A red keeps the rig's folder</b>, whose session folder holds the session's
    /// record with the close's reason and the browser's exit code, in
    /// <see cref="KeptOnRed.Root"/>, and the failure says where. The session also writes a
    /// line of its own when its browser ends under a call,
    /// <c>SessionLog.BrowserEndedDuringACall</c>, with the exit code.
    /// </description></item>
    /// <item><description>
    /// <b>(b) The browser writes its own log to its standard error</b>, through
    /// <c>RigSessionEnvironment.WithChromiumLoggingAsync</c>, the rig's change to the
    /// launch and test-only, so the browser logs Playwright keeps, and puts into an error
    /// about the browser closing, carry every line Chromium logs and not its errors alone.
    /// The session is opened with <c>debug</c>, so the same lines reach this arm's own
    /// output on a passing run too.
    /// </description></item>
    /// <item><description>
    /// <b>(c) It runs beside nothing</b>: the keyless <c>[NotInParallel]</c>, which in
    /// TUnit puts the arm after every arm that runs in parallel and beside none of them. In
    /// a full run it ran from about 13 s in, beside up to 129 other tests, and took 26.7 to
    /// 33.7 s where it takes 6.0 to 10.7 s alone, and 4.0 s in the first run that ran it
    /// beside nothing. <b>The cost is its whole time alone, added to the critical path</b>,
    /// about 4 to 11 s of every full run, which nothing overlaps any more. <b>The
    /// reason</b> is that the arm's subject is the refusal and not a loaded machine, and a
    /// browser lost to the load said nothing about the refusal; what a loaded user machine
    /// may do to a large capture is the arm's open row in <c>HAZARDS.md</c>, which (a) and
    /// (b) are there to answer.
    /// </description></item>
    /// </list>
    /// <para>
    /// ⚠️ <b>The maintainer's 132 b, 2026-10-13, in his words verbatim:</b> <i>"132 b - after you used the data to report to the upstreams"</i>.
    /// (a) stays: the kept rig and event 14 named the cause at the release run's red at
    /// <c>44368995</c>, Chromium's own stack sampling profiler crashing the browser, which every
    /// Chromium launch now keeps from starting (<see cref="StackProfilerTests"/>). (b) and (c) are
    /// taken out, once the reports upstream had what (a) kept: Chromium wrote nothing about that
    /// crash to its standard error, and the profiler sampled the first 30 s of every browser whatever
    /// ran beside it, so the arm runs among the others again and its time alone is off the critical
    /// path. <i>Added 2026-10-13 by addition.</i>
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AgainstARealChromiumAPageOnePixelPastTheLineIsRefusedAndOneAtItIsNot()
    {
        await SuiteEnvironment.RequireProvisionedChromiumAsync();

        await using var sessions = await RigSessionEnvironment.CreateAsync(opensDefaultSession: false, realSessionChildren: true);

        try
        {
            // Disposed before the catch below runs, so the session has let its folder go
            // and its record says how it closed by the time a red keeps it.
            await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

            await TakeBothSidesOfTheLineAsync(rig, Path.Combine(sessions.Root, "real-tall"));
        }
        catch (Exception failure)
        {
            throw await KeptOnRed.KeepAsync(sessions.Root, failure, "screenshot-limit");
        }
    }

    /// <summary>
    /// The real Chromium arm's two pages, each taken whole inline and into a file, and what
    /// each answer must be.
    /// </summary>
    /// <param name="rig">The rig, its sessions' children real.</param>
    /// <param name="directory">The session directory to open.</param>
    /// <returns>The assertion task.</returns>
    private static async Task TakeBothSidesOfTheLineAsync(McpTestHarness rig, string directory)
    {
        _ = await InitAsync(rig, directory, ProvisionedBrowsers.Chromium);

        foreach (var height in new[] { ScreenshotLimit.LargestFaithfulSide + 1, ScreenshotLimit.LargestFaithfulSide })
        {
            var navigated = await CallAsync(rig, "browser_navigate", new JsonObject
            {
                [SessionToolSurface.SessionParameter] = directory,
                [SessionToolSurface.WhyParameter] = "the suite opening a page of an exact height",
                ["url"] = $"data:text/html,<style>html,body{{margin:0}}</style><div style=\"height:{height}px;background:rgb(48,96,192)\"></div>",
            });

            await Assert.That((bool?)navigated["isError"]).IsNotEqualTo(true).Because(TextOf(navigated));

            var inline = await CallAsync(rig, ScreenshotLimit.ScreenshotTool, new JsonObject
            {
                [SessionToolSurface.SessionParameter] = directory,
                [SessionToolSurface.WhyParameter] = "the suite taking the page whole, inline",
                ["fullPage"] = true,
            });

            var named = $"page-{height}.png";

            var filed = await CallAsync(rig, ScreenshotLimit.ScreenshotTool, new JsonObject
            {
                [SessionToolSurface.SessionParameter] = directory,
                [SessionToolSurface.WhyParameter] = "the suite taking the page whole, into a file",
                ["fullPage"] = true,
                ["filename"] = named,
            });

            var file = Path.Combine(SessionPath.For(directory).FullPath, SessionLayout.OutputFolderName, named);

            // What the browser server wrote is the page's own height either way;
            // what changes is whether it was handed over.
            await Assert.That(ScreenshotLimit.SizeOf(await File.ReadAllBytesAsync(file))).IsEqualTo(new ImageSize(BrowserConfiguration.DefaultViewport.Width, height));

            if (height > ScreenshotLimit.LargestFaithfulSide)
            {
                await Assert.That((bool?)inline["isError"]).IsTrue().Because(TextOf(inline));
                await Assert.That(ImagesIn(inline)).IsEqualTo(0);
                await Assert.That(TextOf(inline)).StartsWith("The screenshot was not returned.");
                await Assert.That(TextOf(inline)).Contains(ScreenshotLimit.ChromiumIssue);

                await Assert.That((bool?)filed["isError"]).IsTrue().Because(TextOf(filed));
                await Assert.That(TextOf(filed)).IsEqualTo(SessionErrors.ScreenshotPastChromiumsLimit(BrowserConfiguration.DefaultViewport.Width, height, file));
            }
            else
            {
                await Assert.That((bool?)inline["isError"]).IsNotEqualTo(true).Because(TextOf(inline));
                await Assert.That(ImagesIn(inline)).IsEqualTo(1);
                await Assert.That((bool?)filed["isError"]).IsNotEqualTo(true).Because(TextOf(filed));
            }
        }
    }

    /// <summary>Opens a session of the given family in a rig of its own and takes one full-page screenshot.</summary>
    /// <param name="browser">The family.</param>
    /// <param name="behaviour">What the double answers the screenshot with.</param>
    /// <returns>The answer, and the session's directory.</returns>
    private static async Task<(JsonObject Answer, string Directory)> ScreenshotAsync(string browser, FakeToolBehaviour behaviour)
    {
        await using var sessions = await RigSessionEnvironment.CreateAsync(
            child => child.Tools[ScreenshotLimit.ScreenshotTool] = behaviour,
            opensDefaultSession: false);

        // The rig seeds its own scratch browsers root with Chromium only; a
        // Firefox session's double launches nothing, so its family is seeded the
        // same way, the marker written the way upstream writes it.
        if (BrowserConfiguration.IsFirefox(browser))
        {
            await InstallationMarker.WriteAsync(Path.Combine(sessions.Environment.Paths.BrowsersDirectory, $"firefox-{BrowserAiPaths.FirefoxRevision}"));
        }

        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, $"shot-{browser}");

        _ = await InitAsync(rig, directory, browser);

        var answer = await CallAsync(rig, ScreenshotLimit.ScreenshotTool, new JsonObject
        {
            [SessionToolSurface.SessionParameter] = directory,
            [SessionToolSurface.WhyParameter] = "the suite taking a full-page screenshot",
            ["fullPage"] = true,
        });

        return (answer, directory);
    }

    /// <summary>An answer carrying one inline PNG whose header says the given size.</summary>
    /// <param name="width">How wide.</param>
    /// <param name="height">How tall.</param>
    /// <returns>The double's behaviour.</returns>
    private static FakeToolBehaviour InlineAnswer(int width, int height) => new()
    {
        RawResult = $$"""{"content":[{"type":"text","text":"### Result\n- [Screenshot of full page](page.png)"},{"type":"image","data":"{{Convert.ToBase64String(PngHead(width, height))}}","mimeType":"image/png"}]}""",
    };

    /// <summary>An answer carrying one text block.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The raw result.</returns>
    private static string TextAnswer(string text) =>
        new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        }.ToJsonString();

    /// <summary>Opens a session, refusing the arm when it cannot.</summary>
    /// <param name="rig">The rig.</param>
    /// <param name="directory">The session directory.</param>
    /// <param name="browser">The family.</param>
    /// <returns>The answer.</returns>
    private static async Task<JsonObject> InitAsync(McpTestHarness rig, string directory, string browser)
    {
        var answer = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "a session whose screenshots the suite judges",
            ["browser"] = browser,
            ["headed"] = false,
            ["transcript"] = false,
            ["captureNetwork"] = false,
            ["idleMinutes"] = 10,
        });

        if ((bool?)answer["isError"] is true)
        {
            throw new InvalidOperationException($"The arm could not open '{directory}': {TextOf(answer)}");
        }

        return answer;
    }

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static int ImagesIn(JsonObject result) =>
        (result["content"]?.AsArray() ?? []).Count(block => (string?)block?["type"] is "image");

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
