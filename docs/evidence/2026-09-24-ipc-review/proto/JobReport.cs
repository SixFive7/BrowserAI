// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review scratch prototype, 2026-09-24. Not product code.
// What a process started by a client can learn about the job it runs in, and whether its own child inherits it.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

internal static unsafe partial class JobReport
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsProcessInJob(nint process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(nint job, int infoClass, void* info, uint length, out uint returned);

    public static int Sleep(string[] a)
    {
        Thread.Sleep(int.Parse(a[1], CultureInfo.InvariantCulture));
        return 0;
    }

    public static int Run(string[] a)
    {
        IsProcessInJob(Native.GetCurrentProcess(), 0, out var selfIn);
        var info = stackalloc byte[144]; // JOBOBJECT_EXTENDED_LIMIT_INFORMATION (x64)
        var queried = QueryInformationJobObject(0, 9 /* JobObjectExtendedLimitInformation */, info, 144, out _);
        var flags = queried ? *(uint*)(info + 16) : 0; // BasicLimitInformation.LimitFlags at offset 16
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("sleep");
        psi.ArgumentList.Add("3000");
        using var child = Process.Start(psi)!;
        IsProcessInJob(child.Handle, 0, out var childIn);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"JOBREPORT self inJob={selfIn} immediateJobQueried={queried} limitFlags=0x{flags:X} (BREAKAWAY_OK={(flags & 0x800) != 0} SILENT_BREAKAWAY_OK={(flags & 0x1000) != 0} KILL_ON_JOB_CLOSE={(flags & 0x2000) != 0}) ; a child started with Process.Start inJob={childIn}"));
        child.WaitForExit();
        return 0;
    }
}
