// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// node go.mjs --script <file> --go <file>: waits for the go file the launcher writes once this
// process is in the step's job, then runs the script as `node <file>` would. It is how the
// issue stage runs the two scripts the report to Playwright carries without a line of them
// changed: neither waits for a go file of its own.

import { pathToFileURL } from 'node:url';
import { parseArgs, required, waitForFile } from '../2026-10-13-restore-stall/lib.mjs';

const args = required(parseArgs(process.argv.slice(2)), 'script', 'go');
await waitForFile(args.go);
await import(pathToFileURL(args.script).href);
