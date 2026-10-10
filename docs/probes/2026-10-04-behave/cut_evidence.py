# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Cuts the behaviour rig's run folders into an evidence batch the way
# docs/evidence/README.md asks: text kept as written, the user profile path and
# terminal colour escapes cut from a copy stored under a .trimmed. name with the
# original's SHA-256 in originals.sha256, and images, profiles and anything over
# the size limit left out whole, each with its SHA-256 in left-out.sha256.
#
#   python cut_evidence.py <runs root> <destination> <batch>[,<batch>...]
import hashlib
import os
import re
import sys
from pathlib import Path

LIMIT = 400 * 1024
TEXT = {'.json', '.log', '.txt', '.reg', '.yml', '.armed', '.err', '.out'}
PROFILE = re.compile(r'C:(\\\\|\\|/)Users(\\\\|\\|/)[^\\/"\s]+', re.IGNORECASE)
# A colour escape, and one a truncated line cut short before its closing letter.
ESCAPES = re.compile('\x1b(?:\\[[0-9;]*m?)?')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main(root, destination, batches):
    root = Path(root)
    destination = Path(destination)
    originals = []
    left_out = []
    kept = 0
    kept_bytes = 0
    for batch in batches:
        for path in sorted((root / batch).rglob('*')):
            if not path.is_file():
                continue
            relative = path.relative_to(root)
            data = path.read_bytes()
            if path.suffix.lower() not in TEXT:
                left_out.append(f'{sha(data)}  {relative.as_posix()}  ({len(data)} bytes, {path.suffix.lstrip(".") or "no extension"}, left out whole)')
                continue
            if len(data) > LIMIT:
                left_out.append(f'{sha(data)}  {relative.as_posix()}  ({len(data)} bytes, over {LIMIT} bytes, left out whole)')
                continue
            text = data.decode('utf-8', errors='replace').replace('\r\n', '\n')
            cut, profiles = PROFILE.subn(lambda m: '%USERPROFILE%', text)
            cut, escapes = ESCAPES.subn('', cut)
            target = destination / relative
            if profiles or escapes:
                target = target.with_name(target.stem + '.trimmed' + target.suffix)
                what = []
                if profiles:
                    what.append(f'the user profile path replaced by %USERPROFILE%, {profiles} time(s)')
                if escapes:
                    what.append(f'{escapes} terminal colour escape(s) removed')
                originals.append(f'{sha(data)}  {relative.as_posix()}  -> {target.name}: {"; ".join(what)}')
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(cut, encoding='utf-8', newline='\n')
            kept += 1
            kept_bytes += len(cut.encode('utf-8'))
    (destination / 'originals.sha256').write_text('\n'.join(originals) + '\n', encoding='utf-8', newline='\n')
    (destination / 'left-out.sha256').write_text('\n'.join(left_out) + '\n', encoding='utf-8', newline='\n')
    print(f'kept {kept} files, {kept_bytes} bytes; trimmed {len(originals)}; left out {len(left_out)}')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], sys.argv[3].split(','))
