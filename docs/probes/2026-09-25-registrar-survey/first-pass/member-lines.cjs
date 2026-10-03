// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Counts the lines a C# member occupies, including the XML doc comment and attributes directly above it.
// Usage: node member-lines.cjs <file> <memberNameRegex> [...more]
// A member ends at its matching closing brace, or at the first line ending in ';' for an expression body.
const fs = require("fs");
const [file, ...names] = process.argv.slice(2);
const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
let total = 0;
for (const name of names) {
  const re = new RegExp(`^\\s*(public|private|internal|protected)[^=(]*\\b${name}\\b\\s*(\\(|\\{|=>|=|;|<)`);
  const start = lines.findIndex((l) => re.test(l));
  if (start < 0) { console.log(`  ${name}: NOT FOUND`); continue; }
  // walk up over doc comments / attributes / blank-free contiguous comment block
  let top = start;
  while (top > 0 && /^\s*(\/\/\/|\[|\/\/)/.test(lines[top - 1])) top--;
  // walk down to the end of the member
  let end = start, depth = 0, opened = false;
  for (let i = start; i < lines.length; i++) {
    const l = lines[i].replace(/"(?:[^"\\]|\\.)*"/g, '""');
    for (const ch of l) { if (ch === "{") { depth++; opened = true; } else if (ch === "}") depth--; }
    end = i;
    if (opened && depth <= 0) break;
    if (!opened && /;\s*$/.test(lines[i])) break;
  }
  const n = end - top + 1;
  total += n;
  console.log(`  ${name}: lines ${top + 1}-${end + 1} (${n})`);
}
console.log(`  TOTAL ${file.split(/[\\/]/).pop()}: ${total}`);
