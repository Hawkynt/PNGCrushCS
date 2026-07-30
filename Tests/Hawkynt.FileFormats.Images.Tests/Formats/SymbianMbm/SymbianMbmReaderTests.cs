using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.SymbianMbm;

namespace FileFormat.SymbianMbm.Tests;

[TestFixture]
public sealed class SymbianMbmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SymbianMbmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SymbianMbmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mbm"));
    Assert.Throws<FileNotFoundException>(() => SymbianMbmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SymbianMbmReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => SymbianMbmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[SymbianMbmFile.MinimumFileSize];
    // Write wrong UID1
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(), 0xDEADBEEF);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), SymbianMbmFile.Uid2);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)SymbianMbmFile.HeaderSize);
    // bitmap count = 0 at trailer
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(SymbianMbmFile.HeaderSize), 0);

    Assert.Throws<InvalidDataException>(() => SymbianMbmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidUid2_ThrowsInvalidDataException() {
    var data = new byte[SymbianMbmFile.MinimumFileSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(), SymbianMbmFile.Uid1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0xBADBAD00);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)SymbianMbmFile.HeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(SymbianMbmFile.HeaderSize), 0);

    Assert.Throws<InvalidDataException>(() => SymbianMbmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8bppGrayscale_ParsesCorrectly() {
    var data = _BuildSingleBitmapMbm(4, 2, 8, out var pixelData);

    var result = SymbianMbmReader.FromBytes(data);

    Assert.That(result.Bitmaps.Length, Is.EqualTo(1));
    Assert.That(result.Bitmaps[0].Width, Is.EqualTo(4));
    Assert.That(result.Bitmaps[0].Height, Is.EqualTo(2));
    Assert.That(result.Bitmaps[0].BitsPerPixel, Is.EqualTo(8));
    Assert.That(result.Bitmaps[0].PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = _BuildSingleBitmapMbm(2, 2, 8, out _);

    using var ms = new MemoryStream(data);
    var result = SymbianMbmReader.FromStream(ms);

    Assert.That(result.Bitmaps.Length, Is.EqualTo(1));
    Assert.That(result.Bitmaps[0].Width, Is.EqualTo(2));
    Assert.That(result.Bitmaps[0].Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid24bpp_ParsesDimensions() {
    var data = _BuildSingleBitmapMbm(3, 3, 24, out _);

    var result = SymbianMbmReader.FromBytes(data);

    Assert.That(result.Bitmaps.Length, Is.EqualTo(1));
    Assert.That(result.Bitmaps[0].Width, Is.EqualTo(3));
    Assert.That(result.Bitmaps[0].Height, Is.EqualTo(3));
    Assert.That(result.Bitmaps[0].BitsPerPixel, Is.EqualTo(24));
  }

  /// <summary>Builds a minimal valid MBM file with a single uncompressed bitmap.</summary>
  private static byte[] _BuildSingleBitmapMbm(int width, int height, int bpp, out byte[] pixelData) {
    var bytesPerRow = (width * bpp + 31) / 32 * 4;
    var dataSize = bytesPerRow * height;
    pixelData = new byte[dataSize];
    for (var i = 0; i < dataSize; ++i)
      pixelData[i] = (byte)((i * 37) % 256);

    var bitmapOffset = SymbianMbmFile.HeaderSize;
    var bitmapTotalSize = SymbianMbmFile.BitmapHeaderSize + dataSize;
    var trailerOffset = bitmapOffset + bitmapTotalSize;
    var trailerSize = 4 + 4; // count + 1 offset
    var totalSize = trailerOffset + trailerSize;
    var data = new byte[totalSize];
    var span = data.AsSpan();

    // File header
    BinaryPrimitives.WriteUInt32LittleEndian(span, SymbianMbmFile.Uid1);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], SymbianMbmFile.Uid2);
    BinaryPrimitives.WriteUInt32LittleEndian(span[8..], SymbianMbmFile.Uid3);
    BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 0); // checksum
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)trailerOffset);

    // Bitmap header
    var bmpSpan = span[bitmapOffset..];
    BinaryPrimitives.WriteUInt32LittleEndian(bmpSpan, (uint)bitmapTotalSize);
    BinaryPrimitives.WriteUInt32LittleEndian(bmpSpan[4..], (uint)SymbianMbmFile.BitmapHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan[8..], width);
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan[12..], height);
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan[16..], bpp);
    BinaryPrimitives.WriteUInt32LittleEndian(bmpSpan[20..], 0); // colorMode
    BinaryPrimitives.WriteUInt32LittleEndian(bmpSpan[24..], 0); // compression
    BinaryPrimitives.WriteUInt32LittleEndian(bmpSpan[28..], 0); // paletteSize
    BinaryPrimitives.WriteUInt32LittleEndian(bmpSpan[32..], (uint)dataSize);

    Array.Copy(pixelData, 0, data, bitmapOffset + SymbianMbmFile.BitmapHeaderSize, dataSize);

    // Trailer
    BinaryPrimitives.WriteUInt32LittleEndian(span[trailerOffset..], 1);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(trailerOffset + 4)..], (uint)bitmapOffset);

    return data;
  }
}
