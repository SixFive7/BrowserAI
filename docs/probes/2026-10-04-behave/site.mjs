// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A local test site on 127.0.0.1 for the behaviour rigs. No real site and
// no real credential: the one account is "tester" with the password
// "not-a-secret", and the server forgets it when it exits.
//
//   /echo            a page that shows what the server saw, and starts a worker
//   /headers         what the server saw on this request, as JSON
//   /tall?h=N&w=W    N px of 100 px bands, each a solid colour whose red and
//                    green encode the band's index, optionally W px wide
//   /tallel?h=N      a short page holding one element N px tall, banded the same way
//   /login, /account, /whoami, /store, /form, /h/<n>   as in the lifetime rig

import { createServer } from 'node:http';
import { randomBytes } from 'node:crypto';

const page = (title, body, script = '') =>
  `<!doctype html><html><head><meta charset=utf-8><title>${title}</title></head><body>${body}<script>${script}</script></body></html>`;

const parseCookies = (header) => {
  const jar = {};
  for (const part of (header ?? '').split(';')) {
    const at = part.indexOf('=');
    if (at > 0) jar[part.slice(0, at).trim()] = part.slice(at + 1).trim();
  }
  return jar;
};

// Every request header the user agent can carry, and the client hints.
const seen = (req) => ({
  ua: req.headers['user-agent'] ?? null,
  chUa: req.headers['sec-ch-ua'] ?? null,
  chUaMobile: req.headers['sec-ch-ua-mobile'] ?? null,
  chUaPlatform: req.headers['sec-ch-ua-platform'] ?? null,
  chUaFullVersionList: req.headers['sec-ch-ua-full-version-list'] ?? null,
  chUaPlatformVersion: req.headers['sec-ch-ua-platform-version'] ?? null,
  acceptLanguage: req.headers['accept-language'] ?? null,
});

// A banded page EXACTLY `height` px tall (and `width` px wide when given): the
// root clips its bands, so the document is the size asked for and not the next
// multiple of a band. Neighbouring bands differ strongly in colour (red steps by
// 67 and green by 151 per band, blue fixed at 128), so a JPEG's rounding cannot
// make two different bands look alike.
const bands = (height, width, band = 100) => {
  const count = Math.ceil(height / band);
  const css = `html,body{margin:0;padding:0;background:#fff}#root{position:relative;overflow:hidden;height:${height}px;width:${width ? `${width}px` : '100%'}}div.b{height:${band}px;position:relative;font:bold 40px monospace;color:#000}div.b span{position:absolute;left:220px;top:25px;background:#fff;padding:0 6px}div.k{position:absolute;left:0;top:0;width:200px;height:${band}px}div.r{position:absolute;right:0;top:0;width:200px;height:${band}px}`;
  const script = `
    const root = document.getElementById('root');
    const parts = [];
    for (let i = 0; i < ${count}; i++) {
      const r = (i * 67) % 256, g = (i * 151) % 256;
      const colour = 'rgb(' + r + ',' + g + ',128)';
      parts.push('<div class=b><div class=k style="background:' + colour + '"></div><span>ROW ' + String(i).padStart(4, '0') + ' y=' + (i * ${band}) + '</span><div class=r style="background:' + colour + '"></div></div>');
    }
    root.innerHTML = parts.join('');
    document.title = 'tall ready ' + document.documentElement.scrollHeight + 'x' + document.documentElement.scrollWidth;`;
  return { css, script };
};

export async function startSite(run) {
  const signedIn = new Map();
  const sessionTokens = new Set();
  const hits = [];

  const server = createServer((req, res) => {
    const url = new URL(req.url, 'http://127.0.0.1');
    const jar = parseCookies(req.headers.cookie);
    hits.push({ t: Date.now(), method: req.method, path: url.pathname + url.search, dest: req.headers['sec-fetch-dest'] ?? null, cookies: Object.keys(jar), ...seen(req) });
    const html = (status, body, headers = {}) => {
      res.writeHead(status, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store', ...headers });
      res.end(body);
    };

    if (url.pathname === '/headers') {
      res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
      res.end(JSON.stringify(seen(req)));
      return;
    }

    if (url.pathname === '/sw.js') {
      // A service worker that answers /headers?from=sw* by fetching it itself,
      // so the request the server sees is the worker's own.
      res.writeHead(200, { 'content-type': 'text/javascript', 'cache-control': 'no-store' });
      res.end(`self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()));
self.addEventListener('fetch', (e) => {
  if (new URL(e.request.url).search.startsWith('?from=sw')) {
    e.respondWith(fetch('/headers?from=sw-itself', { cache: 'no-store' }).then((r) => r.json()).then((h) => new Response(JSON.stringify({ ua: 'worker ' + self.navigator.userAgent, seen: h }), { headers: { 'content-type': 'application/json' } })));
  }
});`);
      return;
    }

    if (url.pathname === '/worker.js') {
      res.writeHead(200, { 'content-type': 'text/javascript', 'cache-control': 'no-store' });
      res.end(`fetch('/headers?from=worker', { cache: 'no-store' }).then((r) => r.json()).then((h) => postMessage({ ua: navigator.userAgent, brands: navigator.userAgentData ? navigator.userAgentData.brands.map((b) => b.brand + '/' + b.version).join(', ') : null, headers: h })).catch((e) => postMessage({ error: String(e) }));`);
      return;
    }

    if (url.pathname === '/echo') {
      // Critical-CH asks for the full version list and the platform version on
      // the next request, so the high-entropy hints show up server-side too.
      return html(200, page('echo', `<pre id=server>${JSON.stringify(seen(req), null, 1).replace(/</g, '&lt;')}</pre>`), {
        'accept-ch': 'Sec-CH-UA-Full-Version-List, Sec-CH-UA-Platform-Version',
      });
    }

    if (url.pathname === '/login' && req.method === 'POST') {
      let body = '';
      req.on('data', (c) => { body += c; });
      req.on('end', () => {
        const form = new URLSearchParams(body);
        if (form.get('user') === 'tester' && form.get('pass') === 'not-a-secret') {
          const auth = randomBytes(12).toString('hex');
          const sess = randomBytes(12).toString('hex');
          signedIn.set(auth, 'tester');
          sessionTokens.add(sess);
          res.writeHead(303, {
            location: '/account',
            'cache-control': 'no-store',
            'set-cookie': [
              `auth=${auth}; Path=/; HttpOnly; Max-Age=86400; SameSite=Lax`,
              `sess=${sess}; Path=/; HttpOnly; SameSite=Lax`,
            ],
          });
          res.end();
        } else {
          html(401, page('login failed', '<p id=state>login failed</p>'));
        }
      });
      return;
    }

    if (url.pathname === '/login') {
      return html(200, page('login', '<form method=post action=/login><label>User <input id=user name=user></label> <label>Password <input id=pass name=pass type=password></label> <button id=go type=submit>Sign in</button></form>'));
    }

    if (url.pathname === '/whoami') {
      res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
      res.end(JSON.stringify({ user: signedIn.get(jar.auth) ?? null, sessionCookie: sessionTokens.has(jar.sess), cookieNames: Object.keys(jar).sort() }));
      return;
    }

    if (url.pathname === '/account') {
      const user = signedIn.get(jar.auth);
      return html(200, page(user ? `account: signed in as ${user}` : 'account: signed out', `<p id=state>${user ? `signed in as ${user}` : 'signed out'}</p>`));
    }

    if (url.pathname.startsWith('/h/')) {
      const n = url.pathname.slice(3);
      return html(200, page(`history ${n}`, `<p>history page ${n}</p>`));
    }

    if (url.pathname === '/store') {
      return html(200, page('store', '<p id=state>store</p>'));
    }

    if (url.pathname === '/form') {
      return html(200, page('form', '<label>Notes <textarea id=notes rows=3 cols=40></textarea></label> <label>Field <input id=field></label>'));
    }

    if (url.pathname === '/tall') {
      const height = Number(url.searchParams.get('h') ?? '50000');
      const width = url.searchParams.get('w') ? Number(url.searchParams.get('w')) : null;
      const { css, script } = bands(height, width);
      return html(200, `<!doctype html><html><head><meta charset=utf-8><title>tall ${height}</title><style>${css}</style></head><body><div id=root></div><script>${script}</script></body></html>`);
    }

    if (url.pathname === '/wide') {
      // EXACTLY W px wide and 2,000 px tall, in vertical stripes of 100 px that
      // differ strongly in colour, the way /tall's bands do.
      const width = Number(url.searchParams.get('w') ?? '20000');
      const count = Math.ceil(width / 100);
      return html(200, `<!doctype html><html><head><meta charset=utf-8><title>wide ${width}</title><style>html,body{margin:0;padding:0}#root{display:flex;overflow:hidden;width:${width}px;height:2000px}#root div{flex:0 0 100px;height:2000px}</style></head><body><div id=root></div><script>
        const root = document.getElementById('root');
        const parts = [];
        for (let i = 0; i < ${count}; i++) { parts.push('<div style="background:rgb(' + ((i * 67) % 256) + ',' + ((i * 151) % 256) + ',128)"></div>'); }
        root.innerHTML = parts.join('');
        document.title = 'wide ready ' + document.documentElement.scrollWidth + 'x' + document.documentElement.scrollHeight;
      </script></body></html>`);
    }

    if (url.pathname === '/tallel') {
      // One element N px tall inside a page that scrolls; the element is what a
      // ref-targeted screenshot captures.
      const height = Number(url.searchParams.get('h') ?? '20000');
      const { css, script } = bands(height, null);
      return html(200, `<!doctype html><html><head><meta charset=utf-8><title>tallel ${height}</title><style>${css} #root{border:0;margin:0}</style></head><body><h1 id=above>above the element</h1><div id=root role=img aria-label="the tall element"></div><p>below the element</p><script>${script}</script></body></html>`);
    }

    return html(200, page('home', '<p>home</p>'));
  });

  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  return { origin: `http://127.0.0.1:${server.address().port}`, hits, close: () => new Promise((r) => server.close(r)) };
}
