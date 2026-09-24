// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Q254 SCRATCH PROTOTYPE -- not product code. Research rig for the update toast.
//
// Raises a Windows toast from a NativeAOT binary with NO CsWinRT and NO generated
// interop: every WinRT/COM call below is a function pointer taken out of a vtable
// slot, and every IID and slot index is transcribed from the Windows SDK
// 10.0.26100.0 headers and IDL, cited beside it. The only P/Invokes are blittable
// DllImports (no marshalling stubs at all).
//
// Modes (all output goes to events.log beside the exe; the exe is a Windows-subsystem
// binary so that a COM or protocol launch never puts a console on the screen):
//   show <xmlFile> <aumid> <tag> <group> <waitSeconds> [nohandlers]
//   history <aumid>          list toasts still in the Action Center for an AUMID
//   remove <aumid> <tag> <group>
//   clear <aumid>
//   -ToastActivated / -Embedding   COM local-server mode (INotificationActivationCallback)
//   browserai-q254test:...   protocol activation (logs the URI)
//   anything else            logs the command line (what a plain launch looks like)

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

internal static unsafe class Program
{
    // ---------------- IIDs (Windows SDK 10.0.26100.0) ----------------
    // winrt\windows.ui.notifications.idl:1410
    private static readonly Guid IID_IToastNotificationManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");
    // winrt\windows.ui.notifications.idl:1420
    private static readonly Guid IID_IToastNotificationManagerStatics2 = new("7AB93C52-0E48-4750-BA9D-1A4113981847");
    // winrt\windows.ui.notifications.idl:1331
    private static readonly Guid IID_IToastNotificationFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");
    // winrt\windows.ui.notifications.idl:1278
    private static readonly Guid IID_IToastNotification2 = new("9DFB9FD1-143A-490E-90BF-B9FBA7132DE7");
    // winrt\windows.ui.notifications.idl:1194
    private static readonly Guid IID_IToastActivatedEventArgs = new("E3BF92F3-C197-436F-8265-0625824F8DAC");
    // winrt\windows.ui.notifications.idl:1202
    private static readonly Guid IID_IToastActivatedEventArgs2 = new("AB7DA512-CC61-568E-81BE-304AC31038FA");
    // winrt\windows.ui.notifications.idl:1246
    private static readonly Guid IID_IToastDismissedEventArgs = new("3F89D935-D9CB-4538-A0F0-FFE7659938F8");
    // winrt\windows.ui.notifications.idl:1254
    private static readonly Guid IID_IToastFailedEventArgs = new("35176862-CFD4-44F8-AD64-F500FD896C3B");
    // winrt\windows.ui.notifications.idl:1339
    private static readonly Guid IID_IToastNotificationHistory = new("5CADDC63-01D3-4C97-986F-0533483FEE14");
    // winrt\windows.ui.notifications.idl:1353
    private static readonly Guid IID_IToastNotificationHistory2 = new("3BC3D253-2F31-4092-9129-8AD5ABF067DA");
    // winrt\windows.data.xml.dom.idl:275
    private static readonly Guid IID_IXmlDocument = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");
    // winrt\windows.data.xml.dom.idl:314
    private static readonly Guid IID_IXmlDocumentIO = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");
    // winrt\windows.data.xml.dom.idl:489
    private static readonly Guid IID_IXmlNodeSerializer = new("5CC5B382-E6DD-4991-ABEF-06D8D2E7BD0C");
    // winrt\windows.ui.notifications.h:2206 -- TypedEventHandler<ToastNotification, IInspectable>
    private static readonly Guid IID_ActivatedHandler = new("AB54DE2D-97D9-5528-B6AD-105AFE156530");
    // winrt\windows.ui.notifications.h:2244 -- TypedEventHandler<ToastNotification, ToastDismissedEventArgs>
    private static readonly Guid IID_DismissedHandler = new("61C2402F-0ED0-5A18-AB69-59F4AA99A368");
    // winrt\windows.ui.notifications.h:2283 -- TypedEventHandler<ToastNotification, ToastFailedEventArgs>
    private static readonly Guid IID_FailedHandler = new("95E3E803-C969-5E3A-9753-EA2AD22A9A33");
    // winrt\windows.foundation.h:688 -- IMap<HSTRING, IInspectable*>
    private static readonly Guid IID_IMap_String_Object = new("1B0D3570-0877-5EC2-8A2C-3B9539506ACA");
    // winrt\windows.foundation.h:459 -- IIterable<IKeyValuePair<HSTRING, IInspectable*>>
    private static readonly Guid IID_IIterable_KVP = new("FE2F3D47-5D47-5499-8374-430C7CDA0204");
    // winrt\windows.foundation.idl:633
    private static readonly Guid IID_IPropertyValue = new("4BD682DD-7554-40E9-9A9B-82654EDE7E62");
    // um\NotificationActivationCallback.idl:21
    private static readonly Guid IID_INotificationActivationCallback = new("53E31837-6600-4A81-9395-75CFFE746F94");
    // um\Unknwnbase.idl:36 and :155
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IClassFactory = new("00000001-0000-0000-C000-000000000046");
    // um\objidlbase.idl:111 and :183
    private static readonly Guid IID_IMarshal = new("00000003-0000-0000-C000-000000000046");
    private static readonly Guid IID_IAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");

    // The scratch activator CLSID, minted for this research on 2026-09-24. Registered
    // under HKCU\Software\Classes\CLSID only while a measurement runs.
    private static readonly Guid CLSID_ProtoActivator = new("6E64C7E9-5743-476D-A2EA-6502822581A4");

    private const string ProtocolScheme = "browserai-q254test";

    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

    // ---------------- combase / ole32, blittable only ----------------
    [DllImport("combase.dll", ExactSpelling = true)] private static extern int RoInitialize(int initType);
    [DllImport("combase.dll", ExactSpelling = true)] private static extern int RoGetActivationFactory(nint activatableClassId, Guid* iid, nint* factory);
    [DllImport("combase.dll", ExactSpelling = true)] private static extern int RoActivateInstance(nint activatableClassId, nint* instance);
    [DllImport("combase.dll", ExactSpelling = true)] private static extern int WindowsCreateString(char* sourceString, uint length, nint* hstring);
    [DllImport("combase.dll", ExactSpelling = true)] private static extern int WindowsDeleteString(nint hstring);
    [DllImport("combase.dll", ExactSpelling = true)] private static extern char* WindowsGetStringRawBuffer(nint hstring, uint* length);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(nint reserved, uint coInit);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoRegisterClassObject(Guid* rclsid, nint unknown, uint clsContext, uint flags, uint* cookie);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoRevokeClassObject(uint cookie);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoCreateFreeThreadedMarshaler(nint outer, nint* marshaler);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(nint window, uint* processId);

    private static string Foreground()
    {
        var window = GetForegroundWindow();
        uint pid;
        _ = GetWindowThreadProcessId(window, &pid);
        return $"foreground=0x{window:X} pid={pid}";
    }

    private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "events.log");
    private static readonly object LogGate = new();
    private static readonly ManualResetEventSlim Happened = new(false);

    // Everything handed to WinRT is kept alive for the life of the process.
    private static nint s_toast, s_notifier, s_activated, s_dismissed, s_failed, s_factoryObj, s_callbackObj;

    private static void Log(string text)
    {
        var line = $"{DateTime.UtcNow:O} pid={Environment.ProcessId} {text}\n";
        lock (LogGate)
        {
            using var file = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            file.Write(Encoding.UTF8.GetBytes(line));
        }
    }

    private static int Main(string[] args)
    {
        Log($"START args=[{string.Join(" | ", args)}] commandLine={Environment.CommandLine}");

        try
        {
            if (args.Length >= 6 && args[0] == "show")
            {
                return Show(args);
            }

            if (args.Length >= 6 && args[0] == "schedule")
            {
                return Schedule(args);
            }

            if (args.Length >= 2 && args[0] == "history")
            {
                return History(args[1]);
            }

            if (args.Length >= 4 && args[0] == "remove")
            {
                return Remove(args[1], args[2], args[3]);
            }

            if (args.Length >= 2 && args[0] == "clear")
            {
                return Clear(args[1]);
            }

            if (Array.Exists(args, a => a.Equals("-ToastActivated", StringComparison.OrdinalIgnoreCase)
                || a.Equals("-Embedding", StringComparison.OrdinalIgnoreCase)
                || a.Equals("/Embedding", StringComparison.OrdinalIgnoreCase)))
            {
                return ComServer();
            }

            if (args.Length >= 1 && args[0].StartsWith(ProtocolScheme + ":", StringComparison.OrdinalIgnoreCase))
            {
                Log($"PROTOCOL-ACTIVATED uri={args[0]}");
                return 0;
            }

            Log("PLAIN-LAUNCH (no mode recognised; this is what a launch with no activation data looks like)");
            return 0;
        }
        catch (Exception failure)
        {
            Log("FATAL " + failure);
            return 1;
        }
    }

    // ---------------- raising a toast ----------------
    private static int Show(string[] a)
    {
        var xml = File.ReadAllText(a[1]);
        var aumid = a[2];
        var tag = a[3];
        var group = a[4];
        var wait = int.Parse(a[5], System.Globalization.CultureInfo.InvariantCulture);
        var handlers = !Array.Exists(a, x => x == "nohandlers");
        var suppress = Array.Exists(a, x => x == "suppress");

        Check(RoInitialize(1 /* RO_INIT_MULTITHREADED */), "RoInitialize");

        // Windows.Data.Xml.Dom.XmlDocument: default-activatable, so RoActivateInstance.
        nint inspectable;
        var className = H("Windows.Data.Xml.Dom.XmlDocument");
        Check(RoActivateInstance(className, &inspectable), "RoActivateInstance(XmlDocument)");
        var xmlIo = QueryOrThrow(inspectable, IID_IXmlDocumentIO, "IXmlDocumentIO");
        // IXmlDocumentIO slot 6 = LoadXml(HSTRING)  [windows.data.xml.dom.idl:317]
        Check(((delegate* unmanaged<nint, nint, int>)Slot(xmlIo, 6))(xmlIo, H(xml)), "IXmlDocumentIO.LoadXml");
        var xmlDoc = QueryOrThrow(inspectable, IID_IXmlDocument, "IXmlDocument");

        // ToastNotification: activatable through IToastNotificationFactory.
        var factory = Factory("Windows.UI.Notifications.ToastNotification", IID_IToastNotificationFactory);
        nint toast;
        // IToastNotificationFactory slot 6 = CreateToastNotification(XmlDocument*, ToastNotification**)  [idl:1334]
        Check(((delegate* unmanaged<nint, nint, nint*, int>)Slot(factory, 6))(factory, xmlDoc, &toast), "IToastNotificationFactory.CreateToastNotification");
        s_toast = toast;

        var toast2 = QueryOrThrow(toast, IID_IToastNotification2, "IToastNotification2");
        // IToastNotification2 slot 6 = put_Tag, slot 8 = put_Group  [idl:1281,1283]
        Check(((delegate* unmanaged<nint, nint, int>)Slot(toast2, 6))(toast2, H(tag)), "put_Tag");
        Check(((delegate* unmanaged<nint, nint, int>)Slot(toast2, 8))(toast2, H(group)), "put_Group");

        if (suppress)
        {
            // IToastNotification2 slot 10 = put_SuppressPopup(boolean)  [idl:1285]
            Check(((delegate* unmanaged<nint, byte, int>)Slot(toast2, 10))(toast2, 1), "put_SuppressPopup");
        }

        if (handlers)
        {
            long token;
            s_dismissed = NewHandler(HandlerKind.Dismissed);
            s_activated = NewHandler(HandlerKind.Activated);
            s_failed = NewHandler(HandlerKind.Failed);
            // IToastNotification (default interface): slot 9 add_Dismissed, 11 add_Activated, 13 add_Failed  [idl:1268-1272]
            Check(((delegate* unmanaged<nint, nint, long*, int>)Slot(toast, 9))(toast, s_dismissed, &token), "add_Dismissed");
            Check(((delegate* unmanaged<nint, nint, long*, int>)Slot(toast, 11))(toast, s_activated, &token), "add_Activated");
            Check(((delegate* unmanaged<nint, nint, long*, int>)Slot(toast, 13))(toast, s_failed, &token), "add_Failed");
        }

        var statics = Factory("Windows.UI.Notifications.ToastNotificationManager", IID_IToastNotificationManagerStatics);
        nint notifier;
        // IToastNotificationManagerStatics slot 7 = CreateToastNotifierWithId(HSTRING, ToastNotifier**)  [idl:1414]
        Check(((delegate* unmanaged<nint, nint, nint*, int>)Slot(statics, 7))(statics, H(aumid), &notifier), "CreateToastNotifierWithId");
        s_notifier = notifier;

        int setting;
        // IToastNotifier slot 8 = get_Setting  [idl:1450]
        var hrSetting = ((delegate* unmanaged<nint, int*, int>)Slot(notifier, 8))(notifier, &setting);
        Log($"NOTIFIER aumid={aumid} get_Setting hr=0x{hrSetting:X8} setting={setting} (0 Enabled, 1 DisabledForApplication, 2 DisabledForUser, 3 DisabledByGroupPolicy, 4 DisabledByManifest)");

        Log($"BEFORE-SHOW {Foreground()}");
        // IToastNotifier slot 6 = Show(ToastNotification*)  [idl:1448]
        Check(((delegate* unmanaged<nint, nint, int>)Slot(notifier, 6))(notifier, toast), "IToastNotifier.Show");
        Log($"SHOWN aumid={aumid} tag={tag} group={group} handlers={handlers} suppressPopup={suppress} wait={wait}s {Foreground()}");
        // Sample the foreground window every 25 ms for 3 s and log every change, with the
        // owning process's image name, so a focus change can be attributed.
        var last = (nint)(-1);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 3000)
        {
            var window = GetForegroundWindow();
            if (window != last)
            {
                uint pid;
                _ = GetWindowThreadProcessId(window, &pid);
                string name;
                try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { name = "<gone>"; }
                Log($"FOREGROUND t+{clock.ElapsedMilliseconds}ms window=0x{window:X} pid={pid} process={name}");
                last = window;
            }

            Thread.Sleep(25);
        }

        if (Happened.Wait(TimeSpan.FromSeconds(wait)))
        {
            // Give a second event (for example Activated after Dismissed) time to arrive.
            Thread.Sleep(2000);
        }
        else
        {
            Log("SHOW-WAIT-EXPIRED no event arrived");
        }

        Log("SHOW-EXIT");
        return 0;
    }


    // ---------------- a scheduled toast: delivered by the platform with no process alive ----------------
    // winrt\windows.ui.notifications.idl:1045 IScheduledToastNotificationFactory, :1012 IScheduledToastNotification2
    private static readonly Guid IID_IScheduledToastNotificationFactory = new("E7BED191-0BB9-4189-8394-31761B476FD7");
    private static readonly Guid IID_IScheduledToastNotification2 = new("A66EA09C-31B4-43B0-B5DD-7A40E85363B1");

    private static int Schedule(string[] a)
    {
        var xml = File.ReadAllText(a[1]);
        var aumid = a[2];
        var tag = a[3];
        var group = a[4];
        var delay = int.Parse(a[5], System.Globalization.CultureInfo.InvariantCulture);
        var suppress = Array.Exists(a, x => x == "suppress");

        Check(RoInitialize(1), "RoInitialize");
        nint inspectable;
        Check(RoActivateInstance(H("Windows.Data.Xml.Dom.XmlDocument"), &inspectable), "RoActivateInstance(XmlDocument)");
        var xmlIo = QueryOrThrow(inspectable, IID_IXmlDocumentIO, "IXmlDocumentIO");
        Check(((delegate* unmanaged<nint, nint, int>)Slot(xmlIo, 6))(xmlIo, H(xml)), "LoadXml");
        var xmlDoc = QueryOrThrow(inspectable, IID_IXmlDocument, "IXmlDocument");

        var factory = Factory("Windows.UI.Notifications.ScheduledToastNotification", IID_IScheduledToastNotificationFactory);
        // Windows.Foundation.DateTime is { INT64 UniversalTime }: 100 ns since 1601-01-01 UTC, which is a FILETIME.
        var due = DateTime.UtcNow.AddSeconds(delay).ToFileTimeUtc();
        nint scheduled;
        // IScheduledToastNotificationFactory slot 6 = CreateScheduledToastNotification(XmlDocument*, DateTime, out)  [idl:1048]
        Check(((delegate* unmanaged<nint, nint, long, nint*, int>)Slot(factory, 6))(factory, xmlDoc, due, &scheduled), "CreateScheduledToastNotification");
        var scheduled2 = QueryOrThrow(scheduled, IID_IScheduledToastNotification2, "IScheduledToastNotification2");
        // IScheduledToastNotification2 slot 6 put_Tag, 8 put_Group, 10 put_SuppressPopup  [idl:1015-1019]
        Check(((delegate* unmanaged<nint, nint, int>)Slot(scheduled2, 6))(scheduled2, H(tag)), "put_Tag");
        Check(((delegate* unmanaged<nint, nint, int>)Slot(scheduled2, 8))(scheduled2, H(group)), "put_Group");
        if (suppress)
        {
            Check(((delegate* unmanaged<nint, byte, int>)Slot(scheduled2, 10))(scheduled2, 1), "put_SuppressPopup");
        }

        var statics = Factory("Windows.UI.Notifications.ToastNotificationManager", IID_IToastNotificationManagerStatics);
        nint notifier;
        Check(((delegate* unmanaged<nint, nint, nint*, int>)Slot(statics, 7))(statics, H(aumid), &notifier), "CreateToastNotifierWithId");
        // IToastNotifier slot 9 = AddToSchedule(ScheduledToastNotification*)  [idl:1451]
        Check(((delegate* unmanaged<nint, nint, int>)Slot(notifier, 9))(notifier, scheduled), "IToastNotifier.AddToSchedule");
        Log($"SCHEDULED aumid={aumid} tag={tag} group={group} suppressPopup={suppress} due={DateTime.FromFileTimeUtc(due):O}; this process now exits");
        return 0;
    }

    // ---------------- toast history ----------------
    private static nint HistoryObject()
    {
        Check(RoInitialize(1), "RoInitialize");
        var statics2 = Factory("Windows.UI.Notifications.ToastNotificationManager", IID_IToastNotificationManagerStatics2);
        nint history;
        // IToastNotificationManagerStatics2 slot 6 = get_History  [idl:1423]
        Check(((delegate* unmanaged<nint, nint*, int>)Slot(statics2, 6))(statics2, &history), "get_History");
        return history;
    }

    private static int History(string aumid)
    {
        var history = HistoryObject();
        var history2 = QueryOrThrow(history, IID_IToastNotificationHistory2, "IToastNotificationHistory2");
        nint view;
        // IToastNotificationHistory2 slot 7 = GetHistoryWithId(HSTRING, IVectorView<ToastNotification>**)  [idl:1357]
        Check(((delegate* unmanaged<nint, nint, nint*, int>)Slot(history2, 7))(history2, H(aumid), &view), "GetHistoryWithId");
        uint size;
        // IVectorView slot 7 = get_Size, slot 6 = GetAt  [windows.foundation.collections.h:546-547]
        Check(((delegate* unmanaged<nint, uint*, int>)Slot(view, 7))(view, &size), "IVectorView.get_Size");
        Log($"HISTORY aumid={aumid} count={size}");

        for (uint i = 0; i < size; i++)
        {
            nint item;
            Check(((delegate* unmanaged<nint, uint, nint*, int>)Slot(view, 6))(view, i, &item), "IVectorView.GetAt");
            var item2 = QueryOrThrow(item, IID_IToastNotification2, "IToastNotification2");
            nint tag, group;
            // IToastNotification2 slot 7 = get_Tag, slot 9 = get_Group  [idl:1282,1284]
            Check(((delegate* unmanaged<nint, nint*, int>)Slot(item2, 7))(item2, &tag), "get_Tag");
            Check(((delegate* unmanaged<nint, nint*, int>)Slot(item2, 9))(item2, &group), "get_Group");
            nint content;
            // IToastNotification slot 6 = get_Content  [idl:1265]
            var hrContent = ((delegate* unmanaged<nint, nint*, int>)Slot(item, 6))(item, &content);
            var xmlText = "<no content>";
            if (hrContent == 0 && content != 0 && Query(content, IID_IXmlNodeSerializer, out var serializer))
            {
                nint outer;
                // IXmlNodeSerializer slot 6 = GetXml  [windows.data.xml.dom.idl:492]
                var hrXml = ((delegate* unmanaged<nint, nint*, int>)Slot(serializer, 6))(serializer, &outer);
                xmlText = hrXml == 0 ? S(outer) : $"<GetXml hr=0x{hrXml:X8}>";
            }

            Log($"HISTORY   [{i}] tag={S(tag)} group={S(group)} xml={xmlText}");
        }

        return 0;
    }

    private static int Remove(string aumid, string tag, string group)
    {
        var history = HistoryObject();
        // IToastNotificationHistory slot 8 = RemoveGroupedTagWithId(tag, group, appId)  [idl:1344]
        var hr = ((delegate* unmanaged<nint, nint, nint, nint, int>)Slot(history, 8))(history, H(tag), H(group), H(aumid));
        Log($"REMOVE aumid={aumid} tag={tag} group={group} hr=0x{hr:X8}");
        return hr;
    }

    private static int Clear(string aumid)
    {
        var history = HistoryObject();
        // IToastNotificationHistory slot 12 = ClearWithId(appId)  [idl:1348]
        var hr = ((delegate* unmanaged<nint, nint, int>)Slot(history, 12))(history, H(aumid));
        Log($"CLEAR aumid={aumid} hr=0x{hr:X8}");
        return hr;
    }

    // ---------------- COM local server: INotificationActivationCallback ----------------
    private static int ComServer()
    {
        Check(CoInitializeEx(0, 0 /* COINIT_MULTITHREADED */), "CoInitializeEx");
        s_callbackObj = NewCallback();
        s_factoryObj = NewClassFactory();
        var clsid = CLSID_ProtoActivator;
        uint cookie;
        Check(CoRegisterClassObject(&clsid, s_factoryObj, 4 /* CLSCTX_LOCAL_SERVER */, 1 /* REGCLS_MULTIPLEUSE */, &cookie), "CoRegisterClassObject");
        Log("COM-SERVER registered the class object, waiting up to 30 s for Activate");

        if (Happened.Wait(TimeSpan.FromSeconds(30)))
        {
            Thread.Sleep(1500);
        }
        else
        {
            Log("COM-SERVER-WAIT-EXPIRED no Activate arrived");
        }

        _ = CoRevokeClassObject(cookie);
        Log("COM-SERVER-EXIT");
        return 0;
    }

    // ---------------- hand-built COM objects ----------------
    private enum HandlerKind
    {
        Activated,
        Dismissed,
        Failed,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComObject
    {
        public nint Vtable;
        public int RefCount;
        public int Kind;
        public nint Marshaler;
    }

    private static nint s_handlerVtable, s_callbackVtable, s_factoryVtable;

    private static nint NewHandler(HandlerKind kind)
    {
        if (s_handlerVtable == 0)
        {
            var v = (nint*)NativeMemory.Alloc(4, (nuint)sizeof(nint));
            v[0] = (nint)(delegate* unmanaged<ComObject*, Guid*, nint*, int>)&HandlerQueryInterface;
            v[1] = (nint)(delegate* unmanaged<ComObject*, uint>)&AddRef;
            v[2] = (nint)(delegate* unmanaged<ComObject*, uint>)&Release;
            v[3] = (nint)(delegate* unmanaged<ComObject*, nint, nint, int>)&HandlerInvoke;
            s_handlerVtable = (nint)v;
        }

        var obj = (ComObject*)NativeMemory.AllocZeroed((nuint)sizeof(ComObject));
        obj->Vtable = s_handlerVtable;
        obj->RefCount = 1;
        obj->Kind = (int)kind;
        nint ftm;
        // Aggregate the free-threaded marshaler, which is what makes a delegate agile.
        Check(CoCreateFreeThreadedMarshaler((nint)obj, &ftm), "CoCreateFreeThreadedMarshaler");
        obj->Marshaler = ftm;
        return (nint)obj;
    }

    private static Guid HandlerIid(int kind) => kind switch
    {
        (int)HandlerKind.Activated => IID_ActivatedHandler,
        (int)HandlerKind.Dismissed => IID_DismissedHandler,
        _ => IID_FailedHandler,
    };

    [UnmanagedCallersOnly]
    private static int HandlerQueryInterface(ComObject* self, Guid* iid, nint* result)
    {
        if (*iid == IID_IUnknown || *iid == IID_IAgileObject || *iid == HandlerIid(self->Kind))
        {
            *result = (nint)self;
            Interlocked.Increment(ref self->RefCount);
            return 0;
        }

        if (*iid == IID_IMarshal && self->Marshaler != 0)
        {
            return ((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(self->Marshaler, 0))(self->Marshaler, iid, result);
        }

        *result = 0;
        return E_NOINTERFACE;
    }

    [UnmanagedCallersOnly]
    private static uint AddRef(ComObject* self) => (uint)Interlocked.Increment(ref self->RefCount);

    [UnmanagedCallersOnly]
    private static uint Release(ComObject* self)
    {
        // Never freed: this is a scratch rig and a late release after exit costs nothing.
        var count = Interlocked.Decrement(ref self->RefCount);
        return (uint)Math.Max(count, 0);
    }

    [UnmanagedCallersOnly]
    private static int HandlerInvoke(ComObject* self, nint sender, nint args)
    {
        try
        {
            switch ((HandlerKind)self->Kind)
            {
                case HandlerKind.Activated:
                    Log("EVENT Activated " + DescribeActivation(args));
                    break;

                case HandlerKind.Dismissed:
                {
                    var reason = -1;
                    if (Query(args, IID_IToastDismissedEventArgs, out var dismissed))
                    {
                        // IToastDismissedEventArgs slot 6 = get_Reason  [idl:1249]
                        _ = ((delegate* unmanaged<nint, int*, int>)Slot(dismissed, 6))(dismissed, &reason);
                    }

                    Log($"EVENT Dismissed reason={reason} (0 UserCanceled, 1 ApplicationHidden, 2 TimedOut)");
                    break;
                }

                default:
                {
                    var code = 0;
                    if (Query(args, IID_IToastFailedEventArgs, out var failed))
                    {
                        // IToastFailedEventArgs slot 6 = get_ErrorCode  [idl:1257]
                        _ = ((delegate* unmanaged<nint, int*, int>)Slot(failed, 6))(failed, &code);
                    }

                    Log($"EVENT Failed errorCode=0x{code:X8}");
                    break;
                }
            }
        }
        catch (Exception failure)
        {
            Log("EVENT-HANDLER-THREW " + failure);
        }

        Happened.Set();
        return 0;
    }

    private static string DescribeActivation(nint args)
    {
        var sb = new StringBuilder();

        if (Query(args, IID_IToastActivatedEventArgs, out var activated))
        {
            nint arguments;
            // IToastActivatedEventArgs slot 6 = get_Arguments  [idl:1197]
            _ = ((delegate* unmanaged<nint, nint*, int>)Slot(activated, 6))(activated, &arguments);
            sb.Append($"arguments='{S(arguments)}'");
        }
        else
        {
            sb.Append("arguments=<args did not answer IToastActivatedEventArgs>");
        }

        if (Query(args, IID_IToastActivatedEventArgs2, out var activated2))
        {
            nint valueSet;
            // IToastActivatedEventArgs2 slot 6 = get_UserInput  [idl:1205]
            var hr = ((delegate* unmanaged<nint, nint*, int>)Slot(activated2, 6))(activated2, &valueSet);
            sb.Append($" userInput(hr=0x{hr:X8})={DescribeValueSet(valueSet)}");
        }
        else
        {
            sb.Append(" userInput=<no IToastActivatedEventArgs2>");
        }

        return sb.ToString();
    }

    private static string DescribeValueSet(nint valueSet)
    {
        if (valueSet == 0)
        {
            return "<null>";
        }

        var sb = new StringBuilder("{");

        if (Query(valueSet, IID_IIterable_KVP, out var iterable))
        {
            nint iterator;
            // IIterable slot 6 = First  [windows.foundation.collections.h:300]
            _ = ((delegate* unmanaged<nint, nint*, int>)Slot(iterable, 6))(iterable, &iterator);
            byte hasCurrent;
            // IIterator slot 7 = get_HasCurrent, 6 = get_Current, 8 = MoveNext  [collections.h:506-508]
            _ = ((delegate* unmanaged<nint, byte*, int>)Slot(iterator, 7))(iterator, &hasCurrent);

            while (hasCurrent != 0)
            {
                nint pair;
                _ = ((delegate* unmanaged<nint, nint*, int>)Slot(iterator, 6))(iterator, &pair);
                nint key, value;
                // IKeyValuePair slot 6 = get_Key, 7 = get_Value  [collections.h:651-652]
                _ = ((delegate* unmanaged<nint, nint*, int>)Slot(pair, 6))(pair, &key);
                _ = ((delegate* unmanaged<nint, nint*, int>)Slot(pair, 7))(pair, &value);
                sb.Append($" {S(key)}='{StringOf(value)}'");
                _ = ((delegate* unmanaged<nint, byte*, int>)Slot(iterator, 8))(iterator, &hasCurrent);
            }
        }
        else
        {
            sb.Append(" <ValueSet did not answer IIterable>");
        }

        return sb.Append(" }").ToString();
    }

    private static string StringOf(nint inspectable)
    {
        if (inspectable == 0)
        {
            return "<null>";
        }

        if (!Query(inspectable, IID_IPropertyValue, out var propertyValue))
        {
            return "<not an IPropertyValue>";
        }

        nint text;
        // IPropertyValue slot 19 = GetString  [windows.foundation.idl:649]
        var hr = ((delegate* unmanaged<nint, nint*, int>)Slot(propertyValue, 19))(propertyValue, &text);
        return hr == 0 ? S(text) : $"<GetString hr=0x{hr:X8}>";
    }

    private static nint NewCallback()
    {
        var v = (nint*)NativeMemory.Alloc(4, (nuint)sizeof(nint));
        v[0] = (nint)(delegate* unmanaged<ComObject*, Guid*, nint*, int>)&CallbackQueryInterface;
        v[1] = (nint)(delegate* unmanaged<ComObject*, uint>)&AddRef;
        v[2] = (nint)(delegate* unmanaged<ComObject*, uint>)&Release;
        v[3] = (nint)(delegate* unmanaged<ComObject*, char*, char*, UserInputData*, uint, int>)&Activate;
        s_callbackVtable = (nint)v;
        var obj = (ComObject*)NativeMemory.AllocZeroed((nuint)sizeof(ComObject));
        obj->Vtable = s_callbackVtable;
        obj->RefCount = 1;
        return (nint)obj;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UserInputData
    {
        public char* Key;
        public char* Value;
    }

    [UnmanagedCallersOnly]
    private static int CallbackQueryInterface(ComObject* self, Guid* iid, nint* result)
    {
        if (*iid == IID_IUnknown || *iid == IID_INotificationActivationCallback)
        {
            *result = (nint)self;
            Interlocked.Increment(ref self->RefCount);
            return 0;
        }

        *result = 0;
        return E_NOINTERFACE;
    }

    // INotificationActivationCallback slot 3 = Activate(appUserModelId, invokedArgs, data, count)
    // [um\NotificationActivationCallback.idl:26-30]
    [UnmanagedCallersOnly]
    private static int Activate(ComObject* self, char* appUserModelId, char* invokedArgs, UserInputData* data, uint count)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"COM-ACTIVATE aumid='{new string(appUserModelId)}' invokedArgs='{(invokedArgs == null ? "<null>" : new string(invokedArgs))}' count={count}");

            for (var i = 0; i < count; i++)
            {
                sb.Append($" [{new string(data[i].Key)}='{new string(data[i].Value)}']");
            }

            Log(sb.ToString());
        }
        catch (Exception failure)
        {
            Log("COM-ACTIVATE-THREW " + failure);
        }

        Happened.Set();
        return 0;
    }

    private static nint NewClassFactory()
    {
        var v = (nint*)NativeMemory.Alloc(5, (nuint)sizeof(nint));
        v[0] = (nint)(delegate* unmanaged<ComObject*, Guid*, nint*, int>)&FactoryQueryInterface;
        v[1] = (nint)(delegate* unmanaged<ComObject*, uint>)&AddRef;
        v[2] = (nint)(delegate* unmanaged<ComObject*, uint>)&Release;
        v[3] = (nint)(delegate* unmanaged<ComObject*, nint, Guid*, nint*, int>)&CreateInstance;
        v[4] = (nint)(delegate* unmanaged<ComObject*, int, int>)&LockServer;
        s_factoryVtable = (nint)v;
        var obj = (ComObject*)NativeMemory.AllocZeroed((nuint)sizeof(ComObject));
        obj->Vtable = s_factoryVtable;
        obj->RefCount = 1;
        return (nint)obj;
    }

    [UnmanagedCallersOnly]
    private static int FactoryQueryInterface(ComObject* self, Guid* iid, nint* result)
    {
        if (*iid == IID_IUnknown || *iid == IID_IClassFactory)
        {
            *result = (nint)self;
            Interlocked.Increment(ref self->RefCount);
            return 0;
        }

        *result = 0;
        return E_NOINTERFACE;
    }

    // IClassFactory slot 3 = CreateInstance, slot 4 = LockServer  [um\Unknwnbase.idl, IClassFactory]
    [UnmanagedCallersOnly]
    private static int CreateInstance(ComObject* self, nint outer, Guid* iid, nint* result)
    {
        Log($"COM-FACTORY CreateInstance iid={*iid}");

        if (outer != 0)
        {
            *result = 0;
            return CLASS_E_NOAGGREGATION;
        }

        var callback = (ComObject*)s_callbackObj;

        if (*iid == IID_IUnknown || *iid == IID_INotificationActivationCallback)
        {
            *result = s_callbackObj;
            Interlocked.Increment(ref callback->RefCount);
            return 0;
        }

        *result = 0;
        return E_NOINTERFACE;
    }

    [UnmanagedCallersOnly]
    private static int LockServer(ComObject* self, int doLock) => 0;

    // ---------------- helpers ----------------
    private static nint Slot(nint obj, int index) => (*(nint**)obj)[index];

    private static bool Query(nint obj, Guid iid, out nint result)
    {
        nint r;
        var hr = ((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(obj, 0))(obj, &iid, &r);
        result = r;
        return hr == 0 && r != 0;
    }

    private static nint QueryOrThrow(nint obj, Guid iid, string name)
    {
        nint r;
        var hr = ((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(obj, 0))(obj, &iid, &r);
        Check(hr, "QueryInterface(" + name + ")");
        return r;
    }

    private static nint Factory(string runtimeClass, Guid iid)
    {
        nint factory;
        Check(RoGetActivationFactory(H(runtimeClass), &iid, &factory), "RoGetActivationFactory(" + runtimeClass + ")");
        return factory;
    }

    private static nint H(string value)
    {
        nint h;
        fixed (char* p = value)
        {
            Check(WindowsCreateString(p, (uint)value.Length, &h), "WindowsCreateString");
        }

        return h;
    }

    private static string S(nint h)
    {
        if (h == 0)
        {
            return string.Empty;
        }

        uint length;
        var p = WindowsGetStringRawBuffer(h, &length);
        return new string(p, 0, (int)length);
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            Log($"FAILED {what} hr=0x{hr:X8}");
            throw new InvalidOperationException($"{what} failed with 0x{hr:X8}");
        }
    }
}
