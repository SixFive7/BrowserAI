// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The acceptance test for the vertical slice: a published NativeAOT binary
/// proxying a real <c>@playwright/mcp</c> child, driven by a client that shares
/// no protocol code with it.
/// </summary>
/// <remarks>
/// Every assertion here is aimed at something that would otherwise report
/// healthy — a tool list quietly shortened by the SDK's convenience overload, a
/// navigation whose result is an error nobody reads, a node process left running
/// after the binary that owned it was killed.
/// </remarks>
internal sealed class VerticalSliceTests
{
    [Test]
    public async Task ToolsListReturnsTheChildsToolsWithUpstreamsOwnNames()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        // `initialize` has to advertise tools, and it only does so because
        // McpServerOptions.Capabilities.Tools is set: a server carrying tool
        // handlers but no declared capability tells the caller it has none, and
        // a caller that respects capabilities then never asks. The tool list
        // below would still be right, and the surface would still be invisible.
        await Assert.That(run.InitializeResult["capabilities"]?["tools"]).IsNotNull();

        // ⚠️ AND NOTHING ELSE. Added 2026-08-18, and it is the founding failure
        // shape caught in our own handshake: the SDK's McpServerImpl builds a
        // fresh ServerCapabilities and gates Tools, Prompts, Resources and
        // Completion on configuration -- but ConfigureLogging has no such guard
        // and sets `Logging = new()` unconditionally, so BrowserAI advertised MCP
        // logging it has never implemented, never declared, and would never emit
        // a single notifications/message for. A client that called
        // logging/setLevel got {} and then silence for ever.
        //
        // Asserted as the WHOLE object rather than as "logging is absent",
        // because the next capability the SDK adds a guardless Configure* for
        // would be advertised the same silent way. The value is byte-identical to
        // the child's own snapshot, which is the second thing this fixes: the two
        // ends of the proxy now agree about what this server is.
        //
        // BrowserProxy.UnadvertiseLogging is what removes it, from an outgoing
        // message filter -- the only route that neither lies nor suppresses
        // MCP9005 on an obsolete property the constructor overwrites anyway.
        await Assert.That(run.InitializeResult["capabilities"]?.ToJsonString())
            .IsEqualTo(UpstreamSurface.ServerCapabilities());

        // Byte for byte, and in upstream's order. Renaming is settled as
        // forbidden, so this asserts identity rather than exercising a map; the
        // day a rename map appears, this is what says so. The expected list is
        // computed from the committed snapshot, which the build regenerates from
        // the resolved payload, so an upstream change is a snapshot diff first
        // and this test second.
        //
        // Compared as one joined string rather than as a set, because order is
        // part of the contract: the spec asks for deterministic ordering for
        // prompt-cache hit rates, and a set comparison would pass a proxy that
        // shuffled the list.
        //
        // The seven authored tools come first; upstream's follow, and it is the
        // WHOLE exposable surface — 69 rather than the default 24 — because the
        // run's own child is started with every capability upstream declares.
        // The spec forbids the tool set varying per connection, so one static
        // list is the only shape available and it has to be everything.
        //
        // ⚠️ Corrected 2026-08-20 (previously "the UNION surface — 59 rather
        // than the default 24 — every capability any MODE can have"). Session
        // modes were deleted and `network`, `pdf` and `testing` were granted for
        // the first time, so the list is ten longer.
        //
        // ⚠️ Minus every tool this build withholds. Corrected 2026-08-18
        // (previously the whole union, and `Names.Count + 59`): `browser_annotate`
        // is filtered out of `tools/list` because it blocks with no self-timeout.
        //
        // ⚠️ Corrected 2026-08-26 (previously "see
        // SessionToolPolicy.IsWithheldFromTheSurface"): the withholding is a
        // `deny` row in tool-verdicts.json now, and the predicate reads the
        // shipped file. The expected list is still computed from the committed
        // snapshot rather than typed, and the filter is still applied through the
        // product's own predicate over the product's own file -- so the day the
        // decision is reversed, in the file, this test follows it.
        var expectedUpstream = UpstreamSurface.For(BrowserConfiguration.GrantedCapabilities)
            .Where(tool => !RepositoryVerdicts.Committed.IsWithheldFromTheSurface(tool))
            .ToList();

        await Assert.That(string.Join(", ", run.ToolNames))
            .IsEqualTo(string.Join(", ", [.. SessionToolSurface.Names, .. expectedUpstream]));

        // Stated as a number as well, because 68 of 69 is what DECISIONS records
        // and a list comparison that both sides got wrong the same way would not
        // say so. *(Corrected 2026-08-20, previously 58 of 59.)*
        await Assert.That(run.ToolNames.Count).IsEqualTo(SessionToolSurface.Names.Count + 68);

        // ⚠️ And the withheld tool is absent from the REAL binary's real answer,
        // named individually. The list comparison above would also catch it, but
        // only as one differing string among 74: this is the assertion that says
        // what happened, and it is the off-the-wire half of the decision.
        await Assert.That(run.ToolNames).DoesNotContain(RepositoryVerdicts.TheOneDenial.Name);

        // ⚠️ And the ten that arrived on 2026-08-20 are in the REAL binary's
        // real answer, named individually for the same reason. This is the
        // off-the-wire half of the grant: `ModelSurfaceTests` asserts it in
        // process, and a rewrite that dropped them between there and the pipe
        // would pass that and fail this.
        foreach (var granted in SessionToolSurface.NewlyGrantedTools)
        {
            await Assert.That(run.ToolNames).Contains(granted);
        }

        // Not vacuous — the child really does have it, so the absence above is
        // BrowserAI's filter rather than an upstream that never shipped it.
        await Assert.That(UpstreamSurface.For(BrowserConfiguration.GrantedCapabilities))
            .Contains(RepositoryVerdicts.TheOneDenial.Name);

        // And every one of them gains BrowserAI's `session` parameter, asserted
        // against the REAL child's list rather than against the snapshot the
        // build regenerates from it: routing is the one thing this proxy cannot
        // get wrong, and a tool upstream added that slipped through the rewrite
        // would be answerable by the run's own child.
        //
        // Corrected 2026-08-18 (previously this asserted every name carried a
        // row in `SessionToolPolicy.Classification`, deny-by-default). That
        // table was part of the (tool, mode) permission matrix, which was never
        // a boundary against the caller — who chooses the session directory and
        // reads the profile inside it as the same user — and change detection
        // for a tool upstream adds now lives in the golden `tools-list.json`
        // snapshot, which diffs each tool's inputSchema as well as its name.
        var unrouted = run.ToolList
            .Where(tool => !SessionToolSurface.IsAuthored((string)tool!["name"]!))
            .Where(tool => tool!["inputSchema"]?["properties"]?[SessionToolSurface.SessionParameter] is null)
            .Select(tool => (string)tool!["name"]!)
            .ToList();

        await Assert.That(string.Join(", ", unrouted)).IsEmpty();
    }

    /// <summary>
    /// Every upstream tool gains BrowserAI's two injected parameters, in order,
    /// and loses none of its own.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Two since 2026-08-20 (previously <c>session</c> alone, and the test
    /// was named <c>EveryUpstreamToolGainsTheSessionParameterAndNoneLosesItsOwn</c>).</b>
    /// <c>why</c> rides the same path, and the ORDER is asserted rather than
    /// mere presence: both are appended, upstream's own properties keep their
    /// positions, and <c>session</c> comes before <c>why</c> — a rewrite that
    /// reordered would cost a prompt-cache miss per call with nothing failing.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryUpstreamToolGainsBothInjectedParametersAndNoneLosesItsOwn()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();
        var offenders = new List<string>();

        foreach (var tool in run.ToolList)
        {
            var name = (string)tool!["name"]!;

            if (SessionToolSurface.Names.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            var schema = tool["inputSchema"]!.AsObject();
            var properties = schema["properties"]?.AsObject();
            var required = schema["required"]?.AsArray().Select(entry => (string)entry!).ToList() ?? [];

            foreach (var injected in new[] { SessionToolSurface.SessionParameter, SessionToolSurface.WhyParameter })
            {
                if (properties?[injected] is null)
                {
                    offenders.Add($"{name}: no {injected} property");
                }

                if (!required.Contains(injected, StringComparer.Ordinal))
                {
                    offenders.Add($"{name}: {injected} is not required");
                }
            }

            // Appended, never inserted: upstream's own properties keep their
            // order, and the two injected ones are last in that order. A rewrite
            // that reordered would cost a prompt-cache miss per call with
            // nothing failing.
            if (properties is { Count: > 2 })
            {
                var tail = properties.TakeLast(2).Select(property => property.Key).ToList();

                if (string.Join(",", tail) != $"{SessionToolSurface.SessionParameter},{SessionToolSurface.WhyParameter}")
                {
                    offenders.Add($"{name}: the last two properties are {string.Join(",", tail)} rather than session,why");
                }
            }
        }

        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty();
    }

    [Test]
    public async Task NavigatingToADataUrlReturnsANonErrorResultThatNamesThePage()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        // A JSON-RPC error would mean the call never reached the child.
        await Assert.That(run.NavigateEnvelope.ContainsKey("error")).IsFalse();

        // isError is the other half, and it is the one that looks like success:
        // a tool failure travels as a perfectly valid result.
        await Assert.That((bool?)run.NavigateEnvelope["result"]!["isError"] is true).IsFalse();

        // The proof that a page was really loaded rather than that a result
        // shaped like one came back.
        await Assert.That(run.NavigateText).Contains("Page URL: data:text/html");
    }

    /// <summary>
    /// A screenshot comes back as a legible file <b>and</b> as the image
    /// upstream would have sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The defect this closed was ours, and 2026-08-26 removed its cause
    /// rather than its symptom.</b> Upstream's handler ends
    /// <c>await response.addFileResult(resolvedFile, data); if (!params.filename)
    /// await response.registerImageResult(data, fileType);</c> — the only
    /// <c>registerImageResult</c> call site in the resolved bundle. BrowserAI
    /// used to rewrite <c>arguments["filename"]</c> on every screenshot, to give
    /// the file a name a human could read a month later, so <b>the guard was
    /// never true and no screenshot ever came back inline</b>; the repair was to
    /// append the block ourselves. Nothing rewrites the argument now, so the
    /// guard is true again on its own and there is no restoration left in the
    /// product — <b>the block in the answer is upstream's own</b>.
    /// </para>
    /// <para>
    /// <b>Against the real child, off the wire, because a double proves the
    /// wrong thing here.</b> The claim is that the bytes in the answer are the
    /// bytes upstream produced — a fake child writes whatever a test told it to,
    /// so it can only show that the plumbing moves bytes, not that these are the
    /// right ones. This drives the published NativeAOT binary, a real Chromium
    /// and a real <c>@playwright/mcp</c>, and compares the block against the file
    /// on disk.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AScreenshotComesBackInlineAsWellAsAsAFileWithALegibleName()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();
        var content = run.ScreenshotEnvelope["result"]?["content"]?.AsArray() ?? [];

        await Assert.That(run.ScreenshotEnvelope.ContainsKey("error")).IsFalse();
        await Assert.That((bool?)run.ScreenshotEnvelope["result"]!["isError"] is true).IsFalse();

        // The file half. ⚠️ *Corrected 2026-08-26 (previously "a name derived
        // from the page rather than a timestamp, in the folder its generator
        // prefix names", asserting the path contained `output\page\`).* There
        // is no generator folder and no derived name: upstream chooses the name,
        // upstream writes the file, and it lands at the output ROOT because that
        // is upstream's own working directory. What survives from the old claim
        // is the half that was ever a claim about the product — the path in the
        // answer is the path the file is at.
        await Assert.That(run.ScreenshotFile).EndsWith(".png");
        await Assert.That(Path.GetDirectoryName(run.ScreenshotFile))
            .IsEqualTo(Path.Combine(run.SessionDirectory, SessionLayout.OutputFolderName));
        await Assert.That(run.ScreenshotBytes.Length).IsGreaterThan(0);

        // ⚠️ It is a PNG, checked at the file's own magic number rather than at
        // its extension: the whole point is that these bytes are an image a
        // client can render, and an extension is a claim about that rather than
        // evidence of it.
        await Assert.That(run.ScreenshotBytes.Take(8))
            .IsEquivalentTo(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

        // ⚠️ THE INLINE HALF IS UPSTREAM'S OWN IMAGE SINCE 2026-08-26, AND IT IS
        // NOT THE FILE'S BYTES. *Corrected that day (previously "exactly one
        // image block, and its bytes are the file's bytes — not merely the same
        // length", asserting `inline.SequenceEqual(run.ScreenshotBytes)`).* That
        // was true while BrowserAI produced the block itself, by reading the
        // file back off disk, because it had taken upstream's own block away by
        // always supplying a `filename`. It supplies none now, so the block is
        // the one upstream sends — and upstream puts its bytes through
        // `scaleImageToFitMessage` first, which shrinks anything over 1,568 px
        // on a side and re-encodes.
        //
        // **Measured here rather than assumed, because the divergence is the
        // finding.** The two are different images and the difference is not the
        // direction anybody guesses: at the 1920x1080 default the file is the
        // full capture and the inline block is scaled DOWN in pixels while being
        // several times LARGER in bytes — 9,379 on disk against 379,731 inline
        // on 2026-08-26, because a re-encode is not Chromium's own encoder. So
        // what is asserted is the property that survives: the block is a PNG,
        // and it is within upstream's stated ceiling on both sides while the
        // file is not.
        var images = content.Where(block => (string?)block?["type"] is "image").ToList();

        await Assert.That(images.Count).IsEqualTo(1);
        await Assert.That((string?)images[0]!["mimeType"]).IsEqualTo("image/png");

        var inline = Convert.FromBase64String((string)images[0]!["data"]!);

        await Assert.That(inline.Take(8))
            .IsEquivalentTo(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

        var (inlineWidth, inlineHeight) = PngSize(inline);
        var (fileWidth, fileHeight) = PngSize(run.ScreenshotBytes);

        // The file is the viewport, unscaled: that is what a caller reading the
        // path gets, and it is the number the instructions' cost sentence is
        // about.
        await Assert.That(fileWidth).IsEqualTo(BrowserConfiguration.DefaultViewport.Width);
        await Assert.That(fileHeight).IsEqualTo(BrowserConfiguration.DefaultViewport.Height);

        // The block is upstream's, inside upstream's own ceiling on both sides.
        // ⚠️ 1,568 is UPSTREAM's constant and not one invented here — it is the
        // bound `scaleImageToFitMessage` applies, and a change to it shows up as
        // this assertion rather than as an image nobody compared.
        await Assert.That(Math.Max(inlineWidth, inlineHeight)).IsLessThanOrEqualTo(1568);

        // Not vacuous: it really is a scaled version of the same capture rather
        // than a placeholder — same aspect ratio to within a rounded pixel, and
        // genuinely smaller than the file it stands for.
        await Assert.That(inlineWidth).IsLessThan(fileWidth);
        await Assert.That(Math.Abs(((double)inlineWidth / inlineHeight) - ((double)fileWidth / fileHeight)))
            .IsLessThan(0.01);

        // ⚠️ AND NOTHING OF OURS IS IN THE ANSWER. *Corrected 2026-08-26
        // (previously "the note that names the file is still there, after the
        // child's own text and before the image", asserting two text blocks and
        // the absolute path in the last of them).* There is no note. What names
        // the file is upstream's own `- [Screenshot of viewport](./page-….png)`,
        // relative to its working directory, which is what `ScreenshotFile` was
        // read from — so this asserts the file name and NOT the absolute path,
        // because an absolute path here would mean somebody had started
        // rewriting answers again.
        var texts = content.Where(block => (string?)block?["type"] is "text").ToList();

        var answerText = string.Join("\n", texts.Select(block => (string?)block!["text"]));

        await Assert.That(texts.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(answerText).Contains(Path.GetFileName(run.ScreenshotFile));
        await Assert.That(answerText).DoesNotContain(run.SessionDirectory);

        // The cost, reported rather than asserted. An inline image is the one
        // thing in an answer that costs the caller tokens and appears in no
        // file, so the number belongs somewhere a reader can find it; a
        // threshold on it would be a policy upstream does not have.
        Report(run, (string)images[0]!["data"]!);
    }

    /// <summary>Prints what the inline image costs, on a passing run.</summary>
    /// <remarks>
    /// A separate non-async method for the reason
    /// <c>ModelSurfaceTests.Report</c> is one: <c>TextWriter.WriteLine</c> is a
    /// synchronous call and CA1849 refuses it inside an <c>async</c> method,
    /// which is right — and awaiting a diagnostic write in the middle of an
    /// assertion sequence is the wrong fix.
    /// </remarks>
    /// <summary>A PNG's pixel dimensions, read out of its own IHDR chunk.</summary>
    /// <remarks>
    /// <b>Eight bytes at a fixed offset, and no image library.</b> The IHDR is
    /// the first chunk of every PNG by the format's own rule, so width and
    /// height are big-endian integers at offsets 16 and 20. Decoding the image
    /// to ask its size would mean a second image pipeline in the suite, which is
    /// the thing the scope boundary uses as its own example.
    /// </remarks>
    /// <param name="png">The whole file.</param>
    /// <returns>Width and height in pixels.</returns>
    private static (int Width, int Height) PngSize(byte[] png) =>
        (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
         System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));

    private static void Report(SliceRun run, string base64) =>
        TestContext.Current?.OutputWriter.WriteLine(
            $"inline screenshot: {run.ScreenshotBytes.Length} bytes on disk, {base64.Length} base64 characters, "
            + $"{PngDimensions(run.ScreenshotBytes)} pixels, file {run.ScreenshotFile}");

    /// <summary>Width × height, out of a PNG's <c>IHDR</c>.</summary>
    /// <remarks>
    /// Thirteen bytes of header rather than an image library: the suite has no
    /// decoder and does not need one, and the dimensions are reported rather
    /// than asserted.
    /// </remarks>
    private static string PngDimensions(byte[] png) =>
        png.Length < 24
            ? "unknown"
            : $"{System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4))}x{System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4))}";

    [Test]
    public async Task KillingThePublishedBinaryLeavesNoNodeAndNoBrowser()
    {
        SuiteEnvironment.RequirePublishedSlice();

        var run = await SliceRun.SharedAsync();

        // A tree that never came up would satisfy "no survivors" vacuously, so
        // the shape of what was contained is asserted first: the binary, its
        // node child, and a real browser with children of its own.
        await Assert.That(run.Processes.Any(process =>
            process.ImagePath?.EndsWith(@"payload\node\node.exe", StringComparison.OrdinalIgnoreCase) is true)).IsTrue();

        await Assert.That(run.ChromiumProcesses(BrowserAiPaths.BrowsersDirectory).Count).IsGreaterThanOrEqualTo(3);

        // The contract. BrowserAI was terminated from outside and ran no code
        // afterwards; the only thing that can have cleaned this up is the kernel
        // closing its last job handle.
        var survivors = string.Join(
            ", ",
            run.Survivors.Select(process => $"{process.ProcessId} {process.ImagePath ?? "<unknown>"}"));

        await Assert.That(survivors).IsEmpty();
    }

}
