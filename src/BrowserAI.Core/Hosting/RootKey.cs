// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Sessions;

namespace BrowserAI.Hosting;

/// <summary>
/// A root's key: the 32 hex characters every name keyed to one root ends in.
/// </summary>
/// <remarks>
/// <para>
/// <b>One derivation for every name that has to agree about a root</b>: the logon
/// task's, the background's pipe and, until 2026-10-08, the census gate and the
/// coordinator's pipe. A second spelling of the key is how two of them would come to
/// name different roots while each reported success.
/// </para>
/// <para>
/// <b>It is the session lock's own identity chain</b>, the part of
/// <see cref="SessionPath.For"/>'s mutex name after its prefix, because that chain
/// already resolves a path's spelling, its case, its volume and its aliases to one
/// answer. <i>Moved here 2026-10-08 from <c>LiveInstances.RootKeyFor</c>, unchanged,
/// when the live-instance census was deleted with the per-server pipes.</i>
/// </para>
/// </remarks>
internal static class RootKey
{
    /// <summary>The key of one root.</summary>
    /// <param name="root">The root: an install root or a data root.</param>
    /// <returns>The upper-case hex key.</returns>
    public static string For(string root) =>
        SessionPath.For(root).MutexName[LockScopes.PerDirectoryPrefix.Length..];

    /// <summary>Whether two spellings name one root, by their keys.</summary>
    /// <param name="first">One root.</param>
    /// <param name="second">The other.</param>
    /// <returns>Whether the keys agree; <see langword="false"/> when either is not a root a key can be made of.</returns>
    public static bool Same(string first, string second)
    {
        try
        {
            return string.Equals(For(first), For(second), StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
