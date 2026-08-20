// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;

namespace BrowserAI.Sessions;

/// <summary>
/// The two things a session directory may not be: on a network path, or an
/// aliased spelling of a directory that has another name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both are refusals at the door rather than handling spread through the
/// product</b>, and both were taken by the maintainer on 2026-08-19 against a
/// correct-but-larger alternative. The reasoning is in
/// [`DECISIONS.md`](../../../DECISIONS.md#refusing-network-paths-and-aliased-spellings-at-the-door);
/// what belongs here is the shape of the check, because the shape is what makes
/// the refusal cheap enough to sit in front of everything else.
/// </para>
/// <para>
/// <b>Why refuse a network path.</b> A caller-supplied session directory on a
/// share that stops answering costs a measured <b>22,210 ms for one
/// <c>File.Exists</c></b> — and that measurement is through a mapped
/// <i>drive letter</i>, not a UNC spelling
/// ([kb](../../../kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds)).
/// Several such calls happen inside <see cref="LockScopes.PerDirectoryGate"/>,
/// so one caller naming a dead share stalls every other process contending for
/// that directory. The alternative was to move the slow calls out of the gate,
/// which is a redesign of the critical section; refusing is one predicate.
/// </para>
/// <para>
/// <b>Why refuse an aliased spelling.</b> <c>Path.GetFullPath</c> resolves
/// neither <c>\\?\</c>, 8.3 short names, junctions, <c>subst</c> nor mapped
/// drives, so two spellings of one directory produce <b>two mutex names and one
/// <c>browserai.json</c></b> — the per-directory gate stops serialising while the
/// record still says everything is fine
/// ([review A4](../../../docs/reviews/2026-08-18-adversarial-locking.md)). The
/// correct fix is to canonicalise through the filesystem's own final name, and
/// it rewrites the identity of every mutex name, index key and lock path in the
/// product. Refusing is bounded, and BrowserAI creates its own session
/// directories in the common case.
/// </para>
/// <para>
/// ⚠️ <b>THE ORDER IS THE DESIGN, and it is not an optimisation.</b> The network
/// question is answered by characters and by the object manager, with <b>no
/// filesystem call anywhere in it</b>, because the filesystem call is the thing
/// being defended against. Only once the volume is known local does
/// <see cref="VolumeIdentity.FinalNameOf"/> open anything. Reordering these
/// makes the guard pay the cost it exists to prevent.
/// </para>
/// <para>
/// <b>What this deliberately accepts.</b> A drive letter that names nothing on
/// this machine is neither a network path nor an alias, so it falls through to
/// the ordinary creation failure — <i>the system cannot find the path
/// specified</i> — which already says what to do. Inventing a third refusal for
/// it would be a catalogue row for a case the existing message handles.
/// </para>
/// <para>
/// <b>And what it cannot see</b>, stated here rather than left to be
/// rediscovered:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>A component this process cannot open.</b> The walk stops at the first
///     failure that is not <i>the name does not exist</i>, and the path is
///     accepted unverified — so a junction underneath a directory this token
///     cannot open is invisible. Turning <i>unknown</i> into a refusal would
///     refuse ordinary directories on a locked-down machine.
///   </description></item>
///   <item><description>
///     <b>Anything that becomes true after the check.</b> A <c>subst</c>, a
///     <c>mklink /J</c> or a drive remapped while a session is open moves the
///     directory underneath a session that was admitted correctly. <b>This is a
///     door, not a guard</b>, and nothing here re-checks. See the hazard index.
///   </description></item>
///   <item><description>
///     <b>Case.</b> Two spellings differing only in case are one session by
///     design, so the comparison is case-insensitive — the same claim
///     <see cref="SessionPath.Key"/> makes.
///   </description></item>
/// </list>
/// </remarks>
internal static class SessionDirectoryGuard
{
    /// <summary>
    /// How far up the tree the final-name check will walk looking for a
    /// directory that exists.
    /// </summary>
    /// <remarks>
    /// A bound rather than a loop to the root, because the walk costs one
    /// directory open per level and a caller can name a path of any depth. Past
    /// this the path is accepted unverified, which is the same answer an
    /// unopenable component gets.
    /// </remarks>
    public const int AncestorWalkLimit = 64;

    /// <summary>
    /// Whether this directory may be opened as a session, and why not if not.
    /// </summary>
    /// <param name="argument">
    /// Which tool argument the path arrived in, so the refusal can tell the
    /// caller what to change.
    /// </param>
    /// <param name="location">The already-canonicalised directory.</param>
    /// <returns>
    /// The refusal, or <see langword="null"/> when the directory is admissible.
    /// </returns>
    public static string? Refuse(string argument, SessionPath location)
    {
        ArgumentNullException.ThrowIfNull(location);

        var full = location.FullPath;

        // 1. Characters only. Every UNC and device spelling begins with two
        //    separators and nothing else in Win32 path syntax does.
        if (VolumeIdentity.IsUncOrDeviceSpelling(full))
        {
            return SpelledInTheDeviceNamespace(argument, full);
        }

        // 2. The object manager only -- no filesystem call, so an unreachable
        //    share cannot be reached from here.
        var volume = VolumeIdentity.Of(full);

        if (volume.Kind is VolumeKind.Network)
        {
            return SessionErrors.DirectoryOnANetworkPath(
                argument,
                full,
                $"drive '{full[..2]}' is a mapped network drive");
        }

        if (volume is { Kind: VolumeKind.Substituted, SubstitutedFor: { } target })
        {
            // Answered without touching the filesystem at all: the DOS device
            // target IS the accepted spelling, so `subst`'s alias costs one
            // object-manager read to refuse exactly.
            return SessionErrors.DirectoryIsAnAliasedSpelling(
                argument,
                full,
                Path.Join(target, full.AsSpan(2)),
                $"drive '{full[..2]}' is a 'subst' standing in for '{target}'");
        }

        // 3. One directory open, and only now that the volume is known local.
        return AliasedByItsFinalName(argument, full);
    }

    private static string SpelledInTheDeviceNamespace(string argument, string full)
    {
        // \\?\UNC\host\share and \\.\UNC\host\share are the extended spellings of
        // a UNC path. They are answered as NETWORK rather than as an alias
        // deliberately: telling the caller to strip the prefix would earn them
        // the network refusal on the very next turn, which breaks the one rule
        // this catalogue is built on.
        var afterPrefix = full.Length > 4 && full[1] is '\\' && full[2] is '?' or '.' && full[3] is '\\'
            ? full[4..]
            : null;

        if (afterPrefix is null)
        {
            return SessionErrors.DirectoryOnANetworkPath(argument, full, "it is a UNC path");
        }

        return afterPrefix.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)
            ? SessionErrors.DirectoryOnANetworkPath(
                argument,
                full,
                $"it is the extended spelling of the UNC path '\\\\{afterPrefix[4..]}'")
            : SessionErrors.DirectoryIsAnAliasedSpelling(
                argument,
                full,
                afterPrefix,
                $"'{full[..4]}' is the device-namespace prefix, which is a second spelling of an ordinary path");
    }

    private static string? AliasedByItsFinalName(string argument, string full)
    {
        // Walk up only while the answer is "this name does not exist", which is
        // the ordinary state of `init` on a directory nothing has created yet.
        // A tail that does not exist cannot be a reparse point, so proving the
        // deepest EXISTING ancestor unaliased proves the whole path unaliased.
        //
        // The walk itself moved to VolumeIdentity on 2026-08-20, because
        // Hosting.InstallRootScope asks the identical question of the app root
        // and two walks would be two answers to it.
        var (final, candidate) = VolumeIdentity.DeepestExistingFinalName(full, AncestorWalkLimit);

        if (final is null)
        {
            // Unverifiable, and accepted. Named in this type's remarks as a gap
            // rather than papered over: refusing here would refuse an ordinary
            // directory whose parent this process may not open.
            return null;
        }

        var resolved = final.StartsWith(VolumeIdentity.ExtendedLengthPrefix, StringComparison.Ordinal)
            ? final[VolumeIdentity.ExtendedLengthPrefix.Length..]
            : final;

        // A final name in UNC form means the letter resolved through a
        // redirector after all -- GetDriveTypeW said otherwise, and the
        // filesystem is the better authority. It has not been observed; it is
        // handled because reporting a share as a local alias would send the
        // caller to a spelling that is refused for a different reason.
        if (resolved.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return SessionErrors.DirectoryOnANetworkPath(
                argument,
                full,
                $"drive '{full[..2]}' resolves to the network path '\\\\{resolved[4..]}'");
        }

        if (string.Equals(resolved, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Whatever was trimmed off to find an existing ancestor goes back on, so
        // the accepted spelling is the caller's own directory rather than an
        // ancestor of it.
        var tail = full.AsSpan(candidate.Length);

        return SessionErrors.DirectoryIsAnAliasedSpelling(
            argument,
            full,
            tail.IsEmpty ? resolved : Path.Join(resolved, tail),
            $"the filesystem calls '{candidate}' '{resolved}'");
    }
}
