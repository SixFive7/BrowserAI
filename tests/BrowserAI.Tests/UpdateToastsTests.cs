// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Xml.Linq;
using BrowserAI.Background;
using BrowserAI.Proxy;
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

        await rig.Toasts.HeldAsync("1.2.0");

        await Assert.That(rig.Surface.Lines()).IsEqualTo(Lines(
            "remove installing",
            "remove installed",
            "remove failed",
            "show ready seq=1 banner=yes reboot=yes status='Installs in 50:00 if nothing uses it' title='1 window'"));
        await Assert.That(rig.Surface.Shown.Single().Xml).Contains("BrowserAI 1.2.0 is ready to install");
        await Assert.That(rig.Toasts.Counting).IsTrue();

        // Once per version: the background calling again changes nothing.
        await rig.Toasts.HeldAsync("1.2.0");
        rig.Surface.Clear();

        rig.Clock.Advance(UpdateToasts.Tick);
        rig.Clock.Advance(UpdateToasts.Tick);

        await Assert.That(rig.Surface.Lines()).IsEqualTo(Lines(
            "update ready seq=2 status='Installs in 49:59 if nothing uses it' title='1 window'",
            "update ready seq=3 status='Installs in 49:58 if nothing uses it' title='1 window'"));
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

        await rig.Toasts.HeldAsync("1.2.0");

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
        await rig.Toasts.HeldAsync("1.2.0");
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
    /// countdown; the installing toast takes the ready one's place with no banner.
    /// </summary>
    /// <remarks>
    /// <b>The installing toast's banner went on 2026-10-11 with 49 b</b> (previously
    /// <c>banner=yes</c> here): it follows a ready toast and the installed toast follows
    /// it within seconds, so the banner it put up was the second of two in that time.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task InstallingInstalledAndFailedRemoveTheOthersFirstAndStopTheCountdown()
    {
        using var rig = new Rig();

        await rig.Toasts.HeldAsync("1.2.0");
        rig.Surface.Clear();
        await rig.Toasts.InstallingAsync("1.2.0");

        await Assert.That(rig.Surface.Lines()).IsEqualTo(Lines("remove ready", "remove installed", "remove failed", "show installing seq=0 banner=no reboot=yes"));
        await Assert.That(rig.Surface.Shown[^1].Xml).Contains("Installing BrowserAI 1.2.0 now");
        await Assert.That(rig.Toasts.Counting).IsFalse();

        rig.Surface.Clear();
        rig.Clock.Advance(UpdateToasts.Tick);

        await Assert.That(rig.Surface.Lines()).IsEmpty();

        // The after-update start is another process with no countdown of its own.
        using var after = new Rig(holds: false);

        await after.Memory.WaitedAsync("1.2.0");
        await after.Toasts.InstalledAsync("1.2.0");

        await Assert.That(after.Surface.Lines()).IsEqualTo(Lines("remove ready", "remove installing", "remove failed", "show installed seq=0 banner=yes reboot=no"));
        await Assert.That(await after.Memory.WaitedForAsync()).IsNull();

        after.Surface.Clear();
        await after.Memory.WaitedAsync("1.2.0");
        await after.Toasts.FailedAsync("1.2.0");

        await Assert.That(after.Surface.Lines()).IsEqualTo(Lines("remove ready", "remove installing", "remove installed", "show failed seq=0 banner=yes reboot=no"));
        await Assert.That(after.Surface.Shown[^1].Xml).Contains("BrowserAI 1.1.0 is still installed.");
        await Assert.That(after.Surface.Shown[^1].Xml).Contains(@"%LocalAppData%\velopack, and BrowserAI&apos;s in C:\data\logs.");
        await Assert.That(await after.Memory.WaitedForAsync()).IsNull();

        // And a process that holds no update raises no ready toast.
        await after.Toasts.HeldAsync("1.3.0");

        await Assert.That(after.Surface.Shown.Count(toast => toast.Xml.Contains("ready to install", StringComparison.Ordinal))).IsEqualTo(0);
    }

    /// <summary>
    /// An update that installs at once raises no toast in the background, so the
    /// after-update start's installed toast is the only one the person sees; one that
    /// has to wait raises the ready toast, and when it installs the installing toast
    /// takes its place with no banner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>49 b, the maintainer's answer of 2026-10-11, verbatim: <i>"49 b"</i></b>. That
    /// morning the installed BrowserAI found an update nothing used and raised the ready
    /// and installing toasts 9 ms apart, and the after-update start raised the installed
    /// toast six seconds later: three banners for one update that never waited.
    /// </para>
    /// <para>
    /// <b>The background's own update core raises them here</b>, over
    /// <see cref="BackgroundUpdateRig"/>'s seams and clock, through the
    /// <see cref="UpdateToasts"/> the background builds and the indirection it hands
    /// them, onto a surface that records what Windows would have been asked. Planted
    /// red 2026-10-11 against the core as it stood, which raised the ready toast when a
    /// package was staged and the installing toast, with a banner, before every
    /// hand-over.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUpdateThatInstallsAtOnceRaisesNoToastBeforeTheInstalledOneAndOneThatWaitsRaisesTheReadyOne()
    {
        // Nothing holds it and its one relay agrees, or no relay is there to ask: it
        // installs at once, and the background shows nothing.
        foreach (var withRelay in new[] { true, false })
        {
            using var background = new BackgroundUpdateRig();
            using var shown = new CoreToasts(background);

            if (withRelay)
            {
                background.Relays.Connect(RelayIdleAt(background.Clock.GetUtcNow() - TimeSpan.FromMinutes(1)));
                background.Relays.Answer = _ => Agrees(background);
            }

            background.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");
            await shown.StartAsync();

            await Assert.That(background.IndexOf("apply 1.2.0 with [--after-update 1.2.0]")).IsGreaterThanOrEqualTo(0).Because(string.Join(" | ", background.Events));
            await Assert.That(shown.Surface.Lines()).IsEmpty().Because(withRelay
                ? "an update its one relay agreed to raised a toast before the installed one"
                : "an update with no relay to ask raised a toast before the installed one");
        }

        // A relay's countdown holds it: the ready toast goes up, with a banner.
        using var waits = new BackgroundUpdateRig();
        using var raised = new CoreToasts(waits);
        var idleAt = waits.Clock.GetUtcNow() + TimeSpan.FromMinutes(4);

        waits.Relays.Connect(RelayIdleAt(idleAt));
        waits.Relays.Answer = _ => Agrees(waits);
        waits.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");
        await raised.StartAsync();

        await Assert.That(waits.EventsStartingWith("apply")).IsEmpty().Because("the relay's countdown runs");
        await Assert.That(raised.Surface.Lines().Split('\n')[^1]).StartsWith("show ready seq=1 banner=yes reboot=yes");

        // Its countdown runs out and the relay agrees: the installing toast takes the
        // ready one's place, with no banner of its own.
        raised.Surface.Clear();
        waits.Clock.Advance(idleAt - waits.Clock.GetUtcNow());

        await Assert.That(waits.IndexOf("apply 1.2.0 with [--after-update 1.2.0]")).IsGreaterThanOrEqualTo(0).Because(string.Join(" | ", waits.Events));
        await Assert.That(string.Join('\n', raised.Surface.Lines().Split('\n')[^4..]))
            .IsEqualTo(Lines("remove ready", "remove installed", "remove failed", "show installing seq=0 banner=no reboot=yes"));
        await Assert.That(raised.Surface.Shown.Count(toast => !toast.SuppressPopup)).IsEqualTo(1).Because("the ready toast is the one banner the background puts up");
    }

    /// <summary>
    /// The installed toast says what the apps of the relays the update ended need, a
    /// Claude Code terminal its <c>/mcp</c> and a Codex conversation a new one, and carries no
    /// such line when none needs anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>52 b, the maintainer's answer of 2026-10-11, verbatim: <i>"52 b"</i></b>: with 49 b an
    /// update that installs at once raises no ready toast, which was the one toast that said
    /// which conversations would have to reconnect, so the background hands what each relay
    /// needs to the after-update start, and its installed toast says it in the ready toast's
    /// own words. Only the apps whose relays were connected are named, and Claude Code in VS
    /// Code, which starts BrowserAI again by itself, adds nothing.
    /// </para>
    /// <para>
    /// <b>Two processes, one memory</b>: the background's update core ends the relays and
    /// records through its toasts, and a second <see cref="UpdateToasts"/>, standing for the
    /// after-update start, raises the installed toast from the same memory. Planted red
    /// 2026-10-11 against a build that recorded nothing and raised the installed toast with
    /// its title alone.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheInstalledToastSaysWhatTheEndedRelaysAppsNeedAndCarriesNoLineWhenNoneNeedsOne()
    {
        const string Line = "After the update: run /mcp, BrowserAI, Reconnect in 1 Claude Code terminal; 1 Codex conversation needs a new one.";

        // A terminal, a Codex conversation and Claude Code in VS Code, every one quiet and
        // agreeing: the update installs at once.
        var (texts, remembered) = await InstalledAfterAsync(
            ("a", KnownClients.ClaudeCode, RelayReconnect.McpReconnect),
            ("b", KnownClients.Codex, RelayReconnect.Unknown),
            ("c", KnownClients.ClaudeCode, RelayReconnect.None));

        await Assert.That(Joined(texts)).IsEqualTo(Joined(["BrowserAI 1.2.0 is installed", Line]));
        await Assert.That(remembered).IsNull().Because("the after-update start left what the relays needed on disk for a later start");

        // Only Claude Code in VS Code was connected: nothing to reconnect, so no line.
        var (vsCode, _) = await InstalledAfterAsync(("c", KnownClients.ClaudeCode, RelayReconnect.None));

        await Assert.That(Joined(vsCode)).IsEqualTo(Joined(["BrowserAI 1.2.0 is installed"]));

        // And no relay at all: no line either.
        var (none, _) = await InstalledAfterAsync();

        await Assert.That(Joined(none)).IsEqualTo(Joined(["BrowserAI 1.2.0 is installed"]));
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

        await waited.Memory.WaitedAsync("1.2.0");
        await waited.Toasts.HeldAsync("1.2.0");

        await Assert.That(waited.Surface.Lines().Split('\n')[^1]).StartsWith("show ready seq=1 banner=no reboot=yes");

        using var other = new Rig();

        await other.Memory.WaitedAsync("1.1.9");
        await other.Toasts.HeldAsync("1.2.0");

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

        await rig.Toasts.HeldAsync("1.2.0");
        rig.Surface.Clear();
        rig.Holds.Snapshot = UpdateHoldSnapshot.Nothing(rig.Clock.GetUtcNow());
        rig.Clock.Advance(UpdateToasts.Tick);

        await Assert.That(rig.Surface.Lines()).IsEqualTo(Lines("remove ready"));
        await Assert.That(rig.Toasts.Counting).IsFalse();

        using var installing = new Rig();

        await installing.Toasts.HeldAsync("1.2.0");
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
        await rig.Toasts.HeldAsync("1.2.0");

        await Assert.That(rig.Toasts.Counting).IsFalse();
        await Assert.That(rig.Logs.Logged("Windows did not show the update toast 'ready'.")).IsTrue();

        using var updates = new Rig();

        await updates.Toasts.HeldAsync("1.2.0");
        updates.Surface.UpdateThrows = true;
        updates.Clock.Advance(UpdateToasts.Tick);
        updates.Surface.UpdateThrows = false;
        updates.Clock.Advance(UpdateToasts.Tick);

        await Assert.That(updates.Logs.Logged("A second of the ready toast's countdown failed.")).IsTrue();
        await Assert.That(updates.Toasts.Counting).IsTrue();
        await Assert.That(updates.Surface.Lines().Split('\n')[^1]).StartsWith("update ready seq=3 ");
    }

    /// <summary>The person's wait is one file in the data root, read back and forgotten.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ThePersonsWaitIsOneFileReadBackAndForgotten()
    {
        using var scratch = await ScratchDirectory.CreateAsync("toast-memory");

        var memory = UpdateToastMemoryFile.In(Path.Combine(scratch.Path, "data"));

        await Assert.That(await memory.WaitedForAsync()).IsNull();

        await memory.WaitedAsync("1.2.0");

        await Assert.That(await memory.WaitedForAsync()).IsEqualTo("1.2.0");
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(scratch.Path, "data", UpdateToastMemoryFile.FileName))).IsEqualTo("1.2.0");

        await memory.ForgetAsync();
        await memory.ForgetAsync();

        await Assert.That(await memory.WaitedForAsync()).IsNull();

        // 52 b: what the relays an update ended needed, one word each, for that version alone.
        await memory.EndedAsync("1.2.0", [RelayReconnect.McpReconnect, RelayReconnect.NewConversation, RelayReconnect.None, RelayReconnect.Unknown]);

        await Assert.That(await File.ReadAllTextAsync(Path.Combine(scratch.Path, "data", UpdateToastMemoryFile.EndedFileName))).IsEqualTo("1.2.0\nmcp,new-conversation,none,unknown");
        await Assert.That(Joined(await memory.EndedForAsync("1.2.0") ?? [])).IsEqualTo(Joined([RelayReconnect.McpReconnect, RelayReconnect.NewConversation, RelayReconnect.None, RelayReconnect.Unknown]));
        await Assert.That(await memory.EndedForAsync("1.3.0")).IsNull();

        await memory.ForgetAsync();

        await Assert.That(await memory.EndedForAsync("1.2.0")).IsNull();
        await Assert.That(File.Exists(Path.Combine(scratch.Path, "data", UpdateToastMemoryFile.EndedFileName))).IsFalse();
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

    private static string Joined<T>(IEnumerable<T> items) => string.Join(" | ", items);

    /// <summary>
    /// Has the background's update core install 1.2.0 at once with these relays connected,
    /// then raises the installed toast as the after-update start does, from the same memory.
    /// </summary>
    /// <param name="relays">Each relay's id, client and the reconnect it said it needs.</param>
    /// <returns>The installed toast's lines, and what the memory still holds for 1.2.0 after it.</returns>
    private static async Task<(List<string> Texts, IReadOnlyList<RelayReconnect>? Remembered)> InstalledAfterAsync(
        params (string Id, string Client, RelayReconnect Reconnect)[] relays)
    {
        var memory = new MemoryInProcess();

        using (var background = new BackgroundUpdateRig())
        using (var shown = new CoreToasts(background, memory))
        {
            foreach (var (id, client, reconnect) in relays)
            {
                background.Relays.Connect(new RelayState(id, client, "1.0.0", @"C:\project", background.Clock.GetUtcNow() - TimeSpan.FromMinutes(1), CallInFlight: false) { Reconnect = reconnect });
            }

            background.Relays.Answer = _ => Agrees(background);
            background.Client.Offer = BackgroundUpdateRig.Candidate("1.2.0");
            await shown.StartAsync();

            await Assert.That(background.IndexOf("apply 1.2.0 with [--after-update 1.2.0]")).IsGreaterThanOrEqualTo(0).Because(string.Join(" | ", background.Events));
        }

        using var after = new Rig(holds: false, memory: memory);

        await after.Toasts.InstalledAsync("1.2.0");

        var toast = XDocument.Parse(after.Surface.Shown.Single().Xml);

        return ([.. toast.Descendants("text").Select(text => text.Value)], await memory.EndedForAsync("1.2.0"));
    }

    /// <summary>A relay of the update core's rig with no call in flight, whose countdown runs out at a moment.</summary>
    /// <param name="idleAt">The moment.</param>
    /// <returns>The relay.</returns>
    private static RelayState RelayIdleAt(DateTimeOffset idleAt) =>
        new("a", KnownClients.ClaudeCode, "2.1.300", @"C:\project", idleAt, CallInFlight: false);

    /// <summary>A yes, read against the rig's clock at the moment the relay answers.</summary>
    /// <param name="rig">The rig.</param>
    /// <returns>The answer.</returns>
    private static RelayReadiness Agrees(BackgroundUpdateRig rig) =>
        new(true, rig.Clock.GetUtcNow() - TimeSpan.FromMinutes(1), CallInFlight: false);

    /// <summary>
    /// The product's toasts over a recording surface, raised by an update core over a
    /// <see cref="BackgroundUpdateRig"/> and reading its holds through
    /// <see cref="DeferredUpdateHolds"/>, as <c>Program.Background</c> wires the two.
    /// </summary>
    private sealed class CoreToasts : IDisposable
    {
        private readonly BackgroundUpdateRig _rig;
        private readonly DeferredUpdateHolds _holds = new();
        private readonly UpdateToasts _toasts;

        public CoreToasts(BackgroundUpdateRig rig, MemoryInProcess? memory = null)
        {
            _rig = rig;
            _toasts = new UpdateToasts(Surface, _holds, memory ?? new MemoryInProcess(), Rig.Facts, rig.Clock, TimeZoneInfo.Utc, Logs.CreateLogger("toasts"));
        }

        public RecordingSurface Surface { get; } = new();

        public CapturingLoggerProvider Logs { get; } = new();

        /// <summary>Builds the core with these toasts, hands the toasts its holds, and starts it.</summary>
        public async Task StartAsync()
        {
            var updates = _rig.Build(_toasts);

            _holds.Target = updates;
            await updates.StartAsync();
            _rig.Settle();
        }

        public void Dispose()
        {
            _toasts.Dispose();
            Logs.Dispose();
        }
    }

    /// <summary>One <see cref="UpdateToasts"/>, its recording surface, its holds and its clock.</summary>
    private sealed class Rig : IDisposable
    {
        public Rig(bool holds = true, MemoryInProcess? memory = null)
        {
            Memory = memory ?? new MemoryInProcess();

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

        public MemoryInProcess Memory { get; }

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

        public Task ShowAsync(string tag, string group, ToastRequest toast, uint sequence)
        {
            if (ShowThrows)
            {
                throw new InvalidOperationException("Windows said no.");
            }

            Shown.Add(toast);
            _lines.Add($"show {tag} seq={sequence} banner={(toast.SuppressPopup ? "no" : "yes")} reboot={(toast.ExpiresOnReboot ? "yes" : "no")}"
                + (toast.Data is { } data ? $" status='{data[UpdateToastContent.StatusField]}' title='{data[UpdateToastContent.HoldersField]}'" : string.Empty));
            return Task.CompletedTask;
        }

        public Task<ToastUpdateResult> UpdateAsync(string tag, string group, IReadOnlyDictionary<string, string> values, uint sequence)
        {
            if (UpdateThrows)
            {
                throw new InvalidOperationException("Windows said no.");
            }

            _lines.Add($"update {tag} seq={sequence} status='{values[UpdateToastContent.StatusField]}' title='{values[UpdateToastContent.HoldersField]}'");
            return Task.FromResult(Answer(sequence));
        }

        public Task RemoveAsync(string tag, string group)
        {
            _lines.Add($"remove {tag}");
            return Task.CompletedTask;
        }
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

        public Task<UpdateHoldSnapshot> ReadAsync()
        {
            _ = Interlocked.Increment(ref _named);
            return Task.FromResult(Snapshot);
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
        private (string Version, IReadOnlyList<RelayReconnect> Needs)? _ended;

        public Task<string?> WaitedForAsync() => Task.FromResult(_version);

        public Task WaitedAsync(string version)
        {
            _version = version;
            return Task.CompletedTask;
        }

        public Task EndedAsync(string version, IReadOnlyList<RelayReconnect> needs)
        {
            _ended = (version, [.. needs]);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RelayReconnect>?> EndedForAsync(string version) =>
            Task.FromResult(_ended is { } ended && string.Equals(ended.Version, version, StringComparison.OrdinalIgnoreCase) ? ended.Needs : null);

        public Task ForgetAsync()
        {
            _version = null;
            _ended = null;
            return Task.CompletedTask;
        }
    }
}
