// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

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
/// <b>One request, one answer, one connection</b>, in the JSON-RPC framing of the pipe
/// (<see cref="BackgroundPipe"/>). The wait is bounded by the caller: a person's start
/// waits <c>HandOutBound</c> and judges a background that did not answer within it hung
/// (RESOLUTIONS 10).
/// </remarks>
internal static class BackgroundClient
{
    /// <summary>Asks the background one thing.</summary>
    /// <param name="pipeName">The background's pipe.</param>
    /// <param name="method">The method, <see cref="BackgroundPipe.Show"/> or <see cref="BackgroundPipe.Stop"/>.</param>
    /// <param name="parameters">Its parameters, as a JSON object's text, or <see langword="null"/>.</param>
    /// <param name="bound">How long to wait for the answer.</param>
    /// <returns>What came back.</returns>
    public static BackgroundAnswer Ask(string pipeName, string method, string? parameters, TimeSpan bound)
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

                    // Converse owns it from here, and closes it on every path.
                    handle = null;

                    return Converse(owned, method, parameters, bound);
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

            // Returns as soon as an instance is free; a name that has gone fails the
            // next open with something other than busy.
            _ = NamedPipes.WaitForFreeInstance(pipeName, (uint)left);
        }
    }

    private static BackgroundAnswer Converse(SafeFileHandle handle, string method, string? parameters, TimeSpan bound)
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

        using (stream)
        using (var timeout = new CancellationTokenSource(bound))
        {
            try
            {
                var request = $$"""{"jsonrpc":"2.0","id":"{{BackgroundPipe.IdPrefix}}start-1","method":"{{method}}","params":{{parameters ?? "{}"}}}""" + "\n";
                stream.WriteAsync(Encoding.UTF8.GetBytes(request), timeout.Token).AsTask().GetAwaiter().GetResult();
                stream.Flush();

                var line = ReadLine(stream, timeout.Token);

                if (line is null)
                {
                    return new BackgroundAnswer(BackgroundAnswerOutcome.NoAnswer, null, "The background closed the connection without an answer.", server);
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (root.TryGetProperty("result", out var result))
                {
                    return new BackgroundAnswer(BackgroundAnswerOutcome.Answered, result.GetRawText(), null, server);
                }

                var sentence = root.TryGetProperty("error", out var refusal) && refusal.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : "The background answered with neither a result nor an error.";

                return new BackgroundAnswer(BackgroundAnswerOutcome.Refused, null, sentence, server);
            }
            catch (OperationCanceledException)
            {
                return new BackgroundAnswer(BackgroundAnswerOutcome.NoAnswer, null, string.Create(CultureInfo.InvariantCulture, $"The background did not answer within {bound.TotalSeconds:0} s."), server);
            }
            catch (Exception failure) when (failure is IOException or JsonException)
            {
                return new BackgroundAnswer(BackgroundAnswerOutcome.NoAnswer, null, failure.Message, server);
            }
        }
    }

    private static string? ReadLine(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];

        while (true)
        {
            var read = stream.ReadAsync(one, cancellationToken).AsTask().GetAwaiter().GetResult();

            if (read is 0)
            {
                return bytes.Count is 0 ? null : Encoding.UTF8.GetString([.. bytes]);
            }

            if (one[0] is (byte)'\n')
            {
                return Encoding.UTF8.GetString([.. bytes]);
            }

            bytes.Add(one[0]);
        }
    }
}
