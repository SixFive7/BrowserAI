// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Coordination;
using BrowserAI.Interop;

namespace BrowserAI.TestProbe;

/// <summary>
/// A background as a relay and a person's start meet it, and nothing more: it takes the
/// background's pipe and writes the background's record through the product's own
/// code, takes every connection and answers none.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-10 for two paths lane ARCH's helper T1 left untested on
/// 2026-10-09</b>: a relay that held the background it reached reads that background's
/// exit code when it goes and writes it into the record, and a person's start ends a
/// background that took the connection and never answered. Both need the background to
/// be a process of its own, whose pid and creation time the record names and whose
/// image a person's start verifies before it ends it.
/// </para>
/// <para>
/// <b>It takes the pipe the way the background does</b>, the first instance of the
/// name, and never calls for a client: a client's open succeeds on an instance that is
/// listening, and its question then waits for an answer that never comes, which is what
/// a hung background looks like from outside.
/// </para>
/// <para>
/// <b>It exits with the code it was given when its standard input ends</b>, the host's
/// way of ending it as a background that crashed with that code, and with <c>2</c> if the
/// patience runs out first; a person's start may end it sooner.
/// </para>
/// </remarks>
internal static class BackgroundStandIn
{
    /// <summary>The longest the stand-in lives if nothing ends it: a backstop, never a bound a test waits on.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(30);

    /// <summary>Takes the pipe, writes the record and the ready file, and waits.</summary>
    /// <param name="pipeName">The background's full pipe name.</param>
    /// <param name="recordPath">The background's record.</param>
    /// <param name="readyPath">Written with this process's pid once the pipe and the record are both there.</param>
    /// <param name="exitCode">The exit code once standard input ends.</param>
    /// <returns>The exit code.</returns>
    public static int Run(string pipeName, string recordPath, string readyPath, int exitCode)
    {
        using var listening = NamedPipes.CreateStreamServer(pipeName);

        _ = BackgroundRecord.Started(recordPath, "9.9.9-background-stand-in", Environment.ProcessPath ?? string.Empty, DateTimeOffset.UtcNow);

        var temporary = readyPath + ".writing";
        File.WriteAllText(temporary, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, readyPath, overwrite: true);

        var ended = Task.Run(() =>
        {
            using var input = Console.OpenStandardInput();
            var buffer = new byte[64];

            while (input.Read(buffer, 0, buffer.Length) > 0)
            {
            }
        });

        return ended.Wait(Patience) ? exitCode : 2;
    }
}
