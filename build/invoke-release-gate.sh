#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
#
# The Git Bash half of the RELEASE gate: three full runs under
# BROWSERAI_RELEASE_RUN, each forcing a LOWER-case drive letter and declaring it.
#
# RELEASING.md item 8 owns when this is run; TESTING.md owns how. It is
# invoke-ordinary-gate.sh three times with the release variable set, and the two
# differences are the whole point:
#
#   - THREE RUNS, because the repetition buys the flake that appears once in
#     three. That is how the probe-report race was found on 2026-08-19. On an
#     intermediate batch it buys nothing, which is why the ordinary gate is a
#     separate file and not a switch.
#   - BROWSERAI_RELEASE_RUN=1, which turns every capability skip into a failure
#     and makes a filtered run refuse itself from the session hook.
#
# NOT THE SHARED WRAPPER CLAUDE.md FORBIDS, for the reason the ordinary half
# gives: the PowerShell half forces the other spelling and is a different
# instrument, and nothing here stands in for it.
#
# IT STOPS ON THE FIRST CLEARANCE DIFFERENCE and does not run the rest: a run
# that disturbed the maintainer's Add/Remove entry, registration or Start Menu
# shortcut is a run whose successors would be measuring a changed machine.
#
# Usage: build/invoke-release-gate.sh [runs] [prefix]
set -u

runs="${1:-3}"
prefix="${2:-rel-bash}"
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
cd "$root" || exit 1

# The forced spelling, as an argument and not a `cd` -- see
# invoke-ordinary-gate.sh for why that is the only place it can be forced.
forced=$(cygpath -m "$root")
forced="$(printf %s "${forced:0:1}" | tr 'A-Z' 'a-z')${forced:1}"
windows=$(cygpath -w "$root")

mkdir -p .work/suite

# THE INSTALLER LOCK, BEFORE THE FIRST CLEARANCE SNAPSHOT AND LET GO AFTER THE
# LAST -- Q291, the maintainer's words verbatim: "Q291 a". The suite takes
# .work/installer.lock itself when a session starts; this driver takes it first,
# for the Windows pid of this bash process, which lives for the whole gate, and
# declares the token so the test host it starts finds the holder it was told
# about instead of waiting for it. A live holder is waited for, and a gate that
# could not take it runs nothing. The trap lets it go on every way out.
winpid=$(cat /proc/$$/winpid)
token=$(pwsh -NoProfile -File "$windows\\build\\InstallerLock.ps1" -Take -HolderPid "$winpid") || {
  echo 'RELEASE-BASH-ABORTED-ON-LOCK'
  exit 1
}
export BROWSERAI_INSTALLER_LOCK_HELD="$token"
trap 'pwsh -NoProfile -File "$windows\\build\\InstallerLock.ps1" -Release -HolderPid "$winpid" >/dev/null 2>&1' EXIT
echo "installer lock held: $token"

n=1
while [ "$n" -le "$runs" ]; do
  tag="$prefix-$n"
  echo "=== RELEASE RUN $tag starting $(date +%H:%M:%S) ==="

  pwsh -NoProfile -File "$windows\\build\\Get-ClearanceSnapshot.ps1" -Tag "$tag-before" >/dev/null

  waited=0
  while [ -d .work/test-scratch ] && [ -n "$(ls -A .work/test-scratch 2>/dev/null)" ] && [ "$waited" -lt 120 ]; do
    find .work/test-scratch -mindepth 1 -maxdepth 1 -exec rm -rf {} + 2>/dev/null
    sleep 2
    waited=$((waited + 2))
  done
  echo "scratch released after ${waited}s"

  log=".work/suite/$tag.log"
  BROWSERAI_RELEASE_RUN=1 BROWSERAI_DRIVE_CASE=lower dotnet test "$forced/BrowserAI.slnx" 2>&1 | tee "$log"
  cat .work/suite-coverage.txt >> "$log" 2>/dev/null

  pwsh -NoProfile -File "$windows\\build\\Get-ClearanceSnapshot.ps1" -Tag "$tag-after" >/dev/null

  if ! diff -q ".work/clearance/$tag-before.txt" ".work/clearance/$tag-after.txt" >/dev/null; then
    echo "=== CLEARANCE DIFF ON $tag - STOPPING ==="
    diff ".work/clearance/$tag-before.txt" ".work/clearance/$tag-after.txt"
    echo 'RELEASE-BASH-ABORTED-ON-CLEARANCE'
    exit 1
  fi

  echo "=== RELEASE RUN $tag done $(date +%H:%M:%S), clearance clean ==="
  n=$((n + 1))
done

echo 'RELEASE-BASH-DONE'
