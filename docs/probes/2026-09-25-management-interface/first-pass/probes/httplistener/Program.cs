// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// HttpListener, which on Windows is a client of the kernel's http.sys.
// args: <portFile> [host used in the prefix, default 127.0.0.1]
using System.Net;
using System.Net.Sockets;

var host = args.Length > 1 ? args[1] : "127.0.0.1";

// http.sys takes no port 0, so borrow a free port from the socket layer first.
int port;
using (var borrowed = new TcpListener(IPAddress.Loopback, 0))
{
    borrowed.Start();
    port = ((IPEndPoint)borrowed.LocalEndpoint).Port;
    borrowed.Stop();
}

using var listener = new HttpListener();
listener.Prefixes.Add($"http://{host}:{port}/");

try
{
    listener.Start();
}
catch (HttpListenerException failure)
{
    File.WriteAllText(args[0] + ".error", $"{failure.ErrorCode} {failure.Message}\n");
    return 2;
}

Shared.Announce(args[0], port);

var quit = false;
while (!quit)
{
    var context = listener.GetContext();
    var path = context.Request.Url?.AbsolutePath ?? "/";
    byte[] body;

    if (context.Request.HttpMethod != "GET")
    {
        context.Response.StatusCode = 405;
        body = "no"u8.ToArray();
    }
    else if (path == "/api/state")
    {
        context.Response.ContentType = "application/json";
        body = Shared.StateJson;
    }
    else if (path == "/quit")
    {
        body = "bye"u8.ToArray();
        quit = true;
    }
    else
    {
        context.Response.StatusCode = 404;
        body = "not found"u8.ToArray();
    }

    context.Response.ContentLength64 = body.Length;
    context.Response.OutputStream.Write(body);
    context.Response.Close();
}

listener.Stop();
return 0;
