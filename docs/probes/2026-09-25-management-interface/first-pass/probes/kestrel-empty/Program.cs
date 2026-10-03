// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// ASP.NET Core on Kestrel through the EMPTY builder and one terminal
// middleware: no routing, no configuration files, no logging providers.
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
builder.WebHost.UseKestrelCore();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

var app = builder.Build();
var life = app.Services.GetRequiredService<IHostApplicationLifetime>();

app.Run(async context =>
{
    switch (context.Request.Path.Value)
    {
        case "/api/state":
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(Shared.StateJson);
            break;
        case "/quit":
            await context.Response.WriteAsync("bye");
            life.StopApplication();
            break;
        default:
            context.Response.StatusCode = 404;
            break;
    }
});

await app.StartAsync();

var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
Shared.Announce(args[0], new Uri(address).Port);

await app.WaitForShutdownAsync();
return 0;
