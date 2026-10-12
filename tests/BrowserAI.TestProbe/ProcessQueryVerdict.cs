// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.TestProbe;

/// <summary>
/// What a failed <c>OpenProcess</c> over a pid the descendant walk already saw
/// actually means.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because a probe race read as a containment failure.</b> On
/// 2026-09-16, on a docs-only commit,
/// <c>JobContainmentTests.ADescendantTreeIsContainedAndNothingSurvivesTheLauncher</c>
/// went red after <c>escapees == 0</c> had already passed: a row came back with
/// a null <c>inOurJob</c> because <c>OpenProcess</c> failed for a descendant
/// that had exited between the toolhelp walk and the per-row query. The product
/// was never in question -- a process that has exited is contained by
/// definition, and the arm's own teardown assertion would have said so -- but
/// the rig had no way to express <i>gone</i> and so expressed <i>unknown</i>,
/// which the host reads as a failure.
/// </para>
/// <para>
/// ⚠️ <b>It is a classification and not a retry.</b> Nothing here re-queries,
/// waits, or tries again; the verdict is taken from what Windows said the first
/// time. And it narrows instead of widening: a query that fails for any reason
/// other than the two below is still <see cref="Verdict.Unreadable"/>, which is
/// still a red row carrying its note. The whole change is that <i>this process
/// is no longer there</i> stops being spelled the same way as <i>this process
/// could not be read</i>.
/// </para>
/// <para>
/// <b>Why each of the two is sound, stated and not assumed.</b>
/// <c>ERROR_INVALID_PARAMETER</c> is what <c>OpenProcess</c> returns for a pid
/// no live process has -- the pid is the only parameter that can be invalid, and
/// the walk proved it was well-formed one instant earlier.
/// <c>ERROR_ACCESS_DENIED</c> is the recycled case: this probe opens with
/// <c>PROCESS_QUERY_LIMITED_INFORMATION</c>, which Windows grants over every
/// same-user, unprotected process -- including one that has already exited while
/// somebody still holds a handle -- so a denial means the pid now names
/// something this process may not open, and a pid is only reissued after its
/// previous owner has gone.
/// </para>
/// <para>
/// <b>What it does not claim.</b> It does not say the process was ever in the
/// job, and the report keeps saying so separately: a row classified
/// <see cref="Verdict.Exited"/> still carries <c>inJobProcessIdList</c> read
/// from the kernel's own membership snapshots taken either side of the walk, and
/// the host still asserts that. So a process that vanished is held to having
/// been a member while it existed, which is the containment claim; what is
/// dropped is only the demand that a corpse answer <c>IsProcessInJob</c>.
/// </para>
/// <para>
/// ⚠️ <b>Added 2026-10-12 by addition, 95 c, the maintainer's words verbatim:
/// <i>"95 your recommendation"</i>.</b> A failed open is one of three ways a row
/// reads <see cref="Verdict.Exited"/> since that day. The launcher holds every
/// member from its join message and asks each walked process whether it is still
/// there before it asks the job, so a member that exited while a handle held it
/// reads Exited, and so does a pid that now names a process created after the one
/// the walk listed. A corpse still answers <c>IsProcessInJob</c>, and it answers
/// that it is in the job (20 of 20, measured 2026-10-12), which is why the
/// question waits. The paragraph above holds for <c>JobContainmentTests</c>, whose
/// trees are static; the Chromium and Firefox arms of
/// <c>BrowserContainmentTests</c> no longer hold an Exited row to the job's list,
/// because a helper born after the first list and gone before the second is in
/// neither, and the job's list leaves out an exited member even while a handle
/// holds it.
/// </para>
/// </remarks>
internal static class ProcessQueryVerdict
{
    /// <summary><c>ERROR_ACCESS_DENIED</c>.</summary>
    public const int AccessDenied = 5;

    /// <summary><c>ERROR_INVALID_PARAMETER</c> -- what a pid nobody holds produces.</summary>
    public const int InvalidParameter = 87;

    /// <summary>What the per-row query established about a walked process.</summary>
    internal enum Verdict
    {
        /// <summary>The handle opened and the row's membership was read from it.</summary>
        Queried,

        /// <summary>
        /// The process was gone by the time the row was queried. Contained by
        /// definition: it is not a survivor and it cannot be an escapee.
        /// </summary>
        Exited,

        /// <summary>
        /// The query failed for a reason that is not a vanished process, so
        /// nothing is known about this row and the run is red.
        /// </summary>
        Unreadable,
    }

    /// <summary>
    /// Classifies a failed <c>OpenProcess</c> over a pid the walk already saw.
    /// </summary>
    /// <param name="lastError">The Win32 error the failed call left behind.</param>
    /// <returns>
    /// <see cref="Verdict.Exited"/> for the two shapes a pid that is no longer
    /// there produces, and <see cref="Verdict.Unreadable"/> for everything else.
    /// </returns>
    public static Verdict ForFailedOpen(int lastError) => lastError switch
    {
        InvalidParameter => Verdict.Exited,
        AccessDenied => Verdict.Exited,
        _ => Verdict.Unreadable,
    };
}
