#!/usr/bin/env python3
"""Reports what each third-party tool reads that we do not, and where we disagree with it.

Run after decode-with.sh has filled one directory per tool, and after our own decode has filled
another. Usage:

    compare.py <directory holding ours/ recoil/ xnview/ irfanview/>

Two things make a naive byte comparison wrong, and both were found by it reporting nonsense:

  * A PNG of four bits a channel becomes a PPM stating maxval 15, whose samples run 0-15 rather than
    0-255. Comparing those against full-range ones calls every such picture different, which is most
    of what RECOIL writes.
  * The machines these formats come from had non-square pixels, and the tools disagree on whether to
    correct for that. One drawing the same picture at an integer multiple of another's size is not a
    disagreement about the picture.

What matters for replacing a tool is not how often we match it. It is whether anything it reads is
something we cannot, or read differently — so that is what the report ends with.
"""
import collections
import os
import sys

TOOLS = [("recoil", "RECOIL"), ("xnview", "XnView (nconvert)"), ("irfanview", "IrfanView (wine)")]


def read_ppm(path):
    """Returns (width, height, RGB bytes scaled to 0-255), or None if there is no readable PPM."""
    try:
        with open(path, "rb") as handle:
            data = handle.read()
    except OSError:
        return None

    if not (data.startswith(b"P6") or data.startswith(b"P3")):
        return None
    ascii_form = data.startswith(b"P3")

    fields, at = [], 2
    while len(fields) < 3 and at < len(data):
        while at < len(data) and data[at : at + 1].isspace():
            at += 1
        if data[at : at + 1] == b"#":
            while at < len(data) and data[at] != 0x0A:
                at += 1
            continue
        start = at
        while at < len(data) and not data[at : at + 1].isspace():
            at += 1
        fields.append(int(data[start:at]))
    if len(fields) < 3:
        return None

    at += 1
    width, height, maxval = fields
    if width <= 0 or height <= 0 or maxval <= 0:
        return None

    if ascii_form:
        samples = [int(v) for v in data[at:].split()]
    elif maxval > 255:
        body = data[at : at + width * height * 6]
        samples = [body[i] << 8 | body[i + 1] for i in range(0, len(body), 2)]
    else:
        samples = data[at : at + width * height * 3]

    if maxval != 255:
        if maxval <= 255 and not ascii_form:
            samples = samples.translate(bytes(min(255, v * 255 // maxval) for v in range(256)))
        else:
            samples = bytes(min(255, v * 255 // maxval) for v in samples)
    elif ascii_form:
        samples = bytes(samples)

    return width, height, samples


def same_picture(a, b):
    """Whether two decodes show the same picture, allowing an integer stretch either way."""
    aw, ah, ap = a
    bw, bh, bp = b
    if not ap or not bp:
        return False

    if aw * ah < bw * bh:
        aw, ah, ap, bw, bh, bp = bw, bh, bp, aw, ah, ap
    if aw % bw or ah % bh:
        return False

    kx, ky = aw // bw, ah // bh
    if not (1 <= kx <= 4 and 1 <= ky <= 4):
        return False
    if len(ap) < aw * ah * 3 or len(bp) < bw * bh * 3:
        return False

    for y in range(bh):
        row = y * ky * aw
        for x in range(bw):
            at = (row + x * kx) * 3
            bt = (y * bw + x) * 3
            if ap[at : at + 3] != bp[bt : bt + 3]:
                return False
    return True


def same_picture_different_palette(a, b):
    """Whether two decodes draw the same picture in different colours.

    Two tools can agree on every pixel of a machine's screen and still render it differently, because
    what RGB a hardware colour "is" was measured rather than defined and nobody measured the same.
    That shows up as every colour of one decode corresponding to exactly one colour of the other, and
    it is a difference of opinion about a CRT rather than a fault in either decoder — so counting it
    the same as a wrong picture buries the wrong pictures among a hundred that are right.
    """
    aw, ah, ap = a
    bw, bh, bp = b
    if aw * ah < bw * bh:
        aw, ah, ap, bw, bh, bp = bw, bh, bp, aw, ah, ap
    if bw == 0 or bh == 0 or aw % bw or ah % bh:
        return False

    kx, ky = aw // bw, ah // bh
    if not (1 <= kx <= 4 and 1 <= ky <= 4):
        return False
    if len(ap) < aw * ah * 3 or len(bp) < bw * bh * 3:
        return False

    forward, backward = {}, {}
    for y in range(bh):
        row = y * ky * aw
        for x in range(bw):
            at = (row + x * kx) * 3
            bt = (y * bw + x) * 3
            ours, theirs = ap[at : at + 3], bp[bt : bt + 3]
            if forward.setdefault(ours, theirs) != theirs:
                return False
            if backward.setdefault(theirs, ours) != ours:
                return False

    return True


def main(root):
    ours_dir = os.path.join(root, "ours")
    if not os.path.isdir(ours_dir):
        sys.exit("no ours/ directory under " + root)

    names = set(os.listdir(ours_dir))
    present = []
    for key, label in TOOLS:
        directory = os.path.join(root, key)
        if not os.path.isdir(directory) or not os.listdir(directory):
            print("skipping %s: it was not run here" % label)
            continue
        present.append((key, label))
        names |= set(os.listdir(directory))

    counts = collections.Counter()
    blockers = collections.defaultdict(list)
    for name in sorted(names):
        ours = read_ppm(os.path.join(ours_dir, name))
        for key, _ in present:
            theirs = read_ppm(os.path.join(root, key, name))
            if theirs is None and ours is None:
                continue
            if theirs is None:
                counts[(key, "only we read it")] += 1
            elif ours is None:
                counts[(key, "it reads, we cannot")] += 1
                blockers[key].append((name, "we cannot read it"))
            elif same_picture(ours, theirs):
                counts[(key, "both read, we agree")] += 1
            elif same_picture_different_palette(ours, theirs):
                counts[(key, "same picture, other colours")] += 1
            else:
                counts[(key, "both read, we differ")] += 1
                blockers[key].append((name, "we differ from it"))

    order = ["both read, we agree", "same picture, other colours", "both read, we differ",
             "it reads, we cannot", "only we read it"]
    for key, label in present:
        print("\n=== against %s" % label)
        for line in order:
            print("   %-22s %5d" % (line, counts[(key, line)]))

    print("\n=== what stands between us and replacing each tool")
    for key, label in present:
        rows = blockers[key]
        print("  %-20s %d sample(s)" % (label, len(rows)))
        grouped = collections.Counter(
            os.path.splitext(name[: -len(".ppm")])[1].lower() + " — " + why for name, why in rows)
        for what, count in grouped.most_common(15):
            print("       %-40s %d" % (what, count))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
