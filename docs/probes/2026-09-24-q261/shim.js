// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A stdio pass-through in front of the real BrowserAI server, whose only job is
// to make that server GO AWAY at a chosen moment.
//
// Q261's condition needs a BrowserAI that exits mid-session and is re-launched by
// the client. BrowserAI has no such switch and must not grow one, so the
// disappearance is arranged from outside: the client's registered command is this
// file, this file starts the published server, and after the Nth tools/call
// answer it ends that server and exits. The client then meets a closed transport
// on the next call, re-dials its registered command, and gets a FRESH server --
// which is exactly the shape a Velopack update leaves behind.
//
// It forwards bytes and reads them; it never rewrites a frame. The only thing it
// adds is a line per launch in its own log, which is how the number of server
// processes is counted and not assumed.
//
// The child is ended through the handle of the process THIS file started. No
// name, no image path, no enumeration: one handle, one signal, which is the same
// rule the product holds itself to.

const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const SERVER = process.env.Q261_SERVER;
const LOGDIR = process.env.Q261_LOGDIR || '.';
const TAG = process.env.Q261_TAG || 'q261';
// How many tools/call answers to forward before ending the server. 0 = never.
const DIE_AFTER = Number(process.env.Q261_DIE_AFTER_CALLS || 0);
// ONE DEATH PER RUN, AND THE MARKER IS WHAT MAKES IT ONE. Every shim instance is
// started from the same registered command with the same environment, so without
// this the server the client re-dialled would die the moment it answered the
// refusal -- and the retry the refusal asks for would meet a third launch instead
// of a live server. What Q261 is about is the SECOND server, so only the first one
// is arranged to go.
const ONCE = process.env.Q261_DIED_MARKER || '';

fs.mkdirSync(LOGDIR, { recursive: true });

const LAUNCHES = path.join(LOGDIR, `${TAG}.launches.log`);
const WIRE = path.join(LOGDIR, `${TAG}.wire.jsonl`);

function note(line) {
    fs.appendFileSync(LAUNCHES, `${new Date().toISOString()} ${line}\n`);
}

function wire(direction, frame) {
    fs.appendFileSync(WIRE, `${JSON.stringify({ at: new Date().toISOString(), shim: process.pid, direction, frame })}\n`);
}

if (!SERVER) {
    note('REFUSED: Q261_SERVER is not set, so there is nothing to put behind this shim');
    process.exit(2);
}

const child = spawn(SERVER, [], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });

note(`LAUNCH shim=${process.pid} server=${child.pid} exe=${SERVER} dieAfterCalls=${DIE_AFTER}`);

// The ids of tools/call requests seen on the way in, so an ANSWER can be
// recognised by its id on the way back. Counting requests would end the server
// before it had answered the call the client is waiting for, which is a different
// experiment: what is wanted is a server that dies having done its job.
const callIds = new Set();
let answered = 0;
let ending = false;

function endTheServer(why) {
    if (ending) {
        return;
    }

    ending = true;
    note(`ENDING server=${child.pid} after ${answered} tools/call answer(s): ${why}`);

    // The handle of the process this file started, and nothing else.
    try {
        child.kill();
    } catch (failure) {
        note(`ENDING failed: ${failure.message}`);
    }

    // Give the frame already written to stdout a moment to reach the client, then
    // close this end too: the client decides the server is gone from the PIPE.
    setTimeout(() => {
        note(`EXIT shim=${process.pid}`);
        process.exit(0);
    }, 250);
}

function forward(from, to, direction, onFrame) {
    let buffer = '';

    from.on('data', (chunk) => {
        to.write(chunk);
        buffer += chunk.toString('utf8');

        let cut = buffer.indexOf('\n');

        while (cut >= 0) {
            const line = buffer.slice(0, cut).trim();

            buffer = buffer.slice(cut + 1);
            cut = buffer.indexOf('\n');

            if (line.length === 0) {
                continue;
            }

            try {
                onFrame(JSON.parse(line));
            } catch (failure) {
                wire(`${direction}-unparsed`, line.slice(0, 400));
            }
        }
    });
}

forward(process.stdin, child.stdin, 'client->server', (frame) => {
    wire('client->server', { id: frame.id, method: frame.method, name: frame.params && frame.params.name });

    if (frame.method === 'tools/call' && frame.id !== undefined) {
        callIds.add(String(frame.id));
    }
});

forward(child.stdout, process.stdout, 'server->client', (frame) => {
    wire('server->client', { id: frame.id, method: frame.method });

    if (frame.id !== undefined && callIds.has(String(frame.id))) {
        callIds.delete(String(frame.id));
        answered += 1;

        const alreadyDiedOnce = ONCE.length > 0 && fs.existsSync(ONCE);

        if (DIE_AFTER > 0 && answered >= DIE_AFTER && !alreadyDiedOnce) {
            if (ONCE.length > 0) {
                fs.writeFileSync(ONCE, `${new Date().toISOString()} shim=${process.pid} server=${child.pid}\n`);
            }

            endTheServer(`Q261_DIE_AFTER_CALLS=${DIE_AFTER}`);
        }
    }
});

// The server's stderr is the server's own log channel. It is kept whole, because
// the refusal writes a line there and that line is half the evidence.
child.stderr.on('data', (chunk) => {
    fs.appendFileSync(path.join(LOGDIR, `${TAG}.server.stderr.log`), chunk);
});

child.on('exit', (code, signal) => {
    note(`server=${child.pid} exited code=${code} signal=${signal}`);

    if (!ending) {
        note(`EXIT shim=${process.pid} because the server went on its own`);
        process.exit(0);
    }
});

process.stdin.on('end', () => {
    note(`client closed its end; ending server=${child.pid}`);
    endTheServer('the client closed the pipe');
});
