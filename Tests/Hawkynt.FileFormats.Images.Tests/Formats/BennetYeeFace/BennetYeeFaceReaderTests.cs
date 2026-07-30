using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.BennetYeeFace;

namespace FileFormat.BennetYeeFace.Tests;

[TestFixture]
public sealed class BennetYeeFaceReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BennetYeeFaceReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BennetYeeFaceReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ybm"));
    Assert.Throws<FileNotFoundException>(() => BennetYeeFaceReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BennetYeeFaceReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => BennetYeeFaceReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    Assert.Throws<InvalidDataException>(() => BennetYeeFaceReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 0);
    Assert.Throws<InvalidDataException>(() => BennetYeeFaceReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedPixelData_ThrowsInvalidDataException() {
    // 16x2 = stride 2, need 4 bytes pixel data but only 2 provided
    var data = new byte[4 + 2];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 2);
    Assert.Throws<InvalidDataException>(() => BennetYeeFaceReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid() {
    // 16x2 YBM: stride = 2 bytes per row, 2 rows = 4 bytes pixel data
    var data = new byte[4 + 4];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 2);
    data[4] = 0xFF;
    data[5] = 0xAA;
    data[6] = 0x55;
    data[7] = 0x00;

    var result = BennetYeeFaceReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(4));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WordPaddedStride() {
    // 5 pixels wide: stride = ((5+15)/16)*2 = 2 bytes per row
    var data = new byte[4 + 2];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 5);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    data[4] = 0b11010000;
    data[5] = 0x00;

    var result = BennetYeeFaceReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(5));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[4 + 2];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    data[4] = 0xCD;
    data[5] = 0x00; // padding byte for word alignment

    using var ms = new MemoryStream(data);
    var result = BennetYeeFaceReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }
}
