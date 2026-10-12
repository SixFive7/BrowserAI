// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Hosting;
using BrowserAI.Registration;
using BrowserAI.Tests.Harness;
using Microsoft.Win32;

namespace BrowserAI.Tests;

/// <summary>
/// The scheme of the dashboard's folder links: what each pack's is called, the command
/// Windows starts for it, and the hooks' step that registers it and takes it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>55 a and 72 a, the maintainer's words of 2026-10-11 verbatim: <i>"55 a"</i> and
/// <i>"72 a"</i></b>: a folder button on the page is a <c>browserai:</c> link, registered per
/// user by the install and update hooks and removed by the uninstall hook.
/// </para>
/// <para>
/// ⚠️ <b>No arm writes or removes <c>HKCU\Software\Classes\browserai</c></b>, the maintainer's
/// real registration, nor any key under the user's own classes: the step is handed a scratch
/// key under <c>HKCU\Software\BrowserAI.Tests</c>, and the suite's banned-symbol list refuses
/// <c>LinkScheme.UserClasses</c>. The test pack the real-installer arms install runs the
/// real hooks, which write the test pack's own scheme, never the shipping one, which the first
/// arm holds.
/// </para>
/// </remarks>
internal sealed class LinkSchemeTests
{
    /// <summary>
    /// The shipping pack's scheme is <c>browserai</c>, the test pack's is one of its own, and no
    /// other pack's is <c>browserai</c>, however its id is spelled.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task OnlyTheShippingPackIsNamedBrowserAiAndTheTestPackHasASchemeOfItsOwn()
    {
        await Assert.That(LinkScheme.NameFor(StandardLocation.ShippingPackId)).IsEqualTo("browserai");
        await Assert.That(LinkScheme.NameFor("browserai.APP")).IsEqualTo("browserai");
        await Assert.That(LinkScheme.NameFor(ReleaseLayout.TestPackId)).IsEqualTo("browserai-test");
        await Assert.That(LinkScheme.NameFor(null)).IsNull();
        await Assert.That(LinkScheme.NameFor(string.Empty)).IsNull();

        string[] others = ["BrowserAI.app.test", "BrowserAI", "browserai", "Other.Product", "BrowserAI.app.", "BrowserAI.app.x-y_z", "Pack With Spaces"];

        foreach (var id in others)
        {
            var scheme = LinkScheme.NameFor(id);

            await Assert.That(scheme).IsNotNull().Because(id);
            await Assert.That(scheme).IsNotEqualTo(LinkScheme.ShippingScheme).Because(id);
            await Assert.That(scheme!.StartsWith(LinkScheme.ShippingScheme + "-", StringComparison.Ordinal)).IsTrue().Because(id);
            await Assert.That(scheme.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-')).IsTrue().Because(scheme);
        }
    }

    /// <summary>
    /// The install and update hooks' step registers the install's command under the key it is
    /// handed, with the data root the installer named, and the uninstall hook's takes the whole
    /// key back.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task TheStepRegistersTheInstallsCommandUnderTheKeyItIsHandedAndTakesItBack()
    {
        using var scratch = await ScratchDirectory.CreateAsync("link-scheme");
        var path = $@"Software\BrowserAI.Tests\LinkClasses-{Guid.NewGuid():N}";

        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(path, writable: true);

            var installRoot = Path.Combine(scratch.Path, "install");
            var dataRoot = Path.Combine(scratch.Path, "data");
            var executable = Path.Combine(installRoot, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);

            var said = LinkSchemeStep.ApplyTo(classes, RegistrationIntent.Install, installRoot, ReleaseLayout.TestPackId, new InstallerSettings(dataRoot, null));

            await Assert.That(said).Contains("Registered 'browserai-test:'");

            using (var key = classes.OpenSubKey("browserai-test"))
            {
                await Assert.That(key).IsNotNull();
                await Assert.That(key!.GetValue(string.Empty) as string).IsEqualTo("URL:BrowserAI");
                await Assert.That(key.GetValueNames()).Contains(LinkScheme.UrlProtocolValue);
                await Assert.That(key.GetValue(LinkScheme.UrlProtocolValue) as string).IsEqualTo(string.Empty);
            }

            await Assert.That(LinkScheme.CommandOf(classes, "browserai-test"))
                .IsEqualTo($"\"{executable}\" --link \"%1\" --data-root \"{dataRoot}\"");

            // Nothing of the shipping scheme, which a test install must never touch.
            using (var shipping = classes.OpenSubKey(LinkScheme.ShippingScheme))
            {
                await Assert.That(shipping).IsNull();
            }

            // An update writes the same over it, and the shipping install's names no data root.
            _ = LinkSchemeStep.ApplyTo(classes, RegistrationIntent.Update, installRoot, StandardLocation.ShippingPackId, InstallerSettings.None);

            await Assert.That(LinkScheme.CommandOf(classes, "browserai")).IsEqualTo($"\"{executable}\" --link \"%1\"");

            var removed = LinkSchemeStep.ApplyTo(classes, RegistrationIntent.Uninstall, installRoot, ReleaseLayout.TestPackId, new InstallerSettings(dataRoot, null));

            await Assert.That(removed).IsEqualTo("Removed 'browserai-test:'.");

            using (var gone = classes.OpenSubKey("browserai-test"))
            {
                await Assert.That(gone).IsNull();
            }

            // A pack id of none registers nothing.
            await Assert.That(LinkSchemeStep.ApplyTo(classes, RegistrationIntent.Install, installRoot, null, InstallerSettings.None)).StartsWith("Not changed");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    /// <summary>
    /// An uninstall leaves a scheme whose command starts another install's program: two
    /// installs of one pack share one scheme, and the one that registered last owns it.
    /// </summary>
    /// <returns>The assertion task.</returns>
    [Test]
    public async Task AnUninstallLeavesASchemeWhoseCommandStartsAnotherInstallsProgram()
    {
        using var scratch = await ScratchDirectory.CreateAsync("link-scheme-other");
        var path = $@"Software\BrowserAI.Tests\LinkClasses-{Guid.NewGuid():N}";

        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(path, writable: true);

            var mine = Path.Combine(scratch.Path, "mine");
            var other = Path.Combine(scratch.Path, "other");

            _ = LinkSchemeStep.ApplyTo(classes, RegistrationIntent.Install, mine, ReleaseLayout.TestPackId, InstallerSettings.None);
            _ = LinkSchemeStep.ApplyTo(classes, RegistrationIntent.Install, other, ReleaseLayout.TestPackId, InstallerSettings.None);

            var said = LinkSchemeStep.ApplyTo(classes, RegistrationIntent.Uninstall, mine, ReleaseLayout.TestPackId, InstallerSettings.None);

            await Assert.That(said).StartsWith("Left 'browserai-test:'");
            await Assert.That(LinkScheme.CommandOf(classes, "browserai-test")).Contains(other);

            // The owner's own uninstall takes it.
            _ = LinkSchemeStep.ApplyTo(classes, RegistrationIntent.Uninstall, other, ReleaseLayout.TestPackId, InstallerSettings.None);

            await Assert.That(LinkScheme.CommandOf(classes, "browserai-test")).IsNull();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }
}
