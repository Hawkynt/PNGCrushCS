"""Maps a format's layout by changing one byte at a time and asking the reference tool what moved.

A solver guesses a layout and checks it. This asks the layout directly: alter byte n of a real file,
decode both, and the pixels that differ are the ones byte n controls. It does not care whether the
format is understood, only that the tool reads it.
"""
import subprocess
import sys

SC = '/tmp/claude-1000/-home-hawky-Working-Copies-PNGCrushCS/35e733e1-264c-42df-8147-cea5ff119564/scratchpad'
RECOIL = f'{SC}/recoil-6.4.5/recoil2png'

path = sys.argv[1]
offsets = [int(x) for x in sys.argv[2].split(',')]
original = open(path, 'rb').read()
suffix = path[path.rindex('.'):]


def decode(data):
    tmp = f'{SC}/probe/diffprobe{suffix}'
    open(tmp, 'wb').write(data)
    out = f'{SC}/probe/diffprobe.png'
    subprocess.run(['rm', '-f', out], capture_output=True)
    subprocess.run([RECOIL, '-o', out, tmp], capture_output=True)
    try:
        subprocess.run(['magick', out, '-depth', '8', f'{SC}/probe/diffprobe.ppm'],
                       capture_output=True, check=True)
    except subprocess.CalledProcessError:
        return None, 0, 0

    raw = open(f'{SC}/probe/diffprobe.ppm', 'rb').read()
    parts = raw.split(b'\n', 3)
    w, h = map(int, parts[1].split())
    return parts[3], w, h


base, w, h = decode(original)
if base is None:
    print('the reference tool will not read the file as it stands')
    sys.exit(1)

print(f'reference decodes {w}x{h}')
for off in offsets:
    altered = bytearray(original)
    altered[off] ^= 0xFF
    changed, _, _ = decode(bytes(altered))
    if changed is None:
        print(f'byte {off:6}: the tool refuses the file when this byte changes — a header or a length')
        continue

    moved = [i for i in range(w * h) if base[i * 3:i * 3 + 3] != changed[i * 3:i * 3 + 3]]
    if not moved:
        print(f'byte {off:6}: nothing moved — not part of the picture')
        continue

    xs = sorted({i % w for i in moved})
    ys = sorted({i // w for i in moved})
    print(f'byte {off:6}: {len(moved):6} pixels moved; rows {ys[0]}..{ys[-1]} ({len(ys)} of them), '
          f'columns {xs[0]}..{xs[-1]} ({len(xs)})')
