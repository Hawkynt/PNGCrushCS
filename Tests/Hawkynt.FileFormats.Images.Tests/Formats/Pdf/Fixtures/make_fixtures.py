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


def _s15(v):
    return int(round(v * 65536)).to_bytes(4, "big", signed=True)


def _pad4(b):
    return b + b"\0" * (-len(b) % 4)


def icc_curv(table):
    """curveType: [] identity, [g] a gamma, else a 16-bit table."""
    if len(table) == 1:
        return b"curv\0\0\0\0" + (1).to_bytes(4, "big") + int(round(table[0] * 256)).to_bytes(2, "big")
    return b"curv\0\0\0\0" + len(table).to_bytes(4, "big") + b"".join(int(v).to_bytes(2, "big") for v in table)


def icc_para(function, params):
    return b"para\0\0\0\0" + function.to_bytes(2, "big") + b"\0\0" + b"".join(_s15(p) for p in params)


def icc_xyz(x, y, z):
    return b"XYZ \0\0\0\0" + _s15(x) + _s15(y) + _s15(z)


def icc_grid(inputs, outputs, points, f, scale=65535):
    """Grid values of f over [0,1]^inputs, first input varying slowest, as ICC orders them."""
    values = []

    def walk(prefix):
        if len(prefix) == inputs:
            values.extend(max(0, min(scale, int(round(v * scale)))) for v in f(prefix))
            return
        for i in range(points):
            walk(prefix + [i / (points - 1)])
    walk([])
    return values


def icc_mft(wide, inputs, outputs, points, f, in_curve=lambda v: v, out_curve=lambda v: v, entries=64):
    """lut16Type (wide) or lut8Type with an identity matrix."""
    body = bytearray(b"mft2" if wide else b"mft1") + b"\0\0\0\0" + bytes((inputs, outputs, points, 0))
    for i in range(9):
        body += _s15(1.0 if i % 4 == 0 else 0.0)
    n = entries if wide else 256
    if wide:
        body += n.to_bytes(2, "big") + n.to_bytes(2, "big")
    put = (lambda v: int(round(v * 65535)).to_bytes(2, "big")) if wide else (lambda v: bytes((int(round(v * 255)),)))
    for _ in range(inputs):
        body += b"".join(put(in_curve(i / (n - 1))) for i in range(n))
    for v in icc_grid(inputs, outputs, points, f, 65535 if wide else 255):
        body += v.to_bytes(2, "big") if wide else bytes((v,))
    for _ in range(outputs):
        body += b"".join(put(out_curve(i / (n - 1))) for i in range(n))
    return bytes(body)


def icc_mab(a_to_b, inputs, outputs, points, f, a_curves, m_curves=None, matrix=None, b_curves=None):
    """lutAtoBType / lutBtoAType: element order A, CLUT, M, Matrix, B (reversed for BtoA)."""
    header_len = 32
    parts = []
    offsets = {}
    pos = header_len

    def place(key, data):
        nonlocal pos
        offsets[key] = pos
        data = _pad4(data)
        parts.append(data)
        pos += len(data)

    order = ["B", "Matrix", "M", "CLUT", "A"] if not a_to_b else ["A", "CLUT", "M", "Matrix", "B"]
    for key in order:
        if key == "A" and a_curves:
            place("A", b"".join(_pad4(c) for c in a_curves))
        elif key == "CLUT" and f:
            grid = bytes([points] * inputs + [0] * (16 - inputs)) + b"\x02\0\0\0"
            place("CLUT", grid + b"".join(v.to_bytes(2, "big") for v in icc_grid(inputs, outputs, points, f)))
        elif key == "M" and m_curves:
            place("M", b"".join(_pad4(c) for c in m_curves))
        elif key == "Matrix" and matrix:
            place("Matrix", b"".join(_s15(v) for v in matrix))
        elif key == "B" and b_curves:
            place("B", b"".join(_pad4(c) for c in b_curves))
    head = bytearray(b"mAB \0\0\0\0" if a_to_b else b"mBA \0\0\0\0") + bytes((inputs, outputs, 0, 0))
    for key in ["B", "Matrix", "M", "CLUT", "A"]:
        head += offsets.get(key, 0).to_bytes(4, "big")
    return bytes(head) + b"".join(parts)


def icc_profile(cls, space, pcs, version, tags):
    """Assembles a profile from (signature, data) pairs; identical data is stored once."""
    table = bytearray()
    data = bytearray()
    seen = {}
    start = 128 + 4 + 12 * len(tags)
    for sig, body in tags:
        body = _pad4(body)
        if body not in seen:
            seen[body] = start + len(data)
            data += body
        table += sig + seen[body].to_bytes(4, "big") + len(body).to_bytes(4, "big")
    size = start + len(data)
    header = bytearray(128)
    header[0:4] = size.to_bytes(4, "big")
    header[8:12] = version.to_bytes(4, "big")
    header[12:16], header[16:20], header[20:24] = cls, space, pcs
    header[36:40] = b"acsp"
    header[68:80] = _s15(0.9642) + _s15(1.0) + _s15(0.8249)
    return bytes(header) + len(tags).to_bytes(4, "big") + bytes(table) + bytes(data)


def _lab_encode(L, a, b, v2):
    """Lab to ICC 16-bit grid fractions (v2 legacy or v4 encoding)."""
    if v2:
        return [L * 652.80 / 65535, (a + 128) * 256 / 65535, (b + 128) * 256 / 65535]
    return [L / 100.0, (a + 128) / 255.0, (b + 128) / 255.0]


def _cmyk_to_lab(c, m, y, k):
    """A made-up press: subtractive inks with some cross-talk, a paper white and a dark but not black solid."""
    r = (1 - c) * (1 - 0.1 * m) * (1 - 0.92 * k)
    g = (1 - m) * (1 - 0.15 * c) * (1 - 0.92 * k)
    b = (1 - y) * (1 - 0.1 * m) * (1 - 0.92 * k)
    r, g, b = (0.15 + 0.82 * v for v in (r, g, b))
    lin = [v ** 2.2 for v in (r, g, b)]
    x = 0.4361 * lin[0] + 0.3851 * lin[1] + 0.1431 * lin[2]
    yy = 0.2225 * lin[0] + 0.7169 * lin[1] + 0.0606 * lin[2]
    z = 0.0139 * lin[0] + 0.0971 * lin[1] + 0.7141 * lin[2]
    f = lambda t: t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    fx, fy, fz = f(x / 0.9642), f(yy), f(z / 0.8249)
    return 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)


def _lab_to_cmyk(L, a, b):
    """A rough inverse of _cmyk_to_lab, good enough for a black-point search."""
    fy = (L + 16) / 116
    finv = lambda t: t ** 3 if t > 0.2069 else (t - 16 / 116) / 7.787
    x, yy, z = 0.9642 * finv(fy + a / 500), finv(fy), 0.8249 * finv(fy - b / 200)
    r = 3.1339 * x - 1.6169 * yy - 0.4906 * z
    g = -0.9788 * x + 1.9161 * yy + 0.0335 * z
    bl = 0.0719 * x - 0.2290 * yy + 1.4052 * z
    r, g, bl = (max(0.0, min(1.0, v)) ** (1 / 2.2) for v in (r, g, bl))
    c, m, y = 1 - r, 1 - g, 1 - bl
    k = min(c, m, y) * 0.8
    return [max(0.0, min(1.0, (v - k) / (1 - k) if k < 1 else 0)) for v in (c, m, y)] + [k]


def icc_profiles():
    """Self-made profiles in each structure the evaluator reads."""
    wtpt = (b"wtpt", icc_xyz(0.9642, 1.0, 0.8249))
    paper = (b"wtpt", icc_xyz(0.93, 0.97, 0.80))
    gray = icc_profile(b"mntr", b"GRAY", b"XYZ ", 0x02100000, [wtpt, (b"kTRC", icc_curv([int(65535 * (i / 16) ** 1.8) for i in range(17)]))])
    srgbish = [(b"rXYZ", icc_xyz(0.4361, 0.2225, 0.0139)), (b"gXYZ", icc_xyz(0.3851, 0.7169, 0.0971)), (b"bXYZ", icc_xyz(0.1431, 0.0606, 0.7141))]
    wide = [(b"rXYZ", icc_xyz(0.6097, 0.3111, 0.0195)), (b"gXYZ", icc_xyz(0.2053, 0.6257, 0.0609)), (b"bXYZ", icc_xyz(0.1492, 0.0632, 0.7446))]
    trc3 = icc_para(3, [2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045])
    rgb_v4 = icc_profile(b"mntr", b"RGB ", b"XYZ ", 0x04300000, [wtpt] + wide + [(b"rTRC", trc3), (b"gTRC", icc_para(0, [1.8])), (b"bTRC", icc_para(2, [2.0, 1.0, 0.0, 0.02]))])
    rgb_lut = icc_profile(b"scnr", b"RGB ", b"XYZ ", 0x04300000, [wtpt, (b"A2B0", icc_mab(
        True, 3, 3, 5, lambda p: [p[0] * 0.9 + 0.05 * p[1], p[1] * 0.95, p[2] * 0.85 + 0.1 * p[0]],
        [icc_curv([2.0]), icc_para(0, [1.5]), icc_curv([0, 30000, 50000, 65535])],
        [icc_para(0, [1.2])] * 3,
        [0.4361, 0.3851, 0.1431, 0.2225, 0.7169, 0.0606, 0.0139, 0.0971, 0.7141, 0.002, 0.0, -0.001],
        [icc_curv([])] * 3))] + srgbish)
    fwd = lambda v2: (lambda p: _lab_encode(*_cmyk_to_lab(*p), v2))
    inv = lambda v2: (lambda p: _lab_to_cmyk(*([p[0] * 65535 / 652.80, p[1] * 65535 / 256 - 128, p[2] * 65535 / 256 - 128] if v2 else [p[0] * 100, p[1] * 255 - 128, p[2] * 255 - 128])))
    cmyk_v2 = icc_profile(b"prtr", b"CMYK", b"Lab ", 0x02100000, [paper,
        (b"A2B0", icc_mft(True, 4, 3, 5, fwd(True), entries=32)),
        (b"A2B1", icc_mft(True, 4, 3, 5, lambda p: [v * 0.995 for v in fwd(True)(p)], entries=32)),
        (b"B2A0", icc_mft(True, 3, 4, 7, inv(True), entries=32))])
    cmyk_v4 = icc_profile(b"prtr", b"CMYK", b"Lab ", 0x04300000, [paper,
        (b"A2B0", icc_mab(True, 4, 3, 5, fwd(False), [icc_curv([1.1])] * 4, None, None, [icc_curv([])] * 3)),
        (b"B2A0", icc_mab(False, 3, 4, 7, inv(False), [icc_curv([])] * 4, None, None, [icc_curv([])] * 3))])
    cmyk_8 = icc_profile(b"scnr", b"CMYK", b"Lab ", 0x02100000, [wtpt, (b"A2B0", icc_mft(False, 4, 3, 5, lambda p: [min(1, v * 65535 / 65280) for v in fwd(True)(p)]))])
    return [(b"Gray", 1, gray), (b"RgbTrc", 3, rgb_v4), (b"RgbLut", 3, rgb_lut), (b"CmykV2", 4, cmyk_v2), (b"CmykV4", 4, cmyk_v4), (b"Cmyk8", 4, cmyk_8)]


def color():
    """ICCBased fills and an image through each self-made profile, under every rendering intent."""
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    spaces = []
    for name, n, profile in icc_profiles():
        ref = add(stream(b"/N %d /Alternate /%s" % (n, {1: b"DeviceGray", 3: b"DeviceRGB", 4: b"DeviceCMYK"}[n]), profile))
        spaces.append((name, n, ref))
    refs = {nm: r for nm, _, r in spaces}
    pic = add(image_object(24, 16, b"[/ICCBased %d 0 R]" % refs[b"CmykV2"], 8, picture(24, 16, 4)))
    pic_rgb = add(image_object(24, 16, b"[/ICCBased %d 0 R]" % refs[b"RgbLut"], 8, picture(24, 16, 3)))
    resources = (b"<< /ColorSpace << " + b" ".join(b"/%s [/ICCBased %d 0 R]" % (nm, r) for nm, _, r in spaces) +
                 b" >> /XObject << /Pic %d 0 R /PicRgb %d 0 R >> /ExtGState << /Per << /RI /Perceptual >> >> >>" % (pic, pic_rgb))
    c = bytearray()
    intents = [b"RelativeColorimetric", b"Perceptual", b"Saturation", b"AbsoluteColorimetric"]
    for row, (name, n, _) in enumerate(spaces):
        for column, intent in enumerate(intents):
            c += b"/%s ri /%s cs\n" % (intent, name)
            for i in range(9):
                v = [((i * 37 + j * 71 + row * 13) % 9) / 8 for j in range(n)]
                if i == 0:
                    v = [1.0] * n if n == 4 else [0.0] * n
                c += b"%s sc %d %d 5 8 re f\n" % (b" ".join(b"%.4f" % x for x in v), 4 + column * 48 + i * 5, 190 - row * 12)
    c += b"/RelativeColorimetric ri q 60 0 0 40 10 80 cm /Pic Do Q q /Per gs 60 0 0 40 80 80 cm /Pic Do Q q 50 0 0 40 145 80 cm /PicRgb Do Q\n"
    return page(bytes(c), resources, extra=extra)


def _cff_index(items):
    if not items:
        return b"\0\0"
    offsets = [1]
    for item in items:
        offsets.append(offsets[-1] + len(item))
    return len(items).to_bytes(2, "big") + b"\x04" + b"".join(o.to_bytes(4, "big") for o in offsets) + b"".join(items)


def _cff_int(v):
    return b"\x1d" + v.to_bytes(4, "big", signed=True)


def type1_charstring_cff():
    """Fonts/type1.pfa recast as a bare CFF with CharstringType 1: same glyphs, same subroutines."""
    from fontTools import t1Lib
    from fontTools.cffLib import cffStandardStrings

    here = os.path.dirname(os.path.abspath(__file__))
    t1 = t1Lib.T1Font(os.path.join(here, "Fonts", "type1.pfa"))
    t1.parse()
    names = list(t1.font["CharStrings"].keys())
    names.remove(".notdef")
    names.insert(0, ".notdef")

    def code(cs):
        cs.compile()
        return cs.bytecode
    charstrings = [code(t1.font["CharStrings"][n]) for n in names]
    subrs = [code(s) for s in t1.font["Private"]["Subrs"]]
    charset = b"\0" + b"".join(cffStandardStrings.index(n).to_bytes(2, "big") for n in names[1:])
    private_head = _cff_int(0) + b"\x13"  # Subrs, offset patched below
    private = _cff_int(len(private_head)) + b"\x13"

    header = b"\x01\x00\x04\x04"
    name_index = _cff_index([b"T1CffTest"])
    top_len = len(_cff_index([_cff_int(0) + b"\x0f" + _cff_int(0) + b"\x11" + _cff_int(0) + _cff_int(0) + b"\x12" + b"\x8c\x0c\x06"]))
    start = len(header) + len(name_index) + top_len + len(_cff_index([])) + len(_cff_index([]))
    charset_at = start
    charstrings_at = charset_at + len(charset)
    charstrings_index = _cff_index(charstrings)
    private_at = charstrings_at + len(charstrings_index)
    top = _cff_int(charset_at) + b"\x0f" + _cff_int(charstrings_at) + b"\x11" + _cff_int(len(private)) + _cff_int(private_at) + b"\x12" + b"\x8c\x0c\x06"
    return header + name_index + _cff_index([top]) + _cff_index([]) + _cff_index([]) + charset + charstrings_index + private + _cff_index(subrs)


def type1cff():
    """A Type1C font program carrying Type 1 charstrings, as simple font and as CIDFontType0."""
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    program = add(stream(b"/Subtype /Type1C", type1_charstring_cff(), flate=False))
    d = add(b"<< /Type /FontDescriptor /FontName /T1CffTest /Flags 34 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile3 %d 0 R >>" % program)
    widths = b"[" + b" ".join(b"600" for _ in range(224)) + b"]"
    simple = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /T1CffTest /FirstChar 32 /LastChar 255 /Widths %s /FontDescriptor %d 0 R /Encoding << /Differences [66 /Aacute] >> >>" % (widths, d))
    resources = b"<< /Font << /F %d 0 R >> >>" % simple
    c = b"BT /F 36 Tf 10 120 Td (AFOS) Tj 0 -50 Td 0.7 0 0 rg (WISO) Tj 1 Tr 0 0 1 RG 0 -50 Td (AW) Tj ET\n"
    return page(c, resources, extra=extra)


def cmaps():
    """Composite fonts under predefined CMaps of every collection, one- and two-byte and vertical, plus CIDToGIDMap streams.

    The self-made CID-keyed CFF has glyphs at CIDs 1, 34, 39, 40, 41, 48, 52, 96 and 150; in every
    collection the printable ASCII range of the pc/Uni CMaps lands on CIDs 1-95 (code - 31), so
    "AFGHOS" reaches 34 39 40 41 48 52, and a two-byte code is added where a CMap reaches 96.
    """
    here = os.path.dirname(os.path.abspath(__file__))
    font = lambda name: read(os.path.join(here, "Fonts", name))
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    cidcff = add(stream(b"/Subtype /CIDFontType0C", font("cid-keyed.cff"), flate=False))
    d4 = add(b"<< /Type /FontDescriptor /FontName /CidTest /Flags 4 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile3 %d 0 R >>" % cidcff)
    # poppler looks a predefined CMap up in the descendant's own character collection, so each
    # collection gets a descendant of its own over the same program.
    collections = {}
    for ordering, supplement in [(b"Japan1", 7), (b"GB1", 6), (b"Korea1", 2), (b"CNS1", 7)]:
        collections[ordering] = add(b"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /CidTest /CIDSystemInfo << /Registry (Adobe) /Ordering (%s) /Supplement %d >> /FontDescriptor %d 0 R /DW 700 /W [34 [600 500] 40 52 650] >>" % (ordering, supplement, d4))
    ttf = add(stream(b"/Length1 %d" % len(font("truetype.ttf")), font("truetype.ttf")))
    d3 = add(b"<< /Type /FontDescriptor /FontName /TtTest /Flags 4 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 %d 0 R >>" % ttf)
    gid_map = bytearray(2 * 60)
    for cid, gid in [(34, 2), (39, 3), (41, 4), (48, 2), (52, 3)]:
        gid_map[2 * cid:2 * cid + 2] = gid.to_bytes(2, "big")
    cidmap = add(stream(b"", bytes(gid_map)))
    cid2 = add(b"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /TtTest /CIDSystemInfo << /Registry (Adobe) /Ordering (Korea1) /Supplement 2 >> /FontDescriptor %d 0 R /DW 800 /CIDToGIDMap %d 0 R >>" % (d3, cidmap))
    encodings = [(b"UniJIS-UCS2-H", b"<00410046004700480020004F0053>"), (b"90msp-RKSJ-H", b"<41464748204F53>"),
                 (b"GBpc-EUC-H", b"<4146A1A1474820>"), (b"UniGB-UCS2-H", b"<0041004600470048>"),
                 (b"KSCpc-EUC-H", b"<41814647484F>"), (b"UniKS-UCS2-H", b"<004100460047>"),
                 (b"B5pc-H", b"<414647484F53>"), (b"UniCNS-UCS2-H", b"<0041004600470048>"),
                 (b"GBK-EUC-H", b"<A1A1>"), (b"UniJIS-UTF16-H", b"<004100460047>")]
    def collection(name):
        return collections[b"Japan1" if b"JIS" in name or b"RKSJ" in name else b"GB1" if b"GB" in name else b"Korea1" if b"KS" in name else b"CNS1"]

    fonts = []
    for name, _ in encodings:
        fonts.append(add(b"<< /Type /Font /Subtype /Type0 /BaseFont /CidTest /Encoding /%s /DescendantFonts [%d 0 R] >>" % (name, collection(name))))
    vertical = add(b"<< /Type /Font /Subtype /Type0 /BaseFont /CidTest /Encoding /UniJIS-UCS2-V /DescendantFonts [%d 0 R] >>" % collections[b"Japan1"])
    tt = add(b"<< /Type /Font /Subtype /Type0 /BaseFont /TtTest /Encoding /UniKS-UCS2-H /DescendantFonts [%d 0 R] >>" % cid2)
    names = b" ".join(b"/E%d %d 0 R" % (i, f) for i, f in enumerate(fonts))
    resources = b"<< /Font << %s /V %d 0 R /T %d 0 R >> >>" % (names, vertical, tt)
    c = bytearray(b"BT 16 TL\n")
    for i, (_, text) in enumerate(encodings):
        c += b"/E%d 14 Tf 1 0 0 1 %d %d Tm %s Tj\n" % (i, 8 + (i % 2) * 95, 180 - (i // 2) * 22, text)
    c += b"/V 16 Tf 1 0 0 1 185 190 Tm <0041004600470048> Tj\n"
    c += b"/T 18 Tf 1 0 0 1 8 40 Tm <00410046004800200030> Tj ET\n"
    return page(bytes(c), resources, extra=extra)


def overprint():
    """Overprinting fills over a cyan backdrop: OPM 0 and 1, a spot colour, RGB, grey, black alone and all-zero CMYK."""
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    fn = add(b"<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0.6 1 0] /N 1 >>")
    fn2 = add(b"<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.2 0 0.9 0.1] /N 1 >>")
    resources = (b"<< /ExtGState << /OP << /OP true /op true /OPM 0 >> /OPM1 << /OP true /op true /OPM 1 >> /NoOP << /OP false /op false >> /SOP << /OP true >> >>"
                 b" /ColorSpace << /Spot [/Separation /Orange /DeviceCMYK %d 0 R] /Spot2 [/Separation /Lime /DeviceCMYK %d 0 R] >> >>" % (fn, fn2))
    c = b"0.8 0 0 0 k 0 0 200 200 re f 0 0.3 0 0 k 0 0 40 200 re f\n"  # cyan backdrop, magenta stripe on the left
    c += b"q /OP gs 0 0.9 0 0 k 10 140 50 50 re f Q\n"
    c += b"q /OPM1 gs 0 0.9 0 0 k 70 140 50 50 re f Q\n"
    c += b"q /NoOP gs 0 0.9 0 0 k 130 140 50 50 re f Q\n"
    c += b"q /OP gs /Spot cs 1 scn 10 80 50 50 re f /Spot2 cs 0.7 scn 30 90 50 30 re f Q\n"
    c += b"q /OP gs 0 0 1 rg 70 80 50 50 re f Q\n"
    c += b"q /OP gs 0.5 g 130 80 50 50 re f Q\n"
    c += b"q /OPM1 gs 0 0 0 0.5 k 10 20 50 50 re f Q\n"
    c += b"q /OPM1 gs 0 0 0 0 k 70 20 50 50 re f Q\n"
    c += b"q /OP gs 0 0 0 0 k 130 20 50 50 re f Q\n"
    c += b"q /SOP gs 0 0.9 0 0 K 6 w 5 5 m 195 195 l S Q\n"  # OP alone sets op too
    return page(c, resources, extra=extra)


def symbols():
    """The non-embedded Symbol and ZapfDingbats, drawn with whatever each renderer substitutes."""
    resources = (b"<< /Font << /S << /Type /Font /Subtype /Type1 /BaseFont /Symbol >>"
                 b" /Z << /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >> >> >>")
    c = b"BT /S 18 Tf 8 170 Td (abgdpqwW) Tj 0 -24 Td (\\042\\044\\245\\326\\362\\345\\260) Tj"
    c += b" 0 -24 Td (\\346\\347\\350 \\351\\352\\353 \\355 \\374\\375\\376) Tj ET\n"
    c += b"BT /Z 18 Tf 8 90 Td (3456789:;<) Tj 0 -24 Td (\\254\\255\\256\\257\\330\\331) Tj 0 -24 Td (nopqrs) Tj ET\n"
    return page(c, resources)


def cjk():
    """Non-embedded CID fonts of Adobe-Japan1 and Adobe-GB1, horizontal and vertical, for an installed font to draw.

    The tests point the renderer at CjkFonts/ alone and pdftoppm at the same folder through a private
    fontconfig file (see PdfRenderTests.NonEmbeddedCjkFixture_AgreesWithPoppler), so MS-Gothic is
    drawn with "Fixture Sans CJK JP" (CFF, reached through Unicode), MS-Mincho with "Fixture Serif CJK
    JP" (CFF keyed by Adobe-Japan1, read by CID) and STHeiti with "Fixture Sans CJK SC" (TrueType).
    The Identity lines name CIDs: horizontal forms, and the vertical forms 7887-7912 of 、。「」.
    """
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    def descendant(name, ordering, supplement, flags):
        descriptor = add(b"<< /Type /FontDescriptor /FontName /%s /Flags %d /FontBBox [0 -141 1000 859] /ItalicAngle 0 /Ascent 859 /Descent -141 /CapHeight 700 /StemV 80 >>" % (name, flags))
        return add(b"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /%s /CIDSystemInfo << /Registry (Adobe) /Ordering (%s) /Supplement %d >> /FontDescriptor %d 0 R /DW 1000 /W [1 95 500] >>" % (name, ordering, supplement, descriptor))

    gothic = descendant(b"MS-Gothic", b"Japan1", 6, 4)
    mincho = descendant(b"MS-Mincho", b"Japan1", 6, 6)
    heiti = descendant(b"STHeiti-Regular", b"GB1", 5, 4)
    fonts = {}
    for key, name, encoding, cid in [(b"J", b"MS-Gothic", b"UniJIS-UCS2-H", gothic), (b"JV", b"MS-Gothic", b"UniJIS-UCS2-V", gothic),
                                     (b"JI", b"MS-Gothic", b"Identity-H", gothic), (b"M", b"MS-Mincho", b"UniJIS-UCS2-H", mincho),
                                     (b"MV", b"MS-Mincho", b"UniJIS-UCS2-V", mincho), (b"MI", b"MS-Mincho", b"Identity-V", mincho),
                                     (b"S", b"STHeiti-Regular", b"UniGB-UCS2-H", heiti), (b"SV", b"STHeiti-Regular", b"UniGB-UCS2-V", heiti)]:
        fonts[key] = add(b"<< /Type /Font /Subtype /Type0 /BaseFont /%s /Encoding /%s /DescendantFonts [%d 0 R] >>" % (name, encoding, cid))
    resources = b"<< /Font << " + b" ".join(b"/%s %d 0 R" % (k, v) for k, v in fonts.items()) + b" >> >>"

    def u(text):
        return b"<" + text.encode("utf-16-be").hex().upper().encode() + b">"

    japanese = "日本語のテキスト、縦書き。「ー」（漢字）ＡＢ ABab12"
    chinese = "中文简体字，这是测试。“国”（汉字）AB12"
    cids = b"<" + b"".join(b"%04X" % c for c in (843, 845, 847, 634, 635, 686, 687, 7887, 7888, 7911, 7912, 1533, 2248, 34, 35, 66, 67, 18)) + b">"
    c = b"BT\n"
    c += b"/J 10 Tf 1 0 0 1 8 312 Tm %s Tj\n" % u(japanese)
    c += b"/M 10 Tf 1 0 0 1 8 296 Tm %s Tj\n" % u(japanese)
    c += b"/S 10 Tf 1 0 0 1 8 280 Tm %s Tj\n" % u(chinese)
    c += b"/JI 10 Tf 1 0 0 1 8 264 Tm %s Tj\n" % cids
    c += b"/JV 9 Tf 1 0 0 1 20 250 Tm %s Tj\n" % u(japanese)
    c += b"/MV 9 Tf 1 0 0 1 42 250 Tm %s Tj\n" % u(japanese)
    c += b"/MI 9 Tf 1 0 0 1 64 250 Tm %s Tj\n" % cids
    c += b"/SV 9 Tf 1 0 0 1 86 250 Tm %s Tj\n" % u(chinese)
    c += b"ET\n"
    return page(c, resources, media=b"[0 0 275 325]", extra=extra)


def jpx_alpha():
    """JPX images carrying their own opacity: /SMaskInData 1 and 2 (with and without /Matte), 0, and CMYK."""
    extra = []

    def add(body):
        extra.append(body)
        return 4 + len(extra)

    def jp2(mode, data, size):
        buffer = io.BytesIO()
        Image.frombytes(mode, size, data).save(buffer, "JPEG2000", irreversible=False)
        return buffer.getvalue()

    w, h = 24, 16
    rgb = picture(w, h, 3)
    opacity = [int(255 * x / (w - 1)) for y in range(h) for x in range(w)]
    straight = bytearray()
    for i in range(w * h):
        straight += rgb[3 * i:3 * i + 3] + bytes((opacity[i],))
    matte = (0.0, 1.0, 0.0)
    pre = bytearray()
    for i in range(w * h):
        a = opacity[i] / 255
        pre += bytes(int(round((rgb[3 * i + c] / 255 * a + matte[c] * (1 - a)) * 255)) for c in range(3)) + bytes((opacity[i],))
    gray_alpha = bytearray()
    for i in range(w * h):
        gray_alpha += bytes((rgb[3 * i], 255 - opacity[i]))

    def jpx_image(data, extra_entries):
        return b"<< /Type /XObject /Subtype /Image /Width %d /Height %d /BitsPerComponent 8 /Filter /JPXDecode %s /Length %d >>\nstream\n" % (w, h, extra_entries, len(data)) + data + b"\nendstream"

    one = add(jpx_image(jp2("RGBA", bytes(straight), (w, h)), b"/ColorSpace /DeviceRGB /SMaskInData 1"))
    two = add(jpx_image(jp2("RGBA", bytes(pre), (w, h)), b"/ColorSpace /DeviceRGB /SMaskInData 2 /Matte [0 1 0]"))
    zero = add(jpx_image(jp2("RGBA", bytes(straight), (w, h)), b"/ColorSpace /DeviceRGB"))
    gray = add(jpx_image(jp2("LA", bytes(gray_alpha), (w, h)), b"/ColorSpace /DeviceGray /SMaskInData 1"))
    resources = b"<< /XObject << /One %d 0 R /Two %d 0 R /Zero %d 0 R /Gray %d 0 R >> >>" % (one, two, zero, gray)
    c = b"0.5 0.5 0.9 rg 0 0 200 100 re f 0.9 0.4 0.1 rg 0 40 200 20 re f\n"
    c += b"q 80 0 0 40 10 50 cm /One Do Q q 80 0 0 40 110 50 cm /Two Do Q q 80 0 0 40 10 5 cm /Zero Do Q q 80 0 0 40 110 5 cm /Gray Do Q\n"
    return page(c, resources, media=b"[0 0 200 100]", extra=extra)


if __name__ == "__main__":
    target = sys.argv[1]
    os.makedirs(os.path.join(target, "Render"), exist_ok=True)
    for name, maker in [("images", images), ("vector", vector), ("bilevel", bilevel), ("shading", shading), ("text", text), ("transparency", transparency), ("color", color), ("jpx-alpha", jpx_alpha), ("cmaps", cmaps), ("type1cff", type1cff), ("overprint", overprint), ("symbols", symbols), ("cjk", cjk)]:
        with open(os.path.join(target, "Render", name + ".pdf"), "wb") as handle:
            handle.write(maker())
    os.makedirs(os.path.join(target, "Fonts"), exist_ok=True)
    with open(os.path.join(target, "Fonts", "type1-charstrings.cff"), "wb") as handle:
        handle.write(type1_charstring_cff())
    os.makedirs(os.path.join(target, "Color"), exist_ok=True)
    for name, _, profile in icc_profiles():
        with open(os.path.join(target, "Color", name.decode() + ".icc"), "wb") as handle:
            handle.write(profile)
