// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using BrowserAI.Hosting;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Sessions;

/// <summary>
/// The only inventory of session directories: one file per directory, named for
/// the hash of its canonical path, holding that path and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// There is no root to scan, because there is no default session directory — a
/// session lives wherever the caller said. That makes this store load-bearing,
/// and it is therefore built to <b>fail safe rather than to be correct under
/// every race</b>.
/// </para>
/// <para>
/// <b>Never trusted, only followed.</b> An entry is a pointer, never an
/// authorisation. Every entry is verified by opening the <c>lock.json</c> it
/// points at, and a directory that has none is reported as not ours and acted on
/// in no other way. That is what makes an entry that somehow named a personal
/// Chrome profile harmless: it is an inventory line, and nothing downstream may
/// treat it as permission to touch anything.
/// </para>
/// <para>
/// <b>No lock, by design.</b> Create and delete are atomic per file, so there is
/// no read-modify-write to synchronise and nothing for a mutex to protect. A
/// wrongly-deleted entry is restored by the next <c>init</c> or <c>resume</c>,
/// which costs one sweep cycle of invisibility rather than an orphaned
/// directory. Locking it would put a machine-wide lock on the hot path of every
/// session start to close a race whose cost is that cycle.
/// </para>
/// <para>
/// <b>Recording never throws, and never fails a session.</b> The index is an
/// inventory; a session whose <c>init</c> failed because its inventory line
/// could not be written would make a self-healing store load-bearing in the one
/// direction it was designed not to be. A failure is logged at Warning, with the
/// reason, and the next use re-asserts the entry.
/// </para>
/// <para>
/// <b>The write is <i>not</i> durable, and that is the difference from
/// <see cref="SessionLock"/>.</b> <c>lock.json</c> is written
/// <c>WriteThrough</c> plus <c>Flush(flushToDisk: true)</c> — measured at ~17 ms
/// — because it is the whole ownership guard and its loss cannot be
/// reconstructed. An index entry can: it is a pure function of a directory that
/// is about to be used again. Paying a disk round trip on every <c>init</c> and
/// every <c>resume</c> to protect a fact that regenerates itself would be the
/// cost without the reason.
/// </para>
/// </remarks>
internal sealed class SessionIndex
{
    /// <summary>
    /// The longest entry file this will read. A canonical Windows path is
    /// bounded by ~32,767 characters with long paths enabled, so this is
    /// generous headroom over a legal pointer while still bounding what a file
    /// dropped into the directory by something else can cost.
    /// </summary>
    private const int MaximumEntryBytes = 128 * 1024;

    /// <summary>A byte-order mark a person or an editor may have left on a repaired entry.</summary>
    private const char Bom = '\uFEFF';

    /// <summary>How long a rename keeps retrying before the entry is given up on.</summary>
    /// <remarks>
    /// Shorter than <see cref="SessionLock"/>'s two seconds, and deliberately so.
    /// Nothing holds an index entry open — this store's own reader shares
    /// <c>Delete</c> — so contention here means something outside BrowserAI has
    /// the file, and the fail-safe answer is to give up quickly and let the next
    /// use re-assert rather than to stall a session start.
    /// </remarks>
    private static readonly TimeSpan MoveBudget = TimeSpan.FromMilliseconds(500);

    /// <summary>How old our own rename litter must be before a sweep clears it.</summary>
    /// <remarks>
    /// A temp file exists for microseconds between its creation and the rename,
    /// and is deleted in a <c>finally</c> on every ordinary path. One that
    /// survives belonged to a process that was killed inside that window. The
    /// age bound is what stops a sweep deleting a live writer's temp, and the
    /// name pattern is what stops it ever touching a file this product did not
    /// write.
    /// </remarks>
    private static readonly TimeSpan LitterAge = TimeSpan.FromHours(1);

    private readonly IAppPaths _paths;
    private readonly ILogger _logger;

    /// <summary>Creates an index over the app-paths seam.</summary>
    /// <param name="paths">Where the index root is. Never composed at a call site.</param>
    /// <param name="logger">Where a failure to record is reported.</param>
    public SessionIndex(IAppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _logger = logger;
    }

    /// <summary>The directory holding the entries.</summary>
    public string Root => _paths.IndexDirectory;

    /// <summary>
    /// Re-asserts the entry for a session directory. Idempotent, called on every
    /// <c>init</c> and every <c>resume</c>.
    /// </summary>
    /// <param name="session">The canonicalised session directory.</param>
    /// <remarks>
    /// Re-asserting rather than writing-once is what makes a lost entry
    /// self-heal, and it is what lets this store skip locking entirely. The
    /// write is unconditional — there is no "already correct, skip it" fast path
    /// — because the check would cost a read on the same hot path it saves a
    /// write on, and because a fast path that skips under contention is a fast
    /// path that makes the concurrency test prove nothing.
    /// </remarks>
    public void Record(SessionPath session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var entry = Path.Combine(Root, session.IndexKey);
        var temp = $"{entry}.new-{Guid.NewGuid():N}";

        try
        {
            _ = Directory.CreateDirectory(Root);

            try
            {
                // The path and nothing else: UTF-8, no BOM, no trailing newline,
                // no wrapper object. Anything more is a schema, and a schema is
                // a thing that can be wrong; this file cannot be wrong in a way
                // that matters, because it is re-derived from the directory it
                // names on every use.
                File.WriteAllBytes(temp, Encoding.UTF8.GetBytes(session.FullPath));
                Replace(temp, entry);
            }
            finally
            {
                TryDelete(temp);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Never fatal. The session is real whether or not it is inventoried,
            // and the next init or resume re-asserts this. Logged rather than
            // swallowed: silence is the enemy, a lost line is not.
            SessionIndexLog.CouldNotRecord(_logger, entry, session.FullPath, failure);
        }
    }

    /// <summary>
    /// Removes one session's entry, because the directory it pointed at has just
    /// been destroyed.
    /// </summary>
    /// <param name="session">The canonicalised session directory.</param>
    /// <remarks>
    /// A sweep would remove it anyway — a pointer to a directory that no longer
    /// exists is removable by definition — so this only decides <i>when</i>. It
    /// is worth doing promptly because <c>browserai_list</c> reads the index, and
    /// a destroyed session lingering in an inventory is exactly the kind of
    /// confident wrong answer this project exists to remove. Never fatal, for the
    /// same reason <see cref="Record"/> is not: the entry is re-derivable and a
    /// stale one is a report rather than a fault.
    /// </remarks>
    public void Forget(SessionPath session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _ = TryDelete(Path.Combine(Root, session.IndexKey));
    }

    /// <summary>
    /// Reads every entry and follows it — the only way this store is ever read.
    /// </summary>
    /// <returns>Every entry, each with what following it found, ordered by key.</returns>
    /// <remarks>
    /// <b>Following is verification, not trust.</b> Nothing here decides that a
    /// directory is ours because it is listed; it decides that by opening the
    /// <c>lock.json</c> inside it. The states this returns are an inventory
    /// report and confer no authority on anything.
    /// </remarks>
    public IReadOnlyList<SessionIndexEntry> Follow()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        var entries = new List<SessionIndexEntry>();

        foreach (var file in Directory.EnumerateFiles(Root).Order(StringComparer.OrdinalIgnoreCase))
        {
            var key = Path.GetFileName(file);

            // Anything that is not a key is not an entry. That covers a live
            // writer's rename temp, and anything else that found its way in.
            if (!IsKey(key))
            {
                continue;
            }

            entries.Add(FollowOne(file, key));
        }

        return entries;
    }

    /// <summary>
    /// Removes every entry that can never be followed to a session, and this
    /// store's own rename litter.
    /// </summary>
    /// <returns>What was followed, what was removed and what was kept.</returns>
    /// <remarks>
    /// <para>
    /// The index shrinks as sessions are destroyed, without anyone maintaining
    /// it. <b>Nothing here ever touches a session directory</b> — an entry is a
    /// pointer, and removing a pointer is the only action this store has.
    /// </para>
    /// <para>
    /// <b>Two states are deliberately <i>kept</i> that a first reading of
    /// [§D](../../../plan/D-locking.md#the-session-index-on-disk) would remove.</b> A
    /// directory whose <c>lock.json</c> is present but unparseable is a session
    /// — a broken one — and it cannot restore its own entry, because
    /// <see cref="SessionLock.TryAcquire"/> refuses an unreadable record. And a
    /// path on a volume that is not mounted has not been destroyed; the drive is
    /// simply not there. Removing either would make a directory that still exists
    /// permanently invisible to the only inventory there is, which is this
    /// project's founding failure shape rather than a tidy index.
    /// </para>
    /// </remarks>
    public SessionIndexSweep Sweep()
    {
        var followed = Follow();
        var removed = new List<SessionIndexEntry>();
        var kept = new List<SessionIndexEntry>();

        foreach (var entry in followed)
        {
            if (!entry.IsRemovable)
            {
                kept.Add(entry);
                continue;
            }

            // R7. Absence is re-checked immediately before acting, on this one
            // entry, because between the enumeration above and this line an
            // `init` may have created the very directory the entry names. The
            // race is not prevented -- it is absorbed, since every entry is
            // re-asserted on the next `init` or `resume` -- but a pointer whose
            // directory is already back costs a cycle of invisibility for
            // nothing, and re-following one entry costs one stat.
            if (!ReFollow(entry).IsRemovable)
            {
                kept.Add(entry);
                continue;
            }

            if (TryDelete(entry.EntryFile))
            {
                SessionIndexLog.Removed(_logger, entry.Key, entry.Pointer, entry.State, entry.Problem);
                removed.Add(entry);
            }
            else
            {
                // Could not be deleted right now. Harmless: the next sweep meets
                // it again, and until then it is a line in an inventory that
                // says the directory is not a session.
                kept.Add(entry);
            }
        }

        return new SessionIndexSweep
        {
            Followed = followed.Count,
            Removed = removed,
            Kept = kept,
            LitterRemoved = SweepLitter(),
        };
    }

    /// <summary>
    /// Follows one entry again — what <see cref="Sweep"/> does immediately
    /// before it deletes one (race <b>R7</b>).
    /// </summary>
    /// <remarks>
    /// <b>Exposed so the re-check is testable as itself.</b> The race it closes
    /// is between an enumeration and a delete microseconds later, which cannot
    /// be arranged deterministically from outside — so what the suite asserts is
    /// this: an entry produced by an earlier enumeration, whose directory has
    /// since come back, is no longer removable. That is the whole mechanism, and
    /// a version of <see cref="Sweep"/> that dropped the call would leave this
    /// method correct and nothing else would notice.
    /// </remarks>
    /// <param name="entry">An entry from an earlier <see cref="Follow"/>.</param>
    /// <returns>What following it says now.</returns>
    internal static SessionIndexEntry ReFollow(SessionIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return FollowOne(entry.EntryFile, entry.Key);
    }

    private static SessionIndexEntry FollowOne(string file, string key)
    {
        string pointer;

        try
        {
            var info = new FileInfo(file);

            if (info.Length > MaximumEntryBytes)
            {
                return Unusable(file, key, string.Empty, $"it is {info.Length.ToString(CultureInfo.InvariantCulture)} bytes, which is longer than any path");
            }

            // FileShare.ReadWrite | Delete for the same reason SessionLock's
            // reader has it: a reader that does not share delete blocks the
            // rename another process is using to re-assert this very entry.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            // A BOM is stripped rather than refused. This is a pointer, and a
            // person who repaired one in an editor should be followed, not
            // lectured.
            pointer = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).Trim(Bom, ' ', '\t', '\r', '\n');
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Unreadable right now, which is not the same as wrong. Kept.
            return new SessionIndexEntry
            {
                Key = key,
                EntryFile = file,
                Pointer = string.Empty,
                State = SessionIndexEntryState.EntryUnreadable,
                Problem = $"the entry file itself could not be read ({failure.Message})",
            };
        }

        if (pointer.Length is 0)
        {
            return Unusable(file, key, pointer, "it is empty");
        }

        // A pointer must be absolute. A relative one would resolve against
        // whatever working directory the reader happens to have, which is a
        // different directory per process and never the one that was recorded.
        if (!Path.IsPathFullyQualified(pointer))
        {
            return Unusable(file, key, pointer, "it is not an absolute path");
        }

        SessionPath session;

        try
        {
            session = SessionPath.Resolve(pointer);
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unusable(file, key, pointer, $"it does not name a session directory ({failure.Message})");
        }

        // The name is the hash of the content. An entry whose name does not
        // match what it holds cannot have been written by this build, and
        // following it would produce a second inventory line for a directory
        // that already has a correctly-named one.
        if (!string.Equals(session.IndexKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return Unusable(file, key, pointer, $"its name is not the hash of the path it holds (that path hashes to {session.IndexKey})");
        }

        return Locate(file, key, pointer, session);
    }

    private static SessionIndexEntry Locate(string file, string key, string pointer, SessionPath session)
    {
        if (!Directory.Exists(session.FullPath))
        {
            // A volume that is not mounted is not a session that was destroyed.
            // Distinguishing them is what stops a session on a disconnected
            // network share or a removed drive being dropped out of the only
            // inventory there is.
            var root = Path.GetPathRoot(session.FullPath);

            return root is { Length: > 0 } && !Directory.Exists(root)
                ? new SessionIndexEntry
                {
                    Key = key,
                    EntryFile = file,
                    Pointer = pointer,
                    Session = session,
                    State = SessionIndexEntryState.VolumeMissing,
                    Problem = $"'{root}' is not mounted, so whether the session still exists cannot be known",
                }
                : new SessionIndexEntry
                {
                    Key = key,
                    EntryFile = file,
                    Pointer = pointer,
                    Session = session,
                    State = SessionIndexEntryState.DirectoryMissing,
                    Problem = "the directory it names no longer exists",
                };
        }

        try
        {
            var record = SessionLock.ReadRecord(session);

            return record is null
                ? new SessionIndexEntry
                {
                    Key = key,
                    EntryFile = file,
                    Pointer = pointer,
                    Session = session,
                    State = SessionIndexEntryState.NotASession,
                    Problem = $"'{session.FullPath}' exists but has no '{SessionLayout.LockFileName}', so it is not a BrowserAI session",
                }
                : new SessionIndexEntry
                {
                    Key = key,
                    EntryFile = file,
                    Pointer = pointer,
                    Session = session,
                    State = SessionIndexEntryState.Session,
                    Record = record,
                };
        }
        catch (Exception failure) when (failure is LockFileException or IOException or UnauthorizedAccessException)
        {
            // There IS a lock.json and it cannot be acted on. That is a session
            // in trouble, and the pointer is the only thing that can lead anyone
            // to it — see the note on Sweep().
            return new SessionIndexEntry
            {
                Key = key,
                EntryFile = file,
                Pointer = pointer,
                Session = session,
                State = SessionIndexEntryState.LockUnreadable,
                Problem = failure.Message,
            };
        }
    }

    private static SessionIndexEntry Unusable(string file, string key, string pointer, string why) =>
        new()
        {
            Key = key,
            EntryFile = file,
            Pointer = pointer,
            State = SessionIndexEntryState.Unusable,
            Problem = $"the entry cannot be followed: {why}",
        };

    private int SweepLitter()
    {
        if (!Directory.Exists(Root))
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow - LitterAge;
        var cleared = 0;

        foreach (var file in Directory.EnumerateFiles(Root))
        {
            // Only ever this store's own rename temps, and only ones far too old
            // to belong to a writer that is still running. A file this product
            // did not write is never deleted by anything here.
            if (IsLitter(Path.GetFileName(file)) && File.GetLastWriteTimeUtc(file) < cutoff && TryDelete(file))
            {
                cleared++;
            }
        }

        return cleared;
    }

    private static bool IsKey(string name) =>
        name.Length is 64 && name.All(char.IsAsciiHexDigit);

    private static bool IsLitter(string name)
    {
        var marker = name.IndexOf(".new-", StringComparison.Ordinal);

        return marker is 64
            && IsKey(name[..marker])
            && name.Length is 64 + 5 + 32
            && name[(marker + 5)..].All(char.IsAsciiHexDigit);
    }

    private static void Replace(string temp, string entry)
    {
        var clock = Stopwatch.StartNew();
        var delay = 5;

        while (true)
        {
            try
            {
                // MoveFileEx with MOVEFILE_REPLACE_EXISTING. A concurrent reader
                // sees the old entry or the new one and never a torn one, which
                // is the whole reason the write is not made in place.
                File.Move(temp, entry, overwrite: true);
                return;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // Both types deliberately: a rename over a file somebody has open
                // is refused ERROR_ACCESS_DENIED, which surfaces as
                // UnauthorizedAccessException and is NOT an IOException.
                if (clock.Elapsed >= MoveBudget)
                {
                    // If something else already put the entry there, the job is
                    // done — the content is a pure function of the name, so
                    // whoever won wrote what we were about to write.
                    if (File.Exists(entry))
                    {
                        return;
                    }

                    throw;
                }

                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 50);
            }
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
#pragma warning disable CA1031 // A file that will not delete is the next sweep's problem, never this call's failure.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}

/// <summary>What following one entry found.</summary>
internal enum SessionIndexEntryState
{
    /// <summary>The directory exists and holds a <c>lock.json</c> this build can read.</summary>
    Session,

    /// <summary>
    /// The directory exists and holds a <c>lock.json</c> that cannot be acted on.
    /// <b>Kept</b>: it is a session in trouble, and nothing else can lead anyone to it.
    /// </summary>
    LockUnreadable,

    /// <summary>
    /// The directory exists and has no <c>lock.json</c>. Not ours — a personal
    /// browser profile reaches this state, and nothing acts on it.
    /// </summary>
    NotASession,

    /// <summary>The directory is gone, on a volume that is present.</summary>
    DirectoryMissing,

    /// <summary>
    /// The volume itself is not mounted, so whether the session still exists is
    /// unknown. <b>Kept</b>: unknown is not destroyed.
    /// </summary>
    VolumeMissing,

    /// <summary>The entry does not name a path that can be followed at all.</summary>
    Unusable,

    /// <summary>The entry file could not be read. <b>Kept</b>: unreadable now is not wrong.</summary>
    EntryUnreadable,
}

/// <summary>One line of the inventory, and what following it found.</summary>
internal sealed record SessionIndexEntry
{
    /// <summary>The entry's file name — the SHA-256 of the canonical path, as hex.</summary>
    public required string Key { get; init; }

    /// <summary>The entry file's own absolute path.</summary>
    public required string EntryFile { get; init; }

    /// <summary>The text the entry holds, trimmed. Empty when it could not be read.</summary>
    public required string Pointer { get; init; }

    /// <summary>The canonicalised directory, when the pointer resolved to one.</summary>
    public SessionPath? Session { get; init; }

    /// <summary>What following it found.</summary>
    public required SessionIndexEntryState State { get; init; }

    /// <summary>
    /// The session's own record, when there is one. <see langword="null"/> in
    /// every other state — including <see cref="SessionIndexEntryState.LockUnreadable"/>,
    /// where a lock file exists and could not be parsed.
    /// </summary>
    public LockRecord? Record { get; init; }

    /// <summary>Why this is not a readable session, when it is not.</summary>
    public string? Problem { get; init; }

    /// <summary>
    /// Whether a sweep removes this entry.
    /// </summary>
    /// <remarks>
    /// Removal is only ever safe when re-asserting the entry is possible, which
    /// is exactly the three states below: a directory that is gone, one that was
    /// never a session, and a pointer that leads nowhere. Every other state
    /// names a directory that exists and cannot restore its own entry.
    /// </remarks>
    public bool IsRemovable => State
        is SessionIndexEntryState.DirectoryMissing
        or SessionIndexEntryState.NotASession
        or SessionIndexEntryState.Unusable;
}

/// <summary>What one sweep of the index did.</summary>
internal sealed record SessionIndexSweep
{
    /// <summary>How many entries were followed.</summary>
    public required int Followed { get; init; }

    /// <summary>The entries that were removed, with the reason on each.</summary>
    public required IReadOnlyList<SessionIndexEntry> Removed { get; init; }

    /// <summary>The entries that stayed.</summary>
    public required IReadOnlyList<SessionIndexEntry> Kept { get; init; }

    /// <summary>How many abandoned rename temps were cleared.</summary>
    public int LitterRemoved { get; init; }
}

/// <summary>Source-generated log messages for the session index.</summary>
/// <remarks>
/// Event ids start at 20 so that a reader of the process log can tell an index
/// record from <see cref="SessionLog"/>'s, which occupy 1–8 in the same
/// namespace.
/// </remarks>
internal static partial class SessionIndexLog
{
    /// <summary>An entry could not be written. Never fatal.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="entry">The entry file that could not be written.</param>
    /// <param name="directory">The session directory it would have named.</param>
    /// <param name="failure">Why.</param>
    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Warning,
        Message = "Could not write the session index entry {Entry} for {Directory}. The session is unaffected; it will be listed again after the next init or resume of that directory.")]
    public static partial void CouldNotRecord(ILogger logger, string entry, string directory, Exception failure);

    /// <summary>A sweep removed an entry that could not be followed to a session.</summary>
    /// <param name="logger">Where it goes.</param>
    /// <param name="key">The entry's key.</param>
    /// <param name="pointer">What it pointed at.</param>
    /// <param name="state">What following it found.</param>
    /// <param name="problem">Why it could not be followed to a session.</param>
    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Information,
        Message = "Session index entry {Key} pointing at '{Pointer}' was removed ({State}): {Problem}. Nothing outside the index was touched.")]
    public static partial void Removed(ILogger logger, string key, string pointer, SessionIndexEntryState state, string? problem);
}
