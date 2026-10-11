// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Invented data for the dashboard mockups. No real conversation, path or person.
// Four scenarios: busy (the default), quiet, attention and firstrun.
// Times are minutes from the mockup clock, which starts at 13:41:00.

const MOCK = (() => {
  const loadedAt = Date.now();
  const startMinutes = 13 * 60 + 41;
  const minute = 60 * 1000;
  const base = loadedAt;

  const at = (minutesFromNow) => base + minutesFromNow * minute;
  const clockOf = (ms) => {
    const total = Math.floor((ms - base) / minute) + startMinutes;
    const h = Math.floor(((total % 1440) + 1440) % 1440 / 60);
    const m = ((total % 60) + 60) % 60;
    return String(h).padStart(2, '0') + ':' + String(m).padStart(2, '0');
  };
  const nowClock = () => clockOf(Date.now() - loadedAt + base);
  const left = (ms) => {
    const seconds = Math.max(0, Math.ceil((ms - (Date.now() - loadedAt + base)) / 1000));
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = seconds % 60;
    return h > 0 ? h + ':' + String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0') : m + ':' + String(s).padStart(2, '0');
  };
  const size = (mb) => mb >= 1000 ? (mb / 1024).toFixed(1) + ' GB' : Math.round(mb) + ' MB';

  const install = {
    root: 'C:\\Users\\sam\\AppData\\Local\\BrowserAI.app',
    data: 'C:\\Users\\sam\\AppData\\Local\\BrowserAI',
    logs: 'C:\\Users\\sam\\AppData\\Local\\BrowserAI\\logs',
    server: '"C:\\Users\\sam\\AppData\\Local\\BrowserAI.app\\current\\BrowserAI.exe" --mcp',
  };

  const appsRegistered = [
    { key: 'claude-code', name: 'Claude Code', state: 'ok', short: 'Set up for all your projects',
      sentence: 'Every Claude Code session you start can use BrowserAI, in any folder.',
      restart: 'A Claude Code session that was already open sees BrowserAI after it is restarted.',
      projectFile: '.mcp.json', found: 'C:\\Users\\sam\\.local\\bin\\claude.exe' },
    { key: 'codex', name: 'Codex', state: 'ok', short: 'Set up for all your projects',
      sentence: 'Every new Codex conversation can use BrowserAI, in any folder.',
      restart: 'A Codex conversation that was already open does not see BrowserAI. Start a new one.',
      projectFile: '.codex\\config.toml', found: 'Codex desktop app' },
  ];

  const changelog = {
    version: '1.2.0', date: '2026-10-20',
    groups: [
      { name: 'Added', entries: [
        { icon: '\u2728', head: 'A Start Menu click opens this page in your own browser.', body: 'It replaces the configuration window and lives at an address on your own PC that BrowserAI makes for each run.' },
        { icon: '\u2728', head: 'A downloaded update that has to wait says so in a notification that counts down to its install.', body: 'The notification names what holds the update and has two buttons, Install now and Wait for inactivity.' },
        { icon: '\u2728', head: 'An agent can close a session\'s browser and keep the session.', body: 'Resuming the session opens the browser again with its logins, cookies and tabs.' },
        { icon: '\u2728', head: 'Each conversation is named the way Claude Code or Codex shows it.', body: 'The tabs of one VS Code window are listed together under the window.' },
      ] },
      { name: 'Changed', entries: [
        { icon: '\ud83d\udca5', head: 'BrowserAI is one program, BrowserAI.exe.', body: 'An update rewrites the registrations that name the old file.' },
        { icon: '\ud83d\udca5', head: 'One BrowserAI runs in the background from sign-in to sign-out.', body: 'It holds every session, and Claude Code and Codex hand their calls to it.' },
        { icon: '\ud83d\udd27', head: 'The texts an agent and a person read say the same things in fewer words.', body: 'The tool list an agent is handed is about 2,200 characters shorter.' },
      ] },
      { name: 'Fixed', entries: [
        { icon: '\ud83d\udc1b', head: 'A Firefox started while Shift is held no longer stalls in safe mode.', body: 'Every Firefox BrowserAI starts has its safe mode key switched off.' },
        { icon: '\ud83d\udc1b', head: 'A Claude Code terminal session is told to reconnect through /mcp after an update.', body: 'The page and the notification name each terminal session that will need it.' },
      ] },
    ],
  };

  const onDisk = [
    { id: 'd1', name: 'invoice-export', purpose: 'exporting last month\'s invoices to PDF', folder: 'C:\\Code\\billing\\.work\\invoice-export', mb: 214, used: '3 days ago', flags: ['saved login'] },
    { id: 'd2', name: 'search-slow-network', purpose: 'trying the search page on a slow network', folder: 'C:\\Code\\acme-shop\\.work\\search-slow-network', mb: 377, used: '19 days ago', flags: ['network recording'] },
    { id: 'd3', name: 'signup-form-check', purpose: 'checking the sign-up form with a keyboard only', folder: 'C:\\Code\\acme-shop\\.work\\signup-form-check', mb: 58, used: '6 days ago', flags: [] },
    { id: 'd4', name: 'docs-search', purpose: 'confirming the docs search finds the new pages', folder: 'C:\\Code\\docs-site\\.work\\docs-search', mb: 41, used: '2 weeks ago', flags: ['transcript'] },
    { id: 'd5', name: 'map-tiles-zoom', purpose: 'measuring how long map tiles take at each zoom level', folder: 'C:\\Code\\atlas\\.work\\map-tiles-zoom', mb: 166, used: '5 weeks ago', flags: [] },
    { id: 'd6', name: 'newsletter-preview', purpose: 'previewing the newsletter in a narrow window', folder: 'C:\\Code\\market-notes\\.work\\newsletter-preview', mb: 73, used: '8 days ago', flags: [] },
    { id: 'd7', name: 'status-page-watch', purpose: 'watching the status page during the release', folder: 'C:\\Code\\atlas\\.work\\status-page-watch', mb: 29, used: '2 days ago', flags: [] },
  ];

  const browsersInstalled = [
    { name: 'Chromium 155', mb: 441, note: 'Downloaded once, used by every Chromium session.' },
    { name: 'Firefox 146', mb: 262, note: 'Downloaded once, used by every Firefox session.' },
  ];

  const busySessions = [
    { id: 's1', name: 'checkout-rounding', purpose: 'reproducing the rounding error in the checkout total', folder: 'C:\\Code\\acme-shop\\.work\\checkout-rounding',
      browser: 'Chromium', state: 'hidden', closes: at(7.2), by: 'c1', mb: 96, flags: ['transcript'], traces: 1,
      lastWhy: 'checking whether the total updates after removing a coupon', lastAgo: 'now', running: true },
    { id: 's2', name: 'pricing-compare', purpose: 'comparing the three pricing pages side by side', folder: 'C:\\Code\\market-notes\\.work\\pricing-compare',
      browser: 'Chromium', state: 'window', closes: at(48.1), by: 'c3', mb: 142, flags: [], traces: 0,
      lastWhy: 'comparing the annual prices on the second page', lastAgo: '2 min ago' },
    { id: 's3', name: 'docs-screenshots', purpose: 'taking fresh screenshots for the install guide', folder: 'C:\\Code\\docs-site\\.work\\docs-screenshots',
      browser: 'Firefox', state: 'closed', closes: null, by: 'c4', mb: 61, flags: [], traces: 0,
      lastWhy: 'saving the screenshot of the settings dialog', lastAgo: '22 min ago', closedWhy: 'closed after 10 minutes without a call' },
    { id: 's4', name: 'login-flow-check', purpose: 'checking that the sign-in survives a reload', folder: 'C:\\Code\\acme-shop\\.work\\login-flow-check',
      browser: 'Chromium', state: 'hidden', kept: true, closes: at(17), by: null, mb: 88, flags: ['saved login'], traces: 0,
      lastWhy: 'reloading the account page after signing in', lastAgo: '10 min ago' },
  ];

  const busyConversations = [
    { id: 'c1', title: 'Fix the checkout total rounding', titled: true, app: 'Claude Code', place: 'VS Code', windowKey: 'w1', windowLabel: 'VS Code window on acme-shop',
      folder: 'C:\\Code\\acme-shop', folderName: 'acme-shop', version: '2.1.300', running: true, holdsUntil: null, reconnect: 'self', sessions: ['s1'], since: '12:40' },
    { id: 'c2', title: 'unnamed conversation in acme-shop', titled: false, app: 'Claude Code', place: 'VS Code', windowKey: 'w1', windowLabel: 'VS Code window on acme-shop',
      folder: 'C:\\Code\\acme-shop', folderName: 'acme-shop', version: '2.1.300', running: false, holdsUntil: at(4.2), reconnect: 'self', sessions: [], since: '13:05' },
    { id: 'c3', title: 'Compare pricing pages', titled: true, app: 'Claude Code', place: 'Terminal', windowKey: null,
      folder: 'C:\\Code\\market-notes', folderName: 'market-notes', version: '2.1.300', running: false, holdsUntil: at(8.5), reconnect: 'mcp', sessions: ['s2'], since: '13:10' },
    { id: 'c4', title: 'Update the install guide screenshots', titled: true, app: 'Codex', place: 'Codex', windowKey: null,
      folder: 'C:\\Code\\docs-site', folderName: 'docs-site', version: '0.165.0', running: false, holdsUntil: null, reconnect: 'new', sessions: ['s3'], since: '12:57' },
  ];

  const busyActivity = [
    { t: '13:41', kind: 'call', who: 'c1', session: 's1', text: 'clicked in checkout-rounding', why: 'checking whether the total updates after removing a coupon' },
    { t: '13:39', kind: 'call', who: 'c3', session: 's2', text: 'took a screenshot in pricing-compare', why: 'comparing the annual prices on the second page' },
    { t: '13:36', kind: 'update', text: 'Update 1.2.1 was downloaded. It installs by itself once nothing uses BrowserAI.' },
    { t: '13:35', kind: 'update', text: 'BrowserAI checked GitHub for updates and found 1.2.1.' },
    { t: '13:31', kind: 'kept', session: 's4', text: 'The conversation using login-flow-check ended. BrowserAI keeps the session and its hidden browser until about 13:58, in case a conversation picks it up again.' },
    { t: '13:29', kind: 'closed', session: 's3', text: 'The Firefox of docs-screenshots closed after 10 minutes without a call. The session folder stays.' },
    { t: '13:12', kind: 'window', who: 'c3', session: 's2', text: 'opened a Chromium window on your screen for pricing-compare' },
    { t: '13:10', kind: 'connect', who: 'c3', text: 'connected from a terminal in market-notes' },
    { t: '13:05', kind: 'connect', who: 'c2', text: 'connected from VS Code in acme-shop' },
    { t: '12:58', kind: 'open', who: 'c4', session: 's3', text: 'opened a hidden Firefox for docs-screenshots' },
    { t: '12:57', kind: 'connect', who: 'c4', text: 'connected from Codex in docs-site' },
    { t: '12:41', kind: 'open', who: 'c1', session: 's1', text: 'opened a hidden Chromium for checkout-rounding' },
    { t: '12:40', kind: 'connect', who: 'c1', text: 'connected from VS Code in acme-shop' },
    { t: '08:12', kind: 'start', text: 'BrowserAI started at sign-in.' },
  ];

  const heldUpdate = (extra) => Object.assign({
    state: 'held', version: '1.2.1', older: false, installsAt: at(48.1), wait: 'counting',
    source: 'GitHub releases', checked: '13:35',
  }, extra || {});

  const scenarios = {
    busy: {
      label: 'Busy afternoon',
      occasion: 'ordinary',
      version: '1.2.0', since: '08:12', startedHow: 'at sign-in',
      broken: null,
      install,
      update: heldUpdate(),
      apps: appsRegistered,
      conversations: busyConversations,
      sessions: busySessions,
      onDisk,
      browsersInstalled,
      logsMb: 18,
      activity: busyActivity,
      changelog,
    },
    quiet: {
      label: 'Quiet, nothing running',
      occasion: 'ordinary',
      version: '1.2.0', since: '08:12', startedHow: 'at sign-in',
      broken: null,
      install,
      update: { state: 'none', source: 'GitHub releases', checked: '13:35' },
      apps: appsRegistered,
      conversations: [],
      sessions: [],
      onDisk: onDisk.concat([
        { id: 'd8', name: 'checkout-rounding', purpose: 'reproducing the rounding error in the checkout total', folder: 'C:\\Code\\acme-shop\\.work\\checkout-rounding', mb: 96, used: '2 hours ago', flags: ['transcript'] },
        { id: 'd9', name: 'pricing-compare', purpose: 'comparing the three pricing pages side by side', folder: 'C:\\Code\\market-notes\\.work\\pricing-compare', mb: 142, used: '3 hours ago', flags: [] },
      ]),
      browsersInstalled,
      logsMb: 18,
      activity: [
        { t: '13:35', kind: 'update', text: 'BrowserAI checked GitHub for updates. 1.2.0 is the newest.' },
        { t: '11:52', kind: 'closed', text: 'The last browser closed after 10 minutes without a call. Nothing uses BrowserAI now.' },
        { t: '08:12', kind: 'start', text: 'BrowserAI started at sign-in.' },
      ],
      changelog,
    },
    attention: {
      label: 'Something needs you',
      occasion: 'ordinary',
      version: '1.2.0', since: '08:12', startedHow: 'at sign-in',
      broken: { first: 'browser_navigate, whose description is not the one this BrowserAI was built with' },
      install,
      update: heldUpdate({ wait: 'window-never', installsAt: null }),
      apps: [
        appsRegistered[0],
        { key: 'codex', name: 'Codex', state: 'repair', short: 'Needs repair',
          sentence: 'Codex is set up to start a BrowserAI file that is not there any more, so Codex cannot reach BrowserAI.',
          restart: 'After the repair, start a new Codex conversation.', projectFile: '.codex\\config.toml', found: 'Codex desktop app' },
      ],
      conversations: [busyConversations[2]],
      sessions: [
        Object.assign({}, busySessions[1], { closes: null, never: true }),
        busySessions[3],
      ],
      onDisk,
      browsersInstalled,
      logsMb: 18,
      activity: [
        { t: '13:40', kind: 'problem', text: 'A session was refused: part of this install does not match the rest. Every session is refused until BrowserAI is reinstalled.' },
        { t: '13:36', kind: 'update', text: 'Update 1.2.1 was downloaded. A window set never to close holds it.' },
        { t: '13:12', kind: 'window', who: 'c3', session: 's2', text: 'opened a Chromium window on your screen for pricing-compare, set never to close by itself' },
        { t: '08:12', kind: 'start', text: 'BrowserAI started at sign-in.' },
      ],
      changelog,
    },
    firstrun: {
      label: 'Just installed',
      occasion: 'firstrun',
      version: '1.2.0', since: '13:40', startedHow: 'by the installer',
      broken: null,
      install,
      update: { state: 'none', source: 'GitHub releases', checked: '13:40' },
      apps: [
        appsRegistered[0],
        { key: 'codex', name: 'Codex', state: 'missing', short: 'Not found on this PC',
          sentence: 'Codex is not installed on this PC, so BrowserAI did not set it up. Install Codex, then set it up here.',
          restart: '', projectFile: '.codex\\config.toml', found: null },
      ],
      conversations: [],
      sessions: [],
      onDisk: [],
      browsersInstalled: [],
      logsMb: 1,
      activity: [
        { t: '13:40', kind: 'start', text: 'BrowserAI was installed and set itself up for Claude Code.' },
      ],
      changelog,
    },
  };

  const params = new URLSearchParams(location.search);
  const chosen = scenarios[params.get('state')] ? params.get('state') : 'busy';
  const data = scenarios[chosen];

  const sessionById = (id) => data.sessions.find((s) => s.id === id) || null;
  const conversationById = (id) => data.conversations.find((c) => c.id === id) || null;
  const sessionsMb = data.sessions.reduce((sum, s) => sum + s.mb, 0) + data.onDisk.reduce((sum, s) => sum + s.mb, 0);
  const browsersMb = data.browsersInstalled.reduce((sum, b) => sum + b.mb, 0);
  const totalMb = sessionsMb + browsersMb + data.logsMb;
  const folderCount = data.sessions.length + data.onDisk.length;
  const windows = data.sessions.filter((s) => s.state === 'window');
  const hidden = data.sessions.filter((s) => s.state === 'hidden');
  const openBrowsers = windows.length + hidden.length;
  const attention = [];
  if (data.broken) {
    attention.push({ kind: 'broken', level: 'bad', title: 'BrowserAI needs reinstalling',
      text: 'Part of this install does not match the rest, so every session is refused until BrowserAI is reinstalled. Your sessions and their files are kept.' });
  }
  for (const app of data.apps) {
    if (app.state === 'repair') {
      attention.push({ kind: 'app', level: 'warn', title: app.name + ' needs repair', text: app.sentence, app: app.key });
    }
  }
  if (data.update.state === 'held' && data.update.wait === 'window-never') {
    attention.push({ kind: 'update', level: 'warn', title: 'A window keeps the update waiting',
      text: 'Update ' + data.update.version + ' installs once the pricing-compare window is closed, because that window is set never to close by itself.' });
  }

  const reconnectText = {
    self: 'Reconnects by itself after an update.',
    mcp: 'After an update, run /mcp in that terminal, choose BrowserAI, then Reconnect.',
    new: 'After an update, BrowserAI is back only in a new conversation.',
  };
  const placeText = { 'VS Code': 'Claude Code in VS Code', Terminal: 'Claude Code in a terminal', Codex: 'Codex' };

  function tick(root) {
    for (const el of (root || document).querySelectorAll('[data-ends]')) {
      el.textContent = left(Number(el.dataset.ends));
    }
    for (const el of (root || document).querySelectorAll('[data-clock-now]')) {
      el.textContent = nowClock();
    }
  }

  function startTicking() {
    tick();
    setInterval(() => tick(), 1000);
  }

  function esc(text) {
    return String(text).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  }

  function applyTheme() {
    const theme = params.get('theme');
    if (theme === 'dark' || theme === 'light') {
      document.documentElement.dataset.theme = theme;
    }
  }

  // The mockup's own control: which scenario and which scheme. Not part of any design.
  function mockControl(views) {
    if (params.has('shot')) {
      return;
    }
    const box = document.createElement('div');
    box.className = 'mock-control';
    const states = Object.entries(scenarios).map(([key, s]) =>
      '<a href="?state=' + key + (params.get('theme') ? '&theme=' + params.get('theme') : '') + location.hash + '"' + (key === chosen ? ' aria-current="true"' : '') + '>' + esc(s.label) + '</a>').join('');
    const theme = params.get('theme');
    const themeLink = (value, label) => '<a href="?state=' + chosen + (value ? '&theme=' + value : '') + location.hash + '"' + ((theme || '') === value ? ' aria-current="true"' : '') + '>' + label + '</a>';
    box.innerHTML = '<strong>Mockup only</strong><span>Data:</span>' + states + '<span>Scheme:</span>' + themeLink('', 'System') + themeLink('light', 'Light') + themeLink('dark', 'Dark');
    document.body.appendChild(box);
    window.addEventListener('hashchange', () => {
      for (const a of box.querySelectorAll('a')) {
        a.href = a.href.split('#')[0] + location.hash;
      }
    });
  }

  return {
    data, chosen, scenarios, at, clockOf, left, size, esc, tick, startTicking, applyTheme, mockControl,
    sessionById, conversationById, sessionsMb, browsersMb, totalMb, folderCount, windows, hidden, openBrowsers,
    attention, reconnectText, placeText,
  };
})();
