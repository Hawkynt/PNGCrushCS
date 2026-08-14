using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Heif;

namespace FileFormat.Heif.Tests;

/// <summary>
/// Covers the clean aperture (clap) property: an encoder pads the picture out to a whole number of
/// coding blocks, stores the padded extent in ispe, and states the real extent in clap. Reporting
/// ispe alone hands back the padding.
/// </summary>
/// <remarks>
/// The 64x64 / 61x37 numbers are not invented. They are what ImageMagick 7.1.2 (libheif 1.23.1)
/// wrote for a 61x37 source: ispe says 64x64, clap says 61/1 x 37/1 at offsets -3/2 and -27/2, and
/// heif-info prints "crop: left=0 top=0 right=3 bottom=27" for that same file.
/// </remarks>
[TestFixture]
public sealed class CleanApertureTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_ClapBox_ReportsPictureExtentNotPadding() {
    var bytes = _BuildHeif(64, 64, _Clap(61, 1, 37, 1, -3, 2, -27, 2), new byte[64 * 64 * 3]);

    var result = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(61), "width must come from clap, not ispe");
      Assert.That(result.Height, Is.EqualTo(37), "height must come from clap, not ispe");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ClapBox_PixelDataSizedToPictureExtent() {
    var bytes = _BuildHeif(64, 64, _Clap(61, 1, 37, 1, -3, 2, -27, 2), new byte[64 * 64 * 3]);

    var result = HeifReader.FromBytes(bytes);

    Assert.That(result.PixelData, Has.Length.EqualTo(61 * 37 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClapBox_StaysSelfConsistent() {
    var bytes = _BuildHeif(64, 64, _Clap(61, 1, 37, 1, -3, 2, -27, 2), new byte[64 * 64 * 3]);

    var raw = HeifFile.ToRawImage(HeifReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(61));
      Assert.That(raw.Height, Is.EqualTo(37));
      Assert.That(raw.PixelData, Has.Length.EqualTo(61 * 37 * 3));
    });
  }

  /// <summary>The margin the encoder padded with must be gone, not merely unreported.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ClapBoxAtOrigin_DropsPaddedMargin() {
    const int CODED = 64;
    var raster = _BuildRaster(CODED, CODED);
    var bytes = _BuildHeif(CODED, CODED, _Clap(61, 1, 37, 1, -3, 2, -27, 2), raster);

    var result = HeifReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(61));
    Assert.That(result.Height, Is.EqualTo(37));
    _AssertWindowMatches(result.PixelData, 61, 37, raster, CODED, originX: 0, originY: 0);
  }

  /// <summary>A centred aperture exercises the offset arithmetic rather than the origin case.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_CentredClap_CropsFromCentre() {
    const int CODED = 8;
    var raster = _BuildRaster(CODED, CODED);
    // Offsets of zero put the aperture centre on the image centre: left = (8-4)/2 = 2.
    var bytes = _BuildHeif(CODED, CODED, _Clap(4, 1, 4, 1, 0, 1, 0, 1), raster);

    var result = HeifReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    _AssertWindowMatches(result.PixelData, 4, 4, raster, CODED, originX: 2, originY: 2);
  }

  /// <summary>A non-zero offset shifts the window off centre.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_OffsetClap_ShiftsWindow() {
    const int CODED = 8;
    var raster = _BuildRaster(CODED, CODED);
    // left = (8-4)/2 + (-2) = 0, top = (8-4)/2 + 1 = 3
    var bytes = _BuildHeif(CODED, CODED, _Clap(4, 1, 4, 1, -2, 1, 1, 1), raster);

    var result = HeifReader.FromBytes(bytes);

    _AssertWindowMatches(result.PixelData, 4, 4, raster, CODED, originX: 0, originY: 3);
  }

  /// <summary>
  /// An origin of (8 - 1) / 2 = 3.5 is floored on both axes. libheif floors the left edge and rounds
  /// the top edge up for the same input; the extent, which is what gets reported, agrees regardless.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_HalfPixelOrigin_FlooredOnBothAxes() {
    const int CODED = 8;
    var raster = _BuildRaster(CODED, CODED);
    var bytes = _BuildHeif(CODED, CODED, _Clap(1, 1, 1, 1, 0, 1, 0, 1), raster);

    var result = HeifReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    _AssertWindowMatches(result.PixelData, 1, 1, raster, CODED, originX: 3, originY: 3);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ClapWithRationalDenominators_Resolved() {
    // 122/2 x 74/2 is the same aperture as 61 x 37.
    var bytes = _BuildHeif(64, 64, _Clap(122, 2, 74, 2, -3, 2, -27, 2), new byte[64 * 64 * 3]);

    var result = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(61));
      Assert.That(result.Height, Is.EqualTo(37));
    });
  }

  // --- files without a clap box must be untouched ---

  [Test]
  [Category("Unit")]
  public void FromBytes_NoClapBox_ReportsIspeExtent() {
    var bytes = _BuildHeif(64, 64, clap: null, new byte[64 * 64 * 3]);

    var result = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(64));
      Assert.That(result.Height, Is.EqualTo(64));
      Assert.That(result.PixelData, Has.Length.EqualTo(64 * 64 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoClapBox_PixelDataUntouched() {
    var raster = _BuildRaster(8, 8);
    var bytes = _BuildHeif(8, 8, clap: null, raster);

    var result = HeifReader.FromBytes(bytes);

    Assert.That(result.PixelData, Is.EqualTo(raster));
  }

  // --- malformed apertures fall back to the coded extent rather than throwing ---

  [Test]
  [Category("Unit")]
  public void FromBytes_ClapLargerThanImage_Ignored() {
    var bytes = _BuildHeif(8, 8, _Clap(16, 1, 16, 1, 0, 1, 0, 1), new byte[8 * 8 * 3]);

    var result = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ClapWithZeroDenominator_Ignored() {
    var bytes = _BuildHeif(8, 8, _Clap(4, 0, 4, 1, 0, 1, 0, 1), new byte[8 * 8 * 3]);

    var result = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ClapWithZeroExtent_Ignored() {
    var bytes = _BuildHeif(8, 8, _Clap(0, 1, 4, 1, 0, 1, 0, 1), new byte[8 * 8 * 3]);

    var result = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(8));
    });
  }

  /// <summary>
  /// A coded extent whose byte count overflows a 32-bit int leaves an empty raster behind, and an
  /// aperture nearly as large would overflow the crop allocation too. Neither may throw.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ClapOnOverflowingCodedExtent_Ignored() {
    const int CODED_WIDTH = 715827883; // 715827883 * 3 * 3 wraps past int.MaxValue
    const int CODED_HEIGHT = 3;
    var bytes = _BuildHeif(CODED_WIDTH, CODED_HEIGHT, _Clap(CODED_WIDTH - 1, 1, 2, 1, 0, 1, 0, 1), [1, 2, 3, 4]);

    var result = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(CODED_WIDTH));
      Assert.That(result.Height, Is.EqualTo(CODED_HEIGHT));
      Assert.That(result.PixelData, Is.Empty);
    });
  }

  /// <summary>
  /// A tiny aperture passes every extent check, but indexing row y of a raster that wide overflows
  /// a 32-bit offset. The raster is empty here for the same reason, so no row is readable at all.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TinyClapOnOverflowingCodedExtent_Ignored() {
    const int CODED_WIDTH = 715827883;
    const int CODED_HEIGHT = 3;
    var bytes = _BuildHeif(CODED_WIDTH, CODED_HEIGHT, _Clap(1, 1, 1, 1, 0, 1, 0, 1), [1, 2, 3, 4]);

    var result = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(1));
      Assert.That(result.Height, Is.EqualTo(1));
      Assert.That(result.PixelData, Has.Length.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedClap_Ignored() {
    var bytes = _BuildHeif(8, 8, new byte[16], new byte[8 * 8 * 3]);

    var result = HeifReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(8));
    });
  }

  // --- Helpers ---

  /// <summary>An Rgb24 raster whose every pixel encodes its own coordinates.</summary>
  private static byte[] _BuildRaster(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var i = (y * width + x) * 3;
        data[i] = (byte)(x * 3 + 1);
        data[i + 1] = (byte)(y * 3 + 2);
        data[i + 2] = (byte)((x + y) * 3 + 3);
      }

    return data;
  }

  private static void _AssertWindowMatches(byte[] actual, int width, int height, byte[] source, int sourceWidth, int originX, int originY) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var dst = (y * width + x) * 3;
        var src = ((y + originY) * sourceWidth + (x + originX)) * 3;
        for (var c = 0; c < 3; ++c)
          Assert.That(actual[dst + c], Is.EqualTo(source[src + c]), $"pixel ({x},{y}) channel {c}");
      }
  }

  /// <summary>The eight rationals of a CleanApertureBox payload, in file order.</summary>
  private static byte[] _Clap(int widthN, int widthD, int heightN, int heightD, int horizOffN, int horizOffD, int vertOffN, int vertOffD) {
    var data = new byte[32];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), widthN);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), widthD);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), heightN);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), heightD);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), horizOffN);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), horizOffD);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(24), vertOffN);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(28), vertOffD);
    return data;
  }

  /// <summary>
  /// ftyp + meta{iprp{ipco{ispe[,clap]}}} + mdat, laid out the way libheif emits it: ispe first,
  /// clap second. Built by hand rather than through HeifWriter, which cannot emit a clap box.
  /// </summary>
  private static byte[] _BuildHeif(int codedWidth, int codedHeight, byte[]? clap, byte[] mdatPayload) {
    var ispeBody = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(ispeBody.AsSpan(0), (uint)codedWidth);
    BinaryPrimitives.WriteUInt32BigEndian(ispeBody.AsSpan(4), (uint)codedHeight);

    var ipcoParts = new List<byte[]> { _FullBox("ispe", ispeBody) };
    if (clap != null)
      ipcoParts.Add(_Box("clap", clap));

    var ipco = _Box("ipco", _Concat(ipcoParts));
    var iprp = _Box("iprp", ipco);
    var meta = _FullBox("meta", iprp);
    var mdat = _Box("mdat", mdatPayload);

    return _Concat([_BuildFtyp("heic"), meta, mdat]);
  }

  private static byte[] _BuildFtyp(string brand) {
    var body = new byte[12];
    System.Text.Encoding.ASCII.GetBytes(brand, 0, 4, body, 0);
    System.Text.Encoding.ASCII.GetBytes("mif1", 0, 4, body, 8);
    return _Box("ftyp", body);
  }

  private static byte[] _Box(string type, byte[] body) {
    var result = new byte[8 + body.Length];
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0), (uint)result.Length);
    System.Text.Encoding.ASCII.GetBytes(type, 0, 4, result, 4);
    body.CopyTo(result.AsSpan(8));
    return result;
  }

  private static byte[] _FullBox(string type, byte[] body) {
    var inner = new byte[4 + body.Length];
    body.CopyTo(inner.AsSpan(4));
    return _Box(type, inner);
  }

  private static byte[] _Concat(IReadOnlyList<byte[]> parts) {
    var total = 0;
    foreach (var part in parts)
      total += part.Length;

    var result = new byte[total];
    var offset = 0;
    foreach (var part in parts) {
      part.CopyTo(result.AsSpan(offset));
      offset += part.Length;
    }

    return result;
  }
}
