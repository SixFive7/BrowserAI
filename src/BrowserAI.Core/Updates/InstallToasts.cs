// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using Microsoft.Extensions.Logging;

namespace BrowserAI.Updates;

/// <summary>What a session's open learned about the install, as the background is told it.</summary>
/// <remarks>
/// <b>The maintainer's 10 b, 2026-10-10.</b> Each session's browser server is asked for
/// its tool list right after its handshake and held to the list compiled into the binary
/// (step 1 of the one-binary plan): a difference refuses that session as a broken install.
/// The session host tells this seam what each open found, so the person is told as well as
/// the model.
/// </remarks>
internal interface IInstallHealth
{
    /// <summary>A session's browser server listed different tools: the install is broken.</summary>
    /// <param name="difference">The first difference, as the refusal words it.</param>
    void Broken(string difference);

    /// <summary>A session's browser server listed the tools this binary was built with.</summary>
    void Intact();
}

/// <summary>The broken install's toast: its words, its tag and what a click on it asks for.</summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 10 b, 2026-10-10, as it was asked for</b>: one toast per
/// background run, through the update toasts' machinery, under a tag of its own, with no
/// timeout like the update toasts, and a button that opens the dashboard's explanation of
/// how to reinstall: download <c>BrowserAI.exe</c> from the latest release and run it, and the
/// data is kept. <i>Corrected 2026-10-10 (previously "run <c>BrowserAI-win-Setup.exe</c>
/// again"): the release ships the installer as <c>BrowserAI.exe</c>, and README's recovery
/// note names it that way.</i>
/// </para>
/// <para>
/// <b>Its own tag and its own group</b>, so raising an update toast, which removes the
/// other update toasts first, never takes this one with it, and this one never takes an
/// update toast.
/// </para>
/// </remarks>
internal static class InstallToastContent
{
    /// <summary>The group the broken install's toast carries.</summary>
    public const string Group = "install";

    /// <summary>The broken install's toast's tag.</summary>
    public const string Tag = "install-broken";

    /// <summary>The toast that says BrowserAI needs reinstalling.</summary>
    /// <returns>Its XML.</returns>
    public static string Broken() =>
        UpdateToastContent.Toast(
            UpdateToastContent.Arguments(ToastAction.StatusPage, null),
            [
                "BrowserAI needs reinstalling",
                // ⚠️ Corrected 2026-10-10, the texts polish, page #78 (previously "...
                // so no browser session can open. Download BrowserAI.exe from the latest
                // release and run it; ..."): an install that takes its updates from a
                // folder on this computer is not reinstalled from the release, and the
                // status page the button opens says how for both.
                "Part of this install does not match the rest, so no browser session opens. Reinstall BrowserAI the way you installed it; your sessions and their files are kept.",
            ],
            null,
            ("How to reinstall", UpdateToastContent.Arguments(ToastAction.StatusPage, null)),
            ("Dismiss", UpdateToastContent.Arguments(ToastAction.Dismiss, null)));
}

/// <summary>
/// The broken install as the person meets it: one toast per background run, and the
/// difference the dashboard shows for as long as the condition stands.
/// </summary>
/// <remarks>
/// <para>
/// <b>The maintainer's 10 b, 2026-10-10.</b> The first session refused as a broken
/// install raises the toast; every later one only updates the difference, because the
/// install is the same install and one toast already says what to do. A session whose
/// browser server lists the tools this binary was built with again ends the condition: the
/// notice goes from the dashboard, and the toast goes too, since what it says is no longer
/// true. The toast is not raised a second time in the same run.
/// </para>
/// <para>
/// <b>Nothing here throws into its caller</b>, which is a session's open: a toast Windows
/// refuses is a log record, and the refusal the model reads goes out either way.
/// </para>
/// </remarks>
internal sealed partial class BrokenInstallNotice : IInstallHealth, IDisposable
{
    private readonly Lock _gate = new();
    private readonly IToastSurface? _surface;
    private readonly ILogger _logger;
    private readonly IDisposable? _owned;

    private string? _difference;
    private bool _raised;
    private bool _shown;

    /// <summary>The notice for one background run.</summary>
    /// <param name="surface">What shows the toast, or <see langword="null"/> in a process that runs under no application id and raises none.</param>
    /// <param name="logger">Where a toast Windows refuses is recorded.</param>
    /// <param name="owned">What this disposes with itself: the product's surface, which the background made for it.</param>
    public BrokenInstallNotice(IToastSurface? surface, ILogger logger, IDisposable? owned = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _surface = surface;
        _logger = logger;
        _owned = owned;
    }

    /// <summary>Called whenever the condition starts, changes or ends: the dashboard sends every open tab its new state.</summary>
    public Action? Changed { get; set; }

    /// <summary>The first difference the latest refused session met, or <see langword="null"/> while the install is not known to be broken.</summary>
    public string? Difference
    {
        get
        {
            lock (_gate)
            {
                return _difference;
            }
        }
    }

    /// <inheritdoc />
    public void Broken(string difference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(difference);

        bool raise;

        lock (_gate)
        {
            _difference = difference;
            raise = !_raised;
            _raised = true;
        }

        if (raise && _surface is { } surface)
        {
            try
            {
                surface.Show(InstallToastContent.Tag, InstallToastContent.Group, new ToastRequest(InstallToastContent.Broken(), null, SuppressPopup: false, ExpiresOnReboot: false), 0);

                lock (_gate)
                {
                    _shown = true;
                }

                InstallToastsLog.Shown(_logger, difference);
            }
#pragma warning disable CA1031 // A toast Windows refused is a log record; the session's refusal goes out either way.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                InstallToastsLog.ShowThrew(_logger, failure);
            }
        }

        Tell();
    }

    /// <inheritdoc />
    public void Intact()
    {
        bool remove;

        lock (_gate)
        {
            if (_difference is null)
            {
                return;
            }

            _difference = null;
            remove = _shown;
            _shown = false;
        }

        if (remove && _surface is { } surface)
        {
            try
            {
                surface.Remove(InstallToastContent.Tag, InstallToastContent.Group);
            }
#pragma warning disable CA1031 // A toast that could not be removed is a log record.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                InstallToastsLog.RemoveThrew(_logger, failure);
            }
        }

        Tell();
    }

    /// <summary>Lets go of the surface when this owns it.</summary>
    public void Dispose() => _owned?.Dispose();

    private void Tell()
    {
        try
        {
            Changed?.Invoke();
        }
#pragma warning disable CA1031 // A dashboard that could not be told is a log record; the condition is kept all the same.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            InstallToastsLog.TellThrew(_logger, failure);
        }
    }

    /// <summary>The broken install toast's records.</summary>
    private static partial class InstallToastsLog
    {
        // The texts polish, 2026-10-10, pages #236 and #239 (previously "... was raised."
        // and "The dashboard could not be told ..."): the words 7202 and the pages use.
        [LoggerMessage(EventId = 7201, Level = LogLevel.Warning, Message = "This install is broken ({Difference}), and the toast that says BrowserAI needs reinstalling was shown.")]
        public static partial void Shown(ILogger logger, string difference);

        [LoggerMessage(EventId = 7202, Level = LogLevel.Warning, Message = "Windows did not show the toast that says BrowserAI needs reinstalling.")]
        public static partial void ShowThrew(ILogger logger, Exception failure);

        [LoggerMessage(EventId = 7203, Level = LogLevel.Warning, Message = "Windows did not remove the toast that says BrowserAI needs reinstalling.")]
        public static partial void RemoveThrew(ILogger logger, Exception failure);

        [LoggerMessage(EventId = 7204, Level = LogLevel.Warning, Message = "BrowserAI's page could not be told that the install's state changed.")]
        public static partial void TellThrew(ILogger logger, Exception failure);
    }
}
