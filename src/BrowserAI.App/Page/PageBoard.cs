// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.App.Page;

/// <summary>A session's browser as the page draws it.</summary>
internal enum BrowserState
{
    /// <summary>A window on the person's screen.</summary>
    Window,

    /// <summary>A browser with no window.</summary>
    Hidden,

    /// <summary>No browser is open: the folder stays.</summary>
    Closed,
}

/// <summary>One conversation: the relay its app connected, and the sessions it uses.</summary>
/// <param name="Relay">The relay, as the page's snapshot describes it.</param>
/// <param name="Sessions">The sessions its app drives now.</param>
internal sealed record Conversation(ServerEntry Relay, IReadOnlyList<SessionEntry> Sessions)
{
    /// <summary>The opaque key the page names it by: <c>c-</c> and the relay's id.</summary>
    public string Key => "c-" + Relay.Id;

    /// <summary>Its app's client name.</summary>
    public string? ClientName => Relay.Description.Client?.Name;

    /// <summary>Whether a call of its app is running now.</summary>
    public bool Working => Relay.Description.CallsInFlight > 0;

    /// <summary>The conversation as the person sees it called.</summary>
    public string Name => PageWords.ConversationOf(Relay.Conversation, ClientName, Relay.Description.WorkingDirectory);

    /// <summary>Where it runs: <i>Claude Code in VS Code</i>, <i>Claude Code in a terminal</i> or the app.</summary>
    public string Place => PageWords.PlaceOf(ClientName, Relay.Window, Relay.Reconnect);
}

/// <summary>
/// What every page draws from one sessions snapshot: the conversations, the sessions each
/// uses, the sessions no conversation uses, and the counts the home and the map show.
/// </summary>
/// <remarks>
/// <b>Each session once, under the conversation that uses it</b> (40 a: the page today
/// listed every session twice, once under the background and again under its client).
/// A session no connected conversation drives is listed apart: one its conversation left,
/// which BrowserAI keeps, and, for the moment a relay takes to be gone, one whose
/// conversation the page can no longer see.
/// </remarks>
internal sealed class PageBoard
{
    private PageBoard(ServerEntry? host, IReadOnlyList<Conversation> conversations, IReadOnlyList<SessionEntry> unlinked, IReadOnlyList<SessionEntry> all)
    {
        Host = host;
        Conversations = conversations;
        Unlinked = unlinked;
        All = all;
    }

    /// <summary>The background's own entry, or <see langword="null"/> before the first read.</summary>
    public ServerEntry? Host { get; }

    /// <summary>Every conversation, in the snapshot's order.</summary>
    public IReadOnlyList<Conversation> Conversations { get; }

    /// <summary>Every session no connected conversation drives.</summary>
    public IReadOnlyList<SessionEntry> Unlinked { get; }

    /// <summary>Every session the background holds.</summary>
    public IReadOnlyList<SessionEntry> All { get; }

    /// <summary>How many browsers are windows on the person's screen.</summary>
    public int OnScreen => All.Count(session => StateOf(session) is BrowserState.Window);

    /// <summary>How many browsers are open with no window.</summary>
    public int HiddenBrowsers => All.Count(session => StateOf(session) is BrowserState.Hidden);

    /// <summary>How many browsers are open at all.</summary>
    public int OpenBrowsers => All.Count(session => session.BrowserOpen);

    /// <summary>How many sessions BrowserAI keeps for a conversation to pick up again.</summary>
    public int Kept => Unlinked.Count;

    /// <summary>The page's board from one snapshot.</summary>
    /// <param name="snapshot">The sessions page's snapshot.</param>
    /// <returns>The board.</returns>
    public static PageBoard Of(SessionsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var host = snapshot.Servers.FirstOrDefault(server => server.IsHost);
        var all = host?.Sessions ?? [];
        var conversations = snapshot.Servers
            .Where(server => server.IsRelay)
            .Select(relay => new Conversation(relay, relay.Sessions))
            .ToList();
        var linked = conversations.SelectMany(conversation => conversation.Sessions).Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var unlinked = all.Where(session => !linked.Contains(session.Id)).ToList();

        return new PageBoard(host, conversations, unlinked, all);
    }

    /// <summary>A session's browser as the page draws it.</summary>
    /// <param name="session">The session.</param>
    /// <returns>Its state.</returns>
    public static BrowserState StateOf(SessionEntry session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return !session.BrowserOpen ? BrowserState.Closed : session.Headed ? BrowserState.Window : BrowserState.Hidden;
    }

    /// <summary>The conversation that uses a session, or <see langword="null"/>.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The conversation.</returns>
    public Conversation? UsedBy(SessionEntry session) =>
        Conversations.FirstOrDefault(conversation => conversation.Sessions.Any(each => string.Equals(each.Id, session.Id, StringComparison.Ordinal)));

    /// <summary>The held session an opaque id names, or <see langword="null"/>.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The session.</returns>
    public SessionEntry? SessionById(string? id) =>
        id is null ? null : All.FirstOrDefault(session => string.Equals(session.Id, id, StringComparison.Ordinal));
}
