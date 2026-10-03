// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text;

internal static class Shared
{
    public static readonly Stopwatch SinceMain = Stopwatch.StartNew();

    // About 1 KB, shaped like the status report's JSON.
    public static readonly byte[] StateJson = Encoding.UTF8.GetBytes(
        "{\"schemaVersion\":3,\"product\":\"BrowserAI\",\"version\":\"1.1.1-probe\",\"installRoot\":\"C:\\\\Users\\\\x\\\\AppData\\\\Local\\\\BrowserAI.app\","
        + "\"dataRoot\":\"C:\\\\Users\\\\x\\\\AppData\\\\Local\\\\BrowserAI\",\"clients\":["
        + "{\"key\":\"claude\",\"displayName\":\"Claude Code\",\"clientFound\":true,\"status\":\"Registered for all your Claude Code projects.\",\"userScope\":{\"scope\":\"User\",\"file\":\"C:\\\\Users\\\\x\\\\.claude.json\",\"command\":\"C:\\\\Users\\\\x\\\\AppData\\\\Local\\\\BrowserAI.app\\\\current\\\\BrowserAI.Server.exe\",\"ownership\":\"OursAndPresent\",\"unreadable\":null},\"projectScope\":null},"
        + "{\"key\":\"codex\",\"displayName\":\"Codex\",\"clientFound\":true,\"status\":\"Registered for all your Codex projects.\",\"userScope\":{\"scope\":\"User\",\"file\":\"C:\\\\Users\\\\x\\\\.codex\\\\config.toml\",\"command\":\"C:\\\\Users\\\\x\\\\AppData\\\\Local\\\\BrowserAI.app\\\\current\\\\BrowserAI.Server.exe\",\"ownership\":\"OursAndPresent\",\"unreadable\":null},\"projectScope\":null}"
        + "],\"sessions\":[{\"pid\":1234,\"client\":\"claude-code\",\"workingDirectory\":\"C:\\\\Source\\\\repo\",\"sessions\":[{\"directory\":\"C:\\\\Source\\\\repo\\\\.browser\",\"purpose\":\"Checks the login flow\",\"browserOpen\":true}]}]}");

    // Written to a temporary name and renamed, so the driver never reads half a line.
    public static void Announce(string portFile, int port)
    {
        var line = $"{port}\t{SinceMain.Elapsed.TotalMilliseconds:F3}\n";
        var temporary = portFile + ".tmp";
        File.WriteAllText(temporary, line);
        File.Move(temporary, portFile, overwrite: true);
    }
}
