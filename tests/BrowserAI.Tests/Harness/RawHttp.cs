// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>One answer read off a raw socket, or what stood in for one.</summary>
/// <param name="Status">The status code, or zero when no status line came back.</param>
/// <param name="Headers">Every header, in order, names as sent.</param>
/// <param name="Body">The body, as UTF-8.</param>
/// <param name="Raw">Everything read, for a failure message.</param>
internal sealed record RawHttpAnswer(int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, string Body, string Raw)
{
    /// <summary>Every value of one header, compared without case.</summary>
    /// <param name="name">The header's name.</param>
    /// <returns>Its values.</returns>
    public IReadOnlyList<string> Values(string name) =>
        [.. Headers.Where(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Select(header => header.Value)];

    /// <summary>The one value of a header, or <see langword="null"/>.</summary>
    /// <param name="name">The header's name.</param>
    /// <returns>The value.</returns>
    public string? Header(string name) => Values(name) is [var only] ? only : null;
}

/// <summary>
/// An HTTP client that is nothing but a socket: it sends exactly the bytes it is
/// given and reads until the server closes or a whole answer has arrived.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <see cref="HttpClient"/>.</b> The gate's job is to refuse requests a
/// well-behaved client would never send: two <c>Host</c> headers, an attacker's
/// <c>Host</c>, a missing one, a target with dot segments, bytes that are not HTTP.
/// A real client normalises every one of those away before a byte leaves it, which
/// is the prototype's reason for the same choice
/// ([kb](../../../kb/windows/loopback-page.md#a-listener-with-one-gate-against-two-browsers----measured-2026-10-01)).
/// </para>
/// <para>
/// <b>Every read is bounded by <see cref="TestDefaults.InProcessHang"/></b>, a hang
/// detector and never a claim about how fast the listener answers.
/// </para>
/// </remarks>
internal static class RawHttp
{
    /// <summary>Sends one request and reads its answer.</summary>
    /// <param name="port">The port on <c>127.0.0.1</c>.</param>
    /// <param name="request">The request, exactly as it goes on the wire.</param>
    /// <returns>What came back.</returns>
    public static Task<RawHttpAnswer> SendAsync(int port, string request) =>
        SendAsync(IPAddress.Loopback, port, Encoding.ASCII.GetBytes(request));

    /// <summary>Sends raw bytes and reads the answer.</summary>
    /// <param name="address">Where to connect.</param>
    /// <param name="port">The port.</param>
    /// <param name="request">The bytes.</param>
    /// <returns>What came back.</returns>
    public static async Task<RawHttpAnswer> SendAsync(IPAddress address, int port, byte[] request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        using var bound = new CancellationTokenSource(TestDefaults.InProcessHang);

        await socket.ConnectAsync(address, port, bound.Token);
        _ = await socket.SendAsync(request, SocketFlags.None, bound.Token);

        var received = new List<byte>();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            int read;

            try
            {
                read = await socket.ReceiveAsync(buffer, SocketFlags.None, bound.Token);
            }
            catch (SocketException)
            {
                break;
            }

            if (read is 0)
            {
                break;
            }

            received.AddRange(buffer.AsSpan(0, read));

            if (Complete(received))
            {
                break;
            }
        }

        return Parse(Encoding.UTF8.GetString([.. received]));
    }

    /// <summary>A GET of one target, carrying the headers given and <c>Connection: close</c>.</summary>
    /// <param name="port">The port.</param>
    /// <param name="target">The request target.</param>
    /// <param name="headers">Every header, <c>Host</c> included when it is wanted.</param>
    /// <returns>The request's text.</returns>
    public static string Get(int port, string target, params string[] headers) =>
        $"GET {target} HTTP/1.1\r\n{string.Concat(headers.Select(header => header + "\r\n"))}Connection: close\r\n\r\n";

    /// <summary>A POST of one target with a body, carrying the headers given and <c>Connection: close</c>.</summary>
    /// <param name="target">The request target.</param>
    /// <param name="body">The body.</param>
    /// <param name="headers">Every header except the length, which is computed.</param>
    /// <returns>The request's text.</returns>
    public static string Post(string target, string body, params string[] headers) =>
        $"POST {target} HTTP/1.1\r\n{string.Concat(headers.Select(header => header + "\r\n"))}Content-Length: {Encoding.UTF8.GetByteCount(body).ToString(CultureInfo.InvariantCulture)}\r\nConnection: close\r\n\r\n{body}";

    /// <summary>Whether a whole answer has arrived: a header block, and as much body as it declared.</summary>
    /// <param name="received">What has arrived.</param>
    /// <returns>Whether it is whole.</returns>
    private static bool Complete(List<byte> received)
    {
        var text = Encoding.ASCII.GetString([.. received]);
        var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (end < 0)
        {
            return false;
        }

        foreach (var line in text[..end].Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line["Content-Length:".Length..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
            {
                return received.Count >= end + 4 + length;
            }
        }

        // No length: the answer ends when the server closes, which the caller's loop sees.
        return false;
    }

    private static RawHttpAnswer Parse(string raw)
    {
        var end = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);

        if (end < 0)
        {
            return new RawHttpAnswer(0, [], string.Empty, raw);
        }

        var lines = raw[..end].Split("\r\n");
        var status = lines[0].Split(' ') is [_, var code, ..] && int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        var headers = lines.Skip(1)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length is 2)
            .Select(parts => new KeyValuePair<string, string>(parts[0], parts[1].Trim()))
            .ToList();

        return new RawHttpAnswer(status, headers, raw[(end + 4)..], raw);
    }
}
