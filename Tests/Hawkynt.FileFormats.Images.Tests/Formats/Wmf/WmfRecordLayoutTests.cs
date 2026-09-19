using System;
using System.Buffers.Binary;
using FileFormat.Wmf;

namespace FileFormat.Wmf.Tests;

/// <summary>
/// Reads a written metafile back the way a player reads it: at the byte offsets the specification
/// states, not through the structures this package writes it with.
/// </summary>
/// <remarks>
/// Every other test of this writer goes back through our own reader, and our own reader cannot
/// notice either of the two defects below. It skips a fixed twenty-eight bytes from the start of the
/// META_STRETCHDIB record to reach the DIB and reads none of the parameters in between, and it takes
/// the picture's size from the DIB rather than from the placeable header. So both were wrong for as
/// long as the round trip was the only thing asking, and both are invisible until something else
/// looks:
/// <para/>
/// The parameter order is the one MS-WMF 2.3.1.6 states, and <c>ColorUsage</c> comes straight after
/// the raster operation rather than at the end of the list beside the rectangles. Written last, as
/// it was, every parameter after the raster operation shifted by one word, a player read the
/// destination width out of the field holding <c>yDst</c>, found nothing there, and drew a rectangle
/// of no width. LibreOffice converted the file to a blank page.
/// <para/>
/// The placeable header's box is in the metafile's own units and its <c>Inch</c> field says how many
/// of those make an inch. The box is written in pixels, because the one record in the file paints a
/// DIB across it pixel for pixel, so the field has to agree — it said 1440, the twip, which declared
/// a 320 by 200 picture to be a fifth of an inch wide, and IrfanView rendered it 21 by 13.
/// </remarks>
[TestFixture]
public sealed class WmfRecordLayoutTests {

  private const int _WIDTH = 19;
  private const int _HEIGHT = 11;

  /// <summary>Where the first record begins: after the placeable header and the standard one.</summary>
  private static int _FirstRecord => WmfPlaceableHeader.StructSize + WmfStandardHeader.StructSize;

  /// <summary>The pixel, at the resolution a screen picture is counted in.</summary>
  private const double _PIXELS_PER_INCH = 96.0;

  private static byte[] _Written() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 5 % 251);

    return WmfWriter.ToBytes(new WmfFile { Width = _WIDTH, Height = _HEIGHT, PixelData = pixels });
  }

  private static ushort _Word(byte[] file, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(offset));

  private static short _Signed(byte[] file, int offset) => BinaryPrimitives.ReadInt16LittleEndian(file.AsSpan(offset));

  [Test]
  [Category("Unit")]
  public void ThePlaceableBoxIsTheSizeThePictureActuallyIs() {
    var file = _Written();

    // MS-WMF 2.3.2.3: Key(0..4) HWmf(4..6) Left(6..8) Top(8..10) Right(10..12) Bottom(12..14)
    // Inch(14..16) Reserved(16..20) Checksum(20..22).
    var left = _Signed(file, 6);
    var top = _Signed(file, 8);
    var right = _Signed(file, 10);
    var bottom = _Signed(file, 12);
    var unitsPerInch = _Word(file, 14);

    Assert.That(unitsPerInch, Is.Not.Zero, "A metafile with no units to the inch has no size at all.");

    Assert.Multiple(() => {
      Assert.That((right - left) / (double)unitsPerInch, Is.EqualTo(_WIDTH / _PIXELS_PER_INCH).Within(1e-9),
        "The box divided by the units it is counted in is how wide the picture says it is, in inches.");
      Assert.That((bottom - top) / (double)unitsPerInch, Is.EqualTo(_HEIGHT / _PIXELS_PER_INCH).Within(1e-9));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheStretchDibParametersSitWhereTheSpecificationPutsThem() {
    var file = _Written();
    var record = _FirstRecord;

    // MS-WMF 2.3.1.6, from the start of the record: RecordSize(0..4) RecordFunction(4..6)
    // RasterOperation(6..10) ColorUsage(10..12) SrcHeight(12..14) SrcWidth(14..16) YSrc(16..18)
    // XSrc(18..20) DestHeight(20..22) DestWidth(22..24) yDst(24..26) xDst(26..28) DIB(28..).
    Assert.Multiple(() => {
      Assert.That(_Word(file, record + 4), Is.EqualTo((ushort)0x0F43), "META_STRETCHDIB");
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(record + 6)), Is.EqualTo(0x00CC0020u), "SRCCOPY");
      Assert.That(_Word(file, record + 10), Is.EqualTo((ushort)0), "ColorUsage, DIB_RGB_COLORS");
      Assert.That(_Word(file, record + 12), Is.EqualTo((ushort)_HEIGHT), "SrcHeight");
      Assert.That(_Word(file, record + 14), Is.EqualTo((ushort)_WIDTH), "SrcWidth");
      Assert.That(_Word(file, record + 16), Is.EqualTo((ushort)0), "YSrc");
      Assert.That(_Word(file, record + 18), Is.EqualTo((ushort)0), "XSrc");
      Assert.That(_Word(file, record + 20), Is.EqualTo((ushort)_HEIGHT), "DestHeight");
      Assert.That(_Word(file, record + 22), Is.EqualTo((ushort)_WIDTH), "DestWidth");
      Assert.That(_Word(file, record + 24), Is.EqualTo((ushort)0), "yDst");
      Assert.That(_Word(file, record + 26), Is.EqualTo((ushort)0), "xDst");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheDestinationRectangleFillsTheBoxTheHeaderDeclares() {
    var file = _Written();
    var record = _FirstRecord;

    Assert.Multiple(() => {
      Assert.That(_Word(file, record + 22), Is.EqualTo((ushort)(_Signed(file, 10) - _Signed(file, 6))),
        "The destination rectangle and the placeable box are both in the metafile's units, so a "
        + "picture that does not fill its own box is a picture in the corner of one.");
      Assert.That(_Word(file, record + 20), Is.EqualTo((ushort)(_Signed(file, 12) - _Signed(file, 8))));
    });
  }
}
