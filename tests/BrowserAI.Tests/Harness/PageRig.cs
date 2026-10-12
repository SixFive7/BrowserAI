// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using BrowserAI.App;
using BrowserAI.App.Interop;
using BrowserAI.App.Page;
using BrowserAI.Registration;
using BrowserAI.Relay;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests.Harness;

/// <summary>A page, its stand-ins and its clock, with the listener it hands out.</summary>
/// <remarks>
/// <b>Made with <see cref="CreateAsync"/> and disposed with <see cref="DisposeAsync"/>
/// since 2026-10-11</b>, the maintainer's decisions 54 and 63: its scratch directory is
/// made with an asynchronous write, and the page's disposal is awaited. <i>Previously a
/// constructor and a <c>Dispose</c> that blocked on the page's disposal.</i>
/// </remarks>
internal sealed class PageRig : IAsyncDisposable
{
    private readonly ScratchDirectory _scratch;

    /// <summary>Makes the rig over a scratch directory of its own.</summary>
    /// <param name="logs">Where the page logs, or <see langword="null"/>.</param>
    /// <param name="wake">What the page's wake does, or <see langword="null"/>.</param>
    /// <param name="registration">The registration the page reads, or <see langword="null"/> for a fake.</param>
    /// <param name="occasion">Why the page is open.</param>
    /// <param name="holds">What holds an update, or <see langword="null"/> for nothing.</param>
    /// <param name="changelog">The changelog section, or <see langword="null"/>.</param>
    /// <param name="install">The broken-install notice, or <see langword="null"/>.</param>
    /// <param name="installed">Whether the facts name an install root.</param>
    /// <param name="facts">A change to the facts, or <see langword="null"/>.</param>
    /// <param name="memory">
    /// Where the listener keeps its port and its last tab across a restart (69 b), or
    /// <see langword="null"/> for what the process remembers.
    /// </param>
    /// <param name="releases">Every release the changelog holds, or <see langword="null"/> for none (56 a).</param>
    /// <returns>The rig.</returns>
    public static async Task<PageRig> CreateAsync(
        CapturingLoggerProvider? logs = null,
        Action? wake = null,
        IPageRegistration? registration = null,
        Occasion occasion = Occasion.Ordinary,
        IUpdateHolds? holds = null,
        ChangelogSection? changelog = null,
        BrokenInstallNotice? install = null,
        bool installed = true,
        Func<PageFacts, PageFacts>? facts = null,
        IPageListenerMemory? memory = null,
        IReadOnlyList<ChangelogSection>? releases = null)
    {
        var rig = new PageRig(await ScratchDirectory.CreateAsync("page-rig"), logs, wake, registration, occasion, holds, changelog, install, installed, facts, memory, releases);

        // The background's own start reads the after-update notice's record before any tab
        // (57 a), so the rig's does too.
        await rig.Page.PrepareAsync();
        return rig;
    }

    private PageRig(
        ScratchDirectory scratch,
        CapturingLoggerProvider? logs = null,
        Action? wake = null,
        IPageRegistration? registration = null,
        Occasion occasion = Occasion.Ordinary,
        IUpdateHolds? holds = null,
        ChangelogSection? changelog = null,
        BrokenInstallNotice? install = null,
        bool installed = true,
        Func<PageFacts, PageFacts>? facts = null,
        IPageListenerMemory? memory = null,
        IReadOnlyList<ChangelogSection>? releases = null)
    {
        _scratch = scratch;
        Registration = registration ?? new FakeRegistration();

        var made = new PageFacts
        {
            Version = "9.0.0",
            InstallRoot = installed ? Directory.CreateDirectory(Path.Combine(_scratch.Path, "install")).FullName : null,
            DataRoot = Directory.CreateDirectory(Path.Combine(_scratch.Path, "data")).FullName,
            LogDirectory = Path.Combine(_scratch.Path, "data", "logs"),
            ServerCommand = Path.Combine(_scratch.Path, "install", "current", "BrowserAI.exe"),
            ServerRefusal = null,
            StartedAt = DateTimeOffset.UnixEpoch,
            StartedHow = BackgroundStart.SignIn,
            RelayHold = RelayConstants.IdleCountdown,
        };

        Facts = facts is null ? made : facts(made);

        Page = new PageService(
            Facts,
            occasion,
            Sessions,
            Registration,
            Host,
            wake ?? (() => { }),
            Clock,
            PageTabs.ProductLinger,
            logs?.CreateLogger("page") ?? NullLogger.Instance)
        {
            // The product's page always has the update core behind it (Program.Background),
            // so an arm that sets no holds gets one where nothing waits.
            Holds = holds ?? new NothingHolds(),
            Folders = Folders,
            Usage = Usage,
            Changelog = changelog,
            Releases = releases ?? [],
            Install = install,
            Memory = memory ?? new PageListenerMemoryInProcess(),
        };
    }

    public PageFacts Facts { get; }

    public ManualClock Clock { get; } = new();

    public FakeSessions Sessions { get; } = new();

    /// <summary>The session folders and their sizes, as the arm sets them.</summary>
    public FakeFolders Folders { get; } = new();

    /// <summary>What BrowserAI's processes use, as the arm sets it.</summary>
    public FakeUsage Usage { get; } = new();

    public RecordingHost Host { get; } = new();

    public IPageRegistration Registration { get; }

    public PageService Page { get; }

    public PageGate Gate => Page.Gate ?? throw new InvalidOperationException("Nothing has been handed out yet.");

    public string Root => Gate.Root;

    public async Task<string> HandOutAsync(PageKind kind = PageKind.Status) =>
        await Page.HandOutAsync(kind) ?? throw new InvalidOperationException("The page handed out nothing.");

    /// <summary>Opens an address the way a browser does: the page, after the address that carries the token is traded for a cookie.</summary>
    /// <remarks>
    /// <b>Since 2026-10-12, 74 a</b>, a page opened by an address carrying the token is
    /// answered with the token in a cookie and the address without it; this follows that one
    /// answer with the cookie it set, as a browser does, and returns the page.
    /// <see cref="RawHttp"/> is what an arm sends when it is about that answer itself.
    /// </remarks>
    /// <param name="address">The address.</param>
    /// <returns>The page's answer.</returns>
    public static Task<RawHttpAnswer> GetAsync(string address) => RawHttp.OpenPageAsync(address);

    public Task<RawHttpAnswer> ActAsync(string body) =>
        RawHttp.SendAsync(Gate.Port, RawHttp.Post(
            $"/{Gate.Token}/action",
            body,
            $"Host: {Gate.Host}",
            $"Origin: {Gate.Origin}",
            "Sec-Fetch-Site: same-origin",
            "Content-Type: application/json"));

    public async Task<RawEventStream> StreamAsync(string address, int tab, string page = "status", string? extra = null)
    {
        var uri = new Uri(address);
        var stream = await RawEventStream.OpenAsync(uri.Port, Gate.Token, tab, page, extra);

        await Assert.That(stream.Status).IsEqualTo(200);
        return stream;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Page.DisposeAsync();
        _scratch.Dispose();
    }
}

/// <summary>
/// What holds an update where an arm names nothing: no update waits, and an
/// install-now is refused the way the update core refuses one with nothing waiting.
/// </summary>
internal sealed class NothingHolds : IUpdateHolds
{
    /// <summary>Where the updates come from and when BrowserAI last checked, as the arm sets them.</summary>
    public UpdateWatch Watching { get; set; } = new(UpdateChecks.Checking, UpdateSourceKind.GitHubReleases, null, null, DateTimeOffset.UnixEpoch);

    /// <summary>What a read of <see cref="Watch"/> throws, or <see langword="null"/> for <see cref="Watching"/>.</summary>
    public Exception? WatchFailure { get; set; }

    public Task<UpdateHoldSnapshot> ReadAsync() => Task.FromResult(UpdateHoldSnapshot.Nothing(DateTimeOffset.UnixEpoch));

    public UpdateHoldSnapshot ReadCountdown() => UpdateHoldSnapshot.Nothing(DateTimeOffset.UnixEpoch);

    public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("No update is downloaded and waiting, so there is nothing to install.");

    public UpdateWatch Watch() => WatchFailure is { } failure ? throw failure : Watching;
}

/// <summary>The running servers, scripted.</summary>
/// <remarks>
/// <b>The peek is the snapshot with every conversation's name taken out</b>, as the
/// background's own leaves the names unread, so an arm that renames a conversation and
/// changes nothing else sends the watch nothing new.
/// </remarks>
internal sealed class FakeSessions : IPageSessions
{
    public SessionsSnapshot Snapshot { get; set; } = SessionsSnapshot.Empty;

    /// <summary>How many named reads were asked for.</summary>
    public int Reads => Volatile.Read(ref _reads);

    private int _reads;

    public Task<SessionsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref _reads);
        return Task.FromResult(Snapshot);
    }

    public Task<SessionsSnapshot> PeekAsync() =>
        Task.FromResult(Snapshot with { Servers = [.. Snapshot.Servers.Select(static server => server with { Conversation = null })] });
}

/// <summary>The session folders, scripted, and how many measurements were asked for.</summary>
internal sealed class FakeFolders : IPageFolders
{
    /// <summary>The folders a measurement finds.</summary>
    public IReadOnlyList<SessionFolder> Folders { get; set; } = [];

    /// <summary>When a measurement says it was taken.</summary>
    public DateTimeOffset At { get; set; } = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// A measurement nobody has let finish yet: while it is set, every measurement waits
    /// on it.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>How many measurements were asked for.</summary>
    public int Measured => Volatile.Read(ref _measured);

    private int _measured;

    public async Task<FolderMeasure> MeasureAsync()
    {
        _ = Interlocked.Increment(ref _measured);

        if (Hold is { } hold)
        {
            await hold.Task;
        }

        return new FolderMeasure(At, [.. Folders.OrderByDescending(static folder => folder.Bytes ?? -1)]);
    }
}

/// <summary>What BrowserAI's processes use, scripted, and how many readings were asked for.</summary>
/// <remarks>
/// <b>Added 2026-10-12 for 59 b</b>: a reading answers <see cref="Reading"/> as the arm last set
/// it, or throws <see cref="Failure"/> where one is set.
/// </remarks>
internal sealed class FakeUsage : IPageUsage
{
    /// <summary>What a reading answers.</summary>
    public UsageReading Reading { get; set; } = new(new ProcessCounters(0, 0, 0), [], [], 4);

    /// <summary>What a reading throws, or <see langword="null"/> for <see cref="Reading"/>.</summary>
    public Exception? Failure { get; set; }

    /// <summary>How many readings were asked for.</summary>
    public int Reads => Volatile.Read(ref _reads);

    private int _reads;

    public Task<UsageReading> ReadAsync()
    {
        _ = Interlocked.Increment(ref _reads);
        return Failure is { } failure ? Task.FromException<UsageReading>(failure) : Task.FromResult(Reading);
    }
}

/// <summary>A desktop that records what it was asked to open and opens nothing.</summary>
internal sealed class RecordingHost : IPageHost
{
    public ConcurrentQueue<string> Opened { get; } = new();

    /// <summary>Every prompt the picker was asked with, in order.</summary>
    public ConcurrentQueue<string> Prompts { get; } = new();

    /// <summary>What the picker answers.</summary>
    public FolderPick Pick { get; set; } = FolderPick.Cancelled;

    /// <summary>
    /// A picker nobody has answered yet: while it is set, every picker waits on it
    /// and <see cref="Pick"/> is not read.
    /// </summary>
    public TaskCompletionSource<FolderPick>? Open { get; set; }

    public void OpenFolder(string directory) => Opened.Enqueue(directory);

    public Task<FolderPick> PickFolderAsync(string prompt)
    {
        Prompts.Enqueue(prompt);
        return Open is { } open ? open.Task : Task.FromResult(Pick);
    }

}

/// <summary>Every client's registration, scripted, and every action recorded.</summary>
internal sealed class FakeRegistration : IPageRegistration
{
    /// <summary>What a read answers; <see langword="null"/> makes the read throw.</summary>
    public AppState? State { get; set; }

    public int Reads => Volatile.Read(ref _reads);

    private int _reads;

    /// <summary>Every action, as its verb, the client's key and the folder when there was one.</summary>
    public ConcurrentQueue<(string Verb, string Client, string? Folder)> Actions { get; } = new();

    /// <summary>What every action concludes.</summary>
    public Func<RegistrationClient, RegistrationReport> Report { get; set; } =
        who => new RegistrationReport(RegistrationStatus.Registered, $"Registered with {who.DisplayName}.", null, null);

    public Task<AppState> ReadAsync(CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref _reads);
        return State is { } state ? Task.FromResult(state) : Task.FromException<AppState>(new InvalidOperationException("The registration could not be read."));
    }

    public Task<RegistrationReport> RegisterAsync(RegistrationClient who, CancellationToken cancellationToken) => Act("register", who, null);

    public Task<RegistrationReport> UnregisterAsync(RegistrationClient who, CancellationToken cancellationToken) => Act("unregister", who, null);

    public Task<RegistrationReport> RegisterInProjectAsync(RegistrationClient who, string folder, string command, CancellationToken cancellationToken) =>
        Act("register-in-project", who, folder);

    public Task<RegistrationReport> UnregisterFromProjectAsync(RegistrationClient who, string folder, CancellationToken cancellationToken) =>
        Act("unregister-from-project", who, folder);

    private Task<RegistrationReport> Act(string verb, RegistrationClient who, string? folder)
    {
        Actions.Enqueue((verb, who.Key, folder));
        return Task.FromResult(Report(who));
    }
}
