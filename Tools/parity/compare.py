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


_FOUR_BIT = bytes(round(v / 17) * 17 for v in range(256))


def same_picture_quantised(a, b):
    """Whether two decodes agree once ours is rounded to four bits a channel.

    RECOIL and XnView both render the eight-bit machines with four bits a channel, so every colour
    they draw is a multiple of 17. Ours are the full-precision measurements — a Run Paint screen came
    out pixel-for-pixel identical to both tools with each of its fifteen colours two or three levels
    off, purely because they round and we do not. That is a difference in precision and not in the
    picture, so it is not counted as a disagreement.
    """
    aw, ah, ap = a
    if not ap:
        return False
    return same_picture((aw, ah, bytes(_FOUR_BIT[v] for v in ap)), b)


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
    merged = False
    for y in range(bh):
        row = y * ky * aw
        for x in range(bw):
            at = (row + x * kx) * 3
            bt = (y * bw + x) * 3
            ours, theirs = ap[at : at + 3], bp[bt : bt + 3]
            if forward.setdefault(ours, theirs) != theirs:
                return False
            if backward.setdefault(theirs, ours) != ours:
                # One tool drawing two of the other's colours identically still agrees about the
                # picture: the arrangement is the same and one palette simply has a duplicate in it.
                merged = True

    # Allowing that at all is a risk — a decode of one flat colour maps consistently onto anything —
    # so it is allowed only where a single pair is involved, not where a picture has collapsed.
    if merged and len(forward) - len(set(forward.values())) > 1:
        return False

    return True


def is_flat(decode):
    """Whether a decode is one colour and nothing else.

    A tool that cannot make sense of a file sometimes writes a picture of the right size in a single
    flat colour rather than refusing it. That is the tool failing, not the tool disagreeing, and
    counting it as a difference of opinion credits it with a reading it did not make. The same
    reasoning already governs the palette comparison, which refuses to let a decode collapsed to one
    colour map consistently onto anything.
    """
    width, height, pixels = decode
    if not pixels or width * height < 2:
        return False

    first = pixels[0:3]
    step = max(1, (width * height) // 4000)
    return all(pixels[i * 3 : i * 3 + 3] == first for i in range(0, width * height, step))


def main(root):
    ours_dir = os.path.join(root, "ours")
    if not os.path.isdir(ours_dir):
        sys.exit("no ours/ directory under " + root)

    names = set(n for n in os.listdir(ours_dir) if ".alt" not in n)
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
        # Every other reading our registry gives of the same name. An extension claimed by two
        # formats is claimed by two formats: the tools do not always mean the same one, and the
        # question here is whether we can read the file, not which claimant came first.
        alternates = []
        stem = name[: -len(".ppm")] if name.endswith(".ppm") else name
        index = 1
        while True:
            extra = read_ppm(os.path.join(ours_dir, "%s.alt%d.ppm" % (stem, index)))
            if extra is None:
                break
            alternates.append(extra)
            index += 1
        for key, _ in present:
            theirs = read_ppm(os.path.join(root, key, name))
            if theirs is None and ours is None:
                continue
            if theirs is None:
                counts[(key, "only we read it")] += 1
            elif ours is None:
                counts[(key, "it reads, we cannot")] += 1
                blockers[key].append((name, "we cannot read it"))
            elif is_flat(theirs) and not is_flat(ours):
                counts[(key, "it read it and drew nothing")] += 1
            elif any(same_picture(c, theirs) or same_picture_quantised(c, theirs)
                     for c in [ours] + alternates):
                counts[(key, "both read, we agree")] += 1
            elif any(same_picture_different_palette(c, theirs) for c in [ours] + alternates):
                counts[(key, "same picture, other colours")] += 1
            else:
                counts[(key, "both read, we differ")] += 1
                blockers[key].append((name, "we differ from it"))

    order = ["both read, we agree", "same picture, other colours", "both read, we differ",
             "it read it and drew nothing", "it reads, we cannot", "only we read it"]
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
