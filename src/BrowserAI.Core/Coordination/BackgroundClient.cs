// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BrowserAI.Interop;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Coordination;

/// <summary>How one question to the background came out.</summary>
internal enum BackgroundAnswerOutcome
{
    /// <summary>The background answered.</summary>
    Answered,

    /// <summary>The background answered with a refusal, whose sentence is the answer's.</summary>
    Refused,

    /// <summary>No background serves the pipe.</summary>
    NoBackground,

    /// <summary>A background took the connection and did not answer within the bound: hung.</summary>
    NoAnswer,
}

/// <summary>What one question to the background came back with.</summary>
/// <param name="Outcome">How it came out.</param>
/// <param name="Result">The answer's <c>result</c>, as JSON text, when it answered.</param>
/// <param name="Sentence">The refusal's sentence, or why there was no answer.</param>
/// <param name="ServerProcessId">The pid serving the pipe, read off the connection, when there was one.</param>
internal sealed record BackgroundAnswer(BackgroundAnswerOutcome Outcome, string? Result, string? Sentence, int? ServerProcessId)
{
    /// <summary>One string member of the result, or <see langword="null"/>.</summary>
    /// <param name="name">The member's name.</param>
    /// <returns>Its value.</returns>
    public string? ResultMember(string name)
    {
        if (Result is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(Result);

        return document.RootElement.ValueKind is JsonValueKind.Object
            && document.RootElement.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.String
                ? value.GetString()
                : null;
    }
}

/// <summary>
/// A question to the background from a process that is not a relay: a person's
/// <c>show</c>, the uninstall hook's <c>stop</c>, the suite's.
/// </summary>
/// <remarks>
/// <para>
/// <b>One request, one answer, one connection</b>, in the JSON-RPC framing of the pipe
/// (<see cref="BackgroundPipe"/>). The wait is bounded by the caller: a person's start
/// waits <c>HandOutBound</c> and judges a background that did not answer within it hung
/// (RESOLUTIONS 10).
/// </para>
/// <para>
/// ⚠️ <b>Asynchronous end to end since 2026-10-11</b>, the maintainer's decision 54, in his
/// words verbatim: <i>"Never block a worker anywhere."</i> <i>Corrected 2026-10-11
/// (previously <c>Ask</c>, which waited on its write and on every byte of the answer with
/// <c>GetAwaiter().GetResult()</c> on an overlapped pipe, so each read held its own thread
/// and its completion needed a second; and <c>WaitNamedPipeW</c> while every instance was
/// busy).</i> The answer is read in buffers, since nothing follows it on the connection,
/// and a busy pipe is asked again after <see cref="ProcessBounds.PipeBusyRetry"/>.
/// <c>StarvedPoolTests.ThePipeClientGetsItsAnswerOnAPoolOfOneWorker</c> runs this on a
/// pool of one worker.
/// </para>
/// </remarks>
internal static class BackgroundClient
{
    /// <summary>How much of the answer one read takes: the size the background's pipe buffers a reply in.</summary>
    private const int ReadBufferBytes = NamedPipes.OutBufferBytes;

    /// <summary>Asks the background one thing.</summary>
    /// <param name="pipeName">The background's pipe.</param>
    /// <param name="method">The method, <see cref="BackgroundPipe.Show"/> or <see cref="BackgroundPipe.Stop"/>.</param>
    /// <param name="parameters">Its parameters, as a JSON object's text, or <see langword="null"/>.</param>
    /// <param name="bound">How long to wait for the answer.</param>
    /// <param name="cancellationToken">Ends the question early, as a bound that ran out does.</param>
    /// <returns>What came back.</returns>
    public static async Task<BackgroundAnswer> AskAsync(string pipeName, string method, string? parameters, TimeSpan bound, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var deadline = Environment.TickCount64 + (long)bound.TotalMilliseconds;

        while (true)
        {
            var handle = NamedPipes.OpenClient(pipeName, out var error);

            try
            {
                if (!handle.IsInvalid)
                {
                    var owned = handle;

                    // ConverseAsync owns it from here, and closes it on every path.
                    handle = null;

                    return await ConverseAsync(owned, method, parameters, bound, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                handle?.Dispose();
            }

            var left = deadline - Environment.TickCount64;

            if (error is not NamedPipes.ErrorPipeBusy || left <= 0)
            {
                return new BackgroundAnswer(BackgroundAnswerOutcome.NoBackground, null, new Win32Exception(error).Message, null);
            }

            // Every instance is busy, which lasts the instant between a connect and the
            // background's next instance. A name that has gone fails the next open with
            // something other than busy.
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(left, (long)ProcessBounds.PipeBusyRetry.TotalMilliseconds)), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new BackgroundAnswer(BackgroundAnswerOutcome.NoBackground, null, new Win32Exception(error).Message, null);
            }
        }
    }

    private static async Task<BackgroundAnswer> ConverseAsync(SafeFileHandle handle, string method, string? parameters, TimeSpan bound, CancellationToken cancellationToken)
    {
        var server = NamedPipes.ServerProcessIdOf(handle);
        FileStream stream;

        try
        {
            stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
        }
        catch
        {
            // The stream never took the handle, so nothing else will close it.
            handle.Dispose();
            throw;
        }

        await using (stream.ConfigureAwait(false))
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(bound);

            try
            {
                var request = $$"""{"jsonrpc":"2.0","id":"{{BackgroundPipe.IdPrefix}}start-1","method":"{{method}}","params":{{parameters ?? "{}"}}}""" + "\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(request), timeout.Token).ConfigureAwait(false);
                await stream.FlushAsync(timeout.Token).ConfigureAwait(false);

                var line = await ReadLineAsync(stream, timeout.Token).ConfigureAwait(false);

                if (line is null)
                {
                    // The texts polish, 2026-10-10, pages #218 to #220 (previously "The
                    // background closed the connection without an answer.", "The background
                    // answered with neither a result nor an error." and "The background did
                    // not answer within N s."): every line that carries one names the
                    // background already.
                    return new BackgroundAnswer(BackgroundAnswerOutcome.NoAnswer, null, "The connection closed without an answer.", server);
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (root.TryGetProperty("result", out var result))
                {
                    return new BackgroundAnswer(BackgroundAnswerOutcome.Answered, result.GetRawText(), null, server);
                }

                var sentence = root.TryGetProperty("error", out var refusal) && refusal.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : "The answer carried neither a result nor an error.";

                return new BackgroundAnswer(BackgroundAnswerOutcome.Refused, null, sentence, server);
            }
            catch (OperationCanceledException)
            {
                return new BackgroundAnswer(BackgroundAnswerOutcome.NoAnswer, null, string.Create(CultureInfo.InvariantCulture, $"No answer came within {bound.TotalSeconds:0} s."), server);
            }
            catch (Exception failure) when (failure is IOException or JsonException)
            {
                return new BackgroundAnswer(BackgroundAnswerOutcome.NoAnswer, null, failure.Message, server);
            }
        }
    }

    /// <summary>The answer's one line, read in buffers: nothing follows it on the connection.</summary>
    /// <param name="stream">The connection.</param>
    /// <param name="cancellationToken">The bound.</param>
    /// <returns>The line without its newline, or <see langword="null"/> when the connection closed with nothing.</returns>
    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = new ArrayBufferWriter<byte>();
        var buffer = new byte[ReadBufferBytes];

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (read is 0)
            {
                return line.WrittenCount is 0 ? null : Encoding.UTF8.GetString(line.WrittenSpan);
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);

            line.Write(buffer.AsSpan(0, newline < 0 ? read : newline));

            if (newline >= 0)
            {
                return Encoding.UTF8.GetString(line.WrittenSpan);
            }
        }
    }
}
