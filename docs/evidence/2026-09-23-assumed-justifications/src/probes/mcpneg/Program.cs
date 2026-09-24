// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Probe C: "The SDK refuses to negotiate BELOW a pinned version and throws."
// A fake MCP server echoes a chosen protocolVersion back at initialize; the
// client pins McpClientOptions.ProtocolVersion = BrowserAI's own "2025-11-25".
// POSITIVE CONTROL: the arm that echoes the pinned version must connect.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;

const string Pinned = "2025-11-25";   // BrowserAI's ChildConnection.ChildProtocolVersion

if (args.Length >= 2 && args[0] == "--server") { RunFakeServer(args[1]); return 0; }

Console.WriteLine("############ PROBE C -- what does the SDK do when a child echoes another protocol version?");
Console.WriteLine($"ModelContextProtocol 2.2.0 | .NET 10.0.401 | Windows 11 Pro 10.0.26200 | 2026-09-23");
Console.WriteLine($"client pins McpClientOptions.ProtocolVersion = \"{Pinned}\" (BrowserAI's ChildConnection.ChildProtocolVersion)");
Console.WriteLine();

string[] arms = [
    Pinned,        // CONTROL: exact match, must succeed
    "2025-06-18",  // BELOW  (one revision down)
    "2024-11-05",  // BELOW  (the oldest supported)
    "2026-07-28",  // ABOVE  (the newest supported)
    "1999-01-01",  // unsupported entirely
    "<omit>",      // the field is ABSENT from the initialize result
    "<null>",      // the field is present and null
];

foreach (var echo in arms) await Arm(echo);
return 0;

static async Task Arm(string echo)
{
    var kind = echo == Pinned ? "EXACT (control)" : echo.StartsWith('<') ? "NO VERSION ON THE WIRE" : string.CompareOrdinal(echo, Pinned) < 0 ? "BELOW" : "ABOVE/UNSUPPORTED";
    Console.WriteLine($"--- server echoes protocolVersion=\"{echo}\"  [{kind}]");
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "fake",
        Command = Environment.ProcessPath!,
        Arguments = ["--server", echo],
    });
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ProtocolVersion = Pinned, InitializationTimeout = TimeSpan.FromSeconds(20) },
            cancellationToken: cts.Token);
        Console.WriteLine($"    RESULT: CONNECTED. NegotiatedProtocolVersion=\"{client.NegotiatedProtocolVersion}\"");
    }
    catch (Exception e)
    {
        Console.WriteLine($"    RESULT: THREW {e.GetType().FullName}");
        Console.WriteLine($"            {e.Message.Replace("\n", " ")}");
        if (e.InnerException is { } inner)
            Console.WriteLine($"            inner: {inner.GetType().Name}: {inner.Message.Replace("\n", " ")}");
    }
    Console.WriteLine();
}

static void RunFakeServer(string echo)
{
    var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
    var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
    string line;
    while ((line = stdin.ReadLine()) is not null)
    {
        if (line.Length == 0) continue;
        JsonNode msg;
        try { msg = JsonNode.Parse(line); } catch { continue; }
        var method = msg?["method"]?.GetValue<string>();
        var id = msg?["id"];
        Console.Error.WriteLine($"[fake] <- {method} id={id?.ToJsonString() ?? "null"}");
        if (id is null) continue;   // a notification

        JsonObject result = method switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = echo == "<omit>" ? null : echo == "<null>" ? JsonValue.Create((string)null) : JsonValue.Create(echo),
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "fake", ["version"] = "0.0.0" },
            },
            "tools/list" => new JsonObject { ["tools"] = new JsonArray() },
            _ => new JsonObject(),
        };
        if (method == "initialize" && echo == "<omit>") result.Remove("protocolVersion");
        var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };
        stdout.Write(response.ToJsonString(JsonSerializerOptions.Default));
        stdout.Write('\n');
    }
}
