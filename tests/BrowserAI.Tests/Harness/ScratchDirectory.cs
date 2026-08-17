// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A disposable directory under the repository's own gitignored scratch root.
/// </summary>
/// <remarks>
/// Everything the suite writes lands under <c>.work\</c>, per this
/// repository's own rules, and never in <c>%TEMP%</c> or beside the product's
/// real <c>%LocalAppData%\BrowserAI</c>. Deletion is best-effort: a directory a
/// previous run left behind is reclaimed by the sweep in
/// <see cref="ScratchRoot"/> rather than failing the run that finds it.
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

    /// <inheritdoc />
    public void Dispose() =>
        // Best-effort by design: TreeDelete collects what would not go rather
        // than throwing, so a browser or a log handle that has not let go yet
        // becomes ScratchRoot's sweep problem instead of a failed test. That is
        // also why there is no try/catch left here -- there is nothing to catch.
        _ = RemoveTree(Path);
}
