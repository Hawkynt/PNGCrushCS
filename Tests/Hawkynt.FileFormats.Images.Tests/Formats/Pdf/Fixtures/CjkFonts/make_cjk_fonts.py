"""Cuts the tiny CJK fonts the non-embedded CID font tests draw with out of Noto Sans/Serif CJK.

Run with: python3 make_cjk_fonts.py <NotoSansCJK-Regular.ttc> <NotoSerifCJK-Regular.ttc> <UniJIS-UCS2-H> <UniJIS-UCS2-V> <output folder>

Noto Sans CJK and Noto Serif CJK (https://github.com/notofonts/noto-cjk, SIL Open Font License 1.1,
Reserved Font Name "Source") are subset with fontTools to the characters the tests use, keeping the
vertical alternates (GSUB 'vert' and 'vrt2'), and renamed, as the OFL asks of a modified version:

- FixtureSansCJK.ttc, a collection of two faces:
  "Fixture Sans CJK JP", the Japanese face of Noto Sans CJK with its CFF outlines (Adobe-Identity-0),
  and "Fixture Sans CJK SC", the Simplified Chinese face with its outlines converted to TrueType
  quadratics, so both outline kinds and a collection are read;
- FixtureSerifCJKJP.otf, "Fixture Serif CJK JP", the Japanese face of Noto Serif CJK re-keyed from
  Adobe-Identity-0 to Adobe-Japan1-6: each glyph renamed to the Adobe-Japan1 CID that the UniJIS
  CMaps give its character (or, for a vertical alternate, its character in vertical writing), so a
  renderer can read it by CID.
The UniJIS CMaps are Adobe's (cmap-resources, BSD-3-Clause), read here only to number the glyphs.
"""
import io
import os
import re
import sys

from fontTools import subset
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTCollection, TTFont, newTable

JAPANESE = "日本語のテキスト、。「」ー漢字あアいう縦書き（）ＡＢＣABCabc123 　"
CHINESE = "中文简体字这是测试国汉，。“”（）ABCabc123 　"


def cmap_file(path):
    """Unicode -> CID from a UCS2 CMap, with its usecmap followed in the same folder."""
    text = open(path, "rb").read()
    mapping = {}
    use = re.search(rb"/(\S+)\s+usecmap", text)
    if use:
        mapping.update(cmap_file(os.path.join(os.path.dirname(path), use.group(1).decode())))
    for block in re.findall(rb"begincidrange(.*?)endcidrange", text, re.S):
        for low, high, cid in re.findall(rb"<([0-9a-fA-F]+)>\s*<([0-9a-fA-F]+)>\s*(\d+)", block):
            for k in range(int(high, 16) - int(low, 16) + 1):
                mapping[int(low, 16) + k] = int(cid) + k
    for block in re.findall(rb"begincidchar(.*?)endcidchar", text, re.S):
        for code, cid in re.findall(rb"<([0-9a-fA-F]+)>\s*(\d+)", block):
            mapping[int(code, 16)] = int(cid)
    return mapping


def cut(collection, index, text):
    font = TTCollection(collection).fonts[index]
    options = subset.Options()
    options.layout_features = ["vert", "vrt2"]
    options.name_IDs = [0, 1, 2, 3, 4, 5, 6, 13, 14, 16, 17]
    options.name_languages = [0x409]
    options.notdef_outline = True
    options.desubroutinize = True
    options.hinting = False
    subsetter = subset.Subsetter(options)
    subsetter.populate(text=text)
    subsetter.subset(font)
    return font


def rename(font, family, postscript):
    name = font["name"]
    for record in list(name.names):
        if record.nameID in (1, 16):
            record.string = family
        elif record.nameID == 4:
            record.string = family + " Regular"
        elif record.nameID == 6:
            record.string = postscript
        elif record.nameID == 3:
            record.string = "1.000;" + postscript
        elif record.nameID == 5:
            record.string = "Version 1.000; subset of Noto CJK for tests"
    if "CFF " in font:
        cff = font["CFF "].cff
        cff.fontNames = [postscript]
        top = cff.topDictIndex[0]
        top.FullName = family + " Regular"
        top.FamilyName = family


def to_truetype(font):
    """CFF outlines to quadratic glyf ones, as fontTools' otf2ttf example does."""
    glyph_order = font.getGlyphOrder()
    glyph_set = font.getGlyphSet()
    glyf = newTable("glyf")
    glyf.glyphOrder = glyph_order
    glyf.glyphs = {}
    for name in glyph_order:
        pen = TTGlyphPen(glyph_set)
        glyph_set[name].draw(Cu2QuPen(pen, 1.0, reverse_direction=True))
        glyf.glyphs[name] = pen.glyph()
    font["glyf"] = glyf
    font["loca"] = newTable("loca")
    del font["CFF "]
    if "VORG" in font:
        del font["VORG"]
    font["maxp"] = newTable("maxp")
    font["maxp"].tableVersion = 0x00010000
    for attr in ("maxZones", "maxTwilightPoints", "maxStorage", "maxFunctionDefs", "maxInstructionDefs", "maxStackElements", "maxSizeOfInstructions", "maxComponentElements", "maxPoints", "maxContours", "maxCompositePoints", "maxCompositeContours", "maxComponentDepth"):
        setattr(font["maxp"], attr, 0)
    font["maxp"].maxZones = 1
    font["head"].glyphDataFormat = 0
    post = font["post"]
    post.formatType = 2.0
    post.extraNames = []
    post.mapping = {}
    post.glyphOrder = glyph_order
    font.sfntVersion = "\0\1\0\0"


def rekey(font, horizontal, vertical):
    """The font re-keyed to Adobe-Japan1-6: each glyph renamed to the CID of its character.

    The renaming goes through the font's XML form, where every table names glyphs the same way, so
    cmap, metrics and the vertical alternates of GSUB follow it.
    """
    best = font.getBestCmap()
    names = {}
    for code, glyph in best.items():
        if code in horizontal and glyph not in names:
            names[glyph] = "cid%05d" % horizontal[code]
    # A vertical alternate becomes the CID the -V CMap gives its character.
    for lookup in font["GSUB"].table.LookupList.Lookup:
        for table in lookup.SubTable:
            for source, target in getattr(table, "mapping", {}).items():
                for code, glyph in best.items():
                    if glyph == source and code in vertical and vertical[code] != horizontal.get(code) and target not in names:
                        names[target] = "cid%05d" % vertical[code]
    spare = iter(range(30000, 40000))
    for glyph in font.getGlyphOrder():
        if glyph != ".notdef" and glyph not in names:
            names[glyph] = "cid%05d" % next(spare)

    buffer = io.StringIO()
    font.saveXML(buffer)
    xml = re.sub(r"\bcid\d{5}\b", lambda m: names.get(m.group(0), m.group(0)), buffer.getvalue())
    xml = xml.replace('<ROS Registry="Adobe" Order="Identity" Supplement="0"/>', '<ROS Registry="Adobe" Order="Japan1" Supplement="6"/>')
    rekeyed = TTFont()
    rekeyed.importXML(io.StringIO(xml))
    return rekeyed


def main():
    sans, serif, uni_h, uni_v, out = sys.argv[1:6]
    os.makedirs(out, exist_ok=True)

    jp = cut(sans, 0, JAPANESE)
    rename(jp, "Fixture Sans CJK JP", "FixtureSansCJKjp-Regular")
    sc = cut(sans, 2, CHINESE)
    rename(sc, "Fixture Sans CJK SC", "FixtureSansCJKsc-Regular")
    to_truetype(sc)
    # Round-trip each face so its tables are compiled before they go into the collection.
    faces = []
    for index, face in enumerate((jp, sc)):
        path = os.path.join(out, "face%d.tmp" % index)
        face.save(path)
        faces.append(TTFont(path))
    collection = TTCollection()
    collection.fonts = faces
    collection.save(os.path.join(out, "FixtureSansCJK.ttc"))
    for index in range(2):
        os.remove(os.path.join(out, "face%d.tmp" % index))

    horizontal = cmap_file(uni_h)
    vertical = cmap_file(uni_v)
    mincho = cut(serif, 0, JAPANESE)
    rename(mincho, "Fixture Serif CJK JP", "FixtureSerifCJKjp-Regular")
    mincho = rekey(mincho, horizontal, vertical)
    mincho.save(os.path.join(out, "FixtureSerifCJKJP.otf"))
    for name in ("FixtureSansCJK.ttc", "FixtureSerifCJKJP.otf"):
        print(name, os.path.getsize(os.path.join(out, name)))


if __name__ == "__main__":
    main()
