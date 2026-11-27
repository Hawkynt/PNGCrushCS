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
    g4 = open(os.path.join(here, "Filters", "ccitt-g4.bin"), "rb").read()
    g3 = open(os.path.join(here, "Filters", "ccitt-g3-2d.bin"), "rb").read()
    jbig2 = open(os.path.join(here, "Filters", "jbig2-generic-t0.bin"), "rb").read()

    def raw(dictionary, data):
        return b"<< " + dictionary + b" /Length %d >>\nstream\n" % len(data) + data + b"\nendstream"

    extra = [
        raw(b"/Type /XObject /Subtype /Image /Width 203 /Height 61 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 203 /Rows 61 >>", g4),
        raw(b"/Type /XObject /Subtype /Image /Width 203 /Height 61 /ImageMask true /Filter /CCITTFaxDecode /DecodeParms << /K 1 /Columns 203 /Rows 61 >>", g3),
        raw(b"/Type /XObject /Subtype /Image /Width 96 /Height 64 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /JBIG2Decode", jbig2),
    ]
    content = b"q 203 0 0 61 0 139 cm /G4 Do Q 0 0 0.7 rg q 203 0 0 61 0 70 cm /G3 Do Q q 96 0 0 64 50 2 cm /J2 Do Q"
    return page(content, b"<< /XObject << /G4 5 0 R /G3 6 0 R /J2 7 0 R >> >>", b"[0 0 203 200]", extra)


if __name__ == "__main__":
    target = sys.argv[1]
    os.makedirs(os.path.join(target, "Render"), exist_ok=True)
    for name, maker in [("images", images), ("vector", vector), ("bilevel", bilevel)]:
        with open(os.path.join(target, "Render", name + ".pdf"), "wb") as handle:
            handle.write(maker())
