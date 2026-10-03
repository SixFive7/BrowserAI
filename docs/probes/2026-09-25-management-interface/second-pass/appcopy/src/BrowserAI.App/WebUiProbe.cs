// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Track B scratch: the smallest web server each stack allows, compiled INTO a
// copy of the real configuration app so the size delta is the real one. One
// variant per publish, chosen by -p:WebUiVariant=RAW|HTTPSYS|KESTREL_EMPTY|KESTREL_SLIM.
#if WEBUI_ANY
#pragma warning disable
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BrowserAI.App;

internal static class WebUiProbe
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"probe\":true}");

    public static int Run(string[] args)
    {
        var portFile = args[1];
#if RAW
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(16);
        File.WriteAllText(portFile, ((IPEndPoint)listener.LocalEndPoint!).Port.ToString());
        var buffer = new byte[16 * 1024];
        while (true)
        {
            using var client = listener.Accept();
            var n = client.Receive(buffer);
            var head = Encoding.ASCII.GetString(buffer, 0, n);
            var quit = head.StartsWith("GET /quit ", StringComparison.Ordinal);
            client.Send(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Body.Length}\r\nConnection: close\r\n\r\n"));
            client.Send(Body);
            client.Shutdown(SocketShutdown.Both);
            if (quit)
            {
                return 0;
            }
        }
#elif HTTPSYS
        int port;
        using (var borrowed = new TcpListener(IPAddress.Loopback, 0))
        {
            borrowed.Start();
            port = ((IPEndPoint)borrowed.LocalEndpoint).Port;
            borrowed.Stop();
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        File.WriteAllText(portFile, port.ToString());
        while (true)
        {
            var context = listener.GetContext();
            var quit = context.Request.Url?.AbsolutePath == "/quit";
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = Body.Length;
            context.Response.OutputStream.Write(Body);
            context.Response.Close();
            if (quit)
            {
                return 0;
            }
        }
#elif KESTREL_EMPTY
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateEmptyBuilder(new Microsoft.AspNetCore.Builder.WebApplicationOptions());
        Microsoft.AspNetCore.Hosting.WebHostBuilderKestrelExtensions.UseKestrelCore(builder.WebHost);
        Microsoft.AspNetCore.Hosting.WebHostBuilderKestrelExtensions.ConfigureKestrel(builder.WebHost, o => o.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var life = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(app.Services);
        Microsoft.AspNetCore.Builder.RunExtensions.Run(app, async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(Body);
            if (context.Request.Path.Value == "/quit")
            {
                life.StopApplication();
            }
        });
        app.StartAsync().GetAwaiter().GetResult();
        var server = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>(app.Services);
        var address = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();
        File.WriteAllText(portFile, new Uri(address).Port.ToString());
        Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.WaitForShutdown(app);
        return 0;
#elif KESTREL_SLIM
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        Microsoft.AspNetCore.Hosting.WebHostBuilderKestrelExtensions.UseKestrel(builder.WebHost, o => o.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/api/state", () => Microsoft.AspNetCore.Http.Results.Bytes(Body, "application/json"));
        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/quit", (Microsoft.Extensions.Hosting.IHostApplicationLifetime life) =>
        {
            life.StopApplication();
            return Microsoft.AspNetCore.Http.Results.Text("bye");
        });
        app.StartAsync().GetAwaiter().GetResult();
        var server = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>(app.Services);
        var address = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First();
        File.WriteAllText(portFile, new Uri(address).Port.ToString());
        Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.WaitForShutdown(app);
        return 0;
#elif WEBVIEW2
        return Wv2Probe.Program.Run(args);
#elif S_LOOPBACK
        return SProbeHost.Run(args);
#elif P1_CDP
        return P1Host.Run(args);
#else
        return 3;
#endif
    }
}
#endif
