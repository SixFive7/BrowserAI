// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Protocol;
using BrowserAI.Proxy;
using BrowserAI.Relay;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BrowserAI.Background;

/// <summary>Where the background stands, as a new connection meets it.</summary>
internal enum BackgroundState
{
    /// <summary>Serving.</summary>
    Serving,

    /// <summary>Ending for an update: a new relay is refused with the update sentence.</summary>
    Updating,

    /// <summary>Ending for any other reason.</summary>
    Stopping,
}

/// <summary>What the background does for a connection that is not a relay's.</summary>
internal interface IBackgroundVerbs
{
    /// <summary>Where the background stands.</summary>
    BackgroundState State { get; }

    /// <summary>A person's start: a new tab's address on a page.</summary>
    /// <param name="page">The page asked for, or <see langword="null"/> for the status page.</param>
    /// <returns>The address, or <see langword="null"/> with the reason when none could be handed out.</returns>
    (string? Address, string? Refusal) Show(string? page);

    /// <summary>The uninstall hook's stop, or the suite's: close every session cleanly and end.</summary>
    void Stop();
}

/// <summary>
/// The background's pipe: every relay, every person's start and the uninstall hook
/// reach the background here.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first message on a connection says what the connection is</b>
/// (<see cref="BackgroundPipe"/>): a relay's greeting makes it a relay's, carrying its
/// client's MCP traffic for as long as it stays open; a person's <c>show</c> and the
/// uninstall hook's <c>stop</c> are answered and closed. Anything else is refused and
/// closed.
/// </para>
/// <para>
/// <b>The name is taken in the constructor, with
/// <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c></b>, so a second background for the same user,
/// install root and data root fails there, before it has served anybody, and exits.
/// That is the one-background rule, beside the task's own <c>IgnoreNew</c> (S a).
/// </para>
/// <para>
/// <b>It never ends on its own</b> (S a, decided 2026-10-08, reversing lane c's
/// minute-long linger): a relay, a session, a tab or nothing at all, the background
/// stays until sign-out, an update, a stop or a crash.
/// </para>
/// </remarks>
internal sealed partial class BackgroundServer : IAsyncDisposable
{
    /// <summary>The longest first message accepted: a greeting is a few hundred bytes.</summary>
    public const int FirstFrameLimit = 64 * 1024;

    /// <summary>
    /// How long a connection may take to send its first message: a hang detector, since
    /// every caller of ours writes it first, at once.
    /// </summary>
    public static TimeSpan FirstFrameBound { get; } = TimeSpan.FromSeconds(10);

    private readonly SessionHost _host;
    private readonly BackgroundIdentity _identity;
    private readonly RelayRoster _roster;
    private readonly IBackgroundVerbs _verbs;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ManualResetEvent _stop = new(initialState: false);
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _serving = [];
    private readonly Thread _listener;

    private SafeFileHandle? _first;
    private int _started;
    private int _disposed;

    /// <summary>Takes the pipe's name.</summary>
    /// <param name="host">The sessions.</param>
    /// <param name="identity">Who this background is, for a relay's greeting.</param>
    /// <param name="roster">Every relay, kept for the update.</param>
    /// <param name="verbs">What a person's start and a stop are answered with.</param>
    /// <param name="loggerFactory">Where the connections log.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The name is taken: another background serves it.</exception>
    public BackgroundServer(SessionHost host, BackgroundIdentity identity, RelayRoster roster, IBackgroundVerbs verbs, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(verbs);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _host = host;
        _identity = identity;
        _roster = roster;
        _verbs = verbs;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<BackgroundServer>();

        // ⚠️ IN THE CONSTRUCTOR, so a second background fails here and says why.
        _first = NamedPipes.CreateStreamServer(identity.PipeName);

        _listener = new Thread(Listen)
        {
            IsBackground = true,
            Name = "BrowserAI background pipe",
        };
    }

    /// <summary>The pipe it serves.</summary>
    public string Name => _identity.PipeName;

    /// <summary>Starts accepting.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) is not 0)
        {
            return;
        }

        _listener.Start();
        BackgroundServerLog.Listening(_logger, _identity.PipeName);
    }

    /// <summary>Stops accepting and ends every connection.</summary>
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
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        Stop();

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
                // name never stands without an instance of ours.
                var next = NamedPipes.CreateStreamInstance(_identity.PipeName);

                Serve(listening, client);
                listening = next;
            }
        }
#pragma warning disable CA1031 // The listener's thread boundary: a pipe that fails is a log line, and the sessions run on.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            BackgroundServerLog.ListenerFailed(_logger, _identity.PipeName, failure);
        }
        finally
        {
            listening.Dispose();
        }
    }

    private void Serve(SafeFileHandle connected, int? client)
    {
        var serving = Task.Run(() => ConverseAsync(connected, client), CancellationToken.None);

        lock (_serving)
        {
            _ = _serving.RemoveAll(static task => task.IsCompleted);
            _serving.Add(serving);
        }
    }

    private async Task ConverseAsync(SafeFileHandle connected, int? client)
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

        // Every path closes it here. A relay's transport closes it as well, when the
        // relay goes, and a second close of a stream does nothing.
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                using var firstFrame = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                firstFrame.CancelAfter(FirstFrameBound);

                var first = await ReadFirstFrameAsync(stream, firstFrame.Token).ConfigureAwait(false);

                if (first is not JsonRpcRequest request)
                {
                    BackgroundServerLog.NotARequest(_logger, client ?? 0);
                    return;
                }

                switch (request.Method)
                {
                    case BackgroundPipe.Show:
                        await AnswerShowAsync(stream, request).ConfigureAwait(false);
                        return;

                    case BackgroundPipe.Stop:
                        await WriteAsync(stream, new JsonRpcResponse { Id = request.Id, Result = new JsonObject { ["stopping"] = true, ["pid"] = Environment.ProcessId } }).ConfigureAwait(false);
                        BackgroundServerLog.StopAsked(_logger, client ?? 0);
                        _verbs.Stop();
                        return;

                    case RelayProtocol.Hello:
                        await ServeRelayAsync(stream, request, client).ConfigureAwait(false);
                        return;

                    default:
                        await WriteAsync(stream, Refusal(request.Id, (int)McpErrorCode.MethodNotFound, $"The background's pipe starts with '{RelayProtocol.Hello}', '{BackgroundPipe.Show}' or '{BackgroundPipe.Stop}', and this connection sent '{request.Method}'.", "method")).ConfigureAwait(false);
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                // The background is stopping, or the first message never came.
            }
#pragma warning disable CA1031 // A connection's own boundary: one that fails costs that connection, never the background.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                BackgroundServerLog.ConnectionFailed(_logger, client ?? 0, failure);
            }
        }
    }

    private async Task AnswerShowAsync(FileStream stream, JsonRpcRequest request)
    {
        var page = request.Params?["page"] is JsonValue value && value.TryGetValue<string>(out var named) ? named : null;
        var (address, refusal) = _verbs.Show(page);

        await WriteAsync(
            stream,
            address is { } handed
                ? new JsonRpcResponse { Id = request.Id, Result = new JsonObject { ["address"] = handed, ["pid"] = Environment.ProcessId } }
                : Refusal(request.Id, (int)McpErrorCode.InternalError, refusal ?? "The background could not hand out a tab.", "show")).ConfigureAwait(false);
    }

    private async Task ServeRelayAsync(FileStream stream, JsonRpcRequest hello, int? relayPid)
    {
        var parameters = hello.Params;
        var build = Text(parameters, "build");
        var dataRoot = Text(parameters, "dataRoot");

        if (Judge(build, dataRoot) is { } refused)
        {
            BackgroundServerLog.RelayRefused(_logger, relayPid ?? 0, refused.Kind);
            await WriteAsync(stream, Refusal(hello.Id, (int)McpErrorCode.InvalidRequest, refused.Sentence, refused.Kind)).ConfigureAwait(false);
            return;
        }

        var connection = new CallerConnection(relayPid);
        var greeting = new RelayGreeting(
            Id: string.Create(CultureInfo.InvariantCulture, $"{relayPid ?? 0}-{connection.Number}"),
            RelayPid: relayPid ?? 0,
            ClientPid: Number(parameters, "clientPid"),
            ClientName: Text(parameters?["client"], "name"),
            ClientVersion: Text(parameters?["client"], "version"),
            Folder: Text(parameters, "folder"),
            Reconnect: Enum.TryParse<RelayReconnect>(Text(parameters, "reconnect"), out var reconnect) ? reconnect : RelayReconnect.Unknown,
            Conversation: Text(parameters, "conversation"),
            Label: Text(parameters, "label"));
        var idleAt = Instant(parameters, "idleAt") ?? DateTimeOffset.UtcNow;

        await WriteAsync(stream, new JsonRpcResponse
        {
            Id = hello.Id,
            Result = new JsonObject { ["build"] = _identity.Build, ["pid"] = Environment.ProcessId },
        }).ConfigureAwait(false);

        BackgroundServerLog.RelayConnected(_logger, greeting.Id, greeting.ClientName ?? "unnamed client", greeting.ClientPid ?? 0);

#pragma warning disable CA2000 // The link owns the transport from here and closes it; the transport owns the stream.
        var transport = new PipeServerTransport(stream, $"BrowserAI relay {greeting.Id}", _loggerFactory);
#pragma warning restore CA2000

        var link = new RelayLink(transport, $"BrowserAI relay link {greeting.Id}", (_, notice) => _roster.Heard(greeting.Id, notice), _loggerFactory);

        await using var linkScope = link.ConfigureAwait(false);

        _roster.Add(greeting, link, idleAt);

        var proxy = _host.Accept(connection);

        // The relay answers its client's tool list from the binary, and Q261 b's
        // stale-list refusal is the relay's.
        proxy.ListsThroughTheRelay();

        try
        {
            var server = McpServer.Create(link, proxy.ServerOptions(), _loggerFactory);

            await using var serverScope = server.ConfigureAwait(false);

            try
            {
                await server.RunAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The background is stopping. The detach below still runs.
            }
        }
        finally
        {
            _roster.Remove(greeting.Id);

            // ⚠️ THE DETACH: what this connection drove is judged, kept or let go,
            // and nothing else in the background is touched.
            await proxy.DisposeAsync().ConfigureAwait(false);

            BackgroundServerLog.RelayDisconnected(_logger, greeting.Id, _roster.Count, _host.Sessions.HeldCount);
        }
    }

    /// <summary>Whether a relay's greeting is refused, and the sentence that says why.</summary>
    /// <param name="build">The relay's build.</param>
    /// <param name="dataRoot">The data root the relay was registered for.</param>
    /// <returns>The refusal, or <see langword="null"/> when the relay is served.</returns>
    private (string Kind, string Sentence)? Judge(string? build, string? dataRoot)
    {
        switch (_verbs.State)
        {
            case BackgroundState.Updating:
                return (RelayProtocol.RefusedForAnUpdate, "BrowserAI is installing an update, so this background takes no new client.");

            case BackgroundState.Stopping:
                return (RelayProtocol.RefusedWhileStopping, "BrowserAI's background is stopping, so it takes no new client.");
        }

        if (!string.Equals(build, _identity.Build, StringComparison.Ordinal))
        {
            return (RelayProtocol.RefusedForTheBuild, $"This background is BrowserAI {_identity.Build} and the client's BrowserAI is {build ?? "of no stated version"}, and the two speak to each other only within one version. Starting BrowserAI from the Start Menu starts the installed version's background.");
        }

        if (dataRoot is null || !RootKey.Same(dataRoot, _identity.DataRoot))
        {
            return (RelayProtocol.RefusedForTheDataRoot, $"This background keeps its sessions under '{_identity.DataRoot}', and the client's BrowserAI was registered for '{dataRoot ?? "no data root"}', so it is not served here.");
        }

        return null;
    }

    private static async Task<JsonRpcMessage?> ReadFirstFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        // One byte at a time: whatever follows the first newline belongs to the
        // transport that takes the stream over, and a buffered read would swallow it.
        var buffer = new byte[FirstFrameLimit];
        var length = 0;
        var one = new byte[1];

        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);

            if (read is 0)
            {
                return null;
            }

            if (one[0] is (byte)'\n')
            {
                break;
            }

            buffer[length++] = one[0];
        }

        try
        {
            return JsonSerializer.Deserialize(buffer.AsSpan(0, length), McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage))) as JsonRpcMessage;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteAsync(Stream stream, JsonRpcMessage message)
    {
        var bytes = RelayWire.Encode(message);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static JsonRpcError Refusal(RequestId id, int code, string sentence, string kind) => new()
    {
        Id = id,
        Error = new JsonRpcErrorDetail { Code = code, Message = sentence, Data = JsonSerializer.SerializeToElement(new JsonObject { ["refusal"] = kind }, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonObject))) },
    };

    private static string? Text(JsonNode? parameters, string name) =>
        parameters?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Number(JsonNode? parameters, string name) =>
        parameters?[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static DateTimeOffset? Instant(JsonNode? parameters, string name) =>
        Text(parameters, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;
}

/// <summary>Who a background is, as a relay's greeting is judged against it.</summary>
/// <param name="PipeName">Its pipe.</param>
/// <param name="Build">Its version.</param>
/// <param name="DataRoot">Its data root.</param>
internal sealed record BackgroundIdentity(string PipeName, string Build, string DataRoot);

/// <summary>Source-generated log messages for <see cref="BackgroundServer"/>.</summary>
internal static partial class BackgroundServerLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "The background serves {Name}.")]
    public static partial void Listening(ILogger logger, string name);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Relay {Relay} connected, for {Client} (client pid {ClientPid}).")]
    public static partial void RelayConnected(ILogger logger, string relay, string client, int clientPid);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Relay {Relay} went; {Relays} relay(s) and {Sessions} session(s) are left.")]
    public static partial void RelayDisconnected(ILogger logger, string relay, int relays, int sessions);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "A relay (pid {Relay}) was refused: {Kind}.")]
    public static partial void RelayRefused(ILogger logger, int relay, string kind);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "A connection from pid {Client} failed and was dropped. The background goes on serving every other.")]
    public static partial void ConnectionFailed(ILogger logger, int client, Exception failure);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "The background stopped accepting on {Name} after a failure. Its sessions run on until it ends.")]
    public static partial void ListenerFailed(ILogger logger, string name, Exception failure);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "A connection from pid {Client} sent no request first, and was closed.")]
    public static partial void NotARequest(ILogger logger, int client);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "pid {Client} asked the background to stop.")]
    public static partial void StopAsked(ILogger logger, int client);
}
