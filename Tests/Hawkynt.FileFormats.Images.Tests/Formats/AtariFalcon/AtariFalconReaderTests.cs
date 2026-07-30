using System;
using System.IO;
using FileFormat.AtariFalcon;

namespace FileFormat.AtariFalcon.Tests;

[TestFixture]
public sealed class AtariFalconReaderTests {

  private const int _EXPECTED_SIZE = 320 * 240 * 2;

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariFalconReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariFalconReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ftc"));
    Assert.Throws<FileNotFoundException>(() => AtariFalconReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariFalconReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AtariFalconReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[_EXPECTED_SIZE + 1];
    Assert.Throws<InvalidDataException>(() => AtariFalconReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0xFF;
    data[1] = 0x80;
    data[2] = 0x40;

    var result = AtariFalconReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(240));
    Assert.That(result.PixelData.Length, Is.EqualTo(_EXPECTED_SIZE));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0x80));
    Assert.That(result.PixelData[2], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = AtariFalconReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(240));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesInputData() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0x42;

    var result = AtariFalconReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }
}
