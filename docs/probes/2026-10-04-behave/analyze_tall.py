# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# For every image a tall run copied out: its size as the file's own header says
# it, and whether the rows (or, for a wide image, the columns) at or past 16,384
# px repeat the ones 16,384 px before them. A row counts as repeated when it is
# byte-identical to that row, and, for JPEG, also when its coloured key block
# (the first 200 px, where neighbouring bands differ by at least 67 in red) is
# within a mean difference of 8, because a JPEG encoder rounds. The control:
# rows 1,000 px apart below the seam, which always show different bands, so a
# repeat found past the seam is not something the check makes.
import json
import struct
import sys
from pathlib import Path

import numpy
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
SEAM = 16384


def header_size(path):
    data = Path(path).read_bytes()[:65536]
    if data[:8] == b'\x89PNG\r\n\x1a\n':
        width, height = struct.unpack('>II', data[16:24])
        return 'png', width, height
    if data[:2] == b'\xff\xd8':
        i = 2
        while i + 9 < len(data):
            if data[i] != 0xFF:
                i += 1
                continue
            marker = data[i + 1]
            if marker in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF):
                height, width = struct.unpack('>HH', data[i + 5:i + 9])
                return 'jpeg', width, height
            length = struct.unpack('>H', data[i + 2:i + 4])[0]
            i += 2 + length
        return 'jpeg', None, None
    return 'unknown', None, None


def repeats(lines, seam, jpeg):
    same = 0
    close = 0
    first = None
    for y in range(seam, len(lines)):
        a = lines[y].astype(numpy.int16)
        b = lines[y - seam].astype(numpy.int16)
        identical = bool((a == b).all())
        near = identical or (jpeg and float(numpy.abs(a[:200] - b[:200]).mean()) < 8.0)
        same += identical
        close += near
        if not near and first is None:
            first = y
    return same, close, first


def control(lines, jpeg, upto):
    near = 0
    for y in range(1000, upto):
        a = lines[y].astype(numpy.int16)
        b = lines[y - 1000].astype(numpy.int16)
        if bool((a == b).all()) or (jpeg and float(numpy.abs(a[:200] - b[:200]).mean()) < 8.0):
            near += 1
    return near, max(0, upto - 1000)


def analyse(path):
    kind, hw, hh = header_size(path)
    image = numpy.asarray(Image.open(path).convert('RGB'))
    height, width = image.shape[0], image.shape[1]
    jpeg = kind == 'jpeg'
    out = {'file': Path(path).name, 'kind': kind, 'header_width': hw, 'header_height': hh, 'decoded_width': int(width), 'decoded_height': int(height)}
    if height > SEAM:
        same, close, first = repeats(image, SEAM, jpeg)
        near, checked = control(image, jpeg, min(height, SEAM))
        out.update({'rows_past_the_seam': int(height - SEAM), 'rows_identical_to_16384_above': same, 'rows_near_16384_above': close,
                    'first_row_not_repeated': first, 'control_rows_near_1000_above': near, 'control_rows_checked': checked})
    if width > SEAM:
        columns = numpy.transpose(image, (1, 0, 2))
        same, close, first = repeats(columns, SEAM, jpeg)
        out.update({'columns_past_the_seam': int(width - SEAM), 'columns_identical_to_16384_left': same, 'columns_near_16384_left': close, 'first_column_not_repeated': first})
    return out


if __name__ == '__main__':
    results = [analyse(p) for p in sys.argv[1:]]
    print(json.dumps(results, indent=1))
