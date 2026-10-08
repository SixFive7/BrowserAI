// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

/// <summary>
/// Source-generated log messages for the background's update core and the
/// after-update start.
/// </summary>
/// <remarks>
/// <b>Event ids from 30 up, and that is the whole of the numbering rule here.</b>
/// The background logs updates under the category <c>BrowserAI.Updates</c>, where
/// <c>UpdateLog</c> already holds 1 to 21 and <c>UpdateConfigurationLog</c> 1 to 3,
/// so an id below 30 would answer a saved query written for one of theirs. None is
/// retired here yet.
/// </remarks>
internal static partial class BackgroundUpdateLog
{
    /// <summary>This build checks, and where.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="manifestUrl">The composed manifest URL.</param>
    /// <param name="minutes">The interval.</param>
    [LoggerMessage(
        EventId = 30,
        Level = LogLevel.Information,
        Message = "BrowserAI checks {ManifestUrl} for updates, at most once every {Minutes} minutes.")]
    public static partial void Watching(ILogger logger, string manifestUrl, double minutes);

    /// <summary>This process is not an installed one.</summary>
    /// <param name="logger">Where to write.</param>
    [LoggerMessage(
        EventId = 31,
        Level = LogLevel.Debug,
        Message = "This BrowserAI was not installed by Velopack, so it never checks for updates. That is the normal state under a checkout, a `dotnet run` and every test host.")]
    public static partial void NotInstalled(ILogger logger);

    /// <summary>The source the install named was refused.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="reason">Why.</param>
    [LoggerMessage(
        EventId = 32,
        Level = LogLevel.Error,
        Message = "The update source the install named was refused, so BrowserAI never checks for updates: {Reason}")]
    public static partial void SourceRefused(ILogger logger, string reason);

    /// <summary>A pre-release build with a URL source.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">This build's version.</param>
    /// <param name="manifestUrl">Where the source points.</param>
    [LoggerMessage(
        EventId = 33,
        Level = LogLevel.Information,
        Message = "BrowserAI {Version} is a pre-release build and its update source {ManifestUrl} is a URL, so it never checks for updates. A pre-release build checks a folder source only.")]
    public static partial void PreReleaseNeverChecksAUrl(ILogger logger, string version, string manifestUrl);

    /// <summary>No record of a previous check.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="where">The record's path.</param>
    [LoggerMessage(
        EventId = 34,
        Level = LogLevel.Information,
        Message = "No earlier update check is recorded in {Where}, so the first check runs now.")]
    public static partial void FirstCheckNoRecord(ILogger logger, string where);

    /// <summary>The record exists and could not be used.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="where">The record's path.</param>
    /// <param name="reason">Why.</param>
    [LoggerMessage(
        EventId = 35,
        Level = LogLevel.Warning,
        Message = "The record of the last update check in {Where} could not be read, so the first check runs now: {Reason}")]
    public static partial void FirstCheckUnreadable(ILogger logger, string where, string reason);

    /// <summary>The record names a time later than now.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="where">The record's path.</param>
    /// <param name="lastCheck">What it says.</param>
    [LoggerMessage(
        EventId = 36,
        Level = LogLevel.Warning,
        Message = "The record in {Where} says the last update check was at {LastCheck:O}, which is later than now, so the first check runs now.")]
    public static partial void FirstCheckRecordAhead(ILogger logger, string where, DateTimeOffset lastCheck);

    /// <summary>The last check is older than the interval.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="lastCheck">When it was.</param>
    /// <param name="minutes">The interval.</param>
    [LoggerMessage(
        EventId = 37,
        Level = LogLevel.Information,
        Message = "The last update check was at {LastCheck:O}, more than {Minutes} minutes ago, so the first check runs now.")]
    public static partial void FirstCheckRecordOld(ILogger logger, DateTimeOffset lastCheck, double minutes);

    /// <summary>The last check is recent: the first one of this run waits.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="lastCheck">When it was.</param>
    /// <param name="due">When the next one runs.</param>
    [LoggerMessage(
        EventId = 38,
        Level = LogLevel.Information,
        Message = "The last update check was at {LastCheck:O}, so the next one runs at {Due:O}.")]
    public static partial void FirstCheckLater(ILogger logger, DateTimeOffset lastCheck, DateTimeOffset due);

    /// <summary>The record could not be written.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="where">The record's path.</param>
    /// <param name="minutes">The interval.</param>
    /// <param name="reason">Why.</param>
    [LoggerMessage(
        EventId = 39,
        Level = LogLevel.Warning,
        Message = "Could not record this update check in {Where}, so a restart within the next {Minutes} minutes may check again: {Reason}")]
    public static partial void StampUnwritable(ILogger logger, string where, double minutes, string reason);

    /// <summary>A check starts.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="manifestUrl">The composed manifest URL.</param>
    [LoggerMessage(
        EventId = 40,
        Level = LogLevel.Information,
        Message = "Checking for updates at {ManifestUrl}.")]
    public static partial void Checking(ILogger logger, string manifestUrl);

    /// <summary>Nothing is on offer.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="manifestUrl">Where it looked.</param>
    [LoggerMessage(
        EventId = 41,
        Level = LogLevel.Debug,
        Message = "No update is available at {ManifestUrl}.")]
    public static partial void NothingAvailable(ILogger logger, string manifestUrl);

    /// <summary>Something is on offer.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version offered.</param>
    /// <param name="packId">The pack id it was published under.</param>
    /// <param name="isDowngrade">Whether it is a rollback.</param>
    /// <param name="deltaCount">How many deltas stand between; zero means a full download.</param>
    /// <param name="fullPackageSize">The full package's size in bytes.</param>
    [LoggerMessage(
        EventId = 42,
        Level = LogLevel.Information,
        Message = "Update {Version} of package '{PackId}' is available. rollback={IsDowngrade} deltas={DeltaCount} fullPackageBytes={FullPackageSize}")]
    public static partial void Found(ILogger logger, string version, string packId, bool isDowngrade, int deltaCount, long fullPackageSize);

    /// <summary>The package belongs to another pack.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version offered.</param>
    /// <param name="offered">Its pack id.</param>
    /// <param name="installed">This install's pack id.</param>
    [LoggerMessage(
        EventId = 43,
        Level = LogLevel.Warning,
        Message = "The update source offers {Version} of package '{Offered}' and this install is package '{Installed}', so it is neither downloaded nor applied.")]
    public static partial void ForeignPackage(ILogger logger, string version, string offered, string installed);

    /// <summary>The version failed to hand over earlier in this run.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    [LoggerMessage(
        EventId = 44,
        Level = LogLevel.Information,
        Message = "Update {Version} could not be handed to the installer earlier in this run, so it is not tried again until BrowserAI starts anew.")]
    public static partial void FailedBefore(ILogger logger, string version);

    /// <summary>A new version turned up while the relays were asked or the update installed.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    [LoggerMessage(
        EventId = 45,
        Level = LogLevel.Debug,
        Message = "Update {Version} was found while the relays were being asked or an update was installing, so it is left for the next check.")]
    public static partial void FoundWhileBusy(ILogger logger, string version);

    /// <summary>How far the download has got.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">What is being downloaded.</param>
    /// <param name="percent">0 to 100.</param>
    [LoggerMessage(
        EventId = 46,
        Level = LogLevel.Debug,
        Message = "Downloading update {Version}: {Percent}%.")]
    public static partial void DownloadProgress(ILogger logger, string version, int percent);

    /// <summary>The package is on disk, and the install has already been touched.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">What was downloaded.</param>
    [LoggerMessage(
        EventId = 47,
        Level = LogLevel.Information,
        Message = "Update {Version} is downloaded and staged. The download already rewrote Update.exe and removed the installed version's own package, so the install is changed although nothing is applied yet.")]
    public static partial void Staged(ILogger logger, string version);

    /// <summary>A bound ended the download.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">What was being downloaded.</param>
    /// <param name="why">Which bound, as a clause.</param>
    [LoggerMessage(
        EventId = 48,
        Level = LogLevel.Warning,
        Message = "The download of update {Version} was abandoned because {Why}. Nothing was applied, and the next check tries again.")]
    public static partial void DownloadAbandoned(ILogger logger, string version, string why);

    /// <summary>An update is held.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    [LoggerMessage(
        EventId = 49,
        Level = LogLevel.Information,
        Message = "Update {Version} is held until nothing uses BrowserAI.")]
    public static partial void Held(ILogger logger, string version);

    /// <summary>The held package went from disk.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version that was held.</param>
    [LoggerMessage(
        EventId = 50,
        Level = LogLevel.Warning,
        Message = "The package of update {Version} is no longer on disk, so nothing is held; the next check downloads what the source offers.")]
    public static partial void HeldPackageGone(ILogger logger, string version);

    /// <summary>An earlier run left a package staged.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">Its version.</param>
    [LoggerMessage(
        EventId = 51,
        Level = LogLevel.Information,
        Message = "Update {Version} was downloaded by an earlier run and is held.")]
    public static partial void StagedAtStart(ILogger logger, string version);

    /// <summary>Nothing holds, and the relays are asked.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    /// <param name="count">How many relays.</param>
    [LoggerMessage(
        EventId = 52,
        Level = LogLevel.Information,
        Message = "Nothing holds update {Version}, so BrowserAI asks its {Count} relay(s) whether they may end.")]
    public static partial void Asking(ILogger logger, string version, int count);

    /// <summary>The question was called off.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    /// <param name="why">What called it off.</param>
    [LoggerMessage(
        EventId = 53,
        Level = LogLevel.Information,
        Message = "Update {Version} is called off because {Why}. Every relay goes back to normal and none ends.")]
    public static partial void CalledOff(ILogger logger, string version, string why);

    /// <summary>Every relay agreed.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    [LoggerMessage(
        EventId = 54,
        Level = LogLevel.Information,
        Message = "Every relay agreed, so update {Version} installs now.")]
    public static partial void InstallingAgreed(ILogger logger, string version);

    /// <summary>Nothing held and nobody was there to ask.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    [LoggerMessage(
        EventId = 55,
        Level = LogLevel.Information,
        Message = "Nothing holds update {Version} and no relay is connected, so it installs now.")]
    public static partial void InstallingUnasked(ILogger logger, string version);

    /// <summary>The person chose Install now.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    [LoggerMessage(
        EventId = 56,
        Level = LogLevel.Information,
        Message = "The person chose Install now, so update {Version} installs now: every relay ends and every session closes first.")]
    public static partial void InstallingNow(ILogger logger, string version);

    /// <summary>The package is with Update.exe.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    /// <param name="processId">The pid Update.exe waits on.</param>
    /// <param name="arguments">What BrowserAI is started with afterwards.</param>
    [LoggerMessage(
        EventId = 57,
        Level = LogLevel.Information,
        Message = "Update {Version} is handed to Update.exe, which applies it once process {ProcessId} exits and then starts BrowserAI with {Arguments}.")]
    public static partial void HandedOver(ILogger logger, string version, int processId, string arguments);

    /// <summary>The hand-over threw.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 58,
        Level = LogLevel.Error,
        Message = "Update {Version} could not be handed to Update.exe, so nothing was installed and BrowserAI keeps running the version it has.")]
    public static partial void HandOverFailed(ILogger logger, string version, Exception failure);

    /// <summary>The person's install-now was refused.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version the page showed.</param>
    /// <param name="why">The sentence the page shows.</param>
    [LoggerMessage(
        EventId = 59,
        Level = LogLevel.Information,
        Message = "Install now for {Version} was refused: {Why}")]
    public static partial void InstallNowRefused(ILogger logger, string version, string why);

    /// <summary>The relays had not all ended in time.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="seconds">The bound.</param>
    [LoggerMessage(
        EventId = 60,
        Level = LogLevel.Warning,
        Message = "Not every relay had ended within {Seconds} s; Update.exe ends whatever is left under the install root.")]
    public static partial void RelaysDidNotEnd(ILogger logger, double seconds);

    /// <summary>The sessions had not all closed within the cap.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="seconds">The cap.</param>
    [LoggerMessage(
        EventId = 61,
        Level = LogLevel.Warning,
        Message = "Not every session had closed within {Seconds} s; whatever is left ends when the background exits.")]
    public static partial void SessionsDidNotClose(ILogger logger, double seconds);

    /// <summary>A toast could not be raised.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="toast">Which.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 62,
        Level = LogLevel.Warning,
        Message = "The {Toast} update toast could not be raised.")]
    public static partial void ToastFailed(ILogger logger, string toast, Exception failure);

    /// <summary>A seam the background implements failed.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="what">What was being done.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 63,
        Level = LogLevel.Warning,
        Message = "{What} failed.")]
    public static partial void SeamFailed(ILogger logger, string what, Exception failure);

    /// <summary>A check failed.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="minutes">The interval.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 64,
        Level = LogLevel.Warning,
        Message = "The update check failed. Nothing was changed, and the next check runs in {Minutes} minutes.")]
    public static partial void PassFailed(ILogger logger, double minutes, Exception failure);

    /// <summary>A check outran its budget.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="budgetMinutes">The budget.</param>
    [LoggerMessage(
        EventId = 65,
        Level = LogLevel.Warning,
        Message = "The update check did not answer within its budget of {BudgetMinutes} minutes and was abandoned. That is a slow or dead source, not a defect here.")]
    public static partial void CheckTimedOut(ILogger logger, double budgetMinutes);

    /// <summary>A step of the core's own clock threw.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 66,
        Level = LogLevel.Error,
        Message = "A step of the update core failed; it looks again at the next change or check.")]
    public static partial void TickFailed(ILogger logger, Exception failure);

    /// <summary>The background could not be asked to exit.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 67,
        Level = LogLevel.Error,
        Message = "The background could not be asked to exit after the hand-over; Update.exe ends it once its 60 s wait runs out.")]
    public static partial void ExitRequestFailed(ILogger logger, Exception failure);

    /// <summary>The after-update start is the new version.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    [LoggerMessage(
        EventId = 68,
        Level = LogLevel.Information,
        Message = "BrowserAI {Version} is installed: this start is the version the update was for.")]
    public static partial void AfterUpdateInstalled(ILogger logger, string version);

    /// <summary>The after-update start is still the old version.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="target">The version the update was for.</param>
    /// <param name="running">The version this start is.</param>
    /// <param name="log">Velopack's log for this pack.</param>
    [LoggerMessage(
        EventId = 69,
        Level = LogLevel.Warning,
        Message = "The update to {Target} failed: this start is still {Running}. Velopack's own account is in {Log}.")]
    public static partial void AfterUpdateFailed(ILogger logger, string target, string running, string log);

    /// <summary>One of the two versions is not a version.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="target">What the argument said.</param>
    /// <param name="running">What this build says it is.</param>
    [LoggerMessage(
        EventId = 70,
        Level = LogLevel.Warning,
        Message = "An after-update start was given '{Target}' and is '{Running}', and one of the two is not a version, so no toast is raised.")]
    public static partial void AfterUpdateNotAVersion(ILogger logger, string target, string running);

    /// <summary>The background went down before the hand-over.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="version">The version.</param>
    [LoggerMessage(
        EventId = 71,
        Level = LogLevel.Warning,
        Message = "The background went down before update {Version} was handed to the installer, so nothing was installed.")]
    public static partial void InstallAbandoned(ILogger logger, string version);
}
