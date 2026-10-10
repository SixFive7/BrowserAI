// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Coordination;

/// <summary>
/// What the sessions page knows about one process: the background, or a relay
/// connected to it, read from the background's own memory.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Corrected 2026-10-10 (previously "What one server says about itself when
/// asked through its pipe: a snapshot of its own memory, taken at the moment of
/// asking.")</b>. The pipe per server went with the in-process server on 2026-10-08
/// (S a), and the background fills one of these for itself and one for each relay,
/// in <c>BackgroundPageSessions</c>. What the pipe needed went on 2026-10-10, by the
/// maintainer's decision <i>"9 a"</i>: the JSON this was written as and read from
/// (<c>ToJson</c> and <c>Parse</c>), and the protocol version it carried
/// (<c>Protocol</c>, always <c>ServerPipeProtocol.Version</c>, 1), which nothing on the
/// page read. The paragraph below is the record of the pipe's design.
/// </para>
/// <para>
/// <b>Times, never verdicts.</b> Whether a server is <i>busy</i> is the page's
/// question, and Q269 settled how it is answered: a tool call within
/// the browser-idle period, or one in flight. So the server reports when its
/// last call arrived or was answered and how many are in flight, and the reader
/// does the arithmetic against the period it knows.
/// </para>
/// <para>
/// ⚠️ <b>Four fields went on 2026-10-10, round 2 of the texts review, found by lane
/// FINAL</b>: <c>Version</c>, the BrowserAI version it ran; <c>ImagePath</c>, the
/// executable; <c>State</c>, one of the words of <c>States</c>, by then only
/// <c>serving</c>, which went with it; and <c>Started</c>, when it began answering. The
/// background wrote each for itself and for every relay, and nothing read any of them.
/// </para>
/// </remarks>
/// <param name="ProcessId">The server's pid.</param>
/// <param name="CreatedFileTime">Its creation time, which with the pid is its identity.</param>
/// <param name="Client">What its client said it was at <c>initialize</c>, or <see langword="null"/> before then.</param>
/// <param name="WorkingDirectory">The directory it was started in, which is its client's.</param>
/// <param name="LastToolCall">When a tool call last arrived or was answered, or <see langword="null"/> when none has.</param>
/// <param name="CallsInFlight">How many tool calls it is answering right now.</param>
/// <param name="Sessions">Every session it holds.</param>
/// <param name="Role">
/// One of <see cref="Roles"/>: a server that holds its own sessions, the session
/// host, or a server that relays its client to the host. <i>Added 2026-10-04 for
/// the session host (Q366 b), so the sessions page can tell the three apart</i>; an
/// answer without it is a server's.
/// </param>
internal sealed record ServerDescription(
    int ProcessId,
    long CreatedFileTime,
    ClientIdentity? Client,
    string WorkingDirectory,
    DateTimeOffset? LastToolCall,
    int CallsInFlight,
    IReadOnlyList<HeldSession> Sessions,
    string Role = ServerDescription.Roles.Server)
{
    /// <summary>The words <c>role</c> can carry.</summary>
    public static class Roles
    {
        /// <summary>A server a client started, holding its own sessions.</summary>
        public const string Server = "server";

        /// <summary>
        /// The session host the coordinator starts (Q366 b), holding the sessions of
        /// every client that reaches it.
        /// </summary>
        public const string Host = "host";

        /// <summary>
        /// A server a client started that relays its client to the session host and
        /// holds no session of its own.
        /// </summary>
        public const string Relay = "relay";
    }
}

/// <summary>What a client called itself in its <c>initialize</c> request.</summary>
/// <param name="Name"><c>clientInfo.name</c>.</param>
/// <param name="Title"><c>clientInfo.title</c>, which most clients leave out.</param>
/// <param name="Version"><c>clientInfo.version</c>.</param>
internal sealed record ClientIdentity(string? Name, string? Title, string? Version);

/// <summary>One session a server holds.</summary>
/// <param name="Directory">The session directory, which is its identity.</param>
/// <param name="Purpose">What its record says it is for.</param>
/// <param name="BrowserOpen">
/// Whether its browser server has a browser up: more processes in the child's job
/// than the child had of its own at its handshake, the same predicate a teardown
/// uses. <i>Corrected 2026-10-03 (previously "more than the node child in the
/// child's job"), when a console host was found beside node in every job.</i>
/// </param>
/// <param name="Headed">Whether its browser has a window, which no idle close ends (Q326 a).</param>
/// <param name="Kept">
/// Whether its client has gone and the session host keeps it, browser and all, for
/// the next client that names it (Q366 b).
/// </param>
/// <param name="DrivenBy">What the client that drives it called itself, or <see langword="null"/> when none does or it has not said.</param>
/// <param name="DrivenThrough">The pid of the BrowserAI server that client relays through, when Windows said.</param>
/// <param name="IdleCloseAt">
/// When its idle close ends it if no call comes first, or <see langword="null"/>
/// for a headed session, while a call runs, and once the close has run.
/// </param>
internal sealed record HeldSession(
    string Directory,
    string? Purpose,
    bool BrowserOpen,
    bool Headed = false,
    bool Kept = false,
    string? DrivenBy = null,
    int? DrivenThrough = null,
    DateTimeOffset? IdleCloseAt = null);
