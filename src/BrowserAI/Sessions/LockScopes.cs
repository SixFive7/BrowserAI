// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Sessions;

/// <summary>
/// The lock scopes, named in one place, so that two components cannot disagree
/// about a name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four scopes exist and must not be conflated:</b>
/// </para>
/// <list type="table">
///   <item>
///     <term>Per-directory, guarding create-or-take</term>
///     <description><c>Global\BrowserAI-{sha256(path)[..32]}</c>, held for milliseconds</description>
///   </item>
///   <item>
///     <term>Per-session, proving ownership</term>
///     <description><c>browserai.lock</c> opened <c>FileShare.Read</c>, held for the session's life</description>
///   </item>
///   <item>
///     <term>Machine-wide, guarding the sweep</term>
///     <description><c>Global\BrowserAI-Sweep</c>, held for one sweep pass</description>
///   </item>
///   <item>
///     <term>Per-root, guarding the live-instance set</term>
///     <description><c>Global\BrowserAI-Live-{sha256(root)[..32]}</c>, held for one join or one census</description>
///   </item>
/// </list>
/// <para>
/// ⚠️ <b>Corrected 2026-08-23 (previously "The three lock scopes" and a table of
/// three).</b> The fourth existed and was undocumented, which would be a
/// tidiness complaint except that it also had <b>no namespace of its own</b>:
/// <c>Updates.LiveInstances.MutexNameFor</c> was the per-directory name
/// verbatim, so a session opened on the install root took the live set's gate
/// and held it for the session's life. Found by
/// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md),
/// B3. The prefix and the row above arrived together, because a scope that is
/// not written down here is a scope the next reader composes a name for.
/// </para>
/// <para>
/// <b>The discriminator is the duration the object is held for, and it decides
/// whether there is a wait at all.</b> Anything held for a session's life is the
/// caller's business and never waits: BrowserAI cannot know what a wait costs
/// its caller, so it returns the fact — <i>this directory is busy, and here is
/// who has it</i> — and the decision to retry belongs to the model. The sweep is
/// try-acquire-and-skip at zero timeout for a second reason: a skipped sweep is
/// not a missed sweep, because whoever holds the mutex is scanning the same
/// store.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-08-18 (previously: "Milliseconds is internal, so the
/// per-directory gate keeps a short bounded wait — asking a calling model to
/// retry a 5 ms operation would be absurd").</b> That reasoning sizes the
/// timeout against <i>one</i> holder and is why the gate was five seconds; the
/// wait is behind the <b>queue</b> of every process naming the same directory,
/// and each of them enters the gate in turn merely to discover the file is held.
/// So the duration something is held for decides <i>whether</i> a scope waits;
/// the design point decides <i>how long</i>. See
/// <see cref="PerDirectoryGate"/> for the measurement, which found <c>Busy</c>
/// reachable by queueing alone.
/// </para>
/// <para>
/// ⚠️ <b>Later the same day, the better answer: stop putting the peers in the
/// queue.</b> A process that only wants to <i>report</i> the holder is now
/// answered by <c>SessionLock.ProbeForHolder</c> in front of the gate, because
/// the sharing violation on <c>browserai.lock</c> already proves ownership and the
/// mutex never made it more true. <b>Sizing the timeout and removing the queue
/// are both needed and neither replaces the other</b> — the probe is why the
/// queue is short, the timeout is what still has to be right when it is not.
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
    /// <para>
    /// It covers the create-or-take critical section and nothing else: a file
    /// open, a durable write, a rename and a re-open.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 to sixty seconds (previously five, described as
    /// "four orders of magnitude of headroom … exceeding it means something is
    /// wrong that a longer wait would not fix"). Both halves of that sentence
    /// were false, and the second one was printed at the caller.</b> The headroom
    /// is not against one section — it is against the <b>queue of every process
    /// contending for the same directory</b>, because each of them enters this
    /// gate in turn just to discover the file is held. Measured on an
    /// <b>idle</b> machine, 200 real processes released together against one
    /// directory
    /// ([kb](../../../kb/windows/detection.md#named-mutexes-and-lock-files)):
    /// </para>
    /// <list type="table">
    /// <listheader><term>Contenders</term><description>Slowest refusal, and what it answered</description></listheader>
    /// <item><term>16</term><description>367 ms — every refusal named the holder</description></item>
    /// <item><term>100 — <b>the charter's design point</b></term><description>3,349 ms, p99 3,227 ms. <b>A margin of 1.49× on an idle machine</b></description></item>
    /// <item><term>200</term><description><b>73 refusals of 796 came back <c>Busy</c> at 5,022–5,056 ms</b>, in 4 runs of 4</description></item>
    /// </list>
    /// <para>
    /// <b>So <c>Busy</c> was reachable by queueing alone</b> — no stuck holder, no
    /// starvation, nothing wrong at all — and the sentence it printed told the
    /// calling model that waiting would not help, when waiting was the entire
    /// remedy. At the design point the old value had less headroom than a single
    /// browser launch takes.
    /// </para>
    /// <para>
    /// ⚠️ <b>And it was smaller than a wait taken INSIDE it, which is incoherent
    /// on its face.</b> <c>SessionLock.OpenHeld</c> and
    /// <c>SessionLock.ReadRecord</c> both run under this gate and both go through
    /// <see cref="RenameWindow"/>, whose budget has been <b>30 s</b> since
    /// 2026-08-18. One entitled reader legitimately waiting out a rename window
    /// therefore held this gate six times longer than its own timeout, turning
    /// every peer's correct <i>"held by PID n"</i> into a wrong <i>"something is
    /// wrong"</i>. <c>SessionLockTests.TheGateOutlastsEveryWaitTakenInsideIt</c>
    /// is what stops that ordering being broken again by a change to either
    /// number.
    /// </para>
    /// <para>
    /// ⚠️ <b>And since 2026-08-18 most of that queue does not exist, because
    /// most of it never wanted this mutex.</b> <c>SessionLock.ProbeForHolder</c>
    /// opens <c>browserai.lock</c> <b>in front of</b> this gate: a sharing violation
    /// is the kernel's answer to <i>who owns this</i>, so a peer that only wants
    /// to report the holder is answered there and never creates the object. What
    /// queues here now is processes that intend to <b>take</b> the directory.
    /// Measured before and after against a directory a live holder already had,
    /// 3 runs at each N on an idle machine
    /// ([kb](../../../kb/windows/detection.md#named-mutexes-and-lock-files)):
    /// slowest refusal <b>329 ms → 30 ms</b> at 16, <b>2,084 ms → 203 ms</b> at
    /// the design point of 100, <b>4,267 ms → 449 ms</b> at 200 — and the shape
    /// changed from <c>p50 ≈ max/2</c>, which is a queue draining one entrant at
    /// a time, to a cluster.
    /// </para>
    /// <para>
    /// <b>The number below is deliberately unchanged by that.</b> It was never
    /// sized against the common case; it is sized against
    /// <see cref="RenameWindowWaitsInsideTheGate"/> waits in series plus a queue
    /// of genuine takers, and a smaller value would only be safe while the probe
    /// keeps working. A timeout that depends on an optimisation is not a hang
    /// detector.
    /// </para>
    /// <para>
    /// <b>A hang detector rather than a budget.</b> It is ~36× the measured
    /// design-point queue and ~4,800× the ~25 ms one contender spends inside. A
    /// slow machine must not reach it; a holder that has genuinely wedged still
    /// does, which is why it stays bounded.
    /// </para>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-18 to one hundred and twenty seconds (previously
    /// sixty, described as "twice <see cref="RenameWindow.Budget"/>").</b> Twice
    /// the budget was the wrong comparison, and it was the comparison the test
    /// encoded: <b>one hold of this gate contains
    /// <see cref="RenameWindowWaitsInsideTheGate"/> of them in series</b>, so the
    /// number to beat is 90 s and the gate was 60. A holder legitimately waiting
    /// out three rename windows therefore made every peer's <c>TryAcquire</c>
    /// return <c>Busy</c> — and the <c>Busy</c> sentence offers <i>"a process is
    /// wedged holding it"</i> as one of two explanations, which would again be a
    /// diagnosis the code cannot support. Found by
    /// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-locking.md),
    /// B1. What is asserted now is the sum.
    /// </para>
    /// </remarks>
    public static TimeSpan PerDirectoryGate => TimeSpan.FromSeconds(120);

    /// <summary>
    /// How many <see cref="RenameWindow.Budget"/>-bounded waits one hold of
    /// <see cref="PerDirectoryGate"/> can contain, <b>in series</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three, and they are enumerable rather than estimated.</b>
    /// <c>SessionLock.TakeOrReport</c>, inside the gate: the first
    /// <c>OpenHeld</c>, then <c>WriteDurably</c>'s replace loop — whose
    /// <c>MoveBudget</c> is this same value — then the re-open.
    /// <c>SessionLock.Rewrite</c>, inside the gate, takes the same three in a
    /// different order: the replace, the re-open, and <c>Reclaim</c>'s re-open on
    /// the failure path.
    /// </para>
    /// <para>
    /// <b>It is a constant so that adding a fourth is a red build rather than a
    /// silent 30 s.</b> A new wait inside the critical section has to be counted
    /// here, and counting it fails
    /// <c>SessionLockTests.TheGateOutlastsEveryWaitTakenInsideIt</c> until the
    /// gate above is re-sized — which is the conversation that should happen and
    /// did not when the third one was added.
    /// </para>
    /// </remarks>
    public static int RenameWindowWaitsInsideTheGate => 3;

    /// <summary>
    /// Zero. Every lock a caller can reason about is attempted with this, and
    /// contention is answered immediately with the holder's identity.
    /// </summary>
    public static TimeSpan NeverWaits => TimeSpan.Zero;

    /// <summary>
    /// The bounded wait on the live-instance set's own gate, which
    /// <c>Updates.LiveInstances</c> takes around joining and around the census.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Split out on 2026-08-18, at the value it already had.</b> That code
    /// used <see cref="PerDirectoryGate"/>, and raising the gate to sixty seconds
    /// would have silently taken this with it — putting a <b>sixty-second stall
    /// on the startup path</b>, because <c>Join</c> runs while BrowserAI is
    /// starting and blocks until this expires. Nothing about this scope asked for
    /// that; it inherited it from a constant that was re-sized for a different
    /// problem, which is exactly the coupling that produced the defect the gate
    /// was re-sized for in the first place.
    /// </para>
    /// <para>
    /// <b>Five seconds is right HERE and was wrong there, and the difference is
    /// the section, not the taste.</b> The per-directory gate is held across a
    /// durable write, a rename, a re-open and a <see cref="RenameWindow"/> wait;
    /// this one is held across creating one <c>.live</c> file, or one directory
    /// enumeration. A hundred processes starting at once queue about 200 ms here
    /// against 3.3 s there. And the consequences differ: expiring here means
    /// <i>no update is applied this run</i>, which
    /// <c>LiveInstances.Join</c> documents as the safe direction and logs; there
    /// it means a caller is told the wrong thing about who owns a session.
    /// </para>
    /// </remarks>
    public static TimeSpan LiveInstanceGate => TimeSpan.FromSeconds(5);
}
