# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

set -u
run() {
  m="$1"; n="$2"; q="$3"; shift 3
  ../../payload/node/node.exe google.mjs "$m" "$n" "$q" "$@" > "g-$m-$n.log" 2>&1
  echo "=== $m/$n ==="
  ../../payload/node/node.exe -e "
    const v=require('./google/$m-$n/verdict.json').verdict;
    const cls = v.sorryPath||v.unusualTraffic ? 'UNUSUAL-TRAFFIC/CAPTCHA' : v.consentHost||v.consentText ? 'CONSENT' : v.rso||v.search ? 'RESULTS('+v.resultLinks+')' : 'OTHER';
    console.log(cls, '| recaptchaIframes='+v.recaptchaIframe, '| url='+String(v.url).slice(0,90));
  " 2>&1 || tail -5 "g-$m-$n.log"
}
run funnel auto-1 "unix domain socket credentials" --enable-automation
run funnel plain-2 "ntfs reparse point junction"
run funnel auto-2 "etw provider manifest" --enable-automation
