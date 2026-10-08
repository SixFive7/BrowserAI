// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Relay;
using BrowserAI.Updates;
using ModelContextProtocol.Protocol;

namespace BrowserAI.Background;

/// <summary>One relay connected to the background, as its greeting introduced it.</summary>
/// <param name="Id">The background's name for it: the relay's pid and the connection's number.</param>
/// <param name="RelayPid">The relay's pid.</param>
/// <param name="ClientPid">Its client's pid, or <see langword="null"/>.</param>
/// <param name="ClientName">The client's <c>clientInfo.name</c>, or <see langword="null"/>.</param>
/// <param name="ClientVersion">The client's <c>clientInfo.version</c>, or <see langword="null"/>.</param>
/// <param name="Folder">The folder the client runs in.</param>
/// <param name="Reconnect">What the client needs once an update has ended the relay, as the relay judged it.</param>
/// <param name="Conversation">Which conversation of the client the relay serves, when its greeting said; room for the measurement running on 2026-10-08.</param>
/// <param name="Label">What the person sees that conversation called, when its greeting said.</param>
internal sealed record RelayGreeting(
    string Id,
    int RelayPid,
    int? ClientPid,
    string? ClientName,
    string? ClientVersion,
    string? Folder,
    RelayReconnect Reconnect,
    string? Conversation = null,
    string? Label = null);

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
/// </remarks>
internal sealed class RelayRoster : IUpdateRelays
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private Action _changed = static () => { };
    private Action<string> _withdrew = static _ => { };

    /// <summary>Creates an empty roster.</summary>
    /// <param name="clock">The clock a relay with no countdown reads as run out against.</param>
    public RelayRoster(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
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
    public void Add(RelayGreeting greeting, RelayLink link, DateTimeOffset idleAt)
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
            if (_entries.TryGetValue(id, out var entry) && notice.IdleAt is { } idleAt)
            {
                entry.IdleAt = idleAt;
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
                    Conversation = entry.Greeting.Conversation,
                    Label = entry.Greeting.Label,
                }),
            ];
        }
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
    public void CallOff(string version)
    {
        foreach (var entry in Snapshot())
        {
            _ = entry.Link.TellAsync(RelayProtocol.CalledOff, new JsonObject { ["version"] = version }, CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task EndAllAsync(string version, bool now, CancellationToken cancellationToken)
    {
        var entries = Snapshot();

        foreach (var entry in entries)
        {
            try
            {
                await entry.Link.TellAsync(RelayProtocol.End, new JsonObject { ["now"] = now, ["version"] = version }, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // That relay has gone already, which is what the end asks of it.
            }
        }

        // Each relay answers what it holds and closes its end; the background waits
        // for that, within the caller's bound.
        await Task.WhenAll(entries.Select(static entry => entry.Link.Closed)).WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private sealed class Entry(RelayGreeting greeting, RelayLink link)
    {
        public RelayGreeting Greeting { get; } = greeting;

        public RelayLink Link { get; } = link;

        public DateTimeOffset IdleAt { get; set; }
    }
}
