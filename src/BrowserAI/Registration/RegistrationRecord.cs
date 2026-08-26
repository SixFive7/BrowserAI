// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BrowserAI.Registration;

/// <summary>
/// The registration's state on disk: what happened, when, and to what.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because a log line is not discoverable state.</b> The
/// requirement is that a registration which did not happen says so — and the
/// place a person looks when a client cannot see BrowserAI is not the middle of
/// a rolling log written weeks ago by an installer. This is one small file with
/// one answer in it, and its <c>outcome</c> is the whole finding.
/// </para>
/// <para>
/// <b>It is a sibling of <c>current\</c>, never a child.</b> An update replaces
/// that directory wholesale, so a record written inside it would be deleted by
/// the event most likely to have produced the line somebody came to read — the
/// same rule <see cref="Hosting.IAppPaths"/> states for the log, the browsers and
/// the session index.
/// </para>
/// <para>
/// <b>Nothing reads it back.</b> It is written for a person and for the suite,
/// never consulted to decide anything: the client's own configuration is the
/// single source of truth about what is registered, and a cache of somebody
/// else's state is a second answer that can be wrong. That is also why there is
/// no parser here — a file only a human reads cannot desynchronise from a reader
/// that does not exist.
/// </para>
/// </remarks>
internal static class RegistrationRecord
{
    /// <summary>The schema this build writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The record's file name, directly under the install root.</summary>
    public const string FileName = "mcp-registration.json";

    /// <summary>Where the record for an install root lives.</summary>
    /// <param name="installRoot">The directory containing <c>current\</c>.</param>
    /// <returns>The record's absolute path.</returns>
    public static string PathFor(string installRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(installRoot);
        return Path.Combine(installRoot, FileName);
    }

    /// <summary>Serialises a pass exactly as it is written to disk.</summary>
    /// <param name="report">What the pass concluded.</param>
    /// <param name="intent">Which lifecycle event ran it.</param>
    /// <param name="version">The BrowserAI version the hook was given.</param>
    /// <param name="when">When it ran.</param>
    /// <returns>UTF-8 bytes, LF-separated, no BOM.</returns>
    /// <remarks>
    /// <b>The relaxed encoder, and the reason is the reader:</b> the default
    /// escapes <c>+</c>, so every ISO 8601 timestamp east of UTC would round-trip
    /// perfectly and be unreadable by the person this file exists for.
    /// *(Corrected 2026-08-26, previously "for the same reason
    /// <c>browserai.json</c> uses one" — that file is gone, and the timestamps it
    /// carried are columns in <c>browserai.data</c> now, where no encoder sees
    /// them.)*
    /// </remarks>
    public static byte[] ToUtf8(RegistrationReport report, RegistrationIntent intent, string version, DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("when", when.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("intent", intent.ToString());
            writer.WriteString("outcome", report.Status.ToString());
            writer.WriteBoolean("isWhatWasAskedFor", report.IsWhatWasAskedFor);
            writer.WriteString("server", McpClientRegistration.ServerName);
            writer.WriteString("scope", McpClientRegistration.UserScope);
            writer.WriteString("browserAiVersion", version);

            WriteNullable(writer, "client", report.ClientPath);
            WriteNullable(writer, "command", report.Command);

            writer.WriteString("detail", report.Detail);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>Writes the record, replacing whatever was there.</summary>
    /// <param name="installRoot">The directory containing <c>current\</c>.</param>
    /// <param name="report">What the pass concluded.</param>
    /// <param name="intent">Which lifecycle event ran it.</param>
    /// <param name="version">The BrowserAI version the hook was given.</param>
    /// <param name="when">When it ran.</param>
    /// <returns>The path written.</returns>
    public static string Write(string installRoot, RegistrationReport report, RegistrationIntent intent, string version, DateTimeOffset when)
    {
        var path = PathFor(installRoot);

        _ = Directory.CreateDirectory(installRoot);
        File.WriteAllBytes(path, ToUtf8(report, intent, version, when));

        return path;
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
