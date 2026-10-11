// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Updates;

/// <summary>Whether the background asks its update source, and why not when it does not.</summary>
internal enum UpdateChecks
{
    /// <summary>The background asks its source, at most once every <see cref="BackgroundUpdates.CheckInterval"/>.</summary>
    Checking,

    /// <summary>This build is not installed, so it checks nothing (D11).</summary>
    NotInstalled,

    /// <summary>The install's arguments named a source this build refused, so it checks nothing.</summary>
    SourceRefused,

    /// <summary>A pre-release build whose source is a URL, which it never asks (H2).</summary>
    PreReleaseUrl,

    /// <summary>Nothing reported it: a reader that is not the background's update core.</summary>
    Unreported,
}

/// <summary>What kind of place the updates come from.</summary>
internal enum UpdateSourceKind
{
    /// <summary>The production feed, GitHub's releases of this repository.</summary>
    GitHubReleases,

    /// <summary>A folder on this PC, which the install's hooks named.</summary>
    Folder,

    /// <summary>A served feed at a URL the install's hooks named.</summary>
    Url,

    /// <summary>No source: the install's argument was refused, or nothing reported one.</summary>
    None,
}

/// <summary>
/// Where the background's updates come from, whether it asks there, and when it last
/// did: two of the three facts the maintainer's 43 b lets the page show.
/// </summary>
/// <remarks>
/// <b>43 b, the maintainer's choice of 2026-10-11</b>, in his words verbatim
/// <i>"40-46 your recommendations"</i>: the page may show where updates come from and
/// when BrowserAI last checked, which the background passes to it. The last check is
/// the moment the last check began, as the stamp under the data root records it
/// (<see cref="IUpdateCheckStamp"/>), and it survives a restart the way the stamp does.
/// </remarks>
/// <param name="Checks">Whether the background asks its source.</param>
/// <param name="Source">What kind of place the updates come from.</param>
/// <param name="Location">The folder or the URL, or <see langword="null"/> for GitHub's releases and for no source.</param>
/// <param name="Refusal">Why the install's argument was refused, or <see langword="null"/>.</param>
/// <param name="LastCheck">When the last check began, or <see langword="null"/> when none is recorded.</param>
internal sealed record UpdateWatch(
    UpdateChecks Checks,
    UpdateSourceKind Source,
    string? Location,
    string? Refusal,
    DateTimeOffset? LastCheck)
{
    /// <summary>What a reader that is not the update core answers: nothing known.</summary>
    public static UpdateWatch Unreported { get; } = new(UpdateChecks.Unreported, UpdateSourceKind.None, null, null, null);
}
