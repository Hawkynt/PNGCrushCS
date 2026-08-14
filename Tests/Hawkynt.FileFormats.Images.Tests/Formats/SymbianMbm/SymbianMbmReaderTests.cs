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

  // The picture the converter writes at 24 bits has a width that is not a multiple of four, which is
  // the only case where Symbian's scanline differs from plain word alignment: 61 pixels are 183
  // bytes, word alignment would pad to 184, and Symbian pads to 192. Reading it at 184 shears the
  // picture a little further left on every row.
  [Test]
  [Category("Unit")]
  public void FromBytes_24bppOddWidth_ReadsTwelveByteAlignedRows() {
    var data = _BuildSingleBitmapMbm(61, 37, 24, out var pixelData);

    var result = SymbianMbmReader.FromBytes(data);

    Assert.That(result.Bitmaps[0].PixelData.Length, Is.EqualTo(192 * 37));
    Assert.That(result.Bitmaps[0].PixelData, Is.EqualTo(pixelData));
  }

  // The converter leaves iBitmapSize at zero on its 24-bit files and writes the payload length
  // rather than the whole bitmap on its 8-bit ones, so the field cannot be trusted for the length of
  // an uncompressed bitmap. Its geometry can: width, height and depth give the length outright.
  [Test]
  [Category("Unit")]
  public void FromBytes_BitmapSizeZero_StillReadsEveryRow() {
    var data = _BuildSingleBitmapMbm(61, 37, 24, out var pixelData);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(SymbianMbmFile.HeaderSize), 0);

    var result = SymbianMbmReader.FromBytes(data);

    Assert.That(result.Bitmaps[0].PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TwipsAreRead() {
    var data = _BuildSingleBitmapMbm(4, 2, 8, out _, widthInTwips: 1440, heightInTwips: 720);

    var result = SymbianMbmReader.FromBytes(data);

    Assert.That(result.Bitmaps[0].WidthInTwips, Is.EqualTo(1440));
    Assert.That(result.Bitmaps[0].HeightInTwips, Is.EqualTo(720));
  }

  // RLE-packed bitmaps are not decoded, so they are refused rather than handed back as noise.
  [Test]
  [Category("Unit")]
  public void FromBytes_Compressed_ThrowsInvalidDataException() {
    var data = _BuildSingleBitmapMbm(4, 2, 8, out _, compression: 2);

    var failure = Assert.Throws<InvalidDataException>(() => SymbianMbmReader.FromBytes(data));
    Assert.That(failure!.Message, Does.Contain("compress"));
  }

  // A later Symbian release added fields on the end of the bitmap header. The declared header length
  // says where the pixels start, so a longer header must not shift the payload.
  [Test]
  [Category("Unit")]
  public void FromBytes_LongerBitmapHeader_PixelsStartAfterIt() {
    var data = _BuildSingleBitmapMbm(4, 2, 8, out var pixelData, headerLength: 44);

    var result = SymbianMbmReader.FromBytes(data);

    Assert.That(result.Bitmaps[0].PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BitmapHeaderShorterThanTheStruct_ThrowsInvalidDataException() {
    var data = _BuildSingleBitmapMbm(4, 2, 8, out _);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(SymbianMbmFile.HeaderSize + 4), 20);

    Assert.Throws<InvalidDataException>(() => SymbianMbmReader.FromBytes(data));
  }

  /// <summary>
  /// Builds a minimal valid MBM file with a single uncompressed bitmap, laid out as Symbian's
  /// SEpocBitmapHeader: size, header length, size in pixels, size in twips, depth, colour flag,
  /// palette entries, compression.
  /// </summary>
  private static byte[] _BuildSingleBitmapMbm(
    int width, int height, int bpp, out byte[] pixelData,
    int widthInTwips = 0, int heightInTwips = 0, uint compression = 0,
    int headerLength = SymbianMbmFile.BitmapHeaderSize
  ) {
    var bytesPerRow = SymbianMbmFile.ScanLineLength(width, bpp);
    var dataSize = bytesPerRow * height;
    pixelData = new byte[dataSize];
    for (var i = 0; i < dataSize; ++i)
      pixelData[i] = (byte)((i * 37) % 256);

    var bitmapOffset = SymbianMbmFile.HeaderSize;
    var bitmapTotalSize = headerLength + dataSize;
    var trailerOffset = bitmapOffset + bitmapTotalSize;
    var trailerSize = 4 + 4; // count + 1 offset
    var totalSize = trailerOffset + trailerSize;
    var data = new byte[totalSize];
    var span = data.AsSpan();

    // File header
    BinaryPrimitives.WriteUInt32LittleEndian(span, SymbianMbmFile.Uid1);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], SymbianMbmFile.Uid2);
    BinaryPrimitives.WriteUInt32LittleEndian(span[8..], SymbianMbmFile.Uid3);
    BinaryPrimitives.WriteUInt32LittleEndian(span[12..], SymbianMbmFile.UidChecksum(SymbianMbmFile.Uid1, SymbianMbmFile.Uid2, SymbianMbmFile.Uid3));
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)trailerOffset);

    // Bitmap header
    var bmpSpan = span[bitmapOffset..];
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan, bitmapTotalSize);
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan[4..], headerLength);
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan[8..], width);
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan[12..], height);
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan[16..], widthInTwips);
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan[20..], heightInTwips);
    BinaryPrimitives.WriteInt32LittleEndian(bmpSpan[24..], bpp);
    BinaryPrimitives.WriteUInt32LittleEndian(bmpSpan[28..], 0); // colour flag
    BinaryPrimitives.WriteUInt32LittleEndian(bmpSpan[32..], 0); // palette entries
    BinaryPrimitives.WriteUInt32LittleEndian(bmpSpan[36..], compression);

    Array.Copy(pixelData, 0, data, bitmapOffset + headerLength, dataSize);

    // Trailer
    BinaryPrimitives.WriteUInt32LittleEndian(span[trailerOffset..], 1);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(trailerOffset + 4)..], (uint)bitmapOffset);

    return data;
  }
}
