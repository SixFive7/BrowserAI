// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;

namespace BrowserAI.Runtime;

/// <summary>
/// The machine-wide claim on a browsers root, held for the whole of a
/// <c>browserai_reinstall_browser</c> and refused to every session that tries to
/// start meanwhile.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it closes is a race between two agents on one machine</b>, which the
/// existing running-process census cannot see: a reinstall establishes that
/// nothing is running out of the tree, and a peer's <c>browserai_init</c> then
/// launches a browser into that tree while the recursive delete is part way
/// through it. The delete's own guard answers <i>nothing is running</i> because
/// at the moment it asked, nothing was. Taken by the maintainer on 2026-08-19:
/// <i>"No reinstall if there is any session running system wide. Including any
/// reinstall sessions."</i>
/// </para>
/// <para>
/// <b>It is a FILE and not a named mutex, and that is forced rather than
/// chosen.</b> A named mutex is owned by the <i>thread</i> that waited on it —
/// <see cref="Sessions.MachineMutex"/> says so in its own remarks — and this
/// claim is held across a 203.8 MB download inside an <c>async</c> method, so a
/// continuation resuming on another pool thread would make the release throw
/// about "an unsynchronized block of code". A <see cref="FileStream"/> has no
/// thread affinity at all. The alternative that does span threads is a named
/// <b>semaphore</b>, and it is worse than either: a semaphore's count is not
/// restored when its holder dies, so one crashed reinstall would refuse every
/// <c>browserai_init</c> on the machine until the next reboot. Windows closes a
/// file handle however the process ends, which is the crash recovery this design
/// needs and cannot write itself.
/// </para>
/// <para>
/// <b>Held-ness is a sharing violation and never the file's existence</b>, which
/// is the same rule <c>SessionLock</c> follows for <c>browserai.json</c> and for the
/// same reason: a crashed holder leaves the file behind, so existence would mean
/// <i>somebody died here once</i> rather than <i>somebody is working now</i>.
/// </para>
/// <para>
/// <b>Keyed on the browsers root, exactly as
/// <see cref="BrowserProvisioner.MutexNameFor(string, string)"/> is, and for the
/// reason that method already gives.</b> Two BrowserAI installations with
/// different browsers roots are genuinely independent — their downloads write to
/// different directories and neither can corrupt the other — so one global name
/// would make a reinstall in installation A refuse a session in installation B
/// for no reason at all. A file inside the root <i>is</i> that key, with no hash
/// and no name to get wrong.
/// </para>
/// <para>
/// <b>The order it is taken in, and why nothing here can deadlock.</b> This lock
/// is <b>outermost</b>: <c>SessionManager.ReinstallBrowserAsync</c> takes it
/// first and only then reaches <see cref="BrowserProvisioner"/>, which takes the
/// per-family provisioning mutexes — one for a family, and chromium, firefox and
/// <c>shared</c> in that fixed order for the shared target. <b>No path anywhere
/// takes a provisioning mutex and then asks for this</b>, so there is no cycle to
/// close; and every acquisition on both sides is non-blocking — this one opens
/// at once or fails, the mutexes use <c>LockScopes.NeverWaits</c> — so even a
/// future edit that inverted the order would produce a refusal rather than a
/// hang. <c>browserai_init</c> and <c>browserai_resume</c> only
/// <see cref="Probe"/> this: they never acquire it, so they can never take it
/// away from a reinstall or wait behind one.
/// </para>
/// <para>
/// <b>There is no drain and no wait, and that is a decision rather than an
/// omission.</b> A reinstall that found sessions already open refuses at once
/// and says how many; it does not hold this lock and wait for them to close.
/// Waiting on a browser a human may never close is exactly the shape this
/// product spent a week removing.
/// </para>
/// </remarks>
internal sealed class MaintenanceLock : IDisposable
{
    /// <summary>
    /// The lock file's name, at the root of the browsers directory.
    /// </summary>
    /// <remarks>
    /// <b>Beside the trees rather than inside one</b>, because the trees are what
    /// gets deleted. <see cref="RevisionPrune"/> only removes directories whose
    /// name begins with a manifest prefix, and this is a file, so nothing sweeps
    /// it.
    /// </remarks>
    public const string FileName = "reinstall.lock";

    private readonly FileStream _held;
    private int _disposed;

    private MaintenanceLock(FileStream held) => _held = held;

    /// <summary>Where a browsers root's lock file is.</summary>
    /// <param name="browsersDirectory">The browsers root.</param>
    /// <returns>The absolute path.</returns>
    public static string PathIn(string browsersDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);
        return Path.Combine(browsersDirectory, FileName);
    }

    /// <summary>
    /// Takes the claim, or answers who has it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>FileShare.Read</c> and no more.</b> A peer may read the record to
    /// name the holder — which is what <see cref="Probe"/> does — and no peer may
    /// open it for writing, which is what makes a second taker fail.
    /// </para>
    /// <para>
    /// <b>The record is one line of plain text and deliberately not JSON.</b>
    /// Nothing parses it; its only consumer is a sentence, so a schema would be a
    /// second thing to keep in step for no reader's benefit.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root. Created if absent.</param>
    /// <param name="target">What is being reinstalled, for the sentence a peer reads.</param>
    /// <returns>The claim, or <see langword="null"/> when somebody else holds it.</returns>
    public static MaintenanceLock? TryTake(string browsersDirectory, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);
        ArgumentNullException.ThrowIfNull(target);

        try
        {
            _ = Directory.CreateDirectory(browsersDirectory);

            var held = new FileStream(
                PathIn(browsersDirectory),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.Read);

            try
            {
                using var writer = new StreamWriter(held, leaveOpen: true);
                writer.Write(Describe(target));
                writer.Flush();
                held.Flush(flushToDisk: true);
            }
            catch
            {
                held.Dispose();
                throw;
            }

            return new MaintenanceLock(held);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Held, denied or unreachable. Every one of them means this process
            // may not start a reinstall, and Probe says which for the sentence.
            return null;
        }
    }

    /// <summary>
    /// Whether a reinstall is running against this browsers root, and who is
    /// running it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It never acquires anything.</b> A probe that took the file — even for
    /// the microsecond it takes to release it — would make a racing reinstall
    /// report <i>"another reinstall is running"</i> about an <c>init</c> that was
    /// only looking, which is a refusal naming a cause that does not exist. So
    /// the probe opens for <b>write</b> and reads the answer off the failure:
    /// refused means held, granted means free.
    /// </para>
    /// <para>
    /// <b>And the write-open is closed immediately without writing.</b>
    /// <c>FileMode.Open</c> with no truncation, so a stale record left by a dead
    /// holder survives being looked at.
    /// </para>
    /// <para>
    /// <b>It is a check-then-act and it is not pretending otherwise.</b> A
    /// reinstall can take the lock in the instant between this answer and the
    /// session that follows it. That window is closed from the other side — the
    /// reinstall takes the lock and <i>then</i> counts live sessions, so a
    /// session that got in first is seen and refuses the reinstall instead.
    /// Neither half closes it alone.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root.</param>
    /// <returns>What the holder said about itself, or <see langword="null"/> when nothing holds it.</returns>
    public static string? Probe(string browsersDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);

        var path = PathIn(browsersDirectory);

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return null;
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            // No reinstall has ever run against this root.
            return null;
        }
        catch (IOException failure) when (IsSharingViolation(failure))
        {
            return Read(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // ⚠️ NOT read as free. An ACL that denies the write-open would answer
            // "nobody is reinstalling" on every call, which is the one direction
            // this probe must never fail in: it would put a session back into the
            // race the lock exists to remove. What it costs when it is wrong is a
            // refusal a retry cannot clear, and the sentence says which file to
            // look at.
            return $"the lock file '{path}' could not be examined ({failure.Message}), which is not the same as nobody holding it";
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        _held.Dispose();
    }

    /// <summary>What this process is doing, for a peer's refusal to quote.</summary>
    /// <remarks>
    /// <b>The pid and its creation time together</b>, which is this repository's
    /// standing rule for naming a process: Windows reuses pids, and a sentence
    /// carrying a bare one eventually names a stranger.
    /// </remarks>
    /// <param name="target">What is being reinstalled.</param>
    /// <returns>The one line written into the lock file.</returns>
    private static string Describe(string target)
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();

        return $"PID {self.Id.ToString(CultureInfo.InvariantCulture)}, started {self.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}, is reinstalling '{target}'; it took this claim at {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)}";
    }

    private static string Read(string path)
    {
        try
        {
            // FileShare.ReadWrite because the holder has it open for WRITE, and
            // a reader that shared less than the holder's own access would be
            // refused by its own share mode rather than by the holder's.
            using var reader = new StreamReader(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));

            var said = reader.ReadToEnd().Trim();

            return said.Length is 0
                ? $"the holder wrote nothing into '{path}'"
                : said;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The refusal stands either way -- the sharing violation is what
            // decided it, and this only names who.
            return $"its holder could not be identified from '{path}' ({failure.Message})";
        }
    }

    private static bool IsSharingViolation(IOException failure) =>
        (failure.HResult & 0xFFFF) is 32 or 33;
}
