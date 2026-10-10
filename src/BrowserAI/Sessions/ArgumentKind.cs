// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserAI.Sessions;

/// <summary>
/// What an argument arrived as, in words, for a refusal that says the argument had
/// the wrong kind.
/// </summary>
/// <remarks>
/// <b>Added 2026-10-10, from the texts review</b>: the refusals said a string and a
/// number in words and every other kind by the name .NET gives it, so a model read
/// "it arrived as True", "Object" or "Array". Every refusal that says what arrived
/// names it here, so the kinds read the same everywhere.
/// </remarks>
internal static class ArgumentKind
{
    /// <summary>The value as a refusal names it.</summary>
    /// <param name="value">What the argument carried.</param>
    /// <returns>For example <c>the string 'ten'</c>, <c>the number 1.5</c>, <c>the value true</c>, <c>an object</c> or <c>a list</c>.</returns>
    public static string Of(JsonNode? value) => value?.GetValueKind() switch
    {
        // A value a caller wrote, quoted back into a model's context, so it is escaped.
        JsonValueKind.String => $"the string '{RecordText.Escape(value.GetValue<string>())}'",
        JsonValueKind.Number => $"the number {value.ToJsonString()}",
        JsonValueKind.True => "the value true",
        JsonValueKind.False => "the value false",
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "a list",
        _ => "null",
    };
}
