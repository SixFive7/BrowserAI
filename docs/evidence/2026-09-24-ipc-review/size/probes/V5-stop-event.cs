// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review size probe: a named Global\ stop event whose wait cancels the process's own shutdown token.
namespace BrowserAI;

internal static class IpcProbe
{
    public static void Start()
    {
        var stop = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, @"Global\BrowserAI-Stop-probe-" + System.Environment.ProcessId);
        var cts = new System.Threading.CancellationTokenSource();
        _ = System.Threading.ThreadPool.RegisterWaitForSingleObject(stop, (_, _) => cts.Cancel(), null, System.Threading.Timeout.Infinite, true);
    }
}
