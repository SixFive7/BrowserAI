# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Whether every pixel row at or below 16,384 px is byte-identical to the row
# 16,384 px above it, and where the first row that is not lies.
import json
import sys

import numpy
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
PERIOD = 16384

image = numpy.asarray(Image.open(sys.argv[1]).convert('RGB'))
height = image.shape[0]
same = [bool((image[y] == image[y - PERIOD]).all()) for y in range(PERIOD, height)]
first_different = next((PERIOD + i for i, s in enumerate(same) if not s), None)
# The positive control: rows the same distance apart ABOVE the boundary differ
# wherever the page differs, so a repeat cannot be an artefact of the check.
control = [bool((image[y] == image[y - 1000]).all()) for y in range(1000, PERIOD)]
print(json.dumps({
    'file': sys.argv[1],
    'height': int(height),
    'rows_at_or_below_16384': len(same),
    'rows_identical_to_the_row_16384_above': int(sum(same)),
    'first_row_that_differs': first_different,
    'control_rows_above_16384_identical_to_the_row_1000_above': int(sum(control)),
    'control_rows_checked': len(control),
    'pixel_at_16383': image[16383, 100].tolist(),
    'pixel_at_16384': image[16384, 100].tolist(),
    'pixel_at_0': image[0, 100].tolist(),
}, indent=1))
