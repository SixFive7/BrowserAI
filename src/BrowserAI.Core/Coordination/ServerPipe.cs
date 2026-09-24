// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Coordination;

/// <summary>What a server does with each request its pipe receives.</summary>
/// <remarks>
/// <b>Two members, one per verb, and one place a verb becomes an answer.</b> A
/// server that one day has to decline a verb for a reason of its own -- an update
/// being applied, say -- answers from the member that verb already reaches, with
/// <see cref="ServerPipeProtocol.Refused"/>, and nothing else changes shape.
/// </remarks>
internal interface IServerPipeResponder
{
    /// <summary>The answer to <c>describe</c>.</summary>
    /// <returns>The reply.</returns>
    ServerPipeReply Describe();

    /// <summary>The answer to <c>stop</c>, and the stop itself as what happens after it.</summary>
    /// <returns>The reply.</returns>
    ServerPipeReply Stop();
}

/// <summary>What one pipe answers, verb by verb.</summary>
/// <remarks>
/// <b>Two pipes share one serving loop since 2026-09-25</b>: each server's, which
/// answers <c>describe</c> and <c>stop</c>, and the coordinator's, which answers
/// <c>show</c> and <c>recheck</c> (Q284 a). The loop, the framing, the
/// security descriptor and the one-instance rule are the same for both, so the
/// only thing a pipe supplies is this table.
/// </remarks>
internal interface IPipeAnswers
{
    /// <summary>The verbs this pipe answers, in the order a refusal names them.</summary>
    IReadOnlyList<string> Verbs { get; }

    /// <summary>The answer to one request.</summary>
    /// <param name="verb">The request line, without its newline.</param>
    /// <param name="clientProcessId">
    /// The pid of the process on the other end, as <c>GetNamedPipeClientProcessId</c>
    /// reports it, or <see langword="null"/> when Windows would not say.
    /// </param>
    /// <returns>The reply, or <see langword="null"/> for a verb this pipe does not know.</returns>
    ServerPipeReply? Answer(string verb, int? clientProcessId);
}

/// <summary>
/// One server's named pipe: one instance, one thread, one request per
/// connection, and a length-prefixed answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The thread owns the pipe handle, and closes it itself on the way
/// out.</b> The handle is synchronous, and closing a synchronous handle
/// while another thread is blocked in a call on it can wait on that very call --
/// so nothing but the serving thread ever touches it. <see cref="Dispose"/> asks
/// the thread to stop and connects to the pipe once to wake a listener that is
/// waiting for a client.
/// </para>
/// <para>
/// <b>An answer is read before the connection is dropped.</b> Disconnecting a
/// pipe discards whatever the client has not read yet, so after writing, the
/// thread reads until the client closes its end. A client that never closes
/// holds the thread; BrowserAI's own client closes as soon as it has the answer
/// or its bound runs out.
/// </para>
/// <para>
/// <b>A pipe that cannot be created is not a server that cannot start.</b> The
/// caller logs it and serves stdio anyway; what is lost is the coordinator's
/// view of this one process, which the census still counts.
/// </para>
/// </remarks>
internal sealed class ServerPipe : IDisposable
{
    private readonly IPipeAnswers _answers;
    private readonly ILogger _logger;
    private readonly Thread _thread;

    private SafeFileHandle? _pipe;
    private int _stopping;

    private ServerPipe(string name, SafeFileHandle pipe, IPipeAnswers answers, ILogger logger)
    {
        Name = name;
        _pipe = pipe;
        _answers = answers;
        _logger = logger;

        _thread = new Thread(Serve)
        {
            IsBackground = true,
            Name = "BrowserAI server pipe",
        };
    }

    /// <summary>The pipe's full name.</summary>
    public string Name { get; }

    /// <summary>Creates the pipe named after a live marker and starts serving it.</summary>
    /// <param name="markerPath">This process's own live marker.</param>
    /// <param name="responder">What each request is answered with.</param>
    /// <param name="logger">Where the pipe reports.</param>
    /// <returns>The serving pipe. Dispose it to stop serving.</returns>
    /// <exception cref="IOException">
    /// The pipe was not created. <see cref="Exception.HResult"/> is Windows'
    /// own answer: <c>0x800700E7</c> when an instance of the name already exists.
    /// </exception>
    public static ServerPipe Open(string markerPath, IServerPipeResponder responder, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentNullException.ThrowIfNull(logger);

        return OpenNamed(ServerPipeProtocol.NameFor(markerPath), responder, logger);
    }

    /// <summary>Creates a pipe under an exact name and starts serving it.</summary>
    /// <remarks>
    /// <b>For the suite's arms about the name itself</b> -- a second instance, a
    /// name somebody else created first -- which have to choose it. The product
    /// always goes through <see cref="Open"/>, so its name is always its marker's.
    /// </remarks>
    /// <param name="name">The full pipe name.</param>
    /// <param name="responder">What each request is answered with.</param>
    /// <param name="logger">Where the pipe reports.</param>
    /// <returns>The serving pipe.</returns>
    /// <exception cref="IOException">The pipe was not created.</exception>
    public static ServerPipe OpenNamed(string name, IServerPipeResponder responder, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentNullException.ThrowIfNull(logger);

        return OpenNamed(name, new ServerAnswers(responder, name, logger), logger);
    }

    /// <summary>Creates a pipe under an exact name and serves it with a table of answers.</summary>
    /// <remarks>
    /// <b>What the coordinator's pipe opens through</b>, and what the overload
    /// taking a server's responder is one case of. The creation is
    /// <c>NamedPipes.CreateServer</c> for every pipe, so the coordinator's gets the
    /// first-instance flag, the refusal of remote clients and the current user's
    /// DACL with nothing to remember.
    /// </remarks>
    /// <param name="name">The full pipe name.</param>
    /// <param name="answers">What each request is answered with.</param>
    /// <param name="logger">Where the pipe reports.</param>
    /// <returns>The serving pipe.</returns>
    /// <exception cref="IOException">
    /// The pipe was not created. <see cref="Exception.HResult"/> is Windows' own
    /// answer: <c>0x800700E7</c> when an instance of the name already exists, and
    /// <c>0x80070005</c> when somebody else created the name first.
    /// </exception>
    public static ServerPipe OpenNamed(string name, IPipeAnswers answers, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(logger);

        var handle = NamedPipes.CreateServer(name);

        try
        {
            var pipe = new ServerPipe(name, handle, answers, logger);

            var verbs = string.Join(" and ", answers.Verbs);

            pipe._thread.Start();
            ServerPipeLog.Listening(logger, name, verbs);

            return pipe;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Stops serving.</summary>
    /// <remarks>
    /// It does not wait for the thread. A thread blocked on a client that has
    /// not closed its end finishes with that client and then sees the request
    /// to stop; the process going away takes it regardless, because it is a
    /// background thread.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) is not 0)
        {
            return;
        }

        // Wake a listener that is waiting for a client: connect once and let
        // go. A pipe that is busy with somebody else answers this with
        // ERROR_PIPE_BUSY, and the thread then sees the request to stop as soon
        // as that client is done.
        using var wake = NamedPipes.OpenClient(Name, out _);
    }

    private void Serve()
    {
        var pipe = _pipe!;

        try
        {
            using var stream = new FileStream(pipe, FileAccess.ReadWrite, bufferSize: 0, isAsync: false);

            // The stream owns the handle from here and closes it on the way out.
            _pipe = null;

            while (Volatile.Read(ref _stopping) is 0)
            {
                if (!NamedPipes.WaitForClient(pipe))
                {
                    NamedPipes.Disconnect(pipe);
                    continue;
                }

                if (Volatile.Read(ref _stopping) is not 0)
                {
                    NamedPipes.Disconnect(pipe);
                    break;
                }

                var after = AnswerOne(stream, NamedPipes.ClientProcessIdOf(pipe));

                NamedPipes.Disconnect(pipe);

                // ⚠️ AFTER the disconnect, so the client has its whole answer
                // before anything this reply asked for begins. For stop, this is
                // the stop.
                after?.Invoke();
            }
        }
#pragma warning disable CA1031 // The thread boundary: a pipe that fails is a log record and a coordinator that cannot see this one process, never a crash of the server serving a client.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            try
            {
                ServerPipeLog.Failed(_logger, Name, failure);
            }
#pragma warning disable CA1031 // A logger that throws must not defeat the catch-all that was reporting through it.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
        finally
        {
            _pipe?.Dispose();
        }
    }

    /// <summary>Reads one request, writes its answer and waits for the client to finish reading it.</summary>
    /// <param name="stream">The connected pipe.</param>
    /// <param name="clientProcessId">The pid on the other end, when Windows says.</param>
    /// <returns>What to do once the connection has been dropped, if anything.</returns>
    private Action? AnswerOne(FileStream stream, int? clientProcessId)
    {
        try
        {
            var line = ReadRequest(stream);

            if (line is null)
            {
                // Connected and closed without asking anything: a wake-up from
                // Dispose, or a reader of the pipe's security. Nothing to answer.
                return null;
            }

            var reply = Dispatch(line, clientProcessId);

            stream.Write(ServerPipeProtocol.Frame(reply.Body));

            // Until the client closes its end: a read returns 0 once it has.
            Span<byte> drain = stackalloc byte[64];

            while (stream.Read(drain) > 0)
            {
            }

            return reply.AfterDelivery;
        }
        catch (IOException failure)
        {
            // The client went before the answer was written or read. It asked
            // and left; there is nobody to tell, and nothing this server holds
            // is affected.
            ServerPipeLog.ClientLeft(_logger, Name, failure.Message);
            return null;
        }
    }

    /// <summary>
    /// The one place a request becomes an answer, and the one place a server
    /// failing to answer becomes a refusal a client can read.
    /// </summary>
    /// <param name="line">The verb as it arrived.</param>
    /// <param name="clientProcessId">The pid on the other end, when Windows says.</param>
    /// <returns>The reply.</returns>
    private ServerPipeReply Dispatch(string line, int? clientProcessId)
    {
        try
        {
            return _answers.Answer(line, clientProcessId)
                ?? new ServerPipeReply(ServerPipeProtocol.Refused(
                    line,
                    $"This BrowserAI does not know the request '{line}'. It answers {string.Join(" and ", _answers.Verbs.Select(verb => $"'{verb}'"))}."));
        }
#pragma warning disable CA1031 // A responder that throws must cost one answer and not the pipe: the client is told why, and the next request is served.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            ServerPipeLog.AnswerFailed(_logger, Name, line, failure);
            return new ServerPipeReply(ServerPipeProtocol.Refused(line, $"This BrowserAI could not answer '{line}': {failure.Message}"));
        }
    }

    /// <summary>Reads one request line, or <see langword="null"/> when the client closed first.</summary>
    /// <param name="stream">The connected pipe.</param>
    /// <returns>The verb without its newline.</returns>
    private static string? ReadRequest(FileStream stream)
    {
        Span<byte> buffer = stackalloc byte[ServerPipeProtocol.MaximumRequestBytes];
        var read = 0;

        while (read < buffer.Length)
        {
            var got = stream.Read(buffer[read..]);

            if (got is 0)
            {
                break;
            }

            read += got;

            if (buffer[..read].Contains((byte)'\n'))
            {
                break;
            }
        }

        if (read is 0)
        {
            return null;
        }

        var text = Encoding.ASCII.GetString(buffer[..read]);
        var end = text.IndexOf('\n', StringComparison.Ordinal);

        return (end >= 0 ? text[..end] : text).TrimEnd('\r');
    }

    /// <summary>A server's two verbs, answered by its responder.</summary>
    /// <param name="responder">What answers them.</param>
    /// <param name="name">The pipe's full name, for the stop's record.</param>
    /// <param name="logger">Where the stop is recorded.</param>
    private sealed class ServerAnswers(IServerPipeResponder responder, string name, ILogger logger) : IPipeAnswers
    {
        public IReadOnlyList<string> Verbs { get; } = [ServerPipeProtocol.DescribeVerb, ServerPipeProtocol.StopVerb];

        public ServerPipeReply? Answer(string verb, int? clientProcessId)
        {
            switch (ServerPipeProtocol.Parse(verb))
            {
                case ServerPipeRequest.Describe:
                    return responder.Describe();

                case ServerPipeRequest.Stop:
                    ServerPipeLog.StopRequested(logger, name);
                    return responder.Stop();

                default:
                    return null;
            }
        }
    }
}

/// <summary>Source-generated log messages for a server's pipe.</summary>
internal static partial class ServerPipeLog
{
    /// <summary>The pipe exists and its thread is waiting for a client.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe's full name.</param>
    /// <param name="verbs">What it answers.</param>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Listening on {Name} for {Verbs}.")]
    public static partial void Listening(ILogger logger, string name, string verbs);

    /// <summary>The pipe could not be created, and the server serves stdio without it.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe's full name.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The pipe {Name} could not be created, so no coordinator can describe or stop this server. It serves its client regardless, and the census still counts it.")]
    public static partial void NotCreated(ILogger logger, string name, Exception failure);

    /// <summary>The serving thread stopped on a failure.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe's full name.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "The pipe {Name} stopped serving after a failure. This server goes on serving its client; a coordinator now meets no answer from it.")]
    public static partial void Failed(ILogger logger, string name, Exception failure);

    /// <summary>A client connected and went before its answer was delivered.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe's full name.</param>
    /// <param name="why">What the pipe said.</param>
    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "A client of {Name} left before its answer was delivered: {Why}")]
    public static partial void ClientLeft(ILogger logger, string name, string why);

    /// <summary>A responder threw, and the client was told so.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe's full name.</param>
    /// <param name="request">What was asked.</param>
    /// <param name="failure">What was thrown.</param>
    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "The pipe {Name} could not answer '{Request}', and answered with a refusal saying so. It goes on serving.")]
    public static partial void AnswerFailed(ILogger logger, string name, string request, Exception failure);

    /// <summary>A stop arrived through the pipe and was acknowledged.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe's full name.</param>
    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "A stop arrived on {Name} and was acknowledged; this server now ends its conversation the way a client leaving ends it.")]
    public static partial void StopRequested(ILogger logger, string name);
}
