// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.RegularExpressions;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging;

namespace BrowserAI.Tests;

/// <summary>
/// The rule <c>src\BrowserAI\Interop\CLAUDE.md</c> states and no analyzer can
/// see: <b>a process is <c>(pid, creationFileTime)</c>, never a bare pid.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this was written against was in the file that states the
/// rule.</b> <c>ClientLivenessWatcher</c> opened the pid from
/// <c>InheritedFromUniqueProcessId</c> -- a field the kernel writes once at
/// creation and never invalidates -- with no creation-time pairing anywhere on
/// the path, and firing that watch tears down every session in the process. The
/// directory's own notes predicted the gap and the gap was already there
/// ([the adversarial review](../../docs/reviews/2026-08-18-adversarial-processes.md),
/// finding 1).
/// </para>
/// <para>
/// <b>A recycled pid cannot be staged, and does not need to be.</b> Nothing can
/// make Windows hand a chosen number to a chosen process on demand. What a
/// recycled pid <i>is</i>, exactly, is a pid presented as the parent whose
/// process started after this one -- and that is trivial to stage, because every
/// process a test starts has that property. So the interleaving is not
/// simulated; the state it produces is constructed directly.
/// </para>
/// </remarks>
internal sealed partial class ProcessLivenessTests
{
    /// <summary>
    /// How far after an <c>OpenProcess</c> call site the pairing has to appear.
    /// </summary>
    /// <remarks>
    /// The widest real gap in the tree is eleven lines
    /// (<c>BrowserProcesses.ScanFor</c>, which has a comment and an early-out in
    /// between). Twenty-five leaves room for a call site to grow a guard without
    /// the number becoming the thing under test, and is still far short of a
    /// method body -- so a second, unpaired open cannot borrow the first one's
    /// pairing.
    /// </remarks>
    private const int PairingWindow = 25;

    /// <summary>
    /// What counts as reading a process's creation time.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Adding a name here is the whole decision this test exists to
    /// force.</b> It is not a list of spellings to keep tidy: a new entry is a
    /// claim that some new call establishes the identity of the handle just
    /// opened, and that claim is exactly what nothing else in this repository
    /// can check.
    /// </remarks>
    private static readonly string[] Pairings =
    [
        "GetProcessTimes(",
        "StartedNoLaterThanThisProcess(",
    ];

    [Test]
    public async Task AWatchIsRefusedWhenTheCreationTimeIsNotTheOneRecordedBesideThePid()
    {
        using var scope = new JobObjectScope();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => _ = builder.AddProvider(logs));

        var client = scope.Launch(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Path.GetTempPath());

        var created = ProcessIdentity.CreationTimeOf(client.Id);

        // A pid whose recorded creation time does not match what the handle
        // reports IS a recycled pid, whatever produced the mismatch.
        using var mismatched = ClientLivenessWatcher.ForProcess(
            client.Id,
            created + 1,
            () => throw new InvalidOperationException("A watch on a mismatched identity must never be armed, let alone fire."),
            factory.CreateLogger("mismatched"));

        await Assert.That(mismatched).IsNull();

        // The positive control on the same pid: the pair that IS the process is
        // accepted, so the refusal above is the pairing and not a refusal of
        // everything.
        using var matched = ClientLivenessWatcher.ForProcess(
            client.Id,
            created,
            () => { },
            factory.CreateLogger("matched"));

        await Assert.That(matched).IsNotNull();
        await Assert.That(matched!.ProcessId).IsEqualTo(client.Id);
        await Assert.That(matched.CreatedFileTime).IsEqualTo(created);
    }

    [Test]
    public async Task AParentThatStartedAfterThisProcessIsRefusedBecauseItCannotBeTheParent()
    {
        using var scope = new JobObjectScope();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => _ = builder.AddProvider(logs));

        var stranger = scope.Launch(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Path.GetTempPath());

        // `recordedCreation: null` is the parent path: the pid arrived from
        // InheritedFromUniqueProcessId and there is no creation time beside it
        // anywhere. This process is one this test started, so it started after
        // us -- which is precisely the state a recycled wrapper pid produces,
        // and the state the watch used to accept and arm.
        using var watcher = ClientLivenessWatcher.ForProcess(
            stranger.Id,
            recordedCreation: null,
            () => throw new InvalidOperationException("A watch on a process that cannot be our parent must never be armed, let alone fire."),
            factory.CreateLogger("stranger"));

        await Assert.That(watcher).IsNull();

        // The refusal is said out loud, because a mechanism that declines
        // silently is indistinguishable from one that is not there.
        await Assert.That(logs.Records.Any(record => record.EventId.Id is 75)).IsTrue();

        // The positive control for the same arm: this process did not start
        // after itself, so it passes the test the stranger failed.
        using var self = ClientLivenessWatcher.ForProcess(
            Environment.ProcessId,
            recordedCreation: null,
            () => { },
            factory.CreateLogger("self"));

        await Assert.That(self).IsNotNull();
    }

    /// <summary>
    /// A pid that <b>opens</b> and whose process has <b>already exited</b> is
    /// nobody to watch -- the same answer as a pid that cannot be opened at all,
    /// arriving by a different route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A corpse stays openable for as long as anything holds a handle to
    /// it</b>, and for a launcher that ran in a console something does. So
    /// <i>"OpenProcess succeeded"</i> is not <i>"there is somebody there"</i>,
    /// and reading it that way is what made
    /// <c>InstallerHandoffTests.ARunWithNobodyToServeStartsNothingAndCreatesNothingButItsLog</c>
    /// fail once in four full runs on 2026-09-15: the watch attached to a dead
    /// launcher, the no-client fast exit was skipped on the strength of it, and
    /// the product then served nobody for the whole of
    /// <c>TestDefaults.ProcessHang</c>.
    /// </para>
    /// <para>
    /// <b>The identity pairing runs first and is not what this asks.</b> A
    /// recycled pid belongs to a stranger and is refused by
    /// <see cref="ProcessLiveness.StartedNoLaterThanThisProcess"/>; this pid is
    /// the real launcher, correctly identified, and gone.
    /// </para>
    /// <para>
    /// <b>Nothing here races anything.</b> The job scope owns a
    /// <c>SafeProcessHandle</c> for the whole life of what it launched, which is
    /// what keeps the pid openable after the exit; the process is waited for,
    /// and only then asked about.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AWatchIsRefusedWhenThePidOpensAndItsProcessHasAlreadyExited()
    {
        using var scope = new JobObjectScope();
        using var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => _ = builder.AddProvider(logs));

        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var corpse = scope.Launch(cmd, Path.GetTempPath(), "/c", "exit");
        var created = ProcessIdentity.CreationTimeOf(corpse.Id);

        await Assert.That(await corpse.WaitForExitAsync(TestDefaults.ProcessHang)).IsTrue();

        var fired = 0;

        // The recorded-pair route: the caller has a creation time beside the
        // pid, it matches, and the process is still gone.
        using var recorded = ClientLivenessWatcher.ForProcess(
            corpse.Id,
            created,
            () => Interlocked.Increment(ref fired),
            factory.CreateLogger("corpse"));

        await Assert.That(recorded).IsNull();

        // And the parent route, which is the one that matters: the pid arrives
        // from InheritedFromUniqueProcessId with no creation time beside it.
        using var asAParent = ClientLivenessWatcher.ForProcess(
            corpse.Id,
            recordedCreation: null,
            () => Interlocked.Increment(ref fired),
            factory.CreateLogger("corpse-as-parent"));

        await Assert.That(asAParent).IsNull();

        // ⚠️ NOT MERELY NULL: nothing was armed either. A watcher built over an
        // already-signalled handle fires on the line that registers it, so a
        // build that returned one would have run the teardown callback as well
        // -- and against a real client that is every session's browser.
        await Assert.That(Volatile.Read(ref fired)).IsEqualTo(0);

        // The refusal is said out loud, and says WHICH of the two it saw: 78 is
        // "opened, and already gone", where 72 is "could not be opened".
        //
        // ⚠️ 78 , NOT 76. Corrected 2026-09-22 under Q226 c *(previously
        // `record.EventId.Id is 76`)*: `ClientHasAlreadyExited` moved off 76
        // when that id was retired, because two events held it at once and both
        // of them shipped in v1.0.0. **This arm is the reason the renumber is a
        // behaviour change and not a comment edit** -- an id is what a reader
        // of the log keys on, and this is the one place in the suite that reads
        // one back off a real record.
        await Assert.That(logs.Records.Any(record => record.EventId.Id is 78)).IsTrue();
        await Assert.That(logs.Records.Any(record => record.EventId.Id is 72)).IsFalse();

        // And 76 is gone and not merely unused here: nothing this product
        // emits carries it any more, which is what "retired" has to mean.
        await Assert.That(logs.Records.Any(record => record.EventId.Id is 76)).IsFalse();

        // The positive control, on the same route and the same call: a process
        // that is actually there is still watched. Without it a build that
        // refused every watch would pass this arm.
        var live = scope.Launch(cmd, Path.GetTempPath());

        using var watched = ClientLivenessWatcher.ForProcess(
            live.Id,
            ProcessIdentity.CreationTimeOf(live.Id),
            () => { },
            factory.CreateLogger("live"));

        await Assert.That(watched).IsNotNull();
    }

    [Test]
    public async Task EveryProcessHandleOpenedInTheProductIsPairedWithACreationTimeRead()
    {
        var unpaired = new List<string>();
        var sites = 0;

        foreach (var file in RepositoryLayout.ProductSourceFiles)
        {
            var lines = await File.ReadAllLinesAsync(file.FullName);

            for (var i = 0; i < lines.Length; i++)
            {
                if (!CallSite().IsMatch(lines[i]))
                {
                    continue;
                }

                sites++;

                var window = lines.Skip(i).Take(PairingWindow);

                if (window.Any(line => Pairings.Any(pairing => line.Contains(pairing, StringComparison.Ordinal))))
                {
                    continue;
                }

                unpaired.Add(
                    $"{Path.GetRelativePath(RepositoryLayout.Root.FullName, file.FullName)}:{(i + 1).ToString(CultureInfo.InvariantCulture)}"
                    + $" -- {lines[i].Trim()}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, unpaired)).IsEmpty();

        // A scan that matched nothing would report the tree clean for the one
        // reason that proves nothing about it.
        await Assert.That(sites).IsGreaterThan(0);
    }

    /// <summary>
    /// An <c>OpenProcess</c> <b>call</b>, never its <c>[LibraryImport]</c>
    /// declaration -- the declaration has no handle to pair with and every file
    /// that calls it has one.
    /// </summary>
    [GeneratedRegex(@"OpenProcess\(\s*\w")]
    private static partial Regex CallSite();
}
