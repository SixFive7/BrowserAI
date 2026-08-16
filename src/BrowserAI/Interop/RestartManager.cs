// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace BrowserAI.Interop;

/// <summary>
/// Which live processes hold a given file open, asked of Windows' Restart
/// Manager: <c>RmStartSession</c> → <c>RmRegisterResources</c> →
/// <c>RmGetList</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the file → process direction, and Windows offers exactly one
/// supported way to ask it.</b> A sharing violation says <i>somebody</i> has the
/// file; it never says who. The Restart Manager does, and it is the same
/// mechanism an installer uses to name the applications it would have to close —
/// so it is documented, stable, and needs no privilege beyond opening the
/// processes it reports.
/// </para>
/// <para>
/// <b>It exists here for Firefox, whose profile lock is the only thing that can
/// say which profile a Firefox is on.</b> Chromium publishes its
/// <c>userDataDir</c> as a message-only window's title
/// (<see cref="MessageWindows"/>); Firefox publishes nothing at all, and its
/// <c>parent.lock</c> is <i>never deleted</i> — Mozilla keeps it deliberately and
/// reads its mtime to detect startup crashes — so the file's existence proves
/// nothing and only a live handle on it does.
/// </para>
/// <para>
/// ⚠️ <b>Written from the behaviour of Mozilla's <c>ProfileUnlockerWin::
/// TryToTerminate</c>, never from its text.</b> That file is MPL-2.0 and this
/// repository is <c>LicenseRef-BrowserAI-FSL-1.1-MIT-5yr</c>; the plan's
/// suggestion to copy it "line for line" is a licence incompatibility as well as
/// a design note. What is reproduced is the <i>sequence</i>, which is the
/// documented API contract and belongs to nobody: start a session, register the
/// one file, ask for the list, end the session. The same route
/// [step 9](../../plan/build-order.md#9-lossless-passthrough) took when it
/// implemented parse-error recovery from the MCP SDK's observed behaviour rather
/// than from its Apache-2.0 source.
/// </para>
/// <para>
/// <b>Every holder carries its process start time, and that is not decoration.</b>
/// <c>RM_UNIQUE_PROCESS</c> is <c>(pid, ProcessStartTime)</c> precisely because a
/// pid alone identifies nothing: Windows reuses pids, and a stale pid acted on
/// after a reuse is how a janitor terminates a stranger. The start time is the
/// same <c>FILETIME</c> <c>GetProcessTimes</c> reports, so it compares directly
/// against <see cref="StrayCandidate.CreatedFileTime"/> and
/// <see cref="ProcessLiveness"/>'s pairs.
/// </para>
/// <para>
/// <b>A failure throws rather than returning "nobody".</b> An error path that
/// resolves to the permissive answer is the shape this project exists to
/// eliminate: "nobody holds it" is what the caller acts on, and a caller that
/// learns it from a failed query would launch into the collision the query was
/// asked to prevent.
/// </para>
/// </remarks>
internal static partial class RestartManager
{
    /// <summary>
    /// The most holders that will be reported for one file.
    /// </summary>
    /// <remarks>
    /// A bound rather than an unbounded retry loop: the answer for a browser
    /// profile lock is one process, and a machine that reports thousands is
    /// something going wrong rather than something to allocate for.
    /// </remarks>
    public const int MaximumHolders = 256;

    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;

    /// <summary><c>CCH_RM_SESSION_KEY</c> + 1, counting the terminator.</summary>
    private const int SessionKeyLength = 33;

    /// <summary><c>CCH_RM_MAX_APP_NAME</c> + 1.</summary>
    private const int AppNameLength = 256;

    /// <summary>Every live process holding <paramref name="path"/> open.</summary>
    /// <param name="path">
    /// An absolute path. A file that does not exist has no holders and is not an
    /// error — the Restart Manager registers the <i>name</i>, so the answer is
    /// simply empty.
    /// </param>
    /// <returns>
    /// The holders, each with the <c>(pid, start time)</c> pair that identifies
    /// it. Empty when nothing holds the file.
    /// </returns>
    /// <exception cref="Win32Exception">
    /// The Restart Manager refused the question. <b>Never confused with an empty
    /// answer</b>, because the two mean opposite things to a caller.
    /// </exception>
    public static unsafe IReadOnlyList<FileHolder> HoldersOf(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // The session key buffer is written by RmStartSession and must be
        // CCH_RM_SESSION_KEY + 1 wide characters. Passing anything shorter
        // corrupts the caller's stack, which is why the size is a named constant
        // rather than a literal at the call site.
        var key = stackalloc char[SessionKeyLength];
        var started = RmStartSession(out var session, 0, key);

        if (started is not ErrorSuccess)
        {
            throw new Win32Exception(
                started,
                $"The Windows Restart Manager would not start a session, so BrowserAI cannot say which process holds '{path}'.");
        }

        try
        {
            Register(session, path);
            return List(session, path);
        }
        finally
        {
            // Unconditional: a session that is never ended leaks a registry key
            // under RestartManager\Session0000.. for the life of the machine.
            _ = RmEndSession(session);
        }
    }

    private static unsafe void Register(uint session, string path)
    {
        int registered;

        fixed (char* name = path)
        {
            var names = stackalloc char*[1];
            names[0] = name;

            registered = RmRegisterResources(session, 1, names, 0, null, 0, null);
        }

        if (registered is not ErrorSuccess)
        {
            throw new Win32Exception(
                registered,
                $"The Windows Restart Manager would not accept '{path}' as a resource, so BrowserAI cannot say which process holds it.");
        }
    }

    private static unsafe List<FileHolder> List(uint session, string path)
    {
        // Two steps, which is the documented shape: ask with no buffer to learn
        // the count, then ask again with one. The count can change between the
        // two calls -- a process opening the file in the gap -- so ERROR_MORE_DATA
        // on the second call is a real outcome and not an impossible one.
        uint capacity = 0;
        var sized = RmGetList(session, out var needed, ref capacity, null, out _);

        if (sized is ErrorSuccess || needed is 0)
        {
            return [];
        }

        if (sized is not ErrorMoreData)
        {
            throw new Win32Exception(
                sized,
                $"The Windows Restart Manager would not list the processes holding '{path}'.");
        }

        if (needed > MaximumHolders)
        {
            throw new Win32Exception(
                ErrorMoreData,
                $"The Windows Restart Manager reports {needed.ToString(CultureInfo.InvariantCulture)} processes holding '{path}', which is past the {MaximumHolders.ToString(CultureInfo.InvariantCulture)} this build will accept for one file. Something is wrong with the machine rather than with the file.");
        }

        var entries = new RmProcessInfo[needed];
        capacity = needed;

        fixed (RmProcessInfo* first = entries)
        {
            var listed = RmGetList(session, out _, ref capacity, first, out _);

            if (listed is ErrorMoreData)
            {
                // The set grew between the two calls. Reporting the short list
                // would be an answer that reads complete and is not.
                throw new Win32Exception(
                    listed,
                    $"The set of processes holding '{path}' grew while it was being read, so the list would have been incomplete. Ask again.");
            }

            if (listed is not ErrorSuccess)
            {
                throw new Win32Exception(
                    listed,
                    $"The Windows Restart Manager would not list the processes holding '{path}'.");
            }
        }

        var holders = new List<FileHolder>((int)capacity);

        for (var index = 0; index < capacity; index++)
        {
            var entry = entries[index];

            holders.Add(new FileHolder(
                (int)entry.Process.ProcessId,
                ((long)entry.Process.StartTimeHigh << 32) | entry.Process.StartTimeLow,
                NameOf(entry)));
        }

        return holders;
    }

    /// <summary>
    /// The description Windows attaches to a holder.
    /// </summary>
    /// <remarks>
    /// <b>Diagnostic text and nothing else.</b> It is shown to a person so a
    /// refusal names something recognisable; nothing in this repository compares
    /// it, filters on it or acts on it, which is the whole of the distinction
    /// [§D](../../plan/D-locking.md#never-by-image-name) draws between observing
    /// a name and choosing a process by one.
    /// </remarks>
    private static unsafe string NameOf(RmProcessInfo entry)
    {
        var characters = new char[AppNameLength];

        for (var index = 0; index < AppNameLength; index++)
        {
            var value = entry.AppName[index];

            if (value is 0)
            {
                return new string(characters, 0, index);
            }

            characters[index] = (char)value;
        }

        return new string(characters);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("rstrtmgr.dll")]
    private static unsafe partial int RmStartSession(out uint pSessionHandle, uint dwSessionFlags, char* strSessionKey);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("rstrtmgr.dll")]
    private static unsafe partial int RmRegisterResources(
        uint dwSessionHandle,
        uint nFiles,
        char** rgsFileNames,
        uint nApplications,
        void* rgApplications,
        uint nServices,
        char** rgsServiceNames);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("rstrtmgr.dll")]
    private static unsafe partial int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        RmProcessInfo* rgAffectedApps,
        out uint lpdwRebootReasons);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmEndSession(uint dwSessionHandle);

    /// <summary>
    /// <c>RM_UNIQUE_PROCESS</c> — a pid and the <c>FILETIME</c> it started at.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The start time is two <c>uint</c>s rather than a <c>long</c>, and
    /// that is layout rather than style.</b> <c>FILETIME</c> is two
    /// <c>DWORD</c>s and aligns to 4; a <c>long</c> field would align the struct
    /// to 8 and make the compiler insert four bytes of padding after the pid,
    /// producing a 16-byte struct where Windows writes a 12-byte one. Every
    /// field after it would then be read from the wrong offset, silently.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public uint ProcessId;
        public uint StartTimeLow;
        public uint StartTimeHigh;
    }

    /// <summary><c>RM_PROCESS_INFO</c>, 668 bytes.</summary>
    /// <remarks>
    /// The two name buffers are <c>ushort</c> fixed buffers rather than
    /// <c>char</c> ones: a fixed buffer of <c>char</c> drags the whole struct
    /// into the marshaller's character-set rules, and this type has to stay
    /// blittable for <c>[LibraryImport]</c> under NativeAOT. They are decoded by
    /// hand, which is one loop.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        public fixed ushort AppName[AppNameLength];

        // CCH_RM_MAX_SVC_NAME + 1. Present for its SIZE and nothing else: every
        // field after it would be read from the wrong offset without it.
        public fixed ushort ServiceShortName[64];
        public int ApplicationType;
        public uint AppStatus;
        public uint TerminalServicesSessionId;
        public int Restartable;
    }
}

/// <summary>One live process holding a file open.</summary>
/// <param name="ProcessId">Its pid, meaningful only with <paramref name="StartedFileTime"/>.</param>
/// <param name="StartedFileTime">
/// When it started, as a <c>FILETIME</c>. The pid-reuse guard: the same value
/// <c>GetProcessTimes</c> reports, so it compares directly against the creation
/// time recorded anywhere else in this process.
/// </param>
/// <param name="Description">
/// What Windows calls it. <b>Diagnostic only</b> — never matched on.
/// </param>
internal sealed record FileHolder(int ProcessId, long StartedFileTime, string Description);
