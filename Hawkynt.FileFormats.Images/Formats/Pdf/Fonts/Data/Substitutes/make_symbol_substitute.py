"""Builds NotoSymbolSubstitute.ttf.br, the outline font drawn for a non-embedded Symbol or ZapfDingbats.

Run with: python3 make_symbol_substitute.py <folder with the Noto fonts> <output.ttf.br>

Every glyph the Symbol and ZapfDingbats built-in encodings name (PdfEncodings.cs) is looked up by
its Unicode value (the Adobe Glyph List and the ITC Zapf Dingbats list in GlyphList.cs) in Noto Sans
Math, Noto Sans Symbols 2, Noto Sans Symbols and Noto Sans, in that order, and the outlines found are copied
(composites flattened, hinting dropped) into one new TrueType font. Symbol's bracket, brace, parenthesis, integral
and arrow pieces and its serif/sans copyright marks sit in the Adobe corporate-use area in the
glyph list (F6D9-F6DB, F8E5-F8FE); the font maps
those code points too, to the standard pieces of U+239B-U+23AF they stand for. The Noto fonts are
SIL OFL 1.1 without Reserved Font Names; the result is renamed regardless, keeps their copyright
lines and travels with OFL-Noto.txt.
"""
import io
import os
import re
import sys

import brotli
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.dirname(HERE)

# Adobe corporate-use code points of the glyph list and the standard characters they stand for.
PUA = {
    0xF8E5: 0x23AF,  # radicalex (the radical's horizontal extender)
    0xF8E6: 0x23D0,  # arrowvertex
    0xF8E7: 0x23AF,  # arrowhorizex
    0xF8E8: 0x00AE,  # registersans
    0xF8E9: 0x00A9,  # copyrightsans
    0xF8EA: 0x2122,  # trademarksans
    0xF8EB: 0x239B, 0xF8EC: 0x239C, 0xF8ED: 0x239D,  # parenleft top, extender, bottom
    0xF8EE: 0x23A1, 0xF8EF: 0x23A2, 0xF8F0: 0x23A3,  # bracketleft
    0xF8F1: 0x23A7, 0xF8F2: 0x23A8, 0xF8F3: 0x23A9,  # braceleft top, middle, bottom
    0xF8F4: 0x23AA,  # braceex
    0xF8F5: 0x23AE,  # integralex
    0xF8F6: 0x239E, 0xF8F7: 0x239F, 0xF8F8: 0x23A0,  # parenright
    0xF8F9: 0x23A4, 0xF8FA: 0x23A5, 0xF8FB: 0x23A6,  # bracketright
    0xF8FC: 0x23AB, 0xF8FD: 0x23AC, 0xF8FE: 0x23AD,  # braceright
    0xF6D9: 0x00AE, 0xF6DA: 0x00A9, 0xF6DB: 0x2122,  # registerserif, copyrightserif, trademarkserif
}


def encoding_names(name):
    text = open(os.path.join(DATA, "PdfEncodings.cs"), encoding="utf-8").read()
    body = text[text.index("_%s = [" % name):]
    body = body[:body.index("];")]
    return [n for n in re.findall(r'"([^"]+)"', body)]


def glyph_list():
    text = open(os.path.join(DATA, "GlyphList.cs"), encoding="utf-8").read()
    table = {}
    for n, u in re.findall(r"([A-Za-z0-9_.]+);([0-9A-F]{4,6})(?=[ \n])", text):
        table.setdefault(n, int(u, 16))
    return table


def main():
    source, target = sys.argv[1], sys.argv[2]
    agl = glyph_list()
    wanted = set()
    for name in encoding_names("SymbolSetEncoding") + encoding_names("ZapfDingbatsEncoding"):
        if name in agl:
            wanted.add(agl[name])
    fonts = [TTFont(os.path.join(source, f)) for f in ("NotoSansMath-Regular.ttf", "NotoSansSymbols2-Regular.ttf", "NotoSansSymbols-Regular.ttf", "NotoSans-Regular.ttf")]
    assigned = [set() for _ in fonts]
    missing = []
    for u in sorted(wanted):
        real = PUA.get(u, u)
        for i, f in enumerate(fonts):
            if real in f.getBestCmap():
                assigned[i].add(real)
                break
        else:
            missing.append(u)

    # Copy each outline (composites flattened) into one new TrueType font.
    order = [".notdef"]
    glyphs = {".notdef": TTGlyphPen(None).glyph()}
    metrics = {".notdef": (500, 0)}
    cmap = {}
    for f, codes in zip(fonts, assigned):
        glyphset = f.getGlyphSet()
        best = f.getBestCmap()
        for real in sorted(codes):
            name = "u%04X" % real
            pen = TTGlyphPen(None)
            recording = DecomposingRecordingPen(glyphset)
            glyphset[best[real]].draw(recording)
            recording.replay(pen)
            glyphs[name] = pen.glyph()
            metrics[name] = (f["hmtx"][best[real]][0], 0)
            order.append(name)
            cmap[real] = name
    for pua, real in PUA.items():
        if pua in wanted and real in cmap:
            cmap[pua] = cmap[real]
    copyrights = "; ".join(sorted({f["name"].getDebugName(0) for f in fonts}))
    builder = FontBuilder(1000, isTTF=True)
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap(cmap)
    builder.setupGlyf(glyphs)
    for name in order:
        glyph = glyphs[name]
        glyph.recalcBounds(builder.font["glyf"])
        metrics[name] = (metrics[name][0], getattr(glyph, "xMin", 0))
    builder.setupHorizontalMetrics(metrics)
    builder.setupHorizontalHeader(ascent=900, descent=-300)
    builder.setupNameTable({"copyright": copyrights, "familyName": "Noto Symbol Substitute", "styleName": "Regular",
                            "uniqueFontIdentifier": "NotoSymbolSubstitute", "fullName": "Noto Symbol Substitute",
                            "version": "Subset of Noto Sans Math 3.000, Noto Sans Symbols 2 2.008, Noto Sans Symbols 2.003 and Noto Sans 2.015",
                            "psName": "NotoSymbolSubstitute",
                            "licenseDescription": "This Font Software is licensed under the SIL Open Font License, Version 1.1.",
                            "licenseInfoURL": "https://openfontlicense.org"})
    builder.setupOS2(sTypoAscender=900, sTypoDescender=-300, usWinAscent=900, usWinDescent=300)
    builder.setupPost()
    merged = builder.font
    buffer = io.BytesIO()
    merged.save(buffer)
    data = buffer.getvalue()
    open(target, "wb").write(brotli.compress(data, quality=11))
    print(len(wanted), "characters wanted,", len(missing), "missing:", " ".join("%04X" % u for u in missing))
    print(len(data), "bytes TTF,", os.path.getsize(target), "compressed")


if __name__ == "__main__":
    main()
