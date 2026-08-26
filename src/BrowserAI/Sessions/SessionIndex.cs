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
/// authorisation. No entry is ever <b>reported as a session</b> without opening
/// the <c>browserai.json</c> it points at, and a directory that has none is
/// reported as not ours and acted on in no other way. That is what makes an entry
/// that somehow named a personal Chrome profile harmless: it is an inventory
/// line, and nothing downstream may treat it as permission to touch anything.
/// </para>
/// <para>
/// ⚠️ ***Corrected 2026-08-24 (previously "Every entry is verified by opening the
/// `browserai.json` it points at").*** That was true of
/// <see cref="Follow"/> and became false as a statement about this store the
/// moment <see cref="FollowUnder"/> existed: a subtree read decides which entries
/// to <i>ask about</i> and never which answers to <i>trust</i>. A filtered entry
/// is not reported at all, which is a third thing beside <i>ours</i> and <i>not
/// ours</i> and is why the sentence had to move rather than be dropped.
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
/// <see cref="SessionLock"/>.</b> <c>browserai.json</c> is written
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
    /// Reads every entry and follows it — the whole-machine read, and one of the
    /// two ways this store is ever read.
    /// </summary>
    /// <returns>Every entry, each with what following it found, ordered by key.</returns>
    /// <remarks>
    /// <para>
    /// <b>Following is verification, not trust.</b> Nothing here decides that a
    /// directory is ours because it is listed; it decides that by opening the
    /// <c>browserai.json</c> inside it. The states this returns are an inventory
    /// report and confer no authority on anything. <b>That survives
    /// <see cref="FollowUnder"/> intact</b> — it is a rule about what a returned
    /// state means, and no returned state changes.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24 (previously "the only way this store is ever
    /// read").*** <see cref="FollowUnder"/> is the second, and it is defined in
    /// terms of this one. <b>Three callers must keep using this one</b>, and each
    /// is machine-wide by design: <see cref="Sweep"/>, which would otherwise
    /// leave entries un-swept forever; <c>SessionManager.LiveSessions</c>, whose
    /// browsers root is machine-wide; and <c>StraySweep.AttributeByProfileLock</c>,
    /// whose whole reach is the point of it.
    /// <c>HouseRuleTests.TheThreeWholeMachineIndexReadersStillTakeTheWholeMachineRead</c>
    /// asserts that, by name, rather than leaving it to a reader.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24, same day (previously "<c>HouseRuleTests</c>
    /// asserts that rather than leaving it to a reader").*** On the day it was
    /// written, it did not. The only scan there was —
    /// <c>NoIndexWalkFiltersBySubtreeAfterFollowingTheEntry</c> — holds that no
    /// <c>foreach</c> over this read filters by subtree inside its body and that
    /// at least <b>two</b> such loops exist. It names none of the three; two of
    /// them were held only by that lower bound on a loop <i>shape</i>, and
    /// <see cref="Sweep"/> by nothing at all, because it reads the result into a
    /// local. The named assertion exists now, and a sentence citing a mechanism
    /// is worse than no sentence when the mechanism does not hold what it says.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SessionIndexEntry> Follow() => Walk(under: null);

    /// <summary>
    /// Follows only the entries that name a directory under one subtree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-08-24. The filter used to run on the wrong side of the
    /// parse</b>: <c>browserai_list</c> and the roll-up called
    /// <see cref="Follow"/> and then dropped what was not under their prefix, so
    /// every session on the machine had its record opened and strictly parsed —
    /// up to 250 log entries and all their arguments — to print the four fields
    /// of the few that matched. The roll-up runs on every <c>init</c> and every
    /// <c>resume</c>, so that was a session-open cost rather than a listing one,
    /// and each of those opens inherits <c>RenameWindow</c>'s budget: one denied
    /// or scanner-held record anywhere on the machine could add it to a call
    /// scoped to a completely unrelated tree.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24, same day (previously the paragraph above ended
    /// there, and <c>ARCHITECTURE.md</c> said the same).*** <b>What this removed
    /// is the RECORD open, and the ENTRY-FILE open is still one per index entry
    /// on the machine, still through <c>RenameWindow.WaitOut</c>, still carrying
    /// the whole 30-second budget.</b> <c>FollowOne</c> opens the entry file
    /// before it can read the pointer it filters on, so a denied or
    /// delete-pending <i>entry file</i> anywhere on the host still adds up to
    /// that budget to a <c>browserai_list</c> scoped to an unrelated tree, and to
    /// the roll-up on every <c>init</c> and every <c>resume</c>. The number of
    /// opens per call is unchanged; only the strict <i>parse</i> moved. A reader
    /// who took from the paragraph above that a subtree-scoped call can no longer
    /// be delayed by a stranger's file took the wrong thing.
    /// </para>
    /// <para>
    /// <b>The predicate is unchanged; only its position is.</b> Everything ahead
    /// of the filter is the entry's own verification — a bounded read, an
    /// absolute-path check, a canonical resolve and the hash-of-content check —
    /// and none of it opens the session.
    /// </para>
    /// <para>
    /// <b>An entry this cannot compare is returned rather than dropped.</b>
    /// Every earlier refusal — unreadable, empty, relative, not a session path,
    /// wrongly named — carries no path to test against the prefix, and dropping
    /// it would make a subtree read narrower than its caller can see.
    /// </para>
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24, same day (previously "So the entries this
    /// returns are exactly the entries <c>Follow</c> would have returned for the
    /// same subtree, followed the same way; <c>SessionIndexTests</c> asserts that
    /// equivalence directly").*** That sentence and the paragraph above it — the
    /// one that says an entry this cannot compare is <b>returned</b> — contradict
    /// each other, and the code agrees with the second. <b>The true equivalence
    /// is narrower</b>: <c>FollowUnder(p)</c> is <c>Follow()</c> filtered by
    /// <see cref="IsUnder"/> <i>over the entries that resolved to a session</i>,
    /// plus every entry that resolved to none, whatever subtree it does or does
    /// not name. The falsifying input is an entry that is empty, relative or
    /// mis-hashed and points nowhere near <c>p</c>: returned here, not returned
    /// by <c>Follow()</c> filtered. The test the old sentence cited built its
    /// expectation with <c>entry.Session is { } session &amp;&amp; …</c>, which
    /// drops exactly that class, so it excluded the only case where the
    /// equivalence fails; it now plants one and asserts the narrower claim.
    /// <b>Harmless in the product today</b> — <c>SessionManager.List</c> and
    /// <c>SessionManager.Beneath</c> both drop <c>Session is null</c> on the next
    /// line — and the API's contract is what a later caller will read.
    /// </para>
    /// </remarks>
    /// <param name="prefix">
    /// A case-folded, separator-terminated, fully-qualified path prefix.
    /// <para>
    /// ⚠️ ***Corrected 2026-08-24, same day (previously "as
    /// <c>SessionManager.Subtree</c> produces it").*** There are <b>two</b>
    /// producers and the contract is the shape rather than one of them:
    /// <c>SessionManager.Subtree</c> is one, and <c>SessionManager.Beneath</c>
    /// re-derives the prefix itself — <c>ToUpperInvariant</c> then a separator —
    /// and in particular skips <c>Subtree</c>'s <c>Path.GetFullPath</c>. That is
    /// benign today only because its input is <c>Path.GetDirectoryName</c> of an
    /// already-canonical <see cref="SessionPath"/>. <b>It is also a second
    /// spelling of a derivation, which is what <see cref="IsUnder"/>'s own remark
    /// forbids two members below</b>: that remark says the copy in
    /// <c>SessionManager</c> was deleted rather than left standing, and it is the
    /// <i>predicate</i> that was — the <i>prefix</i> derivation is still there,
    /// unmentioned, and pre-dates this method. Naming it here rather than
    /// changing it: collapsing the two is a change to path handling and belongs
    /// with whoever owns that, not to a documentation pass.
    /// </para>
    /// </param>
    /// <returns>The entries under that subtree, ordered by key.</returns>
    public IReadOnlyList<SessionIndexEntry> FollowUnder(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        return Walk(prefix);
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
    /// <b>Two states are deliberately <i>kept</i> that a first reading of the
    /// self-cleaning rule would remove.</b> That rule licenses removal only
    /// because a wrongly-dropped entry is restored by the next <c>init</c> or
    /// <c>resume</c>, and neither of these two can ever do that. A
    /// directory whose <c>browserai.json</c> is present but unparseable is a session
    /// — a broken one — and it cannot restore its own entry, because
    /// <see cref="SessionLock.TryAcquire"/> refuses an unreadable record. And a
    /// path on a volume that is not mounted has not been destroyed; the drive is
    /// simply not there. Removing either would make a directory that still exists
    /// permanently invisible to the only inventory there is, which is this
    /// project's founding failure shape rather than a tidy index.
    /// <b>Both are asserted</b>, by
    /// <c>SessionIndexTests.AnEntryWhoseRecordCannotBeReadIsKeptBecauseNothingElseCanRestoreIt</c>
    /// and
    /// <c>SessionIndexTests.AnEntryOnAVolumeThatIsNotMountedIsKeptRatherThanSwept</c>
    /// — named here because a distinction this paragraph argues for and nothing
    /// checks is one refactor from being tidied away.
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

        return FollowOne(entry.EntryFile, entry.Key, under: null)!;
    }

    /// <summary>The one walk, with the subtree filter optional.</summary>
    /// <param name="under">
    /// The prefix to keep, or <see langword="null"/> for the whole machine.
    /// </param>
    /// <returns>Every entry that survived, ordered by key.</returns>
    private List<SessionIndexEntry> Walk(string? under)
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

            if (FollowOne(file, key, under) is { } followed)
            {
                entries.Add(followed);
            }
        }

        return entries;
    }

    /// <summary>
    /// Spelled exactly once, and this is the spelling.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Do not re-derive it.</b> <c>SessionManager.Subtree</c> produces the
    /// prefix upper-cased and separator-terminated, and
    /// <see cref="SessionPath.Key"/> is the upper-cased full path. Two spellings
    /// of this predicate is the class of defect this repository keeps re-finding,
    /// which is why the copy that used to live in <c>SessionManager</c> was
    /// deleted rather than left beside this one.
    /// <para>
    /// ⚠️ <b>And it is true of the PREFIX now too — corrected 2026-08-26,
    /// previously "That is true of the PREDICATE and not of the PREFIX …
    /// <c>SessionManager.Beneath</c> still derives the prefix on its own … it is
    /// recorded here rather than quietly fixed because collapsing it is a change
    /// to path handling".</b> That change is this one. <c>Subtree</c> and
    /// <c>Beneath</c> both call <c>CanonicalPath.PrefixOf</c>, and a tree-as-text
    /// scan (<c>HouseRuleTests.ThePrefixIsDerivedInOnePlaceAndTheRestOfTheTreeAsksForIt</c>)
    /// fails the build on a third derivation rather than leaving the next one to
    /// be found by reading.
    /// </para>
    /// </remarks>
    /// <param name="candidate">The session a followed entry points at.</param>
    /// <param name="prefix">The case-folded, separator-terminated prefix.</param>
    /// <returns>Whether the candidate is at or beneath the prefix.</returns>
    private static bool IsUnder(SessionPath candidate, string prefix) =>
        (candidate.Key + Path.DirectorySeparatorChar).StartsWith(prefix, StringComparison.Ordinal);

    private static SessionIndexEntry? FollowOne(string file, string key, string? under)
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
            //
            // ⚠️ Through RenameWindow, added 2026-08-18. Sharing the delete is
            // what lets the other process's rename PROCEED; it does not stop
            // this open being refused ACCESS_DENIED while that rename is in
            // flight, because the file being replaced is delete-pending. Without
            // the wait this read did not throw — it fell into the catch below and
            // reported the entry as `EntryUnreadable`, which is a wrong answer
            // rather than an exception and therefore the worse of the two
            // failures. The entry is kept either way, so nothing was ever
            // destroyed by it; what was lost is a session the caller should have
            // been told about.
            using var stream = RenameWindow.WaitOut(() =>
                new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
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

        // ⚠️ CHECKED, NEVER RESOLVED, and this line runs once per index entry on
        // the WHOLE MACHINE -- on every listing, every roll-up (which is every
        // `init` and every `resume`) and every sweep. Resolving an alias here
        // would be one directory open per entry per call, and up to 64 of them
        // driven by a string this process did not write. It does not have to:
        // everything this build records went through the whole sequence on the
        // way in, so a stored path that is not canonical was not stored by this
        // build -- and an entry like that is exactly what `Unusable` is for.
        //
        // What that deliberately cannot see is an alias in a path some OTHER
        // build wrote. It is swept rather than followed, and the next `init` or
        // `resume` on the real directory records it again, canonically.
        var verdict = CanonicalPath.Of(pointer, PathOrigin.Read, "pointer");

        if (verdict.Refusal is { } refusal)
        {
            return Unusable(file, key, pointer, refusal);
        }

        SessionPath session;

        try
        {
            session = SessionPath.For(verdict.Canonical!);
        }
        catch (ArgumentException failure)
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

        // ⚠️ THE ONE PLACE A PREFIX MAY BE APPLIED, AND IT IS ABOVE `Locate` BY
        // CONSTRUCTION. Everything above this line is the entry's own
        // verification -- a bounded read, an absolute-path check, a canonical
        // resolve and the hash-of-content check -- and none of it opens the
        // session. `Locate` is the open and the strict parse. A caller that wants
        // one subtree stops here for everything outside it, which is why
        // `browserai_list` no longer parses every record on the machine to print
        // the few under its prefix.
        if (under is not null && !IsUnder(session, under))
        {
            return null;
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
                ? Absent(file, key, pointer, session)
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
        catch (Exception failure) when (failure is SessionRecordException or IOException or UnauthorizedAccessException)
        {
            // There IS a browserai.json and it cannot be acted on. That is a session
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

    /// <summary>
    /// What an absent <c>browserai.json</c> means, which is two different things and
    /// only one of them is safe to act on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is the ungated reader that ACTED on an absence, and it is the
    /// only one in the product that reached a destructive act.</b> Added
    /// 2026-08-19, from
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md).
    /// <c>SessionLock.ReadRecord</c> takes no gate — it cannot, because the whole
    /// point of the index is to describe sessions nobody is holding — so it can
    /// land in the instant in which <c>browserai.json</c>'s <b>name is unbound</b>
    /// while another process replaces the record
    /// ([hazard index](../../../HAZARDS.md#hazard-index): a rename over a file
    /// with an open handle is refused, the writer retries, and the name is free
    /// between the pending delete completing and the next attempt landing).
    /// Read as <c>NotASession</c> that is <b>removable</b>, so a sweep dropped
    /// the index entry of a live session that was doing nothing worse than
    /// setting its own purpose.
    /// </para>
    /// <para>
    /// <b>The temp file is the discriminator, and it is a positive signal rather
    /// than a timing guess.</b> A durable write creates
    /// <see cref="SessionLayout.NewLockFilePattern"/> in the same directory
    /// <i>before</i> it renames anything and deletes it only after the rename
    /// has landed, so for the whole of the window in which the name can be
    /// unbound the temp file is on disk. Present means a rewrite is in flight
    /// and the entry is kept; absent means the directory really has no record,
    /// which is a personal browser profile or a destroyed session and is
    /// re-assertable.
    /// </para>
    /// <para>
    /// <b>It cannot be wrong in the dangerous direction, and it can be wrong in
    /// the safe one.</b> A temp file left behind by a process that died mid-write
    /// keeps an entry that could have been dropped — a stale line in an
    /// inventory, which the next sweep removes once the litter sweep has taken
    /// the temp file. There is no reading in which a live session's entry is
    /// dropped while its own writer is still holding the file.
    /// </para>
    /// </remarks>
    /// <param name="file">The entry file.</param>
    /// <param name="key">The entry's key.</param>
    /// <param name="pointer">What the entry holds.</param>
    /// <param name="session">The directory it names.</param>
    /// <returns>The entry, in whichever of the two states applies.</returns>
    private static SessionIndexEntry Absent(string file, string key, string pointer, SessionPath session)
    {
        string[] inFlight;

        try
        {
            inFlight = Directory.GetFiles(session.FullPath, SessionLayout.NewLockFilePattern);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The directory that existed a moment ago cannot be listed now.
            // Keeping the entry is the safe answer to every reason for that.
            return new SessionIndexEntry
            {
                Key = key,
                EntryFile = file,
                Pointer = pointer,
                Session = session,
                State = SessionIndexEntryState.RecordInFlight,
                Problem = $"'{session.FullPath}' has no '{SessionLayout.DataFileName}' and could not be listed to find out why ({failure.Message})",
            };
        }

        return inFlight.Length is 0
            ? new SessionIndexEntry
            {
                Key = key,
                EntryFile = file,
                Pointer = pointer,
                Session = session,
                State = SessionIndexEntryState.NotASession,
                Problem = $"'{session.FullPath}' exists but has no '{SessionLayout.DataFileName}', so it is not a BrowserAI session",
            }
            : new SessionIndexEntry
            {
                Key = key,
                EntryFile = file,
                Pointer = pointer,
                Session = session,
                State = SessionIndexEntryState.RecordInFlight,
                Problem = $"'{session.FullPath}' holds no '{SessionLayout.DataFileName}' at this instant and '{Path.GetFileName(inFlight[0])}' is beside it, so another BrowserAI is inside create-or-take on it right now",
            };
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
    /// <summary>The directory exists and holds a <c>browserai.json</c> this build can read.</summary>
    Session,

    /// <summary>
    /// The directory exists and holds a <c>browserai.json</c> that cannot be acted on.
    /// <b>Kept</b>: it is a session in trouble, and nothing else can lead anyone to it.
    /// </summary>
    LockUnreadable,

    /// <summary>
    /// The directory exists and has no <c>browserai.json</c>. Not ours — a personal
    /// browser profile reaches this state, and nothing acts on it.
    /// </summary>
    NotASession,

    /// <summary>
    /// The directory exists, has no <c>browserai.json</c> <b>at this instant</b>, and
    /// carries a <c>browserai.json.new-…</c> beside the gap — so another BrowserAI is
    /// replacing the record right now. <b>Kept</b>: a session mid-rewrite is the
    /// opposite of a session that never was.
    /// </summary>
    RecordInFlight,

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
    public SessionRecord? Record { get; init; }

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
    /// <para>
    /// ⚠️ <b><see cref="SessionIndexEntryState.RecordInFlight"/> is deliberately
    /// not in the list, and separating it out of
    /// <see cref="SessionIndexEntryState.NotASession"/> is what made the list
    /// safe.</b> An absent <c>browserai.json</c> was one state, so the instant in
    /// which a live session's record is being renamed into place read as *never
    /// was a session* and the entry was dropped. See <c>SessionIndex.Absent</c>
    /// for the discriminator and why it cannot fail dangerously.
    /// </para>
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
