// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// The three lock scopes, named in one place, so that two components cannot
/// disagree about a name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three scopes exist and must not be conflated:</b>
/// </para>
/// <list type="table">
///   <item>
///     <term>Per-directory, guarding create-or-take</term>
///     <description><c>Global\BrowserAI-{sha256(path)[..32]}</c>, held for milliseconds</description>
///   </item>
///   <item>
///     <term>Per-session, proving ownership</term>
///     <description><c>lock.json</c> opened <c>FileShare.Read</c>, held for the session's life</description>
///   </item>
///   <item>
///     <term>Machine-wide, guarding the sweep</term>
///     <description><c>Global\BrowserAI-Sweep</c>, held for one sweep pass</description>
///   </item>
/// </list>
/// <para>
/// <b>The discriminator is the duration the object is held for, and it decides
/// the timeout.</b> Milliseconds is internal, so the per-directory gate keeps a
/// short bounded wait — asking a calling model to retry a 5 ms operation would
/// be absurd. Anything held for a session's life is the caller's business and
/// never waits: BrowserAI cannot know what a wait costs its caller, so it
/// returns the fact — <i>this directory is busy, and here is who has it</i> —
/// and the decision to retry belongs to the model. The sweep is
/// try-acquire-and-skip at zero timeout for a third reason: a skipped sweep is
/// not a missed sweep, because whoever holds the mutex is scanning the same
/// store.
/// </para>
/// <para>
/// <b>Every name carries the <c>Global\</c> prefix and there is no
/// <c>Local\</c> fallback anywhere in this product.</b> A <c>Local\</c> name is
/// scoped to the logon session, so falling back to it does not weaken the lock
/// evenly — it removes it precisely where it is needed, between a Remote Desktop
/// session and the console one, which is the only arrangement in which two
/// BrowserAIs contend without either being able to detect it. Refusing beats
/// descending: a lock that narrows its own scope when it cannot get the scope it
/// asked for reports success while guarding nothing.
/// </para>
/// </remarks>
internal static class LockScopes
{
    /// <summary>The only namespace prefix this product uses for a named object.</summary>
    public const string GlobalPrefix = @"Global\";

    /// <summary>The per-directory mutex's fixed prefix.</summary>
    public const string PerDirectoryPrefix = $@"{GlobalPrefix}BrowserAI-";

    /// <summary>
    /// How many hex characters of the canonical path's SHA-256 go into the
    /// per-directory mutex name.
    /// </summary>
    /// <remarks>
    /// 128 bits of a 256-bit digest. A collision would have to be engineered,
    /// and its worst outcome is two unrelated directories serialising against
    /// each other for a few milliseconds — never a lock that reports success
    /// while guarding nothing, which is the failure the length has to be chosen
    /// against.
    /// </remarks>
    public const int PerDirectoryHashLength = 32;

    /// <summary>
    /// The machine-wide sweep mutex. One name, one place in code, <c>Global\</c>
    /// prefixed — which is what closes race <b>R4</b>, the scheduled task and
    /// BrowserAI using different mutexes.
    /// </summary>
    public const string Sweep = $@"{GlobalPrefix}BrowserAI-Sweep";

    /// <summary>
    /// The bounded wait on the per-directory gate: the one place in this design
    /// that waits at all.
    /// </summary>
    /// <remarks>
    /// It covers the create-or-take critical section and nothing else. The
    /// section is a file open, a durable write, a rename and a re-open — single
    /// milliseconds — so five seconds is four orders of magnitude of headroom
    /// rather than a guess at a busy machine. Exceeding it means something is
    /// wrong that a longer wait would not fix.
    /// </remarks>
    public static TimeSpan PerDirectoryGate => TimeSpan.FromSeconds(5);

    /// <summary>
    /// Zero. Every lock a caller can reason about is attempted with this, and
    /// contention is answered immediately with the holder's identity.
    /// </summary>
    public static TimeSpan NeverWaits => TimeSpan.Zero;
}
