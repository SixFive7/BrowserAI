// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// Upstream's <c>browser_tabs</c> as a resumed Firefox session's child answers it: the
/// browser comes up on Playwright's blank page, and the tabs Firefox's session restore
/// reopens are listed once they are up.
/// </summary>
/// <remarks>
/// <para>
/// <b>Added 2026-10-13 with the maintainer's 161.</b> The child lists a tab once its page
/// has come up, and the blank page comes up first, so the first list a resume makes holds
/// the blank page alone and a later one the reopened tabs beside it, in the order their
/// pages came up. Every answer is the tab list as the action left it, as upstream's is.
/// </para>
/// <para>
/// <b>The rules are upstream's, read in <c>coreBundle.js</c> at
/// <c>playwright-core</c> 1.64.0-alpha-1790635538000</b>: a list adds nothing, a select
/// makes a tab current, and a close of the current tab makes the tab now at its index
/// current, or the last one (<c>Context._onPageClosed</c>). The line is
/// <c>renderTabsMarkdown</c>'s.
/// </para>
/// </remarks>
internal sealed class FakeFirefoxTabs
{
    private readonly Lock _gate = new();
    private readonly List<(string Title, string Url)> _tabs = [(string.Empty, "about:blank")];
    private readonly List<(string Title, string Url)> _reopened;
    private readonly int _listsBeforeTheRestore;
    private readonly List<string> _actions = [];
    private int _current;
    private int _lists;
    private bool _restored;

    /// <summary>Makes the double.</summary>
    /// <param name="reopened">The tabs Firefox reopens, in the order they come up.</param>
    /// <param name="listsBeforeTheRestore">How many lists hold the blank page alone before the reopened tabs are up.</param>
    public FakeFirefoxTabs(IReadOnlyList<(string Title, string Url)> reopened, int listsBeforeTheRestore = 1)
    {
        ArgumentNullException.ThrowIfNull(reopened);

        _reopened = [.. reopened];
        _listsBeforeTheRestore = listsBeforeTheRestore;
    }

    /// <summary>Every action asked of the tool, in order, with its index when it had one.</summary>
    public IReadOnlyList<string> Actions
    {
        get
        {
            lock (_gate)
            {
                return [.. _actions];
            }
        }
    }

    /// <summary>The tabs as they stand now, with the index of the current one.</summary>
    public (IReadOnlyList<(string Title, string Url)> Tabs, int Current) Now
    {
        get
        {
            lock (_gate)
            {
                return ([.. _tabs], _current);
            }
        }
    }

    /// <summary>The behaviour to install on a double's <c>browser_tabs</c>.</summary>
    /// <param name="holdUntil">Holds every call until it completes, or <see langword="null"/>.</param>
    /// <returns>The behaviour.</returns>
    public FakeToolBehaviour Behaviour(Task? holdUntil = null) => new()
    {
        RawResultFor = Answer,
        HoldUntil = holdUntil,
    };

    /// <summary>The literal result one call is answered with.</summary>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>The JSON of the result.</returns>
    public string Answer(JsonNode? arguments)
    {
        lock (_gate)
        {
            var action = arguments?["action"] is JsonValue named ? named.GetValue<string>() : "<none>";
            int? index = arguments?["index"] is JsonValue given ? given.GetValue<int>() : null;

            _actions.Add(index is { } at ? $"{action} {at.ToString(CultureInfo.InvariantCulture)}" : action);

            switch (action)
            {
                case "list":
                    _lists++;

                    if (!_restored && _lists > _listsBeforeTheRestore)
                    {
                        _tabs.AddRange(_reopened);
                        _restored = true;
                    }

                    break;

                case "select" when index is { } selected && selected < _tabs.Count:
                    _current = selected;
                    break;

                case "close":
                    var closed = index ?? _current;
                    _tabs.RemoveAt(closed);

                    if (_current == closed)
                    {
                        _current = Math.Min(closed, _tabs.Count - 1);
                    }
                    else if (_current > closed)
                    {
                        _current--;
                    }

                    break;

                default:
                    return Error($"Tab {index?.ToString(CultureInfo.InvariantCulture) ?? "<none>"} not found");
            }

            var text = new StringBuilder("### Result");

            for (var tab = 0; tab < _tabs.Count; tab++)
            {
                _ = text.Append("\n- ")
                    .Append(tab.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(tab == _current ? " (current)" : string.Empty)
                    .Append(" [")
                    .Append(_tabs[tab].Title)
                    .Append("](")
                    .Append(_tabs[tab].Url)
                    .Append(')');
            }

            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text.ToString() }),
            }.ToJsonString();
        }
    }

    /// <summary>An upstream tool error, as upstream answers one: a result with <c>isError</c>.</summary>
    /// <param name="message">What went wrong.</param>
    /// <returns>The JSON of the result.</returns>
    public static string Error(string message) =>
        new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"### Error\n{message}" }),
            ["isError"] = true,
        }.ToJsonString();
}
