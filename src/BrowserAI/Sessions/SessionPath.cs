// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Security.Cryptography;
using System.Text;

namespace BrowserAI.Sessions;

/// <summary>
/// One session directory, canonicalised once, together with every name derived
/// from it: the mutex, the lock file and the index key.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one canonicalisation function.</b> The mutex name, the lock
/// file and the session index all key on the same directory, and if any two of
/// them normalise differently the same directory quietly acquires two
/// identities — which is a lock that reports success while guarding nothing.
/// There is deliberately no second spelling of this chain anywhere in the
/// product, and a test asserts the three derived names agree across a trailing
/// separator, a case change and a relative form.
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
/// <see cref="FullPath"/> therefore keeps the caller's own casing, which is also
/// what <c>browserai.json</c> records as the resolved path.
/// </para>
/// </remarks>
internal sealed class SessionPath
{
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

    /// <summary>Canonicalises a directory the caller named.</summary>
    /// <param name="directory">
    /// Any spelling of a directory: relative, trailing separator, any casing,
    /// with <c>.</c> or <c>..</c> segments.
    /// </param>
    /// <returns>The canonical session path and every name derived from it.</returns>
    /// <exception cref="ArgumentException">The path is empty, or not a rooted local path once resolved.</exception>
    public static SessionPath Resolve(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        // GetFullPath resolves a relative path against the current directory and
        // collapses `.` and `..` without touching the filesystem, so it works
        // for a directory that does not exist yet.
        var full = Path.GetFullPath(directory);

        // TrimEnd, then guard the root. `C:\` trims to `C:`, which is a
        // drive-relative path meaning "the current directory on C:" -- a
        // different thing entirely, and a silent one.
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return trimmed.Length is 0 || trimmed.EndsWith(':')
            ? throw new ArgumentException(
                $"'{directory}' resolves to '{full}', which is a volume root rather than a session directory. A session directory must be a real directory on the volume.",
                nameof(directory))
            : new SessionPath(trimmed);
    }
}
