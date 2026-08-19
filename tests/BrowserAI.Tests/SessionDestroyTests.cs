// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json.Nodes;
using BrowserAI.Runtime;
using BrowserAI.Sessions;
using BrowserAI.Tests.Harness;
using Microsoft.Extensions.Logging.Abstractions;

namespace BrowserAI.Tests;

/// <summary>
/// <c>browserai_destroy</c>'s two contracts: the directory stays <b>owned</b>
/// for as long as any of it is still on disk, and whatever it could not remove
/// is <b>named</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The second arm exists because a fast machine never reaches it.</b>
/// Destroy returns <c>isError: false</c> in two shapes — everything went, or a
/// tally and a list of what would not — and on a developer machine the first
/// shape is the only one anybody ever sees. <c>FirefoxSessionTests</c> asserted
/// the tree was simply gone, passed nine local runs and failed three consecutive
/// CI runs on a four-core runner, where Firefox was still mapping its profile
/// when the answer was composed. <see cref="DestroyAnswer"/> now carries the
/// contract for both tests, and <see cref="ADestroyThatCannotRemoveEverythingNamesWhatSurvivedAndSaysHowMany"/>
/// provokes the survivor arm deterministically — with a handle this test holds
/// itself, needing no browser and no slow machine — so the arm CI takes is
/// exercised on every run rather than only on the runs that fail.
/// </para>
/// <para>
/// <b>Why this is not asserted on the outcome.</b> A destroy that released the
/// lock first and a destroy that held it to the end leave byte-identical trees —
/// an empty parent, or the same list of survivors. The difference is only
/// visible <i>while it runs</i>, so the assertion is made by a peer that tries to
/// take the directory throughout, using the same call the sweep uses
/// (<see cref="SessionLock.TryHoldUnowned"/>) and therefore getting the same
/// kernel answer a second BrowserAI process would.
/// </para>
/// <para>
/// <b>The peer waits for the delete to begin, and that is what makes the test
/// exact rather than lucky.</b> Before the destroy re-takes the directory there
/// is a legitimate unowned interval — the live session is released first — and a
/// probe landing there would block the destroy's own acquisition rather than
/// measure anything. So the peer probes nothing until the first planted file has
/// gone, which is proof the delete pass is already running and therefore that
/// ownership has already been proven. From that instant on, a directory that can
/// be taken is the defect and nothing else.
/// </para>
/// <para>
/// <b>The window is widened on purpose rather than waited for.</b> The defect
/// this was written against
/// ([the 2026-08-18 adversarial review](../../docs/reviews/2026-08-18-adversarial-locking.md))
/// released ownership, then walked the whole tree for a size, then deleted — so
/// the unowned interval was a full recursive walk of a Chromium profile wide.
/// The planted files reproduce that width without a browser: they sort before
/// <c>lock.json</c>, so the delete pass removes every one of them before it
/// reaches the lock file.
/// </para>
/// <para>
/// <b>The green direction is a property, not a coincidence.</b> Nothing under the
/// session is removed except while <c>lock.json</c> is held, and the last two
/// nodes go inside the per-directory gate that
/// <see cref="SessionLock.TryHoldUnowned"/> has to take itself. So the peer
/// cannot succeed at any point after the first file goes, at any planted size,
/// on any machine.
/// </para>
/// </remarks>
internal sealed class SessionDestroyTests
{
    /// <summary>
    /// How many files are planted at the session root, all sorting before
    /// <c>lock.json</c>.
    /// </summary>
    /// <remarks>
    /// Enough that removing them is measurable work rather than one syscall, and
    /// small enough that planting them is not the cost of the test. The count
    /// sets how wide the broken code's window was; the fixed code has no window
    /// at any width.
    /// </remarks>
    private const int PlantedFiles = 2_000;

    /// <summary>The first planted name, and therefore the first node the delete pass removes.</summary>
    private const string FirstPlantedFile = "aaa-00000.bin";

    /// <summary>How long the peer waits between probes.</summary>
    /// <remarks>
    /// <b>A sleep rather than a spin, and it is not a promptness bound.</b> The
    /// suite runs every test at once on purpose; a thread spinning on a named
    /// mutex and a file open for the length of a directory delete is this test
    /// paying for its evidence with everybody else's timing, and it showed —
    /// the most start-up-sensitive test in the suite began meeting its own
    /// budget. It costs nothing here, because what the peer is watching for is
    /// the removal of two thousand files: hundreds of probes land inside that
    /// interval at a millisecond apiece, and against the pre-fix code the count
    /// they returned was in the thousands.
    /// </remarks>
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(1);

    [Test]
    public async Task DestroyKeepsTheDirectoryOwnedForAsLongAsAnyOfItIsStillOnDisk()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "destroyed-while-a-peer-watches");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "destroyed while a peer tries to take the directory out from under it",
            ["mode"] = "headless",
        });

        for (var i = 0; i < PlantedFiles; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, $"aaa-{i.ToString("D5", CultureInfo.InvariantCulture)}.bin"),
                "planted so the delete pass has real work to do");
        }

        var firstToGo = Path.Combine(directory, FirstPlantedFile);
        var location = SessionPath.Resolve(directory);

        using var stop = new CancellationTokenSource();
        var takenWhileTheTreeWasComingDown = 0;

        var peer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested && File.Exists(firstToGo))
            {
                // Nothing is probed yet. See the type's remarks: the interval
                // before the destroy re-takes the directory is legitimately
                // unowned, and probing it would contend with the acquisition
                // this test needs to succeed.
                Thread.Sleep(ProbeInterval);
            }

            while (!stop.IsCancellationRequested)
            {
                Thread.Sleep(ProbeInterval);

                if (SessionLock.TryHoldUnowned(location, out var hold) is not null)
                {
                    continue;
                }

                hold?.Dispose();
                _ = Interlocked.Increment(ref takenWhileTheTreeWasComingDown);
            }
        });

        var destroyed = await CallAsync(rig, SessionToolSurface.Destroy, new JsonObject
        {
            ["directory"] = directory,
        });

        await stop.CancelAsync();
        await peer;

        // The invariant, asserted first because it is the diagnosis: everything
        // else below is a consequence of a peer having taken the directory.
        await Assert.That(takenWhileTheTreeWasComingDown).IsEqualTo(0);

        await Assert.That((bool?)destroyed["isError"]).IsNotEqualTo(true);
        await Assert.That(Directory.Exists(directory)).IsFalse();

        // The positive control, because a probe that can never answer "yes"
        // reports zero for a reason that has nothing to do with the invariant.
        // The same call, against a session directory whose holder has let go,
        // succeeds.
        var released = Path.Combine(sessions.Root, "a-session-nobody-is-holding");
        _ = Directory.CreateDirectory(released);

        var control = SessionPath.Resolve(released);
        var taken = SessionLock.TryAcquire(
            control,
            new SessionLockRequest
            {
                Mode = "headless",
                Browser = ProvisionedBrowsers.Chromium,
                Purpose = "taken and released, so the peer's own probe has something to say yes to",
            },
            NullLogger.Instance);

        await Assert.That(taken.Acquired).IsNotNull();
        taken.Acquired?.Dispose();

        var refusal = SessionLock.TryHoldUnowned(control, out var held);

        held?.Dispose();
        await Assert.That(refusal).IsNull();
    }

    /// <summary>
    /// A destroy that could not remove everything says so, says how many, and
    /// names them — and still reports success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The survivor is a handle this test holds, which is what makes the arm
    /// deterministic.</b> No browser, no timing, no slow machine: a file opened
    /// <c>FileShare.None</c> cannot be unlinked, so the walk records it and
    /// records the directory above it that therefore cannot go either. Against a
    /// real browser the same arm is reached by the kernel lagging behind a
    /// process that has already exited, which is not something a test can ask
    /// for.
    /// </para>
    /// <para>
    /// ⚠️ <b><c>isError</c> stays <see langword="false"/>, and that is the
    /// decision being asserted rather than an accident of the code.</b> A destroy
    /// that removed a nine-thousand-file profile and could not remove eleven
    /// locked files has done what it was asked; failing the call would throw away
    /// the report of the nine thousand and tell the model to retry something that
    /// mostly worked. What makes that safe is the <i>naming</i> — and a naming
    /// nobody checks is how <c>isError: false</c> becomes a lie.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ADestroyThatCannotRemoveEverythingNamesWhatSurvivedAndSaysHowMany()
    {
        await using var sessions = RigSessionEnvironment.Create();
        await using var rig = await McpTestHarness.ThroughTheProxyAsync(sessions: sessions);

        var directory = Path.Combine(sessions.Root, "destroyed-while-something-holds-a-file");

        _ = await CallAsync(rig, SessionToolSurface.Init, new JsonObject
        {
            ["directory"] = directory,
            ["purpose"] = "destroyed while this test holds one of its files open",
            ["mode"] = "headless",
        });

        var held = Path.Combine(directory, "something-still-has-this-open.bin");
        JsonObject destroyed;

        await using (var handle = new FileStream(held, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await handle.WriteAsync("held open for the whole of the destroy"u8.ToArray());
            await handle.FlushAsync();

            destroyed = await CallAsync(rig, SessionToolSurface.Destroy, new JsonObject
            {
                ["directory"] = directory,
            });

            // Success-shaped on purpose: see the remarks.
            await Assert.That((bool?)destroyed["isError"]).IsNotEqualTo(true);

            var answer = TextOf(destroyed);

            // The whole contract, through the same routine FirefoxSessionTests
            // uses against a real Firefox.
            await DestroyAnswer.AccountsForWhatItLeftAsync(answer, directory);

            // ⚠️ AND THE SURVIVOR IT NAMED IS THE ONE THAT SURVIVED. Every
            // assertion above is satisfied by an answer that names some other
            // node under the session, and "N item(s) could not be removed" is
            // only actionable if the N are the right ones.
            var listed = DestroyAnswer.SurvivorsNamedIn(answer)?.Listed ?? [];

            await Assert.That(listed.Any(line => line.StartsWith(held, StringComparison.OrdinalIgnoreCase)))
                .IsTrue()
                .Because(answer);

            // And the directory above it, which the post-order walk reaches last
            // and reports as `<path>\: <why>` — the trailing separator is how a
            // directory that would not go is told apart from a file of the same
            // name. A caller told only about the file would not know the session
            // directory itself is still there.
            await Assert.That(listed.Any(line => line.StartsWith($"{directory}{Path.DirectorySeparatorChar}:", StringComparison.OrdinalIgnoreCase)))
                .IsTrue()
                .Because(answer);

            await Assert.That(File.Exists(held)).IsTrue();
        }

        // Released, and now the advice the answer gave -- "delete what is left
        // once whatever holds it has exited" -- is a thing that actually works.
        var afterRelease = ScratchDirectory.RemoveTree(directory);

        await Assert.That(string.Join(Environment.NewLine, afterRelease)).IsEmpty();
    }

    private static string TextOf(JsonObject answer) =>
        string.Join(
            "\n",
            (answer["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });
}
