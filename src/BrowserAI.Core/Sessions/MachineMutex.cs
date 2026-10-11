// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>How a wait on a <see cref="MachineMutex"/> ended.</summary>
internal enum MutexAcquisition
{
    /// <summary>The timeout passed and somebody else still holds it.</summary>
    NotAcquired,

    /// <summary>Held, by the hold's owner thread. Dispose the hold to release it.</summary>
    Acquired,

    /// <summary>
    /// <b>Held, by the hold's owner thread</b>, and the previous holder died
    /// without releasing.
    /// </summary>
    /// <remarks>
    /// This is the outcome that has to be a separate value and not folded
    /// into <see cref="Acquired"/>. <c>AbandonedMutexException</c> means the
    /// wait <i>succeeded</i>; what it reports is that whatever the dead holder
    /// was part-way through writing may be torn, which is the only warning the
    /// OS ever gives about the protected state. Folding it into success discards
    /// that warning; letting it escape disables locking permanently after the
    /// first crash and nothing reports it (race <b>R3</b>).
    /// </remarks>
    AcquiredAbandoned,
}

/// <summary>
/// A machine-wide named mutex, <c>Global\</c> only, with no fallback.
/// </summary>
/// <remarks>
/// <para>
/// <b>If the machine-wide object cannot be created there is no lock, and
/// therefore no session.</b> <see cref="Create"/> lets the failure out and
/// does not retry under <c>Local\</c>, and the caller turns that into a hard
/// blocker whose reason reaches the calling model. The prior art on this machine
/// does fall back, deliberately and visibly, because a degraded rig beats an
/// unusable one; BrowserAI refuses because a degraded lock is indistinguishable
/// from a working one at exactly the moment it matters -- two logon sessions get
/// two distinct kernel objects for one directory, neither can see the other, and
/// both report success while a browser profile is opened twice.
/// </para>
/// <para>
/// <b>A named mutex is owned by the <i>thread</i> that waited on it.</b> A
/// continuation resuming on a different pool thread makes the release throw an
/// <c>ApplicationException</c> about "an unsynchronized block of code", which
/// names nothing relevant and points nowhere near the cause.
/// </para>
/// <para>
/// ⚠️ <b>So a hold is a thread's, and never a caller's, since 2026-10-11</b>, the
/// maintainer's decision 54: <see cref="HoldAsync"/> takes the mutex on a thread of
/// its own, which keeps it until the hold is disposed, and the section inside the
/// hold awaits whatever it likes. <i>Corrected 2026-10-11 (previously "Do not
/// <c>await</c> across a critical section guarded by one of these ... Every caller
/// here is synchronous for that reason, not by accident.")</i>
/// </para>
/// </remarks>
internal sealed class MachineMutex : IDisposable
{
    private readonly Mutex _mutex;

    private MachineMutex(Mutex mutex, string name)
    {
        _mutex = mutex;
        Name = name;
    }

    /// <summary>The kernel object's name, always <c>Global\</c> prefixed.</summary>
    public string Name { get; }

    /// <summary>
    /// The timeout the most recent <see cref="HoldAsync"/> was handed, recorded by
    /// the hold itself. <see langword="null"/> until one has happened. <i>Corrected
    /// 2026-10-11 (previously "the most recent <c>Acquire</c> ... recorded by the acquire
    /// itself").</i>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists so that "this call did not wait" can be asserted without a
    /// clock.</b> A <see cref="LockScopes.NeverWaits"/> acquire and a
    /// five-second one that expired both return
    /// <see cref="MutexAcquisition.NotAcquired"/>, so the return value cannot
    /// tell them apart and the only other witness is elapsed wall time -- which
    /// on a loaded machine measures the scheduler, not the lock.
    /// <c>UpdateTests.AReclaimWhosePeerHoldsTheGateSkipsAtOnceAndRemovesNothing</c>
    /// bounded <c>Stopwatch.Elapsed</c> by five seconds and
    /// <b>measured 5.2 s</b> on 2026-08-20 for a zero-timeout acquire, on a
    /// machine whose suite duration varied threefold from external load.
    /// </para>
    /// <para>
    /// ⚠️ <b>What it proves is the argument, not the absence of a delay.</b> It
    /// is set from the value passed in, so an edit that started passing a real
    /// timeout is caught and an edit that slept beside the acquire is not.
    /// That is a weaker claim than a clock would make if a clock could be
    /// trusted here, and it is stated, not glossed, at the one assertion
    /// that rests on it.
    /// </para>
    /// </remarks>
    public TimeSpan? LastAcquireTimeout { get; private set; }

    /// <summary>Creates or opens the named mutex.</summary>
    /// <param name="name">A <c>Global\</c>-prefixed name.</param>
    /// <returns>The mutex.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not <c>Global\</c> prefixed.</exception>
    /// <exception cref="UnauthorizedAccessException">An object of that name exists and this token cannot open it.</exception>
    /// <exception cref="WaitHandleCannotBeOpenedException">The name is taken by a different kind of object.</exception>
    /// <exception cref="IOException">The object manager refused the name.</exception>
    /// <exception cref="NotSupportedException">The name cannot be created on this platform.</exception>
    /// <remarks>
    /// It <b>throws</b> instead of returning a null-and-reason pair, and the
    /// caller turns the throw into a blocker with the reason in it. There is no
    /// second attempt under any other prefix, here or anywhere else in the
    /// product: the four exception types above are the ways
    /// <c>SeCreateGlobalPrivilege</c> and the object manager say no, and every
    /// one of them means <i>there is no lock</i>.
    /// </remarks>
    public static MachineMutex Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!name.StartsWith(LockScopes.GlobalPrefix, StringComparison.Ordinal))
        {
            // A programming error, not an environment failure, so it
            // throws a different type: the whole point of this class is that the
            // scope is never negotiable, so a caller cannot report this one as a
            // blocker and carry on.
            throw new ArgumentException(
                $"'{name}' is not a Global\\ name. Every named object BrowserAI creates is machine-wide; there is no Local\\ fallback and adding one would silently give two logon sessions two different locks for one directory.",
                nameof(name));
        }

        var mutex = new Mutex(initiallyOwned: false, name, out _);

        try
        {
            return new MachineMutex(mutex, name);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Takes the mutex on a thread of its own, for at most <paramref name="timeout"/>, and
    /// holds it there until the hold is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The thread is the mutex's own, and it is one of the two places in the tree that
    /// keep a wait, listed for the maintainer's review</b> under his decision 54, in his words
    /// verbatim: <i>"While scanning if you ever deem there to be any good reason for it in
    /// this goodbase I want to review your reasoning."</i> A Win32 mutex is owned by the
    /// thread that waited on it, and only that thread can release it: a continuation that
    /// resumes on another pool thread makes the release throw. So a hold that spans an
    /// await, which every hold does once the file work inside it is asynchronous, needs a
    /// thread that takes the mutex, waits until the hold ends and releases it. That thread
    /// is created for the hold and ends with it, and no pool worker waits for any of it.
    /// </para>
    /// <para>
    /// <i>Corrected 2026-10-11 (previously <c>Acquire(timeout)</c> and <c>Release()</c>, called
    /// on the caller's own thread, which held the section synchronous: "Every caller here is
    /// synchronous for that reason, not by accident")</i>. Two alternatives were weighed
    /// and not taken: a lock file opened with no sharing, which an older build on the
    /// machine, the installed one among them, would not see; and a section with no wait
    /// inside it, which the asynchronous file work inside every gate rules out.
    /// </para>
    /// </remarks>
    /// <param name="timeout">
    /// <see cref="LockScopes.NeverWaits"/> for anything a caller can reason
    /// about; <see cref="LockScopes.PerDirectoryGate"/> for the internal
    /// create-or-take section.
    /// </param>
    /// <returns>The hold, which says whether it was acquired and whether it was abandoned.</returns>
    public Task<MutexHold> HoldAsync(TimeSpan timeout)
    {
        // Recorded BEFORE the wait, so it is set on every path out of this
        // method -- acquired, expired, or abandoned. See LastAcquireTimeout for
        // what it is for and what it deliberately does not prove.
        LastAcquireTimeout = timeout;

        return MutexHold.TakeAsync(_mutex, timeout, Name);
    }

    /// <inheritdoc />
    public void Dispose() => _mutex.Dispose();
}

/// <summary>
/// A machine-wide mutex held by a thread of its own until this is disposed.
/// </summary>
/// <remarks>
/// <b>Dispose it with <see cref="DisposeAsync"/></b>, which asks the owner thread to release
/// the mutex and returns once it has, so whatever follows the hold runs after the release. A
/// hold that was not acquired holds nothing, and disposing it does nothing.
/// </remarks>
internal sealed class MutexHold : IAsyncDisposable
{
    private readonly ManualResetEvent? _release;
    private readonly Task _released;
    private int _disposed;

    private MutexHold(MutexAcquisition acquisition, ManualResetEvent? release, Task released)
    {
        Acquisition = acquisition;
        _release = release;
        _released = released;
    }

    /// <summary>How the wait for the mutex ended.</summary>
    public MutexAcquisition Acquisition { get; }

    /// <summary>Whether the mutex is held: acquired, abandoned or not.</summary>
    public bool Held => Acquisition is not MutexAcquisition.NotAcquired;

    /// <summary>Starts the owner thread and returns once its wait for the mutex has ended.</summary>
    /// <param name="mutex">The mutex.</param>
    /// <param name="timeout">How long the owner thread waits for it.</param>
    /// <param name="name">Its name, for the owner thread's own.</param>
    /// <returns>The hold.</returns>
    internal static async Task<MutexHold> TakeAsync(Mutex mutex, TimeSpan timeout, string name)
    {
        var acquired = new TaskCompletionSource<MutexAcquisition>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEvent(initialState: false);

        var owner = new Thread(() =>
        {
            MutexAcquisition acquisition;

            try
            {
                // KEPT, for the maintainer's review: the owner thread's wait for its mutex.
                acquisition = mutex.WaitOne(timeout) ? MutexAcquisition.Acquired : MutexAcquisition.NotAcquired;
            }
            catch (AbandonedMutexException)
            {
                // The wait SUCCEEDED. This thread owns the mutex now and must
                // release it exactly as if the wait had returned true.
                acquisition = MutexAcquisition.AcquiredAbandoned;
            }
#pragma warning disable CA1031 // The owner thread's boundary: the failure goes to the caller, who awaits it.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                _ = acquired.TrySetException(failure);
                _ = released.TrySetResult();
                return;
            }

            _ = acquired.TrySetResult(acquisition);

            if (acquisition is MutexAcquisition.NotAcquired)
            {
                _ = released.TrySetResult();
                return;
            }

            try
            {
                // KEPT, for the maintainer's review: the owner thread parks until the hold
                // ends, because only this thread can release what it took.
                _ = release.WaitOne();
            }
            finally
            {
                mutex.ReleaseMutex();
                _ = released.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = $"BrowserAI mutex owner {name}",
        };

        owner.Start();

        try
        {
            var acquisition = await acquired.Task.ConfigureAwait(false);

            if (acquisition is MutexAcquisition.NotAcquired)
            {
                release.Dispose();
                return new MutexHold(acquisition, null, Task.CompletedTask);
            }

            return new MutexHold(acquisition, release, released.Task);
        }
        catch
        {
            release.Dispose();
            throw;
        }
    }

    /// <summary>Asks the owner thread to release the mutex, and returns once it has.</summary>
    /// <returns>The release.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0 || _release is null)
        {
            return;
        }

        _ = _release.Set();
        await _released.ConfigureAwait(false);
        _release.Dispose();
    }
}
