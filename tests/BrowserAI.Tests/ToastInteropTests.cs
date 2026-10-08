// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using BrowserAI.Interop;
using BrowserAI.Tests.Harness;
using BrowserAI.Updates;
using W = Windows.Win32;

namespace BrowserAI.Tests;

/// <summary>
/// The update toasts' hand-written interop, held against Windows' own metadata and
/// against the objects Windows makes, with no toast shown.
/// </summary>
/// <remarks>
/// <para>
/// <b>A vtable slot is its position, and nothing at run time checks it</b>, which is
/// <see cref="InteropLayoutTests"/>' reason for the Task Scheduler's interfaces.
/// The Windows Runtime's are not in the metadata CsWin32 reads, so their oracle is
/// the metadata Windows itself ships, <c>System32\WinMetadata</c>, read here with
/// <see cref="MetadataReader"/>: each interface's id, and its methods' names in
/// order.
/// </para>
/// <para>
/// <b>And then the objects themselves</b>: a real toast object is built through
/// every interface the product calls, and every setting is read back through the
/// same declarations. Building one shows nothing; only a notifier's <c>Show</c>
/// does, and no test here makes a notifier (Q278).
/// </para>
/// </remarks>
internal sealed class ToastInteropTests
{
    /// <summary>Where Windows keeps its own Windows Runtime metadata.</summary>
    private static readonly string WinMetadata = Path.Combine(Environment.SystemDirectory, "WinMetadata");

    /// <summary>
    /// Every Windows Runtime interface the product declares carries the id Windows
    /// gives it, and declares Windows' methods in Windows' order.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task EveryWindowsRuntimeInterfaceDeclaresWindowsSlotsInWindowsOrder()
    {
        (Type Hand, string File, string Name)[] interfaces =
        [
            (typeof(IXmlDocument), "Windows.Data.winmd", "Windows.Data.Xml.Dom.IXmlDocument"),
            (typeof(IXmlDocumentIO), "Windows.Data.winmd", "Windows.Data.Xml.Dom.IXmlDocumentIO"),
            (typeof(IToastNotificationFactory), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotificationFactory"),
            (typeof(IToastNotification), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotification"),
            (typeof(IToastNotification2), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotification2"),
            (typeof(IToastNotification4), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotification4"),
            (typeof(IToastNotification6), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotification6"),
            (typeof(IToastNotificationManagerStatics), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotificationManagerStatics"),
            (typeof(IToastNotificationManagerStatics2), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotificationManagerStatics2"),
            (typeof(IToastNotificationHistory), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotificationHistory"),
            (typeof(IToastNotifier), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotifier"),
            (typeof(IToastNotifier2), "Windows.UI.winmd", "Windows.UI.Notifications.IToastNotifier2"),
            (typeof(INotificationData), "Windows.UI.winmd", "Windows.UI.Notifications.INotificationData"),
        ];

        foreach (var (hand, file, name) in interfaces)
        {
            var (id, methods) = ReadInterface(Path.Combine(WinMetadata, file), name);
            var declared = Declared(hand);

            await Assert.That(hand.GUID).IsEqualTo(id).Because(name);
            await Assert.That(declared.Count).IsLessThanOrEqualTo(methods.Count).Because(name);
            await Assert.That(string.Join(", ", declared)).IsEqualTo(string.Join(", ", methods.Take(declared.Count))).Because(name);
        }

        // The ids the factories are asked for are the interfaces' own.
        await Assert.That(ToastInterop.ToastNotificationFactoryId).IsEqualTo(typeof(IToastNotificationFactory).GUID);
        await Assert.That(ToastInterop.ToastNotificationManagerStaticsId).IsEqualTo(typeof(IToastNotificationManagerStatics).GUID);
        await Assert.That(ToastInterop.ToastNotificationManagerStatics2Id).IsEqualTo(typeof(IToastNotificationManagerStatics2).GUID);

        // IInspectable's three, the base every one of them re-declares.
        await Assert.That(string.Join(", ", typeof(IInspectableSlots).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal)))
            .IsEqualTo("GetIids, GetRuntimeClassName, GetTrustLevel");
    }

    /// <summary>
    /// The map of strings' methods are <c>IMap`2</c>'s, in its order, and its id is
    /// the one Windows derives for <c>IMap&lt;String, String&gt;</c>: the interface
    /// is asked for by that id when a toast's values are written, and Windows hands
    /// it over.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStringMapIsTheInterfaceWindowsHandsOut()
    {
        var (_, methods) = ReadInterface(Path.Combine(WinMetadata, "Windows.Foundation.winmd"), "Windows.Foundation.Collections.IMap`2");
        var declared = Declared(typeof(IStringMap));

        await Assert.That(string.Join(", ", declared)).IsEqualTo(string.Join(", ", methods.Take(declared.Count)));

        var (inserted, read) = OnComThread(() =>
        {
            using var name = new HString(ToastInterop.NotificationDataClass);

            Marshal.ThrowExceptionForHR(ToastInterop.RoActivateInstance(name.Handle, out var instance));

            try
            {
                var data = (INotificationData)new System.Runtime.InteropServices.Marshalling.StrategyBasedComWrappers()
                    .GetOrCreateObjectForComInstance(instance, CreateObjectFlags.None);
                var map = data.GetValues();

                using var key = new HString("progressStatus");
                using var value = new HString("Installs in 9:30 if nothing uses it");

                var replaced = map.Insert(key.Handle, value.Handle);

                return (replaced, HString.Take(map.Lookup(key.Handle)));
            }
            finally
            {
                _ = Marshal.Release(instance);
            }
        });

        await Assert.That(inserted).IsEqualTo((byte)0);
        await Assert.That(read).IsEqualTo("Installs in 9:30 if nothing uses it");
    }

    /// <summary>
    /// A ready toast composed through every interface the product shows one with
    /// carries every setting it was given, read back through the same declarations.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AComposedToastCarriesEverySettingItWasGiven()
    {
        var at = DateTimeOffset.UnixEpoch;
        var holds = new UpdateHoldSnapshot(at, UpdateHoldState.Held, "1.2.0", [], [new HoldingSession(@"C:\w", null, at.AddMinutes(50))], []);
        var (values, _) = UpdateToastContent.ReadyData(holds, at, TimeZoneInfo.Utc, default);
        var request = new ToastRequest(UpdateToastContent.Ready("1.2.0", holds), values, SuppressPopup: true, ExpiresOnReboot: true);

        var (tag, group, silent, reboot, sequence, status, count) = OnComThread(() =>
        {
            var toast = WindowsToastSurface.Compose(WindowsToastSurface.NewFactory(), UpdateToastContent.ReadyTag, UpdateToastContent.Group, request, 41);
            var second = (IToastNotification2)toast;
            var data = (INotificationData)WrapData(((IToastNotification4)toast).GetData());

            using var key = new HString(UpdateToastContent.StatusField);

            return (
                HString.Take(second.GetTag()),
                HString.Take(second.GetGroup()),
                second.GetSuppressPopup(),
                ((IToastNotification6)toast).GetExpiresOnReboot(),
                data.GetSequenceNumber(),
                HString.Take(data.GetValues().Lookup(key.Handle)),
                data.GetValues().GetSize());
        });

        await Assert.That(tag).IsEqualTo("ready");
        await Assert.That(group).IsEqualTo("update");
        await Assert.That(silent).IsEqualTo((byte)1);
        await Assert.That(reboot).IsEqualTo((byte)1);
        await Assert.That(sequence).IsEqualTo(41u);
        await Assert.That(status).IsEqualTo("Installs in 50:00 if nothing uses it");
        await Assert.That(count).IsEqualTo((uint)values.Count);
    }

    /// <summary>
    /// The activator's two COM interfaces carry Windows' ids and declare Windows'
    /// methods in Windows' order, by CsWin32's reading of Microsoft's metadata.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheActivatorsTwoInterfacesAreWindowsOwn()
    {
        (Type Hand, Type Metadata)[] interfaces =
        [
            (typeof(INotificationActivationCallback), typeof(W.UI.Notifications.INotificationActivationCallback)),
            (typeof(IClassFactory), typeof(W.System.Com.IClassFactory)),
        ];

        foreach (var (hand, metadata) in interfaces)
        {
            await Assert.That(hand.GUID).IsEqualTo(metadata.GUID);

            var handSlots = hand.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(method => method.MetadataToken)
                .Select(method => $"{method.Name}/{method.GetParameters().Length}");
            var metadataSlots = metadata.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(method => method.MetadataToken)
                .Select(method => $"{method.Name}/{method.GetParameters().Length}");

            await Assert.That(string.Join(", ", handSlots)).IsEqualTo(string.Join(", ", metadataSlots));
        }
    }

    /// <summary>
    /// One interface's methods in the order it declares them, without the
    /// <c>IInspectable</c> slots the generator re-declares in each.
    /// </summary>
    private static List<string> Declared(Type type) =>
        [.. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => typeof(IInspectableSlots).GetMethod(method.Name) is null)
            .OrderBy(method => method.MetadataToken)
            .Select(method => method.Name)];

    /// <summary>
    /// One interface's id and method names as a Windows metadata file declares them,
    /// accessors spelled the way the product spells them: <c>get_Tag</c> is <c>GetTag</c>.
    /// </summary>
    private static (Guid Id, List<string> Methods) ReadInterface(string file, string fullName)
    {
        using var stream = File.OpenRead(file);
        using var image = new PEReader(stream);

        var reader = image.GetMetadataReader();

        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);

            if (!string.Equals($"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}", fullName, StringComparison.Ordinal))
            {
                continue;
            }

            var id = Guid.Empty;

            foreach (var attributeHandle in type.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(attributeHandle);

                if (attribute.Constructor.Kind is HandleKind.MemberReference
                    && reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is { Kind: HandleKind.TypeReference } parent
                    && reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name) is "GuidAttribute")
                {
                    // The blob's prolog, then the id's fields, little-endian, as Guid reads them.
                    id = new Guid(reader.GetBlobBytes(attribute.Value).AsSpan(2, 16));
                }
            }

            List<string> methods = [.. type.GetMethods().Select(method => Spelled(NameOf(reader, reader.GetMethodDefinition(method))))];

            return (id, methods);
        }

        throw new InvalidOperationException($"{fullName} is not in {file}.");
    }

    /// <summary>
    /// A method's own name in the vtable's terms: an overload's metadata name is the
    /// name it is projected as, shared by every overload, and its own is the one
    /// <c>OverloadAttribute</c> carries, <c>CreateToastNotifierWithId</c> beside
    /// <c>CreateToastNotifier</c>.
    /// </summary>
    private static string NameOf(MetadataReader reader, MethodDefinition method)
    {
        foreach (var attributeHandle in method.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);

            if (attribute.Constructor.Kind is HandleKind.MemberReference
                && reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is { Kind: HandleKind.TypeReference } parent
                && reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name) is "OverloadAttribute")
            {
                var value = reader.GetBlobReader(attribute.Value);

                _ = value.ReadUInt16();

                return value.ReadSerializedString() ?? reader.GetString(method.Name);
            }
        }

        return reader.GetString(method.Name);
    }

    private static string Spelled(string name) =>
        name.StartsWith("get_", StringComparison.Ordinal) ? "Get" + name[4..]
        : name.StartsWith("put_", StringComparison.Ordinal) ? "Put" + name[4..]
        : name;

    private static object WrapData(nint pointer)
    {
        try
        {
            return new System.Runtime.InteropServices.Marshalling.StrategyBasedComWrappers().GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            _ = Marshal.Release(pointer);
        }
    }

    /// <summary>Runs one call on a thread of its own in the multi-threaded apartment, the way the product's surface does.</summary>
    private static T OnComThread<T>(Func<T> call)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var joined = TaskSchedulerInterop.CoInitializeEx(0, TaskSchedulerInterop.MultiThreaded);

            try
            {
                result = call();
            }
#pragma warning disable CA1031 // Handed back to the arm, which fails on it.
            catch (Exception thrown)
#pragma warning restore CA1031
            {
                failure = thrown;
            }
            finally
            {
                if (joined >= 0)
                {
                    TaskSchedulerInterop.CoUninitialize();
                }
            }
        });

        thread.Start();

        if (!thread.Join(TestDefaults.InProcessHang))
        {
            throw new TimeoutException("The call on the COM thread did not return.");
        }

        return failure is null ? result : throw new InvalidOperationException("The call on the COM thread threw.", failure);
    }
}
