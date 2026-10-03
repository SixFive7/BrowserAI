// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace ExitRig;

/// <summary>Reports what a process started inside the pseudoconsole sees as its standard handles.</summary>
static class TtyProbe
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetConsoleMode(IntPtr h, out uint mode);

    public static int Run(string[] a)
    {
        var o = Args.Parse(a, 1);
        var log = new Log(Path.Combine(o["logdir"], $"ttyprobe-{Environment.ProcessId}.log"));
        foreach (var (n, std) in new[] { ("stdin", -10), ("stdout", -11), ("stderr", -12) })
        {
            IntPtr h = Native.GetStdHandle(std);
            bool cm = GetConsoleMode(h, out uint mode);
            log.W("ttyprobe", "STD", $"{n} handle=0x{h.ToInt64():X} type={Native.FileTypeOf(std)} getConsoleMode={cm} mode=0x{mode:X} err={(cm ? 0 : Marshal.GetLastWin32Error())}");
        }
        log.W("ttyprobe", "CONSOLE", Native.ConsoleInfo());
        Console.Out.Write("TTYPROBE-VISIBLE-TEXT\r\n");
        Console.Out.Flush();
        Thread.Sleep(1500);
        return 0;
    }
}
