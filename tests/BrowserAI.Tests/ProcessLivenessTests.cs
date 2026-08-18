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
/// <c>InheritedFromUniqueProcessId</c> — a field the kernel writes once at
/// creation and never invalidates — with no creation-time pairing anywhere on
/// the path, and firing that watch tears down every session in the process. The
/// directory's own notes predicted the gap and the gap was already there
/// ([the adversarial review](../../docs/reviews/2026-08-18-adversarial-processes.md),
/// finding 1).
/// </para>
/// <para>
/// <b>A recycled pid cannot be staged, and does not need to be.</b> Nothing can
/// make Windows hand a chosen number to a chosen process on demand. What a
/// recycled pid <i>is</i>, exactly, is a pid presented as the parent whose
/// process started after this one — and that is trivial to stage, because every
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
    /// method body — so a second, unpaired open cannot borrow the first one's
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
                    + $" — {lines[i].Trim()}");
            }
        }

        await Assert.That(string.Join(Environment.NewLine, unpaired)).IsEmpty();

        // A scan that matched nothing would report the tree clean for the one
        // reason that proves nothing about it.
        await Assert.That(sites).IsGreaterThan(0);
    }

    /// <summary>
    /// An <c>OpenProcess</c> <b>call</b>, never its <c>[LibraryImport]</c>
    /// declaration — the declaration has no handle to pair with and every file
    /// that calls it has one.
    /// </summary>
    [GeneratedRegex(@"OpenProcess\(\s*\w")]
    private static partial Regex CallSite();
}
