// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BrowserAI.Sessions;

/// <summary>
/// One session directory, canonicalised once, together with every name derived
/// from it: the mutex, the lock file and the index key.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one identity chain, and <see cref="CanonicalPath"/> is the one
/// canonicalisation function in front of it.</b> The mutex name, the lock file,
/// the data file and the session index all key on the same directory, and if any
/// two of them normalise differently the same directory quietly acquires two
/// identities — which is a lock that reports success while guarding nothing.
/// There is deliberately no second spelling of either half anywhere in the
/// product, and a test asserts the derived names agree across every alias this
/// machine can build.
/// </para>
/// <para>
/// <b>You cannot put a path in a mutex name.</b> Backslashes are illegal after
/// the <c>Global\</c> prefix, so the path is hashed:
/// <c>Path.GetFullPath</c> → <c>TrimEnd('\')</c> → <c>ToUpperInvariant()</c> →
/// SHA-256 → hex. The real length limit is around 32,000 characters rather than
/// the documented 260, but hashing is required regardless.
/// </para>
/// <para>
/// <b>The identity is upper-cased; the path used for file I/O is not.</b> Those
/// are two different jobs. Upper-casing is what makes <c>c:\a</c> and
/// <c>C:\A</c> one session, and it is applied to the string that gets hashed.
/// Applying it to the path that is actually opened would be a different claim
/// — that the filesystem is case-insensitive — and Windows has supported
/// per-directory case sensitivity since 1803, so a caller whose directory sits
/// under a case-sensitive parent would be sent to a path that does not exist.
/// <see cref="FullPath"/> therefore keeps the casing it was handed, which is
/// also what <c>browserai.data</c> records as the resolved path.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-26 (previously "keeps the caller's own casing").</b>
/// The casing it is handed is the <i>filesystem's</i> now, because
/// <see cref="CanonicalPath"/> reads it back through
/// <c>GetFinalPathNameByHandleW</c> — which reports every component as it is
/// stored and the drive letter upper-case, always. Nothing hashed moves:
/// <see cref="Key"/> case-folds, so the mutex, the index key and the lock file
/// are the same names they were. What does move is the <i>spelling</i> in every
/// answer and every record, for a session opened from a shell that spelled the
/// drive letter lower-case — one <c>directory</c> statement on the next resume,
/// and that is the whole of what the identity change looks like from outside.
/// </para>
/// </remarks>
internal sealed class SessionPath
{
    /// <summary>
    /// Windows' own <c>MAX_PATH</c>, terminating null included.
    /// </summary>
    /// <remarks>
    /// <b>It is here because one Win32 call still keeps it and .NET does not.</b>
    /// <c>CreateProcessW</c>'s <c>lpCurrentDirectory</c> is bounded by it whatever
    /// the process manifest says, while <c>Directory.CreateDirectory</c> is not —
    /// so a directory can be created, locked and recorded and then be unusable as
    /// the place a browser is started.
    /// </remarks>
    public const int MaxPath = 260;

    /// <summary>The suffix SQLite composes from the store's own path.</summary>
    /// <remarks>
    /// <c>-wal</c> is the same length, so one of the two stands for both.
    /// </remarks>
    private const string WalIndexSuffix = "-shm";

    /// <summary>
    /// The longest a session directory may be: <see cref="MaxPath"/> less its
    /// terminator and less the longest name anything puts directly inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Derived rather than written, and the derivation is what found the real
    /// bound.</b> The obvious candidate is <c>\output</c> — the working
    /// directory every child is started in, which is what
    /// <c>CreateProcessW</c>'s <c>lpCurrentDirectory</c> bounds. It is not the
    /// longest. SQLite composes <c>browserai.data-shm</c> and
    /// <c>browserai.data-wal</c> from the store's path and its Win32 VFS is
    /// bounded in <c>MAX_PATH</c> characters too, so the store fails first:
    /// measured 2026-08-26, a 254-character session directory took its guard —
    /// which .NET opens, and .NET is not bounded — and then failed the store
    /// open with <c>SQLITE_CANTOPEN</c>, <i>"unable to open database file
    /// (result 14)"</i>. The profile and the downloads folder are opened by .NET
    /// alone and do not bind.
    /// </para>
    /// <para>
    /// ⚠️ <b>A budget, not a ban on deep paths.</b> A caller may name a
    /// directory of any depth to <c>browserai_list</c>, which creates nothing and
    /// starts nothing — which is why this predicate lives on this type rather
    /// than in <see cref="CanonicalPath"/>, beside the volume-root one and for
    /// the same reason.
    /// </para>
    /// </remarks>
    public static readonly int LongestSessionDirectory =
        MaxPath - 1 - Math.Max(
            1 + SessionLayout.DataFileName.Length + WalIndexSuffix.Length,
            1 + SessionLayout.OutputFolderName.Length);

    private SessionPath(string fullPath)
    {
        FullPath = fullPath;

        // The identity, and the only string that is ever hashed. Ordinal upper
        // casing under the invariant culture: the Turkish dotless i turns a
        // culture-sensitive ToUpper into a machine-dependent identity, which is
        // the same defect as two components normalising differently.
        Key = fullPath.ToUpperInvariant();
        Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key)));
        MutexName = LockScopes.PerDirectoryPrefix + Hash[..LockScopes.PerDirectoryHashLength];
        IndexKey = Hash;
        LockFile = Path.Combine(fullPath, SessionLayout.LockFileName);
        DataFile = Path.Combine(fullPath, SessionLayout.DataFileName);
    }

    /// <summary>
    /// The resolved absolute directory, with the caller's casing and no trailing
    /// separator. Everything that touches the filesystem uses this.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// The case-folded form. This is the identity, and the only thing hashed —
    /// never a filesystem path.
    /// </summary>
    public string Key { get; }

    /// <summary>The SHA-256 of <see cref="Key"/>, as upper-case hex.</summary>
    public string Hash { get; }

    /// <summary>
    /// The per-directory mutex: <c>Global\BrowserAI-{hash[..32]}</c>. Held for
    /// milliseconds, around create-or-take only.
    /// </summary>
    public string MutexName { get; }

    /// <summary>
    /// The session index's file name for this directory — the full hash. The
    /// index itself is build-order step 11; the <i>key</i> lives here because it
    /// must come out of the same canonicalisation as the other two names, and a
    /// second implementation is exactly how they would drift apart.
    /// </summary>
    public string IndexKey { get; }

    /// <summary>The absolute path of <c>browserai.lock</c> inside this directory.</summary>
    /// <remarks>
    /// <b>The guard, and only the guard.</b> It is what a liveness probe opens
    /// and what a holder keeps; nothing about what the session <i>did</i> is in
    /// it. That is <see cref="DataFile"/>.
    /// </remarks>
    public string LockFile { get; }

    /// <summary>The absolute path of <c>browserai.data</c> inside this directory.</summary>
    /// <remarks>
    /// <b>Derived here rather than composed at each call site</b>, for the
    /// reason every other name on this type is: two spellings of one path is
    /// how two components come to read different files while both report
    /// success.
    /// </remarks>
    public string DataFile { get; }

    /// <summary>
    /// Every name a session derives from a directory <see cref="CanonicalPath"/>
    /// has already answered for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It normalises nothing, and that is what makes the one-function rule
    /// true rather than nearly true — 2026-08-26, previously
    /// <c>Resolve(string directory)</c>, which was <c>Path.GetFullPath</c> plus a
    /// trim.</b> With the normalisation here, <c>browserai_list</c> could not use
    /// this chain at all: a listing is pointed at a volume root on purpose and a
    /// volume root is refused below, so <c>list</c> grew a second
    /// <c>GetFullPath</c>-plus-upper-case of its own — which is exactly the
    /// second spelling this type's remarks forbid, and it is where the aliased
    /// listing gave a confident wrong answer. Splitting the two questions apart
    /// lets <c>list</c> ask the first and skip the second.
    /// </para>
    /// <para>
    /// <b>Two predicates are left: the volume root and the length</b> — because
    /// <c>C:\</c> is a legitimate <i>subtree</i> and never a session directory,
    /// and because a directory with no room left inside it for
    /// <c>browserai.data</c> is one that can be created and cannot then be
    /// opened. The trailing-separator trim survives both: this is handed
    /// canonical paths by construction, and a caller that composes one with a
    /// separator on the end would otherwise get a second identity for one
    /// directory.
    /// </para>
    /// <para>
    /// ⚠️ <b>Neither refusal carries a <c>paramName</c>, and that is deliberate
    /// — corrected 2026-08-26.</b> <c>SessionManager.Resolve</c> interpolates
    /// <c>failure.Message</c> straight into
    /// <see cref="SessionErrors.DirectoryUnusable"/>, and
    /// <c>ArgumentException.Message</c> appends <c>(Parameter 'x')</c> whenever
    /// one is set — so a caller naming <c>C:\</c> was answered <i>"…must be a
    /// real directory on the volume. <b>(Parameter 'canonical')</b>"</i>,
    /// measured through the published binary that day. <c>canonical</c> is an
    /// internal identifier that means nothing to a model, in the one sentence
    /// the model is supposed to act on.
    /// </para>
    /// </remarks>
    /// <param name="canonical">
    /// A directory as <see cref="CanonicalPath.Of"/> answered it.
    /// </param>
    /// <returns>The session path and every name derived from it.</returns>
    /// <exception cref="ArgumentException">
    /// The path is empty, a volume root, or too long to hold a session.
    /// </exception>
    public static SessionPath For(string canonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        // `C:\` trims to `C:`, which is a drive-relative path meaning "the
        // current directory on C:" -- a different thing entirely, and a silent
        // one.
        var trimmed = canonical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (trimmed.Length is 0 || trimmed.EndsWith(':'))
        {
            throw new ArgumentException(
                $"'{canonical}' is a volume root rather than a session directory. A session directory must be a real directory on the volume.");
        }

        // ⚠️ AT THE DOOR, because the alternative is a launch-time surprise
        // whose recovery is wrong. Measured 2026-08-26: a directory this long
        // was accepted, created and locked, and the session then failed with
        // either "Could not start '…\node.exe' in '…\output'" or a store that
        // would not open -- and row 7's advice, delete the directory and
        // re-provision, is the wrong recovery for a path problem.
        if (trimmed.Length > LongestSessionDirectory)
        {
            throw new ArgumentException(
                $"'{trimmed}' is {trimmed.Length.ToString(CultureInfo.InvariantCulture)} characters, and a session directory may be at most "
                + $"{LongestSessionDirectory.ToString(CultureInfo.InvariantCulture)}. BrowserAI creates '{SessionLayout.DataFileName}' and "
                + $"'{SessionLayout.OutputFolderName}' inside the directory you name, and Windows still bounds a database open and a child's working "
                + $"directory at {MaxPath.ToString(CultureInfo.InvariantCulture)} characters even where .NET does not — so the directory would be "
                + "created and the session would then fail to open, with a message about the browser rather than about the path. Name a path at least "
                + $"{(trimmed.Length - LongestSessionDirectory).ToString(CultureInfo.InvariantCulture)} character(s) shorter.");
        }

        return new SessionPath(trimmed);
    }
}
