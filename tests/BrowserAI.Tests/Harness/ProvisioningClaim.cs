// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;
using BrowserAI.Sessions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Holds a browsers root's machine-wide provisioning mutex, the way another
/// BrowserAI process part-way through an install holds it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only seam into a published binary's provisioning, and it is
/// not a seam we added — it is the product's own cross-process contract.</b> A
/// BrowserAI that cannot take the mutex does not queue a second download of the
/// same 203.8 MB; it watches for the marker the holder is about to write
/// (<see cref="BrowserProvisioner"/>). So a test that takes the mutex first and
/// then fills the directory itself drives exactly the path a second BrowserAI
/// process would drive — with no product change, no environment override and no
/// installer substitute.
/// </para>
/// <para>
/// ⚠️ <b>The mutex is held on a thread of its own, and that is a correctness
/// requirement rather than a style.</b> A named mutex is owned by the
/// <i>thread</i> that waited on it; a test that acquired one and then awaited
/// anything would release it from whichever pool thread the continuation landed
/// on, and <c>ReleaseMutex</c> would throw about "an unsynchronized block of
/// code" — naming nothing relevant. The same pattern, for the same reason, is in
/// <c>RevisionPruneTests.NothingIsPrunedWhileAnotherProcessIsProvisioning</c>.
/// </para>
/// <para>
/// <b><see cref="Held"/> is not decoration.</b> A claim that silently failed
/// would let BrowserAI take the mutex and download 203.8 MB while the test
/// believed it was reading a cache, so the caller asserts it rather than
/// assuming it.
/// </para>
/// </remarks>
internal sealed class ProvisioningClaim : IDisposable
{
    /// <summary>
    /// The holder thread's own tripwire, matched to the provisioner's absolute
    /// cap.
    /// </summary>
    /// <remarks>
    /// It exists so a test that dies without disposing cannot leave a
    /// machine-wide object held by a background thread for the life of the test
    /// host. It should never fire; if it does, the run is already broken.
    /// </remarks>
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(45);

    private readonly ManualResetEventSlim _taken = new();
    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _holder;

    private ProvisioningClaim(string name)
    {
        MutexName = name;

        _holder = new Thread(() =>
        {
            using var mutex = MachineMutex.Create(name);

            Held = mutex.Acquire(TestDefaults.ProcessHang) is not MutexAcquisition.NotAcquired;
            _taken.Set();

            _ = _release.Wait(Ceiling);

            if (Held)
            {
                mutex.Release();
            }
        })
        {
            IsBackground = true,
            Name = "browserai-provisioning-claim",
        };

        _holder.Start();
        _ = _taken.Wait(TestDefaults.ProcessHang);
    }

    /// <summary>The kernel object's name, for a failure message to quote.</summary>
    public string MutexName { get; }

    /// <summary>Whether the mutex was actually acquired.</summary>
    public bool Held { get; private set; }

    /// <summary>
    /// Takes the claim a second BrowserAI process installing into
    /// <paramref name="browsersDirectory"/> would take.
    /// </summary>
    /// <param name="browsersDirectory">The absolute browsers root, which is half the mutex's name.</param>
    /// <param name="browser">The family, which is the other half.</param>
    /// <returns>The claim, held until it is disposed.</returns>
    public static ProvisioningClaim Take(string browsersDirectory, string browser) =>
        new(BrowserProvisioner.MutexNameFor(browsersDirectory, browser));

    /// <inheritdoc />
    public void Dispose()
    {
        _release.Set();
        _ = _holder.Join(TestDefaults.ProcessHang);
        _release.Dispose();
        _taken.Dispose();
    }
}
