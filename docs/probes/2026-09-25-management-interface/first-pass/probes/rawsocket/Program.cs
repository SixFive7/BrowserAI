// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A hand-rolled HTTP/1.1 listener over a socket, bound to 127.0.0.1 and an
// ephemeral port. No Host or Origin check: this probe measures the default.
using System.Net;
using System.Net.Sockets;
using System.Text;

using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listener.ExclusiveAddressUse = true;
listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
listener.Listen(16);
Shared.Announce(args[0], ((IPEndPoint)listener.LocalEndPoint!).Port);

var quit = false;
var buffer = new byte[16 * 1024];
while (!quit)
{
    using var client = listener.Accept();
    var read = 0;
    int end;
    while ((end = IndexOfHeaderEnd(buffer, read)) < 0 && read < buffer.Length)
    {
        var n = client.Receive(buffer, read, buffer.Length - read, SocketFlags.None);
        if (n == 0)
        {
            break;
        }

        read += n;
    }

    if (end < 0)
    {
        continue;
    }

    var head = Encoding.ASCII.GetString(buffer, 0, end);
    var requestLine = head[..head.IndexOf("\r\n", StringComparison.Ordinal)];
    var parts = requestLine.Split(' ');
    var (status, body, type) = parts.Length == 3 && parts[0] == "GET"
        ? parts[1] switch
        {
            "/api/state" => ("200 OK", Shared.StateJson, "application/json"),
            "/quit" => ("200 OK", "bye"u8.ToArray(), "text/plain"),
            _ => ("404 Not Found", "not found"u8.ToArray(), "text/plain"),
        }
        : ("405 Method Not Allowed", "no"u8.ToArray(), "text/plain");

    if (parts.Length == 3 && parts[1] == "/quit")
    {
        quit = true;
    }

    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
    client.Send(header);
    client.Send(body);
    client.Shutdown(SocketShutdown.Both);
}

return 0;

static int IndexOfHeaderEnd(byte[] b, int length)
{
    for (var i = 3; i < length; i++)
    {
        if (b[i - 3] == '\r' && b[i - 2] == '\n' && b[i - 1] == '\r' && b[i] == '\n')
        {
            return i + 1;
        }
    }

    return -1;
}
