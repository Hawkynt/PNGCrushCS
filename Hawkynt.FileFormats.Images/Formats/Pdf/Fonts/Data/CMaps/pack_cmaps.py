"""Packs Adobe's predefined CMaps (adobe-type-tools/cmap-resources) into cmaps.bin.br.

Run with: python3 pack_cmaps.py <cmap-resources checkout> <output.bin.br>

Every CMap file under <ordering>/CMap/ is read except the deprecated Adobe-Japan2 set, the
UTF-8 encodings (PDF has no use for them) and the Adobe-<ordering>-<supplement> CID-to-CID maps.
Nothing is dropped from a CMap that is kept: codespace ranges, cidrange, cidchar and notdefrange
entries in file order, usecmap and WMode. The record format, read by PdfPredefinedCMaps:

  "PCM1" varint(count) then per CMap:
    string name, byte wmode, string usecmap ("" for none),
    varint(codespaces) { byte bytes, varint low, varint high }
    varint(mappings)   { byte bytes, zigzag(low - previous high - 1), varint(high - low),
                         zigzag(cid - previous cid end - 1) }
    varint(notdefs)    { the same shape as mappings }
  where string = varint(length) + ASCII, and "previous" runs over the list it is in.
The whole is compressed with Brotli at quality 11.
"""
import os
import re
import sys

import brotli

TOKEN = re.compile(rb"<([0-9A-Fa-f\s]*)>|(/[^\s/<>\[\]()]+)|(-?\d+)|([A-Za-z]+)|(%[^\n]*)|\S", re.S)


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


def string(s):
    b = s.encode("ascii")
    return varint(len(b)) + b


def parse(text):
    tokens = []
    for m in TOKEN.finditer(text):
        if m.group(1) is not None:
            hexs = re.sub(rb"\s", b"", m.group(1))
            tokens.append(("hex", len(hexs) // 2, int(hexs, 16) if hexs else 0))
        elif m.group(2):
            tokens.append(("name", m.group(2)[1:].decode()))
        elif m.group(3):
            tokens.append(("num", int(m.group(3))))
        elif m.group(4):
            tokens.append(("op", m.group(4).decode()))
    cmap = {"wmode": 0, "use": "", "codespace": [], "map": [], "notdef": []}
    i = 0
    while i < len(tokens):
        t = tokens[i]
        if t[0] == "op" and t[1] in ("begincodespacerange", "begincidrange", "begincidchar", "beginnotdefrange"):
            kind = t[1]
            end = "end" + kind[5:]
            i += 1
            items = []
            while not (tokens[i][0] == "op" and tokens[i][1] == end):
                items.append(tokens[i])
                i += 1
            if kind == "begincodespacerange":
                for a, b in zip(items[0::2], items[1::2]):
                    cmap["codespace"].append((a[1], a[2], b[2]))
            elif kind == "begincidchar":
                for a, c in zip(items[0::2], items[1::2]):
                    cmap["map"].append((a[1], a[2], a[2], c[1]))
            else:
                target = cmap["map"] if kind == "begincidrange" else cmap["notdef"]
                for a, b, c in zip(items[0::3], items[1::3], items[2::3]):
                    target.append((a[1], a[2], b[2], c[1]))
        elif t[0] == "op" and t[1] == "usecmap" and i > 0 and tokens[i - 1][0] == "name":
            cmap["use"] = tokens[i - 1][1]
        elif t[0] == "name" and t[1] == "WMode" and i + 1 < len(tokens) and tokens[i + 1][0] == "num":
            cmap["wmode"] = tokens[i + 1][1]
        i += 1
    return cmap


def entries(items):
    out = bytearray(varint(len(items)))
    prev_high, prev_cid_end = -1, -1
    for nbytes, low, high, cid in items:
        out += bytes((nbytes,)) + zigzag(low - prev_high - 1) + varint(high - low) + zigzag(cid - prev_cid_end - 1)
        prev_high, prev_cid_end = high, cid + (high - low)
    return bytes(out)


def main():
    root, target = sys.argv[1], sys.argv[2]
    files = []
    for ordering in sorted(os.listdir(root)):
        folder = os.path.join(root, ordering, "CMap")
        if ordering.startswith("deprecated") or not os.path.isdir(folder):
            continue
        for name in sorted(os.listdir(folder)):
            if "UTF8" in name or name.startswith("Adobe-"):
                continue
            files.append((name, os.path.join(folder, name)))
    out = bytearray(b"PCM1") + varint(len(files))
    for name, path in files:
        cmap = parse(open(path, "rb").read())
        out += string(name) + bytes((cmap["wmode"],)) + string(cmap["use"])
        out += varint(len(cmap["codespace"])) + b"".join(bytes((n,)) + varint(lo) + varint(hi) for n, lo, hi in cmap["codespace"])
        out += entries(cmap["map"]) + entries(cmap["notdef"])
    open(target, "wb").write(brotli.compress(bytes(out), quality=11))
    print(len(files), "CMaps,", len(out), "bytes raw,", os.path.getsize(target), "compressed")


if __name__ == "__main__":
    main()
