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
/// ⚠️ <b>Every arm in a file that constructs one must carry <c>[NotInParallel]</c>
/// with NO key — which in TUnit means it runs beside nothing at all.</b>
/// <i>Corrected 2026-09-15 (previously "Every arm that uses one must be
/// <c>[NotInParallel]</c> on the same key. Two scopes at once would each see the
/// other's value, and the loser would assert against a directory the winner
/// wrote").</i> That rule named half the surface and the wrong half. Two scopes
/// colliding with each other is the small case; <b>the readers are every arm in
/// the suite that starts a product child</b>, because a child inherits the
/// process environment block — and those arms open no scope, hold no key and
/// cannot be enumerated. Measured on the 2026-09-15 release gate: an installer
/// arm keyed to one group held <c>BROWSERAI_ROOT</c> for a few seconds, and three
/// arms that had never heard of it launched browsers into an empty root, started
/// a 203.8 MB provisioning download and went red. There is still no per-test
/// channel to have instead: the reader is another process.
/// </para>
/// <para>
/// <b>The rule is a scan rather than a sentence now</b> —
/// <see cref="BrowserAI.Tests.HouseRuleTests.EveryArmInAFileThatOverridesTheEnvironmentRunsBesideNothing"/>
/// reads the tree and refuses a keyed attribute or a missing one. This paragraph
/// is what it enforces, not what has to be remembered.
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
