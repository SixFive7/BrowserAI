// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Keeps a red arm's scratch folder where no reclaim reaches it, and puts where it went
/// into the failure.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 94 a, 2026-10-12, in his words verbatim:</b> <i>"94 your
/// recommendation"</i>, which keeps the real Chromium arm of <c>ScreenshotLimitTests</c>'s
/// session folder in the run's evidence when the arm fails. The folder holds the session's
/// record, and the record holds how the browser ended: the close's reason and the browser's
/// exit code. Until then it went with the scratch, which the next run's reclaim deletes
/// whole and every gate driver clears before its run, so seven reds in 77 full runs left
/// nothing that said how the browser had ended.
/// </para>
/// <para>
/// <b>Moved, not copied</b>, into <see cref="Root"/>, under a folder named for the moment,
/// the test host, the arm and eight characters of a GUID, so two kept folders never share
/// a name. A move inside one volume is a rename, so the folder is kept
/// whole however large the browser's profile in it is. It is tried until the folder's
/// last handle has gone, because a process its job has just ended is signalled before the
/// kernel has closed its handles, and bounded by <see cref="TestDefaults.ProcessHang"/>,
/// a hang detector; a move that never succeeds is said in the failure, beside the arm's
/// own message, and replaces nothing.
/// </para>
/// <para>
/// <b>Nothing deletes a kept folder.</b> The gate drivers do not copy it either, so
/// whoever collects a red run's evidence copies the folder the failure names.
/// </para>
/// </remarks>
internal static class KeptOnRed
{
    /// <summary>
    /// <c>&lt;repo&gt;\.work\kept</c>: outside <c>.work\test-scratch</c>, so neither the
    /// reclaim at the head of a run nor a gate driver's clearing of the scratch reaches it.
    /// </summary>
    public static string Root => Path.Combine(RepositoryLayout.Root.FullName, ".work", "kept");

    /// <summary>
    /// Moves <paramref name="folder"/> under <paramref name="keptRoot"/> and answers the
    /// failure to throw in place of <paramref name="failure"/>: its message first, then
    /// where the folder is now.
    /// </summary>
    /// <param name="folder">The arm's scratch folder.</param>
    /// <param name="failure">What the arm failed with, which stays the failure's inner exception.</param>
    /// <param name="arm">A short name for the arm, which the kept folder's name carries.</param>
    /// <param name="keptRoot">Where kept folders go; <see cref="Root"/> unless an arm about this class says otherwise.</param>
    /// <returns>The failure to throw.</returns>
    public static async Task<Exception> KeepAsync(string folder, Exception failure, string arm, string? keptRoot = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(arm);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var parent = Path.Combine(keptRoot ?? Root, $"{stamp}-{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-{arm}-{Guid.NewGuid().ToString("N")[..8]}");
        var destination = Path.Combine(parent, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)));

        string said;

        try
        {
            _ = Directory.CreateDirectory(parent);
            await MoveOnceReleasedAsync(folder, destination);
            said = $"The arm's scratch folder is kept at '{destination}', where no reclaim reaches it. Each session folder in it holds the session's record: every close, with its reason and the browser's exit code.";
        }
        catch (Exception unkept) when (unkept is IOException or UnauthorizedAccessException or TimeoutException)
        {
            said = $"The arm's scratch folder '{folder}' could not be kept at '{destination}': {unkept.GetType().Name}: {unkept.Message}";
        }

        return new InvalidOperationException($"{failure.Message}{Environment.NewLine}{Environment.NewLine}{said}", failure);
    }

    /// <summary>
    /// Renames <paramref name="from"/> to <paramref name="to"/> once the processes that held
    /// files in it have let go.
    /// </summary>
    /// <param name="from">The folder.</param>
    /// <param name="to">Its new name, whose parent exists.</param>
    /// <returns>The move.</returns>
    /// <exception cref="TimeoutException">Something still held the folder when the hang detector ran out.</exception>
    private static async Task MoveOnceReleasedAsync(string from, string to)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception held) when (held is UnauthorizedAccessException or (IOException and not DirectoryNotFoundException))
            {
                if (waited.Elapsed > TestDefaults.ProcessHang)
                {
                    throw new TimeoutException(
                        $"something still held '{from}' {waited.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s after the arm's rig was disposed, so it could not be renamed",
                        held);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25));
            }
        }
    }
}
