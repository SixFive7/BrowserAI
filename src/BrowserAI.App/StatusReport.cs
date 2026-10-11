// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Buffers;
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
/// <b>Two jobs, and both of them matter.</b> It is a support artifact -- the file
/// somebody attaches when they say <i>it is not working</i>, carrying every
/// answer the dialog would have shown without needing a person at the screen --
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
/// -- and the repository-wide ban on <c>System.Console</c> reaches this assembly
/// as well.
/// </para>
/// </remarks>
internal static class StatusReport
{
    /// <summary>The schema version this build writes and reads.</summary>
    /// <remarks>
    /// <b>3 since 2026-09-24</b>, when the window stopped being about one client:
    /// <c>clientPath</c>, <c>clientFound</c>, <c>userScope</c> and
    /// <c>projectScope</c> moved into a <c>clients</c> array, one entry per
    /// client, and nothing at the top level describes a client any more.
    /// <i>Corrected 2026-09-24 (previously <c>2</c> since 2026-09-16, when
    /// <c>apartment</c> was added; and <c>1</c> before that.)</i>
    /// </remarks>
    public const int SchemaVersion = 3;

    /// <summary>Writes the report.</summary>
    /// <param name="state">What to write.</param>
    /// <param name="path">Where to write it.</param>
    /// <returns>The path it was written to.</returns>
    public static async Task<string> WriteAsync(AppState state, string path)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (directory is { Length: > 0 })
        {
            _ = Directory.CreateDirectory(directory);
        }

        // The apartment of the thread the shell's calls run on, asked of one such thread.
        var apartment = await Interop.ShellInterop.OnAShellThreadAsync(
            static () => Thread.CurrentThread.GetApartmentState(),
            "BrowserAI report apartment").ConfigureAwait(false);

        await File.WriteAllBytesAsync(path, ToUtf8(state, apartment)).ConfigureAwait(false);

        return Path.GetFullPath(path);
    }

    /// <summary>Renders the report.</summary>
    /// <param name="state">What to render.</param>
    /// <param name="shellApartment">The apartment the shell's calls run in, which the report names.</param>
    /// <returns>The bytes, UTF-8, LF, no BOM.</returns>
    public static byte[] ToUtf8(AppState state, ApartmentState shellApartment)
    {
        ArgumentNullException.ThrowIfNull(state);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" }))
        {
            writer.WriteStartObject();

            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("product", "BrowserAI");
            writer.WriteString("version", state.Version);
            writer.WriteString("writtenAt", DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

            // ⚠️ THE APARTMENT OF THE THREAD THE SHELL'S CALLS RUN ON, AND IT IS
            // NOT COSMETIC. The folder picker's BIF_NEWDIALOGSTYLE silently falls
            // back to the pre-Vista dialog on a thread that is not in a single-
            // threaded apartment, and whether NativeAOT puts a thread in one is
            // a question only the PUBLISHED binary answers -- the suite's own host
            // is not this executable. Corrected 2026-10-11 (previously "THE
            // APARTMENT OF THE THREAD THAT RAN Main ... [STAThread] is an
            // attribute the CLR reads"): Main is asynchronous since that day,
            // the maintainer's decision 54, and [STAThread] on an asynchronous
            // Main puts no thread in the apartment, measured that day, so every
            // shell call runs on a thread that asks for one
            // (Interop/ShellInterop.OnAShellThreadAsync), and that thread's
            // apartment is the one the picker meets.
            writer.WriteString("apartment", shellApartment.ToString());

            WriteNullable(writer, "installRoot", state.InstallRoot);
            writer.WriteString("dataRoot", state.DataRoot);
            WriteNullable(writer, "serverCommand", state.ServerCommand);
            WriteNullable(writer, "serverRefusal", state.ServerRefusal);
            writer.WriteString("status", state.StatusSentence());
            WriteNullable(writer, "lastUpdateCheck", state.LastUpdateCheck);

            // ⚠️ ONE ENTRY PER CLIENT -- 2026-09-24. This file is the support
            // artifact somebody attaches when they say it is not working, and
            // with two clients the useful sentence is almost always about which
            // ONE of them is wrong. A report that carried the first client's
            // answers under unqualified names would be a report that looks
            // complete and is half of one.
            writer.WritePropertyName("clients");
            writer.WriteStartArray();

            foreach (var client in state.Clients)
            {
                writer.WriteStartObject();
                writer.WriteString("key", client.Client.Key);
                writer.WriteString("displayName", client.Client.DisplayName);
                WriteNullable(writer, "clientPath", client.ClientPath);
                writer.WriteBoolean("clientFound", client.ClientFound);
                writer.WriteString("status", client.StatusSentence());
                WriteNullable(writer, "projectDirectory", client.ProjectDirectory);

                writer.WritePropertyName("userScope");
                WriteScope(writer, client.UserScope);

                writer.WritePropertyName("projectScope");

                if (client.ProjectScope is { } project)
                {
                    WriteScope(writer, project);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        // The trailing newline every other file this repository writes carries.
        buffer.Write("\n"u8);

        return buffer.WrittenSpan.ToArray();
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
