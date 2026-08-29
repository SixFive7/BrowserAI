// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using BrowserAI.Runtime;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A disposable directory under the repository's own gitignored scratch root.
/// </summary>
/// <remarks>
/// <para>
/// Every directory the suite creates for test data lands under <c>.work\</c>,
/// per this repository's own rules, and never in <c>%TEMP%</c>. Deletion is
/// best-effort: a directory a previous run left behind is reclaimed by the sweep
/// in <see cref="ScratchRoot"/> rather than failing the run that finds it.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-29 (previously "Everything the suite writes lands
/// under <c>.work\</c> … and never in <c>%TEMP%</c> or beside the product's real
/// <c>%LocalAppData%\BrowserAI</c>").</b> The second half was never true of what
/// the suite <i>starts</i> — a published slice writes its records into the real
/// process log, which is what <see cref="ProcessLogRecords"/> reads — and since
/// 2026-08-29 it is not true of the harness either: <see cref="SpawnRecord"/>
/// announces a reclaim it performed to that same log. The two places outside the
/// repository are named on <see cref="ScratchRoot.ProfileScratch"/>.
/// </para>
/// </remarks>
internal sealed class ScratchDirectory : IDisposable
{
    private ScratchDirectory(string path)
    {
        Path = path;
        _ = Directory.CreateDirectory(path);
    }

    /// <summary>The directory's absolute path.</summary>
    public string Path { get; }

    /// <summary>Creates a fresh scratch directory named after the caller.</summary>
    public static ScratchDirectory Create(string label) =>
        new(System.IO.Path.Combine(ScratchRoot.Path, $"{label}-{Guid.NewGuid():N}"));

    /// <summary>
    /// Creates a fresh scratch directory <b>inside this user's profile</b>, for
    /// the one thing the repository's own scratch root cannot hold.
    /// </summary>
    /// <remarks>
    /// See <see cref="ScratchRoot.ProfileScratch"/>: a published BrowserAI
    /// refuses to serve out of an app root outside the current user's profile,
    /// so an app root handed to one through
    /// <see cref="BrowserAiPaths.AppRootOverride"/> has to come from here.
    /// Nothing else may.
    /// </remarks>
    /// <param name="label">What the directory is for.</param>
    /// <returns>The directory.</returns>
    public static ScratchDirectory CreateUnderProfile(string label) =>
        new(System.IO.Path.Combine(ScratchRoot.ProfileScratch, $"{label}-{Guid.NewGuid():N}"));

    /// <summary>
    /// Removes a tree the way the product does, and answers with what would not
    /// go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The suite's one way to delete a directory, because
    /// <c>Directory.Delete(recursive: true)</c> is banned repository-wide</b>
    /// (<c>build/BannedSymbols.txt</c>). The ban has no exception for test code
    /// and never had one to lose: the only recorded violation of that rule in
    /// this repository was in test code — <see cref="ScratchRoot"/>'s reclaim
    /// pass, which used the framework primitive until 2026-08-17 and was caught
    /// by a manual audit rather than by anything mechanical.
    /// </para>
    /// <para>
    /// It returns the survivors instead of throwing so that both callers are
    /// honest ones: a teardown discards them, because a leftover directory is
    /// the next run's reclaim problem rather than this run's failure, and a test
    /// whose next assertion depends on the directory being gone asserts on the
    /// list.
    /// </para>
    /// </remarks>
    /// <param name="path">The directory to remove. One that does not exist is not a failure.</param>
    /// <returns>Every node that could not be deleted, one line each.</returns>
    public static IReadOnlyList<string> RemoveTree(string path)
    {
        var survivors = new List<string>();

        TreeDelete.Remove(path, survivors);

        return survivors;
    }

    /// <summary>
    /// Removes a tree once whatever is holding it has let go, and answers with
    /// what still would not go after <paramref name="patience"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Bounded and retried on the <i>whole</i> tree rather than per file,
    /// because a terminated process is signalled before the kernel has torn its
    /// handles down:</b> <c>TerminateProcess</c> returning is not proof that a
    /// mapped file has been released, and neither is a browser vanishing from
    /// the process table. What a caller measures with this is whether a tree
    /// becomes deletable <i>at all</i> — a handle on its way out against a leak
    /// nothing will ever release — and never how fast.
    /// </para>
    /// <para>
    /// ⚠️ <b>One routine and not three.</b> It was three: two private copies in
    /// <c>BrowserContainmentTests</c> and <c>BrowserIdleTimerTests</c>, and a
    /// third about to be typed into <c>FirefoxSessionTests</c> — which is the
    /// shape <see cref="TreeDelete"/>'s own remarks name as how two callers end
    /// up with one behaviour and the third with another, with nothing reporting
    /// the difference. <paramref name="patience"/> stays a parameter rather than
    /// becoming a constant here: it is a hang detector belonging to the caller's
    /// scenario, and nothing may assert on it.
    /// </para>
    /// </remarks>
    /// <param name="path">The directory to remove. One that does not exist is not a failure.</param>
    /// <param name="patience">How long a hold is waited out before it is reported as a survivor.</param>
    /// <returns>Every node that would still not go, one line each.</returns>
    public static async Task<IReadOnlyList<string>> RemoveTreeWhenReleasedAsync(string path, TimeSpan patience)
    {
        var survivors = new List<string>();
        var waited = Stopwatch.StartNew();

        while (true)
        {
            survivors.Clear();
            TreeDelete.Remove(path, survivors);

            if (survivors.Count is 0 || waited.Elapsed > patience)
            {
                return survivors;
            }

            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose() =>
        // Best-effort by design: TreeDelete collects what would not go rather
        // than throwing, so a browser or a log handle that has not let go yet
        // becomes ScratchRoot's sweep problem instead of a failed test. That is
        // also why there is no try/catch left here -- there is nothing to catch.
        _ = RemoveTree(Path);
}
