// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using BrowserAI.Interop;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A real, long-lived process whose <b>image path</b> is inside a directory the
/// test chooses — which is the only way to provoke the checks that ask whether
/// something is running out of BrowserAI's browsers root.
/// </summary>
/// <remarks>
/// <para>
/// <b>It plants <c>cmd.exe</c>, and the choice is forced rather than
/// arbitrary.</b> The obvious candidate was this suite's own
/// <c>BrowserAI.TestProbe.exe</c>, and copying it does not work: it is a
/// framework-dependent apphost, so a lone <c>.exe</c> in a strange directory
/// fails to find its <c>.dll</c>, its <c>runtimeconfig.json</c> and its
/// dependency closure, and dies immediately. That failure is silent from the
/// outside — the launch succeeds, the process is gone before anything looks —
/// and it made a tool that <i>deletes browser trees</i> act on an empty answer.
/// Observed twice on 2026-08-16 before the wait below existed.
/// </para>
/// <para>
/// <c>cmd.exe</c> is self-contained in the sense that matters: it loads only
/// System32 DLLs, which resolve by absolute path rather than beside the image.
/// With <b>no arguments</b> it reads its stdin forever, and
/// <see cref="JobObjectScope"/> holds the write end of that pipe for the scope's
/// life — so it stays alive without a busy loop, a timer or a script.
/// <c>node.exe</c> from the payload would work equally well and was rejected for
/// one reason: it is 88 MB to copy, and it is absent on a clean clone, which
/// would make the error catalogue's census depend on whether somebody had run
/// the payload build.
/// </para>
/// <para>
/// <b>The wait is not optional.</b> <c>CreateProcessW</c> returns as soon as the
/// process object exists, and a snapshot taken in that instant can legitimately
/// miss it. A test that skipped the wait would ask a destructive tool to act on
/// "nothing is running", which is the one wrong answer that cannot be undone.
/// </para>
/// </remarks>
internal static class PlantedProcess
{
    /// <summary>
    /// Copies a runnable image into <paramref name="directory"/>, starts it, and
    /// waits until the product's own enumeration can see it.
    /// </summary>
    /// <param name="scope">The job that owns it, so a failed assertion cannot leak it.</param>
    /// <param name="directory">Where to plant it. Created if absent.</param>
    /// <param name="root">
    /// The root the caller is going to ask about, which is what the wait watches.
    /// Usually the browsers root or the browser's own directory.
    /// </param>
    /// <returns>The running process and where its image was planted.</returns>
    /// <exception cref="InvalidOperationException">It never became visible.</exception>
    public static async Task<(LaunchedProcess Process, string ImagePath)> StartInAsync(
        JobObjectScope scope,
        string directory,
        string root)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var planted = Path.Combine(directory, "planted-process.exe");

        _ = Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), planted, overwrite: true);

        var process = scope.Launch(planted, directory);
        var waited = Stopwatch.StartNew();

        while (BrowserProcesses.RunningFrom(root).All(entry => entry.ProcessId != process.Id))
        {
            if (waited.Elapsed > TestDefaults.ProcessHang)
            {
                throw new InvalidOperationException(
                    $"The process planted at '{planted}' (pid {process.Id}) was never visible under '{root}' after {waited.Elapsed.TotalSeconds:F1} s. "
                    + "Without it this test would ask a tool that deletes browser trees to act on an empty answer.");
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        return (process, planted);
    }
}
