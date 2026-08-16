// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// <see cref="LocalAppDataPaths"/> with the browsers root pointed somewhere
/// else, for the one rig that needs a scratch app root <b>and</b> a real
/// browser.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two halves pull in opposite directions, which is why this type
/// exists.</b> The session index, the logs and the instance directory must be
/// scratch — the index is machine-wide state, and a rig that wrote into the real
/// one would put throwaway directories into a developer's own
/// <c>browserai_list</c> and leave them there. The browsers root must be the
/// real one — the alternative is a 203.8 MB download per test. A single
/// <c>rootAppDir</c> cannot be both, because <see cref="LocalAppDataPaths"/>
/// composes every path from it.
/// </para>
/// <para>
/// <b>Nothing here writes to the browsers root.</b> It is read to launch a
/// browser and to ask which processes came out of it; the tests that install,
/// delete or re-provision a tree all use a scratch root and a fake installer,
/// which is what keeps this safe.
/// </para>
/// </remarks>
/// <param name="rootAppDir">The scratch app root: logs, index and instances.</param>
/// <param name="browsersDirectory">The browsers root, which is normally the real one.</param>
internal sealed class RigPaths(string rootAppDir, string browsersDirectory) : IAppPaths
{
    private readonly LocalAppDataPaths _scratch = new(rootAppDir);

    /// <inheritdoc />
    public string RootAppDir => _scratch.RootAppDir;

    /// <inheritdoc />
    public string LogDirectory => _scratch.LogDirectory;

    /// <inheritdoc />
    public string BrowsersDirectory { get; } = browsersDirectory;

    /// <inheritdoc />
    public string IndexDirectory => _scratch.IndexDirectory;

    /// <inheritdoc />
    public string InstanceRoot => _scratch.InstanceRoot;
}
