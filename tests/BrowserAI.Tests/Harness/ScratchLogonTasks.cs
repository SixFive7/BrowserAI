// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
using BrowserAI.Registration;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A task scheduler that remembers what it was asked and registers nothing.
/// </summary>
/// <remarks>
/// <b>What every in-process hook arm hands the hook</b>, the way it hands it a
/// <see cref="ScratchUserPath"/>: the hook's own overload takes the scheduler as a
/// required argument so that an arm cannot register a task in the developer's
/// scheduler by forgetting one. The arms about the real scheduler use
/// <see cref="ScheduledTasks"/> with a name under the test pack's id.
/// </remarks>
internal sealed class ScratchLogonTasks : ILogonTasks
{
    /// <summary>A pack id no install on any machine has, for arms that need one.</summary>
    public const string AppId = "BrowserAI.app.scratch";

    /// <summary>What each registered name was given, in the order asked.</summary>
    public ConcurrentDictionary<string, string> Registered { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every call, in order, as <c>verb name [argument]</c>.</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>When set, every call fails with this sentence.</summary>
    public string? FailWith { get; set; }

    /// <inheritdoc />
    public Task<TaskReport> RegisterAsync(string name, string definition)
    {
        Calls.Enqueue($"register {name}");

        if (FailWith is { } why)
        {
            return Task.FromResult(new TaskReport(TaskChange.Failed, why));
        }

        Registered[name] = definition;
        return Task.FromResult(new TaskReport(TaskChange.Registered, $"registered {name}"));
    }

    /// <inheritdoc />
    public Task<TaskReport> RemoveAsync(string name)
    {
        Calls.Enqueue($"remove {name}");

        if (FailWith is { } why)
        {
            return Task.FromResult(new TaskReport(TaskChange.Failed, why));
        }

        return Task.FromResult(Registered.TryRemove(name, out _)
            ? new TaskReport(TaskChange.Removed, $"removed {name}")
            : new TaskReport(TaskChange.Absent, $"no {name}"));
    }

    /// <inheritdoc />
    public Task<TaskReport> RunAsync(string name, string argument)
    {
        Calls.Enqueue($"run {name} {argument}");

        if (FailWith is { } why)
        {
            return Task.FromResult(new TaskReport(TaskChange.Failed, why));
        }

        return Task.FromResult(Registered.ContainsKey(name)
            ? new TaskReport(TaskChange.Started, $"started {name} {argument}")
            : new TaskReport(TaskChange.NotRegistered, $"no {name}"));
    }
}
