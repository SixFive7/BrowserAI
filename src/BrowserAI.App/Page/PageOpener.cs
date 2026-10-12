// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Updates;
using Microsoft.Extensions.Logging;

namespace BrowserAI.App.Page;

/// <summary>
/// What a person's start does with the address it was handed: open it in the
/// person's own browser, or write it to the file it was asked to.
/// </summary>
/// <remarks>
/// <para>
/// <b>The start the person made opens the tab, and not the coordinator</b>, because
/// it is the process that holds the right to bring something to the front: a
/// process the Start Menu started may, and a coordinator the task scheduler started
/// may not (Q284 a's measurement). It receives the address through the pipe and
/// hands it to the shell, which starts or reaches the default browser.
/// </para>
/// <para>
/// <b><see cref="WriteAddressArgument"/> writes the address to a file and opens
/// nothing</b>, for anything that needs the address without a browser: the suite's
/// arms that start the real executable, which may never put a tab on the screen of
/// the person at the machine. The file is the caller's own, and the address is
/// already the current user's to read through the pipe.
/// </para>
/// </remarks>
internal static partial class PageOpener
{
    /// <summary>The argument naming a file the address is written to instead of being opened.</summary>
    public const string WriteAddressArgument = "--write-address";

    /// <summary>The file <see cref="WriteAddressArgument"/> names, or <see langword="null"/>.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The path.</returns>
    public static string? WriteAddressFrom(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], WriteAddressArgument, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>Opens a tab's address, or writes it where it was asked to go.</summary>
    /// <param name="address">The address, or <see langword="null"/> when none was handed out.</param>
    /// <param name="writeTo">The file to write it to instead, or <see langword="null"/> to open it.</param>
    /// <param name="open">Opens an address with the shell; <c>ShellInterop.OpenUrlAsync</c> in the product.</param>
    /// <param name="logger">Where the outcome is recorded, never with the token.</param>
    /// <returns>Whether the address went where it was meant to.</returns>
    public static async Task<bool> DeliverAsync(string? address, string? writeTo, Func<string, Task<bool>> open, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(logger);

        if (address is not { Length: > 0 })
        {
            OpenerLog.NoAddress(logger);
            return false;
        }

        var port = new Uri(address).Port.ToString(CultureInfo.InvariantCulture);

        if (writeTo is { Length: > 0 })
        {
            await File.WriteAllTextAsync(writeTo, address, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);
            OpenerLog.Written(logger, port, writeTo);
            return true;
        }

        var opened = await open(address).ConfigureAwait(false);

        OpenerLog.Opened(logger, port, opened);
        return opened;
    }

    /// <summary>What the page knows about this BrowserAI, read once.</summary>
    /// <param name="paths">Where the data and the log are.</param>
    /// <param name="relayHold">How long a conversation holds an update after its app's last message: the relay's countdown.</param>
    /// <returns>The facts.</returns>
    public static async Task<PageFacts> FactsForAsync(IAppPaths paths, TimeSpan relayHold)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var resolution = await RegistrationTarget.ResolveAsync(Environment.ProcessPath).ConfigureAwait(false);
        var target = resolution.Target;

        return new PageFacts
        {
            Version = BuildVersion.Current,
            InstallRoot = InstallLocation.RootAppDir,
            DataRoot = paths.RootAppDir,
            LogDirectory = paths.LogDirectory,
            ServerCommand = target?.Command,
            ServerArguments = target is not null ? (await InstallerSettings.SavedForAsync(target.InstallRoot, InstallLocation.AppId).ConfigureAwait(false)).RelayArguments : InstallerSettings.None.RelayArguments,
            ServerRefusal = resolution.Resolved ? null : resolution.Refusal,
            RelayHold = relayHold,

            // 55 a and 72 a, 2026-10-11: the scheme this install's hooks registered, and none
            // for a build that is not installed, which registered nothing.
            LinkScheme = InstallLocation.RootAppDir is { Length: > 0 } ? LinkScheme.NameFor(InstallLocation.AppId) : null,
        };
    }

    /// <summary>The opener's own records.</summary>
    private static partial class OpenerLog
    {
        [LoggerMessage(EventId = 7031, Level = LogLevel.Information, Message = "Opened BrowserAI's page on port {Port} with the shell; the shell said {Opened}.")]
        public static partial void Opened(ILogger logger, string port, bool opened);

        [LoggerMessage(EventId = 7032, Level = LogLevel.Information, Message = "Wrote the address of BrowserAI's page on port {Port} to {Path}, as asked, and opened nothing.")]
        public static partial void Written(ILogger logger, string port, string path);

        [LoggerMessage(EventId = 7033, Level = LogLevel.Warning, Message = "No address was handed out for BrowserAI's page, so no tab was opened.")]
        public static partial void NoAddress(ILogger logger);
    }
}
