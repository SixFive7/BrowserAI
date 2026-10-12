// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Runtime;

/// <summary>
/// The one recursive delete in this product: a post-order walk with a try/catch
/// per node, which reports what it could not remove instead of failing whole.
/// </summary>
/// <remarks>
/// <para>
/// <b>Post-order, because a directory cannot be removed while it holds
/// entries.</b> The walk descends into each subdirectory and deletes its
/// contents before deleting the directory itself; a node that will not go is
/// recorded and skipped, and the walk continues through its siblings and its
/// parents' siblings. The result is a count of what went and a list of what did
/// not, which is what the caller reports.
/// </para>
/// <para>
/// <b><c>Directory.GetFiles(path)</c> is top-level only</b> unless
/// <c>AllDirectories</c> is passed, so the safe-looking alternative is not a
/// recursive delete at all -- it silently leaves every subdirectory in place, and
/// the failure is an empty-looking result, not an error.
/// </para>
/// <para>
/// <b>The framework primitive is the wrong shape, and the reason is that the
/// caller is told one thing and not everything.</b>
/// <c>Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)</c>
/// aborts the <i>entire</i> enumeration on the first
/// <c>UnauthorizedAccessException</c> -- one unreadable subdirectory anywhere and
/// every entry after it is never yielded, including the thousands that would
/// have deleted cleanly.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-16 (previously "<c>Directory.Delete(path, recursive:
/// true)</c> inherits that behaviour because that is how it walks. The caller
/// then sees an exception and no partial progress, which converts one locked
/// file into a whole tree nobody ever cleans up").</b> Measured twice on .NET
/// 10.0.11 ([kb](../../../kb/windows/processes.md#files-durable-writes-and-deletes)): the
/// recursive delete <b>does</b> make partial progress. Against a tree with one
/// file held <c>FileShare.None</c>, and again against one holding a subdirectory
/// the caller may not read, it removed everything else and threw <b>one</b>
/// exception naming <b>one</b> node. The on-disk outcome was identical to this
/// routine's, node for node. <b>What differs is the report</b> -- where the
/// framework named one node, the per-node walk named four and two respectively --
/// and that is not a cosmetic difference here, because
/// <c>browserai_destroy</c>'s answer <i>is</i> the list of what survived, and an
/// instance directory nobody can attribute is one nobody ever unblocks.
/// </para>
/// <para>
/// <b>Deleting nine thousand files and reporting the eleven that were locked is
/// a better outcome than deleting nine thousand and reporting one</b>, and it is
/// the one the framework primitive cannot produce.
/// </para>
/// <para>
/// ⚠️ <b>A directory reparse point is removed as a <i>link</i>, and this walk
/// did not check for one until 2026-08-18.</b>
/// <c>Directory.EnumerateDirectories</c> returns a junction or a directory
/// symlink as an ordinary directory, so the walk descended into it and deleted
/// the <b>target's</b> contents -- outside the tree it was asked to remove, on a
/// path a caller named. <c>browserai_destroy</c> takes that path from the model,
/// and a browser profile is a tree in which junctions are ordinary: a
/// <c>Cache</c> moved to another volume, a <c>downloads</c> junction to the real
/// Downloads folder, a redirected <c>AppData</c> beneath it. One junction and
/// the destroy emptied the target.
/// </para>
/// <para>
/// <b>So the hand-rolled replacement was <i>less</i> safe than the banned call
/// in exactly this dimension</b>, and the shape of it is this:
/// <c>Directory.Delete(path, recursive: true)</c> checks
/// <c>FILE_ATTRIBUTE_REPARSE_POINT</c> during its own walk and calls
/// <c>RemoveDirectory</c> on the link. Neither this file's remarks, nor
/// <c>Runtime\AGENTS.md</c>, nor the ban message in
/// [`build/BannedSymbols.txt`](../../../build/BannedSymbols.txt) mentioned
/// reparse points at all -- the ban justified itself on <i>reporting</i> alone,
/// so a reader comparing the two would have concluded this routine was a
/// superset. It is now, and the ban message says which two things it is a
/// superset of. Found by
/// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-processes.md),
/// finding 2; <c>TreeDeleteTests</c> settles it with a real junction on every
/// run.
/// </para>
/// <para>
/// <b>Three callers, three different reasons to meet the failure.</b>
/// <c>browserai_destroy</c> deletes a directory that has just held a running
/// browser, and Chromium leaves mapped files behind for a moment after exit --
/// the race is the normal case, not the unlucky one.
/// <c>browserai_reinstall_browser</c> removes a browser tree that ~100
/// concurrent processes might still be reading, which is why it refuses while
/// any session has a live browser -- and refusing is not the same as being safe,
/// because a leaked handle from a crashed run answers to nobody.
/// <c>InstanceDirectory</c> is the third: the same just-held-a-browser
/// race, on a path taken at every clean exit and every startup sweep.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-16 (previously "The Velopack swap is the third, and
/// arrives with the update path").</b> It shipped and it never
/// arrived, because the swap is <c>force_stop_package</c> -- upstream's own
/// binary, which does not call into this. The third caller was
/// <c>InstanceDirectory</c> all along, and it was using the framework
/// primitive: found by [the plan's final audit](../../../TODO.md), which is exactly
/// the outcome the paragraph below predicts.
/// </para>
/// <para>
/// <b>It is one routine and not three.</b> Each caller writing its own is how
/// two of them end up with the framework call and the third with this one, and
/// nothing reports the difference until a locked file appears in production.
/// </para>
/// <para>
/// ⚠️ <b>The product's walk runs on the file-system thread since 2026-10-12</b>, the
/// maintainer's 79 c and 80 b, in his words verbatim: <i>"78-82 your
/// recommendation"</i>. <see cref="RemoveAsync"/> hands the whole walk to
/// <see cref="FileSystemThread"/> as one item and awaits it, so a nine-thousand-file
/// profile holds that thread and no pool worker. The walk takes the four calls it makes
/// as an <see cref="ITreeCalls"/>: the product's are <see cref="FileSystemThread.Here"/>'s,
/// which refuse any other thread, and the suite passes its own for its scratch, which
/// the product's ban does not reach. <i>Corrected that day (previously a synchronous
/// <c>Remove</c> on the caller's thread).</i>
/// </para>
/// </remarks>
internal static class TreeDelete
{
    /// <summary>
    /// Deletes a directory and everything under it on the file-system thread, recording
    /// what would not go.
    /// </summary>
    /// <param name="directory">The directory to remove. A path that does not exist is not a failure.</param>
    /// <param name="failures">
    /// Every node that could not be deleted, one line each, already indented for
    /// a report. The list is appended to, not cleared, and it is complete once the
    /// returned task is.
    /// </param>
    /// <returns>The walk.</returns>
    public static Task RemoveAsync(string directory, List<string> failures)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(failures);

        return FileSystemThread.RunAsync(() => Walk(directory, failures, OnTheFileSystemThread.Instance));
    }

    /// <summary>
    /// The product's walk inside an item already running on the file-system thread, for a
    /// walk that deletes a tree as one step of a larger one.
    /// </summary>
    /// <param name="directory">The directory to remove. A path that does not exist is not a failure.</param>
    /// <param name="failures">Every node that could not be deleted, appended to.</param>
    /// <exception cref="InvalidOperationException">The caller is not on the file-system thread.</exception>
    internal static void RemoveHere(string directory, List<string> failures) =>
        Walk(directory, failures, OnTheFileSystemThread.Instance);

    /// <summary>
    /// The walk itself, on the calling thread, through the calls it is handed.
    /// </summary>
    /// <param name="directory">The directory to remove. A path that does not exist is not a failure.</param>
    /// <param name="failures">Every node that could not be deleted, appended to.</param>
    /// <param name="calls">
    /// The listings and deletions the walk makes. The product's are the file-system
    /// thread's, through <see cref="RemoveAsync"/>, and refuse any other thread.
    /// </param>
    internal static void Walk(string directory, List<string> failures, ITreeCalls calls)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(calls);

        if (IsReparsePoint(directory))
        {
            // A junction, a directory symlink or a volume mount point is
            // removed AS A LINK. Descending into one would delete files that
            // are not under the tree this call was given, on a path a caller
            // named -- see the remarks above.
            RemoveOneDirectory(directory, failures, calls);
            return;
        }

        foreach (var file in SafeEnumerate(() => calls.Files(directory)))
        {
            try
            {
                calls.DeleteFile(file);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                var second = RetryWithoutTheReadOnlyAttribute(file, failure, calls);

                if (second is not null)
                {
                    failures.Add($"  {file}: {second.Message}");
                }
            }
        }

        foreach (var child in SafeEnumerate(() => calls.Directories(directory)))
        {
            Walk(child, failures, calls);
        }

        // Post-order: a directory cannot be removed while it still holds
        // entries, so this is the last thing that happens at every level.
        RemoveOneDirectory(directory, failures, calls);
    }

    /// <summary>
    /// Whether a directory is a reparse point -- a junction, a directory symlink
    /// or a volume mount point -- and must therefore be unlinked and not
    /// walked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An attribute that cannot be read answers <see langword="true"/>, and
    /// that is the safe direction.</b> Treating an unreadable directory as a
    /// link costs one <c>RemoveDirectory</c> that fails if it has contents,
    /// which lands in the failure list and is reported. Treating a link as an
    /// ordinary directory costs somebody else's files. A directory whose
    /// attributes cannot be read cannot be enumerated either, so the answer
    /// changes nothing for the ordinary case.
    /// </para>
    /// <para>
    /// <b>A volume mount point is unlinked, not dismounted.</b>
    /// <c>RemoveDirectory</c> removes the reparse point and leaves an entry in
    /// the mount manager's database, where the framework primitive calls
    /// <c>DeleteVolumeMountPoint</c> first. That residue is a stale name in a
    /// registry key, not data -- and it is the only respect in which this routine
    /// still does less than the call it replaced. Named, not left to be
    /// discovered.
    /// </para>
    /// </remarks>
    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            // Already gone. Not a link, and the delete below will say so.
            return false;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    /// Clears the read-only attribute and deletes again, answering with the
    /// failure a caller should report, or <see langword="null"/> when the node
    /// is gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-09-17, off a real failure.</b> Windows refuses to delete
    /// a file carrying <c>FILE_ATTRIBUTE_READONLY</c> with
    /// <c>ERROR_ACCESS_DENIED</c> -- <b>the same code a held handle produces</b>,
    /// so the survivor list this routine writes read like a lock for as long as
    /// it did. Git writes every loose object read-only, and that is how it was
    /// found: a scratch tree holding a real repository could not be removed, by
    /// this routine or by the suite's reclaim pass, and a release gate went red
    /// at the head of a run on a tree nobody had changed.
    /// </para>
    /// <para>
    /// <b>The attribute is cleared only after a delete has already been
    /// refused</b>, so the ordinary path is one call and unchanged, and a
    /// genuine sharing violation is still reported and not retried into
    /// silence: the retry's own failure is what goes into the list, and the
    /// first failure is returned unchanged when there was no attribute to
    /// clear.
    /// </para>
    /// <para>
    /// <b>It is not an escalation.</b> Every caller of this routine is deleting
    /// a tree BrowserAI owns or was handed by name, and removing an attribute
    /// from a file that is being deleted in the same breath changes nothing that
    /// outlives the call. What it buys is that ordinary content -- anything a
    /// session downloaded, anything a user dropped into a directory
    /// <c>browserai_destroy</c> is given -- stops being reported as a node the
    /// product could not remove when it could.
    /// </para>
    /// </remarks>
    /// <param name="path">The file or directory the first delete refused.</param>
    /// <param name="first">What that delete threw.</param>
    /// <param name="calls">The deletions to retry with.</param>
    /// <returns>The failure to report, or <see langword="null"/> if the node is gone.</returns>
    private static Exception? RetryWithoutTheReadOnlyAttribute(string path, Exception first, ITreeCalls calls)
    {
        try
        {
            var attributes = File.GetAttributes(path);

            if (!attributes.HasFlag(FileAttributes.ReadOnly))
            {
                // Nothing to clear, so the first failure is the whole story and
                // is reported unchanged. This is the held-handle path.
                return first;
            }

            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return first;
        }

        try
        {
            if (Directory.Exists(path))
            {
                calls.DeleteEmptyDirectory(path);
            }
            else
            {
                calls.DeleteFile(path);
            }

            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return failure;
        }
    }

    /// <summary>Removes one directory, recording it if it will not go.</summary>
    private static void RemoveOneDirectory(string directory, List<string> failures, ITreeCalls calls)
    {
        try
        {
            calls.DeleteEmptyDirectory(directory);
        }
        catch (DirectoryNotFoundException)
        {
            // Never a failure. A caller deleting something that is already gone
            // has the outcome it asked for, and reporting it would make an
            // ordinary reinstall look partial.
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            var second = RetryWithoutTheReadOnlyAttribute(directory, failure, calls);

            if (second is not null)
            {
                failures.Add($"  {directory}\\: {second.Message}");
            }
        }
    }

    /// <summary>
    /// Enumerates one level, answering with nothing when the level cannot be
    /// read.
    /// </summary>
    /// <remarks>
    /// Materialised and not returned lazily, on purpose: the walk deletes
    /// what it enumerates, and a lazy enumerator over a directory being emptied
    /// under it is undefined.
    /// </remarks>
    private static IReadOnlyList<string> SafeEnumerate(Func<IReadOnlyList<string>> enumerate)
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

    /// <summary>The product's calls: the file-system thread's, which refuse any other thread.</summary>
    private sealed class OnTheFileSystemThread : ITreeCalls
    {
        public static readonly OnTheFileSystemThread Instance = new();

        public IReadOnlyList<string> Files(string directory) => FileSystemThread.Here.Files(directory);

        public IReadOnlyList<string> Directories(string directory) => FileSystemThread.Here.Directories(directory);

        public void DeleteFile(string path) => FileSystemThread.Here.DeleteFile(path);

        public void DeleteEmptyDirectory(string path) => FileSystemThread.Here.DeleteEmptyDirectory(path);
    }
}

/// <summary>
/// The four calls <see cref="TreeDelete"/>'s walk makes: list a directory's files and its
/// directories, delete a file and delete an empty directory.
/// </summary>
/// <remarks>
/// The product's are <see cref="FileSystemThread.Here"/>'s, which the product's ban keeps
/// on the file-system thread. The suite hands the walk its own, for the scratch it removes.
/// </remarks>
internal interface ITreeCalls
{
    /// <summary>The files directly in a directory, by full path.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>Their paths, read to the end.</returns>
    IReadOnlyList<string> Files(string directory);

    /// <summary>The directories directly in a directory, by full path.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>Their paths, read to the end.</returns>
    IReadOnlyList<string> Directories(string directory);

    /// <summary>Deletes a file.</summary>
    /// <param name="path">The file.</param>
    void DeleteFile(string path);

    /// <summary>Deletes an empty directory.</summary>
    /// <param name="path">The directory.</param>
    void DeleteEmptyDirectory(string path);
}
