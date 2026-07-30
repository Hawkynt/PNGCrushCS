using System;
using System.IO;
using FileFormat.Rgf;

namespace FileFormat.Rgf.Tests;

[TestFixture]
public sealed class RgfReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RgfReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RgfReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rgf"));
    Assert.Throws<FileNotFoundException>(() => RgfReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RgfReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[1];
    Assert.Throws<InvalidDataException>(() => RgfReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParse_8x8() {
    // 8x8 RGF: 1 byte per row, 8 rows = 2 + 8 = 10 bytes
    var data = new byte[10];
    data[0] = 8;  // width
    data[1] = 8;  // height
    data[2] = 0xFF;
    data[3] = 0x00;
    data[4] = 0xAA;
    data[5] = 0x55;
    data[6] = 0xF0;
    data[7] = 0x0F;
    data[8] = 0xCC;
    data[9] = 0x33;

    var result = RgfReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(8));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[7], Is.EqualTo(0x33));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[3]; // 8x1: 2-byte header + 1 byte pixel data
    data[0] = 8;  // width
    data[1] = 1;  // height
    data[2] = 0xCD;

    using var ms = new MemoryStream(data);
    var result = RgfReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[] { 0, 8 };
    Assert.Throws<InvalidDataException>(() => RgfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[] { 8, 0 };
    Assert.Throws<InvalidDataException>(() => RgfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InsufficientPixelData_ThrowsInvalidDataException() {
    // Header says 16x2 = 4 bytes pixel data needed, but only 2 bytes provided
    var data = new byte[] { 16, 2, 0xFF, 0xAA };
    Assert.Throws<InvalidDataException>(() => RgfReader.FromBytes(data));
  }
}
