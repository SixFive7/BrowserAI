// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch: writes the 72-tool audit as a TSV -- every tool of the golden
// snapshot (identical to the live 0.0.82 list, and by name to 0.0.83's), its
// capability, today's verdict, whether it overlaps BrowserAI's own tools or
// session model, and the recommendation.
'use strict';
const fs = require('fs');
const [SNAPSHOT, VERDICTS, LIST083, OUT] = process.argv.slice(2);
const snap = JSON.parse(fs.readFileSync(SNAPSHOT, 'utf8'));
const verdicts = JSON.parse(fs.readFileSync(VERDICTS, 'utf8')).upstream;
const names083 = new Set(JSON.parse(fs.readFileSync(LIST083, 'utf8')).tools.map((t) => t.name));
const capOf = {};
for (const [cap, list] of Object.entries(snap.toolsByCapability)) for (const n of list) capOf[n] = cap;
// Overlap classes: NAME (name near-collision with an authored tool), LIFE
// (touches the session lifecycle BrowserAI runs), RUN (overlaps a per-run
// argument), STATE (resumption-by-file against the profile), WORD (shares a
// word with a BrowserAI argument), CODE (superset that can do any of these),
// '-' none found.
const notes = {
  browser_resume: ['NAME+LIFE', 'Debugger release; one word from browserai_resume. Measured 0.0.82: releases the parked call in 45-266 ms, then waits for the next pause or the context close (0/6 answered in 20 s; answered when the browser closed). In BrowserAI that is upstream\'s 1 h idle timer; the in-flight call keeps BrowserAI\'s own idle close off. Only in-band recovery from the armed-close wedge (6/6).', 'deny (+ internal use by BrowserAI\'s idle close, see T1)'],
  browser_close: ['LIFE', 'Closes the context; BrowserAI\'s idle close sends it. Not destroy: directory, profile and record stay, next call relaunches. Ends a lingering pause (fresh context, 12/12 no Paused after). Does NOT answer a parked call (0/24). As the first call after an armed pause it parks itself and wedges the child (6/6 per family).', 'allow'],
  browser_tabs: ['LIFE (minor)', 'close of the last tab leaves no page; next call opens one. No session effect.', 'allow'],
  browser_start_tracing: ['WORD+RUN', 'Real Playwright trace into output\\traces; BrowserAI\'s `tracing` per-run arg is upstream saveSession (a session.md log), not this. Trace network log carries cookies.', 'allow'],
  browser_stop_tracing: ['WORD+RUN', 'Pair of browser_start_tracing.', 'allow'],
  browser_storage_state: ['STATE', 'Measured 6/6: 413-416 byte plaintext JSON in output\\ holding the cookie value and localStorage; browserai_catch_up names .har only. Not needed to keep a login: the profile is the session.', 'allow'],
  browser_set_storage_state: ['STATE+LIFE', 'Measured 6/6: an empty state file wiped the cookie and localStorage, and both were still gone after browser_close and relaunch (the profile is the session).', 'allow (or deny, direction C)'],
  browser_get_config: ['RUN (readback)', 'Shows headless/saveSession/userDataDir of THIS run; reads like a session setting.', 'allow'],
  browser_start_recording: ['WORD', 'Records a human\'s actions as code; useless headless; shares "record" with tracing/captureNetwork. Mutes the debugger while recording.', 'allow'],
  browser_stop_recording: ['WORD', 'Pair of browser_start_recording.', 'allow'],
  browser_start_video: ['WORD', 'Video into output\\; needs the shared ffmpeg that browserai_reinstall_browser repairs.', 'allow'],
  browser_stop_video: ['WORD', 'Pair of browser_start_video.', 'allow'],
  browser_video_chapter: ['-', 'Video overlay.', 'allow'],
  browser_video_show_actions: ['-', 'Video overlay.', 'allow'],
  browser_video_hide_actions: ['-', 'Video overlay.', 'allow'],
  browser_resize: ['RUN', 'Changes the viewport mid-run; BrowserAI\'s `viewport` is per-run and a live session\'s resume changes nothing, so this is the only mid-run lever.', 'allow'],
  browser_run_code_unsafe: ['CODE', 'Can arm (debugger.requestPause, page.pause) and release (debugger.resume) a pause, close the context, export storage state; denying browser_resume leaves all of it reachable here.', 'allow (unchanged)'],
  browser_cookie_clear: ['STATE (explicit)', 'Wipes the profile\'s cookies, which persist across resume.', 'allow'],
  browser_localstorage_clear: ['STATE (explicit)', 'Wipes the profile\'s localStorage, which persists across resume.', 'allow'],
  browser_sessionstorage_clear: ['-', 'sessionStorage is lost on every browser close anyway.', 'allow'],
  browser_annotate: ['LIFE', 'Already denied (liveness, dashboard window). The dashboard it opens has pause/resume/step.', 'deny (unchanged)'],
  browser_highlight: ['-', 'DevTools group, page overlay only.', 'allow'],
  browser_hide_highlight: ['-', 'DevTools group, page overlay only.', 'allow'],
  browser_network_requests: ['RUN (minor)', 'Requests since page load; captureNetwork is the launch-time HAR. Different artifacts, no conflict.', 'allow'],
  browser_wait_for: ['-', 'time is capped at 30 s in 0.0.82 code (0.0.83 says so in the schema).', 'allow'],
};
const rows = ['tool\tcapability\tverdictToday\tin0.0.83\toverlap\tnote\trecommendation'];
for (const t of snap.tools) {
  const n = t.name;
  const [cls, note, rec] = notes[n] || ['-', '', (verdicts[n] && verdicts[n].verdict) || '?'];
  rows.push([n, capOf[n] || '?', (verdicts[n] && verdicts[n].verdict) || 'NO ROW', names083.has(n) ? 'yes' : 'NO', cls, note, rec].join('\t'));
}
fs.writeFileSync(OUT, rows.join('\n') + '\n');
const withOverlap = rows.slice(1).filter((r) => r.split('\t')[4] !== '-').length;
console.log(`tools=${snap.tools.length} rowsWithOverlap=${withOverlap} in083=${[...snap.tools].filter((t) => names083.has(t.name)).length}`);
