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
/// <c>show</c> and <c>recheck</c> (Q284 a). The loop, the framing and the
/// security descriptor are the same for both, so the only thing a pipe supplies
/// is this table. <i>Corrected 2026-10-03 with Q368 a (previously "The loop, the
/// framing, the security descriptor and the one-instance rule are the same for
/// both").</i> There is no one-instance rule left: both pipes serve their
/// connections in parallel, and an implementation of this table is called from as
/// many threads at once as there are callers.
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
/// One named pipe: one request per connection and a length-prefixed answer,
/// served on a thread per connection.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <i>Corrected 2026-10-03 with Q368 a (previously "served on one thread for
/// the coordinator's and on a thread per connection for a server's").</i> Both
/// pipes serve in parallel now, so there is one shape and the one-thread loop is
/// gone.
/// </para>
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
/// or its bound runs out. <i>Since Q297 b it holds the thread serving its own
/// connection and nothing else, see below.</i>
/// </para>
/// <para>
/// <b>A pipe that cannot be created is not a server that cannot start.</b> The
/// caller logs it and serves stdio anyway; what is lost is the coordinator's
/// view of this one process, which the census still counts.
/// </para>
/// <para>
/// ⚠️ <b>A SERVER'S pipe serves its connections in parallel since 2026-10-03 --
/// Q297 b, the maintainer's words verbatim: <i>"Q297 b"</i></b> (previously
/// "one instance, one thread" for every pipe, and the paragraphs above describe
/// that shape, which the coordinator's pipe keeps). A caller that connected and
/// never finished used to hold the one thread, at the request read or at the
/// drain, and every other caller met a busy pipe until its own bound ran out. Now
/// the listening thread hands each connected instance to a thread of its own and
/// listens on a fresh one, so such a caller holds its own instance and nothing
/// else. Each connection's thread owns its handle and closes it itself, which keeps
/// the rule the first paragraph states. The listener makes the next instance
/// BEFORE it hands the connected one over, so the name never stands without an
/// instance of ours. The coordinator's pipe was not part of the decision and keeps
/// one instance and one thread.
/// </para>
/// <para>
/// ⚠️ <b>AND THE COORDINATOR'S PIPE SERVES IN PARALLEL TOO SINCE 2026-10-03 --
/// Q368 a, the maintainer's words verbatim: <i>"Q368 a"</i></b> (previously the
/// last sentence above, and the one-instance loop it named). A caller that
/// connected to the coordinator's pipe and never finished held its one thread, so
/// no other start could hand a verb over until it let go. Both pipes now open
/// through <see cref="NamedPipes.CreateParallelServer"/> and share this loop, and
/// <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> is what keeps a second coordinator off
/// the coordinator's name, refused with <c>0x80070005</c> as a second server is.
/// </para>
/// <para>
/// ⚠️ <b>The number of callers held at once has no ceiling of ours or Windows' --
/// corrected 2026-10-03</b> (previously a retry interval, "How long the listener
/// waits before asking again when every instance Windows allows is busy", and a
/// warning, event 7, "255 callers holding a server's pipe at once is not ordinary
/// traffic, and the next caller waits until one lets go"). Every instance is
/// created with <c>PIPE_UNLIMITED_INSTANCES</c>,
/// whose value is 255 and whose meaning is that the instances are limited only by
/// system resources, and so measured: 2,000 callers held connected at once on one
/// pipe created this way, where a cap of 254 refused the 255th instance with
/// <c>ERROR_PIPE_BUSY</c>
/// (<see href="../../../kb/windows/processes.md#a-pipe-created-with-pipe_unlimited_instances-holds-more-than-255-callers----measured-2026-10-03">kb</see>).
/// The wait for a free instance could never be reached and is gone with its
/// warning. What a caller costs is its connection's thread. An instance Windows
/// refuses to create for want of resources ends the listener through its
/// catch-all, recorded as <see cref="ServerPipeLog.Failed"/>, which was never
/// provoked here.
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
    /// own answer: <c>0x80070005</c> when an instance of the name already exists,
    /// <i>corrected 2026-10-03 with Q297 b (previously <c>0x800700E7</c>)</i>.
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
    /// <returns>The serving pipe, which serves its connections in parallel (Q297 b).</returns>
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
    /// <see cref="NamedPipes.CreateParallelServer"/> for every pipe, so the
    /// coordinator's gets the first-instance flag, the refusal of remote clients,
    /// the current user's DACL and the parallel serving with nothing to remember.
    /// <i>Corrected 2026-10-03 with Q368 a (previously <c>NamedPipes.CreateServer</c>,
    /// one instance, which is gone).</i>
    /// </remarks>
    /// <param name="name">The full pipe name.</param>
    /// <param name="answers">What each request is answered with.</param>
    /// <param name="logger">Where the pipe reports.</param>
    /// <returns>The serving pipe, which serves its connections in parallel.</returns>
    /// <exception cref="IOException">
    /// The pipe was not created. <see cref="Exception.HResult"/> is Windows' own
    /// answer: <c>0x80070005</c> when an instance of the name already exists,
    /// ours or somebody else's. <i>Corrected 2026-10-03 with Q368 a (previously
    /// "<c>0x800700E7</c> when an instance of the name already exists, and
    /// <c>0x80070005</c> when somebody else created the name first").</i>
    /// </exception>
    public static ServerPipe OpenNamed(string name, IPipeAnswers answers, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(logger);

        return Start(name, NamedPipes.CreateParallelServer(name), answers, logger);
    }

    /// <summary>Wraps a created first instance and starts its thread.</summary>
    /// <param name="name">The full pipe name.</param>
    /// <param name="handle">The first instance. Owned from here, and disposed if the start fails.</param>
    /// <param name="answers">What each request is answered with.</param>
    /// <param name="logger">Where the pipe reports.</param>
    /// <returns>The serving pipe.</returns>
    private static ServerPipe Start(string name, SafeFileHandle handle, IPipeAnswers answers, ILogger logger)
    {
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
    /// It does not wait for the listener or for a connection's thread. A
    /// connection whose client has not closed its end finishes with that client;
    /// the process going away takes every one of them regardless, because they
    /// are background threads. <i>Corrected 2026-10-03 with Q368 a (previously "A
    /// thread blocked on a client that has not closed its end finishes with that
    /// client and then sees the request to stop"), which described the
    /// one-thread loop.</i>
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) is not 0)
        {
            return;
        }

        // Wake the listener: connect once and let go. Corrected 2026-10-03 with
        // Q368 a (previously "A pipe that is busy with somebody else answers this
        // with ERROR_PIPE_BUSY, and the thread then sees the request to stop as
        // soon as that client is done"). However many callers hold the other
        // instances, the listener is either waiting on one of its own that nobody
        // is connected to, which this connect wakes, or between two waits, where
        // it reads the request to stop before it waits again.
        using var wake = NamedPipes.OpenClient(Name, out _);
    }

    /// <summary>
    /// The listening thread: wait for a client, make the next instance, hand the
    /// connected one to a thread of its own, listen again.
    /// </summary>
    /// <remarks>
    /// <b>The next instance is made before the connected one is handed over</b>,
    /// so this thread holds an instance of the name at every moment and the
    /// creation without <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> can only ever join our
    /// own pipe. <i>Corrected 2026-10-03 (previously "When every instance Windows
    /// allows is busy, the connected client waits here until one is let go: its own
    /// bound ends that wait for it, and this thread goes on asking").</i> No such
    /// state exists: see the type's remarks, and the measurement they link.
    /// </remarks>
    private void Serve()
    {
        var listening = _pipe!;
        _pipe = null;

        try
        {
            while (Volatile.Read(ref _stopping) is 0)
            {
                if (!NamedPipes.WaitForClient(listening))
                {
                    NamedPipes.Disconnect(listening);
                    continue;
                }

                if (Volatile.Read(ref _stopping) is not 0)
                {
                    NamedPipes.Disconnect(listening);
                    break;
                }

                var next = NamedPipes.CreateParallelInstance(Name);

                // The connected instance belongs to its own thread from here.
                new Connection(this, listening).Start();

                listening = next;
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
            listening.Dispose();
        }
    }

    /// <summary>
    /// One connected instance of a parallel pipe, served on a thread of its own,
    /// which drops the connection and closes the instance when it is done.
    /// </summary>
    /// <param name="pipe">The pipe it came from, for its answers and its log.</param>
    /// <param name="connected">The connected instance. This object owns it.</param>
    private sealed class Connection(ServerPipe pipe, SafeFileHandle connected)
    {
        /// <summary>Starts the connection's thread.</summary>
        public void Start() =>
            new Thread(Serve)
            {
                IsBackground = true,
                Name = "BrowserAI server pipe connection",
            }.Start();

        private void Serve()
        {
            try
            {
                // The stream owns the handle from here and closes it on the way
                // out, after the disconnect and after whatever the answer asked for.
                using var stream = new FileStream(connected, FileAccess.ReadWrite, bufferSize: 0, isAsync: false);

                var after = pipe.AnswerOne(stream, NamedPipes.ClientProcessIdOf(connected));

                NamedPipes.Disconnect(connected);

                // ⚠️ AFTER the disconnect, as on the serial pipe: the client has
                // its whole answer before anything this reply asked for begins.
                after?.Invoke();
            }
#pragma warning disable CA1031 // A connection's own thread boundary: one connection that fails costs that connection, never the pipe or the server.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                try
                {
                    ServerPipeLog.ConnectionFailed(pipe._logger, pipe.Name, failure);
                }
#pragma warning disable CA1031 // A logger that throws must not defeat the catch-all that was reporting through it.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }
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

    /// <summary>One connection of a parallel pipe failed, and only that connection was lost.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe's full name.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Warning,
        Message = "A connection to {Name} failed and was dropped. The pipe goes on serving every other caller.")]
    public static partial void ConnectionFailed(ILogger logger, string name, Exception failure);

    // Id 7 was `EveryInstanceBusy`, a warning that every instance Windows allows
    // for a parallel pipe was held and the next caller waited for one to let go.
    // Removed 2026-10-03 with the wait it reported: a pipe created with
    // PIPE_UNLIMITED_INSTANCES has no such ceiling, measured to 2,000 callers held
    // at once, so nothing can produce it, and the id is not reused for anything
    // else.
    //
    // RETIRED-EVENT-IDS: 7
}
