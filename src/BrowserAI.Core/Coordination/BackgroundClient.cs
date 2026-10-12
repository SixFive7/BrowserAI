// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BrowserAI.Interop;

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

        var (conversation, failure) = await BackgroundConversation.OpenAsync(pipeName, bound, cancellationToken).ConfigureAwait(false);

        if (conversation is null)
        {
            return failure!;
        }

        await using (conversation.ConfigureAwait(false))
        {
            return await conversation.AskAsync(method, parameters, bound, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// One connection to the background that may carry more than one question, each answered
/// before the next is asked.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-12 for the maintainer's 75 a</b>, in his words verbatim of 2026-10-11:
/// <i>"75 a"</i>. A folder link that opens Windows' folder picker keeps its connection open
/// while the picker is: it asks the background for the picker's sentence, shows the picker
/// itself, and sends back what was chosen on the same connection, so a link start that dies
/// with its picker open leaves nothing waiting in the background.
/// </para>
/// <para>
/// <b>What a single question was, <see cref="BackgroundClient.AskAsync"/> is one of these</b>,
/// opened, asked and closed, so the two read the background the same way.
/// </para>
/// </remarks>
internal sealed class BackgroundConversation : IAsyncDisposable
{
    /// <summary>How much of an answer one read takes: the size the background's pipe buffers a reply in.</summary>
    private const int ReadBufferBytes = NamedPipes.OutBufferBytes;

    private readonly FileStream _stream;
    private readonly ArrayBufferWriter<byte> _carried = new();
    private int _asked;

    private BackgroundConversation(FileStream stream, int? serverProcessId)
    {
        _stream = stream;
        ServerProcessId = serverProcessId;
    }

    /// <summary>The pid serving the pipe, read off the connection, when it could be.</summary>
    public int? ServerProcessId { get; }

    /// <summary>Connects to the background, asking again while every instance of its pipe is busy.</summary>
    /// <param name="pipeName">The background's pipe.</param>
    /// <param name="bound">How long a busy pipe is asked again.</param>
    /// <param name="cancellationToken">Ends the wait early.</param>
    /// <returns>The conversation, or why there is none: <see cref="BackgroundAnswerOutcome.NoBackground"/>.</returns>
    public static async Task<(BackgroundConversation? Conversation, BackgroundAnswer? Failure)> OpenAsync(string pipeName, TimeSpan bound, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        var deadline = Environment.TickCount64 + (long)bound.TotalMilliseconds;

        while (true)
        {
            var handle = NamedPipes.OpenClient(pipeName, out var error);

            try
            {
                if (!handle.IsInvalid)
                {
                    var server = NamedPipes.ServerProcessIdOf(handle);

                    // The stream owns the handle from here, and closes it on every path.
                    var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);

                    handle = null;
                    return (new BackgroundConversation(stream, server), null);
                }
            }
            finally
            {
                handle?.Dispose();
            }

            var left = deadline - Environment.TickCount64;

            if (error is not NamedPipes.ErrorPipeBusy || left <= 0)
            {
                return (null, new BackgroundAnswer(BackgroundAnswerOutcome.NoBackground, null, new Win32Exception(error).Message, null));
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
                return (null, new BackgroundAnswer(BackgroundAnswerOutcome.NoBackground, null, new Win32Exception(error).Message, null));
            }
        }
    }

    /// <summary>Asks one question on this connection and reads its answer.</summary>
    /// <param name="method">The method.</param>
    /// <param name="parameters">Its parameters, as a JSON object's text, or <see langword="null"/>.</param>
    /// <param name="bound">How long to wait for the answer.</param>
    /// <param name="cancellationToken">Ends the question early, as a bound that ran out does.</param>
    /// <returns>What came back.</returns>
    public async Task<BackgroundAnswer> AskAsync(string method, string? parameters, TimeSpan bound, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var number = Interlocked.Increment(ref _asked);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(bound);

        try
        {
            var request = $$"""{"jsonrpc":"2.0","id":"{{BackgroundPipe.IdPrefix}}start-{{number.ToString(CultureInfo.InvariantCulture)}}","method":"{{method}}","params":{{parameters ?? "{}"}}}""" + "\n";
            await _stream.WriteAsync(Encoding.UTF8.GetBytes(request), timeout.Token).ConfigureAwait(false);
            await _stream.FlushAsync(timeout.Token).ConfigureAwait(false);

            var line = await ReadLineAsync(timeout.Token).ConfigureAwait(false);

            if (line is null)
            {
                // The texts polish, 2026-10-10, pages #218 to #220 (previously "The
                // background closed the connection without an answer.", "The background
                // answered with neither a result nor an error." and "The background did
                // not answer within N s."): every line that carries one names the
                // background already.
                return new BackgroundAnswer(BackgroundAnswerOutcome.NoAnswer, null, "The connection closed without an answer.", ServerProcessId);
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.TryGetProperty("result", out var result))
            {
                return new BackgroundAnswer(BackgroundAnswerOutcome.Answered, result.GetRawText(), null, ServerProcessId);
            }

            var sentence = root.TryGetProperty("error", out var refusal) && refusal.TryGetProperty("message", out var message)
                ? message.GetString()
                : "The answer carried neither a result nor an error.";

            return new BackgroundAnswer(BackgroundAnswerOutcome.Refused, null, sentence, ServerProcessId);
        }
        catch (OperationCanceledException)
        {
            return new BackgroundAnswer(BackgroundAnswerOutcome.NoAnswer, null, string.Create(CultureInfo.InvariantCulture, $"No answer came within {bound.TotalSeconds:0} s."), ServerProcessId);
        }
        catch (Exception failure) when (failure is IOException or JsonException)
        {
            return new BackgroundAnswer(BackgroundAnswerOutcome.NoAnswer, null, failure.Message, ServerProcessId);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    /// <summary>
    /// The answer's one line, read in buffers, and what was read past it kept for the next
    /// answer: the background answers one question at a time, so nothing ever is.
    /// </summary>
    /// <param name="cancellationToken">The bound.</param>
    /// <returns>The line without its newline, or <see langword="null"/> when the connection closed with nothing.</returns>
    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new ArrayBufferWriter<byte>();
        var buffer = new byte[ReadBufferBytes];

        if (_carried.WrittenCount > 0)
        {
            var carried = _carried.WrittenSpan.ToArray();

            _carried.Clear();

            if (Take(carried, carried.Length, line) is { } whole)
            {
                return whole;
            }
        }

        while (true)
        {
            var read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (read is 0)
            {
                return line.WrittenCount is 0 ? null : Encoding.UTF8.GetString(line.WrittenSpan);
            }

            if (Take(buffer, read, line) is { } whole)
            {
                return whole;
            }
        }
    }

    /// <summary>Adds what was read to the line, and gives the line once it has its newline, keeping the rest.</summary>
    private string? Take(byte[] read, int count, ArrayBufferWriter<byte> line)
    {
        var newline = Array.IndexOf(read, (byte)'\n', 0, count);

        line.Write(read.AsSpan(0, newline < 0 ? count : newline));

        if (newline < 0)
        {
            return null;
        }

        _carried.Write(read.AsSpan(newline + 1, count - newline - 1));
        return Encoding.UTF8.GetString(line.WrittenSpan);
    }
}
