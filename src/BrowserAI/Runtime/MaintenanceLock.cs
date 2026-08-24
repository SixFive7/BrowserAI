// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Sessions;

namespace BrowserAI.Runtime;

/// <summary>
/// The machine-wide reader/writer claim on a browsers root: every session holds
/// it <b>shared</b> for its whole life, and
/// <c>browserai_reinstall_browser</c> holds it <b>exclusively</b> for the whole
/// of a call.
/// </summary>
/// <remarks>
/// <para>
/// <b>The design is the maintainer's, verbatim, taken 2026-08-20:</b> <i>"any
/// init or resume should take a system level lock. No matter the browser type.
/// These locks are cumulative. And reinstalling the browser should be an
/// exclusive lock."</i> And on what happens when the exclusive open fails:
/// <i>"I do not want the intent marker. If anything is busy then the reinstall
/// should be refused with the list… But it should not start a drain/preventstart
/// process of sorts. Keep it simple. Let the user solve the open sessions
/// block."</i>
/// </para>
/// <para>
/// <b>Windows file sharing modes give reader/writer directly, and that is why
/// this is a file rather than anything else.</b> An open is refused when the
/// requested <i>access</i> is outside an existing handle's share mode, <b>or</b>
/// when the requested <i>share mode</i> is narrower than an existing handle's
/// granted access — the check runs in both directions, which is exactly what a
/// reader/writer lock needs and what a named object would have to simulate:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Reader</b> — <see cref="TakeShared"/> opens
///     <c>FileAccess.Read</c> / <c>FileShare.Read</c>. Two of them are
///     compatible in both directions, so any number succeed together and the
///     claims are cumulative with no count to keep anywhere.
///   </description></item>
///   <item><description>
///     <b>Writer</b> — <see cref="TryTakeExclusive"/> opens
///     <c>FileAccess.ReadWrite</c> / <c>FileShare.Read</c>. A reader's
///     <c>FileShare.Read</c> does not admit <c>Write</c>, so the exclusive open
///     is refused while <b>any</b> reader holds it; and a reader's later
///     <c>FileShare.Read</c> does not admit the writer's granted <c>Write</c>,
///     so no reader can start while the writer holds it. Two writers exclude
///     each other by the same rule.
///   </description></item>
/// </list>
/// <para>
/// ⚠️ <b>The writer shares <c>Read</c> rather than nothing, and the difference
/// is a sentence rather than a lock.</b> With <c>FileShare.None</c> the
/// exclusion is identical — the arithmetic above never reaches the writer's own
/// share mode — but <b>nothing could read the record</b>, so a peer refused by
/// a reinstall could not say whose reinstall, and could not quote how far in it
/// is. <see cref="Read"/> opens <c>FileAccess.Read</c> /
/// <c>FileShare.ReadWrite | Delete</c>, which is wide enough to admit the
/// writer's granted access and is therefore the one open that succeeds against
/// a holder of either kind. It takes nothing and blocks nothing.
/// </para>
/// <para>
/// <b>The kernel releases a handle however the process dies, and that is the
/// whole reason this is not a named semaphore.</b> A semaphore does span
/// threads, which a named mutex does not — <see cref="Sessions.MachineMutex"/>
/// says why in its own remarks, and this claim is held across a 203.8 MB
/// download inside an <c>async</c> method whose continuations move between pool
/// threads — but a semaphore's count is <b>not</b> restored when its holder
/// dies, so one crashed reinstall would refuse every <c>browserai_init</c> on
/// the machine until the next reboot. A <see cref="FileStream"/> has no thread
/// affinity at all and Windows closes it on any death, clean or not.
/// </para>
/// <para>
/// <b>Held-ness is a sharing violation and never the file's existence</b>, which
/// is the same rule <c>SessionLock</c> follows for <c>browserai.json</c> and for
/// the same reason: a crashed holder leaves the file behind, so existence would
/// mean <i>somebody died here once</i> rather than <i>somebody is working
/// now</i>.
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
/// <b>There is no intent marker, no drain and no wait, and writer starvation is
/// accepted.</b> A reinstall whose exclusive open is refused says so at once and
/// names what is holding the root; it does not publish an intent that would stop
/// new sessions starting, and it does not wait. A machine that always has one
/// session open therefore never lets a reinstall through — that is the
/// maintainer's decision and it is not a defect to be mitigated. Waiting on a
/// browser a human may never close is exactly the shape this product spent a
/// week removing.
/// </para>
/// <para>
/// <b>The order it is taken in, and why nothing here can deadlock.</b> This lock
/// is <b>outermost</b>: <c>SessionManager.ReinstallBrowserAsync</c> takes it
/// first and only then reaches <see cref="BrowserProvisioner"/>, which takes the
/// per-family provisioning mutexes — one for a family, and chromium, firefox and
/// <c>shared</c> in that fixed order for the shared target. <b>No path anywhere
/// takes a provisioning mutex and then asks for this</b>, so there is no cycle to
/// close; and every acquisition on both sides is non-blocking — these opens
/// succeed at once or fail, the mutexes use <c>LockScopes.NeverWaits</c> — so
/// even a future edit that inverted the order would produce a refusal rather
/// than a hang.
/// </para>
/// <para>
/// ⚠️ <b>The file is still called <c>reinstall.lock</c> and the name is now
/// narrower than what it guards</b>, since every session holds it. It is kept
/// because a dated measurement names it —
/// [the cross-user table](../../../kb/windows/detection.md#two-users-and-one-install-root--what-spans-users-and-what-does-not--measured-2026-08-20)
/// lists <c>&lt;browsers&gt;\reinstall.lock</c> by name — and renaming it would
/// leave that measurement describing a file that does not exist, which this
/// repository treats as worse than a name that has outgrown its meaning.
/// </para>
/// </remarks>
internal sealed class MaintenanceLock : IDisposable
{
    /// <summary>
    /// The claim file's name, at the root of the browsers directory.
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

    /// <summary>Where a browsers root's claim file is.</summary>
    /// <param name="browsersDirectory">The browsers root.</param>
    /// <returns>The absolute path.</returns>
    public static string PathIn(string browsersDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);
        return Path.Combine(browsersDirectory, FileName);
    }

    /// <summary>
    /// Takes the claim <b>shared</b>, for a session that is about to open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It creates the file when it is not there, and that is required rather
    /// than convenient.</b> A reader that treated <i>absent</i> as <i>nothing to
    /// take</i> would hold no handle at all, and a reinstall starting a moment
    /// later would find the root free and delete the tree under a live session.
    /// <c>FileMode.OpenOrCreate</c> with <c>FileAccess.Read</c> is a
    /// <c>CreateFileW</c> with <c>OPEN_ALWAYS</c> and <c>GENERIC_READ</c>:
    /// creating the name is governed by the directory's permissions rather than
    /// by the access asked for on the file, so no writer is needed to bring it
    /// into existence.
    /// </para>
    /// <para>
    /// <b>It writes nothing, ever.</b> The record belongs to the writer; a
    /// reader that stamped itself would have to open for write, which is the one
    /// access that would make two sessions exclude each other.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root. Created if absent.</param>
    /// <param name="denial">What the kernel said, when the claim was not taken.</param>
    /// <param name="detail">Windows own message, when the claim was not taken.</param>
    /// <returns>The claim, or <see langword="null"/> when it could not be taken.</returns>
    public static MaintenanceLock? TakeShared(string browsersDirectory, out MaintenanceDenial denial, out string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);

        denial = MaintenanceDenial.None;
        detail = string.Empty;

        try
        {
            _ = Directory.CreateDirectory(browsersDirectory);

            return new MaintenanceLock(new FileStream(
                PathIn(browsersDirectory),
                FileMode.OpenOrCreate,
                FileAccess.Read,
                FileShare.Read));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // ⚠️ WHICH ONE IT WAS IS CARRIED OUT, and until 2026-08-24 it was
            // not: every cause here wore the reinstall's sentence, so an ACL
            // denial, a full volume and a path that is too long all told the
            // caller to wait minutes for a download that was not running.
            //
            // ***Corrected 2026-08-24 (previously "Held by a reinstall, denied,
            // or unreachable. Every one of them means this process may not open a
            // session against this root, and Describe says which for the
            // sentence").*** The last clause was the false one, and it is the
            // finding in miniature: `Describe` returns the LAST WRITER's line and
            // nothing truncates the file when a reinstall ends, so it cannot say
            // which. The kernel had already answered -- a sharing violation is a
            // holder and nothing else on this open, per the exclusion arithmetic
            // in this type's remarks -- and the answer was being discarded one
            // line above the sentence that needed it.
            denial = failure is IOException io && RenameWindow.IsSharingViolation(io)
                ? MaintenanceDenial.Contended
                : MaintenanceDenial.Unreachable;

            detail = failure.Message;

            return null;
        }
    }

    /// <summary>
    /// Takes the claim <b>exclusively</b>, for a reinstall.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>FileMode.Create</c>, so the record is this holder's and not the
    /// last one's.</b> A stale line left by a crashed reinstall would otherwise
    /// be quoted at a peer as though it were current.
    /// </para>
    /// <para>
    /// <b>The record is one line of plain text and deliberately not JSON.</b>
    /// Nothing parses it; its only consumer is a sentence, so a schema would be a
    /// second thing to keep in step for no reader's benefit. Which of the two
    /// causes blocked a call is decided by the <i>session census</i> rather than
    /// by reading this file, because the two are mutually exclusive by
    /// construction: a writer cannot hold the claim while any session does.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root. Created if absent.</param>
    /// <param name="target">What is being reinstalled, for the sentence a peer reads.</param>
    /// <param name="denial">What the kernel said, when the claim was not taken.</param>
    /// <param name="detail">Windows own message, when the claim was not taken.</param>
    /// <returns>The claim, or <see langword="null"/> when it could not be taken.</returns>
    public static MaintenanceLock? TryTakeExclusive(string browsersDirectory, string target, out MaintenanceDenial denial, out string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);
        ArgumentNullException.ThrowIfNull(target);

        denial = MaintenanceDenial.None;
        detail = string.Empty;

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
                writer.Write(DescribeSelf(target));
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
            // A session holds it shared, another reinstall holds it exclusively,
            // or it could not be reached at all -- and the third is carried out
            // separately for the same reason the shared take carries it: a
            // census of zero concludes "another reinstall has it", which is a
            // confident wrong diagnosis when the truth is that nothing could open
            // the file.
            denial = failure is IOException io && RenameWindow.IsSharingViolation(io)
                ? MaintenanceDenial.Contended
                : MaintenanceDenial.Unreachable;

            detail = failure.Message;

            return null;
        }
    }

    /// <summary>
    /// What the current holder wrote about itself, for a refusal to quote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It takes nothing and blocks nothing.</b> The open is
    /// <c>FileAccess.Read</c> with <c>FileShare.ReadWrite | Delete</c> — wide
    /// enough to admit a writer's granted access, so it is the one open that
    /// succeeds against a holder of either kind, and narrow enough that it can
    /// never be what excludes anybody.
    /// </para>
    /// <para>
    /// ⚠️ <b>The line it returns is the <i>last writer's</i>, which is not the
    /// same as <i>the current holder's</i>.</b> Nothing truncates the file when a
    /// reinstall ends, so a root whose last reinstall finished an hour ago still
    /// carries that line. It is quoted only on a path that has already
    /// established the claim is held, and where the session census has already
    /// been consulted — see <c>SessionManager.ReinstallBrowserAsync</c>, which
    /// names open sessions when there are any and quotes this only when there are
    /// none.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root.</param>
    /// <returns>What the last writer said about itself, or a sentence saying why that could not be read.</returns>
    public static string Describe(string browsersDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);

        return Read(PathIn(browsersDirectory));
    }

    /// <summary>
    /// How far into its work the reinstall holding this root is, measured from
    /// outside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both figures are read off the filesystem, so this works from a
    /// different process</b> — which is the only case that matters. A peer
    /// refused by a reinstall cannot see that reinstall's
    /// <see cref="BrowserProvisioner"/> at all: the attempt, its stopwatch and
    /// its samples are in another process's memory. What both processes can see
    /// is the disk.
    /// </para>
    /// <para>
    /// <b>Bytes are the download staging directory and nothing wider</b>, and
    /// that is what makes the number baseline-free. A reinstall deletes a tree
    /// and then downloads it, so bytes under the browsers <i>root</i> fall and
    /// then rise and a reader with no baseline cannot tell which. The installer's
    /// <c>TEMP</c> points at <see cref="BrowserProvisioner.DownloadDirectoryName"/>
    /// inside the root, so the archive in flight is the only thing there, it
    /// starts at nothing, and upstream unlinks it once extraction is done.
    /// </para>
    /// <para>
    /// <b>Elapsed is the claim file's last write time</b>, which is the instant
    /// <see cref="TryTakeExclusive"/> stamped its record — so it is a fact of the
    /// filesystem rather than a field somebody has to parse out of a sentence.
    /// </para>
    /// <para>
    /// ⚠️ <b>Zero bytes is not a stall and the renderer must not say it is.</b>
    /// The staging directory is empty during the delete, which comes first, and
    /// again after extraction begins. See
    /// <c>SessionErrors.BrowsersAreBeingReinstalled</c>, which says which of
    /// those it cannot distinguish rather than implying progress it has not
    /// measured.
    /// </para>
    /// </remarks>
    /// <param name="browsersDirectory">The browsers root.</param>
    /// <returns>The reading, or <see langword="null"/> when there is no claim file to time from.</returns>
    public static MaintenanceProgress? ProgressOf(string browsersDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browsersDirectory);

        try
        {
            var claimed = File.GetLastWriteTimeUtc(PathIn(browsersDirectory));

            // The .NET sentinel for "no such file", which is what this answers
            // rather than throwing. A root with no claim file has no reinstall
            // to be timed.
            if (claimed.Year <= 1601)
            {
                return null;
            }

            var elapsed = DateTime.UtcNow - claimed;

            return new MaintenanceProgress(
                StagedBytes(Path.Combine(browsersDirectory, BrowserProvisioner.DownloadDirectoryName)),
                elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed);
        }
#pragma warning disable CA1031 // A progress clause that can fail the refusal it decorates is worse than a refusal with no clause.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
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

    /// <summary>
    /// What the download staging directory weighs, tolerating a tree another
    /// process is writing and unlinking underneath the walk.
    /// </summary>
    /// <param name="staging">The staging directory, which need not exist.</param>
    /// <returns>The byte total, or 0.</returns>
    private static long StagedBytes(string staging)
    {
        try
        {
            var directory = new DirectoryInfo(staging);

            return directory.Exists
                ? directory.EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                }).Sum(file => file.Length)
                : 0;
        }
#pragma warning disable CA1031 // Same reason as the caller's: no clause beats a thrown refusal.
        catch (Exception)
#pragma warning restore CA1031
        {
            return 0;
        }
    }

    /// <summary>What this process is doing, for a peer's refusal to quote.</summary>
    /// <remarks>
    /// <b>The pid and its creation time together</b>, which is this repository's
    /// standing rule for naming a process: Windows reuses pids, and a sentence
    /// carrying a bare one eventually names a stranger.
    /// </remarks>
    /// <param name="target">What is being reinstalled.</param>
    /// <returns>The one line written into the claim file.</returns>
    private static string DescribeSelf(string target)
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();

        return $"PID {self.Id.ToString(CultureInfo.InvariantCulture)}, started {self.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}, is reinstalling '{target}'; it took this claim at {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)}";
    }

    private static string Read(string path)
    {
        try
        {
            // FileShare.ReadWrite because a writer has it open for WRITE, and a
            // reader that shared less than the holder's own access would be
            // refused by its own share mode rather than by the holder's.
            using var reader = new StreamReader(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));

            var said = reader.ReadToEnd().Trim();

            return said.Length is 0
                ? $"nothing has ever written a record into '{path}'"
                : said;
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            return $"there is no '{path}', so no reinstall has ever run against this root";
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The refusal stands either way -- the sharing violation is what
            // decided it, and this only names who.
            return $"its holder could not be identified from '{path}' ({failure.Message})";
        }
    }
}

/// <summary>
/// How far into its work a reinstall is, as a peer refused by it can measure
/// from outside the process running it.
/// </summary>
/// <param name="StagedBytes">
/// What the download staging directory weighs right now. <b>Zero means the
/// archive is not on disk</b> — the delete, or an extraction already under
/// way — and never that nothing is happening.
/// </param>
/// <param name="Elapsed">How long ago the claim was taken.</param>
internal readonly record struct MaintenanceProgress(long StagedBytes, TimeSpan Elapsed);

/// <summary>Why a claim on the browsers root could not be taken.</summary>
/// <remarks>
/// <para>
/// <b>The discriminator is the kernel's, not a guess.</b> No code outside
/// <c>ERROR_SHARING_VIOLATION</c> and <c>ERROR_LOCK_VIOLATION</c> can be produced
/// by a holder on this open — the exclusion arithmetic in
/// <see cref="MaintenanceLock"/>'s remarks is what makes that exhaustive — so
/// anything else is a failure to reach the file at all, and the two recoveries
/// are different: one is waited out, the other is fixed. <b>That is the direction
/// <see cref="Unreachable"/> needs</b>, and it is sound: a caller who really is
/// behind a reinstall is never shown the unreachable row.
/// </para>
/// <para>
/// ⚠️ ***Corrected 2026-08-24, same day (previously "<c>ERROR_SHARING_VIOLATION</c>
/// and <c>ERROR_LOCK_VIOLATION</c> are the only codes a holder produces on this
/// open").*** As written it read as a biconditional and was relied on as one, and
/// <b>the converse is false for 33</b>. <c>reinstall.lock</c> is claimed by share
/// mode alone (<see cref="MaintenanceLock.TakeShared"/>,
/// <see cref="MaintenanceLock.TryTakeExclusive"/>), and the only byte-range lock
/// in this product is <c>NativeFile.TakeGate</c>, reachable solely through
/// <c>OpenForLockedAppend</c> on <b>log</b> files — so <b>no BrowserAI holder can
/// produce <c>ERROR_LOCK_VIOLATION</c> on this open at all</b>. A 33 here can only
/// come from a foreign process byte-range-locking the file, and it is routed to
/// <see cref="Contended"/> and thence to <i>"BrowserAI is replacing the browsers …
/// right now"</i> with a progress clause counting from zero.
/// </para>
/// <para>
/// <b>Left as it is, deliberately, and this is the note rather than the fix.</b>
/// It is the same trade already taken and written down for code 32 — an AV
/// scanner, a backup agent or an indexer holding <c>reinstall.lock</c> reads as a
/// reinstall — in <c>SessionManager.TheRootCouldNotBeClaimed</c>'s remarks, and
/// the recovery a caller is given (wait, then look again) is right for a foreign
/// holder too. What was wrong was a sentence that made the misdiagnosis
/// impossible; naming it is what stops the next reader deriving from it.
/// </para>
/// </remarks>
internal enum MaintenanceDenial
{
    /// <summary>Nothing was denied.</summary>
    None,

    /// <summary>Somebody holds it in a mode that excludes this one.</summary>
    Contended,

    /// <summary>It could not be opened at all, and why is not knowable here.</summary>
    Unreachable,
}
