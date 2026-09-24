// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch probe for Q288, not part of the product. A stdio MCP server that
// records how it was started (its own image path as the OS reports it, the raw
// command line, argv, cwd and a few environment values), then answers
// initialize, tools/list and tools/call. stdout carries JSON-RPC only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

static class ProbeStub
{
    static string logFile;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    static void Log(string line)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { File.AppendAllText(logFile, line + "\n", new UTF8Encoding(false)); return; }
            catch (IOException) { System.Threading.Thread.Sleep(25); }
        }
    }

    static int Main(string[] args)
    {
        Process me = Process.GetCurrentProcess();
        string image = me.MainModule.FileName;
        string dir = Environment.GetEnvironmentVariable("PROBE_LOG_DIR");
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Path.GetDirectoryName(image), "launches");
        Directory.CreateDirectory(dir);
        logFile = Path.Combine(dir, "stub-" + me.Id + "-" + DateTime.UtcNow.Ticks + ".jsonl");

        var env = new SortedDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            string k = (string)e.Key;
            if (k.StartsWith("PROBE_", StringComparison.OrdinalIgnoreCase)
                || k.Equals("LOCALAPPDATA", StringComparison.OrdinalIgnoreCase)
                || k.Equals("USERPROFILE", StringComparison.OrdinalIgnoreCase)
                || k.Equals("HOME", StringComparison.OrdinalIgnoreCase)
                || k.Equals("PATH", StringComparison.OrdinalIgnoreCase))
                env[k] = (string)e.Value;
        }
        var launch = new Dictionary<string, object>();
        launch["event"] = "launch";
        launch["at"] = DateTime.UtcNow.ToString("o");
        launch["pid"] = me.Id;
        launch["image"] = image;
        launch["commandLine"] = Environment.CommandLine;
        launch["argv"] = args;
        launch["cwd"] = Environment.CurrentDirectory;
        launch["env"] = env;
        string launchJson = Json.Serialize(launch);
        Log(launchJson);

        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        stdout.NewLine = "\n";
        stdout.AutoFlush = true;

        string line;
        while ((line = stdin.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            Dictionary<string, object> msg;
            try { msg = Json.DeserializeObject(line) as Dictionary<string, object>; }
            catch (Exception) { Log("{\"event\":\"nonjson\"}"); continue; }
            if (msg == null) continue;
            object method; msg.TryGetValue("method", out method);
            object id; bool hasId = msg.TryGetValue("id", out id);
            Log(Json.Serialize(new Dictionary<string, object> { { "event", "recv" }, { "at", DateTime.UtcNow.ToString("o") }, { "method", method }, { "id", id } }));
            if (!hasId || method == null) continue;
            object result;
            string m = (string)method;
            if (m == "initialize")
            {
                object pv = "2025-06-18";
                object p; if (msg.TryGetValue("params", out p) && p is Dictionary<string, object>) { object v; if (((Dictionary<string, object>)p).TryGetValue("protocolVersion", out v)) pv = v; }
                result = new Dictionary<string, object> {
                    { "protocolVersion", pv },
                    { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() } } },
                    { "serverInfo", new Dictionary<string, object> { { "name", "q288-probe-stub" }, { "version", "pid-" + me.Id } } } };
            }
            else if (m == "tools/list")
            {
                result = new Dictionary<string, object> { { "tools", new object[] { new Dictionary<string, object> {
                    { "name", "probe_whoami" },
                    { "description", "Returns how this probe process was started." },
                    { "inputSchema", new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() } } } } } } };
            }
            else if (m == "tools/call")
            {
                result = new Dictionary<string, object> { { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", launchJson } } } } };
            }
            else
            {
                result = new Dictionary<string, object>();
            }
            string reply = Json.Serialize(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "result", result } });
            stdout.WriteLine(reply);
            Log(Json.Serialize(new Dictionary<string, object> { { "event", "sent" }, { "at", DateTime.UtcNow.ToString("o") }, { "method", m }, { "id", id } }));
        }
        Log(Json.Serialize(new Dictionary<string, object> { { "event", "stdin-eof" }, { "at", DateTime.UtcNow.ToString("o") } }));
        return 0;
    }
}
