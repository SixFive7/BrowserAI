// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using BrowserAI.Sessions;

namespace BrowserAI.Runtime;

/// <summary>
/// The process's one file-system thread: it runs every walk over many files, and every
/// listing, creation, deletion and rename the product makes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decided 2026-10-12 by the maintainer, 80 b and 81 c</b>, in his words verbatim:
/// <i>"78-82 your recommendation"</i>. None of these calls has an asynchronous form: .NET
/// 10's <c>File</c>, <c>Directory</c>, <c>FileInfo</c> and <c>DirectoryInfo</c> carry
/// asynchronous members for whole-file reads and writes only, and Win32's
/// <c>DeleteFile</c>, <c>MoveFileEx</c>, <c>CreateDirectory</c> and
/// <c>FindFirstFile</c> are synchronous. So each runs here, on a thread of its own, and
/// the caller awaits it: a walk over a nine-thousand-file profile holds this thread and
/// no pool worker.
/// </para>
/// <para>
/// <b>The product's ban keeps the calls to this file.</b>
/// <c>src/BrowserAI/BannedSymbols.txt</c> names every listing, creation, deletion and
/// rename member, and the one place they are called is <see cref="Here"/>, each of whose
/// members refuses a caller on any other thread. A walk is one item, written against
/// <see cref="Here"/> and handed to <see cref="RunAsync{T}(Func{T})"/>; a single change
/// is one of the asynchronous members below.
/// </para>
/// <para>
/// <b>One thread, so the walks of the whole process run one after another.</b> A
/// destroy's delete and the dashboard's measurement do not interleave on the disk, and a
/// long walk delays the next: the hazard index has the row.
/// </para>
/// </remarks>
internal static class FileSystemThread
{
    private static readonly DedicatedThread Thread = new("BrowserAI file system");

    /// <summary>Whether the calling code runs on the file-system thread.</summary>
    public static bool IsCurrent => Thread.IsCurrent;

    /// <summary>Runs <paramref name="work"/> on the file-system thread.</summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work, written against <see cref="Here"/>.</param>
    /// <returns>Its result, or its exception.</returns>
    public static Task<T> RunAsync<T>(Func<T> work) => Thread.RunAsync(work);

    /// <summary>Runs <paramref name="work"/> on the file-system thread.</summary>
    /// <param name="work">The work, written against <see cref="Here"/>.</param>
    /// <returns>Its completion, or its exception.</returns>
    public static Task RunAsync(Action work) => Thread.RunAsync(work);

    /// <summary>Deletes a file on the file-system thread. A file that is not there is no failure.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The deletion.</returns>
    public static Task DeleteFileAsync(string path) => RunAsync(() => Here.DeleteFile(path));

    /// <summary>Deletes an empty directory on the file-system thread.</summary>
    /// <param name="path">The directory.</param>
    /// <returns>The deletion.</returns>
    public static Task DeleteEmptyDirectoryAsync(string path) => RunAsync(() => Here.DeleteEmptyDirectory(path));

    /// <summary>Renames a file on the file-system thread.</summary>
    /// <param name="from">The file.</param>
    /// <param name="to">Its new name.</param>
    /// <param name="overwrite">Whether a file already at <paramref name="to"/> is replaced.</param>
    /// <returns>The rename.</returns>
    public static Task MoveFileAsync(string from, string to, bool overwrite) => RunAsync(() => Here.MoveFile(from, to, overwrite));

    /// <summary>
    /// Moves a file over the one at <paramref name="to"/> on the file-system thread, and
    /// tries again while something holds that file open, for as long as the rename window's
    /// budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-13 with the maintainer's 157 a</b>, in his words verbatim:
    /// <i>"157 a"</i>. Windows refuses a rename over a file that any process has open,
    /// whatever its share mode, with <c>ERROR_ACCESS_DENIED</c>
    /// (<see href="../../../kb/windows/processes.md">the kb</see>, measured 2026-08-16), so
    /// a reader that opened a record at the instant it was replaced made the write fail, and
    /// five writers gave up on that first refusal: the after-update notice's record, the
    /// page listener's memory, the record of the last update check, a session's
    /// <c>browserai.lock</c> and the headed user agent's cache. A notice marked seen came
    /// back after the next start. Each of them moves its record into place through here.
    /// </para>
    /// <para>
    /// <b>What is waited out is a refusal that clears</b>: the denial a rename over an open
    /// file and a file that is itself being replaced both give, and a sharing violation.
    /// Anything else, a source that is not there or a directory that is gone, is answered
    /// at once. The budget and the pauses are <see cref="SessionTimes.RenameBudget"/>,
    /// <see cref="SessionTimes.RenameRetryFirstDelayMilliseconds"/> and
    /// <see cref="SessionTimes.RenameRetryLongestDelayMilliseconds"/>, the numbers
    /// <c>RenameWindow</c> waits out a reader's side of the same refusal with. The record of
    /// a background and the session index keep loops of their own, with the budgets their
    /// remarks give.
    /// </para>
    /// </remarks>
    /// <param name="from">The file, written in full beside the one it replaces.</param>
    /// <param name="to">The file it replaces.</param>
    /// <returns>The move, once the file is in place.</returns>
    /// <exception cref="UnauthorizedAccessException">The destination was still held when the budget ran out.</exception>
    /// <exception cref="IOException">The move failed for another reason, or a sharing violation outlasted the budget.</exception>
    public static Task ReplaceFileAsync(string from, string to) =>
        WaitOutABusyDestinationAsync(() => MoveFileAsync(from, to, overwrite: true));

    /// <summary>
    /// Makes a move, and makes it again after each refusal that a file somebody holds
    /// gives, until the move goes through or the rename window's budget runs out.
    /// </summary>
    /// <remarks>
    /// <b>The loop of <see cref="ReplaceFileAsync"/>, with the move handed in</b>, so the
    /// suite can drive it with refusals of its own making
    /// (<c>DedicatedThreadTests</c>). The first move is handed over before this returns its
    /// task, and every pause between two is awaited, so no thread waits in it.
    /// </remarks>
    /// <param name="move">One try of the move.</param>
    /// <returns>The move, once a try has gone through.</returns>
    internal static async Task WaitOutABusyDestinationAsync(Func<Task> move)
    {
        ArgumentNullException.ThrowIfNull(move);

        var clock = Stopwatch.StartNew();
        var delay = SessionTimes.RenameRetryFirstDelayMilliseconds;

        while (true)
        {
            try
            {
                await move().ConfigureAwait(false);
                return;
            }
            catch (UnauthorizedAccessException) when (clock.Elapsed < SessionTimes.RenameBudget)
            {
            }
            catch (IOException failure) when (IsSharingViolation(failure) && clock.Elapsed < SessionTimes.RenameBudget)
            {
            }

            await Task.Delay(delay).ConfigureAwait(false);
            delay = Math.Min(delay * 2, SessionTimes.RenameRetryLongestDelayMilliseconds);
        }
    }

    private const int ErrorSharingViolation = 32;

    private const int ErrorLockViolation = 33;

    /// <summary>Whether a failed move was refused because somebody holds a file in a mode that excludes it.</summary>
    /// <remarks>The two codes <c>RenameWindow.IsSharingViolation</c> reads, for the one event Windows reports both ways.</remarks>
    /// <param name="failure">The refusal.</param>
    /// <returns><see langword="true"/> when the refusal is a sharing or a lock violation.</returns>
    private static bool IsSharingViolation(IOException failure) =>
        (failure.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;

    /// <summary>Renames a directory on the file-system thread.</summary>
    /// <param name="from">The directory.</param>
    /// <param name="to">Its new name.</param>
    /// <returns>The rename.</returns>
    public static Task MoveDirectoryAsync(string from, string to) => RunAsync(() => Here.MoveDirectory(from, to));

    /// <summary>Creates a directory and every missing parent on the file-system thread.</summary>
    /// <param name="path">The directory.</param>
    /// <returns>The directory.</returns>
    public static Task<DirectoryInfo> CreateDirectoryAsync(string path) => RunAsync(() => Here.CreateDirectory(path));

    /// <summary>
    /// The calls the product's ban keeps to the file-system thread. Each refuses a caller
    /// on any other thread, and each listing is read to its end before it returns, so no
    /// enumeration carries its I/O off this thread.
    /// </summary>
    internal static class Here
    {
        /// <summary>Deletes a file. A file that is not there is no failure.</summary>
        /// <param name="path">The file.</param>
        public static void DeleteFile(string path)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 81 c: the file-system thread is the one place the product deletes a file.
            File.Delete(path);
#pragma warning restore RS0030
        }

        /// <summary>Deletes an empty directory.</summary>
        /// <param name="path">The directory.</param>
        public static void DeleteEmptyDirectory(string path)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 81 c: the file-system thread is the one place the product deletes a directory.
            Directory.Delete(path);
#pragma warning restore RS0030
        }

        /// <summary>Renames a file.</summary>
        /// <param name="from">The file.</param>
        /// <param name="to">Its new name.</param>
        /// <param name="overwrite">Whether a file already at <paramref name="to"/> is replaced.</param>
        public static void MoveFile(string from, string to, bool overwrite)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 81 c: the file-system thread is the one place the product renames a file.
            File.Move(from, to, overwrite);
#pragma warning restore RS0030
        }

        /// <summary>Renames a directory.</summary>
        /// <param name="from">The directory.</param>
        /// <param name="to">Its new name.</param>
        public static void MoveDirectory(string from, string to)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 81 c: the file-system thread is the one place the product renames a directory.
            Directory.Move(from, to);
#pragma warning restore RS0030
        }

        /// <summary>Creates a directory and every missing parent.</summary>
        /// <param name="path">The directory.</param>
        /// <returns>The directory.</returns>
        public static DirectoryInfo CreateDirectory(string path)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 81 c: the file-system thread is the one place the product creates a directory.
            return Directory.CreateDirectory(path);
#pragma warning restore RS0030
        }

        /// <summary>The files directly in a directory, by full path.</summary>
        /// <param name="directory">The directory.</param>
        /// <returns>Their paths.</returns>
        public static IReadOnlyList<string> Files(string directory)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 80 b: the file-system thread is the one place the product lists a directory.
            return [.. Directory.EnumerateFiles(directory)];
#pragma warning restore RS0030
        }

        /// <summary>The files in a directory that match a pattern, by full path.</summary>
        /// <param name="directory">The directory.</param>
        /// <param name="pattern">The pattern, as <c>FindFirstFile</c> reads one.</param>
        /// <returns>Their paths.</returns>
        public static IReadOnlyList<string> Files(string directory, string pattern)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 80 b: the file-system thread is the one place the product lists a directory.
            return [.. Directory.EnumerateFiles(directory, pattern)];
#pragma warning restore RS0030
        }

        /// <summary>The files under a directory that match a pattern, by full path.</summary>
        /// <param name="directory">The directory.</param>
        /// <param name="pattern">The pattern.</param>
        /// <param name="options">How deep, and what to skip.</param>
        /// <returns>Their paths.</returns>
        public static IReadOnlyList<string> Files(string directory, string pattern, EnumerationOptions options)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 80 b: the file-system thread is the one place the product lists a directory.
            return [.. Directory.EnumerateFiles(directory, pattern, options)];
#pragma warning restore RS0030
        }

        /// <summary>The files in a directory, and in every directory under it when asked, that match a pattern.</summary>
        /// <param name="directory">The directory.</param>
        /// <param name="pattern">The pattern.</param>
        /// <param name="search">Whether the directories under it are searched too.</param>
        /// <returns>Their paths.</returns>
        public static IReadOnlyList<string> Files(string directory, string pattern, SearchOption search)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 80 b: the file-system thread is the one place the product lists a directory.
            return [.. Directory.EnumerateFiles(directory, pattern, search)];
#pragma warning restore RS0030
        }

        /// <summary>The first file under a directory that matches a pattern, or <see langword="null"/>.</summary>
        /// <param name="directory">The directory.</param>
        /// <param name="pattern">The pattern.</param>
        /// <param name="options">How deep, and what to skip.</param>
        /// <returns>Its path, the listing stopped where it was found.</returns>
        public static string? FirstFile(string directory, string pattern, EnumerationOptions options)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 80 b: the file-system thread is the one place the product lists a directory.
            return Directory.EnumerateFiles(directory, pattern, options).FirstOrDefault();
#pragma warning restore RS0030
        }

        /// <summary>The directories directly in a directory, by full path.</summary>
        /// <param name="directory">The directory.</param>
        /// <returns>Their paths.</returns>
        public static IReadOnlyList<string> Directories(string directory)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 80 b: the file-system thread is the one place the product lists a directory.
            return [.. Directory.EnumerateDirectories(directory)];
#pragma warning restore RS0030
        }

        /// <summary>
        /// The files under a directory that match a pattern, each with the size and times its
        /// directory entry carries, so reading them asks the disk nothing more.
        /// </summary>
        /// <param name="directory">The directory.</param>
        /// <param name="pattern">The pattern.</param>
        /// <param name="options">How deep, and what to skip.</param>
        /// <returns>The files.</returns>
        public static IReadOnlyList<FileInfo> FilesWithSizes(string directory, string pattern, EnumerationOptions options)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 80 b: the file-system thread is the one place the product lists a directory.
            return [.. new DirectoryInfo(directory).EnumerateFiles(pattern, options)];
#pragma warning restore RS0030
        }

        /// <summary>The files directly in a directory that match a pattern, each with what its directory entry carries.</summary>
        /// <param name="directory">The directory.</param>
        /// <param name="pattern">The pattern.</param>
        /// <returns>The files.</returns>
        public static IReadOnlyList<FileInfo> FilesWithSizes(string directory, string pattern)
        {
            Require();
#pragma warning disable RS0030 // Kept at the maintainer's 80 b: the file-system thread is the one place the product lists a directory.
            return [.. new DirectoryInfo(directory).EnumerateFiles(pattern)];
#pragma warning restore RS0030
        }

        private static void Require()
        {
            if (!IsCurrent)
            {
                throw new InvalidOperationException(
                    "A listing, creation, deletion or rename was called off the file-system thread, the one thread the product makes these calls on. "
                    + "Hand it to FileSystemThread.RunAsync (the maintainer's 80 b and 81 c).");
            }
        }
    }
}
