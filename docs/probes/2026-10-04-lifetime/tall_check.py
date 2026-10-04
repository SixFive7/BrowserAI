# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Reads a full-page screenshot of the local /tall page and says, band by band,
# which band each 100 px row of the image shows. A band's colour encodes its
# own index (R + 256 * G, with B at 128), so the check needs no text
# recognition: band k belongs at y = 100 k, and a repeat every 16,384 px shows
# as band floor((y mod 16384) / 100) where band floor(y / 100) belongs.
import json
import sys

from PIL import Image

Image.MAX_IMAGE_PIXELS = None


def decode(pixel):
    r, g, b = pixel[:3]
    if abs(b - 128) > 3:
        return None
    return r + 256 * g


def main(path, band=100, period=16384):
    image = Image.open(path)
    width, height = image.size
    image = image.convert('RGB')
    rows = []
    for k in range(height // band):
        y = k * band + band // 2
        rows.append((k, y, decode(image.getpixel((100, y)))))
    correct = [r for r in rows if r[2] == r[0]]
    wrong = [r for r in rows if r[2] != r[0]]
    repeat = [r for r in rows if r[2] is not None and r[2] != r[0] and r[2] == (r[1] % period) // band]
    first_wrong = wrong[0] if wrong else None
    summary = {
        'file': path,
        'width': width,
        'height': height,
        'bands_checked': len(rows),
        'bands_correct': len(correct),
        'bands_wrong': len(wrong),
        'wrong_that_match_a_16384_repeat': len(repeat),
        'first_wrong': {'band': first_wrong[0], 'y': first_wrong[1], 'shows': first_wrong[2]} if first_wrong else None,
        'last_correct_y': max((r[1] for r in correct), default=None),
        'samples': [{'band': k, 'y': y, 'shows': s} for (k, y, s) in rows if k in (0, 1, 162, 163, 164, 165, 166, 200, 327, 328, 329, 330, 400, 491, 492, 493, 494, 499)],
    }
    print(json.dumps(summary, indent=1))


if __name__ == '__main__':
    main(sys.argv[1])
