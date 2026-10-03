// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// The second process of row 95's re-take. Reads os_crypt out of Local State,
// unprotects the DPAPI-prefixed key with CryptUnprotectData (no entropy, no
// prompt struct, dwFlags 0) through PowerShell, reads ONE row of a COPY of
// Default\Network\Cookies with node:sqlite, and AES-256-GCM decrypts it.
// The scheme tag and app_bound_encrypted_key are read FIRST.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');
const { DatabaseSync } = require('node:sqlite');

const profile = process.argv[2];
const expected = process.argv[3];
const result = { profile };

const localState = JSON.parse(fs.readFileSync(path.join(profile, 'Local State'), 'utf8'));
const oc = localState.os_crypt || {};
result.osCryptKeys = Object.keys(oc).sort();
result.appBoundEncryptedKeyPresent = Object.prototype.hasOwnProperty.call(oc, 'app_bound_encrypted_key');
const blob = Buffer.from(oc.encrypted_key, 'base64');
result.encryptedKeyBytes = blob.length;
result.encryptedKeyPrefix = blob.subarray(0, 5).toString('latin1');

const ps = `
$sig = @'
using System;
using System.Runtime.InteropServices;
public static class Dp {
  [StructLayout(LayoutKind.Sequential)] public struct BLOB { public int cbData; public IntPtr pbData; }
  [DllImport("crypt32.dll", SetLastError = true)]
  public static extern bool CryptUnprotectData(ref BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref BLOB pDataOut);
  [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr h);
  public static string Run(string b64) {
    byte[] input = Convert.FromBase64String(b64);
    BLOB i = new BLOB(); i.cbData = input.Length; i.pbData = Marshal.AllocHGlobal(input.Length);
    Marshal.Copy(input, 0, i.pbData, input.Length);
    BLOB o = new BLOB();
    try {
      if (!CryptUnprotectData(ref i, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref o)) { return "ERR " + Marshal.GetLastWin32Error(); }
      byte[] output = new byte[o.cbData]; Marshal.Copy(o.pbData, output, 0, o.cbData); LocalFree(o.pbData);
      return Convert.ToBase64String(output);
    } finally { Marshal.FreeHGlobal(i.pbData); }
  }
}
'@
Add-Type -TypeDefinition $sig
[Dp]::Run('${blob.subarray(5).toString('base64')}')
`;
const keyOut = execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' }).trim();
if (keyOut.startsWith('ERR')) { result.unprotect = keyOut; console.log(JSON.stringify(result)); process.exit(0); }
const key = Buffer.from(keyOut, 'base64');
result.unprotectSucceeded = true;
result.masterKeyBytes = key.length;

const copy = path.join(path.dirname(profile), 'Cookies.copy');
fs.copyFileSync(path.join(profile, 'Default', 'Network', 'Cookies'), copy);
const db = new DatabaseSync(copy, { readOnly: true });
const row = db.prepare("SELECT host_key, name, encrypted_value FROM cookies WHERE name = 'browserai_probe'").get();
db.close();
result.cookieRowFound = !!row;
if (row) {
  const ev = Buffer.from(row.encrypted_value);
  result.hostKey = row.host_key;
  result.schemeTag = ev.subarray(0, 3).toString('latin1');
  result.encryptedValueBytes = ev.length;
  const nonce = ev.subarray(3, 15), tag = ev.subarray(ev.length - 16), ct = ev.subarray(15, ev.length - 16);
  try {
    const d = crypto.createDecipheriv('aes-256-gcm', key, nonce);
    d.setAuthTag(tag);
    const plain = Buffer.concat([d.update(ct), d.final()]);
    result.plaintextBytes = plain.length;
    result.prefixIsSha256OfHost = plain.subarray(0, 32).equals(crypto.createHash('sha256').update(row.host_key).digest());
    result.valueAfter32ByteprefixMatches = plain.subarray(32).toString('utf8') === expected;
  } catch (e) { result.decryptError = String(e.message); }
}
fs.rmSync(copy, { force: true });
console.log(JSON.stringify(result));
