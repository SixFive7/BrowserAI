// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Background;
using BrowserAI.Clients;
using BrowserAI.Coordination;
using BrowserAI.Interop;
using BrowserAI.Proxy;
using BrowserAI.Registration;
using BrowserAI.Relay;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The background's pipe in this process: one <see cref="BackgroundServer"/> over a
/// <see cref="SessionHost"/> on a rig's session doubles, on a pipe name of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything behind the pipe is the product's</b>: the server, the relay link, the
/// roster and the MCP server over the session host, exactly as
/// <c>Program.RunTheBackground</c> composes them. What stands in is what a test cannot
/// let the background do: <see cref="FakeBackgroundVerbs"/> answers <c>show</c> and
/// <c>stop</c> where the background's page and its wait would, and the sessions are
/// <see cref="RigSessionEnvironment"/>'s doubles, so no browser and no published
/// binary is ever started.
/// </para>
/// <para>
/// <b>The pipe is named for the arm</b>, under <see cref="BackgroundPipe.NamePrefix"/>
/// with a GUID in it (<see cref="PublishedBackground.NewPipeName"/>), unless an arm
/// needs the name the product composes from two roots, as the uninstall hook does.
/// So no two arms of a run, and no real install, ever share one.
/// </para>
/// <para>
/// <b>The roster is wired to counters</b>, the way the background wires it to the
/// update core, so an arm reads how often it was told of a change and which relays
/// withdrew a yes.
/// </para>
/// </remarks>
internal sealed class BackgroundServerRig : IAsyncDisposable
{
    /// <summary>The build every rig's background says it is.</summary>
    public const string Build = "9.9.9-background-tests";

    private readonly ILoggerFactory _loggerFactory;
    private readonly bool _ownsSessions;
    private readonly ConcurrentQueue<string> _withdrawn = new();
    private int _changes;
    private int _disposed;

    private BackgroundServerRig(RigSessionEnvironment sessions, bool ownsSessions, string pipeName, string? dataRoot, FakeBackgroundVerbs verbs)
    {
        Sessions = sessions;
        _ownsSessions = ownsSessions;
        Verbs = verbs;

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            _ = builder.SetMinimumLevel(LogLevel.Trace);
            _ = builder.AddProvider(new TUnitLoggerProvider());
            _ = builder.AddProvider(Logs);
        });

        sessions.CaptureSessionRecordsInto(Logs);

        Host = SessionHost.Create(_loggerFactory, sessions.Environment);
        Identity = new BackgroundIdentity(pipeName, Build, dataRoot ?? sessions.Environment.Paths.RootAppDir);
        Roster = new RelayRoster(Clock, ConversationReader.Files, _loggerFactory.CreateLogger<RelayRoster>());
        Roster.TellUpdatesThrough(() => _ = Interlocked.Increment(ref _changes), _withdrawn.Enqueue);

        try
        {
            Server = new BackgroundServer(Host, Identity, Roster, Verbs, _loggerFactory);
        }
        catch
        {
            Host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _loggerFactory.Dispose();
            throw;
        }

        Server.Start();
    }

    /// <summary>The clock the roster reads a relay that cannot be asked against.</summary>
    public ManualClock Clock { get; } = new();

    /// <summary>The sessions behind the host.</summary>
    public RigSessionEnvironment Sessions { get; }

    /// <summary>The host every relay's MCP server answers from.</summary>
    public SessionHost Host { get; }

    /// <summary>Who the background says it is.</summary>
    public BackgroundIdentity Identity { get; }

    /// <summary>The relays, as the update core reads them.</summary>
    public RelayRoster Roster { get; }

    /// <summary>What <c>show</c> and <c>stop</c> are answered with.</summary>
    public FakeBackgroundVerbs Verbs { get; }

    /// <summary>The server under test.</summary>
    public BackgroundServer Server { get; }

    /// <summary>Everything the product logged.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>How many times the roster told the update core of a change.</summary>
    public int Changes => Volatile.Read(ref _changes);

    /// <summary>Every relay the roster said withdrew its yes, in order.</summary>
    public IReadOnlyList<string> Withdrawn => [.. _withdrawn];

    /// <summary>Starts a background over a rig's sessions.</summary>
    /// <param name="pipeName">The pipe, or <see langword="null"/> for one of the arm's own.</param>
    /// <param name="dataRoot">The data root it says it serves, or <see langword="null"/> for the rig's.</param>
    /// <param name="sessions">The sessions, which the caller then owns; or <see langword="null"/> for a rig of doubles this rig owns.</param>
    /// <param name="verbs">The verbs, or <see langword="null"/> for ones that hand out <see cref="FakeBackgroundVerbs.DefaultAddress"/>.</param>
    /// <returns>The background, accepting.</returns>
    public static BackgroundServerRig Start(
        string? pipeName = null,
        string? dataRoot = null,
        RigSessionEnvironment? sessions = null,
        FakeBackgroundVerbs? verbs = null)
    {
        var pipe = pipeName ?? PublishedBackground.NewPipeName();

        if (sessions is not null)
        {
            return new BackgroundServerRig(sessions, ownsSessions: false, pipe, dataRoot, verbs ?? new FakeBackgroundVerbs());
        }

        // The rig owns these and disposes them with itself, or below when it could
        // not be made; the rule's dataflow does not follow the move into the rig.
#pragma warning disable CA2000
        var owned = RigSessionEnvironment.Create(opensDefaultSession: false);
#pragma warning restore CA2000

        try
        {
            return new BackgroundServerRig(owned, ownsSessions: true, pipe, dataRoot, verbs ?? new FakeBackgroundVerbs());
        }
        catch
        {
            owned.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>Opens a connection to this background's pipe, as a relay or a start does.</summary>
    /// <returns>The connection.</returns>
    public BackgroundPipeClient Connect() => BackgroundPipeClient.Connect(Identity.PipeName);

    /// <summary>A relay's greeting, as the relay engine writes it, to this background.</summary>
    /// <param name="build">The relay's build, or <see langword="null"/> for this background's.</param>
    /// <param name="dataRoot">The data root it was registered for, or <see langword="null"/> for this background's.</param>
    /// <param name="idleAt">When its countdown runs out.</param>
    /// <returns>The frame.</returns>
    public string Hello(string? build = null, string? dataRoot = null, DateTimeOffset? idleAt = null) =>
        BackgroundPipeClient.Hello(build ?? Identity.Build, dataRoot ?? Identity.DataRoot, idleAt ?? Clock.GetUtcNow());

    /// <summary>
    /// A relay whose greeting this background accepted and whose client has listed the
    /// tools: the state every relay is in once its client has made its first turn.
    /// </summary>
    /// <param name="idleAt">The countdown the greeting carries.</param>
    /// <returns>The relay's connection.</returns>
    public async Task<BackgroundPipeClient> ConnectARelayAsync(DateTimeOffset? idleAt = null)
    {
        var relay = Connect();

        try
        {
            await relay.SendAsync(Hello(idleAt: idleAt));

            var answer = await relay.NextAsync();

            if (answer.Result is null)
            {
                throw new InvalidOperationException($"The background refused the rig's own greeting: {answer.Text}");
            }

            _ = await relay.InitializeAsync();
            _ = await relay.ListAsync();

            return relay;
        }
        catch
        {
            relay.Dispose();
            throw;
        }
    }

    /// <summary>The one relay the roster holds, as the update core reads it.</summary>
    /// <returns>Its state.</returns>
    /// <exception cref="InvalidOperationException">The roster holds no relay, or more than one.</exception>
    public RelayState OnlyRelay() => Roster.Connected().Single();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        // A connection held inside a verb would hold the server's disposal with it.
        Verbs.Release();

        await Server.DisposeAsync();
        await Host.DisposeAsync();

        if (_ownsSessions)
        {
            await Sessions.DisposeAsync();
        }

        _loggerFactory.Dispose();
        Logs.Dispose();
    }

    /// <summary>Waits until a condition on the background holds.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="whatWentWrong">What a timeout means.</param>
    /// <returns>A task that completes once it holds.</returns>
    public static async Task WaitUntilAsync(Func<bool> condition, string whatWentWrong)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            // A hang detector, never a promptness claim: the bound is the suite's
            // own and nothing here asserts on how long the wait took.
            if (waited.Elapsed > TestDefaults.InProcessHang)
            {
                throw new TimeoutException($"{whatWentWrong} -- after {waited.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }
}

/// <summary>
/// What a background answers a person's start and a stop with, as an arm sets it:
/// an address, a refusal, a state, and a record of every call.
/// </summary>
/// <param name="events">A queue several doubles write to, so an arm reads one order across them; or <see langword="null"/>.</param>
internal sealed class FakeBackgroundVerbs(ConcurrentQueue<string>? events = null) : IBackgroundVerbs
{
    /// <summary>The address handed out unless an arm says otherwise.</summary>
    public const string DefaultAddress = "http://127.0.0.1:47110/background-tests";

    private readonly ConcurrentQueue<string?> _pages = new();
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state = (int)BackgroundState.Serving;
    private int _stops;
    private int _holding;

    /// <summary>Every event of every double sharing the queue, in order.</summary>
    public ConcurrentQueue<string> Events { get; } = events ?? new();

    /// <inheritdoc />
    public BackgroundState State
    {
        get => (BackgroundState)Volatile.Read(ref _state);
        set => Volatile.Write(ref _state, (int)value);
    }

    /// <summary>The address <see cref="Show"/> hands out, or <see langword="null"/> to refuse.</summary>
    public string? Address { get; set; } = DefaultAddress;

    /// <summary>The refusal <see cref="Show"/> gives when it hands out no address.</summary>
    public string Refusal { get; set; } = "The suite's background refuses to open a page.";

    /// <summary>Whether <see cref="Show"/> waits for <see cref="Release"/> before it answers, as a hung background does.</summary>
    public bool HoldsShow
    {
        get => Volatile.Read(ref _holding) is not 0;
        set => Volatile.Write(ref _holding, value ? 1 : 0);
    }

    /// <summary>Every page asked for, in order; <see langword="null"/> for the status page.</summary>
    public IReadOnlyList<string?> Pages => [.. _pages];

    /// <summary>How many times <see cref="Stop"/> was called.</summary>
    public int Stops => Volatile.Read(ref _stops);

    /// <inheritdoc />
    public (string? Address, string? Refusal) Show(string? page)
    {
        _pages.Enqueue(page);
        Events.Enqueue($"show {page ?? "status"}");

        if (HoldsShow && !_released.Task.Wait(TestDefaults.InProcessHang))
        {
            throw new TimeoutException("Nothing released the suite's held show.");
        }

        return Address is { } address ? (address, null) : (null, Refusal);
    }

    /// <inheritdoc />
    public void Stop()
    {
        _ = Interlocked.Increment(ref _stops);
        Events.Enqueue("stop");
    }

    /// <summary>Lets a held <see cref="Show"/> answer.</summary>
    public void Release() => _ = _released.TrySetResult();
}

/// <summary>
/// The Task Scheduler as a person's start and the hooks meet it: it records every
/// call, registers into memory, and starts what the arm says a start starts.
/// </summary>
/// <param name="events">A queue several doubles write to, so an arm reads one order across them; or <see langword="null"/>.</param>
internal sealed class ScriptedLogonTasks(ConcurrentQueue<string>? events = null) : ILogonTasks
{
    /// <summary>Every call, as <c>verb name [argument]</c>, in the order asked.</summary>
    public ConcurrentQueue<string> Events { get; } = events ?? new();

    /// <summary>What each registered name was given.</summary>
    public ConcurrentDictionary<string, string> Registered { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What a run answers instead of the default (started when registered, otherwise
    /// not registered), or <see langword="null"/> for the default.
    /// </summary>
    public Func<string, string, TaskReport>? RunAnswer { get; set; }

    /// <summary>What a run that started does, as the task's action would: called before the run returns.</summary>
    public Action<string, string>? Started { get; set; }

    /// <summary>What happens at the moment a run is asked, before it is answered.</summary>
    public Action<string, string>? Asked { get; set; }

    /// <inheritdoc />
    public TaskReport Register(string name, string definition)
    {
        Events.Enqueue($"register {name}");
        Registered[name] = definition;
        return new TaskReport(TaskChange.Registered, $"The task '{name}' is registered.");
    }

    /// <inheritdoc />
    public TaskReport Remove(string name)
    {
        Events.Enqueue($"remove {name}");

        return Registered.TryRemove(name, out _)
            ? new TaskReport(TaskChange.Removed, $"The task '{name}' is removed.")
            : new TaskReport(TaskChange.Absent, $"There was no task '{name}' to remove.");
    }

    /// <inheritdoc />
    public TaskReport Run(string name, string argument)
    {
        Events.Enqueue($"run {name} {argument}");
        Asked?.Invoke(name, argument);

        var report = RunAnswer?.Invoke(name, argument)
            ?? (Registered.ContainsKey(name)
                ? new TaskReport(TaskChange.Started, $"The task '{name}' was started with '{argument}'.")
                : new TaskReport(TaskChange.NotRegistered, $"There is no task '{name}' to start."));

        if (report.Change is TaskChange.Started)
        {
            Started?.Invoke(name, argument);
        }

        return report;
    }
}

/// <summary>
/// One connection to a background's pipe, opened the way a relay and a person's
/// start open it, and read and written frame by frame with no product type.
/// </summary>
/// <remarks>
/// <b>The framing is the suite's own <see cref="FrameChannel"/></b>, so a symmetric
/// mistake in the product's framing cannot pass for a correct one; only the open is
/// the product's interop, because the pipe's client end is a Win32 call and the
/// product's is the one whose flags the background was written against.
/// </remarks>
internal sealed class BackgroundPipeClient : IDisposable
{
    private readonly FileStream _stream;
    private readonly FrameChannel _channel;
    private int _requests;
    private int _disposed;

    private BackgroundPipeClient(SafeFileHandle handle)
    {
        try
        {
            _stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
        }
        catch
        {
            // The stream never took the handle, so nothing else will close it.
            handle.Dispose();
            throw;
        }

        _channel = new FrameChannel(_stream, _stream);
    }

    /// <summary>Opens the client end of a background's pipe, waiting while every instance is busy.</summary>
    /// <param name="pipeName">The full pipe name.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="IOException">No background serves the name.</exception>
    public static BackgroundPipeClient Connect(string pipeName)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            var handle = NamedPipes.OpenClient(pipeName, out var error);

            try
            {
                if (!handle.IsInvalid)
                {
                    var owned = handle;

                    // The client owns it from here, and closes it on every path.
                    handle = null;

                    return new BackgroundPipeClient(owned);
                }
            }
            finally
            {
                handle?.Dispose();
            }

            // A hang detector: the listener makes its next instance right after it
            // accepts one, so a busy name frees within the moment that takes.
            if (error is not NamedPipes.ErrorPipeBusy || waited.Elapsed > TestDefaults.InProcessHang)
            {
                throw new IOException($"No background serves '{pipeName}': Win32 error {error.ToString(CultureInfo.InvariantCulture)}.");
            }

            _ = NamedPipes.WaitForFreeInstance(pipeName, (uint)TestDefaults.InProcessHang.TotalMilliseconds);
        }
    }

    /// <summary>A relay's greeting, every member the relay engine writes.</summary>
    /// <param name="build">The relay's build.</param>
    /// <param name="dataRoot">The data root it was registered for.</param>
    /// <param name="idleAt">When its countdown runs out.</param>
    /// <returns>The frame.</returns>
    public static string Hello(string build, string? dataRoot, DateTimeOffset idleAt) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = RelayWire.IdPrefix + "hello",
        ["method"] = RelayProtocol.Hello,
        ["params"] = new JsonObject
        {
            ["build"] = build,
            ["relayPid"] = 4242,
            ["clientPid"] = ClientPid,
            ["client"] = new JsonObject { ["name"] = ClientName, ["version"] = ClientVersion },
            ["reconnect"] = nameof(RelayReconnect.McpReconnect),
            ["folder"] = Folder,
            ["dataRoot"] = dataRoot,
            ["idleAt"] = RelayWire.Instant(idleAt),
        },
    }.ToJsonString();

    /// <summary>The client pid every greeting names.</summary>
    public const int ClientPid = 2424;

    /// <summary>The client every greeting names.</summary>
    public const string ClientName = "claude-code";

    /// <summary>The client's version every greeting names.</summary>
    public const string ClientVersion = "2.1.290";

    /// <summary>The folder every greeting names.</summary>
    public const string Folder = @"C:\Projects\BackgroundTests";

    /// <summary>Writes one frame.</summary>
    /// <param name="json">The frame, without its newline.</param>
    /// <returns>The write.</returns>
    public async Task SendAsync(string json)
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        await _channel.WriteFrameAsync(json, hang.Token);
    }

    /// <summary>Writes bytes as they are, with no newline: part of a frame.</summary>
    /// <param name="text">The bytes, as UTF-8 text.</param>
    /// <returns>The write.</returns>
    public async Task SendPartAsync(string text)
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        await _stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text), hang.Token);
        await _stream.FlushAsync(hang.Token);
    }

    /// <summary>Reads the next frame the background wrote.</summary>
    /// <returns>The frame.</returns>
    /// <exception cref="InvalidOperationException">The background closed the connection instead.</exception>
    public async Task<WireFrame> NextAsync()
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);
        var frame = await _channel.ReadFrameAsync(hang.Token);

        return frame is null
            ? throw new InvalidOperationException("The background closed this connection with no further frame.")
            : new WireFrame(frame);
    }

    /// <summary>Reads frames until one answers the request with this id.</summary>
    /// <param name="id">The id, as <see cref="WireFrame.IdText"/> spells it.</param>
    /// <returns>The answer, and every frame that came before it.</returns>
    public async Task<(WireFrame Answer, List<WireFrame> Before)> AnswerToAsync(string id)
    {
        var before = new List<WireFrame>();

        while (true)
        {
            var frame = await NextAsync();

            if (frame.Method is null && string.Equals(frame.IdText, id, StringComparison.Ordinal))
            {
                return (frame, before);
            }

            before.Add(frame);
        }
    }

    /// <summary>Sends a request and reads up to its answer.</summary>
    /// <param name="method">The method.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <returns>The answer.</returns>
    public async Task<WireFrame> RequestAsync(string method, JsonObject? parameters)
    {
        var id = $"suite-{Interlocked.Increment(ref _requests).ToString(CultureInfo.InvariantCulture)}";

        await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString());

        return (await AnswerToAsync(id)).Answer;
    }

    /// <summary>Asks for the tool list, as a relay's client would through it.</summary>
    /// <returns>The answer.</returns>
    public Task<WireFrame> ListAsync() => RequestAsync("tools/list", []);

    /// <summary>
    /// The handshake a relay replays for its client: <c>initialize</c>, its answer, and
    /// <c>notifications/initialized</c>.
    /// </summary>
    /// <returns>The answer to <c>initialize</c>.</returns>
    public async Task<WireFrame> InitializeAsync()
    {
        var answer = await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = TestDefaults.CallerProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = ClientName, ["version"] = ClientVersion },
        });

        await SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        return answer;
    }

    /// <summary>Whether the background has closed this connection with nothing more written.</summary>
    /// <returns><see langword="true"/> when the next read is the end of the pipe.</returns>
    public async Task<bool> ClosedAsync()
    {
        using var hang = new CancellationTokenSource(TestDefaults.InProcessHang);

        try
        {
            return await _channel.ReadFrameAsync(hang.Token) is null;
        }
        catch (IOException)
        {
            // A pipe whose server end has gone reads as broken, which is closed.
            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        _channel.Dispose();
        _stream.Dispose();
    }
}

/// <summary>
/// A background's record written by hand, in the shape the background writes it, for
/// identities no running background of this suite has.
/// </summary>
/// <remarks>
/// <b>A gone background is this process's pid with a creation time it never had</b>,
/// which is the standing shape of a pid Windows has handed to somebody else: the record
/// names a process that is not alive, and no process has to be started and ended to
/// make one.
/// </remarks>
internal static class HandWrittenRecord
{
    /// <summary>When every hand-written background started.</summary>
    public static DateTimeOffset StartedAt { get; } = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    /// <summary>Writes a record.</summary>
    /// <param name="path">The record's path.</param>
    /// <param name="processId">The pid it names.</param>
    /// <param name="createdFileTime">The creation time it names.</param>
    /// <param name="ended">How it ended cleanly, or <see langword="null"/>.</param>
    /// <param name="exitCode">The exit code a relay saw, or <see langword="null"/>.</param>
    /// <param name="exitedAt">When a relay saw it go, or <see langword="null"/>.</param>
    public static void Write(string path, int processId, long createdFileTime, string? ended = null, int? exitCode = null, DateTimeOffset? exitedAt = null)
    {
        var record = new JsonObject
        {
            ["pid"] = processId,
            ["created"] = createdFileTime,
            ["startedAt"] = StartedAt.ToString("O", CultureInfo.InvariantCulture),
            ["build"] = BackgroundServerRig.Build,
            ["image"] = @"C:\Users\someone\BrowserAI\current\BrowserAI.exe",
        };

        if (ended is not null)
        {
            record["ended"] = ended;
            record["endedAt"] = StartedAt.AddHours(1).ToString("O", CultureInfo.InvariantCulture);
        }

        if (exitCode is { } code)
        {
            record["exitCode"] = code;
        }

        if (exitedAt is { } at)
        {
            record["exitedAt"] = at.ToString("O", CultureInfo.InvariantCulture);
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, record.ToJsonString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Writes the record of a background that is gone and recorded no clean end: a crash.</summary>
    /// <param name="path">The record's path.</param>
    /// <param name="exitCode">The exit code a relay saw, or <see langword="null"/>.</param>
    /// <param name="exitedAt">When a relay saw it go, or <see langword="null"/>.</param>
    public static void Gone(string path, int? exitCode = null, DateTimeOffset? exitedAt = null) =>
        Write(path, Environment.ProcessId, ProcessLiveness.CreationTimeOfThisProcess() - 1, ended: null, exitCode, exitedAt);
}
