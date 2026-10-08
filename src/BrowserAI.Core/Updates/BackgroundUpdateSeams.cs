// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;

namespace BrowserAI.Updates;

/// <summary>What the background knows about the build it is and the install it runs from.</summary>
/// <param name="IsInstalled">Whether Velopack installed this process.</param>
/// <param name="PackId">The pack id this install came from, or <see langword="null"/> when it is not known.</param>
/// <param name="Version">The version this binary was built as.</param>
internal sealed record UpdateInstall(bool IsInstalled, string? PackId, string Version)
{
    /// <summary>The facts about this process, read from the one place each is kept.</summary>
    /// <returns>
    /// <see cref="InstallLocation.IsInstalled"/>, <see cref="InstallLocation.AppId"/> and
    /// <see cref="BuildVersion.Current"/>.
    /// </returns>
    public static UpdateInstall OfThisProcess() => new(InstallLocation.IsInstalled, InstallLocation.AppId, BuildVersion.Current);
}

/// <summary>One relay the background serves: one client's connection to BrowserAI.</summary>
/// <param name="Id">The background's own name for the connection, unique while it is connected.</param>
/// <param name="ClientName">What the client put in <c>clientInfo.name</c>, or <see langword="null"/> when it said nothing.</param>
/// <param name="ClientVersion">What the client put in <c>clientInfo.version</c>, or <see langword="null"/>.</param>
/// <param name="ProjectFolder">The folder the client started BrowserAI in, or <see langword="null"/> when it is not known.</param>
/// <param name="IdleAt">
/// When the relay's fixed ten-minute activity countdown runs out (U1: every message
/// its client sends except <c>ping</c> starts it again).
/// </param>
/// <param name="CallInFlight">Whether a call from its client is running now.</param>
internal sealed record RelayState(
    string Id,
    string? ClientName,
    string? ClientVersion,
    string? ProjectFolder,
    DateTimeOffset IdleAt,
    bool CallInFlight);

/// <summary>A relay's answer to <i>ready to end?</i>.</summary>
/// <param name="Ready">
/// Yes: the relay's countdown has run out, no call is in flight, and from now on it
/// holds whatever its client sends until it is called off or ended.
/// </param>
/// <param name="IdleAt">When its countdown runs out, as the relay read it while answering.</param>
/// <param name="CallInFlight">Whether a call from its client was running while it answered.</param>
internal sealed record RelayReadiness(bool Ready, DateTimeOffset IdleAt, bool CallInFlight);

/// <summary>
/// The relays the background serves, as the update core asks about them and
/// tells them what to do.
/// </summary>
/// <remarks>
/// <para>
/// <b>Implemented by the background over its pipe</b>; every member below is
/// one message to every relay or a read of what the background already holds.
/// </para>
/// <para>
/// <b>The core calls these members on its own clock's thread or on the thread
/// that completed one of the tasks it awaits, and never while it holds a lock of
/// its own.</b> It never calls one from inside <see cref="BackgroundUpdates.Changed"/>
/// or <see cref="BackgroundUpdates.RelayWithdrew"/>: those record what they were
/// told and return, and the work they cause runs on the core's clock. So the
/// background may call both from inside its own bookkeeping.
/// </para>
/// </remarks>
internal interface IUpdateRelays
{
    /// <summary>Every relay connected now, read from memory.</summary>
    /// <returns>The relays, in any order.</returns>
    IReadOnlyList<RelayState> Connected();

    /// <summary>
    /// Asks one relay <i>ready to end?</i> for an update to <paramref name="version"/>.
    /// </summary>
    /// <remarks>
    /// <b>The core bounds the wait itself</b>, at
    /// <see cref="BackgroundUpdates.ReadyToEndBound"/>, and cancels
    /// <paramref name="cancellationToken"/> once the question is settled either way;
    /// an answer that arrives after that is not read. A relay that has gone answers
    /// by throwing, which the core counts as no answer.
    /// </remarks>
    /// <param name="relay">The relay's <see cref="RelayState.Id"/>.</param>
    /// <param name="version">The version the update would install.</param>
    /// <param name="cancellationToken">Cancelled once the answer is no longer wanted.</param>
    /// <returns>The relay's answer.</returns>
    Task<RelayReadiness> AskReadyToEndAsync(string relay, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Calls the update off for every relay: each goes back to normal and passes on
    /// whatever its client sent while it was holding, and none ends.
    /// </summary>
    /// <param name="version">The version that was called off.</param>
    void CallOff(string version);

    /// <summary>Ends every relay because the update to <paramref name="version"/> installs now.</summary>
    /// <remarks>
    /// With <paramref name="now"/> unset every relay has agreed and holds no call;
    /// with it set, the person chose <i>Install now</i>, and a relay answers its
    /// calls in flight and what it held with the update sentence itself before it
    /// ends. The core waits for the returned task at most
    /// <see cref="BackgroundUpdates.ReadyToEndBound"/> and then installs anyway:
    /// <c>Update.exe</c> ends whatever is still running under the install root.
    /// </remarks>
    /// <param name="version">The version that installs.</param>
    /// <param name="now">Whether the person's install-now asked for it.</param>
    /// <param name="cancellationToken">Cancelled when the background is going down.</param>
    /// <returns>Done once every relay has been told and has gone, as far as the background can tell.</returns>
    Task EndAllAsync(string version, bool now, CancellationToken cancellationToken);
}

/// <summary>One open session, as the update core reads it.</summary>
/// <param name="Directory">The session directory.</param>
/// <param name="Purpose">What its record says it is for, or <see langword="null"/>.</param>
/// <param name="Visible">Whether its browser shows a window.</param>
/// <param name="ClosesAt">
/// When its idle countdown closes it if no call or input comes first, or
/// <see langword="null"/> when an agent set it never to close for idleness.
/// </param>
internal sealed record ListedSession(string Directory, string? Purpose, bool Visible, DateTimeOffset? ClosesAt);

/// <summary>The background's sessions, as the update core reads and closes them.</summary>
/// <remarks>
/// <b>Defined here because the session manager lives in the server assembly</b>,
/// which this library cannot see. The background adapts it: each
/// <c>SessionCountdown</c> becomes a <see cref="ListedSession"/>, field for field.
/// The same threading promise as <see cref="IUpdateRelays"/> holds.
/// </remarks>
internal interface IUpdateSessions
{
    /// <summary>Every listed session and its countdown, read from memory.</summary>
    /// <returns>The sessions, in any order.</returns>
    IReadOnlyList<ListedSession> Countdowns();

    /// <summary>
    /// Closes every session cleanly for the person's <i>Install now</i>, each
    /// within <see cref="Sessions.SessionTimes.BrowserCloseCap"/>.
    /// </summary>
    /// <remarks>
    /// The core waits for the returned task at most
    /// <see cref="Sessions.SessionTimes.BrowserCloseCap"/>, because the closes run
    /// side by side and each is ended at that cap anyway.
    /// </remarks>
    /// <param name="cancellationToken">Cancelled when the background is going down.</param>
    /// <returns>Done once every session has closed or been ended.</returns>
    Task CloseAllAsync(CancellationToken cancellationToken);
}
