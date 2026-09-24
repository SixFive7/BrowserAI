// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review size probe: CreateNamedPipeW with PIPE_REJECT_REMOTE_CLIENTS and an owner-only DACL, one dedicated thread.
namespace BrowserAI;

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static unsafe partial class IpcProbe
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBuf, uint inBuf, uint timeout, nint sa);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConnectNamedPipe(SafeFileHandle h, nint o);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DisconnectNamedPipe(SafeFileHandle h);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadFile(SafeFileHandle h, byte* b, int n, out int r, nint o);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteFile(SafeFileHandle h, byte* b, int n, out int w, nint o);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringToSd(string sddl, uint rev, out nint sd, out uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Sa
    {
        public int Length;
        public nint Sd;
        public int Inherit;
    }

    public static void Start()
    {
        _ = ConvertStringToSd("D:P(A;;GA;;;OW)", 1, out var sd, out _);
        var sa = new Sa { Length = sizeof(Sa), Sd = sd, Inherit = 0 };
        var h = CreateNamedPipe(@"\\.\pipe\BrowserAI-probe-" + System.Environment.ProcessId, 0x3 | 0x00080000, 0x8, 1, 65536, 4096, 0, (nint)(&sa));
        var t = new System.Threading.Thread(() =>
        {
            var buffer = new byte[256];
            while (true)
            {
                _ = ConnectNamedPipe(h, 0);
                fixed (byte* p = buffer)
                {
                    _ = ReadFile(h, p, buffer.Length, out _, 0);
                }

                var answer = System.Text.Encoding.UTF8.GetBytes("{\"pid\":" + System.Environment.ProcessId + "}");
                fixed (byte* p = answer)
                {
                    _ = WriteFile(h, p, answer.Length, out _, 0);
                }

                fixed (byte* p = buffer)
                {
                    while (ReadFile(h, p, buffer.Length, out var r, 0) && r > 0)
                    {
                    }
                }

                _ = DisconnectNamedPipe(h);
            }
        })
        { IsBackground = true };
        t.Start();
    }
}
