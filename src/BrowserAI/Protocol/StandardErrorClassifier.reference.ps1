# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
#
# THE PINNED COPY OF THE REFERENCE IMPLEMENTATION'S STDERR CLASSIFIER.
#
# Not executed, not imported, and not to be tidied. It exists so that the two
# regexes in StandardErrorClassifier.cs can be asserted byte-for-byte against
# the thing they were ported from, which makes a future edit to either side a
# red build rather than a silent divergence in behaviour.
#
#   Source repository : SixFive7/Workspace657
#   File              : playwright/launch.ps1
#   Commit            : a9ac74738fe63ca8aee588489313b77574e2e504
#   Blob SHA-256      : 6a33e435dffa1c5439fb75f151ed5e02b24784a8cfb7beefd1cd91143c82e61d
#   Lines             : 1043-1050 of 1111
#   Copied            : 2026-08-16
#
# That repository is the only correct copy of this setup in existence: a sweep
# on 2026-08-13 found 13 copies of launch.ps1 across 10 repositories, the nine
# non-Workspace657 ones byte-identical to each other, all differing from
# Workspace657, and all still carrying the bugs fixed on 2026-08-12/13 -- of
# which "warned on any stderr" is one. Never re-copy from anywhere else.
#
# To re-verify: git -C <Workspace657> show a9ac747:playwright/launch.ps1 | sed -n '1043,1050p'
#
# ---- begin verbatim excerpt ----
        # Two groups on purpose. Prefix words (error:, fatal:) only count at the
        # start of a line, so prose like "no errors" does not trip them. Phrases
        # specific enough to be unambiguous match anywhere - notably the missing
        # browser build, which reports mid-sentence as
        # 'Browser "chromium" is not installed; expected executable at ...'.
        $StderrLooksLikeError =
          ($StderrText -match '(?im)^\s*(error\b|fatal\b|unknown option)') -or
          ($StderrText -match '(?i)(is not installed|cannot find|ENOENT|EACCES)')
