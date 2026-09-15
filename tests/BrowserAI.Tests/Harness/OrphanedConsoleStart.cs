// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.ComponentModel;
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
/// <b>A dead launcher comes in two shapes and the caller picks one</b> — see
/// <see cref="LauncherCorpse"/>. While anything holds a process handle the
/// kernel keeps the process object and <c>OpenProcess</c> succeeds on the
/// corpse, so <i>"the launcher is gone"</i> and <i>"its pid no longer opens"</i>
/// are two different sentences and the product meets both.
/// <see cref="LauncherCorpse.Freed"/> uses two <c>start /b</c>s so that nothing
/// anywhere holds the inner one — the installer's shape, where
/// <c>OpenProcess</c> answers <c>ERROR_INVALID_PARAMETER</c>;
/// <see cref="LauncherCorpse.Openable"/> uses one and the test host keeps the
/// handle, which is the shape <c>Setup.exe</c> leaves behind whenever the
/// console host is still holding the object.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-09-15 (previously "Two <c>start /b</c>s rather than
/// one, because a handle keeps a dead pid openable … a rig in which the test
/// host itself launched the product's parent produced a <i>watchable</i>
/// launcher and the product shut down cleanly, measured 2026-09-15 at
/// <c>.work/2026-09-15-fix/repro-red-2.txt</c>").</b> That measurement was real
/// and the conclusion drawn from it was that the openable corpse had to be
/// designed <i>out</i> of the rig. It could not be: nothing makes Windows free
/// a pid on request, and the second full run of the day found the product
/// meeting an openable corpse anyway and serving nobody for ten minutes on the
/// strength of it. The product decides it now — an opened parent that has
/// already exited is nobody to serve — and this rig produces the shape on
/// purpose rather than avoiding it.
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

    /// <summary>
    /// The launcher, kept undisposed in <see cref="LauncherCorpse.Openable"/> so
    /// that its handle — and with it its pid — outlives the process.
    /// </summary>
    private Process? _launcher;

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
    /// <param name="corpse">
    /// Which of the two dead-launcher shapes to produce. Defaults to
    /// <see cref="LauncherCorpse.Freed"/>, which is the installer's.
    /// </param>
    /// <returns>The started rig, whether or not the product wrote anything.</returns>
    public static OrphanedConsoleStart Begin(
        string appRoot,
        bool startedByTheInstaller,
        TimeSpan patience,
        LauncherCorpse corpse = LauncherCorpse.Freed)
    {
        ArgumentNullException.ThrowIfNull(appRoot);

        var rig = new OrphanedConsoleStart(PublishedSlice.Executable, appRoot);

        rig.Launch(startedByTheInstaller, corpse);
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

    /// <summary>
    /// Waits until the product's records say <b>one</b> of several things, and
    /// reports which.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>It exists so that a lost race fails in a second instead of in ten
    /// minutes.</b> The decision under test has exactly two outcomes and both
    /// are written down — <i>no client to serve</i> or <i>watching the
    /// client</i> — so an arm that waits only for the one it expects spends the
    /// whole of <c>TestDefaults.ProcessHang</c> learning nothing, which is
    /// precisely what the 2026-09-15 flake cost. Waiting for either and
    /// asserting which arrived turns the same failure into a named one.
    /// </para>
    /// <para>
    /// <b>It is not a shortcut around the hang detector.</b> When the product
    /// says neither, this still runs out the full patience and answers
    /// <see langword="null"/> — a product that writes nothing at all is a hang
    /// and is reported as one.
    /// </para>
    /// </remarks>
    /// <param name="patience">A hang detector, never a budget.</param>
    /// <param name="sentences">The alternatives, in no particular order.</param>
    /// <returns>Whichever appeared, or <see langword="null"/>.</returns>
    public string? WaitUntilItSaysOneOf(TimeSpan patience, params string[] sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);

        var deadline = Stopwatch.StartNew();

        while (true)
        {
            var said = Records();
            var seen = sentences.FirstOrDefault(sentence => said.Contains(sentence, StringComparison.Ordinal));

            if (seen is not null || deadline.Elapsed >= patience)
            {
                return seen;
            }

            Thread.Sleep(PollInterval);
        }
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
        try
        {
            TerminateTheProduct();
        }
        finally
        {
            // ⚠️ LAST. This handle is the whole of LauncherCorpse.Openable: it
            // is what keeps the launcher's pid answering OpenProcess, and
            // releasing it before the product has gone would let Windows hand
            // that number to somebody else while a BrowserAI still names it as
            // its client.
            _launcher?.Dispose();
            _launcher = null;
        }
    }

    /// <summary>Terminates the product, if it is still there.</summary>
    private void TerminateTheProduct()
    {
        if (!IsAlive())
        {
            return;
        }

        try
        {
            ProcessIdentity.Terminate(ProcessId, CreatedFileTime);
        }
        catch (Win32Exception failure)
        {
            // ⚠️ THE ONE RACE THIS TEARDOWN CANNOT AVOID, and what bounds the
            // swallow is a re-read rather than an error code.
            //
            // The product this rig starts is a BrowserAI with nobody to serve,
            // and since 2026-09-15 that process EXITS ON ITS OWN in about half
            // a second — which is the whole point of the arms that use this rig.
            // So the liveness check above can be true and the process already on
            // its way out by the time `TerminateProcess` reaches it, and Windows
            // answers a failure for a pid that is mid-teardown. Measured twice
            // on 2026-09-15, on two full runs, on an arm whose assertions had
            // all passed: `Could not terminate process 109176`, then `82304`.
            //
            // ⚠️ An instantaneous `!IsAlive()` filter was tried first and was
            // NOT enough — the process was still in the table when the filter
            // ran, so the exception escaped and the arm was red again. The
            // question is not *is it gone now* but *does it go*, so this waits,
            // bounded by the suite's own hang detector rather than by a number
            // invented here.
            //
            // What is NOT swallowed: a terminate that failed against a process
            // that then stays. That is the case which would leave an orphan
            // holding a node child past the run, and it throws with the original
            // failure attached.
            if (ProcessIdentity.WaitUntilGone(ProcessId, CreatedFileTime, TestDefaults.ProcessHang))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Process {ProcessId} could not be terminated and is still running: {failure.Message} "
                + "It is a BrowserAI started with no client, so it is holding a node child and a job object, "
                + "and it will outlive this run.",
                failure);
        }

        _ = ProcessIdentity.WaitUntilGone(ProcessId, CreatedFileTime, TestDefaults.ProcessHang);
    }

    /// <summary>How often the log is re-read while waiting for a record.</summary>
    /// <remarks>
    /// A poll interval rather than a bound: it decides how often a question is
    /// asked, never how long the answer may take, so it is not a duration any
    /// assertion rests on.
    /// </remarks>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private void Launch(bool startedByTheInstaller, LauncherCorpse corpse)
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
            // Freed: the INNER `start /b` is what makes the product's parent a
            // pid nothing holds a handle to, and the outer one is what gives
            // both of them the windowless console.
            //
            // Openable: one level, because the launcher this rig keeps a handle
            // on has to BE the product's parent. `start` is an internal command
            // of cmd, so cmd itself calls CreateProcess and cmd itself is the
            // parent.
            Arguments = corpse is LauncherCorpse.Freed
                ? $"/c start /b \"\" cmd.exe /c start /b \"\" \"{_executable}\""
                : $"/c start /b \"\" \"{_executable}\"",
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

        var launcher = Process.Start(start)
            ?? throw new InvalidOperationException("cmd.exe did not start.");

        if (corpse is LauncherCorpse.Freed)
        {
            // Waited for, so that by the time an arm reads anything the outer
            // launcher is already gone. The inner one exits a syscall later and
            // is held by nothing, which is the whole point of the arrangement.
            using (launcher)
            {
                launcher.WaitForExit();
            }

            return;
        }

        // ⚠️ KEPT, AND THE KEEPING IS THE MECHANISM. `Process` holds the handle
        // it was started with until it is disposed, and a held handle is what
        // stops the kernel releasing the process object — so this pid answers
        // OpenProcess for as long as this rig lives, and answers it about a
        // process that has exited. Disposing it here, as `using` would, is the
        // one thing that would turn this mode back into the other one.
        _launcher = launcher;
        _launcher.WaitForExit();
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

internal enum LauncherCorpse
{
    /// <summary>
    /// The launcher's pid is <b>freed</b>: nothing anywhere holds a handle to
    /// it, so <c>OpenProcess</c> answers <c>ERROR_INVALID_PARAMETER</c>.
    /// </summary>
    /// <remarks>
    /// <b>The installer's own shape</b>, and the one the 2026-09-14 incident
    /// was measured in: <c>Setup.exe</c> starts the app and exits, and nothing
    /// is left holding the number. Reached with two <c>start /b</c>s, so that
    /// the process which is the product's parent is one this rig never had a
    /// handle on.
    /// </remarks>
    Freed,

    /// <summary>
    /// The launcher's pid <b>still opens</b>, and the process behind it has
    /// exited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Equally real, and it is what the product actually meets.</b> A pid is
    /// kept alive by any handle anywhere — the console host holds one for a
    /// process that ran in a console — so a launcher that is gone may still be
    /// openable for as long as the kernel is holding the object. Nothing about
    /// that is exotic and nothing about it is up to the rig.
    /// </para>
    /// <para>
    /// <b>Here it is up to the rig, which is the point.</b> The test host starts
    /// the launcher itself and keeps the <c>Process</c> — and its handle — for
    /// the whole life of the rig, so the corpse is openable <i>by
    /// construction</i> rather than by luck. That is what makes the arms over
    /// this mode deterministic where the 2026-09-15 flake was a coin toss.
    /// </para>
    /// </remarks>
    Openable,
}
