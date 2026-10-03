// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BrowserAI.Tests.Harness;

/// <summary>One event read off a stream.</summary>
/// <param name="Name">The event's name.</param>
/// <param name="Data">The event's data, as sent.</param>
internal sealed record RawEvent(string Name, string Data)
{
    /// <summary>One string member of the data's JSON object.</summary>
    /// <param name="member">The member's name.</param>
    /// <returns>Its value.</returns>
    public string Member(string member)
    {
        using var document = JsonDocument.Parse(Data);
        return document.RootElement.GetProperty(member).GetString() ?? string.Empty;
    }
}

/// <summary>
/// A tab's event stream as a socket sees it: the request a page's
/// <c>EventSource</c> makes, and the events read off the answer one at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Closing it is a tab closing</b>: the socket goes, and the listener sees the
/// stream end, which is the whole of how the coordinator learns a tab has left.
/// </para>
/// <para>
/// <b>The answer is chunked</b>, because an event stream has no length, so the
/// body is decoded chunk by chunk before the events are split out of it. Every
/// read is bounded by <see cref="TestDefaults.InProcessHang"/>, a hang detector.
/// </para>
/// </remarks>
internal sealed class RawEventStream : IDisposable
{
    private readonly Socket _socket;
    private readonly List<byte> _wire = [];
    private readonly StringBuilder _events = new();
    private readonly byte[] _buffer = new byte[16 * 1024];
    private bool _ended;

    private RawEventStream(Socket socket) => _socket = socket;

    /// <summary>The status line's code.</summary>
    public int Status { get; private set; }

    /// <summary>Opens a tab's stream.</summary>
    /// <param name="port">The listener's port.</param>
    /// <param name="token">The listener's token.</param>
    /// <param name="tab">The tab's number.</param>
    /// <param name="page">Which page: <c>status</c> or <c>sessions</c>.</param>
    /// <returns>The open stream, its head read.</returns>
    public static async Task<RawEventStream> OpenAsync(int port, string token, int tab, string page = "status")
    {
        RawEventStream? stream = null;

        try
        {
            stream = new RawEventStream(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp));

            using var bound = new CancellationTokenSource(TestDefaults.InProcessHang);

            await stream._socket.ConnectAsync(IPAddress.Loopback, port, bound.Token);

            var request = $"GET /{token}/events?tab={tab}&page={page} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nAccept: text/event-stream\r\nSec-Fetch-Site: same-origin\r\n\r\n";

            _ = await stream._socket.SendAsync(Encoding.ASCII.GetBytes(request), SocketFlags.None, bound.Token);

            var head = await stream.ReadHeadAsync(bound.Token);

            stream.Status = head.Split(' ') is [_, var code, ..] && int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

            var opened = stream;

            // Handed to the caller; nothing here disposes it now.
            stream = null;

            return opened;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    /// <summary>The next event, or <see langword="null"/> when the stream ended first.</summary>
    /// <returns>The event.</returns>
    public async Task<RawEvent?> NextAsync()
    {
        using var bound = new CancellationTokenSource(TestDefaults.InProcessHang);

        while (true)
        {
            var text = _events.ToString();
            var end = text.IndexOf("\n\n", StringComparison.Ordinal);

            if (end >= 0)
            {
                _ = _events.Remove(0, end + 2);

                var name = "message";
                var data = new StringBuilder();

                foreach (var line in text[..end].Split('\n'))
                {
                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        name = line["event: ".Length..];
                    }
                    else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        _ = data.Append(line["data: ".Length..]);
                    }
                }

                return new RawEvent(name, data.ToString());
            }

            if (_ended || !await DecodeChunkAsync(bound.Token))
            {
                return null;
            }
        }
    }

    /// <summary>Reads events until one of the name arrives, or the stream ends.</summary>
    /// <param name="name">The event's name.</param>
    /// <returns>The event, or <see langword="null"/> when the stream ended first.</returns>
    public async Task<RawEvent?> NextNamedAsync(string name)
    {
        while (await NextAsync() is { } @event)
        {
            if (string.Equals(@event.Name, name, StringComparison.Ordinal))
            {
                return @event;
            }
        }

        return null;
    }

    /// <summary>Reads until the stream ends.</summary>
    /// <returns>Whether it ended inside the hang detector.</returns>
    public async Task<bool> EndAsync()
    {
        while (await NextAsync() is not null)
        {
        }

        return _ended;
    }

    /// <summary>Closes the stream, the way a tab closing does.</summary>
    public void Dispose() => _socket.Dispose();

    private async Task<string> ReadHeadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var at = IndexOf(_wire, "\r\n\r\n"u8);

            if (at >= 0)
            {
                var head = Encoding.ASCII.GetString([.. _wire.Take(at)]);

                _wire.RemoveRange(0, at + 4);
                return head;
            }

            if (!await ReceiveAsync(cancellationToken))
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Decodes one chunk into the event text.</summary>
    /// <returns>Whether a chunk with content arrived; <see langword="false"/> when the stream ended.</returns>
    private async Task<bool> DecodeChunkAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = IndexOf(_wire, "\r\n"u8);

            if (line >= 0)
            {
                var size = int.Parse(Encoding.ASCII.GetString([.. _wire.Take(line)]).Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                if (size is 0)
                {
                    _ended = true;
                    return false;
                }

                if (_wire.Count >= line + 2 + size + 2)
                {
                    _ = _events.Append(Encoding.UTF8.GetString([.. _wire.Skip(line + 2).Take(size)]));
                    _wire.RemoveRange(0, line + 2 + size + 2);
                    return true;
                }
            }

            if (!await ReceiveAsync(cancellationToken))
            {
                _ended = true;
                return false;
            }
        }
    }

    private async Task<bool> ReceiveAsync(CancellationToken cancellationToken)
    {
        int read;

        try
        {
            read = await _socket.ReceiveAsync(_buffer, SocketFlags.None, cancellationToken);
        }
        catch (Exception failure) when (failure is SocketException or OperationCanceledException)
        {
            // A hang detector that ran out ends the stream here, so the arm fails on
            // its own assertion with its own message and not on a cancellation.
            return false;
        }

        if (read is 0)
        {
            return false;
        }

        _wire.AddRange(_buffer.AsSpan(0, read));
        return true;
    }

    private static int IndexOf(List<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var start = 0; start <= haystack.Count - needle.Length; start++)
        {
            var found = true;

            for (var offset = 0; offset < needle.Length; offset++)
            {
                if (haystack[start + offset] != needle[offset])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return start;
            }
        }

        return -1;
    }
}
