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
        // The six authored tools come first; upstream's follow, and it is the
        // UNION surface — 59 rather than the default 24 — because the run's own
        // child is started with every capability any mode can have. The spec
        // forbids the tool set varying per connection, so one static list is the
        // only shape available and it has to be the superset.
        //
        // ⚠️ Minus the one tool this build withholds. Corrected 2026-08-18
        // (previously the whole union, and `Names.Count + 59`): `browser_annotate`
        // is filtered out of `tools/list` because it blocks with no self-timeout
        // — see SessionToolPolicy.IsWithheldFromTheSurface. The expected list is
        // still computed from the committed snapshot rather than typed, and the
        // filter is applied through the product's own predicate, so the day the
        // decision is reversed this test follows it.
        var expectedUpstream = UpstreamSurface.For(BrowserConfiguration.UnionCapabilities)
            .Where(tool => !SessionToolPolicy.IsWithheldFromTheSurface(tool))
            .ToList();

        await Assert.That(string.Join(", ", run.ToolNames))
            .IsEqualTo(string.Join(", ", [.. SessionToolSurface.Names, .. expectedUpstream]));

        // Stated as a number as well, because 42/59 is what DECISIONS records and
        // a list comparison that both sides got wrong the same way would not say
        // so.
        await Assert.That(run.ToolNames.Count).IsEqualTo(SessionToolSurface.Names.Count + 58);

        // ⚠️ And the withheld tool is absent from the REAL binary's real answer,
        // named individually. The list comparison above would also catch it, but
        // only as one differing string among 64: this is the assertion that says
        // what happened, and it is the off-the-wire half of the decision.
        await Assert.That(run.ToolNames).DoesNotContain(SessionToolPolicy.AnnotateTool);

        // Not vacuous — the child really does have it, so the absence above is
        // BrowserAI's filter rather than an upstream that never shipped it.
        await Assert.That(UpstreamSurface.For(BrowserConfiguration.UnionCapabilities))
            .Contains(SessionToolPolicy.AnnotateTool);

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

    [Test]
    public async Task EveryUpstreamToolGainsTheSessionParameterAndNoneLosesItsOwn()
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

            if (properties?["session"] is null)
            {
                offenders.Add($"{name}: no session property");
            }

            if (!required.Contains("session", StringComparer.Ordinal))
            {
                offenders.Add($"{name}: session is not required");
            }

            // Appended, never inserted: upstream's own properties keep their
            // order, and `session` is last. A rewrite that reordered would cost
            // a prompt-cache miss per call with nothing failing.
            if (properties is { Count: > 1 } && properties.Last().Key is not "session")
            {
                offenders.Add($"{name}: session is not the last property");
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
    /// ⚠️ <b>The defect this closes was ours, and it was silent for the life of
    /// artifact routing.</b> Upstream's handler ends
    /// <c>await response.addFileResult(resolvedFile, data); if (!params.filename)
    /// await response.registerImageResult(data, fileType);</c> — the only
    /// <c>registerImageResult</c> call site in the resolved bundle. BrowserAI
    /// always rewrote <c>arguments["filename"]</c>, to give the file a name a
    /// human can read a month later, so <b>the guard was never true and no
    /// screenshot ever came back inline, in any mode</b>, where bare
    /// <c>@playwright/mcp</c> returns one. The model paid an extra file read on
    /// the most-used artifact tool and nothing anywhere said so.
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

        // The file half: a name derived from the page rather than a timestamp,
        // in the folder its generator prefix names, and actually on disk.
        await Assert.That(run.ScreenshotFile).EndsWith(".png");
        await Assert.That(run.ScreenshotFile).Contains($@"{SessionLayout.OutputFolderName}\page\");
        await Assert.That(run.ScreenshotFile).StartsWith(run.SessionDirectory);
        await Assert.That(run.ScreenshotBytes.Length).IsGreaterThan(0);

        // ⚠️ It is a PNG, checked at the file's own magic number rather than at
        // its extension: the whole point is that these bytes are an image a
        // client can render, and an extension is a claim about that rather than
        // evidence of it.
        await Assert.That(run.ScreenshotBytes.Take(8))
            .IsEquivalentTo(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

        // The inline half. Exactly one image block, and its bytes are the file's
        // bytes -- not merely the same length.
        var images = content.Where(block => (string?)block?["type"] is "image").ToList();

        await Assert.That(images.Count).IsEqualTo(1);
        await Assert.That((string?)images[0]!["mimeType"]).IsEqualTo("image/png");

        var inline = Convert.FromBase64String((string)images[0]!["data"]!);

        await Assert.That(inline.Length).IsEqualTo(run.ScreenshotBytes.Length);
        await Assert.That(inline.SequenceEqual(run.ScreenshotBytes)).IsTrue();

        // And the note that names the file is still there, after the child's own
        // text and before the image: the file path is what makes the artifact
        // findable later, and the image is what saves the read now. Neither
        // replaces the other.
        var texts = content.Where(block => (string?)block?["type"] is "text").ToList();

        await Assert.That(texts.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That((string?)texts[^1]!["text"]).Contains(run.ScreenshotFile);
        await Assert.That(content.IndexOf(images[0])).IsEqualTo(content.Count - 1);

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
