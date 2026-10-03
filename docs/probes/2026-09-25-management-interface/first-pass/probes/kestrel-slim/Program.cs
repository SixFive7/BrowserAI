// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// ASP.NET Core minimal APIs on Kestrel, through the slim builder, bound to
// 127.0.0.1 and an ephemeral port. No host filtering configured: this probe
// measures the default.
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var builder = WebApplication.CreateSlimBuilder(args.Skip(1).ToArray());
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

var app = builder.Build();
app.MapGet("/api/state", () => Results.Bytes(Shared.StateJson, "application/json"));
app.MapGet("/quit", (IHostApplicationLifetime life) =>
{
    life.StopApplication();
    return Results.Text("bye");
});

await app.StartAsync();

var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
Shared.Announce(args[0], new Uri(address).Port);

await app.WaitForShutdownAsync();
return 0;
