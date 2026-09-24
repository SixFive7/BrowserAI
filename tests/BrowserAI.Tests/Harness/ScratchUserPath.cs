// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Registration;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// A user PATH that lives in this object and nowhere else, for the arms that run the
/// hook body in this process.
/// </summary>
/// <remarks>
/// <b>The in-process arms never write the person's own PATH</b> -- Q294 b's PATH step
/// takes its store as a required argument for exactly that reason, as the hook takes
/// its data root. The arms that run a real <c>Setup.exe</c> cannot be given a store
/// and do write the real <c>HKCU\Environment</c>, adding their own scratch root's
/// entry and taking it off again, which the gate's clearance reading and the arms
/// themselves hold byte-identical.
/// </remarks>
internal sealed class ScratchUserPath : IUserPathStore
{
    /// <summary>The value, or <see langword="null"/> when there is none.</summary>
    public UserPathValue? Value { get; set; }

    /// <summary>How many times a change was announced.</summary>
    public int Announcements { get; private set; }

    /// <summary>How many times the value was written or deleted.</summary>
    public int Writes { get; private set; }

    /// <inheritdoc/>
    public string Where => "the suite's own scratch PATH";

    /// <inheritdoc/>
    public UserPathValue? Read() => Value;

    /// <inheritdoc/>
    public void Write(UserPathValue value)
    {
        Value = value;
        Writes++;
    }

    /// <inheritdoc/>
    public void Delete()
    {
        Value = null;
        Writes++;
    }

    /// <inheritdoc/>
    public void Announce() => Announcements++;
}
