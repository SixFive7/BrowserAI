// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using BrowserAI.Hosting;

namespace BrowserAI.App.Page;

/// <summary>The changelog this build was made from, embedded in the executable.</summary>
internal static class ShippedChangelog
{
    /// <summary>The resource's name, which the project file gives it.</summary>
    public const string ResourceName = "BrowserAI.CHANGELOG.md";

    /// <summary>The whole changelog, or <see langword="null"/> when the build carries none.</summary>
    /// <returns>The text.</returns>
    /// <remarks>
    /// <b>Copied out of the image's memory since 2026-10-11</b> (<see cref="EmbeddedResource"/>),
    /// where it is compiled in, and never read through a stream, which the maintainer's
    /// decisions 54 and 63 ban. UTF-8 with no byte order mark, as every file this repository
    /// writes. <i>Previously a <see cref="StreamReader"/>'s <c>ReadToEnd</c> over the
    /// resource's stream.</i>
    /// </remarks>
    public static string? Text() =>
        EmbeddedResource.BytesOf(typeof(ShippedChangelog).Assembly, ResourceName) is { } bytes
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes)
            : null;

    /// <summary>Every release the changelog this build was made from holds, in its order.</summary>
    /// <returns>The sections, or none when the build carries no changelog.</returns>
    public static IReadOnlyList<ChangelogSection> Releases() =>
        Text() is { } text ? ChangelogPageContent.Releases(text) : [];

    /// <summary>The section for the version this binary is, or <see langword="null"/>.</summary>
    /// <returns>The section.</returns>
    public static ChangelogSection? ForThisBuild() =>
        Text() is { } text ? ChangelogPageContent.SectionFor(text, BuildVersion.Current) : null;
}
