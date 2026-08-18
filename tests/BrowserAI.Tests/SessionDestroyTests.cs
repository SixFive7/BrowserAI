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
/// <c>browserai_destroy</c>'s one invariant that no end state can show: the
/// directory stays <b>owned</b> for as long as any of it is still on disk.
/// </summary>
/// <remarks>
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
            }

            while (!stop.IsCancellationRequested)
            {
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

    private static async Task<JsonObject> CallAsync(McpTestHarness rig, string tool, JsonObject arguments) =>
        await rig.Client.RoundTripAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        });
}
