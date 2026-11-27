"""Writes the self-made PDF fixtures the renderer tests read.

Run with: python3 make_fixtures.py <output folder>
Every file is drawn by hand here, so all of it may be redistributed with the tests. Pictures are
generated from formulas, and the JPEG and JPEG 2000 streams are encoded with Pillow.
"""
import io
import math
import os
import sys
import zlib

from PIL import Image


def read(path):
    with open(path, "rb") as handle:
        return handle.read()


def build(objects, trailer_extra=b""):
    """Classic cross-reference PDF from object bodies numbered from 1; object 1 is the catalog."""
    out = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = []
    for number, body in enumerate(objects, 1):
        offsets.append(len(out))
        out += b"%d 0 obj\n" % number + body + b"\nendobj\n"
    xref = len(out)
    out += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objects) + 1)
    for offset in offsets:
        out += b"%010d 00000 n \n" % offset
    out += b"trailer\n<< /Size %d /Root 1 0 R %s>>\nstartxref\n%d\n%%%%EOF\n" % (len(objects) + 1, trailer_extra, xref)
    return bytes(out)


def stream(dictionary, data, flate=True):
    if flate:
        data = zlib.compress(data, 9)
        dictionary += b" /Filter /FlateDecode"
    return b"<< " + dictionary + b" /Length %d >>\nstream\n" % len(data) + data + b"\nendstream"


def page(content, resources=b"<< >>", media=b"[0 0 200 200]", extra=(), page_extra=b""):
    """One-page document: 1 catalog, 2 pages, 3 page, 4 content, then the extra objects from 5."""
    objects = [b"<< /Type /Catalog /Pages 2 0 R >>",
               b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
               b"<< /Type /Page /Parent 2 0 R /MediaBox " + media + b" /Resources " + resources + b" /Contents 4 0 R " + page_extra + b">>",
               stream(b"", content)]
    objects.extend(extra)
    return build(objects)


def picture(width, height, channels):
    """A smooth colourful test picture as raw interleaved bytes."""
    data = bytearray()
    for y in range(height):
        for x in range(width):
            r = int(127.5 + 127.5 * math.sin(x * 0.21 + y * 0.05))
            g = int(127.5 + 127.5 * math.cos(y * 0.17 - x * 0.03))
            b = (x * 7 + y * 5) % 256
            if channels == 1:
                data.append((r * 3 + g * 5 + b * 2) // 10)
            elif channels == 3:
                data += bytes((r, g, b))
            else:
                k = min(r, g, b) // 2
                data += bytes((255 - r - k if r + k < 255 else 0, 255 - g - k if g + k < 255 else 0, 255 - b - k if b + k < 255 else 0, k))
    return bytes(data)


def pack(samples, width, height, bits):
    """Packs per-pixel sample values (one component) into rows of the given depth."""
    out = bytearray()
    for y in range(height):
        acc, n = 0, 0
        for x in range(width):
            acc = (acc << bits) | samples[y * width + x]
            n += bits
            while n >= 8:
                n -= 8
                out.append((acc >> n) & 0xFF)
        if n:
            out.append((acc << (8 - n)) & 0xFF)
    return bytes(out)


def image_object(width, height, space, bits, data, extra=b"", flate=True):
    return stream(b"/Type /XObject /Subtype /Image /Width %d /Height %d /ColorSpace %s /BitsPerComponent %d %s" % (width, height, space, bits, extra), data, flate)


def images():
    """Image XObjects in every common shape: depths, spaces, decode arrays, masks, filters."""
    xobjects = []
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    rgb = add(image_object(32, 24, b"/DeviceRGB", 8, picture(32, 24, 3)))
    gray = add(image_object(32, 24, b"/DeviceGray", 8, picture(32, 24, 1)))
    cmyk = add(image_object(32, 24, b"/DeviceCMYK", 8, picture(32, 24, 4)))
    g4 = add(image_object(16, 16, b"/DeviceGray", 4, pack([(x + y) % 16 for y in range(16) for x in range(16)], 16, 16, 4)))
    g1 = add(image_object(16, 16, b"/DeviceGray", 1, pack([(x // 2 + y // 2) % 2 for y in range(16) for x in range(16)], 16, 16, 1), b"/Decode [1 0]"))
    palette = bytes(sum(([i * 16, 255 - i * 16, (i * 64) % 256] for i in range(16)), []))
    indexed = add(image_object(16, 16, b"[/Indexed /DeviceRGB 15 <" + palette.hex().encode() + b">]", 4, pack([(x + 2 * y) % 16 for y in range(16) for x in range(16)], 16, 16, 4)))
    sixteen = bytearray()
    for v in picture(16, 16, 3):
        sixteen += bytes((v, 255 - v))
    rgb16 = add(image_object(16, 16, b"/DeviceRGB", 16, bytes(sixteen)))
    alpha = add(image_object(32, 24, b"/DeviceGray", 8, bytes(int(255 * (x / 31.0)) for y in range(24) for x in range(32))))
    smasked = add(image_object(32, 24, b"/DeviceRGB", 8, picture(32, 24, 3), b"/SMask %d 0 R" % alpha))
    stencil = add(stream(b"/Type /XObject /Subtype /Image /Width 16 /Height 16 /ImageMask true /BitsPerComponent 1", pack([1 if (x - 8) ** 2 + (y - 8) ** 2 < 40 else 0 for y in range(16) for x in range(16)], 16, 16, 1)))
    masked = add(image_object(32, 24, b"/DeviceRGB", 8, picture(32, 24, 3), b"/Mask %d 0 R" % stencil))
    keyed = add(image_object(16, 16, b"/DeviceGray", 8, bytes((x * 16) % 256 for y in range(16) for x in range(16)), b"/Mask [96 160]"))
    mask = add(stream(b"/Type /XObject /Subtype /Image /Width 24 /Height 24 /ImageMask true /BitsPerComponent 1 /Decode [1 0]", pack([1 if (x + y) % 6 < 3 else 0 for y in range(24) for x in range(24)], 24, 24, 1)))

    jpeg_buffer = io.BytesIO()
    Image.frombytes("RGB", (32, 24), picture(32, 24, 3)).save(jpeg_buffer, "JPEG", quality=90)
    jpeg = add(b"<< /Type /XObject /Subtype /Image /Width 32 /Height 24 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length %d >>\nstream\n" % len(jpeg_buffer.getvalue()) + jpeg_buffer.getvalue() + b"\nendstream")
    jpx_buffer = io.BytesIO()
    Image.frombytes("RGB", (32, 24), picture(32, 24, 3)).save(jpx_buffer, "JPEG2000", irreversible=False)
    jpx = add(b"<< /Type /XObject /Subtype /Image /Width 32 /Height 24 /Filter /JPXDecode /Length %d >>\nstream\n" % len(jpx_buffer.getvalue()) + jpx_buffer.getvalue() + b"\nendstream")

    # A predictor-coded image: PNG Up predictor rows.
    raw = picture(32, 24, 3)
    rows = b"".join(b"\x02" + bytes((raw[y * 96 + i] - (raw[(y - 1) * 96 + i] if y else 0)) % 256 for i in range(96)) for y in range(24))
    predicted = add(b"<< /Type /XObject /Subtype /Image /Width 32 /Height 24 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /Columns 32 >> /Length %d >>\nstream\n" % len(zlib.compress(rows)) + zlib.compress(rows) + b"\nendstream")

    names = [b"Rgb", b"Gray", b"Cmyk", b"G4", b"G1", b"Idx", b"Rgb16", b"Soft", b"Masked", b"Keyed", b"Mask", b"Jpeg", b"Jpx", b"Pred"]
    numbers = [rgb, gray, cmyk, g4, g1, indexed, rgb16, smasked, masked, keyed, mask, jpeg, jpx, predicted]
    resources = b"<< /XObject << " + b" ".join(b"/%s %d 0 R" % (n, o) for n, o in zip(names, numbers)) + b" >> >>"
    content = bytearray(b"0.9 0.9 0.6 rg 0 0 200 200 re f\n")
    for i, name in enumerate(names):
        x = 8 + (i % 4) * 48
        y = 150 - (i // 4) * 45
        content += b"q 40 0 0 30 %d %d cm /%s Do Q\n" % (x, y, name)
    content += b"q 1 0 0.4 rg 30 0 10 30 150 20 cm /Mask Do Q\n"  # a skewed stencil in red
    content += b"q 0 25 -36 0 190 15 cm /Rgb Do Q\n"  # a quarter-turned image
    content += b"q 23 7 -7 23 100 8 cm /Gray Do Q\n"  # a rotated image
    content += b"BI /W 8 /H 8 /CS /G /BPC 8 ID\n" + bytes((x * 32 + y * 4) % 256 for y in range(8) for x in range(8)) + b"\nEI\n"
    content += b"q 30 0 0 30 160 150 cm BI /W 8 /H 8 /CS /RGB /BPC 8 /F /AHx ID\n" + picture(8, 8, 3).hex().encode() + b">\nEI Q\n"
    return page(bytes(content), resources, extra=extra)


def vector():
    """Paths, strokes, dashes, joins, caps, clipping, forms and colour spaces with functions."""
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    exponential = add(b"<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.9 0.1 0.6 0.05] /N 1.7 >>")
    sampled = add(stream(b"/FunctionType 0 /Domain [0 1] /Range [0 1 0 1 0 1] /Size [5] /BitsPerSample 8", bytes([255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255, 0, 0, 255])))
    calculator = add(stream(b"/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]", b"{ 2 copy mul 3 1 roll exch dup 0.5 gt { 1 exch sub } if }", flate=False))
    stitching = add(b"<< /FunctionType 3 /Domain [0 1] /Functions [<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 2 >>] /Bounds [0.4] /Encode [0 1 0 1] >>")
    form = add(stream(b"/Type /XObject /Subtype /Form /BBox [0 0 40 40] /Matrix [0.8 0.3 -0.3 0.8 0 0] /Resources << >>",
                      b"0 0.5 0 rg 0 0 60 60 re f 1 1 0 rg 10 10 m 30 10 l 20 35 l h f 0 0 1 RG 3 w 0 0 m 40 40 l S"))
    resources = (b"<< /ColorSpace << /Spot [/Separation /Gold /DeviceCMYK %d 0 R] /Lab [/Lab << /WhitePoint [0.9505 1 1.089] /Range [-100 100 -100 100] >>]"
                 b" /Duo [/DeviceN [/A /B] /DeviceRGB %d 0 R] /Samp [/Separation /S2 /DeviceRGB %d 0 R] /Stitch [/Separation /S3 /DeviceGray %d 0 R]"
                 b" /CalR [/CalRGB << /WhitePoint [0.9642 1 0.8249] /Gamma [1.8 1.8 1.8] /Matrix [0.4497 0.2446 0.0252 0.3163 0.672 0.1412 0.1845 0.0833 0.9227] >>] >>"
                 b" /XObject << /F1 %d 0 R >> >>") % (exponential, calculator, sampled, stitching, form)
    c = bytearray()
    c += b"1 1 0.9 rg 0 0 200 200 re f\n"
    # joins and caps
    for i, (join, cap) in enumerate([(0, 0), (1, 1), (2, 2)]):
        c += b"%d j %d J 8 w 0.2 0.3 0.8 RG %d 160 m %d 190 l %d 165 l S\n" % (join, cap, 10 + i * 40, 25 + i * 40, 40 + i * 40)
    c += b"0 j 0 J 3 w 1 0 0 RG [6 3 1 3] 2 d 130 190 m 190 160 l 190 140 l S [] 0 d\n"
    c += b"2 w 0 0.5 0 RG 1 J [0 6] 0 d 130 130 m 190 130 l S [] 0 d 0 J\n"
    c += b"q 1 0 0 0.3 0 0 cm 6 w 0 0 0 RG 130 400 m 190 380 l S Q\n"  # non-uniform stroke
    # fill rules and curves
    c += b"0.8 0.2 0.2 rg 10 100 m 60 150 l 110 100 l 20 135 l 100 135 l h f*\n"
    c += b"0.2 0.2 0.8 rg 60 60 m 60 100 100 100 100 60 c 100 20 60 20 60 60 c f\n"
    c += b"0.2 0.6 0.2 rg 120 60 m 140 100 180 100 v 160 60 130 70 y h f\n"
    # clipping
    c += b"q 20 10 40 40 re W n 10 30 m 40 0 l 70 30 l 40 60 l h W* n 0 0 0 rg 0 0 200 200 re f Q\n"
    # colour spaces through functions
    c += b"/Spot cs 0.8 scn 70 10 20 20 re f /Duo cs 0.3 0.7 scn 92 10 20 20 re f /Samp cs 0.35 scn 114 10 20 20 re f\n"
    c += b"/Stitch cs 0.7 scn 136 10 20 20 re f /Lab cs 60 40 -50 scn 158 10 20 20 re f /CalR cs 0.2 0.6 0.4 scn 180 10 15 20 re f\n"
    # a form with matrix and BBox clip, drawn twice
    c += b"q 1 0 0 1 130 40 cm /F1 Do Q q -0.6 0 0 0.6 120 140 cm /F1 Do Q\n"
    # a constant-alpha overlay and a blend mode
    c += b"/GS1 gs 0 0.6 1 rg 30 70 60 30 re f\n"
    return page(bytes(c), resources.replace(b" >> >>", b" >> /ExtGState << /GS1 << /ca 0.5 /BM /Multiply >> >> >>", 1) if False else resources[:-3] + b" /ExtGState << /GS1 << /ca 0.5 /BM /Multiply >> >> >>", extra=extra)


def bilevel():
    """CCITT and JBIG2 images, as a picture and as a stencil, from the filter fixtures beside this."""
    here = os.path.dirname(os.path.abspath(__file__))
    g4 = read(os.path.join(here, "Filters", "ccitt-g4.bin"))
    g3 = read(os.path.join(here, "Filters", "ccitt-g3-2d.bin"))
    jbig2 = read(os.path.join(here, "Filters", "jbig2-generic-t0.bin"))

    def raw(dictionary, data):
        return b"<< " + dictionary + b" /Length %d >>\nstream\n" % len(data) + data + b"\nendstream"

    extra = [
        raw(b"/Type /XObject /Subtype /Image /Width 203 /Height 61 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 203 /Rows 61 >>", g4),
        raw(b"/Type /XObject /Subtype /Image /Width 203 /Height 61 /ImageMask true /Filter /CCITTFaxDecode /DecodeParms << /K 1 /Columns 203 /Rows 61 >>", g3),
        raw(b"/Type /XObject /Subtype /Image /Width 96 /Height 64 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /JBIG2Decode", jbig2),
    ]
    content = b"q 203 0 0 61 0 139 cm /G4 Do Q 0 0 0.7 rg q 203 0 0 61 0 70 cm /G3 Do Q q 96 0 0 64 50 2 cm /J2 Do Q"
    return page(content, b"<< /XObject << /G4 5 0 R /G3 6 0 R /J2 7 0 R >> >>", b"[0 0 203 200]", extra)


def shading():
    """Every shading type through sh and as patterns, and coloured and uncoloured tiling patterns."""
    import struct
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    def coord(v):
        return struct.pack(">H", int(round(v / 200.0 * 65535)))

    rainbow = add(b"<< /FunctionType 3 /Domain [0 1] /Bounds [0.5] /Encode [0 1 0 1] /Functions ["
                  b"<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 1 0] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0 1 0] /C1 [0 0 1] /N 1 >>] >>")
    function = add(stream(b"/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]", b"{ 2 copy mul 3 1 roll }", flate=False))
    type1 = add(b"<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] /Matrix [40 0 0 40 5 150] /Function %d 0 R >>" % function)
    type2 = add(b"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [55 160 95 180] /Function %d 0 R /Extend [true false] >>" % rainbow)
    type3 = add(b"<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [125 165 2 140 175 22] /Function %d 0 R /Extend [false true] >>" % rainbow)
    tri = b""
    for flag, x, y, rgb in [(0, 155, 150, (255, 0, 0)), (0, 195, 150, (0, 255, 0)), (0, 175, 190, (0, 0, 255)), (1, 195, 190, (255, 255, 0))]:
        tri += bytes([flag]) + coord(x) + coord(y) + bytes(rgb)
    type4 = add(stream(b"/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 200 0 200 0 1 0 1 0 1]", tri))
    lattice = b""
    for x, y, t in [(5, 100), (25, 100), (45, 100), (5, 130), (25, 135), (45, 130)] and [(5, 100, 0), (25, 100, 128), (45, 100, 255), (5, 130, 255), (25, 135, 128), (45, 130, 0)]:
        lattice += coord(x) + coord(y) + bytes([t])
    type5 = add(stream(b"/ShadingType 5 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /VerticesPerRow 3 /Decode [0 200 0 200 0 1] /Function %d 0 R" % rainbow, lattice))
    coons = bytes([0])
    for x, y in [(55, 100), (60, 115), (50, 125), (58, 140), (75, 145), (85, 135), (95, 140), (92, 120), (100, 110), (95, 100), (80, 92), (70, 105)]:
        coons += coord(x) + coord(y)
    coons += bytes([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0])
    type6 = add(stream(b"/ShadingType 6 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 200 0 200 0 1 0 1 0 1]", coons))
    tensor = bytes([0])
    for x, y in [(105, 100), (105, 113), (105, 127), (105, 140), (118, 140), (132, 140), (145, 140), (145, 127), (145, 113), (145, 100), (132, 100), (118, 100), (110, 125), (125, 135), (140, 115), (120, 105)]:
        tensor += coord(x) + coord(y)
    tensor += bytes([0, 128, 255, 255, 0, 128, 128, 255, 0, 255, 255, 255])
    type7 = add(stream(b"/ShadingType 7 /ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 200 0 200 0 1 0 1 0 1]", tensor))
    axialPattern = add(b"<< /PatternType 2 /Matrix [1 0 0 1 0 0] /Shading << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [150 100 195 140] /Function %d 0 R /Background [0.2 0.2 0.2] >> >>" % rainbow)
    tile = add(stream(b"/PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 12 /YStep 12 /Resources << >> /Matrix [1 0.3 -0.3 1 0 0]",
                      b"1 0 0 rg 0 0 6 6 re f 0 0 1 rg 5 5 m 10 5 l 7.5 10 l f"))
    stencil = add(stream(b"/PatternType 1 /PaintType 2 /TilingType 2 /BBox [0 0 8 8] /XStep 8 /YStep 8 /Resources << >>",
                         b"0 0 4 4 re f 4 4 4 4 re f"))
    resources = (b"<< /Shading << /S1 %d 0 R /S2 %d 0 R /S3 %d 0 R /S4 %d 0 R /S5 %d 0 R /S6 %d 0 R /S7 %d 0 R >>"
                 b" /Pattern << /Ax %d 0 R /Tile %d 0 R /Stencil %d 0 R >> /ColorSpace << /UP [/Pattern /DeviceRGB] >> >>") % (type1, type2, type3, type4, type5, type6, type7, axialPattern, tile, stencil)
    c = bytearray(b"q 5 150 40 40 re W n /S1 sh Q q 55 150 40 40 re W n /S2 sh Q q 100 145 50 50 re W n /S3 sh Q /S4 sh /S5 sh /S6 sh /S7 sh\n")
    c += b"/Pattern cs /Ax scn 150 95 45 45 re f\n"
    c += b"/Pattern cs /Tile scn 5 5 90 80 re f\n"
    c += b"/UP cs 0.1 0.6 0.3 /Stencil scn 105 5 90 80 re f\n"
    return page(bytes(c), resources, extra=extra)


def text():
    """Every font kind the renderer reads, the text state operators and the render modes.

    The embedded programs are the tiny self-made fonts in Fonts/; the standard fonts are not
    embedded and come out in the renderer's substitutes (and in whatever poppler substitutes).
    """
    here = os.path.dirname(os.path.abspath(__file__))
    font = lambda name: read(os.path.join(here, "Fonts", name))
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    widths = b"[" + b" ".join(b"600" for _ in range(224)) + b"]"
    type1 = add(stream(b"/Length1 481 /Length2 973 /Length3 0", font("type1.pfa"), flate=False))
    d1 = add(b"<< /Type /FontDescriptor /FontName /T1Test /Flags 34 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile %d 0 R >>" % type1)
    f1 = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /T1Test /FirstChar 32 /LastChar 255 /Widths %s /FontDescriptor %d 0 R /Encoding << /Differences [66 /Aacute] >> >>" % (widths, d1))
    cff = add(stream(b"/Subtype /Type1C", font("name-keyed.cff"), flate=False))
    d2 = add(b"<< /Type /FontDescriptor /FontName /CffTest /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile3 %d 0 R >>" % cff)
    f2 = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /CffTest /FirstChar 32 /LastChar 255 /Widths %s /FontDescriptor %d 0 R /Encoding /WinAnsiEncoding >>" % (widths, d2))
    ttf = add(stream(b"/Length1 %d" % len(font("truetype.ttf")), font("truetype.ttf")))
    d3 = add(b"<< /Type /FontDescriptor /FontName /TtTest /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 %d 0 R >>" % ttf)
    f3 = add(b"<< /Type /Font /Subtype /TrueType /BaseFont /TtTest /FirstChar 32 /LastChar 255 /Widths %s /FontDescriptor %d 0 R /Encoding /WinAnsiEncoding >>" % (widths, d3))
    cidcff = add(stream(b"/Subtype /CIDFontType0C", font("cid-keyed.cff"), flate=False))
    d4 = add(b"<< /Type /FontDescriptor /FontName /CidTest /Flags 4 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile3 %d 0 R >>" % cidcff)
    cid0 = add(b"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /CidTest /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor %d 0 R /DW 700 /W [34 [600 500] 40 52 650] >>" % d4)
    f4 = add(b"<< /Type /Font /Subtype /Type0 /BaseFont /CidTest /Encoding /Identity-H /DescendantFonts [%d 0 R] >>" % cid0)
    cid2 = add(b"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /TtTest /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor %d 0 R /DW 800 /CIDToGIDMap /Identity >>" % d3)
    f5 = add(b"<< /Type /Font /Subtype /Type0 /BaseFont /TtTest /Encoding /Identity-H /DescendantFonts [%d 0 R] >>" % cid2)
    f5v = add(b"<< /Type /Font /Subtype /Type0 /BaseFont /TtTest /Encoding /Identity-V /DescendantFonts [%d 0 R] >>" % cid2)
    square = add(stream(b"", b"1000 0 d0 0.9 0.4 0 rg 100 100 800 800 re f", flate=False))
    triangle = add(stream(b"", b"900 0 0 0 800 800 d1 50 50 m 750 50 l 400 750 l f", flate=False))
    f6 = add(b"<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /sq %d 0 R /tri %d 0 R >> "
             b"/Encoding << /Type /Encoding /Differences [97 /sq /tri] >> /FirstChar 97 /LastChar 98 /Widths [1000 900] /Resources << >> >>" % (square, triangle))
    resources = (b"<< /Font << /F1 %d 0 R /F2 %d 0 R /F3 %d 0 R /F4 %d 0 R /F5 %d 0 R /F5V %d 0 R /F6 %d 0 R"
                 b" /H << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> /TB << /Type /Font /Subtype /Type1 /BaseFont /Times-Bold >>"
                 b" /C << /Type /Font /Subtype /Type1 /BaseFont /Courier >> /AB << /Type /Font /Subtype /TrueType /BaseFont /Arial,BoldItalic /Encoding /WinAnsiEncoding >> >> >>") % (f1, f2, f3, f4, f5, f5v, f6)
    c = bytearray()
    c += b"BT /F1 24 Tf 10 170 Td (AFOS B) Tj /F2 24 Tf 0 -26 Td (AOIFGS) Tj /F3 24 Tf 0 -26 Td (AO\305 OA) Tj ET\n"
    c += b"BT /F4 22 Tf 120 170 Td <0022003000290027> Tj /F5 22 Tf 0 -26 Td [<0002> 300 <0003> -200 <0004>] TJ ET\n"
    c += b"BT /F5V 16 Tf 185 160 Td <000200030002> Tj ET\n"
    c += b"BT /F6 20 Tf 0.1 0.2 0.8 rg 120 110 Td (abab) Tj 3 Tr 0 -20 Td (ab) Tj 0 Tr ET\n"
    c += b"BT /H 12 Tf 10 82 Td 14 TL (Helvetica: Sphinx of black quartz) Tj T* 2 Tc 4 Tw (spaced out words) Tj 0 Tc 0 Tw ET\n"
    c += b"BT /TB 12 Tf 10 54 Td 75 Tz (Times bold, narrow) Tj 100 Tz 0 -14 Td 3 Ts (raised) Tj 0 Ts ( and \\(level\\)) Tj ET\n"
    c += b"BT /C 11 Tf 13 TL 10 26 Td (Courier ') Tj 1 2 (with \\\") \" /AB 11 Tf ( Arial,BoldItalic) Tj ET\n"
    c += b"q BT /H 40 Tf 0.8 0 0 RG 1.2 w 1 Tr 120 40 Td (Out) Tj 2 Tr 0 0 1 rg 70 0 Td (F) Tj ET Q\n"
    c += b"q BT /F2 40 Tf 7 Tr 130 2 Td (OS) Tj ET 0 0.6 0 rg 0 0 200 40 re f Q\n"
    return page(bytes(c), resources, extra=extra)


def transparency():
    """Constant alpha, all sixteen blend modes, isolated and non-isolated groups, soft masks."""
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    modes = [b"Normal", b"Multiply", b"Screen", b"Overlay", b"Darken", b"Lighten", b"ColorDodge", b"ColorBurn",
             b"HardLight", b"SoftLight", b"Difference", b"Exclusion", b"Hue", b"Saturation", b"Color", b"Luminosity"]
    gs = b" ".join(b"/B%d << /BM /%s >>" % (i, m) for i, m in enumerate(modes))
    gs += b" /Half << /ca 0.5 /CA 0.5 >> /Mul << /BM /Multiply >>"
    group_content = b"/Mul gs 1 0.8 0 rg 0 0 30 30 re f 0 0.7 1 rg 15 15 30 30 re f"
    nonisolated = add(stream(b"/Type /XObject /Subtype /Form /BBox [0 0 45 45] /Group << /S /Transparency /I false >> /Resources << /ExtGState << /Mul << /BM /Multiply >> >> >>", group_content))
    isolated = add(stream(b"/Type /XObject /Subtype /Form /BBox [0 0 45 45] /Group << /S /Transparency /I true >> /Resources << /ExtGState << /Mul << /BM /Multiply >> >> >>", group_content))
    lum = add(stream(b"/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >> /Resources << >>",
                     b"q 1 g 0 0 200 200 re f 0 g 70 20 m 130 20 l 100 70 l f 0.5 g 140 20 40 40 re f Q"))
    alphaMask = add(stream(b"/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency >> /Resources << /ExtGState << /H << /ca 0.4 >> >> >>",
                           b"0 g 10 10 40 25 re f /H gs 20 20 40 25 re f"))
    transfer = add(b"<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>")
    gs += b" /LumMask << /SMask << /S /Luminosity /G %d 0 R /TR %d 0 R >> >> /AlphaMask << /SMask << /S /Alpha /G %d 0 R >> >>" % (lum, transfer, alphaMask)
    resources = b"<< /ExtGState << " + gs + b" >> /XObject << /NI %d 0 R /IS %d 0 R >> >>" % (nonisolated, isolated)
    c = bytearray(b"0.2 0.5 0.8 rg 0 100 200 100 re f 1 0.9 0.4 rg 0 100 100 50 re f\n")
    for i in range(16):
        x = 5 + (i % 8) * 24
        y = 160 - (i // 8) * 40
        c += b"q /B%d gs 0.9 0.3 0.6 rg %d %d 20 25 re f Q\n" % (i, x, y)
    c += b"q /Half gs 0 0.6 0.2 rg 10 70 50 25 re f 0 0 0 RG 6 w 20 75 m 80 90 l S Q\n"
    c += b"q 1 0.5 0 rg 70 40 120 60 re f Q q 1 0 0 1 80 45 cm /NI Do Q q 1 0 0 1 140 45 cm /IS Do Q\n"
    c += b"q /LumMask gs 0.6 0 0.6 rg 0 0 200 40 re f Q q /AlphaMask gs 0 0 0.6 rg 0 0 70 50 re f Q\n"
    return page(bytes(c), resources, extra=extra)


if __name__ == "__main__":
    target = sys.argv[1]
    os.makedirs(os.path.join(target, "Render"), exist_ok=True)
    for name, maker in [("images", images), ("vector", vector), ("bilevel", bilevel), ("shading", shading), ("text", text), ("transparency", transparency)]:
        with open(os.path.join(target, "Render", name + ".pdf"), "wb") as handle:
            handle.write(maker())
