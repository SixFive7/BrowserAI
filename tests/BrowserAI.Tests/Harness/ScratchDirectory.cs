// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
#pragma warning disable CA1031 // A leftover directory is the next run's reclaim problem, never this run's failure.
        catch (Exception)
#pragma warning restore CA1031
        {
            // A browser or a log handle that has not let go yet is exactly the
            // case ScratchRoot's sweep exists for.
        }
    }
}
