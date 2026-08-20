// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// The stderr classifier, in both directions, against real children.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both directions, because each is one half of the defect being replaced.</b>
/// The launcher this product supersedes warned on <i>any</i> stderr while
/// <c>@playwright/mcp</c> prints a benign <c>Session: &lt;path&gt;</c> line on
/// every healthy start — so the loud channel was noise and a reader learned to
/// ignore it. A benign line that warns and an error-shaped line that is silent
/// are therefore separate assertions, and a build that satisfies only one of them
/// has reintroduced half of a fixed bug.
/// </para>
/// <para>
/// <b>The benign line comes from a real launch, never from a fixture.</b> A
/// fixture asserts that our own idea of what upstream prints classifies the way
/// we expect, which is a tautology; the point of
/// <a href="../../kb/re-verification.md">row 33</a> is that upstream
/// still prints it. That test spends a browser to say so, which is why it is the
/// only one here that does.
/// </para>
/// </remarks>
internal sealed class StandardErrorClassifierTests
{
    /// <summary>
    /// The event ids of the two halves of the verdict, so the assertions read the
    /// level rather than the text.
    /// </summary>
    private const int BenignEventId = 11;

    private const int DiagnosticEventId = 14;

    /// <summary>
    /// The dead flag from row 1 of the charter's opening table, which upstream
    /// still rejects with a diagnostic on stderr and a non-zero exit.
    /// </summary>
    /// <remarks>
    /// <b>The most faithful possible error arm.</b> This is the exact failure that
    /// killed all four servers for five days, and the exact line the reference
    /// implementation's first regex was written against — so the test is a
    /// reproduction rather than an invention.
    /// </remarks>
    private static readonly string[] DeadFlag = ["--output-mode", "tokens"];

    [Test]
    public async Task TheTwoRegexesAreByteIdenticalToTheReferenceImplementations()
    {
        // Read and re-parsed here rather than shared with the product, so this
        // compares two independently obtained strings. The extraction is
        // deliberately dumb -- every line of the excerpt carrying `-match`, the
        // text between its first and last quote -- because a clever parser could
        // agree with a clever bug.
        var reference = new FileInfo(Path.Combine(
            RepositoryLayout.Root.FullName, "src", "BrowserAI", "Protocol", "StandardErrorClassifier.reference.ps1"));

        await Assert.That(reference.Exists).IsTrue();

        var lines = await File.ReadAllLinesAsync(reference.FullName);
        var excerpt = lines.SkipWhile(line => !line.Contains("begin verbatim excerpt", StringComparison.Ordinal)).Skip(1);

        var patterns = excerpt
            .Where(line => line.Contains("-match", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf('\'', StringComparison.Ordinal) + 1)..line.LastIndexOf('\'')])
            .ToList();

        // Exactly two, in the reference's own order. A third would mean the
        // excerpt grew and nobody adjudicated it; one would mean it shrank.
        await Assert.That(patterns.Count).IsEqualTo(2);
        await Assert.That(patterns[0]).IsEqualTo(StandardErrorClassifier.ErrorPrefixPattern);
        await Assert.That(patterns[1]).IsEqualTo(StandardErrorClassifier.ErrorPhrasePattern);

        // And the copy really is a copy: the provenance a future reader needs in
        // order to re-establish it is in the file, not only in a commit message.
        var provenance = string.Join('\n', lines);
        await Assert.That(provenance).Contains("Workspace657");
        await Assert.That(provenance).Contains("a9ac74738fe63ca8aee588489313b77574e2e504");
    }

    [Test]
    [Arguments("error: unknown option '--output-mode'")]
    [Arguments("Error: something went wrong")]
    [Arguments("  fatal: could not read the config")]
    [Arguments("unknown option --nope")]
    [Arguments("Browser \"chromium\" is not installed; expected executable at C:\\x\\chrome.exe")]
    [Arguments("Error: ENOENT: no such file or directory, open 'C:\\x'")]
    [Arguments("EACCES: permission denied")]
    [Arguments("Cannot find module 'playwright-core'")]
    public async Task AnErrorShapedLineIsClassifiedAsAnError(string line) =>
        await Assert.That(StandardErrorClassifier.LooksLikeError(line)).IsTrue();

    [Test]
    [Arguments("Session: C:\\sessions\\alpha\\output\\session-1786895796938")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("Listening on stdio")]
    // The prose case the line anchor exists for: an error word that is not a
    // diagnostic. Without `^` the reference's first group would fire on this.
    [Arguments("no errors were found")]
    [Arguments("the run completed with 0 errors")]
    public async Task BenignOutputIsNotClassifiedAsAnError(string line) =>
        await Assert.That(StandardErrorClassifier.LooksLikeError(line)).IsFalse();

    [Test]
    public async Task ATextClassifiesTheSameWholeAsItDoesLineByLine()
    {
        // The reference matches against the WHOLE captured stderr file; this
        // product classifies each line as the pump delivers it. That is a port of
        // the regexes into a different call shape, so the equivalence is asserted
        // rather than argued: the first pattern is multiline-anchored and the
        // second is unanchored, which is why it holds.
        string[] buffers =
        [
            "Session: C:\\x\nSession: C:\\y",
            "Session: C:\\x\nerror: unknown option '--output-mode'",
            "no errors\nnothing to report",
            "  \n\nerror: late diagnostic\n",
            "Browser \"chromium\" is not installed; expected executable at C:\\x\nSession: C:\\y",
        ];

        foreach (var buffer in buffers)
        {
            var whole = StandardErrorClassifier.LooksLikeError(buffer);
            var anyLine = buffer.Split('\n').Any(StandardErrorClassifier.LooksLikeError);

            await Assert.That(whole).IsEqualTo(anyLine);
        }
    }

    [Test]
    public async Task AStartupDiagnosticFromARealChildReachesTheLogAtAWarning()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var scratch = ScratchDirectory.Create("stderr-diagnostic");
        using var capture = new CapturingLoggerProvider();
        using var factory = Factory(capture);

        // The product's own launch options, with one flag added: the one upstream
        // deleted in 0.0.79. No browser is involved -- the child rejects the
        // command line and dies -- so this costs a second and runs every build.
        var options = WithArguments(LaunchOptions(scratch.Path, saveSession: false), DeadFlag);
        var transport = new DirectStdioClientTransport(options, factory);
        var session = (ChildProcessSession)await transport.ConnectAsync();

        try
        {
            await WaitForAsync(capture, DiagnosticEventId);
        }
        finally
        {
            await session.DisposeAsync();
        }

        var diagnostics = Records(capture, DiagnosticEventId);

        // The line itself, at a level a reader would notice. Both halves matter:
        // the level is what the classifier decides, and the text is what proves
        // it decided about the right line.
        await Assert.That(diagnostics.Count).IsGreaterThan(0);
        await Assert.That(diagnostics.All(record => record.Level >= LogLevel.Warning)).IsTrue();
        await Assert.That(diagnostics.Any(record => record.Message.Contains("unknown option", StringComparison.Ordinal))).IsTrue();

        // And it did not ALSO go quietly. A classifier that logged both ways
        // would satisfy every assertion above while leaving the Debug channel
        // carrying the thing the Warning exists to surface.
        await Assert.That(Records(capture, BenignEventId)
            .Any(record => record.Message.Contains("unknown option", StringComparison.Ordinal))).IsFalse();

        // The child died of it, which is what makes this a startup failure rather
        // than chatter -- and the exit code is readable after disposal, which is
        // row 2 of the same table still holding.
        await Assert.That(session.ExitCode).IsEqualTo(1);
    }

    [Test]
    public async Task ARealHealthyStartPrintsTheBenignSessionLineAndIsNotWarnedAbout()
    {
        SuiteEnvironment.RequireRepositoryPayload();

        using var scratch = ScratchDirectory.Create("stderr-benign");
        using var capture = new CapturingLoggerProvider();
        using var factory = Factory(capture);

        // saveSession is what makes upstream write the line at all, and it is the
        // product's own `tracing` modifier rather than a test-only key. A real
        // browser is launched, because the line is written when the backend
        // initialises a context and not before.
        var options = LaunchOptions(scratch.Path, saveSession: true);
        var transport = new DirectStdioClientTransport(options, factory);

        await using (var child = await ChildConnection.ConnectAsync(
            transport,
            factory,
            "stderr-benign/",
            (_, _) => ValueTask.CompletedTask))
        {
            var answer = await child.AskAsync(
                "tools/call",
                new JsonObject
                {
                    ["name"] = "browser_navigate",
                    ["arguments"] = new JsonObject { ["url"] = SliceRun.TargetUrl },
                },
                CancellationToken.None);

            // A navigation that failed would still be a start, but not a HEALTHY
            // one, and the whole claim is about what a healthy start prints.
            await Assert.That(answer.TransportFailure?.ToString()).IsNull();
            await Assert.That(answer.ProtocolFailure?.Message).IsNull();
            await Assert.That((bool?)answer.Response?.Result?["isError"]).IsNotEqualTo(true);

            await WaitForAsync(capture, BenignEventId, record => record.Message.Contains("Session: ", StringComparison.Ordinal));
        }

        var session = Records(capture, BenignEventId)
            .FirstOrDefault(record => record.Message.Contains("Session: ", StringComparison.Ordinal));

        // Row 33: upstream still prints it, and this is the observation rather
        // than a memory of one.
        await Assert.That(session).IsNotNull();
        await Assert.That(session!.Level).IsEqualTo(LogLevel.Debug);

        // The line as the child wrote it, classified directly. Taken off the
        // captured record rather than retyped, so this cannot pass against a
        // string that no child ever produced.
        var line = session.Message[session.Message.IndexOf("Session: ", StringComparison.Ordinal)..];

        await Assert.That(Path.IsPathFullyQualified(line["Session: ".Length..])).IsTrue();
        await Assert.That(StandardErrorClassifier.LooksLikeError(line)).IsFalse();

        // And nothing about this start was warned about. This is the assertion the
        // replaced launcher would have failed on every single healthy run.
        var warned = Records(capture, DiagnosticEventId);

        await Assert.That(string.Join(" | ", warned.Select(record => record.Message))).IsEmpty();
    }

    private static ILoggerFactory Factory(CapturingLoggerProvider capture) =>
        LoggerFactory.Create(builder =>
        {
            // Trace, because the benign half is logged at Debug and a default
            // factory filters it out -- which would make "no warning" pass
            // against a classifier that logged nothing at all.
            _ = builder.SetMinimumLevel(LogLevel.Trace);
            _ = builder.AddProvider(capture);
        });

    private static ChildProcessOptions LaunchOptions(string root, bool saveSession)
    {
        var work = Path.Combine(root, "work");
        _ = Directory.CreateDirectory(work);

        return ChildLaunch.Create(
            RepositoryPayload.Layout,
            BrowserAiPaths.BrowsersDirectory,
            work,
            Path.Combine(work, "playwright-mcp.config.json"),
            BrowserConfiguration.Generate(new BrowserConfigurationRequest
            {
                Browser = ProvisionedBrowsers.Chromium,
                Headless = true,
                UserDataDirectory = Path.Combine(work, SessionLayout.ProfileFolderName),
                OutputDirectory = Path.Combine(work, SessionLayout.OutputFolderName),
                DownloadsDirectory = Path.Combine(work, SessionLayout.DownloadsFolderName),
                Capabilities = BrowserConfiguration.GrantedCapabilities,
                SaveSession = saveSession,
            }),
            name: "playwright-mcp[stderr]");
    }

    private static ChildProcessOptions WithArguments(ChildProcessOptions options, IEnumerable<string> extra) =>
        new()
        {
            Command = options.Command,
            WorkingDirectory = options.WorkingDirectory,
            Environment = options.Environment,
            Arguments = [.. options.Arguments, .. extra],
            ShutdownTimeout = options.ShutdownTimeout,
            StandardErrorLines = options.StandardErrorLines,
            Name = options.Name,
        };

    private static List<LogRecord> Records(CapturingLoggerProvider capture, int eventId) =>
        [.. capture.Records.Where(record => record.EventId.Id == eventId)];

    private static async Task WaitForAsync(
        CapturingLoggerProvider capture,
        int eventId,
        Func<LogRecord, bool>? predicate = null)
    {
        // Polled rather than signalled: stderr arrives on its own reader thread,
        // so the frame that proves the work happened can land before the line
        // that describes it. Bounded, and its expiry is not itself a failure --
        // the assertions that follow report what was and was not seen.
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < TestDefaults.ProcessHang)
        {
            if (Records(capture, eventId).Any(predicate ?? (_ => true)))
            {
                return;
            }

            await Task.Delay(25);
        }
    }
}
