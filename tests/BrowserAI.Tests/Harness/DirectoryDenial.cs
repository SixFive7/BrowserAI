// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Security.AccessControl;
using System.Security.Principal;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Denies the current user one right on a directory, so that a failure the
/// product must <b>report</b> can be provoked without a seam in the product.
/// </summary>
/// <remarks>
/// <para>
/// <b>Extracted 2026-08-19 from <c>SessionLockTests</c>, which invented it.</b>
/// It is the only mechanism in this suite that produces a real
/// <see cref="UnauthorizedAccessException"/> from a real filesystem, and two
/// separate tests now need one: the write-landed/write-refused pair, and the
/// permanently-denied <c>lock.json</c> that used to escape
/// <c>SessionLock.TryAcquire</c> as an exception. A second copy of an ACL
/// manipulation is a second thing that can leave a scratch tree undeletable.
/// </para>
/// <para>
/// ⚠️ <b>Every denial must be taken back off before teardown</b>, which is why
/// this hands back a disposable rather than a rule: a scratch directory whose
/// files cannot be read cannot be enumerated either, so a leaked denial does not
/// fail the test that made it — it fails whatever runs next.
/// </para>
/// <para>
/// <b>The rights are chosen for what the two operations do NOT share.</b>
/// <c>WriteDurably</c> creates its temp file <c>FileAccess.Write</c> and renames
/// it into place, needing no <c>FILE_READ_DATA</c>; every open that intends to
/// keep the file asks for <c>FileAccess.ReadWrite</c> and does. So denying
/// <c>ReadData</c> on the files inside a session directory refuses exactly the
/// opens that read, deterministically, while denying <c>CreateFiles</c> on the
/// directory itself stops the temp file ever existing.
/// </para>
/// </remarks>
internal sealed class DirectoryDenial : IDisposable
{
    private readonly DirectoryInfo _directory;
    private readonly FileSystemAccessRule _rule;
    private int _disposed;

    private DirectoryDenial(DirectoryInfo directory, FileSystemAccessRule rule)
    {
        _directory = directory;
        _rule = rule;
    }

    /// <summary>Denies one right and returns the handle that takes it back off.</summary>
    /// <param name="directory">The directory to deny on.</param>
    /// <param name="right">The one right to deny.</param>
    /// <param name="inheritance">Whether the denial reaches objects inside.</param>
    /// <param name="propagation">Whether it applies to the directory itself.</param>
    /// <returns>The denial, to dispose.</returns>
    public static DirectoryDenial Apply(
        string directory,
        FileSystemRights right,
        InheritanceFlags inheritance,
        PropagationFlags propagation)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var info = new DirectoryInfo(directory);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(identity.User!, right, inheritance, propagation, AccessControlType.Deny);

        security.AddAccessRule(rule);
        info.SetAccessControl(security);

        return new DirectoryDenial(info, rule);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        var security = _directory.GetAccessControl();

        _ = security.RemoveAccessRule(_rule);
        _directory.SetAccessControl(security);
    }
}
