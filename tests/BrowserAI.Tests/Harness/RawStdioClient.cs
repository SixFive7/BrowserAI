// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Interop;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A hand-written JSON-RPC client over raw stdio: newline-delimited frames onto
/// a child's stdin, correlation by <c>id</c>, and no protocol type from the
/// product or the SDK anywhere between an assertion and the bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is mandatory rather than a nicety, and the reason is structural.</b>
/// BrowserAI replaces <i>both</i> of the SDK's stdio transports. With both ends
/// replaced, a test that drives BrowserAI through an <c>McpClient</c> is testing
/// the code under test using the code under test: a symmetric bug — the same
/// escaping assumption made on the way out and on the way in, the same framing
/// mistake made twice — passes green, and every layer above it inherits the
/// blind spot. The oracle has to share no code with the product.
/// </para>
/// <para>
/// <b>What "shares no code" means here, precisely.</b> No <c>ModelContextProtocol</c>
/// type and no <c>BrowserAI.Protocol</c> type is on the path: the frames are
/// built with <see cref="JsonNode"/> and read with <see cref="JsonDocument"/>,
/// which is the framework. <see cref="JobLauncher"/> <i>is</i> product code and
/// is used deliberately, because it is process creation rather than protocol: a
/// test that leaks a browser is a defect, <c>Process.Start</c> cannot put a
/// child in a job at creation, and the alternative is a safety net with a
/// measured hole in it.
/// </para>
/// <para>
/// The five properties this exists to carry, each answering a failure that has
/// happened somewhere: newline-delimited frames written straight onto the
/// child's stdin; correlation by <c>id</c> with notifications skipped, because
/// the naive "the next line is the answer" version works locally and hangs under
/// load; stderr drained and attached to <b>every</b> failure message, because a
/// failure whose child stderr is missing has to be reproduced by hand before it
/// can be read; <c>UTF8Encoding(encoderShouldEmitUTF8Identifier: false)</c> on
/// all three streams, since a harness that emits a BOM fails the product for the
/// harness's defect; and an explicit working directory, since an unset one
/// passes <see langword="null"/> to <c>CreateProcess</c> and the child silently
/// inherits the test host's.
/// </para>
/// </remarks>
internal sealed class RawStdioClient : IAsyncDisposable
{
    /// <summary>
    /// UTF-8 with no byte-order mark, on all three streams. Declared once here
    /// rather than at each stream, so the three cannot drift apart.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly JobObject _job;
    private readonly LaunchedProcess _process;
    private readonly StreamWriter _toChild;
    private readonly StreamReader _fromChild;
    private readonly StringBuilder _standardError = new();
    private readonly Task _standardErrorPump;
    private readonly TimeSpan _perExchange;
    private readonly Stopwatch _sinceStart = Stopwatch.StartNew();

    private int _nextId;
    private int _disposed;

    private RawStdioClient(JobObject job, LaunchedProcess process, TimeSpan perExchange)
    {
        _job = job;
        _process = process;
        _perExchange = perExchange;

        _toChild = new StreamWriter(process.StandardInput, Utf8NoBom) { NewLine = "\n", AutoFlush = false };
        _fromChild = new StreamReader(process.StandardOutput, Utf8NoBom);

        // Started before anything is sent, so a child that writes a fatal line
        // and dies has still been heard.
        _standardErrorPump = Task.Run(PumpStandardErrorAsync, CancellationToken.None);
    }

    /// <summary>The pid of the process this client started.</summary>
    public int ProcessId => _process.Id;

    /// <summary>
    /// Every process the kernel currently reports in this client's job: the
    /// child, and everything it started, however deep.
    /// </summary>
    /// <remarks>
    /// The job is the suite's, created here, so this is also the containment net
    /// that makes a failed assertion unable to leave a browser running.
    /// </remarks>
    public IReadOnlyList<int> JobProcessIds() => _job.ProcessIds();

    /// <summary>Starts a process and prepares to speak JSON-RPC to it.</summary>
    /// <param name="command">The executable's absolute path.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="workingDirectory">Its working directory. Explicit, never inherited.</param>
    /// <param name="environment">Its complete environment block.</param>
    /// <param name="perExchange">
    /// The hang detector for <b>one</b> exchange. Defaults to
    /// <see cref="TestDefaults.BrowserHang"/>.
    /// </param>
    /// <returns>The client. Dispose it to close the job and stop everything in it.</returns>
    public static RawStdioClient Start(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan? perExchange = null)
    {
        var job = JobObject.CreateKillOnClose();

        try
        {
            var process = JobLauncher.Start(job, command, arguments, workingDirectory, environment);

            return new RawStdioClient(job, process, perExchange ?? TestDefaults.BrowserHang);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    /// <summary>Performs the <c>initialize</c> handshake and returns its result.</summary>
    /// <param name="protocolVersion">The revision to offer.</param>
    /// <returns>The <c>result</c> object of the <c>initialize</c> response.</returns>
    public async Task<JsonObject> InitializeAsync(string protocolVersion)
    {
        var response = await RoundTripAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "BrowserAI.RawStdioClient", ["version"] = "1" },
        }).ConfigureAwait(false);

        await NotifyAsync("notifications/initialized").ConfigureAwait(false);
        return response;
    }

    /// <summary>Sends a request and returns its <c>result</c>.</summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">Its parameters, or <see langword="null"/> for none.</param>
    /// <returns>The response's <c>result</c> object.</returns>
    /// <exception cref="InvalidOperationException">The peer answered with an error or closed its stdout.</exception>
    public async Task<JsonObject> RoundTripAsync(string method, JsonNode? parameters = null)
    {
        var envelope = await EnvelopeAsync(method, parameters).ConfigureAwait(false);

        if (envelope["error"] is { } error)
        {
            throw await FailureAsync($"'{method}' returned a JSON-RPC error: {error.ToJsonString()}").ConfigureAwait(false);
        }

        return envelope["result"]?.AsObject()
            ?? throw await FailureAsync($"'{method}' returned neither a result nor an error.").ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request and returns the whole response envelope, so a test can
    /// assert on a JSON-RPC <c>error</c> without it being thrown.
    /// </summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <param name="parameters">Its parameters, or <see langword="null"/> for none.</param>
    /// <returns>The response envelope.</returns>
    public async Task<JsonObject> EnvelopeAsync(string method, JsonNode? parameters = null)
    {
        var id = Interlocked.Increment(ref _nextId);

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };

        if (parameters is not null)
        {
            request["params"] = parameters;
        }

        // ⚠️ ONE DEADLINE PER EXCHANGE, armed here rather than in the
        // constructor.
        //
        // Corrected 2026-08-18 (previously a single CancellationTokenSource armed
        // in the constructor with a three-minute whole-conversation budget, and
        // documented as deliberate). It was the same defect RawPipeClient was
        // corrected for a day earlier: a budget covering everything since Start()
        // cannot distinguish "this peer has stopped answering" from "this
        // conversation has a lot of exchanges in it", and it fires on the latter
        // while naming the former. Worse, it was numerically equal to — and
        // therefore always won against — Playwright's own three-minute launch
        // timeout, so upstream's diagnosis was replaced by ours in exactly the
        // case upstream had something to say. Measured 2026-08-17: a Firefox
        // launch reported at 3m00s with the peer still running and its stderr
        // empty.
        //
        // Per exchange is the STRONGER hang detector, not the weaker one. A peer
        // that has genuinely stopped sends nothing, so the deadline still fires;
        // a peer that has DIED closes its stdout, and the read below returns null
        // immediately with the exit code and the stderr attached, which is the
        // event-driven half and needs no clock at all.
        using var deadline = new CancellationTokenSource(_perExchange);

        try
        {
            return await ExchangeAsync(method, request, id, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // ⚠️ Rethrown with the peer's stderr attached, because the raw
            // cancellation carries nothing at all -- no method, no elapsed time
            // and, worst, none of the child's stderr, which is the one place a
            // browser that failed to come up says so. Every other failure on this
            // path already went through FailureAsync; this one was the hole.
            throw new TimeoutException(
                await DiagnosticsAsync(
                    $"'{method}' (id {id.ToString(CultureInfo.InvariantCulture)}) went unanswered for {_perExchange.TotalMinutes:F0} minutes, {_sinceStart.Elapsed.TotalSeconds:F1} s after the peer was started. "
                    + "That is a hang detector with more than a hundred times the cost of a cold browser launch in it, so a busy machine cannot reach it: the peer is alive and has stopped answering.")
                    .ConfigureAwait(false));
        }
    }

    private async Task<JsonObject> ExchangeAsync(string method, JsonObject request, int id, CancellationToken cancellationToken)
    {
        await SendAsync(request, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var line = await _fromChild.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw await FailureAsync($"The peer closed its stdout before answering '{method}' (id {id.ToString(CultureInfo.InvariantCulture)}).").ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject envelope;

            try
            {
                envelope = JsonNode.Parse(line)?.AsObject()
                    ?? throw new JsonException("the frame parsed as JSON null");
            }
            catch (JsonException ex)
            {
                throw await FailureAsync($"The peer sent a frame that is not a JSON object: {ex.Message}. Frame: {Trim(line)}").ConfigureAwait(false);
            }

            // Correlation, not "the next line". A notification, a log message or
            // an answer to an earlier request all arrive on this stream, and a
            // client that assumes otherwise passes on a quiet machine.
            if (envelope["id"] is not { } received || (int?)received != id)
            {
                continue;
            }

            return envelope;
        }
    }

    /// <summary>Sends a notification, which has no reply.</summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <returns>A task that completes once the frame is on the wire.</returns>
    public async Task NotifyAsync(string method)
    {
        using var deadline = new CancellationTokenSource(_perExchange);

        await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }, deadline.Token).ConfigureAwait(false);
    }

    /// <summary>Everything the peer has written to stderr so far.</summary>
    /// <returns>The captured stderr.</returns>
    public string StandardErrorSoFar()
    {
        lock (_standardError)
        {
            return _standardError.ToString();
        }
    }

    /// <summary>
    /// Closes the peer's stdin and waits for it to exit on its own.
    /// </summary>
    /// <param name="timeout">How long to wait.</param>
    /// <returns><see langword="true"/> if it exited within the timeout.</returns>
    public async Task<bool> CloseAndWaitForExitAsync(TimeSpan timeout)
    {
        _toChild.Close();
        return await _process.WaitForExitAsync(timeout).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // The job first. Closing its last handle is what stops the peer and
        // everything it started, without a tree walk and without a name match,
        // and it happens even if an assertion threw halfway through a test.
        _job.Dispose();

        try
        {
            // The job closed above kills the peer, which closes the pipe, which
            // ends the pump -- so this normally returns at once. The bound is a
            // hang detector on a teardown path and nothing asserts on it.
            await _standardErrorPump.WaitAsync(TestDefaults.InProcessHang).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A stderr reader that will not finish must not turn a teardown into a hang.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        // After the pump, which reads through its own reader over the same
        // stream: disposing these first would close the pipe under it.
        _toChild.Dispose();
        _fromChild.Dispose();
        _process.Dispose();
    }

    private static string Trim(string line) => line.Length <= 400 ? line : line[..400] + "…";

    private async Task SendAsync(JsonNode message, CancellationToken cancellationToken)
    {
        await _toChild.WriteLineAsync(message.ToJsonString().AsMemory(), cancellationToken).ConfigureAwait(false);
        await _toChild.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the exception for any failure, with the peer's stderr attached.
    /// </summary>
    private async Task<InvalidOperationException> FailureAsync(string message) =>
        new(await DiagnosticsAsync(message).ConfigureAwait(false));

    /// <summary>
    /// A failure message with the peer's exit code and stderr attached.
    /// </summary>
    /// <remarks>
    /// <b>Separated from <see cref="FailureAsync"/> so a timeout can carry the
    /// same evidence under a different exception type.</b> A deadline expiring is
    /// a <see cref="TimeoutException"/> rather than an
    /// <see cref="InvalidOperationException"/>, and before this split it carried
    /// no evidence at all because it never reached this code.
    /// </remarks>
    /// <param name="message">What went wrong.</param>
    /// <returns>The message, with the peer's own account of itself.</returns>
    private async Task<string> DiagnosticsAsync(string message)
    {
        // ⚠️ The event, not a settle. The interesting stderr is written as the
        // peer dies, which is the same instant a read returns null -- so the
        // thing to wait for is "the pump has seen EOF", which is precisely "every
        // line the peer wrote has been captured".
        //
        // Corrected 2026-08-18 (previously `await Task.Delay(250)`). A quarter of
        // a second is a guess at how long a pipe takes to drain, and on a starved
        // machine it is the wrong guess in the direction that loses evidence.
        // Conditional on the peer having exited because a peer that is still
        // alive is holding stderr open, and there is then nothing to wait for.
        if (_process.HasExited)
        {
            try
            {
                await _standardErrorPump.WaitAsync(TestDefaults.InProcessHang).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Reported below as whatever was captured so far. A diagnostic
                // that cannot itself complete must not replace the failure.
            }
        }

        var exitCode = _process.HasExited
            ? _process.TryReadExitCode()?.ToString(CultureInfo.InvariantCulture) ?? "<unreadable>"
            : "<still running>";

        return $"{message}{Environment.NewLine}--- peer exit code: {exitCode} ---{Environment.NewLine}--- peer stderr ---{Environment.NewLine}{StandardErrorSoFar()}";
    }

    private async Task PumpStandardErrorAsync()
    {
        try
        {
            using var reader = new StreamReader(_process.StandardError, Utf8NoBom);

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                lock (_standardError)
                {
                    _ = _standardError.AppendLine(line);
                }
            }
        }
#pragma warning disable CA1031 // The pipe closing under a read in flight is how this loop normally ends.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
