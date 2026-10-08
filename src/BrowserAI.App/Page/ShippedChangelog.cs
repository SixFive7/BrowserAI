// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;

namespace BrowserAI.App.Page;

/// <summary>The changelog this build was made from, embedded in the executable.</summary>
internal static class ShippedChangelog
{
    /// <summary>The resource's name, which the project file gives it.</summary>
    public const string ResourceName = "BrowserAI.CHANGELOG.md";

    /// <summary>The whole changelog, or <see langword="null"/> when the build carries none.</summary>
    /// <returns>The text.</returns>
    public static string? Text()
    {
        using var stream = typeof(ShippedChangelog).Assembly.GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>The section for the version this binary is, or <see langword="null"/>.</summary>
    /// <returns>The section.</returns>
    public static ChangelogSection? ForThisBuild() =>
        Text() is { } text ? ChangelogPageContent.SectionFor(text, BuildVersion.Current) : null;
}
