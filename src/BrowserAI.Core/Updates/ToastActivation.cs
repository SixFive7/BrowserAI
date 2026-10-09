// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using System.Text;
using BrowserAI.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BrowserAI.Updates;

/// <summary>
/// The toast activator's registration: the COM class Windows starts BrowserAI for
/// when one of its toasts is clicked, named for the application id the toasts are
/// raised under.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two values under the user's own classes, and nothing on the shortcut</b>, the
/// configuration measured on 2026-09-24 ([kb](../../../kb/windows/notifications.md#a-click-carries-the-dropdowns-value-only-to-a-com-activator)):
/// <c>AppUserModelId\&lt;id&gt;\CustomActivator</c> names the class, and
/// <c>CLSID\{class}\LocalServer32</c> names the program COM starts, to which it
/// appends <c>-Embedding</c>. That works beside Velopack's shortcut, which carries
/// the id and no activator, and with no BrowserAI process running COM started the
/// activator 21 to 40 ms after each click measured.
/// </para>
/// <para>
/// <b>The class is derived from the application id</b>, so the suite's test pack
/// (<c>velopack.BrowserAI.app.test</c>) and a real install
/// (<c>velopack.BrowserAI.app</c>) register two classes and neither overwrites the
/// other's program.
/// </para>
/// <para>
/// <b>Called by the install and update hooks, and the uninstall hook takes it
/// back.</b> The program named is the install's <c>current\BrowserAI.exe</c>, whose
/// path an update does not move.
/// </para>
/// </remarks>
internal static class ToastActivatorRegistration
{
    /// <summary>The argument COM starts the activator with, before the <c>-Embedding</c> it adds.</summary>
    public const string Argument = "-ToastActivated";

    /// <summary>The argument COM adds when it starts a local server.</summary>
    public const string EmbeddingArgument = "-Embedding";

    /// <summary>The value under the application id's key that names the class.</summary>
    public const string CustomActivatorValue = "CustomActivator";

    /// <summary>
    /// The namespace the class is derived in: chosen once, 2026-10-08, and never
    /// changed, because changing it moves every install's class.
    /// </summary>
    private static readonly Guid Namespace = new("8C1E1B8D-5D86-4C47-9F2B-6A0F3B1D2E71");

    /// <summary>The user's own classes, where both values live: <c>HKCU\Software\Classes</c>.</summary>
    /// <returns>The key, open for writing; the caller disposes it.</returns>
    public static RegistryKey UserClasses() => Registry.CurrentUser.CreateSubKey(@"Software\Classes", writable: true);

    /// <summary>The activator's class for one application id.</summary>
    /// <remarks>
    /// A name-based identifier in the shape RFC 9562 gives version 8: the SHA-256 of
    /// the namespace and the id's UTF-8, cut to 128 bits, with the version and
    /// variant bits set. The same id always gives the same class.
    /// </remarks>
    /// <param name="appUserModelId">The application id.</param>
    /// <returns>The class.</returns>
    public static Guid ClassFor(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);

        var name = Encoding.UTF8.GetBytes(appUserModelId);
        var input = new byte[16 + name.Length];

        _ = Namespace.TryWriteBytes(input, bigEndian: true, out _);
        name.CopyTo(input, 16);

        var hash = SHA256.HashData(input);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    /// <summary>The command line COM is told to start.</summary>
    /// <param name="executable">The program.</param>
    /// <returns>The command line.</returns>
    public static string CommandFor(string executable) => $"\"{executable}\" {Argument}";

    /// <summary>Registers the activator for one application id.</summary>
    /// <param name="classes">The classes key: <see cref="UserClasses"/> in the product.</param>
    /// <param name="appUserModelId">The application id the toasts are raised under.</param>
    /// <param name="executable">The program COM starts for a click.</param>
    public static void Register(RegistryKey classes, string appUserModelId, string executable)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        var activator = ClassFor(appUserModelId).ToString("B").ToUpperInvariant();

        using (var server = classes.CreateSubKey($@"CLSID\{activator}\LocalServer32", writable: true))
        {
            server.SetValue(string.Empty, CommandFor(executable), RegistryValueKind.String);
        }

        using var id = classes.CreateSubKey($@"AppUserModelId\{appUserModelId}", writable: true);

        id.SetValue(CustomActivatorValue, activator, RegistryValueKind.String);
    }

    /// <summary>Takes the registration back: the class's key, and the value naming it.</summary>
    /// <remarks>
    /// <b>The application id's key goes too when nothing else is left in it</b>; a
    /// key that holds something BrowserAI did not write keeps it.
    /// </remarks>
    /// <param name="classes">The classes key.</param>
    /// <param name="appUserModelId">The application id.</param>
    public static void Unregister(RegistryKey classes, string appUserModelId)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);

        var activator = ClassFor(appUserModelId).ToString("B").ToUpperInvariant();

        classes.DeleteSubKeyTree($@"CLSID\{activator}", throwOnMissingSubKey: false);

        var path = $@"AppUserModelId\{appUserModelId}";
        bool empty;

        using (var id = classes.OpenSubKey(path, writable: true))
        {
            if (id is null)
            {
                return;
            }

            id.DeleteValue(CustomActivatorValue, throwOnMissingValue: false);
            empty = id.ValueCount is 0 && id.SubKeyCount is 0;
        }

        if (empty)
        {
            classes.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
    }
}

/// <summary>
/// The activator mode: what BrowserAI does when COM starts it for a click on one of
/// its toasts.
/// </summary>
/// <remarks>
/// <para>
/// <b>A click is served by a process of its own</b>, the one COM starts, whether or
/// not the background is running: it registers the class, takes the one
/// activation COM hands it, and acts on it. A window that process opens comes to
/// the front, measured 2026-09-24 ([kb](../../../kb/windows/notifications.md#a-click-carries-the-dropdowns-value-only-to-a-com-activator)), which is
/// what a person who clicked <i>Install now</i> asked for.
/// </para>
/// <para>
/// <b>What each click does</b>: <i>Install now</i> and a click on the ready toast
/// open the dashboard's update page; <i>Changelog</i> and a click on the installed
/// toast open its changelog page; <i>Wait for inactivity</i> records the version so
/// the ready toast for it is raised again without a banner; <i>Dismiss</i> does
/// nothing, because the click itself already closed the toast.
/// </para>
/// </remarks>
internal static partial class ToastActivation
{
    /// <summary>The dashboard page an update click opens.</summary>
    public const string UpdatePage = "update";

    /// <summary>The dashboard page a changelog click opens.</summary>
    public const string ChangelogPage = "changelog";

    /// <summary>
    /// How long the activator waits for COM to hand it the click: <b>10 s</b>, a hang
    /// detector. COM hands it over as soon as the class is registered; measured
    /// 2026-09-24, COM started the activator 21 to 40 ms after the click.
    /// </summary>
    public static TimeSpan ActivationBound { get; } = UpdateBudgets.ToastActivationBound;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Whether this start is COM's, for a click.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>Whether it carries the activator's argument.</returns>
    public static bool IsActivation(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains(ToastActivatorRegistration.Argument, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers the activator's class for this process, waits for the click COM
    /// started it for, and returns it.
    /// </summary>
    /// <param name="appUserModelId">The application id the toasts are raised under.</param>
    /// <param name="logger">Where the click is recorded.</param>
    /// <returns>The click, or <see langword="null"/> when none came within <see cref="ActivationBound"/>.</returns>
    public static ToastClick? Receive(string appUserModelId, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        ArgumentNullException.ThrowIfNull(logger);

        var clicked = new TaskCompletionSource<ToastClick>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activator = ToastActivatorRegistration.ClassFor(appUserModelId);

        var thread = new Thread(() =>
        {
            var joined = TaskSchedulerInterop.CoInitializeEx(0, TaskSchedulerInterop.MultiThreaded);
            var factory = NewFactory(clicked);

            try
            {
                var answer = ToastInterop.CoRegisterClassObject(activator, factory, ToastInterop.LocalServer, ToastInterop.SingleUse, out var cookie);

                _ = registered.TrySetResult(answer);

                if (answer >= 0)
                {
                    _ = clicked.Task.Wait(ActivationBound);
                    _ = ToastInterop.CoRevokeClassObject(cookie);
                }
            }
            finally
            {
                _ = Marshal.Release(factory);

                // Only a join that took leaves; a changed-mode answer joined nothing.
                if (joined >= 0)
                {
                    TaskSchedulerInterop.CoUninitialize();
                }
            }
        })
        {
            IsBackground = true,
            Name = "BrowserAI toast activator",
        };

        thread.Start();

        var result = registered.Task.Wait(ActivationBound) ? registered.Task.Result : ToastInterop.NoInterface;

        if (result < 0)
        {
            ToastActivationLog.NotRegistered(logger, result);
            return null;
        }

        if (!clicked.Task.Wait(ActivationBound))
        {
            ToastActivationLog.NoClick(logger, ActivationBound.TotalSeconds);
            return null;
        }

        _ = thread.Join(ActivationBound);

        var click = clicked.Task.Result;

        ToastActivationLog.Clicked(logger, click.Action, click.Version ?? "none");
        return click;
    }

    /// <summary>The activator's class object, as the <c>IUnknown</c> COM is handed.</summary>
    /// <param name="clicked">Where the click it is given goes.</param>
    /// <returns>The pointer, owned by the caller.</returns>
    internal static nint NewFactory(TaskCompletionSource<ToastClick> clicked) =>
        Wrappers.GetOrCreateComInterfaceForObject(new ActivatorFactory(clicked), CreateComInterfaceFlags.None);

    /// <summary>Does what a click asks for.</summary>
    /// <param name="click">The click.</param>
    /// <param name="memory">Where a person's wait is kept.</param>
    /// <param name="openPage">A person's start that opens one dashboard page, by name; it returns the exit code.</param>
    /// <returns>The exit code.</returns>
    public static int Act(ToastClick? click, IUpdateToastMemory memory, Func<string, int> openPage)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(openPage);

        switch (click)
        {
            case { Action: ToastAction.UpdatePage }:
                return openPage(UpdatePage);

            case { Action: ToastAction.Changelog }:
                return openPage(ChangelogPage);

            case { Action: ToastAction.Wait, Version: { Length: > 0 } version }:
                memory.Waited(version);
                return 0;

            default:
                return 0;
        }
    }

    /// <summary>The class object COM asks for an activator.</summary>
    /// <param name="clicked">Where the click goes.</param>
    [GeneratedComClass]
    private sealed partial class ActivatorFactory(TaskCompletionSource<ToastClick> clicked) : IClassFactory
    {
        /// <inheritdoc />
        public void CreateInstance(nint outer, in Guid interfaceId, out nint instance)
        {
            instance = 0;

            if (outer != 0)
            {
                Marshal.ThrowExceptionForHR(ToastInterop.NoAggregation);
            }

            var unknown = Wrappers.GetOrCreateComInterfaceForObject(new Activator(clicked), CreateComInterfaceFlags.None);

            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, interfaceId, out instance));
            }
            finally
            {
                _ = Marshal.Release(unknown);
            }
        }

        /// <inheritdoc />
        public void LockServer(int lockServer)
        {
        }
    }

    /// <summary>The activator: one click, read back from its arguments.</summary>
    /// <param name="clicked">Where the click goes.</param>
    [GeneratedComClass]
    private sealed partial class Activator(TaskCompletionSource<ToastClick> clicked) : INotificationActivationCallback
    {
        /// <inheritdoc />
        public void Activate(string appUserModelId, string invokedArgs, nint data, uint count) =>
            _ = clicked.TrySetResult(UpdateToastContent.Parse(invokedArgs));
    }
}

/// <summary>The activator's records.</summary>
internal static partial class ToastActivationLog
{
    /// <summary>A click arrived.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="action">What it asked for.</param>
    /// <param name="version">The version it named.</param>
    [LoggerMessage(EventId = 7121, Level = LogLevel.Information, Message = "A toast click asked for {Action} (version {Version}).")]
    public static partial void Clicked(ILogger logger, ToastAction action, string version);

    /// <summary>COM did not hand the click over in time.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="seconds">How long it waited.</param>
    [LoggerMessage(EventId = 7122, Level = LogLevel.Warning, Message = "Started for a toast click, and no click arrived within {Seconds} s.")]
    public static partial void NoClick(ILogger logger, double seconds);

    /// <summary>The class could not be registered.</summary>
    /// <param name="logger">Where the record goes.</param>
    /// <param name="hresult">What COM answered.</param>
    [LoggerMessage(EventId = 7123, Level = LogLevel.Warning, Message = "The toast activator's class could not be registered (HRESULT {Hresult}).")]
    public static partial void NotRegistered(ILogger logger, int hresult);
}
