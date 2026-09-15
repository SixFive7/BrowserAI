// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using BrowserAI.App.Interop;

namespace BrowserAI.App.Ui;

/// <summary>One page of the dialog, as managed values.</summary>
/// <remarks>
/// Separate from the marshalling on purpose: everything in here is assertable
/// without a window, and the suite asserts it. What cannot be asserted without a
/// window — that Windows renders it — is the part that is kept as small as
/// possible.
/// </remarks>
internal sealed record TaskDialogPage
{
    /// <summary>The window title.</summary>
    public required string Title { get; init; }

    /// <summary>The large heading.</summary>
    public required string Instruction { get; init; }

    /// <summary>The body, which may carry <c>&lt;a href="…"&gt;</c>.</summary>
    public required string Content { get; init; }

    /// <summary>The footer, which may carry <c>&lt;a href="…"&gt;</c>.</summary>
    public string? Footer { get; init; }

    /// <summary>The command links, in order.</summary>
    public IReadOnlyList<TaskDialogCommand> Commands { get; init; } = [];
}

/// <summary>One command link.</summary>
/// <param name="Id">What the click reports. Never in the <c>IDOK</c> range.</param>
/// <param name="Text">
/// The label, and after a newline the smaller explanatory line beneath it.
/// </param>
internal readonly record struct TaskDialogCommand(int Id, string Text);

/// <summary>
/// What a click asked for, and what the dialog does next.
/// </summary>
internal enum ClickOutcome
{
    /// <summary>Stay open, unchanged.</summary>
    Stay,

    /// <summary>Stay open and re-render, because the state changed.</summary>
    Rerender,

    /// <summary>Close.</summary>
    Close,
}

/// <summary>
/// Shows a task dialog, keeps it open across clicks, and re-renders it when what
/// it is showing has changed.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Every string handed to Windows is allocated here and freed here</b>, and
/// the lifetime is the reason this is a class rather than a method: a page that
/// is navigated to must stay valid for the whole time the dialog is showing it,
/// and a local that went out of scope at the end of the callback would be a
/// use-after-free that renders correctly most of the time.
/// </para>
/// <para>
/// <b>One dialog at a time, on one thread</b>, which is what makes the static
/// callback safe. The handle in <c>lpCallbackData</c> is what carries the
/// instance across the native boundary — never a static field, because a static
/// field is exactly the thing a second dialog would quietly share.
/// </para>
/// </remarks>
internal sealed class TaskDialogHost : IDisposable
{
    private readonly Func<TaskDialogPage> _page;
    private readonly Func<int, ClickOutcome> _onCommand;
    private readonly Action<string> _onLink;
    private readonly List<nint> _allocated = [];

    private GCHandle _self;
    private nint _window;

    /// <summary>Creates a host for one dialog.</summary>
    /// <param name="page">Produces the page to show, called again on every re-render.</param>
    /// <param name="onCommand">What a command link does. Never called for the close button.</param>
    /// <param name="onLink">What a hyperlink does.</param>
    public TaskDialogHost(Func<TaskDialogPage> page, Func<int, ClickOutcome> onCommand, Action<string> onLink)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(onCommand);
        ArgumentNullException.ThrowIfNull(onLink);

        _page = page;
        _onCommand = onCommand;
        _onLink = onLink;
    }

    /// <summary>Shows the dialog and returns when it closes.</summary>
    /// <returns>The <c>HRESULT</c> the entry point answered.</returns>
    public unsafe int Show()
    {
        _self = GCHandle.Alloc(this, GCHandleType.Normal);

        try
        {
            var config = Build(_page());

            config.Callback = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint, int>)&Callback;
            config.CallbackData = GCHandle.ToIntPtr(_self);

            return TaskDialogInterop.TaskDialogIndirect(ref config, out _, out _, out _);
        }
        finally
        {
            _window = 0;

            if (_self.IsAllocated)
            {
                _self.Free();
            }

            Release();
        }
    }

    /// <summary>Replaces the whole page with what <c>page</c> now produces.</summary>
    /// <remarks>
    /// <b><c>TDM_NAVIGATE_PAGE</c> rather than setting texts one at a time</b>,
    /// because the buttons change with the state: <i>Register</i> becomes
    /// <i>Unregister</i>, and a task dialog cannot relabel a button that already
    /// exists. Navigating is the documented way to change the set.
    /// </remarks>
    public unsafe void Rerender()
    {
        if (_window is 0)
        {
            return;
        }

        // ⚠️ The strings of the OUTGOING page are freed only after the new page
        // has been handed over: Windows reads the incoming structure during the
        // message and the outgoing one is still on screen until it returns.
        var outgoing = _allocated.ToArray();
        _allocated.Clear();

        var config = Build(_page());

        config.Callback = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint, int>)&Callback;
        config.CallbackData = GCHandle.ToIntPtr(_self);

        var buffer = Marshal.AllocHGlobal(TaskDialogInterop.ConfigSize);

        try
        {
            Marshal.StructureToPtr(config, buffer, fDeleteOld: false);
            _ = TaskDialogInterop.SendMessageW(_window, TaskDialogInterop.Message.NavigatePage, 0, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);

            foreach (var pointer in outgoing)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    /// <summary>Replaces the content text without changing anything else.</summary>
    /// <param name="content">The new body.</param>
    public void SetContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (_window is 0)
        {
            return;
        }

        var text = Marshal.StringToHGlobalUni(content);

        try
        {
            _ = TaskDialogInterop.SendMessageW(
                _window, TaskDialogInterop.Message.SetElementText, TaskDialogInterop.Element.Content, text);
        }
        finally
        {
            // The dialog copies the string during the message, so this is freed
            // rather than tracked. Documented behaviour of TDM_SET_ELEMENT_TEXT.
            Marshal.FreeHGlobal(text);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_self.IsAllocated)
        {
            _self.Free();
        }

        Release();
    }

    private TaskDialogInterop.TaskDialogConfig Build(TaskDialogPage page)
    {
        var config = new TaskDialogInterop.TaskDialogConfig
        {
            Size = TaskDialogInterop.ConfigSize,
            Instance = TaskDialogInterop.GetModuleHandleW(0),
            Flags = (uint)(TaskDialogInterop.Flags.EnableHyperlinks
                | TaskDialogInterop.Flags.UseCommandLinks
                | TaskDialogInterop.Flags.AllowDialogCancellation
                | TaskDialogInterop.Flags.SizeToContent
                | TaskDialogInterop.Flags.UseHIconMain),
            CommonButtons = (uint)TaskDialogInterop.CommonButton.Close,
            WindowTitle = Keep(page.Title),
            MainInstruction = Keep(page.Instruction),
            Content = Keep(page.Content),
            Footer = page.Footer is null ? 0 : Keep(page.Footer),
            MainIcon = OurIcon(),

            // ⚠️ THE CLOSE BUTTON IS THE DEFAULT, on every page, deliberately.
            // Enter is the key a person presses without reading, and every other
            // button on this dialog edits a file.
            DefaultButton = TaskDialogInterop.ButtonId.Close,
        };

        if (page.Commands.Count is 0)
        {
            return config;
        }

        var buttons = Marshal.AllocHGlobal(TaskDialogInterop.ButtonSize * page.Commands.Count);
        _allocated.Add(buttons);

        for (var index = 0; index < page.Commands.Count; index++)
        {
            var button = new TaskDialogInterop.TaskDialogButton
            {
                Id = page.Commands[index].Id,
                Text = Keep(page.Commands[index].Text),
            };

            Marshal.StructureToPtr(button, buttons + (index * TaskDialogInterop.ButtonSize), fDeleteOld: false);
        }

        config.Buttons = buttons;
        config.ButtonCount = (uint)page.Commands.Count;

        return config;
    }

    /// <summary>
    /// This application's own icon, falling back to the system one.
    /// </summary>
    /// <remarks>
    /// <b>The fallback is not decoration.</b> If the SDK ever stopped writing
    /// the icon group under the id below, the load would answer zero and the
    /// dialog would render with the <i>no icon at all</i> layout — a different
    /// shape, silently. A stock icon is a visible wrong rather than an invisible
    /// one.
    /// </remarks>
    private static nint OurIcon()
    {
        var ours = TaskDialogInterop.LoadIconW(
            TaskDialogInterop.GetModuleHandleW(0), TaskDialogInterop.ApplicationIconResource);

        return ours is not 0
            ? ours
            : TaskDialogInterop.LoadIconW(0, TaskDialogInterop.ApplicationIconResource);
    }

    private nint Keep(string text)
    {
        var pointer = Marshal.StringToHGlobalUni(text);
        _allocated.Add(pointer);
        return pointer;
    }

    private void Release()
    {
        foreach (var pointer in _allocated)
        {
            Marshal.FreeHGlobal(pointer);
        }

        _allocated.Clear();
    }

    /// <summary>
    /// What Windows calls. Blittable parameters only: the attribute forbids
    /// marshalling, so a <see cref="bool"/> anywhere here is a runtime failure
    /// rather than a compile error.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int Callback(nint window, uint notification, nint wParam, nint lParam, nint reference)
    {
        if (reference is 0 || GCHandle.FromIntPtr(reference).Target is not TaskDialogHost host)
        {
            return TaskDialogInterop.Ok;
        }

        switch (notification)
        {
            case TaskDialogInterop.Notification.Created:
                host._window = window;
                return TaskDialogInterop.Ok;

            case TaskDialogInterop.Notification.HyperlinkClicked:
                host._onLink(Marshal.PtrToStringUni(lParam) ?? string.Empty);
                return TaskDialogInterop.Ok;

            case TaskDialogInterop.Notification.ButtonClicked:
                var id = (int)wParam;

                if (id is TaskDialogInterop.ButtonId.Close
                    or TaskDialogInterop.ButtonId.Cancel
                    or TaskDialogInterop.ButtonId.Ok)
                {
                    return TaskDialogInterop.Ok;
                }

                switch (host._onCommand(id))
                {
                    case ClickOutcome.Close:
                        return TaskDialogInterop.Ok;

                    case ClickOutcome.Rerender:
                        host.Rerender();
                        return TaskDialogInterop.False;

                    default:
                        return TaskDialogInterop.False;
                }

            default:
                return TaskDialogInterop.Ok;
        }
    }
}
