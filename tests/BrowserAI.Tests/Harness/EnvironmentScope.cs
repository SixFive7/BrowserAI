// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Environment variables set for the life of a scope, and restored however the
/// test ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Process-wide, because that is the only channel there is.</b> A child
/// inherits this process's environment block, and the two things that have to be
/// sandboxed around a real installer — where the MCP client keeps its
/// configuration, and where BrowserAI keeps its data — are both read by
/// executables the suite does not compose a command line for. An environment
/// overlay on the product's own process launcher would be a seam that exists for
/// no reason but a test.
/// </para>
/// <para>
/// ⚠️ <b>Every arm that uses one must be <c>[NotInParallel]</c> on the same
/// key.</b> Two scopes at once would each see the other's value, and the loser
/// would assert against a directory the winner wrote. There is no per-test
/// channel to have instead: the reader is another process.
/// </para>
/// <para>
/// <b>Moved out of <c>RegistrationTests</c> 2026-09-15</b>, where it was a
/// private class taking one name, when the real-installer arm needed two
/// variables held at once.
/// </para>
/// </remarks>
internal sealed class EnvironmentScope : IDisposable
{
    private readonly Dictionary<string, string?> _previous = [];

    /// <summary>Sets one variable.</summary>
    /// <param name="name">The variable.</param>
    /// <param name="value">Its value for the life of this scope.</param>
    public EnvironmentScope(string name, string? value)
        : this(new Dictionary<string, string?> { [name] = value })
    {
    }

    /// <summary>Sets several variables at once.</summary>
    /// <param name="values">What each one is for the life of this scope.</param>
    public EnvironmentScope(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var (name, value) in values)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>Puts back whatever was there before.</summary>
    public void Dispose()
    {
        foreach (var (name, value) in _previous)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
