using System;
using System.IO;
using System.Text;
using FileFormat.Fbm;

namespace FileFormat.Fbm.Tests;

/// <summary>
/// Reading a CMU Fuzzy Bitmap: a text header, then one whole plane per band, rows bottom to top.
/// </summary>
/// <remarks>
/// These used to build their samples as big-endian integers laid out at 8, 12, 16, 20 and so on,
/// with the bands interleaved and the rows top-down. No file has any of that, so the tests passed
/// against a reader that agreed with them and disagreed with the format. A real 800 by 600 sample
/// settled every one of those three questions.
/// </remarks>
[TestFixture]
public sealed class FbmReaderTests {

  /// <summary>Builds a file the way one is really laid out, from an interleaved top-down picture.</summary>
  private static byte[] _BuildValidFbm(int cols, int rows, int bands, byte[]? pixels = null, int rowLen = 0, string title = "") {
    if (rowLen <= 0)
      rowLen = cols;

    var planeLen = rowLen * rows;
    var data = new byte[FbmHeader.StructSize + planeLen * bands];
    new FbmHeader(cols, rows, bands, 8, 8, rowLen, planeLen, 0, 1.0, title).WriteTo(data);

    if (pixels != null)
      for (var band = 0; band < bands; ++band)
      for (var y = 0; y < rows; ++y)
      for (var x = 0; x < cols; ++x) {
        var source = (y * cols + x) * bands + band;
        if (source < pixels.Length)
          data[FbmHeader.StructSize + band * planeLen + (rows - 1 - y) * rowLen + x] = pixels[source];
      }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FbmReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FbmReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fbm"));
    Assert.Throws<FileNotFoundException>(() => FbmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FbmReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FbmReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[FbmHeader.StructSize];
    data[0] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => FbmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var pixels = new byte[] { 10, 20, 30, 40 };

    var file = FbmReader.FromBytes(_BuildValidFbm(2, 2, 1, pixels));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.Bands, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17);

    var file = FbmReader.FromBytes(_BuildValidFbm(2, 2, 3, pixels));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Bands, Is.EqualTo(3));
      Assert.That(file.PixelData, Is.EqualTo(pixels), "the three planes come back interleaved");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LastRowStoredIsTheTopOfThePicture() {
    // The single thing a made-up sample can never catch: get this backwards and a symmetric test
    // picture still passes.
    var data = _BuildValidFbm(1, 3, 1);
    data[FbmHeader.StructSize] = 0x11;
    data[FbmHeader.StructSize + 1] = 0x22;
    data[FbmHeader.StructSize + 2] = 0x33;

    Assert.That(FbmReader.FromBytes(data).PixelData, Is.EqualTo(new byte[] { 0x33, 0x22, 0x11 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_StripsRowPadding() {
    // rowlen is a row of ONE plane, padding included — not the interleaved stride.
    var data = _BuildValidFbm(3, 1, 1, rowLen: 16);
    data[FbmHeader.StructSize] = 0xAA;
    data[FbmHeader.StructSize + 1] = 0xBB;
    data[FbmHeader.StructSize + 2] = 0xCC;

    Assert.That(FbmReader.FromBytes(data).PixelData, Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC }));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale() {
    var pixels = new byte[] { 42, 84, 126, 168 };
    using var ms = new MemoryStream(_BuildValidFbm(4, 1, 1, pixels));

    var file = FbmReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(4));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TitlePreserved()
    => Assert.That(FbmReader.FromBytes(_BuildValidFbm(1, 1, 1, [0xFF], title: "Test Image")).Title, Is.EqualTo("Test Image"));

  [Test]
  [Category("Unit")]
  public void FromBytes_ColourMapIsSkippedBeforeThePicture() {
    // clrlen says how many bytes sit between the header and the picture.
    var data = new byte[FbmHeader.StructSize + 12 + 2];
    new FbmHeader(2, 1, 1, 8, 8, 2, 2, 12, 1.0, string.Empty).WriteTo(data);
    data[FbmHeader.StructSize + 12] = 0x7F;
    data[FbmHeader.StructSize + 13] = 0x80;

    Assert.That(FbmReader.FromBytes(data).PixelData, Is.EqualTo(new byte[] { 0x7F, 0x80 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBands_ThrowsInvalidDataException() {
    var data = new byte[FbmHeader.StructSize + 64];
    new FbmHeader(4, 4, 2, 8, 8, 4, 16, 0, 1.0, string.Empty).WriteTo(data);

    Assert.Throws<InvalidDataException>(() => FbmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnsupportedDepth_ThrowsInvalidDataException() {
    var data = new byte[FbmHeader.StructSize + 32];
    new FbmHeader(4, 4, 1, 4, 4, 4, 16, 0, 1.0, string.Empty).WriteTo(data);

    Assert.Throws<InvalidDataException>(() => FbmReader.FromBytes(data));
  }
}
