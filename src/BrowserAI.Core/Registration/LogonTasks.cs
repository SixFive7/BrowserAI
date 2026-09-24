// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Runtime.InteropServices;
using BrowserAI.Interop;

namespace BrowserAI.Registration;

/// <summary>What one call to the task scheduler did.</summary>
internal enum TaskChange
{
    /// <summary>The task was registered, or replaced by the definition given.</summary>
    Registered,

    /// <summary>The task was there and was removed.</summary>
    Removed,

    /// <summary>There was no task of that name, so there was nothing to remove.</summary>
    Absent,

    /// <summary>The task was started on demand.</summary>
    Started,

    /// <summary>There is no task of that name to start.</summary>
    NotRegistered,

    /// <summary>The scheduler refused, or did not answer inside the bound.</summary>
    Failed,
}

/// <summary>What one call to the task scheduler did, and one sentence saying what.</summary>
/// <param name="Change">What changed.</param>
/// <param name="Detail">A sentence for the log, naming the task.</param>
internal sealed record TaskReport(TaskChange Change, string Detail);

/// <summary>
/// The three things BrowserAI asks of the task scheduler: register a task, remove
/// it, and start it on demand with an argument.
/// </summary>
/// <remarks>
/// <b>A seam, so that no arm of the suite registers a task by accident.</b> The
/// installer hooks and the server take it; the suite hands them a store that
/// records, and the few arms about the real scheduler use
/// <see cref="ScheduledTasks"/> with a task named for the test pack.
/// </remarks>
internal interface ILogonTasks
{
    /// <summary>Registers a task in the scheduler's root folder, replacing one of the same name.</summary>
    /// <param name="name">The task's name.</param>
    /// <param name="definition">Its Task Scheduler 1.2 XML.</param>
    /// <returns>What happened.</returns>
    TaskReport Register(string name, string definition);

    /// <summary>Removes a task from the root folder.</summary>
    /// <param name="name">The task's name.</param>
    /// <returns>What happened; a task that was not there is <see cref="TaskChange.Absent"/>.</returns>
    TaskReport Remove(string name);

    /// <summary>Starts a task now, with one argument filling its action's <c>$(Arg0)</c>.</summary>
    /// <param name="name">The task's name.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>What happened.</returns>
    TaskReport Run(string name, string argument);
}

/// <summary>The real task scheduler, through its COM interface.</summary>
/// <remarks>
/// <para>
/// <b>Every call runs on a thread of its own, in the multi-threaded apartment, and
/// is waited for inside <see cref="CallBound"/>.</b> A hook runs on the app's
/// main thread, which is a single-threaded apartment for the dialog's sake, and the
/// server calls from its update lane's thread-pool thread; a thread made for the
/// call is the one apartment both can rely on, and a scheduler that does not answer
/// costs the bound and never the hook's own timeout.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> A hook that throws breaks the installer, and a
/// server that throws on its update lane loses nothing but the start it asked for;
/// every failure is a <see cref="TaskChange.Failed"/> with the scheduler's own
/// <c>HRESULT</c> in its sentence.
/// </para>
/// </remarks>
internal sealed class ScheduledTasks : ILogonTasks
{
    /// <summary>The folder every BrowserAI task is registered in: the root.</summary>
    /// <remarks>
    /// A non-elevated token registered a task there in 56 ms, measured 2026-09-24,
    /// and creating a folder of our own was not measured, so the root it is.
    /// </remarks>
    public const string Folder = @"\";

    /// <summary>The one instance.</summary>
    public static ScheduledTasks Instance { get; } = new();

    /// <summary>
    /// How long one call may take before it is abandoned: <b>5 seconds</b>.
    /// </summary>
    /// <remarks>
    /// <b>A hang detector, not a promptness claim.</b> Registering took 19.8 to
    /// 21.9 ms, a run 1.0 to 1.2 ms and a delete 2.4 to 2.5 ms, measured 2026-09-25,
    /// so the bound is more than 200 times the slowest; it sits inside the 15 s
    /// Velopack gives the update hook, which also registers with every client.
    /// </remarks>
    public static TimeSpan CallBound { get; } = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public TaskReport Register(string name, string definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition);

        return Call($"register '{name}'", folder =>
        {
            _ = folder.RegisterTask(
                name,
                definition,
                TaskSchedulerInterop.CreateOrUpdate,
                default,
                default,
                TaskSchedulerInterop.InteractiveToken,
                default);

            return new TaskReport(TaskChange.Registered, $"The task '{name}' is registered.");
        });
    }

    /// <inheritdoc />
    public TaskReport Remove(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Call($"remove '{name}'", folder =>
        {
            try
            {
                folder.DeleteTask(name, 0);
                return new TaskReport(TaskChange.Removed, $"The task '{name}' is removed.");
            }
            catch (Exception failure) when (failure.HResult == TaskSchedulerInterop.NotFound)
            {
                return new TaskReport(TaskChange.Absent, $"There was no task '{name}' to remove.");
            }
        });
    }

    /// <inheritdoc />
    public TaskReport Run(string name, string argument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);

        return Call($"start '{name}'", folder =>
        {
            IRegisteredTask task;

            try
            {
                task = folder.GetTask(name);
            }
            catch (Exception failure) when (failure.HResult == TaskSchedulerInterop.NotFound)
            {
                return new TaskReport(TaskChange.NotRegistered, $"There is no task '{name}' to start.");
            }

            var text = Marshal.StringToBSTR(argument);

            try
            {
                var instance = task.Run(new TaskSchedulerInterop.Variant { Type = TaskSchedulerInterop.VariantString, Value = text });

                return new TaskReport(TaskChange.Started, $"The task '{name}' was started with '{argument}', instance {instance.GetInstanceGuid()}.");
            }
            finally
            {
                Marshal.FreeBSTR(text);
            }
        });
    }

    /// <summary>The definition the scheduler stored for a task, or <see langword="null"/> when there is none.</summary>
    /// <remarks>Read by the suite, to say what a registration left behind.</remarks>
    /// <param name="name">The task's name.</param>
    /// <returns>The XML.</returns>
    public static string? DefinitionOf(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string? xml = null;

        var report = Call($"read '{name}'", folder =>
        {
            try
            {
                xml = folder.GetTask(name).GetXml();
                return new TaskReport(TaskChange.Registered, name);
            }
            catch (Exception failure) when (failure.HResult == TaskSchedulerInterop.NotFound)
            {
                return new TaskReport(TaskChange.Absent, name);
            }
        });

        return report.Change is TaskChange.Failed ? throw new InvalidOperationException(report.Detail) : xml;
    }

    /// <summary>The result and the time of a task's last run, or <see langword="null"/> when there is no such task.</summary>
    /// <remarks>Read by the suite, which starts a task and then asks what came of it.</remarks>
    /// <param name="name">The task's name.</param>
    /// <returns>The last result, and when it ran in UTC.</returns>
    public static (int Result, DateTime RanAt)? LastRunOf(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        (int, DateTime)? last = null;

        var report = Call($"read '{name}'", folder =>
        {
            try
            {
                var task = folder.GetTask(name);
                last = (task.GetLastTaskResult(), DateTime.FromOADate(task.GetLastRunTime()).ToUniversalTime());
                return new TaskReport(TaskChange.Registered, name);
            }
            catch (Exception failure) when (failure.HResult == TaskSchedulerInterop.NotFound)
            {
                return new TaskReport(TaskChange.Absent, name);
            }
        });

        return report.Change is TaskChange.Failed ? throw new InvalidOperationException(report.Detail) : last;
    }

    /// <summary>Runs one call against the root folder on a thread of its own.</summary>
    /// <param name="what">What the call does, for a failure's sentence.</param>
    /// <param name="work">The call.</param>
    /// <returns>What it reported, or a failure.</returns>
    private static TaskReport Call(string what, Func<ITaskFolder, TaskReport> work)
    {
        TaskReport? report = null;

        var thread = new Thread(() =>
        {
            var joined = TaskSchedulerInterop.CoInitializeEx(0, TaskSchedulerInterop.MultiThreaded);

            try
            {
                var created = TaskSchedulerInterop.CreateService(out var service);

                if (created < 0)
                {
                    report = Failed(what, $"the scheduler could not be created (0x{created:X8})");
                    return;
                }

                service.Connect(default, default, default, default);
                report = work(service.GetFolder(Folder));
            }
#pragma warning disable CA1031 // The boundary of every scheduler call: a failure is a sentence in a report, never a crash of a hook or a server.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                report = Failed(what, string.Create(CultureInfo.InvariantCulture, $"0x{failure.HResult:X8}, {failure.Message}"));
            }
            finally
            {
                // S_OK and S_FALSE both count a join that has to be undone; the
                // changed-mode answer joined nothing.
                if (joined >= 0)
                {
                    TaskSchedulerInterop.CoUninitialize();
                }
            }
        })
        {
            IsBackground = true,
            Name = "BrowserAI task scheduler call",
        };

        thread.Start();

        return !thread.Join(CallBound)
            ? Failed(what, string.Create(CultureInfo.InvariantCulture, $"the scheduler did not answer inside {CallBound.TotalSeconds:F0} s"))
            : report ?? Failed(what, "the call ended without saying what it did");
    }

    private static TaskReport Failed(string what, string why) =>
        new(TaskChange.Failed, $"The task scheduler could not {what}: {why}.");
}
