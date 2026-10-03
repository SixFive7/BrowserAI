// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import fs from "node:fs";
import path from "node:path";
import { OUT, stats } from "./lib.mjs";
import { pageList } from "./pages.mjs";

const load = (f) => JSON.parse(fs.readFileSync(path.join(OUT, f), "utf8"));
const pw = [...load("snap-pw-A.json").results, ...load("snap-pw-B.json").results];
const sh = [...load("snap-sh-A.json").results, ...load("snap-sh-B.json").results];
const mem = { pwA: load("snap-pw-A.json").memory, pwB: load("snap-pw-B.json").memory, shA: load("snap-sh-A.json").memory, shB: load("snap-sh-B.json").memory };
const pages = pageList("x").map((p) => p[0]);
const med = (xs) => stats(xs).median;
const rows = [];
for (const p of pages) {
  const a = pw.filter((r) => r.page === p), b = sh.filter((r) => r.page === p);
  rows.push({
    page: p, n_pw: a.length, n_sh: b.length,
    pw_snapMs: med(a.map((r) => r.snapMs)), sh_snapMs: med(b.map((r) => r.snapMs)),
    pw_navMs: med(a.map((r) => r.navMs)), sh_navMs: med(b.map((r) => r.navMs)),
    pw_bodyTok_boxes: med(a.map((r) => r.bodyTokens)), pw_bodyTok_noBoxes: med(a.map((r) => r.bodyNoBoxesTokens)), sh_bodyTok: med(b.map((r) => r.bodyTokens)),
    pw_bodyChars_boxes: med(a.map((r) => r.bodyChars)), pw_bodyChars_noBoxes: med(a.map((r) => r.bodyNoBoxesChars)), sh_bodyChars: med(b.map((r) => r.bodyChars)),
    pw_responseTok: med(a.map((r) => r.responseTokens)), pw_navResponseTok: med(a.map((r) => r.navResponseTokens)),
    pw_refs: med(a.map((r) => r.refs)), sh_ids: med(b.map((r) => r.ids)),
    sh_vs_pw_boxes_tokens: +(med(b.map((r) => r.bodyTokens)) / med(a.map((r) => r.bodyTokens))).toFixed(2),
    sh_vs_pw_noBoxes_tokens: +(med(b.map((r) => r.bodyTokens)) / med(a.map((r) => r.bodyNoBoxesTokens))).toFixed(2),
    pw_mem_privMiB: [mem.pwA[p]?.privateMiB, mem.pwB[p]?.privateMiB], sh_mem_privMiB: [mem.shA[p]?.privateMiB, mem.shB[p]?.privateMiB],
    pw_mem_wsMiB: [mem.pwA[p]?.workingSetMiB, mem.pwB[p]?.workingSetMiB], sh_mem_wsMiB: [mem.shA[p]?.workingSetMiB, mem.shB[p]?.workingSetMiB],
    pw_procs: mem.pwA[p]?.count, sh_procs: mem.shA[p]?.count,
    errors_pw: a.filter((r) => r.isError).length,
  });
}
fs.writeFileSync(path.join(OUT, "snap-summary.json"), JSON.stringify(rows, null, 1));
const cols = ["page", "pw_snapMs", "sh_snapMs", "pw_navMs", "sh_navMs", "pw_bodyTok_boxes", "pw_bodyTok_noBoxes", "sh_bodyTok", "sh_vs_pw_boxes_tokens", "sh_vs_pw_noBoxes_tokens", "pw_navResponseTok", "pw_mem_privMiB", "sh_mem_privMiB", "pw_procs", "sh_procs"];
console.log(cols.join("\t"));
for (const r of rows) console.log(cols.map((c) => Array.isArray(r[c]) ? r[c].join("/") : r[c]).join("\t"));
// totals
const sum = (k) => rows.reduce((a, r) => a + r[k], 0);
console.log("TOTAL tokens pw boxes", sum("pw_bodyTok_boxes"), "pw noBoxes", sum("pw_bodyTok_noBoxes"), "sh", sum("sh_bodyTok"));
