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
/// holds; one nobody holds; and one whose <c>browserai.json</c> cannot be opened at
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
    private static readonly string ProbePath = Path.Combine(AppContext.BaseDirectory, "BrowserAI.TestProbe.exe");

    [Test]
    public async Task ListSaysWhichSessionsAreInUseAndNeverPrintsCouldNotTellAsFree()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var driven = Path.Combine(sessions.Root, "driven-by-this-browserai");
        var peerHeld = Path.Combine(sessions.Root, "held-by-a-peer");
        var free = Path.Combine(sessions.Root, "left-behind-by-a-dead-process");
        var unreadable = Path.Combine(sessions.Root, "lock-file-cannot-be-opened");

        // The first arm is the product's own front door: init leaves browserai.json
        // held for the session's life, so the listing must answer for it without
        // asking the kernel anything.
        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = driven,
            ["purpose"] = "a session this BrowserAI is driving",
        });

        // The other three are made the way a previous process would have made
        // them: taken, recorded in the index, and released. What is left on disk
        // is a real browserai.json that nobody holds.
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

        // ⚠️ Q100e, on the tool a caller reads before deciding what to keep.
        // Nothing here is ever deleted on a schedule or at a size, so the number
        // and the sentence beside it are the whole of what retention is: a
        // decision somebody takes rather than one the server takes quietly.
        await Assert.That(BlockFor(text, driven)).Contains("output:");
        await Assert.That(BlockFor(text, driven)).Contains("BrowserAI never deletes any of it");
        await Assert.That(BlockFor(text, driven)).Contains(SessionToolSurface.Destroy);

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
    /// The probe underneath the listing answers three-valued, only a sharing
    /// violation is ever read as <b>held</b>, and an <b>absent</b> guard is now
    /// genuinely free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted on <see cref="SessionLock.ProbeLiveness"/> directly as well as
    /// through the tool</b>, because the tool can only show three of the arms
    /// and the fourth — no guard at all — is the one that changed.
    /// </para>
    /// <para>
    /// ⚠️ <b>THE ABSENT ARM INVERTED (2026-08-26, previously
    /// <c>Undetermined</c>, "an absence is a record being replaced as often as
    /// it is a session that has gone").</b> That sentence was true of
    /// <c>browserai.json</c>, which was durably rewritten and renamed on every
    /// forwarded call, so its name was unbound for milliseconds at a time and an
    /// absence could not be told from a rewrite. <c>browserai.lock</c> is
    /// written once at acquisition and never again, so an absence is an absence
    /// — and reading it as <i>undetermined</i> would now be the hedge rather
    /// than the honest answer.
    /// </para>
    /// <para>
    /// <b>What is still never read as free is a denial</b>, which the tool-level
    /// test above covers with a real ACL: a guard that could not be opened is
    /// <c>Undetermined</c> with a reason, and printing it as <i>no</i> is the
    /// one direction that costs a caller a session it was about to destroy.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheProbeReadsOnlyASharingViolationAsHeldAndAnAbsentGuardIsFree()
    {
        using var scratch = ScratchDirectory.Create("list-probe");

        var directory = Path.Combine(scratch.Path, "probed");
        var location = SessionPath.Resolve(directory);

        SessionLayout.Create(location);

        // ⚠️ No guard at all, which now means what it says.
        var absent = SessionLock.ProbeLiveness(location);

        await Assert.That(absent.State).IsEqualTo(SessionLiveness.NotHeld);
        await Assert.That(absent.Why).IsNull();

        var taken = SessionLock.TryAcquire(
            location,
            new SessionLockRequest { Browser = "chromium", Purpose = "probed for liveness" },
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
    /// A peer inside create-or-take is the one window the listing can still
    /// misreport, and what it reports there is a momentary truth rather than a
    /// stale one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Corrected 2026-08-26 (previously
    /// <c>ASessionWhoseGateIsHeldByAPeerIsReportedUnknownRatherThanFree</c>,
    /// which required <c>UNKNOWN</c>).</b> That test's premise was the rewrite
    /// window: every forwarded browser call replaced <c>browserai.json</c>
    /// whole, dropping the ownership handle at the top and taking it back at the
    /// bottom with the gate held throughout — so a bare probe could catch a busy
    /// session <i>present and unheld</i> and the listing printed <i>in use:
    /// no</i> about a session another agent was driving. The gate was the
    /// discriminator, and the listing took it once per entry.
    /// </para>
    /// <para>
    /// <b>Nothing rewrites the guard, so the window is gone and the gate with
    /// it.</b> What is left is narrower by orders of magnitude and is asserted
    /// here rather than left to be discovered: between a peer taking the gate
    /// and that peer's own <c>browserai.lock</c> landing, a listing sees the
    /// directory as free. That is a <b>momentary</b> truth — it was free, and it
    /// is about to stop being — and the answer's own text already says a
    /// snapshot is not a reservation. Widening it again would show up here.
    /// </para>
    /// <para>
    /// <b>No clock anywhere.</b> The gate is held by a real process that will
    /// not let go until the job object is disposed, so this asserts the property
    /// without racing anything.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task APeerInsideCreateOrTakeIsTheOneWindowTheListingCanStillMisreport()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var midTake = Path.Combine(sessions.Root, "a-peer-is-inside-create-or-take");
        var index = new SessionIndex(sessions.Environment.Paths, NullLogger.Instance);

        Plant(index, midTake, "a session a peer is taking");

        var ready = Path.Combine(sessions.Root, "gate-ready.json");

        string text;

        using (var scope = new JobObjectScope())
        {
            _ = scope.Launch(ProbePath, AppContext.BaseDirectory, "session-hold-gate", midTake, ready);

            // The control that the right kernel object is held by a process that
            // will not let go: without it a probe that failed to start would
            // leave the gate free and the assertion below would be about
            // nothing.
            var gate = await ProbeReport.ReadAsync(ready, TestDefaults.ProcessHang);

            await Assert.That((string?)gate["acquisition"]).IsEqualTo(nameof(MutexAcquisition.Acquired));
            await Assert.That((string?)gate["mutexName"]).IsEqualTo(SessionPath.Resolve(midTake).MutexName);

            text = TextOf(await CallAsync(rig, SessionToolSurface.List, new JsonObject
            {
                ["directory"] = sessions.Root,
            }));
        }

        var block = BlockFor(text, midTake);

        // ⚠️ THE RESIDUAL, PINNED. The guard really is unheld, so this is not a
        // wrong answer -- it is the answer to a question asked at an instant that
        // will not last. The sentence beside it is what stops a caller reading it
        // as a reservation.
        await Assert.That(block).Contains("in use: no — nothing held ");
        await Assert.That(block).Contains("a snapshot rather than a reservation");
        await Assert.That(block).DoesNotContain("in use: UNKNOWN");

        // ⚠️ THE PRECONDITION, AND IT IS NOT A RETRY. Killing the job stops the
        // holder; the kernel releasing the mutex is a step after that, so this is
        // the deterministic proof that the peer's hold is gone -- an abandoned
        // mutex IS acquired, which is exactly the state a killed holder leaves --
        // and the bound is the product's own constant rather than a number
        // written here.
        using (var gate = MachineMutex.Create(SessionPath.Resolve(midTake).MutexName))
        {
            await Assert.That(gate.Acquire(LockScopes.PerDirectoryGate)).IsNotEqualTo(MutexAcquisition.NotAcquired);
            gate.Release();
        }

        // ⚠️ THE POSITIVE CONTROL, and it is what makes the assertion above
        // about the gate rather than about this listing always saying `no`: the
        // same session, held for real this time, reads YES.
        var location = SessionPath.Resolve(midTake);

        using var peer = new FileStream(location.LockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1);

        var after = TextOf(await CallAsync(rig, SessionToolSurface.List, new JsonObject
        {
            ["directory"] = sessions.Root,
        }));

        await Assert.That(BlockFor(after, midTake)).Contains("in use: YES — something holds ");
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
            new SessionLockRequest { Browser = "chromium", Purpose = purpose },
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
