// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using BrowserAI.Interop;
using BrowserAI.Updates;
using Microsoft.Win32;

namespace BrowserAI.Tests;

/// <summary>
/// The toast activator: its class, its registration, the click it receives and
/// what each click does, with nothing registered where Windows would read it and no
/// toast raised.
/// </summary>
/// <remarks>
/// <para>
/// <b>The registration is written under a scratch key</b>, <c>HKCU\Software\BrowserAI.Tests\...</c>,
/// handed to the product's own code in place of <c>HKCU\Software\Classes</c> and
/// deleted when the arm ends: the property is the registry's, and a store kept in
/// memory could not have it. Windows never reads that key, so no click can reach
/// what an arm registers.
/// </para>
/// <para>
/// <b>The click is delivered in process</b>, through the activator's own class
/// object, the way COM delivers it once the class is registered; no class is
/// registered with COM here.
/// </para>
/// </remarks>
internal sealed class ToastActivationTests
{
    /// <summary>
    /// The class is derived from the application id: the same id always gives the
    /// same class, the test pack's id another, and the derivation is pinned, because
    /// changing it would move every install's class.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheActivatorsClassIsDerivedFromTheApplicationIdAndPinned()
    {
        var real = ToastActivatorRegistration.ClassFor("velopack.BrowserAI.app");
        var test = ToastActivatorRegistration.ClassFor("velopack.BrowserAI.app.test");

        await Assert.That(real).IsEqualTo(ToastActivatorRegistration.ClassFor("velopack.BrowserAI.app"));
        await Assert.That(test).IsNotEqualTo(real);
        await Assert.That(real).IsNotEqualTo(Guid.Empty);

        // RFC 9562's version 8 and its variant, in the places the RFC puts them.
        var text = real.ToString("D");

        await Assert.That(text[14]).IsEqualTo('8');
        await Assert.That("89ab".Contains(text[19], StringComparison.Ordinal)).IsTrue();
        await Assert.That(real.ToString("B").ToUpperInvariant()).IsEqualTo(Pinned);
    }

    /// <summary>
    /// Registering writes exactly the two values the 2026-09-24 measurement used, for
    /// each id separately, and unregistering takes one id's back and leaves the
    /// other's, and leaves anything it did not write.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task RegistrationWritesBothValuesAndUnregistrationTakesBackOnlyItsOwn()
    {
        var path = $@"Software\BrowserAI.Tests\ToastClasses-{Guid.NewGuid():N}";

        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(path, writable: true);

            const string Real = "velopack.BrowserAI.app";
            const string Test = "velopack.BrowserAI.app.test";
            const string Executable = @"C:\Users\someone\AppData\Local\BrowserAI.app\current\BrowserAI.exe";

            ToastActivatorRegistration.Register(classes, Real, Executable);
            ToastActivatorRegistration.Register(classes, Test, @"C:\elsewhere\current\BrowserAI.exe");

            var realClass = ToastActivatorRegistration.ClassFor(Real).ToString("B").ToUpperInvariant();
            var testClass = ToastActivatorRegistration.ClassFor(Test).ToString("B").ToUpperInvariant();

            await Assert.That(Value(classes, $@"CLSID\{realClass}\LocalServer32", string.Empty)).IsEqualTo($"\"{Executable}\" -ToastActivated");
            await Assert.That(Value(classes, $@"AppUserModelId\{Real}", "CustomActivator")).IsEqualTo(realClass);
            await Assert.That(Value(classes, $@"CLSID\{testClass}\LocalServer32", string.Empty)).IsEqualTo("\"C:\\elsewhere\\current\\BrowserAI.exe\" -ToastActivated");
            await Assert.That(Value(classes, $@"AppUserModelId\{Test}", "CustomActivator")).IsEqualTo(testClass);

            // Something BrowserAI did not write, beside its own value.
            using (var id = classes.OpenSubKey($@"AppUserModelId\{Test}", writable: true)!)
            {
                id.SetValue("DisplayName", "Somebody's own");
            }

            await Assert.That(ToastActivatorRegistration.Unregister(classes, Real, Executable)).IsTrue();
            await Assert.That(ToastActivatorRegistration.Unregister(classes, Test, @"C:\elsewhere\current\BrowserAI.exe")).IsTrue();
            await Assert.That(ToastActivatorRegistration.Unregister(classes, Real, Executable)).IsTrue();

            await Assert.That(Exists(classes, $@"CLSID\{realClass}")).IsFalse();
            await Assert.That(Exists(classes, $@"AppUserModelId\{Real}")).IsFalse();
            await Assert.That(Exists(classes, $@"CLSID\{testClass}")).IsFalse();
            await Assert.That(Value(classes, $@"AppUserModelId\{Test}", "CustomActivator")).IsNull();
            await Assert.That(Value(classes, $@"AppUserModelId\{Test}", "DisplayName")).IsEqualTo("Somebody's own");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    /// <summary>
    /// Two installs of one pack id share one class, which starts the program of the
    /// install that registered last; uninstalling the other leaves it whole, and
    /// uninstalling the one it starts takes it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found by lane ARCH's helper T3 reading the hooks on 2026-10-09</b>, and closed
    /// 2026-10-10 with the maintainer's 9 a: the class is keyed to the application id,
    /// which an install root does not change, and every uninstall took it back, so
    /// uninstalling either of two installs left the other's toasts starting nothing.
    /// The two installs here are two roots of the test pack's id, the suite's own case,
    /// registered under a scratch key the way the hooks register them.
    /// </para>
    /// <para>
    /// <b>Planted red 2026-10-10</b> against an unregistration that took the class back
    /// whatever program it started.
    /// </para>
    /// </remarks>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task UninstallingOneOfTwoInstallsOfOnePackLeavesTheClassToTheOther()
    {
        var path = $@"Software\BrowserAI.Tests\ToastClasses-{Guid.NewGuid():N}";

        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(path, writable: true);

            const string Id = "velopack.BrowserAI.app.test";
            const string First = @"C:\Users\someone\AppData\Local\BrowserAI-test-scratch\first\current\BrowserAI.exe";
            const string Second = @"C:\Users\someone\AppData\Local\BrowserAI-test-scratch\second\current\BrowserAI.exe";

            var activator = ToastActivatorRegistration.ClassFor(Id).ToString("B").ToUpperInvariant();

            ToastActivatorRegistration.Register(classes, Id, First);
            ToastActivatorRegistration.Register(classes, Id, Second);

            await Assert.That(Value(classes, $@"CLSID\{activator}\LocalServer32", string.Empty)).IsEqualTo($"\"{Second}\" -ToastActivated");

            // The first install goes: the class starts the second's program, and stays.
            await Assert.That(ToastActivatorRegistration.Unregister(classes, Id, First)).IsFalse();
            await Assert.That(Value(classes, $@"CLSID\{activator}\LocalServer32", string.Empty)).IsEqualTo($"\"{Second}\" -ToastActivated");
            await Assert.That(Value(classes, $@"AppUserModelId\{Id}", "CustomActivator")).IsEqualTo(activator);

            // A spelling of the second's path in another case is the second's.
            await Assert.That(ToastActivatorRegistration.Unregister(classes, Id, Second.ToUpperInvariant())).IsTrue();
            await Assert.That(Exists(classes, $@"CLSID\{activator}")).IsFalse();
            await Assert.That(Exists(classes, $@"AppUserModelId\{Id}")).IsFalse();

            // A class with no program named is nobody's and goes with any install.
            ToastActivatorRegistration.Register(classes, Id, First);

            using (var server = classes.OpenSubKey($@"CLSID\{activator}\LocalServer32", writable: true)!)
            {
                server.DeleteValue(string.Empty);
            }

            await Assert.That(ToastActivatorRegistration.Unregister(classes, Id, Second)).IsTrue();
            await Assert.That(Exists(classes, $@"CLSID\{activator}")).IsFalse();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    /// <summary>
    /// <i>Install now</i> and a click on the ready toast open the update page,
    /// <i>Changelog</i> opens the changelog page, <i>Wait for inactivity</i> records
    /// its version and opens nothing, and <i>Dismiss</i> and anything unknown do
    /// nothing.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AClickOpensItsPageOrRemembersTheWaitAndNothingElse()
    {
        var opened = new List<string>();
        var memory = new Remembered();

        int open(string page)
        {
            opened.Add(page);
            return 7;
        }

        await Assert.That(ToastActivation.Act(new ToastClick(ToastAction.UpdatePage, "1.2.0"), memory, open)).IsEqualTo(7);
        await Assert.That(ToastActivation.Act(new ToastClick(ToastAction.Changelog, "1.2.0"), memory, open)).IsEqualTo(7);
        await Assert.That(string.Join(",", opened)).IsEqualTo("update,changelog");
        await Assert.That(memory.Version).IsNull();

        await Assert.That(ToastActivation.Act(new ToastClick(ToastAction.Wait, "1.2.0"), memory, open)).IsEqualTo(0);
        await Assert.That(memory.Version).IsEqualTo("1.2.0");

        await Assert.That(ToastActivation.Act(new ToastClick(ToastAction.Dismiss, null), memory, open)).IsEqualTo(0);
        await Assert.That(ToastActivation.Act(new ToastClick(ToastAction.None, null), memory, open)).IsEqualTo(0);
        await Assert.That(ToastActivation.Act(null, memory, open)).IsEqualTo(0);
        await Assert.That(string.Join(",", opened)).IsEqualTo("update,changelog");
    }

    /// <summary>
    /// A click handed to the activator's class object, the way COM hands it once the
    /// class is registered, arrives as the click its arguments name.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheActivatorTakesAClickThroughItsOwnClassObject()
    {
        var clicked = new TaskCompletionSource<ToastClick>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrappers = new StrategyBasedComWrappers();
        var factory = ToastActivation.NewFactory(clicked);

        try
        {
            var classObject = (IClassFactory)wrappers.GetOrCreateObjectForComInstance(factory, CreateObjectFlags.None);
            var callbackId = typeof(INotificationActivationCallback).GUID;

            classObject.CreateInstance(0, callbackId, out var instance);

            try
            {
                var callback = (INotificationActivationCallback)wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.None);

                callback.Activate("velopack.BrowserAI.app", "action=wait&version=1.2.0", 0, 0);
            }
            finally
            {
                _ = Marshal.Release(instance);
            }

            // A class object asked to aggregate refuses, as COM requires.
            await Assert.That(() => classObject.CreateInstance(1, callbackId, out _)).ThrowsException();
        }
        finally
        {
            _ = Marshal.Release(factory);
        }

        await Assert.That(clicked.Task.IsCompleted).IsTrue();
        await Assert.That(await clicked.Task).IsEqualTo(new ToastClick(ToastAction.Wait, "1.2.0"));
    }

    /// <summary>Only COM's start for a click is the activator's: its argument, in any case.</summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OnlyComsStartForAClickIsTheActivators()
    {
        await Assert.That(ToastActivation.IsActivation(["-ToastActivated", "-Embedding"])).IsTrue();
        await Assert.That(ToastActivation.IsActivation(["-toastactivated"])).IsTrue();
        await Assert.That(ToastActivation.IsActivation(["--sessions"])).IsFalse();
        await Assert.That(ToastActivation.IsActivation(["-Embedding"])).IsFalse();
        await Assert.That(ToastActivation.IsActivation([])).IsFalse();
    }

    /// <summary>
    /// The class <c>velopack.BrowserAI.app</c> derives, written down once: computed on
    /// 2026-10-08 by a second implementation of the same derivation, in Python's
    /// <c>hashlib</c> and <c>uuid</c>, and not by the code under test.
    /// </summary>
    private const string Pinned = "{61F9DAEF-5FD9-8AF7-BE69-E671E722E32A}";

    private static string? Value(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);

        return key?.GetValue(name) as string;
    }

    private static bool Exists(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);

        return key is not null;
    }

    /// <summary>The person's wait, kept in this process.</summary>
    private sealed class Remembered : IUpdateToastMemory
    {
        public string? Version { get; private set; }

        public string? WaitedFor() => Version;

        public void Waited(string version) => Version = version;

        public void Forget() => Version = null;
    }
}
