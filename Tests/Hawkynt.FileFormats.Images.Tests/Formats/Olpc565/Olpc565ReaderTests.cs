using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Olpc565;

namespace FileFormat.Olpc565.Tests;

[TestFixture]
public sealed class Olpc565ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Olpc565Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Olpc565Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".565"));
    Assert.Throws<FileNotFoundException>(() => Olpc565Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Olpc565Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => Olpc565Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    Assert.Throws<InvalidDataException>(() => Olpc565Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 0);
    Assert.Throws<InvalidDataException>(() => Olpc565Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedPixelData_ThrowsInvalidDataException() {
    // 2x2 = 4 pixels = 8 bytes pixel data but only 4 provided
    var data = new byte[4 + 4];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 2);
    Assert.Throws<InvalidDataException>(() => Olpc565Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid() {
    // 2x1 image: 2 pixels = 4 bytes pixel data
    var data = new byte[4 + 4];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    // Pure red: R=31, G=0, B=0 => (31<<11) = 0xF800
    data[4] = 0x00;
    data[5] = 0xF8;
    // Pure blue: R=0, G=0, B=31 => 0x001F
    data[6] = 0x1F;
    data[7] = 0x00;

    var result = Olpc565Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[4 + 2];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    // White: R=31, G=63, B=31 => 0xFFFF
    data[4] = 0xFF;
    data[5] = 0xFF;

    using var ms = new MemoryStream(data);
    var result = Olpc565Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xFF));
  }
}
