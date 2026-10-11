// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using BrowserAI.Interop;

namespace BrowserAI.Updates;

/// <summary>
/// <see cref="IToastSurface"/> over Windows' own notification platform, under one
/// application id.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every call runs on a thread of its own, in the multi-threaded apartment</b>,
/// which joins the apartment for the one call and leaves it after, and the caller awaits
/// it: Windows' notification calls are synchronous calls into the notification platform
/// with no asynchronous form, so a call that hangs holds that thread and never a pool
/// worker. Calls take their turn in order, which keeps the sequence numbers in the order
/// they were given.
/// </para>
/// <para>
/// ⚠️ <b>Corrected 2026-10-11 (previously "Every call runs on one thread of its own ...
/// joined once when the thread starts", a thread that parked on a queue between calls,
/// and callers that parked on each call's task)</b>, the maintainer's decision 54, in his
/// words verbatim: <i>"Never block a worker anywhere."</i> The apartment is kept alive
/// between calls by <see cref="ToastInterop.CoIncrementMTAUsage"/>, so the toast objects
/// this surface keeps stay valid from one call's thread to the next.
/// </para>
/// <para>
/// <b>A call that Windows has not answered within <see cref="CallBound"/> is a
/// <see cref="TimeoutException"/></b>, which <see cref="UpdateToasts"/> records and
/// goes on from. It is a hang detector: every one of the 60 updates measured on
/// 2026-10-08 was answered within 5 ms.
/// </para>
/// <para>
/// ⚠️ <b>The suite never constructs this type</b> (Q278): it is banned in the tests'
/// <c>BannedSymbols.txt</c>, and every arm hands <see cref="UpdateToasts"/> a
/// recording surface. Nothing in the suite shows a toast.
/// </para>
/// </remarks>
internal sealed class WindowsToastSurface : IToastSurface, IDisposable
{
    /// <summary>How long a caller waits for Windows to answer one call: <b>10 s</b>.</summary>
    public static TimeSpan CallBound { get; } = UpdateBudgets.ToastCallBound;

    /// <summary><c>ERROR_NOT_FOUND</c> as an <c>HRESULT</c>, which a removal of a toast that is not there may answer.</summary>
    private const int NotFound = unchecked((int)0x80070490);

    private static readonly StrategyBasedComWrappers Wrappers = new();

    private readonly string _appUserModelId;
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly nint _apartment;
    private readonly bool _counted;

    private IToastNotifier? _notifier;
    private IToastNotificationHistory? _history;
    private IToastNotificationFactory? _factory;

    /// <summary>A surface for one application id.</summary>
    /// <param name="appUserModelId">The id every toast is raised under.</param>
    public WindowsToastSurface(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        _appUserModelId = appUserModelId;

        // The apartment stays for the surface's life, with no thread in it, so the objects
        // one call's thread made are the next call's to use.
        _counted = ToastInterop.CoIncrementMTAUsage(out _apartment) >= 0;
    }

    /// <summary>
    /// The surface for the application id this process runs under, or
    /// <see langword="null"/> when it runs under none.
    /// </summary>
    /// <remarks>
    /// <b>Velopack sets the id in every installed process</b>, and only there
    /// (measured 2026-09-24, [kb](../../../kb/windows/notifications.md#the-app-id-is-already-set-by-velopack-on-the-shortcut-and-in-every-installed-process)), so a build that is
    /// not installed raises no toast: a toast under no id would wear somebody else's.
    /// </remarks>
    /// <returns>The surface.</returns>
    public static WindowsToastSurface? ForThisProcess() =>
        ToastInterop.CurrentAppUserModelId() is { } id ? new WindowsToastSurface(id) : null;

    /// <inheritdoc />
    public async Task ShowAsync(string tag, string group, ToastRequest toast, uint sequence)
    {
        ArgumentNullException.ThrowIfNull(toast);

        _ = await RunAsync(() =>
        {
            var notification = Compose(Factory(), tag, group, toast, sequence);

            Notifier().Show(notification);
            return true;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ToastUpdateResult> UpdateAsync(string tag, string group, IReadOnlyDictionary<string, string> values, uint sequence)
    {
        ArgumentNullException.ThrowIfNull(values);

        return RunAsync(() =>
        {
            using var hTag = new HString(tag);
            using var hGroup = new HString(group);

            return (ToastUpdateResult)((IToastNotifier2)Notifier()).UpdateWithTagAndGroup(Data(values, sequence), hTag.Handle, hGroup.Handle);
        });
    }

    /// <inheritdoc />
    public Task RemoveAsync(string tag, string group) =>
        RunAsync(() =>
        {
            using var hTag = new HString(tag);
            using var hGroup = new HString(group);
            using var hId = new HString(_appUserModelId);

            try
            {
                History().RemoveGroupedTagWithId(hTag.Handle, hGroup.Handle, hId.Handle);
            }
            catch (COMException failure) when (failure.HResult == NotFound)
            {
                // Nothing under that tag: what a removal is for has already happened.
            }

            return true;
        });

    /// <summary>Lets the apartment go. A call still on its thread finishes on its own.</summary>
    public void Dispose()
    {
        if (_counted)
        {
            _ = ToastInterop.CoDecrementMTAUsage(_apartment);
        }

        _turn.Dispose();
    }

    /// <summary>
    /// Builds a toast Windows can show, and shows nothing: its content parsed, its
    /// tag, group, banner and restart behaviour set, and its first values bound.
    /// </summary>
    /// <remarks>
    /// <b>The whole of a show except the show</b>, so the suite builds a real toast
    /// object through every interface it uses and reads each setting back, which is
    /// the one check that a slot is the slot Windows calls it, and raises nothing.
    /// </remarks>
    /// <param name="factory">The toast factory.</param>
    /// <param name="tag">Its tag.</param>
    /// <param name="group">Its group.</param>
    /// <param name="toast">What it shows.</param>
    /// <param name="sequence">The sequence number of its first values.</param>
    /// <returns>The toast.</returns>
    internal static IToastNotification Compose(IToastNotificationFactory factory, string tag, string group, ToastRequest toast, uint sequence)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(toast);

        var notification = factory.CreateToastNotification(Parse(toast.Xml));

        using (var hTag = new HString(tag))
        using (var hGroup = new HString(group))
        {
            var second = (IToastNotification2)notification;

            second.PutTag(hTag.Handle);
            second.PutGroup(hGroup.Handle);

            if (toast.SuppressPopup)
            {
                second.PutSuppressPopup(1);
            }
        }

        if (toast.ExpiresOnReboot)
        {
            ((IToastNotification6)notification).PutExpiresOnReboot(1);
        }

        if (toast.Data is { } values)
        {
            ((IToastNotification4)notification).PutData(Data(values, sequence));
        }

        return notification;
    }

    /// <summary>The toast factory, made on the calling thread.</summary>
    /// <returns>The factory.</returns>
    internal static IToastNotificationFactory NewFactory() =>
        Statics<IToastNotificationFactory>(ToastInterop.ToastNotificationClass, ToastInterop.ToastNotificationFactoryId);

    /// <summary>Activates a runtime class and wraps what it returns.</summary>
    /// <typeparam name="T">The interface wanted.</typeparam>
    /// <param name="runtimeClass">The class.</param>
    /// <returns>The instance, as that interface.</returns>
    private static T Activate<T>(string runtimeClass)
        where T : class
    {
        using var name = new HString(runtimeClass);

        Marshal.ThrowExceptionForHR(ToastInterop.RoActivateInstance(name.Handle, out var instance));

        return Wrap<T>(instance);
    }

    /// <summary>One of a runtime class's statics or factory interfaces.</summary>
    /// <typeparam name="T">The interface wanted.</typeparam>
    /// <param name="runtimeClass">The class.</param>
    /// <param name="interfaceId">The interface's id, which is <typeparamref name="T"/>'s.</param>
    /// <returns>The interface.</returns>
    private static T Statics<T>(string runtimeClass, Guid interfaceId)
        where T : class
    {
        using var name = new HString(runtimeClass);

        Marshal.ThrowExceptionForHR(ToastInterop.RoGetActivationFactory(name.Handle, interfaceId, out var factory));

        return Wrap<T>(factory);
    }

    /// <summary>Wraps a pointer this method then lets go of; the wrapper keeps its own reference.</summary>
    private static T Wrap<T>(nint pointer)
        where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            _ = Marshal.Release(pointer);
        }
    }

    /// <summary>Parses a toast's XML into the document Windows reads it from.</summary>
    private static IXmlDocument Parse(string xml)
    {
        var document = Activate<IXmlDocumentIO>(ToastInterop.XmlDocumentClass);

        using (var text = new HString(xml))
        {
            document.LoadXml(text.Handle);
        }

        return (IXmlDocument)document;
    }

    /// <summary>One set of bound values with its sequence number.</summary>
    private static INotificationData Data(IReadOnlyDictionary<string, string> values, uint sequence)
    {
        var data = Activate<INotificationData>(ToastInterop.NotificationDataClass);
        var map = data.GetValues();

        foreach (var (name, value) in values)
        {
            using var key = new HString(name);
            using var text = new HString(value);

            _ = map.Insert(key.Handle, text.Handle);
        }

        data.PutSequenceNumber(sequence);
        return data;
    }

    private IToastNotificationFactory Factory() => _factory ??= NewFactory();

    private IToastNotifier Notifier()
    {
        if (_notifier is null)
        {
            using var id = new HString(_appUserModelId);

            _notifier = Statics<IToastNotificationManagerStatics>(ToastInterop.ToastNotificationManagerClass, ToastInterop.ToastNotificationManagerStaticsId).CreateToastNotifierWithId(id.Handle);
        }

        return _notifier;
    }

    private IToastNotificationHistory History() =>
        _history ??= Statics<IToastNotificationManagerStatics2>(ToastInterop.ToastNotificationManagerClass, ToastInterop.ToastNotificationManagerStatics2Id).GetHistory();

    /// <summary>Runs one call on the toast thread and waits for its answer, within <see cref="CallBound"/>.</summary>
    /// <summary>Runs one call on a thread in the apartment, in its turn, and awaits it.</summary>
    /// <typeparam name="T">What the call answers.</typeparam>
    /// <param name="call">The call.</param>
    /// <returns>What it answered.</returns>
    /// <exception cref="TimeoutException">Windows did not answer within <see cref="CallBound"/>.</exception>
    private async Task<T> RunAsync<T>(Func<T> call)
    {
        await _turn.WaitAsync().ConfigureAwait(false);

        try
        {
            var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                var joined = TaskSchedulerInterop.CoInitializeEx(0, TaskSchedulerInterop.MultiThreaded);

                try
                {
                    _ = answer.TrySetResult(call());
                }
#pragma warning disable CA1031 // Handed to the caller, who records it.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    _ = answer.TrySetException(failure);
                }
                finally
                {
                    // Only a join that took leaves; a changed-mode answer joined nothing.
                    if (joined >= 0)
                    {
                        TaskSchedulerInterop.CoUninitialize();
                    }
                }
            })
            {
                IsBackground = true,
                Name = "BrowserAI toasts",
            };

            thread.Start();

            try
            {
                return await answer.Task.WaitAsync(CallBound).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"Windows did not answer a toast call within {CallBound.TotalSeconds:F0} s."));
            }
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>Gives the next call its turn.</summary>
    private void Leave()
    {
        try
        {
            _ = _turn.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposed while this call was in Windows: the call has finished, and no call
            // is left to take a turn.
        }
    }
}
