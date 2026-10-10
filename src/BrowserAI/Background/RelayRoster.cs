// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Clients;
using BrowserAI.Relay;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Background;

/// <summary>What the roster asks of one relay's connection.</summary>
/// <remarks>
/// <b>Added 2026-10-10</b> so the roster's ends can be held against a connection that
/// fails in a way a pipe of the in-process rig never does: <see cref="RelayLink"/> in
/// the product, a double in the suite.
/// </remarks>
internal interface IRelayLink
{
    /// <summary>Whether a call of the client's is in flight.</summary>
    bool CallInFlight { get; }

    /// <summary>The connection's end: the relay has gone.</summary>
    Task Closed { get; }

    /// <summary>Asks the relay something of BrowserAI's own and waits for its answer.</summary>
    /// <param name="method">The method.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>The relay's answer.</returns>
    Task<JsonRpcMessage> AskAsync(string method, JsonNode? parameters, CancellationToken cancellationToken);

    /// <summary>Tells the relay something of BrowserAI's own, expecting no answer.</summary>
    /// <param name="method">The method.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="cancellationToken">Ends the send.</param>
    /// <returns>A task that completes once the frame is written.</returns>
    Task TellAsync(string method, JsonNode? parameters, CancellationToken cancellationToken);
}

/// <summary>One relay connected to the background, as its greeting introduced it.</summary>
/// <param name="Id">The background's name for it: the relay's pid and the connection's number.</param>
/// <param name="RelayPid">The relay's pid.</param>
/// <param name="ClientPid">Its client's pid, or <see langword="null"/>.</param>
/// <param name="ClientName">The client's <c>clientInfo.name</c>, or <see langword="null"/>.</param>
/// <param name="ClientVersion">The client's <c>clientInfo.version</c>, or <see langword="null"/>.</param>
/// <param name="Folder">The folder the client runs in.</param>
/// <param name="Reconnect">What the client needs once an update has ended the relay, as the relay judged it.</param>
/// <param name="Conversation">
/// Where the relay said its client keeps the conversation it serves, or
/// <see langword="null"/>. <i>Corrected 2026-10-10 (previously "Which conversation of the
/// client the relay serves, when its greeting said; room for the measurement running on
/// 2026-10-08")</i>: the measurement found the conversation moves at <c>/clear</c> with no
/// message to the relay, so the greeting carries where to read it and the background
/// reads it when it draws.
/// </param>
/// <param name="Window">
/// The VS Code window the client is a tab of, as the relay read it, or <see langword="null"/>.
/// <i>Replaced 2026-10-10 (previously a <c>Label</c>, "What the person sees that
/// conversation called, when its greeting said")</i>, for the reason above.
/// </param>
internal sealed record RelayGreeting(
    string Id,
    int RelayPid,
    int? ClientPid,
    string? ClientName,
    string? ClientVersion,
    string? Folder,
    RelayReconnect Reconnect,
    ConversationFacts? Conversation = null,
    string? Window = null);

/// <summary>One relay as a reader of the roster draws it: its greeting, its countdown, its call, and its conversation as read now.</summary>
/// <param name="Greeting">What the relay said about itself.</param>
/// <param name="IdleAt">When its countdown runs out.</param>
/// <param name="CallInFlight">Whether a call of its client is running.</param>
/// <param name="Reading">Which conversation it serves and what the person sees it called, read at this moment.</param>
internal sealed record NamedRelay(RelayGreeting Greeting, DateTimeOffset IdleAt, bool CallInFlight, ConversationReading Reading)
{
    /// <summary>The VS Code window its client is a tab of, or <see langword="null"/>.</summary>
    public ClientWindow? Window => Greeting.Window is { Length: > 0 } key ? new ClientWindow(key, Greeting.Folder) : null;
}

/// <summary>
/// Every relay connected to the background, with what the update needs to know of
/// each: its countdown, whether a call is in flight, and how to ask it to end.
/// </summary>
/// <remarks>
/// <para>
/// <b>H1 and RESOLUTIONS 13</b>: a relay holds an update while its countdown runs or a
/// call is in flight, and the background asks every relay whether it may end before it
/// installs. This is the update core's <see cref="IUpdateRelays"/> over the real
/// connections: each question is one <c>browserai/ready-to-end</c> request on the
/// relay's own pipe, and the answer is the relay's, read here and never judged.
/// </para>
/// <para>
/// <b>Every change is told to the update core at once</b>, through the callback, which
/// records and returns: a relay connecting or going, a countdown moving, a call
/// starting or ending.
/// </para>
/// <para>
/// <b>Which conversation each relay serves is read when somebody draws it, and only
/// then</b> (1.3 c, the maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept
/// all your recommendations"</i>): <see cref="ConnectedWithNames"/>, which the snapshot
/// the dashboard and the toast draw is made from, and <see cref="Named"/>, which the
/// sessions page reads, each ask the <see cref="ConversationReader"/> for every relay, out
/// of the lock. <see cref="Connected"/>, which the update core decides by, reads no file.
/// The log says where a relay's conversation and its name were found each time that
/// moves, and never what they are.
/// </para>
/// </remarks>
internal sealed class RelayRoster : IUpdateRelays
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly ConversationReader _reader;
    private readonly ILogger _logger;
    private Action _changed = static () => { };
    private Action<string> _withdrew = static _ => { };

    /// <summary>Creates an empty roster.</summary>
    /// <param name="clock">The clock a relay with no countdown reads as run out against.</param>
    /// <param name="reader">What reads each relay's conversation from its client's records, or <see langword="null"/> for the files themselves.</param>
    /// <param name="logger">Where the roster says where a conversation was found, or <see langword="null"/> for nowhere.</param>
    public RelayRoster(TimeProvider clock, ConversationReader? reader = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _reader = reader ?? ConversationReader.Files;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>How many relays are connected.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Wires the roster to the update core, once both exist.</summary>
    /// <param name="changed">Told of every change.</param>
    /// <param name="withdrew">Told of a relay that withdrew its yes.</param>
    public void TellUpdatesThrough(Action changed, Action<string> withdrew)
    {
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(withdrew);

        _changed = changed;
        _withdrew = withdrew;
    }

    /// <summary>Adds a relay whose greeting the background accepted.</summary>
    /// <param name="greeting">What the relay said about itself.</param>
    /// <param name="link">Its connection.</param>
    /// <param name="idleAt">When its countdown runs out, from its greeting.</param>
    public void Add(RelayGreeting greeting, IRelayLink link, DateTimeOffset idleAt)
    {
        ArgumentNullException.ThrowIfNull(greeting);
        ArgumentNullException.ThrowIfNull(link);

        lock (_gate)
        {
            _entries[greeting.Id] = new Entry(greeting, link) { IdleAt = idleAt };
        }

        _changed();
    }

    /// <summary>Removes a relay whose connection has closed.</summary>
    /// <param name="id">Its id.</param>
    public void Remove(string id)
    {
        lock (_gate)
        {
            _ = _entries.Remove(id);
        }

        _changed();
    }

    /// <summary>What a relay's connection reported.</summary>
    /// <param name="id">The relay.</param>
    /// <param name="notice">What it reported.</param>
    public void Heard(string id, RelayNotice notice)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                if (notice.IdleAt is { } idleAt)
                {
                    entry.IdleAt = idleAt;
                }

                // 1.4 a: the thread of the first call that names one, kept for the
                // relay's life, since one Codex server serves one thread.
                if (notice.Kind is RelayNoticeKind.Conversation && notice.Thread is { Length: > 0 } thread)
                {
                    entry.ThreadId ??= thread;
                }
            }
        }

        if (notice.Kind is RelayNoticeKind.Withdrew)
        {
            _withdrew(id);
        }

        _changed();
    }

    /// <summary>Every relay, for the dashboard: who it is and how long it holds the update.</summary>
    /// <returns>The relays, in no particular order.</returns>
    public IReadOnlyList<(RelayGreeting Greeting, DateTimeOffset IdleAt, bool CallInFlight)> Read()
    {
        lock (_gate)
        {
            return [.. _entries.Values.Select(static entry => (entry.Greeting, entry.IdleAt, entry.Link.CallInFlight))];
        }
    }

    /// <inheritdoc />
    /// <remarks>Reads no file: the update core decides by this, and a conversation's name decides nothing.</remarks>
    public IReadOnlyList<RelayState> Connected()
    {
        lock (_gate)
        {
            return
            [
                .. _entries.Values.Select(static entry => new RelayState(
                    entry.Greeting.Id,
                    entry.Greeting.ClientName,
                    entry.Greeting.ClientVersion,
                    entry.Greeting.Folder,
                    entry.IdleAt,
                    entry.Link.CallInFlight)
                {
                    Reconnect = entry.Greeting.Reconnect,
                }),
            ];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RelayState> ConnectedWithNames() =>
    [
        .. Named().Select(static relay => new RelayState(
            relay.Greeting.Id,
            relay.Greeting.ClientName,
            relay.Greeting.ClientVersion,
            relay.Greeting.Folder,
            relay.IdleAt,
            relay.CallInFlight)
        {
            Reconnect = relay.Greeting.Reconnect,
            Conversation = relay.Reading.Conversation,
            Label = relay.Reading.Name,
            Window = relay.Window,
        }),
    ];

    /// <summary>
    /// Every relay, each with its conversation and what the person sees it called, read
    /// from its client's records now: what the dashboard and the toast draw.
    /// </summary>
    /// <remarks>
    /// <b>The reads run out of the lock</b>, so a slow file never holds up a relay that
    /// connects, reports or goes meanwhile; a relay that went during the read is drawn one
    /// last time.
    /// </remarks>
    /// <returns>The relays, in no particular order.</returns>
    public IReadOnlyList<NamedRelay> Named()
    {
        List<(RelayGreeting Greeting, DateTimeOffset IdleAt, bool CallInFlight, string? ThreadId, ConversationMemo Memo)> entries;

        lock (_gate)
        {
            entries = [.. _entries.Values.Select(static entry => (entry.Greeting, entry.IdleAt, entry.Link.CallInFlight, entry.ThreadId, entry.Memo))];
        }

        var named = new List<NamedRelay>(entries.Count);

        foreach (var (greeting, idleAt, callInFlight, threadId, memo) in entries)
        {
            var reading = _reader.Read(greeting.ClientName, greeting.Conversation, greeting.ClientPid, threadId, greeting.Folder, memo);

            // Where it was found, each time that moves; never what it is.
            if (reading.Name is not null && memo.Moved(reading.Source, reading.NameSource))
            {
                RelayRosterLog.ConversationFound(_logger, greeting.Id, reading.Source, reading.NameSource);
            }

            named.Add(new NamedRelay(greeting, idleAt, callInFlight, reading));
        }

        return named;
    }

    /// <inheritdoc />
    public async Task<RelayReadiness> AskReadyToEndAsync(string relay, string version, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || Find(relay) is not { } entry)
        {
            return new RelayReadiness(false, _clock.GetUtcNow(), CallInFlight: false);
        }

        var answer = await entry.Link.AskAsync(
            RelayProtocol.ReadyToEnd,
            new JsonObject { ["version"] = version },
            cancellationToken).ConfigureAwait(false);

        if (answer is not JsonRpcResponse { Result: { } result })
        {
            return new RelayReadiness(false, entry.IdleAt, entry.Link.CallInFlight);
        }

        var ready = result["ready"] is JsonValue flag && flag.TryGetValue<bool>(out var yes) && yes;
        var idleAt = result["idleAt"] is JsonValue at
            && at.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : entry.IdleAt;
        var callInFlight = result["callInFlight"] is JsonValue busy && busy.TryGetValue<bool>(out var inFlight) && inFlight;

        return new RelayReadiness(ready, idleAt, callInFlight);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ <b>Every relay's notice is watched to its end</b>, and one that fails is a
    /// record naming the relay. <i>Corrected 2026-10-10 (previously each task was
    /// discarded, so a failure was never seen by anybody), found by lane ARCH's helper
    /// T1 reading the code on 2026-10-09.</i>
    /// </remarks>
    public void CallOff(string version)
    {
        foreach (var entry in Snapshot())
        {
            _ = TellAsync(entry, RelayProtocol.CalledOff, new JsonObject { ["version"] = version }, CancellationToken.None);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ <b>A relay whose end fails costs that relay alone</b>: every relay after it is
    /// still told, and a connection that faults as it goes is waited for as gone.
    /// <i>Corrected 2026-10-10 (previously only an <see cref="IOException"/> was caught,
    /// so a link already disposed stopped the loop and every relay after it ended only
    /// with the background), found by lane ARCH's helper T1 reading the code on
    /// 2026-10-09 and held by
    /// <c>BackgroundServerTests.ARelayWhoseLinkFailsKeepsNoOtherFromBeingToldToEnd</c>.</i>
    /// The caller's own token is the one thing that ends the loop early: its bound has
    /// run out, and the install goes ahead without the rest.
    /// </remarks>
    public async Task EndAllAsync(string version, bool now, CancellationToken cancellationToken)
    {
        var entries = Snapshot();

        foreach (var entry in entries)
        {
            await TellAsync(entry, RelayProtocol.End, new JsonObject { ["now"] = now, ["version"] = version }, cancellationToken).ConfigureAwait(false);
        }

        // Each relay answers what it holds and closes its end; the background waits
        // for that, within the caller's bound.
        await Task.WhenAll(entries.Select(Gone)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tells one relay something, and records it when that fails for any reason but its having gone.</summary>
    /// <param name="entry">The relay.</param>
    /// <param name="method">The method.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="cancellationToken">The caller's bound, which alone ends the call with an exception.</param>
    /// <returns>A task that completes once the frame is written or the relay could not be told.</returns>
    private async Task TellAsync(Entry entry, string method, JsonNode parameters, CancellationToken cancellationToken)
    {
        try
        {
            await entry.Link.TellAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            // That relay has gone already, which is what an end asks of it and what a
            // call-off no longer needs to reach.
        }
#pragma warning disable CA1031 // One relay that cannot be told costs that relay alone, never the relays after it.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            RelayRosterLog.NotTold(_logger, entry.Greeting.Id, method, failure);
        }
    }

    /// <summary>One relay's connection, waited for as gone whether it closed or faulted.</summary>
    /// <param name="entry">The relay.</param>
    /// <returns>A task that completes once its connection has ended, and never faults.</returns>
    private async Task Gone(Entry entry)
    {
        try
        {
            await entry.Link.Closed.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A connection that faulted as it went has gone all the same.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            RelayRosterLog.EndedWithAFault(_logger, entry.Greeting.Id, failure);
        }
    }

    private Entry? Find(string id)
    {
        lock (_gate)
        {
            return _entries.GetValueOrDefault(id);
        }
    }

    private List<Entry> Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries.Values];
        }
    }

    private sealed class Entry(RelayGreeting greeting, IRelayLink link)
    {
        public RelayGreeting Greeting { get; } = greeting;

        public IRelayLink Link { get; } = link;

        public DateTimeOffset IdleAt { get; set; }

        /// <summary>The thread of Codex's first call that named one, or <see langword="null"/>.</summary>
        public string? ThreadId { get; set; }

        /// <summary>What the reader keeps of this relay between two draws.</summary>
        public ConversationMemo Memo { get; } = new();
    }
}

/// <summary>Source-generated log messages for <see cref="RelayRoster"/>.</summary>
internal static partial class RelayRosterLog
{
    /// <summary>Where a relay's conversation and its name were found, never what they are.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="relay">The relay.</param>
    /// <param name="source">Where the conversation was found.</param>
    /// <param name="name">Where its name was found.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Relay {Relay}'s conversation was found by {Source} and named by {Name}.")]
    public static partial void ConversationFound(ILogger logger, string relay, ConversationSource source, NameSource name);

    [LoggerMessage(EventId = 31, Level = LogLevel.Warning, Message = "Relay {Relay} could not be told '{Method}'; every other relay is still told.")]
    public static partial void NotTold(ILogger logger, string relay, string method, Exception failure);

    [LoggerMessage(EventId = 32, Level = LogLevel.Warning, Message = "Relay {Relay}'s connection ended with a fault; it has gone all the same.")]
    public static partial void EndedWithAFault(ILogger logger, string relay, Exception failure);
}
