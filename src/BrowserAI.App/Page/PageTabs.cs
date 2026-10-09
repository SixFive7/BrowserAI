// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Threading.Channels;

namespace BrowserAI.App.Page;

/// <summary>Which page a tab is showing.</summary>
internal enum PageKind
{
    /// <summary>The status page: the version, the update and where things are.</summary>
    Status,

    /// <summary>BrowserAI's own sessions page.</summary>
    Sessions,

    /// <summary>
    /// The update page: what holds a downloaded update, and the install button. The
    /// ready toast's <i>Install now</i> opens it (T, 2026-10-08).
    /// </summary>
    Update,

    /// <summary>
    /// The changelog page: the section of the changelog shipped in the build for the
    /// installed version. The installed toast's <i>Changelog</i> opens it.
    /// </summary>
    Changelog,
}

/// <summary>Each page's name, which is its route and what its tab's stream says it shows.</summary>
internal static class PageNames
{
    /// <summary>The page's name: <c>status</c>, <c>sessions</c>, <c>update</c> or <c>changelog</c>.</summary>
    /// <param name="kind">The page.</param>
    /// <returns>Its name.</returns>
    public static string Of(PageKind kind) => kind switch
    {
        PageKind.Sessions => "sessions",
        PageKind.Update => "update",
        PageKind.Changelog => "changelog",
        _ => "status",
    };

    /// <summary>The page's route under the listener's root: the status page is the root itself.</summary>
    /// <param name="kind">The page.</param>
    /// <returns>The route.</returns>
    public static string RouteOf(PageKind kind) => kind is PageKind.Status ? string.Empty : Of(kind);

    /// <summary>The page a name names; anything else is the status page.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The page.</returns>
    public static PageKind Parse(string? name) => name switch
    {
        "sessions" => PageKind.Sessions,
        "update" => PageKind.Update,
        "changelog" => PageKind.Changelog,
        _ => PageKind.Status,
    };
}

/// <summary>One event for one tab's stream.</summary>
/// <param name="Name">The event's name: <c>state</c>, <c>superseded</c> or <c>closing</c>.</param>
/// <param name="Data">The event's data, one line of JSON.</param>
internal sealed record PageEvent(string Name, string Data);

/// <summary>One open event stream: one tab, as the listener sees it.</summary>
/// <param name="tab">The tab's number, as its address carried it.</param>
/// <param name="page">Which page it shows.</param>
internal sealed class TabStream(int tab, PageKind page)
{
    private readonly Channel<PageEvent> _events = Channel.CreateUnbounded<PageEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    /// <summary>The tab's number. Zero for an address that carried none.</summary>
    public int Tab { get; } = tab;

    /// <summary>Which page it shows.</summary>
    public PageKind Page { get; } = page;

    /// <summary>Whether a newer tab has replaced it. A replaced tab no longer counts as connected.</summary>
    public bool Superseded { get; private set; }

    /// <summary>What the listener writes to the tab, in order.</summary>
    public ChannelReader<PageEvent> Events => _events.Reader;

    /// <summary>Queues one event.</summary>
    /// <param name="event">The event.</param>
    public void Send(PageEvent @event) => _ = _events.Writer.TryWrite(@event);

    /// <summary>Tells the tab it was replaced, and ends its stream after that.</summary>
    internal void Supersede()
    {
        if (Superseded)
        {
            return;
        }

        Superseded = true;
        Send(new PageEvent(PageEvents.Superseded, "{}"));
        _ = _events.Writer.TryComplete();
    }

    /// <summary>Tells the tab something final, and ends its stream after that.</summary>
    /// <param name="data">The event's data.</param>
    internal void Close(string data)
    {
        Send(new PageEvent(PageEvents.Closing, data));
        _ = _events.Writer.TryComplete();
    }
}

/// <summary>The three event names a tab's stream carries.</summary>
internal static class PageEvents
{
    /// <summary>What the page shows now, as HTML.</summary>
    public const string State = "state";

    /// <summary>A newer tab has replaced this one.</summary>
    public const string Superseded = "superseded";

    /// <summary>The coordinator is going, and this is the last thing it says.</summary>
    public const string Closing = "closing";
}

/// <summary>
/// The tabs the coordinator has handed out and the ones connected now: who is
/// newest, who is still there, and when the last one left.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q336 a, the maintainer's words verbatim: <i>"Q336 a - also make sure that an
/// exist only happens after 1 min. of a tab closed so a reload keeps working
/// (because that does not take 1 min.)"</i>.</b> A tab is connected while its event
/// stream is open, and the kernel ends the stream when the tab, the browser or the
/// connection goes, so no ping is needed. The coordinator may stop once no tab is
/// connected and <see cref="Linger"/> has passed since the last one left or since an
/// address was last handed out, whichever is later. A reload left no page connected
/// for 5 to 135 ms in the 2026-10-01 measurements, so a minute covers it.
/// </para>
/// <para>
/// <b>Q337 a, the maintainer's words verbatim: <i>"Q337 a"</i>: the newest tab
/// wins.</b> Every address handed out carries the next tab number. When a tab
/// connects with a number above every one seen so far, every older tab still
/// connected is told it was replaced and closes itself; a tab that connects with a
/// lower number is told so at once. A reload keeps its address, so it keeps its
/// number. The replacement happens when the newer tab arrives and not when its
/// address is handed out, so a browser that never opened the new tab leaves the old
/// one working.
/// </para>
/// <para>
/// <b>The linger is the coordinator's first timer</b>, and it is armed on the
/// <see cref="TimeProvider"/> this is given, so the suite drives it with a clock it
/// moves and never by letting a minute pass. All it does is wake the coordinator;
/// the decision to stop is <see cref="TryClose"/>'s, taken on the coordinator's own
/// thread under the same lock a hand-out takes.
/// </para>
/// </remarks>
internal sealed class PageTabs : IDisposable
{
    /// <summary>How long the coordinator waits after the last tab leaves: one minute, Q336 a.</summary>
    public static readonly TimeSpan ProductLinger = ProcessBounds.PageTabsLinger;

    private readonly Lock _gate = new();
    private readonly List<TabStream> _streams = [];
    private readonly Action _changed;
    private readonly TimeProvider _clock;
    private ITimer? _timer;
    private long _lastActivity;
    private int _handedOut;
    private int _newest;
    private bool _closing;

    /// <summary>Tabs for one listener.</summary>
    /// <param name="clock">The clock the linger runs on.</param>
    /// <param name="linger">How long to wait after the last tab leaves.</param>
    /// <param name="changed">
    /// Called whenever a tab arrives or leaves, an address is handed out, or the
    /// linger runs out. The coordinator passes its inbox's wake, so it has one
    /// thing to wait on and not two.
    /// </param>
    public PageTabs(TimeProvider clock, TimeSpan linger, Action changed)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(changed);

        _clock = clock;
        _changed = changed;
        Linger = linger;
        _lastActivity = clock.GetTimestamp();
    }

    /// <summary>How long the coordinator waits after the last tab leaves.</summary>
    public TimeSpan Linger { get; }

    /// <summary>How many tabs are connected and not replaced.</summary>
    public int Connected
    {
        get
        {
            lock (_gate)
            {
                return _streams.Count(stream => !stream.Superseded);
            }
        }
    }

    /// <summary>The newest tab number handed out, zero before the first.</summary>
    public int HandedOut
    {
        get
        {
            lock (_gate)
            {
                return _handedOut;
            }
        }
    }

    /// <summary>Whether the coordinator has decided to stop, after which nothing is handed out.</summary>
    public bool Closing
    {
        get
        {
            lock (_gate)
            {
                return _closing;
            }
        }
    }

    /// <summary>Every connected tab that is not replaced, newest last.</summary>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<TabStream> Streams()
    {
        lock (_gate)
        {
            return [.. _streams.Where(stream => !stream.Superseded).OrderBy(stream => stream.Tab)];
        }
    }

    /// <summary>Takes the next tab number for an address about to be handed out.</summary>
    /// <returns>The number, or <see langword="null"/> once the coordinator is stopping.</returns>
    public int? HandOut()
    {
        int tab;

        lock (_gate)
        {
            if (_closing)
            {
                return null;
            }

            tab = ++_handedOut;
            _lastActivity = _clock.GetTimestamp();
            ArmLocked();
        }

        _changed();
        return tab;
    }

    /// <summary>Registers one tab's stream as it opens.</summary>
    /// <param name="tab">The number its address carried, zero for none.</param>
    /// <param name="page">Which page it shows.</param>
    /// <returns>The stream, or <see langword="null"/> once the coordinator is stopping.</returns>
    public TabStream? Connect(int tab, PageKind page)
    {
        var stream = new TabStream(tab, page);
        List<TabStream> replaced = [];

        lock (_gate)
        {
            if (_closing)
            {
                return null;
            }

            _streams.Add(stream);

            if (tab > _newest)
            {
                _newest = tab;
                replaced.AddRange(_streams.Where(other => other.Tab < tab && !other.Superseded));
            }
            else if (tab < _newest)
            {
                replaced.Add(stream);
            }

            foreach (var old in replaced)
            {
                old.Supersede();
            }
        }

        _changed();
        return stream;
    }

    /// <summary>Takes a tab's stream out as it closes.</summary>
    /// <param name="stream">The stream.</param>
    public void Disconnect(TabStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        lock (_gate)
        {
            if (!_streams.Remove(stream))
            {
                return;
            }

            if (_streams.All(other => other.Superseded))
            {
                _lastActivity = _clock.GetTimestamp();
                ArmLocked();
            }
        }

        _changed();
    }

    /// <summary>Queues one event on every connected tab that is not replaced.</summary>
    /// <param name="render">The event for one tab, or <see langword="null"/> for none.</param>
    public void Broadcast(Func<TabStream, PageEvent?> render)
    {
        ArgumentNullException.ThrowIfNull(render);

        foreach (var stream in Streams())
        {
            if (render(stream) is { } @event)
            {
                stream.Send(@event);
            }
        }
    }

    /// <summary>Says one last thing to every tab and ends their streams.</summary>
    /// <param name="data">The <c>closing</c> event's data.</param>
    public void CloseAll(string data)
    {
        foreach (var stream in Streams())
        {
            stream.Close(data);
        }
    }

    /// <summary>
    /// Decides, under the hand-out's own lock, that the coordinator may stop: no
    /// tab is connected and the linger has run out.
    /// </summary>
    /// <returns>Whether it may. Once this has said yes, <see cref="HandOut"/> refuses.</returns>
    public bool TryClose()
    {
        lock (_gate)
        {
            if (_closing)
            {
                return true;
            }

            if (_streams.Any(stream => !stream.Superseded))
            {
                return false;
            }

            if (_clock.GetElapsedTime(_lastActivity) < Linger)
            {
                ArmLocked();
                return false;
            }

            _closing = true;
            return true;
        }
    }

    /// <summary>Whether no tab is connected and the linger has run out, without deciding anything.</summary>
    /// <returns>Whether the tabs are idle.</returns>
    public bool IsIdle()
    {
        lock (_gate)
        {
            return !_streams.Any(stream => !stream.Superseded) && _clock.GetElapsedTime(_lastActivity) >= Linger;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Arms the linger timer for what is left of the linger. Called under the lock.</summary>
    private void ArmLocked()
    {
        var left = Linger - _clock.GetElapsedTime(_lastActivity);

        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        if (_timer is null)
        {
            _timer = _clock.CreateTimer(static state => ((PageTabs)state!).Expired(), this, left, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _ = _timer.Change(left, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>The linger ran out: wake whoever waits, and let them decide.</summary>
    private void Expired() => _changed();
}
