// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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
        // Through the product's launcher since 2026-10-11, decision 54, whose pipes
        // are read without parking a thread, with the working directory explicit and
        // the output read as UTF-8, as before: console stdio defaults to CP437 in both
        // directions, so a harness that left this alone would fail the product for the
        // harness's defect. The hang detector is the suite's own: until that day the
        // wait had none.
        var run = await ChildRunner.RunAsync(
            ExecutablePath,
            arguments,
            workingDirectory,
            TestDefaults.ProcessHang,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return run.ExitCode is { } exitCode
            ? new Result(exitCode, run.StandardOutput, run.StandardError)
            : throw new TimeoutException(
                $"The probe '{string.Join(' ', arguments)}' did not exit within {TestDefaults.ProcessHang} and was terminated. It said: {run.StandardOutput}{run.StandardError}");
    }

    /// <summary>Reads every process-log record written under a scratch root.</summary>
    public static async Task<string> ReadProcessLogAsync(string root)
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
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.Asynchronous);
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _ = builder.Append(await reader.ReadToEndAsync());
        }

        return builder.ToString();
    }
}
