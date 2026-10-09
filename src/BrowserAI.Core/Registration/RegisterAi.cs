// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BrowserAI.Registration;

/// <summary>What one run of RegisterAI did, before anything is read out of it.</summary>
/// <param name="ExitCode">Its exit code, or <see langword="null"/> when it did not run to an exit.</param>
/// <param name="Output">What it wrote to stdout: one JSON document, when it ran.</param>
/// <param name="Error">What it wrote to stderr.</param>
/// <param name="TimedOut">Whether it ran past its budget and was stopped.</param>
/// <param name="Failure">Why it could not be started, when that is what happened.</param>
internal sealed record ToolRun(int? ExitCode, string Output, string Error, bool TimedOut, string? Failure);

/// <summary>
/// The one thing BrowserAI asks of RegisterAI: run this command line and hand back
/// what it printed.
/// </summary>
/// <remarks>
/// <b>A seam for the suite, and the only one registration has.</b> The product
/// passes <see cref="RegisterAiTool"/>; the in-process arms pass a fake that answers
/// the way the tool does, so a hook can be driven without a client, an install or a
/// configuration file. Added 2026-10-03 with the switch to RegisterAI (Q332, Q349 a).
/// </remarks>
internal interface IRegisterAi
{
    /// <summary>The executable, for a sentence a person reads.</summary>
    string Executable { get; }

    /// <summary>Runs the tool once.</summary>
    /// <param name="arguments">The command line, one element per argument.</param>
    /// <param name="budget">How long it may run.</param>
    /// <returns>What it did. Never throws for a tool that is missing, fails or hangs.</returns>
    ToolRun Run(IReadOnlyList<string> arguments, TimeSpan budget);
}

/// <summary>
/// RegisterAI.exe, which ships in the payload, started directly and never through a
/// shell.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it is.</b> <c>build/Get-RegisterAi.ps1</c> puts it at
/// <c>payload\registerai\RegisterAI.exe</c>, and the payload lands in <c>current\</c>
/// beside the executable, so the file is found from the running image's own folder:
/// the hooks run as <c>current\BrowserAI.exe</c>. <i>Corrected 2026-10-09 (previously
/// "beside both executables"): one executable since D7 a, 2026-10-08.</i>
/// </para>
/// <para>
/// <b>UTF-8 on both pipes, no window, stdin closed.</b> The tool writes one JSON
/// document in UTF-8 whatever the console code page is, and a non-ASCII profile path
/// decoded with the code page would read as somebody else's path. It never reads
/// stdin, and closing it says so to anything that would.
/// </para>
/// </remarks>
/// <param name="executable">The tool's full path.</param>
/// <param name="environment">
/// Variables every run gets on top of this process's own, or <see langword="null"/>.
/// The product passes none; an arm that points a client at a scratch home passes it
/// here, so no other arm inherits it.
/// </param>
internal sealed class RegisterAiTool(string executable, IReadOnlyDictionary<string, string>? environment = null) : IRegisterAi
{
    /// <summary>The executable's file name.</summary>
    public const string FileName = "RegisterAI.exe";

    /// <summary>The folder under the payload it lives in.</summary>
    public const string FolderName = "registerai";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc/>
    public string Executable { get; } = executable;

    /// <summary>The tool an install ships, beside the image that is running.</summary>
    /// <param name="imagePath">The running image, normally <see cref="Environment.ProcessPath"/>.</param>
    /// <returns>
    /// The tool at <c>&lt;image folder&gt;\payload\registerai\RegisterAI.exe</c>. An image
    /// with no folder gives the same path without one, which <c>Run</c> refuses as not
    /// there, so nothing is looked for in a working directory or on PATH.
    /// </returns>
    /// <remarks>
    /// <i>Corrected 2026-10-03 (previously an image with no folder fell back to
    /// <c>AppContext.BaseDirectory</c>)</i>: that directory is inside <c>current\</c>,
    /// and <c>UpdateTests.NoProductPathIsResolvedFromAppContextBaseDirectory</c> keeps it
    /// for the payload's own type. The gate found it.
    /// </remarks>
    public static RegisterAiTool Beside(string? imagePath) =>
        new(imagePath is { Length: > 0 } && Path.GetDirectoryName(imagePath) is { Length: > 0 } folder
            ? Path.Combine(folder, "payload", FolderName, FileName)
            : Path.Combine("payload", FolderName, FileName));

    /// <inheritdoc/>
    public ToolRun Run(IReadOnlyList<string> arguments, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // A path that is not fully qualified would be looked for in the working
        // directory, so it is refused the way a missing file is.
        if (!Path.IsPathFullyQualified(Executable) || !File.Exists(Executable))
        {
            return new ToolRun(null, string.Empty, string.Empty, TimedOut: false, $"'{Executable}' is not there");
        }

        return RunAsync(arguments, budget).GetAwaiter().GetResult();
    }

    private async Task<ToolRun> RunAsync(IReadOnlyList<string> arguments, TimeSpan budget)
    {
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = Path.GetDirectoryName(Executable)!,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        using var process = new Process { StartInfo = start };

        try
        {
            _ = process.Start();
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ToolRun(null, string.Empty, string.Empty, TimedOut: false, failure.Message);
        }

        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var timedOut = false;

        using (var limit = new CancellationTokenSource(budget))
        {
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                Stop(process);
            }
        }

        var (said, complained) = await CollectAsync(output, error).ConfigureAwait(false);

        return timedOut
            ? new ToolRun(null, said, complained, TimedOut: true, null)
            : new ToolRun(process.ExitCode, said, complained, TimedOut: false, null);
    }

    /// <summary>
    /// What the two pipes held. A child that kept them open would hold them for ever,
    /// so the wait is bounded.
    /// </summary>
    private static async Task<(string Output, string Error)> CollectAsync(Task<string> output, Task<string> error)
    {
        try
        {
            var both = await Task.WhenAll(output, error).WaitAsync(ProcessBounds.RegisterAiOutputDrain).ConfigureAwait(false);

            return (both[0], both[1]);
        }
        catch (TimeoutException)
        {
            return (
                output.IsCompletedSuccessfully ? await output.ConfigureAwait(false) : string.Empty,
                error.IsCompletedSuccessfully ? await error.ConfigureAwait(false) : string.Empty);
        }
    }

    /// <summary>Stops the process this runner started, and nothing else.</summary>
    private static void Stop(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception failure) when (failure is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It exited between the deadline and the kill.
        }
    }
}

/// <summary>One entry as RegisterAI reports it.</summary>
/// <param name="State">absent, ours, ours-stale, foreign, unreadable or unknown.</param>
/// <param name="Command">The command the entry names.</param>
/// <param name="ResolvesTo">The file that command resolves to.</param>
internal sealed record ToolEntry(string State, string? Command, string? ResolvesTo);

/// <summary>A condition RegisterAI tells the caller about.</summary>
/// <param name="Code">The published code.</param>
/// <param name="Text">RegisterAI's sentence.</param>
/// <param name="Command">A command a person can run about it, or null.</param>
internal sealed record ToolAdvice(string Code, string Text, string? Command);

/// <summary>What RegisterAI found and did for one client.</summary>
/// <param name="Client">The client's id: <c>claude-code</c> or <c>codex</c>.</param>
/// <param name="ClientPath">The client executable used, or null when none was found.</param>
/// <param name="Config">The file the entry lives in.</param>
/// <param name="Before">The entry before anything ran; for status, the answer.</param>
/// <param name="Action">What was done, or null for status.</param>
/// <param name="After">The entry afterwards.</param>
/// <param name="Said">What the client printed while writing.</param>
/// <param name="Advice">Conditions worth telling a person.</param>
/// <param name="Manual">The line that makes the same change by hand.</param>
/// <param name="Error">Why the result is not what was asked for, or null.</param>
internal sealed record ToolResult(
    string Client,
    string? ClientPath,
    string? Config,
    ToolEntry Before,
    string? Action,
    ToolEntry After,
    string? Said,
    IReadOnlyList<ToolAdvice> Advice,
    string? Manual,
    string? Error);

/// <summary>The document one run wrote, as far as BrowserAI reads it.</summary>
/// <param name="Verb">The verb it answers.</param>
/// <param name="ExitCode">The exit code it states.</param>
/// <param name="Error">Why the whole run failed, or null.</param>
/// <param name="Results">One per client.</param>
internal sealed record ToolDocument(string? Verb, int ExitCode, string? Error, IReadOnlyList<ToolResult> Results)
{
    /// <summary>The result for one client, or null when the document carries none.</summary>
    /// <param name="client">The client's id.</param>
    /// <returns>The result.</returns>
    public ToolResult? For(string client) =>
        Results.FirstOrDefault(result => string.Equals(result.Client, client, StringComparison.Ordinal));
}

/// <summary>
/// Reads RegisterAI's documents, and refuses one written to a schema this build was
/// not written against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Schema 1 is the contract.</b> RegisterAI publishes it with <c>describe</c> and
/// promises a new number for any key or word it renames or removes; new keys can
/// arrive within a number, so everything not read here is ignored.
/// <c>RegisterAiTests.ThePayloadsRegisterAiWritesTheSchemaThisReaderReads</c> holds the
/// shipped tool to it (Q350 a).
/// </para>
/// <para>
/// <b>Never throws.</b> A document that cannot be read is an answer, a sentence saying
/// why, and the caller turns it into a registration that did not happen.
/// </para>
/// </remarks>
internal static class ToolDocuments
{
    /// <summary>The schema number this reader was written for.</summary>
    public const int Schema = 1;

    /// <summary>The keys every result carries that this reader needs, by verb.</summary>
    public static IReadOnlyList<string> StatusResultKeys { get; } = ["client", "clientPath", "config", "state", "command", "resolvesTo", "advice", "error"];

    /// <summary>The keys every register and unregister result carries that this reader needs.</summary>
    public static IReadOnlyList<string> ChangeResultKeys { get; } = ["client", "clientPath", "config", "before", "action", "after", "said", "advice", "manual", "error"];

    /// <summary>Reads one run's output.</summary>
    /// <param name="run">The run.</param>
    /// <param name="document">The document, when there is one this build can read.</param>
    /// <param name="problem">Why there is not, as the middle of a sentence.</param>
    /// <returns>Whether a document was read.</returns>
    public static bool TryRead(ToolRun run, out ToolDocument? document, out string problem)
    {
        ArgumentNullException.ThrowIfNull(run);

        document = null;

        if (run.Failure is { } start)
        {
            problem = $"it could not be started: {start}";
            return false;
        }

        if (run.TimedOut)
        {
            problem = "it did not finish in time and was stopped";
            return false;
        }

        try
        {
            using var parsed = JsonDocument.Parse(run.Output);
            var root = parsed.RootElement;

            if (root.ValueKind is not JsonValueKind.Object
                || !root.TryGetProperty("tool", out var tool) || tool.ValueKind is not JsonValueKind.String || tool.GetString() is not "registerai")
            {
                problem = $"it exited {Describe(run.ExitCode)} and printed something that is not a RegisterAI document: {Clip(run)}";
                return false;
            }

            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind is not JsonValueKind.Number || schema.GetInt32() is not Schema)
            {
                problem = $"it writes schema {(root.TryGetProperty("schema", out var written) ? written.ToString() : "<none>")}, and this BrowserAI reads schema {Schema}";
                return false;
            }

            var exitCode = root.GetProperty("exitCode").GetInt32();

            if (exitCode is 2)
            {
                problem = $"it did not understand the command line BrowserAI gave it: {Text(root, "error") ?? "<no reason given>"}";
                return false;
            }

            document = new ToolDocument(
                Text(root, "verb"),
                exitCode,
                Text(root, "error"),
                [.. root.GetProperty("results").EnumerateArray().Select(Result)]);
            problem = string.Empty;

            return true;
        }
        catch (Exception failure) when (failure is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            problem = $"it exited {Describe(run.ExitCode)} and printed no document BrowserAI could read ({failure.Message}): {Clip(run)}";
            return false;
        }
    }

    private static ToolResult Result(JsonElement result)
    {
        // A status result carries the entry at its own level; a register or
        // unregister result carries it twice, before and after.
        var before = result.TryGetProperty("before", out var was) ? Entry(was) : Entry(result);
        var after = result.TryGetProperty("after", out var now) ? Entry(now) : before;

        return new ToolResult(
            Text(result, "client") ?? string.Empty,
            Text(result, "clientPath"),
            Text(result, "config"),
            before,
            Text(result, "action"),
            after,
            Text(result, "said"),
            result.TryGetProperty("advice", out var advice) && advice.ValueKind is JsonValueKind.Array
                ? [.. advice.EnumerateArray().Select(item => new ToolAdvice(Text(item, "code") ?? string.Empty, Text(item, "text") ?? string.Empty, Text(item, "command")))]
                : [],
            Text(result, "manual"),
            Text(result, "error"));
    }

    private static ToolEntry Entry(JsonElement entry) =>
        new(Text(entry, "state") ?? "unknown", Text(entry, "command"), Text(entry, "resolvesTo"));

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static string Describe(int? exitCode) =>
        exitCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : "<none>";

    /// <summary>What a run printed, both streams, cut to a length a sentence can carry.</summary>
    private static string Clip(ToolRun run)
    {
        var said = string.Join(" | ", new[] { run.Output.Trim(), run.Error.Trim() }.Where(text => text.Length > 0));

        return said.Length is 0 ? "<nothing>" : said.Length <= 300 ? said : said[..300] + " (cut)";
    }
}
