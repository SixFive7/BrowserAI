// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review size probe: NamedPipeServerStream (CurrentUserOnly | Asynchronous), no dedicated thread.
namespace BrowserAI;

internal static class IpcProbe
{
    public static void Start()
    {
        var server = new System.IO.Pipes.NamedPipeServerStream("BrowserAI-probe-" + System.Environment.ProcessId, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.CurrentUserOnly | System.IO.Pipes.PipeOptions.Asynchronous);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            var buffer = new byte[256];
            while (true)
            {
                await server.WaitForConnectionAsync().ConfigureAwait(false);
                _ = await server.ReadAsync(buffer).ConfigureAwait(false);
                await server.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"pid\":" + System.Environment.ProcessId + "}")).ConfigureAwait(false);
                while (await server.ReadAsync(buffer).ConfigureAwait(false) > 0)
                {
                }

                server.Disconnect();
            }
        });
    }
}
