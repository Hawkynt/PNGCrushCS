"""Packs Adobe's CID-to-Unicode maps (adobe-type-tools/mapping-resources-pdf) into cid2unicode.bin.br.

Run with: python3 pack_cid_unicode.py <mapping-resources-pdf checkout> <output.bin.br>

The four ToUnicode CMaps of pdf2unicode/ (Adobe-CNS1-UCS2, Adobe-GB1-UCS2, Adobe-Japan1-UCS2 and
Adobe-Korea1-UCS2) are read; every CID their bfchar and bfrange entries give exactly one Unicode
scalar value is kept (a UTF-16 surrogate pair counts as one), the rest — ligatures and other
sequences — dropped. The record format, read by PdfCidToUnicode:

  "PCU1" varint(count) then per collection:
    string ordering ("Japan1"), varint(runs)
    { zigzag(cid - previous cid end - 1), varint(length - 1), zigzag(unicode - previous unicode end - 1) }
  where a run maps length consecutive CIDs onto consecutive scalar values, string = varint(length)
  + ASCII, and "previous" runs over the collection's list.
The whole is compressed with Brotli at quality 11.
"""
import os
import re
import sys

import brotli


def varint(v):
    out = bytearray()
    while True:
        b = v & 0x7F
        v >>= 7
        if v:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)


def zigzag(v):
    return varint((v << 1) if v >= 0 else ((-v << 1) - 1))


def scalars(hex_text):
    units = [int(hex_text[i:i + 4], 16) for i in range(0, len(hex_text), 4)]
    out = []
    i = 0
    while i < len(units):
        u = units[i]
        if 0xD800 <= u < 0xDC00 and i + 1 < len(units) and 0xDC00 <= units[i + 1] < 0xE000:
            out.append(0x10000 + ((u - 0xD800) << 10) + (units[i + 1] - 0xDC00))
            i += 2
        else:
            out.append(u)
            i += 1
    return out


def parse(text):
    mapping = {}
    for block in re.findall(rb"beginbfchar(.*?)endbfchar", text, re.S):
        for code, dest in re.findall(rb"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>", block):
            values = scalars(dest.decode())
            if len(values) == 1:
                mapping[int(code, 16)] = values[0]
    for block in re.findall(rb"beginbfrange(.*?)endbfrange", text, re.S):
        for low, high, dest in re.findall(rb"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(<[0-9A-Fa-f]+>|\[[^\]]*\])", block):
            low, high = int(low, 16), int(high, 16)
            if dest.startswith(b"["):
                for k, item in enumerate(re.findall(rb"<([0-9A-Fa-f]+)>", dest)):
                    values = scalars(item.decode())
                    if len(values) == 1 and low + k <= high:
                        mapping[low + k] = values[0]
            else:
                values = scalars(dest[1:-1].decode())
                if len(values) == 1:
                    for k in range(high - low + 1):
                        mapping[low + k] = values[0] + k
    return mapping


def runs(mapping):
    out = []
    for cid in sorted(mapping):
        u = mapping[cid]
        if out and out[-1][0] + out[-1][1] == cid and out[-1][2] + out[-1][1] == u:
            out[-1][1] += 1
        else:
            out.append([cid, 1, u])
    return out


def main():
    root, target = sys.argv[1], sys.argv[2]
    out = bytearray(b"PCU1")
    orderings = ["CNS1", "GB1", "Japan1", "Korea1"]
    out += varint(len(orderings))
    for ordering in orderings:
        with open(os.path.join(root, "pdf2unicode", "Adobe-%s-UCS2" % ordering), "rb") as handle:
            mapping = parse(handle.read())
        items = runs(mapping)
        name = ordering.encode("ascii")
        out += varint(len(name)) + name + varint(len(items))
        previous_cid, previous_unicode = -1, -1
        for cid, length, unicode in items:
            out += zigzag(cid - previous_cid - 1) + varint(length - 1) + zigzag(unicode - previous_unicode - 1)
            previous_cid, previous_unicode = cid + length - 1, unicode + length - 1
        print(ordering, len(mapping), "CIDs in", len(items), "runs")
    with open(target, "wb") as handle:
        handle.write(brotli.compress(bytes(out), quality=11))
    print(len(out), "bytes raw,", os.path.getsize(target), "compressed")


if __name__ == "__main__":
    main()
