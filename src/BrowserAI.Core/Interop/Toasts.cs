// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace BrowserAI.Interop;

/// <summary>
/// The Windows Runtime entry points the update toasts need, declared by hand, and
/// the COM registration the toast activator needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hand-written, through <c>[LibraryImport]</c> and <c>[GeneratedComInterface]</c>,
/// with no C#/WinRT projection</b>: Q273 b, decided 2026-09-24, and measured the same
/// day to raise a toast from a NativeAOT binary with no warning in any publish log
/// ([kb](../../../kb/windows/notifications.md#a-nativeaot-binary-raises-a-toast-through-hand-written-winrt-calls-and-nothing-generated)). Every interface below names its id and
/// declares its methods in the order Windows' own metadata does, up to the last one
/// BrowserAI calls; <c>ToastInteropTests</c> reads that metadata from the files
/// Windows ships in <c>System32\WinMetadata</c> and holds both.
/// </para>
/// <para>
/// <b>An <c>HSTRING</c> is an <see cref="nint"/></b>, made and freed by
/// <see cref="HString"/>, and a WinRT <c>boolean</c> is a <see cref="byte"/>.
/// </para>
/// </remarks>
internal static partial class ToastInterop
{
    /// <summary><c>CLSCTX_LOCAL_SERVER</c>.</summary>
    public const uint LocalServer = 4;

    /// <summary>
    /// <c>REGCLS_SINGLEUSE</c>: once this process has been handed one activation, COM
    /// starts another for the next, so a second click is never handed to a process
    /// that is already acting on the first.
    /// </summary>
    public const uint SingleUse = 0;

    /// <summary><c>CLASS_E_NOAGGREGATION</c>.</summary>
    public const int NoAggregation = unchecked((int)0x80040110);

    /// <summary><c>E_NOINTERFACE</c>.</summary>
    public const int NoInterface = unchecked((int)0x80004002);

    /// <summary>The runtime class a toast's content is parsed into.</summary>
    public const string XmlDocumentClass = "Windows.Data.Xml.Dom.XmlDocument";

    /// <summary>The runtime class of a toast.</summary>
    public const string ToastNotificationClass = "Windows.UI.Notifications.ToastNotification";

    /// <summary>The runtime class whose statics make a notifier and read the history.</summary>
    public const string ToastNotificationManagerClass = "Windows.UI.Notifications.ToastNotificationManager";

    /// <summary>The runtime class of a toast's bound values.</summary>
    public const string NotificationDataClass = "Windows.UI.Notifications.NotificationData";

    /// <summary><c>IID_IToastNotificationFactory</c>, which <see cref="IToastNotificationFactory"/> declares too.</summary>
    public static readonly Guid ToastNotificationFactoryId = new("04124B20-82C6-4229-B109-FD9ED4662B53");

    /// <summary><c>IID_IToastNotificationManagerStatics</c>, which <see cref="IToastNotificationManagerStatics"/> declares too.</summary>
    public static readonly Guid ToastNotificationManagerStaticsId = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");

    /// <summary><c>IID_IToastNotificationManagerStatics2</c>, which <see cref="IToastNotificationManagerStatics2"/> declares too.</summary>
    public static readonly Guid ToastNotificationManagerStatics2Id = new("7AB93C52-0E48-4750-BA9D-1A4113981847");

    /// <summary>Activates a runtime class through its default constructor.</summary>
    /// <param name="activatableClassId">The class's name, as an <c>HSTRING</c>.</param>
    /// <param name="instance">The instance's <c>IInspectable</c>, owned by the caller.</param>
    /// <returns>The <c>HRESULT</c>.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("combase.dll", EntryPoint = "RoActivateInstance", SetLastError = false)]
    internal static partial int RoActivateInstance(nint activatableClassId, out nint instance);

    /// <summary>A runtime class's activation factory or statics, as one interface.</summary>
    /// <param name="activatableClassId">The class's name, as an <c>HSTRING</c>.</param>
    /// <param name="interfaceId">The interface wanted.</param>
    /// <param name="factory">The interface, owned by the caller.</param>
    /// <returns>The <c>HRESULT</c>.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("combase.dll", EntryPoint = "RoGetActivationFactory", SetLastError = false)]
    internal static partial int RoGetActivationFactory(nint activatableClassId, in Guid interfaceId, out nint factory);

    /// <summary>Makes an <c>HSTRING</c> holding a copy of a string.</summary>
    /// <param name="source">The string.</param>
    /// <param name="length">Its length in UTF-16 code units.</param>
    /// <param name="value">The <c>HSTRING</c>, owned by the caller; zero for an empty string.</param>
    /// <returns>The <c>HRESULT</c>.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("combase.dll", EntryPoint = "WindowsCreateString", SetLastError = false, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int WindowsCreateString(string source, uint length, out nint value);

    /// <summary>The characters an <c>HSTRING</c> holds, without copying them.</summary>
    /// <param name="value">The <c>HSTRING</c>.</param>
    /// <param name="length">How many UTF-16 code units it holds.</param>
    /// <returns>Where they are; valid while the <c>HSTRING</c> lives.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("combase.dll", EntryPoint = "WindowsGetStringRawBuffer", SetLastError = false)]
    internal static partial nint WindowsGetStringRawBuffer(nint value, out uint length);

    /// <summary>Frees an <c>HSTRING</c>.</summary>
    /// <param name="value">The <c>HSTRING</c>.</param>
    /// <returns>The <c>HRESULT</c>.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("combase.dll", EntryPoint = "WindowsDeleteString", SetLastError = false)]
    internal static partial int WindowsDeleteString(nint value);

    /// <summary>The application id this process runs under, which Velopack sets in every installed process.</summary>
    /// <param name="appId">The id, when one is set.</param>
    /// <returns>The <c>HRESULT</c>: a failure when none is set.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("shell32.dll", EntryPoint = "GetCurrentProcessExplicitAppUserModelID", SetLastError = false, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetCurrentProcessExplicitAppUserModelID(out string? appId);

    /// <summary>Registers a class object, so COM hands this process the next activation of its class.</summary>
    /// <param name="classId">The class.</param>
    /// <param name="unknown">The class object's <c>IUnknown</c>.</param>
    /// <param name="context"><see cref="LocalServer"/>.</param>
    /// <param name="flags"><see cref="SingleUse"/>.</param>
    /// <param name="cookie">What <see cref="CoRevokeClassObject"/> takes back.</param>
    /// <returns>The <c>HRESULT</c>.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ole32.dll", EntryPoint = "CoRegisterClassObject", SetLastError = false)]
    internal static partial int CoRegisterClassObject(in Guid classId, nint unknown, uint context, uint flags, out uint cookie);

    /// <summary>Takes a class object's registration back.</summary>
    /// <param name="cookie">What <see cref="CoRegisterClassObject"/> returned.</param>
    /// <returns>The <c>HRESULT</c>.</returns>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("ole32.dll", EntryPoint = "CoRevokeClassObject", SetLastError = false)]
    internal static partial int CoRevokeClassObject(uint cookie);

    /// <summary>The application id this process runs under, or <see langword="null"/> when none is set.</summary>
    /// <returns>The id.</returns>
    public static string? CurrentAppUserModelId() =>
        GetCurrentProcessExplicitAppUserModelID(out var appId) >= 0 && appId is { Length: > 0 } ? appId : null;
}

/// <summary>One <c>HSTRING</c>, freed when disposed.</summary>
internal sealed class HString : IDisposable
{
    /// <summary>The text an <c>HSTRING</c> holds, which this method then frees.</summary>
    /// <param name="value">An <c>HSTRING</c> the caller owns.</param>
    /// <returns>Its text.</returns>
    public static string Take(nint value)
    {
        try
        {
            var characters = ToastInterop.WindowsGetStringRawBuffer(value, out var length);

            return length is 0 ? string.Empty : Marshal.PtrToStringUni(characters, (int)length);
        }
        finally
        {
            _ = ToastInterop.WindowsDeleteString(value);
        }
    }

    /// <summary>Makes one.</summary>
    /// <param name="value">What it holds.</param>
    public HString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Marshal.ThrowExceptionForHR(ToastInterop.WindowsCreateString(value, (uint)value.Length, out var handle));
        Handle = handle;
    }

    /// <summary>The <c>HSTRING</c>; zero for an empty string, which is what Windows makes of one.</summary>
    public nint Handle { get; private set; }

    /// <summary>Frees it.</summary>
    public void Dispose()
    {
        if (Handle != 0)
        {
            _ = ToastInterop.WindowsDeleteString(Handle);
            Handle = 0;
        }
    }
}

/// <summary>The three <c>IInspectable</c> slots every Windows Runtime interface begins with. BrowserAI never calls them.</summary>
[GeneratedComInterface]
[Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")]
internal partial interface IInspectableSlots
{
    /// <summary>Slot 3.</summary>
    /// <param name="count">Never read.</param>
    /// <param name="interfaceIds">Never read.</param>
    void GetIids(out uint count, out nint interfaceIds);

    /// <summary>Slot 4.</summary>
    /// <returns>Never read.</returns>
    nint GetRuntimeClassName();

    /// <summary>Slot 5.</summary>
    /// <returns>Never read.</returns>
    int GetTrustLevel();
}

/// <summary><c>Windows.Data.Xml.Dom.IXmlDocument</c>, passed through and never called.</summary>
[GeneratedComInterface]
[Guid("F7F3A506-1E87-42D6-BCFB-B8C809FA5494")]
internal partial interface IXmlDocument : IInspectableSlots
{
}

/// <summary><c>Windows.Data.Xml.Dom.IXmlDocumentIO</c>, to its first method.</summary>
[GeneratedComInterface]
[Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")]
internal partial interface IXmlDocumentIO : IInspectableSlots
{
    /// <summary>Parses XML into the document.</summary>
    /// <param name="xml">The XML, as an <c>HSTRING</c>.</param>
    void LoadXml(nint xml);
}

/// <summary><c>IToastNotificationFactory</c>.</summary>
[GeneratedComInterface]
[Guid("04124B20-82C6-4229-B109-FD9ED4662B53")]
internal partial interface IToastNotificationFactory : IInspectableSlots
{
    /// <summary>A toast with this content.</summary>
    /// <param name="content">The parsed XML.</param>
    /// <returns>The toast.</returns>
    IToastNotification CreateToastNotification(IXmlDocument content);
}

/// <summary><c>IToastNotification</c>, passed through and never called.</summary>
[GeneratedComInterface]
[Guid("997E2675-059E-4E60-8B06-1760917C8B80")]
internal partial interface IToastNotification : IInspectableSlots
{
}

/// <summary><c>IToastNotification2</c>, to <c>put_SuppressPopup</c>.</summary>
[GeneratedComInterface]
[Guid("9DFB9FD1-143A-490E-90BF-B9FBA7132DE7")]
internal partial interface IToastNotification2 : IInspectableSlots
{
    /// <summary>Sets the tag.</summary>
    /// <param name="value">An <c>HSTRING</c>.</param>
    void PutTag(nint value);

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    nint GetTag();

    /// <summary>Sets the group.</summary>
    /// <param name="value">An <c>HSTRING</c>.</param>
    void PutGroup(nint value);

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    nint GetGroup();

    /// <summary>Sends it to the Notification Centre with no banner when non-zero.</summary>
    /// <param name="value">A <c>boolean</c>.</param>
    void PutSuppressPopup(byte value);

    /// <summary>Whether it goes to the Notification Centre with no banner; read back by the suite.</summary>
    /// <returns>A <c>boolean</c>.</returns>
    byte GetSuppressPopup();
}

/// <summary><c>IToastNotification4</c>, to <c>put_Data</c>.</summary>
[GeneratedComInterface]
[Guid("15154935-28EA-4727-88E9-C58680E2D118")]
internal partial interface IToastNotification4 : IInspectableSlots
{
    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    nint GetData();

    /// <summary>The bound fields' first values.</summary>
    /// <param name="value">The values.</param>
    void PutData(INotificationData value);
}

/// <summary><c>IToastNotification6</c>.</summary>
[GeneratedComInterface]
[Guid("43EBFE53-89AE-5C1E-A279-3AECFE9B6F54")]
internal partial interface IToastNotification6 : IInspectableSlots
{
    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    byte GetExpiresOnReboot();

    /// <summary>Has Windows remove it at the next restart when non-zero.</summary>
    /// <param name="value">A <c>boolean</c>.</param>
    void PutExpiresOnReboot(byte value);
}

/// <summary><c>IToastNotificationManagerStatics</c>, to its second method.</summary>
[GeneratedComInterface]
[Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
internal partial interface IToastNotificationManagerStatics : IInspectableSlots
{
    /// <summary>Never called: a process with no package identity has to name its id.</summary>
    /// <returns>Never read.</returns>
    nint CreateToastNotifier();

    /// <summary>The notifier for one application id.</summary>
    /// <param name="applicationId">The id, as an <c>HSTRING</c>.</param>
    /// <returns>The notifier.</returns>
    IToastNotifier CreateToastNotifierWithId(nint applicationId);
}

/// <summary><c>IToastNotificationManagerStatics2</c>.</summary>
[GeneratedComInterface]
[Guid("7AB93C52-0E48-4750-BA9D-1A4113981847")]
internal partial interface IToastNotificationManagerStatics2 : IInspectableSlots
{
    /// <summary>The Notification Centre's history of this user's toasts.</summary>
    /// <returns>The history.</returns>
    IToastNotificationHistory GetHistory();
}

/// <summary><c>IToastNotificationHistory</c>, to its third method.</summary>
[GeneratedComInterface]
[Guid("5CADDC63-01D3-4C97-986F-0533483FEE14")]
internal partial interface IToastNotificationHistory : IInspectableSlots
{
    /// <summary>Never called.</summary>
    /// <param name="group">Never passed.</param>
    void RemoveGroup(nint group);

    /// <summary>Never called.</summary>
    /// <param name="group">Never passed.</param>
    /// <param name="applicationId">Never passed.</param>
    void RemoveGroupWithId(nint group, nint applicationId);

    /// <summary>Removes one toast, from the screen too when it is showing.</summary>
    /// <param name="tag">Its tag, as an <c>HSTRING</c>.</param>
    /// <param name="group">Its group, as an <c>HSTRING</c>.</param>
    /// <param name="applicationId">The application id, as an <c>HSTRING</c>.</param>
    void RemoveGroupedTagWithId(nint tag, nint group, nint applicationId);
}

/// <summary><c>IToastNotifier</c>, to its first method.</summary>
[GeneratedComInterface]
[Guid("75927B93-03F3-41EC-91D3-6E5BAC1B38E7")]
internal partial interface IToastNotifier : IInspectableSlots
{
    /// <summary>Shows a toast.</summary>
    /// <param name="notification">The toast.</param>
    void Show(IToastNotification notification);
}

/// <summary><c>IToastNotifier2</c>, to its first method.</summary>
[GeneratedComInterface]
[Guid("354389C6-7C01-4BD5-9C20-604340CD2B74")]
internal partial interface IToastNotifier2 : IInspectableSlots
{
    /// <summary>Gives a shown toast's bound fields new values.</summary>
    /// <param name="data">The values and their sequence number.</param>
    /// <param name="tag">The toast's tag, as an <c>HSTRING</c>.</param>
    /// <param name="group">The toast's group, as an <c>HSTRING</c>.</param>
    /// <returns>A <c>NotificationUpdateResult</c>.</returns>
    int UpdateWithTagAndGroup(INotificationData data, nint tag, nint group);
}

/// <summary><c>INotificationData</c>.</summary>
[GeneratedComInterface]
[Guid("9FFD2312-9D6A-4AAF-B6AC-FF17F0C1F280")]
internal partial interface INotificationData : IInspectableSlots
{
    /// <summary>The values, by name.</summary>
    /// <returns>The map.</returns>
    IStringMap GetValues();

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    uint GetSequenceNumber();

    /// <summary>Orders this set of values among the others: the greatest non-zero wins.</summary>
    /// <param name="value">The number.</param>
    void PutSequenceNumber(uint value);
}

/// <summary><c>IMap&lt;HSTRING, HSTRING&gt;</c>, to <c>Insert</c>.</summary>
/// <remarks>
/// <b>Its id is computed, not declared</b>: Windows derives a parameterized
/// interface's id from its signature, and <c>ToastInteropTests</c> derives this one
/// again from <c>IMap`2</c>'s own id.
/// </remarks>
[GeneratedComInterface]
[Guid("F6D1F700-49C2-52AE-8154-826F9908773C")]
internal partial interface IStringMap : IInspectableSlots
{
    /// <summary>Never called.</summary>
    /// <param name="key">Never passed.</param>
    /// <returns>Never read.</returns>
    nint Lookup(nint key);

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    uint GetSize();

    /// <summary>Never called.</summary>
    /// <param name="key">Never passed.</param>
    /// <returns>Never read.</returns>
    byte HasKey(nint key);

    /// <summary>Never called.</summary>
    /// <returns>Never read.</returns>
    nint GetView();

    /// <summary>Sets one value.</summary>
    /// <param name="key">Its name, as an <c>HSTRING</c>.</param>
    /// <param name="value">Its value, as an <c>HSTRING</c>.</param>
    /// <returns>Whether it replaced a value.</returns>
    byte Insert(nint key, nint value);
}

/// <summary><c>INotificationActivationCallback</c>: what COM calls when a toast or one of its buttons is clicked.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
internal partial interface INotificationActivationCallback
{
    /// <summary>A click.</summary>
    /// <param name="appUserModelId">The application id the toast was raised under.</param>
    /// <param name="invokedArgs">The arguments the toast or the button carried.</param>
    /// <param name="data">The inputs' values; BrowserAI's toasts have none.</param>
    /// <param name="count">How many.</param>
    void Activate(string appUserModelId, string invokedArgs, nint data, uint count);
}

/// <summary><c>IClassFactory</c>.</summary>
[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    /// <summary>One instance of the class.</summary>
    /// <param name="outer">Never anything but zero: the class does not aggregate.</param>
    /// <param name="interfaceId">The interface wanted.</param>
    /// <param name="instance">The interface.</param>
    void CreateInstance(nint outer, in Guid interfaceId, out nint instance);

    /// <summary>Keeps the server in memory; this one lives exactly as long as its one activation.</summary>
    /// <param name="lockServer">Ignored.</param>
    void LockServer(int lockServer);
}
