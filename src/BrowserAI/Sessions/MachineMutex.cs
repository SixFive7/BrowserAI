// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>How a wait on a <see cref="MachineMutex"/> ended.</summary>
internal enum MutexAcquisition
{
    /// <summary>The timeout passed and somebody else still holds it.</summary>
    NotAcquired,

    /// <summary>Held by this thread. Release it.</summary>
    Acquired,

    /// <summary>
    /// <b>Held by this thread</b>, and the previous holder died without
    /// releasing.
    /// </summary>
    /// <remarks>
    /// This is the outcome that has to be a separate value rather than folded
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
/// therefore no session.</b> <see cref="Create"/> lets the failure out rather
/// than retrying under <c>Local\</c>, and the caller turns that into a hard
/// blocker whose reason reaches the calling model. The prior art on this machine
/// does fall back, deliberately and visibly, because a degraded rig beats an
/// unusable one; BrowserAI refuses because a degraded lock is indistinguishable
/// from a working one at exactly the moment it matters — two logon sessions get
/// two distinct kernel objects for one directory, neither can see the other, and
/// both report success while a browser profile is opened twice.
/// </para>
/// <para>
/// <b>Do not <c>await</c> across a critical section guarded by one of these.</b>
/// A named mutex is owned by the <i>thread</i> that waited on it. A continuation
/// resuming on a different pool thread makes the release throw an
/// <c>ApplicationException</c> about "an unsynchronized block of code", which
/// names nothing relevant and points nowhere near the cause. Every caller here
/// is synchronous for that reason, not by accident.
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

    /// <summary>Creates or opens the named mutex.</summary>
    /// <param name="name">A <c>Global\</c>-prefixed name.</param>
    /// <returns>The mutex.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not <c>Global\</c> prefixed.</exception>
    /// <exception cref="UnauthorizedAccessException">An object of that name exists and this token cannot open it.</exception>
    /// <exception cref="WaitHandleCannotBeOpenedException">The name is taken by a different kind of object.</exception>
    /// <exception cref="IOException">The object manager refused the name.</exception>
    /// <exception cref="NotSupportedException">The name cannot be created on this platform.</exception>
    /// <remarks>
    /// It <b>throws</b> rather than returning a null-and-reason pair, and the
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
            // A programming error rather than an environment failure, so it
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

    /// <summary>Waits for the mutex, for at most <paramref name="timeout"/>.</summary>
    /// <param name="timeout">
    /// <see cref="LockScopes.NeverWaits"/> for anything a caller can reason
    /// about; <see cref="LockScopes.PerDirectoryGate"/> for the internal
    /// create-or-take section.
    /// </param>
    /// <returns>Whether it was acquired, and whether it was abandoned.</returns>
    public MutexAcquisition Acquire(TimeSpan timeout)
    {
        try
        {
            return _mutex.WaitOne(timeout) ? MutexAcquisition.Acquired : MutexAcquisition.NotAcquired;
        }
        catch (AbandonedMutexException)
        {
            // The wait SUCCEEDED. This thread owns the mutex now and must
            // release it exactly as if the wait had returned true.
            return MutexAcquisition.AcquiredAbandoned;
        }
    }

    /// <summary>Releases it, on the thread that acquired it.</summary>
    public void Release() => _mutex.ReleaseMutex();

    /// <inheritdoc />
    public void Dispose() => _mutex.Dispose();
}
