// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using BrowserAI.Tests.Harness;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Tests;

/// <summary>
/// What the restart registration reader answers for a process that runs, for one that
/// has exited while a handle to it is still open, for one that has exited and that
/// nothing holds any more, and for one that refuses to be opened.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-11, after the first gate of that day after the maintainer's
/// decision 54</b>: <c>BrowserContainmentTests.AChromiumTreeIsContainedAndItsProfileDeletesCleanly</c>
/// went red at async-ps-1 on <i>"pid 79676 answered 0x8007012B rather than
/// ERROR_NOT_FOUND"</i>, for a Chromium process of the walk that had exited by the time
/// it was asked. <c>GetApplicationRestartSettings</c> reads the registration out of the
/// process's address space, and an exited process's is gone: measured that day, a
/// <c>cmd.exe</c> that had exited with a handle to it still open answered 0x8007012B,
/// <c>HRESULT_FROM_WIN32(ERROR_PARTIAL_COPY)</c>, and one that ran answered 0x80070490.
/// </para>
/// <para>
/// <b>The exited probe is held by the arm's job</b>, whose launch keeps the process
/// handle, so its pid still opens after it has gone, as a walked browser's did. The
/// running one has written its ready file before it is asked, so it is past its start.
/// </para>
/// </remarks>
internal sealed partial class RestartRegistrationTests
{
    /// <summary>The test probe, which plays every process.</summary>
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    /// <summary><c>ERROR_INVALID_PARAMETER</c>, what <c>OpenProcess</c> answers for a pid no process has.</summary>
    private const int InvalidParameter = 87;

    /// <summary>How many probes the arm lets go of before it gives up finding a pid nobody has taken again.</summary>
    /// <remarks>
    /// A pid Windows hands to a new process between the arm's release and its first
    /// question is told by its creation time; the arm then starts again with a new probe
    /// and never reads the stranger's registration.
    /// </remarks>
    private const int LetGoAttempts = 5;

    /// <summary>
    /// A process that has exited reads as gone, and one that runs, registered for
    /// nothing, reads as not registered.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProcessThatHasExitedReadsAsGoneAndOneThatRunsReadsAsNotRegistered()
    {
        using var work = await ScratchDirectory.CreateAsync("restart-registration");
        using var scope = new JobObjectScope();

        var exited = await scope.LaunchAsync(ProbePath, work.Path, "say", "an exited probe");

        await Assert.That(await exited.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();
        await Assert.That(RestartRegistration.Of(exited.Id)).IsEqualTo(RestartRegistration.Gone);

        var ready = Path.Combine(work.Path, "ready.json");
        var running = await scope.LaunchAsync(ProbePath, work.Path, "hold-file", Path.Combine(work.Path, "held.txt"), ready);

        await Assert.That(await AppearsWithinAsync(ready, TestDefaults.ProcessHang)).IsTrue()
            .Because($"the holding probe never wrote its ready file. It said: {await scope.SaidByAsync(running.Id)}");
        await Assert.That(RestartRegistration.Of(running.Id)).IsEqualTo(RestartRegistration.NotRegistered);
    }

    /// <summary>
    /// A process that has exited and that nothing holds any more reads as gone, and one that
    /// runs and refuses to be opened is still refused, and not read as gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-14, after the gate at <c>9c3466a5</c></b>, in whose first PowerShell
    /// half <c>BrowserContainmentTests.AChromiumTreeIsContainedAndItsProfileDeletesCleanly</c>
    /// went red on <i>"Process 88504 could not be opened to read its restart registration
    /// (Win32 87)"</i>. The arm above holds a process that exited while a handle to it was still
    /// open. The walked browser's launcher lets its handles go once its report is written, and
    /// once the last handle to an exited process closes, its pid opens no process at all:
    /// <c>OpenProcess</c> answers <c>ERROR_INVALID_PARAMETER</c>, measured 2026-10-14
    /// ([kb](../../kb/chromium/resurrection.md)).
    /// </para>
    /// <para>
    /// <b>The order is forced and not hoped for.</b> The probe has exited, the arm lets go of
    /// its job and with it of every handle it held, and it asks the reader only once an open
    /// of the pid answers <c>ERROR_INVALID_PARAMETER</c>. A pid that still opens is the
    /// probe's while its creation time is the probe's, and a stranger's once it is not, and
    /// then the arm starts again with a new probe.
    /// </para>
    /// <para>
    /// <b>The refused process is one of the arm's own</b>: a probe that runs, whose access
    /// control list the arm empties through a handle it opens for that alone, so that an open
    /// of it is refused with <c>ERROR_ACCESS_DENIED</c>, the answer
    /// <see href="https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess">OpenProcess</see>
    /// documents for the System process. The job's handle was granted before the list was
    /// emptied, so closing the job still ends the probe.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProcessThatHasExitedAndIsHeldByNothingReadsAsGoneAndOneThatRefusesIsStillRefused()
    {
        using var work = await ScratchDirectory.CreateAsync("restart-registration-let-go");

        var (letGo, how) = await LetGoOfAnExitedProbeAsync(work.Path);

        await Assert.That(letGo).IsNotNull().Because(how);
        await Assert.That(RestartRegistration.Of(letGo!.Value)).IsEqualTo(RestartRegistration.Gone).Because(how);

        using var scope = new JobObjectScope();

        var ready = Path.Combine(work.Path, "ready-refusing.json");
        var refusing = await scope.LaunchAsync(ProbePath, work.Path, "hold-file", Path.Combine(work.Path, "held-refusing.txt"), ready);

        await Assert.That(await AppearsWithinAsync(ready, TestDefaults.ProcessHang)).IsTrue()
            .Because($"the probe to be refused never wrote its ready file. It said: {await scope.SaidByAsync(refusing.Id)}");

        DenyEveryOpen(refusing.Id);

        var refused = Assert.Throws<InvalidOperationException>(() => _ = RestartRegistration.Of(refusing.Id));

        await Assert.That(refused.Message).Contains("(Win32 5)");
    }

    /// <summary>
    /// Starts a probe that exits at once, lets go of every handle this arm holds to it, and
    /// waits until its pid opens no process.
    /// </summary>
    /// <param name="directory">The probe's working directory.</param>
    /// <returns>The pid, or none, with what the arm found.</returns>
    private static async Task<(int? ProcessId, string How)> LetGoOfAnExitedProbeAsync(string directory)
    {
        var found = new List<string>();

        for (var attempt = 1; attempt <= LetGoAttempts; attempt++)
        {
            int pid;
            long created;

            using (var scope = new JobObjectScope())
            {
                var probe = await scope.LaunchAsync(ProbePath, directory, "say", $"a probe let go, attempt {attempt}");

                if (!await probe.WaitForExitAsync(TestDefaults.ProcessHang))
                {
                    return (null, $"attempt {attempt}: the probe never exited. It said: {await scope.SaidByAsync(probe.Id)}");
                }

                pid = probe.Id;

                // Read while the job's handle still holds the exited process, so that its pid
                // still opens.
                created = ProcessIdentity.CreationTimeOf(pid);
            }

            // Every handle this arm held to the probe closed with the job. Any other holder's
            // goes in milliseconds, measured on 2026-10-14.
            var instead = await UntilNoProcessHasThePidAsync(pid, created, TestDefaults.ProcessHang);

            if (instead is null)
            {
                return (pid, $"pid {pid}, created {created}, exited and let go: an open of it answers ERROR_INVALID_PARAMETER (attempt {attempt})");
            }

            found.Add($"attempt {attempt}: pid {pid}, created {created}: {instead}");
        }

        return (null, string.Join(Environment.NewLine, found));
    }

    /// <summary>Waits until an open of a pid answers <c>ERROR_INVALID_PARAMETER</c>.</summary>
    /// <param name="processId">The exited probe's pid.</param>
    /// <param name="createdFileTime">Its creation time.</param>
    /// <param name="patience">The hang detector.</param>
    /// <returns>Nothing once no process has the pid, or what the pid named instead.</returns>
    private static async Task<string?> UntilNoProcessHasThePidAsync(int processId, long createdFileTime, TimeSpan patience)
    {
        using var deadline = new CancellationTokenSource(patience);

        while (true)
        {
            var error = ProcessIdentity.OpenProcessErrorFor(processId);

            if (error is InvalidParameter)
            {
                return null;
            }

            if (error is not 0)
            {
                return $"an open of the pid answered Win32 {error}, so another process has it";
            }

            long? creation = null;

            try
            {
                creation = ProcessIdentity.CreationTimeOf(processId);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Gone between the two opens. The next open says so.
            }

            if (creation is { } other && other != createdFileTime)
            {
                return $"the pid names a process created at {other}, which is not the probe";
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), deadline.Token);
            }
            catch (OperationCanceledException)
            {
                return $"the pid still opened the probe after {patience}";
            }
        }
    }

    /// <summary>
    /// Empties a process's access control list, so that no open of it is granted but the two
    /// rights its owner holds by default.
    /// </summary>
    /// <param name="processId">A process this arm started.</param>
    /// <exception cref="InvalidOperationException">The list could not be emptied.</exception>
    private static void DenyEveryOpen(int processId)
    {
        using var handle = OpenProcess(WriteDac, bInheritHandle: false, (uint)processId);

        if (handle.IsInvalid)
        {
            throw new InvalidOperationException($"Process {processId} could not be opened to change its access control list (Win32 {Marshal.GetLastPInvokeError()}).");
        }

        // An ACL with no entry: revision 2, eight bytes, no ACE.
        byte[] empty = [2, 0, 8, 0, 0, 0, 0, 0];

        var result = SetSecurityInfo(handle, SeKernelObject, DaclSecurityInformation, nint.Zero, nint.Zero, empty, nint.Zero);

        if (result is not 0)
        {
            throw new InvalidOperationException($"Process {processId}'s access control list could not be emptied (Win32 {result}).");
        }
    }

    /// <summary><c>WRITE_DAC</c>.</summary>
    private const uint WriteDac = 0x00040000;

    /// <summary><c>SE_KERNEL_OBJECT</c>.</summary>
    private const int SeKernelObject = 6;

    /// <summary><c>DACL_SECURITY_INFORMATION</c>.</summary>
    private const uint DaclSecurityInformation = 0x00000004;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("advapi32.dll")]
    private static partial uint SetSecurityInfo(
        SafeProcessHandle handle,
        int objectType,
        uint securityInfo,
        nint psidOwner,
        nint psidGroup,
        byte[] pDacl,
        nint pSacl);

    /// <summary>Whether a file appears within a bound, read without parking a thread.</summary>
    /// <param name="path">The file.</param>
    /// <param name="patience">The hang detector.</param>
    /// <returns>Whether it appeared.</returns>
    private static async Task<bool> AppearsWithinAsync(string path, TimeSpan patience)
    {
        using var deadline = new CancellationTokenSource(patience);

        while (!File.Exists(path))
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
            }
            catch (OperationCanceledException)
            {
                return File.Exists(path);
            }
        }

        return true;
    }
}
