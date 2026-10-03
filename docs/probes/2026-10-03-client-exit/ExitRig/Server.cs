// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExitRig;

/// <summary>
/// The dummy stdio MCP server. One tool, "ping". Starts a stand-in inside a kill-on-close job
/// it creates, logs everything against QPC, and on stdin EOF waits --delay-ms before closing
/// the job and exiting with code 77 (a code nothing else on this machine produces).
/// </summary>
static class DummyServer
{
    static Native.HandlerRoutine? _ctrl;
    const int CleanExitCode = 77;

    public static int Run(string[] a)
    {
        var o = Args.Parse(a, 1);
        string dir = o["logdir"];
        var log = new Log(Path.Combine(dir, $"server-{Environment.ProcessId}.log"));
        int delay = int.Parse(o.GetValueOrDefault("delay-ms", "0"), CultureInfo.InvariantCulture);
        int callDelay = int.Parse(o.GetValueOrDefault("call-delay-ms", "0"), CultureInfo.InvariantCulture);
        int ppid = Native.ParentPid();
        log.W("server", "START", $"delayMs={delay} ppid={ppid} parentImage={Native.ImageOfPid(ppid)} cmdline={Environment.CommandLine}");
        log.W("server", "CONSOLE", Native.ConsoleInfo());
        log.W("server", "JOB", Native.OwnJobInfo());
        log.W("server", "STDIO", $"stdin={Native.FileTypeOf(-10)} stdout={Native.FileTypeOf(-11)} stderr={Native.FileTypeOf(-12)}");
        _ctrl = t => { log.W("server", "CTRL", Native.CtrlName(t)); return true; };
        Native.SetConsoleCtrlHandler(_ctrl, true);
        Native.WatchParent(ppid, log, "server");

        IntPtr job = IntPtr.Zero;
        if (!o.ContainsKey("no-standin"))
        {
            job = Native.CreateKillOnCloseJob(out uint flags);
            string exe = Environment.ProcessPath!;
            int sp = Native.StartInJob(job, exe, $"\"{exe}\" standin --logdir \"{dir}\"", out string err);
            log.W("server", "STANDIN", $"pid={sp} jobFlagsReadBack=0x{flags:X} {err}");
        }
        if (o.TryGetValue("keeper", out var keeperMode)) Keeper.StartFromServer(keeperMode, dir, log);

        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        var reader = new StreamReader(stdin, new UTF8Encoding(false), false, 65536);
        var outGate = new object();
        int received = 0;
        void Send(JsonObject msg)
        {
            var bytes = Encoding.UTF8.GetBytes(msg.ToJsonString() + "\n");
            try { lock (outGate) { stdout.Write(bytes, 0, bytes.Length); stdout.Flush(); } }
            catch (Exception e) { log.W("server", "SEND_FAILED", e.GetType().Name + ": " + e.Message); }
        }

        string eofHow = "EOF";
        while (true)
        {
            string? line;
            try { line = reader.ReadLine(); }
            catch (Exception e) { eofHow = "READ_ERROR " + e.GetType().Name + ": " + e.Message; break; }
            if (line is null) break;
            if (line.Length == 0) continue;
            received++;
            JsonNode? node = null;
            try { node = JsonNode.Parse(line); } catch { }
            if (node is not JsonObject msg) { log.W("server", "RECV_BAD", line.Length > 300 ? line[..300] : line); continue; }
            string method = msg["method"]?.GetValue<string>() ?? "";
            JsonNode? id = msg["id"]?.DeepClone();
            log.W("server", "RECV", $"method={method} id={(id is null ? "-" : id.ToJsonString())} bytes={line.Length}");
            JsonObject? result = null;
            switch (method)
            {
                case "initialize":
                    var pv = msg["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18";
                    result = new JsonObject
                    {
                        ["protocolVersion"] = pv,
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "exitprobe", ["version"] = "1.0.0" },
                    };
                    break;
                case "tools/list":
                    result = new JsonObject
                    {
                        ["tools"] = new JsonArray(new JsonObject
                        {
                            ["name"] = "ping",
                            ["description"] = "Returns pong.",
                            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
                        }),
                    };
                    break;
                case "tools/call":
                    result = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"pong from pid {Environment.ProcessId}" }), ["isError"] = false };
                    break;
                case "ping":
                case "logging/setLevel":
                    result = new JsonObject();
                    break;
                case "resources/list": result = new JsonObject { ["resources"] = new JsonArray() }; break;
                case "resources/templates/list": result = new JsonObject { ["resourceTemplates"] = new JsonArray() }; break;
                case "prompts/list": result = new JsonObject { ["prompts"] = new JsonArray() }; break;
            }
            if (id is null) continue;
            if (method == "tools/call" && callDelay > 0 && result is not null)
            {
                var delayedId = id; var delayedResult = result;
                log.W("server", "CALL_DELAYED", $"id={id.ToJsonString()} ms={callDelay}");
                new Thread(() =>
                {
                    Thread.Sleep(callDelay);
                    Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = delayedId, ["result"] = delayedResult });
                    log.W("server", "SENT", $"id={delayedId.ToJsonString()} for=tools/call (after {callDelay} ms)");
                }) { IsBackground = true }.Start();
                continue;
            }
            if (result is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
            else Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" } });
            log.W("server", "SENT", $"id={id.ToJsonString()} for={method}");
        }

        log.W("server", "EOF", $"how={eofHow} messagesReceived={received}");
        var sw = Stopwatch.StartNew();
        while (true)
        {
            double el = sw.Elapsed.TotalMilliseconds;
            if (el >= delay) break;
            Thread.Sleep((int)Math.Max(1, Math.Min(25, delay - el)));
            log.W("server", "ALIVE", $"sinceEofMs={sw.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}");
        }
        log.W("server", "CLOSING_JOB", $"sinceEofMs={sw.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}");
        if (job != IntPtr.Zero) Native.CloseHandle(job);
        log.W("server", "EXIT", $"code={CleanExitCode}");
        Environment.Exit(CleanExitCode);
        return CleanExitCode;
    }
}

/// <summary>Stands in for Playwright's child: sits in the server's job and does nothing but log.</summary>
static class StandIn
{
    static Native.HandlerRoutine? _ctrl;
    public static int Run(string[] a)
    {
        var o = Args.Parse(a, 1);
        var log = new Log(Path.Combine(o["logdir"], $"standin-{Environment.ProcessId}.log"));
        log.W("standin", "START", $"ppid={Native.ParentPid()} {Native.OwnJobInfo()} {Native.ConsoleInfo()}");
        _ctrl = t => { log.W("standin", "CTRL", Native.CtrlName(t)); return true; };
        Native.SetConsoleCtrlHandler(_ctrl, true);
        while (true) { Thread.Sleep(500); log.W("standin", "ALIVE", ""); }
    }
}
