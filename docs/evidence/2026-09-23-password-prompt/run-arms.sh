# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

set -u
run() {
  name="$1"; shift
  ../../payload/node/node.exe probe.mjs --browser chromium --arm "$name" "$@" > "run-chromium-$name.log" 2>&1
  echo "EXIT=$?" >> "run-chromium-$name.log"
  echo "=== $name ==="
  grep -E 'NEW top|VERDICT|Preferences after|seeded |launchOptions' "run-chromium-$name.log"
}
run cred-only   --seed-preferences --seed-json '{"credentials_enable_service":false}'
run pmenabled-only --seed-preferences --seed-json '{"profile":{"password_manager_enabled":false}}'
run automation  --extra-switch '--enable-automation'
