// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Sessions;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Runtime;

/// <summary>What one prune pass did, and what it deliberately did not do.</summary>
/// <param name="Removed">The superseded directories that are gone, absolute.</param>
/// <param name="ReclaimedBytes">
/// What the pass actually freed, measured before and after rather than assumed —
/// a tree that half deleted contributes what it gave up.
/// </param>
/// <param name="Retained">
/// Every superseded directory that is still there, one line each, <b>with the
/// reason</b>. Never silent: a revision left behind because a browser is running
/// out of it is the ordinary case, and one left behind because a file would not
/// delete is a disk nobody is going to reclaim by waiting.
/// </param>
internal sealed record PruneReport(IReadOnlyList<string> Removed, long ReclaimedBytes, IReadOnlyList<string> Retained);

/// <summary>
/// Deletes browser revisions the shipped manifest no longer names — the
/// obligation that turning Playwright's own garbage collection off created.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because of one environment variable.</b>
/// [§A](../../../plan/A-runtime.md#first-run-browser-provisioning) mandates
/// <c>PLAYWRIGHT_SKIP_BROWSER_GC=1</c>, because upstream's stale-browser sweep
/// deletes any registry directory not referenced from <c>.links</c> and the blast
/// radius of that is <i>our own Chromium</i>. Turning it off is right and it hands
/// us the job it was doing: without this, every <c>browsers.json</c> bump strands
/// **430.48 MiB** per machine, forever, and nothing anywhere says so.
/// </para>
/// <para>
/// <b>Superseded means the resolved manifest no longer names it, and nothing
/// looser.</b> A directory is a candidate only when its name begins with a prefix
/// upstream itself would recognise
/// (<see cref="BrowserRevision.DirectoryPrefix"/>) and its revision is not the one
/// the manifest currently carries. Anything else in the browsers root —
/// <c>.links</c>, a directory a future upstream invents, something a person put
/// there — is left alone. <b>The rule is deliberately not "delete what I do not
/// recognise"</b>: this code runs unattended against a directory under the user's
/// <c>%LocalAppData%</c>, so an unrecognised name is a reason to stop rather than
/// a reason to act.
/// </para>
/// <para>
/// <b>Two independent guards stand between a pass and a browser somebody is
/// driving.</b> First, the machine-wide provisioning mutex for <i>every</i> family
/// is held for the whole pass, at a zero timeout — so a pass either proves that no
/// install is in flight anywhere on this machine or does nothing at all. Second,
/// the machine's process list is read once and a candidate holding a live process
/// is retained and named, which is the shape
/// <c>browserai_reinstall_browser</c> already refuses with. Neither guard is
/// sufficient alone: the mutex says nothing about a browser launched an hour ago,
/// and the process list says nothing about an extraction that has not written its
/// first file yet.
/// </para>
/// <para>
/// <b>Pruning is never allowed to fail a provision.</b> The caller has just
/// downloaded 203.8 MB successfully; a disk that would not give up an old tree is
/// a log line and a retained row, never an error handed to a model that asked for
/// a browser.
/// </para>
/// </remarks>
internal static class RevisionPrune
{
    /// <summary>Runs one pass over a browsers root.</summary>
    /// <param name="browsersDirectory">The browsers root, absolute.</param>
    /// <param name="manifest">The resolved payload's manifest — what <i>current</i> means.</param>
    /// <param name="logger">Where the pass reports.</param>
    /// <param name="familyAlreadyHeld">
    /// The family whose provisioning mutex the calling thread already owns, so the
    /// pass does not wait on itself. <see langword="null"/> when the caller holds
    /// none, which is how the suite and any future maintenance caller drive it.
    /// </param>
    /// <returns>What it did.</returns>
    public static PruneReport Run(
        string browsersDirectory,
        BrowsersManifest manifest,
        ILogger logger,
        string? familyAlreadyHeld = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(logger);

        if (!Directory.Exists(browsersDirectory))
        {
            return new PruneReport([], 0, []);
        }

        var held = new List<MachineMutex>();

        try
        {
            foreach (var family in ProvisionedBrowsers.Families)
            {
                if (string.Equals(family, familyAlreadyHeld, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var mutex = MachineMutex.Create(BrowserProvisioner.MutexNameFor(browsersDirectory, family));

                // Zero timeout, exactly as the install path uses it. A pass that
                // waited would hold a thread for the length of somebody else's
                // 203.8 MB download to do work that is never urgent.
                if (mutex.Acquire(LockScopes.NeverWaits) is MutexAcquisition.NotAcquired)
                {
                    mutex.Dispose();
                    PruneLog.AnInstallIsInFlight(logger, family);

                    return new PruneReport(
                        [],
                        0,
                        [$"Nothing was pruned: another BrowserAI process is provisioning {family} into '{browsersDirectory}', so no directory there can be proven idle."]);
                }

                held.Add(mutex);
            }

            return Sweep(browsersDirectory, manifest, logger);
        }
        finally
        {
            foreach (var mutex in held)
            {
                mutex.Release();
                mutex.Dispose();
            }
        }
    }

    private static PruneReport Sweep(string browsersDirectory, BrowsersManifest manifest, ILogger logger)
    {
        var entries = manifest.Entries;
        var current = new HashSet<string>(entries.Select(entry => entry.DirectoryName), StringComparer.OrdinalIgnoreCase);
        var prefixes = entries.Select(entry => entry.DirectoryPrefix).ToArray();

        // ONE enumeration of the machine's process list for the whole pass. Asking
        // per candidate would open every process on the machine once per directory,
        // and the answer cannot become more true by being asked again.
        if (Live(browsersDirectory, logger) is not { } live)
        {
            return new PruneReport(
                [],
                0,
                [$"Nothing was pruned: the machine's process list could not be read, so nothing under '{browsersDirectory}' could be shown to be idle."]);
        }

        var removed = new List<string>();
        var retained = new List<string>();
        var reclaimed = 0L;

        foreach (var candidate in Subdirectories(browsersDirectory))
        {
            var name = Path.GetFileName(candidate);

            if (current.Contains(name))
            {
                // The revision this build wants. The whole point of the pass.
                continue;
            }

            if (!prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                // Not a browser directory at all. `.links` is here, and so is
                // anything upstream or a person adds later.
                continue;
            }

            var holders = live
                .Where(image => image.ImagePath.StartsWith(candidate + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (holders.Length is not 0)
            {
                var pids = string.Join(", ", holders.Select(holder => holder.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

                PruneLog.RevisionIsInUse(logger, name, pids);
                retained.Add($"'{candidate}' is superseded but {holders.Length} process(es) are running out of it (pid {pids}), so it was left alone.");
                continue;
            }

            var before = SizeOf(candidate);
            var failures = new List<string>();

            TreeDelete.Remove(candidate, failures);

            var after = SizeOf(candidate);
            reclaimed += Math.Max(0, before - after);

            if (Directory.Exists(candidate))
            {
                PruneLog.RevisionWouldNotGo(logger, name, failures.Count);
                retained.Add($"'{candidate}' is superseded and would not fully delete; {failures.Count} node(s) survived:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
                continue;
            }

            PruneLog.RevisionPruned(logger, name, before);
            removed.Add(candidate);
        }

        if (removed.Count is not 0)
        {
            PruneLog.PassReclaimed(logger, removed.Count, reclaimed, browsersDirectory);
        }

        return new PruneReport(removed, reclaimed, retained);
    }

    /// <summary>
    /// Every process running out of the browsers root, or <see langword="null"/>
    /// when the machine's process list could not be read at all.
    /// </summary>
    /// <remarks>
    /// <b>A census that failed is not the same as a census that found nothing</b>,
    /// and the difference decides whether anything is deleted: an empty list from a
    /// failed enumeration would make every superseded tree look idle. Returning
    /// <see langword="null"/> is what keeps those two answers apart, and the pass
    /// prunes nothing on it.
    /// </remarks>
    private static IReadOnlyList<RunningImage>? Live(string browsersDirectory, ILogger logger)
    {
        try
        {
            return BrowserProcesses.RunningFrom(browsersDirectory);
        }
#pragma warning disable CA1031 // Any failure to read the process list means the same thing here: idleness cannot be proven, so nothing may be deleted.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            PruneLog.CensusFailed(logger, browsersDirectory, failure);
            return null;
        }
    }

    private static IReadOnlyList<string> Subdirectories(string browsersDirectory)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(browsersDirectory)];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static long SizeOf(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
                : 0;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}

/// <summary>Source-generated log messages for pruning.</summary>
/// <remarks>Event ids start at 80, after <see cref="ProvisioningLog"/>'s 60s.</remarks>
internal static partial class PruneLog
{
    /// <summary>A superseded revision was deleted.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directoryName">The revision directory's leaf name.</param>
    /// <param name="bytes">What it was holding.</param>
    [LoggerMessage(
        EventId = 80,
        Level = LogLevel.Information,
        Message = "Pruned superseded browser revision {DirectoryName}, reclaiming {Bytes} bytes. Playwright's own stale-browser GC is disabled by BrowserAI, so this is BrowserAI's job.")]
    public static partial void RevisionPruned(ILogger logger, string directoryName, long bytes);

    /// <summary>A superseded revision has a live process in it and was left alone.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directoryName">The revision directory's leaf name.</param>
    /// <param name="processIds">The pids running out of it.</param>
    [LoggerMessage(
        EventId = 81,
        Level = LogLevel.Information,
        Message = "Browser revision {DirectoryName} is superseded but pid(s) {ProcessIds} are running out of it, so it was not pruned. It will be reclaimed by a later pass once they exit.")]
    public static partial void RevisionIsInUse(ILogger logger, string directoryName, string processIds);

    /// <summary>A superseded revision would not fully delete.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="directoryName">The revision directory's leaf name.</param>
    /// <param name="survivors">How many nodes stayed.</param>
    [LoggerMessage(
        EventId = 82,
        Level = LogLevel.Warning,
        Message = "Superseded browser revision {DirectoryName} would not fully delete; {Survivors} node(s) survived and the disk they hold will not be reclaimed by waiting.")]
    public static partial void RevisionWouldNotGo(ILogger logger, string directoryName, int survivors);

    /// <summary>What the pass reclaimed in total.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="count">How many revisions went.</param>
    /// <param name="bytes">How much disk came back.</param>
    /// <param name="root">The browsers root.</param>
    [LoggerMessage(
        EventId = 83,
        Level = LogLevel.Information,
        Message = "Pruned {Count} superseded browser revision(s) from {Root}, reclaiming {Bytes} bytes.")]
    public static partial void PassReclaimed(ILogger logger, int count, long bytes, string root);

    /// <summary>Another process is installing, so nothing was pruned.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="family">The family being installed elsewhere.</param>
    [LoggerMessage(
        EventId = 84,
        Level = LogLevel.Debug,
        Message = "Another BrowserAI process is provisioning {Family}, so no superseded revision was pruned this pass. Pruning is never urgent and the next successful provision will do it.")]
    public static partial void AnInstallIsInFlight(ILogger logger, string family);

    /// <summary>The process census failed, so nothing may be deleted.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="root">The browsers root.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 85,
        Level = LogLevel.Warning,
        Message = "Could not read the machine's process list, so nothing under {Root} can be shown to be idle and no superseded browser revision was pruned.")]
    public static partial void CensusFailed(ILogger logger, string root, Exception failure);

    /// <summary>A prune pass threw, and the provision it followed is unaffected.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 86,
        Level = LogLevel.Warning,
        Message = "Pruning superseded browser revisions failed. The browser that was just provisioned is installed and usable; only the disk an old revision holds was not reclaimed.")]
    public static partial void PassFailed(ILogger logger, Exception failure);
}
