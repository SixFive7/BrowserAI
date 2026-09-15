// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// What kind of volume a path is on, and what the filesystem calls that path —
/// answered without ever risking a network round trip on the way to finding
/// out.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole type exists because of an ordering constraint.</b> A filesystem
/// call against an unreachable share costs a measured <b>22,210 ms</b> on this
/// machine — through a mapped <i>drive letter</i>, not only through a UNC
/// spelling ([kb](../../../kb/windows/detection.md#a-mapped-drive-letter-is-a-network-path-and-costs-the-same-22-seconds)).
/// So anything that decides <i>is this a network path</i> must be answerable
/// before the first filesystem call, and the three questions below are ordered
/// so that they are:
/// </para>
/// <list type="number">
///   <item><description>
///     <see cref="IsUncOrDeviceSpelling"/> — characters only, no syscall at all.
///   </description></item>
///   <item><description>
///     <see cref="Of"/> — the object manager only. <c>GetDriveTypeW</c> and
///     <c>QueryDosDeviceW</c> read the DOS device symbolic link; neither opens a
///     file. Measured <b>0.0103–0.0212 ms</b> per <c>QueryDosDeviceW</c> over
///     1,000 calls, and <b>0.9 ms</b> for <c>GetDriveTypeW</c> against a letter
///     mapped to a dead hostname <i>immediately after</i> a <c>File.Exists</c> on
///     that same letter took 22 s.
///   </description></item>
///   <item><description>
///     <see cref="FinalNameOf"/> — one directory open. <b>Only ever called once
///     <see cref="Of"/> has said the volume is local.</b>
///   </description></item>
/// </list>
/// <para>
/// ⚠️ ***Corrected 2026-08-26 (previously step 3 ended "…which is what keeps a
/// bounded call bounded", and the same claim was implied at
/// <see cref="FinalNameOf"/> and <see cref="DeepestExistingFinalName"/>).***
/// <b>The answer is bounded; the cost is not, and the code claimed both.</b>
/// <see cref="Of"/> reads the <b>drive letter's</b> DOS device link and nothing
/// else — it judges the volume, not the path — while
/// <see cref="DeepestExistingFinalName"/> then issues up to its walk limit of
/// <c>CreateFileW</c> calls along components it has judged nothing about. A
/// directory symbolic link or a volume mount point anywhere in that chain,
/// pointing at a share that has stopped answering, is traversed by the open, and
/// the cost is the redirector's rather than the object manager's:
/// <c>Directory.Exists</c> against a dead host re-measured on this machine
/// 2026-08-26 at <b>22,157 ms</b>, per call. It runs inside
/// <c>Sessions.CanonicalPath.FinalName</c>, which <c>SessionLock.TryAcquire</c>
/// reaches under the per-directory gate — where the caller who named the path is
/// not the one who waits.
/// </para>
/// <para>
/// <b>Recorded rather than fixed, and the reason is written down where the
/// decision is</b> — [the hazard index](../../../HAZARDS.md#hazard-index). The
/// composition has <b>not</b> been measured end to end: this account can create
/// neither a directory symlink nor a mount point, having neither
/// <c>SeCreateSymbolicLinkPrivilege</c> nor Developer Mode, so the mechanism is
/// read from the code and the cost from the measurement above. The cheap fix
/// exists and is not taken: one extra open per level with
/// <c>FILE_FLAG_OPEN_REPARSE_POINT</c>, refusing a reparse point whose target is
/// a UNC path. What is <i>not</i> available is a watchdog, because
/// [a clock cannot be honest here](../../../TESTING.md#every-duration-is-a-hang-detector-or-it-is-a-defect).
/// </para>
/// <para>
/// <b>Why <c>QueryDosDeviceW</c> rather than only <c>GetDriveTypeW</c>.</b> They
/// answer different questions and this product needs both. Measured 2026-08-19:
/// a <c>subst</c>ed letter reports <c>DRIVE_FIXED</c> — it is genuinely fixed
/// storage — while its DOS device target is <c>\??\C:\…</c>, a symbolic link to
/// another DOS path rather than to a device. That is the discriminator for an
/// <i>alias</i>. Conversely a mapped letter's target is
/// <c>\Device\LanmanRedirector\…</c>, but pattern-matching device names means
/// enumerating every redirector that exists (SMB, WebDAV, NFS, and whatever
/// ships next), so the <i>network</i> question is asked of
/// <c>GetDriveTypeW</c>, which is the operating system's own classification.
/// </para>
/// <para>
/// ⚠️ <b>And <c>QueryDosDeviceW</c> is asked FIRST — corrected 2026-08-26,
/// previously <c>GetDriveTypeW</c> was.</b> The 0.9 ms figure above holds for a
/// letter that is not a substitution and for no other. Measured 2026-08-26,
/// <c>subst W: V:\dir</c> over a <c>V:</c> mapped to an unroutable address:
/// <c>GetDriveTypeW("W:\")</c> took <b>21,035 ms</b> and then answered
/// <c>DRIVE_NO_ROOT_DIR</c>, while the mapped letter itself answered
/// <c>DRIVE_REMOTE</c> in <b>1 ms</b>
/// ([kb](../../../kb/windows/detection.md#getdrivetypew-through-a-subst-onto-a-mapped-drive-costs-the-full-21-seconds--measured-2026-08-26)).
/// It resolves the substitution and classifies what is at the end of it, so on
/// that one shape the call this type is ordered around <i>was</i> the network
/// call. Unwinding the substitution first means <c>GetDriveTypeW</c> is only
/// ever asked about a letter that is not one.
/// </para>
/// </remarks>
internal static partial class VolumeIdentity
{
    // GetDriveTypeW's documented return values. Only these three are named:
    // every other value means "a local block device of some kind", and this type
    // deliberately does not care which.
    private const uint DriveNoRootDir = 1;
    private const uint DriveRemote = 4;

    // CreateFileW: no access at all is enough for GetFinalPathNameByHandleW, and
    // it is what lets this open a directory the process cannot read.
    private const uint NoAccess = 0;
    private const uint FileShareAll = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    // GetFinalPathNameByHandleW: VOLUME_NAME_DOS | FILE_NAME_NORMALIZED, both 0.
    private const uint FinalPathDosNormalised = 0;

    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    /// <summary>
    /// The <c>\\?\</c> prefix, which every <see cref="FinalNameOf"/> answer
    /// carries and no caller-supplied path may.
    /// </summary>
    public const string ExtendedLengthPrefix = @"\\?\";

    /// <summary>
    /// The object manager's prefix for a DOS path. A drive letter whose target
    /// starts with this is standing in for another letter's directory rather
    /// than for a device.
    /// </summary>
    private const string DosPathPrefix = @"\??\";

    /// <summary>
    /// Whether a path is spelled in the UNC or device namespace, decided on the
    /// characters and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two separators is the whole test</b>, and it is exhaustive over the
    /// spellings that exist: <c>\\host\share</c>, <c>//host/share</c>,
    /// <c>\\?\C:\…</c>, <c>\\?\UNC\host\share</c> and the device form
    /// <c>\\.\</c>. Nothing else in Win32 path syntax begins with two
    /// separators.
    /// </para>
    /// <para>
    /// <b>It is a spelling test and says so in its name.</b> A <c>Z:\</c> that
    /// resolves to a share is a network path and is invisible here; that half is
    /// <see cref="Of"/>'s, and the split is deliberate — this one is safe to run
    /// on a string of unknown provenance, and <see cref="Of"/> needs a drive
    /// letter.
    /// </para>
    /// </remarks>
    /// <param name="path">The path to judge.</param>
    /// <returns><see langword="true"/> for a UNC or device spelling.</returns>
    public static bool IsUncOrDeviceSpelling(string path) =>
        path is { Length: >= 2 }
        && path[0] is '\\' or '/'
        && path[1] is '\\' or '/';

    /// <summary>
    /// What a rooted drive-letter path's volume is, using the object manager
    /// only.
    /// </summary>
    /// <param name="path">
    /// A rooted local drive-letter path — <c>X:\…</c>. Anything else is
    /// <see cref="VolumeKind.NotADriveLetter"/>, including every spelling
    /// <see cref="IsUncOrDeviceSpelling"/> catches.
    /// </param>
    /// <returns>What kind of volume the letter names, and what it stands for.</returns>
    public static VolumeReading Of(string path)
    {
        if (path is not { Length: >= 3 } || !char.IsAsciiLetter(path[0]) || path[1] is not ':' || path[2] is not ('\\' or '/'))
        {
            return new VolumeReading(VolumeKind.NotADriveLetter, null);
        }

        // "X:" for QueryDosDeviceW (a DOS device name, no trailing separator);
        // "X:\" for GetDriveTypeW (a root path, separator required -- without it
        // the function reads the process's current directory on that drive).
        var device = path[..2];

        // ⚠️ ASKED FIRST, AND THE ORDER WAS THE OTHER WAY AROUND UNTIL
        // 2026-08-26 — *previously `GetDriveTypeW` first, with the remark
        // "Deliberately asked AFTER the network question. A letter that does not
        // resolve at all is reported as such rather than as an alias, because
        // the two have completely different fixes."* That reasoning is still
        // true and the order it produced was still wrong, because
        // `GetDriveTypeW` is not the cheap call this type says it is when the
        // letter is a substitution: it resolves the substitution and then
        // classifies whatever is at the end of it.
        //
        // Measured 2026-08-26 on this machine, `subst W: V:\dir` over a `V:`
        // mapped to an unroutable address: `GetDriveTypeW("V:\")` answers
        // DRIVE_REMOTE in **1 ms**, and `GetDriveTypeW("W:\")` takes
        // **21,035 ms** and then answers DRIVE_NO_ROOT_DIR — so the one call
        // this whole type is ordered around paid the exact cost it exists to
        // prevent, and misclassified the letter afterwards
        // ([kb](../../../kb/windows/detection.md#getdrivetypew-through-a-subst-onto-a-mapped-drive-costs-the-full-21-seconds--measured-2026-08-26)).
        //
        // `QueryDosDeviceW` reads the DOS device symbolic link and nothing else,
        // so unwinding the substitution first means `GetDriveTypeW` is only ever
        // asked about a letter that is NOT one — where it is the 0.9 ms call
        // this type was written around. Every other answer is unchanged: a
        // letter that resolves to nothing is still `NoSuchDrive`, and a letter
        // whose target is a device is still classified by the operating system
        // rather than by a list of redirector names kept here.
        if (QueryTarget(device) is not { } target)
        {
            return new VolumeReading(VolumeKind.NoSuchDrive, null);
        }

        // `\??\` is the object manager's own prefix for a DOS path, so a target
        // that starts with it is a letter pointing at another letter's directory
        // -- which is exactly what `subst` and DefineDosDevice without
        // DDD_RAW_TARGET_PATH create. A real volume's target is a device
        // (`\Device\HarddiskVolume3`). The target is carried out because it is
        // the spelling the substitution stands for, which is what lets the
        // caller re-ask every question about the target without a single
        // filesystem call.
        if (target.StartsWith(DosPathPrefix, StringComparison.Ordinal))
        {
            return new VolumeReading(VolumeKind.Substituted, target[DosPathPrefix.Length..]);
        }

        var kind = GetDriveTypeW(string.Concat(device, @"\"));

        return kind switch
        {
            DriveRemote => new VolumeReading(VolumeKind.Network, null),
            DriveNoRootDir => new VolumeReading(VolumeKind.NoSuchDrive, null),
            _ => new VolumeReading(VolumeKind.Local, null),
        };
    }

    /// <summary>
    /// <see cref="Of"/>, following a <c>subst</c> to whatever volume is at the
    /// end of the chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For a caller that wants the kind and not the spelling.</b>
    /// <see cref="Of"/> stops at the first hop and hands back the target,
    /// because a caller that is going to rewrite the path needs the string;
    /// a caller that only asks <i>is this the network</i> would otherwise have
    /// to write this loop itself, and a letter <c>subst</c>ed onto a mapped
    /// drive would read as local to every one that did not.
    /// </para>
    /// <para>
    /// <b>Still no filesystem call anywhere in it</b> — one
    /// <c>QueryDosDeviceW</c> per hop, plus one <c>GetDriveTypeW</c> at the end.
    /// A chain that does not terminate inside <paramref name="chainLimit"/>
    /// answers <see cref="VolumeKind.NoSuchDrive"/>, because there is no volume
    /// at the end of it.
    /// </para>
    /// </remarks>
    /// <param name="path">A path in any spelling.</param>
    /// <param name="chainLimit">How many substitution hops to follow.</param>
    /// <returns>The kind of volume at the end of the chain.</returns>
    public static VolumeReading Through(string path, int chainLimit)
    {
        var spelling = path;

        for (var hop = 0; hop < chainLimit; hop++)
        {
            var reading = Of(spelling);

            if (reading is not { Kind: VolumeKind.Substituted, SubstitutedFor: { } target })
            {
                return reading;
            }

            spelling = Path.Join(target, spelling.AsSpan(2));
        }

        return new VolumeReading(VolumeKind.NoSuchDrive, null);
    }

    /// <summary>
    /// What the filesystem itself calls a directory: the normalised path of the
    /// object an open handle to it lands on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This resolves every alias in one call</b>, measured 2026-08-19 at
    /// <b>0.071 ms</b> over 200 calls: an 8.3 short component, a junction, a
    /// symlink, a <c>subst</c>ed letter and the <c>\\?\</c> prefix all come back
    /// as the true path. It is what makes the refusal exhaustive without a
    /// string test per alias form.
    /// </para>
    /// <para>
    /// ⚠️ <b>Never call this on a path <see cref="Of"/> has not already found
    /// local.</b> It opens a directory, and an open against an unreachable share
    /// is the 22-second call this whole type is ordered around. <b>That
    /// precondition bounds the DRIVE LETTER and nothing deeper</b> — a reparse
    /// point in the middle of the path is traversed by this open, and the type's
    /// own remarks carry the correction and the hazard row.
    /// </para>
    /// <para>
    /// The answer carries the <see cref="ExtendedLengthPrefix"/>; stripping it is
    /// the caller's, because a caller comparing against a UNC final name
    /// (<c>\\?\UNC\host\share</c>) must not strip it into something that looks
    /// rooted.
    /// </para>
    /// </remarks>
    /// <param name="directory">An existing local directory.</param>
    /// <returns>
    /// The final path, or <see langword="null"/> when the directory does not
    /// exist or the handle could not be opened. <b>Absence is not a negative
    /// answer</b> — see <see cref="NameDoesNotExist"/>.
    /// </returns>
    public static unsafe string? FinalNameOf(string directory)
    {
        using var handle = CreateFileW(
            directory,
            NoAccess,
            FileShareAll,
            IntPtr.Zero,
            OpenExisting,
            // Required for a directory: without it CreateFileW refuses one.
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return null;
        }

        // Asked for the size first. The documented contract differs between the
        // two calls -- a short buffer answers the required length INCLUDING the
        // terminator, a successful one answers the length EXCLUDING it -- so
        // `written >= required` is a buffer that was not big enough after all
        // rather than a success, and it reads as "cannot say".
        var required = GetFinalPathNameByHandleW(handle, null, 0, FinalPathDosNormalised);

        if (required is 0)
        {
            return null;
        }

        var buffer = new char[required];

        fixed (char* target = buffer)
        {
            var written = GetFinalPathNameByHandleW(handle, target, required, FinalPathDosNormalised);

            return written is 0 || written >= required ? null : new string(target, 0, (int)written);
        }
    }

    /// <summary>
    /// The filesystem's own name for the deepest ancestor of a path that
    /// exists, and which ancestor that turned out to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One walk, two callers, because it is one discipline.</b>
    /// <c>Sessions.CanonicalPath</c> asks it for the filesystem's own name for a
    /// caller's session directory;
    /// <c>Hosting.InstallRootScope</c> asks it to decide whether this process's
    /// app root is inside the current user's profile. Both are the same
    /// question — <i>what does the filesystem call this?</i> — and a second walk
    /// written beside this one would be a second answer to it.
    /// </para>
    /// <para>
    /// <b>It walks up only while the answer is <i>this name does not exist</i></b>,
    /// which is the ordinary state of a path nothing has created yet. A tail
    /// that does not exist cannot be a reparse point, so proving the deepest
    /// existing ancestor unaliased proves the whole path unaliased. Any other
    /// failure — <c>ERROR_ACCESS_DENIED</c> above all — stops the walk with
    /// <see langword="null"/>, because walking past it would turn <i>unknown</i>
    /// into a confident answer read off an ancestor.
    /// </para>
    /// <para>
    /// ⚠️ <b>Never call this on a path <see cref="Of"/> has not already found
    /// local</b> — it opens a directory, which is
    /// <see cref="FinalNameOf"/>'s 22-second hazard.
    /// </para>
    /// <para>
    /// ⚠️ <b>And <see cref="Of"/> is not sufficient, which is the correction
    /// this walk carries — 2026-08-26.</b> It judges the drive letter; this loop
    /// opens up to <paramref name="walkLimit"/> components nothing has judged, so
    /// a directory symlink or a volume mount point pointing at a dead share is
    /// followed here at the redirector's cost rather than the object manager's.
    /// The bound on this loop is a bound on the number of syscalls, never on what
    /// one of them may cost. See the type's own remarks and the hazard row.
    /// </para>
    /// </remarks>
    /// <param name="path">The path to resolve. Need not exist.</param>
    /// <param name="walkLimit">
    /// How many levels the walk may climb. A bound rather than a loop to the
    /// root, because the walk costs one directory open per level and a caller
    /// can name a path of any depth.
    /// </param>
    /// <returns>
    /// The final name — still carrying <see cref="ExtendedLengthPrefix"/> — and
    /// the ancestor it belongs to, or <see langword="null"/> and the ancestor
    /// the walk gave up on.
    /// </returns>
    public static (string? Final, string Existing) DeepestExistingFinalName(string path, int walkLimit)
    {
        var candidate = path;
        string? final = null;

        for (var level = 0; level < walkLimit; level++)
        {
            final = FinalNameOf(candidate);

            if (final is not null || !NameDoesNotExist() || Path.GetDirectoryName(candidate) is not { } parent)
            {
                break;
            }

            candidate = parent;
        }

        return (final, candidate);
    }

    /// <summary>
    /// The filesystem's own name for a path, in the plain drive-letter spelling
    /// every Win32 path reporter answers with — or why that could not be
    /// established.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one question here that is safe to ask about a path of unknown
    /// shape</b>, because it asks the other two itself:
    /// <see cref="IsUncOrDeviceSpelling"/> on the characters,
    /// <see cref="Of"/> on the object manager, and only then the directory open
    /// <see cref="DeepestExistingFinalName"/> costs. That ordering is the whole
    /// point of this type — an open against an unreachable share costs a measured
    /// <b>22,210 ms</b> — so a caller reaching for <see cref="FinalNameOf"/>
    /// directly is taking the guard on itself.
    /// </para>
    /// <para>
    /// <b>The answer is comparable against what Windows reports and the input is
    /// not.</b> <c>QueryFullProcessImageNameW</c>, <c>GetModuleFileNameW</c> and
    /// every other Win32 path reporter answer in this form: a drive letter, no
    /// <see cref="ExtendedLengthPrefix"/>, and every reparse point already
    /// resolved. A path composed with <c>Path.Combine</c> is none of those, and
    /// comparing the two directly compares the answers to two different
    /// questions — which is what made
    /// <c>Interop.BrowserProcesses.ScanFor</c> return <c>candidates=0</c> for
    /// good under one junction.
    /// </para>
    /// <para>
    /// <b>A UNC answer is refused rather than stripped</b>, for the reason
    /// <c>Hosting.InstallRootScope</c> gives at its own copy of this rule:
    /// <c>\\?\UNC\host\share</c> with the prefix removed reads as a rooted local
    /// path and would then be compared as one.
    /// </para>
    /// <para>
    /// <b>Why a sentence rather than a bare <see langword="null"/>.</b> Every
    /// caller of this is deciding whether it may act on an <i>absence</i> — no
    /// candidate found, nothing running out of a tree — and an absence that
    /// arrives because the question could not be asked is a different fact from
    /// one that arrives because there is nothing there. The sentence is what
    /// makes the two distinguishable on a log line.
    /// </para>
    /// <para>
    /// <b><c>Hosting.InstallRootScope</c> deliberately does not call this</b>,
    /// and that is not an oversight to be tidied. It compares <i>two</i> paths
    /// and answers three ways — serve, refuse, could-not-establish — and its
    /// refusals quote the ancestor the walk stopped at, so it needs the halves
    /// this method composes rather than the composition.
    /// </para>
    /// </remarks>
    /// <param name="path">The path to resolve. Need not exist.</param>
    /// <param name="walkLimit">
    /// How many levels the walk may climb looking for an ancestor that exists,
    /// passed straight to <see cref="DeepestExistingFinalName"/>.
    /// </param>
    /// <returns>
    /// The spelling, or <see langword="null"/> with a clause saying why not.
    /// Exactly one of the two is ever set.
    /// </returns>
    public static (string? Spelling, string? Why) DosSpellingOf(string path, int walkLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // 1. Characters only, and first. `\\?\C:\…` is NOT a share -- it is the
        //    extended spelling of an ordinary local path -- so the prefix is
        //    stripped for the volume question and the caller's own spelling is
        //    what the filesystem is asked about, which CreateFileW accepts
        //    either way.
        var probe = path;

        if (IsUncOrDeviceSpelling(path))
        {
            var afterPrefix = path.Length > 4 && path[1] is '\\' && path[2] is '?' or '.' && path[3] is '\\'
                ? path[4..]
                : null;

            if (afterPrefix is null || afterPrefix.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return (null, $"it is a UNC or device path, and asking the filesystem what it calls one can block for as long as the share takes to answer — measured at 22,210 ms.");
            }

            probe = afterPrefix;
        }

        // 2. The object manager only -- still no filesystem call. A mapped drive
        //    letter is a share wearing a letter, and it is the one form that
        //    would cost 22 s to discover the slow way.
        if (Of(probe).Kind is VolumeKind.Network)
        {
            return (null, $"drive '{probe[..2]}' is a mapped network drive, so asking the filesystem what it calls that path can block for as long as the share takes to answer — measured at 22,210 ms.");
        }

        // 3. And only now, one directory open per level climbed.
        var (final, existing) = DeepestExistingFinalName(path, walkLimit);

        if (final is null)
        {
            return (null, $"the filesystem would not say what it calls '{existing}'.");
        }

        var stripped = final.StartsWith(ExtendedLengthPrefix, StringComparison.Ordinal)
            ? final[ExtendedLengthPrefix.Length..]
            : final;

        if (stripped.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return (null, $"the filesystem calls '{existing}' the share '{final}', which has no drive-letter spelling to compare a reported path against.");
        }

        // Whatever was trimmed off to find an existing ancestor goes back on. The
        // answer is unaffected either way: a tail that does not exist cannot be a
        // reparse point, so an unaliased ancestor makes the whole path unaliased.
        var tail = existing.Length <= path.Length ? path.AsSpan(existing.Length) : [];

        return (tail.IsEmpty ? stripped : Path.Join(stripped, tail), null);
    }

    /// <summary>
    /// Whether the last failure was <i>this name does not exist</i> rather than
    /// anything else.
    /// </summary>
    /// <remarks>
    /// <b>The discriminator that decides whether to walk up.</b> A
    /// <see cref="FinalNameOf"/> that failed because the directory is not there
    /// yet means <c>init</c> on a path nothing has created, and the question
    /// should be re-asked of the parent. A failure for any other reason —
    /// <c>ERROR_ACCESS_DENIED</c> most of all — means the answer is unknown, and
    /// walking up would turn <i>unknown</i> into a confident <i>fine</i> read off
    /// an ancestor that is not the directory in question.
    /// </remarks>
    /// <returns><see langword="true"/> for the two not-found errors.</returns>
    public static bool NameDoesNotExist() =>
        Marshal.GetLastPInvokeError() is ErrorFileNotFound or ErrorPathNotFound;

    private static unsafe string? QueryTarget(string device)
    {
        // MAX_PATH is not a bound on a symbolic link target, and the API answers
        // ERROR_INSUFFICIENT_BUFFER rather than truncating -- so the buffer is
        // sized once well past any real target and one that will not fit reads
        // as "cannot say", which the caller treats as NoSuchDrive. It is only
        // ever the leading `\??\` that is read, so a long target would have to
        // be pathological to matter.
        const int Capacity = 1024;
        var buffer = stackalloc char[Capacity];
        var written = QueryDosDeviceW(device, buffer, Capacity);

        // The answer is a NUL-separated, NUL-NUL-terminated list; only the first
        // entry is a drive letter's target, and the return value counts every
        // character including both terminators.
        return written is 0 ? null : new string(buffer);
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveTypeW(string lpRootPathName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint QueryDosDeviceW(string lpDeviceName, char* lpTargetPath, uint ucchMax);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint GetFinalPathNameByHandleW(SafeFileHandle hFile, char* lpszFilePath, uint cchFilePath, uint dwFlags);
}

/// <summary>What a drive letter names, and what it stands for.</summary>
/// <param name="Kind">What kind of volume the letter names.</param>
/// <param name="SubstitutedFor">
/// For <see cref="VolumeKind.Substituted"/> only: the DOS path the letter stands
/// for, with the object manager's <c>\??\</c> prefix already removed. It is
/// carried so a refusal can name the accepted spelling.
/// </param>
internal readonly record struct VolumeReading(VolumeKind Kind, string? SubstitutedFor);

/// <summary>What kind of volume a drive letter names.</summary>
/// <remarks>
/// <b>Four answers rather than a boolean</b>, because they need different things
/// said about them: a single flag would have merged <i>this letter is a
/// share</i> with <i>this letter is another letter's directory</i>, and those
/// have opposite fixes.
/// </remarks>
internal enum VolumeKind
{
    /// <summary>An ordinary local volume.</summary>
    Local,

    /// <summary>
    /// A drive letter that resolves through a network redirector —
    /// <c>DRIVE_REMOTE</c>. What a <c>net use Z: \\host\share</c> produces.
    /// </summary>
    Network,

    /// <summary>
    /// A drive letter that is a symbolic link to another DOS path — what
    /// <c>subst</c> produces. Local, and still an alias.
    /// </summary>
    Substituted,

    /// <summary>The letter names nothing on this machine.</summary>
    NoSuchDrive,

    /// <summary>
    /// Not a rooted drive-letter path at all, so the object manager has nothing
    /// to be asked about.
    /// </summary>
    NotADriveLetter,
}
