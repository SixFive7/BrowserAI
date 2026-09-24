# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Headless: the current session's logon time, which moves on Restart, on a Fast-Startup
# shutdown and power-on, and on sign-out/sign-in -- unlike the kernel boot time.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Wts {
  [DllImport("wtsapi32.dll", SetLastError=true)] public static extern bool WTSQuerySessionInformationW(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);
  [DllImport("wtsapi32.dll")] public static extern void WTSFreeMemory(IntPtr p);
  [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
  [DllImport("kernel32.dll")] public static extern bool ProcessIdToSessionId(int pid, out int session);
  public static long LogonFileTime() {
    // WTSSessionInfo = 24; WTSINFOW: State(4) SessionId(4) IncomingBytes.. then LARGE_INTEGERs; LogonTime is at offset 0x1B8? read via struct below
    IntPtr buf; int n; if (!WTSQuerySessionInformationW(IntPtr.Zero, -1, 24, out buf, out n)) return -1;
    try { var info = (WTSINFOW)Marshal.PtrToStructure(buf, typeof(WTSINFOW)); return info.LogonTime; } finally { WTSFreeMemory(buf); }
  }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct WTSINFOW {
    public int State; public int SessionId; public int IncomingBytes; public int OutgoingBytes; public int IncomingFrames; public int OutgoingFrames; public int IncomingCompressedBytes; public int OutgoingCompressedBytes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string WinStationName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=17)] public string Domain;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=21)] public string UserName;
    public long ConnectTime; public long DisconnectTime; public long LastInputTime; public long LogonTime; public long CurrentTime;
  }
}
"@
$sw = [Diagnostics.Stopwatch]::StartNew(); $ft = [Wts]::LogonFileTime(); $t = $sw.Elapsed
"session logon time: {0:o} (read in {1:F2} ms)" -f [DateTime]::FromFileTimeUtc($ft), $t.TotalMilliseconds
$boot = [DateTime]::UtcNow.AddMilliseconds(-[double][Wts]::GetTickCount64())
"kernel boot time via GetTickCount64 P/Invoke: {0:o}" -f $boot
