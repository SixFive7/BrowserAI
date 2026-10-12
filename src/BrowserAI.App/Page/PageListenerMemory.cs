// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using BrowserAI.Runtime;

namespace BrowserAI.App.Page;

/// <summary>What the page's listener remembered from the background that ran before this one.</summary>
/// <param name="Port">The port it listened on, or <see langword="null"/> when nothing is remembered.</param>
/// <param name="Tab">The last tab number it handed out, zero when nothing is remembered.</param>
internal readonly record struct PageListenerRecall(int? Port, int Tab);

/// <summary>Where the page's listener keeps its port and its last tab number across the background's restarts.</summary>
/// <remarks>
/// <para>
/// <b>69 d with fallback b, the maintainer's words of 2026-10-11 verbatim: <i>"69 d with
/// fallback to b"</i>.</b> The listener stays up while the background runs, and a background
/// that starts again takes the port the last one listened on, when it is free. A browser
/// remembers its <i>Always allow</i> for opening BrowserAI per address, port included, so the
/// address has to stay the same for the person to be asked once.
/// </para>
/// <para>
/// <b>The last tab number is kept beside the port</b>, so a background that starts again goes
/// on numbering where the last one stopped. A tab is replaced by a newer one (Q337 a), and a tab
/// a person kept open from before the restart reaches the same address now: numbered from one
/// again, the newest tab would have been replaced by that old one.
/// </para>
/// </remarks>
internal interface IPageListenerMemory
{
    /// <summary>Reads what was remembered.</summary>
    /// <returns>The port and the last tab number, or nothing.</returns>
    Task<PageListenerRecall> ReadAsync();

    /// <summary>Remembers the port the listener is on and the last tab number it handed out.</summary>
    /// <param name="port">The port.</param>
    /// <param name="tab">The last tab number.</param>
    /// <returns>The write.</returns>
    Task WriteAsync(int port, int tab);
}

/// <summary>A memory that lasts as long as the process: what a page with nowhere to write it remembers.</summary>
internal sealed class PageListenerMemoryInProcess : IPageListenerMemory
{
    private readonly Lock _gate = new();
    private PageListenerRecall _recall = new(null, 0);

    /// <inheritdoc />
    public Task<PageListenerRecall> ReadAsync()
    {
        lock (_gate)
        {
            return Task.FromResult(_recall);
        }
    }

    /// <inheritdoc />
    public Task WriteAsync(int port, int tab)
    {
        lock (_gate)
        {
            _recall = new PageListenerRecall(port, tab);
        }

        return Task.CompletedTask;
    }
}

/// <summary>The memory as two lines in a file under the data root.</summary>
/// <remarks>
/// <b>Under the data root and never the install root</b>, for the reason the update check's
/// record is there (<c>UpdateCheckStampFile</c>): an update replaces the install root's
/// <c>current\</c>, and the address has to outlive exactly that restart. A file that does not
/// hold what it should is nothing remembered, and the listener takes a port Windows picks.
/// </remarks>
/// <param name="path">The file.</param>
internal sealed class PageListenerMemoryFile(string path) : IPageListenerMemory
{
    /// <summary>The file's name, directly under the data root.</summary>
    public const string FileName = "page-listener.txt";

    /// <summary>The memory for one data root.</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <returns>The memory.</returns>
    public static PageListenerMemoryFile In(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        return new PageListenerMemoryFile(Path.Combine(dataRoot, FileName));
    }

    /// <summary>Where the file is.</summary>
    public string Where => path;

    /// <inheritdoc />
    public async Task<PageListenerRecall> ReadAsync()
    {
        string text;

        try
        {
            text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new PageListenerRecall(null, 0);
        }

        int? port = null;
        var tab = 0;

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2);

            if (parts.Length is not 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            switch (parts[0])
            {
                case "port" when number is > 0 and <= 65535:
                    port = number;
                    break;

                case "tab":
                    tab = number;
                    break;
            }
        }

        return new PageListenerRecall(port, tab);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Written beside itself and moved into place, so a reader never meets half of it; one
    /// background serves a data root at a time, so the temporary name is never contended.
    /// </remarks>
    public async Task WriteAsync(int port, int tab)
    {
        var temporary = path + ".partial";

        await File.WriteAllTextAsync(temporary, string.Create(CultureInfo.InvariantCulture, $"port {port}\ntab {tab}\n")).ConfigureAwait(false);
        await FileSystemThread.MoveFileAsync(temporary, path, overwrite: true).ConfigureAwait(false);
    }
}
