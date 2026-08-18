// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;

namespace BrowserAI.Sessions;

/// <summary>
/// Opening a file this process is <b>entitled</b> to open, while another process
/// is replacing it by rename.
/// </summary>
/// <remarks>
/// <para>
/// <b>The condition is a Windows state, not a defect in either process.</b>
/// Every record this product writes arrives by <c>MoveFileEx</c> with
/// <c>MOVEFILE_REPLACE_EXISTING</c>, which is what makes a reader see the old
/// record or the new one and never a torn one. While that rename is in flight
/// the file being replaced enters a <b>delete-pending</b> state, and every new
/// open of that name is refused <c>STATUS_DELETE_PENDING</c> — which surfaces as
/// <c>ERROR_ACCESS_DENIED</c>, that is, as an
/// <see cref="UnauthorizedAccessException"/> and <b>not</b> as an
/// <see cref="IOException"/>. A handler written for the sharing violation
/// anyone would expect therefore never runs.
/// </para>
/// <para>
/// <b>Both sides of the rename need this and only one side had it.</b> The
/// writers — <c>SessionLock.Replace</c> and <c>SessionIndex.Replace</c> — have
/// waited out a busy destination since 2026-08-16, each with the measured budget
/// and the note explaining that a concurrent reader or a virus scanner is a live
/// condition rather than a bug. The readers had nothing, which made the pair
/// asymmetric in the direction that throws:
/// <c>SessionLock.ReadRecord</c> and the acquire path's own open both propagated
/// <see cref="UnauthorizedAccessException"/> out to a caller, past
/// <c>Contended</c>, which handles only a sharing violation and a
/// <c>LockFileException</c>. One BrowserAI asking whether a session was locked,
/// at the instant another BrowserAI rewrote its own lock, threw.
/// </para>
/// <para>
/// <b>Measured 2026-08-18 at <c>SuiteParallelism.Unbounded</c>:</b> twice in
/// twenty-eight full-suite runs, at two different call sites — a rename in
/// <c>SessionLockTests.ARenameCannotReplaceALockFileWhoseOwnHandleIsStillOpen</c>
/// and a read in <c>SessionLockTests.ARewriteIsNeverObservedTorn</c>, the latter
/// with a reader in a tight loop beside a rewriter doing a hundred renames. Two
/// sites in two streaks is a property of every record rewrite under contention
/// rather than of two tests, which is why this is a primitive and not a third
/// patch.
/// </para>
/// <para>
/// ⚠️ <b>THE DISTINCTION THIS TYPE EXISTS TO HOLD, and getting it backwards is
/// worse than not having it.</b> A refusal means one of two completely different
/// things depending on who is asking, and only one of them may be waited out:
/// </para>
/// <list type="table">
/// <listheader>
/// <term>Caller</term>
/// <description>What a refusal means, and what it must do</description>
/// </listheader>
/// <item>
/// <term><b>Entitled</b> — <c>SessionLock.ReadRecord</c>,
/// <c>SessionLock.OpenHeld</c>, <c>SessionIndex.FollowOne</c></term>
/// <description>
/// It already holds whatever gives it the right to look: the per-directory
/// mutex, or nothing more than the fact that reading a record is always allowed.
/// A denial is therefore <i>somebody else is mid-rename</i>, which is a window
/// that closes. <b>Wait it out.</b>
/// </description>
/// </item>
/// <item>
/// <term><b>Not entitled</b> — <c>InstanceDirectory</c>'s claim,
/// <c>LiveInstances</c>' registration, <c>FirefoxProfile</c>'s probe,
/// <c>SessionLock.ProbeForHolder</c></term>
/// <description>
/// The refusal <b>is the answer</b>. Each of those opens exists precisely to
/// find out whether something else holds the thing, and a retry would convert
/// <i>somebody owns this</i> into <i>eventually, nobody did</i> — which is the
/// mechanism, inverted. <b>Never route one of those through here.</b>
/// <c>SessionLock.ProbeForHolder</c> is the newest and the clearest case: it
/// opens <c>lock.json</c> in front of the per-directory gate to find out whether
/// anyone owns it, so a denial waited out would be a live owner waited out. It
/// is also the one that cannot decide <see cref="UnauthorizedAccessException"/>
/// at all — delete-pending and a permanent ACL denial arrive identically — so it
/// treats that as <i>no answer</i> and lets the gate settle it.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>Only <see cref="UnauthorizedAccessException"/>, never
/// <see cref="IOException"/>.</b> The writers catch both because a rename may
/// meet either; a reader may not. A sharing violation on one of these opens
/// means the holder opened the file in a mode that excludes us, which is a real
/// answer the acquire path turns into <c>Contended</c> — and waiting it out
/// would be waiting for a live owner to go away.
/// </para>
/// </remarks>
internal static class RenameWindow
{
    /// <summary>
    /// How long an entitled reader waits out a rename that is replacing the file
    /// it is opening.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A hang detector rather than a budget, and it is stated once so the two
    /// sides of the rename cannot drift apart.</b>
    /// <c>SessionLock.MoveBudget</c> is this same value and reads it from here.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 (previously two seconds, itself raised from
    /// "five attempts over 150 ms" on 2026-08-16 for the same reason).</b> Two
    /// seconds was reachable, and the failure it produced blamed the wrong thing.
    /// Measured at <c>SuiteParallelism.Unbounded</c>: <i>"could not be replaced
    /// after <b>3 attempts over 2.3 s</b>"</i> — a loop whose own sleeps total
    /// <b>15 ms</b> across those three attempts. The file was not the problem;
    /// the process did not get scheduled, and the message said <i>"something else
    /// is holding it open"</i> about a machine that was merely busy. That is a
    /// promptness assertion wearing a hang detector's name, in shipped code, and
    /// it is the exact class the 2026-08-18 timing work removed from the suite.
    /// </para>
    /// <para>
    /// <b>Thirty seconds, and the arithmetic is the justification.</b> The event
    /// underneath is one syscall wide — microseconds — and the retry loop intends
    /// to spend milliseconds. Thirty seconds is three orders of magnitude above
    /// the contention and, more to the point, <b>2,000× the sleep budget the loop
    /// actually asks for</b>, which is the number that matters when what expires
    /// it is starvation rather than the file. A slow machine must not reach it.
    /// </para>
    /// <para>
    /// It stays bounded rather than becoming open-ended because a <b>permanent</b>
    /// denial is a different fault and must still be reported: a file somebody
    /// has opened in a mode that will never permit us is not a window, and
    /// waiting on it forever would be the silent failure this whole repository is
    /// against.
    /// </para>
    /// <para>
    /// <b><c>SessionIndex</c> keeps its own 500 ms and is deliberately not merged
    /// into this.</b> That is not the same decision made twice: an index entry's
    /// rename is <b>fail-safe</b> — giving up leaves the entry for the next use to
    /// re-assert, and the caller never sees it — so a short budget there trades a
    /// re-assertion for not stalling a session start. This one is <b>fatal</b>:
    /// exhausting it throws out of a lock rewrite. Different consequences,
    /// different numbers, and the reason is written at both.
    /// </para>
    /// </remarks>
    public static TimeSpan Budget { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs an open that this process is entitled to make, retrying only while
    /// the refusal is a rename in flight.
    /// </summary>
    /// <typeparam name="T">What the open produces.</typeparam>
    /// <param name="open">
    /// The open. Called again on a denial, which differs from the call that
    /// failed in the only way that can help: the world has had time to move on.
    /// </param>
    /// <returns>Whatever <paramref name="open"/> returned.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// The denial outlasted <see cref="Budget"/>, so it is not a window.
    /// </exception>
    public static T WaitOut<T>(Func<T> open)
    {
        ArgumentNullException.ThrowIfNull(open);

        var clock = Stopwatch.StartNew();
        var delay = 5;

        while (true)
        {
            try
            {
                return open();
            }
            catch (UnauthorizedAccessException) when (clock.Elapsed < Budget)
            {
                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 100);
            }
        }
    }
}
