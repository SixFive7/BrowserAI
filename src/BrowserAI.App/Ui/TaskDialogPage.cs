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
    private readonly Func<ClickOutcome> _onTick;
    private readonly Action<Exception> _onFailure;
    private readonly List<nint> _allocated = [];

    private GCHandle _self;

    /// <summary>Creates a host for one dialog.</summary>
    /// <param name="page">Produces the page to show, called again on every re-render.</param>
    /// <param name="onCommand">What a command link does. Never called for the close button.</param>
    /// <param name="onLink">What a hyperlink does.</param>
    /// <param name="onTick">
    /// Asked roughly every 200 ms while the dialog is up.
    /// <b>It must return immediately</b> — it runs on the same callback as every
    /// click, so anything it waits for is a frozen window.
    /// </param>
    /// <param name="onFailure">
    /// What to do with an exception out of one of the four above.
    /// <b>It is expected to record the failure where a person will meet it</b> —
    /// the process log, and the note the next page carries — because this host
    /// re-renders straight afterwards and shows whatever that produced.
    /// </param>
    public TaskDialogHost(
        Func<TaskDialogPage> page,
        Func<int, ClickOutcome> onCommand,
        Action<string> onLink,
        Func<ClickOutcome> onTick,
        Action<Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(onCommand);
        ArgumentNullException.ThrowIfNull(onLink);
        ArgumentNullException.ThrowIfNull(onTick);
        ArgumentNullException.ThrowIfNull(onFailure);

        _page = page;
        _onCommand = onCommand;
        _onLink = onLink;
        _onTick = onTick;
        _onFailure = onFailure;
    }

    /// <summary>
    /// The dialog's window while it is up, and zero otherwise.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A modal child needs an owner and this is the only place that has
    /// one.</b> A picker opened with a zero owner is not modal to this dialog:
    /// the dialog's command links stay live underneath it, so a second click
    /// re-enters the command handler and can navigate the page while a modal
    /// child is on top of it. <i>Exposed 2026-09-16</i>, when the picker was
    /// found to be passing zero because this was private.
    /// </remarks>
    public nint Window { get; private set; }

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
            Window = 0;

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
        // ⚠️ THE PAGE IS PRODUCED BEFORE THE WINDOW IS CHECKED, deliberately.
        // The factory is the caller's code and it is one of the three things
        // that can throw into the dialog's callback, so it has to be REACHABLE
        // -- and therefore assertable -- whether or not a window is up. Nothing
        // is allocated until Build runs, so the windowless path costs one
        // discarded record and no native memory at all.
        var page = _page();

        if (Window is 0)
        {
            return;
        }

        // ⚠️ The strings of the OUTGOING page are freed only after the new page
        // has been handed over: Windows reads the incoming structure during the
        // message and the outgoing one is still on screen until it returns.
        var outgoing = _allocated.ToArray();
        _allocated.Clear();

        var config = Build(page);

        config.Callback = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint, int>)&Callback;
        config.CallbackData = GCHandle.ToIntPtr(_self);

        var buffer = Marshal.AllocHGlobal(TaskDialogInterop.ConfigSize);

        try
        {
            Marshal.StructureToPtr(config, buffer, fDeleteOld: false);
            _ = TaskDialogInterop.SendMessageW(Window, TaskDialogInterop.Message.NavigatePage, 0, buffer);
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

        if (Window is 0)
        {
            return;
        }

        var text = Marshal.StringToHGlobalUni(content);

        try
        {
            _ = TaskDialogInterop.SendMessageW(
                Window, TaskDialogInterop.Message.SetElementText, TaskDialogInterop.Element.Content, text);
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
                | TaskDialogInterop.Flags.CallbackTimer
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
    /// <remarks>
    /// <b>It resolves the instance and forwards; every decision is in
    /// <see cref="Dispatch"/>.</b> The split is what makes the callback path
    /// assertable without a window — an <c>[UnmanagedCallersOnly]</c> method
    /// cannot be called from managed code at all, so a dispatch written inside
    /// this one is a dispatch nothing can exercise until Windows exercises it.
    /// </remarks>
    [UnmanagedCallersOnly]
    private static int Callback(nint window, uint notification, nint wParam, nint lParam, nint reference)
    {
        if (reference is 0 || GCHandle.FromIntPtr(reference).Target is not TaskDialogHost host)
        {
            return TaskDialogInterop.Ok;
        }

        return host.Dispatch(window, notification, wParam, lParam);
    }

    /// <summary>
    /// One notification, decided.
    /// </summary>
    /// <param name="window">The dialog's window, as Windows reports it.</param>
    /// <param name="notification">Which notification this is.</param>
    /// <param name="wParam">Its first argument.</param>
    /// <param name="lParam">Its second argument.</param>
    /// <returns>What the notification requires: <c>S_OK</c> or <c>S_FALSE</c>.</returns>
    internal int Dispatch(nint window, uint notification, nint wParam, nint lParam)
    {
        switch (notification)
        {
            case TaskDialogInterop.Notification.Created:
                Window = window;
                return TaskDialogInterop.Ok;

            case TaskDialogInterop.Notification.Timer:
                ClickOutcome tick;

                try
                {
                    tick = _onTick();
                }
#pragma warning disable CA1031 // Same boundary as every other notification: a throw here is a FailFast.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    return Failed(failure, TaskDialogInterop.Ok);
                }

                if (tick is ClickOutcome.Rerender)
                {
                    try
                    {
                        Rerender();
                    }
#pragma warning disable CA1031 // The PAGE FACTORY, again.
                    catch (Exception failure)
#pragma warning restore CA1031
                    {
                        return Failed(failure, TaskDialogInterop.Ok);
                    }
                }

                // S_OK, so the tick count is not reset. Nothing here depends on
                // it, and S_FALSE would make the interval mean something else.
                return TaskDialogInterop.Ok;

            case TaskDialogInterop.Notification.HyperlinkClicked:
                try
                {
                    _onLink(Marshal.PtrToStringUni(lParam) ?? string.Empty);
                }
#pragma warning disable CA1031 // The reverse P/Invoke boundary -- see Failed. Anything at all is better here than terminating the process.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    return Failed(failure, TaskDialogInterop.Ok);
                }

                return TaskDialogInterop.Ok;

            case TaskDialogInterop.Notification.ButtonClicked:
                var id = (int)wParam;

                if (id is TaskDialogInterop.ButtonId.Close
                    or TaskDialogInterop.ButtonId.Cancel
                    or TaskDialogInterop.ButtonId.Ok)
                {
                    return TaskDialogInterop.Ok;
                }

                ClickOutcome outcome;

                try
                {
                    outcome = _onCommand(id);
                }
#pragma warning disable CA1031 // Same boundary.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    // S_FALSE: the dialog stays open, which is what lets the
                    // person read what went wrong.
                    return Failed(failure, TaskDialogInterop.False);
                }

                switch (outcome)
                {
                    case ClickOutcome.Close:
                        return TaskDialogInterop.Ok;

                    case ClickOutcome.Rerender:
                        try
                        {
                            Rerender();
                        }
#pragma warning disable CA1031 // Same boundary. This is the PAGE FACTORY throwing.
                        catch (Exception failure)
#pragma warning restore CA1031
                        {
                            return Failed(failure, TaskDialogInterop.False);
                        }

                        return TaskDialogInterop.False;

                    default:
                        return TaskDialogInterop.False;
                }

            default:
                return TaskDialogInterop.Ok;
        }
    }

    /// <summary>
    /// Records a failure that would otherwise have crossed the unmanaged
    /// boundary, and puts it where the person clicking can read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>An exception out of an <c>[UnmanagedCallersOnly]</c> method is a
    /// <c>FailFast</c>.</b> The runtime cannot unwind into native frames, so the
    /// process is terminated where it stands: the window vanishes mid-click with
    /// no dialog, no log line and no exit code anything could read. Three calls
    /// reachable from a click could produce one today —
    /// <c>Directory.CreateDirectory</c> for the log directory,
    /// <c>Path.Combine</c> outside the registry reader's own <c>try</c> when
    /// <c>CLAUDE_CONFIG_DIR</c> holds an invalid path, and
    /// <c>Path.GetFullPath</c> on a project directory. <i>Added 2026-09-16.</i>
    /// </para>
    /// <para>
    /// <b>Reported once, then shown once.</b> The reporter belongs to the caller
    /// — it writes the process log and sets the note the next page carries — and
    /// the re-render that follows is what puts that note on screen. If the
    /// re-render <i>itself</i> throws then the page factory is the broken thing,
    /// so the content is replaced outright with the message instead; that path
    /// reports nothing further, because a reporter called twice for one click
    /// reads as two failures.
    /// </para>
    /// <para>
    /// <b>Nothing in here may throw</b>, the reporter included: this is the last
    /// managed frame before native code.
    /// </para>
    /// </remarks>
    /// <param name="failure">What was thrown.</param>
    /// <param name="result">What the notification requires this callback to return.</param>
    /// <returns><paramref name="result"/>.</returns>
    private int Failed(Exception failure, int result)
    {
        try
        {
            _onFailure(failure);
        }
#pragma warning disable CA1031 // A reporter that throws must not be the thing that terminates the process.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        try
        {
            Rerender();
        }
#pragma warning disable CA1031 // The page factory is what threw. Fall back to the content alone.
        catch (Exception)
#pragma warning restore CA1031
        {
            try
            {
                SetContent($"Something went wrong and BrowserAI has changed nothing: {failure.Message}");
            }
#pragma warning disable CA1031 // Last resort: there is nowhere left to say this.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        return result;
    }
}
