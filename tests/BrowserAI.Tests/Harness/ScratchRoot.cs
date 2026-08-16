// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The suite's scratch root, reclaimed once per run before anything else
/// happens.
/// </summary>
/// <remarks>
/// <para>
/// Every run starts by reclaiming what a previous run may have leaked. This
/// suite drives real processes, machine-wide named objects and real
/// directories, so a run that is killed — a failed assertion taking the host
/// with it, a debugger detached — leaves state behind that the <i>next</i> run
/// meets as a failure. That failure reports the wrong cause: it names the
/// change under test while describing the previous run's crash, and the time
/// goes on the wrong bug.
/// </para>
/// <para>
/// The reclaim is idempotent and never fatal. What it cannot delete, it leaves;
/// a directory still held open belongs to something still running, and killing
/// that is a later step's job with a later step's evidence.
/// </para>
/// </remarks>
internal static class ScratchRoot
{
    private static readonly Lock Gate = new();
    private static bool _reclaimed;

    /// <summary>
    /// <c>&lt;repo&gt;\.work\test-scratch</c>, created and swept on first use.
    /// </summary>
    public static string Path
    {
        get
        {
            var path = System.IO.Path.Combine(RepositoryLayout.Root.FullName, ".work", "test-scratch");

            lock (Gate)
            {
                if (!_reclaimed)
                {
                    _ = Directory.CreateDirectory(path);
                    Reclaim(path);
                    _reclaimed = true;
                }
            }

            return path;
        }
    }

    private static void Reclaim(string path)
    {
        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
#pragma warning disable CA1031 // See the type's remarks: reclaim is never fatal.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Held by something still alive. Left alone deliberately.
            }
        }
    }
}
