// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Runtime;

/// <summary>
/// A thread of its own that runs the work it is handed, one item at a time and in the
/// order it was handed over, for callers that await it.
/// </summary>
/// <remarks>
/// <para>
/// <b>For the calls Windows offers only synchronously</b>, decided 2026-10-12 by the
/// maintainer, in his words verbatim: <i>"78-82 your recommendation"</i>. The relay's
/// standard output (78 b), the process's walks over many files and its file and directory
/// changes (80 b and 81 c), and each session store's statements (82 b) each run on such a
/// thread: the caller awaits, and the thread that waits in the call is this one, which is
/// never a pool worker.
/// </para>
/// <para>
/// <b>The thread waits for its next item on its own queue's monitor, and that is the
/// type's one kept wait.</b> A pool worker never waits here:
/// <see cref="RunAsync{T}(Func{T})"/> queues and returns, and the item's task completes
/// with its continuations run off this thread, so a caller's code never runs where the
/// next item is waited for.
/// </para>
/// <para>
/// <b>An item handed over from this thread runs at once</b>, inline, because queuing it
/// behind the item that is running would wait for itself.
/// </para>
/// </remarks>
internal sealed class DedicatedThread : IDisposable
{
    private readonly Queue<Item> _items = new();
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;

    /// <summary>Starts the thread, which waits for its first item.</summary>
    /// <param name="name">The thread's name, which a dump and a debugger show.</param>
    public DedicatedThread(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = name,
        };

        _thread.Start();
    }

    /// <summary>Whether the calling code runs on this thread.</summary>
    public bool IsCurrent => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    /// <summary>Completes once the thread has run its last item and ended.</summary>
    public Task Ended => _ended.Task;

    /// <summary>Runs <paramref name="work"/> on this thread, after every item handed over before it.</summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work, which runs synchronously on this thread.</param>
    /// <returns>Its result, or its exception.</returns>
    /// <exception cref="ObjectDisposedException">The thread was told to end.</exception>
    public Task<T> RunAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (IsCurrent)
        {
            return Inline(work);
        }

        var item = new Item<T>(work);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            _items.Enqueue(item);
            Monitor.Pulse(_gate);
        }

        return item.Completion;
    }

    /// <summary>Runs <paramref name="work"/> on this thread, after every item handed over before it.</summary>
    /// <param name="work">The work, which runs synchronously on this thread.</param>
    /// <returns>Its completion, or its exception.</returns>
    /// <exception cref="ObjectDisposedException">The thread was told to end.</exception>
    public Task RunAsync(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return RunAsync(() =>
        {
            work();
            return true;
        });
    }

    /// <summary>
    /// Takes no more work: the thread runs what it holds and then ends. Calling it again
    /// does nothing.
    /// </summary>
    public void Complete()
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _items.Enqueue(Item.End);
            Monitor.Pulse(_gate);
        }
    }

    /// <summary>The same as <see cref="Complete"/>: the thread runs what it holds and then ends.</summary>
    public void Dispose() => Complete();

    private static Task<T> Inline<T>(Func<T> work)
    {
        try
        {
            return Task.FromResult(work());
        }
#pragma warning disable CA1031 // The item's boundary: the failure goes to its caller, who awaits it.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            return Task.FromException<T>(failure);
        }
    }

    /// <summary>The thread: each item in turn, until the end is handed over.</summary>
    private void Run()
    {
        while (true)
        {
            Item item;

            lock (_gate)
            {
                while (_items.Count is 0)
                {
#pragma warning disable RS0030 // Kept at the maintainer's 78 b, 80 b and 82 b: a thread of its own waits for its next item, and it is never a pool worker.
                    _ = Monitor.Wait(_gate);
#pragma warning restore RS0030
                }

                item = _items.Dequeue();
            }

            if (ReferenceEquals(item, Item.End))
            {
                break;
            }

            item.Run();
        }

        _ = _ended.TrySetResult();
    }

    /// <summary>One item of work.</summary>
    private abstract class Item
    {
        /// <summary>The item that tells the thread to end.</summary>
        public static readonly Item End = new EndItem();

        public abstract void Run();

        private sealed class EndItem : Item
        {
            public override void Run()
            {
            }
        }
    }

    /// <summary>One item, and the task its caller awaits.</summary>
    private sealed class Item<T>(Func<T> work) : Item
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Completion => _completion.Task;

        public override void Run()
        {
            try
            {
                _ = _completion.TrySetResult(work());
            }
#pragma warning disable CA1031 // The item's boundary: the failure goes to its caller, who awaits it.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                _ = _completion.TrySetException(failure);
            }
        }
    }
}
