using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Aai;

namespace FileFormat.Aai.Tests;

[TestFixture]
public sealed class AaiReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AaiReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AaiReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".aai"));
    Assert.Throws<FileNotFoundException>(() => AaiReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AaiReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => AaiReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var data = new byte[8 + 12];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 2);
    Assert.Throws<InvalidDataException>(() => AaiReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimensions_ThrowsInvalidDataException() {
    var data = new byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 1);
    Assert.Throws<InvalidDataException>(() => AaiReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParse() {
    // 2x2 image: 4 pixels, 16 pixel bytes
    var data = new byte[8 + 16];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 2);
    data[8] = 0xAA;   // R
    data[9] = 0xBB;   // G
    data[10] = 0xCC;  // B
    data[11] = 0xFF;  // A
    data[12] = 0x11;  // R
    data[13] = 0x22;  // G
    data[14] = 0x33;  // B
    data[15] = 0x80;  // A

    var result = AaiReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(16));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[4], Is.EqualTo(0x11));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[8 + 4];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 1);
    data[8] = 0xFF;
    data[9] = 0x00;
    data[10] = 0x80;
    data[11] = 0x40;

    using var ms = new MemoryStream(data);
    var result = AaiReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(new byte[] { 0xFF, 0x00, 0x80, 0x40 }));
  }
}
