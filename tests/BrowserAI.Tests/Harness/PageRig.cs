// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using BrowserAI.App;
using BrowserAI.App.Page;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests.Harness;

/// <summary>A page, its stand-ins and its clock, with the listener it hands out.</summary>
internal sealed class PageRig : IDisposable
{
    private readonly ScratchDirectory _scratch = ScratchDirectory.Create("page-rig");

    public PageRig(CapturingLoggerProvider? logs = null, Action? wake = null)
    {
        Facts = new PageFacts
        {
            Version = "9.0.0",
            InstallRoot = Directory.CreateDirectory(Path.Combine(_scratch.Path, "install")).FullName,
            DataRoot = Directory.CreateDirectory(Path.Combine(_scratch.Path, "data")).FullName,
            LogDirectory = Path.Combine(_scratch.Path, "data", "logs"),
            ServerCommand = Path.Combine(_scratch.Path, "install", "current", "BrowserAI.Server.exe"),
            ServerRefusal = null,
        };

        Page = new PageService(
            Facts,
            Occasion.Ordinary,
            Updates,
            Sessions,
            Host,
            wake ?? (() => { }),
            Clock,
            PageTabs.ProductLinger,
            logs?.CreateLogger("page") ?? NullLogger.Instance);
    }

    public PageFacts Facts { get; }

    public ManualClock Clock { get; } = new();

    public FakeUpdates Updates { get; } = new();

    public FakeSessions Sessions { get; } = new();

    public RecordingHost Host { get; } = new();

    public PageService Page { get; }

    public PageGate Gate => Page.Gate ?? throw new InvalidOperationException("Nothing has been handed out yet.");

    public string Root => Gate.Root;

    public string HandOut(PageKind kind = PageKind.Status) =>
        Page.HandOut(kind) ?? throw new InvalidOperationException("The page handed out nothing.");

    public static Task<RawHttpAnswer> GetAsync(string address)
    {
        var uri = new Uri(address);

        return RawHttp.SendAsync(uri.Port, RawHttp.Get(uri.Port, uri.PathAndQuery, $"Host: 127.0.0.1:{uri.Port}", "Sec-Fetch-Site: none"));
    }

    public Task<RawHttpAnswer> ActAsync(string body) =>
        RawHttp.SendAsync(Gate.Port, RawHttp.Post(
            $"/{Gate.Token}/action",
            body,
            $"Host: {Gate.Host}",
            $"Origin: {Gate.Origin}",
            "Sec-Fetch-Site: same-origin",
            "Content-Type: application/json"));

    public async Task<RawEventStream> StreamAsync(string address, int tab, string page = "status")
    {
        var uri = new Uri(address);
        var stream = await RawEventStream.OpenAsync(uri.Port, Gate.Token, tab, page);

        await Assert.That(stream.Status).IsEqualTo(200);
        return stream;
    }

    public void Dispose()
    {
        Page.Dispose();
        _scratch.Dispose();
    }
}

/// <summary>The update machinery, scripted.</summary>
internal sealed class FakeUpdates : IPageUpdates
{
    public UpdateStage? Unavailable { get; set; }

    public string? Missing { get; set; }

    public Func<CancellationToken, Task<UpdateCandidate?>> Check { get; set; } = _ => Task.FromResult<UpdateCandidate?>(null);

    public UpdateCandidate? StagedCandidate { get; set; }

    public ConcurrentQueue<UpdateCandidate> Installed { get; } = new();

    public string? MissingReleaseList() => Missing;

    public int Checks => Volatile.Read(ref _checks);

    private int _checks;

    public Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref _checks);
        return Check(cancellationToken);
    }

    public UpdateCandidate? Staged() => StagedCandidate;

    public Task InstallAsync(UpdateCandidate candidate, CancellationToken cancellationToken)
    {
        Installed.Enqueue(candidate);
        return Task.CompletedTask;
    }
}

/// <summary>The running servers, scripted.</summary>
internal sealed class FakeSessions : IPageSessions
{
    public SessionsSnapshot Snapshot { get; set; } = SessionsSnapshot.Empty;

    public ConcurrentQueue<string> Closed { get; } = new();

    public Task<SessionsSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);

    public Task<string?> CloseAsync(ServerEntry server, CancellationToken cancellationToken)
    {
        Closed.Enqueue(server.Id);
        return Task.FromResult<string?>(null);
    }
}

/// <summary>A desktop that records what it was asked to open and opens nothing.</summary>
internal sealed class RecordingHost : IPageHost
{
    public ConcurrentQueue<string> Opened { get; } = new();

    public int RegistrationWindows => Volatile.Read(ref _registrationWindows);

    private int _registrationWindows;

    public void OpenFolder(string directory) => Opened.Enqueue(directory);

    public string? OpenTrace(string trace) => null;

    public void ShowRegistrationWindow() => Interlocked.Increment(ref _registrationWindows);

    public void RunQueuedWork()
    {
    }
}
