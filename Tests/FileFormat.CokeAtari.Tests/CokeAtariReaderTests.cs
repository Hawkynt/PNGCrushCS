using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.CokeAtari;

namespace FileFormat.CokeAtari.Tests;

[TestFixture]
public sealed class CokeAtariReaderTests {

  private static byte[] _MakeValidFile(ushort width, ushort height, byte fillByte = 0x00) {
    var pixelBytes = width * height * 2;
    var data = new byte[CokeAtariHeader.StructSize + pixelBytes];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), width);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), height);
    for (var i = CokeAtariHeader.StructSize; i < data.Length; ++i)
      data[i] = fillByte;
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CokeAtariReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CokeAtariReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tg1"));
    Assert.Throws<FileNotFoundException>(() => CokeAtariReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CokeAtariReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => CokeAtariReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[CokeAtariHeader.StructSize];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 10);
    Assert.Throws<InvalidDataException>(() => CokeAtariReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[CokeAtariHeader.StructSize];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 10);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 0);
    Assert.Throws<InvalidDataException>(() => CokeAtariReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedPixelData_ThrowsInvalidDataException() {
    var data = new byte[CokeAtariHeader.StructSize + 2];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 10);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 10);
    Assert.Throws<InvalidDataException>(() => CokeAtariReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDimensions() {
    var data = _MakeValidFile(8, 4);

    var result = CokeAtariReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.PixelData.Length, Is.EqualTo(8 * 4 * 2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_PixelDataPreserved() {
    var data = _MakeValidFile(2, 2, 0xAB);

    var result = CokeAtariReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[7], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = _MakeValidFile(4, 3, 0xCD);

    using var ms = new MemoryStream(data);
    var result = CokeAtariReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesInputData() {
    var data = _MakeValidFile(2, 1, 0x42);

    var result = CokeAtariReader.FromBytes(data);
    data[CokeAtariHeader.StructSize] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }
}
