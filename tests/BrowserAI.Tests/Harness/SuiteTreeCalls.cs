// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The suite's own calls for <see cref="TreeDelete"/>'s walk over its scratch: the
/// framework's, made on the caller's thread.
/// </summary>
/// <remarks>
/// <b>Added 2026-10-12 with the maintainer's 80 b and 81 c</b>, which put the product's
/// listings and deletions on the file-system thread and banned them elsewhere in the
/// product. The ban is the product's: the suite removes its scratch through the same walk,
/// so a junction under a scratch tree is unlinked and never followed, and with its own
/// calls, so a teardown keeps the synchronous <c>Dispose</c> every <c>using</c> in the
/// suite relies on.
/// </remarks>
internal sealed class SuiteTreeCalls : ITreeCalls
{
    /// <summary>The one instance.</summary>
    public static readonly SuiteTreeCalls Instance = new();

    private SuiteTreeCalls()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Files(string directory) => [.. Directory.EnumerateFiles(directory)];

    /// <inheritdoc />
    public IReadOnlyList<string> Directories(string directory) => [.. Directory.EnumerateDirectories(directory)];

    /// <inheritdoc />
    public void DeleteFile(string path) => File.Delete(path);

    /// <inheritdoc />
    public void DeleteEmptyDirectory(string path) => Directory.Delete(path);
}
