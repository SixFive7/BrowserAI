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
/// <b>Asked of the running process and not inferred from its command
/// line.</b> The charter's resurrection article rests on Playwright's browser
/// command line overshooting <c>RegisterApplicationRestart</c>'s 1023-character
/// limit by 531 or more, which means the registration <i>fails</i> and Windows
/// does not resurrect the browser after a reboot or an update. That is an
/// argument about a length; this is the observation that settles it, and the two
/// can disagree -- a shorter command line, or an upstream that starts trimming its
/// argument list, would flip the answer with nothing else changing.
/// </para>
/// <para>
/// <b><c>0x80070490</c> is the healthy answer.</b> It is
/// <c>HRESULT_FROM_WIN32(ERROR_NOT_FOUND)</c>: the process has no registration at
/// all. Anything else means Windows has been told to bring this browser back, and
/// [the maintainer's own browsers were resurrected by exactly that
/// mechanism](../../../kb/chromium/resurrection.md).
/// </para>
/// </remarks>
internal static partial class RestartRegistration
{
    /// <summary><c>HRESULT_FROM_WIN32(ERROR_NOT_FOUND)</c> -- no registration exists.</summary>
    public const int NotRegistered = unchecked((int)0x80070490);

    /// <summary>
    /// No answer: the process had exited by the time it was asked, so there is no address
    /// space to read a registration out of, and once nothing holds it no process to open.
    /// A value no <c>HRESULT</c> this call returns can take.
    /// </summary>
    public const int Gone = int.MaxValue;

    private const uint ProcessQueryInformation = 0x00000400;
    private const uint ProcessVmRead = 0x00000010;

    /// <summary><c>ERROR_INVALID_PARAMETER</c>, what <c>OpenProcess</c> answers for a pid no process has.</summary>
    private const int InvalidParameter = 87;

    /// <summary>Asks Windows what a live process has registered.</summary>
    /// <param name="processId">The pid, which the caller must know is still alive.</param>
    /// <returns>
    /// The raw <c>HRESULT</c>. <see cref="NotRegistered"/> is the answer this
    /// project requires; <c>S_OK</c> means a registration exists; and
    /// <see cref="Gone"/> when the process had exited by the time it was asked, which
    /// the call itself answers as <c>HRESULT_FROM_WIN32(ERROR_PARTIAL_COPY)</c>, read out
    /// of an address space that is gone, and which <c>OpenProcess</c> answers with
    /// <c>ERROR_INVALID_PARAMETER</c> once nothing holds the process any more. <i>Gone
    /// since 2026-10-11 (previously that raw answer, which an arm read as a registration),
    /// and for a process nothing holds since 2026-10-14 (previously the exception
    /// below).</i>
    /// </returns>
    /// <exception cref="InvalidOperationException">The process is there and could not be opened.</exception>
    public static int Of(int processId)
    {
        // PROCESS_VM_READ as well as QUERY_INFORMATION: the command line comes
        // out of the target's address space, and without it the call fails with
        // a handle error that reads exactly like "not registered".
        using var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();

            // An exited process nothing holds is no process at all: its pid opens nothing,
            // and OpenProcess answers ERROR_INVALID_PARAMETER, measured 2026-10-14
            // (kb/chromium/resurrection.md). Any other refusal comes from a process that is
            // there, and it is still no answer.
            if (error is InvalidParameter)
            {
                return Gone;
            }

            throw new InvalidOperationException(
                $"Process {processId} could not be opened to read its restart registration (Win32 {error}). A closed answer is not the same as 'not registered'.");
        }

        uint size = 0;
        var result = GetApplicationRestartSettings(handle, nint.Zero, ref size, out _);

        // Asked of the same handle, so the answer and the exit are about one process.
        var gone = result == PartialCopy && GetExitCodeProcess(handle, out var exitCode) && exitCode != StillActive;

        GC.KeepAlive(handle);
        return gone ? Gone : result;
    }

    /// <summary><c>HRESULT_FROM_WIN32(ERROR_PARTIAL_COPY)</c>, what an exited process answers.</summary>
    private const int PartialCopy = unchecked((int)0x8007012B);

    /// <summary><c>STILL_ACTIVE</c>, the exit code of a process that runs.</summary>
    private const uint StillActive = 259;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeProcessHandle hProcess, out uint lpExitCode);

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
