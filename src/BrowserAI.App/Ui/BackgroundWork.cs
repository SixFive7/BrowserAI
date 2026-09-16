// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Globalization;
using BrowserAI.Updates;

namespace BrowserAI.App.Ui;

/// <summary>What one poll of some background work found.</summary>
/// <typeparam name="TResult">What the work produces.</typeparam>
/// <param name="Finished">Whether the work is over, either way.</param>
/// <param name="Result">What it produced, when it produced something.</param>
/// <param name="Refusal">
/// Why there is no result: the message it threw, or the sentence saying it ran
/// out of time.
/// </param>
internal readonly record struct BackgroundPoll<TResult>(bool Finished, TResult? Result, string? Refusal);

/// <summary>
/// One piece of work running off the dialog's thread, polled from the dialog's
/// own timer.
/// </summary>
/// <typeparam name="TResult">What the work produces.</typeparam>
/// <remarks>
/// <para>
/// ⚠️ <b>Every action this app offers used to run on the UI thread, and the
/// update check had no bound at all — 2026-09-16.</b> The check and the download
/// were called with <c>.GetAwaiter().GetResult()</c> inside the dialog's
/// callback, with <see cref="CancellationToken.None"/>, against a Velopack
/// <c>UpdateManager</c> built from a bare URL — whose <c>SimpleWebSource</c>
/// default is a <b>thirty-minute</b> <c>HttpClient</c> timeout. A feed that
/// answered slowly therefore froze the window, with no cursor, no repaint and no
/// close button, for up to half an hour. The server's lane wrapped the identical
/// call in a tripwire; this one had nothing.
/// </para>
/// <para>
/// <b>The bound is the server's own constant and is never a number written
/// here.</b> <see cref="DefaultBudget"/> is
/// <see cref="UpdateService.CrashTripwire"/> — the same outer deadline the
/// server's pass runs under, for the same calls. It is a crash tripwire rather
/// than flow control: nothing healthy reaches it, and the dialog stays fully
/// usable the whole time, so a person who does not want to wait closes the
/// window.
/// </para>
/// <para>
/// ⚠️ <b>The deadline is enforced by the POLL and not by the token, deliberately.</b>
/// <c>UpdateManager.CheckForUpdatesAsync</c> takes no cancellation token at all
/// — <see cref="VelopackUpdateClient"/> says so where it calls it — so a
/// cancelled token cannot stop the request in flight. What the deadline does is
/// <b>abandon the wait</b>: the dialog stops waiting, says so, and the orphaned
/// task finishes into nothing. The token is passed as well, because the parts
/// that <i>do</i> honour it should.
/// </para>
/// <para>
/// <b>The result crosses threads once, at completion.</b> The work computes
/// everything it wants to say and hands it back as one value; nothing it touches
/// is read by the dialog while it runs. That is what makes this safe without a
/// lock — a completed <see cref="Task{TResult}"/> is a memory barrier, and an
/// abandoned one is never read at all.
/// </para>
/// </remarks>
/// <param name="budget">
/// How long the dialog will wait. Only the suite ever names one; everything in
/// the product takes <see cref="DefaultBudget"/>.
/// </param>
internal sealed class BackgroundWork<TResult>(TimeSpan budget) : IDisposable
{
    private readonly TimeSpan _budget = budget;
    private readonly Stopwatch _clock = new();

    private Task<TResult>? _task;
#pragma warning disable CA2213 // Cancelled and released rather than disposed: see Retire. Disposing would race the abandoned task's own use of the token.
    private CancellationTokenSource? _cancel;
#pragma warning restore CA2213
    private string _subject = string.Empty;

    /// <summary>Work bounded by <see cref="DefaultBudget"/>.</summary>
    public BackgroundWork()
        : this(DefaultBudget)
    {
    }

    /// <summary>What the dialog says while work is in flight.</summary>
    private string Waiting { get; set; } = string.Empty;

    /// <summary>
    /// How long the dialog waits: the server's own outer deadline for the same
    /// calls.
    /// </summary>
    public static TimeSpan DefaultBudget => UpdateService.CrashTripwire;

    /// <summary>Whether something is in flight.</summary>
    public bool Running => _task is not null;

    /// <summary>What to say while it is.</summary>
    public string Progress => Running ? Waiting : string.Empty;

    /// <summary>
    /// Starts work, unless something is already running.
    /// </summary>
    /// <param name="progress">What the dialog says while this runs.</param>
    /// <param name="subject">
    /// What the timeout sentence calls this, as a noun phrase that can open a
    /// sentence — <i>The update check</i>.
    /// </param>
    /// <param name="work">The work, off this thread.</param>
    /// <returns>Whether it started.</returns>
    public bool Start(string progress, string subject, Func<CancellationToken, TResult> work)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(work);

        if (Running)
        {
            return false;
        }

        Waiting = progress;
        _subject = subject;
        _cancel = new CancellationTokenSource(_budget);

        var token = _cancel.Token;

        _clock.Restart();
        _task = Task.Run(() => work(token), CancellationToken.None);

        return true;
    }

    /// <summary>
    /// Asks whether the work is over, and takes its answer when it is.
    /// </summary>
    /// <remarks>
    /// <b>Called from the dialog's timer notification, so it must be cheap and
    /// must never wait.</b> It answers <c>Finished: false</c> while there is
    /// nothing to say, and exactly once with the outcome.
    /// </remarks>
    /// <returns>What it found.</returns>
    public BackgroundPoll<TResult> Poll()
    {
        if (_task is not { } running)
        {
            return new BackgroundPoll<TResult>(false, default, null);
        }

        if (running.IsCompletedSuccessfully)
        {
            var result = running.Result;

            Retire();
            return new BackgroundPoll<TResult>(true, result, null);
        }

        if (running.IsCompleted)
        {
            var failure = running.Exception?.GetBaseException();
            var said = failure is OperationCanceledException or null
                ? OutOfTime()
                : failure.Message;

            Retire();
            return new BackgroundPoll<TResult>(true, default, said);
        }

        // ⚠️ THE DEADLINE IS READ HERE, not left to the token. The check's
        // underlying call takes no token, so the only thing that can end the
        // wait is the waiter -- and a bound nothing enforces is not a bound.
        if (_clock.Elapsed >= _budget)
        {
            var said = OutOfTime();

            Retire();
            return new BackgroundPoll<TResult>(true, default, said);
        }

        return new BackgroundPoll<TResult>(false, default, null);
    }

    /// <inheritdoc />
    public void Dispose() => Retire();

    /// <summary>The sentence for work that ran out of time.</summary>
    /// <returns>What the dialog shows.</returns>
    private string OutOfTime() =>
        $"{_subject} did not finish within {_budget.TotalSeconds.ToString("F0", CultureInfo.CurrentCulture)} seconds and was stopped.";

    /// <summary>
    /// Lets go of the current run: the task is abandoned rather than waited for.
    /// </summary>
    private void Retire()
    {
        _clock.Stop();

        // ⚠️ CANCEL AND LET GO. Disposing the source would race the abandoned
        // task's own use of the token, and waiting for that task is the thing
        // this whole type exists to stop doing.
        var cancel = _cancel;

        _task = null;
        _cancel = null;

        try
        {
            cancel?.Cancel();
        }
#pragma warning disable CA1031 // A source that will not cancel must not become the dialog's problem.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
