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
    /// An installer that lays a complete tree down after
    /// <paramref name="after"/>.
    /// </summary>
    /// <param name="directory">The browser directory it creates.</param>
    /// <param name="after">How long the whole thing takes.</param>
    /// <returns>The run.</returns>
    public static FakeInstaller Succeeding(string directory, TimeSpan after) =>
        Scripted(directory, after, createDirectory: true, writeMarker: true, exitCode: 0, "Downloading a browser\nDone");

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

    private static FakeInstaller Scripted(
        string? directory,
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

                if (createDirectory && directory is not null)
                {
                    _ = Directory.CreateDirectory(directory);

                    // Something in it, so a test that asserts a partial tree was
                    // removed is asserting about files rather than about an empty
                    // folder.
                    await File.WriteAllTextAsync(Path.Combine(directory, "chrome.exe"), "not really a browser").ConfigureAwait(false);
                }

                if (thenHang)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, installer._stopped.Token).ConfigureAwait(false);
                }

                if (writeMarker && directory is not null)
                {
                    // Last, exactly as upstream writes it: the whole reason an
                    // interrupted install self-heals.
                    await File.WriteAllTextAsync(
                        Path.Combine(directory, BrowsersManifest.InstallationCompleteMarker),
                        string.Empty).ConfigureAwait(false);
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
