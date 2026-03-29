using System;
using System.IO;
using FileFormat.Otb;

namespace FileFormat.Otb.Tests;

[TestFixture]
public sealed class OtbReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OtbReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OtbReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".otb"));
    Assert.Throws<FileNotFoundException>(() => OtbReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OtbReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => OtbReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidInfoField_ThrowsInvalidDataException() {
    // InfoField must be 0x00
    var data = new byte[] { 0x01, 8, 8, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    Assert.Throws<InvalidDataException>(() => OtbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidDepth_ThrowsInvalidDataException() {
    // Depth must be 0x01
    var data = new byte[] { 0x00, 8, 8, 0x02, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    Assert.Throws<InvalidDataException>(() => OtbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[] { 0x00, 0, 8, 0x01 };
    Assert.Throws<InvalidDataException>(() => OtbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[] { 0x00, 8, 0, 0x01 };
    Assert.Throws<InvalidDataException>(() => OtbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedPixelData_ThrowsInvalidDataException() {
    // 8x2 = 1 byte/row * 2 rows = 2 bytes needed, but only 1 provided
    var data = new byte[] { 0x00, 8, 2, 0x01, 0xFF };
    Assert.Throws<InvalidDataException>(() => OtbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid() {
    // 8x2 OTB: 1 byte per row, 2 rows
    var data = new byte[] { 0x00, 8, 2, 0x01, 0xFF, 0xAA };

    var result = OtbReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[] { 0x00, 8, 1, 0x01, 0xCD };

    using var ms = new MemoryStream(data);
    var result = OtbReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }
}
