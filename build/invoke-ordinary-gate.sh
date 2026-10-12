#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
#
# The Git Bash half of the ORDINARY gate: one full run, forcing a LOWER-case
# drive letter and declaring it.
#
# TESTING.md owns the invocation; this is that invocation, kept in the tree
# instead of retyped from it. The two halves of a gate are TWO INSTRUMENTS and
# not redundancy: this one hands the test host `c:/...` and declares `lower`,
# the PowerShell half hands it `C:\...` and declares `upper`, and
# SuiteCoverageTests.TheRunReportsTheDriveLetterSpellingItActuallyReceived fails
# a run that did not receive what it declared.
#
# THIS IS NOT THE SHARED WRAPPER SCRIPT AGENTS.md FORBIDS. That rule is about
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

# THE FORCED SPELLING, AND IT IS AN ARGUMENT, NOT A `cd`. A Git Bash that
# inherits its working directory hands a child `c:\...`; the same shell after ANY
# `cd` hands it `C:\...`, because MSYS resolves the real path and Windows always
# answers upper. MSYS re-spells a command path and a `cd` and does NOT touch a
# path passed as an argument, which is what makes forcing it here work at all.
forced=$(cygpath -m "$root")
forced="$(printf %s "${forced:0:1}" | tr 'A-Z' 'a-z')${forced:1}"
windows=$(cygpath -w "$root")

mkdir -p .work/suite

# EVERY FOLDER UNDER .work/kept GOES INTO THE RUN'S RECORDS, BESIDE ITS LOG -- 119, the
# maintainer's words verbatim: "119 your recommendation". See Invoke-OrdinaryGate.ps1: a
# red arm moves its scratch folder there, where no reclaim and no clearing of the scratch
# reaches it, and until 2026-10-12 nothing collected it. After the run and before the
# clearance comparison, each one is copied into .work/suite/<tag>-kept, made fresh for the
# run, and named in the run's log and in this driver's output, with whether this run kept
# it. GateDriverTests runs each driver over three.
kept_names() {
  find .work/kept -mindepth 1 -maxdepth 1 -type d -printf '%f\n' 2>/dev/null | LC_ALL=C sort
}

copy_kept() {
  local records=".work/suite/$1-kept" before="$2" names name when failed
  names=$(kept_names)
  if [ -z "$names" ]; then
    echo 'kept folders: none under .work\kept'
    return
  fi
  mkdir -p "$records"
  while IFS= read -r name; do
    if printf '%s\n' "$before" | grep -qxF -- "$name"; then when='kept before this run'; else when='kept during this run'; fi
    if failed=$(cp -r -- ".work/kept/$name" "$records/" 2>&1); then
      echo "kept folder copied: $name, $when, to .work\\suite\\$1-kept\\$name"
    else
      echo "kept folder $name, $when, could not be copied whole to .work\\suite\\$1-kept\\$name: $(printf '%s\n' "$failed" | head -n 1)"
    fi
  done <<< "$names"
}

echo "=== ORDINARY RUN $tag starting $(date +%H:%M:%S) ==="

# THE INSTALLER LOCK, BEFORE THE FIRST CLEARANCE SNAPSHOT AND LET GO AFTER THE
# LAST -- Q291, the maintainer's words verbatim: "Q291 a". The suite takes
# .work/installer.lock itself when a session starts; this driver takes it first,
# for the Windows pid of this bash process, which lives for the whole gate, and
# declares the token so the test host it starts finds the holder it was told
# about instead of waiting for it. A live holder is waited for, and a gate that
# could not take it runs nothing. The trap lets it go on every way out.
winpid=$(cat /proc/$$/winpid)
token=$(pwsh -NoProfile -File "$windows\\build\\InstallerLock.ps1" -Take -HolderPid "$winpid") || {
  echo 'ORDINARY-BASH-ABORTED-ON-LOCK'
  exit 1
}
export BROWSERAI_INSTALLER_LOCK_HELD="$token"
trap 'pwsh -NoProfile -File "$windows\\build\\InstallerLock.ps1" -Release -HolderPid "$winpid" >/dev/null 2>&1' EXIT
echo "installer lock held: $token"

# THE SUITE'S INSTALLER, PACKED FROM THIS TREE UNDER THE LOCK AND BEFORE THE FIRST
# RUN -- Q287, the maintainer's words verbatim: "Q287 a". See
# Invoke-OrdinaryGate.ps1: New-Release.ps1 -TestPackOnly packs Releases/test-pack
# from the publish this run tests (two until 2026-10-08) and touches nothing a release is made of.
# Publish the slice before starting a gate (both slices until 2026-10-08): this reads it and does not make it.
pack_log=".work/suite/$tag-testpack.log"
if ! pwsh -NoProfile -File "$windows\\build\\New-Release.ps1" -TestPackOnly > "$pack_log" 2>&1; then
  tail -20 "$pack_log"
  echo 'ORDINARY-BASH-ABORTED-ON-TEST-PACK'
  exit 1
fi
grep -E '^vpk pack of .* took' "$pack_log"

pwsh -NoProfile -File "$windows\\build\\Get-ClearanceSnapshot.ps1" -Tag "$tag-before" >/dev/null

# Wait for the rig tree to be RELEASED and not for the previous run to have
# reported: a test host that has printed its summary has not necessarily let go.
waited=0
while [ -d .work/test-scratch ] && [ -n "$(ls -A .work/test-scratch 2>/dev/null)" ] && [ "$waited" -lt 120 ]; do
  find .work/test-scratch -mindepth 1 -maxdepth 1 -exec rm -rf {} + 2>/dev/null
  sleep 2
  waited=$((waited + 2))
done
echo "scratch released after ${waited}s"

# The kept folders there before the run, and the run's copy of them made fresh, 119.
kept_before=$(kept_names)
rm -rf -- ".work/suite/$tag-kept"

log=".work/suite/$tag.log"
# THE RUN WRITES TO A FILE, AND THIS DRIVER WAITS FOR THE RUN'S PROCESS -- 64 c, the
# maintainer's words verbatim: "64 c now". Until 2026-10-11 a run was read through a
# pipe into tee, and a pipe ends only once every process holding its write end has
# closed it. Twice that day a frozen child of a test host held it after the run had
# printed its summary, a suspended git (pid 102292) and a suspended cmd.exe (pid
# 23920), and the driver waited on them with nothing in any log to say why. The run's
# output is a FILE now, which a child that inherits it cannot hold anybody up with;
# the driver waits for the run's own process, and `tail --pid` copies the log into the
# driver's output as it grows and stops once that process has gone. GateDriverTests
# runs each driver against a run that leaves such a child behind.
: > "$log"
BROWSERAI_DRIVE_CASE=lower dotnet test "$forced/BrowserAI.slnx" > "$log" 2>&1 &
run=$!
tail -n +1 -f --pid="$run" "$log"
wait "$run"

# The coverage block reaches .work/suite-coverage.txt and never a `dotnet test`
# log, so a multi-run gate would otherwise keep one block it needs one of per run.
cat .work/suite-coverage.txt >> "$log" 2>/dev/null
said=$(copy_kept "$tag" "$kept_before")
printf '%s\n' "$said"
printf '%s\n' "$said" >> "$log"

pwsh -NoProfile -File "$windows\\build\\Get-ClearanceSnapshot.ps1" -Tag "$tag-after" >/dev/null

if ! diff -q ".work/clearance/$tag-before.txt" ".work/clearance/$tag-after.txt" >/dev/null; then
  echo "=== CLEARANCE DIFF ON $tag - STOPPING ==="
  diff ".work/clearance/$tag-before.txt" ".work/clearance/$tag-after.txt"
  echo 'ORDINARY-BASH-ABORTED-ON-CLEARANCE'
  exit 1
fi

echo "=== ORDINARY RUN $tag done $(date +%H:%M:%S), clearance clean ==="
echo 'ORDINARY-BASH-DONE'
