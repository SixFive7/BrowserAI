// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using BrowserAI.Runtime;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Writes Playwright's <c>INSTALLATION_COMPLETE</c> sentinel the way this suite
/// needs it written: by more than one writer at once, without either being
/// refused.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Found by running the suite ten times, 2026-08-16, at build-order
/// step 16.</b> <c>ReinstallBrowserTests</c> writes the marker to set up a
/// complete tree, and the rig's own default session <i>legitimately</i> starts
/// an install against the same empty root at the same moment — so the fake
/// installer writes the same file. <c>File.WriteAllText</c> opens
/// <c>FileShare.Read</c>, so the second writer is refused with <i>"the process
/// cannot access the file … because it is being used by another process"</i>.
/// It reproduced <b>once in ten runs of that class alone</b>, which is a red
/// build wearing a disguise and had nothing to do with the change that found it.
/// </para>
/// <para>
/// The content is the empty string from every writer, so there is nothing to
/// serialise and nothing that can be torn: sharing the write is the whole fix,
/// and the retry covers the instant in which a <i>previous</i> writer holds it
/// without sharing.
/// </para>
/// </remarks>
internal static class InstallationMarker
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <summary>Writes the marker into a browser revision directory.</summary>
    /// <param name="directory">The revision directory. Created if absent.</param>
    public static void Write(string directory)
    {
        _ = Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker);
        var clock = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                // FileShare.ReadWrite | Delete: another writer of the same empty
                // file must not be refused, and neither must a tree delete that
                // is about to remove the whole directory.
                using var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);

                return;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                if (clock.Elapsed >= Budget)
                {
                    throw;
                }

                Thread.Sleep(5);
            }
        }
    }
}
