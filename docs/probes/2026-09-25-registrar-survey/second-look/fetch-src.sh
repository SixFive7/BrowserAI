#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Downloads a public GitHub repository's source as a tarball from codeload and unpacks it
# under scratch. No git, no credential helper, no GitHub API: a 404 is a 404 and nothing
# can ask for a sign-in.
# Usage: fetch-src.sh <owner> <repo> [ref]      ref defaults to HEAD (the default branch)
set -u
S=/c/Source/SixFive7/BrowserAI/.work/zoomout/a-second-look
owner=$1; repo=$2; ref=${3:-HEAD}
dest="$S/src/${owner}__${repo}"
tgz="$S/src/${owner}__${repo}.tar.gz"
mkdir -p "$S/src"
code=$(curl -sS -L --max-time 180 -o "$tgz" -w '%{http_code}' "https://codeload.github.com/$owner/$repo/tar.gz/$ref")
if [ "$code" != "200" ]; then echo "FAIL $owner/$repo@$ref http=$code"; rm -f "$tgz"; exit 1; fi
sha=$(gzip -dc "$tgz" 2>/dev/null | git get-tar-commit-id 2>/dev/null)
rm -rf "$dest"; mkdir -p "$dest"
tar -xzf "$tgz" -C "$dest" --strip-components=1 2>/dev/null
rm -f "$tgz"
n=$(find "$dest" -type f | wc -l)
echo "OK $owner/$repo@$ref commit=$sha files=$n at $(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee -a "$S/source-commits-read.txt"
