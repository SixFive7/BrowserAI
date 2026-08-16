// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Whether a live process has registered itself for Windows' restart-after-crash
/// or restart-after-update feature.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked of the running process rather than inferred from its command
/// line.</b> The charter's resurrection article rests on Playwright's browser
/// command line overshooting <c>RegisterApplicationRestart</c>'s 1023-character
/// limit by 531 or more, which means the registration <i>fails</i> and Windows
/// does not resurrect the browser after a reboot or an update. That is an
/// argument about a length; this is the observation that settles it, and the two
/// can disagree — a shorter command line, or an upstream that starts trimming its
/// argument list, would flip the answer with nothing else changing.
/// </para>
/// <para>
/// <b><c>0x80070490</c> is the healthy answer.</b> It is
/// <c>HRESULT_FROM_WIN32(ERROR_NOT_FOUND)</c>: the process has no registration at
/// all. Anything else means Windows has been told to bring this browser back, and
/// [the maintainer's own browsers were resurrected by exactly that
/// mechanism](../../kb/chromium/resurrection.md).
/// </para>
/// </remarks>
internal static partial class RestartRegistration
{
    /// <summary><c>HRESULT_FROM_WIN32(ERROR_NOT_FOUND)</c> — no registration exists.</summary>
    public const int NotRegistered = unchecked((int)0x80070490);

    private const uint ProcessQueryInformation = 0x00000400;
    private const uint ProcessVmRead = 0x00000010;

    /// <summary>Asks Windows what a live process has registered.</summary>
    /// <param name="processId">The pid, which the caller must know is still alive.</param>
    /// <returns>
    /// The raw <c>HRESULT</c>. <see cref="NotRegistered"/> is the answer this
    /// project requires; <c>S_OK</c> means a registration exists.
    /// </returns>
    /// <exception cref="InvalidOperationException">The process could not be opened.</exception>
    public static int Of(int processId)
    {
        // PROCESS_VM_READ as well as QUERY_INFORMATION: the command line comes
        // out of the target's address space, and without it the call fails with
        // a handle error that reads exactly like "not registered".
        using var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            throw new InvalidOperationException(
                $"Process {processId} could not be opened to read its restart registration (Win32 {Marshal.GetLastPInvokeError()}). A closed answer is not the same as 'not registered'.");
        }

        uint size = 0;
        var result = GetApplicationRestartSettings(handle, nint.Zero, ref size, out _);

        GC.KeepAlive(handle);
        return result;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial int GetApplicationRestartSettings(
        SafeProcessHandle hProcess,
        nint pwzCommandline,
        ref uint pcchSize,
        out uint pdwFlags);
}
