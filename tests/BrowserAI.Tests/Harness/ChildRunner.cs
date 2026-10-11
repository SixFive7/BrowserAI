// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections;
using System.Text;
using BrowserAI.Interop;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Runs a child to its end through the product's launcher, and hands back how it ended
/// and what it wrote.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-11 with the maintainer's decision 54</b>, in his words verbatim:
/// <i>"Never block a worker anywhere."</i> A <c>System.Diagnostics.Process</c>'s redirected
/// streams are synchronous <c>FileStream</c>s (kb, windows/processes.md, measured that day),
/// so every read of one is a pool thread parked for as long as the child is quiet, and a
/// suite that ran its tools through them held a worker per stream per child. The product's
/// launcher, <c>Interop/JobLauncher</c>, hands its child the client ends of named pipes whose
/// server ends are overlapped, so these reads park no thread.
/// </para>
/// <para>
/// <b>Outside any job, as <c>Process.Start</c> started it</b>: a child a test runs to its
/// end is awaited and not contained, and some leave a process behind on purpose, an
/// installer the app it installed. The spawn record names it, as it named the ones before.
/// </para>
/// <para>
/// <b>Its standard input is closed at once</b> unless the caller hands it text: the launcher
/// gives every child a pipe where <c>Process.Start</c> with no redirection gave it the test
/// host's own input, and a child that read an open pipe would wait for the test.
/// </para>
/// <para>
/// <b>Its output is read in the console's own encoding</b> unless the caller names one,
/// which is what <c>Process</c> reads it in when <c>StandardOutputEncoding</c> is unset.
/// </para>
/// </remarks>
internal static class ChildRunner
{
    /// <summary>How a child ended and what it wrote.</summary>
    /// <param name="ExitCode">Its exit code, or <see langword="null"/> when it did not end within the patience.</param>
    /// <param name="StandardOutput">Everything it wrote to standard output.</param>
    /// <param name="StandardError">Everything it wrote to standard error.</param>
    internal sealed record Result(int? ExitCode, string StandardOutput, string StandardError)
    {
        /// <summary>Whether it ended within the patience.</summary>
        public bool Exited => ExitCode is not null;
    }

    /// <summary>Runs a child to its end.</summary>
    /// <param name="command">The executable: a full path, or a name looked up on <c>PATH</c> with <c>.exe</c>.</param>
    /// <param name="arguments">Its arguments, each passed whole.</param>
    /// <param name="workingDirectory">Its working directory, never inherited.</param>
    /// <param name="patience">A hang detector: past it the child is terminated and its result carries no exit code.</param>
    /// <param name="changes">Variables to set over this process's own, a <see langword="null"/> value removing one; or <see langword="null"/>.</param>
    /// <param name="standardInput">What to write to its standard input before closing it, or <see langword="null"/> for nothing.</param>
    /// <param name="encoding">What its output is read in, or <see langword="null"/> for the console's.</param>
    /// <returns>How it ended and what it wrote.</returns>
    public static async Task<Result> RunAsync(
        string command,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan patience,
        IReadOnlyDictionary<string, string?>? changes = null,
        string? standardInput = null,
        Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        using var process = JobLauncher.StartOutsideAnyJob(Resolve(command), [.. arguments], workingDirectory, EnvironmentWith(changes));

        // Started outside a job object, so the spawn record is what names it if this
        // run is killed while it is running.
        await SpawnRecord.AddAsync(process.Id);

        var reading = encoding ?? Console.OutputEncoding;
        var said = new StringBuilder();
        var complained = new StringBuilder();
        var output = ReadAllAsync(process.StandardOutput, reading, said);
        var error = ReadAllAsync(process.StandardError, reading, complained);

        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(new UTF8Encoding(false).GetBytes(standardInput));
                await process.StandardInput.FlushAsync();
            }
        }
        catch (IOException)
        {
            // A child that ended without reading its input: its exit below says how.
        }
        finally
        {
            await process.StandardInput.DisposeAsync();
        }

        var exited = await process.WaitForExitAsync(patience);

        if (!exited)
        {
            _ = process.TryTerminate();
            _ = await process.WaitForExitAsync(patience);
        }

        // The readers end when the last writer of each pipe has gone, which a process
        // the child left behind can postpone; what was read by the patience is what is
        // handed back.
        var standardOutput = await Bounded(output, said, patience);
        var standardError = await Bounded(error, complained, patience);

        return new Result(exited ? process.TryReadExitCode() : null, standardOutput, standardError);
    }

    /// <summary>A command as the launcher needs it: a full path.</summary>
    /// <param name="command">A full path, or a name on <c>PATH</c>.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="FileNotFoundException">No directory on <c>PATH</c> holds it.</exception>
    public static string Resolve(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (Path.IsPathFullyQualified(command))
        {
            return command;
        }

        var name = Path.HasExtension(command) ? command : command + ".exe";

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), name);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"'{command}' is on no directory of PATH.", name);
    }

    /// <summary>This process's environment with some variables changed.</summary>
    /// <param name="changes">The changes, a <see langword="null"/> value removing one; or <see langword="null"/>.</param>
    /// <returns>The block.</returns>
    public static Dictionary<string, string> EnvironmentWith(IReadOnlyDictionary<string, string?>? changes)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                environment[name] = value;
            }
        }

        foreach (var (name, value) in changes ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                _ = environment.Remove(name);
            }
            else
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    /// <summary>Reads a stream to its end as text.</summary>
    /// <param name="stream">One of the child's streams.</param>
    /// <param name="encoding">What it is read in.</param>
    /// <param name="into">Where the text goes, as it arrives.</param>
    /// <returns>The read.</returns>
    private static async Task ReadAllAsync(Stream stream, Encoding encoding, StringBuilder into)
    {
        try
        {
            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            var buffer = new char[4096];
            int read;

            while ((read = await reader.ReadAsync(buffer)) > 0)
            {
                lock (into)
                {
                    _ = into.Append(buffer, 0, read);
                }
            }
        }
        catch (IOException)
        {
            // A pipe broken under the read is an end like any other.
        }
    }

    /// <summary>A read's text once it ends, or what it holds when the patience runs out.</summary>
    /// <param name="reading">The read.</param>
    /// <param name="text">What it has read.</param>
    /// <param name="patience">The hang detector.</param>
    /// <returns>The text.</returns>
    private static async Task<string> Bounded(Task reading, StringBuilder text, TimeSpan patience)
    {
        var ended = true;

        try
        {
            await reading.WaitAsync(patience);
        }
        catch (TimeoutException)
        {
            ended = false;
        }

        lock (text)
        {
            return ended
                ? text.ToString()
                : text + $"<the stream did not end within {patience}: something the child started still holds it>";
        }
    }
}
