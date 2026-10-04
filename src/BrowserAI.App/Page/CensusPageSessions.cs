// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BrowserAI.Coordination;
using BrowserAI.Proxy;
using BrowserAI.Sessions;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>
/// The sessions page's servers in the product: the live census under the install
/// root, each server asked through its own pipe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Q254 and Q317 c.</b> Every live server is described by itself, from its own
/// memory (<c>describe</c>, Q284 a), and closed by itself (<c>stop</c>), so a call
/// it is answering gets Q286 b's sentence and its browsers close themselves. The
/// census is read first and decides which pipes are asked at all, which is the
/// rule <see cref="ServerPipeClient"/> keeps.
/// </para>
/// <para>
/// <b>Two warnings are load-bearing</b> and they are computed here, from what the
/// server said: a Codex-hosted server does not get its server back in the same
/// thread, and a server that answered a call within the browser-idle period, or is
/// answering one, may be in the middle of a task (Q269).
/// </para>
/// <para>
/// <b>The page sends back opaque names and never a path.</b> A session's name is a
/// digest of its directory and a server's is its pid and creation time, so an
/// action names something this read found and nothing the page made up.
/// </para>
/// </remarks>
/// <param name="installRoot">The install root, or the data root of a process that is not installed.</param>
/// <param name="clock">What <i>recently</i> is measured against.</param>
internal sealed class CensusPageSessions(string installRoot, TimeProvider clock) : IPageSessions
{
    /// <inheritdoc />
    public async Task<SessionsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var directory = LiveInstances.DirectoryUnder(installRoot);
        var now = clock.GetUtcNow();

        if (!Directory.Exists(directory))
        {
            return new SessionsSnapshot(now, [], []);
        }

        var markers = Directory.GetFiles(directory, "*.live")
            .Where(marker => !(ServerPipeProtocol.TryProcessIdOf(marker, out var pid) && pid == Environment.ProcessId))
            .ToList();

        var answers = await Task.WhenAll(markers.Select(async marker =>
            (Marker: marker, Answer: await ServerPipeClient.DescribeAsync(marker, null, cancellationToken).ConfigureAwait(false))))
            .ConfigureAwait(false);

        var answered = new List<(string Marker, ServerDescription Description)>();
        var unanswered = new List<string>();

        foreach (var (marker, answer) in answers)
        {
            switch (answer.Outcome)
            {
                case ServerPipeOutcome.Answered when answer.Description is { } description:
                    answered.Add((marker, description));
                    break;

                case ServerPipeOutcome.NotRunning:
                    // A marker nobody holds is a server that has gone; the census is right
                    // to leave it out.
                    break;

                default:
                    unanswered.Add($"{Path.GetFileName(marker)}: {answer.Why}");
                    break;
            }
        }

        return Compose(now, answered, unanswered);
    }

    /// <summary>The page's servers from what each one said.</summary>
    /// <remarks>
    /// <b>Q366 b: a server that relays to the session host holds nothing</b> and never
    /// reads its client's handshake, so the host's own description is what says which
    /// of its sessions that client drives and what the client called itself. The
    /// host comes first, then every other server, most recent call first.
    /// </remarks>
    /// <param name="now">When the servers were asked.</param>
    /// <param name="answered">Every server that answered, with its live marker.</param>
    /// <param name="unanswered">One sentence per server that did not.</param>
    /// <returns>The snapshot.</returns>
    internal static SessionsSnapshot Compose(
        DateTimeOffset now,
        IReadOnlyList<(string Marker, ServerDescription Description)> answered,
        IReadOnlyList<string> unanswered)
    {
        ArgumentNullException.ThrowIfNull(answered);

        var hosted = answered
            .Where(server => string.Equals(server.Description.Role, ServerDescription.Roles.Host, StringComparison.Ordinal))
            .SelectMany(server => server.Description.Sessions)
            .ToList();

        var servers = answered
            .Select(server => string.Equals(server.Description.Role, ServerDescription.Roles.Relay, StringComparison.Ordinal)
                ? RelayEntry(server.Marker, server.Description, hosted, now)
                : Entry(server.Marker, server.Description, now))
            .OrderByDescending(server => server.IsHost)
            .ThenByDescending(server => server.Description.LastToolCall ?? DateTimeOffset.MinValue)
            .ToList();

        return new SessionsSnapshot(now, servers, unanswered);
    }

    /// <inheritdoc />
    public async Task<string?> CloseAsync(ServerEntry server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        var answer = await ServerPipeClient.StopAsync(server.Marker, null, cancellationToken).ConfigureAwait(false);

        return answer.Outcome is ServerPipeOutcome.Answered ? null : answer.Why;
    }

    /// <summary>What the page shows about one server.</summary>
    /// <param name="marker">Its live marker.</param>
    /// <param name="description">What it said.</param>
    /// <param name="now">When it was asked.</param>
    /// <returns>The entry.</returns>
    internal static ServerEntry Entry(string marker, ServerDescription description, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(description);

        var client = description.Client?.Name;
        var kind = KnownClients.Matches(client, KnownClients.Codex) ? ClientKind.Codex
            : KnownClients.Matches(client, KnownClients.ClaudeCode) ? ClientKind.ClaudeCode
            : ClientKind.Other;

        var recent = description.CallsInFlight > 0
            || (description.LastToolCall is { } last && now - last < SessionTimes.BrowserIdlePeriod);

        return new ServerEntry(
            string.Create(CultureInfo.InvariantCulture, $"{description.ProcessId}-{description.CreatedFileTime}"),
            marker,
            description,
            kind,
            recent,
            [.. description.Sessions.Select(Session)]);
    }

    /// <summary>What the page shows about a server that relays its client to the session host.</summary>
    /// <param name="marker">Its live marker.</param>
    /// <param name="description">What it said, which names no client and no session.</param>
    /// <param name="hosted">Every session the session host described.</param>
    /// <param name="now">When it was asked.</param>
    /// <returns>The entry, with the host's sessions its client drives.</returns>
    internal static ServerEntry RelayEntry(string marker, ServerDescription description, IReadOnlyList<HeldSession> hosted, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(hosted);

        var driven = hosted.Where(session => session.DrivenThrough == description.ProcessId).ToList();
        var client = driven.Select(session => session.DrivenBy).FirstOrDefault(name => name is { Length: > 0 });
        var kind = KnownClients.Matches(client, KnownClients.Codex) ? ClientKind.Codex
            : KnownClients.Matches(client, KnownClients.ClaudeCode) ? ClientKind.ClaudeCode
            : ClientKind.Other;

        // Q269's predicate, read off the host: a headless session's idle close lies
        // ahead only within the browser-idle period after its last call.
        var recent = driven.Any(session => session.IdleCloseAt is { } at && at > now);

        return new ServerEntry(
            string.Create(CultureInfo.InvariantCulture, $"{description.ProcessId}-{description.CreatedFileTime}"),
            marker,
            description,
            kind,
            recent,
            [.. driven.Select(Session)],
            client);
    }

    private static SessionEntry Session(HeldSession session) =>
        new(
            IdOf(session.Directory),
            session.Directory,
            session.Purpose,
            session.BrowserOpen,
            TracesIn(session.Directory),
            session.Headed,
            session.Kept,
            session.DrivenBy,
            session.DrivenThrough,
            session.IdleCloseAt);

    /// <summary>An opaque, stable name for a path.</summary>
    /// <param name="path">The path.</param>
    /// <returns>Sixteen hex characters of its SHA-256, upper-cased first.</returns>
    internal static string IdOf(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..16];

    /// <summary>The traces Playwright wrote under a session's output folder.</summary>
    /// <param name="session">The session directory.</param>
    /// <returns>One entry per <c>.trace</c> file, newest first.</returns>
    /// <remarks>
    /// <b>Where <c>browser_start_tracing</c> writes them</b>, read in
    /// <c>playwright-core</c> 1.64.0-alpha-1790635538000: <c>&lt;output&gt;\traces\trace-&lt;ms&gt;.trace</c>
    /// beside a <c>.network</c> file and a <c>resources</c> folder. A folder that
    /// cannot be read lists no trace; the page still shows the session.
    /// </remarks>
    internal static IReadOnlyList<TraceEntry> TracesIn(string session)
    {
        var traces = Path.Combine(session, SessionLayout.OutputFolderName, "traces");

        try
        {
            return Directory.Exists(traces)
                ? [.. new DirectoryInfo(traces).GetFiles("*.trace")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => new TraceEntry(IdOf(file.FullName), file.Name, file.FullName))]
                : [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
