using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Fbm;

namespace FileFormat.Fbm.Tests;

[TestFixture]
public sealed class FbmReaderTests {

  private static byte[] _BuildValidFbm(int cols, int rows, int bands, byte[]? pixelData = null) {
    var bytesPerPixelRow = cols * bands;
    var rowLen = (bytesPerPixelRow + 15) & ~15;
    var plnLen = rowLen * rows;
    var fileSize = FbmHeader.StructSize + plnLen;
    var data = new byte[fileSize];

    Array.Copy(FbmHeader.MagicBytes, 0, data, 0, 8);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), cols);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), rows);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), bands);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 8); // bits
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(24), 8); // physbits
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(28), rowLen);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(32), plnLen);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(36), 0); // clrlen
    BinaryPrimitives.WriteDoubleBigEndian(data.AsSpan(40), 1.0);

    if (pixelData != null)
      for (var y = 0; y < rows; ++y) {
        var srcOff = y * bytesPerPixelRow;
        var dstOff = FbmHeader.StructSize + y * rowLen;
        var copyLen = Math.Min(bytesPerPixelRow, pixelData.Length - srcOff);
        if (copyLen > 0)
          Array.Copy(pixelData, srcOff, data, dstOff, copyLen);
      }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FbmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FbmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fbm"));
    Assert.Throws<FileNotFoundException>(() => FbmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FbmReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => FbmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[256];
    data[0] = (byte)'X'; // wrong magic
    Assert.Throws<InvalidDataException>(() => FbmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var data = _BuildValidFbm(2, 2, 1, pixels);

    var result = FbmReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Bands, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17);

    var data = _BuildValidFbm(2, 2, 3, pixels);

    var result = FbmReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Bands, Is.EqualTo(3));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_StripsRowPadding() {
    // 3 cols, 1 band => 3 bytes per row, rowlen = 16 (padded to 16-byte boundary)
    var data = _BuildValidFbm(3, 1, 1);
    data[FbmHeader.StructSize] = 0xAA;
    data[FbmHeader.StructSize + 1] = 0xBB;
    data[FbmHeader.StructSize + 2] = 0xCC;
    // padding bytes 3..15 are zero

    var result = FbmReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(3));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
    Assert.That(result.PixelData[2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale() {
    var pixels = new byte[] { 42, 84, 126, 168 };
    var data = _BuildValidFbm(4, 1, 1, pixels);

    using var ms = new MemoryStream(data);
    var result = FbmReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.Bands, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TitlePreserved() {
    var data = _BuildValidFbm(1, 1, 1, [0xFF]);
    var title = "Test Image";
    var titleBytes = System.Text.Encoding.ASCII.GetBytes(title);
    Array.Copy(titleBytes, 0, data, 48, titleBytes.Length);

    var result = FbmReader.FromBytes(data);

    Assert.That(result.Title, Is.EqualTo(title));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBands_ThrowsInvalidDataException() {
    var data = new byte[256];
    Array.Copy(FbmHeader.MagicBytes, 0, data, 0, 8);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), 4);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), 4);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), 2); // invalid bands
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 8);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(24), 8);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(28), 16);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(32), 64);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(36), 0);

    Assert.Throws<InvalidDataException>(() => FbmReader.FromBytes(data));
  }
}
