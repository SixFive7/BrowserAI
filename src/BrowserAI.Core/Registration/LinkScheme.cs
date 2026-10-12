// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using Microsoft.Win32;

namespace BrowserAI.Registration;

/// <summary>
/// The scheme the dashboard's folder links use: what it is called for a pack, what Windows
/// starts for it, and its registration under the user's own classes.
/// </summary>
/// <remarks>
/// <para>
/// <b>55 a and 72 a, the maintainer's words of 2026-10-11 verbatim: <i>"55 a"</i> and
/// <i>"72 a"</i>.</b> A folder button on the page is a <c>browserai:</c> link. The browser
/// hands it to Windows, Windows starts the command registered here, and the process it starts
/// may bring a window to the front, which the background the Task Scheduler started may not:
/// measured on 2026-10-11 in his Firefox 157.0.1 with a scratch profile, where a folder and the
/// folder picker came to the front only when the link's own process opened them
/// (<c>kb/windows/loopback-page.md</c>).
/// </para>
/// <para>
/// ⚠️ <b>Only the shipping pack is <see cref="ShippingScheme"/></b>, and every other pack gets a
/// scheme of its own (<see cref="NameFor"/>). The suite's installs run beside the maintainer's
/// real one with the test pack's id, and a test install that wrote or removed
/// <c>HKCU\Software\Classes\browserai</c> would point his folder links at a scratch install, or
/// take them away. The toasts' activator is kept apart the same way, by the application id
/// its class is derived from.
/// </para>
/// <para>
/// <b>The command carries the installer's settings</b>, as every registration and the task's
/// action do (<see cref="InstallerSettings"/>): a test install's link start is handed its
/// <c>--data-root</c>, so it finds the background of that data root's pipe.
/// </para>
/// </remarks>
internal static class LinkScheme
{
    /// <summary>The shipping install's scheme, which the maintainer chose: 72 a.</summary>
    public const string ShippingScheme = "browserai";

    /// <summary>The argument Windows starts the one executable with for a link: the link follows it.</summary>
    public const string LinkArgument = "--link";

    /// <summary>The scheme key's default value, which names it as a URL protocol.</summary>
    public const string Description = "URL:BrowserAI";

    /// <summary>The value whose presence makes a key under the user's classes a URL protocol.</summary>
    public const string UrlProtocolValue = "URL Protocol";

    /// <summary>Where the command Windows starts is kept, under the scheme's key.</summary>
    public const string CommandKey = @"shell\open\command";

    /// <summary>The longest link a link start reads, far above any link the page draws.</summary>
    public const int LongestLink = 512;

    /// <summary>The scheme for a pack, or <see langword="null"/> where there is no pack.</summary>
    /// <remarks>
    /// <b>The shipping pack's is <see cref="ShippingScheme"/>, and any other's begins with it
    /// and a hyphen</b>, followed by what the pack id adds to the shipping id, or by the whole
    /// id where it adds nothing to it: the suite's <c>BrowserAI.app.test</c> is
    /// <c>browserai-test</c>. Only letters and digits of the id are kept, so the name is a
    /// scheme a browser accepts, and no pack but the shipping one can be named
    /// <see cref="ShippingScheme"/>.
    /// </remarks>
    /// <param name="packId">The Velopack pack id, or <see langword="null"/> for a build that is not installed.</param>
    /// <returns>The scheme.</returns>
    public static string? NameFor(string? packId)
    {
        if (packId is not { Length: > 0 })
        {
            return null;
        }

        if (StandardLocation.IsShipping(packId))
        {
            return ShippingScheme;
        }

        var added = packId.StartsWith(StandardLocation.ShippingPackId, StringComparison.OrdinalIgnoreCase)
            ? packId[StandardLocation.ShippingPackId.Length..]
            : packId;

        var kept = new string([.. added.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant)]);

        return ShippingScheme + "-" + (kept.Length > 0 ? kept : "pack");
    }

    /// <summary>The user's own classes, where the scheme's key lives: <c>HKCU\Software\Classes</c>.</summary>
    /// <returns>The key, open for writing; the caller disposes it.</returns>
    public static RegistryKey UserClasses() => Registry.CurrentUser.CreateSubKey(@"Software\Classes", writable: true);

    /// <summary>The command Windows starts for a link of this install.</summary>
    /// <param name="executable">The install's <c>current\BrowserAI.exe</c>.</param>
    /// <param name="settings">What the installer named, whose data root the link start is handed.</param>
    /// <returns>The command line, with <c>%1</c> where Windows puts the link.</returns>
    public static string CommandFor(string executable, InstallerSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(settings);

        var command = $"\"{executable}\" {LinkArgument} \"%1\"";

        return settings.DataRoot is { Length: > 0 } root
            ? $"{command} {SignInTask.DataRootArgument} \"{root}\""
            : command;
    }

    /// <summary>Registers a scheme for one install, over whatever stood there before.</summary>
    /// <param name="classes">The classes key: <see cref="UserClasses"/> in the product.</param>
    /// <param name="scheme">The scheme, <see cref="NameFor"/>'s.</param>
    /// <param name="command">What Windows starts, <see cref="CommandFor"/>'s.</param>
    public static void Register(RegistryKey classes, string scheme, string command)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        using (var key = classes.CreateSubKey(scheme, writable: true))
        {
            key.SetValue(string.Empty, Description, RegistryValueKind.String);
            key.SetValue(UrlProtocolValue, string.Empty, RegistryValueKind.String);
        }

        using var open = classes.CreateSubKey($@"{scheme}\{CommandKey}", writable: true);

        open.SetValue(string.Empty, command, RegistryValueKind.String);
    }

    /// <summary>
    /// Takes a scheme's registration back, when its command starts this install's program.
    /// </summary>
    /// <remarks>
    /// <b>A scheme whose command starts another program is left to that program</b>, as the
    /// toasts' activator is: two installs of one pack id share one scheme, the install that
    /// registered last owns it, and its own uninstall takes it back.
    /// </remarks>
    /// <param name="classes">The classes key.</param>
    /// <param name="scheme">The scheme.</param>
    /// <param name="executable">This install's program, the one its own registration named.</param>
    /// <returns>Whether nothing of this install's is left under the scheme; <see langword="false"/> when another program's command was left.</returns>
    public static bool Unregister(RegistryKey classes, string scheme, string executable)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        using (var open = classes.OpenSubKey($@"{scheme}\{CommandKey}", writable: false))
        {
            if (open?.GetValue(string.Empty) is string command
                && !command.StartsWith($"\"{executable}\" ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        classes.DeleteSubKeyTree(scheme, throwOnMissingSubKey: false);
        return true;
    }

    /// <summary>The command a scheme's registration starts, or <see langword="null"/> when it has none.</summary>
    /// <param name="classes">The classes key.</param>
    /// <param name="scheme">The scheme.</param>
    /// <returns>The command line.</returns>
    public static string? CommandOf(RegistryKey classes, string scheme)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        using var open = classes.OpenSubKey($@"{scheme}\{CommandKey}", writable: false);

        return open?.GetValue(string.Empty) as string;
    }
}

/// <summary>The hooks' step for the folder links' scheme: registered at an install or an update, removed at an uninstall.</summary>
/// <remarks>
/// <b>55 a and 72 a, 2026-10-11</b>, beside the toasts' activator (<see cref="ToastActivatorStep"/>):
/// the step turns every outcome into a sentence for the process log, because a hook never fails
/// on a link. A page whose links start nothing still shows every folder's path.
/// </remarks>
internal static class LinkSchemeStep
{
    /// <summary>Registers or removes this install's scheme under the user's own classes.</summary>
    /// <param name="intent">Which hook is running.</param>
    /// <param name="installRoot">The install root.</param>
    /// <param name="packId">The pack id, which names the scheme.</param>
    /// <param name="settings">What the installer named, which the command carries.</param>
    /// <returns>A sentence for the process log.</returns>
    public static string Apply(RegistrationIntent intent, string installRoot, string? packId, InstallerSettings settings)
    {
        try
        {
            using var classes = LinkScheme.UserClasses();

            return ApplyTo(classes, intent, installRoot, packId, settings);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return $"Not changed: {failure.Message}";
        }
    }

    /// <summary>The step over a classes key it is handed: the suite's seam, which hands it a scratch key.</summary>
    /// <param name="classes">The classes key.</param>
    /// <param name="intent">Which hook is running.</param>
    /// <param name="installRoot">The install root.</param>
    /// <param name="packId">The pack id.</param>
    /// <param name="settings">What the installer named.</param>
    /// <returns>A sentence for the process log.</returns>
    internal static string ApplyTo(RegistryKey classes, RegistrationIntent intent, string installRoot, string? packId, InstallerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(settings);

        if (LinkScheme.NameFor(packId) is not { } scheme)
        {
            return "Not changed: this install names no pack id.";
        }

        try
        {
            var executable = Path.Combine(installRoot, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);

            if (intent is RegistrationIntent.Uninstall)
            {
                return LinkScheme.Unregister(classes, scheme, executable)
                    ? $"Removed '{scheme}:'."
                    : $"Left '{scheme}:': it starts another install's program, not '{executable}'.";
            }

            var command = LinkScheme.CommandFor(executable, settings);

            LinkScheme.Register(classes, scheme, command);
            return $"Registered '{scheme}:', starting {command}.";
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return $"Not changed for '{scheme}:': {failure.Message}";
        }
    }
}
