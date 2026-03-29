using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Palm;

namespace FileFormat.Palm.Tests;

[TestFixture]
public sealed class PalmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PalmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PalmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".palm"));
    Assert.Throws<FileNotFoundException>(() => PalmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => PalmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid1bpp() {
    // 8x2, 1bpp: bytesPerRow=2 (word-aligned), 2 rows = 4 bytes pixel data
    var bytesPerRow = 2; // (8*1+7)/8=1, padded to 2
    var data = new byte[PalmHeader.StructSize + bytesPerRow * 2];
    var span = data.AsSpan();
    BinaryPrimitives.WriteUInt16BigEndian(span, 8);          // Width
    BinaryPrimitives.WriteUInt16BigEndian(span[2..], 2);     // Height
    BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)bytesPerRow); // BytesPerRow
    BinaryPrimitives.WriteUInt16BigEndian(span[6..], 0);     // Flags
    span[8] = 1;  // BitsPerPixel
    span[9] = 0;  // Version
    BinaryPrimitives.WriteUInt16BigEndian(span[10..], 0);    // NextDepthOffset
    span[12] = 0; // TransparentIndex
    span[13] = 0; // CompressionType = None
    BinaryPrimitives.WriteUInt16BigEndian(span[14..], 0);    // Reserved
    data[16] = 0xFF;
    data[17] = 0x00;
    data[18] = 0xAA;
    data[19] = 0x00;

    var result = PalmReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BitsPerPixel, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[2], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8bpp() {
    // 4x2, 8bpp: bytesPerRow=4, 2 rows = 8 bytes pixel data
    var bytesPerRow = 4;
    var data = new byte[PalmHeader.StructSize + bytesPerRow * 2];
    var span = data.AsSpan();
    BinaryPrimitives.WriteUInt16BigEndian(span, 4);          // Width
    BinaryPrimitives.WriteUInt16BigEndian(span[2..], 2);     // Height
    BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)bytesPerRow);
    BinaryPrimitives.WriteUInt16BigEndian(span[6..], 0);     // Flags (no color table)
    span[8] = 8;  // BitsPerPixel
    span[9] = 0;  // Version
    BinaryPrimitives.WriteUInt16BigEndian(span[10..], 0);
    span[12] = 0;
    span[13] = 0; // None
    BinaryPrimitives.WriteUInt16BigEndian(span[14..], 0);

    for (var i = 0; i < 8; ++i)
      data[PalmHeader.StructSize + i] = (byte)(i * 30);

    var result = PalmReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(8));
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(30));
  }
}
