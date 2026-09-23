// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Hosting;

/// <summary>
/// Every path BrowserAI keeps <b>data</b> in outside a session directory, behind
/// one seam.
/// </summary>
/// <remarks>
/// <para>
/// The seam exists on day one rather than being bolted on at §G, and the reason
/// is mechanical: <c>VelopackLocator.Current</c> throws under <c>dotnet run</c>
/// and under every test host, so a build that calls it directly cannot be
/// tested at all.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-09-15 (previously "Step 19 swapped the <b>root</b>, not
/// the class ... what actually had to move is where <c>rootAppDir</c> comes from,
/// and that is <see cref="Updates.InstallLocation"/> -- the locator when this
/// process is an installed one, <c>%LocalAppData%\BrowserAI</c> when it is not
/// ... the two agree only while the install is at its default location, and
/// <c>Setup.exe --installto</c> makes them disagree silently, which would put
/// the log and 768 MB of browsers somewhere the running binary is not").</b>
/// That reasoning was sound and its conclusion was upside down. <b>The data
/// root is a constant -- <c>%LocalAppData%\BrowserAI</c> -- and the locator feeds
/// nothing here.</b> What the old wiring guaranteed was that the data would
/// always be found beside the binary; what it cost is that the data was always
/// inside a directory the installer destroys. The binary can be relocated, and
/// is; the browsers, the sessions and the log are the things a person would
/// mind losing.
/// </para>
/// <para>
/// <b>An install root is destroyed twice over, by design.</b> <c>Setup.exe</c>
/// renames a non-empty root aside and, on success, <b>deletes it</b> -- a repair
/// or an overwrite install is exactly that path -- and uninstall runs
/// <c>remove_dir_contents</c> over the whole root
/// ([kb](../../../kb/packaging/velopack.md#where-state-may-live----the-finding-the-provisioning-design-rests-on)).
/// Neither event is a mistake and neither can be hooked: there is no callback
/// before <c>Setup.exe</c>'s rename-and-clean, measured against 1.2.0's own
/// source. So the layout is the only defence there is, and it is
/// <c>%LocalAppData%\BrowserAI.app</c> for the install and
/// <c>%LocalAppData%\BrowserAI</c> for the data -- siblings, neither inside the
/// other.
/// </para>
/// <para>
/// ⚠️ <b>Never derive the data root from the install root, and never from the
/// image path.</b> Both are to hand and both are wrong. <c>Setup.exe
/// --installto</c> and the portable zip put the binary anywhere, so a derived
/// data root moves house whenever the binary does -- abandoning the browsers and
/// the session index rather than keeping them. And a Velopack hook runs on
/// <c>&lt;install root&gt;\current\BrowserAI.exe</c>, so a hook deriving a root
/// from its own image is the one process that would resolve it differently from
/// the product -- which matters most in the hook that offers to
/// <i>delete</i> the data root.
/// </para>
/// <para>
/// <b>Never <c>AppContext.BaseDirectory</c>.</b> It reads as "next to the
/// binary" and resolves <i>inside</i> <c>current\</c>, which an update replaces
/// wholesale -- so a log or a browser tree placed there is deleted by the event
/// most likely to have produced the line you came to read. A shipped product
/// examined for this project does exactly this and carries a 10-day log
/// retention policy that can therefore never once have applied.
/// </para>
/// <para>
/// <b>What is <i>not</i> here, and why.</b> The live-instance markers are keyed
/// to the <b>install root</b> and live on <see cref="Updates.LiveInstances"/>
/// rather than on this seam. They are not data: they answer <i>am I the last
/// process running out of this install?</i>, which is a question about the
/// directory Velopack's <c>force_stop_package</c> matches image paths against.
/// A census keyed to the data root would answer about the wrong set of
/// processes.
/// </para>
/// </remarks>
internal interface IAppPaths
{
    /// <summary>
    /// The data root. <b>A sibling of the install root, never a parent and never
    /// a child of it.</b>
    /// </summary>
    string RootAppDir { get; }

    /// <summary>Where the rolling process log is written.</summary>
    string LogDirectory { get; }

    /// <summary>
    /// Where provisioned browsers live -- 768 MB of them, outside anything an
    /// installer removes.
    /// </summary>
    /// <remarks>
    /// <b>Always absolute.</b> It reaches the child as
    /// <c>PLAYWRIGHT_BROWSERS_PATH</c>, and a relative value there resolves
    /// against <c>INIT_CWD</c> -- inherited from whatever npm ancestor last ran --
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
    /// Outside the install root for the reason the log is -- a repair install
    /// and an uninstall both empty that root, and the index is
    /// [the only inventory of session directories there is](../../BrowserAI/Sessions/SessionIndex.cs).
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
    /// Outside the install root for the same reason the log is, and per-run
    /// rather than shared, because the child's working directory <i>is</i> the
    /// output root -- upstream resolves a relative <c>filename</c> against the
    /// child's cwd, so a bare <c>foo.png</c> lands inside this tree by
    /// construction -- and two runs must not write into one.
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
    /// rather than part of a session's durable state -- and no artifact is ever at
    /// a session's root, so a third file there is out. See
    /// <see cref="Sessions.SessionLayout"/> for what the root is allowed to hold
    /// and why.
    /// </para>
    /// <para>
    /// <b>Corrected 2026-08-16 (previously "Sessions replace this at build-order
    /// step 10").</b> They do not, and step 10 is where that was first noticed.
    /// Step 10 built the session directory, its lock and the three lock scopes --
    /// but nothing in the product created a session yet.
    /// </para>
    /// </remarks>
    string InstanceRoot { get; }
}
