# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

"""Two numbers for an A/B of browser losses: Fisher's exact one-sided p over
launches, and the chance of seeing no loss in the 'off' captures if they lost
the browser at the 'on' arm's observed rate per capture.

Usage: python -I stats.py <on launches> <on lost> <on captures> <off launches> <off lost> <off captures>
"""
import math
import sys

on_n, on_lost, on_captures, off_n, off_lost, off_captures = (int(value) for value in sys.argv[1:7])


def choose(n, k):
    return math.comb(n, k)


# One-sided Fisher exact: probability of a table at least this extreme, with
# the margins fixed, that 'on' holds this many or more of the losses.
total = on_n + off_n
lost = on_lost + off_lost
p = 0.0
for k in range(on_lost, min(lost, on_n) + 1):
    p += choose(on_n, k) * choose(off_n, lost - k) / choose(total, lost)
print("launches: on %d/%d lost, off %d/%d lost, Fisher one-sided p = %.4g" % (on_lost, on_n, off_lost, off_n, p))

rate = on_lost / on_captures
print("per capture: on %d in %d (%.2f %%), off %d in %d; P(no loss in %d captures at the on rate) = %.3g"
      % (on_lost, on_captures, 100 * rate, off_lost, off_captures, off_captures, (1 - rate) ** off_captures))
