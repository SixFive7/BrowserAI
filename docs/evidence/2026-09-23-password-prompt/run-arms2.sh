# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

set -u
run() {
  name="$1"; shift
  ../../payload/node/node.exe probe.mjs --browser chromium --arm "$name" "$@" > "run-chromium-$name.log" 2>&1
  echo "EXIT=$?" >> "run-chromium-$name.log"
  echo "=== $name ==="
  grep -E 'NEW top|VERDICT|Preferences after|fingerprint|PrintWindow' "run-chromium-$name.log"
}
run fp-baseline
run fp-automation --extra-switch '--enable-automation'
run fp-cred --seed-preferences --seed-json '{"credentials_enable_service":false}'
