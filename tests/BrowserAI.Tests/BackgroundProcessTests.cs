// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json.Nodes;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Relay;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The background and the relays that reach it, as published binaries against a real
/// Chromium: a session outlives the relay that opened it, whichever way the relay
/// ends; the background's own death takes every child and browser with it; and every
/// relay then answers with the crash at once, starting nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>S a and R, the maintainer's words of 2026-10-08, verbatim: <i>"s a"</i> and
/// <i>"R I like option 1 and the call response"</i>.</b> One resident background holds
/// every session; a relay per client passes the client's calls to it; a session whose
/// client goes is kept while its browser is up; and a background that dies without a
/// clean end is a crash every relay names, which only the person's start clears.
/// </para>
/// <para>
/// <b>The background runs in a job of the arm's own</b>, the way the Task Scheduler's
/// shared job holds it in production, so ending a relay's job can never reach it.
/// <i>Added 2026-10-08 in place of <c>SessionHostProcessTests</c>, whose session host
/// and front the background and the relay replaced; its two arms are this file's
/// first and third, carried over with the roles renamed.</i>
/// </para>
/// </remarks>
[NotInParallel(nameof(BackgroundProcessTests))]
internal sealed class BackgroundProcessTests
{
    /// <summary>
    /// A session outlives a relay that is killed the way Codex ends a server, and the
    /// next relay finds its page exactly as it was left: no restore, no new browser.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionOutlivesARelayThatIsKilledAndTheNextRelayFindsItsPageAsItWasLeft()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("background-relay-killed");
        await using var background = BackgroundRun.Start(scratch.Path);

        var marker = $"KEPT-{Guid.NewGuid():N}";
        var session = Path.Combine(scratch.Path, "kept-across-a-relay");

        // ---- The first relay: open the session and leave a page in it.
        var first = background.StartRelay(scratch.Path);

        try
        {
            _ = await first.InitializeAsync(SliceRun.OfferedProtocolVersion);

            await CallOkAsync(first, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "the background arm's session, kept across a relay's death",
            });

            await CallOkAsync(first, "browser_navigate", new JsonObject
            {
                ["url"] = "data:text/html," + Uri.EscapeDataString($"<title>{marker}</title><h1>{marker}</h1><input id=typed value=left-as-it-was>"),
                ["session"] = session,
                ["why"] = "the suite leaving a page in a session whose relay it is about to kill",
            });
        }
        finally
        {
            // ⚠️ THE RELAY DIES THE WAY CODEX ENDS A SERVER: its job is closed and it
            // is terminated, with nothing sent and nothing closed first.
            await first.DisposeAsync();
        }

        // ---- The next relay: the page is still there.
        await using var second = background.StartRelay(scratch.Path);

        _ = await second.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var snapshot = await CallOkAsync(second, "browser_snapshot", new JsonObject
        {
            ["session"] = session,
            ["why"] = "the suite's next relay reading the page the dead relay left",
        });

        await Assert.That(TextOf(snapshot)).Contains(marker)
            .Because("the browser the first relay drove is the one the second relay reads, page and all");
        await Assert.That(TextOf(snapshot)).Contains("left-as-it-was");

        await DestroyAsync(second, session);
    }

    /// <summary>
    /// A session outlives a relay whose client closed its standard input, the way a
    /// client ends a server it is done with, and the next relay takes it over.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ASessionOutlivesARelayWhoseClientClosedItsInputAndTheNextRelayTakesItOver()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("background-relay-eof");
        await using var background = BackgroundRun.Start(scratch.Path);

        var marker = $"KEPT-{Guid.NewGuid():N}";
        var session = Path.Combine(scratch.Path, "kept-across-an-eof");

        await using (var first = background.StartRelay(scratch.Path))
        {
            _ = await first.InitializeAsync(SliceRun.OfferedProtocolVersion);

            await CallOkAsync(first, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "the background arm's session, kept across a relay whose input ended",
            });

            await CallOkAsync(first, "browser_navigate", new JsonObject
            {
                ["url"] = "data:text/html," + Uri.EscapeDataString($"<title>{marker}</title><h1>{marker}</h1>"),
                ["session"] = session,
                ["why"] = "the suite leaving a page in a session whose relay's input it is about to close",
            });

            // The relay ends on its own once its input has ended.
            await Assert.That(await first.CloseAndWaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        }

        await using var second = background.StartRelay(scratch.Path);

        _ = await second.InitializeAsync(SliceRun.OfferedProtocolVersion);

        var snapshot = await CallOkAsync(second, "browser_snapshot", new JsonObject
        {
            ["session"] = session,
            ["why"] = "the suite's next relay reading the page a closed relay left",
        });

        await Assert.That(TextOf(snapshot)).Contains(marker);

        await DestroyAsync(second, session);
    }

    /// <summary>
    /// Killing the background takes every child and browser it started with it, and
    /// the relay still connected then answers its next call with the crash at once,
    /// naming the exit code it read, and starts nothing.
    /// </summary>
    /// <remarks>
    /// <b>The crash text is R's</b>, the maintainer's words of 2026-10-08 accepting it:
    /// <i>"r ok"</i>. It is answered at once and never after the 150 s hold, because
    /// waiting cannot change a recorded crash.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task KillingTheBackgroundTakesEveryChildWithItAndEveryRelayAnswersWithTheCrashAtOnce()
    {
        SuiteEnvironment.RequirePublishedSlice();
        SuiteEnvironment.RequireProvisionedChromium();
        PublishedSlice.EnsureFresh();

        using var scratch = ScratchDirectory.Create("background-death");
        await using var background = BackgroundRun.Start(scratch.Path);

        var session = Path.Combine(scratch.Path, "dies-with-its-background");

        await using var relay = background.StartRelay(scratch.Path);

        _ = await relay.InitializeAsync(SliceRun.OfferedProtocolVersion);

        await CallOkAsync(relay, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = session,
            ["purpose"] = "the background arm's session, which dies with its background",
        });

        await CallOkAsync(relay, "browser_navigate", new JsonObject
        {
            ["url"] = SliceRun.TargetUrl,
            ["session"] = session,
            ["why"] = "the suite bringing a browser up for the background to take down with it",
        });

        // Everything the background started is in the arm's job, nested there through
        // its own jobs. More than the background and its console host means a node
        // child and a browser are up.
        await Assert.That(background.JobProcessIds().Count).IsGreaterThan(2).Because("a session's node child and its browser should be up");

        background.Kill();

        var waited = Stopwatch.StartNew();

        while (background.JobProcessIds().Count is not 0)
        {
            if (waited.Elapsed > TestDefaults.ProcessHang)
            {
                throw new TimeoutException(
                    $"The background was killed and these processes of its tree were still alive {waited.Elapsed.TotalSeconds:F1} s later: "
                    + string.Join(", ", background.JobProcessIds()));
            }

            await Task.Delay(100);
        }

        // The relay is still there, and answers the next call with the crash at once.
        var answered = Stopwatch.StartNew();
        var crash = await relay.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_snapshot",
            ["arguments"] = new JsonObject
            {
                ["session"] = session,
                ["why"] = "the suite calling after the background died",
            },
        });

        await Assert.That((bool?)crash["isError"]).IsTrue();
        await Assert.That(TextOf(crash)).Contains("crashed")
            .Because("a background that ended with no clean end recorded is a crash every relay names (R)");
        await Assert.That(TextOf(crash)).Contains("Only that person can restart it");
        await Assert.That(answered.Elapsed).IsLessThan(RelayConstants.HoldBound)
            .Because("a recorded crash is answered at once; holding cannot change it");

        // Nothing the relay did started anything: the arm's job is still empty.
        await Assert.That(background.JobProcessIds().Count).IsEqualTo(0);
    }

    private static async Task DestroyAsync(RawStdioClient client, string session) =>
        _ = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = SessionToolSurface.Destroy,
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["why"] = "the suite cleaning up its session",
            },
        });

    /// <summary>Calls a tool and requires an answer that is not an error.</summary>
    /// <param name="client">The relay.</param>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The result.</returns>
    private static async Task<JsonObject> CallOkAsync(RawStdioClient client, string tool, JsonObject arguments)
    {
        var result = await client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

        if ((bool?)result["isError"] is true)
        {
            throw new InvalidOperationException($"'{tool}' was refused: {TextOf(result)}{Environment.NewLine}{client.StandardErrorSoFar()}");
        }

        return result;
    }

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));

    /// <summary>A published background in a job of the arm's own, and the relays that reach it.</summary>
    private sealed class BackgroundRun : IAsyncDisposable
    {
        private readonly JobObject _job;
        private readonly LaunchedProcess _process;
        private readonly long _created;
        private readonly string _record;

        private BackgroundRun(JobObject job, LaunchedProcess process, string pipe, string record)
        {
            _job = job;
            _process = process;
            _created = ProcessIdentity.CreationTimeOf(process.Id);
            _record = record;
            Pipe = pipe;
        }

        /// <summary>The pipe it serves.</summary>
        public string Pipe { get; }

        /// <summary>Starts one and waits for its pipe.</summary>
        /// <param name="workingDirectory">Its working directory.</param>
        /// <returns>The run.</returns>
        public static BackgroundRun Start(string workingDirectory)
        {
            var job = JobObject.CreateKillOnClose();

            try
            {
                var environment = PublishedSlice.InheritedEnvironment();
                var pipe = PublishedBackground.NewPipeName();
                var process = PublishedBackground.Start(job, workingDirectory, environment, pipe, []);

                return new BackgroundRun(job, process, pipe, PublishedBackground.RecordFor(environment, [], pipe));
            }
            catch
            {
                job.Dispose();
                throw;
            }
        }

        /// <summary>Starts a relay to this background, in a job of its own.</summary>
        /// <param name="workingDirectory">Its working directory.</param>
        /// <returns>The relay.</returns>
        public RawStdioClient StartRelay(string workingDirectory) =>
            RawStdioClient.Start(
                PublishedSlice.Executable,
                [Program.McpArgument, BackgroundPipe.PipeArgument, Pipe],
                workingDirectory,
                PublishedSlice.InheritedEnvironment());

        /// <summary>Every process in the background's job, its own included.</summary>
        /// <returns>The pids.</returns>
        public IReadOnlyList<int> JobProcessIds() => _job.ProcessIds();

        /// <summary>Terminates the background by the identity recorded at its start.</summary>
        public void Kill() => ProcessIdentity.Terminate(_process.Id, _created);

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _job.Dispose();
            _process.Dispose();

            try
            {
                File.Delete(_record);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Left for the next run: a stale record names a pid that is gone.
            }

            return ValueTask.CompletedTask;
        }
    }
}
