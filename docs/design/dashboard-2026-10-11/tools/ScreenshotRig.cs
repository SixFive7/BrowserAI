// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.App.Page;
using BrowserAI.Coordination;
using BrowserAI.Proxy;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>The rig the build's screenshots were served from: the product's page with invented data, until a stop file appears.</summary>
/// <remarks>
/// Kept beside the screenshots as a record of method, and not in the suite: it ran once from
/// <c>tests/BrowserAI.Tests</c> on 2026-10-11, filtered to itself, and was taken out again.
/// </remarks>
internal sealed class ScreenshotRig
{
    [Test]
    public async Task ServeThePageForScreenshots()
    {
        var addresses = Path.Combine(RepositoryLayout.Root.FullName, ".work", "shots-addresses.txt");
        var stop = Path.Combine(RepositoryLayout.Root.FullName, ".work", "shots-stop.txt");
        var real = DateTimeOffset.UtcNow;

        var holds = new Holds();
        using var rig = new PageRig(
            registration: new FakeRegistration { State = DashboardTests.Apps() },
            holds: holds,
            changelog: ChangelogPageContent.SectionFor(ShippedChangelog.Text()!, "1.1.0"),
            facts: facts => facts with
            {
                Version = "1.1.0",
                StartedAt = real.AddHours(-3).AddMinutes(-12),
                StartedHow = BackgroundStart.SignIn,
                InstallRoot = @"C:\Users\sam\AppData\Local\BrowserAI.app",
                DataRoot = @"C:\Users\sam\AppData\Local\BrowserAI",
                LogDirectory = @"C:\Users\sam\AppData\Local\BrowserAI\logs",
                ServerCommand = @"C:\Users\sam\AppData\Local\BrowserAI.app\current\BrowserAI.exe",
            });

        rig.Clock.Advance(real - DateTimeOffset.UnixEpoch);

        var now = rig.Clock.GetUtcNow();

        holds.Snapshot = Held(now);
        holds.Watching = new UpdateWatch(UpdateChecks.Checking, UpdateSourceKind.GitHubReleases, null, null, now.AddMinutes(-47));
        rig.Sessions.Snapshot = Busy(now);

        const long MiB = 1024 * 1024;

        rig.Folders.At = now;
        rig.Folders.Folders =
        [
            new SessionFolder(CensusPageSessions.IdOf(Checkout), Checkout, 96 * MiB),
            new SessionFolder(CensusPageSessions.IdOf(Pricing), Pricing, 188 * MiB),
            new SessionFolder(CensusPageSessions.IdOf(Login), Login, 61 * MiB),
            new SessionFolder(CensusPageSessions.IdOf(@"C:\Code\billing\.work\invoice-export"), @"C:\Code\billing\.work\invoice-export", 214 * MiB),
            new SessionFolder(CensusPageSessions.IdOf(@"C:\Code\acme-shop\.work\search-slow-network"), @"C:\Code\acme-shop\.work\search-slow-network", 377 * MiB),
            new SessionFolder(CensusPageSessions.IdOf(@"C:\Code\docs-site\.work\screenshots-v2"), @"C:\Code\docs-site\.work\screenshots-v2", 12 * MiB),
        ];

        var lines = new List<string>
        {
            "home " + rig.HandOut(PageKind.Status),
            "sessions " + rig.HandOut(PageKind.Sessions),
            "update " + rig.HandOut(PageKind.Update),
            "changelog " + rig.HandOut(PageKind.Changelog),
            "setup " + rig.HandOut(PageKind.Setup),
            "storage " + rig.HandOut(PageKind.Storage),
            "install " + rig.HandOut(PageKind.Install),
        };

        var session = rig.HandOut(PageKind.Session);

        lines.Add("session " + session.Replace("?tab=", "?id=" + CensusPageSessions.IdOf(Checkout) + "&tab=", StringComparison.Ordinal));
        await File.WriteAllLinesAsync(addresses, lines);

        var until = DateTime.UtcNow + TimeSpan.FromMinutes(40);

        while (!File.Exists(stop) && DateTime.UtcNow < until)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        await Assert.That(lines.Count).IsEqualTo(8);
    }

    private const string Checkout = @"C:\Code\acme-shop\.work\checkout-rounding";
    private const string Pricing = @"C:\Code\market-notes\.work\pricing-compare";
    private const string Login = @"C:\Code\acme-shop\.work\login-flow-check";

    private static SessionsSnapshot Busy(DateTimeOffset now)
    {
        var composed = CensusPageSessions.Compose(
            now,
            [
                ("background", DashboardTests.Description(900, ServerDescription.Roles.Host,
                [
                    new HeldSession(Checkout, "reproducing the rounding error in the checkout total", BrowserOpen: true, DrivenBy: "claude-code", DrivenThrough: 301, IdleCloseAt: now.AddMinutes(7).AddSeconds(42)),
                    new HeldSession(Pricing, "comparing the three pricing pages side by side", BrowserOpen: true, Headed: true, DrivenBy: "claude-code", DrivenThrough: 302, IdleCloseAt: now.AddMinutes(48).AddSeconds(5)),
                    new HeldSession(Login, "checking that the sign-in survives a reload", BrowserOpen: true, Kept: true, IdleCloseAt: now.AddMinutes(17).AddSeconds(20)),
                ])),
                ("relay:a", DashboardTests.Description(301, ServerDescription.Roles.Relay, [], "claude-code", @"C:\Code\acme-shop", inFlight: 1)),
                ("relay:b", DashboardTests.Description(302, ServerDescription.Roles.Relay, [], "claude-code", @"C:\Code\market-notes")),
                ("relay:c", DashboardTests.Description(303, ServerDescription.Roles.Relay, [], "codex-mcp-client", @"C:\Code\docs-site")),
            ],
            []);

        var window = new ClientWindow("1200-134360600000000000", @"C:\Code\acme-shop");

        return composed with
        {
            Servers =
            [
                .. composed.Servers.Select(server => server.Description.ProcessId switch
                {
                    301 => server with { Conversation = new ConversationName("Fix the checkout total rounding", IsTitle: true), Window = window, Reconnect = RelayReconnect.None },
                    302 => server with { Conversation = new ConversationName("Compare pricing pages", IsTitle: true), Reconnect = RelayReconnect.McpReconnect },
                    303 => server with { Conversation = new ConversationName("Update the install guide screenshots", IsTitle: true), Reconnect = RelayReconnect.NewConversation },
                    _ => server,
                }),
            ],
        };
    }

    private static UpdateHoldSnapshot Held(DateTimeOffset now) => new(
        now,
        UpdateHoldState.Held,
        "1.2.0",
        [
            new HoldingSession(Checkout, "reproducing the rounding error in the checkout total", now.AddMinutes(7).AddSeconds(42)),
            new HoldingSession(Login, "checking that the sign-in survives a reload", now.AddMinutes(17).AddSeconds(20)),
        ],
        [new HoldingSession(Pricing, "comparing the three pricing pages side by side", now.AddMinutes(48).AddSeconds(5))],
        [
            new HoldingRelay("claude-code 2.1.300", @"C:\Code\acme-shop", now.AddMinutes(9), CallInFlight: true, RelayReconnect.None, "s1", new ConversationName("Fix the checkout total rounding", IsTitle: true), new ClientWindow("1200-134360600000000000", @"C:\Code\acme-shop"))
            {
                ClientName = KnownClients.ClaudeCode,
                ClientVersion = "2.1.300",
            },
            new HoldingRelay("claude-code 2.1.300", @"C:\Code\market-notes", now.AddMinutes(6).AddSeconds(30), CallInFlight: false, RelayReconnect.McpReconnect, "s2", new ConversationName("Compare pricing pages", IsTitle: true))
            {
                ClientName = KnownClients.ClaudeCode,
                ClientVersion = "2.1.300",
            },
            new HoldingRelay("codex-mcp-client 0.165.0", @"C:\Code\docs-site", now.AddMinutes(-2), CallInFlight: false, RelayReconnect.NewConversation, "s3", new ConversationName("Update the install guide screenshots", IsTitle: true))
            {
                ClientName = KnownClients.Codex,
                ClientVersion = "0.165.0",
            },
        ]);

    private sealed class Holds : IUpdateHolds
    {
        public UpdateHoldSnapshot Snapshot { get; set; } = UpdateHoldSnapshot.Nothing(DateTimeOffset.UnixEpoch);

        public UpdateWatch Watching { get; set; } = UpdateWatch.Unreported;

        public UpdateHoldSnapshot Read() => Snapshot;

        public Task<string?> InstallNowAsync(string version, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("This is a picture of the page, so nothing was installed.");

        public UpdateWatch Watch() => Watching;
    }
}
