// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>Runs <c>BrowserAI.TestProbe.exe</c> and reports what it did.</summary>
internal static class ProbeProcess
{
    private static readonly string ExecutablePath =
        Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary>What one probe run produced.</summary>
    /// <param name="ExitCode">The cached exit code.</param>
    /// <param name="StandardOutput">Everything the probe wrote to stdout.</param>
    /// <param name="StandardError">Everything the probe wrote to stderr.</param>
    internal readonly record struct Result(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>Runs the probe to completion.</summary>
    public static Task<Result> RunAsync(params string[] arguments) =>
        RunInAsync(AppContext.BaseDirectory, arguments);

    /// <summary>
    /// Runs the probe to completion from a working directory of the caller's
    /// choosing.
    /// </summary>
    /// <remarks>
    /// The overload exists for one question the test host cannot answer about
    /// itself: what a <b>relative</b> path canonicalises to. That depends on the
    /// process's current directory, and mutating the host's would be a global
    /// change aimed at a local question, in a runner that executes tests in
    /// parallel.
    /// </remarks>
    /// <param name="workingDirectory">The probe's current directory.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>What the run produced.</returns>
    public static async Task<Result> RunInAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Explicit, never left unset. .NET passes null to CreateProcess for
            // an unset WorkingDirectory and the child silently inherits the
            // test host's -- the same rule the product's own spawns obey.
            WorkingDirectory = workingDirectory,

            // Console stdio defaults to CP437 in both directions, so a harness
            // that leaves this alone fails the product for the harness's defect.
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{ExecutablePath}'.");

        // This one is started OUTSIDE a job object -- it is awaited to exit
        // rather than contained -- so the spawn record is the only thing that
        // can name it if this run is killed while it is running.
        SpawnRecord.Add(process.Id);

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        // WaitForExitAsync, never WaitForExit(int): only the former drains the
        // async readers.
        await process.WaitForExitAsync().ConfigureAwait(false);

        // Cached immediately as an int. Process.ExitCode throws after Dispose().
        var exitCode = process.ExitCode;

        return new Result(exitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    /// <summary>Reads every process-log record written under a scratch root.</summary>
    public static string ReadProcessLog(string root)
    {
        var directory = Path.Combine(root, "logs");

        if (!Directory.Exists(directory))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(directory, "browserai-*.log").Order(StringComparer.Ordinal))
        {
            // FileShare.ReadWrite because a writer may still hold the file, and
            // a reader must never be locked out of the log it came to read.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _ = builder.Append(reader.ReadToEnd());
        }

        return builder.ToString();
    }
}
