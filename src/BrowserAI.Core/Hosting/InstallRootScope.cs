// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;

namespace BrowserAI.Hosting;

/// <summary>
/// Whether this process's roots -- <b>both</b> of them -- are ones only the
/// current user can reach, and the refusal when one is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a shared root is unsafe, measured rather than reasoned.</b>
/// <c>%LocalAppData%</c> gives every Windows user their own browsers directory,
/// session index and log. ⚠️ <b>Corrected 2026-09-15 (previously
/// "<see cref="LocalAppDataPaths.RootVariable"/> <b>and the installer's install-to
/// flag</b> both defeat that", and the list above also named the <c>live\</c>
/// marker directory).</b> The installer's flag cannot defeat it any more: the
/// data root is a constant and the flag moves the install root, which this
/// judgement does not read. The variable is what is left. The marker directory
/// left this root the same day and is keyed to the install root -- see the fourth
/// bullet below, which is where that gap is named rather than closed. What
/// happens when a root really is shared was measured on 2026-08-20
/// ([kb](../../../kb/windows/detection.md#two-users-and-one-install-root----what-spans-users-and-what-does-not----measured-2026-08-20)):
/// the <b>file</b> locks keep working across users, because a share mode is
/// enforced by the kernel against handles and is indifferent to which token
/// opened them -- but the <b>`Global\` mutexes do not</b>. The DACL the kernel
/// puts on one names LOCAL SYSTEM, the creating logon session and the creating
/// user, <b>with no group ACE at all</b>. Whichever user creates a name first
/// owns it; the other's <c>Sessions.MachineMutex.Create</c> is refused,
/// <c>Updates.LiveInstances.Join</c> catches that and returns
/// <see langword="null"/>, and a process that never joined <b>creates no
/// marker</b>. It is therefore invisible to the other user's census, which
/// answers <i>alone</i> -- and an update apply then runs
/// <c>force_stop_package</c>, which terminates every process under the install
/// root, the other user's BrowserAI and its browsers included.
/// </para>
/// <para>
/// <b>The maintainer took direction (a) on 2026-08-20 -- refuse at startup</b>
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
/// <see cref="VolumeIdentity.DeepestExistingFinalName"/> -- the same walk
/// <c>Sessions.CanonicalPath</c> uses on a caller's session directory --
/// and the comparison is on what the filesystem itself calls each of them.
/// </para>
/// <para>
/// ⚠️ <b>It narrows the hazard rather than closing it, and the gap is named
/// here rather than left to be rediscovered.</b> Four things it does not do:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>It does not read a DACL.</b> <i>Outside the profile</i> is not the
///     same predicate as <i>shared</i> -- a single-user install at
///     <c>D:\Tools\BrowserAI</c> is refused for nothing, and that trade is
///     stated in <c>QUESTIONS.md</c> §12 direction (a) as the cost of taking
///     it. The converse also holds and is the surviving hole: a profile
///     directory whose ACL an administrator has widened to a group is inside
///     the profile, is genuinely shared, and is accepted here.
///   </description></item>
///   <item><description>
///     <b>An answer it could not establish is not a refusal.</b> A root whose
///     final name cannot be read -- an ancestor this token may not open -- is
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
///   <item><description>
///     ⚠️ <b>CLOSED BY MECHANISM 2026-09-15, later the same day</b>
///     <i>(previously: "It judges the DATA root, and the live-instance census is
///     keyed to the INSTALL root -- added 2026-09-15 with the layout split. So
///     <c>Setup.exe --installto</c> can still put the markers and their
///     <c>Global\</c> mutex somewhere two users share, and nothing here says a
///     word about it … it is <b>open</b> rather than accepted: the row in
///     <c>HAZARDS.md</c> names the three ways out and says the choice belongs to
///     the maintainer")</i>. The maintainer took the first of those three ways
///     out, and <b>this type now judges both roots</b>.
///     <b>The predicate is the same one, said twice</b>: <i>inside the current
///     user's profile</i>, resolved through
///     <see cref="VolumeIdentity.DeepestExistingFinalName"/> on both sides, with
///     the same UNC and mapped-drive short circuits in front of it. What differs
///     is only the sentence -- a refusal names <i>both</i> roots and the remedy
///     that can actually move the one at fault, because
///     <c>BROWSERAI_ROOT</c> cannot move the install root and
///     <c>--installto</c> cannot move the data root, and a refusal naming the
///     wrong lever is a refusal nobody can act on.
///     <b>What it costs is the same trade §12(a) made</b>, now paid twice: a
///     single-user install at <c>D:\Tools\BrowserAI.app</c> is refused for
///     nothing. That is deliberate and is the first bullet above.
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
    /// The same bound <c>Sessions.CanonicalPath.AncestorWalkLimit</c>
    /// uses, and for the same reason: the walk costs one directory open per
    /// level. It is spelled again rather than shared across the namespace
    /// boundary because the two are independent budgets that happen to agree --
    /// an app root is a handful of levels deep and a caller's session directory
    /// can be anything.
    /// </remarks>
    public const int AncestorWalkLimit = 64;

    /// <summary>
    /// Judges <b>both</b> of this process's roots: may it serve, and what to say
    /// if not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Two roots since 2026-09-15 (previously <c>Judge(string root)</c>,
    /// the data root alone).</b> The data root is judged first and a refusal
    /// there ends it, because a process that may not keep its browsers where it
    /// resolved them has nothing to say about where its binary lives. Only then
    /// is the install root judged, and only when there is one: an uninstalled
    /// BrowserAI has no install root, and <c>Program</c> passes
    /// <see langword="null"/> rather than substituting the data root -- which
    /// would judge the same path twice and produce a second refusal saying the
    /// same thing in the wrong words.
    /// </para>
    /// <para>
    /// <b><i>Could not establish</i> from either side is carried out rather than
    /// dropped</b>, and both are carried when both could not be established:
    /// the caller logs it and serves anyway, so losing one of the two would hide
    /// exactly the case somebody would come looking for.
    /// </para>
    /// </remarks>
    /// <param name="dataRoot">The data root this process resolved, absolute.</param>
    /// <param name="installRoot">
    /// The install root -- the directory containing <c>current\</c> -- or
    /// <see langword="null"/> when this process is not an installed one.
    /// </param>
    /// <returns>The verdict.</returns>
    public static InstallRootVerdict Judge(string dataRoot, string? installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        var data = Judge(dataRoot, JudgedRoot.Data, dataRoot, installRoot);

        if (!data.MayServe)
        {
            return data;
        }

        // The same path judged twice would answer the same thing twice. It is
        // compared as a string rather than through the filesystem deliberately:
        // this is an optimisation, not a judgement, and the judgement below does
        // its own resolving either way.
        if (installRoot is not { Length: > 0 }
            || string.Equals(installRoot, dataRoot, StringComparison.OrdinalIgnoreCase))
        {
            return data;
        }

        var install = Judge(installRoot, JudgedRoot.Install, dataRoot, installRoot);

        if (!install.MayServe)
        {
            return install;
        }

        return (data.Unestablished, install.Unestablished) switch
        {
            (null, null) => InstallRootVerdict.MayServeHere,
            (null, not null) => install,
            (not null, null) => data,
            var (first, second) => InstallRootVerdict.CouldNotEstablish($"{first} {second}"),
        };
    }

    /// <summary>
    /// Judges one root against the profile, and composes the refusal for it.
    /// </summary>
    /// <param name="root">The root under judgement, absolute.</param>
    /// <param name="which">Which of this process's roots it is.</param>
    /// <param name="dataRoot">The data root, named in every sentence.</param>
    /// <param name="installRoot">The install root, named in every sentence.</param>
    /// <returns>The verdict.</returns>
    private static InstallRootVerdict Judge(string root, JudgedRoot which, string dataRoot, string? installRoot)
    {
        var profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);

        if (profile is not { Length: > 0 })
        {
            return InstallRootVerdict.CouldNotEstablish(
                $"Windows reported no profile directory for this user, so BrowserAI cannot tell whether its {Noun(which)} '{root}' is a per-user one.");
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
                    which,
                    root,
                    profile,
                    dataRoot,
                    installRoot,
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
                which,
                root,
                profile,
                dataRoot,
                installRoot,
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
                $"The filesystem would not say what it calls '{rootExisting}', so BrowserAI cannot tell whether its {Noun(which)} '{root}' is inside this user's profile at '{profile}'. It is serving anyway; a root that two users share loses the live-instance census silently, and this is the one line that would say so.");
        }

        var (profileFinal, profileExisting) = VolumeIdentity.DeepestExistingFinalName(profile, AncestorWalkLimit);

        if (Canonical(profileFinal) is not { } resolvedProfile)
        {
            return InstallRootVerdict.CouldNotEstablish(
                $"The filesystem would not say what it calls this user's profile at '{profileExisting}', so BrowserAI cannot tell whether its {Noun(which)} '{root}' is inside it. It is serving anyway; a root that two users share loses the live-instance census silently, and this is the one line that would say so.");
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
                which,
                root,
                resolvedProfile,
                dataRoot,
                installRoot,
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

    /// <summary>The noun a sentence calls one of the two roots.</summary>
    /// <param name="which">Which root.</param>
    /// <returns>The noun phrase.</returns>
    private static string Noun(JudgedRoot which) =>
        which is JudgedRoot.Install ? "install root" : "data root";

    /// <summary>
    /// The refusal, which has to carry the remedy and not only the verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It names BOTH roots, always, and the remedy for the one at fault
    /// -- 2026-09-15.</b> There are two roots since the layout split, they are
    /// moved by two different levers, and <b>neither lever can move the other's
    /// root</b>: <c>BROWSERAI_ROOT</c> moves the data root and cannot touch the
    /// install root, <c>Setup.exe --installto</c> moves the install root and
    /// cannot touch the data root. A refusal that named one root would be read
    /// against whichever the reader had in mind, and a refusal that offered the
    /// wrong lever would send them to change a setting that cannot help.
    /// </para>
    /// <para>
    /// <b>The consequence clause differs because the consequences differ.</b>
    /// A shared <i>data</i> root loses the live-instance census through the
    /// mutex DACL; a shared <i>install</i> root loses the same census for the
    /// same reason and costs less, because each user's browsers, session index
    /// and log are their own now -- what an apply destroys there is the other
    /// user's processes and the browsers they were driving.
    /// </para>
    /// </remarks>
    /// <param name="which">Which root is at fault.</param>
    /// <param name="root">The root as this process resolved it.</param>
    /// <param name="profile">This user's profile directory.</param>
    /// <param name="dataRoot">The data root, named whichever is at fault.</param>
    /// <param name="installRoot">The install root, or <see langword="null"/>.</param>
    /// <param name="why">What is wrong with the root, as a clause.</param>
    /// <returns>The whole sentence.</returns>
    private static string Sentence(
        JudgedRoot which,
        string root,
        string profile,
        string dataRoot,
        string? installRoot,
        string why) =>
        $"BrowserAI will not serve out of the {Noun(which)} '{root}': {why}. "
        + "A root two Windows users can both reach is unsafe in a way nothing reports at run time: the file locks span users, but the machine-wide mutexes do not -- the kernel gives one no group ACE at all, so whichever user creates a name first owns it and the other cannot join the live-instance set. "
        + "A process that never joined creates no marker, so it is invisible to the other user's census; that census answers 'nothing else is running', and applying an update then terminates every process under the install root, including the other user's browsers and whatever they were driving. "
        + $"This build has two roots and they are moved by two different levers, so both are named: the data root is '{dataRoot}' and the install root is {(installRoot is { Length: > 0 } installed ? $"'{installed}'" : "absent, because this process was not installed")}. "
        + (which is JudgedRoot.Install
            ? $"Recovery: install BrowserAI inside '{profile}' -- the default location, or 'Setup.exe --installto <a directory under that profile>'. {LocalAppDataPaths.RootVariable} cannot help here: it moves the data root and never the install root. "
            : $"Recovery: clear {LocalAppDataPaths.RootVariable} and start BrowserAI again -- with no override the data root is the per-user one under '{profile}', which Windows keeps separate for every account. The installer's --installto cannot help here: it moves the install root and never the data root. ")
        + $"Nothing was started, nothing was changed, and no session, marker or browser was created under '{root}'.";
}

/// <summary>Which of this process's two roots is under judgement.</summary>
/// <remarks>
/// <b>It exists to choose a noun and a remedy, and for nothing else.</b> The
/// predicate is identical for both -- see <see cref="InstallRootScope"/>'s
/// fourth bullet, which says so in as many words, because a second predicate
/// wearing one name is how two roots start being judged by two different rules.
/// </remarks>
internal enum JudgedRoot
{
    /// <summary>The data root: browsers, session index, log.</summary>
    Data,

    /// <summary>The install root: the binary, and what the live-instance census is keyed to.</summary>
    Install,
}

/// <summary>What <see cref="InstallRootScope.Judge(string, string?)"/> concluded.</summary>
/// <remarks>
/// <b>Three states rather than a boolean</b>, for the reason
/// <c>Updates.Liveness</c> has three: <i>could not establish</i> is neither of
/// the other two, and collapsing it into either loses the only thing that would
/// let somebody diagnose it. Here it collapses to <i>serve</i> -- see
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
