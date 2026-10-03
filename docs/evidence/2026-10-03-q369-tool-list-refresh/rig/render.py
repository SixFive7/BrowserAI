"""Render the pseudoconsole screen of a Q369 TUI run at every SNAP mark (and at the end).

usage: python render.py <run-dir> [--all-marks]
Reads <run>/pty-raw.bin (per chunk: double QPC ms, int32 length, bytes) and the SNAP / MARK
lines of <run>/harness.log, replays the bytes into a 140x45 pyte screen up to each mark's
time, and prints the screen. Writes the same to <run>/screens.txt.
"""
import os, struct, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'pylib'))
import pyte

def chunks(path):
    out = []
    with open(path, 'rb') as f:
        data = f.read()
    i = 0
    while i + 12 <= len(data):
        t = struct.unpack('<d', data[i:i + 8])[0]
        n = struct.unpack('<i', data[i + 8:i + 12])[0]
        out.append((t, data[i + 12:i + 12 + n]))
        i += 12 + n
    return out

def marks(run, all_marks):
    res = []
    for line in open(os.path.join(run, 'harness.log'), encoding='utf-8', errors='replace'):
        p = line.rstrip('\n').split('\t')
        if len(p) < 6:
            continue
        if p[4] == 'SNAP':
            label, _, q = p[5].partition('\tqpc=')
            res.append((float(q) if q else float(p[0]), label, p[1]))
        elif all_marks and p[4] in ('MARK', 'TYPED'):
            res.append((float(p[0]), f'{p[4]} {p[5][:60]}', p[1]))
    return res

def main():
    run = sys.argv[1]
    all_marks = '--all-marks' in sys.argv
    cs = chunks(os.path.join(run, 'pty-raw.bin'))
    ms = marks(run, all_marks)
    ms.append((float('inf'), 'END', ''))
    screen = pyte.Screen(140, 45)
    stream = pyte.ByteStream(screen)
    out = []
    ci = 0
    for t, label, utc in ms:
        while ci < len(cs) and cs[ci][0] <= t:
            stream.feed(cs[ci][1])
            ci += 1
        lines = [l.rstrip() for l in screen.display]
        while lines and not lines[-1]:
            lines.pop()
        out.append(f'===== {label} ({utc}) =====')
        out.extend(lines)
    text = '\n'.join(out) + '\n'
    open(os.path.join(run, 'screens.txt'), 'w', encoding='utf-8').write(text)
    sys.stdout.reconfigure(encoding='utf-8')
    print(text)

if __name__ == '__main__':
    main()
