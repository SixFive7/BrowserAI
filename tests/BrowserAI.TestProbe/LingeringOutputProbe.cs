// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;

namespace BrowserAI.TestProbe;

/// <summary>
/// A RegisterAI that writes its whole document, leaves a process behind that holds its
/// output open, and exits: the shape that lost a written document in the gate at
/// <c>a557aa0f</c> on 2026-10-10.
/// </summary>
/// <remarks>
/// <b>The process left behind inherits the pipe and nothing else</b>: it is this probe
/// started again with every handle this process may hand on, which the framework's start
/// always passes, and its own window suppressed. It sleeps for the seconds it is given and
/// exits, so the pipe ends then and not before.
/// </remarks>
internal static class LingeringOutputProbe
{
    /// <summary>Writes the document, starts the holder, and exits.</summary>
    /// <param name="documentPath">The file whose text is the document.</param>
    /// <param name="holdSeconds">How long the process left behind holds the pipe.</param>
    /// <returns>Zero.</returns>
    public static int Write(string documentPath, string holdSeconds)
    {
        var document = File.ReadAllText(documentPath);

        // Console, deliberately: what is under test is the pipe, as in TransportChild.
        Console.Out.Write(document);
        Console.Out.Flush();

        using var holder = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "registerai-holder", holdSeconds },
        });

        return 0;
    }

    /// <summary>Writes the document, starts the holder, says which process it is, and exits.</summary>
    /// <remarks>
    /// <b>Added 2026-10-11 for 64 c</b>, the gate drivers' arm and the real-client arms'
    /// harness arm: each asserts something about the process left behind, alive while a
    /// driver finishes or gone once an arm has ended, so the holder's identity is written
    /// where the arm can read it, its pid and its creation time on one line, before this
    /// process exits.
    /// </remarks>
    /// <param name="documentPath">The file whose text is the document.</param>
    /// <param name="holdSeconds">How long the process left behind holds the pipe.</param>
    /// <param name="reportPath">Where the holder's pid and creation time go.</param>
    /// <returns>Zero.</returns>
    public static int WriteAndReport(string documentPath, string holdSeconds, string reportPath)
    {
        var document = File.ReadAllText(documentPath);

        Console.Out.Write(document);
        Console.Out.Flush();

        using var holder = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "registerai-holder", holdSeconds },
        }) ?? throw new InvalidOperationException("The holder did not start.");

        File.WriteAllText(
            reportPath,
            string.Create(CultureInfo.InvariantCulture, $"{holder.Id} {holder.StartTime.ToUniversalTime().ToFileTimeUtc()}"));

        return 0;
    }

    /// <summary>Holds what it inherited for a while, and exits.</summary>
    /// <param name="holdSeconds">How long.</param>
    /// <returns>Zero.</returns>
    public static int Hold(string holdSeconds)
    {
        Thread.Sleep(TimeSpan.FromSeconds(double.Parse(holdSeconds, CultureInfo.InvariantCulture)));
        return 0;
    }
}
