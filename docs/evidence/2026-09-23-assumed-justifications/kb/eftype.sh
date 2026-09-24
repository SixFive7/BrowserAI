# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/bin/sh
# Probe: what does launching a CORRUPTED-but-INSTALLATION_COMPLETE browser actually report?
set -u
CORE="C:/Source/SixFive7/BrowserAI/payload/mcp/node_modules/playwright-core"
NODE="C:/Source/SixFive7/BrowserAI/payload/node/node.exe"
BASE="C:/Source/SixFive7/BrowserAI/.work/assumed-2026-09-23/kb/eftype"
cat > "$BASE.js" <<'JS'
const { chromium } = require(process.env.CORE_PATH);
(async () => {
  try {
    const b = await chromium.launch({ headless: true, timeout: 20000 });
    console.log('LAUNCHED OK');
    await b.close();
  } catch (e) {
    console.log('ERROR NAME : ' + e.name);
    console.log('ERROR CODE : ' + (e.code ?? '<none>'));
    console.log('ERROR MSG  : ' + String(e.message).split('\n').slice(0, 8).join(' | '));
  }
})();
JS
EXE_REL="chromium_headless_shell-1246/chrome-headless-shell-win64/chrome-headless-shell.exe"
arm() { # $1 label  $2 how-to-make the exe
  root="$BASE/$1"; rm -rf "$root"
  mkdir -p "$root/chromium_headless_shell-1246/chrome-headless-shell-win64" "$root/.links"
  : > "$root/chromium_headless_shell-1246/INSTALLATION_COMPLETE"
  exe="$root/$EXE_REL"
  eval "$2"
  echo "=== ARM $1 ==="
  ls -l "$exe" 2>&1 | sed 's/^/    /'
  CORE_PATH="$CORE" PLAYWRIGHT_BROWSERS_PATH="$root" PLAYWRIGHT_SKIP_BROWSER_GC=1 \
    PLAYWRIGHT_CHROMIUM_DOWNLOAD_HOST="http://127.0.0.1:9" PLAYWRIGHT_DOWNLOAD_HOST="http://127.0.0.1:9" \
    "$NODE" "$BASE.js" 2>&1 | sed 's/^/    /'
  echo
}
mkdir -p "$BASE"
arm zero         ': > "$exe"'
arm garbage      'printf "NOTAPEFILE" > "$exe"'
arm truncated-pe 'head -c 2048 "C:/Source/SixFive7/BrowserAI/payload/node/node.exe" > "$exe"'
arm CONTROL-valid-exe-wrong-program 'cp "C:/Source/SixFive7/BrowserAI/payload/node/node.exe" "$exe"'
arm CONTROL-missing 'true'
