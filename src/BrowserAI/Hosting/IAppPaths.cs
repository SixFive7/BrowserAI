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
    /// Where one run of BrowserAI keeps the files it generates for its child:
    /// the config file, and the child's working directory.
    /// </summary>
    /// <remarks>
    /// A sibling of <c>current\</c> for the same reason the log is, and per-run
    /// rather than shared, because [the child's working directory is the output
    /// root](../../plan/F-artifacts.md) and two runs must not write into one.
    /// Sessions replace this at build-order step 10; until then a run is the
    /// unit.
    /// </remarks>
    string InstanceRoot { get; }
}
