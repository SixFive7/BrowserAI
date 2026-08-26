// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Interop;

namespace BrowserAI.Sessions;

/// <summary>
/// The one path function: it normalises every alias it can resolve without
/// talking to a network redirector, and refuses everything else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Normalise first; refuse only what cannot be normalised.</b> That is the
/// inverse of what stood here until 2026-08-26, and the argument for it is that
/// the old refusals were already computing the answer they refused to use: a
/// <c>\\?\</c> prefix is four characters off the front, a <c>subst</c> is one
/// object-manager read whose result <i>is</i> the accepted spelling, and a
/// junction is one directory open on a volume already proven local. Every one of
/// those was formatted into a sentence telling the caller to call again with the
/// answer. Handing it back as a path instead adds no syscall whatsoever.
/// </para>
/// <para>
/// <b>What is still refused is refused everywhere, uniformly:</b> the network,
/// the device namespace, and the names Windows would silently rewrite. The
/// earlier build refused at <c>browserai_init</c> and <c>browserai_resume</c>
/// only, so that a session created on a share by an older build stayed
/// removable. Nothing was ever distributed, so that population is empty and the
/// exception costs more than it buys — see
/// [the decision](../../../DECISIONS.md#one-path-function-normalise-what-is-cheap-refuse-what-is-not).
/// </para>
/// <para>
/// ⚠️ <b>THE ORDER IS THE DESIGN, and it is not an optimisation.</b> Every
/// question that could refuse is asked before the one call that opens anything,
/// because a filesystem call against a share that has stopped answering costs a
/// measured <b>22,210 ms</b> — through a mapped <i>drive letter</i>, not only
/// through a UNC spelling
/// ([kb](../../../kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds))
/// — and several such calls happen inside <see cref="LockScopes.PerDirectoryGate"/>,
/// where the caller who named the dead share is not the one who waits.
/// </para>
/// <para>
/// <b>Two origins, and the axis is <i>may this call touch the filesystem</i>.</b>
/// <see cref="PathOrigin.Named"/> is a path a caller put in a tool argument, and
/// it pays for the whole sequence. <see cref="PathOrigin.Read"/> is a path
/// BrowserAI stored or a stranger published, and it runs the subset of the same
/// questions that cost nothing — <b>zero syscalls by construction</b>, because
/// everything this build writes is canonical already and a stored path that is
/// not was not written by this build. The index knows what to do with one of
/// those: it is <c>Unusable</c>, it is swept, and the next <c>init</c> or
/// <c>resume</c> records it again.
/// </para>
/// <para>
/// <b>What this deliberately accepts.</b> A drive letter that names nothing on
/// this machine is neither a network path nor an alias, so it falls through to
/// the ordinary creation failure — <i>the system cannot find the path
/// specified</i> — which already says what to do.
/// </para>
/// <para>
/// <b>And what it cannot see</b>, stated here rather than left to be
/// rediscovered:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>A component this process cannot open.</b> The walk stops at the first
///     failure that is not <i>the name does not exist</i>, and the path is then
///     served with the caller's own spelling and
///     <see cref="PathVerdict.Unestablished"/> set. Turning <i>unknown</i> into
///     a refusal would refuse ordinary directories on a locked-down machine.
///   </description></item>
///   <item><description>
///     <b>Anything that becomes true after the answer.</b> A <c>subst</c>, a
///     <c>mklink /J</c> or a drive remapped while a session is open moves the
///     directory underneath a session that was resolved correctly. <b>This is a
///     door, not a guard</b>, and nothing here re-checks. See the hazard index.
///   </description></item>
///   <item><description>
///     <b>Case.</b> Two spellings differing only in case are one session by
///     design — the claim <see cref="SessionPath.Key"/> makes — so a case
///     difference is not an alias and is never refused. What the canonical form
///     does carry is the filesystem's own casing, because that is what
///     <c>GetFinalPathNameByHandleW</c> reports.
///   </description></item>
/// </list>
/// </remarks>
internal static class CanonicalPath
{
    /// <summary>
    /// How far up the tree the final-name check will walk looking for a
    /// directory that exists.
    /// </summary>
    /// <remarks>
    /// A bound rather than a loop to the root, because the walk costs one
    /// directory open per level and a caller can name a path of any depth. Past
    /// this the path is served unverified, which is the same answer an unopenable
    /// component gets.
    /// </remarks>
    public const int AncestorWalkLimit = 64;

    /// <summary>How many <c>subst</c> hops one path may stand behind.</summary>
    /// <remarks>
    /// <b>A bound because <c>DefineDosDevice</c> will happily build a cycle.</b>
    /// Each hop is one object-manager read and one string rewrite, so the cost is
    /// not what is being bounded — termination is.
    /// </remarks>
    public const int SubstitutionChainLimit = 8;

    /// <summary>Every reserved DOS device name, which no path segment may be.</summary>
    /// <remarks>
    /// <b>The stem is what is tested, not the whole segment.</b> <c>NUL.png</c>
    /// opens the device exactly as <c>NUL</c> does, whatever extension follows —
    /// and <c>Path.GetFullPath</c> rewrites a bare one into <c>\\.\NUL</c>
    /// outright (measured 2026-08-26, .NET 10.0.11). This list was
    /// <c>ArtifactFilename</c>'s until that type was deleted with the filename
    /// gate; it applied to a <c>filename</c> argument and never to a
    /// <c>directory</c> one, which was an asymmetry rather than a decision.
    /// </remarks>
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// The canonical spelling of a path, or the sentence saying why there is not
    /// one.
    /// </summary>
    /// <param name="value">The path, in any spelling.</param>
    /// <param name="origin">Whether a caller named it or BrowserAI stored it.</param>
    /// <param name="argument">
    /// Which tool argument the path arrived in, so a refusal can tell the caller
    /// what to change.
    /// </param>
    /// <returns>The verdict. It returns; it never throws.</returns>
    public static PathVerdict Of(string? value, PathOrigin origin, string argument)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new PathVerdict { Refusal = SessionErrors.DirectoryNotAbsolute(argument, value ?? string.Empty) };
        }

        return origin is PathOrigin.Read ? Stored(value, argument) : Named(value, argument);
    }

    /// <summary>
    /// The case-folded, separator-terminated prefix that decides whether a
    /// session is beneath a root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The one derivation, and the reason it is a member rather than two
    /// lines at each call site.</b> <c>SessionManager.Subtree</c> and
    /// <c>SessionManager.Beneath</c> each derived it — case-fold, then append a
    /// separator — while <see cref="SessionIndex"/>'s own remark forbade
    /// re-deriving the predicate in as many words. It was benign because
    /// <c>Beneath</c>'s input happened to be canonical; what makes it worth a
    /// member is that nothing said so and nothing would have noticed when it
    /// stopped being true. <c>HouseRuleTests.ThePrefixIsDerivedInOnePlaceAndTheRestOfTheTreeAsksForIt</c>
    /// is what keeps it one.
    /// </para>
    /// <para>
    /// <b>The volume root is the case a re-derivation gets wrong.</b>
    /// <c>C:\</c> already ends with a separator, and appending a second produces
    /// a prefix nothing is ever under.
    /// </para>
    /// </remarks>
    /// <param name="canonical">A canonical path.</param>
    /// <returns>The prefix.</returns>
    public static string PrefixOf(string canonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        var folded = canonical.ToUpperInvariant();

        return folded.EndsWith(Path.DirectorySeparatorChar) ? folded : folded + Path.DirectorySeparatorChar;
    }

    /// <summary>The full treatment: normalise, resolve, refuse.</summary>
    private static PathVerdict Named(string value, string argument)
    {
        var spelling = value;

        for (var hop = 0; ; hop++)
        {
            // 1. Characters only. Every UNC and device spelling begins with two
            //    separators and nothing else in Win32 path syntax does.
            if (VolumeIdentity.IsUncOrDeviceSpelling(spelling))
            {
                if (Prefixed(spelling) is not { } afterPrefix)
                {
                    return Refuse(SessionErrors.DirectoryOnANetworkPath(argument, value, "it is a UNC path"));
                }

                // \\?\UNC\host\share and \\.\UNC\host\share are the extended
                // spellings of a UNC path, and they are answered as NETWORK
                // rather than by stripping: stripping would hand back something
                // that earns the network refusal on the very next turn, which
                // breaks the one rule this catalogue is built on.
                if (afterPrefix.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
                {
                    return Refuse(SessionErrors.DirectoryOnANetworkPath(
                        argument,
                        value,
                        $"it is the extended spelling of the UNC path '\\\\{afterPrefix[4..]}'"));
                }

                if (spelling[2] is '.')
                {
                    return Refuse(SessionErrors.DirectorySpelledInTheDeviceNamespace(argument, value, afterPrefix));
                }

                spelling = afterPrefix;
            }

            // 2. Rooted at a drive letter, decided by hand rather than by
            //    Path.IsPathFullyQualified, so that a relative path, a
            //    drive-relative `C:foo` and a rooted-but-unqualified `\foo` all
            //    reach the one refusal that names what an absolute path is. Each
            //    of the three would otherwise be resolved against something this
            //    process happens to have and the caller never chose.
            if (!IsDriveRooted(spelling))
            {
                return Refuse(SessionErrors.DirectoryNotAbsolute(argument, value));
            }

            // 3. The segment shapes, and they are asked BEFORE GetFullPath
            //    because GetFullPath does not reject them -- it rewrites them.
            if (UnkeepableName(spelling) is { } unkeepable)
            {
                return Refuse(SessionErrors.DirectoryUnusable(argument, value, unkeepable));
            }

            string full;

            // 4. Free unless the string carries a `~`, in which case it is one
            //    filesystem touch that expands the 8.3 spelling -- which is why
            //    a short name arrives here already canonical.
            try
            {
                full = Trim(Path.GetFullPath(spelling));
            }
            catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Refuse(SessionErrors.DirectoryUnusable(argument, value, failure.Message));
            }

            // 5. The object manager only -- no filesystem call, so an
            //    unreachable share cannot be reached from here.
            var volume = VolumeIdentity.Of(full);

            if (volume.Kind is VolumeKind.Network)
            {
                return Refuse(SessionErrors.DirectoryOnANetworkPath(
                    argument,
                    value,
                    $"drive '{full[..2]}' is a mapped network drive"));
            }

            if (volume is { Kind: VolumeKind.Substituted, SubstitutedFor: { } target })
            {
                if (hop >= SubstitutionChainLimit)
                {
                    return Refuse(SessionErrors.DirectoryUnusable(
                        argument,
                        value,
                        $"drive '{full[..2]}' stands in for a drive that stands in for another, more than {SubstitutionChainLimit.ToString(CultureInfo.InvariantCulture)} deep, so there is no directory at the end of it."));
                }

                // ⚠️ THE WHOLE SEQUENCE RUNS AGAIN ON THE TARGET, which is what
                // makes the network question get asked about it. The refusal
                // this replaced built the target spelling and handed it back
                // WITHOUT re-asking -- so `subst X: Z:\dir` over a mapped Z:
                // named a path that earned the network refusal on the next turn.
                spelling = Path.Join(target, full.AsSpan(2));
                continue;
            }

            return FinalName(full, value, argument);
        }
    }

    /// <summary>The last step: what the filesystem itself calls this path.</summary>
    private static PathVerdict FinalName(string full, string value, string argument)
    {
        // Walk up only while the answer is "this name does not exist", which is
        // the ordinary state of `init` on a directory nothing has created yet.
        // A tail that does not exist cannot be a reparse point, so proving the
        // deepest EXISTING ancestor unaliased proves the whole path unaliased.
        var (final, candidate) = VolumeIdentity.DeepestExistingFinalName(full, AncestorWalkLimit);

        if (final is null)
        {
            // Unverifiable, and served anyway with the caller's own spelling.
            // Refusing here would refuse an ordinary directory whose parent this
            // process may not open; collapsing it into a plain success would
            // lose the only thing that would let somebody diagnose it.
            return new PathVerdict
            {
                Canonical = full,
                Unestablished = $"the filesystem would not say what it calls '{candidate}', so '{value}' is being taken as spelled",
            };
        }

        var resolved = final.StartsWith(VolumeIdentity.ExtendedLengthPrefix, StringComparison.Ordinal)
            ? final[VolumeIdentity.ExtendedLengthPrefix.Length..]
            : final;

        // A final name in UNC form means the letter resolved through a
        // redirector after all -- the object manager said otherwise, and the
        // filesystem is the better authority. Reporting a share as a local
        // directory would put a session's lock on the other side of one.
        if (resolved.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(SessionErrors.DirectoryOnANetworkPath(
                argument,
                value,
                $"drive '{full[..2]}' resolves to the network path '\\\\{resolved[4..]}'"));
        }

        // Whatever was trimmed off to find an existing ancestor goes back on, so
        // the answer is the caller's own directory rather than an ancestor of
        // it.
        var tail = candidate.Length <= full.Length ? full.AsSpan(candidate.Length) : [];

        return new PathVerdict { Canonical = Trim(tail.IsEmpty ? resolved : Path.Join(resolved, tail)) };
    }

    /// <summary>
    /// The checks only, on a path BrowserAI stored or a stranger published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Zero syscalls, and the way that is guaranteed is that no step here is
    /// a rewrite.</b> Every question is answered on the characters: a rewrite
    /// would mean <c>Path.GetFullPath</c>, which touches the filesystem for an
    /// 8.3 expansion, or the object manager, which is the volume question. A
    /// stored path that fails any of them is not the spelling this build writes,
    /// therefore was not written by this build.
    /// </para>
    /// <para>
    /// <b>One thing is trimmed rather than refused, and it is deliberate:</b> a
    /// trailing separator. The <c>session</c> argument of every forwarded call
    /// comes back through here, a model that appends one to a path BrowserAI
    /// gave it has not named a different directory, and refusing would turn a
    /// live session into <i>no session at all</i> for a character that carries no
    /// meaning. Trimming is free.
    /// </para>
    /// <para>
    /// ⚠️ <b>A <c>~</c> is NOT refused, and the design this was built from said
    /// it should be.</b> The stated reason there was that refusing one makes the
    /// 8.3 expansion unreachable — but nothing here calls <c>GetFullPath</c> at
    /// all, so the justification is circular, and a directory somebody genuinely
    /// named <c>my~project</c> would be unfindable for the life of the session.
    /// The 8.3 spelling it would have caught cannot be in a record this build
    /// wrote, because the writer resolved it.
    /// </para>
    /// </remarks>
    private static PathVerdict Stored(string value, string argument)
    {
        var trimmed = Trim(value);

        if (VolumeIdentity.IsUncOrDeviceSpelling(trimmed))
        {
            return NotRecorded(value, "it is a UNC or device spelling, and BrowserAI records neither");
        }

        if (!IsDriveRooted(trimmed))
        {
            return NotRecorded(value, "it is not a rooted local drive-letter path");
        }

        if (trimmed.Contains('/', StringComparison.Ordinal))
        {
            return NotRecorded(value, "it is spelled with forward slashes");
        }

        foreach (var segment in Segments(trimmed))
        {
            if (segment is "." or "..")
            {
                return NotRecorded(value, $"it carries a '{segment}' segment, which BrowserAI collapses before it records anything");
            }
        }

        return UnkeepableName(trimmed) is { } unkeepable
            ? NotRecorded(value, unkeepable)
            : new PathVerdict { Canonical = trimmed };
    }

    /// <summary>
    /// Whether any segment names something Windows would not keep verbatim, as a
    /// clause.
    /// </summary>
    /// <remarks>
    /// <b>Measured 2026-08-26 on .NET 10.0.11, Windows 11 Pro 26200</b>, which is
    /// why each of these is a refusal rather than something left to fail later:
    /// <c>Path.GetFullPath(@"C:\work\sess.")</c> answers <c>C:\work\sess</c>,
    /// the same with a trailing space answers the same, and
    /// <c>Path.GetFullPath(@"C:\work\NUL")</c> answers <c>\\.\NUL</c> — a device.
    /// A caller told nothing would get a session directory whose name is not the
    /// one it asked for, which is the two-spellings failure arriving by the other
    /// door.
    /// </remarks>
    private static string? UnkeepableName(string spelling)
    {
        foreach (var segment in Segments(spelling))
        {
            // `.` and `..` are navigation rather than names, and GetFullPath
            // collapses them. Testing them for a trailing dot would refuse every
            // ordinary relative-looking spelling of an absolute path.
            if (segment is "." or "..")
            {
                continue;
            }

            if (segment[^1] is '.' or ' ')
            {
                return $"'{segment}' ends with a {(segment[^1] is ' ' ? "space" : "dot")}, which Windows silently strips — "
                    + $"so the directory would be '{segment.TrimEnd(' ', '.')}' rather than the name you asked for.";
            }

            if (segment.IndexOf(':', StringComparison.Ordinal) is var stream and >= 0)
            {
                return $"'{segment}' names an alternate data stream rather than a directory: everything after the ':' is a stream inside '{segment[..stream]}'.";
            }

            var stem = segment.IndexOf('.', StringComparison.Ordinal) is var dot and > 0 ? segment[..dot] : segment;

            if (Array.Exists(ReservedDeviceNames, name => string.Equals(name, stem, StringComparison.OrdinalIgnoreCase)))
            {
                return $"'{segment}' is the reserved device name '{stem.ToUpperInvariant()}', which opens a device rather than a directory whatever follows it.";
            }

            foreach (var character in segment)
            {
                if (character < ' ')
                {
                    return $"'{segment}' contains the control character U+{((int)character).ToString("X4", CultureInfo.InvariantCulture)}, which cannot appear in a Windows file name.";
                }

                if (character is '<' or '>' or '"' or '|' or '?' or '*')
                {
                    return $"'{segment}' contains '{character}', which cannot appear in a Windows file name.";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The clause a stored path that is not canonical is refused with.
    /// </summary>
    /// <remarks>
    /// <b>A clause rather than a catalogue sentence, because nothing a caller
    /// asked for was refused.</b> It becomes an index entry's <c>Problem</c> and
    /// a stray sweep's reason for sparing a process — two places a reader looks
    /// after the fact, neither of them an answer to a tool call.
    /// </remarks>
    private static PathVerdict NotRecorded(string value, string why) =>
        new() { Refusal = $"'{value}' is not the spelling BrowserAI records: {why}" };

    /// <summary>Every named segment of a drive-rooted path, separators dropped.</summary>
    private static string[] Segments(string spelling) =>
        spelling.Length <= 2
            ? []
            : spelling[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether a path is rooted at a drive letter — <c>X:\</c>.</summary>
    private static bool IsDriveRooted(string spelling) =>
        spelling is { Length: >= 3 }
        && char.IsAsciiLetter(spelling[0])
        && spelling[1] is ':'
        && spelling[2] is '\\' or '/';

    /// <summary>
    /// What follows a <c>\\?\</c> or <c>\\.\</c> prefix, or <see langword="null"/>
    /// when the spelling carries neither.
    /// </summary>
    private static string? Prefixed(string spelling) =>
        spelling.Length > 4 && spelling[1] is '\\' && spelling[2] is '?' or '.' && spelling[3] is '\\'
            ? spelling[4..]
            : null;

    /// <summary>
    /// Trailing separators off, except the one that makes a volume root a volume
    /// root.
    /// </summary>
    /// <remarks>
    /// <b><c>C:\</c> and <c>C:</c> are different things.</b> The second is
    /// drive-relative and means <i>the current directory on C</i>, so a trim that
    /// produced it would be a silently different answer — which is exactly why
    /// the volume-root refusal belongs to <see cref="SessionPath"/> and not
    /// here: <c>browserai_list</c> is pointed at a volume root on purpose.
    /// </remarks>
    private static string Trim(string full)
    {
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return trimmed.Length is 2 && trimmed[1] is ':' ? trimmed + Path.DirectorySeparatorChar : trimmed;
    }

    private static PathVerdict Refuse(string refusal) => new() { Refusal = refusal };
}

/// <summary>Where a path came from, which decides what may be spent on it.</summary>
internal enum PathOrigin
{
    /// <summary>
    /// A caller supplied it in a tool argument. Spend the syscalls: this is the
    /// path that becomes a directory, a lock and an identity.
    /// </summary>
    Named,

    /// <summary>
    /// BrowserAI stored it, or a stranger published it. Spend nothing — the
    /// writer already paid, and a stored path that fails the free checks was not
    /// written by this build.
    /// </summary>
    Read,
}

/// <summary>What <see cref="CanonicalPath.Of"/> made of a path.</summary>
/// <remarks>
/// <b>Three outcomes rather than two, and the third is load-bearing.</b>
/// <see cref="Canonical"/> set with <see cref="Unestablished"/> also set is the
/// unopenable-ancestor case: the path is served with the caller's own spelling
/// and the reason it could not be verified travels with it. Collapsing that into
/// either of the other two loses the only thing that would let somebody diagnose
/// it — the same shape, and the same argument, as <c>InstallRootVerdict</c>.
/// </remarks>
internal sealed record PathVerdict
{
    /// <summary>
    /// The filesystem's own spelling, set unless <see cref="Refusal"/> is.
    /// </summary>
    public string? Canonical { get; init; }

    /// <summary>
    /// Why the path was refused, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Two shapes, because a refusal has two audiences.</b> For
    /// <see cref="PathOrigin.Named"/> it is a whole <see cref="SessionErrors"/>
    /// sentence, written for the model that will read it as an answer. For
    /// <see cref="PathOrigin.Read"/> it is a clause — nothing a caller asked for
    /// was refused, so there is no answer to write; it becomes an index entry's
    /// <c>Problem</c> and a stray sweep's reason for sparing a process.
    /// </remarks>
    public string? Refusal { get; init; }

    /// <summary>
    /// Why the spelling could not be verified, or <see langword="null"/>. Set
    /// only beside a <see cref="Canonical"/>.
    /// </summary>
    public string? Unestablished { get; init; }
}
