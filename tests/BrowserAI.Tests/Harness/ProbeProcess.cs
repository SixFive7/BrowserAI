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
    public static async Task<Result> RunAsync(params string[] arguments)
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
            WorkingDirectory = AppContext.BaseDirectory,

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
