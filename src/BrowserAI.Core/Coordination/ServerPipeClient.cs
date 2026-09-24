// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BrowserAI.Interop;
using BrowserAI.Updates;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Coordination;

/// <summary>How one call to a server's pipe ended.</summary>
internal enum ServerPipeOutcome
{
    /// <summary>The server answered, in full.</summary>
    Answered,

    /// <summary>The server answered, and its answer was a refusal.</summary>
    Refused,

    /// <summary>The census says nothing is running there: the marker is not held.</summary>
    NotRunning,

    /// <summary>
    /// The marker is held and no pipe of its name exists: a server from before
    /// the pipe, the configuration app, or one whose pipe could not be created.
    /// </summary>
    NoPipe,

    /// <summary>
    /// The pipe did not produce a whole answer inside the bound: a hung
    /// listener, or a server that died part-way through its answer.
    /// </summary>
    NoAnswer,

    /// <summary>
    /// The process serving the pipe is not the process the marker names, so
    /// nothing was asked of it.
    /// </summary>
    NotItsServer,
}

/// <summary>What one call to a server's pipe came back with.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Why">One sentence saying why, for every outcome.</param>
/// <param name="Description">The description, for an answered <c>describe</c>.</param>
/// <param name="Elapsed">How long the call took, connect included.</param>
internal sealed record ServerPipeAnswer(
    ServerPipeOutcome Outcome,
    string Why,
    ServerDescription? Description,
    TimeSpan Elapsed);

/// <summary>
/// Asks one server, through its pipe, to describe itself or to stop -- after the
/// census has said it is there, and never for longer than the bound.
/// </summary>
/// <remarks>
/// <para>
/// <b>The census first, always, and that ordering is the rule this type
/// exists to keep.</b> Asking whether a marker is held answered a server that
/// had gone in 85 µs, measured 2026-09-24, while the framework's pipe client
/// asked the same question waited out its whole timeout: 493.7 ms against a
/// 500 ms connect. This client does not wait on a name nobody serves -- it says
/// so at once -- but the census is still asked first, because it is the only
/// signal that tells a process that has gone from one that is alive and not
/// answering. So a caller hands this the marker, not the pipe name, and the
/// marker decides whether the pipe is asked at all.
/// </para>
/// <para>
/// <b>Then the pipe's owner, before a byte is sent.</b>
/// <c>GetNamedPipeServerProcessId</c> has to name the pid the marker carries. A
/// different process serving that name is a pipe this caller did not mean to
/// reach, and it is told nothing.
/// </para>
/// <para>
/// <b>Every call is bounded by <see cref="ServerPipeProtocol.CallBound"/>,
/// connect to last byte</b>, and the bound is a deadline read off a
/// <see cref="Stopwatch"/>: a wait that wakes early waits out the remainder, so a
/// hung server costs the bound and not a little less.
/// </para>
/// </remarks>
internal static class ServerPipeClient
{
    /// <summary>Asks a server to describe itself.</summary>
    /// <param name="markerPath">The server's live marker.</param>
    /// <param name="bound">
    /// The whole call's bound. <see cref="ServerPipeProtocol.CallBound"/> in the
    /// product; the suite's happy-path arms pass their own hang detector, since
    /// their host is under a load no coordinator ever is.
    /// </param>
    /// <param name="cancellationToken">Ends the call early.</param>
    /// <returns>What came back.</returns>
    public static Task<ServerPipeAnswer> DescribeAsync(string markerPath, TimeSpan? bound = null, CancellationToken cancellationToken = default) =>
        CallAsync(markerPath, ServerPipeRequest.Describe, bound ?? ServerPipeProtocol.CallBound, cancellationToken);

    /// <summary>Asks a server to stop, the way a client leaving stops it.</summary>
    /// <param name="markerPath">The server's live marker.</param>
    /// <param name="bound">The whole call's bound; see <see cref="DescribeAsync"/>.</param>
    /// <param name="cancellationToken">Ends the call early.</param>
    /// <returns>What came back: <see cref="ServerPipeOutcome.Answered"/> means acknowledged.</returns>
    public static Task<ServerPipeAnswer> StopAsync(string markerPath, TimeSpan? bound = null, CancellationToken cancellationToken = default) =>
        CallAsync(markerPath, ServerPipeRequest.Stop, bound ?? ServerPipeProtocol.CallBound, cancellationToken);

    private static async Task<ServerPipeAnswer> CallAsync(
        string markerPath,
        ServerPipeRequest request,
        TimeSpan bound,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bound, TimeSpan.Zero);

        var clock = Stopwatch.StartNew();

        ServerPipeAnswer answer(ServerPipeOutcome outcome, string why, ServerDescription? description = null) =>
            new(outcome, why, description, clock.Elapsed);

        if (!ServerPipeProtocol.TryProcessIdOf(markerPath, out var expected))
        {
            return answer(ServerPipeOutcome.NotRunning, $"'{markerPath}' is not a live marker: its name carries no pid.");
        }

        if (LiveInstances.IsMarkerHeld(markerPath) is false)
        {
            return answer(ServerPipeOutcome.NotRunning, $"Nothing holds '{markerPath}', so the process that wrote it has gone and its pipe with it.");
        }

        var name = ServerPipeProtocol.NameFor(markerPath);

        var (pipe, refusal) = Connect(name, clock, bound);

        if (pipe is null)
        {
            return answer(refusal!.Value.Outcome, refusal.Value.Why);
        }

        // The stream owns the handle from here, on every path out.
        var stream = new FileStream(pipe, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
        await using var streamScope = stream.ConfigureAwait(false);

        var serving = NamedPipes.ServerProcessIdOf(pipe);

        if (serving != expected)
        {
            var actual = serving?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

            return answer(
                ServerPipeOutcome.NotItsServer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{name}' is served by pid {actual}, not by pid {expected} whose marker names it, so nothing was asked."));
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        deadline.CancelAfter(Remaining(clock, bound));

        byte[] body;

        try
        {
            await stream.WriteAsync(ServerPipeProtocol.Request(request), deadline.Token).ConfigureAwait(false);

            var prefix = new byte[ServerPipeProtocol.LengthPrefixBytes];

            var headerRead = await ReadExactlyAsync(stream, prefix, deadline.Token).ConfigureAwait(false);

            if (headerRead < prefix.Length)
            {
                return answer(
                    ServerPipeOutcome.NoAnswer,
                    string.Create(CultureInfo.InvariantCulture, $"'{name}' closed after {headerRead} of the {prefix.Length} bytes of its length prefix."));
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);

            if (length is < 0 or > ServerPipeProtocol.MaximumReplyBytes)
            {
                return answer(
                    ServerPipeOutcome.NoAnswer,
                    string.Create(CultureInfo.InvariantCulture, $"'{name}' announced an answer of {length} bytes, which is no answer this protocol produces."));
            }

            body = new byte[length];

            var bodyRead = await ReadExactlyAsync(stream, body, deadline.Token).ConfigureAwait(false);

            if (bodyRead < length)
            {
                return answer(
                    ServerPipeOutcome.NoAnswer,
                    string.Create(CultureInfo.InvariantCulture, $"'{name}' closed after {bodyRead} of the {length} bytes its length prefix announced, so the answer is not whole and none of it was read as one."));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The deadline, and only the deadline. A wake that came early -- a
            // timer tick rounded down -- waits out what is left, so the cost of a
            // hung server is the bound and not a little less.
            await WaitOutAsync(clock, bound, cancellationToken).ConfigureAwait(false);

            return answer(
                ServerPipeOutcome.NoAnswer,
                string.Create(CultureInfo.InvariantCulture, $"'{name}' did not answer '{request}' inside {bound.TotalMilliseconds:F0} ms."));
        }
        catch (IOException failure)
        {
            return answer(ServerPipeOutcome.NoAnswer, $"'{name}' failed part-way through the call: {failure.Message}");
        }

        return Read(name, request, body, answer);
    }

    /// <summary>Opens the pipe, waiting for a busy instance inside the bound.</summary>
    /// <param name="name">The full pipe name.</param>
    /// <param name="clock">The call's clock.</param>
    /// <param name="bound">The call's bound.</param>
    /// <returns>The client end, or why there is none.</returns>
    private static (SafeFileHandle? Pipe, (ServerPipeOutcome Outcome, string Why)? Refusal) Connect(
        string name,
        Stopwatch clock,
        TimeSpan bound)
    {
        while (true)
        {
            var pipe = NamedPipes.OpenClient(name, out var error);

            if (!pipe.IsInvalid)
            {
                return (pipe, null);
            }

            pipe.Dispose();

            if (error is NamedPipes.ErrorFileNotFound)
            {
                // No instance of the name exists at all. Waiting would not make
                // one: the marker is held, so this is a server with no pipe --
                // one from before the pipe, the configuration app, or one whose
                // pipe could not be created -- and it costs nothing to say so.
                return (null, (ServerPipeOutcome.NoPipe, $"The marker is held and no pipe named '{name}' exists."));
            }

            if (error is not NamedPipes.ErrorPipeBusy)
            {
                return (null, (ServerPipeOutcome.NoAnswer, $"'{name}' could not be opened: {new System.ComponentModel.Win32Exception(error).Message}"));
            }

            var left = Remaining(clock, bound);

            if (left <= TimeSpan.Zero)
            {
                return (null, (ServerPipeOutcome.NoAnswer, string.Create(CultureInfo.InvariantCulture, $"'{name}' stayed busy for the whole {bound.TotalMilliseconds:F0} ms.")));
            }

            // Busy: another client has the one instance. Wait for it inside what
            // is left of the bound; a wait that ends early loops and asks again.
            _ = NamedPipes.WaitForFreeInstance(name, (uint)Math.Max(1, Math.Ceiling(left.TotalMilliseconds)));
        }
    }

    /// <summary>Reads until a buffer is full or the pipe ends.</summary>
    /// <param name="stream">The pipe.</param>
    /// <param name="into">The buffer.</param>
    /// <param name="cancellationToken">The deadline.</param>
    /// <returns>How many bytes arrived; fewer than the buffer means the server closed.</returns>
    private static async Task<int> ReadExactlyAsync(Stream stream, Memory<byte> into, CancellationToken cancellationToken)
    {
        var read = 0;

        while (read < into.Length)
        {
            var got = await stream.ReadAsync(into[read..], cancellationToken).ConfigureAwait(false);

            if (got is 0)
            {
                break;
            }

            read += got;
        }

        return read;
    }

    /// <summary>Reads a whole answer into an outcome.</summary>
    private static ServerPipeAnswer Read(
        string name,
        ServerPipeRequest request,
        byte[] body,
        Func<ServerPipeOutcome, string, ServerDescription?, ServerPipeAnswer> answer)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var kind = root.TryGetProperty(ServerPipeProtocol.Fields.Answer, out var member) ? member.GetString() : null;

            if (kind is ServerPipeProtocol.Answers.Refused)
            {
                var why = root.TryGetProperty(ServerPipeProtocol.Fields.Why, out var reason) ? reason.GetString() : null;

                return answer(ServerPipeOutcome.Refused, why ?? $"'{name}' refused '{request}' and gave no reason.", null);
            }

            return request switch
            {
                ServerPipeRequest.Describe => answer(ServerPipeOutcome.Answered, $"'{name}' described itself.", ServerDescription.Parse(root)),
                ServerPipeRequest.Stop when kind is ServerPipeProtocol.Answers.Stop => answer(ServerPipeOutcome.Answered, $"'{name}' acknowledged the stop.", null),
                _ => answer(ServerPipeOutcome.NoAnswer, $"'{name}' answered '{request}' with '{kind ?? "nothing it named"}'.", null),
            };
        }
        catch (Exception failure) when (failure is JsonException or FormatException)
        {
            return answer(ServerPipeOutcome.NoAnswer, $"'{name}' answered with something that is not a {request} answer: {failure.Message}", null);
        }
    }

    private static TimeSpan Remaining(Stopwatch clock, TimeSpan bound)
    {
        var left = bound - clock.Elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private static async Task WaitOutAsync(Stopwatch clock, TimeSpan bound, CancellationToken cancellationToken)
    {
        while (Remaining(clock, bound) is var left && left > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(left, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
