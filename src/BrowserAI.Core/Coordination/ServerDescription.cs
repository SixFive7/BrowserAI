// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json;

namespace BrowserAI.Coordination;

/// <summary>
/// What one server says about itself when asked through its pipe: a snapshot of
/// its own memory, taken at the moment of asking.
/// </summary>
/// <remarks>
/// <para>
/// <b>Times, never verdicts.</b> Whether a server is <i>busy</i> is the page's
/// question, and Q269 settled how it is answered: a tool call within
/// the browser-idle period, or one in flight. So the server reports when its
/// last call arrived or was answered and how many are in flight, and the reader
/// does the arithmetic against the period it knows.
/// </para>
/// <para>
/// <b>Written with <see cref="Utf8JsonWriter"/> and read with
/// <see cref="JsonDocument"/>, and no serializer on either side</b>, which is
/// this product's rule for every JSON it owns under NativeAOT.
/// </para>
/// </remarks>
/// <param name="Protocol">The protocol version the server speaks.</param>
/// <param name="ProcessId">The server's pid.</param>
/// <param name="CreatedFileTime">Its creation time, which with the pid is its identity.</param>
/// <param name="Version">The BrowserAI version it is running.</param>
/// <param name="ImagePath">The executable it is running.</param>
/// <param name="State">One of <see cref="States"/>.</param>
/// <param name="Client">What its client said it was at <c>initialize</c>, or <see langword="null"/> before then.</param>
/// <param name="WorkingDirectory">The directory it was started in, which is its client's.</param>
/// <param name="Started">When it began answering its client, or <see langword="null"/> while it is still starting.</param>
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
    int Protocol,
    int ProcessId,
    long CreatedFileTime,
    string Version,
    string ImagePath,
    string State,
    ClientIdentity? Client,
    string WorkingDirectory,
    DateTimeOffset? Started,
    DateTimeOffset? LastToolCall,
    int CallsInFlight,
    IReadOnlyList<HeldSession> Sessions,
    string Role = ServerDescription.Roles.Server)
{
    /// <summary>Writes this description as the body of a <c>describe</c> answer.</summary>
    /// <returns>The answer's JSON.</returns>
    public byte[] ToJson() =>
        ServerPipeProtocol.Json(writer =>
        {
            writer.WriteString(ServerPipeProtocol.Fields.Answer, ServerPipeProtocol.Answers.Describe);
            writer.WriteNumber(ServerPipeProtocol.Fields.Protocol, Protocol);
            writer.WriteNumber(ServerPipeProtocol.Fields.ProcessId, ProcessId);
            writer.WriteNumber(Members.Created, CreatedFileTime);
            writer.WriteString(Members.Version, Version);
            writer.WriteString(Members.Image, ImagePath);
            writer.WriteString(Members.State, State);
            writer.WriteString(Members.Role, Role);

            if (Client is { } client)
            {
                writer.WriteStartObject(Members.Client);
                WriteNullable(writer, Members.Name, client.Name);
                WriteNullable(writer, Members.Title, client.Title);
                WriteNullable(writer, Members.Version, client.Version);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull(Members.Client);
            }

            writer.WriteString(Members.WorkingDirectory, WorkingDirectory);
            WriteInstant(writer, Members.Started, Started);
            WriteInstant(writer, Members.LastToolCall, LastToolCall);
            writer.WriteNumber(Members.CallsInFlight, CallsInFlight);

            writer.WriteStartArray(Members.Sessions);

            foreach (var session in Sessions)
            {
                writer.WriteStartObject();
                writer.WriteString(Members.Directory, session.Directory);
                WriteNullable(writer, Members.Purpose, session.Purpose);
                writer.WriteBoolean(Members.BrowserOpen, session.BrowserOpen);
                writer.WriteBoolean(Members.Headed, session.Headed);
                writer.WriteBoolean(Members.Kept, session.Kept);
                WriteNullable(writer, Members.DrivenBy, session.DrivenBy);

                if (session.DrivenThrough is { } through)
                {
                    writer.WriteNumber(Members.DrivenThrough, through);
                }
                else
                {
                    writer.WriteNull(Members.DrivenThrough);
                }

                WriteInstant(writer, Members.IdleCloseAt, session.IdleCloseAt);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });

    /// <summary>Reads a <c>describe</c> answer.</summary>
    /// <param name="answer">The answer's root object.</param>
    /// <returns>The description.</returns>
    /// <exception cref="FormatException">The answer is not a description this build can read.</exception>
    public static ServerDescription Parse(JsonElement answer)
    {
        try
        {
            if (answer.ValueKind is not JsonValueKind.Object
                || answer.GetProperty(ServerPipeProtocol.Fields.Answer).GetString() is not ServerPipeProtocol.Answers.Describe)
            {
                throw new FormatException("The answer is not a description.");
            }

            ClientIdentity? client = null;

            if (answer.GetProperty(Members.Client) is { ValueKind: JsonValueKind.Object } introduced)
            {
                client = new ClientIdentity(
                    Nullable(introduced, Members.Name),
                    Nullable(introduced, Members.Title),
                    Nullable(introduced, Members.Version));
            }

            var sessions = new List<HeldSession>();

            foreach (var session in answer.GetProperty(Members.Sessions).EnumerateArray())
            {
                // What a server of 2026-10-03 or later adds is read when it is there:
                // an older server describes sessions a client of its own drives.
                sessions.Add(new HeldSession(
                    session.GetProperty(Members.Directory).GetString() ?? string.Empty,
                    Nullable(session, Members.Purpose),
                    session.GetProperty(Members.BrowserOpen).GetBoolean(),
                    Headed: Optional(session, Members.Headed) is { ValueKind: JsonValueKind.True },
                    Kept: Optional(session, Members.Kept) is { ValueKind: JsonValueKind.True },
                    DrivenBy: Optional(session, Members.DrivenBy) is { ValueKind: JsonValueKind.String } by ? by.GetString() : null,
                    DrivenThrough: Optional(session, Members.DrivenThrough) is { ValueKind: JsonValueKind.Number } through && through.TryGetInt32(out var pid) ? pid : null,
                    IdleCloseAt: Optional(session, Members.IdleCloseAt) is { ValueKind: JsonValueKind.String } at
                        ? DateTimeOffset.Parse(at.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                        : null));
            }

            return new ServerDescription(
                answer.GetProperty(ServerPipeProtocol.Fields.Protocol).GetInt32(),
                answer.GetProperty(ServerPipeProtocol.Fields.ProcessId).GetInt32(),
                answer.GetProperty(Members.Created).GetInt64(),
                answer.GetProperty(Members.Version).GetString() ?? string.Empty,
                answer.GetProperty(Members.Image).GetString() ?? string.Empty,
                answer.GetProperty(Members.State).GetString() ?? string.Empty,
                client,
                answer.GetProperty(Members.WorkingDirectory).GetString() ?? string.Empty,
                Instant(answer, Members.Started),
                Instant(answer, Members.LastToolCall),
                answer.GetProperty(Members.CallsInFlight).GetInt32(),
                sessions,
                Optional(answer, Members.Role) is { ValueKind: JsonValueKind.String } role ? role.GetString() ?? Roles.Server : Roles.Server);
        }
        catch (Exception failure) when (failure is KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException($"The description is missing a member or carries one of the wrong kind: {failure.Message}", failure);
        }
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteInstant(Utf8JsonWriter writer, string name, DateTimeOffset? value)
    {
        if (value is { } instant)
        {
            writer.WriteString(name, instant.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static JsonElement? Optional(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) ? value : null;

    private static string? Nullable(JsonElement owner, string name) =>
        owner.GetProperty(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static DateTimeOffset? Instant(JsonElement owner, string name) =>
        owner.GetProperty(name) is { ValueKind: JsonValueKind.String } value
            ? DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;

    /// <summary>The words <c>state</c> can carry.</summary>
    public static class States
    {
        /// <summary>Started, and not yet answering its client.</summary>
        public const string Starting = "starting";

        /// <summary>Answering its client.</summary>
        public const string Serving = "serving";

        /// <summary>
        /// Started while its own install's updater was running, so it refuses
        /// every tool call until the updater has gone, and then says
        /// <see cref="Serving"/>.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>Corrected 2026-10-03, Q296 c</b> (previously "so it refuses every
        /// tool call and starts no browser server"): such a server starts its own
        /// child for the real tool list, and serves once the updater has gone.
        /// </remarks>
        public const string Updating = "updating";

        /// <summary>Asked to stop, and on its way out.</summary>
        public const string Stopping = "stopping";
    }

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

    /// <summary>The member names a description uses, spelled once.</summary>
    private static class Members
    {
        public const string Created = "created";
        public const string Version = "version";
        public const string Image = "image";
        public const string State = "state";
        public const string Client = "client";
        public const string Name = "name";
        public const string Title = "title";
        public const string WorkingDirectory = "workingDirectory";
        public const string Started = "started";
        public const string LastToolCall = "lastToolCall";
        public const string CallsInFlight = "callsInFlight";
        public const string Sessions = "sessions";
        public const string Directory = "directory";
        public const string Purpose = "purpose";
        public const string BrowserOpen = "browserOpen";
        public const string Role = "role";
        public const string Headed = "headed";
        public const string Kept = "kept";
        public const string DrivenBy = "drivenBy";
        public const string DrivenThrough = "drivenThrough";
        public const string IdleCloseAt = "idleCloseAt";
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
