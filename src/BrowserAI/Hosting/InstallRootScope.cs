// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;

namespace BrowserAI.Hosting;

/// <summary>
/// Whether this process's app root is one only the current user can reach, and
/// the refusal when it is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a shared root is unsafe, measured rather than reasoned.</b>
/// <c>%LocalAppData%</c> gives every Windows user their own browsers directory,
/// session index, log and <c>live\</c> marker directory.
/// <see cref="Program.AppRootVariable"/> and the installer's install-to flag
/// both defeat that, and what happens then was measured on 2026-08-20
/// ([kb](../../../kb/windows/detection.md#two-users-and-one-install-root--what-spans-users-and-what-does-not--measured-2026-08-20)):
/// the <b>file</b> locks keep working across users, because a share mode is
/// enforced by the kernel against handles and is indifferent to which token
/// opened them — but the <b>`Global\` mutexes do not</b>. The DACL the kernel
/// puts on one names LOCAL SYSTEM, the creating logon session and the creating
/// user, <b>with no group ACE at all</b>. Whichever user creates a name first
/// owns it; the other's <c>Sessions.MachineMutex.Create</c> is refused,
/// <c>Updates.LiveInstances.Join</c> catches that and returns
/// <see langword="null"/>, and a process that never joined <b>creates no
/// marker</b>. It is therefore invisible to the other user's census, which
/// answers <i>alone</i> — and an update apply then runs
/// <c>force_stop_package</c>, which terminates every process under the install
/// root, the other user's BrowserAI and its browsers included.
/// </para>
/// <para>
/// <b>The maintainer took direction (a) on 2026-08-20 — refuse at startup</b>
/// (<c>QUESTIONS.md</c> §12, answered <i>"L1 a"</i>). It takes a configuration
/// somebody chose on purpose away from them, which is why it was his to decide
/// and not this code's.
/// </para>
/// <para>
/// <b>The predicate is <i>inside the current user's profile</i>, and it is
/// answered through the filesystem rather than through strings.</b> A junction,
/// a <c>subst</c>ed drive letter, an 8.3 short component or the <c>\\?\</c>
/// prefix all make a legitimate per-user root look external to a string
/// comparison, and the second half of that is worse: a junction <i>under</i> the
/// profile pointing at <c>D:\Shared</c> would pass one. So both sides go through
/// <see cref="VolumeIdentity.DeepestExistingFinalName"/> — the same walk
/// <c>Sessions.SessionDirectoryGuard</c> uses on a caller's session directory —
/// and the comparison is on what the filesystem itself calls each of them.
/// </para>
/// <para>
/// ⚠️ <b>It narrows the hazard rather than closing it, and the gap is named
/// here rather than left to be rediscovered.</b> Three things it does not do:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>It does not read a DACL.</b> <i>Outside the profile</i> is not the
///     same predicate as <i>shared</i> — a single-user install at
///     <c>D:\Tools\BrowserAI</c> is refused for nothing, and that trade is
///     stated in <c>QUESTIONS.md</c> §12 direction (a) as the cost of taking
///     it. The converse also holds and is the surviving hole: a profile
///     directory whose ACL an administrator has widened to a group is inside
///     the profile, is genuinely shared, and is accepted here.
///   </description></item>
///   <item><description>
///     <b>An answer it could not establish is not a refusal.</b> A root whose
///     final name cannot be read — an ancestor this token may not open — is
///     served, with a warning naming what could not be established. Refusing
///     there would stop a background MCP server from starting at all on a
///     locked-down machine, which is a worse failure than the one being
///     prevented; and the accepted case is exactly today's behaviour rather
///     than a new exposure.
///   </description></item>
///   <item><description>
///     <b>It is a door, not a guard.</b> Nothing re-checks. A profile
///     redirected, or a junction re-pointed, after startup moves the root under
///     a process that was admitted correctly.
///   </description></item>
/// </list>
/// </remarks>
internal static class InstallRootScope
{
    /// <summary>
    /// How far up the tree the final-name walk climbs looking for a directory
    /// that exists.
    /// </summary>
    /// <remarks>
    /// The same bound <c>Sessions.SessionDirectoryGuard.AncestorWalkLimit</c>
    /// uses, and for the same reason: the walk costs one directory open per
    /// level. It is spelled again rather than shared across the namespace
    /// boundary because the two are independent budgets that happen to agree —
    /// an app root is a handful of levels deep and a caller's session directory
    /// can be anything.
    /// </remarks>
    public const int AncestorWalkLimit = 64;

    /// <summary>
    /// Judges an app root: may this process serve out of it, and what to say if
    /// not.
    /// </summary>
    /// <param name="root">The app root this process resolved, absolute.</param>
    /// <returns>The verdict.</returns>
    public static InstallRootVerdict Judge(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);

        if (profile is not { Length: > 0 })
        {
            return InstallRootVerdict.CouldNotEstablish(
                $"Windows reported no profile directory for this user, so BrowserAI cannot tell whether its app root '{root}' is a per-user one.");
        }

        // 1. Characters only, and first, because everything below opens a
        //    directory and an open against an unreachable share costs a measured
        //    22 s. A UNC app root is not under any local profile whatever the
        //    filesystem would say about it, so it is answered here -- but
        //    `\\?\C:\…` is NOT one: it is the extended spelling of an ordinary
        //    local path, and refusing it would refuse a per-user root for its
        //    punctuation.
        var probe = root;

        if (VolumeIdentity.IsUncOrDeviceSpelling(root))
        {
            var afterPrefix = root.Length > 4 && root[1] is '\\' && root[2] is '?' or '.' && root[3] is '\\'
                ? root[4..]
                : null;

            if (afterPrefix is null || afterPrefix.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return InstallRootVerdict.Refused(Sentence(
                    root,
                    profile,
                    "it is a UNC path, which is storage every account that can reach the share can reach"));
            }

            // The prefix is stripped for the volume question only. Everything
            // the filesystem is asked below is asked of the caller's own
            // spelling, which CreateFileW accepts either way.
            probe = afterPrefix;
        }

        // 2. The object manager only -- still no filesystem call. A mapped drive
        //    letter is a share wearing a letter, and it is the one form that
        //    would cost 22 s to discover the slow way.
        if (VolumeIdentity.Of(probe).Kind is VolumeKind.Network)
        {
            return InstallRootVerdict.Refused(Sentence(
                root,
                profile,
                $"drive '{probe[..2]}' is a mapped network drive, so the root is a share rather than per-user storage"));
        }

        // 3. And only now, one directory open per side. A `subst`ed letter is
        //    deliberately NOT refused on sight the way a session directory is:
        //    the question here is where the root really is, and the substitution
        //    resolves to a real path that may be perfectly per-user.
        var (rootFinal, rootExisting) = VolumeIdentity.DeepestExistingFinalName(root, AncestorWalkLimit);

        if (Canonical(rootFinal) is not { } resolvedAncestor)
        {
            return InstallRootVerdict.CouldNotEstablish(
                $"The filesystem would not say what it calls '{rootExisting}', so BrowserAI cannot tell whether its app root '{root}' is inside this user's profile at '{profile}'. It is serving anyway; a root that two users share loses the live-instance census silently, and this is the one line that would say so.");
        }

        var (profileFinal, profileExisting) = VolumeIdentity.DeepestExistingFinalName(profile, AncestorWalkLimit);

        if (Canonical(profileFinal) is not { } resolvedProfile)
        {
            return InstallRootVerdict.CouldNotEstablish(
                $"The filesystem would not say what it calls this user's profile at '{profileExisting}', so BrowserAI cannot tell whether its app root '{root}' is inside it. It is serving anyway; a root that two users share loses the live-instance census silently, and this is the one line that would say so.");
        }

        // Whatever was trimmed off to find an existing ancestor goes back on, so
        // a refusal names the root rather than an ancestor of it. The judgement
        // itself is unaffected either way: a tail that does not exist cannot be
        // a reparse point, so an ancestor inside the profile puts the whole path
        // inside it.
        var tail = rootExisting.Length <= root.Length ? root.AsSpan(rootExisting.Length) : [];
        var resolvedRoot = tail.IsEmpty ? resolvedAncestor : Path.Join(resolvedAncestor, tail);

        // Upper-cased on both sides and compared ordinally, which is this
        // repository's standing rule for two Windows paths -- expressed as
        // OrdinalIgnoreCase, which is the same comparison asked for by name.
        // A separator is required after the profile so that a sibling directory
        // named `C:\Users\jori-backup` cannot read as being inside `C:\Users\jori`.
        var inside = string.Equals(resolvedRoot, resolvedProfile, StringComparison.OrdinalIgnoreCase)
            || (resolvedRoot.Length > resolvedProfile.Length
                && resolvedRoot.StartsWith(resolvedProfile, StringComparison.OrdinalIgnoreCase)
                && resolvedRoot[resolvedProfile.Length] is '\\' or '/');

        return inside
            ? InstallRootVerdict.MayServeHere
            : InstallRootVerdict.Refused(Sentence(
                root,
                resolvedProfile,
                string.Equals(resolvedRoot, root, StringComparison.OrdinalIgnoreCase)
                    ? "it is outside this user's profile, so it is not storage Windows keeps per-user"
                    : $"the filesystem calls it '{resolvedRoot}', which is outside this user's profile, so it is not storage Windows keeps per-user"));
    }

    /// <summary>The final name with the extended-length prefix removed.</summary>
    /// <remarks>
    /// A UNC final name is answered <see langword="null"/> rather than stripped:
    /// <c>\\?\UNC\host\share</c> with the prefix removed reads as a rooted local
    /// path and would then be compared as one. It cannot be inside a local
    /// profile either way, and the caller reports it as unestablished, which is
    /// the honest answer for a case <c>GetDriveTypeW</c> has already said is not
    /// a share.
    /// </remarks>
    /// <param name="final">What <see cref="VolumeIdentity.DeepestExistingFinalName"/> answered.</param>
    /// <returns>The comparable path, or <see langword="null"/>.</returns>
    private static string? Canonical(string? final)
    {
        if (final is null)
        {
            return null;
        }

        var stripped = final.StartsWith(VolumeIdentity.ExtendedLengthPrefix, StringComparison.Ordinal)
            ? final[VolumeIdentity.ExtendedLengthPrefix.Length..]
            : final;

        return stripped.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase) ? null : stripped;
    }

    /// <summary>
    /// The refusal, which has to carry the remedy and not only the verdict.
    /// </summary>
    /// <param name="root">The root as this process resolved it.</param>
    /// <param name="profile">This user's profile directory.</param>
    /// <param name="why">What is wrong with the root, as a clause.</param>
    /// <returns>The whole sentence.</returns>
    private static string Sentence(string root, string profile, string why) =>
        $"BrowserAI will not serve out of the app root '{root}': {why}. "
        + "A root two Windows users can both reach is unsafe in a way nothing reports at run time: the file locks span users, but the machine-wide mutexes do not — the kernel gives one no group ACE at all, so whichever user creates a name first owns it and the other cannot join the live-instance set. "
        + "A process that never joined creates no marker, so it is invisible to the other user's census; that census answers 'nothing else is running', and applying an update then terminates every process under the install root, including the other user's browsers and whatever they were driving. "
        + $"Recovery: clear {Program.AppRootVariable} and start BrowserAI again — with no override the root is the per-user one under '{profile}', which Windows keeps separate for every account. "
        + $"If the root was set by the installer's install-to flag, reinstall without it. Nothing was started, nothing was changed, and no session, marker or browser was created under '{root}'.";
}

/// <summary>What <see cref="InstallRootScope.Judge"/> concluded.</summary>
/// <remarks>
/// <b>Three states rather than a boolean</b>, for the reason
/// <c>Updates.Liveness</c> has three: <i>could not establish</i> is neither of
/// the other two, and collapsing it into either loses the only thing that would
/// let somebody diagnose it. Here it collapses to <i>serve</i> — see
/// <see cref="InstallRootScope"/>'s remarks for why that direction and not the
/// other.
/// </remarks>
internal sealed record InstallRootVerdict
{
    /// <summary>The root is inside this user's profile. Nothing to say.</summary>
    public static readonly InstallRootVerdict MayServeHere = new() { MayServe = true };

    /// <summary>Whether BrowserAI may serve out of this root.</summary>
    public required bool MayServe { get; init; }

    /// <summary>
    /// The whole refusal, naming the root, why a shared root is unsafe and what
    /// to change. <see langword="null"/> unless <see cref="MayServe"/> is
    /// <see langword="false"/>.
    /// </summary>
    public string? Refusal { get; init; }

    /// <summary>
    /// What stopped the question being settled, when it was not. Serving
    /// continues; this is what a log line says instead of nothing.
    /// </summary>
    public string? Unestablished { get; init; }

    /// <summary>Builds the refusing verdict.</summary>
    /// <param name="refusal">The whole sentence.</param>
    /// <returns>The verdict.</returns>
    public static InstallRootVerdict Refused(string refusal) =>
        new() { MayServe = false, Refusal = refusal };

    /// <summary>Builds the undecided verdict, which still serves.</summary>
    /// <param name="why">What could not be established.</param>
    /// <returns>The verdict.</returns>
    public static InstallRootVerdict CouldNotEstablish(string why) =>
        new() { MayServe = true, Unestablished = why };
}
