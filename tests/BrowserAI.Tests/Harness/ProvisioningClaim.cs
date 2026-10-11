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
/// not a seam we added -- it is the product's own cross-process contract.</b> A
/// BrowserAI that cannot take the mutex does not queue a second download of the
/// same 203.8 MB; it watches for the marker the holder is about to write
/// (<see cref="BrowserProvisioner"/>). So a test that takes the mutex first and
/// then fills the directory itself drives exactly the path a second BrowserAI
/// process would drive -- with no product change, no environment override and no
/// installer substitute.
/// </para>
/// <para>
/// ⚠️ <b>The mutex is held on a thread of its own, and that is a correctness
/// requirement and not a style.</b> A named mutex is owned by the
/// <i>thread</i> that waited on it; a test that acquired one and then awaited
/// anything would release it from whichever pool thread the continuation landed
/// on, and <c>ReleaseMutex</c> would throw about "an unsynchronized block of
/// code" -- naming nothing relevant. <b>The thread is the product's own since
/// 2026-10-11</b>: <see cref="MutexHold"/>, which the claim awaits.
/// <i>Corrected that day (previously a holder thread of this class's own, waited
/// for with a blocking wait on its start and a join on its end).</i>
/// </para>
/// <para>
/// <b><see cref="Held"/> is not decoration.</b> A claim that silently failed
/// would let BrowserAI take the mutex and download 203.8 MB while the test
/// believed it was reading a cache, so the caller asserts it and does not
/// assume it.
/// </para>
/// </remarks>
internal sealed class ProvisioningClaim : IAsyncDisposable
{
    /// <summary>
    /// The claim's own tripwire, matched to the provisioner's absolute cap.
    /// </summary>
    /// <remarks>
    /// It exists so a test that dies without disposing cannot leave a
    /// machine-wide object held by a background thread for the life of the test
    /// host. It should never fire; if it does, the run is already broken.
    /// </remarks>
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(45);

    private readonly MachineMutex _mutex;
    private readonly MutexHold _hold;
    private readonly CancellationTokenSource _disposing = new();
    private readonly Task _ceiling;
    private int _disposed;

    private ProvisioningClaim(string name, MachineMutex mutex, MutexHold hold)
    {
        MutexName = name;
        _mutex = mutex;
        _hold = hold;
        _ceiling = ReleaseAtTheCeilingAsync();
    }

    /// <summary>The kernel object's name, for a failure message to quote.</summary>
    public string MutexName { get; }

    /// <summary>Whether the mutex was actually acquired.</summary>
    public bool Held => _hold.Held;

    /// <summary>
    /// Takes the claim a second BrowserAI process installing into
    /// <paramref name="browsersDirectory"/> would take.
    /// </summary>
    /// <param name="browsersDirectory">The absolute browsers root, which is half the mutex's name.</param>
    /// <param name="browser">The family, which is the other half.</param>
    /// <returns>The claim, held until it is disposed.</returns>
    public static async Task<ProvisioningClaim> TakeAsync(string browsersDirectory, string browser)
    {
        var name = BrowserProvisioner.MutexNameFor(browsersDirectory, browser);
        var mutex = MachineMutex.Create(name);

        try
        {
            return new ProvisioningClaim(name, mutex, await mutex.HoldAsync(TestDefaults.ProcessHang));
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>Once, however often it is called</b>: an arm lets the claim go part-way through
    /// and its <c>await using</c> disposes it again on the way out. <i>Corrected
    /// 2026-10-11 (previously a second call cancelled a disposed source and threw)</i>,
    /// found by <c>ProvisioningTests.AHolderThatLetsGoWithoutInstallingIsOvertakenRatherThanWaitedOnForever</c>
    /// in the first gate after the conversion.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
        {
            return;
        }

        await _disposing.CancelAsync();
        await _ceiling;
        await _hold.DisposeAsync();
        _mutex.Dispose();
        _disposing.Dispose();
    }

    /// <summary>Lets the mutex go once the ceiling passes, unless the claim was disposed first.</summary>
    /// <returns>The wait.</returns>
    private async Task ReleaseAtTheCeilingAsync()
    {
        try
        {
            await Task.Delay(Ceiling, _disposing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await _hold.DisposeAsync();
    }
}
