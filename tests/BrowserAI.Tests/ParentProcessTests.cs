// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// The harness's read of who started a process, for a process that has gone.
/// </summary>
internal sealed class ParentProcessTests
{
    /// <summary>
    /// The parent of a process that has exited and been let go of reads as gone, and the
    /// parent of one that runs reads as its parent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-10-11</b>, after the first PowerShell run of the release gate at
    /// <c>1a0c0cf9</c> went red in
    /// <c>FirefoxTests.ThePreflightRefusesAHeldProfileBeforeAnyFirefoxOrWindowExists</c> on
    /// <i>"Win32Exception: Could not open process 98796 to read its parent."</i>, from
    /// <see cref="ParentProcess.IdOf"/>. That arm lists every Firefox on the machine that
    /// runs from the provisioned browsers root and then reads each one's parent, and a
    /// Firefox another arm started can exit in between.
    /// </para>
    /// <para>
    /// <b>The child is waited for until no handle holds it</b>, the way
    /// <c>JobContainmentTests.AWalkedPidThatVanishedBeforeTheQueryIsExitedAndAnyOtherFailureIsStillAFailure</c>
    /// waits for its own: until then Windows still opens it and answers its parent. That
    /// wait is a hang detector, <see cref="TestDefaults.ProcessHang"/>, and not a
    /// promptness claim. The process that runs is this host, so the control reads a parent
    /// that is there.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AProcessThatHasExitedReadsAsGoneAndOneThatRunsReadsAsItsParent()
    {
        int gonePid;

        using (var transient = Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            "/c exit")
        {
            UseShellExecute = false,

            // Redirecting the streams does not suppress the console, and this
            // redirects nothing: the house rule for every launch in this tree.
            CreateNoWindow = true,
        })!)
        {
            gonePid = transient.Id;
            await transient.WaitForExitAsync();
        }

        var deadline = Stopwatch.StartNew();

        while (ProcessIdentity.OpenProcessErrorFor(gonePid) is 0 && deadline.Elapsed < TestDefaults.ProcessHang)
        {
            await Task.Delay(25);
        }

        // Today this throws that Win32Exception, from IdOf.
        await Assert.That(ParentProcess.IdOfUnlessGone(gonePid)).IsNull()
            .Because("a process that has exited and been let go of has no parent left to read");

        await Assert.That(ParentProcess.IdOfUnlessGone(Environment.ProcessId)).IsEqualTo(ParentProcess.IdOf(Environment.ProcessId))
            .Because("a read that answers gone for everything proves nothing about the process that has gone");
    }
}
