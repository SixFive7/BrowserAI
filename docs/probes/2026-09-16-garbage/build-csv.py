# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import csv, os, datetime

OUT = r'C:\Source\SixFive7\BrowserAI\.work\2026-09-16-garbage\candidates.csv'
CITED = {'suite', 'test-scratch', 'probe2', 'p2', 'p7', 'r3', 'sqlite-p0', '2026-08-30-q160',
         '2026-09-15-icons', '2026-09-15-release', '2026-09-15-upstream', '2026-09-15-notes',
         '2026-09-15-install', '2026-09-15-app', '2026-09-14-webp-ask', '2026-09-14-firstrun',
         '2026-09-06-systemwide'}


def human(n):
    n = float(n)
    for u in ('B', 'KiB', 'MiB', 'GiB', 'TiB'):
        if abs(n) < 1024 or u == 'TiB':
            return (f'{n:.0f} {u}' if u == 'B' else f'{n:.1f} {u}')
        n /= 1024


def size_of(p):
    if os.path.isfile(p):
        return os.path.getsize(p), 1
    total = 0
    count = 0
    for root, dirs, files in os.walk(p):
        for f in files:
            try:
                total += os.path.getsize(os.path.join(root, f))
                count += 1
            except OSError:
                pass
    return total, count


def lw(p):
    try:
        return datetime.datetime.fromtimestamp(os.path.getmtime(p)).strftime('%Y-%m-%d %H:%M:%S')
    except OSError:
        return ''


rows = []


def add(path, verdict, created_by, note, bytes_=None, count=None, lastwrite=None):
    if bytes_ is None:
        if os.path.exists(path):
            bytes_, count = size_of(path)
            lastwrite = lw(path)
        else:
            bytes_, count, lastwrite = 0, 0, 'ABSENT'
    rows.append({
        'path': path, 'bytes': bytes_, 'human': human(bytes_), 'files': count,
        'last_write': lastwrite if lastwrite is not None else '', 'verdict': verdict,
        'created_by': created_by, 'note': note,
    })


L = os.environ['LOCALAPPDATA']
T = os.environ['TEMP']
REPO = r'C:\Source\SixFive7\BrowserAI'

# ---- current data root ----
add(rf'{L}\BrowserAI', 'KEEP', 'BrowserAI LocalAppDataPaths.Default (data root)',
    'Every child matches the current layout: browsers/ index/ instances/ logs/ live/ mcp-registration.json. '
    'No old-layout residue at the root (no lock, browserai.json, browserai.lock, browserai.data).')
add(rf'{L}\BrowserAI\browsers', 'KEEP', 'BrowserProvisioner (IAppPaths.BrowsersDirectory)',
    'chromium-1244, firefox-1544, ffmpeg-1011, winldd-1007, .downloads, .links, reinstall.lock. Current revisions only.')
add(rf'{L}\BrowserAI\logs\browserai-20260915-000.log', 'KEEP', 'BrowserAI log (IAppPaths.LogDirectory)',
    'The live log the running install appends to. 6.8 MiB and growing; only rotation would shrink it.')
add(rf'{L}\BrowserAI\index', 'ASK', 'SessionIndex (IAppPaths.IndexDirectory)',
    'Three entries, all naming .work/test-scratch/sessions-c365ddd2.../{alpha,gamma-copy,gamma-moved}, which still exist. '
    'They dangle the moment .work/test-scratch is swept.')
add(rf'{L}\BrowserAI\live', 'KEEP', 'LiveInstances.DirectoryUnder(data root) for a NON-installed process',
    'Empty. Per LiveInstances the markers are keyed to the INSTALL root; an uninstalled (dotnet run / test host) process '
    'passes its data root instead, which is what made this directory. The current layout does produce it.')
add(rf'{L}\BrowserAI\mcp-registration.json', 'KEEP', 'RegistrationRecord.FileName',
    'Records the 2026-09-15 install; command points at BrowserAI.app\\current\\BrowserAI.exe.')

for d, verdict in [
        ('104168-d2e23afff94d4f6aa95231db3c102935', 'GARBAGE'), ('117628-db2906348c0f444e819ad90e69512648', 'GARBAGE'),
        ('20836-bdc78e50cd304648a903389a8154bc38', 'GARBAGE'), ('61980-d207c0f700264dc5b96c65261888ffaa', 'GARBAGE'),
        ('6372-bd23a768638843d090afd30417ccf647', 'GARBAGE'), ('73668-4182d853558740b184e309575f6b82ec', 'GARBAGE'),
        ('48752-594847f6c5244be68e17d11d3166c686', 'KEEP'), ('68140-9f53f96e0f83492c91309bb715f8b766', 'KEEP'),
        ('68852-ce1720fdcd4a4f0fa8b99147b9ecd36d', 'KEEP'), ('76192-e8e7f1b0823f42b194911165b84a6da5', 'KEEP'),
        ('83980-a1c18cb1f34c448caae56f5e0ed08884', 'KEEP')]:
    pid = d.split('-')[0]
    note = (f'pid {pid} is dead; instance directory left behind by the 2026-09-16 00:34-00:35 run'
            if verdict == 'GARBAGE'
            else f'pid {pid} is ALIVE (BrowserAI.exe out of BrowserAI.app\\current) and holds instance.live')
    add(rf'{L}\BrowserAI\instances\{d}', verdict, 'InstanceDirectory (IAppPaths.InstanceRoot)', note)

# ---- current install ----
add(rf'{L}\BrowserAI.app', 'KEEP', 'Velopack installer, pack id BrowserAI.app',
    'The current install: current\\ (BrowserAI.exe 1.0.0), Update.exe, live\\ (5 held markers matching 5 live pids), packages\\.')
add(rf'{L}\BrowserAI.app\packages\BrowserAI.app-1.0.0-full.nupkg', 'ASK', 'Velopack',
    'The installed package Velopack keeps so a delta can be applied against it. Deleting it forces the next update to take '
    'a full package; it is not stale.')

# ---- gone / absent ----
add(rf'{L}\BrowserAI-rc', 'KEEP', 'RC install, uninstalled 2026-09-15', 'ABSENT - confirmed gone. Nothing to delete.')

# ---- stray roots ----
add(rf'{L}\BrowserAI-test-scratch', 'GARBAGE', 'the real-installer test arm (BROWSERAI_ROOT override)',
    'Empty directory. velopack_BrowserAI.app.test.log ends by scheduling rmdir of '
    'BrowserAI-test-scratch\\real-install-window-255debdc...; the child went, the parent did not.')

# ---- velopack logs ----
add(rf'{L}\velopack\velopack_BrowserAI.log', 'GARBAGE', 'Velopack, OLD pack id "BrowserAI"',
    'Last entry 2026-09-15 16:32:38 running the --veloapp-uninstall fast-exit hook. That pack id no longer exists.')
add(rf'{L}\velopack\velopack_BrowserAI.app.test.log', 'GARBAGE', 'Velopack, TEST pack id "BrowserAI.app.test"',
    'Last entry 2026-09-16 00:35:53 scheduling removal of the test install root. The test pack is uninstalled and has no ARP key.')
add(rf'{L}\velopack\velopack_BrowserAI.app.log', 'KEEP', 'Velopack, CURRENT pack id "BrowserAI.app"',
    'The live install log; last entry 2026-09-16 00:38:03 polling releases.win.json.')
add(rf'{L}\velopack\velopack.log', 'ASK', 'Velopack shared log (all pack ids and unpackaged runs)',
    '15.0 MiB. The current install still appends to it, so it is not stale - it is just large.')

# ---- squirrel ----
add(rf'{L}\SquirrelTemp', 'ASK', 'NOT OURS - classic Squirrel: GitHub Desktop, Discord, Teams',
    'Squirrel-Install.log installs GitHubDesktop (2023-05-15 .. 2026-09-03), SquirrelSetup.log installs Discord, '
    'setup.json names Teams_windows_x64.exe. grep -c browserai = 0 in both logs. Unrelated to BrowserAI.')

# ---- playwright caches ----
add(rf'{L}\ms-playwright', 'ASK', 'NOT OURS except the b\\ subtree below - npx / other-project Playwright installs',
    'chromium-1200/1223/1234/1243 + headless shells + ffmpeg-1011 + winldd-1007 + mcp-chrome-4c8abff (2026-04) + '
    'mcp-chrome-8e8f280 (2026-03) predate or sit outside BrowserAI, which provisions into %LocalAppData%\\BrowserAI\\browsers. '
    'The size given is the WHOLE tree, b\\ included.')
add(rf'{L}\ms-playwright\b', 'GARBAGE',
    'OURS - playwright-core debug/attribution records from this repo\'s own suite runs',
    '26,891 browser@<guid> JSON files, 2026-08-14 05:36 .. 2026-09-16 00:37. 26,888 of them carry '
    'playwrightLib = <repo>\\src\\BrowserAI\\bin\\Release\\...\\payload\\mcp\\node_modules\\playwright-core and a '
    'downloadsPath under .work\\test-scratch; ZERO name BrowserAI.app. Written to defaultCacheDirectory(), which '
    'PLAYWRIGHT_BROWSERS_PATH does not move. Nothing prunes it.')
add(rf'{L}\ms-playwright-mcp', 'GARBAGE', 'upstream @playwright/mcp default profile fallback (unset browser.userDataDir)',
    '11 profile dirs. 7 x mcp-chrome-* dated 2026-08-20 (121.0 MiB) are the leak documented in kb/re-verification.md row 66. '
    '4 x mcp-chrome-for-testing-* dated 2026-09-15 07:41-08:08 (52.6 MiB) fall inside the upstream-review window whose bare '
    'control install is .work/2026-09-15-upstream/control/node_modules/@playwright/mcp - I could NOT prove which process '
    'wrote those four. Row 66 prescribes deleting the whole directory and requiring it to stay absent.')

# ---- temp ----
names = os.listdir(T) if os.path.isdir(T) else []
pa = [os.path.join(T, n) for n in names if n.startswith('playwright-artifacts-') and os.path.isdir(os.path.join(T, n))]
pp = [os.path.join(T, n) for n in names if n.startswith('playwright_chromiumdev_profile-') and os.path.isdir(os.path.join(T, n))]
tb = tc = 0
for p in pa:
    b, c = size_of(p)
    tb += b
    tc += c
add(rf'{T}\playwright-artifacts-* ({len(pa)} directories)', 'GARBAGE',
    'playwright-core per-context artifact dirs (child of BrowserAI)',
    'All empty. 2026-09-15 09:13 .. 2026-09-16 00:35 - produced by yesterday and today suite/session runs and never removed.',
    tb, tc, '2026-09-16 00:35')
tb2 = tc2 = 0
for p in pp:
    b, c = size_of(p)
    tb2 += b
    tc2 += c
add(rf'{T}\playwright_chromiumdev_profile-* ({len(pp)} directories)', 'GARBAGE',
    'playwright-core throwaway chromium profile dirs', 'Both empty, 2026-09-15 18:10 and 18:11.', tb2, tc2, '2026-09-15 18:11')
add(rf'{T}\velopack', 'GARBAGE', 'Velopack scratch', 'Empty directory, created 2026-09-16 00:18.')

# ---- repo ----
add(rf'{REPO}\Releases\BrowserAI.app-1.0.1-alpha.0.19-full.nupkg', 'KEEP', 'vpk pack, current head',
    'Head of the local feed; named by assets.win.json and releases.win.json.')
add(rf'{REPO}\Releases\BrowserAI.app-1.0.1-alpha.0.19-delta.nupkg', 'KEEP', 'vpk pack, current head', 'Named by assets.win.json.')
add(rf'{REPO}\Releases\BrowserAI.exe', 'KEEP', 'vpk pack, current head installer', '')
add(rf'{REPO}\Releases\BrowserAI.zip', 'KEEP', 'vpk pack, current head portable', '')
add(rf'{REPO}\Releases\BrowserAI.app-1.0.1-alpha.0.2-full.nupkg', 'ASK', 'vpk pack 2026-09-15 18:07, superseded by alpha.0.19',
    'Still listed in Releases\\RELEASES and Releases\\releases.win.json, so deleting it leaves the local feed naming a file '
    'that is not there. A copy is in Releases\\archive.')
add(rf'{REPO}\Releases\BrowserAI.app-1.0.1-alpha.0.2-delta.nupkg', 'ASK', 'vpk pack 2026-09-15 18:07, superseded',
    'Listed in releases.win.json. No copy in Releases\\archive.')
add(rf'{REPO}\Releases\BrowserAI.app-1.0.0-full.nupkg', 'ASK', 'vpk pack 2026-09-15 16:17 (v1.0.0)',
    'Same size as Releases\\archive\\BrowserAI.app-1.0.0-full.nupkg (both 50,198,035 B); still listed in RELEASES and releases.win.json.')
add(rf'{REPO}\Releases\archive', 'KEEP', 'kept release evidence',
    'Named KEEP by the order. Holds 0.1.1 / 0.1.2 / 0.1.3 packs and manifests plus 1.0.0 / 1.0.1-alpha.0.2 / 1.0.1-alpha.0.19.')
add(rf'{REPO}\Releases\test-pack', 'ASK', 'vpk pack id BrowserAI.app.test (real-installer test arm)',
    'Not covered by the order. The test pack is uninstalled, has no ARP key and no install root; these 8 files are only '
    'needed if the real-installer arm is re-run from a prebuilt pack.')
add(rf'{REPO}\artifacts', 'ASK', 'dotnet publish output (gitignored)',
    'Build output, regenerable. Not old-version garbage, but it is reproducible bytes.')
add(rf'{REPO}\payload', 'KEEP', 'the bundled @playwright/mcp + node payload the build consumes', 'Product input, not scratch.')

# ---- .work ----
work = rf'{REPO}\.work'
entries = []
for name in os.listdir(work):
    p = os.path.join(work, name)
    if os.path.isdir(p):
        b, c = size_of(p)
        entries.append((name, p, b, c, os.path.getmtime(p)))
entries.sort(key=lambda e: e[4], reverse=True)
for name, p, b, c, m in entries:
    dt = datetime.datetime.fromtimestamp(m)
    day = dt.strftime('%Y-%m-%d')
    if day >= '2026-09-15':
        v, note = 'KEEP', "today's or yesterday's batch"
    elif name in CITED:
        v, note = 'KEEP', 'cited in .work/STATE.md'
    else:
        v, note = 'ASK', 'older than yesterday and not cited in .work/STATE.md'
    add(p, v, 'agent scratch (.work, gitignored)', note, b, c, dt.strftime('%Y-%m-%d %H:%M:%S'))

lf = [os.path.join(work, n) for n in os.listdir(work) if os.path.isfile(os.path.join(work, n))]
old = [f for f in lf if datetime.datetime.fromtimestamp(os.path.getmtime(f)).strftime('%Y-%m-%d') < '2026-09-14']
ob = sum(os.path.getsize(f) for f in old)
add(rf'{work}\<loose files older than 2026-09-14> ({len(old)} files)', 'ASK', 'agent scratch at .work root',
    'Loose .py/.log/.txt/.md at the root of .work, all 2026-08. STATE.md, IN-FLIGHT.md, PLAN-course-correction.md and the '
    '2026-09 files are NOT in this group.', ob, len(old), '2026-08-30')

# ---- things checked and clean ----
add(r'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BrowserAI.app', 'KEEP', 'Velopack ARP entry',
    'DisplayName BrowserAI, 1.0.0, InstallLocation %LocalAppData%\\BrowserAI.app. The only BrowserAI ARP key on the machine.', 0, 0, '')
add(r'HKCU\...\Uninstall\{BrowserAI, BrowserAI-rc, BrowserAI.app.test}', 'KEEP', '-',
    'ABSENT - all three already gone. HKLM and HKLM\\WOW6432Node hold no BrowserAI key either.', 0, 0, 'ABSENT')
add(r'HKCU\Software\BrowserAI* and HKLM\Software\BrowserAI*', 'KEEP', '-', 'ABSENT - no such key under either hive.', 0, 0, 'ABSENT')
add(r'Start Menu (user + common) and Desktop (user + public): BrowserAI*.lnk', 'KEEP', '-',
    'ABSENT - zero matches in %AppData%\\...\\Start Menu\\Programs (recursive), C:\\ProgramData\\...\\Start Menu\\Programs '
    '(top level only - a recursive walk of ProgramData is blocked here), C:\\Users\\jori\\Desktop and C:\\Users\\Public\\Desktop.',
    0, 0, 'ABSENT')
add(r'C:\Users\jori\.claude.json -> mcpServers.browserai', 'KEEP', 'BrowserAI Registration',
    'Single entry, command = %LocalAppData%\\BrowserAI.app\\current\\BrowserAI.exe. No stale BrowserAI path in any '
    'project-scoped mcpServers block. REPORT ONLY - not edited.', 0, 0, '')
add(r'C:\Source\**\.mcp.json naming a BrowserAI path', 'KEEP', '-',
    '15 .mcp.json files under C:\\Source at full depth; exactly one names BrowserAI, and it is the scratch fixture below. '
    'No real project config anywhere under C:\\Source registers BrowserAI.', 0, 0, 'ABSENT')
add(rf'{REPO}\.work\2026-09-15-app\proj\.mcp.json', 'KEEP', 'a project-scope registration test fixture, 2026-09-15',
    'Names ${LOCALAPPDATA}/BrowserAI.app/current/BrowserAI.Server.exe, which does NOT exist - the installed binary is '
    'BrowserAI.exe. It is a fixture inside a KEEP .work batch, not a live registration. REPORT ONLY - not edited.')
add(r'%USERPROFILE%\Downloads\tmp-* (98 dirs + 4 files)', 'ASK', 'the user-global scratch convention, unrelated tasks',
    'NOT BrowserAI garbage: no directory name refers to BrowserAI, velopack or playwright. 2026-08-23 .. 2026-09-15. '
    'Listed only because the order named the path.', 22834254589, 102, '2026-09-15 18:26')
add(r'Orphaned processes', 'KEEP', '-',
    'NONE. 5 BrowserAI.exe (pids 48752, 68140, 68852, 76192, 83980) + 5 node.exe, every image path under '
    'BrowserAI.app\\current. No process runs out of a deleted root or out of .work.', 0, 0, '')

with open(OUT, 'w', newline='', encoding='utf-8') as f:
    w = csv.DictWriter(f, fieldnames=['path', 'bytes', 'human', 'files', 'last_write', 'verdict', 'created_by', 'note'])
    w.writeheader()
    for r in rows:
        w.writerow(r)

tot = {}
for r in rows:
    tot.setdefault(r['verdict'], [0, 0])
    tot[r['verdict']][0] += r['bytes']
    tot[r['verdict']][1] += 1
print('WROTE', OUT, len(rows), 'rows')
for k in sorted(tot):
    print(f'{k}: {tot[k][1]} rows, {tot[k][0]} bytes = {human(tot[k][0])}')
print()
print('GARBAGE breakdown:')
for r in rows:
    if r['verdict'] == 'GARBAGE':
        print(f"  {r['bytes']:>12}  {r['human']:>10}  {r['path']}")
print()
print('ASK breakdown (over 1 MiB):')
for r in rows:
    if r['verdict'] == 'ASK' and r['bytes'] > 1048576:
        print(f"  {r['bytes']:>14}  {r['human']:>10}  {r['path']}")
