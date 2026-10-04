// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using ModelContextProtocol.Server;

namespace BrowserAI.Proxy;

/// <summary>
/// The session host's pipe: one MCP conversation per connection, every one of them
/// over the same <see cref="SessionHost"/>, and the rule that decides when the host
/// has nothing left to do.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q366 b, the maintainer's words of 2026-10-03, verbatim:</b> <i>"Q366 b - lets go
/// with a fully build option c. If the server crashes and the coordinator loses the
/// pipe, keep the browser around with the already running activity timeout timer
/// active."</i> The coordinator starts this process in a kill-on-close job of its
/// own, outside every client's tree and job; a client's server connects here and
/// relays its client's bytes. When that server dies its connection ends, and only
/// what the connection drove is detached (<see cref="SessionHost.EndAsync"/>).
/// </para>
/// <para>
/// <b>The pipe is named for the install root</b>, <c>\\.\pipe\BrowserAI-Host-</c> and
/// the root's key, the key the census gate, the coordinator's pipe and the logon task
/// end in, so one install has one host. It is created with
/// <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>, so a second host for the same root fails to
/// start instead of joining the first one's pipe, and with the current user's DACL
/// and the refusal of remote clients every pipe of ours has.
/// </para>
/// <para>
/// <b>It ends itself once it has neither a connection nor a session for
/// <see cref="Linger"/></b>, so a client's restart, which ends one connection and
/// opens the next a moment later, never meets a host on its way out.
/// </para>
/// </remarks>
internal sealed class SessionHostServer : IAsyncDisposable
{

    /// <summary>
    /// How long the host stays with no connection and no session: the coordinator's
    /// own linger after its last tab, Q336 a's minute, <b>one minute</b>.
    /// </summary>
    /// <remarks>
    /// <b>The same wait for the same reason.</b> The maintainer chose a minute for a
    /// person who closed the last tab and may open another; a client's restart, which
    /// ends one connection and opens the next a few seconds later, is the same kind of
    /// return, and a host that waited less would be gone when it came. The app's
    /// <c>PageTabs.ProductLinger</c> is out of this binary's reach, so
    /// <c>SessionHostBoundsTests</c> holds the two equal.
    /// </remarks>
    public static TimeSpan Linger { get; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often the host asks whether it has been empty for <see cref="Linger"/>: a
    /// quarter of it, <b>15 s</b>.
    /// </summary>
    /// <remarks>
    /// <b>The quarter is the overshoot</b>: an empty host ends between one linger and one
    /// and a quarter after it last had anything to do, and the look itself is two reads
    /// of two counts, which costs nothing at any rate.
    /// </remarks>
    public static TimeSpan LingerLook { get; } = Linger / 4;

    private readonly SessionHost _host;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _linger;
    private readonly ManualResetEvent _stop = new(initialState: false);
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> _serving = [];
    private readonly Thread _listener;
    private readonly ITimer _look;

    private SafeFileHandle? _first;
    private int _connections;
    private long _emptySince = -1;
    private int _started;
    private int _disposed;

    /// <summary>Creates the host's pipe; <see cref="Start"/> begins serving it.</summary>
    /// <param name="host">The sessions every connection is served over. Not owned.</param>
    /// <param name="name">The full pipe name.</param>
    /// <param name="loggerFactory">Where the pipe and its connections log.</param>
    /// <param name="clock">What the linger is measured against.</param>
    /// <param name="linger">How long the host stays with nothing to do.</param>
    /// <exception cref="IOException">The pipe was not created: another host serves this name, or somebody else created it first.</exception>
    public SessionHostServer(SessionHost host, string name, ILoggerFactory loggerFactory, TimeProvider clock, TimeSpan linger)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(clock);

        _host = host;
        Name = name;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SessionHostServer>();
        _clock = clock;
        _linger = linger;

        // ⚠️ IN THE CONSTRUCTOR, so a second host for this root fails here, before
        // it has served anybody, and says why.
        _first = NamedPipes.CreateStreamServer(name);

        _listener = new Thread(Listen)
        {
            IsBackground = true,
            Name = "BrowserAI session host pipe",
        };

        _look = clock.CreateTimer(_ => LookWhetherEmpty(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The pipe's full name.</summary>
    public string Name { get; }

    /// <summary>How many connections are being served right now.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>Completes once the host has nothing left to do, or <see cref="Stop"/> was asked for.</summary>
    public Task Finished => _finished.Task;

    /// <summary>Starts accepting connections and looking at the linger.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) is not 0)
        {
            return;
        }

        _listener.Start();
        _ = _look.Change(LingerLook, LingerLook);
        HostServerLog.Listening(_logger, Name);
    }

    /// <summary>
    /// Stops as <see cref="Stop"/> does, for a stop that arrived through this process's
    /// own pipe: the sessions the shutdown closes record that, and not a client going.
    /// </summary>
    /// <remarks>
    /// <b>8 b, 2026-10-04.</b> The pipe's stop is what an update's install sends, and
    /// what BrowserAI's page sends when a person closes a server there.
    /// </remarks>
    public void StopThroughThePipe()
    {
        _host.Sessions.StoppingThroughThePipe();
        Stop();
    }

    /// <summary>Stops accepting, ends every conversation, and completes <see cref="Finished"/>.</summary>
    public void Stop()
    {
        _ = _stop.Set();

        try
        {
            _stopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Disposal is already under way.
        }

        _ = _finished.TrySetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        Stop();
        await _look.DisposeAsync().ConfigureAwait(false);

        Task[] serving;

        lock (_serving)
        {
            serving = [.. _serving];
        }

        try
        {
            await Task.WhenAll(serving).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Each connection reports its own failure; disposal only waits for them to end.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        if (Volatile.Read(ref _started) is not 0)
        {
            _listener.Join();
        }

        _first?.Dispose();
        _stop.Dispose();
        _stopping.Dispose();
    }

    private void Listen()
    {
        var listening = Interlocked.Exchange(ref _first, null)!;

        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                if (!NamedPipes.WaitForClient(listening, _stop))
                {
                    break;
                }

                var client = NamedPipes.ClientProcessIdOf(listening);

                // The next instance BEFORE the connected one is handed over, so the
                // name never stands without an instance of ours. Created with
                // PIPE_UNLIMITED_INSTANCES, which has no ceiling to wait under:
                // measured 2026-10-03 to 2,000 instances of one name held at once
                // (NamedPipes.UnlimitedInstances). A refusal here is the system
                // out of resources, and the catch below reports it.
                var next = NamedPipes.CreateStreamInstance(Name);

                Serve(listening, client);
                listening = next;
            }
        }
#pragma warning disable CA1031 // The listener's thread boundary: a pipe that fails is a log line, and the host lets its sessions run out.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            HostServerLog.ListenerFailed(_logger, Name, failure);
        }
        finally
        {
            listening.Dispose();
        }
    }

    private void Serve(SafeFileHandle connected, int? client)
    {
        _ = Interlocked.Increment(ref _connections);
        Volatile.Write(ref _emptySince, -1);

        var serving = Task.Run(() => ConverseAsync(connected, client), CancellationToken.None);

        lock (_serving)
        {
            _ = _serving.RemoveAll(task => task.IsCompleted);
            _serving.Add(serving);
        }
    }

    /// <summary>One connection's conversation, from its first frame to its end.</summary>
    /// <param name="connected">The connected instance. Owned from here.</param>
    /// <param name="client">The pid on the other end, when Windows said.</param>
    /// <returns>The conversation.</returns>
    private async Task ConverseAsync(SafeFileHandle connected, int? client)
    {
        var connection = new CallerConnection(client);

        HostServerLog.Connected(_logger, connection.Number, client ?? 0);

        try
        {
            FileStream stream;

            try
            {
                stream = new FileStream(connected, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
            }
            catch
            {
                // The stream never took the handle, so nothing else will close it.
                connected.Dispose();
                throw;
            }

#pragma warning disable CA2000 // The transport owns the stream from here and closes it; the stream owns the handle.
            var transport = new PipeServerTransport(stream, $"BrowserAI host connection {connection.Number}", _loggerFactory);
#pragma warning restore CA2000

            await using var transportScope = transport.ConfigureAwait(false);

            var proxy = _host.Accept(connection);

            try
            {
                var server = McpServer.Create(transport, proxy.ServerOptions(), _loggerFactory);

                await using var serverScope = server.ConfigureAwait(false);

                try
                {
                    await server.RunAsync(_stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The host is stopping. The detach below still runs.
                }
            }
            finally
            {
                // ⚠️ THE DETACH, which is the whole of what a client's server dying
                // means here: what this connection drove is judged, kept or let go,
                // and nothing else in the host is touched.
                await proxy.DisposeAsync().ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // A connection's own boundary: one that fails costs that connection, never the host.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            HostServerLog.ConnectionFailed(_logger, connection.Number, failure);
        }
        finally
        {
            if (Interlocked.Decrement(ref _connections) is 0)
            {
                Volatile.Write(ref _emptySince, _clock.GetTimestamp());
            }

            HostServerLog.Disconnected(_logger, connection.Number, Connections, _host.Sessions.HeldCount);
        }
    }

    private void LookWhetherEmpty()
    {
        if (Connections is not 0 || _host.Sessions.HeldCount is not 0)
        {
            Volatile.Write(ref _emptySince, -1);
            return;
        }

        var since = Volatile.Read(ref _emptySince);

        if (since < 0)
        {
            Volatile.Write(ref _emptySince, _clock.GetTimestamp());
            return;
        }

        if (_clock.GetElapsedTime(since) < _linger)
        {
            return;
        }

        HostServerLog.NothingLeftToDo(_logger, _linger);
        Stop();
    }
}

/// <summary>Source-generated log messages for the session host's pipe.</summary>
internal static partial class HostServerLog
{
    /// <summary>The host's pipe is up.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe's full name.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "The session host serves {Name}.")]
    public static partial void Listening(ILogger logger, string name);

    /// <summary>A client's server connected.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="connection">The host's number for the connection.</param>
    /// <param name="client">The pid on the other end, or zero when Windows would not say.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Connection {Connection} opened by pid {Client}.")]
    public static partial void Connected(ILogger logger, long connection, int client);

    /// <summary>A connection ended, and what is left.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="connection">The host's number for the connection.</param>
    /// <param name="connections">How many connections are left.</param>
    /// <param name="sessions">How many sessions the host still holds.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Connection {Connection} ended; {Connections} connection(s) and {Sessions} session(s) are left.")]
    public static partial void Disconnected(ILogger logger, long connection, int connections, int sessions);

    /// <summary>One connection failed, and only it was lost.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="connection">The host's number for the connection.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Connection {Connection} failed and was dropped. The host goes on serving every other.")]
    public static partial void ConnectionFailed(ILogger logger, long connection, Exception failure);

    /// <summary>The listener stopped on a failure.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="name">The pipe's full name.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "The session host stopped accepting on {Name} after a failure. Its sessions run out as their timers say.")]
    public static partial void ListenerFailed(ILogger logger, string name, Exception failure);

    /// <summary>The host has had nothing to do for its linger, and ends.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="linger">How long it waited.</param>
    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "The session host has held no session and served no connection for {Linger}, so it ends.")]
    public static partial void NothingLeftToDo(ILogger logger, TimeSpan linger);
}
