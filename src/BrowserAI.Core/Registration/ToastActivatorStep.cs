// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using BrowserAI.Interop;
using BrowserAI.Updates;

namespace BrowserAI.Registration;

/// <summary>The hooks' step for the toasts' activator: registered at an install or an update, removed at an uninstall.</summary>
/// <remarks>
/// <b>T, decided 2026-10-08</b>, built by lane UI as
/// <see cref="ToastActivatorRegistration"/>: the class lives under the user's own
/// classes and is derived from the application id, so the suite's test pack and a real
/// install never share one. This step only calls it, with the install's own
/// <c>current\BrowserAI.exe</c>, and turns every outcome into a sentence, because a
/// hook never fails on a toast.
/// </remarks>
internal static class ToastActivatorStep
{
    /// <summary>Registers or removes the activator for one install.</summary>
    /// <param name="intent">Which hook is running.</param>
    /// <param name="installRoot">The install root.</param>
    /// <returns>
    /// A sentence for the process log, the one place the hook writes it. <i>Corrected
    /// 2026-10-10, round 2 of the texts review, first page 142 (previously "A sentence for
    /// the installer's log.")</i>.
    /// </returns>
    public static string Apply(RegistrationIntent intent, string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        if (ToastInterop.CurrentAppUserModelId() is not { Length: > 0 } id)
        {
            return "This process runs under no application id, so the toasts' activator was not changed.";
        }

        try
        {
            using var classes = ToastActivatorRegistration.UserClasses();

            var executable = Path.Combine(installRoot, RegistrationTarget.CurrentDirectoryName, RegistrationTarget.AppFileName);

            if (intent is RegistrationIntent.Uninstall)
            {
                // Another install of the same pack id owns a class that starts its own
                // program: it is left to that install (9 a, 2026-10-10).
                return ToastActivatorRegistration.Unregister(classes, id, executable)
                    ? $"Removed for '{id}'."
                    : $"Left for '{id}': it starts another install's program, not '{executable}'.";
            }

            ToastActivatorRegistration.Register(classes, id, executable);
            return $"Registered for '{id}', starting '{executable}'.";
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return $"Not changed for '{id}': {failure.Message}";
        }
    }
}
