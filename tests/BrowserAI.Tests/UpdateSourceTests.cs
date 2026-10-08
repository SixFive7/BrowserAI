// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Updates;

namespace BrowserAI.Tests;

/// <summary>
/// The background's update source, read from <c>--update-source</c>: absent is the
/// production feed, a folder is a folder, a URL is a URL, and anything else is
/// refused without falling back to the production feed.
/// </summary>
/// <remarks>
/// RESOLUTIONS 17 of 2026-10-08: the source is fixed at install time as an argument
/// the hooks write, and never an environment variable at run time.
/// </remarks>
internal sealed class UpdateSourceTests
{
    /// <summary>With no argument, the source is the production feed this build names today.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task NoArgumentMeansTheProductionFeed()
    {
        var reading = UpdateSource.Read(["--background"]);

        await Assert.That(reading.Refusal).IsNull();
        await Assert.That(reading.Source).IsNotNull();
        await Assert.That(reading.Source!.IsNamed).IsFalse();
        await Assert.That(reading.Source.IsFolder).IsFalse();
        await Assert.That(reading.Source.Feed.ManifestUrl)
            .IsEqualTo(UpdateFeed.Create(UpdateConfiguration.ProductionBaseUrl!).ManifestUrl);
    }

    /// <summary>A fully qualified folder is a folder source, and an http or https URL is not.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AFolderIsAFolderSourceAndAUrlIsNot()
    {
        var folder = UpdateSource.Read([UpdateSource.Argument, @"C:\feeds\dev\"]);

        await Assert.That(folder.Source).IsNotNull();
        await Assert.That(folder.Source!.IsFolder).IsTrue();
        await Assert.That(folder.Source.IsNamed).IsTrue();
        await Assert.That(folder.Source.Feed.BaseUrl).IsEqualTo(@"C:\feeds\dev");
        await Assert.That(folder.Source.Feed.Channel).IsEqualTo(UpdateFeed.DefaultChannel);

        var share = UpdateSource.Read([UpdateSource.Argument, @"\\server\share\feed"]);

        await Assert.That(share.Source).IsNotNull();
        await Assert.That(share.Source!.IsFolder).IsTrue();

        var url = UpdateSource.Read(["--background", UpdateSource.Argument, "https://example.invalid/browserai/"]);

        await Assert.That(url.Source).IsNotNull();
        await Assert.That(url.Source!.IsFolder).IsFalse();
        await Assert.That(url.Source.Feed.ManifestUrl).IsEqualTo("https://example.invalid/browserai/releases.win.json");
    }

    /// <summary>
    /// An argument that names no usable source is refused with a reason, and the
    /// refusal never turns into the production feed.
    /// </summary>
    /// <remarks>
    /// A relative path resolves against a working directory a task-started process
    /// does not share with the hook that wrote it; a <c>file:</c> URI is a folder
    /// Velopack would read under the whole URI's name; a URL ending in the channel is
    /// the feed-URL hazard <see cref="UpdateFeed"/> refuses.
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnArgumentThatNamesNoUsableSourceIsRefusedAndNeverFallsBack()
    {
        string[][] refused =
        [
            [UpdateSource.Argument],
            [UpdateSource.Argument, "--background"],
            [UpdateSource.Argument, @"feeds\dev"],
            [UpdateSource.Argument, @"C:feeds\dev"],
            [UpdateSource.Argument, "file:///C:/feeds/dev"],
            [UpdateSource.Argument, "ftp://example.invalid/feed"],
            [UpdateSource.Argument, "https://example.invalid/browserai/win"],
            [UpdateSource.Argument, @"C:\feeds\a", UpdateSource.Argument, @"C:\feeds\b"],
        ];

        foreach (var arguments in refused)
        {
            var reading = UpdateSource.Read(arguments);

            await Assert.That(reading.Source).IsNull().Because(string.Join(' ', arguments));
            await Assert.That(reading.Refusal).IsNotNull().Because(string.Join(' ', arguments));
        }
    }
}
