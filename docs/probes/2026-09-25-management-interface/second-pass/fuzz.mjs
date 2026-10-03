// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// fuzz: throws malformed and mutated requests at the option S listener and checks that
// nothing but the bare 404 (or a closed connection) ever comes back, that the one
// write never happens, and that the listener still serves its page afterwards.
//   node fuzz.mjs <sprobe.exe> <outDir> <label> <count>
import fs from "node:fs";
import path from "node:path";
import net from "node:net";
import crypto from "node:crypto";
import { spawn } from "node:child_process";

const [sprobe, outDir, label, countText] = process.argv.slice(2);
const count = Number(countText);
const portFile = path.join(outDir, label + ".port");
const logPath = path.join(outDir, label + ".sprobe.log");
fs.rmSync(portFile, { force: true });
const server = spawn(sprobe, [portFile, logPath], { windowsHide: true, stdio: ["pipe", "ignore", "ignore"] });
let exited = null;
server.on("exit", (code) => { exited = code; });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
while (!fs.existsSync(portFile)) await sleep(5);
const [portText, token] = fs.readFileSync(portFile, "utf8").split("\t");
const port = Number(portText);
const host = `127.0.0.1:${port}`;
const origin = `http://${host}`;

const send = (bytes, half = false) => new Promise((resolve) => {
  const s = net.connect(port, "127.0.0.1");
  let data = Buffer.alloc(0);
  const done = (why) => { s.destroy(); resolve({ why, text: data.toString("latin1") }); };
  s.setTimeout(700, () => done("timeout"));
  s.on("connect", () => { s.write(bytes); if (half) s.end(); });
  s.on("data", (c) => { data = Buffer.concat([data, c]); });
  s.on("end", () => done("end"));
  s.on("error", () => done("error"));
  s.on("close", () => done("close"));
});

const valid = `GET /${token}/ HTTP/1.1\r\nHost: ${host}\r\nConnection: close\r\n\r\n`;
const write = `POST /${token}/act HTTP/1.1\r\nHost: ${host}\r\nOrigin: ${origin}\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}`;
const rnd = (n) => crypto.randomInt(n);
const pick = (a) => a[rnd(a.length)];
const junk = (n) => crypto.randomBytes(n);

function mutate() {
  const base = Buffer.from(pick([valid, write, write, valid]), "latin1");
  const kind = rnd(12);
  if (kind === 0) return junk(1 + rnd(600));
  if (kind === 1) return base.subarray(0, rnd(base.length));
  if (kind === 2) { const b = Buffer.from(base); for (let i = 0; i < 1 + rnd(6); i++) b[rnd(b.length)] = rnd(256); return b; }
  if (kind === 3) return Buffer.concat([base.subarray(0, rnd(base.length)), junk(rnd(64)), base.subarray(rnd(base.length))]);
  if (kind === 4) return Buffer.from(base.toString("latin1").replace("Host: ", pick(["host:", "Host : ", "HOST:\t", "Host:\r\n ", "X-Host: "])), "latin1");
  if (kind === 5) return Buffer.from(base.toString("latin1").replace(token, pick([token.slice(1), token + "x", token.toUpperCase(), encodeURIComponent(token).replace(/-/g, "%2D"), token.slice(0, 20) + "/" + token.slice(20), "../" + token])), "latin1");
  if (kind === 6) return Buffer.from(base.toString("latin1").replace("Content-Length: 2", pick(["Content-Length: -1", "Content-Length: 2, 2", "Content-Length: 0x2", "Content-Length: 99999999999", "Content-Length: 2\r\nContent-Length: 3", "Transfer-Encoding: chunked"])), "latin1");
  if (kind === 7) return Buffer.from(base.toString("latin1").replace("HTTP/1.1", pick(["HTTP/2.0", "HTTP/0.9", "", "HTTP/1.1 extra", "http/1.1"])), "latin1");
  if (kind === 8) return Buffer.from(base.toString("latin1").replace(/^(GET|POST)/, pick(["get", "HEAD", "DELETE", "PATCH", "CONNECT", "TRACE", "G ET", ""])), "latin1");
  if (kind === 9) return Buffer.from(base.toString("latin1").replace("\r\n\r\n", "\r\n" + "X-A: b\r\n".repeat(1 + rnd(1500)) + "\r\n"), "latin1");
  if (kind === 10) return Buffer.from(base.toString("latin1").replace(`Origin: ${origin}`, pick([`Origin: ${origin}/`, `Origin: ${origin.toUpperCase()}`, "Origin: null", `Origin: ${origin}\r\nOrigin: ${origin}`, `Origin: http://localhost:${port}`, `Origin: http://127.0.0.1:${port + 1}`, `Origin: https://127.0.0.1:${port}`])), "latin1");
  return Buffer.from(base.toString("latin1").replace(/\r\n/g, pick(["\n", "\r", "\r\n\0", "\r\r\n"])), "latin1");
}

const outcomes = {};
let admitted200 = 0, other = [];
const one = async () => {
  const bytes = mutate();
  const r = await send(bytes, rnd(4) === 0);
  const status = r.text.startsWith("HTTP/1.1 ") ? r.text.slice(9, 12) : r.text.length === 0 ? `closed(${r.why})` : "garbage";
  outcomes[status] = (outcomes[status] ?? 0) + 1;
  if (status === "200" || status === "204") { admitted200++; if (other.length < 8) other.push(JSON.stringify(bytes.toString("latin1").split(token).join("<token>").slice(0, 160))); }
};
for (let i = 0; i < count && exited === null; i += 12) await Promise.all(Array.from({ length: 12 }, one));
const after = await send(Buffer.from(valid, "latin1"));
const state = await send(Buffer.from(`GET /${token}/state HTTP/1.1\r\nHost: ${host}\r\nConnection: close\r\n\r\n`, "latin1"));
const lines = [
  `requests=${count} outcomes=${JSON.stringify(outcomes)}`,
  `answered_200_or_204=${admitted200} (a mutation can leave a request valid; listed below)`,
  ...other.map((o) => "  still-valid mutation: " + o),
  `listener_alive=${exited === null} page_after=${after.text.slice(9, 12)} state_after=${state.text.split("\r\n\r\n")[1]}`,
];
fs.writeFileSync(path.join(outDir, label + ".result.txt"), lines.join("\n") + "\n");
console.log(lines.join("\n"));
await send(Buffer.from(`POST /${token}/quit HTTP/1.1\r\nHost: ${host}\r\nOrigin: ${origin}\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}`, "latin1"));
server.stdin.end();
process.exit(0);
