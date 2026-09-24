// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using Microsoft.Win32;

namespace BrowserAI.Registration;

/// <summary>A PATH value as the registry holds it: the text unexpanded, and its kind.</summary>
/// <param name="Text">The value, exactly as stored.</param>
/// <param name="Kind"><see cref="RegistryValueKind.ExpandString"/> or <see cref="RegistryValueKind.String"/>.</param>
internal sealed record UserPathValue(string Text, RegistryValueKind Kind);

/// <summary>Where the user's PATH lives, and how a change to it is announced.</summary>
/// <remarks>
/// <b>A seam for the one reason every other seam in this namespace has one</b>: the
/// hook body is the same code in the product and in the suite, and the suite's
/// in-process arms must not write the person's own PATH. The product passes
/// <see cref="RegistryUserPathStore.User"/>; nothing else in the product passes
/// anything.
/// </remarks>
internal interface IUserPathStore
{
    /// <summary>Where this store is, for a log line.</summary>
    string Where { get; }

    /// <summary>The value, or <see langword="null"/> when there is none.</summary>
    /// <returns>The value.</returns>
    UserPathValue? Read();

    /// <summary>Writes the value, text and kind.</summary>
    /// <param name="value">The value.</param>
    void Write(UserPathValue value);

    /// <summary>Removes the value.</summary>
    void Delete();

    /// <summary>Tells running programs that the environment changed.</summary>
    void Announce();
}

/// <summary>A PATH value in the registry.</summary>
/// <param name="hive">The hive.</param>
/// <param name="subKey">The key under it.</param>
/// <param name="announce">Whether a change is broadcast to every top-level window.</param>
internal sealed class RegistryUserPathStore(RegistryKey hive, string subKey, bool announce) : IUserPathStore
{
    /// <summary>The value's name.</summary>
    public const string ValueName = "Path";

    /// <summary><c>HKCU\Environment\Path</c>, the user's own PATH, announced on change.</summary>
    public static RegistryUserPathStore User { get; } = new(Registry.CurrentUser, "Environment", announce: true);

    /// <inheritdoc/>
    public string Where => $@"{hive.Name}\{subKey}\{ValueName}";

    /// <inheritdoc/>
    public UserPathValue? Read()
    {
        using var key = hive.OpenSubKey(subKey, writable: false);

        if (key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string text)
        {
            return null;
        }

        return new UserPathValue(text, key.GetValueKind(ValueName));
    }

    /// <inheritdoc/>
    public void Write(UserPathValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        using var key = hive.CreateSubKey(subKey, writable: true);

        key.SetValue(ValueName, value.Text, value.Kind);
    }

    /// <inheritdoc/>
    public void Delete()
    {
        using var key = hive.OpenSubKey(subKey, writable: true);

        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <inheritdoc/>
    public void Announce()
    {
        if (announce)
        {
            _ = EnvironmentBroadcast.Announce();
        }
    }
}

/// <summary>What one change to the user's PATH did.</summary>
internal enum UserPathChange
{
    /// <summary>The entry was not there and was appended.</summary>
    Added,

    /// <summary>The entry was already there, so nothing was written.</summary>
    AlreadyThere,

    /// <summary>The entry was there and was taken off.</summary>
    Removed,

    /// <summary>The entry was not there, so nothing was written.</summary>
    NotThere,

    /// <summary>The PATH could not be read or written.</summary>
    Failed,
}

/// <summary>What a change to the user's PATH concluded.</summary>
/// <param name="Change">The change.</param>
/// <param name="Entry">The folder it was about.</param>
/// <param name="Detail">One sentence for the log.</param>
internal sealed record UserPathReport(UserPathChange Change, string Entry, string Detail);

/// <summary>
/// The install's <c>current\</c> folder on the user's PATH: put there by the install
/// and update hooks, taken off by the uninstall hook, and read by the Codex
/// ownership check.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Q294, decided 2026-09-24 by the maintainer, verbatim: <i>"Q294 b"</i>.</b>
/// Codex expands no variable in a server's command -- measured 0 of 48, and read in
/// its source -- so a committed project entry cannot name this install portably the
/// way Claude Code's <c>${LOCALAPPDATA}</c> spelling does. It names
/// <c>BrowserAI.Server.exe</c> alone, and Codex finds that through the PATH it hands
/// the server, which it takes from its own environment. So the install puts its
/// folder there.
/// </para>
/// <para>
/// <b>Each install root adds and removes only its own entry</b>, spelled as the
/// absolute path of its own <c>current\</c> folder and matched exactly: the suite's
/// test pack, installed under a scratch root, can never touch the real install's
/// entry, and an entry a person wrote in another spelling is never taken off.
/// <b>The value's kind is kept</b>, and a value that did not exist is created as
/// <c>REG_EXPAND_SZ</c>.
/// </para>
/// <para>
/// ⚠️ <b>Appended, and appended after a separator even when the value already ends
/// in one</b>, so that taking the entry off again gives back the value it was added
/// to, byte for byte -- the property the gate's clearance reading holds across every
/// run that installs the test pack.
/// </para>
/// </remarks>
internal static class UserPath
{
    /// <summary>The separator Windows uses between PATH entries.</summary>
    private const char Separator = ';';

    /// <summary>The folder an install puts on the PATH: the one its server is in.</summary>
    /// <param name="target">The install, resolved from the running image.</param>
    /// <returns>The folder.</returns>
    public static string EntryFor(RegistrationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Path.GetDirectoryName(target.Command)
            ?? throw new ArgumentException($"'{target.Command}' has no folder.", nameof(target));
    }

    /// <summary>Puts an entry on the PATH, unless it is there already.</summary>
    /// <param name="store">Where the PATH is.</param>
    /// <param name="entry">The folder.</param>
    /// <returns>What happened. Never throws.</returns>
    public static UserPathReport Add(IUserPathStore store, string entry)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        try
        {
            var value = store.Read();

            if (value is not null && Segments(value.Text).Any(segment => Names(segment, entry)))
            {
                return new UserPathReport(UserPathChange.AlreadyThere, entry, $"'{entry}' is already on the PATH at {store.Where}, so nothing was written.");
            }

            store.Write(value is null
                ? new UserPathValue(entry, RegistryValueKind.ExpandString)
                : value with { Text = value.Text + Separator + entry });
            store.Announce();

            return new UserPathReport(UserPathChange.Added, entry, $"Put '{entry}' on the PATH at {store.Where}, so a program started from now on finds BrowserAI.Server.exe by name.");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new UserPathReport(UserPathChange.Failed, entry, $"Could not put '{entry}' on the PATH at {store.Where}: {failure.Message}");
        }
    }

    /// <summary>Takes an entry off the PATH, exactly as <see cref="Add"/> wrote it.</summary>
    /// <param name="store">Where the PATH is.</param>
    /// <param name="entry">The folder.</param>
    /// <returns>What happened. Never throws.</returns>
    public static UserPathReport Remove(IUserPathStore store, string entry)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        try
        {
            if (store.Read() is not { } value)
            {
                return new UserPathReport(UserPathChange.NotThere, entry, $"There is no PATH at {store.Where}, so '{entry}' was not on it.");
            }

            var segments = Segments(value.Text);
            var kept = segments.Where(segment => !Names(segment, entry)).ToList();

            if (kept.Count == segments.Count)
            {
                return new UserPathReport(UserPathChange.NotThere, entry, $"'{entry}' was not on the PATH at {store.Where}, so nothing was written.");
            }

            // A value that held this entry and nothing else was created by Add, and
            // goes the way it came; a value that is now empty text was empty text
            // with the entry appended, and stays.
            if (kept.Count is 0)
            {
                store.Delete();
            }
            else
            {
                store.Write(value with { Text = string.Join(Separator, kept) });
            }

            store.Announce();

            return new UserPathReport(UserPathChange.Removed, entry, $"Took '{entry}' off the PATH at {store.Where}.");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new UserPathReport(UserPathChange.Failed, entry, $"Could not take '{entry}' off the PATH at {store.Where}: {failure.Message}");
        }
    }

    /// <summary>A PATH value's entries, empty ones included.</summary>
    /// <param name="text">The value.</param>
    /// <returns>The entries, in order.</returns>
    public static List<string> Segments(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return [.. text.Split(Separator)];
    }

    /// <summary>Whether a PATH entry is exactly this folder: case and a trailing separator aside.</summary>
    /// <param name="segment">One entry, as stored.</param>
    /// <param name="entry">The folder.</param>
    /// <returns>Whether it names it.</returns>
    public static bool Names(string segment, string entry) =>
        string.Equals(Trimmed(segment), Trimmed(entry), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The folders a program started now would search for a bare name: the machine's
    /// PATH, then the user's, each read from the registry and expanded, which is the
    /// order a new environment block holds them in -- measured, kb/windows/processes.md.
    /// </summary>
    /// <remarks>
    /// <b>The registry and not this process's own PATH</b>, because this process may
    /// be older than the entry, or started by a program that is: a child receives a
    /// copy of its parent's environment, and a running program keeps the one it has.
    /// </remarks>
    /// <returns>The folders, in order.</returns>
    public static IReadOnlyList<string> SearchDirectories()
    {
        var folders = new List<string>();

        foreach (var (hive, subKey) in new[]
        {
            (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"),
            (Registry.CurrentUser, "Environment"),
        })
        {
            try
            {
                using var key = hive.OpenSubKey(subKey, writable: false);

                if (key?.GetValue(RegistryUserPathStore.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string text)
                {
                    folders.AddRange(Segments(Environment.ExpandEnvironmentVariables(text)).Where(folder => folder.Trim().Length > 0));
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A hive this process cannot read contributes nothing, which is what
                // a program started under the same account would find too.
            }
        }

        return folders;
    }

    /// <summary>The first folder that holds a file of this name, joined to it.</summary>
    /// <param name="fileName">A bare file name.</param>
    /// <param name="folders">The folders to search, in order.</param>
    /// <returns>The file's full path, or <see langword="null"/> when no folder holds it.</returns>
    public static string? Resolve(string fileName, IEnumerable<string> folders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(folders);

        foreach (var folder in folders)
        {
            try
            {
                var candidate = Path.Combine(folder.Trim().Trim('"'), fileName);

                if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception failure) when (failure is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A PATH entry that is not a path cannot hold anything.
            }
        }

        return null;
    }

    /// <summary>Whether a command is a bare file name, which a client resolves through PATH.</summary>
    /// <param name="command">The command.</param>
    /// <returns>Whether it names no folder at all.</returns>
    public static bool IsBareName(string command) =>
        command.Length > 0
        && command.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar]) < 0;

    private static string Trimmed(string path) =>
        path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
