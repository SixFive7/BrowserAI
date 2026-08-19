// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.TestProbe;

/// <summary>
/// Stands in for an MCP client that started BrowserAI <b>through a wrapper</b>,
/// so that killing the client does not close BrowserAI's stdin.
/// </summary>
/// <remarks>
/// <para>
/// <b>This shape is the only way to test the client-liveness watcher at all, and
/// the reason is a Windows fact rather than a preference.</b> In the ordinary
/// case the process that starts BrowserAI is also the one holding the write end
/// of its stdin pipe, so killing it closes that handle and stdin reaches EOF —
/// and EOF alone would explain the teardown. To observe the <i>watcher</i>, the
/// pipe has to outlive the parent. A Windows pipe signals EOF when its
/// <b>last</b> write handle closes, so this probe duplicates that handle into the
/// test process before it dies: the parent is gone, the pipe is open, and the
/// only thing left that can end BrowserAI is the handle it holds on this
/// process.
/// </para>
/// <para>
/// <b>The job handle is duplicated for the same reason and a different
/// purpose.</b> BrowserAI is launched into a <c>KILL_ON_JOB_CLOSE</c> job, and if
/// this probe were its only handle-holder, killing the probe would kill
/// BrowserAI through the kernel and the test would prove nothing. The duplicate
/// keeps the job alive across the kill — and hands the test the containment net,
/// so an assertion that throws still takes the browser down.
/// </para>
/// <para>
/// It drives a real session and a real navigation first, because "tears the
/// session down" is only a claim worth making about a session that had a browser
/// in it.
/// </para>
/// </remarks>
internal static partial class ClientProbe
{
    private const uint DuplicateSameAccess = 0x00000002;

    /// <summary>
    /// Starts BrowserAI, opens a session, navigates, reports, and then waits to
    /// be killed.
    /// </summary>
    /// <param name="browserAi">The published binary.</param>
    /// <param name="workingDirectory">Where BrowserAI runs, and where its session goes.</param>
    /// <param name="testProcessId">The test host, which the handles are duplicated into.</param>
    /// <param name="reportPath">Where to write the report, once everything is up.</param>
    /// <returns>Nothing, ever: it blocks until this process is terminated.</returns>
    public static int Start(string browserAi, string workingDirectory, int testProcessId, string reportPath)
    {
        // CA2000 is disabled for this statement and the two stream wrappers
        // below, and nothing else. Nothing here is ever disposed on purpose:
        // this process is terminated from outside, which is the event under
        // test, and a probe that unwound cleanly would be closing the very
        // handles the test needs to survive it. The job handle it duplicates
        // into the test process is what reaps everything afterwards.
#pragma warning disable CA2000
        var job = JobObject.CreateKillOnClose();
#pragma warning restore CA2000

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                environment[name] = value;
            }
        }

        var process = JobLauncher.Start(job, browserAi, [], workingDirectory, environment);

        // Drained on a background thread, so a BrowserAI writing diagnostics
        // cannot fill a pipe nobody is reading and block.
        _ = Task.Run(() => Drain(process.StandardError));

#pragma warning disable CA2000 // See above: this process is killed rather than unwound, and closing these would close BrowserAI's stdin.
        var writer = new StreamWriter(process.StandardInput, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
        var reader = new StreamReader(process.StandardOutput, new UTF8Encoding(false));
#pragma warning restore CA2000

        var session = Path.Combine(workingDirectory, "watcher-session");

        _ = Exchange(writer, reader, 1, "initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-11-25",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "client-liveness probe", ["version"] = "1" },
        });

        writer.WriteLine(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }.ToJsonString());

        _ = Exchange(writer, reader, 2, "tools/call", new JsonObject
        {
            ["name"] = "browserai_init",
            ["arguments"] = new JsonObject
            {
                ["directory"] = session,
                ["purpose"] = "the session the client-liveness watcher tears down",
                ["mode"] = "headless",
            },
        });

        var navigate = Exchange(writer, reader, 3, "tools/call", new JsonObject
        {
            ["name"] = "browser_navigate",
            ["arguments"] = new JsonObject
            {
                ["url"] = "data:text/html,<h1>ok</h1>",
                ["session"] = session,
            },
        });

        var navigated = navigate["result"] is not null && (bool?)navigate["result"]!["isError"] is not true;

        // Read while the browser is up. Everything after this is the kill.
        var members = job.ProcessIds();

        var report = new JsonObject
        {
            ["browserAiPid"] = process.Id,

            // BrowserAI's parent, and therefore the pid its client-liveness
            // watcher must have opened a handle on. Reported rather than assumed:
            // a watcher pointed at the wrong process fires at the wrong moment
            // and every other signal looks identical.
            ["wrapperPid"] = Environment.ProcessId,
            ["navigated"] = navigated,
            ["session"] = session,
            ["jobPids"] = new JsonArray([.. members.Select(pid => (JsonNode)pid)]),

            // Valid in the TEST's handle table, not in this one.
            ["standardInputHandle"] = Duplicate(StandardInputHandleOf(process), testProcessId),
            ["jobHandle"] = Duplicate(job.Handle.DangerousGetHandle(), testProcessId),
        };

        Write(reportPath, report);

        // Killed from outside, which is the event under test. Nothing below this
        // line runs, deliberately: a probe that shut anything down cleanly would
        // be the thing being observed rather than the watcher.
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    /// <summary>
    /// Writes the report so it appears complete or not at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Added 2026-08-19, after a full-suite run failed on it.</b> This
    /// probe wrote the report in place with <c>File.WriteAllText</c>, which
    /// creates the file, holds it <c>FileAccess.Write</c> while it writes, and
    /// leaves it existing-but-incomplete for as long as that takes. The host was
    /// waiting on <c>File.Exists</c>, so it read at the first instant the name
    /// appeared and was refused with <i>"the process cannot access the file …
    /// because it is being used by another process"</i> — one occurrence in three
    /// consecutive full runs, and the failure named
    /// <c>KillingTheClientTearsTheSessionDownWithoutWaitingForEof</c> rather than
    /// the harness.
    /// </para>
    /// <para>
    /// <b>The sharing violation was the lucky half.</b> A reader that arrived one
    /// instant later would have got a partly-written JSON document, and a
    /// truncated report is a test failing on an assertion about the product. The
    /// two other probes in this project already write temp-and-rename for exactly
    /// this reason and say so; this one did not, and
    /// <c>BrowserAI.Tests.Harness.ProbeReport</c>'s own summary — <i>"the probe
    /// renames its report into place"</i> — was therefore false of one caller.
    /// </para>
    /// <para>
    /// <b>The rename is retried inside a bound.</b> A file this process has just
    /// closed is briefly held by something outside this repository, and
    /// <c>MOVEFILE_REPLACE_EXISTING</c> wants DELETE on the destination, so an
    /// unretried rename fails <c>ACCESS_DENIED</c> and kills the probe — the host
    /// then reports <i>the probe never wrote its report</i>, which is true and
    /// names the wrong cause.
    /// </para>
    /// </remarks>
    /// <param name="path">Where the host is looking.</param>
    /// <param name="report">What to write.</param>
    private static void Write(string path, JsonObject report)
    {
        var temp = $"{path}.writing";

        File.WriteAllText(temp, report.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception failure) when (failure is UnauthorizedAccessException or IOException)
            {
                if (waited.Elapsed > PublishPatience)
                {
                    throw new InvalidOperationException(
                        $"'{path}' could not be replaced by a rename within {PublishPatience}. Something outside this repository is holding it.",
                        failure);
                }

                Thread.Sleep(10);
            }
        }
    }

    /// <summary>How long the rename above may be refused before it is a failure.</summary>
    private static readonly TimeSpan PublishPatience = TimeSpan.FromSeconds(10);

    private static nint StandardInputHandleOf(LaunchedProcess process) =>
        process.StandardInput is FileStream file
            ? file.SafeFileHandle.DangerousGetHandle()
            : throw new InvalidOperationException(
                $"The launched process's stdin is a {process.StandardInput.GetType().Name} rather than a FileStream, so its handle cannot be duplicated into the test.");

    /// <summary>Duplicates one handle into another process, and reports its value there.</summary>
    private static long Duplicate(nint handle, int intoProcessId)
    {
        const uint ProcessDupHandle = 0x00000040;

        using var target = OpenProcess(ProcessDupHandle, bInheritHandle: false, (uint)intoProcessId);

        if (target.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not open process {intoProcessId} to duplicate a handle into it.");
        }

        if (!DuplicateHandle(
                GetCurrentProcess(),
                handle,
                target.DangerousGetHandle(),
                out var duplicate,
                dwDesiredAccess: 0,
                bInheritHandle: false,
                DuplicateSameAccess))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not duplicate a handle into process {intoProcessId}.");
        }

        return duplicate;
    }

    private static void Drain(Stream stream)
    {
        var buffer = new byte[4096];

        try
        {
            while (stream.Read(buffer) > 0)
            {
                // Discarded deliberately.
            }
        }
#pragma warning disable CA1031 // The pipe closing under a read in flight is how this loop normally ends.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>One request and the matching answer, correlated by id.</summary>
    private static JsonObject Exchange(StreamWriter writer, StreamReader reader, int id, string method, JsonNode parameters)
    {
        writer.WriteLine(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
        }.ToJsonString());

        while (reader.ReadLine() is { } line)
        {
            if (line.Length is 0)
            {
                continue;
            }

            var envelope = JsonNode.Parse(line)?.AsObject();

            if (envelope is null || (int?)envelope["id"] != id)
            {
                continue;
            }

            return envelope;
        }

        throw new InvalidOperationException(
            $"BrowserAI closed its stdout before answering '{method}' (id {id.ToString(CultureInfo.InvariantCulture)}).");
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateHandle(
        nint hSourceProcessHandle,
        nint hSourceHandle,
        nint hTargetProcessHandle,
        out nint lpTargetHandle,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwOptions);
}
