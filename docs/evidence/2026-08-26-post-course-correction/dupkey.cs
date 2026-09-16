// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch probe: does JsonDocument keep duplicate property names, and does
// ToFrozenDictionary throw on the duplicate keys that produces?
using System.Collections.Frozen;
using System.Text.Json;

var json = """
{
  "schemaVersion": 1,
  "upstream": {
    "browser_click": { "verdict": "allow" },
    "browser_click": { "verdict": "deny", "why": "x", "since": "2026-01-01" }
  }
}
""";

using var document = JsonDocument.Parse(json);
var rows = document.RootElement.GetProperty("upstream").EnumerateObject().Select(p => p.Name).ToList();
Console.WriteLine($"JsonDocument kept {rows.Count} properties: {string.Join(", ", rows)}");
Console.WriteLine($"TryGetProperty answers the FIRST: {document.RootElement.GetProperty("upstream").GetProperty("browser_click").GetProperty("verdict")}");

try
{
    var frozen = rows.Select(name => (Name: name, Kind: "allow")).ToFrozenDictionary(row => row.Name, StringComparer.Ordinal);
    Console.WriteLine($"ToFrozenDictionary accepted duplicates: {frozen.Count}");
}
catch (Exception failure)
{
    Console.WriteLine($"ToFrozenDictionary threw {failure.GetType().Name}: {failure.Message}");
}
