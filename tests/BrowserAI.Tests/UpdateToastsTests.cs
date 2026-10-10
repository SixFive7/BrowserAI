// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Background;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The four update toasts as the background and the after-update start raise
/// them, driven through a surface that records what Windows would have been asked
/// and a clock the arm moves.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here reaches Windows</b> (Q278): the product's one surface that does
/// is banned in this project's <c>BannedSymbols.txt</c>, and every arm hands
/// <see cref="UpdateToasts"/> a <see cref="RecordingSurface"/>.
/// </para>
/// <para>
/// <b>Each toast removes the others before it is shown</b>, because a replacement
/// under one tag popped up again in only 4 of 6 replacements measured on
/// 2026-10-08, and the installing and installed toasts must be seen.
/// </para>
/// </remarks>
internal sealed class UpdateToastsTests
{
    /// <summary>
    /// <i>Held</i> raises the ready toast once per version, and its countdown is then
    /// written every second with a sequence number one higher each time.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task HeldRaisesTheReadyToastOnceAndWritesItsCountdownEverySecond()
    {
        using var rig = new Rig();

        rig.Toasts.Held("1.2.0");

        await Assert.That(rig.Surface.Lines()).IsEqualTo(Lines(
            "remove installing",
            "remove installed",
            "remove failed",
            "show ready seq=1 banner=yes reboot=yes status='Installs in 50:00 if nothing uses it' title='In use by 1 visible window'"));
        await Assert.That(rig.Surface.Shown.Single().Xml).Contains("BrowserAI 1.2.0 is ready to install");
        await Assert.That(rig.Toasts.Counting).IsTrue();

        // Once per version: the background calling again changes nothing.
        rig.Toasts.Held("1.2.0");
        rig.Surface.Clear();

        rig.Clock.Advance(UpdateToasts.Tick);
        rig.Clock.Advance(UpdateToasts.Tick);

        await Assert.That(rig.Surface.Lines()).IsEqualTo(Lines(
            "update ready seq=2 status='Installs in 49:59 if nothing uses it' title='In use by 1 visible window'",
            "update ready seq=3 status='Installs in 49:58 if nothing uses it' title='In use by 1 visible window'"));
    }

    /// <summary>
    /// The ready toast names the conversations its reconnect line speaks of once, when
    /// it is raised, and its countdown names none: a second of it reads no client's
    /// records.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>1.3 c, the maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept all
    /// your recommendations"</i></b>: the files are read when a toast is drawn. The
    /// reconnect line is written when the toast is raised and does not change after,
    /// and the countdown shows no name, so a toast left counting down for hours would
    /// otherwise read every client's records once a second for nothing.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against a countdown that read the named snapshot.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheRaiseNamesTheConversationsAndTheCountdownReadsNoName()
    {
        using var rig = new Rig();

        rig.Toasts.Held("1.2.0");

        await Assert.That(rig.Holds.NamedReads).IsEqualTo(1);
        await Assert.That(rig.Holds.CountdownReads).IsEqualTo(0);

        for (var second = 0; second < 3; second++)
        {
            rig.Clock.Advance(UpdateToasts.Tick);
        }

        await Assert.That(rig.Holds.CountdownReads).IsEqualTo(3);
        await Assert.That(rig.Holds.NamedReads).IsEqualTo(1).Because("a second of the countdown read every client's records");
    }

    /// <summary>
    /// Once Windows answers that the ready toast is gone, which is what a click or a
    /// dismissal leaves, nothing more is written to it.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheCountdownStopsOnceWindowsNoLongerHasTheToast()
    {
        using var rig = new Rig();

        rig.Surface.Answer = sequence => sequence >= 3 ? ToastUpdateResult.NotificationNotFound : ToastUpdateResult.Succeeded;
        rig.Toasts.Held("1.2.0");
        rig.Surface.Clear();

        for (var second = 0; second < 5; second++)
        {
            rig.Clock.Advance(UpdateToasts.Tick);
        }

        await Assert.That(rig.Surface.Lines().Split('\n').Length).IsEqualTo(2);
        await Assert.That(rig.Toasts.Counting).IsFalse();
        await Assert.That(rig.Logs.Logged("The ready toast for 1.2.0 is gone, so its countdown stopped.")).IsTrue();
    }

    /// <summary>
    /// The installing, installed and failed toasts each remove every other update
    /// toast first and are then shown fresh under their own tag, and each stops the
    /// countdown.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InstallingInstalledAndFailedRemoveTheOthersFirstAndStopTheCountdown()
    {
        using var rig = new Rig();

        rig.Toasts.Held("1.2.0");
        rig.Surface.Clear();
        rig.Toasts.Installing("1.2.0");

        await Assert.That(rig.Surface.Lines()).IsEqualTo(Lines("remove ready", "remove installed", "remove failed", "show installing seq=0 banner=yes reboot=yes"));
        await Assert.That(rig.Surface.Shown[^1].Xml).Contains("Installing BrowserAI 1.2.0 now");
        await Assert.That(rig.Toasts.Counting).IsFalse();

        rig.Surface.Clear();
        rig.Clock.Advance(UpdateToasts.Tick);

        await Assert.That(rig.Surface.Lines()).IsEmpty();

        // The after-update start is another process with no countdown of its own.
        using var after = new Rig(holds: false);

        after.Memory.Waited("1.2.0");
        after.Toasts.Installed("1.2.0");

        await Assert.That(after.Surface.Lines()).IsEqualTo(Lines("remove ready", "remove installing", "remove failed", "show installed seq=0 banner=yes reboot=no"));
        await Assert.That(after.Memory.WaitedFor()).IsNull();

        after.Surface.Clear();
        after.Memory.Waited("1.2.0");
        after.Toasts.Failed("1.2.0");

        await Assert.That(after.Surface.Lines()).IsEqualTo(Lines("remove ready", "remove installing", "remove installed", "show failed seq=0 banner=yes reboot=no"));
        await Assert.That(after.Surface.Shown[^1].Xml).Contains("BrowserAI 1.1.0 is still installed.");
        await Assert.That(after.Surface.Shown[^1].Xml).Contains(Rig.Facts.VelopackLog);
        await Assert.That(after.Memory.WaitedFor()).IsNull();

        // And a process that holds no update raises no ready toast.
        after.Toasts.Held("1.3.0");

        await Assert.That(after.Surface.Shown.Count(toast => toast.Xml.Contains("ready to install", StringComparison.Ordinal))).IsEqualTo(0);
    }

    /// <summary>
    /// A version the person answered <i>Wait for inactivity</i> for is raised again
    /// with no banner, straight into the Notification Centre; any other version pops
    /// up.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AVersionThePersonChoseToWaitForIsRaisedAgainWithNoBanner()
    {
        using var waited = new Rig();

        waited.Memory.Waited("1.2.0");
        waited.Toasts.Held("1.2.0");

        await Assert.That(waited.Surface.Lines().Split('\n')[^1]).StartsWith("show ready seq=1 banner=no reboot=yes");

        using var other = new Rig();

        other.Memory.Waited("1.1.9");
        other.Toasts.Held("1.2.0");

        await Assert.That(other.Surface.Lines().Split('\n')[^1]).StartsWith("show ready seq=1 banner=yes reboot=yes");
    }

    /// <summary>
    /// An update that stops waiting takes its ready toast with it, and one that the
    /// background has started installing stops the countdown for the installing
    /// toast to replace.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUpdateThatStopsWaitingTakesItsCountdownWithIt()
    {
        using var rig = new Rig();

        rig.Toasts.Held("1.2.0");
        rig.Surface.Clear();
        rig.Holds.Snapshot = UpdateHoldSnapshot.Nothing(rig.Clock.GetUtcNow());
        rig.Clock.Advance(UpdateToasts.Tick);

        await Assert.That(rig.Surface.Lines()).IsEqualTo(Lines("remove ready"));
        await Assert.That(rig.Toasts.Counting).IsFalse();

        using var installing = new Rig();

        installing.Toasts.Held("1.2.0");
        installing.Surface.Clear();
        installing.Holds.Snapshot = installing.Holds.Snapshot with { State = UpdateHoldState.Installing };
        installing.Clock.Advance(UpdateToasts.Tick);

        await Assert.That(installing.Surface.Lines()).IsEmpty();
        await Assert.That(installing.Toasts.Counting).IsFalse();
    }

    /// <summary>What Windows refuses is a log record, and nothing throws into the background.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WhatWindowsRefusesIsALogRecordAndNothingThrows()
    {
        using var rig = new Rig();

        rig.Surface.ShowThrows = true;
        rig.Toasts.Held("1.2.0");

        await Assert.That(rig.Toasts.Counting).IsFalse();
        await Assert.That(rig.Logs.Logged("Windows did not show the update toast 'ready'.")).IsTrue();

        using var updates = new Rig();

        updates.Toasts.Held("1.2.0");
        updates.Surface.UpdateThrows = true;
        updates.Clock.Advance(UpdateToasts.Tick);
        updates.Surface.UpdateThrows = false;
        updates.Clock.Advance(UpdateToasts.Tick);

        await Assert.That(updates.Logs.Logged("Windows threw on an update of the ready toast's countdown.")).IsTrue();
        await Assert.That(updates.Toasts.Counting).IsTrue();
        await Assert.That(updates.Surface.Lines().Split('\n')[^1]).StartsWith("update ready seq=3 ");
    }

    /// <summary>The person's wait is one file in the data root, read back and forgotten.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePersonsWaitIsOneFileReadBackAndForgotten()
    {
        using var scratch = ScratchDirectory.Create("toast-memory");

        var memory = UpdateToastMemoryFile.In(Path.Combine(scratch.Path, "data"));

        await Assert.That(memory.WaitedFor()).IsNull();

        memory.Waited("1.2.0");

        await Assert.That(memory.WaitedFor()).IsEqualTo("1.2.0");
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(scratch.Path, "data", UpdateToastMemoryFile.FileName))).IsEqualTo("1.2.0");

        memory.Forget();
        memory.Forget();

        await Assert.That(memory.WaitedFor()).IsNull();
    }

    /// <summary>
    /// The toasts are handed what holds the update and nothing that installs: the
    /// indirection the background builds them over reads, and has no install-now.
    /// </summary>
    /// <remarks>
    /// <b>#101 of the texts review, 2026-10-10</b>: the indirection answered an
    /// install-now with <i>"BrowserAI is still starting, so it installs nothing now."</i>,
    /// which nothing could ever show: only the update page installs, on the update core
    /// itself, and a toast's <i>Install now</i> opens that page.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheToastsAreHandedWhatHoldsTheUpdateAndNothingThatInstalls()
    {
        await Assert.That(typeof(DeferredUpdateHolds).IsAssignableTo(typeof(IUpdateHolds))).IsFalse();
        await Assert.That(typeof(UpdateToasts).GetMethod(nameof(UpdateToasts.ForThisProcess))!.GetParameters()[0].ParameterType.IsAssignableTo(typeof(IUpdateHolds))).IsFalse();
    }

    private static string Lines(params string[] lines) => string.Join('\n', lines);

    /// <summary>One <see cref="UpdateToasts"/>, its recording surface, its holds and its clock.</summary>
    private sealed class Rig : IDisposable
    {
        public Rig(bool holds = true)
        {
            Holds = new ScriptedHolds
            {
                // A visible window fifty minutes from the clock's start.
                Snapshot = new UpdateHoldSnapshot(
                    Clock.GetUtcNow(),
                    UpdateHoldState.Held,
                    "1.2.0",
                    [],
                    [new HoldingSession(@"C:\work\window", null, Clock.GetUtcNow().AddMinutes(50))],
                    []),
            };

            Toasts = new UpdateToasts(Surface, holds ? Holds : null, Memory, Facts, Clock, TimeZoneInfo.Utc, Logs.CreateLogger("toasts"));
        }

        public static UpdateToastFacts Facts { get; } = new("1.1.0", @"%LocalAppData%\velopack\velopack_BrowserAI.app.log", @"C:\data\logs");

        public ManualClock Clock { get; } = new();

        public RecordingSurface Surface { get; } = new();

        public ScriptedHolds Holds { get; }

        public MemoryInProcess Memory { get; } = new();

        public UpdateToasts Toasts { get; }

        public CapturingLoggerProvider Logs { get; } = new();

        public void Dispose()
        {
            Toasts.Dispose();
            Logs.Dispose();
        }
    }

    /// <summary>Records what Windows would have been asked, one line per call.</summary>
    private sealed class RecordingSurface : IToastSurface
    {
        private readonly List<string> _lines = [];

        public List<ToastRequest> Shown { get; } = [];

        public Func<uint, ToastUpdateResult> Answer { get; set; } = _ => ToastUpdateResult.Succeeded;

        public bool ShowThrows { get; set; }

        public bool UpdateThrows { get; set; }

        public string Lines() => string.Join('\n', _lines);

        public void Clear() => _lines.Clear();

        public void Show(string tag, string group, ToastRequest toast, uint sequence)
        {
            if (ShowThrows)
            {
                throw new InvalidOperationException("Windows said no.");
            }

            Shown.Add(toast);
            _lines.Add($"show {tag} seq={sequence} banner={(toast.SuppressPopup ? "no" : "yes")} reboot={(toast.ExpiresOnReboot ? "yes" : "no")}"
                + (toast.Data is { } data ? $" status='{data[UpdateToastContent.StatusField]}' title='{data[UpdateToastContent.HoldersField]}'" : string.Empty));
        }

        public ToastUpdateResult Update(string tag, string group, IReadOnlyDictionary<string, string> values, uint sequence)
        {
            if (UpdateThrows)
            {
                throw new InvalidOperationException("Windows said no.");
            }

            _lines.Add($"update {tag} seq={sequence} status='{values[UpdateToastContent.StatusField]}' title='{values[UpdateToastContent.HoldersField]}'");
            return Answer(sequence);
        }

        public void Remove(string tag, string group) => _lines.Add($"remove {tag}");
    }

    /// <summary>What holds the update, as the arm sets it, counting the reads that name each conversation and the ones that do not.</summary>
    private sealed class ScriptedHolds : IUpdateHolds
    {
        private int _named;
        private int _countdown;

        public required UpdateHoldSnapshot Snapshot { get; set; }

        /// <summary>How many reads named every relay's conversation, a read of every client's records.</summary>
        public int NamedReads => Volatile.Read(ref _named);

        /// <summary>How many reads were a second of the countdown.</summary>
        public int CountdownReads => Volatile.Read(ref _countdown);

        public UpdateHoldSnapshot Read()
        {
            _ = Interlocked.Increment(ref _named);
            return Snapshot;
        }

        public UpdateHoldSnapshot ReadCountdown()
        {
            _ = Interlocked.Increment(ref _countdown);
            return Snapshot;
        }

        public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    /// <summary>The person's wait, kept in this process.</summary>
    private sealed class MemoryInProcess : IUpdateToastMemory
    {
        private string? _version;

        public string? WaitedFor() => _version;

        public void Waited(string version) => _version = version;

        public void Forget() => _version = null;
    }
}
