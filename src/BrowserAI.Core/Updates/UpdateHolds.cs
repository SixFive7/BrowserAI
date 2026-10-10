// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Updates;

/// <summary>Where a downloaded update stands.</summary>
internal enum UpdateHoldState
{
    /// <summary>No update is downloaded and waiting.</summary>
    None,

    /// <summary>
    /// An update is downloaded and waits: something still uses BrowserAI, or the
    /// background is asking its relays whether they may end.
    /// </summary>
    Held,

    /// <summary>Every relay agreed, and the background is handing over to the installer.</summary>
    Installing,
}

/// <summary>What one client needs once an update has ended its relay.</summary>
/// <remarks>
/// <b>H1-T = a, the maintainer's answer of 2026-10-08:</b> the toast and the
/// dashboard name the sessions that will need a reconnect before the update
/// installs. Measured 2026-10-03 at Claude Code 2.1.288
/// ([kb](../../../kb/mcp/protocol.md#claude-code-re-launches-a-dead-server-transparently-and-never-re-lists-its-tools)):
/// the VS Code extension and <c>claude -p</c> start a dead stdio server again at
/// their next call, the terminal UI never does, and Codex never does on the
/// failure path.
/// </remarks>
internal enum RelayReconnect
{
    /// <summary>Nothing: the client starts BrowserAI again at its next call, as VS Code's Claude Code and <c>claude -p</c> do.</summary>
    None,

    /// <summary>Claude Code in a terminal: the person runs <c>/mcp</c>, picks BrowserAI and chooses Reconnect.</summary>
    McpReconnect,

    /// <summary>Codex: BrowserAI comes back only in a new conversation.</summary>
    NewConversation,

    /// <summary>The client did not say enough to tell which of the three it is.</summary>
    Unknown,
}

/// <summary>One browser session that holds the update: a hidden session, or a visible window.</summary>
/// <param name="Directory">The session directory.</param>
/// <param name="Purpose">What its record says it is for, or <see langword="null"/> when it says nothing.</param>
/// <param name="ClosesAt">
/// When its idle countdown closes it if no call or input comes first, or
/// <see langword="null"/> when an agent set it never to close for idleness.
/// </param>
internal sealed record HoldingSession(string Directory, string? Purpose, DateTimeOffset? ClosesAt);

/// <summary>One relay: one client's connection to BrowserAI.</summary>
/// <param name="Client">What the client called itself, with its version when it gave one.</param>
/// <param name="ProjectFolder">The folder the client started BrowserAI in, or <see langword="null"/> when it is not known.</param>
/// <param name="IdleAt">
/// When its fixed ten-minute activity countdown runs out. From then on the relay
/// holds the update only while <paramref name="CallInFlight"/> is set; it still
/// ends when the update installs.
/// </param>
/// <param name="CallInFlight">Whether a call from its client is running now.</param>
/// <param name="Reconnect">What its client needs once the update has ended it.</param>
/// <param name="Conversation">
/// Which conversation of its client the relay serves, as the background read it when
/// the snapshot was taken, or <see langword="null"/> when it could not: Claude Code's
/// session id or Codex's thread id. <i>Added 2026-10-10 by addition: room was left on
/// 2026-10-08 for the maintainer's ask to tell several conversations of one client
/// apart, and his answer of 2026-10-10 filled it.</i> No page shows it.
/// </param>
/// <param name="Label">
/// What the person sees that conversation called, read when the snapshot was taken, or
/// <see langword="null"/> for a client whose records BrowserAI does not read.
/// </param>
/// <param name="Window">The VS Code window its client is a tab of, or <see langword="null"/> for any other client.</param>
internal sealed record HoldingRelay(
    string Client,
    string? ProjectFolder,
    DateTimeOffset IdleAt,
    bool CallInFlight,
    RelayReconnect Reconnect,
    string? Conversation = null,
    ConversationName? Label = null,
    ClientWindow? Window = null);

/// <summary>
/// The downloaded update, and everything that holds it back, read at one moment.
/// </summary>
/// <remarks>
/// <para>
/// <b>The contract between the background that holds the update and the two
/// things that show it</b>, the update toast and the dashboard's update page.
/// The background produces it through <see cref="IUpdateHolds"/>; neither reader
/// asks anything else.
/// </para>
/// <para>
/// <b>Three kinds of thing hold an update, each with a countdown that activity
/// restarts</b> (H1, decided 2026-10-08): hidden browser sessions, ten minutes
/// after the last call naming them; visible windows, an hour after the last call
/// or the person's last input in them, both changeable by the agent; and relays,
/// ten minutes after their client's last message other than <c>ping</c>, fixed.
/// A browser whose countdown ends closes. A relay whose countdown ends is not
/// ended: it stops holding the update, and only the two-phase agreement ends it.
/// </para>
/// <para>
/// <b>Every relay is listed, holding or not</b>, because every relay ends when
/// the update installs, and the person is told before it does which of them
/// will need a reconnect (H1-T a).
/// </para>
/// <para>
/// <b>Deadlines, never remaining times</b>: each countdown is the moment it runs
/// out, so a reader computes what is left against its own clock and a
/// snapshot read a second ago is still right a second later.
/// </para>
/// </remarks>
/// <param name="ReadAt">When it was read.</param>
/// <param name="State">Where the update stands.</param>
/// <param name="Version">The version downloaded and waiting, or <see langword="null"/> when <paramref name="State"/> is <see cref="UpdateHoldState.None"/>.</param>
/// <param name="HiddenSessions">Every open session with no window.</param>
/// <param name="VisibleWindows">Every open session with a window, which waits for the person to close it or for its countdown.</param>
/// <param name="Relays">Every connected relay, holding or not.</param>
/// <param name="Older">
/// Whether the version waiting is older than the one installed: a rollback, which the
/// update page and the ready toast say (Q308 a, built again 2026-10-10).
/// </param>
internal sealed record UpdateHoldSnapshot(
    DateTimeOffset ReadAt,
    UpdateHoldState State,
    string? Version,
    IReadOnlyList<HoldingSession> HiddenSessions,
    IReadOnlyList<HoldingSession> VisibleWindows,
    IReadOnlyList<HoldingRelay> Relays,
    bool Older = false)
{
    /// <summary>The version downloaded and waiting, or <see langword="null"/> when nothing is.</summary>
    /// <remarks>
    /// <b>A held or installing update always names its version</b>, so no reader has to
    /// word one that does not: the background builds those two states only from the
    /// package it holds, whose version is required. <i>Added 2026-10-10, with #62, #64
    /// and #94 of the texts review, which found the page wording "the new version" for a
    /// case that could not arise.</i>
    /// </remarks>
    public string? Version { get; init; } = State is UpdateHoldState.None || Version is { Length: > 0 }
        ? Version
        : throw new ArgumentException("A held or installing update names its version.", nameof(Version));

    /// <summary>No update is waiting.</summary>
    /// <param name="readAt">When that was read.</param>
    /// <returns>The snapshot.</returns>
    public static UpdateHoldSnapshot Nothing(DateTimeOffset readAt) => new(readAt, UpdateHoldState.None, null, [], [], []);

    /// <summary>The relays that hold the update at one moment: their countdown is still running, or a call is.</summary>
    /// <param name="now">The moment.</param>
    /// <returns>Those relays, in the snapshot's order.</returns>
    public IEnumerable<HoldingRelay> HoldingRelaysAt(DateTimeOffset now) =>
        Relays.Where(relay => relay.CallInFlight || relay.IdleAt > now);

    /// <summary>
    /// How the wait stands at one moment, and when it ends if nothing uses
    /// BrowserAI until then.
    /// </summary>
    /// <param name="now">The moment.</param>
    /// <returns>The reading.</returns>
    /// <remarks>
    /// <para>
    /// <b>A holder with no countdown decides first</b>: a window an agent set never
    /// to close holds the update until the person closes it, and a hidden session set
    /// so holds it until an agent closes it, whatever every other countdown says.
    /// </para>
    /// <para>
    /// <b>Otherwise the wait is the last deadline still ahead</b>, among every listed
    /// session and every relay that holds the update now. A session whose countdown
    /// has run out is closing and still holds it while it is listed; a relay whose
    /// countdown has run out holds it only while a call runs.
    /// </para>
    /// </remarks>
    public UpdateWaitReading WaitAt(DateTimeOffset now)
    {
        if (VisibleWindows.Any(window => window.ClosesAt is null))
        {
            return new UpdateWaitReading(UpdateWait.WindowNeverCloses, null);
        }

        if (HiddenSessions.Any(session => session.ClosesAt is null))
        {
            return new UpdateWaitReading(UpdateWait.SessionNeverCloses, null);
        }

        var holding = HoldingRelaysAt(now).ToList();
        var deadlines = HiddenSessions.Concat(VisibleWindows)
            .Select(session => session.ClosesAt!.Value)
            .Concat(holding.Select(relay => relay.IdleAt))
            .ToList();

        if (deadlines.Count > 0 && deadlines.Max() is var last && last > now)
        {
            return new UpdateWaitReading(UpdateWait.Counting, last);
        }

        if (holding.Any(relay => relay.CallInFlight))
        {
            return new UpdateWaitReading(UpdateWait.CallRunning, null);
        }

        return HiddenSessions.Count + VisibleWindows.Count > 0
            ? new UpdateWaitReading(UpdateWait.Closing, null)
            : new UpdateWaitReading(UpdateWait.NothingHolds, null);
    }
}

/// <summary>How the wait for a downloaded update stands at one moment.</summary>
internal enum UpdateWait
{
    /// <summary>Every holder has a countdown, and the last of them is still running.</summary>
    Counting,

    /// <summary>A visible window an agent set never to close holds it, so it waits for the person to close the window.</summary>
    WindowNeverCloses,

    /// <summary>A hidden session an agent set never to close holds it, so it waits for an agent to close the session.</summary>
    SessionNeverCloses,

    /// <summary>Every countdown has run out, and a relay is still answering a call.</summary>
    CallRunning,

    /// <summary>Every countdown has run out, and a session is still closing.</summary>
    Closing,

    /// <summary>Nothing holds it: the background is asking its relays, or the install is starting.</summary>
    NothingHolds,
}

/// <summary>How the wait stands, and when it ends when it counts down.</summary>
/// <param name="Wait">How it stands.</param>
/// <param name="Ends">When the last countdown runs out, for <see cref="UpdateWait.Counting"/>, and otherwise <see langword="null"/>.</param>
internal readonly record struct UpdateWaitReading(UpdateWait Wait, DateTimeOffset? Ends);

/// <summary>
/// What the update toast reads: what holds the update, and nothing that installs it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every member is answered from the background's own memory</b>: the sessions,
/// the relays and the update are all in that one process, so a read asks no pipe and
/// waits on nothing, which is what lets the toast read it once a second while it
/// counts down.
/// </para>
/// <para>
/// <i>Split from <see cref="IUpdateHolds"/> on 2026-10-10</i>, #101 of the texts
/// review: the indirection the toasts are built over answered an install-now with a
/// sentence nothing could show, because only the update page installs, on the update
/// core itself, and a toast's <i>Install now</i> opens that page.
/// </para>
/// </remarks>
internal interface IUpdateHoldsReader
{
    /// <summary>Reads what holds the downloaded update now.</summary>
    /// <remarks>
    /// <b>Since 2026-10-10 it names each relay's conversation</b>, read from its client's
    /// own records at this moment (the maintainer's 1.3 c), because this is what the
    /// dashboard and the ready toast are drawn from.
    /// </remarks>
    /// <returns>The snapshot.</returns>
    UpdateHoldSnapshot Read();

    /// <summary>
    /// Reads what holds the update now with no conversation named: what the ready
    /// toast's countdown reads once a second, which shows no name.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-10 with the names</b>, so that a toast left counting down for
    /// hours reads no client's records once a second; the toast's reconnect line, which
    /// does name them, is written from <see cref="Read"/> when the toast is raised. A
    /// reader with no names to leave out answers <see cref="Read"/>.
    /// </remarks>
    /// <returns>The snapshot, every relay's conversation and name left unread.</returns>
    UpdateHoldSnapshot ReadCountdown() => Read();
}

/// <summary>
/// What the dashboard's update page reads and acts on: what holds the update, and the
/// person's install-now. The background implements it.
/// </summary>
internal interface IUpdateHolds : IUpdateHoldsReader
{
    /// <summary>
    /// The person's install-now, from the dashboard's update page: every session
    /// is closed cleanly, every relay ends, and the update installs at once.
    /// </summary>
    /// <param name="version">The version the page showed, which must still be the one waiting.</param>
    /// <param name="cancellationToken">Ends the wait for the hand-over.</param>
    /// <returns><see langword="null"/> once the install has started, and otherwise why it did not, as one sentence.</returns>
    Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken);
}

/// <summary>
/// The four update toasts, as the background and the after-update start raise
/// them.
/// </summary>
/// <remarks>
/// <b>Decided 2026-10-08 by the maintainer (T, U2):</b> a ready toast with a live
/// countdown and the buttons <i>Install now</i> and <i>Wait for inactivity</i>; an
/// installing toast; an installed toast with <i>Changelog</i> and <i>Dismiss</i>;
/// a failed toast. None times out: each is a reminder that stays on screen until
/// the person acts. Each call replaces whatever update toast is showing.
/// </remarks>
internal interface IUpdateToasts
{
    /// <summary>
    /// A downloaded update is held: raises the ready toast and keeps its countdown
    /// live, once per version.
    /// </summary>
    /// <param name="version">The version that waits.</param>
    void Held(string version);

    /// <summary>The background is about to hand over to the installer.</summary>
    /// <param name="version">The version being installed.</param>
    void Installing(string version);

    /// <summary>The after-update start found the new version running.</summary>
    /// <param name="version">The version now installed.</param>
    void Installed(string version);

    /// <summary>The after-update start found the old version running: the install failed.</summary>
    /// <param name="version">The version that did not install.</param>
    void Failed(string version);
}
