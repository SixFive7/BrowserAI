// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Who is on the other end of a loopback connection: the owning pid from the
// system's TCP table, then that process's user, compared with ours. TCP has no
// per-user access list; this is the check that stands in for one.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SProbe;

internal static unsafe class Peer
{
    private static readonly byte[] Mine = UserOf(GetCurrentProcess());

    public readonly record struct Verdict(bool SameUser, int ProcessId, string Why, double Milliseconds);

    public static Verdict Check(Socket client)
    {
        var clock = Stopwatch.StartNew();
        var remote = (IPEndPoint)client.RemoteEndPoint!;
        var local = (IPEndPoint)client.LocalEndPoint!;
        var pid = OwnerOf(remote, local);
        if (pid == 0)
        {
            return new Verdict(false, 0, "no row in the TCP table", clock.Elapsed.TotalMilliseconds);
        }

        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)pid);
        if (process == 0)
        {
            return new Verdict(false, pid, $"OpenProcess failed ({Marshal.GetLastWin32Error()})", clock.Elapsed.TotalMilliseconds);
        }

        try
        {
            var theirs = UserOf(process);
            var same = theirs.Length != 0 && theirs.AsSpan().SequenceEqual(Mine);
            return new Verdict(same, pid, same ? "same user" : theirs.Length == 0 ? "token unreadable" : "another user", clock.Elapsed.TotalMilliseconds);
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    // The CLIENT's row: its local port is our remote port, and its remote port is our listening port.
    private static int OwnerOf(IPEndPoint remote, IPEndPoint local)
    {
        uint size = 0;
        _ = GetExtendedTcpTable(0, ref size, false, 2 /* AF_INET */, 4 /* TCP_TABLE_OWNER_PID_CONNECTIONS */, 0);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var buffer = Marshal.AllocHGlobal((nint)size);
            try
            {
                var rc = GetExtendedTcpTable(buffer, ref size, false, 2, 4, 0);
                if (rc == 122 /* ERROR_INSUFFICIENT_BUFFER */)
                {
                    continue;
                }

                if (rc != 0)
                {
                    return 0;
                }

                var count = *(uint*)buffer;
                var rows = (Row*)((byte*)buffer + 4);
                var clientPort = (ushort)IPAddress.HostToNetworkOrder((short)remote.Port);
                var serverPort = (ushort)IPAddress.HostToNetworkOrder((short)local.Port);
#pragma warning disable CS0618
                var loopback = (uint)IPAddress.Loopback.Address;
#pragma warning restore CS0618
                for (var i = 0; i < count; i++)
                {
                    if (rows[i].LocalAddr == loopback && rows[i].RemoteAddr == loopback
                        && (ushort)rows[i].LocalPort == clientPort && (ushort)rows[i].RemotePort == serverPort)
                    {
                        return (int)rows[i].OwningPid;
                    }
                }

                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return 0;
    }

    private static byte[] UserOf(nint process)
    {
        if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out var token))
        {
            return [];
        }

        try
        {
            _ = GetTokenInformation(token, 1 /* TokenUser */, 0, 0, out var needed);
            var buffer = Marshal.AllocHGlobal((nint)needed);
            try
            {
                if (!GetTokenInformation(token, 1, buffer, needed, out _))
                {
                    return [];
                }

                var sid = *(nint*)buffer;
                var length = GetLengthSid(sid);
                var bytes = new byte[length];
                Marshal.Copy(sid, bytes, 0, (int)length);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Row
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(nint table, ref uint size, bool order, uint family, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int kind, nint info, uint length, out uint needed);

    [DllImport("advapi32.dll")]
    private static extern uint GetLengthSid(nint sid);
}
