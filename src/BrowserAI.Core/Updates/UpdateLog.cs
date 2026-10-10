// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

// ⚠️ MOVED HERE 2026-10-08 FROM UpdateService.cs, unchanged, when the server's own
// update lane went with the in-process server (S a). The census and every mode's
// startup still write these records under the category they always had, and their
// event ids are a log query's subject, so none moves and none is reused.
//
// ⚠️ CORRECTED 2026-10-10 BY ADDITION: the census writes nothing since that day,
// because it was deleted, by the maintainer's decision "9 a". Its three records
// went with it, and their ids are retired at the end of this class.
//
// ⚠️ AND TWELVE MORE THE SAME DAY, under the same "9 a": every record of the
// server's own update pass, which went with the in-process server (S a, 2026-10-08)
// and left these with no caller. The background's update core writes its own,
// under BrowserAI.Updates through BackgroundUpdateLog and from event 30, and what
// is left here is the startup's Velopack lines, the install's own line and the
// live-marker reclaim.

/// <summary>Source-generated log messages for the update path.</summary>
internal static partial class UpdateLog
{
    /// <summary>Velopack said something.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="message">What Velopack said.</param>
    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Debug,
        Message = "velopack: {Message}")]
    public static partial void Velopack(ILogger logger, string message);

    /// <summary>Velopack reported a problem.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="message">What Velopack said.</param>
    /// <param name="failure">The exception it carried, when it carried one.</param>
    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Warning,
        Message = "velopack: {Message}")]
    public static partial void VelopackProblem(ILogger logger, string message, Exception? failure);

    /// <summary>The install root, the channel and the version the locator reports.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="rootAppDir">The install root, which is the parent of current\.</param>
    /// <param name="channel">The channel this install came from.</param>
    /// <param name="manifestVersion">The version the package manifest carries.</param>
    /// <param name="assemblyVersion">The version the binary carries.</param>
    /// <remarks>
    /// <b>Both versions are logged because they come from different
    /// mechanisms</b> -- one from <c>vpk</c>'s manifest, one from MinVer's
    /// assembly attribute -- and a build packed at one and compiled at the other
    /// is exactly the state that made a fleet download the binary it was already
    /// running, hourly, forever.
    /// </remarks>
    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Information,
        Message = "Installed at {RootAppDir}. channel={Channel} manifestVersion={ManifestVersion} assemblyVersion={AssemblyVersion}")]
    public static partial void Installed(ILogger logger, string rootAppDir, string channel, string manifestVersion, string assemblyVersion);

    /// <summary>One reclaim pass over the live-marker directory, on one line.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="summary">The pass's own census.</param>
    /// <remarks>
    /// <b>Debug, because the healthy case is a pass that removed nothing.</b>
    /// The counts are what makes an unhealthy one visible: a
    /// <c>reclaimed=</c> that keeps growing is a fleet that keeps being killed
    /// from outside, and an <c>undetermined=</c> that is not zero is a
    /// permissions problem on a path the sentence names.
    /// </remarks>
    [LoggerMessage(
        EventId = 18,
        Level = LogLevel.Debug,
        Message = "live-marker reclaim: {Summary}")]
    public static partial void ReclaimedLiveMarkers(ILogger logger, string summary);

    /// <summary>Somebody else holds the gate, so this process did not reclaim.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="mutex">The gate's name.</param>
    /// <remarks>
    /// Not a missed reclaim: whoever holds the gate is walking the same
    /// directory, which is the same argument the stray sweep's own skip rests
    /// on.
    /// </remarks>
    [LoggerMessage(
        EventId = 19,
        Level = LogLevel.Debug,
        Message = "Another process holds {Mutex} and is reclaiming live markers; this one skipped instead of waiting.")]
    public static partial void LiveMarkerReclaimSkipped(ILogger logger, string mutex);

    /// <summary>The reclaim could not run, or could not finish.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="directory">The live-instance directory.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Warning,
        Message = "Could not reclaim stale live-instance markers under {Directory}. Nothing was removed; BrowserAI is unaffected and the next pass tries again.")]
    public static partial void CouldNotReclaimLiveMarkers(ILogger logger, string directory, Exception failure);

    // ⚠️ EVENT IDS 12, 13 AND 14 ARE RETIRED -- 2026-10-10, the maintainer's
    // decision "9 a". They were the census's own records: `CouldNotJoinLiveSet`,
    // "Could not join the live-instance set under {Directory}. BrowserAI serves
    // normally; it simply will not apply an update, because it cannot prove no
    // other instance would be terminated by one."; `CouldNotCensusLiveSet`, "Could
    // not count live BrowserAI instances under {Directory}, so this one is treated
    // as not alone and no update is applied."; and `NotAlone`, "{Others} other
    // BrowserAI instance(s) are running out of this install." Nothing joined the
    // live set or took its census after the one-binary build of 2026-10-08, and
    // LiveInstances.Join and LiveInstances.Census were deleted with these. Every
    // build up to 1.1.0 writes them, so a saved query may still meet them in an old
    // log, and none of the three ids may be taken again.
    //
    // ⚠️ EVENT IDS 1 TO 11 AND 21 ARE RETIRED TOO -- 2026-10-10, the same "9 a".
    // They were the server's update pass, which nothing has run since S a on
    // 2026-10-08: NotAnInstall (1), PreReleaseBuildDoesNotUpdate (2), Checking (3),
    // NothingAvailable (4), Found (5), DownloadProgress (6), Downloaded (7),
    // StagedButNotAlone (8), Applying (9), PassFailed (10), TripwireFired (11) and
    // CheckTimedOut (21). Builds up to 1.1.0 wrote them under this category, so an
    // old log still carries them, and none of these ids may be taken again. The
    // background's own records of a check, an offer, a download and a failure are
    // BackgroundUpdateLog's.
    //
    // ⚠️ THE LINE BELOW IS READ BY `ProxyLogTests`, per class: the
    // machine-readable half of the two paragraphs above, beside them and not in
    // place of them.
    //
    // RETIRED-EVENT-IDS: 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 21
}
