// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Coordination;
using BrowserAI.Registration;
using BrowserAI.Updates;

namespace BrowserAI.App.Page;

/// <summary>
/// What the page knows about this BrowserAI that does not change while the
/// coordinator runs, read once when it starts.
/// </summary>
internal sealed record PageFacts
{
    /// <summary>The product version, as the binary reports it.</summary>
    public required string Version { get; init; }

    /// <summary>Where this install lives, or <see langword="null"/> when this is not one.</summary>
    public required string? InstallRoot { get; init; }

    /// <summary>Where the browsers, sessions and logs live.</summary>
    public required string DataRoot { get; init; }

    /// <summary>Where the log is written.</summary>
    public required string LogDirectory { get; init; }

    /// <summary>The server this install would register, or <see langword="null"/> when it refused to compose one.</summary>
    public required string? ServerCommand { get; init; }

    /// <summary>
    /// The arguments a client starts <see cref="ServerCommand"/> with, as every
    /// registration of this install writes them: <c>--mcp</c>, and the data root
    /// when the install names one.
    /// </summary>
    public IReadOnlyList<string> ServerArguments { get; init; } = [RegistrationTarget.McpArgument];

    /// <summary>Why there is no server command, when there is not.</summary>
    public required string? ServerRefusal { get; init; }

    /// <summary>When the background serving the page started, or <see langword="null"/> where nothing says.</summary>
    /// <remarks><b>Added 2026-10-11, the maintainer's 43 b</b>: one of the three facts the page may show.</remarks>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>How the background serving the page was started.</summary>
    /// <remarks><b>Added 2026-10-11, the maintainer's 43 b</b>, with <see cref="StartedAt"/>.</remarks>
    public BackgroundStart StartedHow { get; init; } = BackgroundStart.Unknown;

    /// <summary>
    /// How long a conversation holds an update after its app's last message other than a
    /// ping: the relay's fixed countdown, <c>RelayConstants.IdleCountdown</c>, which the
    /// background hands over because this project cannot read it.
    /// </summary>
    /// <remarks>
    /// <b>Added 2026-10-11, for fact 6 of that day's reader pass</b>: the home said <i>10 min
    /// after each call</i>, a number written in a string, where the numbers index's scan cannot
    /// see it, and a call where the countdown counts every message.
    /// </remarks>
    public required TimeSpan RelayHold { get; init; }
}

/// <summary>How the background was started, as its task's <c>$(Arg0)</c> said.</summary>
internal enum BackgroundStart
{
    /// <summary>Nothing said, as for a background a developer started by hand.</summary>
    Unknown,

    /// <summary>The sign-in trigger of its task.</summary>
    SignIn,

    /// <summary>A person's start: the Start Menu, a notification's button or <c>--sessions</c>.</summary>
    Person,

    /// <summary>The installer, once, after an install.</summary>
    Installer,

    /// <summary>Velopack's restart after an update.</summary>
    AfterUpdate,
}

/// <summary>One session folder, as the last measurement found it.</summary>
/// <param name="Id">An opaque name for it: the same digest a held session's <see cref="SessionEntry.Id"/> is.</param>
/// <param name="Directory">The folder.</param>
/// <param name="Bytes">What its files weigh, or <see langword="null"/> where the folder could not be read.</param>
/// <remarks>
/// <b>Added 2026-10-11, the maintainer's 43 b</b>: each session folder's size, and the
/// session folders not open now. Whether a folder is open is read when the page is drawn,
/// from the sessions the background holds then, and never stored here. <b>The output
/// folder's two numbers since 2026-10-12</b>, the maintainer's 80 c: the same listing finds
/// them, and <c>browserai_list</c> reports them beside the folder's size.
/// </remarks>
internal sealed record SessionFolder(string Id, string Directory, long? Bytes)
{
    /// <summary>What the folder's <c>output</c> folder holds, in bytes.</summary>
    public long OutputBytes { get; init; }

    /// <summary>How many files the folder's <c>output</c> folder holds.</summary>
    public int OutputFiles { get; init; }
}

/// <summary>Every session folder BrowserAI knows of and its size, measured at one moment.</summary>
/// <param name="MeasuredAt">When the measurement finished.</param>
/// <param name="Folders">Each folder, largest first.</param>
internal sealed record FolderMeasure(DateTimeOffset MeasuredAt, IReadOnlyList<SessionFolder> Folders)
{
    /// <summary>The folder an opaque id names, or <see langword="null"/>.</summary>
    /// <param name="id">The id the page sent.</param>
    /// <returns>The folder.</returns>
    public SessionFolder? ById(string id) =>
        Folders.FirstOrDefault(folder => string.Equals(folder.Id, id, StringComparison.Ordinal));
}

/// <summary>One trace a session holds.</summary>
/// <param name="Id">An opaque name for it, which is all the page ever sends back.</param>
/// <param name="Name">The trace's file name.</param>
/// <param name="Path">Its full path. Never sent to the page as anything but text.</param>
internal sealed record TraceEntry(string Id, string Name, string Path);

/// <summary>One session one server holds.</summary>
/// <param name="Id">An opaque name for it, stable for one directory.</param>
/// <param name="Directory">The session directory.</param>
/// <param name="Purpose">What its record says it is for.</param>
/// <param name="BrowserOpen">Whether a browser is up on it.</param>
/// <param name="Traces">The traces under its output folder.</param>
/// <param name="Headed">Whether its browser has a window.</param>
/// <param name="Kept">Whether its client has gone and the session host keeps it (Q366 b).</param>
/// <param name="DrivenBy">What the client driving it called itself, as the session host saw it.</param>
/// <param name="DrivenThrough">The pid of the server that client relays through.</param>
/// <param name="IdleCloseAt">When its idle close ends it if no call comes first.</param>
internal sealed record SessionEntry(
    string Id,
    string Directory,
    string? Purpose,
    bool BrowserOpen,
    IReadOnlyList<TraceEntry> Traces,
    bool Headed = false,
    bool Kept = false,
    string? DrivenBy = null,
    int? DrivenThrough = null,
    DateTimeOffset? IdleCloseAt = null);

/// <summary>Which client a server serves, as far as the page cares.</summary>
internal enum ClientKind
{
    /// <summary>Claude Code, which starts a server again on its next call.</summary>
    ClaudeCode,

    /// <summary>Codex, which does not start a closed server again in the same thread.</summary>
    Codex,

    /// <summary>Anything else, or a client that has not said yet.</summary>
    Other,
}

/// <summary>One server running from this install, as its pipe described it.</summary>
/// <param name="Id">An opaque name for it: its pid and its creation time.</param>
/// <param name="Marker">Its live marker, which names its pipe.</param>
/// <param name="Description">What it said about itself.</param>
/// <param name="Kind">Which client it serves.</param>
/// <param name="RecentlyActive">Whether it answered a call within the browser-idle period, or is answering one now.</param>
/// <param name="Sessions">
/// The sessions it holds, or for a server that relays to the session host, the
/// host's sessions its client drives.
/// </param>
/// <param name="KnownAs">
/// What its client called itself as the session host saw it, for a server that
/// relays and so never reads its client's handshake.
/// </param>
internal sealed record ServerEntry(
    string Id,
    string Marker,
    ServerDescription Description,
    ClientKind Kind,
    bool RecentlyActive,
    IReadOnlyList<SessionEntry> Sessions,
    string? KnownAs = null)
{
    /// <summary>Whether this is the session host, which the page offers no close for.</summary>
    public bool IsHost => string.Equals(Description.Role, ServerDescription.Roles.Host, StringComparison.Ordinal);

    /// <summary>Whether this server relays its client to the session host.</summary>
    public bool IsRelay => string.Equals(Description.Role, ServerDescription.Roles.Relay, StringComparison.Ordinal);

    /// <summary>
    /// What the person sees the conversation of this relay's client called, read from the
    /// client's records when the page was read, or <see langword="null"/>.
    /// </summary>
    /// <remarks><b>Added 2026-10-10</b>, the maintainer's 1.2 a and 1.3 c.</remarks>
    public ConversationName? Conversation { get; init; }

    /// <summary>The VS Code window this relay's client is a tab of, or <see langword="null"/>.</summary>
    /// <remarks><b>Added 2026-10-10</b>, the maintainer's 1.5 a: the page groups a window's tabs under it.</remarks>
    public ClientWindow? Window { get; init; }

    /// <summary>What this relay's client needs once an update has ended it, as its greeting said.</summary>
    /// <remarks>
    /// <b>Added 2026-10-11</b>: the conversations page says it of each conversation, and
    /// tells a terminal from VS Code by it, as the update page has since 2026-10-08.
    /// </remarks>
    public RelayReconnect Reconnect { get; init; } = RelayReconnect.Unknown;
}

/// <summary>Everything the sessions page shows, read at one moment.</summary>
/// <param name="ReadAt">When it was read.</param>
/// <param name="Servers">Every server that answered.</param>
/// <param name="Unanswered">One sentence per live marker whose server did not answer.</param>
internal sealed record SessionsSnapshot(DateTimeOffset ReadAt, IReadOnlyList<ServerEntry> Servers, IReadOnlyList<string> Unanswered)
{
    /// <summary>Nothing read yet.</summary>
    public static SessionsSnapshot Empty { get; } = new(DateTimeOffset.MinValue, [], []);

    /// <summary>The session an opaque id names, or <see langword="null"/>.</summary>
    /// <param name="id">The id the page sent.</param>
    /// <returns>The session.</returns>
    public SessionEntry? SessionById(string id) =>
        Servers.SelectMany(server => server.Sessions).FirstOrDefault(session => string.Equals(session.Id, id, StringComparison.Ordinal));

    /// <summary>The trace an opaque id names, or <see langword="null"/>.</summary>
    /// <param name="id">The id the page sent.</param>
    /// <returns>The trace.</returns>
    public TraceEntry? TraceById(string id) =>
        Servers.SelectMany(server => server.Sessions)
            .SelectMany(session => session.Traces)
            .FirstOrDefault(trace => string.Equals(trace.Id, id, StringComparison.Ordinal));

    /// <summary>The server an opaque id names, or <see langword="null"/>.</summary>
    /// <param name="id">The id the page sent.</param>
    /// <returns>The server.</returns>
    public ServerEntry? ServerById(string id) =>
        Servers.FirstOrDefault(server => string.Equals(server.Id, id, StringComparison.Ordinal));
}

/// <summary>A sentence to show above a section, with the raw text behind it, Q309 b.</summary>
/// <param name="Sentence">One plain sentence.</param>
/// <param name="Details">The raw text, shown under <i>Show details</i>, or <see langword="null"/>.</param>
internal sealed record PageNote(string Sentence, string? Details = null);

/// <summary>The registration the page last read.</summary>
/// <param name="ReadAt">When it was read.</param>
/// <param name="State">What was read: every client's registration, and the server a registration names.</param>
/// <param name="Failure">Why the read itself failed, as the raw text, or <see langword="null"/> when it did not.</param>
internal sealed record RegistrationSnapshot(DateTimeOffset ReadAt, AppState? State, string? Failure = null);

/// <summary>Why the coordinator's first tab was opened, which changes what that tab says first.</summary>
/// <remarks>
/// <i>Moved here 2026-10-03 from the configuration window, which it was first written for and
/// which the browser tab replaces.</i>
/// </remarks>
internal enum Occasion
{
    /// <summary>Somebody opened it.</summary>
    Ordinary,

    /// <summary>The installer started it, once, immediately after installing.</summary>
    FirstRun,

    /// <summary>It came back after applying an update.</summary>
    AfterUpdate,
}

/// <summary>Everything the page shows at one moment, which is what one render reads.</summary>
/// <param name="Facts">What does not change.</param>
/// <param name="Sessions">The last read of the sessions.</param>
/// <param name="Note">The last action's sentence, or <see langword="null"/>.</param>
/// <param name="Registration">The last read of the registration, or <see langword="null"/> before the first.</param>
/// <param name="Registering">A sentence while a registration action runs, or <see langword="null"/>.</param>
/// <param name="Holds">What holds a downloaded update, or <see langword="null"/> where the read failed or the page does not show it.</param>
/// <param name="Changelog">The installed version's section of the shipped changelog, or <see langword="null"/>.</param>
/// <param name="InstallBroken">
/// The first difference the latest session refused as a broken install met, while the
/// condition stands, or <see langword="null"/> (10 b, 2026-10-10).
/// </param>
/// <remarks>
/// <i>Corrected 2026-10-10 (previously with the update section's own stage and the
/// version a server had staged)</i>: the page's own update check, its offer of a
/// downloaded package and its install are deleted under the maintainer's "9 a",
/// because the background builds the page with no feed of its own.
/// </remarks>
internal sealed record PageView(
    PageFacts Facts,
    SessionsSnapshot Sessions,
    PageNote? Note,
    RegistrationSnapshot? Registration = null,
    string? Registering = null,
    UpdateHoldSnapshot? Holds = null,
    ChangelogSection? Changelog = null,
    string? InstallBroken = null)
{
    /// <summary>The tab number every link the page draws keeps.</summary>
    public int Tab { get; init; }

    /// <summary>The opaque name of the session a session page shows, or <see langword="null"/>.</summary>
    public string? Subject { get; init; }

    /// <summary>Where the updates come from and when BrowserAI last checked (43 b, 2026-10-11).</summary>
    public UpdateWatch Updates { get; init; } = UpdateWatch.Unreported;

    /// <summary>The last measurement of the session folders, or <see langword="null"/> before the first (43 b, 2026-10-11).</summary>
    public FolderMeasure? Folders { get; init; }

    /// <summary>Whether a measurement of the session folders is running now.</summary>
    public bool Measuring { get; init; }

    /// <summary>The moment the page is drawn at.</summary>
    public DateTimeOffset Now { get; init; }

    /// <summary>Every release the changelog shipped in the build holds, in its order (56 a, 2026-10-11).</summary>
    public IReadOnlyList<ChangelogSection> Releases { get; init; } = [];
}
