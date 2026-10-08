// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Hosting;
using BrowserAI.Interop;
using BrowserAI.Proxy;

namespace BrowserAI.Background;

/// <summary>
/// The sessions page, read from the background's own memory: the sessions it holds
/// and the relays connected to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The design's "What moves", 2026-10-08</b>: the tab reads sessions and
/// connections in memory, so no process needs a pipe of its own to be described. The
/// page's model is kept as it was, a list of servers with their sessions: the
/// background stands where the session host stood, holding every session, and each
/// relay stands where a front stood, with its client's name and folder and no
/// session of its own (<see cref="ServerDescription.Roles.Relay"/>). So the page
/// draws them the way it drew lane c's host and fronts.
/// </para>
/// <para>
/// <b>Closing a relay from the page is not offered</b>: a relay ends with its client,
/// or for an update through the agreement, and the background has no message that
/// ends one relay alone. The page's close answers with a sentence that says so.
/// </para>
/// </remarks>
/// <param name="host">The sessions.</param>
/// <param name="roster">The relays.</param>
/// <param name="clock">The clock the snapshot is stamped with.</param>
internal sealed class BackgroundPageSessions(SessionHost host, RelayRoster roster, TimeProvider clock) : IPageSessions
{
    /// <summary>The marker the background's own entry carries.</summary>
    public const string BackgroundMarker = "background";

    /// <summary>What every relay entry's marker begins with.</summary>
    public const string RelayMarkerPrefix = "relay:";

    private readonly DateTimeOffset _started = clock.GetUtcNow();

    /// <inheritdoc />
    public Task<SessionsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var background = new ServerDescription(
            ServerPipeProtocol.Version,
            Environment.ProcessId,
            ProcessLiveness.CreationTimeOfThisProcess(),
            BuildVersion.Current,
            Environment.ProcessPath ?? string.Empty,
            ServerDescription.States.Serving,
            Client: null,
            Environment.CurrentDirectory,
            _started,
            LastToolCall: null,
            CallsInFlight: 0,
            host.Sessions.Held(),
            ServerDescription.Roles.Host);

        var answered = new List<(string Marker, ServerDescription Description)> { (BackgroundMarker, background) };

        foreach (var (greeting, _, callInFlight) in roster.Read())
        {
            answered.Add((RelayMarkerPrefix + greeting.Id, new ServerDescription(
                ServerPipeProtocol.Version,
                greeting.RelayPid,
                CreatedFileTime: 0,
                BuildVersion.Current,
                Environment.ProcessPath ?? string.Empty,
                ServerDescription.States.Serving,
                new ClientIdentity(greeting.ClientName, Title: null, greeting.ClientVersion),
                greeting.Folder ?? string.Empty,
                Started: null,
                LastToolCall: null,
                callInFlight ? 1 : 0,
                [],
                ServerDescription.Roles.Relay)));
        }

        return Task.FromResult(CensusPageSessions.Compose(clock.GetUtcNow(), answered, []));
    }

    /// <inheritdoc />
    public Task<string?> CloseAsync(ServerEntry server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        return Task.FromResult<string?>(
            "A client's BrowserAI ends with the client itself: close the client, or end its conversation, and its BrowserAI goes with it. The background is ended from its own task or when you sign out.");
    }
}
