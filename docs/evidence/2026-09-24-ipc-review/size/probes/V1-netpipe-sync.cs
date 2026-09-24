// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review size probe: NamedPipeServerStream (CurrentUserOnly), synchronous, one dedicated thread.
namespace BrowserAI;

internal static class IpcProbe
{
    public static void Start()
    {
        var server = new System.IO.Pipes.NamedPipeServerStream("BrowserAI-probe-" + System.Environment.ProcessId, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.CurrentUserOnly);
        var t = new System.Threading.Thread(() =>
        {
            var buffer = new byte[256];
            while (true)
            {
                server.WaitForConnection();
                _ = server.Read(buffer, 0, buffer.Length);
                server.Write(System.Text.Encoding.UTF8.GetBytes("{\"pid\":" + System.Environment.ProcessId + "}"));
                server.WaitForPipeDrain();
                server.Disconnect();
            }
        })
        { IsBackground = true };
        t.Start();
    }
}
