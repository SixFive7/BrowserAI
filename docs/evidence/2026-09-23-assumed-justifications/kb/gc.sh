# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/bin/sh
# Probe: Playwright's stale-browser GC blast radius against a browsers tree nothing links to.
set -u
CORE="C:/Source/SixFive7/BrowserAI/payload/mcp/node_modules/playwright-core"
NODE="C:/Source/SixFive7/BrowserAI/payload/node/node.exe"
BASE="C:/Source/SixFive7/BrowserAI/.work/assumed-2026-09-23/kb/gc"
plant() {  # $1 = root
  rm -rf "$1"; mkdir -p "$1/.links"
  for d in chromium-1246 chromium-1244 chromium_headless_shell-1246 firefox-1549 firefox-1544 ffmpeg-1011 winldd-1007; do
    mkdir -p "$1/$d"; : > "$1/$d/INSTALLATION_COMPLETE"; echo payload > "$1/$d/a-real-file.bin"
  done
  mkdir -p "$1/not-a-browser"; echo keep > "$1/not-a-browser/x.txt"
  echo lock > "$1/reinstall.lock"
}
echo "=== ARM A: .links is EMPTY -- nothing references this tree ==="
plant "$BASE/a"
echo "--- before ---"; ls "$BASE/a"
PLAYWRIGHT_BROWSERS_PATH="$BASE/a" "$NODE" "$CORE/cli.js" uninstall 2>&1
echo "--- after ---"; ls "$BASE/a"

echo
echo "=== ARM B: POSITIVE CONTROL -- one .links entry pointing at this playwright-core package ==="
plant "$BASE/b"
# the link file's NAME is sha1(packagePath) and its CONTENT is the package path; only the content is read
printf '%s' "$CORE" > "$BASE/b/.links/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
echo "--- before ---"; ls "$BASE/b"
PLAYWRIGHT_BROWSERS_PATH="$BASE/b" "$NODE" "$CORE/cli.js" uninstall 2>&1
echo "--- after ---"; ls "$BASE/b"

echo
echo "=== ARM C: does PLAYWRIGHT_SKIP_BROWSER_GC protect the uninstall path? ==="
plant "$BASE/c"
echo "--- before ---"; ls "$BASE/c"
PLAYWRIGHT_SKIP_BROWSER_GC=1 PLAYWRIGHT_BROWSERS_PATH="$BASE/c" "$NODE" "$CORE/cli.js" uninstall 2>&1
echo "--- after ---"; ls "$BASE/c"
