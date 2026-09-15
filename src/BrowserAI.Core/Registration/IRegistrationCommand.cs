// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Registration;

/// <summary>
/// The seam between deciding to register and actually starting somebody else's
/// executable.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists for the same reason <see cref="Hosting.IAppPaths"/> does.</b>
/// Registration runs inside a Velopack fast-exit hook, which is a context no
/// test host can enter — so the decision has to be separable from the effect or
/// none of it is testable without an install. Everything above this interface
/// is exercised against a double; what remains below it is one
/// <c>CreateProcessW</c> and a <c>PATH</c> walk, and the suite drives those
/// against the real client too.
/// </para>
/// <para>
/// <b>Two members and no async.</b> A hook is a fast-exit callback on a thread
/// that is about to end; a synchronous call with its own budget is the shape
/// that cannot leave work in flight when the process exits underneath it.
/// </para>
/// </remarks>
internal interface IRegistrationCommand
{
    /// <summary>
    /// Finds the client's command-line executable, or reports that this machine
    /// has none.
    /// </summary>
    /// <param name="executableName">The file name to look for, with extension.</param>
    /// <returns>An absolute path, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <b><see langword="null"/> is an ordinary answer, not a failure.</b> A
    /// machine with no MCP client installed is a machine BrowserAI still
    /// installs correctly on — it simply has nothing to register with, which is
    /// a logged fact rather than an error.
    /// </remarks>
    string? Locate(string executableName);

    /// <summary>Runs the executable and waits for it, within a budget.</summary>
    /// <param name="executable">An absolute path, from <see cref="Locate"/>.</param>
    /// <param name="arguments">The argument vector, passed one element at a time.</param>
    /// <param name="budget">How long it may take before it is abandoned.</param>
    /// <returns>What happened. Never throws.</returns>
    CommandOutcome Run(string executable, IReadOnlyList<string> arguments, TimeSpan budget);
}

/// <summary>What one invocation of the client did.</summary>
/// <param name="ExitCode">
/// The process's exit code, cached the instant it exited —
/// <c>Process.ExitCode</c> throws after <c>Dispose()</c>, which is the defect
/// that made a hard startup failure log identically to a clean shutdown in the
/// setup this project replaces.
/// </param>
/// <param name="Output">
/// Everything the process wrote, both streams interleaved, trimmed. It is the
/// only channel the client has for <i>why</i>: every failure it has, benign or
/// otherwise, exits 1.
/// </param>
/// <param name="TimedOut">Whether the budget ran out and the process was killed.</param>
/// <param name="Failure">
/// Why it could not be started at all, when that is what happened. Distinct from
/// a non-zero exit: one means the client refused, the other means there was no
/// client to refuse.
/// </param>
internal readonly record struct CommandOutcome(int ExitCode, string Output, bool TimedOut, string? Failure)
{
    /// <summary>Whether the invocation ran to completion and exited zero.</summary>
    public bool Succeeded => !TimedOut && Failure is null && ExitCode is 0;
}
