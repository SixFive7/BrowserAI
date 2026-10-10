// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

namespace BrowserAI.Updates;

/// <summary>What a person sees one conversation of a client called, as the background read it.</summary>
/// <remarks>
/// <para>
/// <b>The maintainer's answer of 2026-10-10, verbatim: <i>"1.1-2.3 I accept all your
/// recommendations"</i></b>, which for this type is 1.2 a: the label is the VS Code
/// extension's own title rule over the conversation's record, and a conversation the
/// background cannot name gets BrowserAI's own words, <i>new conversation in
/// &lt;folder&gt;</i>, <i>Claude Code in &lt;folder&gt;</i> or <i>Codex in
/// &lt;folder&gt;</i>. The measurement behind it, of 2026-10-08, is in
/// <see href="../../../kb/mcp/protocol.md">kb/mcp/protocol.md</see>.
/// </para>
/// <para>
/// <b>It appears on the dashboard and in the update toast and nowhere else</b>: a title
/// is what a person typed or what a model wrote about it, so no log, record or answer
/// carries one.
/// </para>
/// </remarks>
/// <param name="Text">The words.</param>
/// <param name="IsTitle">
/// Whether they are the conversation's own title, read from the client's records, which
/// every place that shows one puts in quotes; otherwise they are BrowserAI's words for a
/// conversation it could not name, shown as they are.
/// </param>
internal sealed record ConversationName(string Text, bool IsTitle)
{
    /// <summary>The most a Claude Code tab in VS Code shows of a title: 25 characters.</summary>
    /// <remarks>
    /// <b>Upstream</b>, read in the extension's webview at 2.1.292 on 2026-10-08: a longer
    /// title is cut to 24 characters and an ellipsis. The toast cuts every title the same
    /// way, so what it names is what the tab says, and writes the ellipsis as three full
    /// stops, the character a person types.
    /// </remarks>
    public const int TabWidth = 25;

    /// <summary>The name as a page shows it: a title in quotes, BrowserAI's own words as they are.</summary>
    /// <returns>The words.</returns>
    public string Shown() => IsTitle ? $"\"{Text}\"" : Text;

    /// <summary>The name as the toast shows it: a title cut as a VS Code tab cuts it, in quotes.</summary>
    /// <returns>The words.</returns>
    public string ShownAsATab() => IsTitle ? $"\"{Cut(Text)}\"" : Text;

    /// <summary>A title cut to what a VS Code tab shows: 24 characters and three full stops when it is longer than <see cref="TabWidth"/>.</summary>
    /// <param name="text">The title.</param>
    /// <returns>It, or its cut form.</returns>
    internal static string Cut(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length <= TabWidth)
        {
            return text;
        }

        var kept = TabWidth - 1;

        // Never half a surrogate pair: the character it begins goes with the cut.
        if (char.IsHighSurrogate(text[kept - 1]))
        {
            kept--;
        }

        return text[..kept] + "...";
    }
}

/// <summary>One VS Code window, the extension host that started the Claude Code of a relay's client.</summary>
/// <remarks>
/// <b>1.5 a, decided 2026-10-10</b>: VS Code tabs are grouped by their window's
/// extension host, the parent of every tab's Claude Code (21 of 21 in the measurement),
/// and the group is labelled by the window's folder. Two windows on one folder are two
/// groups with one label, which is what the person sees too.
/// </remarks>
/// <param name="Key">What tells one window from another: the extension host's pid and creation time, as the relay read them.</param>
/// <param name="Folder">The folder the window has open, which is the folder the relay runs in, or <see langword="null"/>.</param>
internal sealed record ClientWindow(string Key, string? Folder)
{
    /// <summary>What a page calls the window.</summary>
    /// <returns><i>VS Code window on &lt;folder&gt;</i>, or <i>VS Code window</i> when the folder is not known.</returns>
    public string Label() => ClientFolder.NameOf(Folder) is { } name ? $"VS Code window on {name}" : "VS Code window";
}

/// <summary>What BrowserAI calls a client's folder in a label.</summary>
internal static class ClientFolder
{
    /// <summary>The folder's own name, the way a window's title shows it.</summary>
    /// <param name="folder">The folder, or <see langword="null"/>.</param>
    /// <returns>Its last part; the whole of it for a drive's root; <see langword="null"/> when there is none.</returns>
    public static string? NameOf(string? folder)
    {
        if (folder is not { Length: > 0 })
        {
            return null;
        }

        var trimmed = folder.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);

        return name is { Length: > 0 } ? name : folder;
    }

    /// <summary>BrowserAI's words for a conversation it could not name: <i>&lt;what&gt; in &lt;folder&gt;</i>.</summary>
    /// <param name="what">What it is, such as <i>Claude Code</i> or <i>new conversation</i>.</param>
    /// <param name="folder">The client's folder, or <see langword="null"/>.</param>
    /// <returns>The name, never a title.</returns>
    public static ConversationName Unnamed(string what, string? folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        return new ConversationName(NameOf(folder) is { } name ? $"{what} in {name}" : what, IsTitle: false);
    }
}
