// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using BrowserAI.Runtime;
using BrowserAI.Sessions;

namespace BrowserAI.Background;

/// <summary>
/// The session folders the page shows, read by the background: every folder the session
/// index names, every session the background holds, and what each weighs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 43 b, 2026-10-11</b>: the page may show each session folder's size
/// and the session folders not open now, which the background passes to it.
/// </para>
/// <para>
/// <b>Read and nothing else.</b> The index is walked at
/// <see cref="SessionIndexDepth.Presence"/>, which opens no session's store and probes no
/// lock, so a session an agent is using right now is not touched; a folder is weighed by
/// listing its files, and a junction or a symbolic link inside it is not followed, so a
/// folder linked into a session is never counted as part of it. Nothing here writes,
/// moves or deletes anything (41 a).
/// </para>
/// <para>
/// ⚠️ <b>The one measurement since 2026-10-12</b>, decided by the maintainer, 80 c, in his
/// words verbatim: <i>"78-82 your recommendation"</i>, and his condition, <i>"Reuse the
/// dashboard's folder measurement, so there is one measurement and not two"</i>. The
/// session manager makes this, the page and <c>browserai_list</c> both read it, and the
/// newest result is kept: a measurement asked for while one runs joins it, so two askers
/// at once cost one walk. The same listing weighs each folder's <c>output</c> folder,
/// which the listing reports beside the folder's size. <i>Corrected that day (previously
/// the page's alone, each measurement its asker's own, and <c>browserai_list</c> walked
/// every session it listed on every call, following a junction inside one).</i>
/// </para>
/// </remarks>
/// <param name="index">The session index of the background's data root.</param>
/// <param name="held">The folders of the sessions the background holds now.</param>
/// <param name="clock">The clock a measurement is stamped with.</param>
internal sealed class BackgroundPageFolders(SessionIndex index, Func<IEnumerable<string>> held, TimeProvider clock) : IPageFolders
{
    /// <summary>How a folder's files are listed: every file below it, past what cannot be read, and no reparse point followed.</summary>
    private static readonly EnumerationOptions Every = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly Lock _state = new();
    private TaskCompletionSource<FolderMeasure>? _running;
    private FolderMeasure? _latest;

    /// <summary>The newest measurement that finished, or <see langword="null"/> before the first.</summary>
    public FolderMeasure? Latest => Volatile.Read(ref _latest);

    /// <summary>Measures every folder, or joins the measurement already running.</summary>
    /// <returns>The measurement.</returns>
    public Task<FolderMeasure> MeasureAsync()
    {
        TaskCompletionSource<FolderMeasure> mine;

        lock (_state)
        {
            if (_running is { } running)
            {
                return running.Task;
            }

            _running = mine = new TaskCompletionSource<FolderMeasure>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = FinishAsync(mine);

        return mine.Task;
    }

    /// <summary>
    /// The measurement <c>browserai_list</c> answers from: the newest, and a new one started
    /// behind it when it is older than <paramref name="freshFor"/>, which the answer after
    /// this one reads. Waited for only when no measurement has weighed one of
    /// <paramref name="sessions"/> yet.
    /// </summary>
    /// <param name="sessions">The sessions the listing names.</param>
    /// <param name="freshFor">How old a measurement may be and still be the one answered from without a new one.</param>
    /// <returns>The measurement.</returns>
    public async Task<FolderMeasure> ForAsync(IReadOnlyCollection<SessionPath> sessions, TimeSpan freshFor)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var latest = Latest;

        if (latest is null || sessions.Any(session => latest.ById(CensusPageSessions.IdOf(session.FullPath)) is null))
        {
            return await MeasureAsync().ConfigureAwait(false);
        }

        if (clock.GetUtcNow() - latest.MeasuredAt > freshFor)
        {
            // Not awaited: the answer is this measurement, said with its age, and the next
            // one reads the new measurement. A failed one is the next asker's to meet.
            _ = MeasureAsync().ContinueWith(
                static measured => _ = measured.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return latest;
    }

    /// <summary>Runs one measurement, keeps it, and lets the next asker start another.</summary>
    /// <param name="mine">What every asker of this measurement awaits.</param>
    /// <returns>The measurement's end, which never fails.</returns>
    private async Task FinishAsync(TaskCompletionSource<FolderMeasure> mine)
    {
        try
        {
            var measured = await MeasureNowAsync().ConfigureAwait(false);

            lock (_state)
            {
                Volatile.Write(ref _latest, measured);
                _running = null;
            }

            _ = mine.TrySetResult(measured);
        }
#pragma warning disable CA1031 // The measurement's boundary: its failure goes to every asker, who awaits it.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            lock (_state)
            {
                _running = null;
            }

            _ = mine.TrySetException(failure);
        }
    }

    /// <summary>Finds the folders and weighs each on the file-system thread.</summary>
    /// <returns>The measurement.</returns>
    private async Task<FolderMeasure> MeasureNowAsync()
    {
        var folders = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var session in await index.PresentAsync().ConfigureAwait(false))
        {
            _ = folders.TryAdd(session.Key, session.FullPath);
        }

        foreach (var directory in held())
        {
            if (Canonical(directory) is { } session)
            {
                _ = folders.TryAdd(session.Key, session.FullPath);
            }
        }

        var weighed = await FileSystemThread.RunAsync(() => folders.Values.Select(static directory => (Directory: directory, Weight: Weigh(directory))).ToList()).ConfigureAwait(false);

        return new FolderMeasure(
            clock.GetUtcNow(),
            [
                .. weighed
                    .Select(static folder => new SessionFolder(CensusPageSessions.IdOf(folder.Directory), folder.Directory, folder.Weight.Bytes)
                    {
                        OutputBytes = folder.Weight.OutputBytes,
                        OutputFiles = folder.Weight.OutputFiles,
                    })
                    .OrderByDescending(static folder => folder.Bytes ?? -1)
                    .ThenBy(static folder => folder.Directory, StringComparer.OrdinalIgnoreCase),
            ]);
    }

    /// <summary>A held session's folder as the index spells it, or <see langword="null"/> for one that names no session folder.</summary>
    private static SessionPath? Canonical(string directory)
    {
        try
        {
            return SessionPath.For(directory);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// What a folder's files weigh together, or <see langword="null"/> when it could not be
    /// listed, and what its <c>output</c> folder holds, out of the same listing.
    /// </summary>
    private static (long? Bytes, long OutputBytes, int OutputFiles) Weigh(string directory)
    {
        var output = Path.Combine(directory, SessionLayout.OutputFolderName) + Path.DirectorySeparatorChar;

        try
        {
            var bytes = 0L;
            var outputBytes = 0L;
            var outputFiles = 0;

            foreach (var file in FileSystemThread.Here.FilesWithSizes(directory, "*", Every))
            {
                bytes += file.Length;

                if (file.FullName.StartsWith(output, StringComparison.OrdinalIgnoreCase))
                {
                    outputBytes += file.Length;
                    outputFiles++;
                }
            }

            return (bytes, outputBytes, outputFiles);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return (null, 0, 0);
        }
    }
}
