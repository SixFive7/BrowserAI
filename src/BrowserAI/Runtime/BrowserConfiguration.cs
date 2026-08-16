// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace BrowserAI.Runtime;

/// <summary>
/// The config file BrowserAI generates for its child, holding exactly the keys
/// that decide <b>which browser runs</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every key here is load-bearing, and omitting any of them is silent.</b>
/// <c>validateBrowserConfig</c> defaults <c>browserName</c> to <c>chromium</c>
/// <i>and</i> sets <c>channel: "chrome"</c> — the user's installed Google
/// Chrome. Verified by execution: with an <b>empty</b> browsers directory,
/// <c>initialize</c>, <c>tools/list</c> and <c>browser_navigate</c> all
/// succeeded, because nothing BrowserAI ships was ever consulted. Omit these and
/// the entire batteries-included premise is dead code with the suite green.
/// </para>
/// <para>
/// <b>The channel must be a chromium alias and must be present.</b> Read from
/// the shipped bundle, <c>getExecutableName</c> for Chromium is: a channel that
/// is a chromium alias → <c>chromium</c>; any other channel → that channel;
/// <i>no</i> channel → <c>headless ? "chromium-headless-shell" : "chromium"</c>.
/// <c>chromiumAliases</c> is exactly <c>["chrome-for-testing"]</c>, and it is
/// what upstream's own <c>--browser chromium</c> resolves to. So dropping the
/// channel is <b>not</b> the same as setting it: a headless launch with no
/// channel asks for <c>chrome-headless-shell</c>, which
/// [is never provisioned](../../plan/A-runtime.md) and fails.
/// </para>
/// <para>
/// <b>The sandbox is deliberately absent from this file.</b> Measured
/// 2026-08-16 against <c>@playwright/mcp</c> 0.0.79 — <c>chromiumSandbox: true</c>
/// set here is discarded and the browser still runs <c>--no-sandbox</c>, because
/// upstream's CLI layer merges last and <b>always</b> defines the key
/// (<c>cliOptions.sandbox</c> defaults to <see langword="false"/> rather than to
/// undefined). The flag on the command line is the only thing that works, and it
/// lives in <see cref="ChildLaunch"/>.
/// </para>
/// <para>
/// <b>This is not the full generator.</b> Modes, tracing, console level, the
/// user data directory and the output directory arrive with the session tools at
/// build-order step 12, together with the <c>browser_get_config</c> round trip
/// that proves every generated opinion survived into the child.
/// </para>
/// </remarks>
internal static class BrowserConfiguration
{
    /// <summary>The browser family. Never left to the default, which is Chrome.</summary>
    public const string BrowserName = "chromium";

    /// <summary>
    /// The chromium-alias channel, spelled as upstream's <c>chromiumAliases</c>
    /// spells it. Never <c>chrome</c>, which is the user's Google Chrome, and
    /// never absent, which selects the headless shell.
    /// </summary>
    public const string Channel = "chrome-for-testing";

    /// <summary>
    /// Whether the child launches headless. One value at this step; the three
    /// modes arrive at build-order step 12.
    /// </summary>
    /// <remarks>
    /// It is written explicitly rather than left out. Upstream fills an absent
    /// <c>headless</c> with <c>platform === "linux" &amp;&amp; !DISPLAY</c>,
    /// which on Windows is <see langword="false"/> — so "no key" means "a window
    /// appears", not "upstream decides".
    /// </remarks>
    public const bool Headless = true;

    /// <summary>Writes the config file the child is started with.</summary>
    /// <param name="path">Where to write it. Overwritten if present.</param>
    /// <remarks>
    /// Written with <see cref="Utf8JsonWriter"/> rather than through a
    /// serializer: there is no model type to keep in step with the file, the
    /// bytes are visible in this method, and it needs no
    /// <c>JsonSerializerContext</c> under NativeAOT.
    /// </remarks>
    public static void WriteTo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteStartObject("browser");
        writer.WriteString("browserName", BrowserName);
        writer.WriteStartObject("launchOptions");
        writer.WriteString("channel", Channel);
        writer.WriteBoolean("headless", Headless);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.Flush();
    }
}
