using System;
using System.IO;
using FileFormat.Wbmp;

namespace FileFormat.Wbmp.Tests;

[TestFixture]
public sealed class WbmpReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WbmpReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WbmpReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wbmp"));
    Assert.Throws<FileNotFoundException>(() => WbmpReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WbmpReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => WbmpReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidType_ThrowsInvalidDataException() {
    // Type byte = 1 (only 0 is supported)
    var bad = new byte[] { 0x01, 0x00, 0x01, 0x01, 0x80 };
    Assert.Throws<InvalidDataException>(() => WbmpReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    // 8x2 WBMP: type=0, fixedHeader=0, width=8, height=2, 2 bytes pixel data
    var data = new byte[] { 0x00, 0x00, 0x08, 0x02, 0xFF, 0x00 };
    var result = WbmpReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0x00));
  }
}
