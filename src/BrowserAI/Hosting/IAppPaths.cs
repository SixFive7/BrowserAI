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
/// tested at all. Step 19 swaps <see cref="LocalAppDataPaths"/> for an
/// implementation over <c>VelopackLocator.Current.RootAppDir</c> and nothing
/// else moves.
/// </para>
/// <para>
/// <b>Never <c>AppContext.BaseDirectory</c>.</b> It reads as "next to the
/// binary" and resolves <i>inside</i> <c>current\</c>, which an update replaces
/// wholesale — so a log or a browser tree placed there is deleted by the event
/// most likely to have produced the line you came to read. ExoFabric/UCC does
/// exactly this and carries a 10-day log retention policy that can therefore
/// never once have applied.
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
    /// rather than shared, because [the child's working directory is the output
    /// root](../../plan/F-artifacts.md) and two runs must not write into one.
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
    /// rather than part of a session's durable state — and
    /// [§C](../../plan/C-sessions.md#the-session-directory-is-the-identity) keeps
    /// the session root to <c>lock.json</c> and the session log, so a third file
    /// there is out.
    /// </para>
    /// <para>
    /// <b>Corrected 2026-08-16 (previously "Sessions replace this at build-order
    /// step 10").</b> They do not, and step 10 is where that was first noticed.
    /// Step 10 built the session directory, its lock and the three lock scopes —
    /// but nothing in the product created a session yet.
    /// </para>
    /// </remarks>
    string InstanceRoot { get; }
}
