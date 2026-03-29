using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Awd;

namespace FileFormat.Awd.Tests;

[TestFixture]
public sealed class AwdReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AwdReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AwdReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".awd"));
    Assert.Throws<FileNotFoundException>(() => AwdReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AwdReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => AwdReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[16];
    data[0] = (byte)'X';
    data[1] = (byte)'Y';
    data[2] = (byte)'Z';
    data[3] = 0;
    Assert.Throws<InvalidDataException>(() => AwdReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidImage_ParsesDimensions() {
    var data = _CreateValid8x2();

    var result = AwdReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidImage_PixelDataPreserved() {
    var data = _CreateValid8x2();
    data[16] = 0xFF;
    data[17] = 0xAA;

    var result = AwdReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidImage_ParsesCorrectly() {
    var data = _CreateValid8x2();
    data[16] = 0xCD;

    using var ms = new MemoryStream(data);
    var result = AwdReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }

  private static byte[] _CreateValid8x2() {
    // 8x2 AWD: header (16 bytes) + 1 byte/row * 2 rows = 18 bytes
    var data = new byte[18];
    data[0] = (byte)'A';
    data[1] = (byte)'W';
    data[2] = (byte)'D';
    data[3] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);    // version
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), 8);    // width
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), 2);   // height
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), 0);   // reserved
    return data;
  }
}
