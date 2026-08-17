// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Hosting;

/// <summary>
/// Every path BrowserAI owns outside a session directory, behind one seam.
/// </summary>
/// <remarks>
/// <para>
/// The seam exists on day one rather than being bolted on at §G, and the reason
/// is mechanical: <c>VelopackLocator.Current</c> throws under <c>dotnet run</c>
/// and under every test host, so a build that calls it directly cannot be
/// tested at all.
/// </para>
/// <para>
/// <b>Corrected 2026-08-16 (previously "Step 19 swaps
/// <see cref="LocalAppDataPaths"/> for an implementation over
/// <c>VelopackLocator.Current.RootAppDir</c> and nothing else moves").</b> Step
/// 19 swapped the **root**, not the class. The layout below is identical either
/// way, so a second <c>IAppPaths</c> would have been the same five expressions
/// twice; what actually had to move is where <c>rootAppDir</c> comes from, and
/// that is <see cref="Updates.InstallLocation"/> — the locator when this process
/// is an installed one, <c>%LocalAppData%\BrowserAI</c> when it is not. The
/// distinction is not cosmetic: the two agree only while the install is at its
/// default location, and <c>Setup.exe --installto</c> makes them disagree
/// silently, which would put the log and 768 MB of browsers somewhere the
/// running binary is not.
/// </para>
/// <para>
/// <b>Never <c>AppContext.BaseDirectory</c>.</b> It reads as "next to the
/// binary" and resolves <i>inside</i> <c>current\</c>, which an update replaces
/// wholesale — so a log or a browser tree placed there is deleted by the event
/// most likely to have produced the line you came to read. A shipped product
/// examined for this project does exactly this and carries a 10-day log
/// retention policy that can therefore never once have applied.
/// </para>
/// </remarks>
internal interface IAppPaths
{
    /// <summary>
    /// The installation root, which <b>contains</b> <c>current\</c> rather than
    /// being it. Everything that must outlive an update is a sibling of
    /// <c>current\</c>, never a child.
    /// </summary>
    string RootAppDir { get; }

    /// <summary>Where the rolling process log is written.</summary>
    string LogDirectory { get; }

    /// <summary>
    /// Where provisioned browsers live. A sibling of <c>current\</c>, never a
    /// child, or every update re-downloads 203.8 MB.
    /// </summary>
    /// <remarks>
    /// <b>Always absolute.</b> It reaches the child as
    /// <c>PLAYWRIGHT_BROWSERS_PATH</c>, and a relative value there resolves
    /// against <c>INIT_CWD</c> — inherited from whatever npm ancestor last ran —
    /// before it resolves against the child's own working directory. That
    /// failure lands the browser somewhere nobody chose and reports nothing.
    /// </remarks>
    string BrowsersDirectory { get; }

    /// <summary>
    /// The session index: one file per session directory, named for the hash of
    /// its canonical path and holding that path and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sibling of <c>current\</c> for the same reason the log is — an update
    /// replaces that folder, and the index is
    /// [the only inventory of session directories there is](../Sessions/SessionIndex.cs).
    /// Losing it would not lose a session, because every entry is re-asserted on
    /// the next <c>init</c> or <c>resume</c>, but it would make every session a
    /// caller had forgotten the path of invisible until they used it again.
    /// </para>
    /// <para>
    /// <b>On the seam rather than composed at the call site</b>, so that the
    /// suite can point the index at a scratch root. It is machine-wide state: a
    /// test that wrote into the real one would put its own scratch directories
    /// into a developer's <c>browserai_list</c>.
    /// </para>
    /// </remarks>
    string IndexDirectory { get; }

    /// <summary>
    /// Where one run of BrowserAI keeps the files it generates for its child:
    /// the config file, and the child's working directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sibling of <c>current\</c> for the same reason the log is, and per-run
    /// rather than shared, because the child's working directory <i>is</i> the
    /// output root — upstream resolves a relative <c>filename</c> against the
    /// child's cwd, so a bare <c>foo.png</c> lands inside this tree by
    /// construction — and two runs must not write into one.
    /// </para>
    /// <para>
    /// <b>Corrected again 2026-08-16 (previously "The replacement is step 12's,
    /// and it is one change: the generated config and the child's working
    /// directory move into the session directory, and this member goes with the
    /// per-run concept").</b> Sessions do <b>not</b> replace this, and step 12 is
    /// where that was settled rather than assumed. Two things still need a
    /// per-run home. The first is <b>the run's own child</b>: the MCP spec
    /// forbids the tool set varying per connection, so <c>tools/list</c> has to
    /// be answerable before any session exists, and the child that answers it
    /// needs a working directory and a profile of its own. The second is
    /// <b>every session's generated config</b>, which is a per-run artifact
    /// rather than part of a session's durable state — and no artifact is ever at
    /// a session's root, so a third file there is out. See
    /// <see cref="Sessions.SessionLayout"/> for what the root is allowed to hold
    /// and why.
    /// </para>
    /// <para>
    /// <b>Corrected 2026-08-16 (previously "Sessions replace this at build-order
    /// step 10").</b> They do not, and step 10 is where that was first noticed.
    /// Step 10 built the session directory, its lock and the three lock scopes —
    /// but nothing in the product created a session yet.
    /// </para>
    /// </remarks>
    string InstanceRoot { get; }

    /// <summary>
    /// Where every live BrowserAI under this install root announces itself: one
    /// held file per running process, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It exists for exactly one question — <i>am I the last instance?</i> —
    /// and that question gates the update apply.</b> Velopack's
    /// <c>force_stop_package</c> kills every process whose image is under the
    /// install root, without asking and after every hook returns
    /// ([kb](../../../kb/packaging/velopack.md#where-state-may-live--the-finding-the-provisioning-design-rests-on)).
    /// With ~100 concurrent registrations that is every other live session
    /// destroyed mid-task, so an apply is only ever allowed to run when nothing
    /// else is there to destroy.
    /// </para>
    /// <para>
    /// <b>A sibling of <c>current\</c>, and deliberately not
    /// <see cref="InstanceRoot"/>.</b> An instance directory's liveness signal is
    /// the child holding it as a working directory, which means a run has no
    /// signal at all until its child has started — precisely the window in which
    /// an update check can run. This one is a file handle taken by BrowserAI
    /// itself, before anything else, and released by the OS however the process
    /// dies.
    /// </para>
    /// </remarks>
    string LiveInstanceDirectory { get; }
}
