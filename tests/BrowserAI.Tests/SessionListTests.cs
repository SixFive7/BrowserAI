// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Security.AccessControl;
using System.Text.Json.Nodes;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// What <c>browserai_list</c> says about whether each session it reports is
/// being driven right now.
/// </summary>
/// <remarks>
/// <para>
/// <b>Until 2026-08-20 it said nothing at all.</b> The listing carried mode,
/// browser, purpose, dates and size and performed no liveness check, so a caller
/// could not tell an abandoned session from one another agent was inside — which
/// is the distinction that matters most in the turn before
/// <c>browserai_destroy</c>.
/// </para>
/// <para>
/// <b>Four arms, because the answer has four shapes and three of them are
/// reachable only by construction.</b> A session this process drives; one a peer
/// holds; one nobody holds; and one whose <c>lock.json</c> cannot be opened at
/// all. The fourth is the one this test exists for: <i>could not tell</i> must
/// not be printed as <i>free</i>, because a caller about to destroy a session
/// acts on the difference.
/// </para>
/// <para>
/// <b>Every arm carries its own positive control.</b> The peer's handle is
/// released and the ACL is lifted, and the same two sessions are then required
/// to report <i>not in use</i> — so an implementation that answered "in use" for
/// everything, or "unknown" for everything, fails the second listing even though
/// it would pass the first.
/// </para>
/// <para>
/// <b>In-process against the rig rather than the published binary.</b> The
/// mechanism under test is the kernel's file-sharing rule, which is enforced
/// against handles rather than against processes — the same argument
/// <c>UpdateTests</c> makes about the live-marker set — and the rig is what
/// allows a peer's handle and an ACL to be planted around a single call.
/// </para>
/// </remarks>
internal sealed class SessionListTests
{
    [Test]
    public async Task ListSaysWhichSessionsAreInUseAndNeverPrintsCouldNotTellAsFree()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var driven = Path.Combine(sessions.Root, "driven-by-this-browserai");
        var peerHeld = Path.Combine(sessions.Root, "held-by-a-peer");
        var free = Path.Combine(sessions.Root, "left-behind-by-a-dead-process");
        var unreadable = Path.Combine(sessions.Root, "lock-file-cannot-be-opened");

        // The first arm is the product's own front door: init leaves lock.json
        // held for the session's life, so the listing must answer for it without
        // asking the kernel anything.
        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = driven,
            ["purpose"] = "a session this BrowserAI is driving",
            ["mode"] = "headless",
        });

        // The other three are made the way a previous process would have made
        // them: taken, recorded in the index, and released. What is left on disk
        // is a real lock.json that nobody holds.
        var index = new SessionIndex(sessions.Environment.Paths, NullLogger.Instance);

        Plant(index, peerHeld, "a session another BrowserAI is driving");
        Plant(index, free, "a session whose process exited");
        Plant(index, unreadable, "a session whose lock file cannot be examined");

        // The peer's handle, byte for byte what SessionLock.OpenHeld takes.
        var peer = new FileStream(
            SessionPath.Resolve(peerHeld).LockFile,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1);

        // WriteData and not ReadData: the probe asks for ReadWrite, so denying
        // the write half refuses it while leaving the record readable -- which
        // is what keeps this entry IN the listing with an unanswered liveness
        // question rather than dropping it for want of a record.
        var denied = DirectoryDenial.Apply(
            unreadable,
            FileSystemRights.WriteData,
            InheritanceFlags.ObjectInherit,
            PropagationFlags.InheritOnly);

        string text;

        try
        {
            text = TextOf(await CallAsync(rig, SessionToolSurface.List, new JsonObject
            {
                ["directory"] = sessions.Root,
            }));
        }
        finally
        {
            await peer.DisposeAsync();
            denied.Dispose();
        }

        await Assert.That(BlockFor(text, driven)).Contains("in use: YES — this BrowserAI process is driving it right now.");

        var peerBlock = BlockFor(text, peerHeld);

        await Assert.That(peerBlock).Contains("in use: YES — something holds ");
        await Assert.That(peerBlock).Contains(SessionPath.Resolve(peerHeld).LockFile);

        // ⚠️ THE TRAP, ASSERTED AS AN ABSENCE. A sharing violation says the file
        // is held and never by whom -- the record inside can name a previous
        // holder -- and turning it into "held by PID n" would publish, on every
        // listing, the wrong SENTENCE the ownership work recorded on 2026-08-19.
        // This process's own pid is the strongest available probe for that,
        // because it is the one number a wrong implementation would most easily
        // print here.
        await Assert.That(peerBlock).DoesNotContain("PID");
        await Assert.That(peerBlock).DoesNotContain(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await Assert.That(BlockFor(text, free)).Contains("in use: no — nothing held ");

        var unknownBlock = BlockFor(text, unreadable);

        await Assert.That(unknownBlock).Contains("in use: UNKNOWN");
        await Assert.That(unknownBlock).Contains("this is not the same answer as 'no'");
        await Assert.That(unknownBlock).DoesNotContain("in use: no");

        // The positive controls, both of them, in one second listing: the handle
        // is gone and the ACL is off, so the two sessions that were YES and
        // UNKNOWN must both now be no. An implementation that answered the same
        // thing for every entry passes above and fails here.
        var after = TextOf(await CallAsync(rig, SessionToolSurface.List, new JsonObject
        {
            ["directory"] = sessions.Root,
        }));

        await Assert.That(BlockFor(after, peerHeld)).Contains("in use: no — nothing held ");
        await Assert.That(BlockFor(after, unreadable)).Contains("in use: no — nothing held ");
        await Assert.That(BlockFor(after, driven)).Contains("in use: YES — this BrowserAI process is driving it right now.");
    }

    /// <summary>
    /// The probe underneath the listing answers three-valued, and only a sharing
    /// violation is ever read as <b>held</b>.
    /// </summary>
    /// <remarks>
    /// <b>Asserted on <see cref="SessionLock.ProbeLiveness"/> directly as well as
    /// through the tool</b>, because the tool can only show three of the arms and
    /// the fourth — an absent <c>lock.json</c> — is the one the house rule is
    /// most specific about. An absence is a record being replaced as often as it
    /// is a session that has gone, so it is <c>Undetermined</c> and never
    /// <c>NotHeld</c>.
    /// </remarks>
    [Test]
    public async Task TheProbeReadsOnlyASharingViolationAsHeldAndNeverAnAbsenceAsFree()
    {
        using var scratch = ScratchDirectory.Create("list-probe");

        var directory = Path.Combine(scratch.Path, "probed");
        var location = SessionPath.Resolve(directory);

        SessionLayout.Create(location);

        // Nothing there at all: the arm the tool cannot reach, because an entry
        // with no readable record is not listed.
        var absent = SessionLock.ProbeLiveness(location);

        await Assert.That(absent.State).IsEqualTo(SessionLiveness.Undetermined);
        await Assert.That(absent.Why!).Contains(location.LockFile);

        var taken = SessionLock.TryAcquire(
            location,
            new SessionLockRequest { Mode = "headless", Browser = "chromium", Purpose = "probed for liveness" },
            NullLogger.Instance);

        await Assert.That(taken.Taken).IsTrue();

        // Held by this process's own lock, which is the same handle a peer's
        // would be as far as the kernel is concerned.
        await Assert.That(SessionLock.ProbeLiveness(location).State).IsEqualTo(SessionLiveness.Held);

        taken.Acquired!.Dispose();

        var released = SessionLock.ProbeLiveness(location);

        await Assert.That(released.State).IsEqualTo(SessionLiveness.NotHeld);
        await Assert.That(released.Why).IsNull();

        // And a probe cannot be defeated by another probe: two of these overlap
        // without either reading the other as an owner, which is what keeps a
        // listing from reporting a peer's own look as a holder.
        using var first = SessionLock.ProbeLiveness(location) is { State: SessionLiveness.NotHeld }
            ? new FileStream(location.LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1)
            : throw new InvalidOperationException("the directory was expected to be free at this point");

        await Assert.That(SessionLock.ProbeLiveness(location).State).IsEqualTo(SessionLiveness.NotHeld);
    }

    /// <summary>
    /// Creates a session directory the way a process that has since exited would
    /// have left it: a real record on disk, an index entry, and no handle.
    /// </summary>
    /// <param name="index">The rig's own index.</param>
    /// <param name="directory">Where the session goes.</param>
    /// <param name="purpose">What the record says it was for.</param>
    private static void Plant(SessionIndex index, string directory, string purpose)
    {
        var location = SessionPath.Resolve(directory);

        SessionLayout.Create(location);

        var taken = SessionLock.TryAcquire(
            location,
            new SessionLockRequest { Mode = "headless", Browser = "chromium", Purpose = purpose },
            NullLogger.Instance);

        index.Record(location);
        taken.Acquired!.Dispose();
    }

    /// <summary>
    /// The one entry's worth of the listing that begins with a directory.
    /// </summary>
    /// <remarks>
    /// <b><c>Single</c> rather than a substring search</b>, so an assertion can
    /// never accidentally be satisfied by a neighbouring session's line — which
    /// is exactly the failure a test with four almost-identical entries invites.
    /// </remarks>
    /// <param name="text">The whole listing.</param>
    /// <param name="directory">The session directory.</param>
    /// <returns>That session's block.</returns>
    private static string BlockFor(string text, string directory) =>
        text.Split("\n\n", StringSplitOptions.None)
            .Single(block => block.StartsWith(directory + "\n", StringComparison.Ordinal));

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });

    private static string TextOf(JsonObject result) =>
        string.Concat((result["content"]?.AsArray() ?? [])
            .Select(block => (string?)block?["text"] ?? string.Empty));
}
