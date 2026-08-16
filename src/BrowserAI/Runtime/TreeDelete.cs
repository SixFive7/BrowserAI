// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Runtime;

/// <summary>
/// The one recursive delete in this product: a post-order walk with a try/catch
/// per node, which reports what it could not remove instead of failing whole.
/// </summary>
/// <remarks>
/// <para>
/// <b>The framework primitive is the wrong shape, and the reason is that it
/// fails whole rather than per node.</b>
/// <c>Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)</c>
/// aborts the <i>entire</i> enumeration on the first
/// <c>UnauthorizedAccessException</c> — one unreadable subdirectory anywhere and
/// every entry after it is never yielded, including the thousands that would
/// have deleted cleanly. <c>Directory.Delete(path, recursive: true)</c> inherits
/// that behaviour because that is how it walks. The caller then sees an
/// exception and no partial progress, which converts one locked file into a
/// whole tree nobody ever cleans up.
/// </para>
/// <para>
/// <b>Deleting nine thousand files and reporting the eleven that were locked is
/// a better outcome than deleting nothing and reporting one exception</b>, and
/// it is the one the framework primitive cannot produce.
/// </para>
/// <para>
/// <b>Three callers, three different reasons to meet the failure</b>
/// ([§E](../../plan/E-lifecycle.md#deleting-a-tree-that-fights-back)).
/// <c>browserai_destroy</c> deletes a directory that has just held a running
/// browser, and Chromium leaves mapped files behind for a moment after exit —
/// the race is the normal case rather than the unlucky one.
/// <c>browserai_reinstall_browser</c> removes a browser tree that ~100
/// concurrent processes might still be reading, which is why it refuses while
/// any session has a live browser — and refusing is not the same as being safe,
/// because a leaked handle from a crashed run answers to nobody. The Velopack
/// swap is the third, and arrives at [§G](../../plan/G-updates.md).
/// </para>
/// <para>
/// <b>It is one routine and not three.</b> Each caller writing its own is how
/// two of them end up with the framework call and the third with this one, and
/// nothing reports the difference until a locked file appears in production.
/// </para>
/// </remarks>
internal static class TreeDelete
{
    /// <summary>
    /// Deletes a directory and everything under it, recording what would not go.
    /// </summary>
    /// <param name="directory">The directory to remove. A path that does not exist is not a failure.</param>
    /// <param name="failures">
    /// Every node that could not be deleted, one line each, already indented for
    /// a report. The list is appended to rather than cleared.
    /// </param>
    public static void Remove(string directory, List<string> failures)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(failures);

        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(directory)))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                failures.Add($"  {file}: {failure.Message}");
            }
        }

        foreach (var child in SafeEnumerate(() => Directory.EnumerateDirectories(directory)))
        {
            Remove(child, failures);
        }

        try
        {
            // Post-order: a directory cannot be removed while it still holds
            // entries, so this is the last thing that happens at every level.
            Directory.Delete(directory);
        }
        catch (DirectoryNotFoundException)
        {
            // Never a failure. A caller deleting something that is already gone
            // has the outcome it asked for, and reporting it would make an
            // ordinary reinstall look partial.
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            failures.Add($"  {directory}\\: {failure.Message}");
        }
    }

    /// <summary>
    /// Enumerates one level, answering with nothing when the level cannot be
    /// read.
    /// </summary>
    /// <remarks>
    /// Materialised rather than returned lazily on purpose: the walk deletes
    /// what it enumerates, and a lazy enumerator over a directory being emptied
    /// under it is undefined.
    /// </remarks>
    private static IReadOnlyList<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return [.. enumerate()];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
