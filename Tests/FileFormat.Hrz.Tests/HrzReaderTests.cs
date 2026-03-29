using System;
using System.IO;
using FileFormat.Hrz;

namespace FileFormat.Hrz.Tests;

[TestFixture]
public sealed class HrzReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HrzReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HrzReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hrz"));
    Assert.Throws<FileNotFoundException>(() => HrzReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HrzReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => HrzReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[184321];
    Assert.Throws<InvalidDataException>(() => HrzReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[184320];
    data[0] = 0xFF;
    data[1] = 0x80;
    data[2] = 0x40;

    var result = HrzReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(240));
    Assert.That(result.PixelData.Length, Is.EqualTo(184320));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0x80));
    Assert.That(result.PixelData[2], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[184320];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = HrzReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(240));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }
}
