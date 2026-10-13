// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// One page on a loopback port, answered with <c>Cache-Control: no-store</c>, whose
/// port refuses every connection once the site is disposed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-13 with the maintainer's 161</b>, for the arm whose subject is a
/// resumed Firefox session whose page can neither load nor come back from Firefox's cache:
/// <c>no-store</c> keeps the page out of the cache, and a socket of its own makes the end of
/// the server a refused connection, the state the two sightings of 2026-10-12 and every
/// stalled resume of 2026-10-13 had.
/// </para>
/// <para>
/// <b>A socket and not <see cref="LoopbackSite"/>'s <see cref="HttpListener"/></b>, whose
/// port is the HTTP service's and not this process's, so whether it refuses a connection
/// once the listener closes is the service's decision and was not measured.
/// </para>
/// </remarks>
internal sealed class NoStoreSite : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;
    private int _requests;

    private NoStoreSite(TcpListener listener)
    {
        _listener = listener;

        // Read while the listener is bound: once it stops, its endpoint is the one it was
        // asked for, port 0.
        Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture)}/no-store";
        _serving = ServeAsync();
    }

    /// <summary>The page's address, the same before and after the site is disposed.</summary>
    public string Url { get; }

    /// <summary>How many requests the site has answered.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <summary>The title the page carries.</summary>
    public const string Title = "no-store page";

    /// <summary>Binds a loopback port of the system's choosing and starts answering.</summary>
    /// <returns>The running site.</returns>
    public static NoStoreSite Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return new NoStoreSite(listener);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>The listener stops first</b>, so the port refuses from this moment on, and the
    /// loop ends with it.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();

        try
        {
            await _serving.WaitAsync(TestDefaults.InProcessHang);
        }
        catch (Exception failure) when (failure is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // The loop ends on the stop, which is the ordinary way out of it.
        }

        _stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception failure) when (failure is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = AnswerAsync(client);
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var read = 0;

                // The request, up to the blank line that ends its headers.
                while (read < buffer.Length)
                {
                    var got = await stream.ReadAsync(buffer.AsMemory(read), _stopping.Token);

                    if (got is 0)
                    {
                        return;
                    }

                    read += got;

                    if (buffer.AsSpan(0, read).IndexOf("\r\n\r\n"u8) >= 0)
                    {
                        break;
                    }
                }

                _ = Interlocked.Increment(ref _requests);

                var body = Encoding.UTF8.GetBytes($"<!doctype html><title>{Title}</title><h1>{Title}</h1>");
                var head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: text/html; charset=utf-8\r\n"
                    + "Cache-Control: no-store\r\n"
                    + $"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n"
                    + "Connection: close\r\n\r\n");

                await stream.WriteAsync(head, _stopping.Token);
                await stream.WriteAsync(body, _stopping.Token);
            }
            catch (Exception failure) when (failure is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // A browser that hung up, or the site stopping: nothing is owed either way.
            }
        }
    }
}
