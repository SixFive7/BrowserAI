// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Runtime;
using BrowserAI.Tests.Harness;

namespace BrowserAI.Tests;

/// <summary>
/// A thread of its own that runs queued work for callers that await it, and the
/// process's one file-system thread built on it: the maintainer's 78 b, 80 b, 81 c and
/// 82 b of 2026-10-12, in his words verbatim, <i>"78-82 your recommendation"</i>.
/// </summary>
internal sealed class DedicatedThreadTests
{
    /// <summary>
    /// The work runs on the thread of its own, never on its caller and never on a second
    /// thread, in the order it was handed over.
    /// </summary>
    /// <remarks>
    /// <b>The order is read from the work since 2026-10-12, the maintainer's 111 a</b>, in his
    /// words verbatim, <i>"your recommendation"</i>: each item takes a run number when its
    /// work runs, and the run numbers must be the indexes. <i>Previously the arm compared the
    /// indexes the items returned</i>, which <c>Task.WhenAll</c> hands back in the order of the
    /// tasks whatever order the work ran in, so with the queue taken newest first it stayed
    /// green while the work ran 0, 49, 48, 47 and on. Planted red that way since, and with the
    /// check for an item handed over from the thread inverted, which runs every item on its
    /// caller.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task WorkRunsOnTheThreadOfItsOwnInTheOrderItWasHandedOver()
    {
        using var thread = new DedicatedThread("dedicated-thread-order");
        var caller = Environment.CurrentManagedThreadId;
        var ran = 0;

        var runs = Enumerable.Range(0, 50)
            .Select(index => thread.RunAsync(() => (Index: index, Ran: Interlocked.Increment(ref ran) - 1, Thread: Environment.CurrentManagedThreadId)))
            .ToList();

        var results = await Task.WhenAll(runs).WaitAsync(TestDefaults.InProcessHang);

        await Assert.That(string.Join(",", results.Select(static result => result.Ran))).IsEqualTo(string.Join(",", results.Select(static result => result.Index)));
        await Assert.That(results.Select(static result => result.Thread).Distinct().Count()).IsEqualTo(1);
        await Assert.That(results[0].Thread).IsNotEqualTo(caller);
    }

    [Test]
    public async Task AFailureReachesItsCallerAndTheThreadRunsTheNextItem()
    {
        using var thread = new DedicatedThread("dedicated-thread-failure");

        var failing = thread.RunAsync(static () => throw new InvalidOperationException("the item's own failure"));
        var next = thread.RunAsync(static () => 7);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => failing.WaitAsync(TestDefaults.InProcessHang));

        await Assert.That(failure!.Message).IsEqualTo("the item's own failure");
        await Assert.That(await next.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(7);
    }

    /// <summary>
    /// A caller that awaits an item continues off the thread, so its code never holds the
    /// thread every other caller queues behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two halves since 2026-10-12, the maintainer's 112 a and b</b>, in his words verbatim,
    /// <i>"your recommendation"</i>. <i>Previously the arm awaited one item on a new thread</i>,
    /// and with the item's completion made without
    /// <c>TaskCreationOptions.RunContinuationsAsynchronously</c> it stayed green 10 times of
    /// 10: the thread completes that item before the arm's wait has registered its
    /// continuation, and a continuation registered after the completion is queued to the pool,
    /// with the defect in or out.
    /// </para>
    /// <para>
    /// (a) <b>Two hundred hand-overs on the running thread</b>, each continuation checked for
    /// the thread it landed on. With that plant one landed on the thread at the first of them
    /// in 5 runs of 5, the item awaited before the loop having started the thread; a loop on
    /// a new thread caught it at the second or the third. Its red rests on the thread losing
    /// that race once in the two hundred; its green does not depend on timing, because
    /// without the defect no continuation of a caller can run there. (b) <b>The item's task carries
    /// <c>RunContinuationsAsynchronously</c></b>, red with the same plant on every run: the
    /// option is what keeps a continuation off the thread, so a version of the type that kept
    /// them off some other way would fail here and pass (a). Both halves are asserted
    /// together, so a red names each one it fails.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnAwaitingCallerContinuesOffTheThread()
    {
        using var thread = new DedicatedThread("dedicated-thread-continuation");

        var first = thread.RunAsync(static () => Environment.CurrentManagedThreadId);
        var options = first.CreationOptions;
        _ = await first.WaitAsync(TestDefaults.InProcessHang);

        var landedOnTheThreadAt = 0;

        for (var handOver = 1; handOver <= 200 && landedOnTheThreadAt is 0; handOver++)
        {
            var worker = await thread.RunAsync(static () => Environment.CurrentManagedThreadId).WaitAsync(TestDefaults.InProcessHang);

            // The continuation of the await above ran somewhere: never on the thread that
            // waits for the next item, or a caller's code would hold the thread every other
            // caller queues behind.
            if (Environment.CurrentManagedThreadId == worker)
            {
                landedOnTheThreadAt = handOver;
            }
        }

        // Off whichever thread this is, so a continuation that landed on the thread lets it go.
        await Task.Yield();

        using (Assert.Multiple())
        {
            await Assert.That(landedOnTheThreadAt).IsEqualTo(0);
            await Assert.That(options & TaskCreationOptions.RunContinuationsAsynchronously).IsEqualTo(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    [Test]
    public async Task AnItemHandedOverFromTheThreadRunsAtOnce()
    {
        using var thread = new DedicatedThread("dedicated-thread-inline");

        var (ranAtOnce, inner) = await thread.RunAsync(() =>
        {
            var inner = thread.RunAsync(static () => 3);

            // Queued behind the item that is running, it would wait for itself.
            return (inner.IsCompletedSuccessfully, inner);
        }).WaitAsync(TestDefaults.InProcessHang);

        await Assert.That(ranAtOnce).IsTrue();
        await Assert.That(await inner).IsEqualTo(3);
    }

    /// <summary>
    /// A thread told to complete runs every item it holds, ends, and refuses any more work.
    /// </summary>
    /// <remarks>
    /// <b>A thousand items are handed over before <c>Complete</c> since 2026-10-12, the
    /// maintainer's 113 a</b>, in his words verbatim, <i>"your recommendation"</i>, so most of
    /// them are still queued when it is called: 999 of the thousand in 5 runs of 5.
    /// <i>Previously the arm handed over one item</i>, which had left the queue by the time
    /// <c>Complete</c> ran in 5 runs of 5, so with <c>Complete</c> dropping what the queue held
    /// it stayed green 6 times of 6. Planted red that way since, at the hang detector, and
    /// with the end never reported and with work handed over after the end taken and not
    /// refused.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task ACompletedThreadRunsWhatItHoldsEndsAndRefusesMoreWork()
    {
        var thread = new DedicatedThread("dedicated-thread-complete");

        var held = Enumerable.Range(0, 1000).Select(index => thread.RunAsync(() => index)).ToList();
        thread.Complete();

        var results = await Task.WhenAll(held).WaitAsync(TestDefaults.InProcessHang);

        await Assert.That(results.Length).IsEqualTo(1000);
        await thread.Ended.WaitAsync(TestDefaults.InProcessHang);
        _ = Assert.Throws<ObjectDisposedException>(() => _ = thread.RunAsync(static () => 2));

        // A second end changes nothing.
        thread.Complete();
        thread.Dispose();
    }

    [Test]
    public async Task TheCallsTheBanKeepsOnTheFileSystemThreadRefuseACallerOnAnyOtherThread()
    {
        using var scratch = await ScratchDirectory.CreateAsync("file-system-thread-refusal");
        var directory = Path.Combine(scratch.Path, "made-here");

        await Assert.That(FileSystemThread.IsCurrent).IsFalse();
        _ = Assert.Throws<InvalidOperationException>(() => _ = FileSystemThread.Here.CreateDirectory(directory));
        await Assert.That(Directory.Exists(directory)).IsFalse();

        var (onTheThread, exists) = await FileSystemThread.RunAsync(() => (FileSystemThread.IsCurrent, FileSystemThread.Here.CreateDirectory(directory).Exists))
            .WaitAsync(TestDefaults.InProcessHang);

        await Assert.That(onTheThread).IsTrue();
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task EveryAsynchronousMemberMakesItsCallOnTheFileSystemThread()
    {
        using var scratch = await ScratchDirectory.CreateAsync("file-system-thread-members");
        var directory = Path.Combine(scratch.Path, "a");
        var renamed = Path.Combine(scratch.Path, "b");
        var file = Path.Combine(renamed, "one.txt");
        var moved = Path.Combine(renamed, "two.txt");

        _ = await FileSystemThread.CreateDirectoryAsync(directory).WaitAsync(TestDefaults.InProcessHang);
        await FileSystemThread.MoveDirectoryAsync(directory, renamed).WaitAsync(TestDefaults.InProcessHang);
        await File.WriteAllTextAsync(file, "one");
        await FileSystemThread.MoveFileAsync(file, moved, overwrite: false).WaitAsync(TestDefaults.InProcessHang);

        var listed = await FileSystemThread.RunAsync(() => FileSystemThread.Here.Files(renamed)).WaitAsync(TestDefaults.InProcessHang);

        await Assert.That(listed).IsEquivalentTo([moved]);

        await FileSystemThread.DeleteFileAsync(moved).WaitAsync(TestDefaults.InProcessHang);
        await FileSystemThread.DeleteEmptyDirectoryAsync(renamed).WaitAsync(TestDefaults.InProcessHang);

        await Assert.That(Directory.Exists(renamed)).IsFalse();
    }
}
