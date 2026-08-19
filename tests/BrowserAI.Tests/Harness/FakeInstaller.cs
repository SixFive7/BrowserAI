// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A scripted <see cref="IInstallerRun"/>, so the provisioning state machine and
/// its three caps can be driven without downloading 203.8 MB per assertion.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it replaces is one <c>CreateProcessW</c> and nothing else.</b> The
/// marker check, the machine-wide mutex, the phase watcher, every cap and the
/// removal of a partial tree are all on the product's side of the seam and run
/// here exactly as they do in production. What a double cannot say anything about
/// is whether upstream's installer works — and that is why the empty-root run
/// against the published binary exists and downloads for real.
/// </para>
/// <para>
/// <b>It writes what the real one writes, in the order the real one writes
/// it.</b> The browser's directory appears first and
/// <c>INSTALLATION_COMPLETE</c> last, because that ordering is exactly what the
/// watcher reads as the download-to-extraction boundary and what makes an
/// interrupted install self-heal. A double that created both at once would make
/// the extraction cap untestable and would never have caught a watcher that
/// looked for the wrong thing.
/// </para>
/// </remarks>
internal sealed class FakeInstaller : IInstallerRun
{
    private readonly CancellationTokenSource _stopped = new();
    private int _disposed;

    private FakeInstaller()
    {
    }

    private static int _starts;

    /// <summary>How many times a double has been asked to install, ever.</summary>
    /// <remarks>
    /// Static because the interesting assertion is across <i>provisioners</i>: two
    /// of them over one browsers root must produce one install, not two, and a
    /// per-instance counter cannot see that.
    /// </remarks>
    public static int Starts => Volatile.Read(ref _starts);

    /// <summary>
    /// Whether the run has finished. <b>Written last, always</b>: the watcher
    /// reads it and then reads the exit code, so a double that published the
    /// flag first would hand back -1 on a run that succeeded.
    /// </summary>
    public bool HasExited { get; private set; }

    /// <inheritdoc />
    public int ExitCode { get; private set; } = -1;

    /// <inheritdoc />
    public string Output { get; private set; } = string.Empty;

    /// <summary>Whether <see cref="Stop"/> was called, which is what a cap does.</summary>
    public bool WasStopped { get; private set; }

    /// <summary>
    /// How many of a creeping run's writes have landed, so an arm asserting that
    /// a slow install survived can prove it really was slow.
    /// </summary>
    public int StepsWritten { get; private set; }

    /// <summary>
    /// An installer that lays a complete tree down after
    /// <paramref name="after"/>.
    /// </summary>
    /// <param name="directory">The browser directory it creates.</param>
    /// <param name="after">How long the whole thing takes.</param>
    /// <returns>The run.</returns>
    public static FakeInstaller Succeeding(string directory, TimeSpan after) =>
        Scripted(directory, after, createDirectory: true, writeMarker: true, exitCode: 0, "Downloading a browser\nDone");

    /// <summary>
    /// An installer that lays down <b>several</b> complete trees, which is what
    /// one <c>install-browser ffmpeg</c> really does.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A double must not be less capable than the thing it replaces.</b>
    /// Measured 2026-08-19 against the resolved payload: one
    /// <c>install-browser ffmpeg</c> into an empty root produced
    /// <c>ffmpeg-1011</c> and <c>winldd-1007</c>, each with its own
    /// <c>INSTALLATION_COMPLETE</c>. A double that wrote one marker would make
    /// the product's per-component completeness check fail against a fake rather
    /// than against a fault.
    /// </remarks>
    /// <param name="directories">Every directory it creates and marks.</param>
    /// <param name="after">How long the whole thing takes.</param>
    /// <returns>The run.</returns>
    public static FakeInstaller SucceedingForAll(IReadOnlyList<string> directories, TimeSpan after) =>
        Scripted(directories, after, createDirectory: true, writeMarker: true, exitCode: 0, "Downloading a browser\nDone");

    /// <summary>
    /// An installer that finishes only when the test says so.
    /// </summary>
    /// <remarks>
    /// <b>A duration is not a gate, and this is what replaced one.</b> The
    /// in-session recovery test asserted that a call is refused <i>while</i> the
    /// download runs and succeeds after; with a 400 ms double it passed until a
    /// fast machine finished the install before the first call arrived, and then
    /// failed on the refusal rather than on the recovery. A completion source
    /// makes both halves deterministic: nothing lands until the test releases it.
    /// </remarks>
    /// <param name="directory">The browser directory it creates.</param>
    /// <param name="release">Completed by the test when the install should land.</param>
    /// <returns>The run.</returns>
    public static FakeInstaller SucceedingWhenReleased(string directory, Task release) =>
        Scripted(directory, TimeSpan.Zero, createDirectory: true, writeMarker: true, exitCode: 0, "Downloading a browser\nDone", release: release);

    /// <summary>
    /// An installer that exits 0 having written no marker — the shape that
    /// produces <c>spawn EFTYPE</c> and never re-downloads.
    /// </summary>
    /// <param name="directory">The browser directory it creates.</param>
    /// <returns>The run.</returns>
    public static FakeInstaller ExitingCleanWithoutTheMarker(string directory) =>
        Scripted(directory, TimeSpan.Zero, createDirectory: true, writeMarker: false, exitCode: 0, "Downloading a browser");

    /// <summary>An installer that fails the way a dead mirror does.</summary>
    /// <param name="directory">The browser directory it creates before failing.</param>
    /// <param name="said">What it wrote before dying.</param>
    /// <returns>The run.</returns>
    public static FakeInstaller Failing(string directory, string said) =>
        Scripted(directory, TimeSpan.Zero, createDirectory: true, writeMarker: false, exitCode: 1, said);

    /// <summary>
    /// An installer that never finishes and never creates the directory, which is
    /// what a stalled download looks like.
    /// </summary>
    /// <returns>The run.</returns>
    public static FakeInstaller Hanging() =>
        Scripted(directory: null, TimeSpan.Zero, createDirectory: false, writeMarker: false, exitCode: 0, "Downloading a browser", thenHang: true);

    /// <summary>
    /// An installer that creates the directory and then stalls, which is what a
    /// wedged extraction looks like.
    /// </summary>
    /// <param name="directory">The browser directory it creates.</param>
    /// <returns>The run.</returns>
    public static FakeInstaller StallingInExtraction(string directory) =>
        Scripted(directory, TimeSpan.Zero, createDirectory: true, writeMarker: false, exitCode: 0, "Downloading a browser", thenHang: true);

    /// <summary>
    /// An installer that is <b>slow and working</b>: it writes a little every
    /// step, for far longer than the stall cap it runs under, and then completes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written for the arm that the old total-time cap could not pass.</b> A
    /// ceiling on the whole install fires here on the fifth step; a detector that
    /// measures the gap between writes never sees one longer than a step. The
    /// bytes go into the browser's own directory, which is where a real
    /// extraction puts them and which the product weighs as part of the browsers
    /// root.
    /// </para>
    /// <para>
    /// <b>Each step is a NEW file rather than an append.</b> The product sums file
    /// lengths, and a growing file and a new file are the same to it — but a new
    /// file cannot be mistaken for a buffered write that has not reached the
    /// filesystem yet, which is the one way this double could report progress the
    /// product cannot see.
    /// </para>
    /// </remarks>
    /// <param name="directory">The browser directory it creates and fills.</param>
    /// <param name="step">How long between writes.</param>
    /// <param name="steps">How many writes.</param>
    /// <returns>The run.</returns>
    public static FakeInstaller CreepingForward(string directory, TimeSpan step, int steps)
    {
        var installer = new FakeInstaller();

        _ = Interlocked.Increment(ref _starts);

        // ⚠️ A THREAD OF ITS OWN, and it is a correctness requirement rather than
        // a preference. The product's watcher polls from a `LongRunning` thread
        // and never starves; a double that ticked from the thread pool would
        // starve at unbounded suite parallelism, so its gaps would stretch while
        // the watcher's did not -- and the stall cap would then fire on the
        // scheduler rather than on the behaviour under test. Observed exactly
        // that way on 2026-08-19: green alone, red in a full run. The real
        // installer is a separate OS process and is never pool-bound either, so
        // this is the double being as schedulable as the thing it replaces.
        _ = Task.Factory.StartNew(
            () =>
            {
                _ = Directory.CreateDirectory(directory);

                for (var index = 0; index < steps; index++)
                {
                    if (installer._stopped.Token.WaitHandle.WaitOne(step))
                    {
                        // Stopped by a cap, which is what the other arm asserts.
                        return;
                    }

                    PlantPart(directory, index);
                    installer.StepsWritten = index + 1;
                }

                InstallationMarker.Write(directory);

                installer.Output = "Downloading a browser\nDone";
                installer.ExitCode = 0;
                installer.HasExited = true;
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return installer;
    }

    /// <summary>
    /// An installer that writes a known number of bytes into a file and then
    /// waits, so a progress sentence has something predictable to quote.
    /// </summary>
    /// <remarks>
    /// <b>It never creates the browser directory</b>, which is what keeps the
    /// product in its download phase: the revision directory appearing is the
    /// phase boundary, and a double that created it would be asserting the
    /// extraction sentence instead.
    /// </remarks>
    /// <param name="file">The file to write, whose parent directories are created.</param>
    /// <param name="bytes">How many bytes to write.</param>
    /// <param name="release">Completed by the test when the run should end.</param>
    /// <returns>The run.</returns>
    public static FakeInstaller WritingThenWaiting(string file, int bytes, Task release)
    {
        var installer = new FakeInstaller();

        _ = Interlocked.Increment(ref _starts);

        _ = Task.Run(async () =>
        {
            try
            {
                PlantArchive(file, bytes);
                installer.StepsWritten = 1;

                await release.WaitAsync(installer._stopped.Token).ConfigureAwait(false);

                installer.Output = "Downloading a browser";
                installer.ExitCode = 1;
                installer.HasExited = true;
            }
            catch (OperationCanceledException)
            {
                // Disposed with the rig before the test released it.
            }
        });

        return installer;
    }

    /// <inheritdoc />
    public void Stop()
    {
        WasStopped = true;
        _stopped.Cancel();
        HasExited = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        _stopped.Cancel();
        _stopped.Dispose();
    }

    /// <summary>
    /// Creates a half-finished browser tree, with a file in it so that a test
    /// asserting a partial tree was removed is asserting about files rather than
    /// about an empty folder.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>One synchronous step, in a method of its own, and both halves of
    /// that are the point.</b> Written inline as <c>await WriteAllTextAsync</c>
    /// there is a suspension point in the middle of "the tree appears" — and the
    /// provisioner's watcher is polling for exactly that directory. Under a
    /// congested thread pool the extraction cap then fires, deletes the tree,
    /// and the continuation <b>re-creates</b> it, so the test fails against a
    /// product that did remove it. Observed once in ten full-suite runs on
    /// 2026-08-16. It lives here rather than inline because the blocking-call
    /// analyzer is right about async methods in general and wrong about this
    /// one; a suppression would have turned that off for the whole body.
    /// </remarks>
    /// <param name="directory">The browser directory to plant.</param>
    private static void PlantPartialTree(string directory)
    {
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "chrome.exe"), "not really a browser");
    }

    /// <summary>
    /// One step of a creeping install, in a method of its own for
    /// <see cref="PlantPartialTree"/>'s reason: the blocking-call analyzer is
    /// right about <c>async</c> bodies in general and wrong about this one, and a
    /// suppression would switch it off for the whole method.
    /// </summary>
    /// <param name="directory">The browser directory.</param>
    /// <param name="index">Which step this is.</param>
    private static void PlantPart(string directory, int index) =>
        File.WriteAllText(
            Path.Combine(directory, $"part-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}.bin"),
            new string('x', 512));

    /// <summary>
    /// A download in flight, as bytes in the directory upstream downloads into.
    /// </summary>
    /// <param name="file">The archive path.</param>
    /// <param name="bytes">How large it is.</param>
    private static void PlantArchive(string file, int bytes)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, new byte[bytes]);
    }

    private static FakeInstaller Scripted(
        string? directory,
        TimeSpan after,
        bool createDirectory,
        bool writeMarker,
        int exitCode,
        string said,
        bool thenHang = false,
        Task? release = null) =>
        Scripted(directory is null ? [] : [directory], after, createDirectory, writeMarker, exitCode, said, thenHang, release);

    private static FakeInstaller Scripted(
        IReadOnlyList<string> directories,
        TimeSpan after,
        bool createDirectory,
        bool writeMarker,
        int exitCode,
        string said,
        bool thenHang = false,
        Task? release = null)
    {
        _ = Interlocked.Increment(ref _starts);

        var installer = new FakeInstaller();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(after, installer._stopped.Token).ConfigureAwait(false);

                if (release is not null)
                {
                    // The test decides when this install lands, so "refused
                    // while downloading" and "succeeds afterwards" are both
                    // ordered facts rather than races against a duration.
                    await release.WaitAsync(installer._stopped.Token).ConfigureAwait(false);
                }

                if (createDirectory)
                {
                    foreach (var directory in directories)
                    {
                        PlantPartialTree(directory);
                    }
                }

                if (thenHang)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, installer._stopped.Token).ConfigureAwait(false);
                }

                if (writeMarker)
                {
                    // Last, exactly as upstream writes it: the whole reason an
                    // interrupted install self-heals.
                    // Shared, because a test may be writing the same empty file
                    // at the same instant -- see InstallationMarker's remarks.
                    foreach (var directory in directories)
                    {
                        InstallationMarker.Write(directory);
                    }
                }

                installer.Output = said;
                installer.ExitCode = exitCode;

                // Last. The watcher reads HasExited and then ExitCode, so
                // publishing the flag first would report -1 for a run that
                // succeeded.
                installer.HasExited = true;
            }
            catch (OperationCanceledException)
            {
                // Stopped by a cap or by disposal, which is the case under test
                // rather than a fault.
            }
        });

        return installer;
    }
}
