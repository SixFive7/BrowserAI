#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
#
# The Git Bash half of the ORDINARY gate: one full run, forcing a LOWER-case
# drive letter and declaring it.
#
# TESTING.md owns the invocation; this is that invocation, kept in the tree
# rather than retyped from it. The two halves of a gate are TWO INSTRUMENTS and
# not redundancy: this one hands the test host `c:/...` and declares `lower`,
# the PowerShell half hands it `C:\...` and declares `upper`, and
# SuiteCoverageTests.TheRunReportsTheDriveLetterSpellingItActuallyReceived fails
# a run that did not receive what it declared.
#
# THIS IS NOT THE SHARED WRAPPER SCRIPT CLAUDE.md FORBIDS. That rule is about
# one script standing in for both halves, which would run one instrument twice
# and report what two report. There are four gate scripts here, two per shell,
# and each forces and declares its own spelling.
#
# THREE RUNS PER SHELL IS THE RELEASE GATE AND THIS IS NOT ONE --
# invoke-release-gate.sh is that. BROWSERAI_RELEASE_RUN is deliberately NOT set
# here: it would make the run's own coverage block say `release run YES`.
#
# THE RUN IS DETACHED BY THE CALLER, NOT BY THIS SCRIPT. A grandchild that
# inherits the caller's stdout handle keeps the pipe open after the command has
# exited, so the caller never sees EOF and its timeout does not fire. Start it
# with `nohup bash build/invoke-ordinary-gate.sh > <log> 2>&1 </dev/null &` and
# poll the log -- and read that log for this script's own `starting` line before
# waiting on it. On 2026-09-22 at 19:26 a driver whose script did not exist died
# in milliseconds and read exactly like one that was working.
#
# Usage: build/invoke-ordinary-gate.sh [tag]
set -u

tag="${1:-ord-bash-1}"
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
cd "$root" || exit 1

# THE FORCED SPELLING, AND IT IS AN ARGUMENT RATHER THAN A `cd`. A Git Bash that
# inherits its working directory hands a child `c:\...`; the same shell after ANY
# `cd` hands it `C:\...`, because MSYS resolves the real path and Windows always
# answers upper. MSYS re-spells a command path and a `cd` and does NOT touch a
# path passed as an argument, which is what makes forcing it here work at all.
forced=$(cygpath -m "$root")
forced="$(printf %s "${forced:0:1}" | tr 'A-Z' 'a-z')${forced:1}"
windows=$(cygpath -w "$root")

mkdir -p .work/suite

echo "=== ORDINARY RUN $tag starting $(date +%H:%M:%S) ==="
pwsh -NoProfile -File "$windows\\build\\Get-ClearanceSnapshot.ps1" -Tag "$tag-before" >/dev/null

# Wait for the rig tree to be RELEASED rather than for the previous run to have
# reported: a test host that has printed its summary has not necessarily let go.
waited=0
while [ -d .work/test-scratch ] && [ -n "$(ls -A .work/test-scratch 2>/dev/null)" ] && [ "$waited" -lt 120 ]; do
  find .work/test-scratch -mindepth 1 -maxdepth 1 -exec rm -rf {} + 2>/dev/null
  sleep 2
  waited=$((waited + 2))
done
echo "scratch released after ${waited}s"

log=".work/suite/$tag.log"
BROWSERAI_DRIVE_CASE=lower dotnet test "$forced/BrowserAI.slnx" 2>&1 | tee "$log"

# The coverage block reaches .work/suite-coverage.txt and never a `dotnet test`
# log, so a multi-run gate would otherwise keep one block it needs one of per run.
cat .work/suite-coverage.txt >> "$log" 2>/dev/null

pwsh -NoProfile -File "$windows\\build\\Get-ClearanceSnapshot.ps1" -Tag "$tag-after" >/dev/null

if ! diff -q ".work/clearance/$tag-before.txt" ".work/clearance/$tag-after.txt" >/dev/null; then
  echo "=== CLEARANCE DIFF ON $tag - STOPPING ==="
  diff ".work/clearance/$tag-before.txt" ".work/clearance/$tag-after.txt"
  echo 'ORDINARY-BASH-ABORTED-ON-CLEARANCE'
  exit 1
fi

echo "=== ORDINARY RUN $tag done $(date +%H:%M:%S), clearance clean ==="
echo 'ORDINARY-BASH-DONE'
