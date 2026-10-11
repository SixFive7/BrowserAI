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
    public static async Task<int> WriteAsync(string documentPath, string holdSeconds)
    {
        var document = await File.ReadAllTextAsync(documentPath);

        // Console's handle, deliberately: what is under test is the pipe, as in TransportChild.
        await WriteToStandardOutputAsync(document);

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
    public static async Task<int> WriteAndReportAsync(string documentPath, string holdSeconds, string reportPath)
    {
        var document = await File.ReadAllTextAsync(documentPath);

        await WriteToStandardOutputAsync(document);

        using var holder = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "registerai-holder", holdSeconds },
        }) ?? throw new InvalidOperationException("The holder did not start.");

        await File.WriteAllTextAsync(
            reportPath,
            string.Create(CultureInfo.InvariantCulture, $"{holder.Id} {holder.StartTime.ToUniversalTime().ToFileTimeUtc()}"));

        return 0;
    }

    /// <summary>Writes the document to this process's standard output and waits until it is on the wire.</summary>
    /// <remarks>
    /// The bytes are <c>Console.Out</c>'s own, in its encoding, written by the product's
    /// pump since 2026-10-11 (previously <c>Console.Out.Write</c> and <c>Flush</c>, a write
    /// that held the thread it ran on). The pump leaves the handle open, which is what the
    /// holder inherits.
    /// </remarks>
    /// <param name="document">The document.</param>
    /// <returns>The write.</returns>
    private static async Task WriteToStandardOutputAsync(string document)
    {
#pragma warning disable CA2000 // The pump disposes the stream it wraps.
        await using var output = global::BrowserAI.Protocol.StdioChannel.PumpWrites(Console.OpenStandardOutput());
#pragma warning restore CA2000
        await output.WriteAsync(Console.OutputEncoding.GetBytes(document));
        await output.FlushAsync();
    }

    /// <summary>Holds what it inherited for a while, and exits.</summary>
    /// <param name="holdSeconds">How long.</param>
    /// <returns>Zero.</returns>
    public static async Task<int> HoldAsync(string holdSeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(double.Parse(holdSeconds, CultureInfo.InvariantCulture)));
        return 0;
    }
}
