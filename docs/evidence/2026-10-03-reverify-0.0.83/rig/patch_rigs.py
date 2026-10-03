import pathlib

W = pathlib.Path(r"C:/Source/SixFive7/BrowserAI/.work/wt/stale")
P = W / "docs/probes"
E = W / "docs/evidence"
R = pathlib.Path(r"C:/Source/SixFive7/BrowserAI/.work/stale-scratch/rigs")
S = r"C:\Source\SixFive7\BrowserAI\.work\stale-scratch"


def patch(src, dst, pairs):
    t = src.read_text(encoding="utf-8")
    for a, b in pairs:
        assert t.count(a) >= 1, (src.name, a)
        t = t.replace(a, b)
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(t, encoding="utf-8", newline="")
    print("patched", dst)


# row 122: raw-child.js. The JS source spells each backslash twice.
patch(P / "2026-09-14-webp-ask/raw-child.js", R / "row122/raw-child.js", [
    (r"const REPO = 'c:\\Source\\SixFive7\\BrowserAI';",
     r"const REPO = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\wt\\stale';"),
    (r"const OUT = path.join(REPO, '.work', '2026-09-14-webp-ask', 'raw-' + TAG);",
     r"const OUT = path.join('C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\out\\row122', 'raw-' + TAG);"),
])

# row 152: fp.mjs
patch(E / "2026-09-23-password-prompt/fp.mjs", R / "row152/fp.mjs", [
    ("const REPO = path.join('C:', 'Source', 'SixFive7', 'BrowserAI');",
     "const REPO = path.join('C:', 'Source', 'SixFive7', 'BrowserAI', '.work', 'wt', 'stale');"),
    ("'chromium-1246'", "'chromium-1247'"),
])

# rows 163-166: the four dashboard rigs, REPO only; C follows it into the worktree's own .work
for f in ["dashboard-probe.cjs", "mcp-pause-probe.cjs", "singleton-probe.cjs", "trace-viewer-probe.cjs"]:
    patch(P / "2026-09-25-dashboard-exposure" / f, W / ".work/zoomout/c/rig" / f, [
        ("const REPO = 'C:/Source/SixFive7/BrowserAI';", "const REPO = 'C:/Source/SixFive7/BrowserAI/.work/wt/stale';"),
    ])

# row 103: the three rename rigs, revision constants, scratch profile and scratch cwd
row103 = S + r"\out\row103"
rename = {
    "rename-under-chromium.ps1": [
        ("chromium-1237", "chromium-1247"),
        (r"Join-Path 'C:\Source\SixFive7\BrowserAI\.work' 'rename-probe-profile'", "Join-Path '" + row103 + "' 'rename-probe-profile'"),
        (r"-WorkingDirectory 'C:\Source\SixFive7\BrowserAI'", "-WorkingDirectory '" + row103 + "'"),
    ],
    "rename-under-firefox.ps1": [
        ("firefox-1539", "firefox-1553"),
        (r"Join-Path 'C:\Source\SixFive7\BrowserAI\.work' 'rename-probe-profile-ff'", "Join-Path '" + row103 + "' 'rename-probe-profile-ff'"),
        (r"-WorkingDirectory 'C:\Source\SixFive7\BrowserAI'", "-WorkingDirectory '" + row103 + "'"),
    ],
    "rename-shared-components.ps1": [
        ("chromium-1237", "chromium-1247"),
        ("firefox-1539", "firefox-1553"),
        (r"Join-Path 'C:\Source\SixFive7\BrowserAI\.work' 'rename-probe-profile-shared'", "Join-Path '" + row103 + "' 'rename-probe-profile-shared'"),
        (r"-WorkingDirectory 'C:\Source\SixFive7\BrowserAI'", "-WorkingDirectory '" + row103 + "'"),
    ],
}
for f, pairs in rename.items():
    patch(P / "2026-08-19-rename-under-browser" / f, R / "row103" / f, pairs)
