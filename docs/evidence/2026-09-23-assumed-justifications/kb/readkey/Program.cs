// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Probe: does Console.ReadKey() BLOCK when a console is attached and nobody types?
using System;
using System.IO;

var log = args.Length > 0 ? args[0] : "readkey.log";
void W(string s) => File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} {s}{Environment.NewLine}");
W($"start pid={Environment.ProcessId} IsInputRedirected={Console.IsInputRedirected}");
try
{
    W("calling Console.ReadKey(true)");
    var k = Console.ReadKey(true);
    W($"RETURNED key={k.Key}");
}
catch (Exception ex)
{
    W($"THREW {ex.GetType().FullName}: {ex.Message}");
}
W("end");
