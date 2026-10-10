// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using BrowserAI.Coordination;
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
/// <b>The page closes nothing</b>: a relay ends with its client, or for an update
/// through the agreement, and the background has no message that ends one relay
/// alone. <i>Corrected 2026-10-10 (previously "The page's close answers with a sentence
/// that says so")</i>: the maintainer's 17 a took the page's close away, and with it the
/// refusal this answered every close with.
/// </para>
/// <para>
/// <b>Each relay carries its conversation's name and its VS Code window</b>, added
/// 2026-10-10 by the maintainer's 1.2 a and 1.5 a, read from the client's own records
/// each time the page is read (<see cref="RelayRoster.Named"/>).
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

    /// <inheritdoc />
    public Task<SessionsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var background = new ServerDescription(
            Environment.ProcessId,
            ProcessLiveness.CreationTimeOfThisProcess(),
            Client: null,
            Environment.CurrentDirectory,
            LastToolCall: null,
            CallsInFlight: 0,
            host.Sessions.Held(),
            ServerDescription.Roles.Host);

        var answered = new List<(string Marker, ServerDescription Description)> { (BackgroundMarker, background) };

        // Named, so the page shows each conversation as the person sees it, read from the
        // client's records now (1.2 a, 1.3 c, 2026-10-10).
        var relays = roster.Named();

        foreach (var relay in relays)
        {
            var greeting = relay.Greeting;

            answered.Add((RelayMarkerPrefix + greeting.Id, new ServerDescription(
                greeting.RelayPid,
                CreatedFileTime: 0,
                new ClientIdentity(greeting.ClientName, Title: null, greeting.ClientVersion),
                greeting.Folder ?? string.Empty,
                LastToolCall: null,
                relay.CallInFlight ? 1 : 0,
                [],
                ServerDescription.Roles.Relay)));
        }

        var byMarker = relays.ToDictionary(relay => RelayMarkerPrefix + relay.Greeting.Id, StringComparer.Ordinal);
        var composed = CensusPageSessions.Compose(clock.GetUtcNow(), answered, []);

        return Task.FromResult(composed with
        {
            Servers =
            [
                .. composed.Servers.Select(server => byMarker.TryGetValue(server.Marker, out var relay)
                    ? server with { Conversation = relay.Reading.Name, Window = relay.Window }
                    : server),
            ],
        });
    }
}
