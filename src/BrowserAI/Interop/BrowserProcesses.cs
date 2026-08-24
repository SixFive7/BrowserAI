// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrowserAI.Interop;

/// <summary>
/// Which live processes are running an executable BrowserAI owns — answered by
/// <b>full image path</b>, never by image name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Image path is the documented detection route; image name is the one this
/// project bans.</b> <c>chrome.exe</c> names the user's own Chrome, every other
/// Chromium on the machine, and ours. <c>&lt;browsers-root&gt;\chromium-1237\
/// chrome-win64\chrome.exe</c> names exactly one tree, the one BrowserAI
/// provisioned, and neither a prefix match against the browsers root nor an
/// exact match against one binary can reach a browser that came from anywhere
/// else. That is the whole difference between a question this product may ask
/// and one it may not.
/// </para>
/// <para>
/// <b>The path is <c>EnumProcesses</c> → <c>OpenProcess</c> →
/// <c>QueryFullProcessImageNameW</c>, and every API on it is documented and
/// supported.</b> <c>EnumProcesses</c> itself costs ~0.06 ms; the whole cost is
/// the per-process open, and the ~150 processes this token cannot open are
/// protected and SYSTEM-owned ones — nothing BrowserAI launched can be in that
/// set, because it runs as the user and non-elevated
/// ([kb](../../../kb/windows/detection.md#process-image-path--the-fully-documented-detection-path)).
/// </para>
/// <para>
/// <b>Terminating is <see cref="StrayCandidate"/>'s, and only after a second
/// guard agrees.</b> Nothing on the <see cref="RunningFrom"/> path can
/// terminate anything: its one caller is
/// <c>browserai_reinstall_browser</c>, which refuses rather than coordinates.
/// The sweep's scan does hold a handle with <c>PROCESS_TERMINATE</c>, and a
/// candidate is still only killed when its attributed directory holds a
/// <c>browserai.json</c> whose lock the sweeper can take itself.
/// </para>
/// <para>
/// <b>Every row carries a creation time.</b> A pid alone is meaningless the
/// moment the process exits, and Windows reuses pids — so the pair is the
/// identity, exactly as it is for <see cref="ProcessLiveness"/>. A process that
/// exits between the snapshot and the image-path read is dropped rather than
/// reported with a name that may already belong to a stranger.
/// </para>
/// </remarks>
internal static partial class BrowserProcesses
{
    private const uint ProcessQueryLimitedInformation = 0x00001000;
    private const uint ProcessTerminate = 0x00000001;

    /// <summary>Every live process whose executable sits under <paramref name="root"/>.</summary>
    /// <param name="root">
    /// An absolute directory. Matching is a case-insensitive prefix match on the
    /// process's full image path, with a separator appended so that a root of
    /// <c>…\browsers</c> cannot match <c>…\browsers-backup</c> — and it is made
    /// against <b>every spelling of this root</b> a Win32 path reporter could
    /// answer with, never only the one <c>Path.Combine</c> produced. See
    /// <see cref="ImageSpellings"/>.
    /// </param>
    /// <returns>
    /// The matching processes, which may be empty. Processes this token cannot
    /// open are absent: a process BrowserAI cannot see is not one it can claim
    /// anything about.
    /// </returns>
    /// <exception cref="Win32Exception">The process list could not be read at all.</exception>
    public static IReadOnlyList<RunningImage> RunningFrom(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // Every spelling of the root a Win32 path reporter could answer with, not
        // only the one Path.Combine produced. See ImageSpellings: a junction
        // above the root made this list empty for every process on the machine,
        // and here that empties RevisionPrune's live set — which is the census a
        // tree is DELETED on when it comes back empty.
        var prefixes = ImageSpellings.OfDirectory(root).Matched
            .Select(spelling => spelling.EndsWith(Path.DirectorySeparatorChar) ? spelling : spelling + Path.DirectorySeparatorChar)
            .ToArray();

        var found = new List<RunningImage>();

        foreach (var processId in ProcessIds())
        {
            using var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, (uint)processId);

            if (handle.IsInvalid)
            {
                continue;
            }

            var path = ImagePathOf(handle);

            if (path is null
                || !Array.Exists(prefixes, prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                || !GetProcessTimes(handle, out var created, out _, out _, out _))
            {
                continue;
            }

            found.Add(new RunningImage(processId, created, path));
        }

        return found;
    }

    /// <summary>
    /// Every live process running <b>exactly</b> one of the executables
    /// BrowserAI provisioned, each with an open handle held from this moment.
    /// </summary>
    /// <remarks>
    /// <b>The handle is what closes race R2.</b> Windows will not recycle a pid
    /// while a handle to that process is open, so a pid captured here still
    /// names the same process when the sweep gets round to acting on it. Measured
    /// 2026-08-18 against a control that repeated a pid after 2,010 spawns with
    /// no handle held, and did not repeat once in 6,030 with one
    /// (<see href="../../../kb/windows/processes.md">kb</see>). The
    /// creation time is re-verified regardless, immediately before terminating,
    /// because belt-and-braces is the only acceptable posture for a call that
    /// cannot be undone.
    /// </remarks>
    /// <param name="images">The absolute executable paths that count as ours.</param>
    /// <returns>The scan. <b>The caller owns it and must dispose it.</b></returns>
    /// <exception cref="Win32Exception">The process list could not be read at all.</exception>
    public static StrayScan ScanFor(IReadOnlyCollection<string> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        // ⚠️ EVERY SPELLING OF OUR OWN EXECUTABLES, AND NOTHING ELSE. The match
        // below is still exact and still against a closed set of absolute paths
        // BrowserAI itself composed; what the set gained is the spelling the
        // FILESYSTEM gives those same files, because that is the one
        // QueryFullProcessImageNameW answers with. See ImageSpellings.
        var spellings = ImageSpellings.Of(images);
        var wanted = spellings.Matched;
        var candidates = new List<StrayCandidate>();
        var enumerated = 0;
        var opened = 0;

        try
        {
            foreach (var processId in ProcessIds())
            {
                enumerated++;

                // PROCESS_TERMINATE is asked for here rather than later on
                // purpose: a handle acquired after the decision would be a
                // second chance for the pid to have become somebody else.
                var handle = OpenProcess(ProcessQueryLimitedInformation | ProcessTerminate, bInheritHandle: false, (uint)processId);

                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    continue;
                }

                opened++;
                var path = ImagePathOf(handle);

                if (path is null || !wanted.Contains(path) || !GetProcessTimes(handle, out var created, out _, out _, out _))
                {
                    handle.Dispose();
                    continue;
                }

                candidates.Add(new StrayCandidate(processId, created, path, handle));
            }
        }
        catch
        {
            foreach (var candidate in candidates)
            {
                candidate.Dispose();
            }

            throw;
        }

        return new StrayScan(candidates, enumerated, opened, spellings);
    }

    /// <summary>Every pid on the machine, from <c>EnumProcesses</c>.</summary>
    /// <remarks>
    /// <b><c>K32EnumProcesses</c> is <c>EnumProcesses</c>.</b> The name in
    /// <c>psapi.h</c> maps to this kernel32 export under
    /// <c>PSAPI_VERSION = 2</c>; taking it directly avoids loading the
    /// <c>psapi.dll</c> forwarder and changes nothing else.
    /// <b><c>NtQuerySystemInformation</c> is deliberately not used</b> even
    /// though it would return every image name in one call: at this cost there
    /// is nothing to buy, and it would put an image-<i>name</i> comparison
    /// inside the detection path — which is the pattern that erodes into the
    /// rule this file exists to keep.
    /// </remarks>
    private static int[] ProcessIds()
    {
        var buffer = new uint[1024];

        while (true)
        {
            if (!K32EnumProcesses(buffer, (uint)(buffer.Length * sizeof(uint)), out var bytesReturned))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Could not read the machine's process list, so BrowserAI cannot tell whether a browser of its own is running.");
            }

            var returned = (int)(bytesReturned / sizeof(uint));

            // A full buffer means the list may have been truncated, and a
            // truncated list under-reports silently -- which is the one failure
            // shape this project exists to eliminate. Grow and ask again.
            if (returned == buffer.Length)
            {
                buffer = new uint[buffer.Length * 2];
                continue;
            }

            // Pid 0 is the idle process. It is not openable and never ours.
            return [.. buffer.Take(returned).Where(id => id is not 0).Select(id => (int)id)];
        }
    }

    private static unsafe string? ImagePathOf(SafeProcessHandle handle)
    {
        // Sized for the extended limit rather than MAX_PATH: the app manifest is
        // longPathAware and the browsers root is the caller's LocalAppData,
        // which can be arbitrarily deep.
        var buffer = new char[32768];
        var length = (uint)buffer.Length;

        fixed (char* start = buffer)
        {
            return QueryFullProcessImageNameW(handle, 0, start, ref length)
                ? new string(buffer, 0, (int)length)
                : null;
        }
    }

    // System32 only, on every P/Invoke in this repository (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool K32EnumProcesses([Out] uint[] lpidProcess, uint cb, out uint lpcbNeeded);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(
        SafeProcessHandle hProcess,
        uint dwFlags,
        char* lpExeName,
        ref uint lpdwSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        SafeProcessHandle hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);
}

/// <summary>
/// One path BrowserAI composed, and what the filesystem itself calls it.
/// </summary>
/// <param name="Composed">The path as <c>Path.Combine</c> produced it.</param>
/// <param name="Reported">
/// The same object in the spelling every Win32 path reporter answers with, or
/// <see langword="null"/> when that could not be established.
/// </param>
/// <param name="Why">
/// Why it could not be established, when it could not. Never a
/// <see langword="null"/> on its own: an absence a caller cannot explain is the
/// failure shape this whole type exists to remove.
/// </param>
internal sealed record PathSpelling(string Composed, string? Reported, string? Why)
{
    /// <summary>Whether the filesystem spells this path differently.</summary>
    public bool IsAliased =>
        Reported is not null && !string.Equals(Composed, Reported, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Every spelling of a set of paths BrowserAI composed that a Win32 path
/// reporter could answer with — and, where one could not be established, why.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because the two sides of the detection comparison are produced
/// by different things.</b> <c>Hosting.LocalAppDataPaths</c> composes every path
/// with <c>Path.Combine</c>, which never resolves a link;
/// <c>QueryFullProcessImageNameW</c> answers with the path <b>after</b> the
/// object manager has done reparse processing. One junction above the install
/// root — a relocated user profile, a redirected <c>AppData</c>, a
/// <c>subst</c>ed letter, an 8.3 component — therefore made the two different
/// strings for every process on the machine, so
/// <see cref="BrowserProcesses.ScanFor"/> returned <c>candidates=0</c> for good
/// and <see cref="BrowserProcesses.RunningFrom"/> returned an empty live set for
/// good. Casing was handled; link resolution was not. Found by
/// [the adversarial review](../../../docs/reviews/2026-08-18-adversarial-processes.md),
/// finding 4, and fixed 2026-08-24.
/// </para>
/// <para>
/// ⚠️ <b>What this widens is what the sweep may MATCH, and it does not widen
/// what the sweep may TERMINATE.</b> The distinction is the whole reason this
/// is a type rather than a line:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Only paths BrowserAI itself composed are ever resolved.</b> Nothing
///     here is ever asked of a path a foreign process reported. The direction is
///     one-way by construction — the wanted set is canonicalised once, before
///     the scan — so no stranger's process can influence what is opened, and the
///     22-second hazard <see cref="VolumeIdentity"/> is ordered around cannot be
///     reached through the process list.
///   </description></item>
///   <item><description>
///     <b>A spelling names the same file, never a different one.</b> The answer
///     is what the filesystem calls the object the composed path already named,
///     so a process matching it is running <i>that exact binary</i>. The set of
///     processes that match is unchanged in intent and only corrected in fact;
///     the match is still exact, still full-path, still never a prefix and still
///     never an image name.
///   </description></item>
///   <item><description>
///     <b>Nothing between a candidate and a kill is touched.</b> A candidate
///     becomes a stray only when a second, independent guard agrees — its
///     attributed directory holds a <c>browserai.json</c> whose lock the sweeper
///     can take itself — and the kill still runs behind the held process handle
///     and the creation-time re-check. Widening detection moves the first guard
///     and no other.
///   </description></item>
/// </list>
/// <para>
/// <b>The leaf is deliberately not resolved, and the gap is named rather than
/// left to be found.</b> What is resolved is the <i>containing directory</i>,
/// with the file name re-attached. That is what every alias form this product
/// actually meets is made of — a link, a substitution or a short name is a
/// property of a directory component — and it is also what keeps this off a
/// running image: opening the executable itself would meet the loader's own
/// section, whose refusal is not <i>this name does not exist</i> and would
/// therefore stop <see cref="VolumeIdentity.DeepestExistingFinalName"/>'s walk
/// with no answer at all, on exactly the machine where a browser is running.
/// <b>A symlinked leaf is consequently still invisible</b>, and it lands in
/// <see cref="Unresolved"/>'s sibling condition rather than being reported —
/// which is a real, narrow hole in a guard that used to have a wide one.
/// </para>
/// </remarks>
internal sealed class ImageSpellings
{
    /// <summary>
    /// How far up the tree the final-name walk may climb looking for a directory
    /// that exists.
    /// </summary>
    /// <remarks>
    /// The same bound <c>Hosting.InstallRootScope.AncestorWalkLimit</c> and
    /// <c>Sessions.SessionDirectoryGuard.AncestorWalkLimit</c> use, spelled again
    /// for the reason the first of those gives: they are independent budgets that
    /// happen to agree, and the walk costs one directory open per level.
    /// </remarks>
    public const int AncestorWalkLimit = 64;

    private ImageSpellings(IReadOnlyList<PathSpelling> each)
    {
        Each = each;
        Matched = new HashSet<string>(
            each.SelectMany(spelling => spelling.Reported is null
                ? (IEnumerable<string>)[spelling.Composed]
                : [spelling.Composed, spelling.Reported]),
            StringComparer.OrdinalIgnoreCase);

        Unresolved = [.. each
            .Where(spelling => spelling.Why is not null)
            .Select(spelling => $"'{spelling.Composed}' could not be matched against what Windows reports, because {spelling.Why} Anything running out of it is invisible to this pass.")];
    }

    /// <summary>One entry per path asked about, in the order they were given.</summary>
    public IReadOnlyList<PathSpelling> Each { get; }

    /// <summary>
    /// Every spelling a match may be made against: each composed path, plus the
    /// filesystem's own name for it where that differs.
    /// </summary>
    public IReadOnlySet<string> Matched { get; }

    /// <summary>How many paths were asked about.</summary>
    public int Watched => Each.Count;

    /// <summary>How many the filesystem spells differently.</summary>
    public int Aliased => Each.Count(spelling => spelling.IsAliased);

    /// <summary>
    /// Every path whose filesystem spelling could not be established, one
    /// sentence each.
    /// </summary>
    public IReadOnlyList<string> Unresolved { get; }

    /// <summary>Resolves a set of composed <b>executable</b> paths.</summary>
    /// <remarks>
    /// <b>Each is resolved through its containing directory and the file name is
    /// re-attached</b> — see this type's remarks for both halves of why. There
    /// are two factories rather than one that guesses, because whether a path
    /// names a file or a directory is a fact the caller has and the filesystem
    /// would have to be asked for.
    /// </remarks>
    /// <param name="imagePaths">The executables, absolute, as BrowserAI composed them.</param>
    /// <returns>The spellings.</returns>
    public static ImageSpellings Of(IReadOnlyCollection<string> imagePaths)
    {
        ArgumentNullException.ThrowIfNull(imagePaths);

        return new ImageSpellings([.. imagePaths.Select(ResolveImage)]);
    }

    /// <summary>Resolves one composed <b>directory</b>, leaf included.</summary>
    /// <remarks>
    /// <b>The leaf is resolved here and skipped for an executable</b>, and the
    /// asymmetry is the point: a directory leaf may itself be the junction — a
    /// browsers root somebody linked aside is exactly that — while an executable
    /// leaf is a file the loader has mapped, which is the one open that would end
    /// the walk with no answer.
    /// </remarks>
    /// <param name="directory">The directory, absolute, as BrowserAI composed it.</param>
    /// <returns>The spellings, holding one entry.</returns>
    public static ImageSpellings OfDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var (spelling, why) = VolumeIdentity.DosSpellingOf(directory, AncestorWalkLimit);

        return new ImageSpellings([new PathSpelling(directory, spelling, why)]);
    }

    private static PathSpelling ResolveImage(string path)
    {
        if (Path.GetDirectoryName(path) is not { Length: > 0 } directory)
        {
            // No directory part to be aliased above it, so the path is asked
            // about whole and the loader's mapping is not in the way of anything.
            var (whole, wholeWhy) = VolumeIdentity.DosSpellingOf(path, AncestorWalkLimit);
            return new PathSpelling(path, whole, wholeWhy);
        }

        var (spelling, why) = VolumeIdentity.DosSpellingOf(directory, AncestorWalkLimit);

        return spelling is null
            ? new PathSpelling(path, null, why)
            : new PathSpelling(path, Path.Combine(spelling, Path.GetFileName(path)), null);
    }
}

/// <summary>One live process running an image BrowserAI owns.</summary>
/// <param name="ProcessId">Its pid, meaningful only together with <paramref name="CreatedFileTime"/>.</param>
/// <param name="CreatedFileTime">Its creation time, which together with the pid is its identity.</param>
/// <param name="ImagePath">The full path of the executable it is running.</param>
internal sealed record RunningImage(int ProcessId, long CreatedFileTime, string ImagePath);

/// <summary>
/// One pass of the machine's process list, and the candidates it found.
/// </summary>
/// <param name="candidates">Every process running one of our binaries, each holding an open handle.</param>
/// <param name="enumerated">How many pids <c>EnumProcesses</c> returned.</param>
/// <param name="opened">How many of them this token could open. The rest are protected or SYSTEM-owned.</param>
/// <param name="spellings">What the pass was matching against, and what it could not establish.</param>
internal sealed class StrayScan(
    IReadOnlyList<StrayCandidate> candidates,
    int enumerated,
    int opened,
    ImageSpellings spellings) : IDisposable
{
    /// <summary>Every process running one of our binaries.</summary>
    public IReadOnlyList<StrayCandidate> Candidates { get; } = candidates;

    /// <summary>How many pids the machine reported.</summary>
    public int Enumerated { get; } = enumerated;

    /// <summary>How many of them could be opened at all.</summary>
    public int Opened { get; } = opened;

    /// <summary>How many executables this pass counted as ours.</summary>
    public int ImagesWatched => spellings.Watched;

    /// <summary>
    /// How many of them the filesystem spells differently from the way they were
    /// composed.
    /// </summary>
    /// <remarks>
    /// <b>Evidence rather than a fault.</b> A non-zero here is an aliased install
    /// root working correctly; before 2026-08-24 the same machine produced
    /// <c>candidates=0</c> on every pass forever and said nothing at all.
    /// </remarks>
    public int ImagesAliased => spellings.Aliased;

    /// <summary>
    /// Every image whose filesystem spelling could not be established, one
    /// sentence each.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This is the tripwire, and it is the sibling of
    /// <c>Sessions.StraySweepResult.TitledWindows</c>.</b> That count exists
    /// because a walk that found dozens of windows and none named is what a
    /// broken title read looks like, and would otherwise be indistinguishable
    /// from a clean machine. The identical thing is true one column over: a pass
    /// reporting no candidates while it could not establish what any of its own
    /// binaries are called is a pass that cannot match anything, and it must not
    /// read as an empty machine.
    /// </remarks>
    public IReadOnlyList<string> ImagesUnresolved => spellings.Unresolved;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var candidate in Candidates)
        {
            candidate.Dispose();
        }
    }
}

/// <summary>
/// One process running a binary BrowserAI provisioned, with the handle that
/// pins its pid held from the moment it was found.
/// </summary>
/// <remarks>
/// A candidate is <b>not</b> a stray. It becomes one only when a second,
/// independent guard agrees: its attributed directory holds a
/// <c>browserai.json</c> whose lock the sweeper can take itself. Everything here is
/// the first guard and the mechanics of acting on the second.
/// </remarks>
internal sealed partial class StrayCandidate : IDisposable
{
    private readonly SafeProcessHandle _handle;

    internal StrayCandidate(int processId, long createdFileTime, string imagePath, SafeProcessHandle handle)
    {
        ProcessId = processId;
        CreatedFileTime = createdFileTime;
        ImagePath = imagePath;
        _handle = handle;
    }

    /// <summary>Its pid, pinned by the open handle for as long as this object lives.</summary>
    public int ProcessId { get; }

    /// <summary>Its creation time, captured at detection. With the pid, this is its identity.</summary>
    public long CreatedFileTime { get; }

    /// <summary>The full path of the executable it is running.</summary>
    public string ImagePath { get; }

    /// <summary>
    /// Whether the process this handle names is still the one that was found.
    /// </summary>
    /// <returns><see langword="false"/> if the creation time no longer matches.</returns>
    public bool IsStillTheProcessThatWasFound() =>
        GetProcessTimes(_handle, out var created, out _, out _, out _) && created == CreatedFileTime;

    /// <summary>Terminates it, after re-checking that it is still itself.</summary>
    /// <param name="refusal">Why nothing was terminated, when nothing was.</param>
    /// <returns>Whether the process was terminated.</returns>
    /// <remarks>
    /// <b>The re-check is not redundant with the held handle.</b> The handle is
    /// what makes the pid safe; this is what makes a <i>stale candidate object</i>
    /// safe — one built before a wait, carried across a decision, or handed in by
    /// a caller who read the identity from somewhere else. It costs one syscall
    /// and it is the last thing between this product and terminating a stranger.
    /// </remarks>
    public bool TryTerminate(out string? refusal)
    {
        if (!IsStillTheProcessThatWasFound())
        {
            refusal = $"PID {ProcessId} is no longer the process that was found — its creation time has changed, so the pid now names something else. Nothing was terminated.";
            return false;
        }

        if (!TerminateProcess(_handle, 1))
        {
            refusal = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            return false;
        }

        refusal = null;
        return true;
    }

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        SafeProcessHandle hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);
}
