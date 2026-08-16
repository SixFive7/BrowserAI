// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Sessions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// The <see cref="SessionEnvironment"/> the in-process rig hands
/// <c>BrowserProxy</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It refuses to open a session rather than pretending it can.</b> This layer
/// starts no process and touches nothing on disk, which is the property that
/// makes it run in milliseconds — and a session needs a real <c>node</c> child.
/// A double that quietly produced a working logger would let a session tool be
/// half-exercised here and leave the impression it was covered; the session tools
/// are proved against the published binary, by <c>SessionToolTests</c>.
/// </para>
/// <para>
/// The paths still point into the suite's own scratch root rather than at
/// <c>%LocalAppData%\BrowserAI</c>, because the session index is machine-wide
/// state and a rig that reached it would put throwaway directories into a
/// developer's own <c>browserai_list</c>.
/// </para>
/// </remarks>
internal static class RigSessionEnvironment
{
    /// <summary>Builds the environment for one rig.</summary>
    /// <returns>An environment whose paths are scratch and whose session log refuses.</returns>
    public static SessionEnvironment Create() => new()
    {
        Paths = new LocalAppDataPaths(Path.Combine(ScratchRoot.Path, "rig-app-root")),
        Payload = RepositoryPayload.Layout,
        InstanceDirectory = Path.Combine(ScratchRoot.Path, "rig-app-root", "instances"),
        OpenSessionLog = (directory, _) => throw new NotSupportedException(
            $"The in-process rig cannot open a session log for '{directory}'. This layer starts no process, so it cannot open a session either; SessionToolTests drives the session tools against the published binary."),
    };
}
