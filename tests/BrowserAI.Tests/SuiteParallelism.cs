// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<BrowserAI.Tests.SuiteParallelism>]

namespace BrowserAI.Tests;

/// <summary>
/// How many tests this suite runs at once: <b>every one of them</b>.
/// </summary>
/// <remarks>
/// <para>
/// MEASUREMENT IN PROGRESS 2026-08-17.
/// </para>
/// </remarks>
internal sealed class SuiteParallelism : IParallelLimit
{
    /// <summary>Above the test count, so the scheduler's semaphore never blocks.</summary>
    public const int Unbounded = 1024;

    /// <inheritdoc />
    public int Limit => Unbounded;
}

