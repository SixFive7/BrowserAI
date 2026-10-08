// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Collections.Concurrent;
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
/// <b>Every call runs on one thread of its own, in the multi-threaded
/// apartment</b>, joined once when the thread starts: the background calls in from
/// its own threads and from a timer, and the toast objects are created and used on
/// a thread whose apartment this type chose. Calls are queued in order, which keeps
/// the sequence numbers in the order they were given.
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
    public static TimeSpan CallBound { get; } = TimeSpan.FromSeconds(10);

    /// <summary><c>ERROR_NOT_FOUND</c> as an <c>HRESULT</c>, which a removal of a toast that is not there may answer.</summary>
    private const int NotFound = unchecked((int)0x80070490);

    private static readonly StrategyBasedComWrappers Wrappers = new();

    private readonly string _appUserModelId;
    private readonly BlockingCollection<Action> _work = [];
    private readonly Lock _gate = new();

    private Thread? _thread;
    private IToastNotifier? _notifier;
    private IToastNotificationHistory? _history;
    private IToastNotificationFactory? _factory;

    /// <summary>A surface for one application id.</summary>
    /// <param name="appUserModelId">The id every toast is raised under.</param>
    public WindowsToastSurface(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        _appUserModelId = appUserModelId;
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
    public void Show(string tag, string group, ToastRequest toast, uint sequence)
    {
        ArgumentNullException.ThrowIfNull(toast);

        _ = Run(() =>
        {
            var notification = Compose(Factory(), tag, group, toast, sequence);

            Notifier().Show(notification);
            return true;
        });
    }

    /// <inheritdoc />
    public ToastUpdateResult Update(string tag, string group, IReadOnlyDictionary<string, string> values, uint sequence)
    {
        ArgumentNullException.ThrowIfNull(values);

        return Run(() =>
        {
            using var hTag = new HString(tag);
            using var hGroup = new HString(group);

            return (ToastUpdateResult)((IToastNotifier2)Notifier()).UpdateWithTagAndGroup(Data(values, sequence), hTag.Handle, hGroup.Handle);
        });
    }

    /// <inheritdoc />
    public void Remove(string tag, string group) =>
        _ = Run(() =>
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

    /// <summary>Ends the thread once the calls queued before this one have run.</summary>
    public void Dispose()
    {
        Thread? thread;

        lock (_gate)
        {
            thread = _thread;
            _work.CompleteAdding();
        }

        // A thread still inside a call it was given keeps the queue; one that left
        // has drained it, and the queue goes with it.
        if (thread is null || thread.Join(CallBound))
        {
            _work.Dispose();
        }
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
    private T Run<T>(Func<T> call)
    {
        var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _thread ??= Start();
            _work.Add(() =>
            {
                try
                {
                    answer.SetResult(call());
                }
#pragma warning disable CA1031 // Handed to the caller, who records it.
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    answer.SetException(failure);
                }
            });
        }

        return answer.Task.Wait(CallBound)
            ? answer.Task.GetAwaiter().GetResult()
            : throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"Windows did not answer a toast call within {CallBound.TotalSeconds:F0} s."));
    }

    private Thread Start()
    {
        var thread = new Thread(() =>
        {
            var joined = TaskSchedulerInterop.CoInitializeEx(0, TaskSchedulerInterop.MultiThreaded);

            try
            {
                foreach (var call in _work.GetConsumingEnumerable())
                {
                    call();
                }
            }
            finally
            {
                _notifier = null;
                _history = null;
                _factory = null;

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
        return thread;
    }
}
