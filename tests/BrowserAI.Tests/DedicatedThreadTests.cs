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
    [Test]
    public async Task WorkRunsOnTheThreadOfItsOwnInTheOrderItWasHandedOver()
    {
        using var thread = new DedicatedThread("dedicated-thread-order");
        var caller = Environment.CurrentManagedThreadId;

        var runs = Enumerable.Range(0, 50)
            .Select(index => thread.RunAsync(() => (Index: index, Thread: Environment.CurrentManagedThreadId)))
            .ToList();

        var results = await Task.WhenAll(runs).WaitAsync(TestDefaults.InProcessHang);

        await Assert.That(results.Select(static result => result.Index).SequenceEqual(Enumerable.Range(0, 50))).IsTrue();
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

    [Test]
    public async Task AnAwaitingCallerContinuesOffTheThread()
    {
        using var thread = new DedicatedThread("dedicated-thread-continuation");

        var worker = await thread.RunAsync(static () => Environment.CurrentManagedThreadId).WaitAsync(TestDefaults.InProcessHang);

        // The continuation of the await above ran somewhere: never on the thread that waits
        // for the next item, or a caller's code would hold the thread every other caller
        // queues behind.
        await Assert.That(Environment.CurrentManagedThreadId).IsNotEqualTo(worker);
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

    [Test]
    public async Task ACompletedThreadRunsWhatItHoldsEndsAndRefusesMoreWork()
    {
        var thread = new DedicatedThread("dedicated-thread-complete");

        var held = thread.RunAsync(static () => 1);
        thread.Complete();

        await Assert.That(await held.WaitAsync(TestDefaults.InProcessHang)).IsEqualTo(1);
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
