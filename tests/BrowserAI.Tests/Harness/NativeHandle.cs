// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The two things a test can do with a raw handle another process duplicated
/// into it: ask whether it is still ours, and close it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raw rather than a <c>SafeHandle</c>, and the reason is the assertion.</b>
/// The client-liveness test's central claim is that BrowserAI's stdin never
/// reached EOF, and the evidence for it is that <i>this</i> process still holds a
/// write end of that pipe at the moment BrowserAI exits. A <c>SafeHandle</c>
/// would answer "is it disposed", which is a question about this object;
/// <c>GetHandleInformation</c> answers "does the kernel still have it", which is
/// the question the pipe's EOF rule is written in terms of.
/// </para>
/// <para>
/// Nothing here is product code and nothing here belongs in <c>src/</c>: the
/// product never receives a handle from anywhere.
/// </para>
/// </remarks>
internal static partial class NativeHandle
{
    /// <summary>Whether the kernel still recognises this handle in this process.</summary>
    /// <param name="handle">A handle duplicated into this process.</param>
    /// <returns><see langword="true"/> if it is open here.</returns>
    public static bool IsValid(nint handle) => handle != nint.Zero && GetHandleInformation(handle, out _);

    /// <summary>Closes a handle this process was handed, if it has one.</summary>
    /// <param name="handle">A handle duplicated into this process.</param>
    public static void Close(nint handle)
    {
        if (handle != nint.Zero)
        {
            _ = CloseHandle(handle);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetHandleInformation(nint hObject, out uint lpdwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}
