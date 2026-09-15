// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using BrowserAI.Updates;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Starts the published binary the way Velopack's post-install launch does: with
/// a launcher that is <b>already gone</b> and a standard input that is a
/// <b>console</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This is the shape that shipped broken in v1.0.0, and until 2026-09-15
/// nothing in this suite could produce it.</b>
/// <c>InstallerHandoffTests</c>' own class remarks said so —
/// <i>"no test in this suite starts BrowserAI with a real console. It cannot"</i>
/// — and the consequence was measured on the maintainer's screen: a non-silent
/// install left an orphan <c>BrowserAI.exe</c>, an orphan <c>node.exe</c> and a
/// full-size Windows Terminal window, for 215 seconds, while six green suite
/// runs said nothing.
/// </para>
/// <para>
/// <b>Nothing appears on screen, and that is what makes it allowable.</b>
/// <c>cmd.exe</c> is started with <c>CreateNoWindow</c>, so Windows allocates it
/// a console <b>with no window</b>; <c>start /b</c> shares that console rather
/// than opening another. The child's standard input is therefore a real console
/// handle — <c>GetConsoleMode</c> succeeds on it, which is the predicate
/// <see cref="Interop.StandardInput.IsAConsole"/> actually asks — with no
/// terminal anywhere.
/// </para>
/// <para>
/// ⚠️ <b>Nothing is redirected, and that is load-bearing rather than lazy.</b>
/// .NET sets <c>STARTF_USESTDHANDLES</c> as soon as <i>any</i> stream is
/// redirected, and fills the others from the <b>test host's own</b> standard
/// handles — which are a console under PowerShell and a pipe under Git Bash
/// (measured 2026-09-15, and the reason
/// <c>InstallerHandoffTests.TheConsoleQuestionIsRepeatableAndHasNoSideEffect</c>
/// asserts no value). A rig that redirected stderr to capture the product's log
/// would therefore be red from one shell and green from the other, about a
/// property of whoever started the suite. The product's records are read from
/// its rolling log file instead, which is the durable channel anyway
/// (see <see cref="ProcessLogRecords"/>).
/// </para>
/// <para>
/// <b>Two <c>start /b</c>s rather than one, because a handle keeps a dead pid
/// openable.</b> While anything holds a process handle the kernel keeps the
/// process object, and <c>OpenProcess</c> succeeds on the corpse — so a rig in
/// which the test host itself launched the product's parent produced a
/// <i>watchable</i> launcher and the product shut down cleanly, measured
/// 2026-09-15 at <c>.work/2026-09-15-fix/repro-red-2.txt</c>. The outer
/// <c>cmd</c> starts an inner one and exits; the inner starts the product and
/// exits; nothing anywhere holds a handle to the inner one, so its pid is freed
/// and <c>OpenProcess</c> answers <c>ERROR_INVALID_PARAMETER</c> — exactly what
/// the installer's <c>Setup.exe</c> leaves behind.
/// </para>
/// <para>
/// <b>The environment is stated rather than inherited for one name.</b>
/// <c>VELOPACK_FIRSTRUN</c> is set or removed explicitly on every start, because
/// another arm in this suite sets it process-wide and a child that inherited it
/// would take the installer exit and pass for a reason the arm never asked
/// about.
/// </para>
/// </remarks>
internal sealed class OrphanedConsoleStart : IDisposable
{
    private readonly string _executable;
    private readonly string _appRoot;

    private OrphanedConsoleStart(string executable, string appRoot)
    {
        _executable = executable;
        _appRoot = appRoot;
    }

    /// <summary>The product's pid, once its first record has been read.</summary>
    public int ProcessId { get; private set; }

    /// <summary>
    /// The creation time recorded beside <see cref="ProcessId"/>, which is the
    /// other half of this repository's standing process identity.
    /// </summary>
    public long CreatedFileTime { get; private set; }

    /// <summary>Whether the product got far enough to write its first record.</summary>
    public bool Started => ProcessId is not 0;

    /// <summary>
    /// Starts the published binary with a launcher that is gone and a console
    /// standard input, and waits until it has written its first record.
    /// </summary>
    /// <param name="appRoot">
    /// The scratch data root to hand it through
    /// <see cref="BrowserAiPaths.AppRootOverride"/>. Must be under the user's
    /// profile: a published BrowserAI refuses to serve out of anything else.
    /// </param>
    /// <param name="startedByTheInstaller">
    /// Whether to set <c>VELOPACK_FIRSTRUN=true</c>. The variable is removed when
    /// this is <see langword="false"/>, never merely left alone.
    /// </param>
    /// <param name="patience">
    /// A hang detector for the start, never a budget. Nothing may assert on it.
    /// </param>
    /// <returns>The started rig, whether or not the product wrote anything.</returns>
    public static OrphanedConsoleStart Begin(string appRoot, bool startedByTheInstaller, TimeSpan patience)
    {
        ArgumentNullException.ThrowIfNull(appRoot);

        var rig = new OrphanedConsoleStart(PublishedSlice.Executable, appRoot);

        rig.Launch(startedByTheInstaller);
        rig.WaitUntilItSaysWhoItIs(patience);

        return rig;
    }

    /// <summary>Everything the product recorded, read from its own log file.</summary>
    /// <remarks>
    /// Scoped to the <c>(pid, creation)</c> pair rather than to the file: the
    /// scratch root is fresh, but the scope is what makes the read say something
    /// about <i>this</i> process rather than about whatever the directory holds.
    /// </remarks>
    /// <returns>Its records, joined, or an empty string when it wrote none.</returns>
    public string Records() =>
        Started
            ? ProcessLogRecords.In(Path.Combine(_appRoot, "logs"), ProcessId, CreatedFileTime)
            : string.Empty;

    /// <summary>Waits for the product to exit on its own.</summary>
    /// <remarks>
    /// ⚠️ <b>Not called <c>WaitForExit</c>, and the name is the point.</b>
    /// <c>ProcessLogTests.EveryTimedWaitForExitIsFollowedByABareOne</c> is a
    /// tree-as-text scan for the timed <c>Process.WaitForExit</c> overload, which
    /// returns without draining the async readers and truncates stderr silently.
    /// A method of that name on another type reads identically to it, so the
    /// scan reported this one — correctly, in the sense that it could not
    /// know. Renaming keeps the scan sharp rather than teaching it an exception.
    /// </remarks>
    /// <param name="patience">
    /// A hang detector, never a budget: the thing being caught is a process that
    /// is never going to exit at all.
    /// </param>
    /// <returns><see langword="true"/> when it went.</returns>
    public bool WaitUntilItExits(TimeSpan patience) =>
        !Started || ProcessIdentity.WaitUntilGone(ProcessId, CreatedFileTime, patience);

    /// <summary>Whether the product is still running.</summary>
    /// <returns><see langword="true"/> when that exact process is alive.</returns>
    public bool IsAlive() => Started && ProcessIdentity.IsAlive(ProcessId, CreatedFileTime);

    /// <summary>
    /// Waits until the product's own records say something, so an arm can assert
    /// on a record rather than on a clock.
    /// </summary>
    /// <param name="sentence">The text to wait for.</param>
    /// <param name="patience">A hang detector, never a budget.</param>
    /// <returns>Whether it appeared.</returns>
    public bool WaitUntilItSays(string sentence, TimeSpan patience)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < patience)
        {
            if (Records().Contains(sentence, StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(PollInterval);
        }

        return Records().Contains(sentence, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ <b>By identity, never by image name</b> — the repository-wide rule.
    /// The pid and its creation time both come out of the product's own first
    /// record, which also names the image, so the thing terminated is provably
    /// the process this rig started. A build in which the product does not exit
    /// leaves an orphan holding a node child, and this is what stops it
    /// outliving the run; its job object takes the child with it.
    /// </remarks>
    public void Dispose()
    {
        if (!IsAlive())
        {
            return;
        }

        ProcessIdentity.Terminate(ProcessId, CreatedFileTime);
        _ = ProcessIdentity.WaitUntilGone(ProcessId, CreatedFileTime, TestDefaults.ProcessHang);
    }

    /// <summary>How often the log is re-read while waiting for a record.</summary>
    /// <remarks>
    /// A poll interval rather than a bound: it decides how often a question is
    /// asked, never how long the answer may take, so it is not a duration any
    /// assertion rests on.
    /// </remarks>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private void Launch(bool startedByTheInstaller)
    {
        var environment = PublishedSlice.InheritedEnvironment();

        environment[BrowserAiPaths.AppRootOverride] = _appRoot;

        if (startedByTheInstaller)
        {
            environment[VelopackStartup.FirstRunVariable] = "true";
        }
        else
        {
            _ = environment.Remove(VelopackStartup.FirstRunVariable);
        }

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            // The inner `start /b` is what makes the product's parent a pid
            // nothing holds a handle to. The outer one is what gives both of
            // them the windowless console.
            Arguments = $"/c start /b \"\" cmd.exe /c start /b \"\" \"{_executable}\"",
            WorkingDirectory = _appRoot,
            UseShellExecute = false,

            // ⚠️ The console is allocated and has no window. Removing this does
            // not merely break the rig: it puts a terminal over whatever is on
            // the maintainer's screen, which is the defect under test.
            CreateNoWindow = true,
        };

        start.Environment.Clear();

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var launcher = Process.Start(start)
            ?? throw new InvalidOperationException("cmd.exe did not start.");

        // Waited for, so that by the time an arm reads anything the outer
        // launcher is already gone. The inner one exits a syscall later and is
        // held by nothing, which is the whole point of the arrangement.
        launcher.WaitForExit();
    }

    private void WaitUntilItSaysWhoItIs(TimeSpan patience)
    {
        var deadline = Stopwatch.StartNew();

        while (deadline.Elapsed < patience && !ReadIdentity())
        {
            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>
    /// Reads the product's identity out of its own first record.
    /// </summary>
    /// <remarks>
    /// <b>Out of the log rather than off the process table</b>, because the pid
    /// and its creation time have to come from the same instant. Every record
    /// carries <c>pid=&lt;n&gt;@&lt;creation&gt;</c> in its header — the pair
    /// <see cref="ProcessLogRecords"/> selects on — and the <c>Startup[1]</c>
    /// record names the image beside it, so the identity is established by the
    /// product saying who it is rather than by this rig guessing.
    /// </remarks>
    /// <returns>Whether the identity was read.</returns>
    private bool ReadIdentity()
    {
        var logs = Path.Combine(_appRoot, "logs");

        if (!Directory.Exists(logs))
        {
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(logs, "browserai-*.log").Order(StringComparer.Ordinal))
        {
            string text;

            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);

                text = reader.ReadToEnd();
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var line in text.Split('\n'))
            {
                if (line.Contains("BrowserAI.Startup[1]", StringComparison.Ordinal)
                    && line.Contains("image=" + _executable, StringComparison.Ordinal)
                    && TryReadHeaderIdentity(line, out var processId, out var created))
                {
                    ProcessId = processId;
                    CreatedFileTime = created;

                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Pulls <c>(pid, creation)</c> out of a record header written as
    /// <c>"  pid=&lt;n&gt;@&lt;creation&gt;  "</c>.
    /// </summary>
    /// <param name="line">One record.</param>
    /// <param name="processId">The pid.</param>
    /// <param name="created">The creation FILETIME.</param>
    /// <returns>Whether the header was in that form.</returns>
    private static bool TryReadHeaderIdentity(string line, out int processId, out long created)
    {
        processId = 0;
        created = 0;

        var opens = line.IndexOf("  pid=", StringComparison.Ordinal);

        if (opens < 0)
        {
            return false;
        }

        var digits = opens + "  pid=".Length;
        var at = line.IndexOf('@', digits);

        if (at < 0)
        {
            return false;
        }

        var closes = line.IndexOf("  ", at, StringComparison.Ordinal);

        return closes > at
            && int.TryParse(line.AsSpan(digits, at - digits), NumberStyles.None, CultureInfo.InvariantCulture, out processId)
            && long.TryParse(line.AsSpan(at + 1, closes - at - 1), NumberStyles.None, CultureInfo.InvariantCulture, out created);
    }
}
