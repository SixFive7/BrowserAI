// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Interop;

namespace BrowserAI.TestProbe;

/// <summary>
/// Stands in for a relay and the client above it: a middle process that starts a leaf
/// and waits, and a leaf that reads its parent and its parent's parent the way the relay
/// does.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape a VS Code tab has</b>, measured 2026-10-08: the window's extension host
/// starts the tab's Claude Code, which starts the relay. Here the test host stands where
/// the extension host does, the middle where Claude Code does, and the leaf where the
/// relay does, so every process the leaf reads is alive while it reads and the test
/// knows each one's pid and creation time to hold the reading against.
/// </para>
/// <para>
/// <b>Nothing here opens a window</b>: the leaf is started with no window, as every
/// process this tree starts is.
/// </para>
/// </remarks>
internal static class ParentChainProbe
{
    /// <summary>The middle: writes its own pid and creation time, starts the leaf, and waits for it.</summary>
    /// <param name="reportPath">Where the leaf writes its reading; the middle writes its own beside it.</param>
    /// <returns>The leaf's exit code.</returns>
    public static int Middle(string reportPath)
    {
        Write(reportPath + ".middle", new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["created"] = ProcessLiveness.CreationTimeOfThisProcess().ToString(CultureInfo.InvariantCulture),
        });

        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        start.ArgumentList.Add("parent-chain");
        start.ArgumentList.Add(reportPath);

        using var leaf = Process.Start(start) ?? throw new InvalidOperationException("The leaf did not start.");

        leaf.WaitForExit();
        return leaf.ExitCode;
    }

    /// <summary>The leaf: reads its parent as the relay does, and writes what it read.</summary>
    /// <param name="reportPath">Where to write it.</param>
    /// <returns>0.</returns>
    public static int Leaf(string reportPath)
    {
        var reading = ProcessLiveness.ReadParent();

        Write(reportPath, new JsonObject
        {
            ["read"] = reading is not null,
            ["pid"] = reading?.ProcessId,
            ["created"] = reading?.CreatedFileTime.ToString(CultureInfo.InvariantCulture),
            ["commandLine"] = reading?.CommandLine,
            ["parentPid"] = reading?.Parent?.ProcessId,
            ["parentCreated"] = reading?.Parent?.CreatedFileTime.ToString(CultureInfo.InvariantCulture),
        });

        return 0;
    }

    /// <summary>Writes a report whole or not at all: to a name beside it, then moved into place.</summary>
    private static void Write(string path, JsonObject report)
    {
        var partial = path + ".partial";

        File.WriteAllText(partial, report.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(partial, path, overwrite: true);
    }
}
