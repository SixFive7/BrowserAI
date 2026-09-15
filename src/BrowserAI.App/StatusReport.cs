// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Encodings.Web;
using System.Text.Json;
using BrowserAI.Registration;

namespace BrowserAI.App;

/// <summary>
/// The whole of <see cref="AppState"/> as one JSON file, written and then the
/// process exits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two jobs, and both of them matter.</b> It is a support artifact — the file
/// somebody attaches when they say <i>it is not working</i>, carrying every
/// answer the dialog would have shown without needing a person at the screen —
/// and it is this application's <b>testable non-interactive path</b>. A window
/// application whose only entry point opens a window is one the suite cannot
/// assert anything about at all.
/// </para>
/// <para>
/// ⚠️ <b>Written with <see cref="Utf8JsonWriter"/> and no serializer.</b>
/// <c>JsonSerializer</c> over a type would need either reflection, which
/// NativeAOT refuses, or a source-generated context, which is a second
/// description of this shape that can drift from the first. This is the same
/// choice <see cref="RegistrationRecord"/> made and for the same reason.
/// </para>
/// <para>
/// <b>Never <c>stdout</c>.</b> It takes a path. This is a Windows-subsystem
/// binary with no console at all, so a write to a standard stream goes nowhere
/// — and the repository-wide ban on <c>System.Console</c> reaches this assembly
/// as well.
/// </para>
/// </remarks>
internal static class StatusReport
{
    /// <summary>The schema version this build writes and reads.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Writes the report.</summary>
    /// <param name="state">What to write.</param>
    /// <param name="path">Where to write it.</param>
    /// <returns>The path it was written to.</returns>
    public static string Write(AppState state, string path)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (directory is { Length: > 0 })
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, ToUtf8(state));

        return Path.GetFullPath(path);
    }

    /// <summary>Renders the report.</summary>
    /// <param name="state">What to render.</param>
    /// <returns>The bytes, UTF-8, LF, no BOM.</returns>
    public static byte[] ToUtf8(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" }))
        {
            writer.WriteStartObject();

            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("product", "BrowserAI");
            writer.WriteString("version", state.Version);
            writer.WriteString("writtenAt", DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

            WriteNullable(writer, "installRoot", state.InstallRoot);
            writer.WriteString("dataRoot", state.DataRoot);
            WriteNullable(writer, "serverCommand", state.ServerCommand);
            WriteNullable(writer, "serverRefusal", state.ServerRefusal);
            WriteNullable(writer, "clientPath", state.ClientPath);
            writer.WriteBoolean("clientFound", state.ClientFound);
            writer.WriteString("status", state.StatusSentence());
            WriteNullable(writer, "lastUpdateCheck", state.LastUpdateCheck);

            writer.WritePropertyName("userScope");
            WriteScope(writer, state.UserScope);

            writer.WritePropertyName("projectScope");

            if (state.ProjectScope is { } project)
            {
                WriteScope(writer, project);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteEndObject();
        }

        // The trailing newline every other file this repository writes carries.
        buffer.WriteByte((byte)'\n');

        return buffer.ToArray();
    }

    private static void WriteScope(Utf8JsonWriter writer, RegistrationView view)
    {
        writer.WriteStartObject();
        writer.WriteString("scope", view.Scope.ToString());
        writer.WriteString("file", view.File);
        WriteNullable(writer, "command", view.Command);
        writer.WriteString("ownership", view.Ownership.ToString());
        WriteNullable(writer, "unreadable", view.Unreadable);
        writer.WriteEndObject();
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
